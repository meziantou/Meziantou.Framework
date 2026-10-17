using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.TypedOm;

/// <summary>The CSSNumberish typedef: (double or CSSNumericValue) (https://drafts.css-houdini.org/css-typed-om-1/#typedefdef-cssnumberish).</summary>
public readonly struct CssNumberish : IUnionValue, IEquatable<CssNumberish>
{
    private CssNumberish(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="double"/> or a <see cref="CssNumericValue"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Converts a <c>double</c> to a <c>CssNumberish</c>.</summary>
    [NotInIdl]
    public static implicit operator CssNumberish(double value) => new(value);

    /// <summary>Converts a <c>CssNumericValue</c> to a <c>CssNumberish</c>.</summary>
    [NotInIdl]
    public static implicit operator CssNumberish(CssNumericValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a <c>CssNumberish</c> from a <c>double</c>.</summary>
    [NotInIdl]
    public static CssNumberish FromDouble(double value) => value;

    /// <summary>Creates a <c>CssNumberish</c> from a <c>CssNumericValue</c>.</summary>
    [NotInIdl]
    public static CssNumberish FromCssNumericValue(CssNumericValue value) => value;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#rectify-a-numberish-value</summary>
    internal CssNumericValue Rectify(string unit = "number") => Value switch
    {
        CssNumericValue numeric => numeric,
        double number => new CssUnitValue(number, unit),
        _ => new CssUnitValue(0, unit),
    };

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(CssNumberish other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CssNumberish other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two <c>CssNumberish</c> values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(CssNumberish left, CssNumberish right) => left.Equals(right);

    /// <summary>Determines whether two <c>CssNumberish</c> values are not equal.</summary>
    [NotInIdl]
    public static bool operator !=(CssNumberish left, CssNumberish right) => !left.Equals(right);
}

/// <summary>
/// A numeric value: a &lt;number&gt;, a &lt;percentage&gt;, a &lt;dimension&gt; or a math expression
/// (https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue).
/// </summary>
[IdlInterface("CSSNumericValue", "https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue")]
public abstract class CssNumericValue : CssStyleValue
{
    private protected CssNumericValue(NumericType type)
    {
        NumericType = type;
    }

    /// <summary>The type of the value (https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue-type).</summary>
    internal NumericType NumericType { get; }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-add</summary>
    [IdlMember("add", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-add")]
    public CssNumericValue Add(params ReadOnlySpan<CssNumberish> values) => AddCore(Rectify(values));

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-sub</summary>
    [IdlMember("sub", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-sub")]
    public CssNumericValue Sub(params ReadOnlySpan<CssNumberish> values)
    {
        var items = Rectify(values);
        for (var i = 0; i < items.Count; i++)
        {
            items[i] = items[i].Negate();
        }

        return AddCore(items);
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-mul</summary>
    [IdlMember("mul", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-mul")]
    public CssNumericValue Mul(params ReadOnlySpan<CssNumberish> values) => MultiplyCore(Rectify(values));

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-div</summary>
    [IdlMember("div", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-div")]
    public CssNumericValue Div(params ReadOnlySpan<CssNumberish> values)
    {
        var items = Rectify(values);
        for (var i = 0; i < items.Count; i++)
        {
            items[i] = items[i].Invert();
        }

        return MultiplyCore(items);
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-min</summary>
    [IdlMember("min", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-min")]
    public CssNumericValue Min(params ReadOnlySpan<CssNumberish> values)
    {
        var items = Prepend<CssMathMin>(Rectify(values));
        if (TryGetCommonUnit(items, out var unit))
            return new CssUnitValue(items.Min(item => ((CssUnitValue)item).Value), unit);

        return new CssMathMin(items);
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-max</summary>
    [IdlMember("max", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-max")]
    public CssNumericValue Max(params ReadOnlySpan<CssNumberish> values)
    {
        var items = Prepend<CssMathMax>(Rectify(values));
        if (TryGetCommonUnit(items, out var unit))
            return new CssUnitValue(items.Max(item => ((CssUnitValue)item).Value), unit);

        return new CssMathMax(items);
    }

    /// <summary>
    /// Returns whether the values are equal numeric values to this value (https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-equals):
    /// the same classes, units and values, in the same order.
    /// </summary>
    [IdlMember("equals", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-equals")]
    public bool NumericEquals(params ReadOnlySpan<CssNumberish> value)
    {
        foreach (var item in value)
        {
            if (!AreEqual(this, item.Rectify()))
                return false;
        }

        return true;
    }

    /// <summary>Converts the value to a unit (https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-to).</summary>
    [IdlMember("to", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-to")]
    public CssUnitValue To(string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        unit = InfraAscii.ToAsciiLowercase(unit);
        if (NumericType.FromUnit(unit) is null)
            throw DomException.Syntax($"'{unit}' is not a valid unit");

        var sum = SumValue.Create(this) ?? throw new TypeErrorException("The value cannot be converted to a sum of units");
        if (sum.Count != 1)
            throw new TypeErrorException("The value contains more than one unit");

        var item = sum[0].ToUnitValue() ?? throw new TypeErrorException("The value has a compound unit");
        return item.ConvertTo(unit) ?? throw new TypeErrorException($"The value cannot be converted to '{unit}'");
    }

    /// <summary>Converts the value to a sum of values of the units (https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-tosum).</summary>
    [IdlMember("toSum", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-tosum")]
    public CssMathSum ToSum(params ReadOnlySpan<string> units)
    {
        var lowercaseUnits = new List<string>(units.Length);
        foreach (var unit in units)
        {
            if (unit is null)
                throw new ArgumentException("The units must not be null", nameof(units));

            var lowercase = InfraAscii.ToAsciiLowercase(unit);
            if (NumericType.FromUnit(lowercase) is null)
                throw DomException.Syntax($"'{unit}' is not a valid unit");

            lowercaseUnits.Add(lowercase);
        }

        var sum = SumValue.Create(this) ?? throw new TypeErrorException("The value cannot be converted to a sum of units");
        var values = new List<CssUnitValue>(sum.Count);
        foreach (var item in sum)
        {
            values.Add(item.ToUnitValue() ?? throw new TypeErrorException("The value has a compound unit"));
        }

        if (lowercaseUnits.Count == 0)
        {
            values.Sort((a, b) => string.CompareOrdinal(a.Unit, b.Unit));
            return new CssMathSum(values.ConvertAll(CssNumericValue (v) => v));
        }

        var result = new List<CssNumericValue>(lowercaseUnits.Count);
        foreach (var unit in lowercaseUnits)
        {
            var temp = 0d;
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i].ConvertTo(unit) is { } converted)
                {
                    temp += converted.Value;
                    values.RemoveAt(i);
                    i--;
                }
            }

            result.Add(new CssUnitValue(temp, unit));
        }

        if (values.Count > 0)
            throw new TypeErrorException("The value contains units that were not requested");

        return new CssMathSum(result);
    }

    /// <summary>Returns the type of the value (https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-type).</summary>
    [IdlMember("type", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-type")]
    public CssNumericType Type() => NumericType.ToDictionary();

    /// <summary>Parses a numeric value (https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-parse).</summary>
    [IdlMember("parse", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-parse")]
    public static CssNumericValue Parse(string cssText)
    {
        ArgumentNullException.ThrowIfNull(cssText);
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(cssText));
        if (values is not [var value])
            throw DomException.Syntax("The text is not a single component value");

        return CssStyleValueReifier.ReifyNumericValue(value, computed: false, lengthContext: false) ?? throw DomException.Syntax("The text is not a valid numeric value");
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#cssmath-negate-a-cssnumericvalue</summary>
    internal CssNumericValue Negate() => this switch
    {
        CssMathNegate negate => negate.Value,
        CssUnitValue unit => new CssUnitValue(-unit.Value, unit.Unit),
        _ => new CssMathNegate(this),
    };

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#cssmath-invert-a-cssnumericvalue</summary>
    internal CssNumericValue Invert()
    {
        if (this is CssMathInvert invert)
            return invert.Value;

        if (this is CssUnitValue { Unit: "number" } number)
        {
            if (number.Value == 0)
                throw new RangeErrorException("Cannot invert zero");

            return new CssUnitValue(1 / number.Value, "number");
        }

        return new CssMathInvert(this);
    }

    /// <summary>Returns whether two values are equal numeric values (https://drafts.css-houdini.org/css-typed-om-1/#equal-numeric-value).</summary>
    internal static bool AreEqual(CssNumericValue value1, CssNumericValue value2)
    {
        if (value1.GetType() != value2.GetType())
            return false;

        switch (value1)
        {
            case CssUnitValue unit1:
            {
                var unit2 = (CssUnitValue)value2;
                return unit1.Unit == unit2.Unit && unit1.Value == unit2.Value;
            }

            case CssMathValue { Operands: { } items1 }:
            {
                var items2 = ((CssMathValue)value2).Operands!;
                if (items1.Count != items2.Count)
                    return false;

                for (var i = 0; i < items1.Count; i++)
                {
                    if (!AreEqual(items1[i], items2[i]))
                        return false;
                }

                return true;
            }

            case CssMathClamp clamp1:
            {
                var clamp2 = (CssMathClamp)value2;
                return AreEqual(clamp1.Lower, clamp2.Lower) && AreEqual(clamp1.Value, clamp2.Value) && AreEqual(clamp1.Upper, clamp2.Upper);
            }

            case CssMathNegate negate1:
                return AreEqual(negate1.Value, ((CssMathNegate)value2).Value);

            case CssMathInvert invert1:
                return AreEqual(invert1.Value, ((CssMathInvert)value2).Value);

            default:
                return false;
        }
    }

    private static List<CssNumericValue> Rectify(ReadOnlySpan<CssNumberish> values)
    {
        var result = new List<CssNumericValue>(values.Length + 1);
        foreach (var value in values)
        {
            result.Add(value.Rectify());
        }

        return result;
    }

    /// <summary>Prepends this value to the values (or its values when it is a math value of the same operation).</summary>
    private List<CssNumericValue> Prepend<T>(List<CssNumericValue> values)
        where T : CssMathValue
    {
        if (this is T { Operands: { } operands })
        {
            values.InsertRange(0, operands);
        }
        else
        {
            values.Insert(0, this);
        }

        return values;
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-add (after the values are rectified)</summary>
    private CssNumericValue AddCore(List<CssNumericValue> values)
    {
        values = Prepend<CssMathSum>(values);
        if (TryGetCommonUnit(values, out var unit))
        {
            // The addition is done left to right
            var sum = 0d;
            foreach (var value in values)
            {
                sum += ((CssUnitValue)value).Value;
            }

            return new CssUnitValue(sum, unit);
        }

        return new CssMathSum(values);
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericvalue-mul (after the values are rectified)</summary>
    private CssNumericValue MultiplyCore(List<CssNumericValue> values)
    {
        values = Prepend<CssMathProduct>(values);

        // All the values are numbers, except at most one
        string? unit = null;
        var product = 1d;
        foreach (var value in values)
        {
            if (value is not CssUnitValue unitValue)
                return new CssMathProduct(values);

            if (unitValue.Unit != "number")
            {
                if (unit is not null)
                    return new CssMathProduct(values);

                unit = unitValue.Unit;
            }

            product *= unitValue.Value;
        }

        return new CssUnitValue(product, unit ?? "number");
    }

    private static bool TryGetCommonUnit(List<CssNumericValue> values, out string unit)
    {
        unit = "";
        foreach (var value in values)
        {
            if (value is not CssUnitValue unitValue || (unit.Length > 0 && unitValue.Unit != unit))
                return false;

            unit = unitValue.Unit;
        }

        return unit.Length > 0;
    }
}

/// <summary>
/// A value with a single unit, a number or a percentage (https://drafts.css-houdini.org/css-typed-om-1/#cssunitvalue), e.g.
/// 42px (the unit is "px"), 10 (the unit is "number") or 50% (the unit is "percent").
/// </summary>
[IdlInterface("CSSUnitValue", "https://drafts.css-houdini.org/css-typed-om-1/#cssunitvalue")]
public sealed class CssUnitValue : CssNumericValue
{
    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssunitvalue-cssunitvalue</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssunitvalue-cssunitvalue")]
    public CssUnitValue(double value, string unit)
        : base(GetUnitType(unit))
    {
        Value = value;
        Unit = InfraAscii.ToAsciiLowercase(unit);
    }

    /// <summary>Gets or sets the numeric value, without its unit.</summary>
    [IdlMember("value", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssunitvalue-value")]
    public double Value { get; set; }

    /// <summary>The unit: "number", "percent" or the unit of a dimension (ASCII lowercase, e.g. "px", "q", "hz").</summary>
    [IdlMember("unit", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssunitvalue-unit")]
    public string Unit { get; }

    private static NumericType GetUnitType(string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return NumericType.FromUnit(InfraAscii.ToAsciiLowercase(unit)) ?? throw new TypeErrorException($"'{unit}' is not a valid unit");
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#convert-a-cssunitvalue: returns null when the units are not compatible.</summary>
    internal CssUnitValue? ConvertTo(string unit)
    {
        if (CssUnits.Convert(Value, Unit, unit) is not { } value)
            return null;

        return new CssUnitValue(value, unit);
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#cssunitvalue-serialization</summary>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => Serialize(minimum: null);

    /// <summary>
    /// https://drafts.css-houdini.org/css-typed-om-1/#cssunitvalue-serialization: the value is wrapped in calc() when it is less
    /// than the minimum (or when the comparison is not possible, e.g. a relative length).
    /// </summary>
    internal string Serialize(CssUnitValue? minimum)
    {
        var text = CssSerializer.SerializeNumber(Value) + Unit switch
        {
            "number" => "",
            "percent" => "%",
            _ => Unit,
        };

        if (minimum is not null && (CssUnits.Convert(Value, Unit, minimum.Unit) is not { } converted || converted < minimum.Value))
            return "calc(" + text + ")";

        return text;
    }
}

/// <summary>
/// A sum value (https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue-sum-value): a list of numbers with units,
/// each unit with a power (e.g. calc(1px + 2em) is «(1, «[px → 1]»), (2, «[em → 1]»)»).
/// </summary>
internal sealed class SumValue : List<SumValueItem>
{
    private SumValue()
    {
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#create-a-sum-value: returns null on failure.</summary>
    public static SumValue? Create(CssNumericValue value)
    {
        switch (value)
        {
            case CssUnitValue unit:
            {
                var (canonicalValue, canonicalUnit) = CssUnits.ToCanonical(unit.Value, unit.Unit);
                var units = new SortedDictionary<string, int>(StringComparer.Ordinal);
                if (canonicalUnit != "number")
                {
                    units[canonicalUnit] = 1;
                }

                return [new SumValueItem(canonicalValue, units)];
            }

            case CssMathSum sum:
            {
                var values = new SumValue();
                foreach (var item in sum.Values)
                {
                    if (Create(item) is not { } itemValues)
                        return null;

                    foreach (var subvalue in itemValues)
                    {
                        var index = values.FindIndex(v => HaveSameUnits(v.Units, subvalue.Units));
                        if (index >= 0)
                        {
                            values[index] = values[index] with { Value = values[index].Value + subvalue.Value };
                        }
                        else
                        {
                            values.Add(subvalue);
                        }
                    }
                }

                // The types of the items must be addable
                NumericType? type = null;
                foreach (var item in values)
                {
                    var itemType = CreateType(item.Units);
                    type = type is null ? itemType : NumericType.Add(type.Value, itemType);
                    if (type is null)
                        return null;
                }

                return values;
            }

            case CssMathNegate negate:
            {
                if (Create(negate.Value) is not { } values)
                    return null;

                return [.. values.Select(v => v with { Value = -v.Value })];
            }

            case CssMathProduct product:
            {
                SumValue values = [new SumValueItem(1, new SortedDictionary<string, int>(StringComparer.Ordinal))];
                foreach (var item in product.Values)
                {
                    if (Create(item) is not { } newValues)
                        return null;

                    var temp = new SumValue();
                    foreach (var item1 in values)
                    {
                        foreach (var item2 in newValues)
                        {
                            var units = new SortedDictionary<string, int>(item1.Units, StringComparer.Ordinal);
                            foreach (var (unit, power) in item2.Units)
                            {
                                units[unit] = units.GetValueOrDefault(unit) + power;
                                if (units[unit] == 0)
                                {
                                    units.Remove(unit);
                                }
                            }

                            temp.Add(new SumValueItem(item1.Value * item2.Value, units));
                        }
                    }

                    values = temp;
                }

                return values;
            }

            case CssMathInvert invert:
            {
                if (Create(invert.Value) is not { Count: 1 } values)
                    return null;

                var item = values[0];
                var units = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var (unit, power) in item.Units)
                {
                    units[unit] = -power;
                }

                return [new SumValueItem(1 / item.Value, units)];
            }

            case CssMathMin or CssMathMax:
            {
                var args = new List<SumValueItem>();
                foreach (var item in ((CssMathValue)value).Operands!)
                {
                    if (Create(item) is not { Count: 1 } itemValues)
                        return null;

                    if (args.Count > 0 && !HaveSameUnits(args[0].Units, itemValues[0].Units))
                        return null;

                    args.Add(itemValues[0]);
                }

                var selected = value is CssMathMin ? args.MinBy(a => a.Value) : args.MaxBy(a => a.Value);
                return selected is null ? null : [selected];
            }

            case CssMathClamp clamp:
            {
                if (Create(clamp.Lower) is not [var lower] || Create(clamp.Value) is not [var center] || Create(clamp.Upper) is not [var upper])
                    return null;

                if (!HaveSameUnits(lower.Units, center.Units) || !HaveSameUnits(lower.Units, upper.Units))
                    return null;

                return [center with { Value = Math.Max(lower.Value, Math.Min(center.Value, upper.Value)) }];
            }

            default:
                return null;
        }
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#create-a-type-from-a-unit-map</summary>
    private static NumericType CreateType(SortedDictionary<string, int> units)
    {
        var result = NumericType.Number;
        foreach (var (unit, power) in units)
        {
            var type = NumericType.FromUnit(unit)!.Value.Power(power);
            result = NumericType.Multiply(result, type) ?? result;
        }

        return result;
    }

    private static bool HaveSameUnits(SortedDictionary<string, int> units1, SortedDictionary<string, int> units2)
    {
        if (units1.Count != units2.Count)
            return false;

        foreach (var (unit, power) in units1)
        {
            if (!units2.TryGetValue(unit, out var otherPower) || otherPower != power)
                return false;
        }

        return true;
    }
}

/// <summary>An item of a sum value: a number and a map of units to their power.</summary>
internal sealed record SumValueItem(double Value, SortedDictionary<string, int> Units)
{
    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#create-a-cssunitvalue-from-a-sum-value-item: returns null on failure.</summary>
    public CssUnitValue? ToUnitValue()
    {
        if (Units.Count > 1)
            return null;

        if (Units.Count == 0)
            return new CssUnitValue(Value, "number");

        var (unit, power) = Units.First();
        return power == 1 ? new CssUnitValue(Value, unit) : null;
    }
}
