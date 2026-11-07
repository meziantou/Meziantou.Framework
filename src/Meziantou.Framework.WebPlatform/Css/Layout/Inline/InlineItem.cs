using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The kinds of <see cref="InlineItem"/>.</summary>
internal enum InlineItemKind : byte
{
    /// <summary>Characters of a text run after the white space processing and the text transformation, with one bidi level.</summary>
    Text,

    /// <summary>A forced line break (a U+000A): the line break of a <c>br</c> element or a preserved segment break.</summary>
    ForcedBreak,

    /// <summary>
    /// Characters that are not displayed and have no advance: the bidi control codes of an inline box or of the block container
    /// (https://drafts.csswg.org/css-writing-modes-4/#bidi-control-codes-injection), or the U+200B of a <c>wbr</c> element.
    /// </summary>
    Control,

    /// <summary>The start of an inline box (no character): its inline-start margin, border and padding.</summary>
    OpenTag,

    /// <summary>The end of an inline box (no character): its inline-end margin, border and padding.</summary>
    CloseTag,

    /// <summary>An atomic inline, represented by U+FFFC in the text: its fragment is laid out before the line breaking.</summary>
    Atomic,

    /// <summary>
    /// A float (no character, its <see cref="InlineItem.Fragment"/> is laid out with the items and placed when the line breaking
    /// meets it), or an absolutely positioned box (no fragment: laid out by its containing block at its static position).
    /// </summary>
    OutOfFlow,

    /// <summary>
    /// The start of a ruby column (no character, https://drafts.csswg.org/css-ruby-1/#ruby-columns): the items of its bases then
    /// of its annotations follow, up to <c>RubyColumn.EndItem</c>. The line breaking handles the column as a unit.
    /// </summary>
    RubyColumn,
}

/// <summary>Flags of an <see cref="InlineItem"/>.</summary>
[Flags]
internal enum InlineItemFlags : ushort
{
    None = 0,

    /// <summary>The spaces of the text are collapsible (https://drafts.csswg.org/css-text-4/#collapsible-white-space): removed at the start and at the end of a line.</summary>
    CollapsesSpaces = 1,

    /// <summary>The preserved spaces at the end of a line hang (https://drafts.csswg.org/css-text-4/#hang): <c>white-space-collapse: preserve</c> with wrapping.</summary>
    HangsSpaces = 2,

    /// <summary>The style of the item does not allow soft wrap opportunities (<c>text-wrap-mode: nowrap</c>).</summary>
    NoWrap = 4,

    /// <summary>A float whose position is decided: placed next to the content of its line, or below the line (the line breaking passes it).</summary>
    FloatPlaced = 8,

    /// <summary>An out-of-flow item that is a float (not an absolutely positioned box).</summary>
    Float = 16,

    /// <summary>
    /// A text item that is a preserved tab (https://drafts.csswg.org/css-text-4/#preserved-tab): its advance (<see cref="InlineItem.InlineSize"/>)
    /// is computed by the line breaking, from its position on the line and the tab stops.
    /// </summary>
    Tab = 32,

    /// <summary>
    /// An item of a ruby annotation (https://drafts.csswg.org/css-ruby-1/#ruby-annotation-box): its text is not part of the text
    /// of the line for the line breaking and the spacing, and it is placed over or under the bases of its ruby column.
    /// </summary>
    RubyAnnotation = 64,

    /// <summary>
    /// An open or close tag whose edge has a margin, a border or a padding although they add up to zero (a negative margin): its
    /// line is not a zero-height line box (https://drafts.csswg.org/css2/#inline-formatting: "no inline elements with non-zero
    /// margins, padding, or borders").
    /// </summary>
    HasEdges = 128,

    /// <summary>
    /// A text item that is combined upright in vertical text (<c>text-combine-upright</c>, https://drafts.csswg.org/css-writing-modes-4/#text-combine-upright):
    /// the text of a run with <c>all</c>, or a sequence of ASCII digits of a run with <c>digits</c>. It is shaped horizontally
    /// and laid out as a single upright glyph of 1em, without wrap opportunity inside.
    /// </summary>
    CombinedUpright = 256,

