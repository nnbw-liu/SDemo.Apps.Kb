namespace SDemo.App.Kb.Contracts;

/// <summary>标识符生成。文档与版本都用 UUIDv7（时间有序，索引友好）。</summary>
public static class KbIds
{
    public static string NewDocId() => Guid.CreateVersion7().ToString("D");

    public static string NewVersionId() => Guid.CreateVersion7().ToString("D");

    public static string NewBlockId() => Guid.CreateVersion7().ToString("D");

    public static string NewToken() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public static bool LooksLikeId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length is >= 8 and <= 64;

    /// <summary>版本序号的人类可读形式：<c>v7</c>。</summary>
    public static string FormatSeq(int seq) => "v" + seq.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>本机目录约定（kb-design §9）。</summary>
public static class KbPaths
{
    public static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SDemo",
        "Apps",
        "sdemo.kb");

    public static string DatabaseFile(string dataDirectory) => Path.Combine(dataDirectory, "kb.db");

    public static string BlobsDirectory(string dataDirectory) => Path.Combine(dataDirectory, "blobs");

    public static string CacheDirectory(string dataDirectory) => Path.Combine(dataDirectory, "cache");

    /// <summary>运行期连接信息，供 Hub / 其它 App 发现本服务。</summary>
    public static string RuntimeDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SDemo",
        "Hub",
        "apps");

    public static string RuntimeFile(string appId = KbConstants.AppId) =>
        Path.Combine(RuntimeDirectory, appId + ".json");
}
