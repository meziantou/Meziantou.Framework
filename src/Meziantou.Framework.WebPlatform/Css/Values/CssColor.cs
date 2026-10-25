using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// A parsed &lt;color&gt; (https://drafts.csswg.org/css-color-4/). The color keeps its color space so it can be
/// serialized as specified (https://drafts.csswg.org/css-color-4/#serializing-color-values).
/// </summary>
internal sealed class CssColor
{
    // Named colors (https://drafts.csswg.org/css-color-4/#named-colors)
    private static readonly FrozenDictionary<string, uint> NamedColors = new Dictionary<string, uint>(StringComparer.Ordinal)
    {
        ["aliceblue"] = 0xf0f8ff, ["antiquewhite"] = 0xfaebd7, ["aqua"] = 0x00ffff, ["aquamarine"] = 0x7fffd4, ["azure"] = 0xf0ffff,
        ["beige"] = 0xf5f5dc, ["bisque"] = 0xffe4c4, ["black"] = 0x000000, ["blanchedalmond"] = 0xffebcd, ["blue"] = 0x0000ff,
        ["blueviolet"] = 0x8a2be2, ["brown"] = 0xa52a2a, ["burlywood"] = 0xdeb887, ["cadetblue"] = 0x5f9ea0, ["chartreuse"] = 0x7fff00,
        ["chocolate"] = 0xd2691e, ["coral"] = 0xff7f50, ["cornflowerblue"] = 0x6495ed, ["cornsilk"] = 0xfff8dc, ["crimson"] = 0xdc143c,
        ["cyan"] = 0x00ffff, ["darkblue"] = 0x00008b, ["darkcyan"] = 0x008b8b, ["darkgoldenrod"] = 0xb8860b, ["darkgray"] = 0xa9a9a9,
        ["darkgreen"] = 0x006400, ["darkgrey"] = 0xa9a9a9, ["darkkhaki"] = 0xbdb76b, ["darkmagenta"] = 0x8b008b, ["darkolivegreen"] = 0x556b2f,
        ["darkorange"] = 0xff8c00, ["darkorchid"] = 0x9932cc, ["darkred"] = 0x8b0000, ["darksalmon"] = 0xe9967a, ["darkseagreen"] = 0x8fbc8f,
        ["darkslateblue"] = 0x483d8b, ["darkslategray"] = 0x2f4f4f, ["darkslategrey"] = 0x2f4f4f, ["darkturquoise"] = 0x00ced1, ["darkviolet"] = 0x9400d3,
        ["deeppink"] = 0xff1493, ["deepskyblue"] = 0x00bfff, ["dimgray"] = 0x696969, ["dimgrey"] = 0x696969, ["dodgerblue"] = 0x1e90ff,
        ["firebrick"] = 0xb22222, ["floralwhite"] = 0xfffaf0, ["forestgreen"] = 0x228b22, ["fuchsia"] = 0xff00ff, ["gainsboro"] = 0xdcdcdc,
        ["ghostwhite"] = 0xf8f8ff, ["gold"] = 0xffd700, ["goldenrod"] = 0xdaa520, ["gray"] = 0x808080, ["green"] = 0x008000,
        ["greenyellow"] = 0xadff2f, ["grey"] = 0x808080, ["honeydew"] = 0xf0fff0, ["hotpink"] = 0xff69b4, ["indianred"] = 0xcd5c5c,
        ["indigo"] = 0x4b0082, ["ivory"] = 0xfffff0, ["khaki"] = 0xf0e68c, ["lavender"] = 0xe6e6fa, ["lavenderblush"] = 0xfff0f5,
        ["lawngreen"] = 0x7cfc00, ["lemonchiffon"] = 0xfffacd, ["lightblue"] = 0xadd8e6, ["lightcoral"] = 0xf08080, ["lightcyan"] = 0xe0ffff,
        ["lightgoldenrodyellow"] = 0xfafad2, ["lightgray"] = 0xd3d3d3, ["lightgreen"] = 0x90ee90, ["lightgrey"] = 0xd3d3d3, ["lightpink"] = 0xffb6c1,
        ["lightsalmon"] = 0xffa07a, ["lightseagreen"] = 0x20b2aa, ["lightskyblue"] = 0x87cefa, ["lightslategray"] = 0x778899, ["lightslategrey"] = 0x778899,
        ["lightsteelblue"] = 0xb0c4de, ["lightyellow"] = 0xffffe0, ["lime"] = 0x00ff00, ["limegreen"] = 0x32cd32, ["linen"] = 0xfaf0e6,
        ["magenta"] = 0xff00ff, ["maroon"] = 0x800000, ["mediumaquamarine"] = 0x66cdaa, ["mediumblue"] = 0x0000cd, ["mediumorchid"] = 0xba55d3,
        ["mediumpurple"] = 0x9370db, ["mediumseagreen"] = 0x3cb371, ["mediumslateblue"] = 0x7b68ee, ["mediumspringgreen"] = 0x00fa9a, ["mediumturquoise"] = 0x48d1cc,
        ["mediumvioletred"] = 0xc71585, ["midnightblue"] = 0x191970, ["mintcream"] = 0xf5fffa, ["mistyrose"] = 0xffe4e1, ["moccasin"] = 0xffe4b5,
        ["navajowhite"] = 0xffdead, ["navy"] = 0x000080, ["oldlace"] = 0xfdf5e6, ["olive"] = 0x808000, ["olivedrab"] = 0x6b8e23,
        ["orange"] = 0xffa500, ["orangered"] = 0xff4500, ["orchid"] = 0xda70d6, ["palegoldenrod"] = 0xeee8aa, ["palegreen"] = 0x98fb98,
        ["paleturquoise"] = 0xafeeee, ["palevioletred"] = 0xdb7093, ["papayawhip"] = 0xffefd5, ["peachpuff"] = 0xffdab9, ["peru"] = 0xcd853f,
        ["pink"] = 0xffc0cb, ["plum"] = 0xdda0dd, ["powderblue"] = 0xb0e0e6, ["purple"] = 0x800080, ["rebeccapurple"] = 0x663399,
        ["red"] = 0xff0000, ["rosybrown"] = 0xbc8f8f, ["royalblue"] = 0x4169e1, ["saddlebrown"] = 0x8b4513, ["salmon"] = 0xfa8072,
        ["sandybrown"] = 0xf4a460, ["seagreen"] = 0x2e8b57, ["seashell"] = 0xfff5ee, ["sienna"] = 0xa0522d, ["silver"] = 0xc0c0c0,
        ["skyblue"] = 0x87ceeb, ["slateblue"] = 0x6a5acd, ["slategray"] = 0x708090, ["slategrey"] = 0x708090, ["snow"] = 0xfffafa,
        ["springgreen"] = 0x00ff7f, ["steelblue"] = 0x4682b4, ["tan"] = 0xd2b48c, ["teal"] = 0x008080, ["thistle"] = 0xd8bfd8,
        ["tomato"] = 0xff6347, ["turquoise"] = 0x40e0d0, ["violet"] = 0xee82ee, ["wheat"] = 0xf5deb3, ["white"] = 0xffffff,
        ["whitesmoke"] = 0xf5f5f5, ["yellow"] = 0xffff00, ["yellowgreen"] = 0x9acd32,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // System colors (https://drafts.csswg.org/css-color-4/#css-system-colors), including the deprecated ones
    private static readonly FrozenSet<string> SystemColors = FrozenSet.Create(StringComparer.Ordinal,
        "accentcolor", "accentcolortext", "activetext", "buttonborder", "buttonface", "buttontext", "canvas", "canvastext", "field", "fieldtext",
        "graytext", "highlight", "highlighttext", "linktext", "mark", "marktext", "selecteditem", "selecteditemtext", "visitedtext",
        "activeborder", "activecaption", "appworkspace", "background", "buttonhighlight", "buttonshadow", "captiontext", "inactiveborder",
        "inactivecaption", "inactivecaptiontext", "infobackground", "infotext", "menu", "menutext", "scrollbar", "threeddarkshadow",
        "threedface", "threedhighlight", "threedlightshadow", "threedshadow", "window", "windowframe", "windowtext");

    private static readonly FrozenSet<string> PredefinedSpaces = FrozenSet.Create(StringComparer.Ordinal,
        "srgb", "srgb-linear", "display-p3", "display-p3-linear", "a98-rgb", "prophoto-rgb", "rec2020", "xyz", "xyz-d50", "xyz-d65",
        "rec2100-pq", "rec2100-hlg", "rec2100-linear");

    private CssColor(string function, double?[] channels, double? alpha, bool legacyRgb, string? sourceSpace = null, double?[]? sourceChannels = null)
    {
        Function = function;
        Channels = channels;
        Alpha = alpha;
        LegacyRgb = legacyRgb;
        SourceSpace = sourceSpace;
        SourceChannels = sourceChannels;
    }

    /// <summary>Creates a color that is not a legacy color, e.g. the result of color-mix() or of a relative color.</summary>
    /// <param name="sourceSpace">The color space the color comes from when it is an sRGB color specified in hsl or hwb.</param>
    public static CssColor Create(string function, double?[] channels, double? alpha, string? sourceSpace = null, double?[]? sourceChannels = null)
        => new(function, channels, alpha, legacyRgb: false, sourceSpace, sourceChannels);

    /// <summary>
    /// The color function: "rgb" (sRGB legacy colors), "hsl" and "hwb" (legacy colors with a none component), "lab", "lch",
    /// "oklab", "oklch", "ictcp", "jzazbz", "jzczhz", or the space of color().
    /// </summary>
    public string Function { get; }

    /// <summary>The channels (null for none).</summary>
    public double?[] Channels { get; }

    /// <summary>The alpha (null for none).</summary>
    public double? Alpha { get; }

    public bool LegacyRgb { get; }

    /// <summary>
    /// The color space of the specified color when the color is an sRGB color specified in hsl or hwb (e.g. hsl(120 50% 50%)):
    /// the color keeps its components for the conversions (e.g. its powerless hue is not missing when it is used in hsl).
    /// </summary>
    public string? SourceSpace { get; }

    /// <summary>The components of the color in <see cref="SourceSpace"/>.</summary>
    public double?[]? SourceChannels { get; }

    /// <summary>Returns the color with another alpha.</summary>
    public CssColor WithAlpha(double? alpha) => new(Function, Channels, alpha, LegacyRgb, SourceSpace, SourceChannels);

    public static bool IsNamedColor(string name) => NamedColors.ContainsKey(InfraAscii.ToAsciiLowercase(name));

    public static bool IsSystemColor(string name) => SystemColors.Contains(InfraAscii.ToAsciiLowercase(name));

    [ThreadStatic]
    private static bool s_keepOutOfRangeRgb;

    /// <summary>
    /// Returns a scope in which the out-of-range channels of the rgb() colors are kept when parsed: the intermediate results
    /// of the additive animations are clamped only once interpolated (https://drafts.csswg.org/css-color-4/#interpolation).
    /// </summary>
    internal static OutOfRangeRgbScope KeepOutOfRangeRgb() => new(enable: true);

    internal readonly struct OutOfRangeRgbScope : IDisposable
    {
        private readonly bool _previous;

        public OutOfRangeRgbScope(bool enable)
        {
            _previous = s_keepOutOfRangeRgb;
            s_keepOutOfRangeRgb = enable;
        }

        public void Dispose() => s_keepOutOfRangeRgb = _previous;
    }

    /// <summary>Parses a color from a single component value; returns null when the value is not a color (keywords such as currentcolor are handled by the caller).</summary>
    public static CssColor? Parse(CssComponentValue value)
    {
        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Hash } hash:
                return ParseHex(hash.Token.Value);

            case CssPreservedToken { Type: CssTokenType.Ident } ident:
            {
                var name = InfraAscii.ToAsciiLowercase(ident.Token.Value);
                if (name == "transparent")
                    return new CssColor("rgb", [0, 0, 0], 0, legacyRgb: true);

                if (NamedColors.TryGetValue(name, out var rgb))
                    return new CssColor("rgb", [(rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff], 1, legacyRgb: true);

                return null;
            }

            case CssFunction function:
                return ParseFunction(InfraAscii.ToAsciiLowercase(function.Name), ValueMatcher.RemoveWhitespace(function.Value));

            default:
                return null;
        }
    }

