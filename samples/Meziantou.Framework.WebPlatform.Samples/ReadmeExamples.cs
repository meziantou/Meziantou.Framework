using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;

namespace Meziantou.Framework.WebPlatform.Samples;

internal static class ReadmeExamples
{
    public static async Task AllowNetworkRequests()
    {
        #region WebPlatform_AllowNetworkRequests
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = DocumentLoader.Create(),  // HTTP GET requests with a shared HttpClient (or pass your own)
            CookieJar = new CookieJar(),               // optional: the cookies (RFC 6265bis, document.cookie, cookieStore)
        });

        context.Navigate("https://example.com/");
        await context.EventLoop.RunUntilIdleAsync(); // runs the tasks until the document and its resources are loaded

        var document = context.Document;
        #endregion

        Console.WriteLine($"Loaded {document.Url}: {document.Title}");
    }

    public static async Task FilterRequests()
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new SameHostLoader("example.com"),
        });

        context.Navigate("https://example.com/");
        await context.EventLoop.RunUntilIdleAsync();
        Console.WriteLine($"Loaded {context.Document.Url} without requesting the other hosts");
    }

    public static async Task TakeScreenshots()
    {
        #region WebPlatform_TakeScreenshots
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            Viewport = new ViewportOptions { Width = 1280, Height = 720, DevicePixelRatio = 2 },
            DocumentLoader = DocumentLoader.Create(),
            RenderingBackend = new ImagingRenderingBackend(),
        });

        context.Navigate("https://example.com/");
        await context.EventLoop.RunUntilIdleAsync();

        await File.WriteAllBytesAsync("page.png", context.TakeScreenshotAsPng());            // the viewport: 2560x1440 pixels
        using var title = context.Document.QuerySelector("h1")!.TakeScreenshot();           // an element, as an Image<Rgba32>

        // Print the document: the print media type, the @page rules, one image per page
        var pages = context.RenderPagesAsPng(new PrintOptions { PageWidth = 794, PageHeight = 1123 }); // A4 at 96 dpi
        #endregion

        Console.WriteLine($"{pages.Count} printed pages, title of {title.Width}x{title.Height} pixels");
    }

    public static void UseSystemFonts()
    {
        #region WebPlatform_SystemFonts
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            UseSystemFonts = true, // font-family and local() can name the fonts installed on the machine
            RenderingBackend = new ImagingRenderingBackend(),
        });

        context.Navigate("https://example.com/", _ => """<p style="font-family: 'Segoe UI', sans-serif">Hello</p>""");
        using var screenshot = context.TakeScreenshot(); // the pixels depend on the fonts of the machine
        #endregion

        Console.WriteLine($"Screenshot of {screenshot.Width}x{screenshot.Height} pixels");
    }

    public static void DefineCustomElement()
    {
        #region WebPlatform_DefineCustomElement
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => "<x-greeting name=Ada></x-greeting>");

        // The elements already in the document are upgraded when the element is defined
        context.Window.CustomElements.Define("x-greeting", new CustomElementConstructor(element => new GreetingElement(element))
        {
            ObservedAttributes = ["name"],
        });

        var greeting = (HtmlElement)document.QuerySelector("x-greeting")!;
        Console.WriteLine(greeting.TextContent); // Hello Ada

        greeting.SetAttribute("name", "Grace");
        Console.WriteLine(greeting.TextContent); // Hello Grace
        #endregion
    }
}

#region WebPlatform_SameHostLoader
internal sealed class SameHostLoader(string host) : DocumentLoader
{
    private static readonly DocumentLoader Http = DocumentLoader.Create();

    public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        if (new Uri(request.Url).Host != host)
            return ValueTask.FromResult<DocumentResource?>(null); // network error

        return Http.LoadAsync(request, cancellationToken);
    }
}
#endregion

#region WebPlatform_CustomElement
/// <summary>The <c>x-greeting</c> element: its text greets the person named by its <c>name</c> attribute.</summary>
internal sealed class GreetingElement(HtmlElement element) : CustomElement(element)
{
    // As in a JavaScript constructor, the element must not get attributes or children in the constructor
    protected override void ConnectedCallback() => Update();

    protected override void AttributeChangedCallback(string name, string? oldValue, string? newValue, string? @namespace) => Update();

    private void Update() => Element.TextContent = "Hello " + Element.GetAttribute("name");
}
#endregion
