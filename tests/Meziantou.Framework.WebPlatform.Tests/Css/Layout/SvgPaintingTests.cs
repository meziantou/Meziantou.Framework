using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The painting of the SVG content (#377): the paint servers, the strokes, the markers, the texts, the use elements, the images,
/// the SVG viewports and the SVG images.
/// </summary>
public sealed class SvgPaintingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static string Dump(Document document) => document.GetDisplayList()!.Dump();

    private static DisplayItem[] Items(Document document) => document.GetDisplayList()!.Items.ToArray();

    [Fact]
    public void LinearGradient_ObjectBoundingBoxShader()
    {
        var document = Parse("""
            <svg width=100 height=100 style='display: block'>
              <linearGradient id=g><stop offset=0 stop-color='rgb(255, 0, 0)'/><stop offset=1 stop-color='rgb(0, 0, 255)'/></linearGradient>
              <rect id=r x=10 y=20 width=50 height=40 fill='url(#g)'/>
            </svg>
            """);
        var item = Items(document).OfType<DrawPathItem>().Single();
        var shader = Assert.IsType<LinearGradientShader>(item.Paint.Shader);
        Assert.Equal(new PhysicalOffset(0, 0), shader.Start);
        Assert.Equal(new PhysicalOffset(1, 0), shader.End);
        Assert.Equal(2, shader.Stops.Length);
        Assert.False(shader.InterpolatesPremultiplied);

        // The objectBoundingBox units map the unit square to the bounding box
        Assert.Equal(new PhysicalOffset(10, 20), shader.LocalTransform!.Value.Apply(new PhysicalOffset(0, 0)));
        Assert.Equal(new PhysicalOffset(60, 60), shader.LocalTransform!.Value.Apply(new PhysicalOffset(1, 1)));
    }

    [Fact]
    public void Gradient_InheritsTheStopsAndTheAttributesOfItsTemplate()
    {
        var document = Parse("""
            <svg width=100 height=100 style='display: block'>
              <radialGradient id=base cx=0.25 spreadMethod=reflect><stop offset=0 stop-color='rgb(255, 0, 0)'/><stop offset=1 stop-color='rgb(0, 0, 255)'/></radialGradient>
              <radialGradient id=g href='#base' r=0.75 gradientUnits=userSpaceOnUse />
              <rect width=100 height=100 fill='url(#g)'/>
            </svg>
            """);
        var shader = Assert.IsType<RadialGradientShader>(Items(document).OfType<DrawPathItem>().Single().Paint.Shader);
        Assert.Equal(SpreadMode.Reflect, shader.Spread);
        Assert.Equal(0.75, shader.EndRadius);
        Assert.Equal(0.25, shader.EndCenter.X);
        Assert.Equal(2, shader.Stops.Length);
    }

    [Fact]
    public void PaintServer_InvalidReferenceUsesTheFallback()
    {
        var document = Parse("<svg width=100 height=100 style='display: block'><rect width=10 height=10 fill='url(#missing) rgb(0, 128, 0)'/></svg>");
        Assert.Contains("fill #008000", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Pattern_TileIsADisplayList()
    {
        var document = Parse("""
            <svg width=100 height=100 style='display: block'>
              <pattern id=p width=20 height=10 patternUnits=userSpaceOnUse><rect width=10 height=10 fill='rgb(0, 128, 0)'/></pattern>
              <rect width=100 height=100 fill='url(#p)'/>
            </svg>
            """);
        var tiled = Assert.IsType<TiledShader>(Items(document).OfType<DrawPathItem>().Single().Paint.Shader);
        Assert.Equal(new PhysicalSize(20, 10), tiled.TileSize);
        var content = Assert.IsType<DisplayListShader>(tiled.Content);
        Assert.Contains("fill #008000", content.Content.Dump(), StringComparison.Ordinal);
    }

    [Fact]
    public void Stroke_PathLengthScalesTheDashes()
    {
        var document = Parse("<svg width=100 height=100 style='display: block'><path d='M0 10H100' pathLength=10 stroke='rgb(0, 0, 0)' stroke-dasharray='1 1'/></svg>");
        Assert.Contains("stroke #000000 width=1 dashes=10,10", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Stroke_NonScalingStrokeIsInTheCoordinatesOfThePainting()
    {
        var document = Parse("""
            <svg width=100 height=100 viewBox='0 0 10 10' style='display: block'>
              <line x1=0 y1=5 x2=10 y2=5 stroke='rgb(0, 0, 0)' stroke-width=2 vector-effect=non-scaling-stroke />
            </svg>
            """);
        var dump = Dump(document);
        Assert.Contains("draw-path M0 50L100 50 stroke #000000 width=2", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Markers_ArePaintedAtTheVerticesWithTheirOrientation()
    {
        var document = Parse("""
            <svg width=100 height=100 style='display: block'>
              <marker id=m markerWidth=4 markerHeight=4 refX=2 refY=2 orient=auto markerUnits=userSpaceOnUse><rect width=4 height=4 fill='context-stroke'/></marker>
              <polyline points='10,10 50,10 50,50' fill=none stroke='rgb(0, 0, 255)' marker-start='url(#m)' marker-mid='url(#m)' marker-end='url(#m)'/>
            </svg>
            """);
        var markers = Items(document).OfType<DrawPathItem>().Where(item => item.Source is SvgElement { LocalName: "rect" }).ToList();
        Assert.Equal(3, markers.Count);
        Assert.All(markers, item => Assert.Equal(DisplayColor.FromRgb(0, 0, 255), item.Paint.Color));

        // The middle vertex turns from 0 to 90 degrees: the marker is rotated by 45 degrees
        Assert.Contains("transform matrix(0.707, 0.707, -0.707, 0.707, 50, 7.172)", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Markers_ShorthandSetsTheThreeProperties()
    {
        var document = Parse("<svg><g id=g style='marker: url(#m)'/></svg>");
        var style = document.GetStyleEngine().GetStyle(document.GetElementById("g")!);
        Assert.Equal(style.GetComputedValue("marker-start"), style.GetComputedValue("marker-end"));
        Assert.StartsWith("url(", style.GetComputedValue("marker-mid"), StringComparison.Ordinal);
    }

    [Fact]
    public void PaintOrder_KeepsTheSpecifiedOrder()
    {
        var document = Parse("<svg><g id=a style='paint-order: stroke fill markers'/><g id=b style='paint-order: markers stroke fill'/></svg>");
        Assert.Equal("stroke", document.GetStyleEngine().GetStyle(document.GetElementById("a")!).GetComputedValue("paint-order"));
        Assert.Equal("markers stroke", document.GetStyleEngine().GetStyle(document.GetElementById("b")!).GetComputedValue("paint-order"));
    }

    [Fact]
    public void Text_PaintsTheGlyphsOfTheCharacters()
    {
        var document = Parse("<svg width=200 height=100 style='display: block'><text x=10 y=50 font-family=Ahem font-size=20 fill='rgb(0, 128, 0)'>ab</text></svg>");
        var glyphs = Items(document).OfType<DrawGlyphsItem>().Single();
        Assert.Equal("ab", glyphs.Run.Text);
        Assert.Equal(DisplayColor.FromRgb(0, 128, 0), glyphs.Paint.Color);
        Assert.Equal(new PhysicalOffset(10, 50), glyphs.Run.Positions[0]);
        Assert.Equal(new PhysicalOffset(30, 50), glyphs.Run.Positions[1]);
    }

    [Fact]
    public void Text_SelectionPaintsWithTheHighlightPaints()
    {
        var document = Parse("""
            <style>::selection { background-color: rgb(255, 255, 0); fill: rgb(0, 0, 255); stroke: rgb(255, 0, 0); stroke-width: 2px }</style>
            <svg width=200 height=100 style='display: block'><text id=t x=10 y=50 font-family=Ahem font-size=20 fill='rgb(0, 128, 0)'>abcd</text></svg>
            """);
        var text = document.GetElementById("t")!.FirstChild!;
        var range = document.CreateRange();
        range.SetStart(text, 1);
        range.SetEnd(text, 3);
        document.GetSelection()!.AddRange(range);

        // The background of the selection covers the glyph cells of "bc", under the glyphs
        var items = Items(document);
        var background = items.OfType<DrawPathItem>().Single();
        Assert.Equal(DisplayColor.FromRgb(255, 255, 0), background.Paint.Color);
        Assert.Equal(new PhysicalRect(30, 34, 40, 20), background.Path.GetBounds());
        Assert.True(Array.IndexOf(items, background) < Array.FindIndex(items, item => item is DrawGlyphsItem));

        // "bc" is filled and stroked with the paints of the selection, "a" and "d" with the fill of the text
        var glyphs = items.OfType<DrawGlyphsItem>().ToArray();
        Assert.Equal(["a", "bc", "d", "bc"], glyphs.Select(item => item.Run.Text));
        Assert.Equal(DisplayColor.FromRgb(0, 128, 0), glyphs[0].Paint.Color);
        Assert.Equal(DisplayColor.FromRgb(0, 0, 255), glyphs[1].Paint.Color);
        Assert.Equal(DisplayColor.FromRgb(0, 128, 0), glyphs[2].Paint.Color);
        Assert.Equal(PaintStyle.Stroke, glyphs[3].Paint.Style);
        Assert.Equal(DisplayColor.FromRgb(255, 0, 0), glyphs[3].Paint.Color);
        Assert.Equal(2, glyphs[3].Paint.Stroke!.Width);
    }

    [Fact]
    public void Text_SelectionColorIsTheFillOfTheSelectedText()
    {
        // A selection that only specifies color: no default background, the glyphs are filled with the color
        var document = Parse("""
            <style>::selection { color: rgb(0, 0, 255) }</style>
            <svg width=200 height=100 style='display: block'><text id=t x=10 y=50 font-family=Ahem font-size=20 fill='rgb(0, 128, 0)'>ab</text></svg>
            """);
        var range = document.CreateRange();
        range.SelectNodeContents(document.GetElementById("t")!);
        document.GetSelection()!.AddRange(range);
        var items = Items(document);
        Assert.Empty(items.OfType<DrawPathItem>());
        Assert.Equal(DisplayColor.FromRgb(0, 0, 255), items.OfType<DrawGlyphsItem>().Single().Paint.Color);
    }

    [Fact]
    public void Text_RightToLeftRunIsPaintedFromItsLeftEdge()
    {
        // The glyphs of "bc", reordered by the override, are painted at their visual positions in the direction of the line
        var document = Parse("<svg width=200 height=100 style='display: block'><text x=10 y=50 font-family=Ahem font-size=20>a<tspan style='unicode-bidi: bidi-override; direction: rtl'>bc</tspan></text></svg>");
        var glyphs = Items(document).OfType<DrawGlyphsItem>().ToArray();
        Assert.Equal(2, glyphs.Length);
        Assert.Equal("bc", glyphs[1].Run.Text);
        Assert.Equal(new PhysicalOffset(50, 50), glyphs[1].Run.Positions[0]);
        Assert.Equal(new PhysicalOffset(30, 50), glyphs[1].Run.Positions[1]);
    }

    [Fact]
    public void Use_InstanceInheritsFromTheUseElement()
    {
        var document = Parse("""
            <svg width=100 height=100 style='display: block'>
              <style>.container rect { fill: rgb(255, 0, 0) }</style>
              <defs><g class=container><rect id=r width=10 height=10 /></g></defs>
              <use href='#r' fill='rgb(0, 128, 0)'/>
            </svg>
            """);

        // The selectors do not cross the root of the use-element shadow tree: the rect inherits the fill of the use element
        var item = Items(document).OfType<DrawPathItem>().Single();
        Assert.Equal(DisplayColor.FromRgb(0, 128, 0), item.Paint.Color);
    }

    [Fact]
    public void NestedSvg_ClipsToItsViewport()
    {
        var document = Parse("<svg width=100 height=100 style='display: block'><svg x=10 y=10 width=20 height=20><rect width=50 height=50 /></svg></svg>");
        Assert.Contains("clip-rect (10, 10, 20x20)", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionalProcessing_SwitchPaintsTheFirstMatchingChild()
    {
        var document = Parse("""
            <svg width=100 height=100 style='display: block'><switch>
              <rect width=10 height=10 fill='rgb(255, 0, 0)' requiredExtensions='http://example.com/unknown'/>
              <rect width=10 height=10 fill='rgb(0, 0, 255)' systemLanguage='fr'/>
              <rect width=10 height=10 fill='rgb(0, 128, 0)' systemLanguage='en-GB'/>
              <rect width=10 height=10 fill='rgb(255, 0, 0)'/>
            </switch></svg>
            """);
        Assert.Equal(DisplayColor.FromRgb(0, 128, 0), Items(document).OfType<DrawPathItem>().Single().Paint.Color);
    }

    [Fact]
    public void ShapeRendering_CrispEdgesTurnsOffTheAntialiasing()
    {
        var document = Parse("<svg width=100 height=100 style='display: block'><rect width=10 height=10 shape-rendering=crispEdges /></svg>");
        Assert.False(Items(document).OfType<DrawPathItem>().Single().Paint.AntiAlias);
    }

    [Fact]
    public void SvgImage_DocumentIsLaidOutAtTheSizeOfTheImage()
    {
        var host = HtmlDocumentParser.ParseDocument("<p>x</p>");
        var bytes = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='50' height='50'><rect width='50' height='50' fill='blue'/></svg>");
        var image = new PaintImage("data:image/svg+xml,", 50, 50, bytes, "image/svg+xml");
        var svgImage = SvgImage.Get(image, host);
        Assert.NotNull(svgImage);
        Assert.Equal(50, svgImage.NaturalSize.Width);

        // The viewBox of the natural size is synthesized: the image is scaled to the size it is drawn at
        var list = svgImage.GetDisplayList(new PhysicalSize(100, 100));
        Assert.NotNull(list);
        Assert.Contains("transform matrix(2, 0, 0, 2, 0, 0)", list.Dump(), StringComparison.Ordinal);
        Assert.Same(list, svgImage.GetDisplayList(new PhysicalSize(100, 100)));
    }

    [Fact]
    public void SvgImage_ViewOfTheFragmentOverridesTheViewBox()
    {
        var host = HtmlDocumentParser.ParseDocument("<p>x</p>");
        var bytes = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='200' viewBox='0 0 200 200'><view id='half' viewBox='0 0 100 100'/><rect width='50' height='50' fill='blue'/></svg>");

        // svgView(): the viewBox of the view replaces the one of the root (0 0 200 200), and its transform applies to the content
        var image = new PaintImage("https://example.com/a.svg#svgView(viewBox(100,0,100,100);transform(scale(2)))", 200, 200, bytes, "image/svg+xml");
        var list = SvgImage.Get(image, host)!.GetDisplayList(new PhysicalSize(100, 100));
        Assert.Contains("transform matrix(2, 0, 0, 2, -100, 0)", list!.Dump(), StringComparison.Ordinal);

        // The id of a view element
        image = new PaintImage("https://example.com/a.svg#half", 200, 200, bytes, "image/svg+xml");
        list = SvgImage.Get(image, host)!.GetDisplayList(new PhysicalSize(100, 100));
        Assert.DoesNotContain("transform matrix(0.5", list!.Dump(), StringComparison.Ordinal);

        // A view specification followed by something else than a time segment is ignored
        image = new PaintImage("https://example.com/a.svg#svgView(viewBox(100,0,100,100))&xywh=0,0,10,10", 200, 200, bytes, "image/svg+xml");
        list = SvgImage.Get(image, host)!.GetDisplayList(new PhysicalSize(100, 100));
        Assert.Contains("transform matrix(0.5, 0, 0, 0.5, 0, 0)", list!.Dump(), StringComparison.Ordinal);
    }

    [Fact]
    public void Use_PercentagesOfTheReferencedSvgReferToTheViewportOfTheUseElement()
    {
        var document = Parse("""
            <svg width=800 height=600 style='display: block'>
              <defs><svg id=s><rect width='100%' height='100%' fill='rgb(0, 128, 0)'/></svg></defs>
              <svg width=10 height=10 overflow=visible><use href='#s' x=90 y=90 /><use href='#s' width=90 height=40 /></svg>
            </svg>
            """);

        // Without width and height, the svg element is 100% of the viewport of the use element (10x10), not of its own parent
        Assert.Equal(2, Items(document).OfType<DrawPathItem>().Count());
        var dump = Dump(document);
        Assert.Contains("draw-path M0 0L10 0L10 10L0 10Z", dump, StringComparison.Ordinal);
        Assert.Contains("draw-path M0 0L90 0L90 40L0 40Z", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Use_HrefIsTrimmedAndItsFragmentPercentDecoded()
    {
        var document = Parse("""
            <svg width=100 height=100 style='display: block'>
              <defs><rect id='a b' width=10 height=10 fill='rgb(0, 128, 0)'/></defs>
              <use href=' #a%20b '/>
            </svg>
            """);
        Assert.Equal(DisplayColor.FromRgb(0, 128, 0), Items(document).OfType<DrawPathItem>().Single().Paint.Color);
    }

    [Fact]
    public void Use_ExternalResourceDocumentIsFetched()
    {
        var requests = new List<string>();
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
            DocumentLoader = new ResourceLoader(requests),
        });
        var document = context.Navigate("https://example.com/", _ => """
            <!DOCTYPE html><svg width=100 height=100 style='display: block'>
              <use id=u href='sprites.svg#green'/>
              <use href='sprites.svg#none'/>
              <use href='missing.svg#green'/>
              <use href='data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg"><rect id="green" width="1" height="1"/></svg>#green'/>
            </svg>
            """);
        _contexts.EventLoop.RunUntilIdle();

        // The referenced element of the resource document is painted with the style sheets of the resource document, where it
        // matches :target; the data: URL is not loaded. The resource is fetched once for the use elements that reference it
        Assert.Equal(["https://example.com/sprites.svg#green", "https://example.com/missing.svg#green"], requests);
        Assert.Equal(DisplayColor.FromRgb(0, 128, 0), Items(document).OfType<DrawPathItem>().Single().Paint.Color);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);

        // A same-document reference replaces the external one
        document.GetElementById("u")!.SetAttribute("href", "#none");
        Assert.Empty(Items(document).OfType<DrawPathItem>());
    }

    [Fact]
    public void Properties_ReferenceTheElementsOfExternalResourceDocuments()
    {
        var requests = new List<string>();
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
            DocumentLoader = new PaintServerLoader(requests),
        });
        var document = context.Navigate("https://example.com/", _ => """
            <!DOCTYPE html><style>body { margin: 0 } #box { width: 50px; height: 50px; background: rgb(255, 0, 0); clip-path: url(servers.svg#clip) }</style>
            <svg width=100 height=100 style='display: block'>
              <rect id=gradient width=10 height=10 fill='url(servers.svg#gradient) rgb(255, 0, 0)'/>
              <rect id=missing y=20 width=10 height=10 fill='url(servers.svg#missing) rgb(0, 0, 255)'/>
              <rect id=error y=40 width=10 height=10 fill='url(missing.svg#gradient) rgb(0, 0, 255)'/>
              <rect id=other y=60 width=10 height=10 style='fill: url("https://other.example/servers.svg#gradient") rgb(0, 0, 255)'/>
              <g style='display: none'><rect fill='url(hidden.svg#gradient)'/></g>
            </svg>
            <div id=box></div>
            """);
        _contexts.EventLoop.RunUntilIdle();

        // The document of a reference is fetched once, when the property is required for rendering an element (not for the
        // elements that are not rendered), with a CORS request: the cross-origin response without CORS headers is a network error
        Assert.Equal(["https://example.com/missing.svg", "https://example.com/servers.svg", "https://other.example/servers.svg"], requests.Select(url => url.Split('#')[0]).Order(StringComparer.Ordinal));
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);

        var paths = Items(document).OfType<DrawPathItem>().ToArray();
        Assert.Equal(4, paths.Length);
        Assert.IsType<LinearGradientShader>(paths[0].Paint.Shader);

        // An invalid reference (no element with this id, a network error) uses the fallback
        Assert.Equal(DisplayColor.FromRgb(0, 0, 255), paths[1].Paint.Color);
        Assert.Equal(DisplayColor.FromRgb(0, 0, 255), paths[2].Paint.Color);
        Assert.Equal(DisplayColor.FromRgb(0, 0, 255), paths[3].Paint.Color);

        // The clip path of the CSS box is the one of the resource document
        Assert.Single(Items(document).OfType<ClipPathItem>());
    }

    /// <summary>Serves an SVG document with a gradient and a clip path for servers.svg (without CORS headers), and a 404 response for the other URLs.</summary>
    private sealed class PaintServerLoader(List<string> requests) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            requests.Add(request.Url);
            var resource = request.Url.Contains("/servers.svg", StringComparison.Ordinal)
                ? new DocumentResource("<svg xmlns='http://www.w3.org/2000/svg'><linearGradient id='gradient'><stop offset='0' stop-color='rgb(0, 128, 0)'/><stop offset='1' stop-color='rgb(0, 255, 0)'/></linearGradient><clipPath id='clip'><rect width='25' height='25'/></clipPath></svg>") { Headers = [new("Content-Type", "image/svg+xml")] }
                : new DocumentResource("") { StatusCode = 404, Headers = [new("Content-Type", "text/plain")] };
            return ValueTask.FromResult<DocumentResource?>(resource);
        }
    }

    [Fact]
    public void SvgImage_SpatialMediaFragmentOverridesTheViewBox()
    {
        var host = HtmlDocumentParser.ParseDocument("<p>x</p>");
        var bytes = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='200'><rect width='50' height='50' fill='blue'/></svg>");
        string Draw(string fragment) => SvgImage.Get(new PaintImage("https://example.com/a.svg#" + fragment, 200, 200, bytes, "image/svg+xml"), host)!.GetDisplayList(new PhysicalSize(200, 200))!.Dump();

        // The region is the viewBox of the image (in pixels, or in percentages of the size of the image); the last valid one wins
        Assert.Contains("transform matrix(2, 0, 0, 2, -200, -100)", Draw("xywh=100,50,100,100"), StringComparison.Ordinal);
        Assert.Contains("transform matrix(2, 0, 0, 2, -200, -100)", Draw("xywh=pixel:100,50,100,100"), StringComparison.Ordinal);
        Assert.Contains("transform matrix(2, 0, 0, 2, -200, -100)", Draw("xywh=percent:50,25,50,50"), StringComparison.Ordinal);
        Assert.Contains("transform matrix(2, 0, 0, 2, -200, -100)", Draw("xywh=0,0,10,10&t=5&xywh=100,50,100,100&xywh=0,0,0,10"), StringComparison.Ordinal);

        // A region that extends past the image is clipped to it
        Assert.Contains("transform matrix(2, 0, 0, 2, -200, -200)", Draw("xywh=100,100,500,500"), StringComparison.Ordinal);

        // An invalid region is ignored: no area, not integers, a percentage past the image
        foreach (var fragment in (string[])["xywh=100,50,0,100", "xywh=100.5,50,100,100", "xywh=-1,0,100,100", "xywh=percent:50,50,60,50", "xywh=300,0,10,10", "xywh=1,2,3"])
        {
            Assert.DoesNotContain("transform matrix(2", Draw(fragment), StringComparison.Ordinal);
        }
    }

    /// <summary>Serves an SVG document for sprites.svg, and a 404 response for the other URLs.</summary>
    private sealed class ResourceLoader(List<string> requests) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            requests.Add(request.Url);
            var resource = request.Url.Contains("/sprites.svg", StringComparison.Ordinal)
                ? new DocumentResource("<svg xmlns='http://www.w3.org/2000/svg'><style>:target { fill: rgb(0, 128, 0) }</style><rect id='green' width='10' height='10' fill='rgb(255, 0, 0)'/></svg>") { Headers = [new("Content-Type", "image/svg+xml")] }
                : new DocumentResource("") { StatusCode = 404, Headers = [new("Content-Type", "text/plain")] };
            return ValueTask.FromResult<DocumentResource?>(resource);
        }
    }

    [Fact]
    public void ContextFill_CurrentColorIsTheColorOfTheElementThatUsesIt()
    {
        var document = Parse("""
            <svg width=100 height=100 style='display: block' color='rgb(255, 0, 0)'>
              <defs><g id=g color='rgb(0, 128, 0)'><rect width=10 height=10 fill=context-fill /></g></defs>
              <use href='#g' fill=currentcolor />
            </svg>
            """);
        Assert.Equal(DisplayColor.FromRgb(0, 128, 0), Items(document).OfType<DrawPathItem>().Single().Paint.Color);
    }

    [Fact]
    public void Markers_StrokeWidthUnitsWithNonScalingStroke()
    {
        var document = Parse("""
            <svg width=200 height=200 style='display: block'>
              <marker id=m markerWidth=10 markerHeight=10 refY=5><rect width=10 height=10 fill='rgb(0, 128, 0)'/></marker>
              <line x2=10 y1=5 y2=5 stroke='rgb(255, 0, 0)' stroke-width=10 vector-effect=non-scaling-stroke marker-start='url(#m)' transform='scale(10)'/>
            </svg>
            """);

        // The stroke width (10 px of the host coordinate space) is 1 user unit: the marker is not scaled in the user space
        Assert.Contains("transform matrix(10, 0, 0, 10, 0, 0)", Dump(document), StringComparison.Ordinal);
        Assert.DoesNotContain("transform matrix(100, 0, 0, 100", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Stroke_DashArrayWithMathFunctionsAndPathLengthProperty()
    {
        var document = Parse("""
            <svg width=200 height=200 style='display: block'>
              <line id=a x2=100 stroke='rgb(0, 0, 0)' stroke-dasharray='calc(25% + 25px) 10' />
              <line id=b x2=100 stroke='rgb(0, 0, 0)' stroke-dasharray=1 pathLength=10 style='path-length: 4px' />
            </svg>
            """);
        var a = SvgStrokeStyle.FromElement(document.GetElementById("a")!);
        Assert.Equal([75, 10], a.DashArray!);

        // The pathLength attribute is the presentation attribute of the path-length property
        var b = SvgStrokeStyle.FromElement(document.GetElementById("b")!);
        Assert.Equal(4, b.PathLength);
        document.GetElementById("b")!.RemoveAttribute("style");
        Assert.Equal(10, SvgStrokeStyle.FromElement(document.GetElementById("b")!).PathLength);
        Assert.Equal("10px", document.DefaultView!.GetComputedStyle(document.GetElementById("b")!).GetPropertyValue("path-length"));
    }

    [Fact]
    public void Text_LengthAdjustSpacingAndGlyphsScalesTheGlyphs()
    {
        var document = Parse("<svg width=200 height=100 style='display: block'><text x=10 y=50 font-family=Ahem font-size=20 textLength=80 lengthAdjust=spacingAndGlyphs>ab</text></svg>");

        // The advance of the 2 characters (40) is stretched to 80: the glyphs are drawn in a frame scaled by 2 along the progression
        Assert.Contains("transform matrix(2, 0, 0, 1, 0, 0)", Dump(document), StringComparison.Ordinal);
        var glyphs = Items(document).OfType<DrawGlyphsItem>().Single();
        Assert.Equal(new PhysicalOffset(5, 50), glyphs.Run.Positions[0]);
        Assert.Equal(new PhysicalOffset(25, 50), glyphs.Run.Positions[1]);
    }

    [Fact]
    public void Text_UniformOpacityOfTheContentElementsIsTheGroupOfTheText()
    {
        var document = Parse("<svg width=200 height=100 style='display: block'><text x=10 y=50 opacity=0.5><tspan opacity=0.5>ab</tspan></text></svg>");

        // One group of opacity 0.25, not a group of 0.5 in a group of 0.5
        var layers = Items(document).OfType<SaveLayerItem>().ToList();
        Assert.Equal(0.25, Assert.Single(layers).Opacity);
    }

    [Fact]
    public void Outline_IsPaintedAroundTheBoundingBox()
    {
        var document = Parse("<svg width=100 height=100 style='display: block'><rect x=10 y=10 width=20 height=20 style='outline: 2px solid rgb(0, 0, 255)'/></svg>");
        Assert.Contains("draw-ring (8, 8, 24x24) - (10, 10, 20x20) fill #0000ff", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void RootSvg_IsClippedToItsViewport()
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/a.svg", _ => new DocumentResource("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='50'><circle r='200'/></svg>") { Headers = [new("Content-Type", "image/svg+xml")] });
        _contexts.EventLoop.RunUntilIdle();
        Assert.Contains("clip-rect (0, 0, 100x50)", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ForeignObject_IsAnIsolatedGroupForTheBlendModes()
    {
        var document = Parse("""
            <svg width=100 height=100 style='display: block'>
              <rect width=100 height=100 fill='rgb(255, 255, 0)'/>
              <foreignObject width=100 height=100><div style='width: 100px; height: 100px; background: lime; mix-blend-mode: difference'></div></foreignObject>
            </svg>
            """);

        // A layer isolates the content of the foreignObject element, then the layer of the blend mode
        var layers = Items(document).OfType<SaveLayerItem>().ToList();
        Assert.Equal(2, layers.Count);
        Assert.Equal(BlendMode.Normal, layers[0].BlendMode);
        Assert.Equal(BlendMode.Difference, layers[1].BlendMode);
    }

    [Fact]
    public void Mask_InADisplayNoneSubtreeIsNotApplied()
    {
        var document = Parse("""
            <div id=d style='width: 100px; height: 100px; background: rgb(0, 128, 0); mask: url(#m)'></div>
            <svg style='display: none'><mask id=m><rect width=10 height=10 fill='white'/></mask></svg>
            """);
        Assert.Empty(Items(document).OfType<SaveLayerItem>());
    }

    [Fact]
    public void ConditionalProcessing_ElementThatIsNotRenderedHasNoBoundingBox()
    {
        var document = Parse("""
            <svg width=100 height=100 style='display: block'>
              <rect id=a width=10 height=10 systemLanguage='x-none'/>
              <switch><rect id=b width=10 height=10 /><rect id=c width=10 height=10 /></switch>
            </svg>
            """);
        Assert.Equal(0, document.GetElementById("a")!.GetBoundingClientRect().Width);
        Assert.Equal(10, document.GetElementById("b")!.GetBoundingClientRect().Width);
        Assert.Equal(0, document.GetElementById("c")!.GetBoundingClientRect().Width);
    }

    [Fact]
    public void PresentationAttributes_NegativeRadiusAndNestedSvgSize()
    {
        var document = Parse("""
            <svg width=100 height=100 style='display: block'>
              <circle id=c r='-10'/><svg id=nested width=20 height=30 /><animate id=a fill=freeze />
              <text id=t dominant-baseline=text-before-edge>a</text>
            </svg>
            """);
        string Computed(string id, string property) => document.DefaultView!.GetComputedStyle(document.GetElementById(id)!).GetPropertyValue(property);

        // A negative radius is invalid; the width and height attributes of a nested svg element and the fill attribute of the
        // animation elements are not presentation attributes; text-before-edge is a legacy alias of text-top
        Assert.Equal("0px", Computed("c", "r"));
        Assert.Equal(0, ((SvgCircleElement)document.GetElementById("c")!).R.BaseVal.Value);
        Assert.Equal("auto", Computed("nested", "width"));
        Assert.Equal("rgb(0, 0, 0)", Computed("a", "fill"));
        Assert.Equal("text-top", Computed("t", "dominant-baseline"));
    }

    [Fact]
    public void SvgImage_NotWellFormedIsNotPainted()
    {
        var host = HtmlDocumentParser.ParseDocument("<p>x</p>");
        var image = new PaintImage("data:image/svg+xml,", 0, 0, System.Text.Encoding.UTF8.GetBytes("<svg"), "image/svg+xml");
        Assert.Null(SvgImage.Get(image, host));
        Assert.True(image.SvgImageFailed);
    }
}
