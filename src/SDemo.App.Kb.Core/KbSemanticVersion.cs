using System.Globalization;
using System.Text.RegularExpressions;

namespace SDemo.App.Kb.Core;

/// <summary>
/// requirement 的语义版本：<c>v1.0 / v1.1 / v2.0</c>。
/// 普通笔记不命名，仍用 seq；requirement 必须可对外说清“第几版”。
/// </summary>
public sealed partial record KbSemanticVersion(int Major, int Minor, int Patch = 0)
{
    public override string ToString() =>
        Patch > 0
            ? $"v{Major}.{Minor}.{Patch}"
            : $"v{Major}.{Minor}";

    public static bool TryParse(string? value, out KbSemanticVersion? version)
    {
        version = null;
        var match = NamedVersionRegex().Match((value ?? string.Empty).Trim());
        if (!match.Success)
        {
            return false;
        }

        var major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minor = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var patch = match.Groups[3].Success
            ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)
            : 0;

        version = new KbSemanticVersion(major, minor, patch);
        return true;
    }

    public static KbSemanticVersion Parse(string value) =>
        TryParse(value, out var parsed) ? parsed! : throw new FormatException($"语义版本不合法：{value}");

    public static KbSemanticVersion Initial => new(1, 0);

    public KbSemanticVersion Next() => this with { Minor = Minor + 1, Patch = 0 };

    /// <summary>用于排序；按 major/minor/patch 比较。</summary>
    public int CompareTo(KbSemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    [GeneratedRegex(@"^v?(\d+)\.(\d+)(?:\.(\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex NamedVersionRegex();
}
