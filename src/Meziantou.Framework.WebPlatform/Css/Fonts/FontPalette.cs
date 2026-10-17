using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The colors of the color glyphs of a font for an element: the palette selected by <c>font-palette</c>
/// (https://drafts.csswg.org/css-fonts-4/#font-palette-prop) among the palettes of the font, with the colors overridden by the
/// <c>@font-palette-values</c> rule it names (https://drafts.csswg.org/css-fonts-4/#font-palette-values).
/// </summary>
internal sealed class FontPalette
{
    /// <summary>The maximum depth of the nested palette-mix() functions.</summary>
    private const int MaxMixDepth = 16;

    private FontPalette(OpenTypeColorGlyphs glyphs, DisplayColor[] colors)
    {
        Glyphs = glyphs;
        Colors = colors;
    }

    /// <summary>The color glyphs of the font.</summary>
    public OpenTypeColorGlyphs Glyphs { get; }

    /// <summary>The color of each entry of the palette.</summary>
    public IReadOnlyList<DisplayColor> Colors { get; }

    private Dictionary<ushort, (ushort GlyphId, DisplayColor? Color)[]>? _layers;

    /// <summary>
    /// Returns the layers of a glyph with their colors, from the bottom one to the top one, when it is a color glyph; a null
    /// color is the color of the text (the palette index 0xFFFF).
    /// </summary>
    public bool TryGetLayers(ushort glyphId, out (ushort GlyphId, DisplayColor? Color)[] layers)
    {
        if (_layers is not null && _layers.TryGetValue(glyphId, out var cached))
        {
            layers = cached;
            return true;
        }

        if (!Glyphs.TryGetLayers(glyphId, out var glyphLayers))
        {
            layers = [];
            return false;
        }

        layers = new (ushort, DisplayColor?)[glyphLayers.Length];
        for (var i = 0; i < glyphLayers.Length; i++)
        {
            var layer = glyphLayers[i];
            layers[i] = (layer.GlyphId, layer.PaletteIndex == OpenTypeColorGlyphs.ForegroundPaletteIndex ? null : Colors[layer.PaletteIndex]);
        }

        (_layers ??= [])[glyphId] = layers;
        return true;
    }

