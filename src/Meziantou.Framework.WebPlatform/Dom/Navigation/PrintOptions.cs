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

    /// <summary>
    /// Gets or sets whether the pages are in landscape orientation: the width and the height of the pages of <c>size: auto</c>
    /// are swapped (as the <c>landscape</c> option of the PDF of Playwright). The default is false.
    /// </summary>
    public bool Landscape { get; init; }

    /// <summary>
    /// Gets or sets the scale of the content of the pages, from 0.1 to 2 (as the <c>scale</c> option of the PDF of Playwright):
    /// the content is laid out in the page area divided by the scale, then painted scaled (the page margins and the page-margin
    /// boxes are not scaled). The default is 1.
    /// </summary>
    public double Scale { get; init; } = 1;

    /// <summary>
    /// Gets or sets whether the backgrounds are printed. The default is true. When it is false (as the default
    /// <c>printBackground: false</c> of Playwright), as in the browsers, the boxes whose <c>print-color-adjust</c> is
    /// <c>economy</c> (the initial value) paint a white background instead of their background color and images, and their
    /// text, when it is almost white, is darkened (https://drafts.csswg.org/css-color-adjust-1/#print-color-adjust).
    /// </summary>
    public bool PrintBackground { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the size of the pages set by the <c>size</c> descriptor of the <c>@page</c> rules is the size of the
    /// printed pages (as the <c>preferCSSPageSize</c> option of the PDF of Playwright). The default is true. When it is false,
    /// the pages are printed on the paper of the options (<see cref="PageWidth"/> x <see cref="PageHeight"/>, in the
    /// orientation of the page box): a page box of another size is centered on it, scaled down when it does not fit.
    /// </summary>
    public bool PreferCssPageSize { get; init; } = true;

    /// <summary>
    /// Gets or sets the pages to print, as the <c>pageRanges</c> option of the PDF of Playwright: the 1-based numbers of the
    /// pages and the ranges of pages separated by commas (e.g. <c>1-5, 8, 11-13</c>; <c>-3</c> is the first three pages and
    /// <c>11-</c> the pages from the eleventh one). The pages are printed once, in order; the numbers beyond the last page are
    /// ignored, and the pages keep their numbers (<c>counter(page)</c>). The default is null: all the pages.
    /// </summary>
    public string? PageRanges { get; init; }

    /// <summary>
    /// Gets or sets whether the header and the footer templates (<see cref="HeaderTemplate"/>, <see cref="FooterTemplate"/>)
    /// are printed in the top and bottom margins of the pages (as the <c>displayHeaderFooter</c> option of the PDF of
    /// Playwright). The default is false.
    /// </summary>
    public bool DisplayHeaderFooter { get; init; }

    /// <summary>
    /// Gets or sets the HTML of the header of the pages (with <see cref="DisplayHeaderFooter"/>), as the <c>headerTemplate</c>
    /// option of the PDF of Playwright: it is rendered in the top margin of each page, as wide as the page; the content of its
    /// elements of class <c>date</c>, <c>title</c>, <c>url</c>, <c>pageNumber</c> and <c>totalPages</c> is the date of the
    /// printing, the title of the document, its URL, the number of the page and the number of pages. Its scripts do not run
    /// and only its inline resources (<c>data:</c> URLs) are loaded. The default is null: no header.
    /// </summary>
    public string? HeaderTemplate { get; init; }

    /// <summary>Gets or sets the HTML of the footer of the pages, rendered in their bottom margin (see <see cref="HeaderTemplate"/>). The default is null: no footer.</summary>
    public string? FooterTemplate { get; init; }

    /// <summary>The width of the pages of <c>size: auto</c>, in the orientation of the options.</summary>
    internal double EffectivePageWidth => Landscape ? PageHeight : PageWidth;

    /// <summary>The height of the pages of <c>size: auto</c>, in the orientation of the options.</summary>
    internal double EffectivePageHeight => Landscape ? PageWidth : PageHeight;
}
