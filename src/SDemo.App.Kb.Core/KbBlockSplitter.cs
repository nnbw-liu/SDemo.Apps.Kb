using System.Text;
using System.Text.Json;

namespace SDemo.App.Kb.Core;

/// <summary>一个待写入的块：正文 + 结构 + 可选 anchor。</summary>
public sealed record TextBlockSpec(
    string Text,
    bool IsHeading,
    int HeadingLevel,
    string? AnchorJson);

/// <summary>
/// 确定性切块（P4-a 只做 md / txt；docx / pptx / xlsx / pdf 在 P4-d）。
/// 粒度是「标题 + 段落」：行级 diff 全是噪音，整篇又说不清哪句变了。
/// 围栏代码块整块保留，不按空行切开。
/// </summary>
public static class KbBlockSplitter
{
    public static IReadOnlyList<TextBlockSpec> SplitMarkdown(string markdown)
    {
        var blocks = new List<TextBlockSpec>();
        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new StringBuilder();
        var paragraphStart = -1;
        var offset = 0;
        var inFence = false;
        var fence = new StringBuilder();
        var fenceStart = 0;

        void FlushParagraph(int end)
        {
            if (paragraph.Length == 0)
            {
                return;
            }

            var text = paragraph.ToString().Trim();
            if (text.Length > 0)
            {
                blocks.Add(new TextBlockSpec(text, false, 0, OffsetAnchor(paragraphStart, end)));
            }

            paragraph.Clear();
            paragraphStart = -1;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineStart = offset;
            var lineEnd = offset + line.Length;

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (!inFence)
                {
                    FlushParagraph(lineStart);
                    inFence = true;
                    fenceStart = lineStart;
                    fence.Clear();
                    fence.AppendLine(line);
                }
                else
                {
                    fence.AppendLine(line);
                    var text = fence.ToString().Trim();
                    if (text.Length > 0)
                    {
                        blocks.Add(new TextBlockSpec(text, false, 0, OffsetAnchor(fenceStart, lineEnd)));
                    }

                    fence.Clear();
                    inFence = false;
                }

                offset = lineEnd + 1;
                continue;
            }

            if (inFence)
            {
                fence.AppendLine(line);
                offset = lineEnd + 1;
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                FlushParagraph(lineStart);
                offset = lineEnd + 1;
                continue;
            }

            if (TryParseHeading(trimmed, out var level, out var headingText))
            {
                FlushParagraph(lineStart);
                blocks.Add(new TextBlockSpec(headingText, true, level, OffsetAnchor(lineStart, lineEnd)));
                offset = lineEnd + 1;
                continue;
            }

            if (paragraph.Length == 0)
            {
                paragraphStart = lineStart;
                paragraph.Append(trimmed);
            }
            else
            {
                paragraph.Append('\n').Append(trimmed);
            }

            offset = lineEnd + 1;
        }

        FlushParagraph(offset);
        if (inFence && fence.Length > 0)
        {
            blocks.Add(new TextBlockSpec(fence.ToString().Trim(), false, 0, OffsetAnchor(fenceStart, offset)));
        }

        return blocks;
    }

    public static IReadOnlyList<TextBlockSpec> SplitPlainText(string text)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var blocks = new List<TextBlockSpec>();
        var offset = 0;

        foreach (var paragraph in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = paragraph.Trim();
            var start = normalized.IndexOf(trimmed, offset, StringComparison.Ordinal);
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (start < 0)
            {
                start = offset;
            }

            blocks.Add(new TextBlockSpec(trimmed, false, 0, OffsetAnchor(start, start + trimmed.Length)));
            offset = start + trimmed.Length;
        }

        return blocks;
    }

    /// <summary>导入入口：按扩展名选切法；未知文本按纯文本处理。</summary>
    public static IReadOnlyList<TextBlockSpec> SplitByExtension(string path, string content)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".md" or ".markdown" ? SplitMarkdown(content) : SplitPlainText(content);
    }

    private static bool TryParseHeading(string line, out int level, out string text)
    {
        level = 0;
        text = string.Empty;
        var index = 0;
        while (index < line.Length && line[index] == '#')
        {
            index++;
        }

        if (index is < 1 or > 6 || index >= line.Length || !char.IsWhiteSpace(line[index]))
        {
            return false;
        }

        level = index;
        text = line[index..].Trim();
        return text.Length > 0;
    }

    internal static string OffsetAnchor(int start, int end) => JsonSerializer.Serialize(new
    {
        kind = "offset",
        value = new { start, end },
    });
}