    /// <summary>Returns whether a glyph run of the font has a color glyph.</summary>
    public bool HasColorGlyph(GlyphRun run)
    {
        foreach (var glyph in run.Glyphs)
        {
            if (Glyphs.IsColorGlyph(glyph))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Splits a glyph run into the glyph runs that draw it with its color glyphs: the glyphs are drawn in the order of the
    /// run, and a color glyph is drawn as the glyphs of its layers at its position, from the bottom layer to the top one. The
    /// consecutive glyphs drawn with the same paint are one glyph run; the first run has the text of the run.
    /// </summary>
    /// <param name="run">The glyph run.</param>
    /// <param name="paint">The paint of the glyphs that are not color glyphs, and of the layers in the color of the text.</param>
    /// <param name="colorize">
    /// Whether the layers are painted with the colors of the palette (the fill of the text; the alpha of the paint applies
    /// to them), or with the paint, as a silhouette of the glyphs (a shadow, a mask). The color glyphs are not drawn by a
    /// paint that strokes.
    /// </param>
    public List<(GlyphRun Run, DisplayPaint Paint)> SplitColorGlyphs(GlyphRun run, DisplayPaint paint, bool colorize)
    {
        var result = new List<(GlyphRun Run, DisplayPaint Paint)>();
        var ids = new List<ushort>();
        var positions = new List<PhysicalOffset>();
        var current = paint;
        Dictionary<DisplayColor, DisplayPaint>? layerPaints = null;
        var count = Math.Min(run.Glyphs.Length, run.Positions.Length);
        for (var i = 0; i < count; i++)
        {
            if (!TryGetLayers(run.Glyphs[i], out var layers))
            {
                Append(run.Glyphs[i], run.Positions[i], paint);
                continue;
            }

            // A color glyph is not stroked, as in the browsers: it has no outline of its own (the ones of its layers overlap)
            if (paint.Style == PaintStyle.Stroke)
                continue;

            foreach (var layer in layers)
            {
                Append(layer.GlyphId, run.Positions[i], colorize && layer.Color is { } color ? GetLayerPaint(color) : paint);
            }
        }

        Flush();
        return result;

        DisplayPaint GetLayerPaint(DisplayColor color)
        {
            layerPaints ??= [];
            if (!layerPaints.TryGetValue(color, out var layerPaint))
            {
                layerPaint = paint with { Color = color.MultiplyAlpha(paint.Color.A), Shader = null };
                layerPaints.Add(color, layerPaint);
            }

            return layerPaint;
        }

        void Append(ushort glyph, PhysicalOffset position, DisplayPaint glyphPaint)
        {
            if (ids.Count > 0 && !ReferenceEquals(glyphPaint, current))
            {
                Flush();
            }

            current = glyphPaint;
            ids.Add(glyph);
            positions.Add(position);
        }

        void Flush()
        {
            if (ids.Count == 0)
                return;

            result.Add((run with { Glyphs = [.. ids], Positions = [.. positions], Text = result.Count == 0 ? run.Text : "" }, current));
            ids.Clear();
            positions.Clear();
        }
    }

    /// <summary>
    /// Returns the palette of a font for the style of an element, or null when the font has no color glyph. The relative units
    /// of the math functions of the <c>override-colors</c> descriptor are the ones of the element.
    /// </summary>
    public static FontPalette? Resolve(ComputedStyle style, MatchedFont font)
    {
        if (font.Font.ColorGlyphs is not { } glyphs)
            return null;

        return Resolve(style, glyphs, GetFamilies(font), style.GetComputedValue("font-palette"));
    }

    /// <summary>
    /// Returns the default palette of a font (the palette at index 0, as <c>font-palette: normal</c> without color scheme),
    /// for the texts that have no <c>font-palette</c> (the text of a canvas), or null when the font has no color glyph.
    /// </summary>
    public static FontPalette? GetDefault(MatchedFont font)
    {
        if (font.Font.ColorGlyphs is not { } glyphs)
            return null;

        var colors = glyphs.PaletteCount == 0 ? [] : GetFontPalette(glyphs, 0);
        return new FontPalette(glyphs, Array.ConvertAll(colors, static color => DisplayColor.Parse(color) ?? DisplayColor.Transparent));
    }

    /// <summary>
    /// Returns the palette of the color glyphs of a font for a computed value of <c>font-palette</c>;
    /// <paramref name="families"/> are the names of the family of the font, which the <c>font-family</c> descriptor of the
    /// <c>@font-palette-values</c> rules is compared to.
    /// </summary>
    internal static FontPalette Resolve(ComputedStyle style, OpenTypeColorGlyphs glyphs, IReadOnlyList<string> families, string value)
    {
        var colors = glyphs.PaletteCount == 0 ? [] : ResolveColors(style, glyphs, families, ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)), depth: 0);
        return new FontPalette(glyphs, Array.ConvertAll(colors, static color => DisplayColor.Parse(color) ?? DisplayColor.Transparent));
    }

    /// <summary>
    /// The names the family of a font is known by: the family of its font face (a font is only known by the name of the
    /// <c>@font-face</c> rule that loads it), or the family names of an installed font.
    /// </summary>
    private static IReadOnlyList<string> GetFamilies(MatchedFont font)
    {
        if (font.Face is { } face)
            return [FontFaceSet.ParseFamilies(face.Family) is [var name] ? name : face.Family];

        return font.Font.GetFamilyNames();
    }

