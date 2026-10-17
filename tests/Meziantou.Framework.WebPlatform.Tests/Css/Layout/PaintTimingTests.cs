using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// Paint Timing (https://w3c.github.io/paint-timing/), Largest Contentful Paint (https://w3c.github.io/largest-contentful-paint/)
/// and Element Timing (https://w3c.github.io/element-timing/): the paints are found in the display list painted by the rendering
/// updates once the entries are observed.
/// </summary>
public sealed class PaintTimingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport (the images are PNG headers named WxH.png).</summary>
    private Document Parse(string html, WebPlatformFeatures? features = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
            DocumentLoader = new PngLoader(),
            Features = features,
        });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Runs the rendering steps of the next frames (the virtual clock advances by one frame each time).</summary>
    private void RunFrames(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(17));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    private static List<T> Observe<T>(Document document, string type, bool buffered = false)
        where T : PerformanceEntry
    {
        var entries = new List<T>();
        var observer = new PerformanceObserver(document.DefaultView!, (list, _, _) => entries.AddRange(list.GetEntries().Cast<T>()));
        observer.Observe(new PerformanceObserverInit { Type = type, Buffered = buffered });
        return entries;
    }

    private static HtmlElement Get(Document document, string id) => (HtmlElement)document.GetElementById(id)!;

    [Fact]
    public void Untracked_DocumentIsNotPaintedForThePaintTiming()
    {
        var document = Parse("<p>Text</p>");
        RunFrames(3);

        Assert.Null(document.LayoutEngineOrNull);
    }

    [Fact]
    public void FirstPaint_ThenFirstContentfulPaint()
    {
        var document = Parse("<div id=box style='width: 100px; height: 100px; background: blue'></div>");
        var entries = Observe<PerformancePaintTiming>(document, "paint");
        RunFrames(2);

        var firstPaint = Assert.Single(entries);
        Assert.Equal("first-paint", firstPaint.Name);
        Assert.Equal("paint", firstPaint.EntryType);
        Assert.Equal(0, firstPaint.Duration);
        Assert.Equal(firstPaint.PaintTime, firstPaint.StartTime);
        Assert.Null(firstPaint.PresentationTime);

        Get(document, "box").TextContent = "Text";
        RunFrames(2);

        Assert.Equal(["first-paint", "first-contentful-paint"], entries.Select(entry => entry.Name));
        Assert.True(entries[1].StartTime > firstPaint.StartTime);

        // The paint timing entries are available from the performance timeline
        Assert.Equal(entries, document.DefaultView!.Performance.GetEntriesByType("paint"));
    }

    [Fact]
    public void FirstContentfulPaint_TransparentText()
    {
        var document = Parse("<div style='color: transparent'>Text</div>");
        var entries = Observe<PerformancePaintTiming>(document, "paint");
        var lcp = Observe<LargestContentfulPaint>(document, "largest-contentful-paint");
        RunFrames(2);

        Assert.Equal(["first-paint", "first-contentful-paint"], entries.Select(entry => entry.Name));

        // A transparent text is not a largest contentful paint candidate
        Assert.Empty(lcp);
    }

    [Theory]
    [InlineData("opacity: 0")]
    [InlineData("visibility: hidden")]
    [InlineData("position: relative; left: -1000px")]
    [InlineData("transform: scale(0)")]
    public void FirstContentfulPaint_NotPaintable(string style)
    {
        var document = Parse($"<div id=text style='{style}'>Text</div>");
        var entries = Observe<PerformancePaintTiming>(document, "paint");
        RunFrames(2);

        Assert.DoesNotContain(entries, entry => entry.Name == "first-contentful-paint");

        Get(document, "text").RemoveAttribute("style");
        RunFrames(2);

        Assert.Contains(entries, entry => entry.Name == "first-contentful-paint");
    }

    [Fact]
    public void FirstContentfulPaint_WhiteSpaceIsNotContentful()
    {
        var document = Parse("<div style='white-space: pre; background: blue'>   </div>");
        var entries = Observe<PerformancePaintTiming>(document, "paint");
        RunFrames(2);

        Assert.Equal(["first-paint"], entries.Select(entry => entry.Name));
    }

    [Fact]
    public void LargestContentfulPaint_Text_IsOwnedByItsContainingBlock()
    {
        var document = Parse("<p id=small>ab</p><p id=large>abc <span id=inline>defgh</span></p>");
        var entries = Observe<LargestContentfulPaint>(document, "largest-contentful-paint");
        RunFrames(2);

        var entry = Assert.Single(entries);
        Assert.Same(Get(document, "large"), entry.Element);
        Assert.Equal("large", entry.Id);
        Assert.Equal((uint)(90 * 10), entry.Size);
        Assert.Equal("", entry.Url);
        Assert.Equal(0, entry.LoadTime);
        Assert.Equal(entry.RenderTime, entry.StartTime);
        Assert.Equal("", entry.Name);

        // A larger text is a new candidate; a removed element is not exposed anymore
        var paragraph = document.CreateElement("p");
        paragraph.TextContent = "abcdefghijklmnop";
        document.Body!.AppendChild(paragraph);
        RunFrames(2);

        Assert.Equal(2, entries.Count);
        Assert.Same(paragraph, entries[1].Element);
        paragraph.Remove();
        Assert.Null(entries[1].Element);
    }

    [Fact]
    public void LargestContentfulPaint_Image_UpscaledToItsNaturalSize()
    {
        var document = Parse("<img id=image src='/20x10.png' style='width: 200px; height: 100px'>");
        var entries = Observe<LargestContentfulPaint>(document, "largest-contentful-paint");
        RunFrames(3);

        var entry = Assert.Single(entries);
        Assert.Same(Get(document, "image"), entry.Element);
        Assert.Equal("https://example.com/20x10.png", entry.Url);
        Assert.Equal((uint)(20 * 10), entry.Size);
        Assert.True(entry.LoadTime >= 0 && entry.LoadTime <= entry.RenderTime);
    }

    [Fact]
    public void LargestContentfulPaint_StopsAfterAnInput()
    {
        var document = Parse("<p>ab</p>");
        var entries = Observe<LargestContentfulPaint>(document, "largest-contentful-paint");
        RunFrames(2);
        Assert.Single(entries);

        EventDispatcher.Dispatch(new KeyboardEvent("keydown", new KeyboardEventInit { Key = "a" }) { IsTrusted = true }, document.Body!);
        var paragraph = document.CreateElement("p");
        paragraph.TextContent = "abcdefghijklmnop";
        document.Body!.AppendChild(paragraph);
        RunFrames(2);

        Assert.Single(entries);
    }

    [Fact]
    public void ElementTiming_ImageAndText()
    {
        var document = Parse("<img id=image elementtiming=picture src='/40x30.png' style='display: block; margin-left: 10px'><p id=text elementtiming=paragraph style='margin: 0'>abc</p><p>no attribute</p>");
        var entries = Observe<PerformanceElementTiming>(document, "element", buffered: true);
        RunFrames(3);

        Assert.Equal(2, entries.Count);
        var image = Assert.Single(entries, entry => entry.Name == "image-paint");
        Assert.Equal("picture", image.Identifier);
        Assert.Equal("image", image.Id);
        Assert.Equal("https://example.com/40x30.png", image.Url);
        Assert.Equal((10, 0, 40, 30), (image.IntersectionRect.X, image.IntersectionRect.Y, image.IntersectionRect.Width, image.IntersectionRect.Height));
        Assert.Equal((40u, 30u), (image.NaturalWidth, image.NaturalHeight));
        Assert.Equal(image.RenderTime, image.StartTime);
        Assert.Equal(image.RenderTime, image.PaintTime);

        var text = Assert.Single(entries, entry => entry.Name == "text-paint");
        Assert.Equal("paragraph", text.Identifier);
        Assert.Same(Get(document, "text"), text.Element);
        Assert.Equal("", text.Url);
        Assert.Equal(0, text.LoadTime);
        Assert.Equal((0, 30, 30, 10), (text.IntersectionRect.X, text.IntersectionRect.Y, text.IntersectionRect.Width, text.IntersectionRect.Height));

        // A text element is reported once
        Get(document, "text").TextContent = "abcdef";
        RunFrames(2);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ElementTiming_Attribute_IsReflected()
    {
        var document = Parse("<div id=a></div>");
        var element = Get(document, "a");
        Assert.Equal("", element.ElementTiming);
        element.ElementTiming = "id";
        Assert.Equal("id", element.GetAttribute("elementtiming"));
    }

    [Fact]
    public void DisabledFeatures_AreNotSupportedEntryTypes()
    {
        var document = Parse("<p>Text</p>", new WebPlatformFeatures { PaintTimingEnabled = false, LargestContentfulPaintEnabled = false, ElementTimingEnabled = false });
        var entries = Observe<PerformanceEntry>(document, "paint");
        RunFrames(2);

        Assert.Empty(entries);
        Assert.DoesNotContain("paint", document.DefaultView!.Performance.SupportedEntryTypes);
        Assert.Null(document.LayoutEngineOrNull);
    }

    private sealed class PngLoader : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            var name = request.Url[(request.Url.LastIndexOf('/', StringComparison.Ordinal) + 1)..];
            if (name.EndsWith(".png", StringComparison.Ordinal) && name[..^4].Split('x') is [var w, var h])
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource(CreatePng(uint.Parse(w, CultureInfo.InvariantCulture), uint.Parse(h, CultureInfo.InvariantCulture))) { Headers = [new("Content-Type", "image/png")] });

            return ValueTask.FromResult<DocumentResource?>(new DocumentResource("not found") { StatusCode = 404, Headers = [new("Content-Type", "text/plain")] });
        }

        // A PNG header followed by bytes: the image data is large enough not to be a low-content image for the largest contentful paint
        private static byte[] CreatePng(uint width, uint height)
        {
            var png = new byte[29 + (width * height)];
            byte[] header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 0, 0, 0, 0, 0, 8, 6, 0, 0, 0];
            header.CopyTo(png, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), width);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), height);
            return png;
        }
    }
}
