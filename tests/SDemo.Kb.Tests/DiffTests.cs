using SDemo.App.Kb.Contracts;

namespace SDemo.Kb.Tests;

public sealed class DiffTests
{
    [Fact]
    public void 块级与词级差异能说清哪一句改了()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("差异", ["A 方案", "B 场景", "C 验收", "D 备注"]);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;

        kb.Revise(
            created.DocId,
            1,
            new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[1].BlockId, Text = "B 场景改成 D" },
            new BlockInput { Op = ChangeOp.Remove, BlockId = v1.Blocks[2].BlockId },
            new BlockInput { Op = ChangeOp.Add, Text = "E 新增验收" });

        var diff = kb.Service.Diff(new KbDiffRequest { DocId = created.DocId, From = 1, To = 2 });

        Assert.Equal(1, diff.Stats.Replaced);
        Assert.Equal(1, diff.Stats.Removed);
        Assert.Equal(1, diff.Stats.Added);

        var replaced = Assert.Single(diff.Blocks, block => block.Op == ChangeOp.Replace);
        Assert.Equal("B 场景", replaced.BeforeText);
        Assert.Equal("B 场景改成 D", replaced.AfterText);
        Assert.Contains(replaced.WordDiffs, segment => segment.Op == "insert" && segment.Text.Contains("改成"));
        Assert.Contains(replaced.WordDiffs, segment => segment.Op == "equal" && segment.Text.Contains("B"));
    }

    [Fact]
    public void 默认从上一版到当前版()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("差异", ["第一段", "第二段"]);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;

        kb.Revise(
            created.DocId,
            1,
            new BlockInput { Op = ChangeOp.Add, Text = "第三段" });

        var diff = kb.Service.Diff(new KbDiffRequest { DocId = created.DocId });

        Assert.Equal(1, diff.FromSeq);
        Assert.Equal(2, diff.ToSeq);
        Assert.Equal(1, diff.Stats.Added);
        Assert.Equal(2, diff.Stats.Unchanged);
    }

    [Fact]
    public void 块内词级diff保留未变片段()
    {
        var segments = SDemo.App.Kb.Core.KbDiff.WordDiff("部署流程是本地双写", "部署流程是云端双写");

        Assert.Contains(segments, segment => segment.Op == "equal" && segment.Text.Contains("部署流程是"));
        Assert.Contains(segments, segment => segment.Op == "delete" && segment.Text.Contains("本地"));
        Assert.Contains(segments, segment => segment.Op == "insert" && segment.Text.Contains("云端"));
    }

    [Fact]
    public void 纯格式变化不产生版本()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("格式", ["第一段", "第二段"]);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;

        var result = kb.Revise(
            created.DocId,
            1,
            new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[0].BlockId, Text = "  第一段 \r\n" });

        Assert.True(result.NoChange);
        Assert.Single(kb.Service.History(created.DocId));
    }
}


