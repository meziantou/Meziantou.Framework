using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>A family of a <c>font-family</c> list (https://drafts.csswg.org/css-fonts-4/#font-family-prop).</summary>
/// <param name="Name">The family name, or the generic family keyword (e.g. <c>serif</c>).</param>
/// <param name="IsGeneric">Whether the family is a generic family (https://drafts.csswg.org/css-fonts-4/#generic-font-families).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontFamilyName(string Name, bool IsGeneric)
{
    /// <summary>
    /// Parses a computed value of <c>font-family</c>: the strings and the sequences of identifiers are family names, a single
    /// identifier that is a generic family keyword (or a <c>generic()</c> function) is a generic family.
    /// </summary>
    public static FontFamilyName[] ParseList(string value)
    {
        var families = new List<FontFamilyName>();
        foreach (var entry in CssValueComputer.SplitArguments(CssParser.ParseListOfComponentValues(value)))
        {
            var significant = ValueMatcher.RemoveWhitespace(entry);
            if (significant is [CssPreservedToken { Type: CssTokenType.String } stringToken])
            {
                families.Add(new FontFamilyName(stringToken.Token.Value, IsGeneric: false));
            }
            else if (significant is [CssFunction { } function] && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "generic"))
            {
                families.Add(new FontFamilyName(CssSerializer.SerializeValue(function.Value, property: null).Trim(), IsGeneric: true));
            }
            else if (significant is [CssPreservedToken { Type: CssTokenType.Ident } ident] && Om.FontFace.IsGenericFamily(InfraAscii.ToAsciiLowercase(ident.Token.Value)))
            {
                families.Add(new FontFamilyName(InfraAscii.ToAsciiLowercase(ident.Token.Value), IsGeneric: true));
            }
            else if (significant.Count > 0 && significant.TrueForAll(component => component is CssPreservedToken { Type: CssTokenType.Ident }))
            {
                families.Add(new FontFamilyName(string.Join(' ', significant.Select(component => ((CssPreservedToken)component).Token.Value)), IsGeneric: false));
            }
        }

        return [.. families];
    }
}

/// <summary>The kind of a value of <c>font-style</c>.</summary>
internal enum FontSlopeKind : byte
{
    Normal,
    Italic,
    Oblique,
}

/// <summary>A value of <c>font-style</c> (https://drafts.csswg.org/css-fonts-4/#font-style-prop).</summary>
/// <param name="Kind">normal, italic or oblique.</param>
/// <param name="Angle">The angle of an oblique style, in degrees (14 for <c>oblique</c> without angle).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontSlope(FontSlopeKind Kind, double Angle)
{
    /// <summary>The angle of <c>oblique</c> without angle (https://drafts.csswg.org/css-fonts-4/#valdef-font-style-oblique-angle--90deg-90deg).</summary>
    public const double DefaultObliqueAngle = 14;

    public static FontSlope Normal => default;

    public static FontSlope Italic => new(FontSlopeKind.Italic, 0);

    /// <summary>
    /// Parses a value of <c>font-style</c> (normal, italic, oblique, oblique &lt;angle&gt;). <c>left</c> and <c>right</c>
    /// (https://drafts.csswg.org/css-fonts-5/#valdef-font-style-left) are oblique styles slanted to the left and to the
    /// right in horizontal text. A value that cannot be parsed is normal.
    /// </summary>
    public static FontSlope Parse(string value)
    {
        value = value.Trim();
        switch (value)
        {
            case "" or "normal":
                return Normal;
            case "italic":
                return Italic;
            case "oblique" or "left":
                return new(FontSlopeKind.Oblique, DefaultObliqueAngle);
            case "right":
                return new(FontSlopeKind.Oblique, -DefaultObliqueAngle);
        }

        if (value.StartsWith("oblique ", StringComparison.Ordinal) && TryParseAngle(value[8..].Trim(), out var angle))
            return angle == 0 ? Normal : new(FontSlopeKind.Oblique, Math.Clamp(angle, -90, 90));

        return Normal;
    }

    /// <summary>Parses an angle (a dimension in deg, grad, rad or turn) and converts it to degrees.</summary>
    internal static bool TryParseAngle(string value, out double degrees)
    {
        degrees = 0;
        if (ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)) is not [CssPreservedToken { Type: CssTokenType.Dimension } dimension])
            return false;

        var number = dimension.Token.NumericValue;
        double? result = InfraAscii.ToAsciiLowercase(dimension.Token.Value) switch
        {
            "deg" => number,
            "grad" => number * 0.9,
            "rad" => number * 180 / Math.PI,
            "turn" => number * 360,
            _ => null,
        };

        degrees = result ?? 0;
        return result is not null;
    }
}

