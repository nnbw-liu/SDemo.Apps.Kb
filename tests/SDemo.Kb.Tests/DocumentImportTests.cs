using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Core;
using PdfPageSize = UglyToad.PdfPig.Content.PageSize;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using WText = DocumentFormat.OpenXml.Wordprocessing.Text;
using SText = DocumentFormat.OpenXml.Spreadsheet.Text;
using WRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using WTable = DocumentFormat.OpenXml.Wordprocessing.Table;

namespace SDemo.Kb.Tests;

public sealed class DocumentImportTests
{
    [Fact]
    public void docx按标题段落表格成块并带para锚点()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(
                    new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                    new WRun(new WText("部署方案"))),
                new Paragraph(new WRun(new WText("第一段正文。"))),
                new WTable(
                    new TableRow(
                        new TableCell(new Paragraph(new WRun(new WText("列 A")))),
                        new TableCell(new Paragraph(new WRun(new WText("列 B"))))))));
        }

        stream.Position = 0;
        var blocks = KbDocumentImporter.Extract("plan.docx", stream);

        Assert.Equal(3, blocks.Count);
        Assert.True(blocks[0].IsHeading);
        Assert.Equal(1, blocks[0].HeadingLevel);
        Assert.Equal("部署方案", blocks[0].Text);
        Assert.Contains("\"kind\":\"para\"", blocks[0].AnchorJson);
        Assert.Contains("列 A | 列 B", blocks[2].Text);
    }

    [Fact]
    public void pptx按slide成块并带slide锚点()
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation();

            var slidePart = presentationPart.AddNewPart<SlidePart>();
            slidePart.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(
                new P.Shape(
                    new P.NonVisualShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 2, Name = "Title" },
                        new P.NonVisualShapeDrawingProperties()),
                    new P.ShapeProperties(),
                    new P.TextBody(
                        new A.Paragraph(new A.Run(new A.Text("项目周会"))),
                        new A.Paragraph(new A.Run(new A.Text("部署改为云端双写"))))))));

            presentationPart.Presentation.AppendChild(new P.SlideIdList(
                new P.SlideId { Id = 256, RelationshipId = presentationPart.GetIdOfPart(slidePart) }));
        }

        stream.Position = 0;
        var blocks = KbDocumentImporter.Extract("meeting.pptx", stream);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("项目周会", blocks[0].Text);
        Assert.True(blocks[0].IsHeading);
        Assert.Contains("部署改为云端双写", blocks[1].Text);
        Assert.Contains("\"kind\":\"slide\"", blocks[0].AnchorJson);
    }

    [Fact]
    public void xlsx按sheet概述和每行摘要成块并带sheet范围()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(new SheetData(
                new Row(
                    new Cell { CellReference = "A1", DataType = CellValues.InlineString, InlineString = new InlineString(new SText("项目")) },
                    new Cell { CellReference = "B1", DataType = CellValues.InlineString, InlineString = new InlineString(new SText("预算")) }),
                new Row(
                    new Cell { CellReference = "A2", DataType = CellValues.InlineString, InlineString = new InlineString(new SText("Agent")) },
                    new Cell { CellReference = "B2", CellValue = new CellValue("1200") })));

            workbookPart.Workbook.AppendChild(new Sheets(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "预算表",
            }));
        }

        stream.Position = 0;
        var blocks = KbDocumentImporter.Extract("budget.xlsx", stream);

        Assert.True(blocks.Count >= 3);
        Assert.Contains("预算表", blocks[0].Text);
        Assert.Contains("第 2 行", blocks[2].Text);
        Assert.Contains("1200", blocks[2].Text);
        Assert.Contains("\"kind\":\"sheet\"", blocks[2].AnchorJson);
        Assert.Contains("A2:B2", blocks[2].AnchorJson);
    }

    [Fact]
    public void pdf文字层按页成块并带page锚点()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PdfPageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("Deploy plan is cloud dual-write.", 12, new PdfPoint(50, 700), font);
        var bytes = builder.Build();

        using var stream = new MemoryStream(bytes);
        var blocks = KbDocumentImporter.Extract("plan.pdf", stream);

        Assert.Single(blocks);
        Assert.Contains("Deploy plan", blocks[0].Text);
        Assert.Contains("\"kind\":\"page\"", blocks[0].AnchorJson);
        Assert.Contains("\"page\":1", blocks[0].AnchorJson);
    }

    [Fact]
    public void 通过ImportContent导入docx()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new WRun(new WText("导入的 Word 文档")))));
        }

        using var kb = new TestKb();
        var result = kb.Service.ImportContent("note.docx", stream.ToArray());

        Assert.Equal(1, result.Seq);
        Assert.Equal(1, result.BlockCount);
        var content = kb.Service.Get(new KbGetRequest { DocId = result.DocId })!;
        Assert.Equal("note", content.Doc.Title);
        Assert.Equal("导入的 Word 文档", content.Blocks[0].Text);
    }

    [Fact]
    public void 导入原件写入blobs并按doc聚合()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new WRun(new WText("带原件的导入")))));
        }

        using var kb = new TestKb();
        var bytes = stream.ToArray();
        var result = kb.Service.ImportContent("original.docx", bytes);

        var content = kb.Service.Get(new KbGetRequest { DocId = result.DocId })!;
        var artifact = Assert.Single(content.Artifacts);
        Assert.Equal("original.docx", artifact.Name);
        Assert.Equal(bytes.Length, artifact.Size);
        Assert.True(kb.Service.GetStorageStats().BlobBytes >= bytes.Length);

        var stored = Path.Combine(kb.DataDirectory, "blobs", artifact.Sha256[..2], artifact.Sha256);
        Assert.True(File.Exists(stored));
        Assert.Equal(stored, artifact.StoredPath);
    }

    [Fact]
    public void 图片导入建pending并保留原件()
    {
        // 1x1 PNG；KB 不解码图片，只负责 pending + 原件。
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nH0AAAAASUVORK5CYII=");

        using var kb = new TestKb();
        var result = kb.Service.ImportContent("scan.png", png);

        var content = kb.Service.Get(new KbGetRequest { DocId = result.DocId })!;
        Assert.Equal(DerivedState.Pending, content.Doc.DerivedState);
        Assert.Contains("待识别", content.Blocks[0].Text);
        var artifact = Assert.Single(content.Artifacts);
        Assert.Equal("image/png", artifact.Mime);
        Assert.True(File.Exists(artifact.StoredPath));

        var pending = Assert.Single(kb.Service.ListDerivations("sdemo.agent").Items);
        Assert.Equal("scan.png", pending.Artifacts[0].Name);
    }

    [Fact]
    public void 无文字层PDF建pending文档_回写后转ready并可检索()
    {
        var builder = new PdfDocumentBuilder();
        _ = builder.AddPage(PdfPageSize.A4);
        var bytes = builder.Build();

        using var kb = new TestKb();
        var result = kb.Service.ImportContent("scan.pdf", bytes);

        var pending = kb.Service.Get(new KbGetRequest { DocId = result.DocId })!;
        Assert.Equal(DerivedState.Pending, pending.Doc.DerivedState);
        var placeholder = Assert.Single(pending.Blocks);
        Assert.Contains("待识别", placeholder.Text);

        var pendingList = kb.Service.ListDerivations("sdemo.agent");
        var item = Assert.Single(pendingList.Items);
        Assert.Equal(result.DocId, item.DocId);
        Assert.Equal(placeholder.BlockId, item.PlaceholderBlockId);
        Assert.Single(item.Artifacts);
        Assert.True(File.Exists(item.Artifacts[0].StoredPath));

        // pending 文档不进检索面。
        Assert.True(kb.Service.Search(new KbSearchRequest { Query = "待识别" }).NoHit);

        // Agent OCR 回写：替换占位块后自动转 ready。
        kb.Service.Update(
            new KbUpdateRequest
            {
                DocId = result.DocId,
                ExpectedVersion = 1,
                Rationale = "OCR 完成",
                RationaleSource = RationaleSource.Ai,
                Ops =
                [
                    new BlockInput
                    {
                        Op = ChangeOp.Replace,
                        BlockId = placeholder.BlockId,
                        Text = "OCR 回来的正文：部署方案。",
                    },
                ],
            },
            "sdemo.kb");

        var ready = kb.Service.Get(new KbGetRequest { DocId = result.DocId })!;
        Assert.Equal(DerivedState.Ready, ready.Doc.DerivedState);
        Assert.Equal("OCR 回来的正文：部署方案。", ready.Blocks[0].Text);

        var hit = kb.Service.Search(new KbSearchRequest { Query = "OCR" });
        Assert.False(hit.NoHit);
        Assert.Contains(hit.Hits, item => item.DocId == result.DocId);
    }
}



