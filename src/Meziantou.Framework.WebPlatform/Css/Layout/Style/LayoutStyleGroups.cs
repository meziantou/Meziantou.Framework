using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The groups of properties of LayoutStyle: each group is converted at once, on first access. The lengths are in CSS px,
// multiplied by the effective zoom; the percentages are resolved by the layout.

/// <summary>
/// The groups of layout properties shared by the layout styles of a style engine: most elements have the same box model (no
/// size, margin, padding nor border), the same text properties (they are inherited) and the same fragmentation properties, so
/// a group equal to a group of another style is replaced by it. The groups are immutable values (records).
/// </summary>
internal sealed class LayoutStyleGroupTable
{
    // A document has few distinct groups; the table is emptied when it is full (e.g. the styles of an animation), the groups
    // already shared stay valid
    private const int MaxCount = 4096;

    private readonly HashSet<object> _groups = [];

    /// <summary>Returns the group of the table that is equal to <paramref name="group"/>, or adds it to the table.</summary>
    public T Share<T>(T group)
        where T : class, IEquatable<T>
    {
        if (_groups.TryGetValue(group, out var existing))
            return (T)existing;

        if (_groups.Count >= MaxCount)
        {
            _groups.Clear();
        }

        _groups.Add(group);
        return group;
    }
}

/// <summary>The box model (https://drafts.csswg.org/css-box-4/, https://drafts.csswg.org/css-sizing-3/): sizes, margins, paddings and borders.</summary>
internal sealed record BoxModelStyle
{
    internal BoxModelStyle(ComputedStyle style, double zoom)
    {
        Width = Size.Parse(style.GetComputedValue("width"), zoom);
        Height = Size.Parse(style.GetComputedValue("height"), zoom);
        MinWidth = Size.Parse(style.GetComputedValue("min-width"), zoom);
        MinHeight = Size.Parse(style.GetComputedValue("min-height"), zoom);
        MaxWidth = Size.Parse(style.GetComputedValue("max-width"), zoom, SizeKind.None);
        MaxHeight = Size.Parse(style.GetComputedValue("max-height"), zoom, SizeKind.None);
        Margin = new(
            LengthPercentageAuto.Parse(style.GetComputedValue("margin-top"), zoom),
            LengthPercentageAuto.Parse(style.GetComputedValue("margin-right"), zoom),
            LengthPercentageAuto.Parse(style.GetComputedValue("margin-bottom"), zoom),
            LengthPercentageAuto.Parse(style.GetComputedValue("margin-left"), zoom));
        Padding = new(
            ParsePadding(style.GetComputedValue("padding-top"), zoom),
            ParsePadding(style.GetComputedValue("padding-right"), zoom),
            ParsePadding(style.GetComputedValue("padding-bottom"), zoom),
            ParsePadding(style.GetComputedValue("padding-left"), zoom));
        BorderStyle = new(
            LayoutKeywords.ParseBorderStyle(style.GetComputedValue("border-top-style")),
            LayoutKeywords.ParseBorderStyle(style.GetComputedValue("border-right-style")),
            LayoutKeywords.ParseBorderStyle(style.GetComputedValue("border-bottom-style")),
            LayoutKeywords.ParseBorderStyle(style.GetComputedValue("border-left-style")));

        // https://drafts.csswg.org/css-backgrounds/#border-width: the used width is 0 when the style is none or hidden
        BorderWidth = new(
            BorderStyle.Top is Layout.BorderStyle.None or Layout.BorderStyle.Hidden ? 0 : ParseFixedLength(style.GetComputedValue("border-top-width"), zoom),
            BorderStyle.Right is Layout.BorderStyle.None or Layout.BorderStyle.Hidden ? 0 : ParseFixedLength(style.GetComputedValue("border-right-width"), zoom),
            BorderStyle.Bottom is Layout.BorderStyle.None or Layout.BorderStyle.Hidden ? 0 : ParseFixedLength(style.GetComputedValue("border-bottom-width"), zoom),
            BorderStyle.Left is Layout.BorderStyle.None or Layout.BorderStyle.Hidden ? 0 : ParseFixedLength(style.GetComputedValue("border-left-width"), zoom));
        AspectRatio = AspectRatio.Parse(style.GetComputedValue("aspect-ratio"));
        ContainIntrinsicWidth = ContainIntrinsicSize.Parse(style.GetComputedValue("contain-intrinsic-width"), zoom);
        ContainIntrinsicHeight = ContainIntrinsicSize.Parse(style.GetComputedValue("contain-intrinsic-height"), zoom);

        // The root element of a scoped view transition whose rendering is suppressed has the size of its old state (see LayoutStyle.Contain)
        if (style.PseudoElement is null && style.Element.NodeDocument.GetScopedViewTransition(style.Element) is { FrozenFrame: { } frozen })
        {
            ContainIntrinsicWidth = new ContainIntrinsicSize(IsAuto: false, frozen.ContentSize.Width);
            ContainIntrinsicHeight = new ContainIntrinsicSize(IsAuto: false, frozen.ContentSize.Height);
        }
        InterpolateSize = style.GetComputedValue("interpolate-size") == "allow-keywords" ? InterpolateSize.AllowKeywords : InterpolateSize.NumericOnly;
    }

    /// <summary>https://drafts.csswg.org/css-sizing-3/#preferred-size-properties</summary>
    public Size Width { get; }

    public Size Height { get; }

    /// <summary>https://drafts.csswg.org/css-sizing-3/#min-size-properties (auto is the automatic minimum size).</summary>
    public Size MinWidth { get; }

    public Size MinHeight { get; }

    /// <summary>https://drafts.csswg.org/css-sizing-3/#max-size-properties (none when there is no maximum).</summary>
    public Size MaxWidth { get; }