/// <summary>The font metric of <c>font-size-adjust</c> (https://drafts.csswg.org/css-fonts-5/#font-size-adjust-prop).</summary>
internal enum FontSizeAdjustMetric : byte
{
    ExHeight,
    CapHeight,
    ChWidth,
    IcWidth,
    IcHeight,
}

/// <summary>A computed value of <c>font-size-adjust</c> (https://drafts.csswg.org/css-fonts-5/#font-size-adjust-prop).</summary>
/// <param name="Metric">The font metric that is normalized.</param>
/// <param name="Value">The aspect value, or NaN for <c>none</c>.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontSizeAdjust(FontSizeAdjustMetric Metric, double Value)
{
    public static FontSizeAdjust None => new(FontSizeAdjustMetric.ExHeight, double.NaN);

    public bool IsNone => double.IsNaN(Value);

    /// <summary>Returns the keyword of a metric.</summary>
    public static string GetKeyword(FontSizeAdjustMetric metric) => metric switch
    {
        FontSizeAdjustMetric.CapHeight => "cap-height",
        FontSizeAdjustMetric.ChWidth => "ch-width",
        FontSizeAdjustMetric.IcWidth => "ic-width",
        FontSizeAdjustMetric.IcHeight => "ic-height",
        _ => "ex-height",
    };

    /// <summary>Returns the metric of a keyword, or null when the keyword is not a metric.</summary>
    public static FontSizeAdjustMetric? ParseMetric(string keyword) => keyword switch
    {
        "ex-height" => FontSizeAdjustMetric.ExHeight,
        "cap-height" => FontSizeAdjustMetric.CapHeight,
        "ch-width" => FontSizeAdjustMetric.ChWidth,
        "ic-width" => FontSizeAdjustMetric.IcWidth,
        "ic-height" => FontSizeAdjustMetric.IcHeight,
        _ => null,
    };

    /// <summary>Parses a computed value (<c>none</c>, or an optional metric and a number); <c>from-font</c> is none.</summary>
    public static FontSizeAdjust Parse(string value)
    {
        var metric = FontSizeAdjustMetric.ExHeight;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ParseMetric(part) is { } parsed)
            {
                metric = parsed;
            }
            else if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= 0)
            {
                return new(metric, number);
            }
        }

        return None;
    }
}

/// <summary>The value of <c>font-synthesis-style</c> (https://drafts.csswg.org/css-fonts-4/#font-synthesis-style).</summary>
internal enum FontStyleSynthesis : byte
{
    Auto,
    None,

    /// <summary>Only the oblique styles are synthesized (not italic).</summary>
    ObliqueOnly,
}

