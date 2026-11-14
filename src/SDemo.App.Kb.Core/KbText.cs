using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SDemo.App.Kb.Core;

/// <summary>
/// 文本归一化与内容寻址。归一化的唯一目的：**只改格式不产生新块**。
/// 规则：统一换行、去掉行首尾空白、压缩行内空白、去掉首尾空行；大小写保留。
/// </summary>
public static partial class KbText
{
    /// <summary>词级 diff 的 token 上限；超长段落退化为整块替换。</summary>
    public const int MaxDiffTokens = 1200;

    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var unified = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var lines = unified.Split('\n')
            .Select(line => CollapseSpaces().Replace(line, " ").Trim())
            .ToList();

        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        // 连续空行压成一行：段落之间的空行数属于格式，不属于内容。
        var collapsed = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Length == 0 && collapsed.Count > 0 && collapsed[^1].Length == 0)
            {
                continue;
            }

            collapsed.Add(line);
        }

        return string.Join('\n', collapsed);
    }

    public static string Hash(string normalizedText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>版本 content hash：块 hash 序列的整体哈希。</summary>
    public static string ContentHash(IEnumerable<string> blockHashes)
    {
        var joined = string.Join('\n', blockHashes);
        return Hash(joined);
    }

    /// <summary>词级 diff 用的 token：CJK 单字、拉丁词、空白、标点各自成 token。</summary>
    public static IReadOnlyList<string> DiffTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var tokens = new List<string>();
        foreach (Match match in DiffTokenRegex().Matches(text))
        {
            tokens.Add(match.Value);
        }

        return tokens;
    }

    public static string Snippet(string text, string term, int maxChars = 160)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var index = string.IsNullOrEmpty(term)
            ? 0
            : text.IndexOf(term, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            index = 0;
        }

        var start = Math.Max(0, index - 40);
        var length = Math.Min(maxChars, text.Length - start);
        var snippet = text.Substring(start, length).Replace('\n', ' ').Trim();

        if (start > 0)
        {
            snippet = "…" + snippet;
        }

        if (start + length < text.Length)
        {
            snippet += "…";
        }

        return snippet;
    }

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex CollapseSpaces();

    [GeneratedRegex(@"[\u3400-\u9FFF]|[A-Za-z0-9_]+|[\r\n]+|[^\S\r\n]|[^\w\s]")]
    private static partial Regex DiffTokenRegex();
}
