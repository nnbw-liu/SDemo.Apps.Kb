using SDemo.App.Kb.Contracts;

namespace SDemo.Kb.Tests;

public sealed class SearchTests
{
    [Fact]
    public void 一百篇导入后中文两字查询命中()
    {
        using var kb = new TestKb();

        for (var index = 0; index < 100; index++)
        {
            kb.Ingest(
                $"文档 {index:000}",
                [$"第 {index} 篇：部署方案与版本回滚。唯一标识 术语{index}。"]);
        }

        var response = kb.Service.Search(new KbSearchRequest { Query = "部署", Limit = 20 });

        Assert.False(response.NoHit);
        Assert.NotEmpty(response.Hits);
        Assert.Contains("部署", response.HitTerms);
        Assert.All(response.Hits, hit => Assert.Contains("部署", hit.Snippet));
        Assert.True(response.Hits.Count <= 20);
    }

    [Fact]
    public void 当前态只含持久内容_暂存需要recent()
    {
        using var kb = new TestKb();
        var staged = kb.Service.Create(new KbCreateRequest
        {
            Kind = KbKind.Note,
            Title = "秘密预算",
            Blocks = [new BlockInput { Text = "秘密预算只有三个人知道" }],
        });

        Assert.Equal(RetentionLevel.Staging, kb.Service.Get(new KbGetRequest { DocId = staged.DocId })!.Doc.Retention);

        var current = kb.Service.Search(new KbSearchRequest { Query = "秘密预算", Scope = SearchScope.Current });
        Assert.True(current.NoHit);

        var recent = kb.Service.Search(new KbSearchRequest { Query = "秘密预算", Scope = SearchScope.Recent });
        Assert.False(recent.NoHit);
        Assert.Single(recent.Hits);
    }

    [Fact]
    public void 历史态召回旧版本内容_当前态不召回()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("方案", ["旧方案是本地部署"]);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;

        kb.Revise(
            created.DocId,
            1,
            new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[0].BlockId, Text = "新方案是云端部署" });

        var current = kb.Service.Search(new KbSearchRequest { Query = "旧方案", Scope = SearchScope.Current });
        Assert.True(current.NoHit);

        var history = kb.Service.Search(new KbSearchRequest { Query = "旧方案", Scope = SearchScope.History });
        Assert.False(history.NoHit);
        Assert.Contains(history.Hits, hit => hit.Matched == "history");

        var latest = kb.Service.Search(new KbSearchRequest { Query = "新方案", Scope = SearchScope.Current });
        Assert.False(latest.NoHit);
    }

    [Fact]
    public void 关键词路由把以前切到历史面()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("方案", ["旧方案是本地部署"]);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;

        kb.Revise(
            created.DocId,
            1,
            new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[0].BlockId, Text = "新方案是云端部署" });

        var response = kb.Service.Search(new KbSearchRequest { Query = "以前 旧方案" });

        Assert.Equal(SearchScope.History, response.Scope);
        Assert.False(response.NoHit);
    }

    [Fact]
    public void 变更事实面能回答为什么改()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("部署方案", ["原方案是 A"]);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;

        kb.Service.Revise(
            new ReviseRequest
            {
                Source = new IngestSource { AppId = KbConstants.AppId },
                DocId = created.DocId,
                ExpectedVersion = 1,
                Rationale = "原方案不可行，改成本地双写",
                RationaleSource = RationaleSource.User,
                Blocks =
                [
                    new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[0].BlockId, Text = "新方案是 D" },
                ],
            },
            KbConstants.AppId);

        var response = kb.Service.Search(new KbSearchRequest { Query = "原方案 不可行", Scope = SearchScope.Change });

        Assert.False(response.NoHit);
        Assert.Contains(response.Hits, hit => hit.Matched == "change");
        Assert.Contains("不可行", response.Hits[0].Rationale);
    }

    [Fact]
    public void 过滤器按kind与retention生效()
    {
        using var kb = new TestKb();
        kb.Ingest("参考文档", ["共享关键词 阿尔法"], kind: KbKind.Reference);
        kb.Ingest("需求文档", ["共享关键词 阿尔法"], kind: KbKind.Requirement);

        var reference = kb.Service.Search(new KbSearchRequest
        {
            Query = "阿尔法",
            Filter = new KbSearchFilter { Kind = KbKind.Reference },
        });

        Assert.Single(reference.Hits);
        Assert.Equal(KbKind.Reference, reference.Hits[0].Kind);
    }

    [Fact]
    public void 零命中返回noHit()
    {
        using var kb = new TestKb();
        kb.Ingest("随便", ["内容"]);

        var response = kb.Service.Search(new KbSearchRequest { Query = "完全不存在的词" });

        Assert.True(response.NoHit);
        Assert.Empty(response.Hits);
    }
}
