namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>https://drafts.csswg.org/css-position-3/#position-property</summary>
internal enum PositionKind : byte
{
    Static,
    Relative,
    Absolute,
    Sticky,
    Fixed,
}

/// <summary>
/// https://drafts.csswg.org/css2/#float-position, with the logical values of https://drafts.csswg.org/css-logical/#float-clear.
/// The page floats (top, bottom, snap-block...) are not supported: they are <see cref="None"/>.
/// </summary>
internal enum FloatKind : byte
{
    None,
    Left,
    Right,
    InlineStart,
    InlineEnd,
}

/// <summary>https://drafts.csswg.org/css2/#flow-control, with the logical values of https://drafts.csswg.org/css-logical/#float-clear.</summary>
internal enum ClearKind : byte
{
    None,
    Left,
    Right,
    Both,
    InlineStart,
    InlineEnd,
}

/// <summary>
/// The sides of a container whose adjacent margins of its in-flow children are trimmed (https://drafts.csswg.org/css-box-4/#margin-trim):
/// the computed value of <c>margin-trim</c>.
/// </summary>
[Flags]
internal enum MarginTrimSides : byte
{
    None = 0,
    BlockStart = 1 << 0,
    BlockEnd = 1 << 1,
}

/// <summary>https://drafts.csswg.org/css-box-4/#box-sizing</summary>
internal enum BoxSizing : byte
{
    ContentBox,
    BorderBox,
}

/// <summary>https://drafts.csswg.org/css-overflow-3/#overflow-control</summary>
internal enum OverflowKind : byte
{
    Visible,
    Hidden,
    Clip,
    Scroll,
    Auto,
}

/// <summary>https://drafts.csswg.org/css-writing-modes-4/#block-flow</summary>
internal enum WritingMode : byte
{
    HorizontalTb,
    VerticalRl,
    VerticalLr,
    SidewaysRl,
    SidewaysLr,
}

/// <summary>https://drafts.csswg.org/css-writing-modes-4/#direction</summary>
internal enum Direction : byte
{
    Ltr,
    Rtl,
}

/// <summary>https://drafts.csswg.org/css-writing-modes-4/#text-orientation</summary>
internal enum TextOrientation : byte
{
    Mixed,
    Upright,
    Sideways,
}

/// <summary>https://drafts.csswg.org/css-writing-modes-4/#unicode-bidi</summary>
internal enum UnicodeBidi : byte
{
    Normal,
    Embed,
    Isolate,
    BidiOverride,
    IsolateOverride,
    Plaintext,
}

/// <summary>https://drafts.csswg.org/css-display-4/#visibility</summary>
internal enum Visibility : byte
{
    Visible,
    Hidden,
    Collapse,
    ForceHidden,
}

/// <summary>https://drafts.csswg.org/css-contain-2/#content-visibility</summary>
internal enum ContentVisibility : byte
{
    Visible,
    Auto,
    Hidden,
}

/// <summary>The containment types of contain (https://drafts.csswg.org/css-contain-2/#contain-property), strict and content expanded.</summary>
[Flags]
internal enum Containment : byte
{
    None = 0,
    Size = 1,
    InlineSize = 2,
    Layout = 4,
    Style = 8,
    Paint = 16,
}

/// <summary>https://drafts.csswg.org/css-conditional-5/#container-type</summary>
[Flags]
internal enum ContainerType : byte
{
    Normal = 0,
    Size = 1,
    InlineSize = 2,
    ScrollState = 4,

    /// <summary>An anchored query container (https://drafts.csswg.org/css-anchor-position-2/#container-type-anchored).</summary>
    Anchored = 8,
}

/// <summary>https://drafts.csswg.org/css-text-4/#white-space-collapsing</summary>
internal enum WhiteSpaceCollapse : byte
{
    Collapse,
    Discard,
    Preserve,
    PreserveBreaks,
    PreserveSpaces,
    BreakSpaces,
}

/// <summary>https://drafts.csswg.org/css-text-4/#text-wrap-mode</summary>
internal enum TextWrapMode : byte
{
    Wrap,
    NoWrap,
}

/// <summary>https://drafts.csswg.org/css-text-4/#text-wrap-style</summary>
internal enum TextWrapStyle : byte
{
    Auto,
    Balance,
    Stable,
    Pretty,
    AvoidShortLastLine,
}

/// <summary>The scaling of <c>text-fit</c> (https://drafts.csswg.org/css-text-5/#text-fit).</summary>
internal enum TextFitMode : byte
{
    None,

    /// <summary>The inline-level contents are scaled up to fit the line box.</summary>
    Grow,

    /// <summary>The inline-level contents are scaled down to fit the line box.</summary>
    Shrink,
}

/// <summary>The lines that a <c>text-fit</c> scaling factor applies to (https://drafts.csswg.org/css-text-5/#text-fit).</summary>
internal enum TextFitTarget : byte
{
    /// <summary>All the lines are scaled by the same factor.</summary>
    Consistent,

    /// <summary>Each line has its own factor; the last line and the lines that end with a forced break are not scaled.</summary>
    PerLine,

    /// <summary>Each line has its own factor.</summary>
    PerLineAll,
}

