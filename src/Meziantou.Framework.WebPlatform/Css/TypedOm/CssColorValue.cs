using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.TypedOm;

/// <summary>
/// The CSSColorRGBComp, CSSColorPercent, CSSColorNumber and CSSColorAngle typedefs: (CSSNumberish or CSSKeywordish)
/// (https://drafts.css-houdini.org/css-typed-om-1/#typedefdef-csscolorrgbcomp).
/// </summary>
public readonly struct CssColorChannel : IUnionValue, IEquatable<CssColorChannel>
{
    private CssColorChannel(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="double"/>, a <see cref="CssNumericValue"/>, a <see cref="string"/> or a <see cref="CssKeywordValue"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Converts a <c>double</c> to a <c>CssColorChannel</c>.</summary>
    [NotInIdl]
    public static implicit operator CssColorChannel(double value) => new(value);

    /// <summary>Converts a <c>CssNumericValue</c> to a <c>CssColorChannel</c>.</summary>
    [NotInIdl]
    public static implicit operator CssColorChannel(CssNumericValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>string</c> to a <c>CssColorChannel</c>.</summary>
    [NotInIdl]
    public static implicit operator CssColorChannel(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>CssKeywordValue</c> to a <c>CssColorChannel</c>.</summary>
    [NotInIdl]
    public static implicit operator CssColorChannel(CssKeywordValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a <c>CssColorChannel</c> from a <c>double</c>.</summary>
    [NotInIdl]
    public static CssColorChannel FromDouble(double value) => value;

    /// <summary>Creates a <c>CssColorChannel</c> from a <c>CssNumericValue</c>.</summary>
    [NotInIdl]
    public static CssColorChannel FromCssNumericValue(CssNumericValue value) => value;

    /// <summary>Creates a <c>CssColorChannel</c> from a <c>string</c>.</summary>
    [NotInIdl]
    public static CssColorChannel FromString(string value) => value;

    /// <summary>Creates a <c>CssColorChannel</c> from a <c>CssKeywordValue</c>.</summary>
    [NotInIdl]
    public static CssColorChannel FromCssKeywordValue(CssKeywordValue value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(CssColorChannel other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CssColorChannel other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two <c>CssColorChannel</c> values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(CssColorChannel left, CssColorChannel right) => left.Equals(right);

    /// <summary>Determines whether two <c>CssColorChannel</c> values are not equal.</summary>
    [NotInIdl]
    public static bool operator !=(CssColorChannel left, CssColorChannel right) => !left.Equals(right);
}

/// <summary>The CSSKeywordish typedef: (DOMString or CSSKeywordValue) (https://drafts.css-houdini.org/css-typed-om-1/#typedefdef-csskeywordish).</summary>
public readonly struct CssKeywordish : IUnionValue, IEquatable<CssKeywordish>
{
    private CssKeywordish(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="string"/> or a <see cref="CssKeywordValue"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Converts a <c>string</c> to a <c>CssKeywordish</c>.</summary>
    [NotInIdl]
    public static implicit operator CssKeywordish(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>CssKeywordValue</c> to a <c>CssKeywordish</c>.</summary>
    [NotInIdl]
    public static implicit operator CssKeywordish(CssKeywordValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a <c>CssKeywordish</c> from a <c>string</c>.</summary>
    [NotInIdl]
    public static CssKeywordish FromString(string value) => value;

    /// <summary>Creates a <c>CssKeywordish</c> from a <c>CssKeywordValue</c>.</summary>
    [NotInIdl]
    public static CssKeywordish FromCssKeywordValue(CssKeywordValue value) => value;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#rectify-a-keywordish-value</summary>
    internal CssKeywordValue Rectify() => Value as CssKeywordValue ?? new CssKeywordValue(Value as string ?? "");

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(CssKeywordish other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CssKeywordish other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two <c>CssKeywordish</c> values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(CssKeywordish left, CssKeywordish right) => left.Equals(right);

    /// <summary>Determines whether two <c>CssKeywordish</c> values are not equal.</summary>
    [NotInIdl]
    public static bool operator !=(CssKeywordish left, CssKeywordish right) => !left.Equals(right);
}

/// <summary>The kinds of color channels, which determine how a value is rectified (https://drafts.css-houdini.org/css-typed-om-1/#rectify-a-csscolorrgbcomp).</summary>
internal enum CssColorChannelKind
{
    /// <summary>CSSColorRGBComp: a &lt;number&gt;, a &lt;percentage&gt; or none; a double is a percentage (0.5 is 50%).</summary>
    RgbComponent,

    /// <summary>CSSColorPercent: a &lt;percentage&gt; or none; a double is a percentage (0.5 is 50%).</summary>
    Percent,

    /// <summary>CSSColorNumber: a &lt;number&gt; or none.</summary>
    Number,

    /// <summary>CSSColorAngle: an &lt;angle&gt; or a keyword; a double is in degrees.</summary>
    Angle,
}

/// <summary>
/// A &lt;color&gt; value (https://drafts.css-houdini.org/css-typed-om-1/#csscolorvalue). The browsers do not reify the
/// colors of the properties as color values (they are direct <see cref="CssStyleValue"/> objects, like in this library):
/// the color values are created by their constructors and by <see cref="Parse(string)"/>.
/// </summary>
/// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#csscolorvalue"/>
[IdlInterface("CSSColorValue", "https://drafts.css-houdini.org/css-typed-om-1/#csscolorvalue")]
public abstract class CssColorValue : CssStyleValue
{
    private protected CssColorValue()
    {
    }

    /// <summary>
    /// Parses a &lt;color&gt; (https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolorvalue-parse): a color function or
    /// a named color is a <see cref="CssColorValue"/>, currentcolor and the system colors are <see cref="CssKeywordValue"/>
    /// objects, and the other colors (e.g. color-mix()) are direct <see cref="CssStyleValue"/> objects.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolorvalue-parse"/>
    [IdlMember("parse", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolorvalue-parse")]
    public static CssStyleValue Parse(string cssText)
    {
        ArgumentNullException.ThrowIfNull(cssText);
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(cssText));
        if (values is not [var value] || !ValueMatcher.IsColor(value))
            throw DomException.Syntax($"'{cssText}' is not a valid color");

        if (value is CssPreservedToken { Type: CssTokenType.Ident } ident && Values.CssColor.Parse(value) is null)
            return new CssKeywordValue(InfraAscii.ToAsciiLowercase(ident.Token.Value));

        return Reify(value) ?? new CssStyleValue("color", cssText.Trim(' ', '\t', '\n'));
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#reify-a-color-value: returns null when the color cannot be represented (e.g. color-mix()).</summary>
    private static CssColorValue? Reify(CssComponentValue value)
    {
        if (value is not CssFunction function)
        {
            // A hex color or a named color is an rgb() color
            if (Values.CssColor.Parse(value) is not { } color)
                return null;

            CssStyleValue Channel(double? value) => new CssUnitValue(value ?? 0, "number");
            return new CssRgb(Channel(color.Channels[0]), Channel(color.Channels[1]), Channel(color.Channels[2]), new CssUnitValue((color.Alpha ?? 1) * 100, "percent"));
        }

        var name = InfraAscii.ToAsciiLowercase(function.Name);
        var arguments = ValueMatcher.RemoveWhitespace(function.Value).Where(v => v is not CssPreservedToken { Type: CssTokenType.Comma }).ToList();
        CssStyleValue? alpha = new CssUnitValue(100, "percent");
        var slash = arguments.FindIndex(v => v is CssPreservedToken { Token: var token } && token.IsDelim('/'));
        if (slash >= 0)
        {
            if (slash != arguments.Count - 2)
                return null;

            alpha = ReifyChannel(arguments[^1], percentScale: 1);
            arguments.RemoveRange(slash, 2);
        }
        else if (name is "rgba" or "hsla" or "rgb" or "hsl" && arguments.Count == 4)
        {
            // The legacy syntax: the alpha is the fourth argument
            alpha = ReifyChannel(arguments[3], percentScale: 1);
            arguments.RemoveAt(3);
        }

        if (alpha is null)
            return null;

        try
        {
            switch (name)
            {
                case "rgb" or "rgba" when arguments.Count == 3:
                    return Create(arguments, [Number, Number, Number], channels => new CssRgb(channels[0], channels[1], channels[2], alpha));
                case "hsl" or "hsla" when arguments.Count == 3:
                    return Create(arguments, [Angle, 100, 100], channels => new CssHsl(channels[0], channels[1], channels[2], alpha));
                case "hwb" when arguments.Count == 3:
                    return Create(arguments, [Angle, 100, 100], channels => new CssHwb((CssNumericValue)channels[0], (CssNumericValue)channels[1], (CssNumericValue)channels[2], (CssNumericValue)alpha));
                case "lab" when arguments.Count == 3:
                    return Create(arguments, [100, Number, Number], channels => new CssLab(channels[0], channels[1], channels[2], alpha));
                case "lch" when arguments.Count == 3:
                    return Create(arguments, [100, 150, Angle], channels => new CssLch(channels[0], channels[1], channels[2], alpha));
                case "oklab" when arguments.Count == 3:
                    return Create(arguments, [1, Number, Number], channels => new CssOkLab(channels[0], channels[1], channels[2], alpha));
                case "oklch" when arguments.Count == 3:
                    return Create(arguments, [1, 0.4, Angle], channels => new CssOkLch(channels[0], channels[1], channels[2], alpha));
                case "color" when arguments is [CssPreservedToken { Type: CssTokenType.Ident } space, _, _, _]:
                    return Create(arguments[1..], [1, 1, 1], channels => new CssColor(InfraAscii.ToAsciiLowercase(space.Token.Value), [.. channels.Select(ToChannel)], (CssNumericValue)alpha));
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is DomException or TypeErrorException or InvalidCastException)
        {
            return null;
        }

        // The numbers of the channels whose canonical type is a percentage are converted with the percent scale (e.g. 1 is 100%
        // for the saturation of hsl(), but 0.4 is 100% for the chroma of oklch())
        static CssColorValue? Create(List<CssComponentValue> arguments, double[] percentScales, Func<List<CssStyleValue>, CssColorValue> create)
        {
            var channels = new List<CssStyleValue>(arguments.Count);
            for (var i = 0; i < arguments.Count; i++)
            {
                if (ReifyChannel(arguments[i], percentScales[i]) is not { } channel)
                    return null;

                channels.Add(channel);
            }

            return create(channels);
        }
    }

    /// <summary>The percent scale of a channel that is a &lt;number&gt;.</summary>
    private const double Number = double.NaN;

    /// <summary>The percent scale of a channel that is an &lt;angle&gt; (a hue).</summary>
    private const double Angle = 0;

    /// <summary>
    /// Reifies a channel of a color function: none is a keyword, a hue is an angle (a number is in degrees), and a number of
    /// a channel whose canonical type is a percentage is converted (<paramref name="percentScale"/> is the number that is 100%).
    /// </summary>
    private static CssStyleValue? ReifyChannel(CssComponentValue value, double percentScale)
    {
        if (value is not CssPreservedToken { Token: var token })
            return null;

        return token.Type switch
        {
            CssTokenType.Ident when token.IsIdent("none") => new CssKeywordValue("none"),
            CssTokenType.Number when percentScale == Angle => new CssUnitValue(token.NumericValue, "deg"),
            CssTokenType.Number when double.IsNaN(percentScale) => new CssUnitValue(token.NumericValue, "number"),
            CssTokenType.Number => new CssUnitValue(token.NumericValue * 100 / percentScale, "percent"),
            CssTokenType.Percentage => new CssUnitValue(token.NumericValue, "percent"),
            CssTokenType.Dimension when CssUnits.GetBaseType(InfraAscii.ToAsciiLowercase(token.Value)) == CssNumericBaseType.Angle => new CssUnitValue(token.NumericValue, InfraAscii.ToAsciiLowercase(token.Value)),
            _ => null,
        };
    }

    /// <summary>Rectifies the value of a channel (https://drafts.css-houdini.org/css-typed-om-1/#rectify-a-csscolorrgbcomp), or throws a SyntaxError.</summary>
    internal static CssStyleValue Rectify(CssColorChannel value, CssColorChannelKind kind)
    {
        switch (value.Value)
        {
            case double number:
                return kind switch
                {
                    CssColorChannelKind.Number => new CssUnitValue(number, "number"),
                    CssColorChannelKind.Angle => new CssUnitValue(number, "deg"),
                    _ => new CssUnitValue(number * 100, "percent"),
                };

            case CssNumericValue numeric:
            {
                var type = numeric.NumericType;
                var matches = kind switch
                {
                    CssColorChannelKind.RgbComponent => type.IsNumber || type.Matches(CssNumericBaseType.Percent),
                    CssColorChannelKind.Percent => type.Matches(CssNumericBaseType.Percent),
                    CssColorChannelKind.Number => type.IsNumber,
                    _ => type.Matches(CssNumericBaseType.Angle),
                };

                return matches ? numeric : throw DomException.Syntax($"'{numeric}' is not a valid color channel");
            }

            default:
            {
                var keyword = value.Value as CssKeywordValue ?? new CssKeywordValue(value.Value as string ?? "");
                if (kind != CssColorChannelKind.Angle && !InfraAscii.EqualsIgnoreAsciiCase(keyword.Value, "none"))
                    throw DomException.Syntax($"'{keyword.Value}' is not a valid color channel");

                return keyword;
            }
        }
    }

    /// <summary>Returns the value of a channel as a union.</summary>
    internal static CssColorChannel ToChannel(CssStyleValue value) => value is CssNumericValue numeric ? numeric : (CssKeywordValue)value;

    /// <summary>Serializes a color function, e.g. rgb(255 0 0 / 50%).</summary>
    private protected static string Serialize(string name, CssStyleValue alpha, params ReadOnlySpan<CssStyleValue> channels)
    {
        var builder = new StringBuilder(name).Append('(');
        for (var i = 0; i < channels.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(channels[i].ToString());
        }

        return builder.Append(" / ").Append(alpha.ToString()).Append(')').ToString();
    }
}

/// <summary>rgb() and rgba() (https://drafts.css-houdini.org/css-typed-om-1/#cssrgb).</summary>
/// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#cssrgb"/>
[IdlInterface("CSSRGB", "https://drafts.css-houdini.org/css-typed-om-1/#cssrgb")]
public sealed class CssRgb : CssColorValue
{
    private CssStyleValue _r;
    private CssStyleValue _g;
    private CssStyleValue _b;
    private CssStyleValue _alpha;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrgb-cssrgb</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrgb-cssrgb"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrgb-cssrgb")]
    public CssRgb(CssColorChannel r, CssColorChannel g, CssColorChannel b, CssColorChannel? alpha = null)
    {
        _r = Rectify(r, CssColorChannelKind.RgbComponent);
        _g = Rectify(g, CssColorChannelKind.RgbComponent);
        _b = Rectify(b, CssColorChannelKind.RgbComponent);
        _alpha = Rectify(alpha ?? 1, CssColorChannelKind.Percent);
    }

    internal CssRgb(CssStyleValue r, CssStyleValue g, CssStyleValue b, CssStyleValue alpha)
        : this(ToChannel(r), ToChannel(g), ToChannel(b), ToChannel(alpha))
    {
    }

    /// <summary>Gets or sets the red channel of the <c>rgb()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrgb-r"/>
    [IdlMember("r", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrgb-r")]
    public CssColorChannel R
    {
        get => ToChannel(_r);
        set => _r = Rectify(value, CssColorChannelKind.RgbComponent);
    }

    /// <summary>Gets or sets the green channel of the <c>rgb()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrgb-g"/>
    [IdlMember("g", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrgb-g")]
    public CssColorChannel G
    {
        get => ToChannel(_g);
        set => _g = Rectify(value, CssColorChannelKind.RgbComponent);
    }

    /// <summary>Gets or sets the blue channel of the <c>rgb()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrgb-b"/>
    [IdlMember("b", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrgb-b")]
    public CssColorChannel B
    {
        get => ToChannel(_b);
        set => _b = Rectify(value, CssColorChannelKind.RgbComponent);
    }

    /// <summary>Gets or sets the alpha (opacity) channel of the <c>rgb()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrgb-alpha"/>
    [IdlMember("alpha", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrgb-alpha")]
    public CssColorChannel Alpha
    {
        get => ToChannel(_alpha);
        set => _alpha = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => Serialize("rgb", _alpha, _r, _g, _b);
}

/// <summary>hsl() and hsla() (https://drafts.css-houdini.org/css-typed-om-1/#csshsl).</summary>
/// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#csshsl"/>
[IdlInterface("CSSHSL", "https://drafts.css-houdini.org/css-typed-om-1/#csshsl")]
public sealed class CssHsl : CssColorValue
{
    private CssStyleValue _h;
    private CssStyleValue _s;
    private CssStyleValue _l;
    private CssStyleValue _alpha;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-csshsl-csshsl</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csshsl-csshsl"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csshsl-csshsl")]
    public CssHsl(CssColorChannel h, CssColorChannel s, CssColorChannel l, CssColorChannel? alpha = null)
    {
        _h = Rectify(h, CssColorChannelKind.Angle);
        _s = Rectify(s, CssColorChannelKind.Percent);
        _l = Rectify(l, CssColorChannelKind.Percent);
        _alpha = Rectify(alpha ?? 1, CssColorChannelKind.Percent);
    }

    internal CssHsl(CssStyleValue h, CssStyleValue s, CssStyleValue l, CssStyleValue alpha)
        : this(ToChannel(h), ToChannel(s), ToChannel(l), ToChannel(alpha))
    {
    }

    /// <summary>Gets or sets the hue channel of the <c>hsl()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csshsl-h"/>
    [IdlMember("h", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csshsl-h")]
    public CssColorChannel H
    {
        get => ToChannel(_h);
        set => _h = Rectify(value, CssColorChannelKind.Angle);
    }

    /// <summary>Gets or sets the saturation channel of the <c>hsl()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csshsl-s"/>
    [IdlMember("s", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csshsl-s")]
    public CssColorChannel S
    {
        get => ToChannel(_s);
        set => _s = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <summary>Gets or sets the lightness channel of the <c>hsl()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csshsl-l"/>
    [IdlMember("l", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csshsl-l")]
    public CssColorChannel L
    {
        get => ToChannel(_l);
        set => _l = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <summary>Gets or sets the alpha (opacity) channel of the <c>hsl()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csshsl-alpha"/>
    [IdlMember("alpha", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csshsl-alpha")]
    public CssColorChannel Alpha
    {
        get => ToChannel(_alpha);
        set => _alpha = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => Serialize("hsl", _alpha, _h, _s, _l);
}

/// <summary>hwb() (https://drafts.css-houdini.org/css-typed-om-1/#csshwb).</summary>
/// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#csshwb"/>
[IdlInterface("CSSHWB", "https://drafts.css-houdini.org/css-typed-om-1/#csshwb")]
public sealed class CssHwb : CssColorValue
{
    private CssNumericValue _h;
    private CssNumericValue _w;
    private CssNumericValue _b;
    private CssNumericValue _alpha;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-csshwb-csshwb</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csshwb-csshwb"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csshwb-csshwb")]
    public CssHwb(CssNumericValue h, CssNumberish w, CssNumberish b, CssNumberish? alpha = null)
    {
        _h = RectifyNumeric(h, CssColorChannelKind.Angle);
        _w = RectifyPercent(w);
        _b = RectifyPercent(b);
        _alpha = RectifyPercent(alpha ?? 1);
    }

    /// <summary>Gets or sets the hue channel of the <c>hwb()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csshwb-h"/>
    [IdlMember("h", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csshwb-h")]
    public CssNumericValue H
    {
        get => _h;
        set => _h = RectifyNumeric(value, CssColorChannelKind.Angle);
    }

    /// <summary>Gets or sets the whiteness channel of the <c>hwb()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csshwb-w"/>
    [IdlMember("w", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csshwb-w")]
    public CssNumberish W
    {
        get => _w;
        set => _w = RectifyPercent(value);
    }

    /// <summary>Gets or sets the blackness channel of the <c>hwb()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csshwb-b"/>
    [IdlMember("b", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csshwb-b")]
    public CssNumberish B
    {
        get => _b;
        set => _b = RectifyPercent(value);
    }

    /// <summary>Gets or sets the alpha (opacity) channel of the <c>hwb()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csshwb-alpha"/>
    [IdlMember("alpha", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csshwb-alpha")]
    public CssNumberish Alpha
    {
        get => _alpha;
        set => _alpha = RectifyPercent(value);
    }

    private static CssNumericValue RectifyNumeric(CssNumericValue value, CssColorChannelKind kind)
    {
        ArgumentNullException.ThrowIfNull(value);
        return (CssNumericValue)Rectify(value, kind);
    }

    private static CssNumericValue RectifyPercent(CssNumberish value) => (CssNumericValue)Rectify(value.Value is double number ? (CssColorChannel)number : value.Rectify(), CssColorChannelKind.Percent);

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => Serialize("hwb", _alpha, _h, _w, _b);
}

/// <summary>lab() (https://drafts.css-houdini.org/css-typed-om-1/#csslab).</summary>
/// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#csslab"/>
[IdlInterface("CSSLab", "https://drafts.css-houdini.org/css-typed-om-1/#csslab")]
public sealed class CssLab : CssColorValue
{
    private CssStyleValue _l;
    private CssStyleValue _a;
    private CssStyleValue _b;
    private CssStyleValue _alpha;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-csslab-csslab</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csslab-csslab"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csslab-csslab")]
    public CssLab(CssColorChannel l, CssColorChannel a, CssColorChannel b, CssColorChannel? alpha = null)
    {
        _l = Rectify(l, CssColorChannelKind.Percent);
        _a = Rectify(a, CssColorChannelKind.Number);
        _b = Rectify(b, CssColorChannelKind.Number);
        _alpha = Rectify(alpha ?? 1, CssColorChannelKind.Percent);
    }

    internal CssLab(CssStyleValue l, CssStyleValue a, CssStyleValue b, CssStyleValue alpha)
        : this(ToChannel(l), ToChannel(a), ToChannel(b), ToChannel(alpha))
    {
    }

    /// <summary>Gets or sets the lightness channel of the <c>lab()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csslab-l"/>
    [IdlMember("l", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csslab-l")]
    public CssColorChannel L
    {
        get => ToChannel(_l);
        set => _l = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <summary>Gets or sets the a axis channel of the <c>lab()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csslab-a"/>
    [IdlMember("a", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csslab-a")]
    public CssColorChannel A
    {
        get => ToChannel(_a);
        set => _a = Rectify(value, CssColorChannelKind.Number);
    }

    /// <summary>Gets or sets the b axis channel of the <c>lab()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csslab-b"/>
    [IdlMember("b", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csslab-b")]
    public CssColorChannel B
    {
        get => ToChannel(_b);
        set => _b = Rectify(value, CssColorChannelKind.Number);
    }

    /// <summary>Gets or sets the alpha (opacity) channel of the <c>lab()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csslab-alpha"/>
    [IdlMember("alpha", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csslab-alpha")]
    public CssColorChannel Alpha
    {
        get => ToChannel(_alpha);
        set => _alpha = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => Serialize("lab", _alpha, _l, _a, _b);
}

/// <summary>lch() (https://drafts.css-houdini.org/css-typed-om-1/#csslch).</summary>
/// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#csslch"/>
[IdlInterface("CSSLCH", "https://drafts.css-houdini.org/css-typed-om-1/#csslch")]
public sealed class CssLch : CssColorValue
{
    private CssStyleValue _l;
    private CssStyleValue _c;
    private CssStyleValue _h;
    private CssStyleValue _alpha;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-csslch-csslch</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csslch-csslch"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csslch-csslch")]
    public CssLch(CssColorChannel l, CssColorChannel c, CssColorChannel h, CssColorChannel? alpha = null)
    {
        _l = Rectify(l, CssColorChannelKind.Percent);
        _c = Rectify(c, CssColorChannelKind.Percent);
        _h = Rectify(h, CssColorChannelKind.Angle);
        _alpha = Rectify(alpha ?? 1, CssColorChannelKind.Percent);
    }

    internal CssLch(CssStyleValue l, CssStyleValue c, CssStyleValue h, CssStyleValue alpha)
        : this(ToChannel(l), ToChannel(c), ToChannel(h), ToChannel(alpha))
    {
    }

    /// <summary>Gets or sets the lightness channel of the <c>lch()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csslch-l"/>
    [IdlMember("l", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csslch-l")]
    public CssColorChannel L
    {
        get => ToChannel(_l);
        set => _l = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <summary>Gets or sets the chroma channel of the <c>lch()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csslch-c"/>
    [IdlMember("c", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csslch-c")]
    public CssColorChannel C
    {
        get => ToChannel(_c);
        set => _c = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <summary>Gets or sets the hue channel of the <c>lch()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csslch-h"/>
    [IdlMember("h", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csslch-h")]
    public CssColorChannel H
    {
        get => ToChannel(_h);
        set => _h = Rectify(value, CssColorChannelKind.Angle);
    }

    /// <summary>Gets or sets the alpha (opacity) channel of the <c>lch()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csslch-alpha"/>
    [IdlMember("alpha", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csslch-alpha")]
    public CssColorChannel Alpha
    {
        get => ToChannel(_alpha);
        set => _alpha = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => Serialize("lch", _alpha, _l, _c, _h);
}

/// <summary>oklab() (https://drafts.css-houdini.org/css-typed-om-1/#cssoklab).</summary>
/// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#cssoklab"/>
[IdlInterface("CSSOKLab", "https://drafts.css-houdini.org/css-typed-om-1/#cssoklab")]
public sealed class CssOkLab : CssColorValue
{
    private CssStyleValue _l;
    private CssStyleValue _a;
    private CssStyleValue _b;
    private CssStyleValue _alpha;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklab-cssoklab</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklab-cssoklab"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklab-cssoklab")]
    public CssOkLab(CssColorChannel l, CssColorChannel a, CssColorChannel b, CssColorChannel? alpha = null)
    {
        _l = Rectify(l, CssColorChannelKind.Percent);
        _a = Rectify(a, CssColorChannelKind.Number);
        _b = Rectify(b, CssColorChannelKind.Number);
        _alpha = Rectify(alpha ?? 1, CssColorChannelKind.Percent);
    }

    internal CssOkLab(CssStyleValue l, CssStyleValue a, CssStyleValue b, CssStyleValue alpha)
        : this(ToChannel(l), ToChannel(a), ToChannel(b), ToChannel(alpha))
    {
    }

    /// <summary>Gets or sets the lightness channel of the <c>oklab()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklab-l"/>
    [IdlMember("l", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklab-l")]
    public CssColorChannel L
    {
        get => ToChannel(_l);
        set => _l = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <summary>Gets or sets the a axis channel of the <c>oklab()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklab-a"/>
    [IdlMember("a", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklab-a")]
    public CssColorChannel A
    {
        get => ToChannel(_a);
        set => _a = Rectify(value, CssColorChannelKind.Number);
    }

    /// <summary>Gets or sets the b axis channel of the <c>oklab()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklab-b"/>
    [IdlMember("b", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklab-b")]
    public CssColorChannel B
    {
        get => ToChannel(_b);
        set => _b = Rectify(value, CssColorChannelKind.Number);
    }

    /// <summary>Gets or sets the alpha (opacity) channel of the <c>oklab()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklab-alpha"/>
    [IdlMember("alpha", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklab-alpha")]
    public CssColorChannel Alpha
    {
        get => ToChannel(_alpha);
        set => _alpha = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => Serialize("oklab", _alpha, _l, _a, _b);
}

/// <summary>oklch() (https://drafts.css-houdini.org/css-typed-om-1/#cssoklch).</summary>
/// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#cssoklch"/>
[IdlInterface("CSSOKLCH", "https://drafts.css-houdini.org/css-typed-om-1/#cssoklch")]
public sealed class CssOkLch : CssColorValue
{
    private CssStyleValue _l;
    private CssStyleValue _c;
    private CssStyleValue _h;
    private CssStyleValue _alpha;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklch-cssoklch</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklch-cssoklch"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklch-cssoklch")]
    public CssOkLch(CssColorChannel l, CssColorChannel c, CssColorChannel h, CssColorChannel? alpha = null)
    {
        _l = Rectify(l, CssColorChannelKind.Percent);
        _c = Rectify(c, CssColorChannelKind.Percent);
        _h = Rectify(h, CssColorChannelKind.Angle);
        _alpha = Rectify(alpha ?? 1, CssColorChannelKind.Percent);
    }

    internal CssOkLch(CssStyleValue l, CssStyleValue c, CssStyleValue h, CssStyleValue alpha)
        : this(ToChannel(l), ToChannel(c), ToChannel(h), ToChannel(alpha))
    {
    }

    /// <summary>Gets or sets the lightness channel of the <c>oklch()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklch-l"/>
    [IdlMember("l", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklch-l")]
    public CssColorChannel L
    {
        get => ToChannel(_l);
        set => _l = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <summary>Gets or sets the chroma channel of the <c>oklch()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklch-c"/>
    [IdlMember("c", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklch-c")]
    public CssColorChannel C
    {
        get => ToChannel(_c);
        set => _c = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <summary>Gets or sets the hue channel of the <c>oklch()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklch-h"/>
    [IdlMember("h", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklch-h")]
    public CssColorChannel H
    {
        get => ToChannel(_h);
        set => _h = Rectify(value, CssColorChannelKind.Angle);
    }

    /// <summary>Gets or sets the alpha (opacity) channel of the <c>oklch()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklch-alpha"/>
    [IdlMember("alpha", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssoklch-alpha")]
    public CssColorChannel Alpha
    {
        get => ToChannel(_alpha);
        set => _alpha = Rectify(value, CssColorChannelKind.Percent);
    }

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => Serialize("oklch", _alpha, _l, _c, _h);
}

/// <summary>color() (https://drafts.css-houdini.org/css-typed-om-1/#csscolor).</summary>
/// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#csscolor"/>
[IdlInterface("CSSColor", "https://drafts.css-houdini.org/css-typed-om-1/#csscolor")]
public sealed class CssColor : CssColorValue
{
    private CssKeywordValue _colorSpace;
    private List<CssStyleValue> _channels;
    private CssNumericValue _alpha;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolor-csscolor</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolor-csscolor"/>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolor-csscolor")]
    public CssColor(CssKeywordish colorSpace, IEnumerable<CssColorChannel> channels, CssNumberish? alpha = null)
    {
        _colorSpace = colorSpace.Rectify();
        _channels = RectifyChannels(channels);
        _alpha = RectifyAlpha(alpha ?? 1);
    }

    /// <summary>
    /// Gets or sets the color space of the <c>color()</c> color (e.g. <c>srgb</c> or <c>display-p3</c>).
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolor-colorspace"/>
    [IdlMember("colorSpace", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolor-colorspace")]
    public CssKeywordish ColorSpace
    {
        get => _colorSpace;
        set => _colorSpace = value.Rectify();
    }

    /// <summary>The channels (an observable array: setting an item rectifies it, https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolor-channels).</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolor-channels"/>
    [IdlMember("channels", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolor-channels")]
    public IReadOnlyList<CssColorChannel> Channels
    {
        get => [.. _channels.Select(ToChannel)];
        set => _channels = RectifyChannels(value);
    }

    /// <summary>Gets or sets the alpha (opacity) channel of the <c>color()</c> color.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolor-alpha"/>
    [IdlMember("alpha", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csscolor-alpha")]
    public CssNumberish Alpha
    {
        get => _alpha;
        set => _alpha = RectifyAlpha(value);
    }

    private static List<CssStyleValue> RectifyChannels(IEnumerable<CssColorChannel> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        return [.. channels.Select(channel => Rectify(channel, CssColorChannelKind.Percent))];
    }

    private static CssNumericValue RectifyAlpha(CssNumberish alpha) => (CssNumericValue)Rectify(alpha.Value is double number ? (CssColorChannel)number : alpha.Rectify(), CssColorChannelKind.Percent);

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => Serialize("color", _alpha, [_colorSpace, .. _channels]);
}
