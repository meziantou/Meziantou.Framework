using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>A page of a printed document painted by <see cref="PrintSession.Paint"/>: the size of its page box (CSS px) and its display list.</summary>
internal sealed record PrintedPage(PhysicalSize Size, DisplayList DisplayList);

/// <summary>
/// The printing of a document into pages (https://drafts.csswg.org/css-page-3/, see <see cref="Document.BeginPrint"/>): while it
/// is active, the document has a style engine whose environment is the <c>print</c> media type and a layout engine that
/// paginates it. As in the browsers, the media queries see the page area of the default page of the options (the size of the
/// page without the default margins, https://github.com/w3c/csswg-drafts/issues/5437), and the viewport-relative lengths and the
/// initial containing block are the page area of the first page. The styles of the print engine neither start transitions nor
/// update the animations (their current values apply): the screen engines are kept as they are and used again when the
/// session is disposed.
/// </summary>
internal sealed class PrintSession : IDisposable
{
    private readonly Document _document;
    private readonly StyleEngine? _screenStyleEngine;
    private readonly LayoutEngine? _screenLayoutEngine;
    private MediaEnvironment? _environment;
    private LayoutEngine? _layoutEngine;
    private PhysicalSize _viewportSize;
    private PrintHeaderFooter? _headerFooter;
    private bool _disposed;

    internal PrintSession(Document document, PrintOptions options, StyleEngine? screenStyleEngine, LayoutEngine? screenLayoutEngine)
    {
        _document = document;
        Options = options;
        _screenStyleEngine = screenStyleEngine;
        _screenLayoutEngine = screenLayoutEngine;
    }

    /// <summary>The options of the printing.</summary>
    public PrintOptions Options { get; }

    /// <summary>The size of the default page box (the page size of the options).</summary>
    private PhysicalSize PageSize => new(Options.EffectivePageWidth, Options.EffectivePageHeight);

    /// <summary>The margins of the pages when no @page rule sets them (top, right, bottom, left).</summary>
    internal double[] DefaultMargins => [Options.MarginTop, Options.MarginRight, Options.MarginBottom, Options.MarginLeft];

    internal void Start()
    {
        var screen = _document.GetScreenEnvironment();
        _environment = screen with
        {
            MediaType = "print",
            Width = Math.Max(0, Options.EffectivePageWidth - Options.MarginLeft - Options.MarginRight) / Options.Scale,
            Height = Math.Max(0, Options.EffectivePageHeight - Options.MarginTop - Options.MarginBottom) / Options.Scale,
        };

        // The page area of the first page (an unnamed page, the actual name of the first page is known once the document is laid out)
        var probe = new StyleEngine(_document, _environment) { PrintViewportSize = new PhysicalSize(_environment.Width, _environment.Height), PrintPageSize = PageSize };
        _document.SetEngines(probe, layoutEngine: null);
        var style = probe.GetPageStyle(new PageSelection("", IsFirst: true, IsLeft: false, IsBlank: false), DefaultMargins, out var size, out var orientation, out var marginSafety);
        Install(PageGeometry.Resolve(style, size, orientation, marginSafety, Options, probe).LayoutAreaSize);
    }

    private void Install(PhysicalSize viewportSize)
    {
        _viewportSize = viewportSize;
        _layoutEngine = new LayoutEngine(_document, this);
        _document.SetEngines(new StyleEngine(_document, _environment) { PrintViewportSize = viewportSize, PrintPageSize = PageSize }, _layoutEngine);
    }