/// <summary>
/// The computed value of <c>text-fit</c> (https://drafts.csswg.org/css-text-5/#text-fit): <paramref name="Limit"/> is the limit
/// of the scaling factor (1 for 100%), or NaN when the percentage is omitted.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct TextFit(TextFitMode Mode, TextFitTarget Target, double Limit)
{
    /// <summary>
    /// Limits a scaling factor of a line: "If grow is specified and the value is 100% or greater, it is the maximum scaling
    /// factor. If shrink is specified and the value is between 0% and 100% inclusive, it is the minimum scaling factor.
    /// Otherwise [...] there is no limit"; grow does not scale down and shrink does not scale up.
    /// </summary>
    public double Clamp(double factor) => Mode switch
    {
        TextFitMode.Grow => Math.Max(1, Limit >= 1 ? Math.Min(factor, Limit) : factor),
        TextFitMode.Shrink => Math.Min(1, Limit is >= 0 and <= 1 ? Math.Max(factor, Limit) : factor),
        _ => 1,
    };
}

/// <summary>https://drafts.csswg.org/css-text-4/#white-space-trim</summary>
[Flags]
internal enum WhiteSpaceTrim : byte
{
    None = 0,
    DiscardBefore = 1,
    DiscardAfter = 2,
    DiscardInner = 4,
}

/// <summary>The text transforms of text-transform (https://drafts.csswg.org/css-text-4/#text-transform-property).</summary>
[Flags]
internal enum TextTransform : byte
{
    None = 0,
    Capitalize = 1,
    Uppercase = 2,
    Lowercase = 4,
    FullWidth = 8,
    FullSizeKana = 16,
    MathAuto = 32,
}

/// <summary>https://drafts.csswg.org/css-text-4/#word-break-property</summary>
internal enum WordBreak : byte
{
    Normal,
    BreakAll,
    KeepAll,
    Manual,
    AutoPhrase,
    BreakWord,
}

/// <summary>https://drafts.csswg.org/css-text-4/#overflow-wrap-property</summary>
internal enum OverflowWrap : byte
{
    Normal,
    BreakWord,
    Anywhere,
}

/// <summary>https://drafts.csswg.org/css-text-4/#line-break-property</summary>
internal enum LineBreak : byte
{
    Auto,
    Loose,
    Normal,
    Strict,
    Anywhere,
}

/// <summary>https://drafts.csswg.org/css-text-4/#hyphens-property</summary>
internal enum Hyphens : byte
{
    None,
    Manual,
    Auto,
}

/// <summary>
/// The values of text-align-all and text-align-last (https://drafts.csswg.org/css-text-4/#text-align-property);
/// <see cref="Auto"/> only for text-align-last. match-parent computes to a physical value.
/// </summary>
internal enum TextAlign : byte
{
    Start,
    End,
    Left,
    Right,
    Center,
    Justify,
    JustifyAll,
    MatchParent,
    Auto,

    /// <summary>A &lt;string&gt; (character-based alignment in a table column): aligned as start when it does not apply.</summary>
    String,
}

/// <summary>
/// How an element aligns its descendants (https://html.spec.whatwg.org/multipage/rendering.html#align-descendants): the
/// <c>center</c> element and the <c>align</c> attribute of the <c>div</c>, <c>thead</c>, <c>tbody</c>, <c>tfoot</c>, <c>tr</c>,
/// <c>td</c> and <c>th</c> elements.
/// </summary>
internal enum DescendantAlignment : byte
{
    None,
    Left,
    Center,
    Right,
}

/// <summary>https://drafts.csswg.org/css-text-4/#text-justify-property</summary>
internal enum TextJustify : byte
{
    Auto,
    None,
    InterWord,
    InterCharacter,
    Ruby,
}

/// <summary>https://drafts.csswg.org/css-ruby-1/#rubypos: the side of the annotations, and whether it alternates between the levels.</summary>
internal enum RubyPosition : byte
{
    /// <summary><c>alternate</c> / <c>alternate over</c> (the initial value): over for the first level, then alternating.</summary>
    AlternateOver,

    /// <summary><c>alternate under</c>: under for the first level, then alternating.</summary>
    AlternateUnder,
    Over,
    Under,

    /// <summary><c>inter-character</c>: laid out as <c>over</c> (the annotations are not placed between the characters).</summary>
    InterCharacter,
}

/// <summary>The side of the line of the emphasis marks (https://drafts.csswg.org/css-text-decor-4/#text-emphasis-position-property).</summary>
internal enum EmphasisMarksSide : byte
{
    None,
    Over,
    Under,
}

/// <summary>https://drafts.csswg.org/css-ruby-1/#ruby-align-property</summary>
internal enum RubyAlign : byte
{
    SpaceAround,
    Start,
    Center,
    SpaceBetween,
}

/// <summary>https://drafts.csswg.org/css-text-4/#text-spacing-trim-property</summary>
internal enum TextSpacingTrim : byte
{
    Normal,
    SpaceAll,
    SpaceFirst,
    TrimStart,
    TrimBoth,
    TrimAll,
    Auto,
}

/// <summary>
/// The character that replaces the expandable separators of the text (https://drafts.csswg.org/css-text-4/#word-space-transform);
/// the <c>auto-phrase</c> keyword is <see cref="TextLayoutStyle.WordSpaceTransformAutoPhrase"/>.
/// </summary>
internal enum WordSpaceTransform : byte
{
    None,
    Space,
    IdeographicSpace,
}