    public Size MaxHeight { get; }

    /// <summary>The margins (https://drafts.csswg.org/css-box-4/#margins): their percentages refer to the inline size of the containing block.</summary>
    public Sides<LengthPercentageAuto> Margin { get; }

    /// <summary>The paddings (https://drafts.csswg.org/css-box-4/#paddings): their percentages refer to the inline size of the containing block.</summary>
    public Sides<LengthPercentage> Padding { get; }

    /// <summary>The used border widths (0 when the border style is none or hidden).</summary>
    public Sides<double> BorderWidth { get; }

    public Sides<BorderStyle> BorderStyle { get; }

    /// <summary>https://drafts.csswg.org/css-sizing-4/#aspect-ratio</summary>
    public AspectRatio AspectRatio { get; }

    /// <summary>https://drafts.csswg.org/css-sizing-4/#intrinsic-size-override</summary>
    public ContainIntrinsicSize ContainIntrinsicWidth { get; }

    public ContainIntrinsicSize ContainIntrinsicHeight { get; }

    /// <summary>https://drafts.csswg.org/css-values-5/#interpolate-size</summary>
    public InterpolateSize InterpolateSize { get; }

    /// <summary>Returns the size of a physical axis (width or height).</summary>
    public Size GetSize(bool horizontal) => horizontal ? Width : Height;

    public Size GetMinSize(bool horizontal) => horizontal ? MinWidth : MinHeight;

    public Size GetMaxSize(bool horizontal) => horizontal ? MaxWidth : MaxHeight;

    /// <summary>The sum of the paddings and of the borders of a physical axis, or null when a percentage padding has no basis.</summary>
    public double? GetPaddingAndBorder(bool horizontal, double? percentageBasis)
    {
        var (paddingStart, paddingEnd, borderStart, borderEnd) = horizontal
            ? (Padding.Left, Padding.Right, BorderWidth.Left, BorderWidth.Right)
            : (Padding.Top, Padding.Bottom, BorderWidth.Top, BorderWidth.Bottom);
        return paddingStart.TryResolve(percentageBasis) + paddingEnd.TryResolve(percentageBasis) + borderStart + borderEnd;
    }

    private static LengthPercentage ParsePadding(string value, double zoom) => LengthPercentage.TryParse(value, zoom, nonNegative: true, out var result) ? result : LengthPercentage.Zero;

    /// <summary>Parses a computed length in px (multiplied by the zoom), or 0.</summary>
    internal static double ParseFixedLength(string value, double zoom) => LengthPercentage.TryParse(value, zoom, nonNegative: false, out var result) && result.IsFixed ? result.Length : 0;
}

