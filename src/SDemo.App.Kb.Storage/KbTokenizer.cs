using System.Text;

namespace SDemo.App.Kb.Storage;

/// <summary>
/// 中文 2-gram 预分词（复用 workspace-memory 的已验证方案）：
/// <list type="bullet">
/// <item>连续 CJK 段切重叠 2-gram（单字段保留 1-gram）；</item>
/// <item>拉丁 / 数字按非字母数字切分并小写。</item>
/// </list>
/// 索引与查询共用同一个函数，保证 token 对齐；<c>unicode61</c> 会把整段中文当一个 token，
/// <c>trigram</c> 又不支持两字查询，所以必须预分词。
/// </summary>
public static class KbTokenizer
{
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var tokens = new List<string>();
        var run = new StringBuilder();
        bool? runIsCjk = null;

        foreach (var character in text)
        {
            if (!char.IsLetterOrDigit(character))
            {
                Flush();
                continue;
            }

            var isCjk = IsCjk(character);
            if (runIsCjk is not null && runIsCjk != isCjk)
            {
                Flush();
            }

            runIsCjk = isCjk;
            run.Append(character);
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (run.Length == 0)
            {
                runIsCjk = null;
                return;
            }

            var value = run.ToString().ToLowerInvariant();
            run.Clear();
            runIsCjk = null;

            if (!IsCjk(value[0]) || value.Length == 1)
            {
                tokens.Add(value);
                return;
            }

            for (var index = 0; index + 2 <= value.Length; index++)
            {
                tokens.Add(value.Substring(index, 2));
            }
        }
    }

    /// <summary>把文本转成 FTS5 的 terms 列内容（去重、空格分隔）。</summary>
    public static string BuildTerms(string text, bool distinct = false)
    {
        var tokens = Tokenize(text);
        return string.Join(' ', distinct ? tokens.Distinct(StringComparer.Ordinal) : tokens);
    }

    /// <summary>标题加权：拼 N 份，近似 workspace-memory 的「标题 ×4」。</summary>
    public static string BuildWeightedTerms(string title, string body, int titleWeight)
    {
        var titleTerms = string.Join(' ', Tokenize(title).Distinct(StringComparer.Ordinal));
        var bodyTerms = BuildTerms(body);

        var parts = new List<string>(titleWeight + 1);
        for (var index = 0; index < titleWeight; index++)
        {
            parts.Add(titleTerms);
        }

        parts.Add(bodyTerms);
        return string.Join(' ', parts);
    }

    public static string Quote(string token) => $"\"{token.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    public static bool IsCjk(char character) =>
        character is >= '\u3400' and <= '\u4DBF'
        || character is >= '\u4E00' and <= '\u9FFF'
        || character is >= '\uF900' and <= '\uFAFF';
}