/// <summary>https://drafts.csswg.org/css-text-4/#text-group-align-property</summary>
internal enum TextGroupAlign : byte
{
    None,
    Start,
    End,
    Left,
    Right,
    Center,
}

/// <summary>The spacing classes of text-autospace (https://drafts.csswg.org/css-text-4/#text-autospace-property); <see cref="None"/> is no-autospace.</summary>
[Flags]
internal enum TextAutospace : byte
{
    None = 0,
    IdeographAlpha = 1,
    IdeographNumeric = 2,
    Punctuation = 4,

    /// <summary>The spaces (U+0020) at a boundary are replaced by the automatic spacing.</summary>
    Replace = 8,
}

/// <summary>https://drafts.csswg.org/css-text-4/#hanging-punctuation-property</summary>
[Flags]
internal enum HangingPunctuation : byte
{
    None = 0,
    First = 1,
    Last = 2,
    ForceEnd = 4,
    AllowEnd = 8,
}

/// <summary>https://drafts.csswg.org/css-inline-3/#text-box-trim</summary>
[Flags]
internal enum TextBoxTrim : byte
{
    None = 0,
    TrimStart = 1,
    TrimEnd = 2,
    TrimBoth = TrimStart | TrimEnd,
}

/// <summary>A metric of a &lt;text-edge&gt; (https://drafts.csswg.org/css-inline-3/#typedef-text-edge).</summary>
internal enum TextEdgeMetric : byte
{
    Text,
    Ideographic,
    IdeographicInk,
    Cap,
    Ex,
    Alphabetic,
}

/// <summary>https://drafts.csswg.org/css-lists-3/#list-style-position-property</summary>
internal enum ListStylePosition : byte
{
    Outside,
    Inside,
}

/// <summary>https://drafts.csswg.org/css-break-4/#break-decoration</summary>
internal enum BoxDecorationBreak : byte
{
    Slice,
    Clone,
}

/// <summary>https://drafts.csswg.org/css-inline-3/#alignment-baseline-property (dominant-baseline values included).</summary>
internal enum AlignmentBaseline : byte
{
    Baseline,
    TextBottom,
    Alphabetic,
    Ideographic,
    Middle,
    Central,
    Mathematical,
    TextTop,
    Hanging,

    /// <summary><c>-webkit-baseline-middle</c>: the vertical middle of the box is aligned with the baseline of its parent (WebKit and Blink).</summary>
    BaselineMiddle,
}

/// <summary>The keywords of baseline-shift (https://drafts.csswg.org/css-inline-3/#baseline-shift-property).</summary>
internal enum BaselineShiftKind : byte
{
    /// <summary>A &lt;length-percentage&gt; (the percentages refer to the line-height).</summary>
    Length,
    Sub,
    Super,
    Top,
    Center,
    Bottom,
}

/// <summary>https://drafts.csswg.org/css-inline-3/#baseline-source</summary>
internal enum BaselineSource : byte
{
    Auto,
    First,
    Last,
}

/// <summary>https://drafts.csswg.org/css-flexbox-1/#flex-direction-property</summary>
internal enum FlexDirection : byte
{
    Row,
    RowReverse,
    Column,
    ColumnReverse,
}

/// <summary>https://drafts.csswg.org/css-flexbox-1/#flex-wrap-property (the stacking of the lines: <c>balance</c> is <see cref="FlexLayoutStyle.IsBalanced"/>)</summary>
internal enum FlexWrap : byte
{
    NoWrap,
    Wrap,
    WrapReverse,
}

/// <summary>https://drafts.csswg.org/css-tables-3/#table-layout-property</summary>
internal enum TableLayout : byte
{
    Auto,
    Fixed,
}

/// <summary>https://drafts.csswg.org/css-tables-3/#border-collapse-property</summary>
internal enum BorderCollapse : byte
{
    Separate,
    Collapse,
}

/// <summary>https://drafts.csswg.org/css-tables-3/#empty-cells-property</summary>
internal enum EmptyCells : byte
{
    Show,
    Hide,
}

/// <summary>https://drafts.csswg.org/css-tables-3/#caption-side-property</summary>
internal enum CaptionSide : byte
{
    Top,
    Bottom,
}

/// <summary>https://drafts.csswg.org/css-backgrounds-3/#typedef-line-style</summary>
internal enum BorderStyle : byte
{
    None,
    Hidden,
    Dotted,
    Dashed,
    Solid,
    Double,
    Groove,
    Ridge,
    Inset,
    Outset,
}

/// <summary>https://drafts.csswg.org/css-multicol-1/#cf</summary>
internal enum ColumnFill : byte
{
    Auto,
    Balance,
    BalanceAll,
}

/// <summary>https://drafts.csswg.org/css-multicol-2/#column-span</summary>
internal enum ColumnSpan : byte
{
    None,
    All,
}

/// <summary>The used values of column-wrap (https://drafts.csswg.org/css-multicol-2/#cwr).</summary>
internal enum ColumnWrap : byte
{
    /// <summary>The overflow columns are created in the inline direction.</summary>
    NoWrap,

    /// <summary>The overflow columns create a new multicol row in the block direction.</summary>
    Wrap,
}