    /// <summary>Returns the colors (absolute CSS colors) of the palette a value of <c>font-palette</c> selects.</summary>
    private static string[] ResolveColors(ComputedStyle style, OpenTypeColorGlyphs glyphs, IReadOnlyList<string> families, List<CssComponentValue> value, int depth)
    {
        switch (value)
        {
            // "This keyword causes the user-agent to use the first available palette in the font file marked this way. If [...]
            // no palette in the font is marked this way, this value behaves as normal."
            case [CssPreservedToken { Type: CssTokenType.Ident } keyword] when keyword.Token.IsIdent("light") && glyphs.LightPalette is { } light:
                return GetFontPalette(glyphs, light);

            case [CssPreservedToken { Type: CssTokenType.Ident } keyword] when keyword.Token.IsIdent("dark") && glyphs.DarkPalette is { } dark:
                return GetFontPalette(glyphs, dark);

            // "If no applicable @font-palette-values rule is present, this value behaves as normal."
            case [CssPreservedToken { Type: CssTokenType.Ident } identifier] when identifier.Token.Value.StartsWith("--", StringComparison.Ordinal) && FindRule(style, families, identifier.Token.Value) is { } rule:
                return ApplyRule(style, glyphs, rule);

            case [CssFunction function] when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "palette-mix") && depth < MaxMixDepth && Mix(style, glyphs, families, function, depth) is { } mixed:
                return mixed;

            default:
                return GetFontPalette(glyphs, GetNormalPalette(style, glyphs));
        }
    }

    /// <summary>
    /// The palette of <c>font-palette: normal</c>: "If the color-scheme property is set to a value other than normal,
    /// user-agents should select the first light or dark palette as appropriate. Otherwise, user-agents display the color font
    /// with the default palette" (the palette at index 0).
    /// </summary>
    private static int GetNormalPalette(ComputedStyle style, OpenTypeColorGlyphs glyphs)
    {
        if (glyphs.LightPalette is null && glyphs.DarkPalette is null)
            return 0;

        if (style.GetComputedValue("color-scheme") is "normal" or "")
            return 0;

        return (CssValueComputer.GetUsedColorScheme(style) == "dark" ? glyphs.DarkPalette : glyphs.LightPalette) ?? 0;
    }

    private static string[] GetFontPalette(OpenTypeColorGlyphs glyphs, int index)
    {
        var palette = glyphs.GetPalette(index);
        var colors = new string[palette.Length];
        for (var i = 0; i < palette.Length; i++)
        {
            var color = palette[i];
            colors[i] = color.Alpha == 255
                ? string.Create(CultureInfo.InvariantCulture, $"rgb({color.Red}, {color.Green}, {color.Blue})")
                : string.Create(CultureInfo.InvariantCulture, $"rgba({color.Red}, {color.Green}, {color.Blue}, {color.Alpha / 255d:R})");
        }

        return colors;
    }

    /// <summary>
    /// The rule of a palette name for a font: the name is case-sensitive, and the families of the rule are compared to the
    /// family of the font as the family names are (https://drafts.csswg.org/css-fonts-4/#font-family-desc). When several
    /// rules define the palette for the family, the one of the strongest cascade layer wins, then the last one (the rules
    /// are sorted by layer).
    /// </summary>
    private static CssFontPaletteValuesRule? FindRule(ComputedStyle style, IReadOnlyList<string> families, string name)
    {
        var rules = style.Engine.GetFontPaletteValuesRules();
        for (var i = rules.Count - 1; i >= 0; i--)
        {
            var rule = rules[i];
            if (!string.Equals(rule.Name, name, StringComparison.Ordinal))
                continue;

            foreach (var ruleFamily in rule.Families)
            {
                foreach (var family in families)
                {
                    if (FontSelector.FamilyNamesEqual(ruleFamily, family))
                        return rule;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The palette of a <c>@font-palette-values</c> rule: the palette of the font named by <c>base-palette</c> ("If the
    /// specified base-palette does not exist, then the initial color palette will be created using the palette at index 0",
    /// as when the descriptor is not set), with the colors of <c>override-colors</c> (an entry that is not in the palette is
    /// ignored; the last color of an entry wins).
    /// </summary>
    private static string[] ApplyRule(ComputedStyle style, OpenTypeColorGlyphs glyphs, CssFontPaletteValuesRule rule)
    {
        var index = 0;
        if (rule.BasePalette is { Length: > 0 } basePalette)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(basePalette, "light"))
            {
                index = glyphs.LightPalette ?? 0;
            }
            else if (InfraAscii.EqualsIgnoreAsciiCase(basePalette, "dark"))
            {
                index = glyphs.DarkPalette ?? 0;
            }
            else if (GetInteger(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(CssValueComputer.ComputeGeneric(style, property: null, basePalette, baseUrl: null, hasColors: false)))) is { } integer && integer < glyphs.PaletteCount)
            {
                index = integer;
            }
        }

        var colors = GetFontPalette(glyphs, index);
        if (rule.OverrideColors is { Length: > 0 } overrideColors)
        {
            // The colors are absolute: they are computed without the color of the element
            var computed = CssValueComputer.ComputeGeneric(style, property: null, overrideColors, baseUrl: null, hasColors: true);
            foreach (var item in CssValueComputer.SplitArguments(CssParser.ParseListOfComponentValues(computed)))
            {
                if (item.Count == 2 && GetInteger([item[0]]) is { } entry && entry < colors.Length)
                {
                    colors[entry] = CssSerializer.SerializeValue([item[1]], property: null);
                }
            }
        }

        return colors;
    }

    /// <summary>Returns the value of a non-negative integer (a number, or a math function computed to a number), or null.</summary>
    private static int? GetInteger(List<CssComponentValue> value)
    {
        if (value is [CssFunction { Value: var argument } function] && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "calc"))
            return GetInteger(ValueMatcher.RemoveWhitespace(argument));

        if (value is [CssPreservedToken { Type: CssTokenType.Number } number] && number.Token.NumericValue >= 0)
        {
            // https://drafts.csswg.org/css-values-4/#calc-type-checking: the result of a math function is rounded to the nearest integer
            var rounded = Math.Floor(number.Token.NumericValue + 0.5);
            return rounded > int.MaxValue ? int.MaxValue : (int)rounded;
        }

        return null;
    }

    /// <summary>
    /// The palette of palette-mix() (https://drafts.csswg.org/css-fonts-4/#funcdef-palette-mix): each color is the color-mix()
    /// (https://drafts.csswg.org/css-color-5/#color-mix) of the colors of the entry in the palettes, with the color
    /// interpolation method and the percentages of the function. Returns null when the function is not valid.
    /// </summary>
    private static string[]? Mix(ComputedStyle style, OpenTypeColorGlyphs glyphs, IReadOnlyList<string> families, CssFunction function, int depth)
    {
        var arguments = CssValueComputer.SplitArguments(function.Value);
        string? method = null;
        var start = 0;
        if (arguments is [[CssPreservedToken { Type: CssTokenType.Ident } first, ..], ..] && first.Token.IsIdent("in"))
        {
            // (the arguments are split without their whitespace)
            method = string.Join(' ', arguments[0].Select(static component => CssSerializer.SerializeValue([component], property: null)));
            start = 1;
        }

        var palettes = new List<(string[] Colors, string? Percentage)>();
        for (var i = start; i < arguments.Count; i++)
        {
            // <'font-palette'> && <percentage [0,100]>?
            switch (arguments[i])
            {
                case [var palette]:
                    palettes.Add((ResolveColors(style, glyphs, families, [palette], depth + 1), null));
                    break;
                case [var palette, CssPreservedToken { Type: CssTokenType.Percentage } percentage]:
                    palettes.Add((ResolveColors(style, glyphs, families, [palette], depth + 1), percentage.Token.ToString()));
                    break;
                case [CssPreservedToken { Type: CssTokenType.Percentage } percentage, var palette]:
                    palettes.Add((ResolveColors(style, glyphs, families, [palette], depth + 1), percentage.Token.ToString()));
                    break;
                default:
                    return null;
            }
        }

        if (palettes.Count == 0)
            return null;

        var colors = new string[glyphs.PaletteEntryCount];
        var builder = new StringBuilder();
        for (var entry = 0; entry < colors.Length; entry++)
        {
            builder.Clear();
            builder.Append("color-mix(");
            if (method is not null)
            {
                builder.Append(method).Append(", ");
            }

            for (var i = 0; i < palettes.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(palettes[i].Colors[entry]);
                if (palettes[i].Percentage is { } percentage)
                {
                    builder.Append(' ').Append(percentage);
                }
            }

            builder.Append(')');
            colors[entry] = CssValueComputer.ComputeGeneric(style, property: null, builder.ToString(), baseUrl: null, hasColors: true);
        }

        return colors;
    }
}
