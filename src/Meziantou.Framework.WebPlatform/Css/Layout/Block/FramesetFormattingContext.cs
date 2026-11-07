using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The state of the layout of a frameset, stored in <see cref="LayoutBox.LayoutState"/> of its box: the borders between its
/// rectangles, which the painting fills with its frame border color (see <see cref="FramesetFormattingContext"/>).
/// </summary>
internal sealed class FramesetState
{
    /// <summary>The rectangles of the borders between the rows and between the columns, relative to the content box of the frameset.</summary>
    public List<PhysicalRect> Borders { get; } = [];
}

/// <summary>
/// The layout of a <c>frameset</c> element (https://html.spec.whatwg.org/multipage/rendering.html#frames-and-framesets): "User
/// agents are expected to render frameset elements as a box with the height and width of the viewport, with a surface rendered
/// according to the following layout algorithm". The surface is split into a grid of the sizes of its <c>cols</c> and
/// <c>rows</c> attributes, and the <c>frame</c> and <c>frameset</c> children are assigned to the rectangles from top to bottom,
/// then from left to right (a nested frameset is laid out with its rectangle as its surface; a frame displays its content
/// navigable in its rectangle).
/// </summary>
/// <remarks>
/// <para>
/// The box of a frameset is a block box whatever its display (other than <c>none</c> and <c>contents</c>), as in the browsers,
/// and only its <c>frame</c> and <c>frameset</c> children generate boxes, which are block-level boxes too (see
/// <c>BoxTreeBuilder</c>). The border box of a frameset is the viewport (the initial containing block), or the rectangle it is
/// assigned in its parent frameset; its sizing properties are ignored. The border box of each child is its rectangle; the
/// children left once the grid is full are not rendered (as in Blink, they are laid out with a zero size).
/// </para>
/// <para>
/// The sizes are whole CSS pixels: "User agents working with integer values for frame widths [...] are expected to distribute
/// the remainder first to the last entry whose unit is relative, then equally (not proportionally) to each entry whose unit is
/// percentage, then equally (not proportionally) to each entry whose unit is absolute, and finally, failing all else, to the
/// last entry" (as in the browsers).
/// </para>
/// <para>
/// The specification does not size the borders: as in the browsers, a frameset that has a border has borders between its rows
/// and between its columns, whose thickness is its <c>border</c> attribute (a non-negative integer), else the one of its parent
/// frameset, else 6 pixels, and they take their space from the surface before it is split (they are painted with the frame
/// border color of the frameset, see <c>Painter</c>). There is no border around the grid. A frameset that has no border
/// (<c>frameborder=0</c>) has no space between its rectangles. The borders cannot be moved by the user (<c>noresize</c> has no
/// effect).
/// </para>
/// </remarks>
internal sealed class FramesetFormattingContext : FormattingContext
{
    /// <summary>The thickness of the borders of a frameset without <c>border</c> attribute, as in the browsers.</summary>
    private const int DefaultBorderThickness = 6;

    public static FramesetFormattingContext Instance { get; } = new();

    private FramesetFormattingContext()
    {
    }

    public override FormattingContextKind Kind => FormattingContextKind.Block;

    /// <summary>Whether a box is the box of a <c>frameset</c> element, laid out by this formatting context.</summary>
    public static bool IsFrameset(ContainerBox box) => box.Kind == ContainerBoxKind.Normal && box.PseudoElement is null && box.Element is HtmlFrameSetElement;

    /// <summary>Whether the element is a <c>frame</c> or a <c>frameset</c> element whose parent is a <c>frameset</c> element: it is laid out by its parent.</summary>
    public static bool IsFramesetChild(Element element)
        => element.IsHtmlNamespace && element.TagId is TagId.Frame or TagId.Frameset && element.ParentNode is HtmlFrameSetElement;

    public override BoxFragment Layout(LayoutContext context, ContainerBox box, in ConstraintSpace space)
    {
        // A nested frameset is sized by its parent; the others are as large as the viewport
        if (box.Parent is { } parent && IsFrameset(parent))
            return base.Layout(context, box, space);

        var size = space.WritingDirection.ToLogical(context.InitialContainingBlockSize);
        return base.Layout(context, box, space with { FixedInlineSize = size.Inline, FixedBlockSize = size.Block, IsFixedBlockSizeIndefinite = false });
    }

