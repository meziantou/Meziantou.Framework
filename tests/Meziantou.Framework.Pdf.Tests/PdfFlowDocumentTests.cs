using System.Globalization;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfFlowDocumentTests
{
    private static readonly PdfFlowOptions SmallPages = Small();

    [Fact]
    public void Example_FlowDocument()
    {
        #region Pdf_FlowDocument
        var flow = new PdfFlowDocument(new PdfFlowOptions
        {
            Title = "Quarterly report",
            Language = "en-US",
            Footer = (page, count) => $"Page {page} of {count}",
        });
        flow.TableOfContents()
            .Heading(1, "Summary")
            .Paragraph(text => text
                .Text("Sales grew by ")
                .Text("12%", new PdfTextStyle { Font = PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold) })
                .Text(". See ")
                .Link("the website", new Uri("https://example.com")))
            .List(["North: +8%", "South: +15%"])
            .Heading(1, "Details")
            .Table(table => table
                .Columns(2, 1)
                .HeaderRow("Region", "Sales")
                .Row("North", "1,200")
                .Row("South", "1,500"));

        var document = flow.Build();
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        var text = string.Join('\n', read.Pages.Select(page => page.ExtractText()));
        Assert.Contains("Sales grew by 12%. See the website", text);
        Assert.Contains("Page 1 of 1", text);
        Assert.Equal(["Summary", "Details"], read.Outlines.Select(item => item.Title));
        Assert.Equal("Quarterly report", read.Information.Title);
        Assert.Equal("en-US", read.Language);
        var root = Assert.Single(read.StructureTree.Elements);
        Assert.Equal("Document", root.Type);
        Assert.Equal(["TOC", "H1", "P", "L", "H1", "Table"], root.Children.Select(element => element.Type));
        var link = Assert.IsType<PdfLinkAnnotation>(read.Pages[0].Annotations.Single(annotation => annotation is PdfLinkAnnotation { Uri: not null }));
        Assert.Equal(new Uri("https://example.com"), link.Uri);
        Assert.Equal("the website", link.Contents);
    }

    [Fact]
    public void TheTextFlowsToTheNextPagesInsideTheMargins()
    {
        var flow = new PdfFlowDocument(SmallPages);
        for (var i = 0; i < 40; i++)
        {
            flow.Paragraph(string.Create(CultureInfo.InvariantCulture, $"Paragraph {i}: the quick brown fox jumps over the lazy dog, again and again, until the line breaks."));
        }

        var read = PdfDocument.Open(flow.Build().ToByteArray());

        Assert.True(read.Pages.Count > 3);
        foreach (var page in read.Pages)
        {
            foreach (var line in page.ExtractTextLayout().Lines)
            {
                Assert.True(line.Bounds.Left >= 29.5 && line.Bounds.Right <= 270.5, $"{line.Text}: {line.Bounds}");
                Assert.True(line.Bounds.Bottom >= 25 && line.Bounds.Top <= 275, $"{line.Text}: {line.Bounds}");
            }
        }

        var all = string.Join(' ', read.Pages.Select(page => page.ExtractText().ReplaceLineEndings(" ")));
        for (var i = 0; i < 40; i++)
        {
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"Paragraph {i}:"), all);
        }
    }

    [Fact]
    public void AWordLongerThanTheLineIsBroken()
    {
        var flow = new PdfFlowDocument(SmallPages).Paragraph(new string('W', 100));

        var page = PdfDocument.Open(flow.Build().ToByteArray()).Pages[0];

        var lines = page.ExtractTextLayout().Lines;
        Assert.True(lines.Count > 2);
        Assert.All(lines, line => Assert.True(line.Bounds.Right <= 270.5, line.Bounds.ToString()));
        Assert.Equal(new string('W', 100), string.Concat(lines.Select(line => line.Text)));
    }

    [Fact]
    public void TheHeaderRowsAreRepeatedAndTheCellsSpanColumnsAndRows()
    {
        var flow = new PdfFlowDocument(SmallPages).Table(table =>
        {
            table.Columns(1, 1, 1).HeaderRow("Name", "Value", "Note");
            table.Row(row => row.Cell("Spans two rows", rowSpan: 2).Cell("Two columns", columnSpan: 2));
            table.Row("b", "c");
            for (var i = 0; i < 30; i++)
            {
                table.Row(string.Create(CultureInfo.InvariantCulture, $"Row {i}"), "x", "y");
            }
        });

        var read = PdfDocument.Open(flow.Build().ToByteArray());

        Assert.True(read.Pages.Count > 1);
        Assert.All(read.Pages, page => Assert.StartsWith("Name Value Note", page.ExtractText()));
        var table = Assert.Single(Assert.Single(read.StructureTree.Elements).Children);
        Assert.Equal("Table", table.Type);
        Assert.Equal(["THead", "TBody"], table.Children.Select(element => element.Type));
        var headerCells = table.Children[0].Children[0].Children;
        Assert.All(headerCells, cell => Assert.Equal(PdfName.Create("Column"), cell.GetAttribute("Table", "Scope")));
        var firstRow = table.Children[1].Children[0].Children;
        Assert.Equal(PdfInteger.Get(2), firstRow[0].GetAttribute("Table", "RowSpan"));
        Assert.Equal(PdfInteger.Get(2), firstRow[1].GetAttribute("Table", "ColSpan"));
        Assert.Null(firstRow[0].GetAttribute("Table", "ColSpan"));
    }

    [Fact]
    public void TablesAreChecked()
    {
        var flow = new PdfFlowDocument();

        Assert.Throws<ArgumentException>(() => flow.Table(table => { }));
        Assert.Throws<ArgumentException>(() => flow.Table(table => table.Columns(1).Row("a", "b")));
        Assert.Throws<InvalidOperationException>(() => flow.Table(table => table.Row("a").HeaderRow("b")));
        Assert.Throws<ArgumentOutOfRangeException>(() => flow.Heading(7, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => flow.Columns(0, 0, _ => { }));
    }

    [Fact]
    public void CellsThatOverlapAreRefused()
    {
        var flow = new PdfFlowDocument();

        Assert.Throws<ArgumentException>(() => flow.Table(table => table
            .Columns(1, 1)
            .Row(row => row.Cell("a").Cell("b", rowSpan: 2))
            .Row(row => row.Cell("c", columnSpan: 2))));
    }

    [Fact]
    public void TheHeaderRowsAreNotRepeatedAboveARowThatFillsThePage()
    {
        var flow = new PdfFlowDocument(SmallPages).Table(table => table
            .Columns(1)
            .HeaderRow("Name")
            .Row(row => row.Cell(cell => cell.Paragraph("Top").Spacer(200).Paragraph("Bottom"))));

        var read = PdfDocument.Open(flow.Build().ToByteArray());

        Assert.Equal(2, read.Pages.Count);
        Assert.Equal("Name", read.Pages[0].ExtractText().Trim());
        Assert.Equal("Top\nBottom", read.Pages[1].ExtractText().Trim());
    }

    [Fact]
    public void TheHeadingsOfTheCellsAreInTheOutline()
    {
        var flow = new PdfFlowDocument(SmallPages)
            .Paragraph("First page")
            .PageBreak()
            .Paragraph("Second page")
            .Spacer(100)
            .Table(table => table.Columns(1).Row(row => row.Cell(cell => cell.Heading(1, "In a cell").Paragraph("Text"))));

        var read = PdfDocument.Open(flow.Build().ToByteArray());

        var item = Assert.Single(read.Outlines);
        Assert.Equal("In a cell", item.Title);
        Assert.Same(read.Pages[1], item.Destination!.Page);
        Assert.InRange(item.Destination.Top!.Value, 130, 160);
    }

    [Fact]
    public void ColumnsThatWouldNotHoldALineStartOnTheNextPage()
    {
        var flow = new PdfFlowDocument(SmallPages)
            .Paragraph("Before")
            .Spacer(220)
            .Columns(2, 10, columns => columns.Paragraph("In a column"));

        var read = PdfDocument.Open(flow.Build().ToByteArray());

        Assert.Equal(2, read.Pages.Count);
        Assert.Equal("Before", read.Pages[0].ExtractText().Trim());
        Assert.Equal("In a column", read.Pages[1].ExtractText().Trim());
    }

    [Fact]
    public void TheLinksAreInTheReadingOrderOfTheirParagraph()
    {
        var flow = new PdfFlowDocument(SmallPages).Paragraph(text => text
            .Text("Before ")
            .Link("first", new Uri("https://example.com/1"))
            .Text(" between ")
            .Link("second", new Uri("https://example.com/2")));

        var read = PdfDocument.Open(flow.Build().ToByteArray());

        var paragraph = Assert.Single(Assert.Single(read.StructureTree.Elements).Children);
        var kids = Assert.IsType<PdfArray>(paragraph.Dictionary[PdfName.Create("K")]);
        var types = kids.Select(kid => (kid is PdfReference reference ? reference.Resolve() : kid) switch
        {
            PdfInteger => "MCID",
            PdfDictionary dictionary => dictionary.GetName(PdfName.Create("S"))?.Value ?? "MCR",
            _ => "?",
        });
        Assert.Equal(["MCID", "Link", "MCID", "Link"], types.Select(type => type == "MCR" ? "MCID" : type));
        Assert.Equal(["Link", "Link"], paragraph.Children.Select(element => element.Type));
    }

    [Fact]
    public void CellsHoldBlocks()
    {
        using var image = new Meziantou.Framework.Imaging.Image<Meziantou.Framework.Imaging.Rgba32>(20, 10);
        var flow = new PdfFlowDocument(SmallPages).Table(table => table
            .Columns(1, 1)
            .Row(row => row
                .Cell(cell => cell.Paragraph("Text").List(["One", "Two"], ordered: true))
                .Cell(cell => cell.Image(PdfImage.FromImage(image), "A red rectangle", width: 40).Table(nested => nested.Row("Nested")))));

        var read = PdfDocument.Open(flow.Build().ToByteArray());

        var text = read.Pages[0].ExtractText();
        Assert.Contains("1. One", text);
        Assert.Contains("2. Two", text);
        Assert.Contains("Nested", text);
        var cells = Assert.Single(read.StructureTree.Elements).Children[0].Children[0].Children[0].Children;
        Assert.Equal(["P", "L"], cells[0].Children.Select(element => element.Type));
        Assert.Equal(["Figure", "Table"], cells[1].Children.Select(element => element.Type));
        Assert.Equal("A red rectangle", cells[1].Children[0].AlternateDescription);
        Assert.IsType<PdfArray>(cells[1].Children[0].GetAttribute("Layout", "BBox"));
    }

    [Fact]
    public void TheTableOfContentsGivesThePagesOfTheHeadings()
    {
        var flow = new PdfFlowDocument(SmallPages).TableOfContents("Index");
        for (var chapter = 1; chapter <= 3; chapter++)
        {
            flow.PageBreak().Heading(1, string.Create(CultureInfo.InvariantCulture, $"Chapter {chapter}"));
            for (var i = 0; i < 10; i++)
            {
                flow.Paragraph("Some text of the chapter that takes a line or two of the page.");
            }

            flow.Heading(2, string.Create(CultureInfo.InvariantCulture, $"Section {chapter}.1")).Paragraph("End.");
        }

        var read = PdfDocument.Open(flow.Build().ToByteArray());

        var contents = read.Pages[0].ExtractText().Split('\n');
        var headings = read.Outlines.SelectMany(item => new[] { item }.Concat(item.Children)).ToList();
        Assert.HasCount(6, headings);
        // Each entry, then its page number at the right
        var expected = new List<string> { "Index" };
        foreach (var heading in headings)
        {
            var page = read.Pages.IndexOf(heading.Destination!.Page) + 1;
            expected.Add(heading.Title + " " + page.ToString(CultureInfo.InvariantCulture));
            Assert.Contains(heading.Title, read.Pages[page - 1].ExtractText());
        }

        Assert.Equal(expected, contents.Select(line => line.Trim()));

        var links = read.Pages[0].Annotations.OfType<PdfLinkAnnotation>().ToList();
        Assert.HasCount(6, links);
        Assert.Equal(headings.Select(heading => heading.Destination!.Page), links.Select(link => link.Destination!.Page));
        Assert.Equal(["P", "TOCI", "TOCI", "TOCI", "TOCI", "TOCI", "TOCI"], Assert.Single(read.StructureTree.Elements).Children[0].Children.Select(element => element.Type));
    }

    [Fact]
    public void ColumnsFillOneColumnThenTheNext()
    {
        var flow = new PdfFlowDocument(SmallPages)
            .Columns(2, 10, columns =>
            {
                for (var i = 0; i < 20; i++)
                {
                    columns.Paragraph(string.Create(CultureInfo.InvariantCulture, $"Item {i}"));
                }
            });
        flow.Paragraph("After the columns");

        var read = PdfDocument.Open(flow.Build().ToByteArray());

        var lines = read.Pages[0].ExtractTextLayout().Lines;
        Assert.Contains(lines, line => line.Text == "Item 0" && line.Bounds.Left < 100);
        Assert.Contains(lines, line => line.Text.StartsWith("Item 1", StringComparison.Ordinal) && line.Bounds.Left > 150);
        Assert.All(lines, line => Assert.True(line.Bounds.Right <= 270.5, line.Text));
        var after = read.Pages.SelectMany(page => page.ExtractTextLayout().Lines).Single(line => line.Text == "After the columns");
        Assert.True(after.Bounds.Left < 40);
    }

    [Fact]
    public void ARightToLeftParagraphIsAlignedToTheRight()
    {
        var font = PdfFont.Load(TestHarness.RepositoryPaths.Fonts / "noto-sans-hebrew" / "NotoSansHebrew-Regular.ttf");
        var flow = new PdfFlowDocument(Small(font)).Paragraph("שלום עולם");

        var page = PdfDocument.Open(flow.Build().ToByteArray()).Pages[0];

        var line = Assert.Single(page.ExtractTextLayout().Lines);
        Assert.True(line.Bounds.Right > 265 && line.Bounds.Left > 150, line.Bounds.ToString());
    }

    [Fact]
    public void AlignmentsAndJustification()
    {
        var text = "The quick brown fox jumps over the lazy dog and runs away into the forest.";
        var flow = new PdfFlowDocument(SmallPages)
            .Paragraph(text, new PdfParagraphStyle { Alignment = PdfTextAlignment.Justify })
            .Paragraph("Right", new PdfParagraphStyle { Alignment = PdfTextAlignment.Right })
            .Paragraph("Center", new PdfParagraphStyle { Alignment = PdfTextAlignment.Center });

        var lines = PdfDocument.Open(flow.Build().ToByteArray()).Pages[0].ExtractTextLayout().Lines;

        Assert.True(lines[0].Bounds.Right > 268, "The first line of a justified paragraph fills the width");
        Assert.True(lines.Single(line => line.Text == "Right").Bounds.Right > 268);
        var center = lines.Single(line => line.Text == "Center").Bounds;
        Assert.True(Math.Abs(((center.Left + center.Right) / 2) - 150) < 2, center.ToString());
    }

    [Fact]
    public void ADocumentWithEmbeddedFontsIsPdfUA()
    {
        var font = PdfFont.Load(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");
        using var image = new Meziantou.Framework.Imaging.Image<Meziantou.Framework.Imaging.Rgba32>(4, 4);
        var flow = new PdfFlowDocument(new PdfFlowOptions { Font = font, Title = "Office", Language = "en", Footer = (page, _) => page.ToString(CultureInfo.InvariantCulture) });
        flow.Heading(1, "AVATAR").Paragraph("Office").List(["Office"]).Image(PdfImage.FromImage(image), "Office").Rule().Table(table => table.HeaderRow("Office").Row("Office"));
        var document = flow.Build();
        document.PdfUAConformance = PdfUAConformance.PdfUA1;

        var read = PdfDocument.Open(document.ToByteArray());

        var report = PdfAccessibilityInspector.Inspect(read);
        Assert.True(report.HasNoIssues, string.Join("; ", report.Issues));
    }

    [Fact]
    public void AnUntaggedDocumentHasNoStructure()
    {
        var flow = new PdfFlowDocument(new PdfFlowOptions { Tagged = false, CreateOutline = false }).Heading(1, "Title").Paragraph("Text");

        var read = PdfDocument.Open(flow.Build().ToByteArray());

        Assert.False(read.StructureTree.Exists);
        Assert.Empty(read.Outlines);
        Assert.Equal("Title\nText", read.Pages[0].ExtractText().Trim());
    }

    [Fact]
    public void StructureAttributes()
    {
        var document = new PdfDocument();
        var element = document.StructureTree.AddElement("TH");

        element.SetAttribute("Table", "Scope", PdfName.Create("Row"));
        element.SetAttribute("Layout", "Placement", PdfName.Create("Block"));
        element.SetAttribute("Table", "ColSpan", PdfInteger.Get(3));

        Assert.Equal(PdfName.Create("Row"), element.GetAttribute("Table", "Scope"));
        Assert.Equal(PdfInteger.Get(3), element.GetAttribute("Table", "ColSpan"));
        Assert.Equal(PdfName.Create("Block"), element.GetAttribute("Layout", "Placement"));
        Assert.HasCount(2, Assert.IsType<PdfArray>(element.Dictionary[PdfName.Create("A")]));
        element.SetAttribute("Table", "Scope", value: null);
        Assert.Null(element.GetAttribute("Table", "Scope"));
        Assert.Null(element.GetAttribute("List", "ListNumbering"));
        element.SetAttribute("List", "ListNumbering", value: null);
        Assert.HasCount(2, Assert.IsType<PdfArray>(element.Dictionary[PdfName.Create("A")]));
    }

    private static PdfFlowOptions Small(PdfFont? font = null) => new()
    {
        PageSize = new PdfSize(300, 300),
        MarginLeft = 30,
        MarginRight = 30,
        MarginTop = 30,
        MarginBottom = 30,
        Font = font ?? PdfFont.GetStandardFont(PdfStandardFont.Helvetica),
    };
}