    /// <summary>
    /// Returns the pages of the document: the document is laid out when it is out of date. The initial containing block is the
    /// page area of the first page: when the first page is a named page whose page area differs from the one of an unnamed page,
    /// the document is styled and laid out again.
    /// </summary>
    public IReadOnlyList<LayoutPage> GetPages()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (var attempt = 0; ; attempt++)
        {
            var pages = _layoutEngine!.GetPages();
            if (pages.Count == 0 || attempt >= 2)
                return pages;

            var area = pages[0].Geometry.LayoutAreaSize;
            if (Math.Abs(area.Width - _viewportSize.Width) < 0.001 && Math.Abs(area.Height - _viewportSize.Height) < 0.001)
                return pages;

            Install(area);
        }
    }

    /// <summary>
    /// Paints the pages of the document (see <see cref="GetPages"/>). A page whose <c>page-orientation</c> is
    /// <c>rotate-left</c> or <c>rotate-right</c> is rotated a quarter turn counter-clockwise or clockwise for its presentation
    /// (https://drafts.csswg.org/css-page-3/#page-orientation-prop): its printed size is the size of its page box transposed.
    /// Without <paramref name="paper"/>, the pages are painted over a transparent sheet instead of white paper.
    /// </summary>
    public IReadOnlyList<PrintedPage> Paint(bool paper = true)
    {
        var pages = GetPages();
        var ranges = ParsePageRanges(Options.PageRanges);
        var result = new PrintedPage[pages.Count];
        for (var i = 0; i < pages.Count; i++)
        {
            var geometry = pages[i].Geometry;
            var size = geometry.PageSize;
            var displayList = _layoutEngine!.PaintPage(pages[i], paper);

            // The header and the footer in the page margins (PrintOptions.DisplayHeaderFooter)
            if (Options.DisplayHeaderFooter && (Options.HeaderTemplate is { Length: > 0 } || Options.FooterTemplate is { Length: > 0 }))
            {
                _headerFooter ??= new PrintHeaderFooter(_document);
                displayList = _headerFooter.Paint(displayList, size, geometry.Margin, i + 1, pages.Count, Options);
            }
            if (geometry.Orientation is "rotate-left" or "rotate-right")
            {
                var builder = new DisplayListBuilder();
                builder.Save();
                builder.Transform(geometry.Orientation == "rotate-right" ? new DisplayMatrix(0, 1, -1, 0, size.Height, 0) : new DisplayMatrix(0, -1, 1, 0, 0, size.Width));
                builder.AddList(displayList);
                builder.Restore();
                size = new PhysicalSize(size.Height, size.Width);
                displayList = builder.ToDisplayList(size);
            }

            if (!Options.PreferCssPageSize)
            {
                (size, displayList) = FitOnPaper(size, displayList, paper);
            }

            result[i] = new PrintedPage(size, displayList);
        }

        if (ranges is null)
            return result;

        // The pages of the ranges, once each, in order
        var selected = new List<PrintedPage>(result.Length);
        for (var i = 0; i < result.Length; i++)
        {
            var number = i + 1;
            if (ranges.Exists(range => number >= range.First && number <= range.Last))
            {
                selected.Add(result[i]);
            }
        }

        if (selected.Count == 0)
            throw new InvalidOperationException("The page ranges select no page of the document.");

        return selected;
    }

    /// <summary>
    /// Prints a page on the paper of the options when the size of the page box is not the size of the paper
    /// (<see cref="PrintOptions.PreferCssPageSize"/> is false), as Chromium: the paper is in the orientation of the page box,
    /// and the page box is centered on it, scaled down when it does not fit.
    /// </summary>
    private (PhysicalSize Size, DisplayList DisplayList) FitOnPaper(PhysicalSize size, DisplayList displayList, bool paper)
    {
        var paperWidth = Options.EffectivePageWidth;
        var paperHeight = Options.EffectivePageHeight;
        if ((size.Width > size.Height) != (paperWidth > paperHeight) && size.Width != size.Height)
        {
            (paperWidth, paperHeight) = (paperHeight, paperWidth);
        }

        if (Math.Abs(size.Width - paperWidth) < 0.01 && Math.Abs(size.Height - paperHeight) < 0.01)
            return (size, displayList);

        var scale = Math.Min(1, Math.Min(paperWidth / size.Width, paperHeight / size.Height));
        var builder = new DisplayListBuilder();

        // (the paper is white, as the page sheet)
        if (paper)
        {
            builder.Add(new DrawPaintItem(new DisplayPaint(DisplayColor.White)));
        }

        builder.Save();
        builder.Transform(DisplayMatrix.Translation((paperWidth - (size.Width * scale)) / 2, (paperHeight - (size.Height * scale)) / 2).PreConcat(DisplayMatrix.Scale(scale, scale)));
        builder.AddList(displayList);
        builder.Restore();
        var paperSize = new PhysicalSize(paperWidth, paperHeight);
        return (paperSize, builder.ToDisplayList(paperSize));
    }

    /// <summary>
    /// Parses the page ranges of the options (<see cref="PrintOptions.PageRanges"/>, the syntax of Chromium): the 1-based numbers
    /// of pages and the ranges separated by commas (<c>2</c>, <c>1-5</c>, <c>-3</c>, <c>11-</c>). Returns null when all the
    /// pages are printed.
    /// </summary>
    /// <exception cref="ArgumentException">The syntax is invalid, a page number is 0, or a range starts after its end.</exception>
    internal static List<(int First, int Last)>? ParsePageRanges(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var ranges = new List<(int First, int Last)>();
        foreach (var part in value.Split(','))
        {
            var range = part.Trim();
            if (range.Length == 0)
                continue;

            var dash = range.IndexOf('-', StringComparison.Ordinal);
            var first = dash < 0 ? ParsePageNumber(range) : dash == 0 ? 1 : ParsePageNumber(range[..dash].Trim());
            var last = dash < 0 ? first : dash == range.Length - 1 ? int.MaxValue : ParsePageNumber(range[(dash + 1)..].Trim());
            if (first is null || last is null)
                throw new ArgumentException($"The page ranges '{value}' are invalid.", nameof(value));

            if (first > last)
                throw new ArgumentException($"The page range '{range}' starts after its end.", nameof(value));

            ranges.Add((first.Value, last.Value));
        }

        return ranges.Count == 0 ? null : ranges;

        static int? ParsePageNumber(string text)
            => text.Length > 0 && text.All(char.IsAsciiDigit) && int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number >= 1 ? number : null;
    }

    /// <summary>Fetches the images and the fonts used by the styles of the printing (e.g. the rules of <c>@media print</c>, the page contexts and the page-margin boxes).</summary>
    public void FetchResources()
    {
        _document.UpdatePrintResources();

        // (the images of the page boxes and of the page-margin boxes: the styles of the pages are known once they are laid out)
        var styles = new List<ComputedStyle>();
        foreach (var page in GetPages())
        {
            if (page.Geometry.Style is { } pageStyle)
            {
                styles.Add(pageStyle);
            }

            foreach (var marginBox in page.MarginBoxes ?? [])
            {
                styles.Add(marginBox.Box.Style);
            }
        }

        _document.UpdatePrintPageResources(styles);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _headerFooter?.Dispose();
        _document.EndPrint(this, _screenStyleEngine, _screenLayoutEngine);
    }
}