/// <summary>The insets of the positioned boxes (https://drafts.csswg.org/css-position-3/#insets) and z-index.</summary>
internal sealed record InsetStyle
{
    internal InsetStyle(ComputedStyle style, double zoom)
    {
        Inset = new(
            LengthPercentageAuto.Parse(style.GetComputedValue("top"), zoom),
            LengthPercentageAuto.Parse(style.GetComputedValue("right"), zoom),
            LengthPercentageAuto.Parse(style.GetComputedValue("bottom"), zoom),
            LengthPercentageAuto.Parse(style.GetComputedValue("left"), zoom));
        // The integers outside of the range of the implementation are clamped (https://drafts.csswg.org/css-values-4/#numeric-ranges,
        // WPT css/CSS2/zindex/z-index-001.xht)
        ZIndex = double.TryParse(style.GetComputedValue("z-index"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var zIndex) ? (int)Math.Clamp(zIndex, int.MinValue, int.MaxValue) : null;
    }

    /// <summary>top, right, bottom and left: the percentages refer to the size of the containing block in the same axis.</summary>
    public Sides<LengthPercentageAuto> Inset { get; }

    /// <summary>https://drafts.csswg.org/css2/#z-index: null for auto.</summary>
    public int? ZIndex { get; }
}

/// <summary>The properties of the inline layout (https://drafts.csswg.org/css-inline-3/) and of the text (https://drafts.csswg.org/css-text-4/).</summary>
internal sealed record TextLayoutStyle
{
    internal TextLayoutStyle(ComputedStyle style, double zoom, double fontSize)
    {
        TextAlign = LayoutKeywords.ParseTextAlign(style.GetComputedValue("text-align-all"));
        TextAlignLast = LayoutKeywords.ParseTextAlign(style.GetComputedValue("text-align-last"));
        TextJustify = LayoutKeywords.ParseTextJustify(style.GetComputedValue("text-justify"));

        // https://drafts.csswg.org/css-text-3/#text-indent-property: <length-percentage> && hanging? && each-line?
        var indent = LengthPercentage.Zero;
        foreach (var part in CssValueComputer.SplitComponents(style.GetComputedValue("text-indent")))
        {
            switch (part)
            {
                case "hanging":
                    TextIndentHanging = true;
                    break;
                case "each-line":
                    TextIndentEachLine = true;
                    break;
                default:
                    if (LengthPercentage.TryParse(part, zoom, nonNegative: false, out var length))
                    {
                        indent = length;
                    }

                    break;
            }
        }

        TextIndent = indent;
        TextWrapStyle = LayoutKeywords.ParseTextWrapStyle(style.GetComputedValue("text-wrap-style"));
        TextFit = LayoutKeywords.ParseTextFit(style.GetComputedValue("text-fit"));
        WhiteSpaceTrim = LayoutKeywords.ParseWhiteSpaceTrim(style.GetComputedValue("white-space-trim"));
        WordBreak = LayoutKeywords.ParseWordBreak(style.GetComputedValue("word-break"));
        OverflowWrap = LayoutKeywords.ParseOverflowWrap(style.GetComputedValue("overflow-wrap"));
        LineBreak = LayoutKeywords.ParseLineBreak(style.GetComputedValue("line-break"));
        Hyphens = LayoutKeywords.ParseHyphens(style.GetComputedValue("hyphens"));
        var hyphenateCharacter = style.GetComputedValue("hyphenate-character");
        HyphenateCharacter = hyphenateCharacter == "auto" ? null : ParseString(hyphenateCharacter);

        // https://drafts.csswg.org/css-text-4/#letter-spacing-property: normal is 0, the percentages refer to the font size
        LetterSpacing = ParseSpacing(style.GetComputedValue("letter-spacing"), zoom, fontSize);
        WordSpacing = ParseSpacing(style.GetComputedValue("word-spacing"), zoom, fontSize);
        TabSize = TabSize.Parse(style.GetComputedValue("tab-size"), zoom);
        VerticalAlign = ParseVerticalAlign(style, zoom);
        ListStylePosition = style.GetComputedValue("list-style-position") == "inside" ? ListStylePosition.Inside : ListStylePosition.Outside;
        TextCombineUpright = TextCombineUpright.Parse(style.GetComputedValue("text-combine-upright"));
        TextSpacingTrim = LayoutKeywords.ParseTextSpacingTrim(style.GetComputedValue("text-spacing-trim"));
        TextAutospace = LayoutKeywords.ParseTextAutospace(style.GetComputedValue("text-autospace"));
        var wordSpaceTransform = style.GetComputedValue("word-space-transform");
        WordSpaceTransform = LayoutKeywords.ParseWordSpaceTransform(wordSpaceTransform);
        WordSpaceTransformAutoPhrase = WordSpaceTransform != WordSpaceTransform.None && wordSpaceTransform.Contains("auto-phrase", StringComparison.Ordinal);
        HangingPunctuation = LayoutKeywords.ParseHangingPunctuation(style.GetComputedValue("hanging-punctuation"));
        HyphenateLimitChars = HyphenateLimitChars.Parse(style.GetComputedValue("hyphenate-limit-chars"));
        var limitLines = style.GetComputedValue("hyphenate-limit-lines");
        HyphenateLimitLines = limitLines != "no-limit" && LengthPercentage.TryParseNumber(limitLines, out var lines) ? (int)Math.Max(0, lines) : null;
        HyphenateLimitLastAlways = style.GetComputedValue("hyphenate-limit-last") == "always";
        TextBoxTrim = LayoutKeywords.ParseTextBoxTrim(style.GetComputedValue("text-box-trim"));
        TextBoxEdge = TextEdge.Parse(style.GetComputedValue("text-box-edge"), style.GetComputedValue("line-fit-edge"));
        RubyPosition = LayoutKeywords.ParseRubyPosition(style.GetComputedValue("ruby-position"));
        RubyAlign = LayoutKeywords.ParseRubyAlign(style.GetComputedValue("ruby-align"));
        RubyOverhangSpaces = style.GetComputedValue("ruby-overhang") == "spaces";
        if (style.GetComputedValue("text-emphasis-style") is not ("none" or ""))
        {
            // https://drafts.csswg.org/css-text-decor-4/#text-emphasis-position-property: over/under in horizontal writing modes,
            // right (the line-over side)/left in vertical writing modes
            var position = style.GetComputedValue("text-emphasis-position");
            var under = style.Layout.IsHorizontalWritingMode ? position.Contains("under", StringComparison.Ordinal) : position.Contains("left", StringComparison.Ordinal);
            EmphasisMarks = under ? EmphasisMarksSide.Under : EmphasisMarksSide.Over;
        }
    }

    /// <summary>text-align-all (the text-align of the lines other than the last one).</summary>
    public TextAlign TextAlign { get; }

    /// <summary>text-align-last (auto: like text-align-all, except justify which is start).</summary>
    public TextAlign TextAlignLast { get; }

    public TextJustify TextJustify { get; }

    /// <summary>https://drafts.csswg.org/css-text-3/#text-indent-property: the percentages refer to the inline size of the block container.</summary>
    public LengthPercentage TextIndent { get; }

    public bool TextIndentHanging { get; }

    public bool TextIndentEachLine { get; }

    public TextWrapStyle TextWrapStyle { get; }

    public TextFit TextFit { get; }

    public WhiteSpaceTrim WhiteSpaceTrim { get; }

    public WordBreak WordBreak { get; }

    public OverflowWrap OverflowWrap { get; }

    public LineBreak LineBreak { get; }

    public Hyphens Hyphens { get; }

    /// <summary>The string of hyphenate-character, or null for auto (https://drafts.csswg.org/css-text-4/#hyphenate-character).</summary>
    public string? HyphenateCharacter { get; }

    /// <summary>The used letter spacing in px (normal is 0).</summary>
    public double LetterSpacing { get; }

    /// <summary>The used word spacing in px (normal is 0).</summary>
    public double WordSpacing { get; }

    public TabSize TabSize { get; }

    public VerticalAlign VerticalAlign { get; }

    public ListStylePosition ListStylePosition { get; }

    public TextCombineUpright TextCombineUpright { get; }

    /// <summary>https://drafts.csswg.org/css-text-4/#text-spacing-trim-property</summary>
    public TextSpacingTrim TextSpacingTrim { get; }

    /// <summary>https://drafts.csswg.org/css-text-4/#text-autospace-property (<c>normal</c> is <c>ideograph-alpha ideograph-numeric</c>).</summary>
    public TextAutospace TextAutospace { get; }

    /// <summary>https://drafts.csswg.org/css-text-4/#word-space-transform</summary>
    public WordSpaceTransform WordSpaceTransform { get; }

