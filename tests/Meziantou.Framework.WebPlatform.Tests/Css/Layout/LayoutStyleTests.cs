using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

public sealed class LayoutStyleTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html) => _contexts.Create(new BrowsingContextOptions { Viewport = new ViewportOptions { Width = 800, Height = 600 } }).Navigate(_ => html);

    private LayoutStyle GetLayout(string html, string selector = "#target", string? pseudoElement = null)
    {
        var document = Parse(html);
        return document.GetStyleEngine().GetStyle(document.QuerySelector(selector)!, pseudoElement).Layout;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // <length-percentage>
    // ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("10px", 1, 200, 10, false)]
    [InlineData("10px", 2, 200, 20, false)]
    [InlineData("0", 1, 200, 0, false)]
    [InlineData("-5.5px", 1, 200, -5.5, false)]
    [InlineData("50%", 1, 200, 100, true)]
    [InlineData("50%", 2, 200, 100, true)]
    [InlineData("0%", 1, 200, 0, true)]
    [InlineData("calc(50% + 10px)", 1, 200, 110, true)]
    [InlineData("calc(50% + 10px)", 3, 200, 130, true)]
    [InlineData("calc(100% - 16px)", 1, 50, 34, true)]
    [InlineData("min(10px, 50%)", 1, 10, 5, true)]
    [InlineData("min(10px, 50%)", 1, 100, 10, true)]
    [InlineData("min(10px, 50%)", 2, 100, 20, true)]
    [InlineData("min(10px, 50%)", 2, 10, 5, true)]
    [InlineData("clamp(10px, 50%, 30px)", 1, 100, 30, true)]
    [InlineData("max(10px, calc(20% + 5px))", 1, 100, 25, true)]
    public void LengthPercentage_Parse(string value, double zoom, double basis, double expected, bool hasPercentage)
    {
        Assert.True(LengthPercentage.TryParse(value, zoom, nonNegative: false, out var length));
        Assert.Equal(expected, length.Resolve(basis), 6);
        Assert.Equal(hasPercentage, length.HasPercentage);
        Assert.Equal(!hasPercentage, length.IsFixed);
        Assert.Equal(hasPercentage ? null : expected, length.TryResolve(null));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("none")]
    [InlineData("")]
    [InlineData("anchor-size(width)")]
    [InlineData("10deg")]
    public void LengthPercentage_Parse_Invalid(string value) => Assert.False(LengthPercentage.TryParse(value, 1, nonNegative: false, out _));

    [Fact]
    public void LengthPercentage_Linear()
    {
        Assert.True(LengthPercentage.TryParse("calc(25% + 4px)", 1, nonNegative: false, out var length));
        Assert.Null(length.Calc);
        Assert.Equal(4, length.Length);
        Assert.Equal(25, length.Percentage);
    }

    // https://drafts.csswg.org/css-values-4/#calc-range: the non-negative properties clamp their math functions at used time
    [Theory]
    [InlineData("calc(10px - 50%)", 100, 0)]
    [InlineData("calc(10px - 50%)", 10, 5)]
    [InlineData("calc(50% + 10px)", 100, 60)]
    public void LengthPercentage_NonNegative(string value, double basis, double expected)
    {
        Assert.True(LengthPercentage.TryParse(value, 1, nonNegative: true, out var length));
        Assert.Equal(expected, length.Resolve(basis));
    }

    [Fact]
    public void LengthPercentage_Default_IsZeroPixels()
    {
        var length = default(LengthPercentage);
        Assert.True(length.IsZero);
        Assert.False(length.HasPercentage);
        Assert.Equal(0, length.Resolve(100));
        Assert.Equal(LengthPercentage.Zero, LengthPercentage.FromPixels(0));
        Assert.NotEqual(LengthPercentage.Zero, LengthPercentage.FromPercentage(0));
    }

    // https://drafts.csswg.org/css-values-4/#calc-ieee: infinity is clamped to the largest length
    [Fact]
    public void LengthPercentage_Infinity()
    {
        Assert.True(LengthPercentage.TryParse("calc(infinity * 1px)", 1, nonNegative: false, out var length));
        Assert.Equal(CalcLength.MaxLength, length.Resolve(0));
    }

    [Theory]
    [InlineData("auto", SizeKind.Auto)]
    [InlineData("none", SizeKind.None)]
    [InlineData("min-content", SizeKind.MinContent)]
    [InlineData("max-content", SizeKind.MaxContent)]
    [InlineData("fit-content", SizeKind.FitContent)]
    [InlineData("fit-content(50%)", SizeKind.FitContentFunction)]
    [InlineData("stretch", SizeKind.Stretch)]
    [InlineData("contain", SizeKind.Contain)]
    [InlineData("content", SizeKind.Content)]
    [InlineData("100px", SizeKind.LengthPercentage)]
    [InlineData("calc(100% - 10px)", SizeKind.LengthPercentage)]
    [InlineData("calc-size(auto, size + 10px)", SizeKind.CalcSize)]
    [InlineData("anchor-size(width)", SizeKind.Auto)]
    public void Size_Parse(string value, object expected) => Assert.Equal((SizeKind)expected, Size.Parse(value, 1).Kind);

    [Fact]
    public void Size_FitContentFunction()
    {
        var size = Size.Parse("fit-content(50%)", 1);
        Assert.Equal(100, size.Value.Resolve(200));
        Assert.Null(size.TryResolve(200));
        Assert.Equal(SizeKind.None, Size.Parse("anchor-size(width)", 1, SizeKind.None).Kind);
        Assert.NotNull(Size.Parse("calc-size(auto, size + 10px)", 1).CalcSizeFunction);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Keywords
    // ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("none", DisplayBox.None, DisplayOutside.Inline, DisplayInside.Flow, DisplayInternal.None, false)]
    [InlineData("contents", DisplayBox.Contents, DisplayOutside.Inline, DisplayInside.Flow, DisplayInternal.None, false)]
    [InlineData("block", DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Flow, DisplayInternal.None, false)]
    [InlineData("inline", DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Flow, DisplayInternal.None, false)]
    [InlineData("inline-block", DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.FlowRoot, DisplayInternal.None, false)]
    [InlineData("flow-root", DisplayBox.Normal, DisplayOutside.Block, DisplayInside.FlowRoot, DisplayInternal.None, false)]
    [InlineData("list-item", DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Flow, DisplayInternal.None, true)]
    [InlineData("inline flow-root list-item", DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.FlowRoot, DisplayInternal.None, true)]
    [InlineData("flex", DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Flex, DisplayInternal.None, false)]
    [InlineData("inline-grid", DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Grid, DisplayInternal.None, false)]
    [InlineData("inline-table", DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Table, DisplayInternal.None, false)]
    [InlineData("block ruby", DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Ruby, DisplayInternal.None, false)]
    [InlineData("ruby", DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Ruby, DisplayInternal.None, false)]
    [InlineData("math", DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Math, DisplayInternal.None, false)]
    [InlineData("run-in", DisplayBox.Normal, DisplayOutside.RunIn, DisplayInside.Flow, DisplayInternal.None, false)]
    [InlineData("-webkit-box", DisplayBox.Normal, DisplayOutside.Block, DisplayInside.WebkitBox, DisplayInternal.None, false)]
    [InlineData("table-cell", DisplayBox.Normal, DisplayOutside.Block, DisplayInside.FlowRoot, DisplayInternal.TableCell, false)]
    [InlineData("table-row", DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Flow, DisplayInternal.TableRow, false)]
    [InlineData("ruby-text", DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Flow, DisplayInternal.RubyText, false)]
    public void Display_Parse(string value, object box, object outside, object inside, object @internal, bool listItem)
        => Assert.Equal(new Display((DisplayBox)box, (DisplayOutside)outside, (DisplayInside)inside, (DisplayInternal)@internal, listItem), Display.Parse(value));

    [Theory]
    [InlineData("block", true, false, false, true)]
    [InlineData("inline", false, true, false, false)]
    [InlineData("inline-block", false, false, true, true)]
    [InlineData("inline-flex", false, false, true, false)]
    [InlineData("table-cell", false, false, false, true)]
    [InlineData("table-caption", true, false, false, true)]
    [InlineData("table-row", false, false, false, false)]
    [InlineData("list-item", true, false, false, true)]
    [InlineData("flex", true, false, false, false)]
    public void Display_Classification(string value, bool blockLevel, bool inlineBox, bool atomicInline, bool blockContainer)
    {
        var display = Display.Parse(value);
        Assert.Equal(blockLevel, display.IsBlockLevel);
        Assert.Equal(inlineBox, display.IsInlineBox);
        Assert.Equal(atomicInline, display.IsAtomicInline);
        Assert.Equal(blockContainer, display.IsBlockContainer);
    }

    // https://drafts.csswg.org/css-display/#transformations: the computed display is blockified
    [Fact]
    public void ComputedStyle_DisplayAndPosition()
    {
        var layout = GetLayout("<div id=target style='display: inline-flex; position: absolute; float: left; clear: both; box-sizing: border-box'></div>");
        Assert.Equal(Display.Parse("flex"), layout.Display);
        Assert.Equal(PositionKind.Absolute, layout.Position);
        Assert.True(layout.IsAbsolutelyPositioned);
        Assert.True(layout.IsOutOfFlow);

        // The float of an absolutely positioned box computes to none
        Assert.Equal(FloatKind.None, layout.Float);
        Assert.Equal(ClearKind.Both, layout.Clear);
        Assert.Equal(BoxSizing.BorderBox, layout.BoxSizing);
    }

    [Fact]
    public void ComputedStyle_LayoutIsCached()
    {
        var document = Parse("<div id=target></div>");
        var style = document.GetStyleEngine().GetStyle(document.QuerySelector("#target")!);
        Assert.Same(style.Layout, style.Layout);
        Assert.Same(style.Layout.Box, style.Layout.Box);
        Assert.Same(style.Layout.Text, style.Layout.Text);
    }

    [Fact]
    public void ComputedStyle_WritingModes()
    {
        var layout = GetLayout("<div dir=rtl style='writing-mode: vertical-rl'><span id=target style='text-orientation: sideways; unicode-bidi: isolate'></span></div>");
        Assert.Equal(WritingMode.VerticalRl, layout.WritingMode);
        Assert.True(layout.IsVerticalWritingMode);
        Assert.Equal(Direction.Rtl, layout.Direction);
        Assert.Equal(TextOrientation.Sideways, layout.TextOrientation);
        Assert.Equal(UnicodeBidi.Isolate, layout.UnicodeBidi);

        // The used direction of the upright vertical text is ltr; text-orientation does not apply to the sideways writing modes
        layout = GetLayout("<div dir=rtl style='writing-mode: vertical-rl'><span id=target style='text-orientation: upright'></span></div>");
        Assert.Equal(TextOrientation.Upright, layout.TextOrientation);
        Assert.Equal(Direction.Ltr, layout.Direction);
        layout = GetLayout("<div dir=rtl style='writing-mode: sideways-rl'><span id=target style='text-orientation: upright'></span></div>");
        Assert.Equal(Direction.Rtl, layout.Direction);
    }

    // https://drafts.csswg.org/css-overflow-3/#overflow-control: visible and clip become auto and hidden when the other axis scrolls
    [Theory]
    [InlineData("overflow: visible", OverflowKind.Visible, OverflowKind.Visible, false)]
    [InlineData("overflow: hidden", OverflowKind.Hidden, OverflowKind.Hidden, true)]
    [InlineData("overflow-x: scroll", OverflowKind.Scroll, OverflowKind.Auto, true)]
    [InlineData("overflow-x: clip; overflow-y: auto", OverflowKind.Clip, OverflowKind.Auto, true)]
    [InlineData("overflow-x: clip", OverflowKind.Clip, OverflowKind.Visible, false)]
    public void ComputedStyle_Overflow(string declarations, object x, object y, bool scrollContainer)
    {
        var layout = GetLayout($"<div id=target style='{declarations}'></div>");
        Assert.Equal((OverflowKind)x, layout.OverflowX);
        Assert.Equal((OverflowKind)y, layout.OverflowY);
        Assert.Equal(scrollContainer, layout.IsScrollContainer);
    }

    // https://drafts.csswg.org/css-position-3/#fixed-cb
    [Theory]
    [InlineData("", false, false)]
    [InlineData("position: relative", false, true)]
    [InlineData("transform: translateX(1px)", true, true)]
    [InlineData("filter: blur(1px)", true, true)]
    [InlineData("contain: paint", true, true)]
    [InlineData("container-type: inline-size", false, false)] // no layout containment (csswg-drafts#10544)
    [InlineData("content-visibility: auto", true, true)]
    [InlineData("transform-style: preserve-3d", true, true)]
    [InlineData("will-change: transform", true, true)]
    [InlineData("will-change: position", false, true)]
    public void ComputedStyle_ContainingBlocks(string declarations, bool fixedContainingBlock, bool absoluteContainingBlock)
    {
        var layout = GetLayout($"<div><div id=target style='{declarations}'></div></div>");
        Assert.Equal(fixedContainingBlock, layout.EstablishesFixedPositionContainingBlock);
        Assert.Equal(absoluteContainingBlock, layout.EstablishesAbsolutePositionContainingBlock);
    }

    [Theory]
    [InlineData("contain: strict", Containment.Size | Containment.Layout | Containment.Paint | Containment.Style)]
    [InlineData("contain: content", Containment.Layout | Containment.Paint | Containment.Style)]
    [InlineData("contain: inline-size layout", Containment.InlineSize | Containment.Layout)]
    [InlineData("contain: none", Containment.None)]
    public void ComputedStyle_Contain(string declarations, object expected) => Assert.Equal((Containment)expected, GetLayout($"<div id=target style='{declarations}'></div>").Contain);

    [Fact]
    public void ComputedStyle_SizeContainment()
    {
        var layout = GetLayout("<div id=target style='container-type: inline-size'></div>");
        Assert.True(layout.HasSizeContainment(inlineAxis: true));
        Assert.False(layout.HasSizeContainment(inlineAxis: false));
        Assert.False(layout.HasLayoutContainment);
        Assert.True(layout.HasStyleContainment);
    }

    [Fact]
    public void ComputedStyle_TextKeywords()
    {
        var layout = GetLayout("<div id=target style='white-space: pre; text-transform: uppercase full-width; visibility: collapse; content-visibility: hidden'></div>");
        Assert.Equal(WhiteSpaceCollapse.Preserve, layout.WhiteSpaceCollapse);
        Assert.Equal(TextWrapMode.NoWrap, layout.TextWrapMode);
        Assert.Equal(TextTransform.Uppercase | TextTransform.FullWidth, layout.TextTransform);
        Assert.Equal(Visibility.Collapse, layout.Visibility);
        Assert.Equal(ContentVisibility.Hidden, layout.ContentVisibility);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Box model
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Box_SizesMarginsPaddingsBorders()
    {
        var layout = GetLayout("<div id=target style='font-size: 10px; width: 50%; height: 2em; min-height: min-content; max-width: calc(100% - 20px); margin: auto 5% 3px; padding: 1em 10%; border: 3px solid; border-left-style: none; aspect-ratio: 16 / 9'></div>");
        var box = layout.Box;
        Assert.Equal(SizeKind.LengthPercentage, box.Width.Kind);
        Assert.Equal(100, box.Width.TryResolve(200));
        Assert.Equal(20, box.Height.TryResolve(null));
        Assert.Equal(SizeKind.Auto, box.MinWidth.Kind);
        Assert.Equal(SizeKind.MinContent, box.MinHeight.Kind);
        Assert.Equal(180, box.MaxWidth.TryResolve(200));
        Assert.Equal(SizeKind.None, box.MaxHeight.Kind);
        Assert.True(box.Margin.Top.IsAuto);
        Assert.Equal(10, box.Margin.Right.Resolve(200));
        Assert.Equal(3, box.Margin.Bottom.Resolve(200));
        Assert.Equal(10, box.Margin.Left.Resolve(200));
        Assert.Equal(10, box.Padding.Top.Resolve(200));
        Assert.Equal(20, box.Padding.Right.Resolve(200));
        Assert.Equal(new Sides<double>(3, 3, 3, 0), box.BorderWidth);
        Assert.Equal(BorderStyle.None, box.BorderStyle.Left);
        Assert.Equal(16.0 / 9, box.AspectRatio.Ratio);
        Assert.Equal(3 + 20 + 20 + 0, box.GetPaddingAndBorder(horizontal: true, 200));
        Assert.Null(box.GetPaddingAndBorder(horizontal: true, null));
    }

    // https://drafts.csswg.org/css-viewport/#zoom-property: the used lengths are multiplied by the effective zoom, the percentages are not
    [Fact]
    public void Zoom_UsedValues()
    {
        var layout = GetLayout("<div style='zoom: 2'><div id=target style='zoom: 150%; font-size: 10px; line-height: 20px; width: calc(10px + 10%); border: 1px solid; margin-top: 5px; letter-spacing: 1px'></div></div>");
        Assert.Equal(1.5, layout.Zoom);
        Assert.Equal(3, layout.EffectiveZoom);
        Assert.Equal(30, layout.FontSize);
        Assert.Equal(new LineHeight(LineHeightKind.Length, 60), layout.LineHeight);
        Assert.Equal(60, layout.UsedLineHeight);
        Assert.Equal(30 + 20, layout.Box.Width.TryResolve(200));
        Assert.Equal(3, layout.Box.BorderWidth.Top);
        Assert.Equal(15, layout.Box.Margin.Top.Resolve(0));
        Assert.Equal(3, layout.Text.LetterSpacing);
    }

    [Fact]
    public void Zoom_PseudoElements()
    {
        var document = Parse("<style>#target::before { content: 'a'; zoom: 2 } #target::selection { color: red }</style><div style='zoom: 3'><div id=target></div></div>");
        var engine = document.GetStyleEngine();
        var element = document.QuerySelector("#target")!;
        Assert.Equal(6, engine.GetStyle(element, "before").Layout.EffectiveZoom);
        Assert.Equal(3, engine.GetStyle(element, "selection").Layout.EffectiveZoom);
    }

    [Theory]
    [InlineData("normal", LineHeightKind.Normal, 20)] // Ahem: ascent 0.8em + descent 0.2em, no line gap
    [InlineData("1.5", LineHeightKind.Number, 30)]
    [InlineData("150%", LineHeightKind.Length, 30)]
    [InlineData("2em", LineHeightKind.Length, 40)]
    public void LineHeight(string value, object kind, double used)
    {
        var layout = GetLayout($"<div id=target style='font: 20px Ahem; line-height: {value}'></div>");
        Assert.Equal((LineHeightKind)kind, layout.LineHeight.Kind);
        Assert.Equal(used, layout.UsedLineHeight, 6);
    }

    [Fact]
    public void Insets()
    {
        var layout = GetLayout("<div id=target style='position: relative; inset: 10px auto 20% calc(50% - 5px); z-index: -3'></div>");
        Assert.Equal(10, layout.Insets.Inset.Top.Resolve(0));
        Assert.True(layout.Insets.Inset.Right.IsAuto);
        Assert.Equal(40, layout.Insets.Inset.Bottom.Resolve(200));
        Assert.Equal(95, layout.Insets.Inset.Left.Resolve(200));
        Assert.Equal(-3, layout.Insets.ZIndex);
        Assert.Null(GetLayout("<div id=target></div>").Insets.ZIndex);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Groups
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Text()
    {
        var text = GetLayout("<div id=target style='font-size: 20px; text-align: right; text-align-last: justify; text-indent: 10% hanging; letter-spacing: 10%; word-spacing: normal; vertical-align: super; tab-size: 4; hyphens: auto; word-break: break-all; overflow-wrap: anywhere; line-break: strict; text-wrap-style: balance; list-style-position: inside; hyphenate-character: \"=\"'></div>").Text;
        Assert.Equal(TextAlign.Right, text.TextAlign);
        Assert.Equal(TextAlign.Justify, text.TextAlignLast);
        Assert.Equal(20, text.TextIndent.Resolve(200));
        Assert.True(text.TextIndentHanging);
        Assert.False(text.TextIndentEachLine);
        Assert.Equal(2, text.LetterSpacing, 6);
        Assert.Equal(0, text.WordSpacing);
        Assert.Equal(BaselineShiftKind.Super, text.VerticalAlign.BaselineShift.Kind);
        Assert.Equal(new TabSize(4, IsLength: false), text.TabSize);
        Assert.Equal(Hyphens.Auto, text.Hyphens);
        Assert.Equal(WordBreak.BreakAll, text.WordBreak);
        Assert.Equal(OverflowWrap.Anywhere, text.OverflowWrap);
        Assert.Equal(LineBreak.Strict, text.LineBreak);
        Assert.Equal(TextWrapStyle.Balance, text.TextWrapStyle);
        Assert.Equal(ListStylePosition.Inside, text.ListStylePosition);
        Assert.Equal("=", text.HyphenateCharacter);
    }

    [Theory]
    [InlineData("grow", TextFitMode.Grow, TextFitTarget.Consistent, double.NaN)]
    [InlineData("shrink per-line", TextFitMode.Shrink, TextFitTarget.PerLine, double.NaN)]
    [InlineData("grow per-line-all 250%", TextFitMode.Grow, TextFitTarget.PerLineAll, 2.5)]
    [InlineData("shrink consistent 75%", TextFitMode.Shrink, TextFitTarget.Consistent, 0.75)]
    internal void TextFit(string value, TextFitMode mode, TextFitTarget target, double limit)
    {
        var fit = GetLayout($"<div id=target style='text-fit: {value}'></div>").Text.TextFit;
        Assert.Equal(mode, fit.Mode);
        Assert.Equal(target, fit.Target);
        Assert.Equal(limit, fit.Limit);
        Assert.Equal(TextFitMode.None, GetLayout("<div id=target style='text-fit: none'></div>").Text.TextFit.Mode);
    }

    [Fact]
    public void TextFit_ClampsTheScalingFactor()
    {
        // grow does not scale down and shrink does not scale up; the percentage limits the factor in the direction of the scaling
        Assert.Equal(3, new TextFit(TextFitMode.Grow, TextFitTarget.Consistent, double.NaN).Clamp(3));
        Assert.Equal(1, new TextFit(TextFitMode.Grow, TextFitTarget.Consistent, double.NaN).Clamp(0.5));
        Assert.Equal(2, new TextFit(TextFitMode.Grow, TextFitTarget.Consistent, 2).Clamp(3));
        Assert.Equal(3, new TextFit(TextFitMode.Grow, TextFitTarget.Consistent, 0.5).Clamp(3)); // not a limit of grow
        Assert.Equal(0.25, new TextFit(TextFitMode.Shrink, TextFitTarget.Consistent, double.NaN).Clamp(0.25));
        Assert.Equal(1, new TextFit(TextFitMode.Shrink, TextFitTarget.Consistent, double.NaN).Clamp(3));
        Assert.Equal(0.75, new TextFit(TextFitMode.Shrink, TextFitTarget.Consistent, 0.75).Clamp(0.25));
        Assert.Equal(0.25, new TextFit(TextFitMode.Shrink, TextFitTarget.Consistent, 1.5).Clamp(0.25)); // not a limit of shrink
    }

    [Theory]
    [InlineData("baseline", AlignmentBaseline.Baseline, BaselineShiftKind.Length, 0)]
    [InlineData("middle", AlignmentBaseline.Middle, BaselineShiftKind.Length, 0)]
    [InlineData("text-top", AlignmentBaseline.TextTop, BaselineShiftKind.Length, 0)]
    [InlineData("sub", AlignmentBaseline.Baseline, BaselineShiftKind.Sub, 0)]
    [InlineData("top", AlignmentBaseline.Baseline, BaselineShiftKind.Top, 0)]
    [InlineData("5px", AlignmentBaseline.Baseline, BaselineShiftKind.Length, 5)]
    [InlineData("50%", AlignmentBaseline.Baseline, BaselineShiftKind.Length, 50)]
    public void VerticalAlign(string value, object baseline, object shift, double length)
    {
        var verticalAlign = GetLayout($"<span id=target style='vertical-align: {value}'></span>").Text.VerticalAlign;
        Assert.Equal((AlignmentBaseline)baseline, verticalAlign.AlignmentBaseline);
        Assert.Equal((BaselineShiftKind)shift, verticalAlign.BaselineShift.Kind);

        // The percentages refer to the line height
        Assert.Equal(length, verticalAlign.BaselineShift.Length.Resolve(100));
    }

    [Fact]
    public void Font()
    {
        var font = GetLayout("<div id=target style='font-family: \"Times New Roman\", Open Sans, serif; font-weight: bold; font-style: oblique 10deg; font-stretch: condensed; font-kerning: none; font-synthesis: weight'></div>").Font;
        Assert.Equal([new FontFamily("Times New Roman", IsGeneric: false), new FontFamily("Open Sans", IsGeneric: false), new FontFamily("serif", IsGeneric: true)], font.Families);
        Assert.Equal(700, font.Weight);
        Assert.Equal(new FontStyle(FontStyleKind.Oblique, 10), font.Style);
        Assert.Equal(75, font.Width);
        Assert.Equal(FontKerning.None, font.Kerning);
        Assert.Equal(FontSynthesis.Weight, font.Synthesis);
    }

    [Fact]
    public void Flex()
    {
        var document = Parse("<div id=container style='display: flex; flex-flow: column-reverse wrap; gap: 10px 5%'><div id=target style='flex: 2 3 0%; order: -1'></div></div>");
        var engine = document.GetStyleEngine();
        var container = engine.GetStyle(document.QuerySelector("#container")!).Layout;
        Assert.True(container.Display.IsFlexContainer);
        Assert.Equal(FlexDirection.ColumnReverse, container.Flex.Direction);
        Assert.True(container.Flex.IsReverse);
        Assert.False(container.Flex.IsRow);
        Assert.Equal(FlexWrap.Wrap, container.Flex.Wrap);
        Assert.Equal(10, container.Alignment.RowGap.Resolve(200));
        Assert.Equal(10, container.Alignment.ColumnGap.Resolve(200));

        var item = engine.GetStyle(document.QuerySelector("#target")!).Layout;
        Assert.Equal(2, item.Flex.Grow);
        Assert.Equal(3, item.Flex.Shrink);
        Assert.Equal(0, item.Flex.Basis.TryResolve(100));
        Assert.True(item.Flex.Basis.Value.HasPercentage);
        Assert.Equal(-1, item.Flex.Order);
        Assert.True(item.Alignment.RowGap.IsNormal);
        Assert.Equal(SizeKind.Content, GetLayout("<div id=target style='flex-basis: content'></div>").Flex.Basis.Kind);
    }

    [Theory]
    [InlineData("align-items: safe center", AlignmentKeyword.Center, OverflowAlignment.Safe, false)]
    [InlineData("align-items: last baseline", AlignmentKeyword.LastBaseline, OverflowAlignment.Default, false)]
    [InlineData("align-items: first baseline", AlignmentKeyword.Baseline, OverflowAlignment.Default, false)]
    [InlineData("align-items: unsafe flex-end", AlignmentKeyword.FlexEnd, OverflowAlignment.Unsafe, false)]
    [InlineData("align-items: normal", AlignmentKeyword.Normal, OverflowAlignment.Default, false)]
    public void Alignment(string declarations, object keyword, object overflow, bool legacy)
        => Assert.Equal(new Alignment((AlignmentKeyword)keyword, (OverflowAlignment)overflow, legacy), GetLayout($"<div id=target style='{declarations}'></div>").Alignment.AlignItems);

    [Fact]
    public void Alignment_Legacy()
    {
        var alignment = GetLayout("<div id=target style='justify-items: legacy right; justify-content: space-between; align-self: self-start'></div>").Alignment;
        Assert.Equal(new Alignment(AlignmentKeyword.Right, OverflowAlignment.Default, IsLegacy: true), alignment.JustifyItems);
        Assert.Equal(AlignmentKeyword.SpaceBetween, alignment.JustifyContent.Keyword);
        Assert.True(alignment.JustifyContent.IsDistribution);
        Assert.Equal(AlignmentKeyword.SelfStart, alignment.AlignSelf.Keyword);
        Assert.Equal(AlignmentKeyword.Auto, alignment.JustifySelf.Keyword);
    }

    [Fact]
    public void Grid_TrackList()
    {
        var grid = GetLayout("<div id=target style='display: grid; grid-template-columns: [a] 100px repeat(2, [b] 1fr minmax(10px, auto)) [c] fit-content(50%) [d]; grid-template-rows: repeat(auto-fill, 20%); grid-auto-columns: min-content 2fr; grid-auto-flow: column dense'></div>").Grid;
        var columns = grid.TemplateColumns;
        Assert.False(columns.IsNone);
        Assert.Equal(3, columns.Segments.Length);

        var first = columns.Segments[0];
        Assert.Equal(GridRepeatKind.None, first.Repeat);
        Assert.Equal([new GridTrackSize(new GridBreadth(GridBreadthKind.LengthPercentage, LengthPercentage.FromPixels(100)), new GridBreadth(GridBreadthKind.LengthPercentage, LengthPercentage.FromPixels(100)))], first.Tracks);
        Assert.Equal(["a"], first.LineNames[0]);
        Assert.Empty(first.LineNames[1]);

        var repeat = columns.Segments[1];
        Assert.Equal(GridRepeatKind.Count, repeat.Repeat);
        Assert.Equal(2, repeat.Count);
        Assert.Equal(2, repeat.Tracks.Length);
        Assert.Equal(GridBreadthKind.Auto, repeat.Tracks[0].Min.Kind);
        Assert.Equal(new GridBreadth(GridBreadthKind.Flex, Flex: 1), repeat.Tracks[0].Max);
        Assert.Equal(GridBreadthKind.Auto, repeat.Tracks[1].Max.Kind);
        Assert.Equal(["b"], repeat.LineNames[0]);

        var last = columns.Segments[2];
        Assert.Equal(["c"], last.LineNames[0]);
        Assert.True(last.Tracks[0].IsFitContent);
        Assert.Equal(50, last.Tracks[0].Max.Length.Resolve(100));
        Assert.Equal(["d"], last.LineNames[1]);

        Assert.Equal(GridRepeatKind.AutoFill, grid.TemplateRows.AutoRepeat?.Repeat);
        Assert.Equal([GridBreadthKind.MinContent, GridBreadthKind.Flex], grid.AutoColumns.Select(track => track.Max.Kind));
        Assert.Equal(new GridAutoFlow(IsColumn: true, IsDense: true), grid.AutoFlow);
        Assert.True(grid.TemplateAreas.IsNone);
        Assert.Equal([GridTrackSize.Auto], grid.AutoRows);
    }

    [Fact]
    public void Grid_Subgrid()
    {
        var grid = GetLayout("<div style='display: grid'><div id=target style='display: grid; grid-template-columns: subgrid [a] [b c]'></div></div>").Grid;
        Assert.True(grid.TemplateColumns.IsSubgrid);
        Assert.Equal([["a"], ["b", "c"]], grid.TemplateColumns.Segments.SelectMany(segment => segment.LineNames));
        Assert.True(grid.TemplateRows.IsNone);
    }

    [Fact]
    public void Grid_AreasAndPlacement()
    {
        var grid = GetLayout("<div id=target style='grid-template-areas: \"a a b\" \"a a .\"; grid-row: span 2 / x 3; grid-column-start: -1'></div>").Grid;
        Assert.Equal(2, grid.TemplateAreas.RowCount);
        Assert.Equal(3, grid.TemplateAreas.ColumnCount);
        Assert.True(grid.TemplateAreas.TryGetArea("a", out var area));
        Assert.Equal((1, 3, 1, 3), area);
        Assert.Null(grid.TemplateAreas.Rows[1][2]);
        Assert.Equal(new GridLine(IsAuto: false, IsSpan: true, 2, Name: null), grid.RowStart);
        Assert.Equal(new GridLine(IsAuto: false, IsSpan: false, 3, "x"), grid.RowEnd);
        Assert.Equal(new GridLine(IsAuto: false, IsSpan: false, -1, Name: null), grid.ColumnStart);
        Assert.Equal(GridLine.Auto, grid.ColumnEnd);
    }

    [Fact]
    public void Table()
    {
        var table = GetLayout("<table id=target style='border-spacing: 2px 4px; border-collapse: collapse; table-layout: fixed; caption-side: bottom; empty-cells: hide'></table>").Table;
        Assert.Equal(2, table.BorderSpacingInline);
        Assert.Equal(4, table.BorderSpacingBlock);
        Assert.Equal(BorderCollapse.Collapse, table.BorderCollapse);
        Assert.Equal(TableLayout.Fixed, table.TableLayout);
        Assert.Equal(CaptionSide.Bottom, table.CaptionSide);
        Assert.Equal(EmptyCells.Hide, table.EmptyCells);
    }

    [Fact]
    public void GapDecorations()
    {
        var layout = GetLayout("<div id=target style='column-rule: 2px solid, repeat(2, 4px dashed), repeat(auto, 6px dotted), 8px double; row-rule-break: intersection; rule-visibility-items: around; column-rule-inset: 5px / overlap-join; rule-overlap: column-over-row'></div>");
        Assert.True(layout.HasGapRules);
        var gaps = layout.GapDecorations;
        Assert.True(gaps.ColumnOverRow);
        Assert.True(gaps.Column.IsVisible);
        Assert.False(gaps.Row.IsVisible);

        // The leading values (the integer repeaters are expanded), the values of the auto repeater, the first trailing values at the end
        Assert.Equal([2, 4, 4, 6, 6, 8], Enumerable.Range(0, 6).Select(index => gaps.Column.Widths.Get(index, 6)));
        Assert.Equal([BorderStyle.Solid, BorderStyle.Dashed, BorderStyle.Dashed, BorderStyle.Double], Enumerable.Range(0, 4).Select(index => gaps.Column.Styles.Get(index, 4)));
        Assert.Equal(RuleBreak.Normal, gaps.Column.Break);
        Assert.Equal(RuleBreak.Intersection, gaps.Row.Break);
        Assert.Equal(RuleVisibilityItems.Around, gaps.Row.VisibilityItems);
        Assert.Equal(5, gaps.Column.InsetCapEnd.Length.Resolve(0));
        Assert.True(gaps.Column.InsetJunctionStart.IsOverlapJoin);

        // Without rule (the initial style is none), the layout does not compute the gaps
        Assert.False(GetLayout("<div id=target style='column-rule-width: 5px; column-rule-color: red'></div>").HasGapRules);
    }

    [Fact]
    public void Fragmentation()
    {
        var layout = GetLayout("<div id=target style='columns: 3 200px; column-fill: auto; column-span: all; column-rule: 2px solid; break-before: column; break-inside: avoid; orphans: 3; box-decoration-break: clone'></div>");
        Assert.True(layout.IsMultiColumnContainer);
        var fragmentation = layout.Fragmentation;
        Assert.Equal(3, fragmentation.ColumnCount);
        Assert.Equal(200, fragmentation.ColumnWidth.TryResolve(null));
        Assert.Equal(ColumnFill.Auto, fragmentation.ColumnFill);
        Assert.Equal(ColumnSpan.All, fragmentation.ColumnSpan);
        Assert.Equal(BreakBetween.Column, fragmentation.BreakBefore);
        Assert.Equal(BreakInside.Avoid, fragmentation.BreakInside);
        Assert.Equal(3, fragmentation.Orphans);
        Assert.Equal(2, fragmentation.Widows);
        Assert.Equal(BoxDecorationBreak.Clone, fragmentation.BoxDecorationBreak);
        Assert.False(GetLayout("<div id=target></div>").IsMultiColumnContainer);
    }

    [Fact]
    public void Overflow()
    {
        var overflow = GetLayout("<div id=target style='overflow: clip; overflow-clip-margin: content-box 5px; scrollbar-gutter: stable both-edges; scrollbar-width: thin; text-overflow: \"[\" ellipsis; scroll-padding: 10%; scroll-margin: 4px; scroll-behavior: smooth'></div>").Overflow;
        Assert.Equal(new OverflowClipMargin(VisualBox.ContentBox, 5), overflow.OverflowClipMargin.Top);
        Assert.Equal(ScrollbarGutter.StableBothEdges, overflow.ScrollbarGutter);
        Assert.Equal(ScrollbarWidth.Thin, overflow.ScrollbarWidth);
        Assert.Equal(new TextOverflow(new TextOverflowSide(TextOverflowKind.String, "["), new TextOverflowSide(TextOverflowKind.Ellipsis)), overflow.TextOverflow);
        Assert.Equal(20, overflow.ScrollPadding.Left.Resolve(200));
        Assert.Equal(4, overflow.ScrollMargin.Bottom);
        Assert.True(overflow.SmoothScrolling);
    }

    [Fact]
    public void Replaced()
    {
        var replaced = GetLayout("<img id=target style='object-fit: cover; object-position: right 10px top 0px; image-orientation: none'>").Replaced;
        Assert.Equal(ObjectFit.Cover, replaced.ObjectFit);
        Assert.Equal(90, replaced.ObjectPositionX.Resolve(100));
        Assert.Equal(0, replaced.ObjectPositionY.Resolve(100));
        Assert.Equal(new ImageOrientation(FromImage: false), replaced.ImageOrientation);
    }

    [Fact]
    public void ContainIntrinsicSize()
    {
        var box = GetLayout("<div id=target style='contain-intrinsic-width: auto 100px'></div>").Box;
        Assert.Equal(new ContainIntrinsicSize(IsAuto: true, 100), box.ContainIntrinsicWidth);
        Assert.Equal(new ContainIntrinsicSize(IsAuto: false, null), box.ContainIntrinsicHeight);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Resolved values (BoxSizes)
    // ----------------------------------------------------------------------------------------------------------------

    // The used sizes of a zoomed element are divided by its effective zoom in the resolved values
    [Theory]
    [InlineData("<div id=target style='margin: 0 10px'></div>", "width", "780px")]
    [InlineData("<div id=target style='zoom: 2; margin: 0 10px'></div>", "width", "380px")]
    [InlineData("<div style='width: 400px'><div id=target style='zoom: 2; width: 50%'></div></div>", "width", "100px")]
    [InlineData("<div style='width: 400px'><div id=target style='zoom: 2; margin-left: 10%'></div></div>", "margin-left", "20px")]
    public void ResolvedValue_Zoom(string html, string property, string expected)
    {
        var document = Parse("<body style='margin: 0'>" + html);
        Assert.Equal(expected, document.DefaultView!.GetComputedStyle(document.QuerySelector("#target")!).GetPropertyValue(property));
    }
}
