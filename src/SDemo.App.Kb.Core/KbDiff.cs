using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Storage;

namespace SDemo.App.Kb.Core;

/// <summary>
/// 差异两层（kb-versioning §4）：
/// 块级用 LCS 对齐块 id（结构影响面），块内用词级 LCS 做高亮。
/// 不嵌 diff 文本——差异是结构化事实，检索只命中块。
/// </summary>
public static class KbDiff
{
    public static DiffResult Build(
        string docId,
        VersionRow from,
        VersionRow to,
        IReadOnlyList<BlockRow> before,
        IReadOnlyList<BlockRow> after)
    {
        var result = new DiffResult
        {
            DocId = docId,
            FromSeq = from.Seq,
            ToSeq = to.Seq,
            Blocks = BlockDiffs(before, after),
        };

        result.Stats.Added = result.Blocks.Count(block => block.Op == ChangeOp.Add);
        result.Stats.Removed = result.Blocks.Count(block => block.Op == ChangeOp.Remove);
        result.Stats.Replaced = result.Blocks.Count(block => block.Op == ChangeOp.Replace);
        result.Stats.Unchanged = before.Count - result.Stats.Removed - result.Stats.Replaced;

        return result;
    }

    /// <summary>把两个版本的块序列对齐成 add / remove / replace；相同块不计入结果。</summary>
    public static List<BlockDiff> BlockDiffs(IReadOnlyList<BlockRow> before, IReadOnlyList<BlockRow> after)
    {
        var diffs = new List<BlockDiff>();

        foreach (var op in AlignBlocks(before, after))
        {
            switch (op.Kind)
            {
                case ChangeOp.Add:
                    diffs.Add(new BlockDiff
                    {
                        Op = ChangeOp.Add,
                        BlockId = op.After!.Id,
                        AfterText = op.After.Text,
                    });
                    break;

                case ChangeOp.Remove:
                    diffs.Add(new BlockDiff
                    {
                        Op = ChangeOp.Remove,
                        BlockId = op.Before!.Id,
                        BeforeText = op.Before.Text,
                    });
                    break;

                case ChangeOp.Replace:
                    diffs.Add(new BlockDiff
                    {
                        Op = ChangeOp.Replace,
                        BlockId = op.After!.Id,
                        BeforeBlockId = op.Before!.Id,
                        BeforeText = op.Before.Text,
                        AfterText = op.After.Text,
                        WordDiffs = WordDiff(op.Before.Text, op.After.Text),
                    });
                    break;
            }
        }

        return diffs;
    }

    /// <summary>词级差异：equal / insert / delete 片段序列；超长文本退化为「全删 + 全插」。</summary>
    public static List<WordDiffSegment> WordDiff(string? before, string? after)
    {
        var left = KbText.DiffTokens(before);
        var right = KbText.DiffTokens(after);

        if (left.Count > KbText.MaxDiffTokens || right.Count > KbText.MaxDiffTokens)
        {
            return BuildCoarse(left, right);
        }

        var n = left.Count;
        var m = right.Count;
        var lcs = new int[n + 1, m + 1];

        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var segments = new List<WordDiffSegment>();
        var i2 = 0;
        var j2 = 0;

        while (i2 < n && j2 < m)
        {
            if (string.Equals(left[i2], right[j2], StringComparison.Ordinal))
            {
                Append(segments, "equal", left[i2]);
                i2++;
                j2++;
            }
            else if (lcs[i2 + 1, j2] >= lcs[i2, j2 + 1])
            {
                Append(segments, "delete", left[i2]);
                i2++;
            }
            else
            {
                Append(segments, "insert", right[j2]);
                j2++;
            }
        }

        while (i2 < n)
        {
            Append(segments, "delete", left[i2++]);
        }

        while (j2 < m)
        {
            Append(segments, "insert", right[j2++]);
        }

        return segments;
    }

    private static List<WordDiffSegment> BuildCoarse(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        var segments = new List<WordDiffSegment>();
        if (left.Count > 0)
        {
            segments.Add(new WordDiffSegment { Op = "delete", Text = string.Concat(left) });
        }

        if (right.Count > 0)
        {
            segments.Add(new WordDiffSegment { Op = "insert", Text = string.Concat(right) });
        }

        return segments;
    }

    private static void Append(List<WordDiffSegment> segments, string op, string text)
    {
        if (segments.Count > 0 && segments[^1].Op == op)
        {
            segments[^1] = new WordDiffSegment { Op = op, Text = segments[^1].Text + text };
            return;
        }

        segments.Add(new WordDiffSegment { Op = op, Text = text });
    }

    private sealed record BlockOp(ChangeOp? Kind, BlockRow? Before, BlockRow? After);

    /// <summary>块级对齐：先 LCS（按 block id），再把相邻的 remove+add 合并成 replace。</summary>
    private static List<BlockOp> AlignBlocks(IReadOnlyList<BlockRow> before, IReadOnlyList<BlockRow> after)
    {
        var n = before.Count;
        var m = after.Count;
        var lcs = new int[n + 1, m + 1];

        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(before[i].Id, after[j].Id, StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var raw = new List<BlockOp>();
        var x = 0;
        var y = 0;

        while (x < n && y < m)
        {
            if (string.Equals(before[x].Id, after[y].Id, StringComparison.Ordinal))
            {
                raw.Add(new BlockOp(null, before[x], after[y]));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                raw.Add(new BlockOp(ChangeOp.Remove, before[x], null));
                x++;
            }
            else
            {
                raw.Add(new BlockOp(ChangeOp.Add, null, after[y]));
                y++;
            }
        }

        while (x < n)
        {
            raw.Add(new BlockOp(ChangeOp.Remove, before[x++], null));
        }

        while (y < m)
        {
            raw.Add(new BlockOp(ChangeOp.Add, null, after[y++]));
        }

        // remove 后紧跟 add：视为同一位置的 replace（A → D 的典型形态）。
        var merged = new List<BlockOp>(raw.Count);
        var index = 0;
        while (index < raw.Count)
        {
            if (raw[index].Kind == ChangeOp.Remove)
            {
                var removes = new List<BlockRow>();
                while (index < raw.Count && raw[index].Kind == ChangeOp.Remove)
                {
                    removes.Add(raw[index].Before!);
                    index++;
                }

                var adds = new List<BlockRow>();
                while (index < raw.Count && raw[index].Kind == ChangeOp.Add)
                {
                    adds.Add(raw[index].After!);
                    index++;
                }

                var pairs = Math.Min(removes.Count, adds.Count);
                for (var pair = 0; pair < pairs; pair++)
                {
                    merged.Add(new BlockOp(ChangeOp.Replace, removes[pair], adds[pair]));
                }

                for (var r = pairs; r < removes.Count; r++)
                {
                    merged.Add(new BlockOp(ChangeOp.Remove, removes[r], null));
                }

                for (var a = pairs; a < adds.Count; a++)
                {
                    merged.Add(new BlockOp(ChangeOp.Add, null, adds[a]));
                }

                continue;
            }

            if (raw[index].Kind == ChangeOp.Add)
            {
                merged.Add(raw[index]);
                index++;
                continue;
            }

            merged.Add(raw[index]);
            index++;
        }

        return merged;
    }
}

