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
    private PhysicalSize PageSize => new(Options.PageWidth, Options.PageHeight);

    /// <summary>The margins of the pages when no @page rule sets them (top, right, bottom, left).</summary>
    internal double[] DefaultMargins => [Options.MarginTop, Options.MarginRight, Options.MarginBottom, Options.MarginLeft];

    internal void Start()
    {
        var screen = _document.GetScreenEnvironment();
        _environment = screen with
        {
            MediaType = "print",
            Width = Math.Max(0, Options.PageWidth - Options.MarginLeft - Options.MarginRight),
            Height = Math.Max(0, Options.PageHeight - Options.MarginTop - Options.MarginBottom),
        };

        // The page area of the first page (an unnamed page, the actual name of the first page is known once the document is laid out)
        var probe = new StyleEngine(_document, _environment) { PrintViewportSize = new PhysicalSize(_environment.Width, _environment.Height), PrintPageSize = PageSize };
        _document.SetEngines(probe, layoutEngine: null);
        var style = probe.GetPageStyle(new PageSelection("", IsFirst: true, IsLeft: false, IsBlank: false), DefaultMargins, out var size, out var orientation, out var marginSafety);
        Install(PageGeometry.Resolve(style, size, orientation, marginSafety, Options, probe).PageArea.Size);
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

            var area = pages[0].Geometry.PageArea.Size;
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
        var result = new PrintedPage[pages.Count];
        for (var i = 0; i < pages.Count; i++)
        {
            var geometry = pages[i].Geometry;
            var size = geometry.PageSize;
            var displayList = _layoutEngine!.PaintPage(pages[i], paper);
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

            result[i] = new PrintedPage(size, displayList);
        }

        return result;
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
        _document.EndPrint(this, _screenStyleEngine, _screenLayoutEngine);
    }
}
