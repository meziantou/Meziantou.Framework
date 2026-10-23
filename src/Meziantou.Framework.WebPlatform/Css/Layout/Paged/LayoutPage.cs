using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A page of a paginated document (https://drafts.csswg.org/css-page-3/#page-model): its page box (the sheet, sized by
/// <c>size</c>), the margins, borders and paddings of the page context, and the page area, whose fragment contains the content
/// of the document placed on the page (see <see cref="LayoutEngine.GetPages"/>).
/// </summary>
internal sealed class LayoutPage
{
    /// <summary>The index of the page in the document (0 for the first page).</summary>
    public required int Index { get; init; }

    /// <summary>The page name (the used value of the <c>page</c> property of the content that starts the page), or the empty string.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the page is a blank page inserted by a forced break to a left or right page (<c>:blank</c>).</summary>
    public required bool IsBlank { get; init; }

    /// <summary>Whether the page is a left page (<c>:left</c>), else a right page.</summary>
    public required bool IsLeft { get; init; }

    /// <summary>The geometry of the page box.</summary>
    public required PageGeometry Geometry { get; init; }

    /// <summary>
    /// The fragment of the page area (a fragment of the viewport box, the root of the fragment tree of the page): the fragments
    /// of the content of the page, the boxes fixed to the viewport (repeated on every page), relative to the page area.
    /// </summary>
    public required BoxFragment Content { get; init; }

    /// <summary>The fragments of the page-margin boxes (https://drafts.csswg.org/css-page-3/#margin-boxes), relative to the page box, or null.</summary>
    public IReadOnlyList<BoxFragment>? MarginBoxes { get; set; }
}

/// <summary>
/// The geometry of a page box (https://drafts.csswg.org/css-page-3/#page-box-page-rule): the size of the page sheet, the
/// margins, borders and paddings of the page context (their percentages refer to the width of the page box in the horizontal
/// axis and to its height in the vertical axis, as in the browsers), and the page area inside them.
/// </summary>
/// <param name="Style">The style of the page context, or null when the document has no root element.</param>
/// <param name="PageSize">The size of the page box.</param>
/// <param name="Margin">The page margins.</param>
/// <param name="Border">The borders of the page box.</param>
/// <param name="Padding">The paddings of the page box.</param>
/// <param name="Orientation">The value of <c>page-orientation</c> (<c>upright</c>, <c>rotate-left</c> or <c>rotate-right</c>).</param>
internal sealed record PageGeometry(ComputedStyle? Style, PhysicalSize PageSize, Sides<double> Margin, Sides<double> Border, Sides<double> Padding, string Orientation)
{
    /// <summary>The size of a millimeter in CSS px.</summary>
    private const double Millimeter = 96 / 25.4;

    /// <summary>The border box of the page box, in the page box (inside the page margins).</summary>
    public PhysicalRect BorderBox => new(Margin.Left, Margin.Top, Math.Max(0, PageSize.Width - Margin.Left - Margin.Right), Math.Max(0, PageSize.Height - Margin.Top - Margin.Bottom));

    /// <summary>The padding box of the page box, in the page box (inside its page margins and borders).</summary>
    public PhysicalRect PaddingBox
    {
        get
        {
            var border = BorderBox;
            return new PhysicalRect(border.X + Border.Left, border.Y + Border.Top, Math.Max(0, border.Width - Border.Left - Border.Right), Math.Max(0, border.Height - Border.Top - Border.Bottom));
        }
    }

    /// <summary>The page area (https://drafts.csswg.org/css-page-3/#page-area), in the page box: the content box of the page box.</summary>
    public PhysicalRect PageArea
    {
        get
        {
            var left = Margin.Left + Border.Left + Padding.Left;
            var top = Margin.Top + Border.Top + Padding.Top;
            var right = Margin.Right + Border.Right + Padding.Right;
            var bottom = Margin.Bottom + Border.Bottom + Padding.Bottom;

            // (as in the browsers, the page area is never empty: the content makes progress)
            return new PhysicalRect(left, top, Math.Max(1, PageSize.Width - left - right), Math.Max(1, PageSize.Height - top - bottom));
        }
    }

    /// <summary>
    /// Returns the geometry of a page from the style of its page context and its descriptors (see
    /// <see cref="StyleEngine.GetPageStyle"/>): https://drafts.csswg.org/css-page-3/#page-size-prop. The page sheet has the
    /// size of <c>size</c>; the <c>width</c> and <c>height</c> of the page context are the size of the page area, which is
    /// placed in the page sheet as a block box in its containing block (https://drafts.csswg.org/css2/#blockwidth): its
    /// automatic margins share what remains (they may be negative, https://github.com/w3c/csswg-drafts/issues/8508), and when
    /// no margin is automatic (over-constrained), the page sheet is resized to contain the page box with its margins, as in
    /// Blink. The page margins that are not automatic stay within the printable area of the sheet with
    /// <c>page-margin-safety</c> (https://drafts.csswg.org/css-page-3/#page-margin-safety).
    /// </summary>
    public static PageGeometry Resolve(ComputedStyle? style, string size, string orientation, string marginSafety, PrintOptions options, StyleEngine? styleEngine)
    {
        var pageSize = GetPageSize(style, size, options, styleEngine);
        if (style is null)
            return new PageGeometry(null, pageSize, new Sides<double>(options.MarginTop, options.MarginRight, options.MarginBottom, options.MarginLeft), default, default, "upright");

        var layout = style.Layout;
        var box = layout.Box;
        var padding = new Sides<double>(box.Padding.Top.Resolve(pageSize.Height), box.Padding.Right.Resolve(pageSize.Width), box.Padding.Bottom.Resolve(pageSize.Height), box.Padding.Left.Resolve(pageSize.Width));
        var border = box.BorderWidth;
        var borderBox = layout.BoxSizing == BoxSizing.BorderBox;
        var inset = options.SafePrintableInset;
        var (width, left, right) = ResolveAxis(pageSize.Width, box.Margin.Left, box.Margin.Right, box.Width, box.MinWidth, box.MaxWidth, border.Left + border.Right + padding.Left + padding.Right, borderBox, marginSafety, GetSafePrintableInset(inset, pageSize.Width));
        var (height, top, bottom) = ResolveAxis(pageSize.Height, box.Margin.Top, box.Margin.Bottom, box.Height, box.MinHeight, box.MaxHeight, border.Top + border.Bottom + padding.Top + padding.Bottom, borderBox, marginSafety, GetSafePrintableInset(inset, pageSize.Height));
        return new PageGeometry(style, new PhysicalSize(width, height), new Sides<double>(top, right, bottom, left), border, padding, orientation is "rotate-left" or "rotate-right" ? orientation : "upright");

        // The size of the page sheet and the page margins in an axis
        static (double Sheet, double Start, double End) ResolveAxis(double sheet, LengthPercentageAuto marginStart, LengthPercentageAuto marginEnd, Size size, Size minSize, Size maxSize, double edges, bool borderBox, string marginSafety, double inset)
        {
            var start = ApplyMarginSafety(marginStart.Resolve(sheet), inset, marginSafety);
            var end = ApplyMarginSafety(marginEnd.Resolve(sheet), inset, marginSafety);
            if (size.TryResolve(sheet) is not { } specified)
                return (sheet, start, end);

            var content = Math.Max(0, specified - (borderBox ? edges : 0));
            if (maxSize.TryResolve(sheet) is { } max)
            {
                content = Math.Min(content, Math.Max(0, max - (borderBox ? edges : 0)));
            }

            if (minSize.TryResolveMinimum(sheet) is { } min)
            {
                content = Math.Max(content, min - (borderBox ? edges : 0));
            }

            var remaining = sheet - content - edges;
            if (marginStart.IsAuto && marginEnd.IsAuto)
                return (sheet, remaining / 2, remaining / 2);

            if (marginStart.IsAuto)
                return (sheet, remaining - end, end);

            if (marginEnd.IsAuto)
                return (sheet, start, remaining - start);

            // (over-constrained: the page sheet is resized)
            return (Math.Max(0, start + content + edges + end), start, end);
        }
    }

    /// <summary>
    /// Returns the safe printable inset of the edges of the sheet in an axis: an inset whose two edges cover the sheet leaves no
    /// printable area, it is ignored (the content makes progress).
    /// </summary>
    internal static double GetSafePrintableInset(double inset, double sheetSize) => inset > 0 && 2 * inset < sheetSize ? inset : 0;

    /// <summary>
    /// Returns the used value of a margin adjacent to an edge of the page sheet (https://drafts.csswg.org/css-page-3/#page-margin-safety):
    /// with <c>clamp</c>, at least the safe printable inset; with <c>add</c>, the margin plus the inset.
    /// </summary>
    internal static double ApplyMarginSafety(double margin, double inset, string marginSafety)
    {
        if (inset == 0)
            return margin;

        if (marginSafety.Equals("clamp", StringComparison.OrdinalIgnoreCase))
            return Math.Max(margin, inset);

        if (marginSafety.Equals("add", StringComparison.OrdinalIgnoreCase))
            return margin + inset;

        return margin;
    }

    /// <summary>
    /// Returns the size of the page box: the lengths of <c>size</c> (one length is a square), or a page size keyword and an
    /// orientation (https://drafts.csswg.org/css-page-3/#page-size-prop); <c>auto</c> is the size of the options.
    /// </summary>
    private static PhysicalSize GetPageSize(ComputedStyle? style, string size, PrintOptions options, StyleEngine? styleEngine)
    {
        var width = options.PageWidth;
        var height = options.PageHeight;
        string? pageOrientation = null;
        foreach (var keyword in size.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (keyword.ToLowerInvariant())
            {
                case "auto":
                    break;
                case "portrait" or "landscape":
                    pageOrientation = keyword.ToLowerInvariant();
                    break;
                case "a5":
                    (width, height) = (148 * Millimeter, 210 * Millimeter);
                    break;
                case "a4":
                    (width, height) = (210 * Millimeter, 297 * Millimeter);
                    break;
                case "a3":
                    (width, height) = (297 * Millimeter, 420 * Millimeter);
                    break;
                case "b5":
                    (width, height) = (176 * Millimeter, 250 * Millimeter);
                    break;
                case "b4":
                    (width, height) = (250 * Millimeter, 353 * Millimeter);
                    break;
                case "jis-b5":
                    (width, height) = (182 * Millimeter, 257 * Millimeter);
                    break;
                case "jis-b4":
                    (width, height) = (257 * Millimeter, 364 * Millimeter);
                    break;
                case "letter":
                    (width, height) = (8.5 * 96, 11 * 96);
                    break;
                case "legal":
                    (width, height) = (8.5 * 96, 14 * 96);
                    break;
                case "ledger":
                    (width, height) = (11 * 96, 17 * 96);
                    break;
                default:
                    // The lengths, computed by the style of the page context (see StyleEngine.GetPageStyle)
                    // (as in the browsers, an empty page box is the page box of the options)
                    if (style is not null && styleEngine?.GetPageSizeLengths(style, size) is var (lengthWidth, lengthHeight) && lengthWidth > 0 && lengthHeight > 0)
                        return new PhysicalSize(lengthWidth, lengthHeight);

                    break;
            }
        }

        // The orientation of the page sheet: the short side is the width in portrait (the page size keywords are portrait)
        return pageOrientation switch
        {
            "landscape" => new PhysicalSize(Math.Max(width, height), Math.Min(width, height)),
            "portrait" => new PhysicalSize(Math.Min(width, height), Math.Max(width, height)),
            _ => new PhysicalSize(width, height),
        };
    }
}
