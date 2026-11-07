using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The intrinsic sizing: min-content/max-content sizes and contributions, the sizing keywords, stretch, calc-size() and size containment.</summary>
public sealed class IntrinsicSizingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html) => Load("<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
            DocumentLoader = new PngLoader(),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Serves a PNG of WxH pixels for the URLs ending with <c>/WxH.png</c> (only the header is read), and a 404 for the other ones.</summary>
    private sealed class PngLoader : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            var name = request.Url[(request.Url.LastIndexOf('/', StringComparison.Ordinal) + 1)..];
            if (name.EndsWith(".png", StringComparison.Ordinal) && name[..^4].Split('x') is [var w, var h])
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource(CreatePng(uint.Parse(w, CultureInfo.InvariantCulture), uint.Parse(h, CultureInfo.InvariantCulture))) { Headers = [new("Content-Type", "image/png")] });

            return ValueTask.FromResult<DocumentResource?>(new DocumentResource("not found") { StatusCode = 404, Headers = [new("Content-Type", "text/plain")] });
        }

        private static byte[] CreatePng(uint width, uint height)
        {
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 0, 0, 0, 0, 0, 8, 6, 0, 0, 0];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), width);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), height);
            return png;
        }
    }

    private static (double Width, double Height) GetSize(Document document, string id)
    {
        var rect = document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById(id)!) ?? throw new InvalidOperationException("No box");
        return (rect.Width, rect.Height);
    }

    [Theory]
    [InlineData("width: min-content", 70)]
    [InlineData("width: max-content", 150)]
    [InlineData("width: fit-content", 150)]
    [InlineData("width: fit-content(100px)", 110)]
    [InlineData("width: fit-content(10px)", 70)]
    [InlineData("width: min-content; padding: 0 5px; border: 2px solid", 74)]
    [InlineData("width: min-content; box-sizing: border-box; padding: 0 5px", 70)]
    [InlineData("width: 0; min-width: max-content", 150)]
    [InlineData("width: 500px; max-width: min-content", 70)]
    [InlineData("width: stretch", 800)]
    [InlineData("width: -webkit-fill-available; margin: 0 10px", 780)]
    [InlineData("width: calc-size(max-content, size + 10px)", 160)]
    [InlineData("width: calc-size(min-content, size * 2)", 130)]
    [InlineData("width: calc-size(auto, size / 2)", 405)]
    [InlineData("width: calc-size(any, 50% + 5px)", 415)]
    public void InlineSizeKeywords_BlockLevel(string style, double width)
    {
        // "XXX XXX" in the 20px Ahem font: min-content 60px, max-content 140px, plus the 10px of paddings (the keywords are not
        // affected by box-sizing, fit-content() and calc-size() are)
        var document = Parse($"<div id=t style='font-size: 20px; padding: 0 5px; box-sizing: content-box; {style}'>XXX XXX</div>");
        Assert.Equal(width, GetSize(document, "t").Width);
    }

    [Theory]
    [InlineData(0, 70)]
    [InlineData(100, 90)]
    [InlineData(300, 150)]
    public void ShrinkToFit_Float(double containerWidth, double width)
    {
        var document = Parse($"<div style='width: {containerWidth}px'><div id=t style='float: left; margin: 5px; border: 3px solid; padding: 2px; font: 20px/1 Ahem'>XXX XXX</div></div>");
        Assert.Equal(width, GetSize(document, "t").Width);
    }

    [Theory]
    [InlineData("width: min-content", 70)]
    [InlineData("width: fit-content", 90)]
    [InlineData("width: max-content", 150)]
    [InlineData("width: stretch", 90)]
    [InlineData("width: 50%", 90)]
    [InlineData("min-width: max-content; width: 0", 150)]
    [InlineData("max-width: min-content; width: 200px", 70)]
    [InlineData("min-width: calc(100px + 50%); width: 0", 110)] // The cyclic percentage of a minimum size resolves against zero
    [InlineData("min-width: 50%; width: 0", 10)]
    [InlineData("min-width: fit-content(50%); width: 0", 70)] // fit-content(0): the min-content size
    [InlineData("max-width: fit-content(50%); width: 500px", 150)] // The maximum behaves as none: the max-content size
    public void Contributions_OfKeywordSizedChild(string style, double width)
    {
        // https://drafts.csswg.org/css-sizing-3/#intrinsic-contribution: a float in a 100px wrapper (90px available) containing a child
        var document = Parse($"<div style='width: 100px'><div id=t style='float: left; margin: 5px; border: 3px solid; padding: 2px; font: 20px/1 Ahem'><div style='{style}'>XXX XXX</div></div></div>");
        Assert.Equal(width, GetSize(document, "t").Width);
    }

    [Fact]
    public void Contribution_ReplacedElement_PercentageWidthIsCompressible()
    {
        // The min-content contribution of an image with a percentage width resolves it against zero: the float shrinks in a
        // narrow space, and takes the natural width in a wide one
        var narrow = Parse("<div style='width: 10px'><div id=t style='float: left'><img style='width: 100%' src='/200x100.png'></div></div>");
        Assert.Equal(10, GetSize(narrow, "t").Width);
        var wide = Parse("<div style='width: 500px'><div id=t style='float: left'><img style='width: 100%' src='/200x100.png'></div></div>");
        Assert.Equal(200, GetSize(wide, "t").Width);
    }

    [Fact]
    public void Contribution_ReplacedElement_TransfersDefiniteHeight()
    {
        // https://drafts.csswg.org/css-sizing-4/#aspect-ratio: the percentage height resolves against the definite height of the
        // inline-block, and is transferred through the natural aspect ratio of the image
        var document = Parse("<div><span id=t style='display: inline-block; height: 20px'><img style='width: 100%; height: 100%' src='/200x100.png'></span></div>");
        Assert.Equal((40, 20), GetSize(document, "t"));
    }

    [Fact]
    public void Contribution_SvgWithOnlyAspectRatio_IsZero()
    {
        var document = Load("<!DOCTYPE html><style>body { margin: 0 } .block { width: max-content; border: 2px solid } svg { display: block }</style>"
            + "<div class=block id=a><svg></svg></div><div class=block id=b><svg viewBox='0 0 4 1'></svg></div><div class=block id=c><svg height=40 viewBox='0 0 4 1'></svg></div>");
        Assert.Equal((304, 154), GetSize(document, "a"));
        Assert.Equal((4, 4), GetSize(document, "b"));
        Assert.Equal((164, 44), GetSize(document, "c"));
    }

    [Theory]
    [InlineData("width: min-content; height: 50px", 60, 60)]
    [InlineData("width: max-content; height: min-content", 110, 110)]
    [InlineData("width: 50px; height: max-content", 60, 60)]
    [InlineData("min-width: min-content; width: 0; height: 50px", 60, 60)]
    [InlineData("min-height: max-content; height: 0", 110, 110)]
    [InlineData("max-width: 50px; max-height: fit-content; height: 500px", 60, 60)]
    public void ReplacedElement_Keywords(string style, double width, double height)
    {
        var document = Parse($"<img id=t src='/100x100.png' style='margin: 5px; border: 3px solid; padding: 2px; {style}'>");
        Assert.Equal((width, height), GetSize(document, "t"));
    }

    [Theory]
    [InlineData("height: stretch", 100)]
    [InlineData("height: stretch; margin: 10px", 100)]
    [InlineData("height: stretch; margin: 10px; display: flow-root", 100)]
    [InlineData("min-height: stretch", 100)]
    [InlineData("height: 300px; max-height: stretch", 100)]
    [InlineData("height: min-content", 20)]
    [InlineData("height: 500px; max-height: max-content", 20)]
    [InlineData("height: 0; min-height: fit-content", 20)]
    [InlineData("height: calc-size(auto, size * 2 + 5px)", 45)]
    [InlineData("height: calc-size(stretch, size / 2)", 50)]
    public void BlockSizeKeywords(string style, double height)
    {
        // The parent participates in the block formatting context of the body and has no border: the margins of the child are
        // treated as zero for its stretch-fit block size (they would collapse with the margins of the parent)
        var document = Parse($"<div style='height: 100px'><div id=t style='{style}'><div style='height: 20px'></div></div></div>");
        Assert.Equal(height, GetSize(document, "t").Height);
    }

    [Fact]
    public void BlockSizeStretch_ParentWithBorder()
    {
        var document = Parse("<div style='height: 100px; border-top: 5px solid'><div id=t style='height: stretch; margin: 10px'></div></div>");
        Assert.Equal(90, GetSize(document, "t").Height);
    }

    [Fact]
    public void BlockSizeStretch_IndefiniteBehavesAsAuto()
    {
        var document = Parse("<div><div id=t style='height: stretch'><div style='height: 20px'></div></div></div>");
        Assert.Equal(20, GetSize(document, "t").Height);
    }

    [Theory]
    [InlineData("contain: size", 0, 0)]
    [InlineData("contain: size; contain-intrinsic-size: 30px 40px", 30, 40)]
    [InlineData("contain: inline-size; contain-intrinsic-size: 30px 40px", 30, 20)]
    [InlineData("content-visibility: hidden; contain-intrinsic-size: 30px 40px", 30, 40)]
    [InlineData("container-type: size; contain-intrinsic-size: 30px 40px", 30, 40)]
    public void SizeContainment_ContainIntrinsicSize(string style, double width, double height)
    {
        var document = Parse($"<div id=t style='width: max-content; {style}'><div style='width: 100px; height: 20px'></div></div>");
        Assert.Equal((width, height), GetSize(document, "t"));
    }

    [Fact]
    public void OrthogonalFlow_AutoInlineSizeIsFitContent()
    {
        // https://drafts.csswg.org/css-writing-modes-4/#orthogonal-auto: the available inline space is the definite height of the
        // containing block; the auto height of the vertical box is its fit-content size, not the whole space
        var document = Parse("<div style='height: 200px'><div id=t style='writing-mode: vertical-rl; font: 20px/1 Ahem'>XX XX</div></div>");
        Assert.Equal((20, 100), GetSize(document, "t"));
    }

    [Fact]
    public void OrthogonalFlow_Contribution()
    {
        // The vertical box contributes its block size (its width: one line, its fit-content height in the 600px initial
        // containing block is 100px) to the float
        var document = Parse("<div id=t style='float: left'><div style='writing-mode: vertical-rl; font: 20px/1 Ahem'>XX XX</div></div>");
        Assert.Equal((20, 100), GetSize(document, "t"));
    }

    [Fact]
    public void AspectRatio_KeywordSizeIsTransferredSize()
    {
        // https://drafts.csswg.org/css-sizing-4/#aspect-ratio-minimum: max-content is the size transferred from height: 0, then the
        // automatic minimum size (min-content) applies unless min-width is set
        var document = Parse("<style>.t { aspect-ratio: 1; width: max-content; height: 0; font: 20px/1 Ahem }</style><div class=t id=a style='min-width: 0'>X</div><div class=t id=b>X</div>");
        Assert.Equal(0, GetSize(document, "a").Width);
        Assert.Equal(20, GetSize(document, "b").Width);
    }

    [Fact]
    public void IntrinsicSizes_AreCachedPerPass()
    {
        // Nested inline-blocks: each one is measured once per layout pass (the intrinsic sizes are cached on the boxes)
        var html = string.Concat(Enumerable.Repeat("<span style='display: inline-block'>", 20)) + "X" + string.Concat(Enumerable.Repeat("</span>", 20));
        var document = Parse("<div id=t style='float: left'>" + html + "</div>");
        Assert.Equal(10, GetSize(document, "t").Width);
    }

    [Theory]
    [InlineData("allow-keywords", 50)]
    [InlineData("numeric-only", 100)]
    public void InterpolateSize_TransitionToAuto(string interpolateSize, double height)
    {
        // https://drafts.csswg.org/css-values-5/#interpolate-size: with allow-keywords, the transition from 0 to auto interpolates
        // calc-size(auto, size); otherwise it is discrete (auto at 50%)
        var document = Parse($"<div id=t style='interpolate-size: {interpolateSize}; height: 0px'><div style='height: 100px'></div></div>");
        var target = (HtmlElement)document.GetElementById("t")!;
        _ = document.DefaultView!.GetComputedStyle(target).GetPropertyValue("height");
        target.Style.TransitionDuration = "100s";
        target.Style.TransitionDelay = "-50s";
        target.Style.TransitionTimingFunction = "linear";
        target.Style.TransitionProperty = "height";
        target.Style.TransitionBehavior = "allow-discrete";
        target.Style.SetProperty("height", "auto");
        Assert.Equal(height, GetSize(document, "t").Height);
    }

    [Fact]
    public void CalcSize_AnimationIsInterpolated()
    {
        var document = Parse("<div id=t style='height: calc-size(auto, size)'><div style='height: 100px'></div></div>");
        var target = (HtmlElement)document.GetElementById("t")!;
        _ = document.DefaultView!.GetComputedStyle(target).GetPropertyValue("height");
        target.Style.TransitionDuration = "100s";
        target.Style.TransitionDelay = "-50s";
        target.Style.TransitionTimingFunction = "linear";
        target.Style.TransitionProperty = "height";
        target.Style.SetProperty("height", "calc-size(auto, size * 2)");
        Assert.Equal(150, GetSize(document, "t").Height);
    }

    [Theory]
    [InlineData("min-height: calc(25px + 50%)", 25)]
    [InlineData("min-height: 50%", 10)]
    [InlineData("min-height: calc(50% - 30px)", 10)]
    public void MinimumBlockSize_PercentageOfAnIndefiniteSizeResolvesAgainstZero(string style, double height)
    {
        // As Blink (WPT css/css-values/calc-min-height-block-1.html): the block size of the containing block depends on its contents
        var document = Parse($"<div><div id=t style='{style}'><div style='height: 10px'></div></div></div>");
        Assert.Equal(height, GetSize(document, "t").Height);
    }

    [Theory]
    [InlineData("-webkit-fill-available")]
    [InlineData("-moz-available")]
    public void Stretch_LegacyKeywords(string keyword)
    {
        var document = Parse($"<div id=t style='float: left; width: {keyword}'>X</div>");
        Assert.Equal(800, GetSize(document, "t").Width);
    }

    [Fact]
    public void AspectRatio_TransfersTheBlockLimitsToAnIntrinsicInlineSize()
    {
        // https://drafts.csswg.org/css-sizing-4/#aspect-ratio-size-transfers: max-content is an indefinite preferred size
        var document = Parse("<div id=t style='max-height: 100px; aspect-ratio: 1; width: max-content'><div style='width: 200px'></div></div>");
        Assert.Equal((100d, 100d), GetSize(document, "t"));
    }

    [Theory]
    [InlineData("min-height: auto", 50)] // The automatic minimum size: the box grows to its contents
    [InlineData("min-height: min-content", 25)] // The min-content block size is the size given by the ratio
    public void AspectRatio_MinimumBlockSize(string style, double height)
    {
        var document = Parse($"<div id=t style='width: 100px; aspect-ratio: 4 / 1; {style}'><div style='height: 50px'></div></div>");
        Assert.Equal(height, GetSize(document, "t").Height);
    }
}