    /// <summary>
    /// The <c>auto-phrase</c> keyword of <c>word-space-transform</c>
    /// (https://drafts.csswg.org/css-text-4/#valdef-word-space-transform-auto-phrase): a virtual expandable separator is inserted
    /// at the phrase boundaries (with <see cref="WordSpaceTransform"/>, it is replaced by a space).
    /// </summary>
    public bool WordSpaceTransformAutoPhrase { get; }

    /// <summary>https://drafts.csswg.org/css-text-4/#hanging-punctuation-property</summary>
    public HangingPunctuation HangingPunctuation { get; }

    /// <summary>https://drafts.csswg.org/css-text-4/#hyphenate-limit-chars-property</summary>
    public HyphenateLimitChars HyphenateLimitChars { get; }

    /// <summary>The maximum number of successive hyphenated lines (https://drafts.csswg.org/css-text-4/#hyphenate-limit-lines), or null for <c>no-limit</c>.</summary>
    public int? HyphenateLimitLines { get; }

    /// <summary>Whether <c>hyphenate-limit-last</c> is <c>always</c>: the last full line of the element is not hyphenated (https://drafts.csswg.org/css-text-4/#hyphenate-limit-last).</summary>
    public bool HyphenateLimitLastAlways { get; }

    /// <summary>https://drafts.csswg.org/css-inline-3/#text-box-trim (not inherited).</summary>
    public TextBoxTrim TextBoxTrim { get; }

    /// <summary>The used <c>text-box-edge</c> (https://drafts.csswg.org/css-inline-3/#text-box-edge): <c>auto</c> is <c>line-fit-edge</c>, whose <c>leading</c> is <c>text</c>.</summary>
    public TextEdge TextBoxEdge { get; }

    /// <summary>
    /// The side of the line where the emphasis marks of the text are drawn (https://drafts.csswg.org/css-text-decor-4/#emphasis-marks),
    /// or <see cref="EmphasisMarksSide.None"/> when <c>text-emphasis-style</c> is <c>none</c>.
    /// </summary>
    public EmphasisMarksSide EmphasisMarks { get; }

    /// <summary>https://drafts.csswg.org/css-ruby-1/#rubypos (inherited): read on the annotation containers.</summary>
    public RubyPosition RubyPosition { get; }

    /// <summary>https://drafts.csswg.org/css-ruby-1/#ruby-align-property (inherited): read on the ruby bases and annotations.</summary>
    public RubyAlign RubyAlign { get; }

    /// <summary>
    /// Whether <c>ruby-overhang</c> is <c>spaces</c> (https://drafts.csswg.org/css-ruby-1/#ruby-overhang): the annotations only
    /// overhang the spaces next to the ruby container; <c>auto</c> lets them overhang the adjacent text.
    /// </summary>
    public bool RubyOverhangSpaces { get; }

    internal static VerticalAlign ParseVerticalAlign(ComputedStyle style, double zoom) => new(
        LayoutKeywords.ParseAlignmentBaseline(style.GetComputedValue("alignment-baseline")),
        BaselineShift.Parse(style.GetComputedValue("baseline-shift"), zoom),
        LayoutKeywords.ParseBaselineSource(style.GetComputedValue("baseline-source")));

    private static double ParseSpacing(string value, double zoom, double fontSize)
    {
        if (value == "normal" || !LengthPercentage.TryParse(value, zoom, nonNegative: false, out var spacing))
            return 0;

        return spacing.Resolve(fontSize);
    }

    internal static string? ParseString(string value) => CssParser.ParseListOfComponentValues(value) is [CssPreservedToken { Type: CssTokenType.String } token] ? token.Token.Value : null;
}

/// <summary>The properties of the font matching (https://drafts.csswg.org/css-fonts-4/#font-matching-algorithm), without the font size (<see cref="LayoutStyle.FontSize"/>).</summary>
internal sealed class FontLayoutStyle
{
    private static readonly HashSet<string> GenericFamilies = new(StringComparer.Ordinal)
    {
        "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded", "math", "emoji", "fangsong",
    };

    internal FontLayoutStyle(ComputedStyle style)
    {
        Families = ParseFamilies(style.GetComputedValue("font-family"));
        Weight = LengthPercentage.TryParseNumber(style.GetComputedValue("font-weight"), out var weight) ? weight : 400;
        var width = style.GetComputedValue("font-width");
        Width = width.EndsWith('%', StringComparison.Ordinal) && LengthPercentage.TryParseNumber(width.AsSpan(0, width.Length - 1), out var percentage) ? percentage : 100;
        Style = FontStyle.Parse(style.GetComputedValue("font-style"));
        SizeAdjust = FontSizeAdjust.Parse(style.GetComputedValue("font-size-adjust"));
        Kerning = LayoutKeywords.ParseFontKerning(style.GetComputedValue("font-kerning"));
        Synthesis = LayoutKeywords.ParseFontSynthesisFlag(style.GetComputedValue("font-synthesis-weight"), FontSynthesis.Weight)
            | LayoutKeywords.ParseFontSynthesisFlag(style.GetComputedValue("font-synthesis-style"), FontSynthesis.Style)
            | LayoutKeywords.ParseFontSynthesisFlag(style.GetComputedValue("font-synthesis-small-caps"), FontSynthesis.SmallCaps)
            | LayoutKeywords.ParseFontSynthesisFlag(style.GetComputedValue("font-synthesis-position"), FontSynthesis.Position);
        OpticalSizing = style.GetComputedValue("font-optical-sizing") != "none";
        FeatureSettings = style.GetComputedValue("font-feature-settings");
        VariationSettings = style.GetComputedValue("font-variation-settings");
    }

    /// <summary>The font families, in order of preference.</summary>
    public FontFamily[] Families { get; }

