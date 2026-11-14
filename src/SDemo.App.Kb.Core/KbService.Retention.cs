using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Storage;

namespace SDemo.App.Kb.Core;

/// <summary>P4-b：保留策略、TTL、回收站、块/原件 GC、收件箱与存储统计。</summary>
public sealed partial class KbService
{
    internal const string RetentionSettingsKey = "retention.settings";

    public RetentionSettings GetRetentionSettings()
    {
        using var session = _store.Read();
        return ReadRetentionSettings(session);
    }

    public void SaveRetentionSettings(RetentionSettings settings, string actorApp = "sdemo.kb")
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);

        using var session = _store.Begin();
        session.SetMeta(RetentionSettingsKey, KbJson.Serialize(settings));
        session.WriteAudit(actorApp, "settings.retention", null, KbJson.Serialize(settings));
        session.Commit();
    }

    public InboxResult Inbox(int limit = 100)
    {
        using var session = _store.Read();
        var now = Now;
        var rows = session.ListStaging(limit);
        var result = new InboxResult
        {
            Total = session.CountStaging(),
            ExpiringSoon = session.CountExpiringSoon(now.AddDays(3)),
        };

        foreach (var row in rows)
        {
            var doc = row.Doc;
            result.Items.Add(new InboxItem
            {
                DocId = doc.Id,
                Title = doc.Title,
                Kind = ParseEnum(doc.Kind, KbKind.Note),
                OwnerApp = doc.OwnerApp,
                VersionSeq = row.VersionSeq,
                BlockCount = row.BlockCount,
                TextChars = row.TextChars,
                BlobBytes = row.BlobBytes,
                ExpiresAt = doc.ExpiresAt,
                DaysRemaining = doc.ExpiresAt is { } expires
                    ? (int)Math.Ceiling((expires - now).TotalDays)
                    : int.MaxValue,
                DerivedState = ParseEnum(doc.DerivedState, DerivedState.Ready),
                Uri = doc.Uri,
                CreatedAt = doc.CreatedAt,
                UpdatedAt = doc.UpdatedAt,
            });
        }

        return result;
    }

    public PromoteBatchResult PromoteMany(PromoteBatchRequest request, string actorApp = "sdemo.agent")
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DocIds.Count == 0)
        {
            throw KbException.Invalid("docIds 不能为空。");
        }

        if (request.DocIds.Count > 200)
        {
            throw new KbException(KbErrorCodes.TooLarge, "单次最多归档 200 篇。");
        }

        var result = new PromoteBatchResult();
        foreach (var docId in request.DocIds.Distinct(StringComparer.Ordinal))
        {
            try
            {
                result.Promoted.Add(Promote(
                    new KbPromoteRequest
                    {
                        DocId = docId,
                        Retention = request.Retention,
                        Rationale = request.Rationale,
                    },
                    actorApp));
            }
            catch (KbException ex)
            {
                result.Failed.Add($"{docId}: {ex.Code} {ex.Message}");
            }
        }

        return result;
    }

    public StorageStats GetStorageStats()
    {
        using var session = _store.Read();
        var counters = session.GetCounters();
        return new StorageStats
        {
            DatabaseBytes = DatabaseBytes(),
            BlobBytes = counters.BlobBytes,
            TextChars = counters.TextChars,
            DocCount = counters.Docs,
            StagingCount = counters.Staging,
            TrashCount = counters.Trash,
            BlockCount = counters.Blocks,
            VersionCount = counters.Versions,
            ReclaimableBlobBytes = counters.ReclaimableBlobBytes,
        };
    }

    /// <summary>
    /// 一次维护：到期软删 → 回收站到期物理清理（有引用的整篇保护）→ 块 GC → 原件 GC。
    /// 幂等，可随时重复跑；Host 每小时触发一次，也可 --maintain 手动跑。
    /// </summary>
    public MaintenanceReport RunMaintenance(DateTimeOffset? now = null)
    {
        var at = now ?? Now;
        var report = new MaintenanceReport { RunAt = at };
        var blobVictims = new List<BlobRow>();

        using (var session = _store.Begin())
        {
            var settings = ReadRetentionSettings(session);

            foreach (var doc in session.ListExpired(at))
            {
                session.SetDeleted(doc.Id, at);
                report.ExpiredDocs++;
            }

            foreach (var doc in session.ListPurgeable(at.AddDays(-settings.TrashDays)))
            {
                // 引用保护：只要还有入库引用指向它，整篇保留，避免历史引用变成死链。
                if (session.CountInboundLinks(doc.Id) > 0)
                {
                    report.ProtectedDocs++;
                    continue;
                }

                session.DeleteAnnotations(doc.Id);
                session.PurgeDocVersions(doc.Id);
                session.DeleteBlobRefs(doc.Id);
                session.MarkPurged(doc.Id, at);
                report.PurgedDocs++;
            }

            report.CollectedBlocks = session.CollectUnreferencedBlocks();
            blobVictims.AddRange(session.CollectUnreferencedBlobs());
            report.CollectedBlobs = blobVictims.Count;
            report.ReclaimedBlobBytes = blobVictims.Sum(blob => blob.Size);

            session.WriteAudit(
                KbConstants.AppId,
                "maintenance",
                null,
                $"expired={report.ExpiredDocs}; purged={report.PurgedDocs}; blocks={report.CollectedBlocks}; blobs={report.CollectedBlobs}");

            session.Commit();
        }

        // 文件删除放在事务提交后：DB 回滚不会造成「行在、文件没了」。
        foreach (var blob in blobVictims)
        {
            if (string.IsNullOrEmpty(blob.StoredPath))
            {
                continue;
            }

            try
            {
                if (File.Exists(blob.StoredPath))
                {
                    File.Delete(blob.StoredPath);
                }
            }
            catch (IOException)
            {
                // 下次维护重试；孤儿文件不阻塞。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }

        return report;
    }

    internal static RetentionSettings ReadRetentionSettings(KbSession session)
    {
        var json = session.GetMeta(RetentionSettingsKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new RetentionSettings();
        }

        try
        {
            var settings = KbJson.Deserialize<RetentionSettings>(json) ?? new RetentionSettings();
            ValidateSettings(settings);
            return settings;
        }
        catch (Exception)
        {
            return new RetentionSettings();
        }
    }

    internal static void ValidateSettings(RetentionSettings settings)
    {
        if (settings.StagingVersions is < 1 or > 100
            || settings.NoteVersions is < 1 or > 1000
            || settings.MeetingTtlDays is < 0 or > 3650
            || settings.AgentTtlDays is < 0 or > 3650
            || settings.TrashDays is < 1 or > 365)
        {
            throw KbException.Invalid("保留设置超出允许范围。");
        }
    }

    internal DateTimeOffset? ComputeExpiry(
        RetentionLevel retention,
        KbKind kind,
        string ownerApp,
        int? ttlDays,
        DateTimeOffset now,
        RetentionSettings settings)
    {
        if (retention != RetentionLevel.Staging)
        {
            return null;
        }

        var days = ttlDays
            ?? (string.Equals(ownerApp, "sdemo.meeting", StringComparison.Ordinal) || kind == KbKind.Decision
                ? settings.MeetingTtlDays
                : settings.AgentTtlDays);

        return now.AddDays(Math.Clamp(days, 0, 3650));
    }

    private long DatabaseBytes()
    {
        var path = _store.Database.DatabasePath;
        long total = 0;
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            var info = new FileInfo(candidate);
            if (info.Exists)
            {
                total += info.Length;
            }
        }

        return total;
    }
}
