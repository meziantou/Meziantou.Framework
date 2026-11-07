using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A used &lt;length-percentage&gt; (https://drafts.csswg.org/css-values-4/#typedef-length-percentage): a length in CSS px
/// (multiplied by the effective zoom, https://drafts.csswg.org/css-viewport/#zoom-property) plus a percentage, which is
/// resolved against its basis during layout. A math function that is not a sum of a length and a percentage (e.g.
/// <c>min(10px, 50%)</c>) is kept as a <see cref="CalcLength"/> and evaluated when the basis is known.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct LengthPercentage : IEquatable<LengthPercentage>
{
    private readonly double _length;

    /// <summary>The percentage (50 for 50%), when <see cref="_hasPercentage"/> is true.</summary>
    private readonly double _percentage;

    private readonly CalcLength? _calc;
    private readonly bool _hasPercentage;

    private LengthPercentage(double length, double percentage, bool hasPercentage, CalcLength? calc)
    {
        _length = length;
        _percentage = hasPercentage ? percentage : 0;
        _hasPercentage = hasPercentage;
        _calc = calc;
    }

    /// <summary>0px.</summary>
    public static LengthPercentage Zero => default;

    /// <summary>The fixed part of the value, in px (0 for a <see cref="CalcLength"/>).</summary>
    public double Length => _length;

    /// <summary>The percentage part of the value (50 for 50%), or 0 when there is none.</summary>
    public double Percentage => _percentage;

    /// <summary>Whether the value depends on its percentage basis (a percentage, or a math function with a percentage).</summary>
    public bool HasPercentage => _hasPercentage || _calc is not null;

    /// <summary>Whether the value is a length that does not depend on a percentage basis.</summary>
    public bool IsFixed => !HasPercentage;

    /// <summary>Whether the value is 0px (a 0% does not count: it depends on the basis, e.g. a height of 0% may behave as auto).</summary>
    public bool IsZero => IsFixed && _length == 0;

    /// <summary>The math function that is evaluated with the percentage basis, or null.</summary>
    public CalcLength? Calc => _calc;

    public static LengthPercentage FromPixels(double pixels) => new(pixels, 0, hasPercentage: false, calc: null);

    /// <summary>Creates a percentage (50 for 50%).</summary>
    public static LengthPercentage FromPercentage(double percentage) => new(0, percentage, hasPercentage: true, calc: null);

    /// <summary>Creates the sum of a length in px and a percentage (e.g. <c>calc(50% + 10px)</c>).</summary>
    public static LengthPercentage FromSum(double pixels, double percentage) => new(pixels, percentage, hasPercentage: true, calc: null);

    internal static LengthPercentage FromCalc(CalcLength calc) => new(0, 0, hasPercentage: false, calc);

    /// <summary>Resolves the value against its percentage basis, in px.</summary>
    public double Resolve(double percentageBasis)
    {
        if (_calc is not null)
            return _calc.Resolve(percentageBasis);

        return _hasPercentage ? _length + (percentageBasis * _percentage / 100) : _length;
    }

    /// <summary>Resolves the value, or returns null when it depends on a percentage basis that is not known (indefinite).</summary>
    public double? TryResolve(double? percentageBasis)
    {
        if (percentageBasis is { } basis)
            return Resolve(basis);

        return HasPercentage ? null : _length;
    }

    /// <summary>
    /// Parses a computed &lt;length-percentage&gt; (e.g. <c>10px</c>, <c>50%</c>, <c>calc(50% + 10px)</c>, <c>min(10px, 5%)</c>):
    /// the lengths are multiplied by <paramref name="zoom"/>. Returns false for the other values (keywords, anchor functions...).
    /// </summary>
    /// <param name="value">The computed value.</param>
    /// <param name="zoom">The effective zoom of the element.</param>
    /// <param name="nonNegative">Whether the property only accepts non-negative values: the math functions are clamped at used time (https://drafts.csswg.org/css-values-4/#calc-range).</param>
    /// <param name="result">The value.</param>
    public static bool TryParse(string value, double zoom, bool nonNegative, out LengthPercentage result)
    {
        if (value.Length == 0)
        {
            result = default;
            return false;
        }

        var last = value[^1];
        if (last == 'x' && value.EndsWith("px", StringComparison.Ordinal) && TryParseNumber(value.AsSpan(0, value.Length - 2), out var pixels))
        {
            result = FromPixels(pixels * zoom);
            return true;
        }

        if (last == '%' && TryParseNumber(value.AsSpan(0, value.Length - 1), out var percentage))
        {
            result = FromPercentage(percentage);
            return true;
        }

        if (value == "0")
        {
            result = Zero;
            return true;
        }

        if (last == ')' && ParseMathFunction(value) is { } function)
            return TryParseMathFunction(function, zoom, nonNegative, out result);

        result = default;
        return false;
    }

    /// <summary>Parses a component value of a computed value that is a &lt;length-percentage&gt; (e.g. a track size of a grid template).</summary>
    public static bool TryParse(CssComponentValue value, double zoom, bool nonNegative, out LengthPercentage result)
    {
        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Dimension } dimension when string.Equals(dimension.Token.Value, "px", StringComparison.OrdinalIgnoreCase):
                result = FromPixels(dimension.Token.NumericValue * zoom);
                return true;
            case CssPreservedToken { Type: CssTokenType.Percentage } percentage:
                result = FromPercentage(percentage.Token.NumericValue);
                return true;
            case CssPreservedToken { Type: CssTokenType.Number } number when number.Token.NumericValue == 0:
                result = Zero;
                return true;
            case CssFunction function when IsLengthMathFunction(function.Name):
                return TryParseMathFunction(function, zoom, nonNegative, out result);
            default:
                result = default;
                return false;
        }
    }

    /// <summary>Whether a function is a math function that may be a &lt;length-percentage&gt; (calc-size() is a sizing value, see <see cref="Size"/>).</summary>
    private static bool IsLengthMathFunction(string name) => CssCalc.IsMathFunction(name) && !Infra.InfraAscii.EqualsIgnoreAsciiCase(name, "calc-size");

    /// <summary>Parses a value that is a single math function, or returns null.</summary>
    internal static CssFunction? ParseMathFunction(string value)
    {
        var values = CssParser.ParseListOfComponentValues(value);
        CssFunction? function = null;
        foreach (var component in values)
        {
            switch (component)
            {
                case CssPreservedToken { Type: CssTokenType.Whitespace }:
                    break;
                case CssFunction f when function is null && IsLengthMathFunction(f.Name):
                    function = f;
                    break;
                default:
                    return null;
            }
        }

        return function;
    }

    /// <summary>Converts a math function: a sum of a length and a percentage is stored as is, the other functions are kept.</summary>
    internal static bool TryParseMathFunction(CssFunction function, double zoom, bool nonNegative, out LengthPercentage result)
    {
        if (CssCalc.Evaluate(function, LengthPercentageContext) is not { } calc || calc.TypeUnit != "px")
        {
            result = default;
            return false;
        }

        // https://drafts.csswg.org/css-values-4/#calc-simplification: the computed value of a calc() of lengths and
        // percentages is a sum of a length and a percentage
        double length = 0;
        var percentage = double.NaN;
        var linear = true;
        switch (calc.Root)
        {
            case CssCalcNumericNode numeric:
                linear = AddTerm(numeric, ref length, ref percentage);
                break;

            case CssCalcSumNode sum:
                foreach (var child in sum.Children)
                {
                    if (child is not CssCalcNumericNode term || !AddTerm(term, ref length, ref percentage))
                    {
                        linear = false;
                        break;
                    }
                }

                break;

            default:
                linear = false;
                break;
        }

        // A sum that may be negative is clamped at used time, by the math function
        if (linear && (!nonNegative || (length >= 0 && (double.IsNaN(percentage) || percentage >= 0))))
        {
            result = new LengthPercentage(SanitizeLength(length * zoom), double.IsNaN(percentage) ? 0 : percentage, hasPercentage: !double.IsNaN(percentage), calc: null);
            return true;
        }

        result = FromCalc(new CalcLength(function, zoom, nonNegative));
        return true;

        static bool AddTerm(CssCalcNumericNode term, ref double length, ref double percentage)
        {
            switch (term.Unit)
            {
                case "px":
                    // NaN is 0 (https://drafts.csswg.org/css-values-4/#calc-ieee)
                    length += double.IsNaN(term.Value) ? 0 : term.Value;
                    return true;
                case "%":
                    percentage = (double.IsNaN(percentage) ? 0 : percentage) + (double.IsNaN(term.Value) ? 0 : term.Value);
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>The context of the computed math functions of a &lt;length-percentage&gt;: the percentages stand for lengths.</summary>
    internal static CssCalcContext LengthPercentageContext { get; } = new() { PercentageUnit = "px" };

    /// <summary>
    /// https://drafts.csswg.org/css-values-4/#calc-ieee: NaN is 0, and the infinite values are clamped to the largest
    /// representable values.
    /// </summary>
    internal static double SanitizeLength(double value)
    {
        if (double.IsNaN(value))
            return 0;

        if (double.IsInfinity(value))
            return value > 0 ? CalcLength.MaxLength : -CalcLength.MaxLength;

        return value;
    }

    internal static bool TryParseNumber(ReadOnlySpan<char> value, out double result) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    public bool Equals(LengthPercentage other) => _length.Equals(other._length) && _percentage.Equals(other._percentage) && _hasPercentage == other._hasPercentage && ReferenceEquals(_calc, other._calc);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is LengthPercentage other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_length, _percentage, _hasPercentage, _calc);

    public static bool operator ==(LengthPercentage left, LengthPercentage right) => left.Equals(right);

    public static bool operator !=(LengthPercentage left, LengthPercentage right) => !left.Equals(right);

    public override string ToString()
    {
        if (_calc is not null)
            return _calc.ToString();

        if (!_hasPercentage)
            return Format(_length) + "px";

        return _length == 0 ? Format(_percentage) + "%" : "calc(" + Format(_percentage) + "% + " + Format(_length) + "px)";

        static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
