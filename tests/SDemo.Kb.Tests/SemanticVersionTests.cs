using SDemo.App.Kb.Contracts;

namespace SDemo.Kb.Tests;

public sealed class SemanticVersionTests
{
    [Fact]
    public void requirement从v1_0开始并按minor自动递增()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("需求", ["A"], kind: KbKind.Requirement);

        Assert.Equal("v1.0", created.NamedVersion);

        var first = kb.Revise(
            created.DocId,
            1,
            new BlockInput { Op = ChangeOp.Replace, BlockId = created.BlockIds[0], Text = "A1" });

        Assert.Equal("v1.1", first.NamedVersion);
        Assert.Equal("v1.1", kb.Service.History(created.DocId)[0].NamedVersion);
    }

    [Fact]
    public void requirement可以显式跳到major版本但不能倒退()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("需求", ["A"], kind: KbKind.Requirement);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;

        var major = kb.Service.Revise(
            new ReviseRequest
            {
                Source = new IngestSource { AppId = KbConstants.AppId },
                DocId = created.DocId,
                ExpectedVersion = 1,
                NamedVersion = "v2.0",
                Blocks = [new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[0].BlockId, Text = "A2" }],
            },
            KbConstants.AppId);

        Assert.Equal("v2.0", major.NamedVersion);

        Assert.Throws<KbException>(() => kb.Service.Revise(
            new ReviseRequest
            {
                Source = new IngestSource { AppId = KbConstants.AppId },
                DocId = created.DocId,
                ExpectedVersion = 2,
                NamedVersion = "v1.9",
                Blocks = [new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[0].BlockId, Text = "A3" }],
            },
            KbConstants.AppId));
    }

    [Fact]
    public void 普通笔记不允许命名版本()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("笔记", ["A"], kind: KbKind.Note);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;

        Assert.Null(created.NamedVersion);
        Assert.Throws<KbException>(() => kb.Service.Revise(
            new ReviseRequest
            {
                Source = new IngestSource { AppId = KbConstants.AppId },
                DocId = created.DocId,
                ExpectedVersion = 1,
                NamedVersion = "v1.1",
                Blocks = [new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[0].BlockId, Text = "A1" }],
            },
            KbConstants.AppId));
    }

    [Fact]
    public void 同一文档的语义版本不能重复()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("需求", ["A"], kind: KbKind.Requirement);

        kb.Service.Revise(
            new ReviseRequest
            {
                Source = new IngestSource { AppId = KbConstants.AppId },
                DocId = created.DocId,
                ExpectedVersion = 1,
                AllowBranch = true,
                NamedVersion = "v1.1",
                Blocks = [new BlockInput { Op = ChangeOp.Replace, BlockId = created.BlockIds[0], Text = "A1" }],
            },
            KbConstants.AppId);

        Assert.Throws<KbException>(() => kb.Service.Revise(
            new ReviseRequest
            {
                Source = new IngestSource { AppId = KbConstants.AppId },
                DocId = created.DocId,
                ExpectedVersion = 1,
                AllowBranch = true,
                NamedVersion = "v1.1",
                Blocks = [new BlockInput { Op = ChangeOp.Replace, BlockId = created.BlockIds[0], Text = "A2" }],
            },
            KbConstants.AppId));
    }

    [Fact]
    public void 从旧版本拉分支再合并两个分支()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("需求", ["A"], kind: KbKind.Requirement);

        var branch1 = kb.Service.Revise(
            new ReviseRequest
            {
                Source = new IngestSource { AppId = KbConstants.AppId },
                DocId = created.DocId,
                ExpectedVersion = 1,
                AllowBranch = true,
                NamedVersion = "v1.1",
                Blocks = [new BlockInput { Op = ChangeOp.Replace, BlockId = created.BlockIds[0], Text = "A-左" }],
            },
            KbConstants.AppId);

        var branch2 = kb.Service.Revise(
            new ReviseRequest
            {
                Source = new IngestSource { AppId = KbConstants.AppId },
                DocId = created.DocId,
                ExpectedVersion = 1,
                AllowBranch = true,
                NamedVersion = "v1.2",
                Blocks = [new BlockInput { Op = ChangeOp.Replace, BlockId = created.BlockIds[0], Text = "A-右" }],
            },
            KbConstants.AppId);

        var versions = kb.Service.History(created.DocId);
        Assert.Single(versions.Single(version => version.Seq == 2).ParentVersionIds);
        Assert.Single(versions.Single(version => version.Seq == 3).ParentVersionIds);
        Assert.Equal("v1.1", branch1.NamedVersion);
        Assert.Equal("v1.2", branch2.NamedVersion);

        var merged = kb.Service.Merge(
            new KbMergeRequest
            {
                DocId = created.DocId,
                LeftVersion = 2,
                RightVersion = 3,
                ExpectedVersion = 3,
                NamedVersion = "v2.0",
                Rationale = "拉分支后合并",
                Blocks = [new BlockInput { Op = ChangeOp.Add, Text = "A-合并" }],
            },
            KbConstants.AppId);

        Assert.Equal(4, merged.Seq);
        var mergeVersion = kb.Service.History(created.DocId).Single(version => version.Seq == 4);
        Assert.Equal(2, mergeVersion.ParentVersionIds.Count);
        Assert.Equal("v2.0", mergeVersion.NamedVersion);
    }

    [Fact]
    public void 合并生成双父版本并保留语义版本()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("需求", ["A", "B"], kind: KbKind.Requirement);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;

        kb.Service.Revise(
            new ReviseRequest
            {
                Source = new IngestSource { AppId = KbConstants.AppId },
                DocId = created.DocId,
                ExpectedVersion = 1,
                NamedVersion = "v1.1",
                Blocks = [new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[0].BlockId, Text = "A1" }],
            },
            KbConstants.AppId);

        var merged = kb.Service.Merge(
            new KbMergeRequest
            {
                DocId = created.DocId,
                LeftVersion = 1,
                RightVersion = 2,
                ExpectedVersion = 2,
                NamedVersion = "v2.0",
                Rationale = "合并两个分叉",
                Blocks =
                [
                    new BlockInput { Op = ChangeOp.Add, Text = "A2" },
                    new BlockInput { Op = ChangeOp.Add, Text = "B" },
                ],
            },
            KbConstants.AppId);

        Assert.Equal(3, merged.Seq);
        Assert.Equal("v2.0", merged.NamedVersion);

        var versions = kb.Service.History(created.DocId);
        var mergeVersion = versions.Single(version => version.Seq == 3);
        Assert.Equal(2, mergeVersion.ParentVersionIds.Count);
        Assert.Contains(versions.Single(version => version.Seq == 1).VersionId, mergeVersion.ParentVersionIds);
        Assert.Contains(versions.Single(version => version.Seq == 2).VersionId, mergeVersion.ParentVersionIds);
    }
}

