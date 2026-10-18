using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The flex layout (https://drafts.csswg.org/css-flexbox-1/).</summary>
public sealed class FlexFormattingContextTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html, bool quirks = false) => Load((quirks ? "" : "<!DOCTYPE html>") + "<style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);

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

    private static PhysicalRect GetRect(Document document, string id) => document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById(id)!) ?? throw new InvalidOperationException("No box");

    private static (double Width, double Height) GetSize(Document document, string id)
    {
        var rect = GetRect(document, id);
        return (rect.Width, rect.Height);
    }

    private static PhysicalRect Rect(double x, double y, double width, double height) => new(x, y, width, height);

    private static Element Get(Document document, string id) => document.GetElementById(id)!;

    [Fact]
    public void Lines_ItemsFitWithinTheLayoutUnitOfTheBrowsers()
    {
        // A row of Bootstrap: the columns of 25%, 8.33333333% + 16.66666667% and 3 x 16.66666667% are a little wider than the
        // container, and fit on one line in the browsers, whose sizes are multiples of 1/64px
        var document = Parse("<div style='display: flex; flex-wrap: wrap; width: 1156px'><div style='flex: none; width: 25%'></div><div style='flex: none; width: 16.66666667%; margin-left: 8.33333333%'></div>"
            + string.Concat(Enumerable.Range(0, 3).Select(i => $"<div id=c{i} style='flex: none; width: 16.66666667%; height: 10px'></div>")) + "</div>");
        Assert.Equal(0, GetRect(document, "c2").Y);
    }

    [Fact]
    public void Grow_DistributesThePositiveFreeSpace()
    {
        var document = Parse("<div style='display: flex; width: 300px; height: 20px'><div id=a style='flex: 1'></div><div id=b style='flex: 2'></div></div>");
        Assert.Equal(Rect(0, 0, 100, 20), GetRect(document, "a"));
        Assert.Equal(Rect(100, 0, 200, 20), GetRect(document, "b"));
    }

    [Fact]
    public void Shrink_IsScaledByTheFlexBaseSize()
    {
        var document = Parse("<div style='display: flex; width: 300px'><div id=a style='width: 200px; min-width: 0'></div><div id=b style='width: 400px; min-width: 0'></div></div>");
        Assert.Equal(100, GetSize(document, "a").Width);
        Assert.Equal(200, GetSize(document, "b").Width);
    }

    [Fact]
    public void ResolveFlexibleLengths_FreezesTheMinViolations()
    {
        var document = Parse("<div style='display: flex; width: 300px'><div id=a style='flex: 1; min-width: 200px'></div><div id=b style='flex: 1'></div></div>");
        Assert.Equal(200, GetSize(document, "a").Width);
        Assert.Equal(100, GetSize(document, "b").Width);
    }

    [Fact]
    public void AutomaticMinimumSize_IsTheMinContentSize()
    {
        var document = Parse("<div style='display: flex; width: 20px'><div id=a style='flex: 1 1 0'>XXXXX</div><div id=b style='flex: 1 1 0; min-width: 0'>XXXXX</div></div>");
        Assert.Equal(50, GetSize(document, "a").Width);
        Assert.Equal(0, GetSize(document, "b").Width);
    }

    [Fact]
    public void Wrap_CollectsTheItemsIntoLines()
    {
        var document = Parse("<div style='display: flex; flex-wrap: wrap; width: 100px'><div id=a style='width: 40px; height: 10px'></div><div id=b style='width: 40px; height: 20px'></div><div id=c style='width: 40px; height: 10px'></div></div>");
        Assert.Equal(Rect(0, 0, 40, 10), GetRect(document, "a"));
        Assert.Equal(Rect(40, 0, 40, 20), GetRect(document, "b"));
        Assert.Equal(Rect(0, 20, 40, 10), GetRect(document, "c"));
    }

    [Fact]
    public void WrapReverse_StacksTheLinesFromTheCrossEnd()
    {
        var document = Parse("<div style='display: flex; flex-wrap: wrap-reverse; width: 100px'><div id=a style='width: 60px; height: 10px'></div><div id=b style='width: 60px; height: 20px'></div></div>");
        Assert.Equal(Rect(0, 20, 60, 10), GetRect(document, "a"));
        Assert.Equal(Rect(0, 0, 60, 20), GetRect(document, "b"));
    }

    [Theory]
    [InlineData("flex-start", 0)]
    [InlineData("flex-end", 60)]
    [InlineData("center", 30)]
    [InlineData("space-between", 0)]
    [InlineData("space-around", 15)]
    [InlineData("space-evenly", 20)]
    public void JustifyContent(string value, double firstOffset)
    {
        var document = Parse($"<div style='display: flex; width: 100px; justify-content: {value}'><div id=a style='width: 20px'></div><div id=b style='width: 20px'></div></div>");
        Assert.Equal(firstOffset, GetRect(document, "a").X);
    }

    [Theory]
    [InlineData("stretch", 0, 50)]
    [InlineData("flex-start", 0, 10)]
    [InlineData("flex-end", 40, 10)]
    [InlineData("center", 20, 10)]
    public void AlignItems(string value, double y, double height)
    {
        var document = Parse($"<div style='display: flex; height: 50px; align-items: {value}'><div id=a style='width: 20px'>X</div></div>");
        var rect = GetRect(document, "a");
        Assert.Equal(y, rect.Y);
        Assert.Equal(height, rect.Height);
    }

    [Fact]
    public void AlignItems_Baseline()
    {
        // Ahem: the baseline is at 0.8em
        var document = Parse("<div style='display: flex; align-items: baseline'><div id=a style='font-size: 20px'>X</div><div id=b>X</div></div>");
        Assert.Equal(0, GetRect(document, "a").Y);
        Assert.Equal(8, GetRect(document, "b").Y);
    }

    [Fact]
    public void AutoMargins_AbsorbTheFreeSpace()
    {
        var document = Parse("<div style='display: flex; width: 100px; height: 50px'><div id=a style='width: 20px; height: 10px; margin: auto'></div></div>");
        Assert.Equal(Rect(40, 20, 20, 10), GetRect(document, "a"));
    }

    [Fact]
    public void Order_ChangesThePositionAndThePaintOrder()
    {
        var document = Parse("<div style='display: flex'><div id=a style='width: 20px; height: 10px'></div><div id=b style='width: 30px; height: 10px; order: -1'></div></div>");
        Assert.Equal(30, GetRect(document, "a").X);
        Assert.Equal(0, GetRect(document, "b").X);
    }

    [Fact]
    public void RowReverse_PlacesTheItemsFromTheMainEnd()
    {
        var document = Parse("<div style='display: flex; flex-direction: row-reverse; width: 100px'><div id=a style='width: 20px; height: 10px'></div><div id=b style='width: 30px; height: 10px'></div></div>");
        Assert.Equal(80, GetRect(document, "a").X);
        Assert.Equal(50, GetRect(document, "b").X);
    }

    [Fact]
    public void Column_IndefiniteHeightIsTheSumOfTheItems()
    {
        var document = Parse("<div id=c style='display: flex; flex-direction: column; width: 100px'><div id=a style='height: 20px; flex-grow: 1'></div><div id=b style='height: 30px'></div></div>");
        Assert.Equal(Rect(0, 0, 100, 50), GetRect(document, "c"));
        Assert.Equal(Rect(0, 20, 100, 30), GetRect(document, "b"));
    }

    [Fact]
    public void Column_MinHeightIsTheAvailableMainSpace()
    {
        var document = Parse("<div id=c style='display: flex; flex-direction: column; width: 100px; min-height: 100px'><div id=a style='height: 20px; flex-grow: 1'></div><div id=b style='height: 30px'></div></div>");
        Assert.Equal(100, GetSize(document, "c").Height);
        Assert.Equal(70, GetSize(document, "a").Height);
    }

    [Fact]
    public void Column_AutoWidthItemsAreFitContentWhenNotStretched()
    {
        var document = Parse("<div style='display: flex; flex-direction: column; width: 100px; align-items: flex-start'><div id=a>XXX</div></div>");
        Assert.Equal((30, 10), GetSize(document, "a"));
    }

    [Fact]
    public void Gap_SeparatesTheItemsAndTheLines()
    {
        var document = Parse("<div style='display: flex; flex-wrap: wrap; width: 100px; gap: 5px 10px'><div id=a style='width: 50px; height: 10px'></div><div id=b style='width: 40px; height: 10px'></div><div id=c style='width: 50px; height: 10px'></div></div>");
        Assert.Equal(60, GetRect(document, "b").X);
        Assert.Equal(15, GetRect(document, "c").Y);
    }

    [Fact]
    public void Percentages_ResolveAgainstTheStretchedCrossSize()
    {
        var document = Parse("<div style='display: flex; height: 100px'><div><div id=a style='height: 50%'></div></div></div>");
        Assert.Equal(50, GetSize(document, "a").Height);
    }

    [Fact]
    public void Percentages_AreIndefiniteInTheFlexedItemsOfAnIndefiniteColumn()
    {
        var document = Parse("<div style='display: flex; flex-direction: column'><div style='flex: 1'><div id=a style='height: 50%'>X</div></div></div>");
        Assert.Equal(10, GetSize(document, "a").Height);
    }

    [Fact]
    public void AbsolutelyPositionedChild_StaticPositionIsAlignedAsTheSoleItem()
    {
        var document = Parse("<div style='display: flex; position: relative; width: 100px; height: 100px; justify-content: center; align-items: flex-end'><div id=a style='position: absolute; width: 20px; height: 10px'></div></div>");
        Assert.Equal(Rect(40, 90, 20, 10), GetRect(document, "a"));
    }

    [Fact]
    public void AbsolutelyPositionedChild_CenteredOnAStaticPositionOutsideOfTheContainingBlock()
    {
        // The containing block is the initial containing block (785x600 with the scrollbar): the flex container is below it, and the box is
        // centered on its static position, as in the browsers (the hidden labels of www.w3.org)
        var document = Parse("<div style='height: 1000px'></div><div style='display: flex; height: 20px; align-items: center; justify-content: center'><div id=a style='position: absolute; width: 10px; height: 10px'></div></div>");
        Assert.Equal(Rect(387.5, 1005, 10, 10), GetRect(document, "a"));
    }

    [Fact]
    public void VisibilityCollapse_KeepsTheCrossSizeOfTheLine()
    {
        var document = Parse("<div id=c style='display: flex; width: 300px'><div style='visibility: collapse; width: 100px; height: 50px'></div><div id=b style='flex: 1; height: 10px'></div></div>");
        Assert.Equal(Rect(0, 0, 300, 10), GetRect(document, "b"));
        Assert.Equal(50, GetSize(document, "c").Height);
    }

    [Fact]
    public void IntrinsicSizes_RowAddsTheContributions()
    {
        var document = Parse("<div id=c style='display: inline-flex'><div style='width: 30px'></div><div style='width: 40px; margin-left: 5px'></div></div>");
        Assert.Equal(75, GetSize(document, "c").Width);
    }

    [Fact]
    public void IntrinsicSizes_ColumnTakesTheLargestContribution()
    {
        var document = Parse("<div id=c style='display: inline-flex; flex-direction: column'><div style='width: 30px'></div><div style='width: 40px'></div></div>");
        Assert.Equal(40, GetSize(document, "c").Width);
    }

    [Fact]
    public void IntrinsicSizes_MultiLineColumnAddsTheLines()
    {
        var document = Parse("<div id=c style='display: inline-flex; flex-flow: column wrap; height: 30px'><div style='width: 30px; height: 20px'></div><div style='width: 40px; height: 20px'></div></div>");
        Assert.Equal(70, GetSize(document, "c").Width);
    }

    [Fact]
    public void Baseline_OfAnInlineFlexContainerIsTheOneOfItsFirstItem()
    {
        var document = Parse("<div><span id=s>X</span><div id=c style='display: inline-flex; flex-direction: column'><div style='font-size: 20px'>X</div><div>X</div></div></div>");

        // The baseline of the container is the one of its first item: 16px from its top, aligned with the baseline of the line (8px)
        Assert.Equal(GetRect(document, "s").Y + 8 - 16, GetRect(document, "c").Y);
    }

    [Fact]
    public void WebkitBox_MapsToTheFlexLayout()
    {
        var document = Parse("<div style='display: -webkit-box; -webkit-box-orient: vertical; -webkit-box-pack: center; width: 100px; height: 100px'><div id=a style='height: 20px'></div><div id=b style='height: 20px; -webkit-box-flex: 1'></div></div>");
        Assert.Equal(Rect(0, 0, 100, 20), GetRect(document, "a"));
        Assert.Equal(Rect(0, 20, 100, 80), GetRect(document, "b"));
    }

    [Fact]
    public void ColumnReverse_OverflowIsReachableFromTheScrollOrigin()
    {
        var document = Parse("<div id=c style='display: flex; flex-direction: column-reverse; overflow: hidden; width: 100px; height: 50px'><div style='flex: none; height: 100px'></div></div>");
        var container = Get(document, "c");
        Assert.Equal(100, container.ScrollHeight);

        // The scroll origin is at the bottom: the overflow above it is reached with negative scroll positions
        container.ScrollTop = -1000;
        Assert.Equal(-50, container.ScrollTop);
    }

    [Theory]
    [InlineData("column")]
    [InlineData("row")]
    [InlineData("alternate")]
    public void NestedContainers_AreLaidOutInLinearTime(string direction)
    {
        // Each item of a column container is measured (its automatic block size) and laid out: the measures are cached for the
        // layout pass, so a deep nesting is not laid out an exponential number of times
        const int Depth = 40;
        var html = new System.Text.StringBuilder();
        for (var i = 0; i < Depth; i++)
        {
            var value = direction == "alternate" ? (i % 2 == 0 ? "column" : "row") : direction;
            html.Append("<div style='display: flex; flex-direction: ").Append(value).Append("; padding: 1px'><div>X</div>");
        }

        html.Append("<div id=t>X</div>");
        html.Insert(html.Length, "</div>", Depth);
        var document = Parse(html.ToString());
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var rect = GetRect(document, "t");
        Assert.InRange(stopwatch.Elapsed.TotalSeconds, 0, 5);
        Assert.Equal(10, rect.Height);
    }

    [Theory]
    [InlineData("width: 0; min-width: stretch")]
    [InlineData("width: 100px; max-width: stretch")]
    public void Stretch_MultiLine_ResolvesAgainstTheFlexLine(string sizing)
    {
        // WPT css/css-sizing/stretch/stretch-min-inline-size-001.html: the cross size of the line is the one of the larger sibling
        var document = Parse($"""
            <div style='display: inline-flex; flex-flow: column wrap; width: 50px; height: 40px'>
              <div id=a style='{sizing}; height: 20px; margin: 0 3px 0 2px'></div>
              <div style='width: 60px; height: 20px'></div>
            </div>
            """);
        Assert.Equal(55, GetRect(document, "a").Width);
    }

    [Fact]
    public void IntrinsicSize_StretchedItemTransfersTheDefiniteCrossSize()
    {
        // WPT css/css-flexbox/aspect-ratio-intrinsic-size-003.html, -008.html: the cross size of a stretched item of a single-line
        // container with a definite cross size is definite for the intrinsic sizes of the container
        var document = Parse("""
            <div id=a style='display: inline-flex; height: 100px'><div style='aspect-ratio: 1 / 1'></div></div>
            <div id=b style='display: inline-flex; height: 100px'><div style='aspect-ratio: 1 / 1; padding: 3px; border: 7px solid; margin: 10px'></div></div>
            <div id=c style='display: inline-flex; height: 100px; align-items: center'><div style='aspect-ratio: 1 / 1'></div></div>
            """);
        Assert.Equal(100, GetRect(document, "a").Width);
        Assert.Equal(100, GetRect(document, "b").Width);
        Assert.Equal(0, GetRect(document, "c").Width);
    }

    [Theory]
    [InlineData("flex: 1 0 100px", "", 100, 100)]
    [InlineData("flex: 0 1 100px; min-width: 0", "<div style='width: 200px'></div>", 100, 100)]
    [InlineData("flex: 0 0 200px; max-width: 100px; border: 10px solid; box-sizing: border-box", "", 100, 100)]
    [InlineData("flex: 1 1 0px", "<div style='width: 60px'></div>", 60, 60)]
    [InlineData("flex: 0 0 auto", "XX XXX", 30, 60)]
    public void IntrinsicSize_ItemContributionIsClampedByTheFlexBaseSize(string style, string content, double minContent, double maxContent)
    {
        // https://drafts.csswg.org/css-flexbox-1/#intrinsic-item-contributions: WPT css/css-flexbox/intrinsic-size/row-002.html, -004.html, -007.html
        var document = Parse($"""
            <div style='width: 0'><div id=min style='display: flex; float: left'><div style='{style}'>{content}</div></div></div>
            <div id=max style='display: flex; width: max-content; clear: both'><div style='{style}'>{content}</div></div>
            """);
        Assert.Equal(minContent, GetRect(document, "min").Width);
        Assert.Equal(maxContent, GetRect(document, "max").Width);
    }

    [Fact]
    public void IntrinsicSize_GrowingItemsUseTheLargestFlexFraction()
    {
        // https://drafts.csswg.org/css-flexbox-1/#intrinsic-main-sizes: each item grows from its flex base size by the largest
        // desired flex fraction, so that every item is at least as large as its max-content contribution
        var document = Parse("""
            <div id=a style='display: flex; width: max-content'>
              <div style='flex: 1 1 0px'><div style='width: 30px'></div></div>
              <div style='flex: 1 1 0px'><div style='width: 80px'></div></div>
            </div>
            <div id=b style='display: flex; width: max-content'>
              <div style='flex: 0 1 100px; width: 250px'></div>
              <div style='flex: 1 1 200px; width: 50px'><div style='width: 100px'></div></div>
            </div>
            """);
        Assert.Equal(160, GetRect(document, "a").Width);

        // The items shrink to their contributions: the container is as large as their sum
        Assert.Equal(200, GetRect(document, "b").Width);
    }

    [Fact]
    public void IntrinsicSize_ColumnWrap_BreaksTheLinesAtTheMaximumBlockSize()
    {
        // WPT css/css-flexbox/intrinsic-size/col-wrap-002.html
        var document = Parse("""
            <div id=a style='display: flex; flex-flow: column wrap; max-height: 100px; width: max-content'>
              <div style='flex: 0 0 100px; min-height: 0; width: 50px'></div>
              <div style='flex: 0 0 100px; min-height: 0; width: 50px'></div>
            </div>
            """);
        Assert.Equal(100, GetRect(document, "a").Width);
    }

    [Theory]
    [InlineData("flex-wrap: wrap-reverse; align-content: safe flex-start", 0, 0)]
    [InlineData("flex-flow: row-reverse; justify-content: safe flex-start", 0, 0)]
    [InlineData("flex-flow: row-reverse; justify-content: flex-start", -10, 0)]
    [InlineData("flex-flow: column-reverse; justify-content: safe center", 0, 0)]
    [InlineData("align-items: safe center", 0, 0)]
    [InlineData("align-items: center", 0, -5)]
    public void SafeAlignment_FallsBackToTheStartEdgeOfTheWritingMode(string style, double x, double y)
    {
        // WPT css/css-flexbox/flexbox-safe-overflow-position-002.html, -003.html, -004.html: "the alignment subject is instead
        // aligned as if the alignment mode were start", which is the flex-end edge of a reversed axis
        var document = Parse($"<div style='display: flex; width: 90px; height: 90px; {style}'><div id=a style='flex: 0 0 100px; width: 100px; height: 100px'></div></div>");
        Assert.Equal(Rect(x, y, 100, 100), GetRect(document, "a"));
    }

    [Fact]
    public void AbsolutelyPositionedChild_SafeAlignmentAtTheStaticPosition()
    {
        // WPT css/css-flexbox/abspos/flex-abspos-staticpos-align-self-safe-001.html: the box that overflows is start-aligned
        var document = Parse("""
            <div style='display: flex; position: relative; width: 50px; height: 50px'>
              <div id=a style='position: absolute; width: 70px; height: 70px; align-self: safe center'></div>
              <div id=b style='position: absolute; width: 30px; height: 30px; align-self: safe center'></div>
              <div id=c style='position: absolute; width: 70px; height: 70px; align-self: center'></div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 70, 70), GetRect(document, "a"));
        Assert.Equal(Rect(0, 10, 30, 30), GetRect(document, "b"));
        Assert.Equal(Rect(0, -10, 70, 70), GetRect(document, "c"));
    }

    [Fact]
    public void CollapsedItem_TakesNoGap()
    {
        // WPT css/css-flexbox/gap-collapse.html
        var document = Parse("""
            <div id=f style='display: flex; gap: 50px; width: max-content'>
              <div id=a style='width: 50px; height: 50px'></div>
              <div style='width: 50px; height: 50px; visibility: collapse'></div>
              <div id=b style='width: 50px; height: 50px'></div>
            </div>
            """);
        Assert.Equal(150, GetRect(document, "f").Width);
        Assert.Equal(100, GetRect(document, "b").X);
    }

    [Fact]
    public void BaselineOutsideOfTheItem_DoesNotEnlargeTheLine()
    {
        // WPT css/css-flexbox/baseline-outside-flex-item.html: the distance of the baseline to the cross-end edge is negative
        var document = Parse("""
            <div id=f style='display: flex; width: 100px; align-items: baseline'>
              <div id=a style='height: 100px; min-width: 100px'><div style='margin-top: 200px'>X</div></div>
            </div>
            """);
        Assert.Equal(100, GetRect(document, "f").Height);
        Assert.Equal(0, GetRect(document, "a").Y);
    }

    [Fact]
    public void TableItem_SizesApplyToTheGridBox()
    {
        // WPT css/css-flexbox/table-as-item-inflexible-in-column-2.html, table-as-item-stretch-cross-size-3.html: the captions are
        // added to the block sizes of the table; table-as-item-fixed-min-width-3.html, table-as-item-min-content-height-1.tentative.html:
        // a table is never smaller than its min-content size
        var document = Parse("""
            <div style='display: flex; flex-direction: column; width: 100px'>
              <div id=a style='display: table; flex: 0 0 50px'><div style='display: table-caption; height: 30px'></div></div>
              <div id=b style='display: table; height: 60px'><div style='display: table-caption; height: 10px'></div></div>
            </div>
            <div style='display: flex; width: 100px'>
              <div id=c style='display: table; min-width: 50px'><div style='display: table-cell'><div style='width: 100px; height: 10px'></div></div></div>
              <div style='flex-basis: 100px; min-width: 0'></div>
            </div>
            <div style='display: flex; flex-direction: column; width: 100px; height: 0'>
              <div id=d style='display: table; min-height: 0'><div style='display: table-cell'><div style='height: 50px'></div></div></div>
            </div>
            """);
        Assert.Equal(80, GetRect(document, "a").Height);
        Assert.Equal(70, GetRect(document, "b").Height);
        Assert.Equal(100, GetRect(document, "c").Width);
        Assert.Equal(50, GetRect(document, "d").Height);
    }

    [Fact]
    public void ReplacedItem_PercentageSizeIsASpecifiedSizeSuggestion()
    {
        // WPT css/css-flexbox/flex-aspect-ratio-img-row-017.html: the percentage of an image resolves against the definite main size
        // of the container for its automatic minimum size; flex-aspect-ratio-img-column-012.html: the minimum block size of an
        // image is not part of its content-based flex base size
        var document = Parse("""
            <div style='display: flex; width: 100px'><img id=a src='100x100.png' style='width: 100%; flex: 0 0 10px'></div>
            <div style='display: flex; flex-direction: column; width: 100px; height: 200px; align-items: flex-start'>
              <img id=b src='1x1.png' style='min-height: 100px; flex: 1 0 auto'>
              <div style='flex: 1 0 1px'></div>
            </div>
            """);
        Assert.Equal(100, GetRect(document, "a").Width);
        Assert.Equal((100d, 100d), GetSize(document, "b"));
    }

    [Fact]
    public void AutomaticMinimumSize_AspectRatio_ContentSizeSuggestionIsAtLeastTheContents()
    {
        // https://drafts.csswg.org/css-flexbox-1/#content-size-suggestion: the size transferred from the cross size (50px) does not
        // let a non-replaced item shrink below its contents (100px); an item without contents keeps the transferred size
        var document = Parse("""
            <div style='display: flex; width: 300px'><div id=a style='height: 100px; aspect-ratio: 1 / 2; flex-basis: 0'><div style='width: 100px'></div></div></div>
            <div style='display: flex; flex-direction: column; width: 300px'><div id=b style='width: 100px; aspect-ratio: 2 / 1; flex-basis: 0'><div style='height: 100px'></div></div></div>
            <div style='display: flex; width: 0; height: 100px'><div id=c style='aspect-ratio: 1'></div></div>
            """);
        Assert.Equal(100, GetRect(document, "a").Width);
        Assert.Equal(100, GetRect(document, "b").Height);
        Assert.Equal(100, GetRect(document, "c").Width);
    }

    [Fact]
    public void FlexBaseSize_TransferredFromADefiniteCrossSize_IsDefinite()
    {
        // The percentages of the contents of the item resolve against its main size, although the main size of the container is indefinite
        var document = Parse("<div style='display: flex; flex-direction: column; width: 100px'><div style='aspect-ratio: 1'><div id=a style='height: 100%'></div></div></div>");
        Assert.Equal(100, GetRect(document, "a").Height);
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("wrap balance")]
    public void Balance_MakesTheLinesAsSimilarAsPossible(string wrap)
    {
        // https://drafts.csswg.org/css-flexbox-2/#algo-balance: the greedy breaking gives two lines (three items, then one),
        // which are balanced as two lines of two items; the items are then flexed in their line
        var document = Parse($"""
            <div style='display: flex; flex-wrap: {wrap}; width: 100px; gap: 10px'>
              <div id=a style='width: 25px; height: 10px'></div><div id=b style='width: 25px; height: 10px; flex-grow: 1'></div>
              <div id=c style='width: 25px; height: 10px'></div><div id=d style='width: 25px; height: 10px'></div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 25, 10), GetRect(document, "a"));
        Assert.Equal(Rect(35, 0, 65, 10), GetRect(document, "b"));
        Assert.Equal(Rect(0, 20, 25, 10), GetRect(document, "c"));
        Assert.Equal(Rect(35, 20, 25, 10), GetRect(document, "d"));
    }

    [Fact]
    public void Balance_WrapReverse_StacksTheBalancedLinesFromTheCrossEnd()
    {
        var document = Parse("""
            <div style='display: flex; flex-wrap: balance wrap-reverse; width: 100px'>
              <div id=a style='width: 30px; height: 10px'></div><div style='width: 30px; height: 10px'></div>
              <div style='width: 30px; height: 10px'></div><div id=d style='width: 30px; height: 10px'></div>
            </div>
            """);
        Assert.Equal(Rect(0, 10, 30, 10), GetRect(document, "a"));
        Assert.Equal(Rect(30, 0, 30, 10), GetRect(document, "d"));
    }

    [Fact]
    public void Balance_EqualErrors_AssignTheMostItemsToTheFirstLines()
    {
        // "If multiple possible sequence assignments have equal minimum error, select the sequence that assigns the most items
        // to the first line": 3 lines for 5 items are 2, 2 and 1 items
        var document = Parse("""
            <div style='display: flex; flex-wrap: balance; flex-line-count: 3; width: 100px'>
              <div style='width: 20px; height: 10px'></div><div id=b style='width: 20px; height: 10px'></div>
              <div id=c style='width: 20px; height: 10px'></div><div id=d style='width: 20px; height: 10px'></div>
              <div id=e style='width: 20px; height: 10px'></div>
            </div>
            """);
        Assert.Equal(Rect(20, 0, 20, 10), GetRect(document, "b"));
        Assert.Equal(Rect(0, 10, 20, 10), GetRect(document, "c"));
        Assert.Equal(Rect(20, 10, 20, 10), GetRect(document, "d"));
        Assert.Equal(Rect(0, 20, 20, 10), GetRect(document, "e"));
    }

    [Theory]
    [InlineData("flex-wrap: balance; flex-line-count: 10", 20)] // limited by the number of items
    [InlineData("flex-wrap: balance; flex-line-count: 2", 10)]
    [InlineData("flex-wrap: balance", 0)]
    [InlineData("flex-wrap: wrap; flex-line-count: 3", 0)] // only a balanced container has a minimum number of lines
    [InlineData("flex-line-count: 3", 0)]
    [InlineData("display: -webkit-box; flex-wrap: balance; flex-line-count: 3", 0)] // a -webkit-box is single-line
    public void Balance_FlexLineCount_IsTheMinimumNumberOfLines(string style, double lastItemY)
    {
        var document = Parse($"""
            <div style='display: flex; width: 100px; {style}'>
              <div style='width: 20px; height: 10px'></div><div style='width: 20px; height: 10px'></div><div id=c style='width: 20px; height: 10px'></div>
            </div>
            """);
        Assert.Equal(lastItemY, GetRect(document, "c").Y);
    }

    [Fact]
    public void Balance_NegativeMargins_AreFlooredAtZeroToBreakTheLinesOnly()
    {
        // https://drafts.csswg.org/css-flexbox-2/#algo-line-break: "floor the outer hypothetical main size of each flex item at
        // zero": the second item does not fit next to the first one, although its negative margins would make room for it; its
        // line is then flexed with its margins (WPT css/css-flexbox/balance/balance-negative-margin-001.html)
        var document = Parse("""
            <div style='display: flex; flex-wrap: balance; width: 100px'>
              <div id=a style='width: 150px; height: 10px; flex-shrink: 0'></div>
              <div id=b style='width: 0; height: 10px; margin: 0 -50px; flex-grow: 1'></div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 150, 10), GetRect(document, "a"));
        Assert.Equal(Rect(-50, 10, 200, 10), GetRect(document, "b"));
    }

    [Fact]
    public void Balance_ColumnWithAnIndefiniteBlockSize_HasFlexLineCountLines()
    {
        // The lines of a column container whose block size is indefinite are not limited: flex-line-count gives their number,
        // the container is as tall as its largest line and its max-content inline size is the sum of its lines and of the gaps
        var document = Parse("""
            <div id=f style='display: inline-flex; flex-flow: column balance; flex-line-count: 2; gap: 20px'>
              <div style='width: 40px; height: 100px'></div><div style='width: 40px; height: 30px'></div><div id=c style='width: 40px; height: 30px'></div>
            </div>
            """);
        Assert.Equal((100d, 100d), GetSize(document, "f"));
        Assert.Equal(Rect(60, 50, 40, 30), GetRect(document, "c"));
    }

    [Fact]
    public void Balance_RowMaxContentSize_IsTheLargestBalancedLine()
    {
        // WPT css/css-flexbox/balance/balance-line-count-intrinsic-004.html
        var document = Parse("""
            <div id=f style='display: inline-flex; flex-wrap: balance; flex-line-count: 2; gap: 20px'>
              <div style='width: 80px; height: 10px'></div><div style='width: 40px; height: 10px'></div><div style='width: 40px; height: 10px'></div>
            </div>
            <div id=g style='display: inline-flex; flex-wrap: wrap; flex-line-count: 2; gap: 20px'>
              <div style='width: 80px; height: 10px'></div><div style='width: 40px; height: 10px'></div><div style='width: 40px; height: 10px'></div>
            </div>
            """);
        Assert.Equal((100d, 40d), GetSize(document, "f"));
        Assert.Equal((200d, 10d), GetSize(document, "g"));
    }

    [Theory]
    [InlineData("flex-flow: column balance", "width: auto", 100)]
    [InlineData("flex-flow: column wrap", "width: auto", 100)] // "the only effect flex-line-count has on non-balance flexboxes"
    [InlineData("flex-flow: column wrap", "width: stretch", 100)]
    [InlineData("flex-flow: column wrap", "width: 50%", 110)] // "Percentages continue to resolve as normal"
    [InlineData("flex-flow: column", "width: auto", 200)] // no effect on a single-line container
    public void FlexLineCount_DividesTheAvailableCrossSpaceOfTheItems(string flow, string itemSize, double expectedWidth)
    {
        // https://drafts.csswg.org/css-flexbox-2/#flex-line-count-property: "subtracting the size of N-1 gaps from the normal
        // available space, then dividing the leftover by N": (220px - 20px) / 2 (the line is not stretched: a cross size of
        // stretch fills its line)
        var document = Parse($"""
            <div style='display: flex; {flow}; flex-line-count: 2; gap: 20px; width: 220px; align-items: start; align-content: start'>
              <div id=a style='{itemSize}'><span style='display: inline-block; width: 100px; height: 10px'></span><span style='display: inline-block; width: 100px; height: 10px'></span></div>
            </div>
            """);
        Assert.Equal(expectedWidth, GetRect(document, "a").Width);
    }

    [Fact]
    public void Balance_DynamicChanges_BalanceTheLinesAgain()
    {
        var document = Parse("""
            <div id=f style='display: flex; flex-wrap: wrap; width: 150px'>
              <div style='width: 50px; height: 10px'></div><div style='width: 50px; height: 10px'></div><div id=c style='width: 50px; height: 10px'></div><div style='width: 50px; height: 10px'></div>
            </div>
            """);
        Assert.Equal(Rect(100, 0, 50, 10), GetRect(document, "c"));

        Get(document, "f").SetAttribute("style", "display: flex; flex-wrap: balance; width: 150px");
        Assert.Equal(Rect(0, 10, 50, 10), GetRect(document, "c"));

        Get(document, "f").SetAttribute("style", "display: flex; flex-wrap: balance; flex-line-count: 4; width: 150px");
        Assert.Equal(Rect(0, 20, 50, 10), GetRect(document, "c"));
    }

    [Fact]
    public void MaxHeightMaxContent_TheLinesAreNotStretchedToTheSpecifiedHeight()
    {
        // The definite height (200px) is limited by max-height: max-content (the automatic height, 20px): the contents are laid
        // out in the limited size (https://github.com/w3c/csswg-drafts/issues/12123)
        var document = Parse("""
            <div id=f style='display: flex; flex-wrap: wrap; height: 200px; max-height: max-content'>
              <div id=a style='width: 20px'>X X</div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 800, 20), GetRect(document, "f"));
        Assert.Equal(Rect(0, 0, 20, 20), GetRect(document, "a"));
    }

    [Fact]
    public void StretchMaxCrossSize_ResolvesAgainstTheLineOfAnIndefiniteContainer()
    {
        // The cross size of the container is indefinite: max-height: stretch behaves as none for the hypothetical cross size
        // (500px), then resolves against the line, which the max-height of the single-line container clamps to 100px
        var document = Parse("""
            <div style='display: flex; max-height: 100px'>
              <div id=a style='flex: none; width: 10px; height: 500px; max-height: stretch'></div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 10, 100), GetRect(document, "a"));
    }
}