    /// <summary>The computed font-weight (a number in [1, 1000]).</summary>
    public double Weight { get; }

    /// <summary>The computed font-width (font-stretch) in percent.</summary>
    public double Width { get; }

    public FontStyle Style { get; }

    public FontSizeAdjust SizeAdjust { get; }

    public FontKerning Kerning { get; }

    /// <summary>The font features that can be synthesized (https://drafts.csswg.org/css-fonts-4/#font-synthesis).</summary>
    public FontSynthesis Synthesis { get; }

    public bool OpticalSizing { get; }

    /// <summary>The computed font-feature-settings (normal or the features, sorted by tag).</summary>
    public string FeatureSettings { get; }

    /// <summary>The computed font-variation-settings (normal or the axes, sorted by tag).</summary>
    public string VariationSettings { get; }

    private static FontFamily[] ParseFamilies(string value)
    {
        var families = new List<FontFamily>();
        foreach (var family in CssValueComputer.SplitArguments(CssParser.ParseListOfComponentValues(value)))
        {
            var significant = ValueMatcher.RemoveWhitespace(family);
            switch (significant)
            {
                case [CssPreservedToken { Type: CssTokenType.String } name]:
                    families.Add(new FontFamily(name.Token.Value, IsGeneric: false));
                    break;
                case [CssPreservedToken { Type: CssTokenType.Ident } ident] when GenericFamilies.Contains(Infra.InfraAscii.ToAsciiLowercase(ident.Token.Value)):
                    families.Add(new FontFamily(Infra.InfraAscii.ToAsciiLowercase(ident.Token.Value), IsGeneric: true));
                    break;
                case [CssFunction { Name: var functionName } function] when Infra.InfraAscii.EqualsIgnoreAsciiCase(functionName, "generic"):
                    // generic(<custom-ident>) (https://drafts.csswg.org/css-fonts-5/#generic-font-families)
                    families.Add(new FontFamily(CssSerializer.SerializeValue([function], property: null), IsGeneric: true));
                    break;
                case { Count: > 0 }:
                    // A sequence of identifiers is a family name, joined with single spaces
                    families.Add(new FontFamily(string.Join(' ', significant.OfType<CssPreservedToken>().Select(token => token.Token.Value)), IsGeneric: false));
                    break;
            }
        }

        return [.. families];
    }
}

/// <summary>The properties of the flex layout (https://drafts.csswg.org/css-flexbox-1/), and of the legacy -webkit-box (https://compat.spec.whatwg.org/#the-webkit-box-orient-property).</summary>
internal sealed record FlexLayoutStyle
{
    internal FlexLayoutStyle(ComputedStyle style, double zoom)
    {
        Direction = LayoutKeywords.ParseFlexDirection(style.GetComputedValue("flex-direction"));
        Wrap = LayoutKeywords.ParseFlexWrap(style.GetComputedValue("flex-wrap"), out var balance);
        IsBalanced = balance;
        LineCount = ParseLineCount(style.GetComputedValue("flex-line-count"));
        Grow = ParseNumber(style.GetComputedValue("flex-grow"), 0);
        Shrink = ParseNumber(style.GetComputedValue("flex-shrink"), 1);
        Basis = Size.Parse(style.GetComputedValue("flex-basis"), zoom);
        Order = ParseInteger(style.GetComputedValue("order"), 0);
        WebkitBoxOrient = style.GetComputedValue("-webkit-box-orient");
        WebkitBoxDirectionReverse = style.GetComputedValue("-webkit-box-direction") == "reverse";
        WebkitBoxAlign = style.GetComputedValue("-webkit-box-align") switch
        {
            "start" => AlignmentKeyword.FlexStart,
            "center" => AlignmentKeyword.Center,
            "end" => AlignmentKeyword.FlexEnd,
            "baseline" => AlignmentKeyword.Baseline,
            _ => AlignmentKeyword.Stretch,
        };
        WebkitBoxPack = style.GetComputedValue("-webkit-box-pack") switch
        {
            "center" => AlignmentKeyword.Center,
            "end" => AlignmentKeyword.FlexEnd,
            "justify" => AlignmentKeyword.SpaceBetween,
            _ => AlignmentKeyword.FlexStart,
        };
        WebkitBoxFlex = ParseNumber(style.GetComputedValue("-webkit-box-flex"), 0);
        WebkitBoxOrdinalGroup = ParseInteger(style.GetComputedValue("-webkit-box-ordinal-group"), 1);
    }

    public FlexDirection Direction { get; }

    public FlexWrap Wrap { get; }

    /// <summary>Whether the lines are balanced (https://drafts.csswg.org/css-flexbox-2/#valdef-flex-wrap-balance).</summary>
    public bool IsBalanced { get; }

    /// <summary>https://drafts.csswg.org/css-flexbox-2/#flex-line-count-property: the computed <c>flex-line-count</c>, at least 1.</summary>
    public int LineCount { get; }

    public double Grow { get; }

    public double Shrink { get; }

    /// <summary>https://drafts.csswg.org/css-flexbox-1/#flex-basis-property: auto, content, or a width value.</summary>
    public Size Basis { get; }

    public int Order { get; }

    /// <summary>Whether the main axis is the inline axis (flex-direction is row or row-reverse).</summary>
    public bool IsRow => Direction is FlexDirection.Row or FlexDirection.RowReverse;

    public bool IsReverse => Direction is FlexDirection.RowReverse or FlexDirection.ColumnReverse;

    /// <summary>The computed -webkit-box-orient (horizontal, vertical, inline-axis, block-axis).</summary>
    public string WebkitBoxOrient { get; }

    public bool WebkitBoxDirectionReverse { get; }

