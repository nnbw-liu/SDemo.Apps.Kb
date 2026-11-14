using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Core;
using SDemo.App.Kb.Storage;

namespace SDemo.Kb.Tests;

public sealed class TextTests
{
    [Fact]
    public void 归一化后只改格式不产生新hash()
    {
        var a = KbText.Normalize("第一段\r\n\r\n\r\n第二段");
        var b = KbText.Normalize("  第一段  \n\n第二段   ");

        Assert.Equal("第一段\n\n第二段", a);
        Assert.Equal(a, b);
        Assert.Equal(KbText.Hash(a), KbText.Hash(b));
    }

    [Fact]
    public void 中文按2gram切分()
    {
        var tokens = KbTokenizer.Tokenize("部署方案");

        Assert.Equal(["部署", "署方", "方案"], tokens);
    }

    [Fact]
    public void 混合文本中英文与数字分别切分()
    {
        var tokens = KbTokenizer.Tokenize("KB 2026 部署");

        Assert.Contains("kb", tokens);
        Assert.Contains("2026", tokens);
        Assert.Contains("部署", tokens);
    }

    [Fact]
    public void 引用的版本段可以省略或用v前缀()
    {
        Assert.True(KbUri.TryParse("sdemo.kb/doc/abc@v3#b1", out var uri, out _));
        Assert.Equal("abc", uri!.DocId);
        Assert.Equal(3, uri.VersionSeq);
        Assert.Equal("b1", uri.BlockId);
        Assert.Equal("sdemo.kb/doc/abc@v3#b1", uri.ToString());

        Assert.True(KbUri.TryParse("sdemo.kb/doc/abc@12", out var noBlock, out _));
        Assert.Equal(12, noBlock!.VersionSeq);
        Assert.Null(noBlock.BlockId);
    }

    [Fact]
    public void 非KB的URI被拒绝()
    {
        Assert.False(KbUri.TryParse("sdemo.planner/todo/1", out _, out var error));
        Assert.NotEmpty(error);
    }
}