/// <summary>The values of break-before and break-after (https://drafts.csswg.org/css-break-4/#break-between).</summary>
internal enum BreakBetween : byte
{
    Auto,
    Avoid,
    AvoidPage,
    Page,
    Left,
    Right,
    Recto,
    Verso,
    AvoidColumn,
    Column,
    AvoidRegion,
    Region,
    Always,
    All,
}

/// <summary>https://drafts.csswg.org/css-break-4/#break-within</summary>
internal enum BreakInside : byte
{
    Auto,
    Avoid,
    AvoidPage,
    AvoidColumn,
    AvoidRegion,
}

/// <summary>https://drafts.csswg.org/css-images-3/#the-object-fit</summary>
internal enum ObjectFit : byte
{
    Fill,
    Contain,
    Cover,
    None,
    ScaleDown,
}

/// <summary>https://drafts.csswg.org/css-scrollbars-1/#scrollbar-width</summary>
internal enum ScrollbarWidth : byte
{
    Auto,
    Thin,
    None,
}

/// <summary>https://drafts.csswg.org/css-overflow-3/#scrollbar-gutter-property</summary>
internal enum ScrollbarGutter : byte
{
    Auto,
    Stable,
    StableBothEdges,
}

/// <summary>The value of a side of text-overflow (https://drafts.csswg.org/css-overflow-4/#text-overflow).</summary>
/// <summary>continue (https://drafts.csswg.org/css-overflow-4/#continue), as it applies to the line clamping.</summary>
internal enum ContinueKind : byte
{
    /// <summary>auto (normal), and the fragmentation values: no clamping.</summary>
    Auto,

    /// <summary>collapse (and an effective -webkit-legacy): the content after the clamp point is hidden, the box ends at the clamp point.</summary>
    Collapse,

    /// <summary>discard: as collapse (the content after the clamp point is not laid out in another fragmentation container).</summary>
    Discard,

    /// <summary>
    /// -webkit-legacy on a vertical <c>-webkit-box</c>: as collapse, with the legacy block overflow ellipsis of the browsers (the
    /// content of the last line that does not fit with the ellipsis is truncated instead of displaced, see <see cref="LineClamp"/>).
    /// </summary>
    WebkitLegacy,
}

/// <summary>https://drafts.csswg.org/css-rhythm/#block-step-insert: where the space added by the block step sizing goes.</summary>
internal enum BlockStepInsert : byte
{
    /// <summary>margin-box: outside of the border, as margin.</summary>
    MarginBox,

    /// <summary>padding-box: inside of the border, as padding.</summary>
    PaddingBox,

    /// <summary>content-box: in the content box, which is taller.</summary>
    ContentBox,
}

/// <summary>https://drafts.csswg.org/css-rhythm/#block-step-align</summary>
internal enum BlockStepAlign : byte
{
    Auto,
    Center,
    Start,
    End,
}

/// <summary>https://drafts.csswg.org/css-rhythm/#block-step-round</summary>
internal enum BlockStepRound : byte
{
    Up,
    Down,
    Nearest,
}

/// <summary>
/// The block-step properties of a box (https://drafts.csswg.org/css-rhythm/#block-step-size) whose <c>block-step-size</c> is not
/// <c>none</c>: the step unit (in px, 0 does not round) and how the outer block size is rounded to it.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct BlockStep(double Size, BlockStepInsert Insert, BlockStepAlign Align, BlockStepRound Round);

internal enum TextOverflowKind : byte
{
    Clip,
    Ellipsis,

    /// <summary>A &lt;string&gt;.</summary>
    String,

    /// <summary>fade or fade() (rendered as clip).</summary>
    Fade,
}

/// <summary>https://drafts.csswg.org/css-box-4/#typedef-visual-box</summary>
internal enum VisualBox : byte
{
    ContentBox,
    PaddingBox,
    BorderBox,
}

/// <summary>https://drafts.csswg.org/css-values-5/#interpolate-size</summary>
internal enum InterpolateSize : byte
{
    NumericOnly,
    AllowKeywords,
}

/// <summary>https://drafts.csswg.org/css-fonts-4/#font-style-prop</summary>
internal enum FontStyleKind : byte
{
    Normal,
    Italic,
    Oblique,
}

/// <summary>https://drafts.csswg.org/css-fonts-4/#font-kerning-prop</summary>
internal enum FontKerning : byte
{
    Auto,
    Normal,
    None,
}

/// <summary>The synthesized font features of font-synthesis (https://drafts.csswg.org/css-fonts-4/#font-synthesis).</summary>
[Flags]
internal enum FontSynthesis : byte
{
    None = 0,
    Weight = 1,
    Style = 2,
    SmallCaps = 4,
    Position = 8,
}

/// <summary>Parses the keywords of the computed values (ASCII-lowercase): an unknown keyword is the initial value.</summary>
internal static class LayoutKeywords
{
    /// <summary>https://drafts.csswg.org/css-rhythm/#block-step-insert</summary>
    public static BlockStepInsert ParseBlockStepInsert(string value) => value switch
    {
        "padding-box" => BlockStepInsert.PaddingBox,
        "content-box" => BlockStepInsert.ContentBox,
        _ => BlockStepInsert.MarginBox,
    };

    /// <summary>https://drafts.csswg.org/css-rhythm/#block-step-align</summary>
    public static BlockStepAlign ParseBlockStepAlign(string value) => value switch
    {
        "center" => BlockStepAlign.Center,
        "start" => BlockStepAlign.Start,
        "end" => BlockStepAlign.End,
        _ => BlockStepAlign.Auto,
    };

