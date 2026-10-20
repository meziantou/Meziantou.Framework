using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.TypedOm;

/// <summary>https://drafts.css-houdini.org/css-typed-om-1/#enumdef-cssnumericbasetype</summary>
/// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#enumdef-cssnumericbasetype"/>
[IdlInterface("CSSNumericBaseType", "https://drafts.css-houdini.org/css-typed-om-1/#enumdef-cssnumericbasetype")]
public enum CssNumericBaseType
{
    /// <summary>The <c>length</c> base type.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-length"/>
    [IdlMember("length", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-length")]
    Length,

    /// <summary>The <c>angle</c> base type.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-angle"/>
    [IdlMember("angle", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-angle")]
    Angle,

    /// <summary>The <c>time</c> base type.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-time"/>
    [IdlMember("time", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-time")]
    Time,

    /// <summary>The <c>frequency</c> base type.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-frequency"/>
    [IdlMember("frequency", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-frequency")]
    Frequency,

    /// <summary>The <c>resolution</c> base type.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-resolution"/>
    [IdlMember("resolution", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-resolution")]
    Resolution,

    /// <summary>The <c>flex</c> base type.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-flex"/>
    [IdlMember("flex", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-flex")]
    Flex,

    /// <summary>The <c>percent</c> base type.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-percent"/>
    [IdlMember("percent", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericbasetype-percent")]
    Percent,
}

/// <summary>The type of a numeric value, returned by <see cref="CssNumericValue.Type"/> (https://drafts.css-houdini.org/css-typed-om-1/#dictdef-cssnumerictype).</summary>
/// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dictdef-cssnumerictype"/>
[IdlInterface("CSSNumericType", "https://drafts.css-houdini.org/css-typed-om-1/#dictdef-cssnumerictype")]
public sealed class CssNumericType
{
    /// <summary>Gets the exponent of the <c>length</c> base type, or null when it is zero.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-length"/>
    [IdlMember("length", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-length")]
    public int? Length { get; init; }

    /// <summary>Gets the exponent of the <c>angle</c> base type, or null when it is zero.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-angle"/>
    [IdlMember("angle", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-angle")]
    public int? Angle { get; init; }

    /// <summary>Gets the exponent of the <c>time</c> base type, or null when it is zero.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-time"/>
    [IdlMember("time", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-time")]
    public int? Time { get; init; }

    /// <summary>Gets the exponent of the <c>frequency</c> base type, or null when it is zero.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-frequency"/>
    [IdlMember("frequency", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-frequency")]
    public int? Frequency { get; init; }

    /// <summary>Gets the exponent of the <c>resolution</c> base type, or null when it is zero.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-resolution"/>
    [IdlMember("resolution", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-resolution")]
    public int? Resolution { get; init; }

    /// <summary>Gets the exponent of the <c>flex</c> base type, or null when it is zero.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-flex"/>
    [IdlMember("flex", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-flex")]
    public int? Flex { get; init; }

    /// <summary>Gets the exponent of the <c>percent</c> base type, or null when it is zero.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-percent"/>
    [IdlMember("percent", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-percent")]
    public int? Percent { get; init; }

    /// <summary>Gets the base type that percentages are resolved against, or null when there is none.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-percenthint"/>
    [IdlMember("percentHint", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumerictype-percenthint")]
    public CssNumericBaseType? PercentHint { get; init; }
}

/// <summary>
/// The type of a numeric value (https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue-type): the exponent of each
/// base type, in the order of <see cref="CssNumericBaseType"/>, and the percent hint.
/// </summary>
internal readonly struct NumericType : IEquatable<NumericType>
{
    private const int BaseTypeCount = 7;
    private readonly int[]? _exponents;

    private NumericType(int[] exponents, CssNumericBaseType? percentHint)
    {
        _exponents = exponents;
        PercentHint = percentHint;
    }

    public CssNumericBaseType? PercentHint { get; }

    public int this[CssNumericBaseType baseType] => _exponents?[(int)baseType] ?? 0;

    /// <summary>The type of a number: no base type.</summary>
    public static NumericType Number => new(new int[BaseTypeCount], percentHint: null);

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue-create-a-type</summary>
    public static NumericType? FromUnit(string unit)
    {
        if (unit == "number")
            return Number;

        if (CssUnits.GetBaseType(unit) is not { } baseType)
            return null;

        return FromBaseType(baseType, 1);
    }

    public static NumericType FromBaseType(CssNumericBaseType baseType, int exponent)
    {
        var exponents = new int[BaseTypeCount];
        exponents[(int)baseType] = exponent;
        return new NumericType(exponents, percentHint: null);
    }

    /// <summary>Whether the type has no non-zero entry and no percent hint (a &lt;number&gt;).</summary>
    public bool IsNumber => IsOnly(baseType: null) && PercentHint is null;

    /// <summary>
    /// https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue-match: whether the only non-zero entry is the base type
    /// with an exponent of 1 (no entry for a null base type: a &lt;number&gt;), whatever the percent hint.
    /// </summary>
    public bool IsOnly(CssNumericBaseType? baseType)
    {
        for (var i = 0; i < BaseTypeCount; i++)
        {
            var expected = baseType is { } type && (int)type == i ? 1 : 0;
            if (this[(CssNumericBaseType)i] != expected)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the type matches a production of a base type (e.g. &lt;length&gt;): its only non-zero entry is the base type, and its
    /// percent hint is null, or is the base type when <paramref name="allowPercentages"/> is true (e.g. &lt;length-percentage&gt;).
    /// </summary>
    public bool Matches(CssNumericBaseType baseType, bool allowPercentages = false)
    {
        if (!IsOnly(baseType))
            return false;

        return PercentHint is null || (allowPercentages && PercentHint == baseType);
    }

    /// <summary>Whether the type matches &lt;length-percentage&gt; (or another base type with percentages).</summary>
    public bool MatchesWithPercentage(CssNumericBaseType baseType) => Matches(baseType, allowPercentages: true) || (IsOnly(CssNumericBaseType.Percent) && PercentHint is null or CssNumericBaseType.Percent);

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue-add-two-types</summary>
    public static NumericType? Add(NumericType type1, NumericType type2)
    {
        if (type1.PercentHint is { } hint1 && type2.PercentHint is { } hint2 && hint1 != hint2)
            return null;

        if (type1.PercentHint is { } hint && type2.PercentHint is null)
        {
            type2 = type2.ApplyPercentHint(hint);
        }
        else if (type2.PercentHint is { } otherHint && type1.PercentHint is null)
        {
            type1 = type1.ApplyPercentHint(otherHint);
        }

        if (type1.HasSameEntries(type2))
            return new NumericType(type1.CopyExponents(), type1.PercentHint);

        var hasPercent = type1[CssNumericBaseType.Percent] != 0 || type2[CssNumericBaseType.Percent] != 0;
        var hasOther = type1.HasNonZeroEntryOtherThanPercent() || type2.HasNonZeroEntryOtherThanPercent();
        if (hasPercent && hasOther)
        {
            for (var i = 0; i < BaseTypeCount; i++)
            {
                var baseType = (CssNumericBaseType)i;
                if (baseType == CssNumericBaseType.Percent)
                    continue;

                // Provisionally apply the percent hint to both types
                var provisional1 = type1.ApplyPercentHint(baseType);
                var provisional2 = type2.ApplyPercentHint(baseType);
                if (provisional1.HasSameEntries(provisional2))
                    return new NumericType(provisional1.CopyExponents(), baseType);
            }
        }

        return null;
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue-multiply-two-types</summary>
    public static NumericType? Multiply(NumericType type1, NumericType type2)
    {
        if (type1.PercentHint is { } hint1 && type2.PercentHint is { } hint2 && hint1 != hint2)
            return null;

        if (type1.PercentHint is { } hint && type2.PercentHint is null)
        {
            type2 = type2.ApplyPercentHint(hint);
        }
        else if (type2.PercentHint is { } otherHint && type1.PercentHint is null)
        {
            type1 = type1.ApplyPercentHint(otherHint);
        }

        var exponents = new int[BaseTypeCount];
        for (var i = 0; i < BaseTypeCount; i++)
        {
            exponents[i] = type1[(CssNumericBaseType)i] + type2[(CssNumericBaseType)i];
        }

        return new NumericType(exponents, type1.PercentHint);
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue-invert-a-type</summary>
    public NumericType Invert()
    {
        var exponents = new int[BaseTypeCount];
        for (var i = 0; i < BaseTypeCount; i++)
        {
            exponents[i] = -this[(CssNumericBaseType)i];
        }

        return new NumericType(exponents, PercentHint);
    }

    /// <summary>Returns the type with each exponent multiplied by <paramref name="power"/> (the type of a unit map entry).</summary>
    public NumericType Power(int power)
    {
        var exponents = new int[BaseTypeCount];
        for (var i = 0; i < BaseTypeCount; i++)
        {
            exponents[i] = this[(CssNumericBaseType)i] * power;
        }

        return new NumericType(exponents, PercentHint);
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#apply-the-percent-hint</summary>
    private NumericType ApplyPercentHint(CssNumericBaseType hint)
    {
        var exponents = CopyExponents();
        if (hint != CssNumericBaseType.Percent)
        {
            exponents[(int)hint] += exponents[(int)CssNumericBaseType.Percent];
            exponents[(int)CssNumericBaseType.Percent] = 0;
        }

        return new NumericType(exponents, hint);
    }

    private int[] CopyExponents()
    {
        var exponents = new int[BaseTypeCount];
        _exponents?.CopyTo(exponents, 0);
        return exponents;
    }

    private bool HasSameEntries(NumericType other)
    {
        for (var i = 0; i < BaseTypeCount; i++)
        {
            if (this[(CssNumericBaseType)i] != other[(CssNumericBaseType)i])
                return false;
        }

        return true;
    }

    private bool HasNonZeroEntryOtherThanPercent()
    {
        for (var i = 0; i < BaseTypeCount; i++)
        {
            if (i != (int)CssNumericBaseType.Percent && this[(CssNumericBaseType)i] != 0)
                return true;
        }

        return false;
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-type</summary>
    public CssNumericType ToDictionary()
    {
        var type = this;
        int? Get(CssNumericBaseType baseType) => type[baseType] is var power and not 0 ? power : null;
        return new CssNumericType
        {
            Length = Get(CssNumericBaseType.Length),
            Angle = Get(CssNumericBaseType.Angle),
            Time = Get(CssNumericBaseType.Time),
            Frequency = Get(CssNumericBaseType.Frequency),
            Resolution = Get(CssNumericBaseType.Resolution),
            Flex = Get(CssNumericBaseType.Flex),
            Percent = Get(CssNumericBaseType.Percent),
            PercentHint = PercentHint,
        };
    }

    public bool Equals(NumericType other) => HasSameEntries(other) && PercentHint == other.PercentHint;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is NumericType other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (var i = 0; i < BaseTypeCount; i++)
        {
            hash.Add(this[(CssNumericBaseType)i]);
        }

        hash.Add(PercentHint);
        return hash.ToHashCode();
    }
}

/// <summary>The units of the numeric values of the Typed OM (https://drafts.css-houdini.org/css-typed-om-1/#numeric-factory).</summary>
internal static class CssUnits
{
    /// <summary>Returns the base type of a unit (ASCII lowercase), or null when it is not a unit of a dimension ("number" and "percent" are not dimensions).</summary>
    public static CssNumericBaseType? GetBaseType(string unit)
    {
        switch (unit)
        {
            case "percent":
                return CssNumericBaseType.Percent;
            case "deg" or "grad" or "rad" or "turn":
                return CssNumericBaseType.Angle;
            case "s" or "ms":
                return CssNumericBaseType.Time;
            case "hz" or "khz":
                return CssNumericBaseType.Frequency;
            case "dpi" or "dpcm" or "dppx" or "x":
                return CssNumericBaseType.Resolution;
            case "fr":
                return CssNumericBaseType.Flex;
            case "cm" or "mm" or "q" or "in" or "pt" or "pc" or "px":
                return CssNumericBaseType.Length;
        }

        return CssCalc.IsRelativeUnit(unit) ? CssNumericBaseType.Length : null;
    }

    /// <summary>Returns whether the string is a valid unit ("number", "percent" or a dimension unit), ASCII case-insensitively.</summary>
    public static bool IsValid(string unit) => NumericType.FromUnit(InfraAscii.ToAsciiLowercase(unit)) is not null;

    /// <summary>
    /// Converts a value to the canonical unit of its set of compatible units (https://drafts.csswg.org/css-values-4/#compatible-units),
    /// e.g. 1in to 96px. The other units (relative lengths, "number", "percent") are returned as is.
    /// </summary>
    public static (double Value, string Unit) ToCanonical(double value, string unit)
    {
        if (CssCalc.TryConvertToCanonicalUnit(value, unit, out var result, out var canonicalUnit))
            return (result, canonicalUnit);

        return (value, unit);
    }

    /// <summary>Converts a value to a compatible unit; returns null when the units are not compatible.</summary>
    public static double? Convert(double value, string unit, string targetUnit)
    {
        if (unit == targetUnit)
            return value;

        var (canonicalValue, canonicalUnit) = ToCanonical(value, unit);
        var (ratio, targetCanonicalUnit) = ToCanonical(1, targetUnit);
        if (canonicalUnit != targetCanonicalUnit)
            return null;

        return canonicalValue / ratio;
    }

    /// <summary>Returns whether two units are compatible units (https://drafts.csswg.org/css-values-4/#compatible-units).</summary>
    public static bool AreCompatible(string unit1, string unit2) => Convert(1, unit1, unit2) is not null;
}
