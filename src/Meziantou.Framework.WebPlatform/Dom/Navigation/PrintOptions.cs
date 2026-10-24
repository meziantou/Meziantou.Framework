namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The settings of the printing of a document into pages (https://drafts.csswg.org/css-page-3/): the size of the pages and
/// their margins when the style sheets do not set them (<c>@page { size; margin }</c>). The document is laid out with the
/// <c>print</c> media type.
/// </summary>
public sealed record PrintOptions
{
    /// <summary>Gets the default options: US Letter pages (8.5in x 11in) with margins of 0.5in.</summary>
    public static PrintOptions Default { get; } = new();

    /// <summary>Gets or sets the width of the pages (the page box of <c>size: auto</c>), in CSS pixels. The default is 816 (8.5in).</summary>
    public double PageWidth { get; init; } = 816;

    /// <summary>Gets or sets the height of the pages (the page box of <c>size: auto</c>), in CSS pixels. The default is 1056 (11in).</summary>
    public double PageHeight { get; init; } = 1056;

    /// <summary>Gets or sets the top margin of the pages when no <c>@page</c> rule sets it, in CSS pixels. The default is 48 (0.5in).</summary>
    public double MarginTop { get; init; } = 48;

    /// <summary>Gets or sets the right margin of the pages when no <c>@page</c> rule sets it, in CSS pixels. The default is 48 (0.5in).</summary>
    public double MarginRight { get; init; } = 48;

    /// <summary>Gets or sets the bottom margin of the pages when no <c>@page</c> rule sets it, in CSS pixels. The default is 48 (0.5in).</summary>
    public double MarginBottom { get; init; } = 48;

    /// <summary>Gets or sets the left margin of the pages when no <c>@page</c> rule sets it, in CSS pixels. The default is 48 (0.5in).</summary>
    public double MarginLeft { get; init; } = 48;

    /// <summary>
    /// Gets or sets the safe printable inset of the pages, in CSS pixels: the width of the region along each edge of the page
    /// sheet that the printer cannot print, used by the <c>page-margin-safety</c> descriptor of <c>@page</c> and of the margin
    /// rules (https://drafts.csswg.org/css-page-3/#page-margin-safety). The default is 0 (the whole sheet is printable).
    /// </summary>
    public double SafePrintableInset { get; init; }
}
