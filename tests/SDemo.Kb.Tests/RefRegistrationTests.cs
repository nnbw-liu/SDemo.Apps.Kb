using SDemo.App.Kb.Contracts;

namespace SDemo.Kb.Tests;

public sealed class RefRegistrationTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Planner待办登记引用后影响面能看到()
    {
        using var kb = new TestKb(Start);
        var target = kb.Ingest("工作区设计", ["部署流程", "验收标准"]);
        var blockId = kb.Service.Get(new KbGetRequest { DocId = target.DocId })!.Blocks[0].BlockId;

        var registered = kb.Service.RegisterRef(
            new RefRegistrationRequest
            {
                Source = new IngestSource
                {
                    AppId = "sdemo.planner",
                    EntityUri = "sdemo.planner/todo/01H8TODO",
                },
                To = KbUri.Format(target.DocId, 1, blockId),
                Rel = "based_on",
            },
            "sdemo.planner");

        Assert.True(registered.Created);
        Assert.Equal("sdemo.planner/todo/01H8TODO", registered.FromUri);

        var refs = kb.Service.Refs(target.DocId);
        var inbound = Assert.Single(refs.Inbound);
        Assert.Equal("sdemo.planner", inbound.OwnerApp);
        Assert.Equal("based_on", inbound.Rel);
        Assert.False(inbound.Stale);

        var own = kb.Service.ListRefs("sdemo.planner/todo/01H8TODO", "sdemo.planner");
        Assert.Single(own.Items);

        // 其它 App 不能看 Planner 的登记。
        var other = kb.Service.ListRefs("sdemo.planner/todo/01H8TODO", "sdemo.meeting");
        Assert.Empty(other.Items);

        var retracted = kb.Service.RetractRefs(
            new RefRetractRequest
            {
                Source = new IngestSource
                {
                    AppId = "sdemo.planner",
                    EntityUri = "sdemo.planner/todo/01H8TODO",
                },
            },
            "sdemo.planner");

        Assert.Equal(1, retracted.Removed);
        Assert.Empty(kb.Service.Refs(target.DocId).Inbound);
    }

    [Fact]
    public void 外部引用保护块不被GC()
    {
        using var kb = new TestKb(Start);
        kb.Service.SaveRetentionSettings(new RetentionSettings { NoteVersions = 1 });

        var target = kb.Ingest("被引用的设计", ["保留段", "旧段"]);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = target.DocId })!;
        var oldBlock = v1.Blocks[1].BlockId;

        kb.Revise(
            target.DocId,
            1,
            new BlockInput { Op = ChangeOp.Replace, BlockId = oldBlock, Text = "旧段改" });

        kb.Service.RegisterRef(
            new RefRegistrationRequest
            {
                Source = new IngestSource
                {
                    AppId = "sdemo.planner",
                    EntityUri = "sdemo.planner/todo/protect",
                },
                To = KbUri.Format(target.DocId, 1, oldBlock),
            },
            "sdemo.planner");

        var report = kb.Service.RunMaintenance();

        Assert.Equal(0, report.CollectedBlocks);
        Assert.NotNull(kb.Store.Read().GetBlock(oldBlock));
    }

    [Fact]
    public void 外部引用保护回收站文档不被物理清理()
    {
        using var kb = new TestKb(Start);
        var target = kb.CreateStaging("被待办引用的纪要", ["内容"], ttlDays: 1);
        var blockId = kb.Service.Get(new KbGetRequest { DocId = target.DocId })!.Blocks[0].BlockId;

        kb.Service.RegisterRef(
            new RefRegistrationRequest
            {
                Source = new IngestSource
                {
                    AppId = "sdemo.planner",
                    EntityUri = "sdemo.planner/todo/staging",
                },
                To = KbUri.Format(target.DocId, 1, blockId),
            },
            "sdemo.planner");

        kb.Clock.Advance(TimeSpan.FromDays(2));
        kb.Service.RunMaintenance();
        kb.Clock.Advance(TimeSpan.FromDays(8));
        var report = kb.Service.RunMaintenance();

        Assert.Equal(1, report.ProtectedDocs);
        Assert.Equal(0, report.PurgedDocs);

        var resolved = kb.Service.Resolve(KbUri.Format(target.DocId, 1, blockId));
        Assert.True(resolved.Deleted);
        Assert.False(resolved.BlockMissing);
    }

    [Fact]
    public void 登记引用拒绝冒名app()
    {
        using var kb = new TestKb(Start);
        var target = kb.Ingest("目标", ["内容"]);
        var blockId = kb.Service.Get(new KbGetRequest { DocId = target.DocId })!.Blocks[0].BlockId;

        var request = new RefRegistrationRequest
        {
            Source = new IngestSource { AppId = "sdemo.meeting", EntityUri = "sdemo.meeting/meeting/1" },
            To = KbUri.Format(target.DocId, 1, blockId),
        };

        var error = Assert.Throws<KbException>(() => kb.Service.RegisterRef(request, "sdemo.planner"));
        Assert.Equal(KbErrorCodes.UnauthorizedApp, error.Code);
    }

    [Fact]
    public void 引用登记必须带版本()
    {
        using var kb = new TestKb(Start);
        var target = kb.Ingest("目标", ["内容"]);

        var error = Assert.Throws<KbException>(() => kb.Service.RegisterRef(
            new RefRegistrationRequest
            {
                Source = new IngestSource { AppId = "sdemo.planner", EntityUri = "sdemo.planner/todo/x" },
                To = KbUri.Doc(target.DocId),
            },
            "sdemo.planner"));

        Assert.Equal(KbErrorCodes.InvalidRequest, error.Code);
    }
}