    /// <summary>-webkit-box-align as a box alignment keyword (https://compat.spec.whatwg.org/#the-webkit-box-align-property).</summary>
    public AlignmentKeyword WebkitBoxAlign { get; }

    /// <summary>-webkit-box-pack as a box alignment keyword (https://compat.spec.whatwg.org/#the-webkit-box-pack-property).</summary>
    public AlignmentKeyword WebkitBoxPack { get; }

    public double WebkitBoxFlex { get; }

    public int WebkitBoxOrdinalGroup { get; }

    internal static double ParseNumber(string value, double defaultValue) => LengthPercentage.TryParseNumber(value, out var number) ? number : defaultValue;

    /// <summary>Parses a computed <c>flex-line-count</c> (an integer that is at least 1): an integer that is too large is the largest one.</summary>
    private static int ParseLineCount(string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            return Math.Max(1, count);

        return value.Length > 0 && !value.AsSpan().ContainsAnyExceptInRange('0', '9') ? int.MaxValue : 1;
    }

    internal static int ParseInteger(string value, int defaultValue) => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : defaultValue;
}

/// <summary>The box alignment properties (https://drafts.csswg.org/css-align-3/) and the gaps.</summary>
internal sealed record AlignmentStyle
{
    internal AlignmentStyle(ComputedStyle style, double zoom)
    {
        AlignContent = Alignment.Parse(style.GetComputedValue("align-content"));
        JustifyContent = Alignment.Parse(style.GetComputedValue("justify-content"));
        AlignItems = Alignment.Parse(style.GetComputedValue("align-items"));
        JustifyItems = Alignment.Parse(style.GetComputedValue("justify-items"));
        AlignSelf = Alignment.Parse(style.GetComputedValue("align-self"));
        JustifySelf = Alignment.Parse(style.GetComputedValue("justify-self"));
        RowGap = Gap.Parse(style.GetComputedValue("row-gap"), zoom);
        ColumnGap = Gap.Parse(style.GetComputedValue("column-gap"), zoom);
    }

    public Alignment AlignContent { get; }

    public Alignment JustifyContent { get; }

    public Alignment AlignItems { get; }

    public Alignment JustifyItems { get; }

    /// <summary>align-self (auto: the align-items of the parent).</summary>
    public Alignment AlignSelf { get; }

    /// <summary>justify-self (auto: the justify-items of the parent).</summary>
    public Alignment JustifySelf { get; }

    /// <summary>row-gap: the percentages refer to the content box of the container.</summary>
    public Gap RowGap { get; }

    /// <summary>column-gap: the percentages refer to the content box of the container.</summary>
    public Gap ColumnGap { get; }
}

/// <summary>The properties of the grid layout (https://drafts.csswg.org/css-grid-2/).</summary>
internal sealed class GridLayoutStyle
{
    internal GridLayoutStyle(ComputedStyle style, double zoom)
    {
        TemplateRows = GridTrackList.Parse(style.GetComputedValue("grid-template-rows"), zoom);
        TemplateColumns = GridTrackList.Parse(style.GetComputedValue("grid-template-columns"), zoom);
        TemplateAreas = GridTemplateAreas.Parse(style.GetComputedValue("grid-template-areas"));
        AutoRows = GridTrackList.ParseAutoTracks(style.GetComputedValue("grid-auto-rows"), zoom);
        AutoColumns = GridTrackList.ParseAutoTracks(style.GetComputedValue("grid-auto-columns"), zoom);
        AutoFlow = GridAutoFlow.Parse(style.GetComputedValue("grid-auto-flow"));
        RowStart = GridLine.Parse(style.GetComputedValue("grid-row-start"));
        RowEnd = GridLine.Parse(style.GetComputedValue("grid-row-end"));
        ColumnStart = GridLine.Parse(style.GetComputedValue("grid-column-start"));
        ColumnEnd = GridLine.Parse(style.GetComputedValue("grid-column-end"));
        LanesDirection = GridLanesDirection.Parse(style.GetComputedValue("grid-lanes-direction"));
        LanesPackDense = style.GetComputedValue("grid-lanes-pack") == "dense";
        FlowTolerance = FlowTolerance.Parse(style.GetComputedValue("fit-tolerance"), zoom);
    }

    public GridTrackList TemplateRows { get; }

    public GridTrackList TemplateColumns { get; }

    public GridTemplateAreas TemplateAreas { get; }

    public GridTrackSize[] AutoRows { get; }

    public GridTrackSize[] AutoColumns { get; }

    public GridAutoFlow AutoFlow { get; }

    public GridLine RowStart { get; }

    public GridLine RowEnd { get; }

    public GridLine ColumnStart { get; }

    public GridLine ColumnEnd { get; }

    /// <summary>The orientation and the reversals of a grid lanes container (<c>grid-lanes-direction</c>, see <see cref="GridLanesDirection"/>).</summary>
    public GridLanesDirection LanesDirection { get; }

    /// <summary>
    /// Whether a grid lanes container uses dense packing (https://drafts.csswg.org/css-grid-3/#grid-lanes-dense): <c>grid-lanes-pack: dense</c>
    /// (the property of the WPT tests, tentative); <c>grid-auto-flow: dense</c> is the other syntax of the tests.
    /// </summary>
    public bool LanesPackDense { get; }

