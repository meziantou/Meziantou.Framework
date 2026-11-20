namespace Meziantou.Framework.Pdf;

/// <summary>Builds the runs of text of a paragraph of a flow document, each with its style.</summary>
public sealed class PdfInlineBuilder
{
    internal PdfInlineBuilder()
    {
    }

    internal List<FlowRun> Runs { get; } = [];

    /// <summary>Adds text.</summary>
    /// <param name="text">The text. A line feed breaks the line.</param>
    /// <param name="style">The style of the text, or <see langword="null"/> for the style of the paragraph.</param>
    /// <returns>The builder.</returns>
    public PdfInlineBuilder Text(string text, PdfTextStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        Runs.Add(new FlowRun(text, style, Link: null));
        return this;
    }

    /// <summary>Adds text that is a link to a URI: a link annotation over the text, tagged as a link.</summary>
    /// <param name="text">The text.</param>
    /// <param name="uri">The absolute URI.</param>
    /// <param name="style">The style of the text, or <see langword="null"/> for blue.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">The URI is not absolute.</exception>
    public PdfInlineBuilder Link(string text, Uri uri, PdfTextStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("The URI must be absolute", nameof(uri));

        Runs.Add(new FlowRun(text, style ?? new PdfTextStyle { Color = PdfColor.FromRgb(0, 0.2, 0.8) }, uri));
        return this;
    }

    /// <summary>Breaks the line.</summary>
    /// <returns>The builder.</returns>
    public PdfInlineBuilder LineBreak() => Text("\n");
}