    /// <summary>https://drafts.csswg.org/css-rhythm/#block-step-round</summary>
    public static BlockStepRound ParseBlockStepRound(string value) => value switch
    {
        "down" => BlockStepRound.Down,
        "nearest" => BlockStepRound.Nearest,
        _ => BlockStepRound.Up,
    };

    public static PositionKind ParsePosition(string value) => value switch
    {
        "relative" => PositionKind.Relative,
        "absolute" => PositionKind.Absolute,
        "sticky" or "-webkit-sticky" => PositionKind.Sticky,
        "fixed" => PositionKind.Fixed,
        _ => PositionKind.Static,
    };

    public static FloatKind ParseFloat(string value) => value switch
    {
        "left" => FloatKind.Left,
        "right" => FloatKind.Right,
        "inline-start" => FloatKind.InlineStart,
        "inline-end" => FloatKind.InlineEnd,
        _ => FloatKind.None,
    };

    public static ClearKind ParseClear(string value) => value switch
    {
        "left" => ClearKind.Left,
        "right" => ClearKind.Right,
        "both" or "both-inline" => ClearKind.Both,
        "inline-start" => ClearKind.InlineStart,
        "inline-end" => ClearKind.InlineEnd,
        _ => ClearKind.None,
    };

    /// <summary>Parses a computed value of <c>margin-trim</c> (https://drafts.csswg.org/css-box-4/#margin-trim): <c>none | block | [ block-start || block-end ]</c>.</summary>
    public static MarginTrimSides ParseMarginTrim(string value) => value switch
    {
        "block" or "block-start block-end" or "block-end block-start" => MarginTrimSides.BlockStart | MarginTrimSides.BlockEnd,
        "block-start" => MarginTrimSides.BlockStart,
        "block-end" => MarginTrimSides.BlockEnd,
        _ => MarginTrimSides.None,
    };

    public static OverflowKind ParseOverflow(string value) => value switch
    {
        "hidden" => OverflowKind.Hidden,
        "clip" => OverflowKind.Clip,
        "scroll" => OverflowKind.Scroll,
        "auto" or "overlay" => OverflowKind.Auto,
        _ => OverflowKind.Visible,
    };

    public static WritingMode ParseWritingMode(string value) => value switch
    {
        "vertical-rl" => WritingMode.VerticalRl,
        "vertical-lr" => WritingMode.VerticalLr,
        "sideways-rl" => WritingMode.SidewaysRl,
        "sideways-lr" => WritingMode.SidewaysLr,
        _ => WritingMode.HorizontalTb,
    };

    public static TextOrientation ParseTextOrientation(string value) => value switch
    {
        "upright" => TextOrientation.Upright,
        "sideways" or "sideways-right" => TextOrientation.Sideways,
        _ => TextOrientation.Mixed,
    };

    public static UnicodeBidi ParseUnicodeBidi(string value) => value switch
    {
        "embed" => UnicodeBidi.Embed,
        "isolate" => UnicodeBidi.Isolate,
        "bidi-override" => UnicodeBidi.BidiOverride,
        "isolate-override" => UnicodeBidi.IsolateOverride,
        "plaintext" => UnicodeBidi.Plaintext,
        _ => UnicodeBidi.Normal,
    };

    public static Visibility ParseVisibility(string value) => value switch
    {
        "hidden" => Visibility.Hidden,
        "collapse" => Visibility.Collapse,
        "force-hidden" => Visibility.ForceHidden,
        _ => Visibility.Visible,
    };

    public static ContentVisibility ParseContentVisibility(string value) => value switch
    {
        "auto" => ContentVisibility.Auto,
        "hidden" => ContentVisibility.Hidden,
        _ => ContentVisibility.Visible,
    };

    /// <summary>https://drafts.csswg.org/css-contain-2/#contain-property: strict is size layout paint style, content is layout paint style.</summary>
    public static Containment ParseContain(string value)
    {
        switch (value)
        {
            case "none":
                return Containment.None;
            case "strict":
                return Containment.Size | Containment.Layout | Containment.Paint | Containment.Style;
            case "content":
                return Containment.Layout | Containment.Paint | Containment.Style;
        }

        var result = Containment.None;
        foreach (var keyword in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            result |= keyword switch
            {
                "size" => Containment.Size,
                "inline-size" => Containment.InlineSize,
                "layout" => Containment.Layout,
                "style" => Containment.Style,
                "paint" => Containment.Paint,
                "strict" => Containment.Size | Containment.Layout | Containment.Paint | Containment.Style,
                "content" => Containment.Layout | Containment.Paint | Containment.Style,
                _ => Containment.None,
            };
        }

        return result;
    }

    public static ContainerType ParseContainerType(string value)
    {
        if (value == "normal")
            return ContainerType.Normal;

        var result = ContainerType.Normal;
        foreach (var keyword in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            result |= keyword switch
            {
                "size" => ContainerType.Size,
                "inline-size" => ContainerType.InlineSize,
                "scroll-state" => ContainerType.ScrollState,
                "anchored" => ContainerType.Anchored,
                _ => ContainerType.Normal,
            };
        }

        return result;
    }