    /// <summary>https://drafts.csswg.org/css-grid-3/#placement-tolerance</summary>
    public FlowTolerance FlowTolerance { get; }
}

/// <summary>The properties of the tables (https://drafts.csswg.org/css-tables-3/).</summary>
internal sealed class TableLayoutStyle
{
    internal TableLayoutStyle(ComputedStyle style, double zoom)
    {
        TableLayout = style.GetComputedValue("table-layout") == "fixed" ? TableLayout.Fixed : TableLayout.Auto;
        BorderCollapse = style.GetComputedValue("border-collapse") == "collapse" ? BorderCollapse.Collapse : BorderCollapse.Separate;
        var spacing = style.GetComputedValue("border-spacing").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        BorderSpacingInline = spacing.Length > 0 ? BoxModelStyle.ParseFixedLength(spacing[0], zoom) : 0;
        BorderSpacingBlock = spacing.Length > 1 ? BoxModelStyle.ParseFixedLength(spacing[1], zoom) : BorderSpacingInline;
        EmptyCells = style.GetComputedValue("empty-cells") == "hide" ? EmptyCells.Hide : EmptyCells.Show;
        CaptionSide = style.GetComputedValue("caption-side") == "bottom" ? CaptionSide.Bottom : CaptionSide.Top;
    }

    public TableLayout TableLayout { get; }

    public BorderCollapse BorderCollapse { get; }

    /// <summary>
    /// The first value of the border spacing in px (https://drafts.csswg.org/css-tables-3/#border-spacing-property): the spacing
    /// between the columns, in the inline axis of the table (https://drafts.csswg.org/css-writing-modes-4/#dimension-mapping:
    /// "The first and second values of the border-spacing property represent spacing between columns and rows respectively").
    /// </summary>
    public double BorderSpacingInline { get; }

    /// <summary>The second value of the border spacing in px: the spacing between the rows, in the block axis of the table.</summary>
    public double BorderSpacingBlock { get; }

    public EmptyCells EmptyCells { get; }

