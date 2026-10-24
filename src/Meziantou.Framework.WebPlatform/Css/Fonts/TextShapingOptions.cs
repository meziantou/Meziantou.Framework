using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The orientation of the glyphs of a text run (https://drafts.csswg.org/css-writing-modes-4/#text-orientation).</summary>
internal enum TextShapingOrientation : byte
{
    /// <summary>Horizontal text: the glyphs are laid out with their horizontal advances.</summary>
    Horizontal,

    /// <summary>
    /// Vertical text with <c>text-orientation: mixed</c>: the characters whose Vertical_Orientation is U or Tu are upright,
    /// the other ones are rotated 90 degrees clockwise (https://drafts.csswg.org/css-writing-modes-4/#valdef-text-orientation-mixed).
    /// </summary>
    VerticalMixed,

    /// <summary>Vertical text with <c>text-orientation: upright</c>: all the characters are upright, with their vertical advances.</summary>
    VerticalUpright,

    /// <summary>Vertical text with <c>text-orientation: sideways</c> (and the <c>sideways-*</c> writing modes): all the characters are rotated.</summary>
    VerticalSideways,
}

/// <summary>The values of <c>font-variant-caps</c> (https://drafts.csswg.org/css-fonts-4/#font-variant-caps-prop).</summary>
internal enum FontCapsVariant : byte
{
    Normal,
    SmallCaps,
    AllSmallCaps,
    PetiteCaps,
    AllPetiteCaps,
    Unicase,
    TitlingCaps,
}

/// <summary>The values of <c>font-variant-position</c> (https://drafts.csswg.org/css-fonts-4/#font-variant-position-prop).</summary>
internal enum FontPositionVariant : byte
{
    Normal,
    Sub,
    Super,
}

/// <summary>The values of <c>font-variant-emoji</c> (https://drafts.csswg.org/css-fonts-4/#font-variant-emoji-prop).</summary>
internal enum FontEmojiVariant : byte
{
    /// <summary>The font fallback of the characters, without preference.</summary>
    Normal,

    /// <summary>As if U+FE0E VARIATION SELECTOR-15 was appended to every Emoji Presentation Participating Code Point.</summary>
    Text,

    /// <summary>As if U+FE0F VARIATION SELECTOR-16 was appended to every Emoji Presentation Participating Code Point.</summary>
    Emoji,

    /// <summary>The presentation of the Emoji_Presentation property (UTS #51).</summary>
    Unicode,
}

/// <summary>
/// The characters around a text that change its shaping when it is shaped alone (e.g. the text of an element between two
/// other elements, https://drafts.csswg.org/css-text-3/#boundary-shaping): the closest characters before and after it that
/// are not transparent for the joining of the cursive scripts (0 for none). The Arabic joining continues across the text.
/// </summary>
/// <param name="Before">The character before the text, or 0.</param>
/// <param name="After">The character after the text, or 0.</param>
/// <param name="Script">
/// The script of the text around a text that has only characters of the scripts Common and Inherited (e.g. a punctuation in its
/// own element), which they take, as when the script runs are computed on the whole paragraph; Unknown for none.
/// </param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ShapingContext(int Before, int After, UnicodeScript Script = UnicodeScript.Unknown)
{
    /// <summary>Whether there is no context.</summary>
    public bool IsEmpty => Before == 0 && After == 0 && Script == UnicodeScript.Unknown;
}

/// <summary>
/// The style of a text run for the text shaping (<see cref="TextShaper"/>): the font size, the direction and the orientation of
/// the run, the spacing (https://drafts.csswg.org/css-text-4/#spacing) and the font features. It is a cache key: two runs with
/// the same text, fonts and options have the same glyphs.
/// </summary>
/// <remarks>
/// The direction is the direction of the bidi run (its embedding level), which the inline layout knows, not necessarily the
/// <c>direction</c> of the element. The lengths are in CSS px; <see cref="FromStyle"/> reads the computed values (without zoom).
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TextShapingOptions
{
    // Caps, Position, Emoji and the synthesis flags are packed in one field: the options are the key of the shaping cache
    private const int CapsMask = 0x7;
    private const int PositionShift = 3;
    private const int PositionMask = 0x3 << PositionShift;
    private const int EmojiShift = 5;
    private const int EmojiMask = 0x3 << EmojiShift;
    private const int NoSmallCapsSynthesis = 0x80;
    private const int NoPositionSynthesis = 0x100;

    private readonly ushort _variants;

    public TextShapingOptions()
    {
    }

    /// <summary>The computed font size in CSS px (the em size of each font applies <c>size-adjust</c> and <c>font-size-adjust</c>).</summary>
    public double FontSize { get; init; } = CssValueComputer.MediumFontSize;

    /// <summary>Whether the run is right-to-left (an odd bidi embedding level). The glyphs stay in logical order.</summary>
    public bool IsRightToLeft { get; init; }

    /// <summary>The orientation of the glyphs.</summary>
    public TextShapingOrientation Orientation { get; init; }

    /// <summary>
    /// The used <c>letter-spacing</c> in px (https://drafts.csswg.org/css-text-4/#letter-spacing-property), added after each
    /// typographic character unit (including the last one: the inline layout removes it at the end of a line).
    /// </summary>
    public double LetterSpacing { get; init; }

    /// <summary>The used <c>word-spacing</c> in px (https://drafts.csswg.org/css-text-4/#word-spacing-property), added to each word-separator character.</summary>
    public double WordSpacing { get; init; }

    /// <summary>
    /// Whether the kerning of the fonts is applied: the resolution of <c>font-kerning</c>, <c>text-rendering</c> and the
    /// <c>kern</c> feature of <c>font-feature-settings</c> (https://drafts.csswg.org/css-fonts-4/#feature-precedence).
    /// </summary>
    public bool Kerning { get; init; } = true;

    /// <summary>
    /// The OpenType features of the CSS properties (https://drafts.csswg.org/css-fonts-4/#feature-precedence): the features of
    /// <c>font-variant-*</c> and of <c>text-rendering</c>, the optional ligatures disabled by <c>letter-spacing</c>, then
    /// <c>font-feature-settings</c>; null for the default features. The kerning is <see cref="Kerning"/>.
    /// </summary>
    public FontFeatureList? Features { get; init; }

    /// <summary>The content language of the text (a BCP 47 language tag, https://html.spec.whatwg.org/multipage/dom.html#language), or null: it selects the language system of the fonts (the <c>locl</c> forms).</summary>
    public string? Language { get; init; }

    /// <summary>The OpenType language system tag of <c>font-language-override</c> (https://drafts.csswg.org/css-fonts-4/#font-language-override-prop), or 0 to use <see cref="Language"/>.</summary>
    public uint LanguageOverride { get; init; }

    /// <summary>
    /// The characters around the text that change its shaping (the joining of the cursive scripts across the elements, see
    /// <see cref="ShapingContext"/>): the inline layout sets it for the texts of an element next to another one.
    /// </summary>
    public ShapingContext Context { get; init; }

    /// <summary>
    /// Whether the kerning is requested explicitly (<c>font-kerning: normal</c>): the CJK text is kerned (with its <c>kern</c>
    /// and <c>palt</c>/<c>vpal</c> features) only then, as in Chrome.
    /// </summary>
    public bool KerningRequested { get; init; }

    /// <summary>
    /// The computed <c>font-variant-caps</c>: its features are in <see cref="Features"/>; the small capitals are synthesized
    /// (or use a fallback feature) for the characters the font has no glyph for (https://drafts.csswg.org/css-fonts-4/#font-variant-caps-prop).
    /// </summary>
    public FontCapsVariant Caps
    {
        get => (FontCapsVariant)(_variants & CapsMask);
        init => _variants = (ushort)((_variants & ~CapsMask) | (int)value);
    }

    /// <summary>
    /// The computed <c>font-variant-position</c>: its features are in <see cref="Features"/>; the subscripts and the superscripts
    /// are synthesized when the font has no glyph for a character of a run (https://drafts.csswg.org/css-fonts-4/#font-variant-position-prop).
    /// </summary>
    public FontPositionVariant Position
    {
        get => (FontPositionVariant)((_variants & PositionMask) >> PositionShift);
        init => _variants = (ushort)((_variants & ~PositionMask) | ((int)value << PositionShift));
    }

    /// <summary>
    /// The computed <c>font-variant-emoji</c>: the presentation of the Emoji Presentation Participating Code Points that are not
    /// followed by a variation selector, which selects the font of the cluster (https://drafts.csswg.org/css-fonts-4/#font-variant-emoji-prop).
    /// </summary>
    public FontEmojiVariant Emoji
    {
        get => (FontEmojiVariant)((_variants & EmojiMask) >> EmojiShift);
        init => _variants = (ushort)((_variants & ~EmojiMask) | ((int)value << EmojiShift));
    }

    /// <summary>Whether the small capitals may be synthesized (<c>font-synthesis-small-caps: auto</c>, https://drafts.csswg.org/css-fonts-4/#font-synthesis-small-caps).</summary>
    public bool SynthesizeSmallCaps
    {
        get => (_variants & NoSmallCapsSynthesis) == 0;
        init => _variants = (ushort)(value ? _variants & ~NoSmallCapsSynthesis : _variants | NoSmallCapsSynthesis);
    }

    /// <summary>Whether the subscripts and the superscripts may be synthesized (<c>font-synthesis-position: auto</c>, https://drafts.csswg.org/css-fonts-4/#font-synthesis-position).</summary>
    public bool SynthesizePosition
    {
        get => (_variants & NoPositionSynthesis) == 0;
        init => _variants = (ushort)(value ? _variants & ~NoPositionSynthesis : _variants | NoPositionSynthesis);
    }

    /// <summary>Whether the text is vertical.</summary>
    public bool IsVertical => Orientation != TextShapingOrientation.Horizontal;

    /// <summary>
    /// Returns the options of the text of an element from its computed style: the font size, the <c>direction</c>, the
    /// orientation from <c>writing-mode</c> and <c>text-orientation</c>, <c>letter-spacing</c>, <c>word-spacing</c> and the
    /// kerning.
    /// </summary>
    public static TextShapingOptions FromStyle(ComputedStyle style)
    {
        var fontSize = style.FontSize;
        return new TextShapingOptions
        {
            FontSize = fontSize,
            IsRightToLeft = style.GetComputedValue("direction") == "rtl",
            Orientation = GetOrientation(style.GetComputedValue("writing-mode"), style.GetComputedValue("text-orientation")),
            LetterSpacing = GetSpacing(style.GetComputedValue("letter-spacing"), fontSize),
            WordSpacing = GetSpacing(style.GetComputedValue("word-spacing"), fontSize),
            Kerning = IsKerningEnabled(style.GetComputedValue("font-kerning"), style.GetComputedValue("text-rendering"), style.GetComputedValue("font-feature-settings")),
            KerningRequested = style.GetComputedValue("font-kerning") == "normal",
            Features = GetFeatures(
                style.GetComputedValue("text-rendering"),
                style.GetComputedValue("font-variant-ligatures"),
                style.GetComputedValue("font-variant-caps"),
                style.GetComputedValue("font-variant-numeric"),
                style.GetComputedValue("font-variant-east-asian"),
                style.GetComputedValue("font-variant-position"),
                FontFeatureValues.ResolveAlternates(style, style.GetComputedValue("font-variant-alternates")),
                disableOptionalLigatures: style.GetComputedValue("letter-spacing") is not ("normal" or "0px" or "0" or "") || IsJustifiedBetweenCharacters(style),
                style.GetComputedValue("font-feature-settings")),
            Language = style.Element is { } element ? HtmlElementStates.GetLanguage(element) : null,
            LanguageOverride = ParseLanguageOverride(style.GetComputedValue("font-language-override")),
            Caps = ParseCaps(style.GetComputedValue("font-variant-caps")),
            Position = ParsePosition(style.GetComputedValue("font-variant-position")),
            SynthesizeSmallCaps = style.GetComputedValue("font-synthesis-small-caps") != "none",
            SynthesizePosition = style.GetComputedValue("font-synthesis-position") != "none",
            Emoji = ParseEmoji(style.GetComputedValue("font-variant-emoji")),
        };
    }

    /// <summary>The value of a computed <c>font-variant-emoji</c>.</summary>
    internal static FontEmojiVariant ParseEmoji(string value) => value switch
    {
        "text" => FontEmojiVariant.Text,
        "emoji" => FontEmojiVariant.Emoji,
        "unicode" => FontEmojiVariant.Unicode,
        _ => FontEmojiVariant.Normal,
    };

    /// <summary>The value of a computed <c>font-variant-caps</c>.</summary>
    internal static FontCapsVariant ParseCaps(string value) => value switch
    {
        "small-caps" => FontCapsVariant.SmallCaps,
        "all-small-caps" => FontCapsVariant.AllSmallCaps,
        "petite-caps" => FontCapsVariant.PetiteCaps,
        "all-petite-caps" => FontCapsVariant.AllPetiteCaps,
        "unicase" => FontCapsVariant.Unicase,
        "titling-caps" => FontCapsVariant.TitlingCaps,
        _ => FontCapsVariant.Normal,
    };

    /// <summary>The value of a computed <c>font-variant-position</c>.</summary>
    internal static FontPositionVariant ParsePosition(string value) => value switch
    {
        "sub" => FontPositionVariant.Sub,
        "super" => FontPositionVariant.Super,
        _ => FontPositionVariant.Normal,
    };

    /// <summary>
    /// Whether the justification may add space between the characters of the text: <c>text-align</c> or <c>text-align-last</c>
    /// is <c>justify</c> with <c>text-justify: inter-character</c> (https://drafts.csswg.org/css-text-4/#justify-ligatures).
    /// </summary>
    private static bool IsJustifiedBetweenCharacters(ComputedStyle style)
        => style.GetComputedValue("text-justify") is "inter-character" or "distribute" && (style.GetComputedValue("text-align-all") == "justify" || style.GetComputedValue("text-align-last") == "justify");

    /// <summary>The tag of a value of <c>font-language-override</c> (a string of 1 to 4 characters, padded with spaces), or 0 for <c>normal</c>.</summary>
    internal static uint ParseLanguageOverride(string value)
    {
        if (value is "normal" or "" || ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)) is not [CssPreservedToken { Type: CssTokenType.String } token])
            return 0;

        return OpenTypeTag.TryParse(token.Token.Value, out var tag) ? tag : 0;
    }

    /// <summary>
    /// Resolves the OpenType features of the font properties in the order of their precedence
    /// (https://drafts.csswg.org/css-fonts-4/#feature-precedence): <c>text-rendering: optimizeSpeed</c> disables the common
    /// ligatures (as Chrome), then the features of <c>font-variant-ligatures</c>, <c>font-variant-caps</c>,
    /// <c>font-variant-numeric</c>, <c>font-variant-east-asian</c>, <c>font-variant-position</c> and
    /// <c>font-variant-alternates</c> (whose named values are already resolved), then a non-zero <c>letter-spacing</c> or a
    /// justification between the characters disables the optional ligatures (https://drafts.csswg.org/css-text-4/#letter-spacing-property), then
    /// <c>font-feature-settings</c>. Returns null when no feature is set (the default features of the shaping).
    /// </summary>
    internal static FontFeatureList? GetFeatures(string textRendering, string ligatures, string caps, string numeric, string eastAsian, string position, IReadOnlyList<FontFeature>? alternates, bool disableOptionalLigatures, string featureSettings)
    {
        var features = new List<FontFeature>();
        if (string.Equals(textRendering, "optimizeSpeed", StringComparison.OrdinalIgnoreCase))
        {
            Add("liga", 0);
            Add("clig", 0);
        }

        // https://drafts.csswg.org/css-fonts-4/#font-variant-ligatures-prop
        foreach (var keyword in Keywords(ligatures))
        {
            switch (keyword)
            {
                case "none":
                    Add("liga", 0);
                    Add("clig", 0);
                    Add("dlig", 0);
                    Add("hlig", 0);
                    Add("calt", 0);
                    break;
                case "common-ligatures":
                    Add("liga", 1);
                    Add("clig", 1);
                    break;
                case "no-common-ligatures":
                    Add("liga", 0);
                    Add("clig", 0);
                    break;
                case "discretionary-ligatures":
                    Add("dlig", 1);
                    break;
                case "no-discretionary-ligatures":
                    Add("dlig", 0);
                    break;
                case "historical-ligatures":
                    Add("hlig", 1);
                    break;
                case "no-historical-ligatures":
                    Add("hlig", 0);
                    break;
                case "contextual":
                    Add("calt", 1);
                    break;
                case "no-contextual":
                    Add("calt", 0);
                    break;
            }
        }

        // https://drafts.csswg.org/css-fonts-4/#font-variant-caps-prop (the shaping synthesizes the small capitals the font does
        // not have, or replaces these features by their fallback: their range is recorded)
        var capsStart = features.Count;
        switch (caps)
        {
            case "small-caps":
                Add("smcp", 1);
                break;
            case "all-small-caps":
                Add("c2sc", 1);
                Add("smcp", 1);
                break;
            case "petite-caps":
                Add("pcap", 1);
                break;
            case "all-petite-caps":
                Add("c2pc", 1);
                Add("pcap", 1);
                break;
            case "unicase":
                Add("unic", 1);
                break;
            case "titling-caps":
                Add("titl", 1);
                break;
        }

        var capsRange = (capsStart, features.Count - capsStart);

        // https://drafts.csswg.org/css-fonts-4/#font-variant-numeric-prop
        foreach (var keyword in Keywords(numeric))
        {
            var tag = keyword switch
            {
                "lining-nums" => "lnum",
                "oldstyle-nums" => "onum",
                "proportional-nums" => "pnum",
                "tabular-nums" => "tnum",
                "diagonal-fractions" => "frac",
                "stacked-fractions" => "afrc",
                "ordinal" => "ordn",
                "slashed-zero" => "zero",
                _ => null,
            };

            if (tag is not null)
            {
                Add(tag, 1);
            }
        }

        // https://drafts.csswg.org/css-fonts-4/#font-variant-east-asian-prop
        foreach (var keyword in Keywords(eastAsian))
        {
            var tag = keyword switch
            {
                "jis78" => "jp78",
                "jis83" => "jp83",
                "jis90" => "jp90",
                "jis04" => "jp04",
                "simplified" => "smpl",
                "traditional" => "trad",
                "full-width" => "fwid",
                "proportional-width" => "pwid",
                "ruby" => "ruby",
                _ => null,
            };

            if (tag is not null)
            {
                Add(tag, 1);
            }
        }

        // https://drafts.csswg.org/css-fonts-4/#font-variant-position-prop (the shaping synthesizes the runs whose characters the
        // font does not all have: the range of the features is recorded)
        var positionStart = features.Count;
        switch (position)
        {
            case "sub":
                Add("subs", 1);
                break;
            case "super":
                Add("sups", 1);
                break;
        }

        var positionRange = (positionStart, features.Count - positionStart);

        if (alternates is not null)
        {
            features.AddRange(alternates);
        }

        // https://drafts.csswg.org/css-text-4/#letter-spacing-property: "When the effective spacing between two characters is
        // not zero (due to either justification or a non-zero value of letter-spacing), user agents should not apply optional ligatures"
        if (disableOptionalLigatures)
        {
            Add("liga", 0);
            Add("clig", 0);
            Add("dlig", 0);
            Add("hlig", 0);
            Add("calt", 0);
        }

        FontFeatureList.ParseFeatureSettings(featureSettings, features);
        return features.Count == 0 ? null : new FontFeatureList([.. features], capsRange, positionRange);

        void Add(string tag, uint value) => features.Add(new FontFeature(((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3], value));

        static string[] Keywords(string value) => value is "normal" or "" ? [] : value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The orientation of the glyphs for a <c>writing-mode</c> and a <c>text-orientation</c> (https://drafts.csswg.org/css-writing-modes-4/#text-orientation).</summary>
    internal static TextShapingOrientation GetOrientation(string writingMode, string textOrientation)
    {
        switch (writingMode)
        {
            case "vertical-rl" or "vertical-lr":
                return textOrientation switch
                {
                    "upright" => TextShapingOrientation.VerticalUpright,
                    "sideways" or "sideways-right" => TextShapingOrientation.VerticalSideways,
                    _ => TextShapingOrientation.VerticalMixed,
                };

            // https://drafts.csswg.org/css-writing-modes-4/#valdef-writing-mode-sideways-rl: the text is set sideways, text-orientation does not apply
            case "sideways-rl" or "sideways-lr":
                return TextShapingOrientation.VerticalSideways;
            default:
                return TextShapingOrientation.Horizontal;
        }
    }

    /// <summary>
    /// The used value of <c>letter-spacing</c> or <c>word-spacing</c> in px: <c>normal</c> is 0, and the percentages refer to
    /// the computed font size (https://drafts.csswg.org/css-text-4/#letter-spacing-property).
    /// </summary>
    internal static double GetSpacing(string value, double fontSize)
    {
        if (value is "normal" or "0px" or "")
            return 0;

        if (value.EndsWith("px", StringComparison.Ordinal) && CssValueComputer.ParsePixels(value) is { } pixels)
            return double.IsFinite(pixels) ? pixels : 0;

        if (value.EndsWith('%', StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
            return fontSize * percentage / 100;

        // A calc() mixing a length and a percentage
        if (value.Contains('(', StringComparison.Ordinal) && ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)) is [CssFunction function]
            && CssCalc.Evaluate(function, new CssCalcContext { PercentageBasis = (fontSize, "px") }) is { IsResolved: true, Unit: "px" } result && double.IsFinite(result.Value))
        {
            return result.Value;
        }

        return 0;
    }

    /// <summary>
    /// Resolves whether the kerning is applied (https://drafts.csswg.org/css-fonts-4/#feature-precedence): <c>font-kerning:
    /// auto</c> lets the browser decide, and the browsers apply the kerning, except Chrome with <c>text-rendering: optimizeSpeed</c>;
    /// <c>normal</c> and <c>none</c> force it; then the <c>kern</c> feature of <c>font-feature-settings</c> overrides it.
    /// </summary>
    internal static bool IsKerningEnabled(string fontKerning, string textRendering, string featureSettings)
    {
        var enabled = fontKerning switch
        {
            "none" => false,
            "normal" => true,
            _ => !string.Equals(textRendering, "optimizeSpeed", StringComparison.OrdinalIgnoreCase),
        };

        if (featureSettings != "normal" && GetFeatureValue(featureSettings, OpenTypeTag.Kern) is { } kern)
        {
            enabled = kern != 0;
        }

        return enabled;
    }

    /// <summary>
    /// Returns the value of a feature in a computed value of <c>font-feature-settings</c>
    /// (https://drafts.csswg.org/css-fonts-4/#font-feature-settings-prop): <c>"tag"</c> and <c>on</c> are 1, <c>off</c> 0;
    /// the last setting of a feature wins. Null when the feature is not set.
    /// </summary>
    internal static int? GetFeatureValue(string featureSettings, uint feature)
    {
        int? result = null;
        foreach (var entry in CssValueComputer.SplitArguments(CssParser.ParseListOfComponentValues(featureSettings)))
        {
            var significant = ValueMatcher.RemoveWhitespace(entry);
            if (significant is not [CssPreservedToken { Type: CssTokenType.String } tag, ..] || tag.Token.Value is not { Length: 4 } name)
                continue;

            if ((((uint)name[0] << 24) | ((uint)name[1] << 16) | ((uint)name[2] << 8) | name[3]) != feature)
                continue;

            result = significant switch
            {
                [_] => 1,
                [_, CssPreservedToken { Type: CssTokenType.Number } number] => (int)number.Token.NumericValue,
                [_, CssPreservedToken { Type: CssTokenType.Ident } keyword] when keyword.Token.Value.Equals("off", StringComparison.OrdinalIgnoreCase) => 0,
                [_, CssPreservedToken { Type: CssTokenType.Ident }] => 1,
                _ => result,
            };
        }

        return result;
    }
}
