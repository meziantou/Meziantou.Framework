using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The page-margin boxes of a page (https://drafts.csswg.org/css-page-3/#margin-boxes): the sixteen boxes of the margin rules
/// of the @page rules that are generated (their <c>content</c> is not <c>none</c> nor <c>normal</c>), placed in the page
/// margins: the corner boxes fill the corners, the boxes of each edge share the edge between the corners
/// (https://drafts.csswg.org/css-page-3/#margin-dimension). Their contents are the strings, the quotes and the counters of
/// <c>content</c>, aligned by <c>text-align</c> and <c>vertical-align</c>.
/// </summary>
internal static class PageMarginBoxes
{
    /// <summary>The names of the margin boxes, in their painting order (https://drafts.csswg.org/css-page-3/#painting: from the top-left corner, clockwise).</summary>
    private static readonly string[] Names =
    [
        "top-left-corner", "top-left", "top-center", "top-right", "top-right-corner",
        "right-top", "right-middle", "right-bottom",
        "bottom-right-corner", "bottom-right", "bottom-center", "bottom-left", "bottom-left-corner",
        "left-bottom", "left-middle", "left-top",
    ];

    /// <summary>A margin box being placed: its style, its box and its outer rectangle in the page box.</summary>
    private sealed class MarginBox(string name, ComputedStyle style)
    {
        public string Name { get; } = name;

        /// <summary>
        /// The side of the box toward the edge of the page in each axis where the box fills the page margin (the depth of the
        /// margin: both axes for a corner): -1 for the start (left, top), 1 for the end, 0 along an edge. The margin of that
        /// side is ignored when the margins and the size of the box are over-constrained: the box is next to the page area.
        /// </summary>
        public (int Horizontal, int Vertical) OuterSides => Name switch
        {
            "top-left-corner" => (-1, -1),
            "top-right-corner" => (1, -1),
            "bottom-right-corner" => (1, 1),
            "bottom-left-corner" => (-1, 1),
            _ when Name.StartsWith("top-", StringComparison.Ordinal) => (0, -1),
            _ when Name.StartsWith("bottom-", StringComparison.Ordinal) => (0, 1),
            _ when Name.StartsWith("left-", StringComparison.Ordinal) => (-1, 0),
            _ => (1, 0),
        };

        public ComputedStyle Style { get; } = style;

        public ContainerBox? Box { get; set; }

        public PhysicalRect Rect { get; set; }

        /// <summary>The size of the containing block of the box (the corner, or the whole edge): the basis of its percentages.</summary>
        public PhysicalSize ContainingBlock { get; set; }
    }

    /// <summary>
    /// Lays out the generated margin boxes of a page; returns their fragments (in painting order, relative to the page box), or
    /// null when the page has none.
    /// </summary>
    /// <param name="context">The layout pass.</param>
    /// <param name="page">The page.</param>
    /// <param name="selection">The page, as the page selectors see it.</param>
    /// <param name="pageCount">The number of pages of the document (the <c>pages</c> counter).</param>
    /// <param name="counters">The counters of the page context of the page (see <see cref="PageCounters"/>).</param>
    public static List<BoxFragment>? Layout(LayoutContext context, LayoutPage page, PageSelection selection, int pageCount, IReadOnlyDictionary<string, long> counters)
    {
        if (page.Geometry.Style is not { } pageStyle)
            return null;

        var styleEngine = context.Document.GetStyleEngine();
        var boxes = new Dictionary<string, MarginBox>(StringComparer.Ordinal);
        CounterStyleResolver? counterStyles = null;
        foreach (var name in Names)
        {
            var style = styleEngine.GetPageMarginStyle(selection, pageStyle, name);
            var content = style.GetComputedValue("content");
            if (content is "none" or "normal" || GenerateContent(style, content, PageCounters.ForMarginBox(counters, style), pageCount, ref counterStyles, styleEngine) is not { } text)
                continue;

            var box = new ContainerBox(ContainerBoxKind.Normal, style, Display.Block, style.Element, style.PseudoElement) { ChildrenAreInline = true };
            if (text.Length > 0)
            {
                box.AppendChild(new TextRun(style, style.Element, style.PseudoElement, TextRunKind.Generated, node: null, text, 0, text.Length));
            }

            boxes[name] = new MarginBox(name, style) { Box = box };
        }

        if (boxes.Count == 0)
            return null;

        var geometry = page.Geometry;
        var (width, height) = (geometry.PageSize.Width, geometry.PageSize.Height);
        var margin = geometry.Margin;

        // The corners
        Place(boxes, "top-left-corner", new PhysicalRect(0, 0, margin.Left, margin.Top));
        Place(boxes, "top-right-corner", new PhysicalRect(width - margin.Right, 0, margin.Right, margin.Top));
        Place(boxes, "bottom-right-corner", new PhysicalRect(width - margin.Right, height - margin.Bottom, margin.Right, margin.Bottom));
        Place(boxes, "bottom-left-corner", new PhysicalRect(0, height - margin.Bottom, margin.Left, margin.Bottom));

        // The edges: the boxes share the length of the edge between the corners
        var horizontalLength = Math.Max(0, width - margin.Left - margin.Right);
        var verticalLength = Math.Max(0, height - margin.Top - margin.Bottom);
        PlaceEdge(context, boxes, ["top-left", "top-center", "top-right"], new PhysicalRect(margin.Left, 0, horizontalLength, margin.Top), horizontal: true);
        PlaceEdge(context, boxes, ["bottom-left", "bottom-center", "bottom-right"], new PhysicalRect(margin.Left, height - margin.Bottom, horizontalLength, margin.Bottom), horizontal: true);
        PlaceEdge(context, boxes, ["left-top", "left-middle", "left-bottom"], new PhysicalRect(0, margin.Top, margin.Left, verticalLength), horizontal: false);
        PlaceEdge(context, boxes, ["right-top", "right-middle", "right-bottom"], new PhysicalRect(width - margin.Right, margin.Top, margin.Right, verticalLength), horizontal: false);

        var result = new List<BoxFragment>(boxes.Count);
        foreach (var name in Names)
        {
            if (boxes.TryGetValue(name, out var box) && LayoutBox(context, box) is { } fragment)
            {
                result.Add(fragment);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the text of the computed value of <c>content</c> of a margin box: its strings, its quotes and its counters (the
    /// counters of the margin box, and <c>pages</c>, https://drafts.csswg.org/css-page-3/#page-based-counters); the other
    /// values (an image) are not generated.
    /// </summary>
    private static string? GenerateContent(ComputedStyle style, string content, IReadOnlyDictionary<string, long> counters, int pageCount, ref CounterStyleResolver? counterStyles, StyleEngine styleEngine)
    {
        var result = new StringBuilder();
        var quoteDepth = 0;
        foreach (var value in CssParser.ParseListOfComponentValues(content))
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Whitespace }:
                    break;
                case CssPreservedToken { Type: CssTokenType.String } text:
                    result.Append(text.Token.Value);
                    break;

                // https://drafts.csswg.org/css-content-3/#quote-values: the quotes of the box, nested in the box
                case CssPreservedToken { Type: CssTokenType.Ident } keyword:
                    switch (keyword.Token.Value)
                    {
                        case "open-quote":
                            result.Append(BoxTreeBuilder.GetQuote(style, quoteDepth++, open: true));
                            break;
                        case "no-open-quote":
                            quoteDepth++;
                            break;
                        case "close-quote" when quoteDepth > 0:
                            result.Append(BoxTreeBuilder.GetQuote(style, --quoteDepth, open: false));
                            break;
                        case "no-close-quote" when quoteDepth > 0:
                            quoteDepth--;
                            break;
                    }

                    break;
                case CssFunction { Name: var name } function when name.Equals("counter", StringComparison.OrdinalIgnoreCase) || name.Equals("counters", StringComparison.OrdinalIgnoreCase):
                {
                    var arguments = SplitArguments(function.Value);
                    if (arguments.Count == 0 || arguments[0] is not [CssPreservedToken { Type: CssTokenType.Ident } counter])
                        break;

                    // (as in Blink, the counters of the page context are not nested: counters() shows one value)
                    var counterValue = counter.Token.Value == "pages" ? pageCount : counters.GetValueOrDefault(counter.Token.Value);
                    var styleIndex = name.Equals("counters", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                    counterStyles ??= new CounterStyleResolver(styleEngine);
                    var counterStyle = arguments.Count > styleIndex && counterStyles.Resolve(arguments[styleIndex], treeScope: null) is { } resolved ? resolved : CounterStyle.Decimal;
                    result.Append(counterStyles.Generate(counterStyle, counterValue, WritingMode.HorizontalTb, Direction.Ltr));
                    break;
                }

                default:
                    break;
            }
        }

        return result.ToString();

        static List<List<CssComponentValue>> SplitArguments(List<CssComponentValue> values)
        {
            var arguments = new List<List<CssComponentValue>> { new() };
            foreach (var value in values)
            {
                if (value is CssPreservedToken { Type: CssTokenType.Comma })
                {
                    arguments.Add([]);
                }
                else if (value is not CssPreservedToken { Type: CssTokenType.Whitespace })
                {
                    arguments[^1].Add(value);
                }
            }

            return arguments;
        }
    }

    /// <summary>Places a corner box: it fills the corner.</summary>
    private static void Place(Dictionary<string, MarginBox> boxes, string name, PhysicalRect area)
    {
        if (boxes.TryGetValue(name, out var box))
        {
            box.Rect = area;
            box.ContainingBlock = area.Size;
        }
    }

    /// <summary>
    /// Places the three boxes of an edge (https://drafts.csswg.org/css-page-3/#margin-dimension): their outer size in the depth
    /// of the margin is the page margin; along the edge, the box at the start (A), the box at the middle (B) and the box at the
    /// end (C) share its length (https://drafts.csswg.org/css-page-3/#variable-auto-sizing): when B is generated, it is centered
    /// and A and C have the same size, else A and C share the length.
    /// </summary>
    private static void PlaceEdge(LayoutContext context, Dictionary<string, MarginBox> boxes, string[] names, PhysicalRect area, bool horizontal)
    {
        boxes.TryGetValue(names[0], out var a);
        boxes.TryGetValue(names[1], out var b);
        boxes.TryGetValue(names[2], out var c);
        if (a is null && b is null && c is null)
            return;

        var length = horizontal ? area.Width : area.Height;
        var depth = horizontal ? area.Height : area.Width;
        var sizeA = Measure(context, a, horizontal, length, depth);
        var sizeB = Measure(context, b, horizontal, length, depth);
        var sizeC = Measure(context, c, horizontal, length, depth);
        double outerA, outerB, outerC;
        if (b is null)
        {
            // (a box that is not generated has no size)
            (outerA, outerC) = Distribute(length, a is null ? new EdgeSize(0, 0, 0) : sizeA, c is null ? new EdgeSize(0, 0, 0) : sizeC);
            outerB = 0;
        }
        else
        {
            // An imaginary box twice as large as A, then as C, shares the edge with B: the larger one is the one that keeps B
            // centered (as in Blink: a specified size is its min-content and max-content size)
            // (a box that is not generated has no size: B alone fills the edge)
            var (outerB1, outerDoubleA) = Distribute(length, sizeB, Double(sizeA), secondWeight: a is null ? 0 : 2);
            var (outerB2, outerDoubleC) = Distribute(length, sizeB, Double(sizeC), secondWeight: c is null ? 0 : 2);
            outerB = outerDoubleA >= outerDoubleC ? outerB1 : outerB2;
            var outerAc = length - outerB;
            outerA = sizeA.Specified ?? outerAc / 2;
            outerC = sizeC.Specified ?? outerAc / 2;
        }

        SetRect(a, 0, outerA);
        SetRect(b, (length - outerB) / 2, outerB);
        SetRect(c, length - outerC, outerC);

        void SetRect(MarginBox? box, double offset, double size)
        {
            if (box is not null)
            {
                box.ContainingBlock = area.Size;
                box.Rect = horizontal ? new PhysicalRect(area.X + offset, area.Y, size, area.Height) : new PhysicalRect(area.X, area.Y + offset, area.Width, size);
            }
        }
    }

    private static EdgeSize Double(EdgeSize size) => new(2 * size.Min, 2 * size.Max, 2 * size.Specified);

    /// <summary>The outer min-content and max-content sizes of a margin box along its edge, or its specified outer size.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct EdgeSize(double Min, double Max, double? Specified);

    private static EdgeSize Measure(LayoutContext context, MarginBox? box, bool horizontal, double length, double depth)
    {
        if (box?.Box is not { } layoutBox)
            return default;

        // (the percentages of the box refer to the width of its containing block horizontally and to its height vertically:
        // the edge is the length of one of them, the depth of the margin the other one)
        var layout = box.Style.Layout;
        var boxStyle = layout.Box;
        var margins = horizontal ? boxStyle.Margin.Left.Resolve(length) + boxStyle.Margin.Right.Resolve(length) : boxStyle.Margin.Top.Resolve(length) + boxStyle.Margin.Bottom.Resolve(length);
        var edges = horizontal
            ? boxStyle.BorderWidth.Left + boxStyle.BorderWidth.Right + boxStyle.Padding.Left.Resolve(length) + boxStyle.Padding.Right.Resolve(length)
            : boxStyle.BorderWidth.Top + boxStyle.BorderWidth.Bottom + boxStyle.Padding.Top.Resolve(length) + boxStyle.Padding.Bottom.Resolve(length);
        var size = horizontal ? boxStyle.Width : boxStyle.Height;
        if (size.TryResolve(length) is { } specified)
        {
            var outer = margins + specified + (layout.BoxSizing == BoxSizing.BorderBox ? 0 : edges);
            return new EdgeSize(outer, outer, outer);
        }

        // The min-content and max-content sizes of the box along the edge: its intrinsic inline sizes when its inline axis is
        // along the edge, else its size along the edge once laid out in the depth of the margin
        if (horizontal == (layout.WritingMode == WritingMode.HorizontalTb))
        {
            var sizes = IntrinsicSizing.GetIntrinsicInlineSizes(context, layoutBox, contentBlockSize: null);
            return new EdgeSize(sizes.MinContent + edges + margins, sizes.MaxContent + edges + margins, null);
        }

        double content;
        if (horizontal)
        {
            var blockMargins = boxStyle.Margin.Top.Resolve(depth) + boxStyle.Margin.Bottom.Resolve(depth);
            var fragment = context.LayoutBox(layoutBox, ConstraintSpace.ForContentBox(WritingDirection.HorizontalLtr, length, depth) with { FixedBlockSize = Math.Max(0, depth - blockMargins) });
            content = fragment.Size.Width + margins;
        }
        else
        {
            var inlineMargins = boxStyle.Margin.Left.Resolve(depth) + boxStyle.Margin.Right.Resolve(depth);
            var fragment = context.LayoutBox(layoutBox, ConstraintSpace.ForContentBox(WritingDirection.HorizontalLtr, depth, null) with { FixedInlineSize = Math.Max(0, depth - inlineMargins) });
            content = fragment.Size.Height + margins;
        }

        return new EdgeSize(content, content, null);
    }

    /// <summary>
    /// Distributes the length of an edge between two boxes (https://drafts.csswg.org/css-page-3/#distribute-margin-box-space):
    /// a box with a specified size keeps it and the other one gets the rest; two automatic sizes grow from their max-content
    /// sizes in proportion to them, or shrink from them in proportion to the difference with their min-content sizes, or below
    /// their min-content sizes in proportion to them. When both max-content sizes are 0, the boxes share the length equally, the
    /// imaginary box AC counting as two boxes (<paramref name="secondWeight"/>), as in Blink.
    /// </summary>
    private static (double First, double Second) Distribute(double length, EdgeSize first, EdgeSize second, double secondWeight = 1)
    {
        if (first.Specified is { } firstSize)
            return (firstSize, second.Specified ?? Math.Max(0, length - firstSize));

        if (second.Specified is { } secondSize)
            return (Math.Max(0, length - secondSize), secondSize);

        var maxSum = first.Max + second.Max;
        if (maxSum <= length)
        {
            var space = length - maxSum;
            return maxSum > 0 ? (first.Max + (space * first.Max / maxSum), second.Max + (space * second.Max / maxSum)) : (length / (1 + secondWeight), length * secondWeight / (1 + secondWeight));
        }

        var minSum = first.Min + second.Min;
        if (minSum < length)
        {
            var space = length - minSum;
            var flexSum = (first.Max - first.Min) + (second.Max - second.Min);
            return flexSum > 0 ? (first.Min + (space * (first.Max - first.Min) / flexSum), second.Min + (space * (second.Max - second.Min) / flexSum)) : (first.Min, second.Min);
        }

        var overflow = length - minSum;
        return minSum > 0 ? (first.Min + (overflow * first.Min / minSum), second.Min + (overflow * second.Min / minSum)) : (0, 0);
    }

    /// <summary>
    /// Lays out a margin box in its outer rectangle: its margins are in it (the automatic margins center a box whose size is
    /// specified), and its contents are aligned in its content box by <c>vertical-align</c> (<c>top</c>, <c>middle</c>,
    /// <c>center</c> or <c>bottom</c>, https://drafts.csswg.org/css-page-3/#margin-text-alignment).
    /// </summary>
    private static BoxFragment? LayoutBox(LayoutContext context, MarginBox box)
    {
        if (box.Box is not { } layoutBox)
            return null;

        var rect = box.Rect;
        var containingBlock = box.ContainingBlock;
        var boxStyle = box.Style.Layout.Box;
        var marginTop = boxStyle.Margin.Top;
        var marginBottom = boxStyle.Margin.Bottom;
        var marginLeft = boxStyle.Margin.Left;
        var marginRight = boxStyle.Margin.Right;
        var edgesX = boxStyle.BorderWidth.Left + boxStyle.BorderWidth.Right + boxStyle.Padding.Left.Resolve(containingBlock.Width) + boxStyle.Padding.Right.Resolve(containingBlock.Width);
        var edgesY = boxStyle.BorderWidth.Top + boxStyle.BorderWidth.Bottom + boxStyle.Padding.Top.Resolve(containingBlock.Height) + boxStyle.Padding.Bottom.Resolve(containingBlock.Height);
        var borderBox = box.Style.Layout.BoxSizing == BoxSizing.BorderBox;
        var (outerX, outerY) = box.OuterSides;
        // (the intrinsic sizes are the sizes of the contents: min-content, max-content, fit-content in the outer rectangle)
        double? specifiedWidth = boxStyle.Width.TryResolve(containingBlock.Width) is { } w ? w + (borderBox ? 0 : edgesX) : null;
        if (specifiedWidth is null && boxStyle.Width.IsIntrinsic && box.Style.Layout.WritingMode != WritingMode.HorizontalTb)
        {
            // (the block size of a box in a vertical writing mode: the width of its contents laid out in the height of the rectangle)
            var blockSize = boxStyle.Height.TryResolve(containingBlock.Height) is { } fixedHeight ? fixedHeight + (borderBox ? 0 : edgesY) : rect.Height - marginTop.Resolve(containingBlock.Height) - marginBottom.Resolve(containingBlock.Height);
            specifiedWidth = context.LayoutBox(layoutBox, ConstraintSpace.ForContentBox(WritingDirection.HorizontalLtr, containingBlock.Width, containingBlock.Height) with { FixedBlockSize = Math.Max(0, blockSize) }).Size.Width;
        }
        else if (specifiedWidth is null && boxStyle.Width.IsIntrinsic)
        {
            var sizes = IntrinsicSizing.GetIntrinsicInlineSizes(context, layoutBox, contentBlockSize: null);
            var available = rect.Width - marginLeft.Resolve(containingBlock.Width) - marginRight.Resolve(containingBlock.Width) - edgesX;
            specifiedWidth = edgesX + boxStyle.Width.Kind switch
            {
                SizeKind.MinContent => sizes.MinContent,
                SizeKind.MaxContent => sizes.MaxContent,
                _ => Math.Max(sizes.MinContent, Math.Min(sizes.MaxContent, available)),
            };
        }

        var (left, width) = Resolve(rect.Width, containingBlock.Width, marginLeft, marginRight, specifiedWidth, outerX);
        double? specifiedHeight = boxStyle.Height.TryResolve(containingBlock.Height) is { } h ? h + (borderBox ? 0 : edgesY) : null;
        if (specifiedHeight is null && boxStyle.Height.IsIntrinsic)
        {
            specifiedHeight = context.LayoutBox(layoutBox, ConstraintSpace.ForContentBox(WritingDirection.HorizontalLtr, containingBlock.Width, containingBlock.Height) with { FixedInlineSize = width }).Size.Height;
        }

        var (top, height) = Resolve(rect.Height, containingBlock.Height, marginTop, marginBottom, specifiedHeight, outerY);
        var space = ConstraintSpace.ForContentBox(WritingDirection.HorizontalLtr, containingBlock.Width, containingBlock.Height) with { FixedInlineSize = width, FixedBlockSize = height };
        var fragment = context.LayoutBox(layoutBox, space);
        fragment.Offset = new PhysicalOffset(rect.X + left, rect.Y + top);

        // The vertical alignment of the contents in the content box (line-relative: in a vertical writing mode, the line-over
        // side is the right side, the left side for sideways-lr)
        var children = fragment.ChildrenSpan;
        if (children.Length > 0)
        {
            var writingMode = box.Style.Layout.WritingMode;
            var horizontalLines = writingMode == WritingMode.HorizontalTb;
            var contentStart = horizontalLines ? fragment.Border.Top + fragment.Padding.Top : fragment.Border.Left + fragment.Padding.Left;
            var contentSize = horizontalLines
                ? fragment.Size.Height - contentStart - fragment.Border.Bottom - fragment.Padding.Bottom
                : fragment.Size.Width - contentStart - fragment.Border.Right - fragment.Padding.Right;
            var extentStart = double.PositiveInfinity;
            var extentEnd = double.NegativeInfinity;
            foreach (var child in children)
            {
                extentStart = Math.Min(extentStart, horizontalLines ? child.Offset.Y : child.Offset.X);
                extentEnd = Math.Max(extentEnd, horizontalLines ? child.Offset.Y + child.Size.Height : child.Offset.X + child.Size.Width);
            }

            var alignment = box.Style.GetComputedValue("baseline-shift") switch
            {
                "top" => 0d,
                "center" => 0.5,
                "bottom" => 1d,
                _ => box.Style.GetComputedValue("alignment-baseline") is "middle" or "central" ? 0.5 : 0,
            };
            if (!horizontalLines && writingMode != WritingMode.SidewaysLr)
            {
                alignment = 1 - alignment;
            }

            var shift = (contentSize - (extentEnd - extentStart)) * alignment + contentStart - extentStart;
            if (Math.Abs(shift) > 0.001)
            {
                foreach (var child in children)
                {
                    child.Offset = horizontalLines ? new PhysicalOffset(child.Offset.X, child.Offset.Y + shift) : new PhysicalOffset(child.Offset.X + shift, child.Offset.Y);
                }
            }
        }

        return fragment;

        // The margins and the size of the box in an axis of its outer rectangle: an automatic size fills it, the automatic
        // margins of a box with a specified size share what remains
        static (double Start, double Size) Resolve(double outer, double basis, LengthPercentageAuto start, LengthPercentageAuto end, double? size, int outerSide)
        {
            if (size is not { } specified)
                return (start.Resolve(basis), Math.Max(0, outer - start.Resolve(basis) - end.Resolve(basis)));

            var remaining = outer - specified;
            if (start.IsAuto && end.IsAuto)
                return (remaining / 2, specified);

            if (start.IsAuto || (!end.IsAuto && outerSide < 0))
                return (remaining - end.Resolve(basis), specified);

            return (start.Resolve(basis), specified);
        }
    }
}

/// <summary>
/// The counters of the page contexts (https://drafts.csswg.org/css-page-3/#page-based-counters), as in Blink: the page context
/// inherits the counters of the root element (its <c>counter-reset</c>, <c>counter-increment</c> and <c>counter-set</c>) and the
/// <c>page</c> counter (0 before the first page). Each page applies the counter properties of its page context to them
/// (<c>page</c> is incremented by 1 unless <c>counter-increment</c> names it): the changes of the counters that the page does
/// not reset are kept for the next pages, a counter reset by the page is a counter of that page only. The page-margin boxes
/// apply their own counter properties to the counters of their page, for themselves only.
/// </summary>
internal sealed class PageCounters
{
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal) { ["page"] = 0 };

    public PageCounters(ComputedStyle? rootStyle)
    {
        if (rootStyle is not null)
        {
            Apply(_counters, rootStyle, implicitPageIncrement: false, persistent: null);
        }
    }

    /// <summary>Returns the counters of the next page, whose page context has a style.</summary>
    public Dictionary<string, long> Next(ComputedStyle? pageStyle)
    {
        var counters = new Dictionary<string, long>(_counters, StringComparer.Ordinal);
        if (pageStyle is null)
        {
            counters["page"] = _counters["page"] = _counters["page"] + 1;
            return counters;
        }

        Apply(counters, pageStyle, implicitPageIncrement: true, _counters);
        return counters;
    }

    /// <summary>Returns the counters of a page-margin box of a page: the counters of the page changed by the counter properties of the box.</summary>
    public static IReadOnlyDictionary<string, long> ForMarginBox(IReadOnlyDictionary<string, long> pageCounters, ComputedStyle style)
    {
        if (style.GetComputedValue("counter-reset") == "none" && style.GetComputedValue("counter-increment") == "none" && style.GetComputedValue("counter-set") == "none")
            return pageCounters;

        var counters = new Dictionary<string, long>(pageCounters, StringComparer.Ordinal);
        Apply(counters, style, implicitPageIncrement: false, persistent: null);
        return counters;
    }

    /// <summary>
    /// Applies <c>counter-reset</c>, <c>counter-increment</c> then <c>counter-set</c> to counters
    /// (https://drafts.csswg.org/css-lists-3/#auto-numbering); the changes of the counters that are not reset are also made to
    /// the persistent counters.
    /// </summary>
    private static void Apply(Dictionary<string, long> counters, ComputedStyle style, bool implicitPageIncrement, Dictionary<string, long>? persistent)
    {
        HashSet<string>? resets = null;
        foreach (var counter in CssCounters.ParseCounters(style.GetComputedValue("counter-reset"), defaultValue: 0))
        {
            counters[counter.Name] = counter.Value;
            (resets ??= new(StringComparer.Ordinal)).Add(counter.Name);
        }

        var increments = CssCounters.ParseCounters(style.GetComputedValue("counter-increment"), defaultValue: 1);
        if (implicitPageIncrement && !increments.Exists(static counter => counter.Name == "page"))
        {
            increments.Add(new CssCounters.CounterValue("page", 1, Reversed: false, HasValue: true));
        }

        foreach (var counter in increments)
        {
            Set(counter.Name, counters.GetValueOrDefault(counter.Name) + counter.Value);
        }

        foreach (var counter in CssCounters.ParseCounters(style.GetComputedValue("counter-set"), defaultValue: 0))
        {
            Set(counter.Name, counter.Value);
        }

        void Set(string name, long value)
        {
            counters[name] = value;
            if (persistent is not null && resets?.Contains(name) != true)
            {
                persistent[name] = value;
            }
        }
    }
}
