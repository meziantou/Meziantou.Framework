using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The interpolation, addition and accumulation of the transform lists (https://drafts.csswg.org/css-transforms-2/#interpolation-of-transforms):
/// the functions are paired (the shorter list is padded with identity functions); the pairs with a common primitive are
/// interpolated argument by argument (a pair of matrices as matrices), and the lists are interpolated as matrices from the
/// first pair without common primitive.
/// </summary>
internal static class CssTransformInterpolation
{
    private sealed record TransformFunction(string Name, List<CssComponentValue> Arguments);

    private enum Family
    {
        Translate,
        Scale,
        Rotate,
        Skew,
        Perspective,
        Matrix,
    }

    public static Func<double, string?>? Prepare(CssInterpolationContext context, string from, string to)
    {
        if (Parse(from) is not { } a || Parse(to) is not { } b)
            return null;

        if (a.Count == 0 && b.Count == 0)
            return _ => "none";

        if (Combine(context, a, b, CssCombineOperation.Interpolate, 0.5) is null)
            return null;

        return progress => Combine(context, a, b, CssCombineOperation.Interpolate, progress);
    }

    /// <summary>https://drafts.csswg.org/css-transforms-2/#combining-transform-lists: the addition appends the lists.</summary>
    public static string? Add(string underlying, string value)
    {
        if (Parse(underlying) is not { } a || Parse(value) is not { } b)
            return null;

        if (a.Count == 0 && b.Count == 0)
            return "none";

        var builder = new StringBuilder();
        foreach (var function in a.Concat(b))
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(Serialize(function));
        }

        return builder.ToString();
    }

    /// <summary>https://drafts.csswg.org/css-transforms-2/#combining-transform-lists: the accumulation combines the paired functions.</summary>
    public static string? Accumulate(CssInterpolationContext context, string underlying, string value)
    {
        if (Parse(underlying) is not { } a || Parse(value) is not { } b)
            return null;

        if (a.Count == 0 && b.Count == 0)
            return "none";

        return Combine(context, a, b, CssCombineOperation.Accumulate, 1);
    }

    private static string? Combine(CssInterpolationContext context, List<TransformFunction> a, List<TransformFunction> b, CssCombineOperation operation, double progress)
    {
        // The shorter list is padded with the identity functions of the other list (none is an empty list)
        var count = Math.Max(a.Count, b.Count);
        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var functionA = i < a.Count ? a[i] : GetIdentity(b[i]);
            var functionB = i < b.Count ? b[i] : GetIdentity(a[i]);
            var combined = CombinePair(context, functionA, functionB, operation, progress);
            if (combined is null)
            {
                // From the first pair without common primitive, the lists are combined as matrices
                var rest = CombineMatrices(context, PadList(a, b, i), PadList(b, a, i), operation, progress);
                if (rest is null)
                    return null;

                Append(builder, rest);
                break;
            }

            Append(builder, combined);
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string value)
    {
        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(value);
    }

    /// <summary>Returns the functions of a list from an index, padded with the identity functions of the other list.</summary>
    private static List<TransformFunction> PadList(List<TransformFunction> list, List<TransformFunction> other, int start)
    {
        var result = new List<TransformFunction>();
        for (var i = start; i < Math.Max(list.Count, other.Count); i++)
        {
            result.Add(i < list.Count ? list[i] : GetIdentity(other[i]));
        }

        return result;
    }

    /// <summary>Combines two functions with a common primitive; returns null when they have none.</summary>
    private static string? CombinePair(CssInterpolationContext context, TransformFunction a, TransformFunction b, CssCombineOperation operation, double progress)
    {
        if (GetFamily(a.Name) is not { } familyA || GetFamily(b.Name) is not { } familyB || familyA != familyB)
            return null;

        // https://drafts.csswg.org/css-transforms-2/#interpolation-of-transform-functions: "the values of the source transform
        // functions get converted to 4x4 matrices first and interpolated as defined in section Interpolation of Matrices": a pair
        // of matrices is combined as matrices, and the functions that follow are still combined pair by pair
        if (familyA == Family.Matrix)
            return CombineMatrices(context, [a], [b], operation, progress);

        // The accumulation only combines the functions of the same type (e.g. skewX and skewY are accumulated as matrices)
        if (operation == CssCombineOperation.Accumulate && !string.Equals(a.Name, b.Name, StringComparison.Ordinal) && !IsIdentity(a) && !IsIdentity(b))
            return null;

        // https://drafts.csswg.org/css-transforms-1/#transform-primitives: the skew functions have no primitive, a skew is only
        // interpolated with the same skew function (the identity function of the padding is the same function)
        if (familyA == Family.Skew && !string.Equals(a.Name, b.Name, StringComparison.Ordinal))
            return null;

        var threeD = Is3D(a.Name) || Is3D(b.Name);
        if (ToPrimitive(a, threeD) is not { } argumentsA || ToPrimitive(b, threeD) is not { } argumentsB || argumentsA.Count != argumentsB.Count)
            return null;

        // The functions with the same name keep their name (e.g. translateX), the others use their primitive
        var sameFunction = string.Equals(a.Name, b.Name, StringComparison.Ordinal) && a.Arguments.Count == b.Arguments.Count;
        var name = sameFunction ? a.Name : GetPrimitiveName(familyA, threeD);
        var arguments = sameFunction ? (a.Arguments, b.Arguments) : (argumentsA, argumentsB);
        switch (familyA)
        {
            case Family.Perspective:
                return CombinePerspective(a, b, operation, progress);

            case Family.Rotate when threeD:
            {
                // The rotations around different axes are interpolated as matrices (quaternions)
                if (!TryGetRotation(argumentsA, out var axisA, out var angleA) || !TryGetRotation(argumentsB, out var axisB, out var angleB))
                    return null;

                if (!SameAxis(axisA, axisB) && angleA != 0 && angleB != 0)
                    return null;

                var axis = angleA != 0 ? axisA : axisB;
                var angle = operation == CssCombineOperation.Interpolate ? CssGenericInterpolation.Lerp(angleA, angleB, progress) : angleA + angleB;
                return name is "rotateX" or "rotateY" or "rotateZ" ? name + "(" + CssGenericInterpolation.FormatNumber(angle) + "deg)"
                    : "rotate3d(" + CssGenericInterpolation.FormatNumber(axis[0]) + ", " + CssGenericInterpolation.FormatNumber(axis[1]) + ", " + CssGenericInterpolation.FormatNumber(axis[2]) + ", " + CssGenericInterpolation.FormatNumber(angle) + "deg)";
            }
        }

        var builder = new StringBuilder(name).Append('(');
        for (var i = 0; i < arguments.Item1.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            string? combined;
            if (familyA == Family.Scale && operation == CssCombineOperation.Accumulate
                && CssGenericInterpolation.TryGetNumber(arguments.Item1[i], out var scaleA, out _) && CssGenericInterpolation.TryGetNumber(arguments.Item2[i], out var scaleB, out _))
            {
                // https://drafts.csswg.org/css-transforms-2/#combining-transform-lists: the scales accumulate as a + b - 1
                combined = CssGenericInterpolation.FormatNumber(scaleA + scaleB - 1);
            }
            else if (familyA == Family.Translate && operation == CssCombineOperation.Interpolate
                && CssGenericInterpolation.TryGetNumber(arguments.Item1[i], out var lengthA, out var unitA) && CssGenericInterpolation.TryGetNumber(arguments.Item2[i], out var lengthB, out var unitB)
                && unitA != unitB && unitA is "px" or "%" && unitB is "px" or "%" && (lengthA == 0 || lengthB == 0))
            {
                // A zero length or percentage has no type of its own: the result has the type of the other value, or of the
                // end value when both are zero (as in the browsers: translateY(0%) to translateX(50%) is translate(25%, 0px))
                combined = CssGenericInterpolation.FormatNumber(CssGenericInterpolation.Lerp(lengthA, lengthB, progress)) + (lengthB == 0 && lengthA != 0 ? unitA : unitB);
            }
            else
            {
                combined = CssGenericInterpolation.CombineNumeric(arguments.Item1[i], arguments.Item2[i], CssNumericTraits.LengthPercentage, operation == CssCombineOperation.Interpolate ? operation : CssCombineOperation.Add, progress, topLevel: false);
            }

            if (combined is null)
                return null;

            builder.Append(combined);
        }

        return builder.Append(')').ToString();
    }

    /// <summary>The perspective functions are interpolated by the inverse of their distance (none is an infinite distance).</summary>
    private static string? CombinePerspective(TransformFunction a, TransformFunction b, CssCombineOperation operation, double progress)
    {
        if (GetPerspectiveInverse(a) is not { } inverseA || GetPerspectiveInverse(b) is not { } inverseB)
            return null;

        if (operation != CssCombineOperation.Interpolate)
            return null;

        var inverse = CssGenericInterpolation.Lerp(inverseA, inverseB, progress);
        return inverse <= 0 ? "perspective(none)" : "perspective(" + CssGenericInterpolation.FormatNumber(1 / inverse) + "px)";
    }

    private static double? GetPerspectiveInverse(TransformFunction function)
    {
        if (function.Arguments is [CssPreservedToken { Type: CssTokenType.Ident } ident] && ident.Token.IsIdent("none"))
            return 0;

        if (function.Arguments is [var argument] && CssGenericInterpolation.TryGetNumber(argument, out var distance, out var unit) && unit == "px")
        {
            // https://drafts.csswg.org/css-transforms-2/#funcdef-perspective: "values less than 1px [...] are treated as 1px"
            return 1 / Math.Max(distance, 1);
        }


        return null;
    }

    /// <summary>Combines two lists of functions as matrices (https://drafts.csswg.org/css-transforms-2/#interpolation-of-3d-matrices).</summary>
    private static string? CombineMatrices(CssInterpolationContext context, List<TransformFunction> a, List<TransformFunction> b, CssCombineOperation operation, double progress)
    {
        (double Width, double Height)? referenceBox;
        using (new CssColor.OutOfRangeRgbScope(enable: false))
        {
            referenceBox = context.Style?.GetTransformReferenceBox();
        }

        if (ToMatrix(a, referenceBox) is not { } matrixA || ToMatrix(b, referenceBox) is not { } matrixB)
            return null;

        if (operation == CssCombineOperation.Interpolate && CssMatrixDecomposition.Is2D(matrixA) && CssMatrixDecomposition.Is2D(matrixB))
        {
            if (CssMatrixDecomposition.Decompose2D(matrixA) is not { } decomposedA || CssMatrixDecomposition.Decompose2D(matrixB) is not { } decomposedB)
                return null;

            return CssTransforms.Serialize(CssMatrixDecomposition.Recompose(CssMatrixDecomposition.Interpolate(decomposedA, decomposedB, progress)));
        }

        if (CssMatrixDecomposition.Decompose3D(matrixA) is not { } a3 || CssMatrixDecomposition.Decompose3D(matrixB) is not { } b3)
            return null;

        var combined = operation == CssCombineOperation.Interpolate ? CssMatrixDecomposition.Interpolate(a3, b3, progress) : CssMatrixDecomposition.Accumulate(a3, b3);
        return CssTransforms.Serialize(CssMatrixDecomposition.Recompose(combined));
    }

    private static double[]? ToMatrix(List<TransformFunction> functions, (double Width, double Height)? referenceBox)
    {
        var text = string.Join(' ', functions.Select(Serialize));
        return CssTransforms.ToMatrix(CssParser.ParseListOfComponentValues(text.Length == 0 ? "none" : text), CssCalcContext.Default, referenceBox, out _);
    }

    /// <summary>Parses a computed transform list: an empty list for none, or null when it is not a transform list.</summary>
    private static List<TransformFunction>? Parse(string value)
    {
        var components = CssGenericInterpolation.Parse(value);
        if (components is [CssPreservedToken { Type: CssTokenType.Ident } ident] && ident.Token.IsIdent("none"))
            return [];

        var result = new List<TransformFunction>(components.Count);
        foreach (var component in components)
        {
            if (component is not CssFunction function || GetFamily(InfraAscii.ToAsciiLowercase(function.Name)) is null)
                return null;

            var arguments = new List<CssComponentValue>();
            foreach (var item in ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value)))
            {
                if (item is not [var argument])
                    return null;

                arguments.Add(argument);
            }

            result.Add(new TransformFunction(GetCanonicalName(function.Name), arguments));
        }

        return result;
    }

    private static string GetCanonicalName(string name) => InfraAscii.ToAsciiLowercase(name) switch
    {
        "translatex" => "translateX",
        "translatey" => "translateY",
        "translatez" => "translateZ",
        "scalex" => "scaleX",
        "scaley" => "scaleY",
        "scalez" => "scaleZ",
        "rotatex" => "rotateX",
        "rotatey" => "rotateY",
        "rotatez" => "rotateZ",
        "skewx" => "skewX",
        "skewy" => "skewY",
        var lower => lower,
    };

    private static Family? GetFamily(string name) => InfraAscii.ToAsciiLowercase(name) switch
    {
        "translate" or "translatex" or "translatey" or "translatez" or "translate3d" => Family.Translate,
        "scale" or "scalex" or "scaley" or "scalez" or "scale3d" => Family.Scale,
        "rotate" or "rotatex" or "rotatey" or "rotatez" or "rotate3d" => Family.Rotate,
        "skew" or "skewx" or "skewy" => Family.Skew,
        "perspective" => Family.Perspective,
        "matrix" or "matrix3d" => Family.Matrix,
        _ => null,
    };

    private static bool Is3D(string name) => name is "translateZ" or "translate3d" or "scaleZ" or "scale3d" or "rotateX" or "rotateY" or "rotateZ" or "rotate3d" or "matrix3d" or "perspective";

    private static string GetPrimitiveName(Family family, bool threeD) => family switch
    {
        Family.Translate => threeD ? "translate3d" : "translate",
        Family.Scale => threeD ? "scale3d" : "scale",
        Family.Rotate => threeD ? "rotate3d" : "rotate",
        Family.Skew => "skew",
        _ => "perspective",
    };

    /// <summary>Returns the arguments of the primitive of a function (e.g. translateX(10px) is translate(10px, 0px)).</summary>
    private static List<CssComponentValue>? ToPrimitive(TransformFunction function, bool threeD)
    {
        var args = function.Arguments;
        CssComponentValue Zero(string unit) => Token(0, unit);
        CssComponentValue One() => Token(1, "");
        List<CssComponentValue>? result = function.Name switch
        {
            "translate" when args.Count is 1 or 2 => threeD ? [args[0], args.Count == 2 ? args[1] : Zero("px"), Zero("px")] : [args[0], args.Count == 2 ? args[1] : Zero("px")],
            "translateX" when args.Count == 1 => threeD ? [args[0], Zero("px"), Zero("px")] : [args[0], Zero("px")],
            "translateY" when args.Count == 1 => threeD ? [Zero("px"), args[0], Zero("px")] : [Zero("px"), args[0]],
            "translateZ" when args.Count == 1 => [Zero("px"), Zero("px"), args[0]],
            "translate3d" when args.Count == 3 => args,
            "scale" when args.Count is 1 or 2 => threeD ? [args[0], args.Count == 2 ? args[1] : args[0], One()] : [args[0], args.Count == 2 ? args[1] : args[0]],
            "scaleX" when args.Count == 1 => threeD ? [args[0], One(), One()] : [args[0], One()],
            "scaleY" when args.Count == 1 => threeD ? [One(), args[0], One()] : [One(), args[0]],
            "scaleZ" when args.Count == 1 => [One(), One(), args[0]],
            "scale3d" when args.Count == 3 => args,
            "rotate" when args.Count == 1 => threeD ? [Zero(""), Zero(""), One(), args[0]] : args,
            "rotateX" when args.Count == 1 => [One(), Zero(""), Zero(""), args[0]],
            "rotateY" when args.Count == 1 => [Zero(""), One(), Zero(""), args[0]],
            "rotateZ" when args.Count == 1 => [Zero(""), Zero(""), One(), args[0]],
            "rotate3d" when args.Count == 4 => args,
            "skew" when args.Count is 1 or 2 => [args[0], args.Count == 2 ? args[1] : Zero("deg")],
            "skewX" when args.Count == 1 => [args[0], Zero("deg")],
            "skewY" when args.Count == 1 => [Zero("deg"), args[0]],
            "perspective" when args.Count == 1 => args,
            _ => null,
        };

        return result;
    }

    /// <summary>
    /// Returns the identity function of a function (https://drafts.csswg.org/css-transforms-2/#identity-transform-function): the
    /// same function with the arguments of the identity (e.g. translateX(0px) for translateX(), scale(1) for scale(2)), so that
    /// the functions combined with it keep their form.
    /// </summary>
    private static TransformFunction GetIdentity(TransformFunction function)
    {
        var zero = Token(0, "px");
        var one = Token(1, "");
        var zeroAngle = Token(0, "deg");
        return function.Name switch
        {
            "translate" or "translateX" or "translateY" or "translateZ" or "translate3d" => new TransformFunction(function.Name, [.. function.Arguments.Select(_ => zero)]),
            "scale" or "scaleX" or "scaleY" or "scaleZ" or "scale3d" => new TransformFunction(function.Name, [.. function.Arguments.Select(_ => one)]),
            "rotate" => new TransformFunction("rotate", [zeroAngle]),
            "rotateX" or "rotateY" or "rotateZ" => new TransformFunction(function.Name, [zeroAngle]),
            "rotate3d" => new TransformFunction("rotate3d", [function.Arguments[0], function.Arguments[1], function.Arguments[2], zeroAngle]),
            "skew" => new TransformFunction("skew", [zeroAngle, zeroAngle]),
            "skewX" or "skewY" => new TransformFunction(function.Name, [zeroAngle]),
            "perspective" => new TransformFunction("perspective", [CssGenericInterpolation.Parse("none")[0]]),
            "matrix3d" => new TransformFunction("matrix3d", [one, Token(0, ""), Token(0, ""), Token(0, ""), Token(0, ""), one, Token(0, ""), Token(0, ""), Token(0, ""), Token(0, ""), one, Token(0, ""), Token(0, ""), Token(0, ""), Token(0, ""), one]),
            _ => new TransformFunction("matrix", [one, Token(0, ""), Token(0, ""), one, Token(0, ""), Token(0, "")]),
        };
    }

    /// <summary>Returns whether a function is an identity function (all the arguments are 0, or 1 for the scales, or none).</summary>
    private static bool IsIdentity(TransformFunction function)
    {
        var family = GetFamily(function.Name);
        foreach (var argument in function.Arguments)
        {
            if (argument is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.IsIdent("none"))
                continue;

            if (!CssGenericInterpolation.TryGetNumber(argument, out var value, out _) || value != (family == Family.Scale ? 1 : 0))
                return false;
        }

        return family != Family.Matrix;
    }

    private static CssComponentValue Token(double value, string unit) => CssGenericInterpolation.Parse(CssGenericInterpolation.FormatNumber(value) + unit)[0];

    private static bool TryGetRotation(List<CssComponentValue> arguments, out double[] axis, out double angle)
    {
        axis = new double[3];
        angle = 0;
        if (arguments.Count != 4)
            return false;

        for (var i = 0; i < 3; i++)
        {
            if (!CssGenericInterpolation.TryGetNumber(arguments[i], out axis[i], out var unit) || unit.Length != 0)
                return false;
        }

        if (!CssGenericInterpolation.TryGetNumber(arguments[3], out angle, out var angleUnit))
            return false;

        angle = angleUnit switch
        {
            "deg" => angle,
            "rad" => angle * 180 / Math.PI,
            "grad" => angle * 0.9,
            "turn" => angle * 360,
            "" when angle == 0 => 0,
            _ => double.NaN,
        };

        var length = Math.Sqrt((axis[0] * axis[0]) + (axis[1] * axis[1]) + (axis[2] * axis[2]));
        if (length == 0 || double.IsNaN(angle))
            return false;

        axis[0] /= length;
        axis[1] /= length;
        axis[2] /= length;
        return true;
    }

    private static bool SameAxis(double[] a, double[] b) => Math.Abs(a[0] - b[0]) < 1e-9 && Math.Abs(a[1] - b[1]) < 1e-9 && Math.Abs(a[2] - b[2]) < 1e-9;

    /// <summary>
    /// Serializes a computed transform list as the declared value written by Animation.commitStyles()
    /// (https://drafts.csswg.org/web-animations-1/#dom-animation-commitstyles), in the shortest form of its functions, as
    /// in the browsers: a zero y translation of translate() and a y scale of scale() equal to the x scale are omitted.
    /// </summary>
    public static string SerializeCommitted(string value)
    {
        if (Parse(value) is not { Count: > 0 } functions)
            return value;

        var builder = new StringBuilder();
        foreach (var function in functions)
        {
            var shortened = function;
            if (function is { Name: "translate", Arguments: [var x, var y] } && CssGenericInterpolation.TryGetNumber(y, out var translation, out var unit) && translation == 0 && unit == "px")
            {
                shortened = new TransformFunction(function.Name, [x]);
            }
            else if (function is { Name: "scale", Arguments: [var scaleX, var scaleY] } && CssGenericInterpolation.TryGetNumber(scaleX, out var sx, out var unitX) && CssGenericInterpolation.TryGetNumber(scaleY, out var sy, out var unitY) && sx == sy && unitX == unitY)
            {
                shortened = new TransformFunction(function.Name, [scaleX]);
            }

            Append(builder, Serialize(shortened));
        }

        return builder.ToString();
    }

    private static string Serialize(TransformFunction function) => function.Name + "(" + string.Join(", ", function.Arguments.Select(argument => CssSerializer.SerializeValue([argument], property: null))) + ")";
}
