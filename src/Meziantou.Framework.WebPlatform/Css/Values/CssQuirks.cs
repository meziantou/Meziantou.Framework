using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The values that are only valid when CSS is parsed in quirks mode: the &lt;quirky-length&gt;
/// (https://drafts.csswg.org/css-values-4/#deprecated-quirky-length) and the &lt;quirky-color&gt;
/// (https://drafts.csswg.org/css-color-4/#quirky-color).
/// </summary>
internal static class CssQuirks
{
    /// <summary>The properties that accept a &lt;quirky-length&gt;.</summary>
    private static readonly FrozenSet<string> QuirkyLengthProperties = FrozenSet.Create(StringComparer.Ordinal,
        "background-position", "border-spacing", "border-top-width", "border-right-width", "border-bottom-width", "border-left-width", "border-width",
        "bottom", "clip", "font-size", "height", "left", "letter-spacing", "margin-right", "margin-left", "margin-top", "margin-bottom", "margin",
        "max-height", "max-width", "min-height", "min-width", "padding-top", "padding-right", "padding-bottom", "padding-left", "padding",
        "right", "text-indent", "top", "vertical-align", "width", "word-spacing");

    /// <summary>The properties that accept a &lt;quirky-color&gt;.</summary>
    private static readonly FrozenSet<string> QuirkyColorProperties = FrozenSet.Create(StringComparer.Ordinal,
        "background-color", "border-color", "border-top-color", "border-right-color", "border-bottom-color", "border-left-color", "color");

    /// <summary>
    /// Returns the value of a property with its quirky lengths written as px lengths and its quirky colors written as
    /// &lt;hex-color&gt;s, or null when the property accepts none of them. They are only valid at the top level of the value
    /// (and in the rect() function of clip), not in the other functions.
    /// </summary>
    public static List<CssComponentValue>? RewriteQuirkyValues(string property, List<CssComponentValue> values)
    {
        var lengths = QuirkyLengthProperties.Contains(property);
        var colors = QuirkyColorProperties.Contains(property);
        if (!lengths && !colors)
            return null;

        var builder = new StringBuilder();
        Append(builder, values, lengths, colors, property == "clip");
        return CssParser.ParseListOfComponentValues(builder.ToString());
    }

    private static void Append(StringBuilder builder, List<CssComponentValue> values, bool lengths, bool colors, bool clip)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Number } number when lengths:
                    builder.Append(CssSerializer.SerializeNumber(number.Token.NumericValue)).Append("px ");
                    break;

                case CssPreservedToken { Type: CssTokenType.Number } number when colors && GetQuirkyColorDigits(number.Token, unit: "") is { } digits:
                    builder.Append('#').Append(digits).Append(' ');
                    break;

                case CssPreservedToken { Type: CssTokenType.Dimension } dimension when colors && GetQuirkyColorDigits(dimension.Token, dimension.Token.Value) is { } digits:
                    builder.Append('#').Append(digits).Append(' ');
                    break;

                case CssPreservedToken { Type: CssTokenType.Ident } ident when colors && ident.Token.Value.Length is 3 or 6 && ident.Token.Value.All(char.IsAsciiHexDigit):
                    builder.Append('#').Append(ident.Token.Value).Append(' ');
                    break;

                case CssFunction function when clip && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "rect"):
                    builder.Append("rect(");
                    Append(builder, function.Value, lengths, colors: false, clip: false);
                    builder.Append(") ");
                    break;

                default:
                    builder.Append(CssSerializer.SerializeValue([value], property: null)).Append(' ');
                    break;
            }
        }
    }

    /// <summary>
    /// Returns the digits of the &lt;hex-color&gt; that an integer &lt;number-token&gt; or &lt;dimension-token&gt; stands for: the
    /// serialization of its value followed by its unit, padded with zeros to 6 digits. Like the browsers, the result must be
    /// 6 hexadecimal digits (e.g. 1234567 and -1 are invalid). Returns null when the token is not a quirky color.
    /// </summary>
    private static string? GetQuirkyColorDigits(CssToken token, string unit)
    {
        if (!token.IsInteger || token.NumericValue is < 0 or > 999_999)
            return null;

        var digits = ((int)token.NumericValue).ToString(CultureInfo.InvariantCulture) + unit;
        if (digits.Length > 6 || !digits.All(char.IsAsciiHexDigit))
            return null;

        return digits.PadLeft(6, '0');
    }
}
