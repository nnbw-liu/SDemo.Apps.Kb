using SDemo.App.Kb.Contracts;

namespace SDemo.Kb.Tests;

public sealed class ImportTextTests
{
    [Fact]
    public void 从文本导入同名文件再次导入按块diff建新版本()
    {
        using var kb = new TestKb();
        const string markdown = """
            # 部署方案

            第一段：本地双写。

            ## 验收

            第二段：回滚演练。
            """;

        var first = kb.Service.ImportText("deploy.md", markdown);

        Assert.True(first.Created);
        Assert.Equal(1, first.Seq);
        Assert.Equal(4, first.BlockCount);

        var content = kb.Service.Get(new KbGetRequest { DocId = first.DocId })!;
        Assert.True(content.Blocks[0].IsHeading);
        Assert.Equal("部署方案", content.Blocks[0].Text);
        Assert.Equal("deploy", content.Doc.Title);

        var same = kb.Service.ImportText("deploy.md", markdown);
        Assert.True(same.Deduplicated);
        Assert.Equal(1, same.Seq);

        var changed = kb.Service.ImportText("deploy.md", markdown.Replace("本地双写", "云端双写"));
        Assert.Equal(2, changed.Seq);
        Assert.Equal(first.DocId, changed.DocId);
    }

    [Fact]
    public void 导入拒绝未支持的格式()
    {
        using var kb = new TestKb();

        Assert.Throws<KbException>(() => kb.Service.ImportText("x.pdf", "not a pdf"));
    }

    [Fact]
    public void 空文本导入被拒绝()
    {
        using var kb = new TestKb();

        Assert.Throws<KbException>(() => kb.Service.ImportText("empty.md", "   "));
    }
}
