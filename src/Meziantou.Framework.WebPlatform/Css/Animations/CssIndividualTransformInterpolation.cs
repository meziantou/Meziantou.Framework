using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The interpolation of the individual transform properties (https://drafts.csswg.org/css-transforms-2/#individual-transform-interpolation):
/// translate and scale component by component (none is the identity when the other value is not none), rotate by its angle
/// when the axes are the same, or by the spherical linear interpolation of the rotations otherwise.
/// </summary>
internal static class CssIndividualTransformInterpolation
{
    public static Func<double, string?>? Prepare(CssInterpolationKind kind, string from, string to)
    {
        if (from == "none" && to == "none")
            return _ => "none";

        if (Combine(kind, from, to, CssCombineOperation.Interpolate, 0.5) is null)
            return null;

        return progress => Combine(kind, from, to, CssCombineOperation.Interpolate, progress);
    }

    public static string? Combine(CssInterpolationKind kind, string a, string b, CssCombineOperation operation, double progress)
    {
        if (a == "none" && b == "none")
            return "none";

        return kind switch
        {
            CssInterpolationKind.Translate => CombineTranslate(a, b, operation, progress),
            CssInterpolationKind.Scale => CombineScale(a, b, operation, progress),
            _ => CombineRotate(a, b, operation, progress),
        };
    }

    private static string? CombineTranslate(string a, string b, CssCombineOperation operation, double progress)
    {
        if (ParseTranslate(a) is not { } componentsA || ParseTranslate(b) is not { } componentsB)
            return null;

        var result = new string[3];
        for (var i = 0; i < 3; i++)
        {
            if (CssGenericInterpolation.CombineNumeric(componentsA[i], componentsB[i], CssNumericTraits.LengthPercentage, operation == CssCombineOperation.Interpolate ? operation : CssCombineOperation.Add, progress, topLevel: false) is not { } combined)
                return null;

            result[i] = combined;
        }

        return string.Join(' ', result);
    }

    /// <summary>Returns the three components of a translation (none is 0px 0px 0px).</summary>
    private static List<CssComponentValue>? ParseTranslate(string value)
    {
        var zero = CssGenericInterpolation.Parse("0px")[0];
        if (value == "none")
            return [zero, zero, zero];

        var components = CssGenericInterpolation.Parse(value);
        if (components.Count is < 1 or > 3 || !components.TrueForAll(CssGenericInterpolation.IsNumeric))
            return null;

        while (components.Count < 3)
        {
            components.Add(zero);
        }

        return components;
    }

    private static string? CombineScale(string a, string b, CssCombineOperation operation, double progress)
    {
        if (ParseScale(a) is not { } componentsA || ParseScale(b) is not { } componentsB)
            return null;

        var result = new string[3];
        for (var i = 0; i < 3; i++)
        {
            var value = operation switch
            {
                CssCombineOperation.Interpolate => CssGenericInterpolation.Lerp(componentsA[i], componentsB[i], progress),

                // https://drafts.csswg.org/css-transforms-2/#combining-transform-lists: the scales are multiplied, and accumulated as a + b - 1
                CssCombineOperation.Add => componentsA[i] * componentsB[i],
                _ => componentsA[i] + componentsB[i] - 1,
            };

            result[i] = CssGenericInterpolation.FormatNumber(value);
        }

        return string.Join(' ', result);
    }

    /// <summary>Returns the three factors of a scale (a single value scales x and y, none is 1 1 1).</summary>
    private static double[]? ParseScale(string value)
    {
        if (value == "none")
            return [1, 1, 1];

        var components = CssGenericInterpolation.Parse(value);
        var result = new double[] { 1, 1, 1 };
        for (var i = 0; i < components.Count; i++)
        {
            if (i > 2 || !CssGenericInterpolation.TryGetNumber(components[i], out var number, out var unit))
                return null;

            result[i] = unit == "%" ? number / 100 : number;
        }

        if (components.Count == 1)
        {
            result[1] = result[0];
        }

        return result;
    }

    private static string? CombineRotate(string a, string b, CssCombineOperation operation, double progress)
    {
        if (ParseRotate(a) is not { } rotationA || ParseRotate(b) is not { } rotationB)
            return null;

        var (axisA, angleA) = rotationA;
        var (axisB, angleB) = rotationB;
        var sameAxis = Math.Abs(axisA[0] - axisB[0]) < 1e-9 && Math.Abs(axisA[1] - axisB[1]) < 1e-9 && Math.Abs(axisA[2] - axisB[2]) < 1e-9;
        if (sameAxis || angleA == 0 || angleB == 0)
        {
            var axis = angleA == 0 && !sameAxis ? axisB : axisA;
            var angle = operation == CssCombineOperation.Interpolate ? CssGenericInterpolation.Lerp(angleA, angleB, progress) : angleA + angleB;
            return Serialize(axis, angle);
        }

        var quaternionA = ToQuaternion(axisA, angleA);
        var quaternionB = ToQuaternion(axisB, angleB);
        var quaternion = operation == CssCombineOperation.Interpolate ? CssMatrixDecomposition.Slerp(quaternionA, quaternionB, progress) : CssMatrixDecomposition.MultiplyQuaternions(quaternionB, quaternionA);
        return FromQuaternion(quaternion);
    }

    /// <summary>Returns the normalized axis and the angle (in degrees) of a rotation (an angle alone rotates around the z axis, none is 0deg).</summary>
    private static (double[] Axis, double Angle)? ParseRotate(string value)
    {
        if (value == "none")
            return ([0, 0, 1], 0);

        var components = CssGenericInterpolation.Parse(value);
        if (components.Count == 0 || ToDegrees(components[^1]) is not { } angle)
            return null;

        double[] axis;
        switch (components.Count)
        {
            case 1:
                axis = [0, 0, 1];
                break;
            case 2 when components[0] is CssPreservedToken { Type: CssTokenType.Ident } ident:
                axis = InfraAscii.ToAsciiLowercase(ident.Token.Value) switch
                {
                    "x" => [1, 0, 0],
                    "y" => [0, 1, 0],
                    "z" => [0, 0, 1],
                    _ => [],
                };

                if (axis.Length == 0)
                    return null;

                break;
            case 4:
                axis = new double[3];
                for (var i = 0; i < 3; i++)
                {
                    if (!CssGenericInterpolation.TryGetNumber(components[i], out axis[i], out var unit) || unit.Length != 0)
                        return null;
                }

                var length = Math.Sqrt((axis[0] * axis[0]) + (axis[1] * axis[1]) + (axis[2] * axis[2]));
                if (length == 0)
                {
                    axis = [0, 0, 1];
                }
                else
                {
                    axis[0] /= length;
                    axis[1] /= length;
                    axis[2] /= length;
                }

                break;
            default:
                return null;
        }

        return (axis, angle);
    }

    private static double? ToDegrees(CssComponentValue component)
    {
        if (!CssGenericInterpolation.TryGetNumber(component, out var value, out var unit))
            return null;

        return unit switch
        {
            "deg" => value,
            "rad" => value * 180 / Math.PI,
            "grad" => value * 0.9,
            "turn" => value * 360,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the quaternion of a rotation, as the decomposition of its matrix gives it
    /// (https://drafts.csswg.org/css-transforms-2/#decomposing-a-3d-matrix): its scalar part is not negative, so that a
    /// rotation of more than half a turn is the equivalent rotation in the other direction (360deg is the identity).
    /// </summary>
    private static double[] ToQuaternion(double[] axis, double angle)
    {
        var half = angle * Math.PI / 360;
        var sin = Math.Sin(half);
        var cos = Math.Cos(half);
        if (cos < 0)
        {
            sin = -sin;
            cos = -cos;
        }

        return [axis[0] * sin, axis[1] * sin, axis[2] * sin, cos];
    }

    private static string FromQuaternion(double[] quaternion)
    {
        var w = Math.Clamp(quaternion[3], -1, 1);
        var angle = 2 * Math.Acos(w) * 180 / Math.PI;
        var sin = Math.Sqrt(1 - (w * w));
        if (sin < 1e-9)
            return "0deg";

        return Serialize([quaternion[0] / sin, quaternion[1] / sin, quaternion[2] / sin], angle);
    }

    private static string Serialize(double[] axis, double angle) =>
        CssGenericInterpolation.FormatNumber(axis[0]) + " " + CssGenericInterpolation.FormatNumber(axis[1]) + " " + CssGenericInterpolation.FormatNumber(axis[2]) + " " + CssGenericInterpolation.FormatNumber(angle) + "deg";
}