    private static CssColor? ParseHex(string value)
    {
        if (value.Length is not (3 or 4 or 6 or 8))
            return null;

        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c))
                return null;
        }

        int Digit(int index) => Convert.ToInt32(value.Substring(index, 1), 16);
        int Pair(int index) => Convert.ToInt32(value.Substring(index, 2), 16);
        return value.Length switch
        {
            3 => new CssColor("rgb", [Digit(0) * 17, Digit(1) * 17, Digit(2) * 17], 1, legacyRgb: true),
            4 => new CssColor("rgb", [Digit(0) * 17, Digit(1) * 17, Digit(2) * 17], HexAlpha(Digit(3) * 17), legacyRgb: true),
            6 => new CssColor("rgb", [Pair(0), Pair(2), Pair(4)], 1, legacyRgb: true),
            _ => new CssColor("rgb", [Pair(0), Pair(2), Pair(4)], HexAlpha(Pair(6)), legacyRgb: true),
        };
    }

    /// <summary>
    /// The alpha of a hex color is an 8-bit value: it is the shortest decimal (2 or 3 digits) that round-trips to the same
    /// 8-bit value (https://drafts.csswg.org/css-color-4/#serializing-alpha-values), e.g. #00800080 is rgba(0, 128, 0, 0.5).
    /// </summary>
    private static double HexAlpha(int value)
    {
        var rounded = Math.Round(value / 255.0, 2, MidpointRounding.AwayFromZero);
        return (int)Math.Round(rounded * 255, MidpointRounding.AwayFromZero) == value ? rounded : Math.Round(value / 255.0, 3, MidpointRounding.AwayFromZero);
    }

    // A parsed argument: a number, a percentage, an angle (in degrees) or none
    private readonly record struct Argument(double Value, char Kind)
    {
        public bool IsNone => Kind == 'n';
    }

    private static Argument? ParseArgument(CssComponentValue value)
    {
        if (value is not CssPreservedToken { Token: var token })
            return null;

        // A math function that resolves to NaN is zero (https://drafts.csswg.org/css-values-4/#calc-ieee)
        if (double.IsNaN(token.NumericValue) && token.Type is CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension)
            return ParseArgument(new CssPreservedToken(new CssToken { Type = token.Type, NumericValue = 0, Value = token.Value }));

        switch (token.Type)
        {
            case CssTokenType.Number:
                return new Argument(token.NumericValue, 'd');
            case CssTokenType.Percentage:
                return new Argument(token.NumericValue, '%');
            case CssTokenType.Dimension:
                var degrees = InfraAscii.ToAsciiLowercase(token.Value) switch
                {
                    "deg" => token.NumericValue,
                    "grad" => token.NumericValue * 0.9,
                    "rad" => token.NumericValue * 180 / Math.PI,
                    "turn" => token.NumericValue * 360,
                    _ => double.NaN,
                };
                return double.IsNaN(degrees) ? null : new Argument(degrees, 'a');
            case CssTokenType.Ident when InfraAscii.EqualsIgnoreAsciiCase(token.Value, "none"):
                return new Argument(0, 'n');
            default:
                return null;
        }
    }

    /// <summary>Splits the arguments: the comma-separated legacy syntax, or the space-separated modern syntax with / alpha.</summary>
    private static bool TrySplitArguments(List<CssComponentValue> values, int count, out List<Argument> channels, out Argument? alpha, out bool legacy)
    {
        channels = [];
        alpha = null;
        legacy = values.Count > 1 && values[1] is CssPreservedToken { Type: CssTokenType.Comma };
        if (legacy)
        {
            // a, b, c[, alpha]
            if (values.Count != (count * 2) - 1 && values.Count != (count * 2) + 1)
                return false;

            for (var i = 0; i < values.Count; i++)
            {
                if (i % 2 == 1)
                {
                    if (values[i] is not CssPreservedToken { Type: CssTokenType.Comma })
                        return false;

                    continue;
                }

                if (ParseArgument(values[i]) is not { } argument || argument.IsNone || (i / 2 >= count && argument.Kind == 'a'))
                    return false;

                if (i / 2 < count)
                {
                    channels.Add(argument);
                }
                else
                {
                    alpha = argument;
                }
            }

            return true;
        }

        var slash = values.FindIndex(v => v is CssPreservedToken { Token: var t } && t.IsDelim('/'));
        var channelValues = slash < 0 ? values : values[..slash];
        if (channelValues.Count != count)
            return false;

        foreach (var value in channelValues)
        {
            if (ParseArgument(value) is not { } argument)
                return false;

            channels.Add(argument);
        }

        if (slash >= 0)
        {
            if (slash != values.Count - 2 || ParseArgument(values[^1]) is not { } alphaArgument || alphaArgument.Kind == 'a')
                return false;

            alpha = alphaArgument;
        }

        return true;
    }

    private static double? AlphaValue(Argument? alpha) => alpha switch
    {
        null => 1,
        { IsNone: true } => null,
        { Kind: '%' } a => Math.Clamp(a.Value / 100, 0, 1),
        { } a => Math.Clamp(a.Value, 0, 1),
    };

    private static CssColor? ParseFunction(string name, List<CssComponentValue> values)
    {
        switch (name)
        {
            case "rgb" or "rgba":
            {
                if (!TrySplitArguments(values, 3, out var channels, out var alpha, out var legacy))
                    return null;

                // The legacy syntax requires all the channels to be numbers, or all percentages
                if (legacy && channels.Exists(c => c.Kind != channels[0].Kind))
                    return null;

                if (channels.Exists(c => c.Kind == 'a'))
                    return null;

                var keepOutOfRange = s_keepOutOfRangeRgb;
                var rgb = channels.ConvertAll(c => c.IsNone ? (double?)null : c.Kind == '%' ? c.Value * 2.55 : c.Value);
                if (!keepOutOfRange)
                {
                    rgb = rgb.ConvertAll(c => c is { } channel ? Math.Clamp(channel, 0, 255) : (double?)null);
                }


                // https://drafts.csswg.org/css-color-4/#serializing-sRGB-values: a legacy color with a none component is a color(srgb) color
                if (rgb.Exists(c => c is null) || alpha is { IsNone: true })
                    return new CssColor("srgb", [.. rgb.ConvertAll(c => c / 255)], AlphaValue(alpha), legacyRgb: false);

                return new CssColor("rgb", [.. rgb], AlphaValue(alpha) ?? 0, legacyRgb: true);
            }

            case "hsl" or "hsla" or "hwb":
            {
                if (!TrySplitArguments(values, 3, out var channels, out var alpha, out var legacy))
                    return null;

                if (name == "hwb" && legacy)
                    return null;

                // Hue: a number or an angle; the other channels: percentages (legacy) or numbers/percentages
                if (channels[0].Kind == '%' || (legacy && (channels[1].Kind != '%' || channels[2].Kind != '%')) || channels[1].Kind == 'a' || channels[2].Kind == 'a')
                    return null;

                // A color with a none component keeps its color function (https://drafts.csswg.org/css-color-4/#serializing-sRGB-values)
                if (channels.Exists(c => c.IsNone) || alpha is { IsNone: true })
                    return new CssColor(name == "hwb" ? "hwb" : "hsl", [.. channels.ConvertAll(c => c.IsNone ? (double?)null : c.Value)], AlphaValue(alpha), legacyRgb: false);

                var hue = channels[0].IsNone ? 0 : channels[0].Value;
                var second = channels[1].IsNone ? 0 : channels[1].Value;
                var third = channels[2].IsNone ? 0 : channels[2].Value;
                var saturation = name == "hwb" ? second : Math.Max(0, second);
                var (r, g, b) = name == "hwb" ? HwbToRgb(hue, second / 100, third / 100) : HslToRgb(hue, saturation / 100, third / 100);

                // A saturation above 100% or a lightness outside [0%, 100%] gives sRGB channels outside [0, 255]: they are
                // clamped as the channels of rgb(), so that the serialization (rgb()) is parsed as the same color
                if (!s_keepOutOfRangeRgb)
                {
                    (r, g, b) = (Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
                }

                return new CssColor("rgb", [r * 255, g * 255, b * 255],AlphaValue(alpha) ?? 0, legacyRgb: true, name == "hwb" ? "hwb" : "hsl", [hue, saturation, third]);
            }

            case "lab" or "oklab" or "lch" or "oklch":
            {
                if (!TrySplitArguments(values, 3, out var channels, out var alpha, out var legacy) || legacy)
                    return null;

                var isLab = name.EndsWith("lab", StringComparison.Ordinal);
                var lightnessScale = name.StartsWith("ok", StringComparison.Ordinal) ? 1.0 : 100.0;
                var chromaScale = name switch
                {
                    "lab" => 125.0,
                    "oklab" => 0.4,
                    "lch" => 150.0,
                    _ => 0.4,
                };

                double? Channel(Argument argument, double percentScale)
                {
                    if (argument.IsNone)
                        return null;

                    return argument.Kind == '%' ? argument.Value * percentScale / 100 : argument.Value;
                }

                if (channels[0].Kind == 'a' || channels[1].Kind == 'a' || (isLab && channels[2].Kind == 'a') || (!isLab && channels[2].Kind == '%'))
                    return null;

                // The lightness is clamped to [0, 100] (lab, lch) or [0, 1] (oklab, oklch), and the chroma to positive values
                // (https://drafts.csswg.org/css-color-4/#specifying-lab-lch)
                var lightness = Channel(channels[0], lightnessScale) is { } l ? Math.Clamp(l, 0, lightnessScale) : (double?)null;
                var second = Channel(channels[1], chromaScale);
                if (!isLab && second < 0)
                {
                    second = 0;
                }

                var third = isLab ? Channel(channels[2], chromaScale) : channels[2].IsNone ? null : NormalizeHue(channels[2].Value);

                return new CssColor(name, [lightness, second, third], AlphaValue(alpha), legacyRgb: false);
            }

            case "ictcp" or "jzazbz" or "jzczhz":
            {
                // https://drafts.csswg.org/css-color-hdr-1/#color-syntax: the modern syntax only
                if (!TrySplitArguments(values, 3, out var channels, out var alpha, out var legacy) || legacy)
                    return null;

                var isPolar = name == "jzczhz";
                if (channels[0].Kind == 'a' || channels[1].Kind == 'a' || (!isPolar && channels[2].Kind == 'a') || (isPolar && channels[2].Kind == '%'))
                    return null;

                double? Channel(int index) => channels[index].IsNone ? null : channels[index].Kind == '%' ? channels[index].Value * HdrPercentageScale(name, index) : channels[index].Value;

                // Like the chroma of lch() and oklch(), a negative chroma is zero
                var second = Channel(1);
                if (isPolar && second < 0)
                {
                    second = 0;
                }

                var third = isPolar ? channels[2].IsNone ? null : NormalizeHue(channels[2].Value) : Channel(2);
                return new CssColor(name, [Channel(0), second, third], AlphaValue(alpha), legacyRgb: false);
            }

            case "device-cmyk":
            {
                // The legacy (comma-separated) syntax only accepts numbers
                if (!TrySplitArguments(values, 4, out var channels, out var alpha, out var legacy) || (legacy && channels.Exists(c => c.Kind != 'd')) || channels.Exists(c => c.Kind == 'a'))
                    return null;

                var result = channels.ConvertAll(c => c.IsNone ? (double?)null : Math.Clamp(c.Kind == '%' ? c.Value / 100 : c.Value, 0, 1));
                return new CssColor("device-cmyk", [.. result], AlphaValue(alpha), legacyRgb: false);
            }

            case "color":
            {
                if (values.Count == 0 || values[0] is not CssPreservedToken { Type: CssTokenType.Ident } space)
                    return null;

                var spaceName = InfraAscii.ToAsciiLowercase(space.Token.Value);

                // A custom color space (@color-profile): any number of channels
                if (space.Token.Value.StartsWith("--", StringComparison.Ordinal))
                {
                    var slashIndex = values.FindIndex(v => v is CssPreservedToken { Token: var t } && t.IsDelim('/'));
                    var channelCount = (slashIndex < 0 ? values.Count : slashIndex) - 1;
                    if (channelCount <= 0 || !TrySplitArguments(values[1..], channelCount, out var customChannels, out var customAlpha, out var customLegacy) || customLegacy || customChannels.Exists(c => c.Kind == 'a'))
                        return null;

                    return new CssColor(space.Token.Value, [.. customChannels.ConvertAll(c => c.IsNone ? (double?)null : c.Kind == '%' ? c.Value / 100 : c.Value)], AlphaValue(customAlpha), legacyRgb: false);
                }

                if (!PredefinedSpaces.Contains(spaceName))
                    return null;

                if (!TrySplitArguments(values[1..], 3, out var channels, out var alpha, out var legacy) || legacy || channels.Exists(c => c.Kind == 'a'))
                    return null;

                var result = channels.ConvertAll(c => c.IsNone ? (double?)null : c.Kind == '%' ? c.Value / 100 : c.Value);
                return new CssColor(spaceName == "xyz" ? "xyz-d65" : spaceName, [.. result], AlphaValue(alpha), legacyRgb: false);
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// The value of 1% of a channel of ictcp(), jzazbz() and jzczhz() (https://drafts.csswg.org/css-color-hdr-1/#ICtCp): 100% is
    /// 1 for the lightness, 0.5 for Ct and Cp, 0.21 for az and bz, and 0.26 for the chroma of jzczhz().
    /// </summary>
    internal static double HdrPercentageScale(string function, int channel) => (function, channel) switch
    {
        (_, 0) => 0.01,
        ("ictcp", _) => 0.005,
        ("jzazbz", _) => 0.0021,
        _ => 0.0026,
    };

    // The math functions of the specified colors: percentages are their own type and the relative units are kept
    private static readonly CssCalcContext SpecifiedMathContext = new() { KeepRelativeUnits = true };

    /// <summary>
    /// Validates and serializes a color function whose channels contain math functions, e.g. rgb(calc(50% + 1em) 0 0)
    /// (https://drafts.csswg.org/css-color-4/#serializing-color-values). When the math functions of rgb(), hsl() or hwb() are
    /// resolved at parse time, the color is serialized as a resolved sRGB color. Otherwise, the color is serialized with the
    /// modern syntax (rgba() and hsla() are rgb() and hsl()): the math functions are simplified and the other components
    /// are converted to numbers in their canonical range (e.g. the percentages of rgb() to [0, 255], the lightness of lab()
    /// to [0, 100]). Returns null when the function is not a valid color.
    /// </summary>
    public static string? SerializeWithMathFunctions(CssFunction function)
    {
        var name = InfraAscii.ToAsciiLowercase(function.Name);
        if (name is not ("rgb" or "rgba" or "hsl" or "hsla" or "hwb" or "lab" or "lch" or "oklab" or "oklch" or "ictcp" or "jzazbz" or "jzczhz" or "color"))
            return null;

        // The math functions are replaced with their value, or a value of their type when they cannot be resolved, to validate the color
        var values = ValueMatcher.RemoveWhitespace(function.Value);
        var substituted = new CssFunction(function.Name);
        var resolved = true;
        var hasMathFunction = false;

        // The hue is a <number> or an <angle>: a percentage cannot be resolved in its math functions, even in sign(50%)
        // (https://drafts.csswg.org/css-values-4/#calc-type-checking)
        var hueIndex = name switch
        {
            "hsl" or "hsla" or "hwb" => 0,
            "lch" or "oklch" or "jzczhz" => 2,
            _ => -1,
        };

        var channel = -1;
        foreach (var value in values)
        {
            if (value is not CssPreservedToken { Type: CssTokenType.Comma })
            {
                channel++;
            }

            if (value is not CssFunction math || !CssCalc.IsMathFunction(math.Name))
            {
                substituted.Value.Add(value);
                continue;
            }

            if (channel == hueIndex && ValueMatcher.ContainsPercentage(math.Value))
                return null;

            hasMathFunction = true;
            if (CssCalc.Evaluate(math, CssCalcContext.Default) is { IsNumeric: true } result)
            {
                substituted.Value.Add(CreateNumericToken(result.Value, result.Unit));
                continue;
            }

            if (CssCalc.Evaluate(math, SpecifiedMathContext) is not { } unresolved)
                return null;

            resolved = false;
            substituted.Value.Add(CreateNumericToken(0, unresolved.TypeUnit));
        }

        if (!hasMathFunction || Parse(substituted) is not { } color)
            return null;

        var isLegacySrgb = name is "rgb" or "rgba" or "hsl" or "hsla" or "hwb";
        if (resolved && isLegacySrgb)
            return color.Serialize();

        // name(channels [/ alpha]), the channels of color() follow the color space
        var builder = new StringBuilder();
        var start = 0;
        if (name == "color")
        {
            var space = InfraAscii.ToAsciiLowercase(((CssPreservedToken)values[0]).Token.Value);
            builder.Append("color(").Append(space == "xyz" ? "xyz-d65" : space).Append(' ');
            start = 1;
        }
        else
        {
            builder.Append(name switch { "rgba" => "rgb", "hsla" => "hsl", _ => name }).Append('(');
        }

        var arguments = values[start..].FindAll(v => v is not CssPreservedToken { Type: CssTokenType.Comma });
        var slash = arguments.FindIndex(v => v is CssPreservedToken { Token: var t } && t.IsDelim('/'));
        if (slash >= 0)
        {
            arguments.RemoveAt(slash);
        }

        // The alpha follows the slash, or is the fourth argument of the legacy syntax
        var alphaIndex = slash >= 0 ? slash : name != "color" && arguments.Count == 4 ? 3 : -1;
        for (var i = 0; i < arguments.Count; i++)
        {
            string? serialized;
            if (arguments[i] is CssFunction math && CssCalc.IsMathFunction(math.Name))
            {
                serialized = CssCalc.SerializeSpecifiedValue(math);
            }
            else if (ParseArgument(arguments[i]) is not { } argument)
            {
                return null;
            }
            else if (argument.IsNone)
            {
                serialized = "none";
            }
            else if (i == alphaIndex)
            {
                // An opaque alpha is omitted
                if (AlphaValue(argument) is 1)
                    continue;

                serialized = FormatNumber(AlphaValue(argument) ?? 0);
            }
            else
            {
                serialized = FormatNumber(CanonicalChannel(name, i, argument));
            }

            builder.Append(i == alphaIndex ? " / " : i > 0 ? " " : "").Append(serialized);
        }

        return builder.Append(')').ToString();
    }

    private static CssPreservedToken CreateNumericToken(double value, string unit) => new(unit switch
    {
        "" => new CssToken { Type = CssTokenType.Number, NumericValue = value },
        "%" => new CssToken { Type = CssTokenType.Percentage, NumericValue = value },
        _ => new CssToken { Type = CssTokenType.Dimension, NumericValue = value, Value = unit },
    });

    /// <summary>A channel of a color function converted to a number in its canonical range (https://drafts.csswg.org/css-color-4/#serializing-color-values).</summary>
    private static double CanonicalChannel(string function, int index, Argument argument)
    {
        var value = argument.Value;
        var percentage = argument.Kind == '%';
        return (function, index) switch
        {
            ("rgb" or "rgba", _) => Math.Clamp(percentage ? value * 2.55 : value, 0, 255),
            ("hsl" or "hsla" or "hwb", 0) or ("lch" or "oklch" or "jzczhz", 2) => NormalizeHue(value),
            ("hsl" or "hsla", 1) => Math.Max(0, value),
            ("hsl" or "hsla" or "hwb", _) => value,
            ("lab" or "lch", 0) => Math.Clamp(value, 0, 100),
            ("oklab" or "oklch", 0) => Math.Clamp(percentage ? value / 100 : value, 0, 1),
            ("lab", _) => percentage ? value * 1.25 : value,
            ("lch", _) => Math.Max(0, percentage ? value * 1.5 : value),
            ("oklab", _) => percentage ? value * 0.004 : value,
            ("oklch", _) => Math.Max(0, percentage ? value * 0.004 : value),
            ("jzczhz", 1) => Math.Max(0, percentage ? value * HdrPercentageScale(function, index) : value),
            ("ictcp" or "jzazbz" or "jzczhz", _) => percentage ? value * HdrPercentageScale(function, index) : value,
            _ => percentage ? value / 100 : value,
        };
    }

    private static double NormalizeHue(double hue)
    {
        if (!double.IsFinite(hue))
            return 0;

        var result = hue % 360;
        return result < 0 ? result + 360 : result;
    }

    /// <summary>https://drafts.csswg.org/css-color-4/#hsl-to-rgb</summary>
    internal static (double R, double G, double B) HslToRgb(double hue, double saturation, double lightness)
    {
        hue = NormalizeHue(hue);
        double F(double n)
        {
            var k = (n + (hue / 30)) % 12;
            var a = saturation * Math.Min(lightness, 1 - lightness);
            return lightness - (a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)));
        }

        return (F(0), F(8), F(4));
    }

    /// <summary>https://drafts.csswg.org/css-color-4/#hwb-to-rgb</summary>
    internal static (double R, double G, double B) HwbToRgb(double hue, double white, double black)
    {
        if (white + black >= 1)
        {
            var gray = white / (white + black);
            return (gray, gray, gray);
        }

        var (r, g, b) = HslToRgb(hue, 1, 0.5);
        var scale = 1 - white - black;
        return ((r * scale) + white, (g * scale) + white, (b * scale) + white);
    }

    /// <summary>https://drafts.csswg.org/css-color-4/#serializing-color-values</summary>
    /// <param name="roundLegacyChannels">
    /// Whether the channels of the legacy colors are rounded to integers (https://drafts.csswg.org/css-color-4/#serializing-sRGB-values).
    /// </param>
    public string Serialize(bool roundLegacyChannels = true)
    {
        var builder = new StringBuilder();
        if (LegacyRgb)
        {
            var alpha = Alpha ?? 0;
            // The channels are rounded to integers (after removing the floating-point errors, e.g. 127.49999999999999 is 128)
            string Channel(double? value) => FormatNumber(roundLegacyChannels ? Math.Round(Math.Round(value ?? 0, 6, MidpointRounding.AwayFromZero), MidpointRounding.AwayFromZero) : value ?? 0);
            builder.Append(alpha >= 1 ? "rgb(" : "rgba(");
            builder.Append(Channel(Channels[0])).Append(", ").Append(Channel(Channels[1])).Append(", ").Append(Channel(Channels[2]));
            if (alpha < 1)
            {
                builder.Append(", ").Append(FormatNumber(alpha));
            }

            return builder.Append(')').ToString();
        }

        if (Function is "hsl" or "hwb")
        {
            builder.Append(Function).Append('(').Append(Channels[0] is { } hue ? FormatNumber(hue) : "none");
            for (var i = 1; i < 3; i++)
            {
                builder.Append(' ').Append(Channels[i] is { } value ? FormatNumber(value) + "%" : "none");
            }

            if (Alpha is not 1)
            {
                builder.Append(" / ").Append(Alpha is { } hslAlpha ? FormatNumber(hslAlpha) : "none");
            }

            return builder.Append(')').ToString();
        }

        var isFunction = Function is "lab" or "lch" or "oklab" or "oklch" or "ictcp" or "jzazbz" or "jzczhz";
        builder.Append(isFunction ? Function + "(" : "color(" + Function + " ");
        builder.Append(string.Join(' ', Channels.Select(c => c is { } value ? FormatNumber(value) : "none")));
        if (Alpha is not 1)
        {
            builder.Append(" / ").Append(Alpha is { } alphaValue ? FormatNumber(alphaValue) : "none");
        }

        return builder.Append(')').ToString();
    }

    /// <summary>
    /// Numbers are serialized with at most 6 significant digits and 6 decimals, like the browsers (e.g. lch(10 20 1.28rad) is
    /// lch(10 20 73.3386)); the infinite values and NaN as math functions (https://drafts.csswg.org/css-values-4/#calc-serialize).
    /// </summary>
    internal static string FormatNumber(double value)
    {
        if (!double.IsFinite(value))
            return double.IsNaN(value) ? "calc(NaN)" : value > 0 ? "calc(infinity)" : "calc(-infinity)";

        var integerDigits = Math.Abs(value) < 1 ? 0 : (int)Math.Floor(Math.Log10(Math.Abs(value))) + 1;
        var rounded = Math.Round(value, Math.Clamp(6 - integerDigits, 0, 6), MidpointRounding.AwayFromZero);
        if (rounded == 0)
            return "0";

        return rounded.ToString("0.######", CultureInfo.InvariantCulture);
    }

    /// <summary>The sRGB channels (0-255) and alpha of legacy colors, or null for the other color spaces.</summary>
    public (double R, double G, double B, double A)? ToRgb() => LegacyRgb ? (Channels[0] ?? 0, Channels[1] ?? 0, Channels[2] ?? 0, Alpha ?? 0) : null;
}
