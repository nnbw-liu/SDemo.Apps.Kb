using SDemo.App.Kb.Contracts;

namespace SDemo.Kb.Tests;

public sealed class DocumentListTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 文档列表带摘要与引用数并支持过滤()
    {
        using var kb = new TestKb(Start);
        var target = kb.Ingest("部署方案", ["第一段正文内容", "第二段"], kind: KbKind.Requirement);
        kb.Ingest("普通笔记", ["另一篇内容"], kind: KbKind.Note);
        var blockId = kb.Service.Get(new KbGetRequest { DocId = target.DocId })!.Blocks[0].BlockId;
        kb.Ingest(
            "引用方",
            ["引用目标"],
            links: [new IngestLink { To = KbUri.Format(target.DocId, 1, blockId) }]);

        var all = kb.Service.ListDocuments(new DocListRequest());
        Assert.Equal(3, all.Total);
        Assert.Equal(3, all.Items.Count);

        var deployment = all.Items.Single(item => item.DocId == target.DocId);
        Assert.Equal("第一段正文内容", deployment.Excerpt);
        Assert.Equal(1, deployment.InboundRefCount);
        Assert.Equal(KbKind.Requirement, deployment.Kind);

        var byQuery = kb.Service.ListDocuments(new DocListRequest { Query = "部署" });
        Assert.Single(byQuery.Items);
        Assert.Equal(target.DocId, byQuery.Items[0].DocId);

        var byKind = kb.Service.ListDocuments(new DocListRequest { Kind = KbKind.Note });
        Assert.Single(byKind.Items);
        Assert.Equal("普通笔记", byKind.Items[0].Title);
    }

    [Fact]
    public void 文档列表能按保留等级过滤()
    {
        using var kb = new TestKb(Start);
        kb.CreateStaging("暂存", ["暂存内容"], ttlDays: 10);
        kb.Ingest("归档", ["归档内容"]);

        var staging = kb.Service.ListDocuments(new DocListRequest { Retention = RetentionLevel.Staging });

        Assert.Single(staging.Items);
        Assert.Equal("暂存", staging.Items[0].Title);
        Assert.NotNull(staging.Items[0].ExpiresAt);
    }

    [Fact]
    public void 批量resolve按顺序返回存在性()
    {
        using var kb = new TestKb(Start);
        var doc = kb.Ingest("目标", ["内容"]);

        var result = kb.Service.ResolveMany(new ResolveBatchRequest
        {
            Uris = [KbUri.Doc(doc.DocId), "sdemo.kb/doc/missing@v1"],
        });

        Assert.Equal(2, result.Items.Count);
        Assert.True(result.Items[0].Exists);
        Assert.False(result.Items[1].Exists);
    }
}
