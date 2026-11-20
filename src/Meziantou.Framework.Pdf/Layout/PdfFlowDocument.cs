namespace Meziantou.Framework.Pdf;

/// <summary>
/// A document laid out from blocks (headings, paragraphs, lists, images, tables): the text is broken into lines, the
/// blocks into pages and columns, and the document is tagged, so that it is accessible (PDF/UA) when its fonts are
/// embedded.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Pdf.Tests/PdfFlowDocumentTests.cs" ref:region="Pdf_FlowDocument" ref:dedent="true">
/// var flow = new PdfFlowDocument(new PdfFlowOptions
/// {
///     Title = "Quarterly report",
///     Language = "en-US",
///     Footer = (page, count) =&gt; $"Page {page} of {count}",
/// });
/// flow.TableOfContents()
///     .Heading(1, "Summary")
///     .Paragraph(text =&gt; text
///         .Text("Sales grew by ")
///         .Text("12%", new PdfTextStyle { Font = PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold) })
///         .Text(". See ")
///         .Link("the website", new Uri("https://example.com")))
///     .List(["North: +8%", "South: +15%"])
///     .Heading(1, "Details")
///     .Table(table =&gt; table
///         .Columns(2, 1)
///         .HeaderRow("Region", "Sales")
///         .Row("North", "1,200")
///         .Row("South", "1,500"));
///
/// var document = flow.Build();
/// </code>
/// </example>
public sealed class PdfFlowDocument : PdfFlowContainer
{
    /// <summary>Initializes a new instance of the <see cref="PdfFlowDocument"/> class.</summary>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    public PdfFlowDocument(PdfFlowOptions? options = null)
        : base(options ?? new PdfFlowOptions())
    {
    }

    /// <inheritdoc cref="PdfFlowContainer.Heading(int, string)"/>
    public new PdfFlowDocument Heading(int level, string text) => (PdfFlowDocument)base.Heading(level, text);

    /// <inheritdoc cref="PdfFlowContainer.Paragraph(string, PdfParagraphStyle?)"/>
    public new PdfFlowDocument Paragraph(string text, PdfParagraphStyle? style = null) => (PdfFlowDocument)base.Paragraph(text, style);

    /// <inheritdoc cref="PdfFlowContainer.Paragraph(Action{PdfInlineBuilder}, PdfParagraphStyle?)"/>
    public new PdfFlowDocument Paragraph(Action<PdfInlineBuilder> build, PdfParagraphStyle? style = null) => (PdfFlowDocument)base.Paragraph(build, style);

    /// <inheritdoc cref="PdfFlowContainer.List(IEnumerable{string}, bool)"/>
    public new PdfFlowDocument List(IEnumerable<string> items, bool ordered = false) => (PdfFlowDocument)base.List(items, ordered);

    /// <inheritdoc cref="PdfFlowContainer.List(IEnumerable{Action{PdfFlowContainer}}, bool)"/>
    public new PdfFlowDocument List(IEnumerable<Action<PdfFlowContainer>> items, bool ordered = false) => (PdfFlowDocument)base.List(items, ordered);

    /// <inheritdoc cref="PdfFlowContainer.Image(PdfImage, string, double?)"/>
    public new PdfFlowDocument Image(PdfImage image, string alternateDescription, double? width = null) => (PdfFlowDocument)base.Image(image, alternateDescription, width);

    /// <inheritdoc cref="PdfFlowContainer.Table(Action{PdfFlowTableBuilder})"/>
    public new PdfFlowDocument Table(Action<PdfFlowTableBuilder> build) => (PdfFlowDocument)base.Table(build);

    /// <inheritdoc cref="PdfFlowContainer.Rule(PdfColor?, double)"/>
    public new PdfFlowDocument Rule(PdfColor? color = null, double width = 0.5) => (PdfFlowDocument)base.Rule(color, width);

    /// <inheritdoc cref="PdfFlowContainer.Spacer(double)"/>
    public new PdfFlowDocument Spacer(double height) => (PdfFlowDocument)base.Spacer(height);

    /// <summary>Starts a new page.</summary>
    /// <returns>The document.</returns>
    public PdfFlowDocument PageBreak()
    {
        Blocks.Add(new PageBreakBlock());
        return this;
    }

    /// <summary>Adds blocks laid out in columns: they fill the first column, then the next one, then the columns of the next page. The blocks that follow start below the columns.</summary>
    /// <param name="count">The number of columns.</param>
    /// <param name="gap">The space between the columns, in points.</param>
    /// <param name="content">The function that adds the blocks.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not between 1 and 16, or <paramref name="gap"/> is negative.</exception>
    public PdfFlowDocument Columns(int count, double gap, Action<PdfFlowContainer> content)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 16);
        ArgumentOutOfRangeException.ThrowIfNegative(gap);
        ArgumentNullException.ThrowIfNull(content);
        var container = new PdfFlowContainer(Options);
        content(container);
        Blocks.Add(new ColumnsBlock(count, gap, container.Blocks));
        return this;
    }

    /// <summary>Adds a table of contents (<c>TOC</c>): a title, then an entry (<c>TOCI</c>) per heading of levels 1 to 3, with its page number and a link to it.</summary>
    /// <param name="title">The title of the table of contents.</param>
    /// <returns>The document.</returns>
    public PdfFlowDocument TableOfContents(string title = "Contents")
    {
        ArgumentNullException.ThrowIfNull(title);
        Blocks.Add(new TableOfContentsBlock(title));
        return this;
    }

    /// <summary>Lays out the blocks and creates the document.</summary>
    /// <returns>The document. It can be changed and saved like any other.</returns>
    /// <exception cref="ArgumentException">A character of a text has no glyph in its font.</exception>
    public PdfDocument Build() => FlowLayout.Build(this);
}
