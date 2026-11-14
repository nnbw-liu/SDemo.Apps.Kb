using System.Globalization;
using System.Text.RegularExpressions;

namespace SDemo.App.Kb.Contracts;

/// <summary>
/// 引用 URI：<c>sdemo.kb/doc/&lt;docId&gt;@v&lt;seq&gt;#&lt;blockId&gt;</c>。
/// 版本段是 <b>seq</b>（人可读的 v7），不是 versionId；两段都可缺省，
/// 但只要作为「引用」写出，就必须带版本段（铁律 4）。
/// </summary>
public sealed partial record KbUri(string DocId, int? VersionSeq, string? BlockId)
{
    public bool HasVersion => VersionSeq is not null;

    public bool HasBlock => !string.IsNullOrEmpty(BlockId);

    /// <summary>规范形式；缺版本/块时按实际存在拼接。</summary>
    public override string ToString()
    {
        var text = KbConstants.DocUriPrefix + DocId;
        if (VersionSeq is { } seq)
        {
            text += "@v" + seq.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrEmpty(BlockId))
        {
            text += "#" + BlockId;
        }

        return text;
    }

    public static string Format(string docId, int versionSeq, string? blockId = null) =>
        new KbUri(docId, versionSeq, blockId).ToString();

    public static string Doc(string docId) => KbConstants.DocUriPrefix + docId;

    public static bool TryParse(string? value, out KbUri? uri, out string error)
    {
        uri = null;
        error = string.Empty;

        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            error = "URI 为空。";
            return false;
        }

        if (!text.StartsWith(KbConstants.DocUriPrefix, StringComparison.Ordinal))
        {
            error = $"URI 必须以 {KbConstants.DocUriPrefix} 开头。";
            return false;
        }

        var rest = text[KbConstants.DocUriPrefix.Length..];
        string? blockId = null;
        var hash = rest.IndexOf('#');
        if (hash >= 0)
        {
            blockId = rest[(hash + 1)..];
            rest = rest[..hash];
            if (blockId.Length == 0)
            {
                error = "块 id 为空。";
                return false;
            }
        }

        int? version = null;
        var at = rest.IndexOf('@');
        if (at >= 0)
        {
            var versionText = rest[(at + 1)..];
            rest = rest[..at];
            versionText = versionText.StartsWith('v') || versionText.StartsWith('V')
                ? versionText[1..]
                : versionText;

            if (!int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var seq) || seq <= 0)
            {
                error = $"版本段「{versionText}」不是正整数。";
                return false;
            }

            version = seq;
        }

        var docId = rest;
        if (docId.Length == 0 || docId.Contains('/') || docId.Contains('@') || docId.Contains('#'))
        {
            error = $"文档 id「{docId}」不合法。";
            return false;
        }

        if (blockId is not null && blockId.Any(char.IsWhiteSpace))
        {
            error = "块 id 不能含空白。";
            return false;
        }

        uri = new KbUri(docId, version, blockId);
        return true;
    }

    public static KbUri Parse(string value) =>
        TryParse(value, out var uri, out var error) ? uri! : throw new FormatException(error);
}
