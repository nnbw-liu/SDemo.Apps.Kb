using System.Security.Cryptography;
using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Core;

namespace SDemo.Kb.Tests;

public sealed class RetentionTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 暂存到期后软删并从收件箱消失()
    {
        using var kb = new TestKb(Start);
        var created = kb.CreateStaging("会议纪要", ["待归档内容"], ttlDays: 1);

        var content = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;
        Assert.NotNull(content.Doc.ExpiresAt);
        Assert.Equal(Start.AddDays(1), content.Doc.ExpiresAt);

        var inbox = kb.Service.Inbox();
        var item = Assert.Single(inbox.Items);
        Assert.Equal(1, item.DaysRemaining);
        Assert.Equal(1, inbox.ExpiringSoon);

        kb.Clock.Advance(TimeSpan.FromDays(2));
        var report = kb.Service.RunMaintenance();

        Assert.Equal(1, report.ExpiredDocs);
        Assert.Null(kb.Service.Get(new KbGetRequest { DocId = created.DocId }));

        var resolved = kb.Service.Resolve(KbUri.Doc(created.DocId));
        Assert.True(resolved.Exists);
        Assert.True(resolved.Deleted);
        Assert.Empty(kb.Service.Inbox().Items);
    }

    [Fact]
    public void 回收站七天后物理清理并GC块()
    {
        using var kb = new TestKb(Start);
        var created = kb.CreateStaging("过期文档", ["这段会被回收"], ttlDays: 1);
        var blockId = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!.Blocks[0].BlockId;

        kb.Clock.Advance(TimeSpan.FromDays(2));
        kb.Service.RunMaintenance();

        kb.Clock.Advance(TimeSpan.FromDays(8));
        var report = kb.Service.RunMaintenance();

        Assert.Equal(1, report.PurgedDocs);
        Assert.Equal(1, report.CollectedBlocks);
        Assert.Equal(0, kb.Service.GetStorageStats().VersionCount);
        Assert.Equal(0, kb.Service.GetStorageStats().BlockCount);

        var resolved = kb.Service.Resolve(KbUri.Format(created.DocId, 1, blockId));
        Assert.True(resolved.Deleted);
        Assert.True(resolved.BlockMissing);
    }

    [Fact]
    public void 被引用的文档过保后保留版本与块()
    {
        using var kb = new TestKb(Start);
        var target = kb.CreateStaging("被引用的纪要", ["被引用的内容"], ttlDays: 1);
        var blockId = kb.Service.Get(new KbGetRequest { DocId = target.DocId })!.Blocks[0].BlockId;

        kb.Ingest(
            "待办引用",
            ["这条待办基于会议纪要"],
            links: [new IngestLink { To = KbUri.Format(target.DocId, 1, blockId) }]);

        kb.Clock.Advance(TimeSpan.FromDays(2));
        kb.Service.RunMaintenance();

        kb.Clock.Advance(TimeSpan.FromDays(8));
        var report = kb.Service.RunMaintenance();

        Assert.Equal(1, report.ProtectedDocs);
        Assert.Equal(0, report.PurgedDocs);

        var resolved = kb.Service.Resolve(KbUri.Format(target.DocId, 1, blockId));
        Assert.True(resolved.Exists);
        Assert.True(resolved.Deleted);
        Assert.False(resolved.BlockMissing);
        Assert.NotNull(kb.Store.Read().GetBlock(blockId));
    }

    [Fact]
    public void 块GC跳过被引用块()
    {
        using var kb = new TestKb(Start);
        kb.Service.SaveRetentionSettings(new RetentionSettings { NoteVersions = 1 });

        var unreferenced = kb.Ingest("无引用", ["保留段", "旧段"]);
        var unreferencedV1 = kb.Service.Get(new KbGetRequest { DocId = unreferenced.DocId })!;
        var oldUnreferenced = unreferencedV1.Blocks[1].BlockId;
        kb.Revise(
            unreferenced.DocId,
            1,
            new BlockInput { Op = ChangeOp.Replace, BlockId = oldUnreferenced, Text = "旧段改" });

        var referenced = kb.Ingest("有引用", ["B 保留段", "B 旧段"]);
        var referencedV1 = kb.Service.Get(new KbGetRequest { DocId = referenced.DocId })!;
        var oldReferenced = referencedV1.Blocks[1].BlockId;
        kb.Revise(
            referenced.DocId,
            1,
            new BlockInput { Op = ChangeOp.Replace, BlockId = oldReferenced, Text = "B 旧段改" });

        kb.Ingest(
            "引用方",
            ["引用旧段"],
            links: [new IngestLink { To = KbUri.Format(referenced.DocId, 1, oldReferenced) }]);

        var report = kb.Service.RunMaintenance();

        Assert.Equal(1, report.CollectedBlocks);
        Assert.Null(kb.Store.Read().GetBlock(oldUnreferenced));
        Assert.NotNull(kb.Store.Read().GetBlock(oldReferenced));
    }

    [Fact]
    public void 收件箱按到期时间排序并统计即将到期()
    {
        using var kb = new TestKb(Start);
        kb.CreateStaging("十天", ["A"], ttlDays: 10);
        kb.CreateStaging("两天", ["B"], ttlDays: 2);
        kb.Ingest("归档内容", ["不进收件箱"]);

        var inbox = kb.Service.Inbox();

        Assert.Equal(2, inbox.Total);
        Assert.Equal(1, inbox.ExpiringSoon);
        Assert.Equal(2, inbox.Items.Count);
        Assert.Equal("两天", inbox.Items[0].Title);
        Assert.Equal(2, inbox.Items[0].DaysRemaining);
        Assert.Equal(10, inbox.Items[1].DaysRemaining);
        Assert.True(inbox.Items[0].TextChars > 0);
    }

    [Fact]
    public void 批量归档清空到期时间()
    {
        using var kb = new TestKb(Start);
        var first = kb.CreateStaging("一", ["A"], ttlDays: 3);
        var second = kb.CreateStaging("二", ["B"], ttlDays: 3);

        var result = kb.Service.PromoteMany(new PromoteBatchRequest
        {
            DocIds = [first.DocId, second.DocId],
            Retention = RetentionLevel.Durable,
        });

        Assert.Equal(2, result.Promoted.Count);
        Assert.Empty(result.Failed);
        Assert.Empty(kb.Service.Inbox().Items);

        foreach (var docId in new[] { first.DocId, second.DocId })
        {
            var doc = kb.Service.Get(new KbGetRequest { DocId = docId })!;
            Assert.Equal(RetentionLevel.Durable, doc.Doc.Retention);
            Assert.Null(doc.Doc.ExpiresAt);
        }
    }

    [Fact]
    public void 保留设置持久化并影响TTL()
    {
        using var kb = new TestKb(Start);
        kb.Service.SaveRetentionSettings(new RetentionSettings
        {
            AgentTtlDays = 5,
            MeetingTtlDays = 2,
            NoteVersions = 12,
        });

        // 同一库上的新服务实例应读到持久化设置。
        var restarted = new KbService(kb.Store, new KbBlobStore(kb.DataDirectory), kb.Clock);
        var settings = restarted.GetRetentionSettings();
        Assert.Equal(5, settings.AgentTtlDays);
        Assert.Equal(2, settings.MeetingTtlDays);
        Assert.Equal(12, settings.NoteVersions);

        var created = kb.CreateStaging("按设置到期", ["内容"]);
        var content = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;
        Assert.Equal(Start.AddDays(5), content.Doc.ExpiresAt);

        Assert.Throws<KbException>(() => kb.Service.SaveRetentionSettings(new RetentionSettings
        {
            AgentTtlDays = -1,
        }));
    }

    [Fact]
    public void 存储统计能给出占用与可清理项()
    {
        using var kb = new TestKb(Start);
        kb.Ingest("归档", ["一段正文"]);
        kb.CreateStaging("暂存", ["另一段"], ttlDays: 5);

        var stats = kb.Service.GetStorageStats();

        Assert.Equal(2, stats.DocCount);
        Assert.Equal(1, stats.StagingCount);
        Assert.True(stats.BlockCount >= 2);
        Assert.True(stats.VersionCount >= 2);
        Assert.True(stats.DatabaseBytes > 0);
        Assert.True(stats.TextChars > 0);
    }

    [Fact]
    public void 原件在回收站清理后被物理删除()
    {
        using var kb = new TestKb(Start);
        var source = Path.Combine(kb.DataDirectory, "source.bin");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(source, bytes);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var stored = Path.Combine(kb.DataDirectory, "blobs", sha[..2], sha);

        var created = kb.CreateStaging("带原件", ["附件说明"], ttlDays: 1);
        kb.Ingest(
            "带原件",
            ["附件说明"],
            appId: "sdemo.agent",
            artifacts: [new IngestArtifact { Sha256 = sha, Size = bytes.Length, Name = "source.bin", Path = source }],
            entityUri: null,
            retention: RetentionLevel.Staging,
            ttlDays: 1);

        Assert.True(File.Exists(stored));

        // 上面创建了两篇，清理全部过期暂存即可。
        _ = created;
        kb.Clock.Advance(TimeSpan.FromDays(2));
        kb.Service.RunMaintenance();
        kb.Clock.Advance(TimeSpan.FromDays(8));
        kb.Service.RunMaintenance();

        Assert.False(File.Exists(stored));
        Assert.Equal(0, kb.Service.GetStorageStats().BlobBytes);
    }

    [Fact]
    public void 维护可重复执行()
    {
        using var kb = new TestKb(Start);
        kb.CreateStaging("过期", ["内容"], ttlDays: 1);

        kb.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, kb.Service.RunMaintenance().ExpiredDocs);

        var second = kb.Service.RunMaintenance();
        Assert.Equal(0, second.ExpiredDocs);
        Assert.Equal(0, second.PurgedDocs);
        Assert.Equal(0, second.CollectedBlocks);
    }
}