    public CaptionSide CaptionSide { get; }
}

/// <summary>The properties of the multi-column layout (https://drafts.csswg.org/css-multicol-1/) and of the fragmentation (https://drafts.csswg.org/css-break-4/).</summary>
internal sealed record FragmentationStyle
{
    internal FragmentationStyle(ComputedStyle style, double zoom)
    {
        ColumnCount = int.TryParse(style.GetComputedValue("column-count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : null;
        ColumnWidth = Size.Parse(style.GetComputedValue("column-width"), zoom);
        ColumnFill = LayoutKeywords.ParseColumnFill(style.GetComputedValue("column-fill"));
        ColumnSpan = style.GetComputedValue("column-span") == "all" ? ColumnSpan.All : ColumnSpan.None;
        var columnHeight = style.GetComputedValue("column-height");
        ColumnHeight = columnHeight == "auto" ? null : BoxModelStyle.ParseFixedLength(columnHeight, zoom);
        ColumnWrap = style.GetComputedValue("column-wrap") switch
        {
            "wrap" => ColumnWrap.Wrap,
            "nowrap" => ColumnWrap.NoWrap,

            // https://drafts.csswg.org/css-multicol-2/#valdef-column-wrap-auto: "If column-height is not auto behaves as the value
            // wrap otherwise acts as nowrap"
            _ => ColumnHeight is null ? ColumnWrap.NoWrap : ColumnWrap.Wrap,
        };
        BreakBefore = LayoutKeywords.ParseBreakBetween(style.GetComputedValue("break-before"));
        BreakAfter = LayoutKeywords.ParseBreakBetween(style.GetComputedValue("break-after"));
        BreakInside = LayoutKeywords.ParseBreakInside(style.GetComputedValue("break-inside"));
        Orphans = FlexLayoutStyle.ParseInteger(style.GetComputedValue("orphans"), 2);
        Widows = FlexLayoutStyle.ParseInteger(style.GetComputedValue("widows"), 2);
        BoxDecorationBreak = style.GetComputedValue("box-decoration-break") == "clone" ? BoxDecorationBreak.Clone : BoxDecorationBreak.Slice;
    }

    /// <summary>column-count, or null for auto.</summary>
    public int? ColumnCount { get; }

    /// <summary>column-width (auto or a length).</summary>
    public Size ColumnWidth { get; }

    public ColumnFill ColumnFill { get; }

    public ColumnSpan ColumnSpan { get; }

    /// <summary>column-height (https://drafts.csswg.org/css-multicol-2/#ch) in px, or null for auto.</summary>
    public double? ColumnHeight { get; }

    /// <summary>The used column-wrap (https://drafts.csswg.org/css-multicol-2/#cwr): <c>auto</c> is resolved.</summary>
    public ColumnWrap ColumnWrap { get; }

    public BreakBetween BreakBefore { get; }

    public BreakBetween BreakAfter { get; }

    public BreakInside BreakInside { get; }

    public int Orphans { get; }

    public int Widows { get; }

    public BoxDecorationBreak BoxDecorationBreak { get; }
}

/// <summary>The properties of the overflow (https://drafts.csswg.org/css-overflow-3/) and of the scroll containers.</summary>
internal sealed class OverflowStyle
{
    internal OverflowStyle(ComputedStyle style, double zoom)
    {
        OverflowClipMargin = new(
            Layout.OverflowClipMargin.Parse(style.GetComputedValue("overflow-clip-margin-top"), zoom),
            Layout.OverflowClipMargin.Parse(style.GetComputedValue("overflow-clip-margin-right"), zoom),
            Layout.OverflowClipMargin.Parse(style.GetComputedValue("overflow-clip-margin-bottom"), zoom),
            Layout.OverflowClipMargin.Parse(style.GetComputedValue("overflow-clip-margin-left"), zoom));
        ScrollbarGutter = style.GetComputedValue("scrollbar-gutter") switch
        {
            "stable" => ScrollbarGutter.Stable,
            "stable both-edges" or "both-edges stable" => ScrollbarGutter.StableBothEdges,
            _ => ScrollbarGutter.Auto,
        };
        ScrollbarWidth = style.GetComputedValue("scrollbar-width") switch
        {
            "thin" => ScrollbarWidth.Thin,
            "none" => ScrollbarWidth.None,
            _ => ScrollbarWidth.Auto,
        };
        TextOverflow = TextOverflow.Parse(style.GetComputedValue("text-overflow"));
        (MaxLines, MaxLinesAuto) = ParseMaxLines(style.GetComputedValue("max-lines"));
        BlockEllipsis = style.GetComputedValue("block-ellipsis") switch
        {
            "no-ellipsis" => TextOverflowSide.Clip,
            "ellipsis" => new TextOverflowSide(TextOverflowKind.Ellipsis),
            var value => TextOverflow.Parse(value).First is { Kind: TextOverflowKind.String, String: { Length: > 0 } } side ? side : TextOverflowSide.Clip,
        };
        SmoothScrolling = style.GetComputedValue("scroll-behavior") == "smooth";
        ScrollMargin = new(
            BoxModelStyle.ParseFixedLength(style.GetComputedValue("scroll-margin-top"), zoom),
            BoxModelStyle.ParseFixedLength(style.GetComputedValue("scroll-margin-right"), zoom),
            BoxModelStyle.ParseFixedLength(style.GetComputedValue("scroll-margin-bottom"), zoom),
            BoxModelStyle.ParseFixedLength(style.GetComputedValue("scroll-margin-left"), zoom));
        ScrollPadding = new(
            LengthPercentageAuto.Parse(style.GetComputedValue("scroll-padding-top"), zoom),
            LengthPercentageAuto.Parse(style.GetComputedValue("scroll-padding-right"), zoom),
            LengthPercentageAuto.Parse(style.GetComputedValue("scroll-padding-bottom"), zoom),
            LengthPercentageAuto.Parse(style.GetComputedValue("scroll-padding-left"), zoom));
        ScrollSnapType = ScrollSnapType.Parse(style.GetComputedValue("scroll-snap-type"));
        ScrollSnapAlign = ScrollSnapAlign.Parse(style.GetComputedValue("scroll-snap-align"));
        ScrollSnapStopAlways = style.GetComputedValue("scroll-snap-stop") == "always";
    }

    /// <summary>https://drafts.csswg.org/css-overflow-4/#overflow-clip-margin</summary>
    public Sides<OverflowClipMargin> OverflowClipMargin { get; }

    public ScrollbarGutter ScrollbarGutter { get; }

    public ScrollbarWidth ScrollbarWidth { get; }

    public TextOverflow TextOverflow { get; }

    /// <summary>The number of lines of max-lines (https://drafts.csswg.org/css-overflow-4/#max-lines), or null for <c>auto</c> alone.</summary>
    public int? MaxLines { get; }

    /// <summary>Whether max-lines has the auto keyword (or no number of lines): a line-clamp container is also clamped by its block size.</summary>
    public bool MaxLinesAuto { get; }

    /// <summary>
    /// block-ellipsis (https://drafts.csswg.org/css-overflow-4/#block-ellipsis): <see cref="TextOverflowKind.Clip"/> for
    /// <c>no-ellipsis</c> (and the empty string), <see cref="TextOverflowKind.Ellipsis"/>, or a &lt;string&gt;.
    /// </summary>
    public TextOverflowSide BlockEllipsis { get; }

    /// <summary>Whether scroll-behavior is smooth.</summary>
    public bool SmoothScrolling { get; }

    /// <summary>https://drafts.csswg.org/css-scroll-snap-1/#scroll-margin (lengths).</summary>
    public Sides<double> ScrollMargin { get; }

    /// <summary>https://drafts.csswg.org/css-scroll-snap-1/#scroll-padding (auto or a length-percentage of the scrollport).</summary>
    public Sides<LengthPercentageAuto> ScrollPadding { get; }

    /// <summary>https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-type</summary>
    public ScrollSnapType ScrollSnapType { get; }

    /// <summary>https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-align</summary>
    public ScrollSnapAlign ScrollSnapAlign { get; }

    /// <summary>Whether scroll-snap-stop is <c>always</c> (https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-stop).</summary>
    public bool ScrollSnapStopAlways { get; }

    /// <summary>Parses the computed max-lines: an integer, <c>auto</c>, or both.</summary>
    private static (int? Lines, bool Auto) ParseMaxLines(string value)
    {
        int? lines = null;
        var auto = false;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "auto")
            {
                auto = true;
            }
            else if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                lines = count;
            }
        }

        return (lines, auto || lines is null);
    }
}

/// <summary>The properties of the replaced elements (https://drafts.csswg.org/css-images-3/).</summary>
internal sealed class ReplacedStyle
{
    internal ReplacedStyle(ComputedStyle style, double zoom)
    {
        ObjectFit = LayoutKeywords.ParseObjectFit(style.GetComputedValue("object-fit"));
        var position = CssValueComputer.SplitComponents(style.GetComputedValue("object-position"));
        ObjectPositionX = position.Length > 0 && LengthPercentage.TryParse(position[0], zoom, nonNegative: false, out var x) ? x : LengthPercentage.FromPercentage(50);
        ObjectPositionY = position.Length > 1 && LengthPercentage.TryParse(position[1], zoom, nonNegative: false, out var y) ? y : LengthPercentage.FromPercentage(50);
        ImageOrientation = ImageOrientation.Parse(style.GetComputedValue("image-orientation"));
    }

    public ObjectFit ObjectFit { get; }

    /// <summary>The horizontal offset of object-position from the left edge (the percentages refer to the free space).</summary>
    public LengthPercentage ObjectPositionX { get; }

    /// <summary>The vertical offset of object-position from the top edge.</summary>
    public LengthPercentage ObjectPositionY { get; }

    public ImageOrientation ImageOrientation { get; }
}
