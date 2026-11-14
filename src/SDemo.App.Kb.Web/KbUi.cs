using SDemo.App.Kb.Contracts;
using System.Text.Json;

namespace SDemo.App.Kb.Web;

/// <summary>界面上用的纯展示映射；不放业务规则。</summary>
public static class KbUi
{
    public static string KindLabel(KbKind kind) => kind switch
    {
        KbKind.Requirement => "需求",
        KbKind.Decision => "决策",
        KbKind.Reference => "资料",
        _ => "笔记",
    };

    public static string RetentionLabel(RetentionLevel retention) =>
        retention == RetentionLevel.Durable ? "归档" : "暂存";

    public static string RetentionClass(RetentionLevel retention) =>
        retention == RetentionLevel.Durable ? "ret ret-durable" : "ret ret-staging";

    public static string AuthorLabel(VersionAuthor author) => author switch
    {
        VersionAuthor.User => "你",
        VersionAuthor.Ai => "DesktopAgent",
        VersionAuthor.Import => "导入",
        VersionAuthor.Meeting => "会议投影",
        _ => "系统",
    };

    public static string RationaleLabel(RationaleSource? source) => source switch
    {
        RationaleSource.User => "你",
        RationaleSource.Ai => "Agent",
        RationaleSource.Import => "导入",
        RationaleSource.Meeting => "会议",
        _ => "系统",
    };

    public static string RelativeTime(DateTimeOffset value)
    {
        var delta = DateTimeOffset.UtcNow - value;
        if (delta < TimeSpan.FromMinutes(1))
        {
            return "刚刚";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"{(int)delta.TotalMinutes} 分钟前";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"{(int)delta.TotalHours} 小时前";
        }

        if (delta < TimeSpan.FromDays(7))
        {
            return $"{(int)delta.TotalDays} 天前";
        }

        return value.ToLocalTime().ToString("MM-dd");
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:0.#} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):0.#} MB";
        }

        return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
    }

    public static string ExpiryText(int daysRemaining) => daysRemaining switch
    {
        <= 0 => "今天到期",
        int.MaxValue => "长期",
        1 => "1 天后清理",
        _ => $"{daysRemaining} 天后清理",
    };

    public static string ScopeLabel(SearchScope scope) => scope switch
    {
        SearchScope.History => "历史态",
        SearchScope.Change => "变更事实",
        SearchScope.Recent => "含暂存",
        SearchScope.All => "全部",
        _ => "当前态",
    };

    public static string MatchedLabel(string matched) => matched switch
    {
        "history" => "历史态",
        "change" => "变更事实",
        _ => "当前态",
    };

    public static string ShortId(string id) => id.Length <= 8 ? id : id[..8];

    /// <summary>外部来源 URI 太长，右栏只显示去掉 sdemo. 前缀后的尾部。</summary>
    public static string ShortFrom(string uri)
    {
        var value = uri.StartsWith("sdemo.", StringComparison.Ordinal) ? uri["sdemo.".Length..] : uri;
        return value.Length <= 28 ? value : "…" + value[^27..];
    }

    public static string BlockAnchor(BlockView block) =>
        $"#{ShortId(block.BlockId)} · 第 {block.Ordinal + 1} 块";

    /// <summary>把 anchor JSON 变成人能读的位置提示；解析失败时回退为空。</summary>
    public static string AnchorLabel(string? anchorJson)
    {
        if (string.IsNullOrWhiteSpace(anchorJson))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(anchorJson);
            var root = document.RootElement;
            var kind = root.GetProperty("kind").GetString();
            var value = root.GetProperty("value");

            return kind switch
            {
                "offset" => $"字符 {value.GetProperty("start").GetInt32()}–{value.GetProperty("end").GetInt32()}",
                "para" => $"第 {value.GetProperty("index").GetInt32()} 段",
                "slide" => $"第 {value.GetProperty("index").GetInt32()} 页",
                "page" => $"第 {value.GetProperty("page").GetInt32()} 页",
                "sheet" => $"{value.GetProperty("sheet").GetString()}!{value.GetProperty("range").GetString()}",
                "region" => "图片区域",
                _ => string.Empty,
            };
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}


