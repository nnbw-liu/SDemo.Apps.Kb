using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using SDemo.App.Kb.Contracts;

namespace SDemo.App.Kb.Core;

/// <summary>
/// per-app token 的签发与落盘。首次启动给白名单 App 各签一个，写到
/// <c>%LOCALAPPDATA%\SDemo\Hub\apps\sdemo.kb\tokens\&lt;appId&gt;.token</c>。
/// 文件存在且仍能通过 KB 校验时不动；缺失或已失效才重新签发（旧 token 同时失效）。
/// </summary>
public sealed class KbTokenProvisioner
{
    private readonly KbService _service;
    private readonly string _directory;

    public KbTokenProvisioner(KbService service, string? directory = null)
    {
        _service = service;
        _directory = directory ?? KbPaths.TokenDirectory;
    }

    public string Directory => _directory;

    public IReadOnlyList<TokenFileInfo> EnsureAll(IEnumerable<string>? apps = null)
    {
        var results = new List<TokenFileInfo>();
        foreach (var appId in (apps ?? KbConstants.ProvisionedApps).Distinct(StringComparer.Ordinal))
        {
            results.Add(Ensure(appId));
        }

        return results;
    }

    public TokenFileInfo Ensure(string appId)
    {
        var path = PathFor(appId);
        var existing = ReadToken(appId);

        if (existing is not null
            && string.Equals(_service.Authenticate(existing), appId, StringComparison.Ordinal))
        {
            return new TokenFileInfo(appId, path, true, File.GetLastWriteTimeUtc(path), existing[..8] + "…");
        }

        return Issue(appId);
    }

    /// <summary>重新签发（会轮换 DB 里该 app 的 token，并覆盖文件）。</summary>
    public TokenFileInfo Issue(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            throw KbException.Invalid("appId 不能为空。");
        }

        var path = PathFor(appId);
        var token = _service.IssueToken(appId);

        System.IO.Directory.CreateDirectory(_directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, token, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
        RestrictToCurrentUser(path);

        return new TokenFileInfo(
            appId,
            path,
            true,
            File.GetLastWriteTimeUtc(path),
            token[..8] + "…");
    }

    public string? ReadToken(string appId)
    {
        var path = PathFor(appId);
        if (!File.Exists(path))
        {
            return null;
        }

        var value = File.ReadAllText(path).Trim();
        return value.Length == 0 ? null : value;
    }

    public IReadOnlyList<TokenFileInfo> List()
    {
        var results = new List<TokenFileInfo>();
        foreach (var appId in KbConstants.ProvisionedApps)
        {
            var path = PathFor(appId);
            var token = ReadToken(appId);
            results.Add(new TokenFileInfo(
                appId,
                path,
                token is not null,
                token is null ? null : File.GetLastWriteTimeUtc(path),
                token is null ? null : token[..Math.Min(8, token.Length)] + "…"));
        }

        return results;
    }

    private string PathFor(string appId)
    {
        if (appId.Length == 0
            || appId.Contains(Path.DirectorySeparatorChar)
            || appId.Contains(Path.AltDirectorySeparatorChar)
            || appId.Contains("..", StringComparison.Ordinal))
        {
            throw KbException.Invalid($"appId 不合法：{appId}");
        }

        return Path.Combine(_directory, appId + ".token");
    }

    /// <summary>
    /// token 文件只给当前用户读写。Windows 上断开继承并显式授当前用户；
    /// 非 Windows 退化为 chmod 600。失败不阻塞签发（文件仍在 LOCALAPPDATA）。
    /// </summary>
    private static void RestrictToCurrentUser(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var info = new FileInfo(path);
                var security = info.GetAccessControl();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                var user = WindowsIdentity.GetCurrent().User;
                if (user is not null)
                {
                    security.AddAccessRule(new FileSystemAccessRule(
                        user,
                        FileSystemRights.FullControl,
                        AccessControlType.Allow));
                }

                info.SetAccessControl(security);
            }
            else
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception)
        {
            // 非 NTFS / 受限环境：保留默认用户配置目录 ACL，不阻塞使用。
        }
    }
}

public sealed record TokenFileInfo(
    string AppId,
    string Path,
    bool Exists,
    DateTimeOffset? UpdatedAt,
    string? Prefix);