    /// <summary>
    /// An atomic inline that ends a run of consecutive atomic inlines in a box whose <c>letter-spacing</c> is not zero: its inline
    /// size includes the letter spacing after the run (https://drafts.csswg.org/css-text-4/#letter-spacing-property: "each
    /// consecutive run of atomic inlines [...] is treated as a single typographic character unit"), removed at the end of a line.
    /// </summary>
    LetterSpaced = 512,

    /// <summary>
    /// An open or close tag of an inline box with <c>box-decoration-break: clone</c> that has inline edges: its margins, borders
    /// and paddings are repeated at the start and at the end of each of its lines (https://drafts.csswg.org/css-break-4/#break-decoration).
    /// </summary>
    ClonesEdges = 1024,
}

/// <summary>
/// An item of the content of an inline formatting context (https://drafts.csswg.org/css-inline-3/#inline-formatting-context):
/// the box tree of a block container flattened into a sequence of text, control characters, atomic inlines and the starts
/// and ends of the inline boxes. <see cref="Start"/> and <see cref="End"/> are the range of the item in the text of the
/// formatting context (after the white space processing), which the line breaking and the bidi algorithm work on.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct InlineItem
{
    public InlineItemKind Kind;

    public InlineItemFlags Flags;

    /// <summary>The bidi embedding level of the characters of the item (https://www.unicode.org/reports/tr9/#BD2).</summary>
    public byte BidiLevel;

    /// <summary>The start of the item in the text of the formatting context.</summary>
    public int Start;

    /// <summary>The end of the item in the text of the formatting context (<see cref="Start"/> for the items without character).</summary>
    public int End;

    /// <summary>The box of the item: the text run of a text or a forced break, the inline box of a tag or of its control codes, the atomic inline, the out-of-flow box.</summary>
    public LayoutBox Box;

    /// <summary>
    /// The box that contains the item: the parent of a text run, of an atomic inline or of an out-of-flow box (an inline box or
    /// the block container), the inline box itself for its tags and control codes.
    /// </summary>
    public ContainerBox Container;

    /// <summary>The style of the item (of the text run, of the box).</summary>
    public ComputedStyle Style;

    /// <summary>The glyphs of a text item.</summary>
    public ShapedText? Shaped;

    /// <summary>The glyphs of a text item with the style of the first formatted line (<c>::first-line</c>), when it differs.</summary>
    public ShapedText? FirstLineShaped;

    /// <summary>The fragment of an atomic inline or of an out-of-flow box.</summary>
    public BoxFragment? Fragment;

    /// <summary>
    /// The inline size of the item, other than the text: the margin box of an atomic inline or of a float, the inline-start
    /// margin, border and padding of an open tag, the inline-end ones of a close tag (0 for the parts of an inline box split
    /// around blocks that do not have these edges); the advance of a preserved tab on the line being laid out.
    /// </summary>
    public double InlineSize;

    /// <summary>
    /// The inline size of an atomic inline or of a float for the min-content size of the content: its min-content contribution
    /// when the intrinsic sizes are computed (<see cref="InlineSize"/> is then its max-content contribution), else <see cref="InlineSize"/>.
    /// </summary>
    public double MinContentInlineSize;

    /// <summary>
    /// The index + 1 in the ruby columns of the formatting context of the innermost ruby column that contains the item (of the
    /// column itself for a <see cref="InlineItemKind.RubyColumn"/> item), or 0 outside of the ruby columns.
    /// </summary>
    public int RubyColumn;

    /// <summary>The offset in the DOM text (<see cref="TextRun.Data"/>) of the start of a text item.</summary>
    public int DomStart;

    /// <summary>The offset in the DOM text of the end of a text item (the collapsed white space at its end included).</summary>
    public int DomEnd;

    public readonly bool HasFlag(InlineItemFlags flag) => (Flags & flag) != InlineItemFlags.None;

    /// <summary>Whether the item is a float.</summary>
    public readonly bool IsFloat => Kind == InlineItemKind.OutOfFlow && HasFlag(InlineItemFlags.Float);

    /// <summary>Whether the item contains characters of the text.</summary>
    public readonly bool HasText => End > Start;
}
