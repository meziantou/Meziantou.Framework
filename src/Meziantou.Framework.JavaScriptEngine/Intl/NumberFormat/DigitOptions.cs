using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

internal enum RoundingType : byte
{
    FractionDigits,
    SignificantDigits,
    MorePrecision,
    LessPrecision,
}

/// <summary>The result of FormatNumericToString: the rounded value and its digits ("123.40", "-" not included).</summary>
internal readonly record struct FormattedNumber(IntlDecimal RoundedNumber, string FormattedString);

/// <summary>
/// The digit options of Intl.NumberFormat and Intl.PluralRules (SetNumberFormatDigitOptions) and the rounding of
/// FormatNumericToString (ToRawPrecision, ToRawFixed).
/// </summary>
internal sealed class DigitOptions
{
    private static readonly int[] RoundingIncrements = [1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000];

    public int MinimumIntegerDigits { get; set; } = 1;

    public int MinimumFractionDigits { get; set; }

    public int MaximumFractionDigits { get; set; }

    public int MinimumSignificantDigits { get; set; }

    public int MaximumSignificantDigits { get; set; }

    public RoundingType RoundingType { get; set; }

    /// <summary>[[ComputedRoundingPriority]]: "auto", "morePrecision" or "lessPrecision".</summary>
    public string ComputedRoundingPriority { get; set; } = "auto";

    public int RoundingIncrement { get; set; } = 1;

    /// <summary>[[RoundingMode]]: "ceil", "floor", "expand", "trunc", "halfCeil", "halfFloor", "halfExpand", "halfTrunc" or "halfEven".</summary>
    public string RoundingMode { get; set; } = "halfExpand";

    /// <summary>[[TrailingZeroDisplay]]: "auto" or "stripIfInteger".</summary>
    public string TrailingZeroDisplay { get; set; } = "auto";

    public bool HasSignificantDigits => RoundingType is not RoundingType.FractionDigits;

    public bool HasFractionDigits => RoundingType is not RoundingType.SignificantDigits;

