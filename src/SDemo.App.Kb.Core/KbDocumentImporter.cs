using System.Text;
using System.Text.Json;
using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Storage;
using WParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using WText = DocumentFormat.OpenXml.Wordprocessing.Text;
using WTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using WTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;
using WTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace SDemo.App.Kb.Core;

/// <summary>
/// 确定性格式解析：md / txt / docx / pptx / xlsx / 文字层 pdf。
/// 不做 OCR；扫描件请交给 Agent，KB 只接收 OCR 后的块（P4-d 尾）。
/// </summary>
public static partial class KbDocumentImporter
{
    private const int MaxSpreadsheetRows = 2000;

    public static IReadOnlyList<TextBlockSpec> Extract(string fileName, Stream stream)
    {
        var document = ExtractDocument(fileName, stream);
        if (document.RequiresDerivation)
        {
            throw KbException.Invalid("PDF 没有文字层（扫描件需要 Agent OCR，P4-d 尾再接）。");
        }

        return document.Blocks;
    }

    /// <summary>
    /// 与 <see cref="Extract"/> 的差别：没有文字层的 PDF 不抛，返回
    /// <see cref="ImportedDocument.RequiresDerivation"/>=true，由 KB 建 pending 文档等 Agent OCR。
    /// </summary>
    public static ImportedDocument ExtractDocument(string fileName, Stream stream)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            var blocks = extension switch
            {
                ".md" or ".markdown" => ReadText(stream) is { } markdown
                    ? KbBlockSplitter.SplitMarkdown(markdown)
                    : [],
                ".txt" => ReadText(stream) is { } text
                    ? KbBlockSplitter.SplitPlainText(text)
                    : [],
                ".docx" => ExtractDocx(stream),
                ".pptx" => ExtractPptx(stream),
                ".xlsx" => ExtractXlsx(stream),
                ".pdf" => ExtractPdf(stream),
                _ => throw KbException.Invalid($"不支持导入的格式：{extension}"),
            };

