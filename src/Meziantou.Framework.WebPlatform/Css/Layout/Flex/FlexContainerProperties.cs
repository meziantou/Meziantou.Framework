using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The used values of the properties of a flex container and of its items that the flex layout algorithm reads: the ones
/// of the flex layout (https://drafts.csswg.org/css-flexbox-1/, <c>flex-wrap: balance</c> and <c>flex-line-count</c> of
/// https://drafts.csswg.org/css-flexbox-2/), or the ones of the legacy <c>-webkit-box</c> mapped to them
/// (https://compat.spec.whatwg.org/#css-display-webkit-box), as the browsers do: <c>-webkit-box-orient</c> and
/// <c>-webkit-box-direction</c> give the direction (logical: <c>horizontal</c> is <c>row</c>), the box is single-line,
/// <c>-webkit-box-pack</c> is <c>justify-content</c>, <c>-webkit-box-align</c> is the alignment of every item (their
/// <c>align-self</c> is ignored), <c>-webkit-box-flex</c> is both the flex grow and the flex shrink factors of the items,
/// <c>-webkit-box-ordinal-group</c> their order.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FlexContainerProperties
{
    /// <summary>Whether the main axis is the inline axis of the container (<c>row</c>, <c>row-reverse</c>).</summary>
    public bool IsRow { get; init; }

    /// <summary>Whether the main-start edge is the end edge of the axis (<c>row-reverse</c>, <c>column-reverse</c>).</summary>
    public bool IsMainReverse { get; init; }

    /// <summary>Whether the container is multi-line (https://drafts.csswg.org/css-flexbox-1/#multi-line-flex-container).</summary>
    public bool IsMultiLine { get; init; }

    /// <summary>Whether the cross-start edge is the end edge of the cross axis (<c>wrap-reverse</c>).</summary>
    public bool IsCrossReverse { get; init; }

    /// <summary>
    /// Whether the container balances its lines (<c>flex-wrap: balance</c>, https://drafts.csswg.org/css-flexbox-2/#valdef-flex-wrap-balance):
    /// a multi-line container that chooses its line breaks to make the main sizes of its lines as similar as possible.
    /// </summary>
    public bool IsBalanced { get; init; }

    /// <summary>
    /// The <c>flex-line-count</c> of a multi-line container (https://drafts.csswg.org/css-flexbox-2/#flex-line-count-property),
    /// 1 for a single-line container, where the property has no effect: the minimum number of lines of a balanced container
    /// (limited by the number of its items), and the number of lines that share the available cross space of the items.
    /// </summary>
    public int LineCount { get; init; }

    /// <summary>Whether the container is a legacy <c>-webkit-box</c>.</summary>
    public bool IsWebkitBox { get; init; }

    public Alignment JustifyContent { get; init; }

    public Alignment AlignItems { get; init; }

    public Alignment AlignContent { get; init; }

    /// <summary>The gap between the items of a line: <c>column-gap</c> for a row container, <c>row-gap</c> for a column container.</summary>
    public Gap MainGap { get; init; }

    /// <summary>The gap between the lines.</summary>
    public Gap CrossGap { get; init; }

    /// <summary>Returns the properties of a flex container.</summary>
    public static FlexContainerProperties Get(ContainerBox box)
    {
        var layout = box.Style.Layout;
        var flex = layout.Flex;
        var alignment = layout.Alignment;
        if (box.Display.Inside == DisplayInside.WebkitBox)
        {
            var isRow = flex.WebkitBoxOrient is not ("vertical" or "block-axis");
            return new FlexContainerProperties
            {
                IsRow = isRow,
                IsMainReverse = flex.WebkitBoxDirectionReverse,
                IsWebkitBox = true,
                LineCount = 1,
                JustifyContent = new Alignment(flex.WebkitBoxPack),
                AlignItems = new Alignment(flex.WebkitBoxAlign),
                AlignContent = Alignment.Normal,
                MainGap = isRow ? alignment.ColumnGap : alignment.RowGap,
                CrossGap = isRow ? alignment.RowGap : alignment.ColumnGap,
            };
        }

        var row = flex.IsRow;
        return new FlexContainerProperties
        {
            IsRow = row,
            IsMainReverse = flex.IsReverse,
            IsMultiLine = flex.Wrap != FlexWrap.NoWrap,
            IsBalanced = flex.IsBalanced && flex.Wrap != FlexWrap.NoWrap,
            LineCount = flex.Wrap != FlexWrap.NoWrap ? flex.LineCount : 1,
            IsCrossReverse = flex.Wrap == FlexWrap.WrapReverse,
            JustifyContent = alignment.JustifyContent,
            AlignItems = alignment.AlignItems,
            AlignContent = alignment.AlignContent,
            MainGap = row ? alignment.ColumnGap : alignment.RowGap,
            CrossGap = row ? alignment.RowGap : alignment.ColumnGap,
        };
    }

    /// <summary>Returns the used main gap (https://drafts.csswg.org/css-align-3/#gaps): <c>normal</c> is 0, a percentage of an indefinite size resolves against zero.</summary>
    public double GetMainGap(double? percentageBasis) => MainGap.Resolve(percentageBasis ?? 0);

    /// <summary>Returns the used cross gap (see <see cref="GetMainGap"/>).</summary>
    public double GetCrossGap(double? percentageBasis) => CrossGap.Resolve(percentageBasis ?? 0);

    /// <summary>The flex grow factor of an item (https://drafts.csswg.org/css-flexbox-1/#flex-grow-property).</summary>
    public double GetGrow(LayoutStyle item) => IsWebkitBox ? Math.Max(0, item.Flex.WebkitBoxFlex) : item.Flex.Grow;

    /// <summary>The flex shrink factor of an item (https://drafts.csswg.org/css-flexbox-1/#flex-shrink-property).</summary>
    public double GetShrink(LayoutStyle item) => IsWebkitBox ? Math.Max(0, item.Flex.WebkitBoxFlex) : item.Flex.Shrink;

    /// <summary>The order of an item (https://drafts.csswg.org/css-display/#order-property).</summary>
    public int GetOrder(LayoutStyle item) => IsWebkitBox ? item.Flex.WebkitBoxOrdinalGroup : item.Flex.Order;

    /// <summary>
    /// The used <c>align-self</c> of an item (https://drafts.csswg.org/css-align-3/#align-self-property): <c>auto</c> is the
    /// <c>align-items</c> of the container, <c>normal</c> behaves as <c>stretch</c> on a flex item.
    /// </summary>
    public Alignment GetAlignSelf(LayoutStyle item)
    {
        var alignment = IsWebkitBox ? AlignItems : item.Alignment.AlignSelf;
        if (alignment.Keyword == AlignmentKeyword.Auto)
        {
            alignment = AlignItems;
        }

        return alignment.Keyword is AlignmentKeyword.Normal or AlignmentKeyword.Auto ? alignment with { Keyword = AlignmentKeyword.Stretch } : alignment;
    }
}