    /// <summary>SetNumberFormatDigitOptions(intlObj, options, mnfdDefault, mxfdDefault, notation)</summary>
    public void Set(JsObject options, int mnfdDefault, int mxfdDefault, string notation)
    {
        var mnid = IntlOperations.GetNumberOption(options, "minimumIntegerDigits", 1, 21, 1)!.Value;
        var mnfd = options.Get("minimumFractionDigits");
        var mxfd = options.Get("maximumFractionDigits");
        var mnsd = options.Get("minimumSignificantDigits");
        var mxsd = options.Get("maximumSignificantDigits");
        MinimumIntegerDigits = mnid;
        var roundingIncrement = IntlOperations.GetNumberOption(options, "roundingIncrement", 1, 5000, 1)!.Value;
        if (Array.IndexOf(RoundingIncrements, roundingIncrement) < 0)
            ThrowHelper.ThrowRangeError("roundingIncrement value is out of range.");

        var roundingMode = IntlOperations.GetStringOption(options, "roundingMode", ["ceil", "floor", "expand", "trunc", "halfCeil", "halfFloor", "halfExpand", "halfTrunc", "halfEven"], "halfExpand")!;
        var roundingPriority = IntlOperations.GetStringOption(options, "roundingPriority", ["auto", "morePrecision", "lessPrecision"], "auto")!;
        var trailingZeroDisplay = IntlOperations.GetStringOption(options, "trailingZeroDisplay", ["auto", "stripIfInteger"], "auto")!;
        if (roundingIncrement != 1)
        {
            mxfdDefault = mnfdDefault;
        }

        RoundingIncrement = roundingIncrement;
        RoundingMode = roundingMode;
        TrailingZeroDisplay = trailingZeroDisplay;
        var hasSd = !mnsd.IsUndefined || !mxsd.IsUndefined;
        var hasFd = !mnfd.IsUndefined || !mxfd.IsUndefined;
        var needSd = true;
        var needFd = true;
        if (roundingPriority is "auto")
        {
            needSd = hasSd;
            if (needSd || (!hasFd && notation is "compact"))
            {
                needFd = false;
            }
        }

        if (needSd)
        {
            if (hasSd)
            {
                MinimumSignificantDigits = IntlOperations.DefaultNumberOption(mnsd, 1, 21, 1, "minimumSignificantDigits")!.Value;
                MaximumSignificantDigits = IntlOperations.DefaultNumberOption(mxsd, MinimumSignificantDigits, 21, 21, "maximumSignificantDigits")!.Value;
            }
            else
            {
                MinimumSignificantDigits = 1;
                MaximumSignificantDigits = 21;
            }
        }

        if (needFd)
        {
            if (hasFd)
            {
                var minimum = IntlOperations.DefaultNumberOption(mnfd, 0, 100, null, "minimumFractionDigits");
                var maximum = IntlOperations.DefaultNumberOption(mxfd, 0, 100, null, "maximumFractionDigits");
                if (minimum is null)
                {
                    minimum = Math.Min(mnfdDefault, maximum!.Value);
                }
                else if (maximum is null)
                {
                    maximum = Math.Max(mxfdDefault, minimum.Value);
                }
                else if (minimum > maximum)
                {
                    ThrowHelper.ThrowRangeError("maximumFractionDigits value is out of range.");
                }

                MinimumFractionDigits = minimum.Value;
                MaximumFractionDigits = maximum!.Value;
            }
            else
            {
                MinimumFractionDigits = mnfdDefault;
                MaximumFractionDigits = mxfdDefault;
            }
        }

        if (!needSd && !needFd)
        {
            MinimumFractionDigits = 0;
            MaximumFractionDigits = 0;
            MinimumSignificantDigits = 1;
            MaximumSignificantDigits = 2;
            RoundingType = RoundingType.MorePrecision;
            ComputedRoundingPriority = "morePrecision";
        }
        else if (roundingPriority is "morePrecision")
        {
            RoundingType = RoundingType.MorePrecision;
            ComputedRoundingPriority = "morePrecision";
        }
        else if (roundingPriority is "lessPrecision")
        {
            RoundingType = RoundingType.LessPrecision;
            ComputedRoundingPriority = "lessPrecision";
        }
        else if (hasSd)
        {
            RoundingType = RoundingType.SignificantDigits;
            ComputedRoundingPriority = "auto";
        }
        else
        {
            RoundingType = RoundingType.FractionDigits;
            ComputedRoundingPriority = "auto";
        }

        if (roundingIncrement != 1)
        {
            if (RoundingType is not RoundingType.FractionDigits)
                ThrowHelper.ThrowTypeError("roundingIncrement is only supported with fraction digits rounding");

            if (MaximumFractionDigits != MinimumFractionDigits)
                ThrowHelper.ThrowRangeError("maximumFractionDigits must be equal to minimumFractionDigits with a roundingIncrement");
        }
    }

    /// <summary>GetUnsignedRoundingMode(roundingMode, sign)</summary>
    public static UnsignedRoundingMode GetUnsignedRoundingMode(string roundingMode, bool isNegative) => roundingMode switch
    {
        "ceil" => isNegative ? UnsignedRoundingMode.Zero : UnsignedRoundingMode.Infinity,
        "floor" => isNegative ? UnsignedRoundingMode.Infinity : UnsignedRoundingMode.Zero,
        "expand" => UnsignedRoundingMode.Infinity,
        "trunc" => UnsignedRoundingMode.Zero,
        "halfCeil" => isNegative ? UnsignedRoundingMode.HalfZero : UnsignedRoundingMode.HalfInfinity,
        "halfFloor" => isNegative ? UnsignedRoundingMode.HalfInfinity : UnsignedRoundingMode.HalfZero,
        "halfTrunc" => UnsignedRoundingMode.HalfZero,
        "halfEven" => UnsignedRoundingMode.HalfEven,
        _ => UnsignedRoundingMode.HalfInfinity,
    };

