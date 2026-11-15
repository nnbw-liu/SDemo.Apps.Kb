namespace SDemo.App.Kb.Core;

/// <summary>导入格式的 MIME 映射；只覆盖 KB 接受的类型。</summary>
public static class KbMime
{
    public static string? ForFile(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".md" or ".markdown" => "text/markdown",
        ".txt" => "text/plain",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        _ => null,
    };
}
