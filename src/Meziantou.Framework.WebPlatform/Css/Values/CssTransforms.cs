using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The transform functions (https://drafts.csswg.org/css-transforms-2/#transform-functions) as matrices: a &lt;transform-list&gt;
/// is converted to the product of the matrices of its functions (https://drafts.csswg.org/css-transforms-2/#ctm).
/// </summary>
/// <remarks>The matrices are 4x4 column-major arrays: m[0..3] are m11..m14 (the first column), m[12..14] the translation.</remarks>
internal static class CssTransforms
{
    /// <summary>
    /// Converts a &lt;transform-list&gt; or none to a matrix. Returns null when the value is not a transform list, or when a
    /// function cannot be resolved without layout: the lengths must be absolute (relative units and percentages are resolved by
    /// <paramref name="context"/> when it can resolve them). <paramref name="is2D"/> is false when the list contains a 3D
    /// transform function (https://drafts.csswg.org/css-transforms-2/#three-d-transform-functions).
    /// </summary>
    public static double[]? ToMatrix(List<CssComponentValue> values, CssCalcContext context, out bool is2D) => ToMatrix(values, context, referenceBox: null, out is2D);

    /// <summary>
    /// Converts a &lt;transform-list&gt; or none to a matrix (see <see cref="ToMatrix(List{CssComponentValue}, CssCalcContext, out bool)"/>):
    /// the percentages of the translations refer to the size of <paramref name="referenceBox"/>
    /// (https://drafts.csswg.org/css-transforms-1/#transform-box), and the matrix is null when one is not resolved.
    /// </summary>
    public static double[]? ToMatrix(List<CssComponentValue> values, CssCalcContext context, (double Width, double Height)? referenceBox, out bool is2D)
    {
        is2D = true;
        var significant = ValueMatcher.RemoveWhitespace(values);
        double[] result = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } ident] && ident.Token.IsIdent("none"))
            return result;

        if (significant.Count == 0)
            return null;

        var horizontal = context;
        var vertical = context;
        if (referenceBox is { } box)
        {
            horizontal = WithPercentageBasis(context, box.Width);
            vertical = WithPercentageBasis(context, box.Height);
        }

        foreach (var value in significant)
        {
            if (value is not CssFunction function || GetMatrix(function, horizontal, vertical, out var functionIs2D) is not { } matrix)
                return null;

            is2D &= functionIs2D;
            result = Multiply(result, matrix);
        }

        return result;
    }

    /// <summary>
    /// Serializes a matrix to a &lt;matrix()&gt; function when it is a 2D matrix, or to a &lt;matrix3d()&gt; function otherwise
    /// (https://drafts.csswg.org/css-transforms-2/#serialization-of-the-computed-value).
    /// </summary>
    public static string Serialize(ReadOnlySpan<double> matrix)
    {
        // https://drafts.csswg.org/css-transforms-2/#2d-matrix: m13, m14, m23, m24, m31, m32, m34 and m43 are 0, m33 and m44 are 1
        var is2D = matrix[2] == 0 && matrix[3] == 0 && matrix[6] == 0 && matrix[7] == 0 && matrix[8] == 0 && matrix[9] == 0 && matrix[10] == 1 && matrix[11] == 0 && matrix[14] == 0 && matrix[15] == 1;
        var builder = new StringBuilder(is2D ? "matrix(" : "matrix3d(");
        ReadOnlySpan<int> indices = is2D ? [0, 1, 4, 5, 12, 13] : [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
        for (var i = 0; i < indices.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(CssSerializer.SerializeNumber(matrix[indices[i]]));
        }

        return builder.Append(')').ToString();
    }

    /// <summary>Returns the product a × b of two matrices.</summary>
    public static double[] Multiply(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        var result = new double[16];
        for (var col = 0; col < 4; col++)
        {
            for (var row = 0; row < 4; row++)
            {
                double sum = 0;
                for (var k = 0; k < 4; k++)
                {
                    sum += a[(k * 4) + row] * b[(col * 4) + k];
                }

                result[(col * 4) + row] = sum;
            }
        }

        return result;
    }

    /// <summary>The matrix of a rotation of <paramref name="angle"/> degrees around the vector (x, y, z) (https://drafts.csswg.org/css-transforms-2/#Rotate3dDefined).</summary>
    public static double[] Rotation(double x, double y, double z, double angle)
    {
        var length = Math.Sqrt((x * x) + (y * y) + (z * z));
        if (length == 0)
            return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

        x /= length;
        y /= length;
        z /= length;
        // Like in the browsers, the angle is reduced modulo 360 first (the remainder is exact): the conversion of a large angle
        // to radians is not precise, e.g. an infinite angle is clamped to the largest float, a multiple of 360 (no rotation)
        var alpha = (angle % 360) * Math.PI / 180;
        var sc = Math.Sin(alpha / 2) * Math.Cos(alpha / 2);
        var sq = Math.Sin(alpha / 2) * Math.Sin(alpha / 2);
        return
        [
            1 - (2 * ((y * y) + (z * z)) * sq), 2 * ((x * y * sq) + (z * sc)), 2 * ((x * z * sq) - (y * sc)), 0,
            2 * ((x * y * sq) - (z * sc)), 1 - (2 * ((x * x) + (z * z)) * sq), 2 * ((y * z * sq) + (x * sc)), 0,
            2 * ((x * z * sq) + (y * sc)), 2 * ((y * z * sq) - (x * sc)), 1 - (2 * ((x * x) + (y * y)) * sq), 0,
            0, 0, 0, 1,
        ];
    }

    private static CssCalcContext WithPercentageBasis(CssCalcContext context, double size) => new()
    {
        ResolveDimension = context.ResolveDimension,
        ResolveTreeCountingFunction = context.ResolveTreeCountingFunction,
        ResolveRandomBaseValue = context.ResolveRandomBaseValue,
        PercentageBasis = (size, "px"),
    };

    /// <summary>
    /// Returns the matrix of a transform function: the percentages of the translations along the x axis are resolved by
    /// <paramref name="context"/>, the ones along the y axis by <paramref name="vertical"/>.
    /// </summary>
    private static double[]? GetMatrix(CssFunction function, CssCalcContext context, CssCalcContext vertical, out bool is2D)
    {
        is2D = true;
        var arguments = CommaSeparated(function.Value);
        var name = InfraAscii.ToAsciiLowercase(function.Name);
        switch (name)
        {
            case "matrix" when Numbers(arguments, context, 6) is { } m:
                return [m[0], m[1], 0, 0, m[2], m[3], 0, 0, 0, 0, 1, 0, m[4], m[5], 0, 1];

            case "translate" when arguments.Count is 1 or 2 && Length(arguments[0], context) is { } tx:
            {
                var ty = arguments.Count == 2 ? Length(arguments[1], vertical) : 0;
                return ty is null ? null : Translation(tx, ty.Value, 0);
            }

            case "translatex" when arguments is [var argument] && Length(argument, context) is { } tx:
                return Translation(tx, 0, 0);

            case "translatey" when arguments is [var argument] && Length(argument, vertical) is { } ty:
                return Translation(0, ty, 0);

            case "scale" when arguments.Count is 1 or 2 && NumberOrPercentage(arguments[0], context) is { } sx:
            {
                var sy = arguments.Count == 2 ? NumberOrPercentage(arguments[1], context) : sx;
                return sy is null ? null : Scale(sx, sy.Value, 1);
            }

            case "scalex" when arguments is [var argument] && NumberOrPercentage(argument, context) is { } sx:
                return Scale(sx, 1, 1);

            case "scaley" when arguments is [var argument] && NumberOrPercentage(argument, context) is { } sy:
                return Scale(1, sy, 1);

            case "rotate" when arguments is [var argument] && Angle(argument, context) is { } angle:
                return Rotation(0, 0, 1, angle);

            case "skew" when arguments.Count is 1 or 2 && Angle(arguments[0], context) is { } ax:
            {
                var ay = arguments.Count == 2 ? Angle(arguments[1], context) : 0;
                return ay is null ? null : Skew(ax, ay.Value);
            }

            case "skewx" when arguments is [var argument] && Angle(argument, context) is { } ax:
                return Skew(ax, 0);

            case "skewy" when arguments is [var argument] && Angle(argument, context) is { } ay:
                return Skew(0, ay);
        }

        is2D = false;
        switch (name)
        {
            case "matrix3d" when Numbers(arguments, context, 16) is { } m:
                return m;

            case "translate3d" when arguments.Count == 3 && Length(arguments[0], context) is { } tx && Length(arguments[1], vertical) is { } ty && Length(arguments[2], context, allowPercentage: false) is { } tz:
                return Translation(tx, ty, tz);

            case "translatez" when arguments is [var argument] && Length(argument, context, allowPercentage: false) is { } tz:
                return Translation(0, 0, tz);

            case "scale3d" when arguments.Count == 3 && NumberOrPercentage(arguments[0], context) is { } sx && NumberOrPercentage(arguments[1], context) is { } sy && NumberOrPercentage(arguments[2], context) is { } sz:
                return Scale(sx, sy, sz);

            case "scalez" when arguments is [var argument] && NumberOrPercentage(argument, context) is { } sz:
                return Scale(1, 1, sz);

            case "rotate3d" when arguments.Count == 4 && Number(arguments[0], context) is { } x && Number(arguments[1], context) is { } y && Number(arguments[2], context) is { } z && Angle(arguments[3], context) is { } angle:
                return Rotation(x, y, z, angle);

            case "rotatex" when arguments is [var argument] && Angle(argument, context) is { } angle:
                return Rotation(1, 0, 0, angle);

            case "rotatey" when arguments is [var argument] && Angle(argument, context) is { } angle:
                return Rotation(0, 1, 0, angle);

            case "rotatez" when arguments is [var argument] && Angle(argument, context) is { } angle:
                return Rotation(0, 0, 1, angle);

            case "perspective" when arguments is [var argument]:
            {
                if (argument is [CssPreservedToken { Type: CssTokenType.Ident } none] && none.Token.IsIdent("none"))
                    return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

                // The lengths less than 1px are treated as 1px (https://drafts.csswg.org/css-transforms-2/#funcdef-perspective)
                if (Length(argument, context, allowPercentage: false) is not { } distance || distance < 0)
                    return null;

                return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -1 / Math.Max(distance, 1), 0, 0, 0, 1];
            }
        }

        return null;
    }

    private static double[] Translation(double x, double y, double z) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];

    private static double[] Scale(double x, double y, double z) => [x, 0, 0, 0, 0, y, 0, 0, 0, 0, z, 0, 0, 0, 0, 1];

    private static double[] Skew(double ax, double ay) => [1, Math.Tan(ay * Math.PI / 180), 0, 0, Math.Tan(ax * Math.PI / 180), 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    /// <summary>Splits the arguments of a function on the commas (the whitespace is removed); the 2D functions of SVG allow whitespace separators, CSS does not.</summary>
    private static List<List<CssComponentValue>> CommaSeparated(List<CssComponentValue> values)
    {
        var result = new List<List<CssComponentValue>> { new() };
        foreach (var value in values)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                result.Add([]);
            }
            else if (value is not CssPreservedToken { Type: CssTokenType.Whitespace })
            {
                result[^1].Add(value);
            }
        }

        return result is [[]] ? [] : result;
    }

    private static double[]? Numbers(List<List<CssComponentValue>> arguments, CssCalcContext context, int count)
    {
        if (arguments.Count != count)
            return null;

        var result = new double[count];
        for (var i = 0; i < count; i++)
        {
            if (Number(arguments[i], context) is not { } number)
                return null;

            result[i] = number;
        }

        return result;
    }

    private static double? Number(List<CssComponentValue> argument, CssCalcContext context) => Resolve(argument, context, percentageUnit: null) is ({ } value, "") ? value : null;

    private static double? NumberOrPercentage(List<CssComponentValue> argument, CssCalcContext context) => Resolve(argument, context, percentageUnit: "") switch
    {
        ({ } value, "") => value,
        ({ } value, "%") => value / 100,
        _ => null,
    };

    private static double? Angle(List<CssComponentValue> argument, CssCalcContext context) => Resolve(argument, context, percentageUnit: null) switch
    {
        ({ } value, "deg") => value,

        // The unitless zero (https://drafts.csswg.org/css-transforms-1/#transform-functions)
        (0, "") when argument is [CssPreservedToken { Type: CssTokenType.Number }] => 0,
        _ => null,
    };

    private static double? Length(List<CssComponentValue> argument, CssCalcContext context, bool allowPercentage = true) => Resolve(argument, context, allowPercentage ? "px" : null) switch
    {
        ({ } value, "px") => value,
        (0, "") when argument is [CssPreservedToken { Type: CssTokenType.Number }] => 0,
        _ => null,
    };

    /// <summary>Resolves a single numeric value: its canonical value and unit ("" for a number, "%" for a percentage that is not resolved).</summary>
    private static (double? Value, string? Unit) Resolve(List<CssComponentValue> argument, CssCalcContext context, string? percentageUnit)
    {
        if (argument is not [var value])
            return (null, null);

        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Number } number:
                return (number.Token.NumericValue, "");

            case CssPreservedToken { Type: CssTokenType.Percentage } percentage:
                if (context.PercentageBasis is { } basis && percentageUnit is not null && basis.Unit == percentageUnit)
                    return (percentage.Token.NumericValue / 100 * basis.Value, basis.Unit);

                return (percentage.Token.NumericValue, "%");

            case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
            {
                var unit = InfraAscii.ToAsciiLowercase(dimension.Token.Value);
                if (CssCalc.TryConvertToCanonicalUnit(dimension.Token.NumericValue, unit, out var canonical, out var canonicalUnit))
                    return (canonical, canonicalUnit);

                return context.ResolveDimension?.Invoke(dimension.Token.NumericValue, unit) is { } resolved ? (resolved.Value, resolved.Unit) : (null, null);
            }

            case CssFunction function when CssCalc.IsMathFunction(function.Name):
            {
                var calcContext = new CssCalcContext
                {
                    ResolveDimension = context.ResolveDimension,
                    ResolveTreeCountingFunction = context.ResolveTreeCountingFunction,
                    PercentageBasis = context.PercentageBasis is { } percentageBasis && percentageBasis.Unit == percentageUnit ? percentageBasis : null,
                    PercentageUnit = percentageUnit,
                };

                return CssCalc.Evaluate(function, calcContext) is { IsResolved: true } result ? (result.Value, result.Unit) : (null, null);
            }

            default:
                return (null, null);
        }
    }
}