    /// <summary>FormatNumericToString(intlObject, x): x must be finite.</summary>
    public FormattedNumber FormatNumericToString(IntlDecimal x)
    {
        var isNegative = x.IsNegative;
        var abs = x.Abs();
        var mode = GetUnsignedRoundingMode(RoundingMode, isNegative);
        RawResult result;
        switch (RoundingType)
        {
            case RoundingType.SignificantDigits:
                result = ToRawPrecision(abs, MinimumSignificantDigits, MaximumSignificantDigits, mode);
                break;
            case RoundingType.FractionDigits:
                result = ToRawFixed(abs, MinimumFractionDigits, MaximumFractionDigits, RoundingIncrement, mode);
                break;
            default:
                var sResult = ToRawPrecision(abs, MinimumSignificantDigits, MaximumSignificantDigits, mode);
                var fResult = ToRawFixed(abs, MinimumFractionDigits, MaximumFractionDigits, RoundingIncrement, mode);
                if (RoundingType is RoundingType.MorePrecision)
                {
                    result = sResult.RoundingMagnitude <= fResult.RoundingMagnitude ? sResult : fResult;
                }
                else
                {
                    result = sResult.RoundingMagnitude <= fResult.RoundingMagnitude ? fResult : sResult;
                }

                break;
        }

        var rounded = result.RoundedNumber;
        var text = result.FormattedString;
        if (TrailingZeroDisplay is "stripIfInteger" && rounded.IsInteger)
        {
            var dot = text.IndexOf('.', StringComparison.Ordinal);
            if (dot >= 0)
            {
                text = text[..dot];
            }
        }

        if (result.IntegerDigitsCount < MinimumIntegerDigits)
        {
            text = new string('0', MinimumIntegerDigits - result.IntegerDigitsCount) + text;
        }

        if (isNegative)
        {
            rounded = rounded.Negate();
        }

        return new FormattedNumber(rounded, text);
    }

    private readonly record struct RawResult(string FormattedString, IntlDecimal RoundedNumber, int IntegerDigitsCount, int RoundingMagnitude);

    /// <summary>ToRawPrecision(x, minPrecision, maxPrecision, unsignedRoundingMode) for a non-negative x.</summary>
    private static RawResult ToRawPrecision(IntlDecimal x, int minPrecision, int maxPrecision, UnsignedRoundingMode mode)
    {
        var p = maxPrecision;
        string m;
        int e;
        IntlDecimal rounded;
        if (x.IsZero)
        {
            m = new string('0', p);
            e = 0;
            rounded = IntlDecimal.Zero;
        }
        else
        {
            e = x.Magnitude;
            var n = IntlDecimal.RoundToMagnitude(x.Coefficient, x.Exponent, e - p + 1, 1, mode);
            if (n >= IntlDecimal.PowerOfTen(p))
            {
                // Rounded up to the next power of ten
                n /= 10;
                e++;
            }

            rounded = new IntlDecimal(isNegative: false, n, e - p + 1);
            m = n.ToString(CultureInfo.InvariantCulture);
        }

        int integerDigits;
        if (e >= p - 1)
        {
            m += new string('0', e - p + 1);
            integerDigits = e + 1;
        }
        else if (e >= 0)
        {
            m = string.Concat(m.AsSpan(0, e + 1), ".", m.AsSpan(e + 1));
            integerDigits = e + 1;
        }
        else
        {
            m = "0." + new string('0', -(e + 1)) + m;
            integerDigits = 1;
        }

        if (m.Contains('.', StringComparison.Ordinal) && maxPrecision > minPrecision)
        {
            m = TrimTrailingZeros(m, maxPrecision - minPrecision);
        }

        return new RawResult(m, rounded, integerDigits, e - p + 1);
    }

    /// <summary>ToRawFixed(x, minFraction, maxFraction, roundingIncrement, unsignedRoundingMode) for a non-negative x.</summary>
    private static RawResult ToRawFixed(IntlDecimal x, int minFraction, int maxFraction, int roundingIncrement, UnsignedRoundingMode mode)
    {
        var f = maxFraction;
        var n = x.IsZero ? BigInteger.Zero : IntlDecimal.RoundToMagnitude(x.Coefficient, x.Exponent, -f, roundingIncrement, mode);
        var rounded = new IntlDecimal(isNegative: false, n, -f);
        var m = n.IsZero ? "0" : n.ToString(CultureInfo.InvariantCulture);
        int integerDigits;
        if (f != 0)
        {
            var k = m.Length;
            if (k <= f)
            {
                m = new string('0', f + 1 - k) + m;
                k = f + 1;
            }

            m = string.Concat(m.AsSpan(0, k - f), ".", m.AsSpan(k - f));
            integerDigits = k - f;
        }
        else
        {
            integerDigits = m.Length;
        }

        m = TrimTrailingZeros(m, maxFraction - minFraction);
        return new RawResult(m, rounded, integerDigits, -f);
    }

    private static string TrimTrailingZeros(string value, int cut)
    {
        var length = value.Length;
        while (cut > 0 && value[length - 1] == '0')
        {
            length--;
            cut--;
        }

        if (value[length - 1] == '.')
        {
            length--;
        }

        return value[..length];
    }
}