            var requiresDerivation = extension == ".pdf" && blocks.Count == 0;
            return new ImportedDocument(blocks, requiresDerivation);
        }
        catch (KbException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw KbException.Invalid($"解析 {extension} 失败：{ex.Message}");
        }
    }

    private static string ReadText(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    // ---------------------------------------------------------------- docx

    private static IReadOnlyList<TextBlockSpec> ExtractDocx(Stream stream)
    {
        var blocks = new List<TextBlockSpec>();

        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return blocks;
        }

        var paragraphIndex = 0;

        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case WParagraph paragraph:
                {
                    var text = string.Concat(paragraph.Descendants<WText>().Select(item => item.Text)).Trim();
                    paragraphIndex++;
                    if (text.Length == 0)
                    {
                        break;
                    }

                    var level = HeadingLevel(paragraph);
                    blocks.Add(new TextBlockSpec(
                        text,
                        level > 0,
                        level,
                        Anchor("para", new { index = paragraphIndex })));
                    break;
                }

                case WTable table:
                {
                    foreach (var row in table.Elements<WTableRow>())
                    {
                        var cells = row.Elements<WTableCell>()
                            .Select(cell => string.Concat(cell.Descendants<WText>().Select(item => item.Text)).Trim())
                            .Where(value => value.Length > 0)
                            .ToList();

                        if (cells.Count == 0)
                        {
                            continue;
                        }

                        blocks.Add(new TextBlockSpec(
                            string.Join(" | ", cells),
                            false,
                            0,
                            Anchor("para", new { index = paragraphIndex })));
                    }

                    break;
                }
            }
        }

        return blocks;
    }

    private static int HeadingLevel(WParagraph WParagraph)
    {
        var styleId = WParagraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(styleId))
        {
            var match = HeadingStyleRegex().Match(styleId);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var level))
            {
                return Math.Clamp(level, 1, 6);
            }
        }

        var outline = WParagraph.ParagraphProperties?.OutlineLevel?.Val?.Value;
        return outline is { } value ? Math.Clamp(value + 1, 1, 6) : 0;
    }

    // ---------------------------------------------------------------- pptx

    private static IReadOnlyList<TextBlockSpec> ExtractPptx(Stream stream)
    {
        var blocks = new List<TextBlockSpec>();

        using var document = PresentationDocument.Open(stream, isEditable: false);
        var presentationPart = document.PresentationPart;
        if (presentationPart is null)
        {
            return blocks;
        }

        var slideIndex = 0;
        foreach (var slidePart in presentationPart.SlideParts)
        {
            slideIndex++;
            var paragraphs = slidePart.Slide?.Descendants<DocumentFormat.OpenXml.Drawing.Paragraph>()
                .Select(WParagraph => string.Concat(WParagraph.Descendants<DocumentFormat.OpenXml.Drawing.Text>().Select(text => text.Text)).Trim())
                .Where(text => text.Length > 0)
                .ToList() ?? [];

            if (paragraphs.Count == 0)
            {
                continue;
            }

            blocks.Add(new TextBlockSpec(
                paragraphs[0],
                true,
                1,
                Anchor("slide", new { index = slideIndex })));

            if (paragraphs.Count > 1)
            {
                blocks.Add(new TextBlockSpec(
                    string.Join('\n', paragraphs.Skip(1)),
                    false,
                    0,
                    Anchor("slide", new { index = slideIndex })));
            }

            var notes = slidePart.NotesSlidePart?.NotesSlide?.Descendants<DocumentFormat.OpenXml.Drawing.Text>()
                .Select(text => text.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList() ?? [];

            if (notes.Count > 0)
            {
                blocks.Add(new TextBlockSpec(
                    string.Join('\n', notes).Trim(),
                    false,
                    0,
                    Anchor("slide", new { index = slideIndex })));
            }
        }

        return blocks;
    }

    // ---------------------------------------------------------------- xlsx

    private static IReadOnlyList<TextBlockSpec> ExtractXlsx(Stream stream)
    {
        var blocks = new List<TextBlockSpec>();

        using var document = SpreadsheetDocument.Open(stream, isEditable: false);
        var workbookPart = document.WorkbookPart;
        var workbook = workbookPart?.Workbook;
        if (workbookPart is null || workbook is null)
        {
            return blocks;
        }

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
            ?.Elements<SharedStringItem>()
            .Select(item => item.InnerText)
            .ToList() ?? [];

        foreach (var sheet in workbook.Sheets?.Elements<Sheet>() ?? [])
        {
            if (sheet.Id?.Value is not { } relationshipId
                || workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
            {
                continue;
            }

            var sheetName = sheet.Name?.Value ?? "Sheet";
            var worksheet = worksheetPart.Worksheet;
            var rows = worksheet.Descendants<Row>().Take(MaxSpreadsheetRows + 1).ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            var used = worksheet.SheetDimension?.Reference?.Value;
            var overviewRange = string.IsNullOrWhiteSpace(used) ? "A1:A1" : used;
            blocks.Add(new TextBlockSpec(
                $"表格「{sheetName}」：{rows.Count} 行。",
                false,
                0,
                Anchor("sheet", new { sheet = sheetName, range = overviewRange })));

            var rowOrdinal = 0;
            foreach (var row in rows.Take(MaxSpreadsheetRows))
            {
                rowOrdinal++;
                var values = row.Elements<Cell>()
                    .Select(cell => CellText(cell, sharedStrings))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();

                if (values.Count == 0)
                {
                    continue;
                }

                var rowIndex = (int)(row.RowIndex?.Value ?? (uint)rowOrdinal);
                var cells = row.Elements<Cell>().ToList();
                var first = ColumnName(cells.FirstOrDefault()?.CellReference?.Value, 1);
                var last = ColumnName(cells.LastOrDefault()?.CellReference?.Value, Math.Max(1, values.Count));

                blocks.Add(new TextBlockSpec(
                    $"第 {rowIndex} 行：{string.Join(" | ", values)}",
                    false,
                    0,
                    Anchor("sheet", new { sheet = sheetName, range = $"{first}{rowIndex}:{last}{rowIndex}" })));
            }

            if (rows.Count > MaxSpreadsheetRows)
            {
                blocks.Add(new TextBlockSpec(
                    $"（「{sheetName}」只导入了前 {MaxSpreadsheetRows} 行）",
                    false,
                    0,
                    Anchor("sheet", new { sheet = sheetName, range = overviewRange })));
            }
        }

        return blocks;
    }

    private static string CellText(Cell cell, IReadOnlyList<string> sharedStrings)
    {
        var raw = cell.CellValue?.Text ?? cell.InlineString?.InnerText;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(raw, out var index)
            && index >= 0
            && index < sharedStrings.Count)
        {
            return sharedStrings[index].Trim();
        }

        return raw.Trim();
    }

    private static string ColumnName(string? reference, int fallback)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return ColumnName(fallback);
        }

        var letters = new string(reference.TakeWhile(char.IsLetter).ToArray());
        return letters.Length == 0 ? ColumnName(fallback) : letters;
    }

    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + (column % 26)) + name;
            column /= 26;
        }

        return name;
    }

    // ---------------------------------------------------------------- pdf

    private static IReadOnlyList<TextBlockSpec> ExtractPdf(Stream stream)
    {
        var blocks = new List<TextBlockSpec>();

        using var document = PdfDocument.Open(stream);
        foreach (var page in document.GetPages())
        {
            var words = page.GetWords()
                .OrderByDescending(word => Math.Round(word.BoundingBox.Bottom, 1))
                .ThenBy(word => word.BoundingBox.Left)
                .ToList();

            if (words.Count == 0)
            {
                continue;
            }

            var lines = new List<string>();
            foreach (var group in words.GroupBy(word => Math.Round(word.BoundingBox.Bottom, 1)))
            {
                var line = JoinWords(group.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text));
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }

            var text = string.Join('\n', lines).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            blocks.Add(new TextBlockSpec(
                text,
                false,
                0,
                Anchor("page", new { page = page.Number })));
        }

        if (blocks.Count == 0)
        {
            return blocks;
        }

        return blocks;
    }

    private static string JoinWords(IEnumerable<string> words)
    {
        var builder = new StringBuilder();
        string? previous = null;

        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            if (previous is not null
                && !(KbTokenizer.IsCjk(previous[^1]) && KbTokenizer.IsCjk(word[0])))
            {
                builder.Append(' ');
            }

            builder.Append(word);
            previous = word;
        }

        return builder.ToString().Trim();
    }

    private static string Anchor(string kind, object value) => JsonSerializer.Serialize(new
    {
        kind,
        value,
    });

    [GeneratedRegex(@"^(?:Heading|标题)\s*(\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingStyleRegex();
}

public sealed record ImportedDocument(IReadOnlyList<TextBlockSpec> Blocks, bool RequiresDerivation);