/// <summary>
/// The computed values of the font properties that select the fonts of an element (https://drafts.csswg.org/css-fonts-4/#font-matching-algorithm):
/// the key of the font lists of a <see cref="FontSelector"/>. The font size is not part of it: the fonts are scaled by their
/// users (see <see cref="MatchedFont"/>).
/// </summary>
/// <param name="Family">The computed value of <c>font-family</c>.</param>
/// <param name="Weight">The computed value of <c>font-weight</c>.</param>
/// <param name="Style">The computed value of <c>font-style</c>.</param>
/// <param name="Width">The computed value of <c>font-width</c>, as a percentage.</param>
/// <param name="SizeAdjust">The computed value of <c>font-size-adjust</c>.</param>
/// <param name="SynthesizeWeight">Whether <c>font-synthesis-weight</c> is auto.</param>
/// <param name="SynthesizeStyle">The value of <c>font-synthesis-style</c>.</param>
/// <param name="VariationSettings">The computed value of <c>font-variation-settings</c>.</param>
/// <param name="OpticalSizing">Whether <c>font-optical-sizing</c> is auto.</param>
/// <param name="TreeScope">
/// The innermost shadow root with <c>@font-face</c> rules that the family names can reference (from the tree scope of the
/// declaration of <c>font-family</c>, https://drafts.csswg.org/css-scoping-1/#shadow-names), or null for the document.
/// </param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontDescription(
    string Family,
    double Weight,
    FontSlope Style,
    double Width,
    FontSizeAdjust SizeAdjust,
    bool SynthesizeWeight,
    FontStyleSynthesis SynthesizeStyle,
    string VariationSettings,
    bool OpticalSizing,
    ShadowRoot? TreeScope)
{
    /// <summary>The initial values of the font properties (https://drafts.csswg.org/css-fonts-4/#font-prop).</summary>
    public static FontDescription Initial { get; } = new("serif", 400, FontSlope.Normal, 100, FontSizeAdjust.None, SynthesizeWeight: true, FontStyleSynthesis.Auto, "normal", OpticalSizing: true, TreeScope: null);

    /// <summary>Returns the description of the computed font properties of a style.</summary>
    /// <param name="style">The style.</param>
    /// <param name="includeSizeAdjust">Whether <c>font-size-adjust</c> is read (it is not when its <c>from-font</c> value is computed).</param>
    public static FontDescription FromStyle(ComputedStyle style, bool includeSizeAdjust = true)
    {
        return new FontDescription(
            style.GetComputedValue("font-family"),
            CssValueComputer.TryParseNumber(style.GetComputedValue("font-weight"), out var weight) ? weight : 400,
            FontSlope.Parse(style.GetComputedValue("font-style")),
            ParsePercentage(style.GetComputedValue("font-width")) ?? 100,
            includeSizeAdjust ? FontSizeAdjust.Parse(style.GetComputedValue("font-size-adjust")) : FontSizeAdjust.None,
            style.GetComputedValue("font-synthesis-weight") != "none",
            style.GetComputedValue("font-synthesis-style") switch
            {
                "none" => FontStyleSynthesis.None,
                "oblique-only" => FontStyleSynthesis.ObliqueOnly,
                _ => FontStyleSynthesis.Auto,
            },
            style.GetComputedValue("font-variation-settings"),
            style.GetComputedValue("font-optical-sizing") != "none",
            style.Engine.Document.FontSelector.GetFontFamilyScope(style));
    }

    /// <summary>Parses a computed value of <c>font-variation-settings</c> (<c>normal</c>, or a list of <c>"tag" value</c>).</summary>
    public static FontVariationSetting[] ParseVariationSettings(string value)
    {
        if (value is "normal" or "")
            return [];

        var settings = new List<FontVariationSetting>();
        foreach (var entry in CssValueComputer.SplitArguments(CssParser.ParseListOfComponentValues(value)))
        {
            if (ValueMatcher.RemoveWhitespace(entry) is [CssPreservedToken { Type: CssTokenType.String } tag, CssPreservedToken { Type: CssTokenType.Number } number] && tag.Token.Value.Length == 4)
            {
                var name = tag.Token.Value;
                settings.Add(new FontVariationSetting(((uint)name[0] << 24) | ((uint)name[1] << 16) | ((uint)name[2] << 8) | name[3], number.Token.NumericValue));
            }
        }

        return [.. settings];
    }

    /// <summary>Parses a percentage (e.g. <c>87.5%</c>) to a number, or returns null.</summary>
    internal static double? ParsePercentage(string value)
        => value.EndsWith('%', StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
}