    public static WhiteSpaceCollapse ParseWhiteSpaceCollapse(string value) => value switch
    {
        "discard" => WhiteSpaceCollapse.Discard,
        "preserve" => WhiteSpaceCollapse.Preserve,
        "preserve-breaks" => WhiteSpaceCollapse.PreserveBreaks,
        "preserve-spaces" => WhiteSpaceCollapse.PreserveSpaces,
        "break-spaces" => WhiteSpaceCollapse.BreakSpaces,
        _ => WhiteSpaceCollapse.Collapse,
    };

    public static TextWrapStyle ParseTextWrapStyle(string value) => value switch
    {
        "balance" => TextWrapStyle.Balance,
        "stable" => TextWrapStyle.Stable,
        "pretty" => TextWrapStyle.Pretty,
        "avoid-short-last-line" => TextWrapStyle.AvoidShortLastLine,
        _ => TextWrapStyle.Auto,
    };

    /// <summary>Parses a computed value of <c>text-fit</c>: <c>[ none | grow | shrink ] [ consistent | per-line | per-line-all ]? &lt;percentage&gt;?</c>.</summary>
    public static TextFit ParseTextFit(string value)
    {
        if (value is "none" or "")
            return default;

        var mode = TextFitMode.None;
        var target = TextFitTarget.Consistent;
        var limit = double.NaN;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part)
            {
                case "grow":
                    mode = TextFitMode.Grow;
                    break;
                case "shrink":
                    mode = TextFitMode.Shrink;
                    break;
                case "per-line":
                    target = TextFitTarget.PerLine;
                    break;
                case "per-line-all":
                    target = TextFitTarget.PerLineAll;
                    break;
                default:
                    if (part.EndsWith('%', StringComparison.Ordinal) && double.TryParse(part.AsSpan(0, part.Length - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percentage))
                    {
                        limit = percentage / 100;
                    }

                    break;
            }
        }

