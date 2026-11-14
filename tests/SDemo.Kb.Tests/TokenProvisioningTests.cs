using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Core;

namespace SDemo.Kb.Tests;

public sealed class TokenProvisioningTests
{
    [Fact]
    public void 首次启动签发token文件且可校验()
    {
        using var kb = new TestKb();
        var directory = Path.Combine(kb.DataDirectory, "tokens");
        var provisioner = new KbTokenProvisioner(kb.Service, directory);

        var results = provisioner.EnsureAll(["sdemo.planner", "sdemo.meeting"]);

        Assert.Equal(2, results.Count);
        foreach (var info in results)
        {
            Assert.True(File.Exists(info.Path));
            var token = File.ReadAllText(info.Path).Trim();
            Assert.Equal(info.AppId, kb.Service.Authenticate(token));
        }
    }

    [Fact]
    public void 重复启动不轮换已有token()
    {
        using var kb = new TestKb();
        var directory = Path.Combine(kb.DataDirectory, "tokens");
        var provisioner = new KbTokenProvisioner(kb.Service, directory);

        var first = provisioner.Ensure("sdemo.planner");
        var firstToken = File.ReadAllText(first.Path).Trim();

        var second = provisioner.Ensure("sdemo.planner");
        var secondToken = File.ReadAllText(second.Path).Trim();

        Assert.Equal(firstToken, secondToken);
        Assert.Equal("sdemo.planner", kb.Service.Authenticate(secondToken));
    }

    [Fact]
    public void token文件丢失会重新签发并让旧token失效()
    {
        using var kb = new TestKb();
        var directory = Path.Combine(kb.DataDirectory, "tokens");
        var provisioner = new KbTokenProvisioner(kb.Service, directory);

        var first = provisioner.Ensure("sdemo.planner");
        var oldToken = File.ReadAllText(first.Path).Trim();
        File.Delete(first.Path);

        var second = provisioner.Ensure("sdemo.planner");
        var newToken = File.ReadAllText(second.Path).Trim();

        Assert.NotEqual(oldToken, newToken);
        Assert.Null(kb.Service.Authenticate(oldToken));
        Assert.Equal("sdemo.planner", kb.Service.Authenticate(newToken));
    }

    [Fact]
    public void token目录列出所有白名单app()
    {
        using var kb = new TestKb();
        var directory = Path.Combine(kb.DataDirectory, "tokens");
        var provisioner = new KbTokenProvisioner(kb.Service, directory);

        var listed = provisioner.List();

        Assert.Equal(KbConstants.ProvisionedApps.Length, listed.Count);
        Assert.All(listed, item => Assert.False(item.Exists));
    }
}
