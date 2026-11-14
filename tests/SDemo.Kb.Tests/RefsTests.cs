using SDemo.App.Kb.Contracts;

namespace SDemo.Kb.Tests;

public sealed class RefsTests
{
    [Fact]
    public void 引用带版本_改动后能反查影响面()
    {
        using var kb = new TestKb();
        var target = kb.Ingest("工作区设计", ["部署流程", "验收标准"]);
        var targetContent = kb.Service.Get(new KbGetRequest { DocId = target.DocId })!;
        var targetBlock = targetContent.Blocks[0].BlockId;
        var uri = KbUri.Format(target.DocId, 1, targetBlock);

        var source = kb.Ingest(
            "待办引用",
            ["这条待办基于工作区设计"],
            links: [new IngestLink { To = uri, Rel = "based_on" }]);

        var resolve = kb.Service.Resolve(uri);
        Assert.True(resolve.Exists);
        Assert.False(resolve.Stale);
        Assert.False(resolve.BlockMissing);
        Assert.Equal("工作区设计", resolve.Title);

        var refs = kb.Service.Refs(target.DocId);
        var inbound = Assert.Single(refs.Inbound);
        Assert.Equal("based_on", inbound.Rel);
        Assert.False(inbound.Stale);

        // 来源块被替换：引用过期，块缺失。
        kb.Revise(
            target.DocId,
            1,
            new BlockInput { Op = ChangeOp.Replace, BlockId = targetBlock, Text = "部署流程改成双写" });

        var stale = kb.Service.Resolve(uri);
        Assert.True(stale.Stale);
        Assert.True(stale.BlockMissing);

        var impacted = kb.Service.Refs(target.DocId);
        Assert.True(Assert.Single(impacted.Inbound).Stale);

        var outbound = kb.Service.Refs(source.DocId).Outbound;
        Assert.Single(outbound);
        Assert.True(outbound[0].Stale);
    }

    [Fact]
    public void 删除前给出受影响引用清单()
    {
        using var kb = new TestKb();
        var target = kb.Ingest("目标", ["被引用内容"]);
        var block = kb.Service.Get(new KbGetRequest { DocId = target.DocId })!.Blocks[0].BlockId;

        kb.Ingest(
            "引用方",
            ["引用目标"],
            links: [new IngestLink { To = KbUri.Format(target.DocId, 1, block) }]);

        var deleted = kb.Service.Delete(new KbDeleteRequest { DocId = target.DocId, Reason = "测试删除" });

        Assert.Single(deleted.AffectedRefs);
        Assert.Null(kb.Service.Get(new KbGetRequest { DocId = target.DocId }));

        var resolve = kb.Service.Resolve(KbUri.Format(target.DocId, 1, block));
        Assert.True(resolve.Exists);
        Assert.True(resolve.Deleted);
    }

    [Fact]
    public void 引用必须带版本()
    {
        using var kb = new TestKb();
        var target = kb.Ingest("目标", ["内容"]);

        var error = Assert.Throws<KbException>(() => kb.Ingest(
            "引用方",
            ["引用目标"],
            links: [new IngestLink { To = KbUri.Doc(target.DocId) }]));

        Assert.Equal(KbErrorCodes.InvalidRequest, error.Code);
    }

    [Fact]
    public void 投影appId不能冒充其他app()
    {
        using var kb = new TestKb();

        var request = new IngestRequest
        {
            Source = new IngestSource { AppId = "sdemo.meeting", EntityUri = "sdemo.meeting/meeting/1" },
            Kind = KbKind.Decision,
            Title = "冒名",
            Blocks = [new BlockInput { Text = "内容" }],
        };

        var error = Assert.Throws<KbException>(() => kb.Service.Ingest(request, KbConstants.AppId));

        Assert.Equal(KbErrorCodes.UnauthorizedApp, error.Code);
    }
}
