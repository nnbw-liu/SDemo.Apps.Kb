using Microsoft.Data.Sqlite;
using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Core;
using SDemo.App.Kb.Storage;

namespace SDemo.Kb.Tests;

/// <summary>每个测试一个临时库；不共享静态状态，测试可并行。</summary>
internal sealed class TestKb : IDisposable
{
    public TestKb()
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "sdemo-kb-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDirectory);

        Database = new KbDatabase(DataDirectory);
        Store = new KbStore(Database);
        Service = new KbService(Store, new KbBlobStore(DataDirectory));
        Token = Service.IssueToken(KbConstants.AppId);
    }

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
        string? rationale = null)
    {
        var request = new IngestRequest
        {
            Source = new IngestSource { AppId = appId, EntityUri = entityUri ?? string.Empty },
            Kind = kind,
            Title = title,
            Rationale = rationale,
            RationaleSource = RationaleSource.Import,
            Blocks = blocks
                .Select(text => new BlockInput { Op = ChangeOp.Add, Text = text })
                .ToList(),
            Links = links?.ToList() ?? [],
        };

        return Service.Ingest(request, appId);
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
