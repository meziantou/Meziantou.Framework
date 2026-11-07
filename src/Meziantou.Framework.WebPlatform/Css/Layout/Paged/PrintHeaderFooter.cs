using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The header and the footer of the printed pages (<see cref="PrintOptions.DisplayHeaderFooter"/>), as the templates of the PDF
/// of Chromium: each template is a document of its own (without scripts nor loader: only its inline resources are loaded),
/// printed on a page as wide as the printed page and as high as its top (or bottom) margin. As in Chromium, the template is
/// placed 20px from the top (or the bottom) edge of the page, its root font size is 0 (the text must set its size), its
/// backgrounds are not printed (unless <c>print-color-adjust: exact</c>), and the content of the elements of class
/// <c>date</c>, <c>title</c>, <c>url</c>, <c>pageNumber</c> and <c>totalPages</c> is replaced by the values of the page.
/// </summary>
internal sealed class PrintHeaderFooter : IDisposable
{
    /// <summary>The distance between the template and the edge of the page, as in Chromium.</summary>
    private const double EdgeInset = 20;

    private readonly Document _document;
    private BrowsingContext? _header;
    private BrowsingContext? _footer;

    public PrintHeaderFooter(Document document)
    {
        _document = document;
    }

    /// <summary>Paints the header and the footer of a page over its display list.</summary>
    /// <param name="page">The display list of the page.</param>
    /// <param name="size">The size of the page.</param>
    /// <param name="margin">The page margins (the header is in the top margin, the footer in the bottom margin).</param>
    /// <param name="pageNumber">The number of the page.</param>
    /// <param name="totalPages">The number of pages of the document.</param>
    public DisplayList Paint(DisplayList page, PhysicalSize size, Sides<double> margin, int pageNumber, int totalPages, PrintOptions options)
    {
        var builder = new DisplayListBuilder();
        builder.AddList(page);
        if (options.HeaderTemplate is { Length: > 0 } header && margin.Top > 0)
        {
            _header ??= CreateContext(header, isHeader: true);
            AddTemplate(builder, _header, size.Width, margin.Top, 0, pageNumber, totalPages);
        }

        if (options.FooterTemplate is { Length: > 0 } footer && margin.Bottom > 0)
        {
            _footer ??= CreateContext(footer, isHeader: false);
            AddTemplate(builder, _footer, size.Width, margin.Bottom, size.Height - margin.Bottom, pageNumber, totalPages);
        }

        return builder.ToDisplayList(size);
    }

    private BrowsingContext CreateContext(string template, bool isHeader)
    {
        var parentOptions = _document.BrowsingContext?.Options;
        var context = new BrowsingContext(new BrowsingContextOptions
        {
            RenderingBackend = parentOptions?.RenderingBackend,
            TimeProvider = _document.EventLoop?.TimeProvider ?? parentOptions?.TimeProvider,
            Viewport = new ViewportOptions { ScrollbarThickness = 0, CaretVisible = false, DevicePixelRatio = parentOptions?.Viewport?.DevicePixelRatio ?? 1 },
        });

        var position = isHeader ? "top" : "bottom";
        context.Navigate(_document.Url, _ => $$"""
            <!DOCTYPE html>
            <style>
              html { font-size: 0 }
              body { margin: 0 }
              #template { position: absolute; left: 0; right: 0; {{position}}: {{EdgeInset.ToString(CultureInfo.InvariantCulture)}}px }
            </style>
            <div id="template">{{template}}</div>
            """);
        context.EventLoop.RunUntilIdle();
        return context;
    }

    private void AddTemplate(DisplayListBuilder builder, BrowsingContext context, double width, double height, double top, int pageNumber, int totalPages)
    {
        var document = context.Document;
        var date = (_document.EventLoop?.TimeProvider ?? TimeProvider.System).GetLocalNow().ToString("M/d/yyyy", CultureInfo.InvariantCulture);
        SetText(document, "date", date);
        SetText(document, "title", _document.Title);
        SetText(document, "url", _document.Url);
        SetText(document, "pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture));
        SetText(document, "totalPages", totalPages.ToString(CultureInfo.InvariantCulture));

        // The fonts and the images of the template (data: URLs) are loaded before it is painted
        context.EventLoop.RunUntilIdle();
        using var session = document.BeginPrint(new PrintOptions { PageWidth = width, PageHeight = height, MarginTop = 0, MarginRight = 0, MarginBottom = 0, MarginLeft = 0, PrintBackground = false });
        session.FetchResources();
        context.EventLoop.RunUntilIdle();
        if (session.Paint(paper: false) is not [var first, ..])
            return;

        builder.Save();
        builder.Add(new ClipRectItem(new PhysicalRect(0, top, width, height)));
        builder.Transform(DisplayMatrix.Translation(0, top));
        builder.AddList(first.DisplayList);
        builder.Restore();

        static void SetText(Document document, string className, string text)
        {
            foreach (var element in document.QuerySelectorAll("." + className))
            {
                element.TextContent = text;
            }
        }
    }

    public void Dispose()
    {
        _header?.Dispose();
        _footer?.Dispose();
    }
}