        return new TextFit(mode, target, limit);
    }

    public static WhiteSpaceTrim ParseWhiteSpaceTrim(string value)
    {
        var result = WhiteSpaceTrim.None;
        foreach (var keyword in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            result |= keyword switch
            {
                "discard-before" => WhiteSpaceTrim.DiscardBefore,
                "discard-after" => WhiteSpaceTrim.DiscardAfter,
                "discard-inner" => WhiteSpaceTrim.DiscardInner,
                _ => WhiteSpaceTrim.None,
            };
        }

        return result;
    }

    public static TextTransform ParseTextTransform(string value)
    {
        switch (value)
        {
            case "none":
                return TextTransform.None;
            case "uppercase":
                return TextTransform.Uppercase;
            case "lowercase":
                return TextTransform.Lowercase;
            case "capitalize":
                return TextTransform.Capitalize;
        }

        var result = TextTransform.None;
        foreach (var keyword in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            result |= keyword switch
            {
                "capitalize" => TextTransform.Capitalize,
                "uppercase" => TextTransform.Uppercase,
                "lowercase" => TextTransform.Lowercase,
                "full-width" => TextTransform.FullWidth,
                "full-size-kana" => TextTransform.FullSizeKana,
                "math-auto" => TextTransform.MathAuto,
                _ => TextTransform.None,
            };
        }

        return result;
    }

    public static WordBreak ParseWordBreak(string value) => value switch
    {
        "break-all" => WordBreak.BreakAll,
        "keep-all" => WordBreak.KeepAll,
        "manual" => WordBreak.Manual,
        "auto-phrase" => WordBreak.AutoPhrase,
        "break-word" => WordBreak.BreakWord,
        _ => WordBreak.Normal,
    };

    public static OverflowWrap ParseOverflowWrap(string value) => value switch
    {
        "break-word" => OverflowWrap.BreakWord,
        "anywhere" => OverflowWrap.Anywhere,
        _ => OverflowWrap.Normal,
    };

    public static LineBreak ParseLineBreak(string value) => value switch
    {
        "loose" => LineBreak.Loose,
        "normal" => LineBreak.Normal,
        "strict" => LineBreak.Strict,
        "anywhere" => LineBreak.Anywhere,
        _ => LineBreak.Auto,
    };

    public static Hyphens ParseHyphens(string value) => value switch
    {
        "none" => Hyphens.None,
        "auto" => Hyphens.Auto,
        _ => Hyphens.Manual,
    };

    public static TextAlign ParseTextAlign(string value) => value switch
    {
        "start" => TextAlign.Start,
        "end" => TextAlign.End,
        "left" or "-webkit-left" => TextAlign.Left,
        "right" or "-webkit-right" => TextAlign.Right,
        "center" or "-webkit-center" => TextAlign.Center,
        "justify" => TextAlign.Justify,
        "justify-all" => TextAlign.JustifyAll,
        "match-parent" => TextAlign.MatchParent,
        "auto" => TextAlign.Auto,
        _ when value.StartsWith('"', StringComparison.Ordinal) => TextAlign.String,
        _ => TextAlign.Start,
    };

    public static TextJustify ParseTextJustify(string value)
    {
        // no-compress only affects the compression of the spaces
        foreach (var keyword in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (keyword)
            {
                case "none":
                    return TextJustify.None;
                case "inter-word":
                    return TextJustify.InterWord;
                case "inter-character" or "distribute":
                    return TextJustify.InterCharacter;
                case "ruby":
                    return TextJustify.Ruby;
            }
        }

        return TextJustify.Auto;
    }

    /// <summary>Parses <c>ruby-position</c>: <c>[ alternate || [ over | under ] ] | inter-character</c>.</summary>
    public static RubyPosition ParseRubyPosition(string value)
    {
        if (value == "inter-character")
            return RubyPosition.InterCharacter;

        var alternate = value.Contains("alternate", StringComparison.Ordinal);
        var under = value.Contains("under", StringComparison.Ordinal);
        if (alternate)
            return under ? RubyPosition.AlternateUnder : RubyPosition.AlternateOver;

        return under ? RubyPosition.Under : RubyPosition.Over;
    }

    public static RubyAlign ParseRubyAlign(string value) => value switch
    {
        "start" => RubyAlign.Start,
        "center" => RubyAlign.Center,
        "space-between" => RubyAlign.SpaceBetween,
        _ => RubyAlign.SpaceAround,
    };

    /// <summary>Parses word-space-transform: <c>none | [ space | ideographic-space ] &amp;&amp; auto-phrase?</c>.</summary>
    public static WordSpaceTransform ParseWordSpaceTransform(string value)
    {
        if (value is "none" or "")
            return WordSpaceTransform.None;

        return value.Contains("ideographic-space", StringComparison.Ordinal) ? WordSpaceTransform.IdeographicSpace
            : value.Contains("space", StringComparison.Ordinal) ? WordSpaceTransform.Space
            : WordSpaceTransform.None;
    }

    public static TextGroupAlign ParseTextGroupAlign(string value) => value switch
    {
        "start" => TextGroupAlign.Start,
        "end" => TextGroupAlign.End,
        "left" => TextGroupAlign.Left,
        "right" => TextGroupAlign.Right,
        "center" => TextGroupAlign.Center,
        _ => TextGroupAlign.None,
    };

    public static TextSpacingTrim ParseTextSpacingTrim(string value) => value switch
    {
        "space-all" => TextSpacingTrim.SpaceAll,
        "space-first" => TextSpacingTrim.SpaceFirst,
        "trim-start" => TextSpacingTrim.TrimStart,
        "trim-both" => TextSpacingTrim.TrimBoth,
        "trim-all" => TextSpacingTrim.TrimAll,
        "auto" => TextSpacingTrim.Auto,
        _ => TextSpacingTrim.Normal,
    };

    /// <summary>
    /// Parses text-autospace: <c>normal</c> is <c>ideograph-alpha ideograph-numeric</c>, and <c>auto</c> is the same set (the
    /// "typographically high quality spacing values" of the user agent).
    /// </summary>
    public static TextAutospace ParseTextAutospace(string value)
    {
        if (value is "normal" or "auto")
            return TextAutospace.IdeographAlpha | TextAutospace.IdeographNumeric;

        var result = TextAutospace.None;
        var hasClass = false;
        foreach (var keyword in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (keyword)
            {
                case "ideograph-alpha":
                    result |= TextAutospace.IdeographAlpha;
                    hasClass = true;
                    break;
                case "ideograph-numeric":
                    result |= TextAutospace.IdeographNumeric;
                    hasClass = true;
                    break;
                case "punctuation":
                    result |= TextAutospace.Punctuation;
                    hasClass = true;
                    break;
                case "replace":
                    result |= TextAutospace.Replace;
                    break;
            }
        }

        // "[ insert | replace ]" alone keeps the spacing classes of normal
        return hasClass || result == TextAutospace.None ? result : result | TextAutospace.IdeographAlpha | TextAutospace.IdeographNumeric;
    }

    public static HangingPunctuation ParseHangingPunctuation(string value)
    {
        var result = HangingPunctuation.None;
        foreach (var keyword in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            result |= keyword switch
            {
                "first" => HangingPunctuation.First,
                "last" => HangingPunctuation.Last,
                "force-end" => HangingPunctuation.ForceEnd,
                "allow-end" => HangingPunctuation.AllowEnd,
                _ => HangingPunctuation.None,
            };
        }

        return result;
    }

    public static TextBoxTrim ParseTextBoxTrim(string value) => value switch
    {
        "trim-start" => TextBoxTrim.TrimStart,
        "trim-end" => TextBoxTrim.TrimEnd,
        "trim-both" => TextBoxTrim.TrimBoth,
        _ => TextBoxTrim.None,
    };

    /// <summary>Parses a keyword of a &lt;text-edge&gt;, or null.</summary>
    public static TextEdgeMetric? ParseTextEdgeMetric(string value) => value switch
    {
        "text" => TextEdgeMetric.Text,
        "ideographic" => TextEdgeMetric.Ideographic,
        "ideographic-ink" => TextEdgeMetric.IdeographicInk,
        "cap" => TextEdgeMetric.Cap,
        "ex" => TextEdgeMetric.Ex,
        "alphabetic" => TextEdgeMetric.Alphabetic,
        _ => null,
    };

    public static AlignmentBaseline ParseAlignmentBaseline(string value) => value switch
    {
        "text-bottom" => AlignmentBaseline.TextBottom,
        "alphabetic" => AlignmentBaseline.Alphabetic,
        "ideographic" => AlignmentBaseline.Ideographic,
        "middle" => AlignmentBaseline.Middle,
        "central" => AlignmentBaseline.Central,
        "mathematical" => AlignmentBaseline.Mathematical,
        "text-top" => AlignmentBaseline.TextTop,
        "hanging" => AlignmentBaseline.Hanging,
        "-webkit-baseline-middle" => AlignmentBaseline.BaselineMiddle,
        _ => AlignmentBaseline.Baseline,
    };

    public static BaselineSource ParseBaselineSource(string value) => value switch
    {
        "first" => BaselineSource.First,
        "last" => BaselineSource.Last,
        _ => BaselineSource.Auto,
    };

    public static FlexDirection ParseFlexDirection(string value) => value switch
    {
        "row-reverse" => FlexDirection.RowReverse,
        "column" => FlexDirection.Column,
        "column-reverse" => FlexDirection.ColumnReverse,
        _ => FlexDirection.Row,
    };

    /// <summary>
    /// Parses a computed <c>flex-wrap</c> (https://drafts.csswg.org/css-flexbox-2/#flex-wrap-property,
    /// <c>nowrap | [ wrap | wrap-reverse ] || balance</c>): <c>balance</c> alone behaves "as if wrap was specified".
    /// </summary>
    public static FlexWrap ParseFlexWrap(string value, out bool balance)
    {
        balance = false;
        switch (value)
        {
            case "nowrap":
                return FlexWrap.NoWrap;
            case "wrap":
                return FlexWrap.Wrap;
            case "wrap-reverse":
                return FlexWrap.WrapReverse;
        }

        var wrap = FlexWrap.NoWrap;
        foreach (var keyword in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (keyword)
            {
                case "wrap":
                    wrap = FlexWrap.Wrap;
                    break;
                case "wrap-reverse":
                    wrap = FlexWrap.WrapReverse;
                    break;
                case "balance":
                    balance = true;
                    break;
            }
        }

        return balance && wrap == FlexWrap.NoWrap ? FlexWrap.Wrap : wrap;
    }

    public static BorderStyle ParseBorderStyle(string value) => value switch
    {
        "hidden" => BorderStyle.Hidden,
        "dotted" => BorderStyle.Dotted,
        "dashed" => BorderStyle.Dashed,
        "solid" => BorderStyle.Solid,
        "double" => BorderStyle.Double,
        "groove" => BorderStyle.Groove,
        "ridge" => BorderStyle.Ridge,
        "inset" => BorderStyle.Inset,
        "outset" => BorderStyle.Outset,
        _ => BorderStyle.None,
    };

    public static ColumnFill ParseColumnFill(string value) => value switch
    {
        "auto" => ColumnFill.Auto,
        "balance-all" => ColumnFill.BalanceAll,
        _ => ColumnFill.Balance,
    };

    public static BreakBetween ParseBreakBetween(string value) => value switch
    {
        "avoid" => BreakBetween.Avoid,
        "avoid-page" => BreakBetween.AvoidPage,
        "page" => BreakBetween.Page,
        "left" => BreakBetween.Left,
        "right" => BreakBetween.Right,
        "recto" => BreakBetween.Recto,
        "verso" => BreakBetween.Verso,
        "avoid-column" => BreakBetween.AvoidColumn,
        "column" => BreakBetween.Column,
        "avoid-region" => BreakBetween.AvoidRegion,
        "region" => BreakBetween.Region,
        "always" => BreakBetween.Always,
        "all" => BreakBetween.All,
        _ => BreakBetween.Auto,
    };

    public static BreakInside ParseBreakInside(string value) => value switch
    {
        "avoid" => BreakInside.Avoid,
        "avoid-page" => BreakInside.AvoidPage,
        "avoid-column" => BreakInside.AvoidColumn,
        "avoid-region" => BreakInside.AvoidRegion,
        _ => BreakInside.Auto,
    };

    public static ObjectFit ParseObjectFit(string value) => value switch
    {
        "contain" => ObjectFit.Contain,
        "cover" => ObjectFit.Cover,
        "none" => ObjectFit.None,
        "scale-down" or "contain scale-down" or "scale-down contain" => ObjectFit.ScaleDown,

        // https://drafts.csswg.org/css-images-4/#valdef-object-fit-scale-down: cover scale-down is cover or none, whichever is smaller (approximated by cover)
        "cover scale-down" or "scale-down cover" => ObjectFit.Cover,
        _ => ObjectFit.Fill,
    };

    public static VisualBox? ParseVisualBox(string value) => value switch
    {
        "content-box" => VisualBox.ContentBox,
        "padding-box" => VisualBox.PaddingBox,
        "border-box" => VisualBox.BorderBox,
        _ => null,
    };

    public static TextOverflowKind ParseTextOverflowSide(string value) => value switch
    {
        "clip" => TextOverflowKind.Clip,
        "ellipsis" => TextOverflowKind.Ellipsis,
        _ when value.StartsWith('"', StringComparison.Ordinal) || value.StartsWith('\'', StringComparison.Ordinal) => TextOverflowKind.String,
        _ when value.StartsWith("fade", StringComparison.Ordinal) => TextOverflowKind.Fade,
        _ => TextOverflowKind.Clip,
    };

    public static FontKerning ParseFontKerning(string value) => value switch
    {
        "normal" => FontKerning.Normal,
        "none" => FontKerning.None,
        _ => FontKerning.Auto,
    };

    public static FontSynthesis ParseFontSynthesisFlag(string value, FontSynthesis flag) => value == "none" ? FontSynthesis.None : flag;
}
