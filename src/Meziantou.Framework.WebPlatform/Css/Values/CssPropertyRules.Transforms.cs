using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// The individual transform properties and transform-origin (https://drafts.csswg.org/css-transforms-2/#individual-transforms)
internal static partial class CssPropertyRules
{
    private static bool IsPlainNumber(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Number };

    /// <summary>
    /// https://drafts.csswg.org/css-transforms-2/#propdef-scale: the percentages are numbers (<c>100%</c> is <c>1</c>), and the
    /// value is serialized in its shortest form: a z scale of 1 is omitted, then a y scale equal to the x scale
    /// (<c>100 100 1</c> is <c>100</c>). The scales that are math functions are kept.
    /// </summary>
    private static string? CanonicalizeScale(List<CssComponentValue> significant)
    {
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident }])
            return null;

        var scales = new List<CssComponentValue>(significant.Count);
        foreach (var value in significant)
        {
            scales.Add(value is CssPreservedToken { Token: { Type: CssTokenType.Percentage } percentage }
                ? new CssPreservedToken(new CssToken { Type = CssTokenType.Number, Value = "", NumericValue = percentage.NumericValue / 100 })
                : value);
        }

        if (scales.TrueForAll(IsPlainNumber))
        {
            if (scales is [_, _, CssPreservedToken { Token.NumericValue: 1 }])
            {
                scales.RemoveAt(2);
            }

            if (scales is [CssPreservedToken x, CssPreservedToken y] && x.Token.NumericValue == y.Token.NumericValue)
            {
                scales.RemoveAt(1);
            }
        }

        return Serialize(scales, separator: " ");
    }

    /// <summary>
    /// https://drafts.csswg.org/css-transforms-2/#propdef-translate: the shortest form, without a zero z translation and then
    /// without a zero y translation (a zero length in px, not a percentage: <c>100px 0px 0px</c> is <c>100px</c>).
    /// </summary>
    private static string? CanonicalizeTranslate(List<CssComponentValue> significant)
    {
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident }])
            return null;

        static bool IsZeroLength(CssComponentValue value) => value is CssPreservedToken { Token: { NumericValue: 0 } token }
            && (token.Type == CssTokenType.Number || (token.Type == CssTokenType.Dimension && InfraAscii.EqualsIgnoreAsciiCase(token.Value, "px")));

        var count = significant.Count;
        if (count == 3 && IsZeroLength(significant[2]))
        {
            count = 2;
        }

        if (count == 2 && IsZeroLength(significant[1]))
        {
            count = 1;
        }

        return string.Join(' ', significant.Take(count).Select(value => CssSerializer.SerializeValue([value], CssPropertyRegistry.Get("translate"))));
    }

    /// <summary>
    /// https://drafts.csswg.org/css-transforms-2/#propdef-rotate: the axis precedes the angle; an axis parallel to the x or
    /// y axis is serialized as the keyword (the sign of the vector is applied to the angle), and the z axis is omitted
    /// (<c>-1 0 0 400grad</c> is <c>x -400grad</c>, <c>400grad z</c> is <c>400grad</c>). The null vector is kept.
    /// </summary>
    private static string? CanonicalizeRotate(List<CssComponentValue> significant)
    {
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident }])
            return null;

        // The angle is the only dimension or math function; the axis is a keyword or three numbers
        var angleIndex = significant.FindIndex(value => value is CssPreservedToken { Type: CssTokenType.Dimension } or CssFunction);
        if (angleIndex < 0 && significant.Count is 1 or 2)
        {
            // A unitless zero angle
            angleIndex = significant.FindIndex(IsPlainNumber);
        }

        if (angleIndex < 0)
            return null;

        var angle = significant[angleIndex];
        var axis = significant.Where((_, index) => index != angleIndex).ToList();
        var sign = 1;
        string? keyword;
        switch (axis)
        {
            case []:
                keyword = "z";
                break;

            case [CssPreservedToken { Type: CssTokenType.Ident } ident]:
                keyword = InfraAscii.ToAsciiLowercase(ident.Token.Value);
                break;

            case [CssPreservedToken { Type: CssTokenType.Number } x, CssPreservedToken { Type: CssTokenType.Number } y, CssPreservedToken { Type: CssTokenType.Number } z]:
            {
                var (vx, vy, vz) = (x.Token.NumericValue, y.Token.NumericValue, z.Token.NumericValue);
                keyword = (vx != 0, vy != 0, vz != 0) switch
                {
                    (true, false, false) => "x",
                    (false, true, false) => "y",
                    (false, false, true) => "z",
                    _ => null,
                };

                sign = Math.Sign(vx + vy + vz);
                break;
            }

            default:
                return null;
        }

        var property = CssPropertyRegistry.Get("rotate");
        string angleText;
        if (keyword is null || sign >= 0)
        {
            angleText = CssSerializer.SerializeValue([angle], property);
        }
        else if (angle is CssPreservedToken { Token: { Type: CssTokenType.Dimension or CssTokenType.Number } token })
        {
            // The opposite axis is the rotation by the opposite angle
            angleText = CssSerializer.SerializeValue([new CssPreservedToken(token with { NumericValue = -token.NumericValue })], property);
        }
        else
        {
            // The angle is a math function: the vector is kept
            keyword = null;
            angleText = CssSerializer.SerializeValue([angle], property);
        }

        if (angle is CssPreservedToken { Token: { Type: CssTokenType.Number } })
        {
            angleText += "deg";
        }

        return keyword switch
        {
            "z" => angleText,
            null => Serialize(axis, separator: " ") + " " + angleText,
            _ => keyword + " " + angleText,
        };
    }

    /// <summary>
    /// https://drafts.csswg.org/css-transforms-1/#transform-origin-property: a single value is the offset of an axis, the
    /// other one is center (<c>left</c> is <c>left center</c>, <c>top</c> is <c>center top</c>); the values with two or three
    /// components are serialized in the order of the grammar.
    /// </summary>
    private static string? CanonicalizeTransformOrigin(List<CssComponentValue> significant)
    {
        if (significant is not [var single])
            return null;

        var text = CssSerializer.SerializeValue([single], CssPropertyRegistry.Get("transform-origin"));
        return text is "top" or "bottom" ? "center " + text : text + " center";
    }
}
