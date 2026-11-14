using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Storage;

namespace SDemo.App.Kb.Core;

/// <summary>管理路径：promote / retract / annotate / 重建索引。</summary>
public sealed partial class KbService
{
    public WriteResult Promote(KbPromoteRequest request, string actorApp = "sdemo.agent")
    {
        ArgumentNullException.ThrowIfNull(request);

        using var session = _store.Begin();
        var doc = session.GetDoc(request.DocId)
            ?? throw KbException.NotFound($"文档不存在：{request.DocId}");

        if (!IsPrivileged(actorApp))
        {
            throw new KbException(KbErrorCodes.Forbidden, "promote 只允许 KB 界面或 Agent。");
        }

        var target = request.Retention == RetentionLevel.Durable ? "durable" : "staging";
        if (string.Equals(doc.Retention, target, StringComparison.Ordinal))
        {
            var current = session.GetCurrentVersion(doc.Id);
            return new WriteResult
            {
                DocId = doc.Id,
                VersionId = current?.Id ?? string.Empty,
                Seq = current?.Seq ?? 0,
                Created = false,
                Deduplicated = true,
                CurrentVersionSeq = current?.Seq ?? 0,
            };
        }

        if (string.Equals(doc.Retention, "durable", StringComparison.Ordinal)
            && string.Equals(target, "staging", StringComparison.Ordinal)
            && !request.Confirmed)
        {
            throw KbException.Invalid("撤销归档可能已被引用，必须二次确认（confirmed=true）。");
        }

        var now = Now;
        session.SetRetention(doc.Id, target, now);

        var expiresAt = target == "staging"
            ? ComputeExpiry(
                RetentionLevel.Staging,
                ParseEnum(doc.Kind, KbKind.Note),
                doc.OwnerApp,
                null,
                now,
                ReadRetentionSettings(session))
            : null;

        session.SetExpiry(doc.Id, expiresAt);
        session.WriteAudit(actorApp, "promote", doc.Id, $"{doc.Retention} → {target}");

        var updated = doc with { Retention = target, UpdatedAt = now, ExpiresAt = expiresAt };
        MaybePrune(session, updated);

        var version = session.GetCurrentVersion(doc.Id);
        session.Commit();

        return new WriteResult
        {
            DocId = doc.Id,
            VersionId = version?.Id ?? string.Empty,
            Seq = version?.Seq ?? 0,
            Created = false,
            CurrentVersionSeq = version?.Seq ?? 0,
        };
    }

    public DeleteResult Delete(KbDeleteRequest request, string actorApp = "sdemo.agent")
    {
        ArgumentNullException.ThrowIfNull(request);

        using var session = _store.Begin();
        var doc = session.GetDoc(request.DocId, includeDeleted: true)
            ?? throw KbException.NotFound($"文档不存在：{request.DocId}");

        if (!IsPrivileged(actorApp))
        {
            throw new KbException(KbErrorCodes.Forbidden, "retract 只允许 KB 界面或 Agent。");
        }

        var result = new DeleteResult { DocId = doc.Id };

        // 删除前先扫引用：影响面清单交给 Agent 提示用户复核（kb-design §5.1）。
        foreach (var link in session.ListInboundLinks(doc.Id))
        {
            result.AffectedRefs.Add(BuildRef(session, link));
        }

        if (doc.DeletedAt is null)
        {
            var now = Now;
            session.SetDeleted(doc.Id, now);
            result.DeletedAt = now;
            session.WriteAudit(actorApp, "retract", doc.Id, request.Reason ?? string.Empty);
        }
        else
        {
            result.DeletedAt = doc.DeletedAt.Value;
        }

        session.Commit();
        return result;
    }

    public void Annotate(KbAnnotateRequest request, string author = "ai")
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Summary))
        {
            throw KbException.Invalid("摘要不能为空。");
        }

        if (request.Summary.Length > 4000)
        {
            throw new KbException(KbErrorCodes.TooLarge, "摘要超过 4000 字符。");
        }

        using var session = _store.Begin();
        var doc = session.GetDoc(request.DocId)
            ?? throw KbException.NotFound($"文档不存在：{request.DocId}");

        var version = string.IsNullOrWhiteSpace(request.VersionId)
            ? session.GetCurrentVersion(doc.Id)
            : session.GetVersionById(request.VersionId);

        if (version is null || !string.Equals(version.DocId, doc.Id, StringComparison.Ordinal))
        {
            throw new KbException(KbErrorCodes.VersionNotFound, "版本不属于该文档。");
        }

        session.UpsertAnnotation(doc.Id, version.Id, request.Summary.Trim(), author, Now);
        session.Commit();
    }

    public string? GetAnnotation(string docId, string versionId)
    {
        using var session = _store.Read();
        return session.GetAnnotation(versionId);
    }

    /// <summary>整库重建索引：schema 升级或索引损坏后的自愈入口。</summary>
    public void RebuildIndexes() => _store.RebuildIndexes();

    /// <summary>写入方 token 的签发 / 校验（Host 启动时调用）。</summary>
    public string IssueToken(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            throw KbException.Invalid("appId 不能为空。");
        }

        var token = KbIds.NewToken();
        var hash = HashToken(token);

        using var session = _store.Begin();
        session.UpsertToken(appId.Trim(), hash, Now);
        session.WriteAudit("sdemo.kb", "token.issue", null, appId.Trim());
        session.Commit();

        return token;
    }

    public string? Authenticate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = HashToken(token);
        using var session = _store.Read();
        return session.FindAppByTokenHash(hash);
    }

    internal static string HashToken(string token) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token)));
}

