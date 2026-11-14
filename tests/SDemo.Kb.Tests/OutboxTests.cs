using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Core;

namespace SDemo.Kb.Tests;

public sealed class OutboxTests
{
    [Fact]
    public async Task KB离线时写入落outbox且不抛异常()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sdemo-kb-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var outbox = new KbOutbox(Path.Combine(directory, "outbox.db"));
            using var client = new KbIngestClient("http://127.0.0.1:1", "token", "sdemo.meeting", outbox);

            var request = new IngestRequest
            {
                Source = new IngestSource { AppId = "sdemo.meeting", EntityUri = "sdemo.meeting/meeting/offline" },
                Kind = KbKind.Decision,
                Title = "离线纪要",
                Blocks = [new BlockInput { Text = "KB 没起来时这条也要保住" }],
            };

            var result = await client.IngestAsync(request);

            Assert.Null(result);
            Assert.Equal(1, outbox.CountPending());
            Assert.Single(outbox.Due(DateTimeOffset.UtcNow.AddMinutes(1)));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响断言。
            }
        }
    }

    [Fact]
    public void 退避失败次数会累加()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sdemo-kb-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var outbox = new KbOutbox(Path.Combine(directory, "outbox.db"));
            var id = outbox.Enqueue("sdemo.meeting", "x", "{}");

            outbox.MarkFailed(id, "boom", DateTimeOffset.UtcNow);

            var item = Assert.Single(outbox.Due(DateTimeOffset.UtcNow.AddDays(1)));
            Assert.Equal(1, item.Attempts);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响断言。
            }
        }
    }
}
