using Microsoft.Data.Sqlite;
using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Core;
using SDemo.App.Kb.Storage;

namespace SDemo.Kb.Tests;

/// <summary>每个测试一个临时库；不共享静态状态，测试可并行。</summary>
internal sealed class TestKb : IDisposable
{
    public TestKb(DateTimeOffset? now = null)
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "sdemo-kb-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDirectory);

        Clock = new FakeClock(now ?? DateTimeOffset.UtcNow);
        Database = new KbDatabase(DataDirectory);
        Store = new KbStore(Database);
        Service = new KbService(Store, new KbBlobStore(DataDirectory), Clock);
        Token = Service.IssueToken(KbConstants.AppId);
    }

    public FakeClock Clock { get; }

    public string DataDirectory { get; }

    public KbDatabase Database { get; }

    public KbStore Store { get; }

    public KbService Service { get; }

    public string Token { get; }

    public WriteResult Ingest(
        string title,
        IEnumerable<string> blocks,
        string? entityUri = null,
        KbKind kind = KbKind.Reference,
        string appId = KbConstants.AppId,
        IEnumerable<IngestLink>? links = null,
        string? rationale = null,
        int? ttlDays = null,
        RetentionLevel? retention = null,
        IEnumerable<IngestArtifact>? artifacts = null)
    {
        var request = new IngestRequest
        {
            Source = new IngestSource { AppId = appId, EntityUri = entityUri ?? string.Empty },
            Kind = kind,
            Title = title,
            Retention = retention,
            TtlDays = ttlDays,
            Rationale = rationale,
            RationaleSource = appId == KbConstants.AppId ? RationaleSource.Import : RationaleSource.Ai,
            Blocks = blocks
                .Select(text => new BlockInput { Op = ChangeOp.Add, Text = text })
                .ToList(),
            Links = links?.ToList() ?? [],
            Artifacts = artifacts?.ToList() ?? [],
        };

        return Service.Ingest(request, appId);
    }

    /// <summary>以 Agent 身份投一条暂存内容（默认 staging，可指定 TTL）。</summary>
    public WriteResult CreateStaging(string title, IEnumerable<string> blocks, int? ttlDays = null)
    {
        return Ingest(
            title,
            blocks,
            appId: "sdemo.agent",
            kind: KbKind.Note,
            ttlDays: ttlDays,
            retention: RetentionLevel.Staging);
    }

    public WriteResult Revise(string docId, int expectedVersion, params BlockInput[] ops)
    {
        return Service.Revise(
            new ReviseRequest
            {
                Source = new IngestSource { AppId = KbConstants.AppId },
                DocId = docId,
                ExpectedVersion = expectedVersion,
                Reason = "测试修订",
                Rationale = "测试理由",
                RationaleSource = RationaleSource.User,
                Blocks = ops.ToList(),
            },
            KbConstants.AppId);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(DataDirectory))
                {
                    Directory.Delete(DataDirectory, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }
}
