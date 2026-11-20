namespace Meziantou.Framework.Pdf;

/// <summary>
/// A sequence of blocks of a flow document: headings, paragraphs, lists, images, tables, rules and spaces, laid out from
/// top to bottom. <see cref="PdfFlowDocument"/> is the container of the pages; the cells of a table are containers too.
/// </summary>
public class PdfFlowContainer
{
    internal PdfFlowContainer(PdfFlowOptions options)
    {
        Options = options;
    }

    internal PdfFlowOptions Options { get; }

    internal List<FlowBlock> Blocks { get; } = [];

    /// <summary>Adds a heading (<c>H1</c> to <c>H6</c>), in the heading font, larger for the higher levels. It is kept on the page of the block that follows it, and added to the outline and the table of contents.</summary>
    /// <param name="level">The level, from 1 to 6.</param>
    /// <param name="text">The text.</param>
    /// <returns>The container.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not between 1 and 6.</exception>
    public PdfFlowContainer Heading(int level, string text)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 6);
        ArgumentNullException.ThrowIfNull(text);
        Blocks.Add(new ParagraphBlock([new FlowRun(text, null, null)], null, level));
        return this;
    }

    /// <summary>Adds a paragraph (<c>P</c>) of text in the style of the document.</summary>
    /// <param name="text">The text. A line feed breaks the line.</param>
    /// <param name="style">The style of the paragraph, or <see langword="null"/> for the default.</param>
    /// <returns>The container.</returns>
    public PdfFlowContainer Paragraph(string text, PdfParagraphStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        Blocks.Add(new ParagraphBlock([new FlowRun(text, null, null)], style, HeadingLevel: 0));
        return this;
    }

    /// <summary>Adds a paragraph (<c>P</c>) of runs of text, each with its style, and links.</summary>
    /// <param name="build">The function that adds the runs.</param>
    /// <param name="style">The style of the paragraph, or <see langword="null"/> for the default.</param>
    /// <returns>The container.</returns>
    public PdfFlowContainer Paragraph(Action<PdfInlineBuilder> build, PdfParagraphStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        var builder = new PdfInlineBuilder();
        build(builder);
        Blocks.Add(new ParagraphBlock([.. builder.Runs], style, HeadingLevel: 0));
        return this;
    }

    /// <summary>Adds a list (<c>L</c>) of items of text: each item (<c>LI</c>) has a label (<c>Lbl</c>), a bullet or its number, and a body (<c>LBody</c>).</summary>
    /// <param name="items">The texts of the items.</param>
    /// <param name="ordered">Whether the items are numbered (1., 2., 3.) instead of bulleted.</param>
    /// <returns>The container.</returns>
    public PdfFlowContainer List(IEnumerable<string> items, bool ordered = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        return List(items.Select(item => (Action<PdfFlowContainer>)(content => content.Paragraph(item, new PdfParagraphStyle { SpaceAfter = 2 }))), ordered);
    }

    /// <summary>Adds a list (<c>L</c>) whose items hold blocks, such as paragraphs and nested lists.</summary>
    /// <param name="items">The functions that add the blocks of each item.</param>
    /// <param name="ordered">Whether the items are numbered (1., 2., 3.) instead of bulleted.</param>
    /// <returns>The container.</returns>
    public PdfFlowContainer List(IEnumerable<Action<PdfFlowContainer>> items, bool ordered = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        var bodies = new List<List<FlowBlock>>();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(items));
            var container = new PdfFlowContainer(Options);
            item(container);
            bodies.Add(container.Blocks);
        }

        Blocks.Add(new ListBlock(bodies, ordered));
        return this;
    }

    /// <summary>Adds an image (<c>Figure</c>), with its alternate description, which screen readers read.</summary>
    /// <param name="image">The image.</param>
    /// <param name="alternateDescription">The description of the image.</param>
    /// <param name="width">The width of the image, in points, or <see langword="null"/> for the width of the container (smaller images keep their size of 1 point per pixel). The height keeps the ratio of the image, and the image is scaled down to fit a page.</param>
    /// <returns>The container.</returns>
    public PdfFlowContainer Image(PdfImage image, string alternateDescription, double? width = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(alternateDescription);
        if (width is { } value && !(value > 0 && double.IsFinite(value)))
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be positive");

        Blocks.Add(new ImageBlock(image, alternateDescription, width));
        return this;
    }

    /// <summary>Adds a table (<c>Table</c>): header rows (<c>THead</c>, <c>TH</c>), rows (<c>TBody</c>, <c>TR</c>, <c>TD</c>) and cells that hold blocks and span columns and rows.</summary>
    /// <param name="build">The function that adds the columns and the rows.</param>
    /// <returns>The container.</returns>
    /// <exception cref="ArgumentException">The table has no row, or its cells do not fit its columns.</exception>
    public PdfFlowContainer Table(Action<PdfFlowTableBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var builder = new PdfFlowTableBuilder(Options);
        build(builder);
        Blocks.Add(TableBlock.Create(builder));
        return this;
    }

    /// <summary>Adds a horizontal rule, an artifact.</summary>
    /// <param name="color">The color of the rule, or <see langword="null"/> for gray.</param>
    /// <param name="width">The width of the line, in points.</param>
    /// <returns>The container.</returns>
    public PdfFlowContainer Rule(PdfColor? color = null, double width = 0.5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        Blocks.Add(new RuleBlock(color ?? PdfColor.FromGray(0.6), width));
        return this;
    }

    /// <summary>Adds an empty space.</summary>
    /// <param name="height">The height of the space, in points.</param>
    /// <returns>The container.</returns>
    public PdfFlowContainer Spacer(double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Blocks.Add(new SpacerBlock(height));
        return this;
    }
}
