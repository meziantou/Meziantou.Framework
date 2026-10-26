using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The painting of the effects (#376): the clip paths, the clip rectangles, the masks, the filters (CSS functions, SVG filters,
/// backdrop filters, the filter() image), the isolation of the blend modes and the 3D transforms, and their hit testing.
/// </summary>
public sealed class EffectsPaintingTests : IDisposable
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

    private static Element Get(Document document, string id) => document.GetElementById(id)!;

    [Fact]
    public void ClipPath_BasicShapeClipsTheStackingContext()
    {
        var document = Parse("<div id=a style='clip-path: polygon(0 0, 100% 0, 0 100%); width: 100px; height: 50px; background: rgb(255, 0, 0)'></div>");
        var dump = Dump(document);
        Assert.Contains("clip-path M0 0L100 0L0 50Z  [div#a]", dump, StringComparison.Ordinal);

        // A clip path makes the stacking context a backdrop root (a layer)
        Assert.Contains("save-layer  [div#a]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void ClipPath_CircleIsARoundedRectangle()
    {
        var document = Parse("<div style='clip-path: circle(25px at 25px 25px); width: 100px; height: 100px'></div>");
        Assert.Contains("clip-rounded-rect (0, 0, 50x50) radii=25,25,25,25", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ClipPath_InsetResolvesThePercentagesPerAxis()
    {
        var document = Parse("<div style='clip-path: inset(10%); width: 200px; height: 100px'></div>");
        Assert.Contains("clip-rect (20, 10, 160x80)", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ClipPath_ClipPathElementWithSeveralShapesIsAMask()
    {
        var document = Parse("""
            <svg width=0 height=0 style="position: absolute"><clipPath id=c><rect width=10 height=10 /><rect x=20 width=10 height=10 /></clipPath></svg>
            <div id=a style='clip-path: url(#c); width: 100px; height: 100px; background: rgb(255, 0, 0)'></div>
            """);
        var dump = Dump(document);
        Assert.Contains("save-layer blend=destinationin  [div#a]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-path M20 0L30 0L30 10L20 10Z fill #000000", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void ClipPath_ClipPathElementWithOneShapeIsAClip()
    {
        var document = Parse("""
            <svg width=0 height=0 style="position: absolute"><clipPath id=c clipPathUnits=objectBoundingBox><rect width=0.5 height=0.5 /></clipPath></svg>
            <div style='clip-path: url(#c); width: 100px; height: 40px'></div>
            """);
        Assert.Contains("clip-path M0 0L50 0L50 20L0 20Z", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ClipPath_MissingReferenceDoesNotClip()
    {
        var document = Parse("<div style='clip-path: url(#missing); width: 100px; height: 40px'></div>");
        var dump = Dump(document);
        Assert.DoesNotContain("clip-path", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("destinationin", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Clip_RectangleOfAnAbsolutelyPositionedBox()
    {
        var document = Parse("<div id=a style='position: absolute; left: 10px; top: 10px; clip: rect(5px, 20px, 15px, auto); width: 100px; height: 100px; background: rgb(255, 0, 0)'></div>");
        Assert.Contains("clip-rect (10, 15, 20x10)", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Clip_IgnoredOnStaticBoxes()
    {
        var document = Parse("<div style='clip: rect(5px, 20px, 15px, auto); width: 100px; height: 100px; background: rgb(255, 0, 0)'></div>");
        Assert.DoesNotContain("clip-rect", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_ImageLayersArePaintedInADestinationInLayer()
    {
        var document = Parse("<div id=a style='mask-image: linear-gradient(black, transparent), radial-gradient(black, transparent); mask-composite: intersect; width: 100px; height: 100px; background: rgb(255, 0, 0)'></div>");
        var dump = Dump(document);
        Assert.Contains("save-layer blend=destinationin  [div#a]", dump, StringComparison.Ordinal);

        // The bottom layer is painted first, the top one is composited with it by its operator
        Assert.Contains("save-layer blend=sourcein  [div#a]", dump, StringComparison.Ordinal);
        Assert.True(dump.IndexOf("radial-gradient", StringComparison.Ordinal) < dump.IndexOf("linear-gradient", StringComparison.Ordinal));
    }

    [Fact]
    public void Mask_MaskElementIsALuminanceMask()
    {
        var document = Parse("""
            <svg width=0 height=0 style="position: absolute"><mask id=m><rect width=50 height=50 fill=white /></mask></svg>
            <div id=a style='mask-image: url(#m); width: 100px; height: 100px; background: rgb(255, 0, 0)'></div>
            """);
        var dump = Dump(document);
        Assert.Contains("save-layer filters=luminance-to-alpha  [div#a]", dump, StringComparison.Ordinal);
        Assert.Contains("clip-rect (-10, -10, 120x120)", dump, StringComparison.Ordinal);
        Assert.Contains("draw-path M0 0L50 0L50 50L0 50Z fill #ffffff", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void MaskBorder_IsPaintedAsABorderImageInTheMask()
    {
        var document = Parse("<div id=a style='mask-border: linear-gradient(black, black) 10; width: 100px; height: 100px'></div>");
        var dump = Dump(document);
        Assert.Contains("save-layer blend=destinationin  [div#a]", dump, StringComparison.Ordinal);
        Assert.Contains("fill tiles(linear-gradient 80x10)", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_ReferenceToAnSvgFilterIsAGraph()
    {
        var document = Parse("""
            <svg width=0 height=0 style="position: absolute"><filter id=f><feGaussianBlur stdDeviation=2 result=blur /><feOffset in=blur dx=3 /><feMerge><feMergeNode /><feMergeNode in=SourceGraphic /></feMerge></filter></svg>
            <div style='filter: url(#f); width: 100px; height: 100px'></div>
            """);
        Assert.Contains("filters=graph(gaussianblur[-1] offset[0] merge[1,-1] in (-10, -10, 120x120))", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_MissingReferenceIgnoresTheFilters()
    {
        var document = Parse("<div style='filter: blur(2px) url(#missing); width: 100px; height: 100px'></div>");
        Assert.DoesNotContain("filters=", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_PrimitivesInSrgb()
    {
        var document = Parse("""
            <svg width=0 height=0 style="position: absolute"><filter id=f color-interpolation-filters=sRGB><feFlood flood-color=green /></filter></svg>
            <div style='filter: url(#f); width: 100px; height: 100px'></div>
            """);
        Assert.Contains("graph(flood[]srgb", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void BackdropFilter_FiltersTheBackdropInTheBorderBox()
    {
        var document = Parse("<div id=a style='backdrop-filter: blur(4px); width: 100px; height: 100px; border-radius: 10px'></div>");
        Assert.Contains("save-layer backdrop=blur(4) in (0, 0, 100x100) radii=10,10,10,10  [div#a]", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void FilterImage_IsAFilteredShader()
    {
        var document = Parse("<div id=a style='background-image: filter(linear-gradient(red, blue), invert(1)); width: 100px; height: 100px'></div>");
        var draw = document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().Single(item => item.Source == Get(document, "a"));
        var shader = Assert.IsType<FilteredShader>(draw.Paint.Shader);
        Assert.IsType<LinearGradientShader>(shader.Content);
    }

    [Fact]
    public void BlendMode_IsolatesTheParentStackingContext()
    {
        var document = Parse("""
            <div id=parent style='position: relative; z-index: 1; width: 100px; height: 100px'>
              <div id=child style='mix-blend-mode: multiply; width: 50px; height: 50px; background: rgb(255, 0, 0)'></div>
            </div>
            """);
        var dump = Dump(document);
        Assert.Contains("save-layer  [div#parent]", dump, StringComparison.Ordinal);
        Assert.Contains("save-layer blend=multiply  [div#child]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void RootElementEffects_ApplyToTheCanvasBackground()
    {
        var document = Parse("<style>html { background: rgb(0, 0, 255); opacity: 0.5 }</style>");
        var dump = Dump(document);
        Assert.True(dump.IndexOf("save-layer opacity=0.5  [html]", StringComparison.Ordinal) < dump.IndexOf("draw-paint fill #0000ff", StringComparison.Ordinal));
    }

    [Fact]
    public void Transforms3D_PerspectiveOfTheParentIsApplied()
    {
        var document = Parse("<div style='perspective: 100px; width: 100px; height: 100px'><div style='transform: translateZ(-100px); width: 100px; height: 100px'></div></div>");
        Assert.Contains("matrix3d(", Dump(document), StringComparison.Ordinal);

        var child = (HtmlElement)document.Body!.FirstElementChild!.FirstElementChild!;
        var rect = child.GetBoundingClientRect();
        Assert.Equal(50, rect.Width, 6);
        Assert.Equal(25, rect.X, 6);
    }

    [Fact]
    public void Transforms3D_PlanesArePaintedFromTheFarthest()
    {
        var document = Parse("""
            <div style='transform-style: preserve-3d; width: 100px; height: 100px'>
              <div id=near style='position: absolute; transform: translateZ(10px); width: 10px; height: 10px; background: rgb(0, 128, 0)'></div>
              <div id=far style='position: absolute; transform: translateZ(-10px); width: 10px; height: 10px; background: rgb(255, 0, 0)'></div>
            </div>
            """);
        var dump = Dump(document);
        Assert.True(dump.IndexOf("[div#far]", StringComparison.Ordinal) < dump.IndexOf("[div#near]", StringComparison.Ordinal));
    }

    [Fact]
    public void Transforms3D_PlaneWithoutDrawableContentDoesNotSplitTheOtherPlanes()
    {
        var document = Parse("""
            <div style='transform-origin: 0 0; transform-style: preserve-3d; transform: rotate3d(1, 1, 1, 90deg); height: 100px'>
              <div id=a style='transform-origin: 0 0; transform: rotate3d(1, 1, 1, -90deg); height: 50px; background: rgb(0, 128, 0)'></div>
            </div>
            """);
        var dump = Dump(document);
        Assert.Contains("[div#a]", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("clip-path", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Transforms3D_EdgesCreatedBySplitsAreNotAntialiased()
    {
        var document = Parse("""
            <div style='transform-style: preserve-3d; width: 100px; height: 100px'>
              <div style='position: absolute; transform: rotateY(45deg); width: 100px; height: 100px; background: rgb(0, 128, 0)'></div>
              <div style='position: absolute; transform: rotateY(-45deg); width: 100px; height: 100px; background: rgb(0, 0, 255)'></div>
            </div>
            """);
        var clips = document.GetDisplayList()!.Items.ToArray().OfType<ClipPathItem>().ToList();
        Assert.Equal(2, clips.Count);
        Assert.All(clips, clip => Assert.False(clip.AntiAlias));
    }

    [Fact]
    public void Transforms3D_BackFaceHidden()
    {
        var document = Parse("<div id=a style='transform: rotateY(180deg); backface-visibility: hidden; width: 10px; height: 10px; background: rgb(255, 0, 0)'></div>");
        Assert.DoesNotContain("[div#a]", Dump(document), StringComparison.Ordinal);
        Assert.NotSame(Get(document, "a"), document.ElementFromPoint(5, 5));
    }

    [Fact]
    public void Transforms3D_NonInvertibleTransformIsNotPainted()
    {
        var document = Parse("<div id=a style='transform: scale3d(1, 1, 0); width: 10px; height: 10px; background: rgb(255, 0, 0)'></div>");
        Assert.DoesNotContain("[div#a]", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Transforms3D_PreserveThreeDAccumulatesTheTransforms()
    {
        var document = Parse("""
            <div style='transform-style: preserve-3d; transform: rotateY(-45deg); width: 200px; height: 200px'>
              <div id=child style='transform: rotateY(45deg); width: 200px; height: 200px'></div>
            </div>
            <div style='transform-style: preserve-3d; transform: rotateY(-45deg); width: 200px; height: 200px'>
              <div><div id=flattened style='transform: rotateY(45deg); width: 200px; height: 200px'></div></div>
            </div>
            """);
        Assert.Equal(200, Get(document, "child").GetBoundingClientRect().Width, 6);
        Assert.Equal(100, Get(document, "flattened").GetBoundingClientRect().Width);
    }

    [Fact]
    public void HitTesting_ClipPathAndClip()
    {
        var document = Parse("""
            <div id=clipped style='clip-path: inset(0 50px 0 0); width: 100px; height: 100px; background: rgb(0, 128, 0)'></div>
            <div id=rect style='position: absolute; top: 200px; clip: rect(0, 50px, 50px, 0); width: 100px; height: 100px'></div>
            """);
        Assert.Same(Get(document, "clipped"), document.ElementFromPoint(10, 10));
        Assert.NotSame(Get(document, "clipped"), document.ElementFromPoint(80, 10));
        Assert.Same(Get(document, "rect"), document.ElementFromPoint(10, 210));
        Assert.NotSame(Get(document, "rect"), document.ElementFromPoint(80, 210));
    }

    [Fact]
    public void HitTesting_ClipPathElement()
    {
        var document = Parse("""
            <svg width=0 height=0 style="position: absolute"><clipPath id=c clipPathUnits=objectBoundingBox><circle cx=0.5 cy=0.5 r=0.5 /></clipPath></svg>
            <div id=a style='clip-path: url(#c); width: 100px; height: 100px'></div>
            """);
        Assert.Same(Get(document, "a"), document.ElementFromPoint(50, 50));
        Assert.NotSame(Get(document, "a"), document.ElementFromPoint(3, 3));
    }

    [Fact]
    public void HitTesting_ClipPathOfAShapeOfAClipPathElement()
    {
        // The clip path of the rect is applied in the user space of the rect (the bounding box units of the outer clipPath)
        var document = Parse("""
            <svg width=0 height=0 style="position: absolute">
              <clipPath id=inner><rect width=0.25 height=0.25 /></clipPath>
              <clipPath id=outer clipPathUnits=objectBoundingBox><rect width=0.5 height=0.5 clip-path="url(#inner)" /><rect x=0.9 width=0.1 height=0.1 /></clipPath>
            </svg>
            <div id=a style='clip-path: url(#outer); width: 100px; height: 100px'></div>
            """);
        Assert.Same(Get(document, "a"), document.ElementFromPoint(10, 10));
        Assert.NotSame(Get(document, "a"), document.ElementFromPoint(40, 40));
        Assert.Same(Get(document, "a"), document.ElementFromPoint(95, 5));
    }

    [Fact]
    public void ClipPath_ReferenceCycleIsIgnored()
    {
        var document = Parse("""
            <svg width=0 height=0 style="position: absolute"><clipPath id=c><rect width=50 height=50 clip-path="url(#c)" /><rect x=60 width=10 height=10 /></clipPath></svg>
            <div id=a style='clip-path: url(#c); width: 100px; height: 100px; background: rgb(255, 0, 0)'></div>
            """);
        Assert.Contains("draw-path M0 0L50 0L50 50L0 50Z fill #000000", Dump(document), StringComparison.Ordinal);
        Assert.Same(Get(document, "a"), document.ElementFromPoint(10, 10));
    }

    [Fact]
    public void ClipPath_TransformOfAClipPathElementInBoundingBoxUnitsIsInUserUnits()
    {
        var document = Parse("""
            <svg width=0 height=0 style="position: absolute"><clipPath id=c clipPathUnits=objectBoundingBox transform="translate(50, 0)"><rect width=0.5 height=1 /></clipPath></svg>
            <div style='clip-path: url(#c); width: 100px; height: 40px'></div>
            """);
        Assert.Contains("clip-path M50 0L100 0L100 40L50 40Z", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void HitTesting_ClipPathOfAnInlineBoxUsesItsFirstFragment()
    {
        var document = Parse("<div style='width: 20px'><span id=s style='clip-path: inset(0)'>XX XX</span></div>");
        Assert.Same(Get(document, "s"), document.ElementFromPoint(5, 5));
        Assert.NotSame(Get(document, "s"), document.ElementFromPoint(5, 15));
    }

    [Theory]
    [InlineData("inset(0 100 0 0) view-box", true)]
    [InlineData("inset(0 150 0 0) view-box", false)]
    public void HitTesting_ViewBoxOfAForeignObjectIsItsSvgViewport(string clipPath, bool hit)
    {
        // The unitless lengths of the presentation attribute are user units; the reference box is the 200x200 viewport
        var document = Parse($"""
            <svg width=200 height=200 style="display: block"><foreignObject x=50 y=20 width=100 height=100 clip-path="{clipPath}"><div id=d style="width: 100px; height: 100px"></div></foreignObject></svg>
            """);
        Assert.Equal(hit, ReferenceEquals(Get(document, "d"), document.ElementFromPoint(60, 30)));
        Assert.NotSame(Get(document, "d"), document.ElementFromPoint(120, 30));
    }

    [Fact]
    public void ClipPath_ClipPathElementOfAForeignObjectIsInItsUserSpace()
    {
        var document = Parse("""
            <svg width=200 height=200 style="display: block">
              <clipPath id=c><rect x=50 y=20 width=30 height=30 /></clipPath>
              <foreignObject x=50 y=20 width=100 height=100 clip-path="url(#c)"><div id=d style="width: 100px; height: 100px"></div></foreignObject>
            </svg>
            """);
        Assert.Same(Get(document, "d"), document.ElementFromPoint(60, 30));
        Assert.NotSame(Get(document, "d"), document.ElementFromPoint(100, 30));
    }

    [Fact]
    public void ClipPath_MarginBoxHasTheRadiiOfTheMarginBox()
    {
        // https://drafts.csswg.org/css-borders-4/#outset-adjusted-border-radius: the corner covers 2 * 10 / 50 = 0.4 of the border
        // box: 10 + 50 * (1 - (1 - 10 / 50)^3 * (1 - 0.4^3)) = 36.038 (34.4 with the formula of CSS Shapes 1, which has no coverage)
        var document = Parse("<div style='clip-path: margin-box; float: left; margin: 50px; border-radius: 10px; width: 50px; height: 50px'></div>");
        Assert.Contains("clip-rounded-rect (0, 0, 150x150) radii=36.038,36.038,36.038,36.038", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_RegionWithAMathFunctionInBoundingBoxUnits()
    {
        var document = Parse("""
            <svg width=0 height=0 style="position: absolute"><filter id=f x=0 y=0 width="calc(25% + 0.25px)" height=1><feFlood flood-color=green /></filter></svg>
            <div style='filter: url(#f); width: 200px; height: 100px'></div>
            """);
        Assert.Contains("in (0, 0, 100x100))", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_PercentagesOfTheUserSpaceReferToTheBorderBox()
    {
        var document = Parse("""
            <svg width=0 height=0 style="position: absolute"><filter id=f filterUnits=userSpaceOnUse x=0% y=0% width=80% height=80%><feFlood flood-color=green /></filter></svg>
            <div style='filter: url(#f); position: relative; width: 100px; height: 100px'><div style='position: absolute; left: 50px; top: 50px; width: 100px; height: 100px; background: rgb(255, 0, 0)'></div></div>
            """);
        Assert.Contains("in (0, 0, 80x80))", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void BackdropFilter_FiltersOfTheElementApplyToTheFilteredBackdrop()
    {
        var document = Parse("<div id=a style='backdrop-filter: blur(4px); filter: invert(1); width: 100px; height: 100px'></div>");
        var layer = document.GetDisplayList()!.Items.ToArray().OfType<SaveLayerItem>().Single(item => item.Source == Get(document, "a"));
        Assert.NotNull(layer.Filters);
        Assert.NotNull(layer.BackdropFilters);
    }

    [Theory]
    [InlineData("backdrop-filter: invert(1)", false)]
    [InlineData("opacity: 1", true)]
    public void Isolation_IsNotALayerAroundABackdropFilter(string childStyle, bool layer)
    {
        // https://drafts.fxtf.org/filter-effects-2/#BackdropRoot: isolation: isolate does not create a backdrop root
        var document = Parse($"""
            <div id=parent style='isolation: isolate; width: 100px; height: 100px'>
              <div style='{childStyle}; width: 50px; height: 50px'></div>
            </div>
            """);
        Assert.Equal(layer, Dump(document).Contains("save-layer  [div#parent]", StringComparison.Ordinal));
    }

    [Fact]
    public void Transform_TranslationIsPaintedAtItsSnappedPosition()
    {
        // The box is painted as a box that is not transformed (no transformed group whose origin is at a fractional position)
        var document = Parse("<div id=a style='transform: translate(10.3px, 20px); margin-left: 0.4px; width: 100px; height: 50px; background: rgb(255, 0, 0)'></div>");
        var dump = Dump(document);
        Assert.Contains("draw-rect (11, 20, 100x50) fill #ff0000  [div#a]", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("translate(10", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void FixedBackground_IsScrolledInATransformedBox()
    {
        // https://drafts.csswg.org/css-transforms-1/#transform-rendering: the fixed backgrounds of the transformed boxes are
        // positioned as scrolled backgrounds (also when the box is at the origin of its parent)
        string Paint(string style) => Dump(Parse($"<div style='{style}'><div id=a style='margin-left: 30px; width: 100px; height: 50px; background: linear-gradient(to right, rgb(255, 0, 0) 50px, rgb(0, 0, 255) 50px) fixed no-repeat'></div></div>"));
        var scrolled = Paint("transform: rotate(0.001deg)").Split('\n').Single(line => line.Contains("[div#a]", StringComparison.Ordinal));
        var fixedToViewport = Paint("").Split('\n').Single(line => line.Contains("[div#a]", StringComparison.Ordinal));
        Assert.NotEqual(fixedToViewport, scrolled);
        Assert.Equal(Dump(Parse("<div style='transform: rotate(0.001deg)'><div id=a style='margin-left: 30px; width: 100px; height: 50px; background: linear-gradient(to right, rgb(255, 0, 0) 50px, rgb(0, 0, 255) 50px) no-repeat'></div></div>")).Split('\n').Single(line => line.Contains("[div#a]", StringComparison.Ordinal)), scrolled);
    }

    [Fact]
    public void PaintOrder_LayersWithTheSameZIndexAreInTreeOrder()
    {
        var document = Parse("""
            <div id=abs style='position: absolute; width: 10px; height: 10px; background: rgb(255, 0, 0)'></div>
            <div><div id=opacity style='opacity: 0.5; width: 10px; height: 10px; background: rgb(0, 128, 0)'></div></div>
            """);
        var dump = Dump(document);
        Assert.True(dump.IndexOf("[div#abs]", StringComparison.Ordinal) < dump.IndexOf("[div#opacity]", StringComparison.Ordinal));
    }
}
