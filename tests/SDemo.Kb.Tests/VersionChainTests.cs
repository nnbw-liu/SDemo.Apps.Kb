using SDemo.App.Kb.Contracts;

namespace SDemo.Kb.Tests;

public sealed class VersionChainTests
{
    [Fact]
    public void 一次改动只新建一个版本且复用未变块()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("需求 A", ["标题", "第一段 A", "第二段"]);

        Assert.Equal(1, created.Seq);
        Assert.True(created.Created);

        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId });
        Assert.NotNull(v1);
        Assert.Equal(3, v1!.Blocks.Count);

        var revised = kb.Revise(
            created.DocId,
            1,
            new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[1].BlockId, Text = "第一段 D" });

        Assert.Equal(2, revised.Seq);
        Assert.False(revised.Created);

        var v2 = kb.Service.Get(new KbGetRequest { DocId = created.DocId });
        Assert.NotNull(v2);
        Assert.Equal(2, v2!.Version.Seq);

        // 未改的块复用同一个 id：改一段不复制整篇。
        Assert.Equal(v1.Blocks[0].BlockId, v2.Blocks[0].BlockId);
        Assert.Equal(v1.Blocks[2].BlockId, v2.Blocks[2].BlockId);
        Assert.NotEqual(v1.Blocks[1].BlockId, v2.Blocks[1].BlockId);

        Assert.Equal(2, kb.Service.History(created.DocId).Count);
    }

    [Fact]
    public void 相同内容不产生新版本()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("笔记", ["一段话", "另一段"]);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;

        var same = kb.Revise(
            created.DocId,
            1,
            new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[0].BlockId, Text = "一段话" });

        Assert.True(same.NoChange);
        Assert.Equal(1, same.Seq);
        Assert.Single(kb.Service.History(created.DocId));
    }

    [Fact]
    public void 投影重复投递幂等_内容变化才建新版本()
    {
        using var kb = new TestKb();
        var first = kb.Ingest("会议纪要", ["决议一", "决议二"], entityUri: "sdemo.meeting/meeting/01H8");

        Assert.Equal(1, first.Seq);

        var repeat = kb.Ingest("会议纪要", ["决议一", "决议二"], entityUri: "sdemo.meeting/meeting/01H8");
        Assert.True(repeat.Deduplicated);
        Assert.True(repeat.NoChange);
        Assert.Equal(first.DocId, repeat.DocId);
        Assert.Equal(1, repeat.Seq);

        var changed = kb.Ingest("会议纪要", ["决议一", "决议二改"], entityUri: "sdemo.meeting/meeting/01H8");
        Assert.Equal(2, changed.Seq);
        Assert.Equal(first.DocId, changed.DocId);
    }

    [Fact]
    public void 格式变化不产生新块()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("格式", ["第一段\r\n\r\n\r\n第二段"]);

        var content = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;
        Assert.Equal("第一段\n\n第二段", content.Blocks[0].Text);
        Assert.Equal(1, content.Doc.BlockCount);
    }

    [Fact]
    public void 修订后的块级变更事实带理由()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("追溯", ["A 方案"]);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;

        kb.Revise(
            created.DocId,
            1,
            new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[0].BlockId, Text = "D 方案" });

        var changes = kb.Service.Changes(new KbChangesRequest { DocId = created.DocId });
        var change = changes.Changes.Single(item => item.Op == ChangeOp.Replace);
        Assert.Equal(ChangeOp.Replace, change.Op);
        Assert.Equal("A 方案", change.BeforeText);
        Assert.Equal("D 方案", change.AfterText);
        Assert.Equal("测试理由", change.Rationale);
        Assert.Equal(RationaleSource.User, change.RationaleSource);
    }
}