    protected override ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content)
    {
        var writingDirection = content.WritingDirection;
        var element = (HtmlFrameSetElement)box.Element!;
        var state = box.LayoutState as FramesetState ?? new FramesetState();
        box.LayoutState = state;
        state.Borders.Clear();

        // The rows and the columns are physical: from top to bottom and from left to right
        var surface = writingDirection.ToPhysical(new LogicalSize(content.InlineSize, content.BlockSize ?? 0));
        var border = HasBorder(element) ? GetBorderThickness(element) : 0;
        var cols = GetDimensions(element, "cols");
        var rows = GetDimensions(element, "rows");
        var columnWidths = ConvertToPixels(cols, Math.Max(0, Math.Floor(surface.Width) - ((cols.Count - 1) * border)));
        var rowHeights = ConvertToPixels(rows, Math.Max(0, Math.Floor(surface.Height) - ((rows.Count - 1) * border)));

        // The borders between the columns and between the rows, across the surface
        if (border > 0)
        {
            var x = 0d;
            for (var column = 0; column < columnWidths.Length - 1; column++)
            {
                x += columnWidths[column];
                state.Borders.Add(new PhysicalRect(x, 0, border, surface.Height));
                x += border;
            }

            var y = 0d;
            for (var row = 0; row < rowHeights.Length - 1; row++)
            {
                y += rowHeights[row];
                state.Borders.Add(new PhysicalRect(0, y, surface.Width, border));
                y += border;
            }
        }

        // The children are assigned to the rectangles in tree order: the boxes of the frame and frameset children (a child with
        // display: none has no box but takes its rectangle)
        var child = FirstFramesetChildBox(box);
        var cellIndex = 0;
        for (var childElement = element.FirstElementChild; childElement is not null; childElement = childElement.NextElementSibling)
        {
            if (!IsFramesetChild(childElement))
                continue;

            var assigned = cellIndex < columnWidths.Length * rowHeights.Length;
            var rect = default(PhysicalRect);
            if (assigned)
            {
                var row = cellIndex / columnWidths.Length;
                var column = cellIndex % columnWidths.Length;
                var x = 0d;
                for (var i = 0; i < column; i++)
                {
                    x += columnWidths[i] + border;
                }

                var y = 0d;
                for (var i = 0; i < row; i++)
                {
                    y += rowHeights[i] + border;
                }

                rect = new PhysicalRect(x, y, columnWidths[column], rowHeights[row]);
            }

            cellIndex++;
            if (child is null || !ReferenceEquals(child.Element, childElement))
                continue;

            var logicalSize = writingDirection.ToLogical(rect.Size);
            var space = ConstraintSpace.ForContentBox(writingDirection, content.InlineSize, content.BlockSize) with
            {
                FixedInlineSize = logicalSize.Inline,
                FixedBlockSize = logicalSize.Block,
            };
            var fragment = context.LayoutBox(child, space);
            var offset = writingDirection.ToLogical(rect.Offset, surface, rect.Size);
            builder.AddChild(fragment, new LogicalOffset(content.Offset.Inline + offset.Inline, content.Offset.Block + offset.Block));
            child = NextFramesetChildBox(child.NextSibling);
        }

        return new ContentLayoutResult(content.BlockSize ?? 0);
    }

    /// <summary>The intrinsic inline sizes of a frameset are the inline size of the viewport, its used size.</summary>
    protected internal override IntrinsicSizes ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
        => IntrinsicSizes.FromSize(WritingDirection.FromStyle(box.Style.Layout).ToLogical(context.InitialContainingBlockSize).Inline);

    private static ContainerBox? FirstFramesetChildBox(ContainerBox box) => NextFramesetChildBox(box.FirstChild);

    private static ContainerBox? NextFramesetChildBox(LayoutBox? box)
    {
        for (; box is not null; box = box.NextSibling)
        {
            if (box is ContainerBox { Element: { } element } container && !container.IsOutOfFlow && IsFramesetChild(element))
                return container;
        }

        return null;
    }

    /// <summary>The <c>cols</c> or <c>rows</c> of a frameset: "For any of the entries in cols or rows that have the number zero and the unit relative, change the entry's number to one. If cols has no entries, then add a single entry consisting of the value 1 and the unit relative to cols."</summary>
    private static List<(double Value, DimensionUnit Unit)> GetDimensions(Element element, string name)
    {
        var dimensions = element.GetAttributeValue(name) is { } value ? Microsyntaxes.ParseListOfDimensions(value) : [];
        for (var i = 0; i < dimensions.Count; i++)
        {
            if (dimensions[i] is (0, DimensionUnit.Relative))
            {
                dimensions[i] = (1, DimensionUnit.Relative);
            }
        }

        if (dimensions.Count == 0)
        {
            dimensions.Add((1, DimensionUnit.Relative));
        }

        return dimensions;
    }

    /// <summary>
    /// The algorithm to "convert a list of dimensions to a list of pixel values"
    /// (https://html.spec.whatwg.org/multipage/rendering.html#convert-a-list-of-dimensions-to-a-list-of-pixel-values), in whole
    /// CSS pixels (the remainder of the rounding is distributed as the specification expects of the user agents that work
    /// with integer values).
    /// </summary>
    internal static double[] ConvertToPixels(List<(double Value, DimensionUnit Unit)> input, double inputDimension)
    {
        var output = new double[input.Count];
        var totalPercentage = 0d;
        var totalRelative = 0d;
        var totalAbsolute = 0d;
        foreach (var (value, unit) in input)
        {
            switch (unit)
            {
                case DimensionUnit.Percentage:
                    totalPercentage += value;
                    break;
                case DimensionUnit.Relative:
                    totalRelative += value;
                    break;
                default:
                    totalAbsolute += value;
                    break;
            }
        }

        // (the ratios are computed before the multiplications: the numbers may be very large)
        var remainingSpace = inputDimension;
        if (totalAbsolute > remainingSpace)
        {
            Set(DimensionUnit.Absolute, value => value / totalAbsolute * remainingSpace);
            remainingSpace = 0;
        }
        else
        {
            Set(DimensionUnit.Absolute, value => value);
            remainingSpace -= totalAbsolute;
        }

        if (totalPercentage / 100 * inputDimension > remainingSpace)
        {
            Set(DimensionUnit.Percentage, value => value / totalPercentage * remainingSpace);
            remainingSpace = 0;
        }
        else
        {
            Set(DimensionUnit.Percentage, value => value / 100 * inputDimension);
            remainingSpace -= totalPercentage / 100 * inputDimension;
        }

        if (totalRelative > 0)
        {
            Set(DimensionUnit.Relative, value => value / totalRelative * remainingSpace);
        }

        // Whole pixels: the remainder of the rounding goes to the last relative entry, then equally to the percentage entries,
        // then equally to the absolute entries, then to the last entry
        var sum = 0d;
        var floored = 0d;
        for (var i = 0; i < output.Length; i++)
        {
            sum += output[i];
            output[i] = Math.Floor(output[i] + 1e-7);
            floored += output[i];
        }

        var remainder = Math.Max(0, Math.Round(sum, MidpointRounding.AwayFromZero) - floored);
        if (remainder > 0 && input.FindLastIndex(entry => entry.Unit == DimensionUnit.Relative) is >= 0 and var lastRelative)
        {
            output[lastRelative] += remainder;
            remainder = 0;
        }

        remainder = DistributeEqually(DimensionUnit.Percentage, remainder);
        remainder = DistributeEqually(DimensionUnit.Absolute, remainder);
        if (remainder > 0 && output.Length > 0)
        {
            output[^1] += remainder;
        }

        return output;

        void Set(DimensionUnit unit, Func<double, double> compute)
        {
            for (var i = 0; i < input.Count; i++)
            {
                if (input[i].Unit == unit)
                {
                    output[i] = compute(input[i].Value);
                }
            }
        }

        double DistributeEqually(DimensionUnit unit, double remaining)
        {
            var count = input.Count(entry => entry.Unit == unit);
            if (remaining <= 0 || count == 0)
                return remaining;

            var share = Math.Floor(remaining / count);
            var extra = remaining - (share * count);
            for (var i = 0; i < input.Count; i++)
            {
                if (input[i].Unit == unit)
                {
                    output[i] += share;
                }
            }

            return extra;
        }
    }

    /// <summary>
    /// Whether a frameset or frame element has a border (https://html.spec.whatwg.org/multipage/rendering.html#frames-and-framesets):
    /// its <c>frameborder</c> attribute starts with 1, y or Y; otherwise, when it has one, false; otherwise the one of its parent
    /// frameset, else true.
    /// </summary>
    internal static bool HasBorder(Element element)
    {
        for (var current = element; current is not null; current = current.ParentNode as HtmlFrameSetElement)
        {
            if (current.GetAttributeValue("frameborder") is { } frameBorder)
                return frameBorder.Length > 0 && frameBorder[0] is '1' or 'y' or 'Y';
        }

        return true;
    }

    /// <summary>The thickness of the borders of a frameset: its <c>border</c> attribute, else the one of its parent frameset, else 6 pixels (as in the browsers).</summary>
    private static int GetBorderThickness(Element element)
    {
        for (var current = element; current is not null; current = current.ParentNode as HtmlFrameSetElement)
        {
            if (current.GetAttributeValue("border") is { } value)
                return Microsyntaxes.TryParseInteger(value, out var thickness) ? (int)Math.Clamp(thickness, 0, 10_000) : 0;
        }

        return DefaultBorderThickness;
    }

    /// <summary>
    /// The frame border color of a frameset or frame element (https://html.spec.whatwg.org/multipage/rendering.html#frames-and-framesets):
    /// its <c>bordercolor</c> attribute when it is a valid legacy color value, else the one of its parent frameset, else gray.
    /// </summary>
    internal static string GetFrameBorderColor(Element element)
    {
        for (var current = element; current is not null; current = current.ParentNode as HtmlFrameSetElement)
        {
            if (Cascade.PresentationalHints.ParseLegacyColor(current.GetAttributeValue("bordercolor")) is { } color)
                return color;
        }

        return "gray";
    }
}
