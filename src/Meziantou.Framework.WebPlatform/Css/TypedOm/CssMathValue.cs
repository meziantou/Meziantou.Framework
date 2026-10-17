using System.Collections;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.TypedOm;

/// <summary>https://drafts.css-houdini.org/css-typed-om-1/#enumdef-cssmathoperator</summary>
[IdlInterface("CSSMathOperator", "https://drafts.css-houdini.org/css-typed-om-1/#enumdef-cssmathoperator")]
public enum CssMathOperator
{
    /// <summary>The operator of a sum (<c>calc(a + b)</c>).</summary>
    [IdlMember("sum", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathoperator-sum")]
    Sum,

    /// <summary>The operator of a product (<c>calc(a * b)</c>).</summary>
    [IdlMember("product", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathoperator-product")]
    Product,

    /// <summary>The operator of a negation.</summary>
    [IdlMember("negate", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathoperator-negate")]
    Negate,

    /// <summary>The operator of an inversion (<c>calc(1 / a)</c>).</summary>
    [IdlMember("invert", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathoperator-invert")]
    Invert,

    /// <summary>The operator of a <c>min()</c> expression.</summary>
    [IdlMember("min", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathoperator-min")]
    Min,

    /// <summary>The operator of a <c>max()</c> expression.</summary>
    [IdlMember("max", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathoperator-max")]
    Max,

    /// <summary>The operator of a <c>clamp()</c> expression.</summary>
    [IdlMember("clamp", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathoperator-clamp")]
    Clamp,
}

/// <summary>
/// A math expression (https://drafts.css-houdini.org/css-typed-om-1/#cssmathvalue): a tree of math values whose leaves are
/// <see cref="CssUnitValue"/> objects, e.g. calc(1em + 5px) is a <see cref="CssMathSum"/> of 1em and 5px.
/// </summary>
[IdlInterface("CSSMathValue", "https://drafts.css-houdini.org/css-typed-om-1/#cssmathvalue")]
public abstract class CssMathValue : CssNumericValue
{
    private protected CssMathValue(CssMathOperator @operator, NumericType type)
        : base(type)
    {
        Operator = @operator;
    }

    /// <summary>Gets the operator of the math expression.</summary>
    [IdlMember("operator", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathvalue-operator")]
    public CssMathOperator Operator { get; }

    /// <summary>The values of a sum, a product, a min() or a max(); null for the other math values.</summary>
    internal virtual IReadOnlyList<CssNumericValue>? Operands => null;

    /// <summary>Rectifies the arguments of a constructor (https://drafts.css-houdini.org/css-typed-om-1/#rectify-a-numberish-value).</summary>
    private protected static List<CssNumericValue> RectifyArguments(ReadOnlySpan<CssNumberish> args)
    {
        var result = new List<CssNumericValue>(args.Length);
        foreach (var arg in args)
        {
            result.Add(arg.Rectify());
        }

        return result;
    }

    /// <summary>Returns the sum of the types of the values (https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue-add-two-types), or throws a TypeError.</summary>
    private protected static NumericType AddTypes(IReadOnlyList<CssNumericValue> values)
    {
        if (values.Count == 0)
            throw DomException.Syntax("At least one value is required");

        var type = values[0].NumericType;
        for (var i = 1; i < values.Count; i++)
        {
            type = NumericType.Add(type, values[i].NumericType) ?? throw new TypeErrorException("The types of the values cannot be added");
        }

        return type;
    }

    /// <summary>Returns the product of the types of the values (https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue-multiply-two-types), or throws a TypeError.</summary>
    private protected static NumericType MultiplyTypes(IReadOnlyList<CssNumericValue> values)
    {
        if (values.Count == 0)
            throw DomException.Syntax("At least one value is required");

        var type = values[0].NumericType;
        for (var i = 1; i < values.Count; i++)
        {
            type = NumericType.Multiply(type, values[i].NumericType) ?? throw new TypeErrorException("The types of the values cannot be multiplied");
        }

        return type;
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#cssmathvalue-serialization</summary>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString()
    {
        var builder = new StringBuilder();
        Serialize(builder, this, nested: false, parenLess: false);
        return builder.ToString();
    }

    /// <summary>Serializes a numeric value (https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue-serialization).</summary>
    private static void Serialize(StringBuilder builder, CssNumericValue value, bool nested, bool parenLess)
    {
        switch (value)
        {
            case CssUnitValue unit:
                builder.Append(unit.ToString());
                break;

            case CssMathMin or CssMathMax or CssMathClamp:
            {
                var math = (CssMathValue)value;
                builder.Append(math.Operator switch
                {
                    CssMathOperator.Min => "min(",
                    CssMathOperator.Max => "max(",
                    _ => "clamp(",
                });

                var operands = math.Operands ?? (value is CssMathClamp clamp ? [clamp.Lower, clamp.Value, clamp.Upper] : []);
                for (var i = 0; i < operands.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    Serialize(builder, operands[i], nested: true, parenLess: true);
                }

                builder.Append(')');
                break;
            }

            case CssMathSum or CssMathProduct:
            {
                var isSum = value is CssMathSum;
                OpenParenthesis(builder, nested, parenLess);
                var operands = ((CssMathValue)value).Operands!;
                Serialize(builder, operands[0], nested: true, parenLess: false);
                for (var i = 1; i < operands.Count; i++)
                {
                    var operand = operands[i];
                    if (isSum && operand is CssMathNegate negate)
                    {
                        builder.Append(" - ");
                        Serialize(builder, negate.Value, nested: true, parenLess: false);
                    }
                    else if (!isSum && operand is CssMathInvert invert)
                    {
                        builder.Append(" / ");
                        Serialize(builder, invert.Value, nested: true, parenLess: false);
                    }
                    else
                    {
                        builder.Append(isSum ? " + " : " * ");
                        Serialize(builder, operand, nested: true, parenLess: false);
                    }
                }

                CloseParenthesis(builder, parenLess);
                break;
            }

            case CssMathNegate negateValue:
                OpenParenthesis(builder, nested, parenLess);
                builder.Append('-');
                Serialize(builder, negateValue.Value, nested: true, parenLess: false);
                CloseParenthesis(builder, parenLess);
                break;

            case CssMathInvert invertValue:
                OpenParenthesis(builder, nested, parenLess);
                builder.Append("1 / ");
                Serialize(builder, invertValue.Value, nested: true, parenLess: false);
                CloseParenthesis(builder, parenLess);
                break;
        }

        static void OpenParenthesis(StringBuilder builder, bool nested, bool parenLess)
        {
            if (!parenLess)
            {
                builder.Append(nested ? "(" : "calc(");
            }
        }

        static void CloseParenthesis(StringBuilder builder, bool parenLess)
        {
            if (!parenLess)
            {
                builder.Append(')');
            }
        }
    }
}

/// <summary>A sum of values (https://drafts.css-houdini.org/css-typed-om-1/#cssmathsum), e.g. calc(1em + 5px).</summary>
[IdlInterface("CSSMathSum", "https://drafts.css-houdini.org/css-typed-om-1/#cssmathsum")]
public sealed class CssMathSum : CssMathValue
{
    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathsum-cssmathsum</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathsum-cssmathsum")]
    public CssMathSum(params ReadOnlySpan<CssNumberish> args)
        : this(RectifyArguments(args))
    {
    }

    internal CssMathSum(List<CssNumericValue> values)
        : base(CssMathOperator.Sum, AddTypes(values))
    {
        Values = new CssNumericArray(values);
    }

    /// <summary>Gets the terms of the sum.</summary>
    [IdlMember("values", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathsum-values")]
    public CssNumericArray Values { get; }

    internal override IReadOnlyList<CssNumericValue> Operands => Values;
}

/// <summary>A product of values (https://drafts.css-houdini.org/css-typed-om-1/#cssmathproduct), e.g. calc(2 * 1em).</summary>
[IdlInterface("CSSMathProduct", "https://drafts.css-houdini.org/css-typed-om-1/#cssmathproduct")]
public sealed class CssMathProduct : CssMathValue
{
    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathproduct-cssmathproduct</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathproduct-cssmathproduct")]
    public CssMathProduct(params ReadOnlySpan<CssNumberish> args)
        : this(RectifyArguments(args))
    {
    }

    internal CssMathProduct(List<CssNumericValue> values)
        : base(CssMathOperator.Product, MultiplyTypes(values))
    {
        Values = new CssNumericArray(values);
    }

    /// <summary>Gets the factors of the product.</summary>
    [IdlMember("values", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathproduct-values")]
    public CssNumericArray Values { get; }

    internal override IReadOnlyList<CssNumericValue> Operands => Values;
}

/// <summary>The negation of a value (https://drafts.css-houdini.org/css-typed-om-1/#cssmathnegate), e.g. the right operand of a subtraction.</summary>
[IdlInterface("CSSMathNegate", "https://drafts.css-houdini.org/css-typed-om-1/#cssmathnegate")]
public sealed class CssMathNegate : CssMathValue
{
    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathnegate-cssmathnegate</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathnegate-cssmathnegate")]
    public CssMathNegate(CssNumberish arg)
        : this(arg.Rectify())
    {
    }

    internal CssMathNegate(CssNumericValue value)
        : base(CssMathOperator.Negate, value.NumericType)
    {
        Value = value;
    }

    /// <summary>Gets the negated value.</summary>
    [IdlMember("value", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathnegate-value")]
    public CssNumericValue Value { get; }
}

/// <summary>The inverse of a value (https://drafts.css-houdini.org/css-typed-om-1/#cssmathinvert), e.g. the right operand of a division.</summary>
[IdlInterface("CSSMathInvert", "https://drafts.css-houdini.org/css-typed-om-1/#cssmathinvert")]
public sealed class CssMathInvert : CssMathValue
{
    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathinvert-cssmathinvert</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathinvert-cssmathinvert")]
    public CssMathInvert(CssNumberish arg)
        : this(arg.Rectify())
    {
    }

    internal CssMathInvert(CssNumericValue value)
        : base(CssMathOperator.Invert, value.NumericType.Invert())
    {
        Value = value;
    }

    /// <summary>Gets the inverted value.</summary>
    [IdlMember("value", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathinvert-value")]
    public CssNumericValue Value { get; }
}

/// <summary>The min() function (https://drafts.css-houdini.org/css-typed-om-1/#cssmathmin).</summary>
[IdlInterface("CSSMathMin", "https://drafts.css-houdini.org/css-typed-om-1/#cssmathmin")]
public sealed class CssMathMin : CssMathValue
{
    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathmin-cssmathmin</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathmin-cssmathmin")]
    public CssMathMin(params ReadOnlySpan<CssNumberish> args)
        : this(RectifyArguments(args))
    {
    }

    internal CssMathMin(List<CssNumericValue> values)
        : base(CssMathOperator.Min, AddTypes(values))
    {
        Values = new CssNumericArray(values);
    }

    /// <summary>Gets the arguments of the <c>min()</c> expression.</summary>
    [IdlMember("values", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathmin-values")]
    public CssNumericArray Values { get; }

    internal override IReadOnlyList<CssNumericValue> Operands => Values;
}

/// <summary>The max() function (https://drafts.css-houdini.org/css-typed-om-1/#cssmathmax).</summary>
[IdlInterface("CSSMathMax", "https://drafts.css-houdini.org/css-typed-om-1/#cssmathmax")]
public sealed class CssMathMax : CssMathValue
{
    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathmax-cssmathmax</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathmax-cssmathmax")]
    public CssMathMax(params ReadOnlySpan<CssNumberish> args)
        : this(RectifyArguments(args))
    {
    }

    internal CssMathMax(List<CssNumericValue> values)
        : base(CssMathOperator.Max, AddTypes(values))
    {
        Values = new CssNumericArray(values);
    }

    /// <summary>Gets the arguments of the <c>max()</c> expression.</summary>
    [IdlMember("values", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathmax-values")]
    public CssNumericArray Values { get; }

    internal override IReadOnlyList<CssNumericValue> Operands => Values;
}

/// <summary>The clamp() function (https://drafts.css-houdini.org/css-typed-om-1/#cssmathclamp).</summary>
[IdlInterface("CSSMathClamp", "https://drafts.css-houdini.org/css-typed-om-1/#cssmathclamp")]
public sealed class CssMathClamp : CssMathValue
{
    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathclamp-cssmathclamp</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathclamp-cssmathclamp")]
    public CssMathClamp(CssNumberish lower, CssNumberish value, CssNumberish upper)
        : this(lower.Rectify(), value.Rectify(), upper.Rectify())
    {
    }

    internal CssMathClamp(CssNumericValue lower, CssNumericValue value, CssNumericValue upper)
        : base(CssMathOperator.Clamp, AddTypes([lower, value, upper]))
    {
        Lower = lower;
        Value = value;
        Upper = upper;
    }

    /// <summary>Gets the lower bound (the minimum) of the <c>clamp()</c> expression.</summary>
    [IdlMember("lower", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathclamp-lower")]
    public CssNumericValue Lower { get; }

    /// <summary>Gets the preferred value of the <c>clamp()</c> expression.</summary>
    [IdlMember("value", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathclamp-value")]
    public CssNumericValue Value { get; }

    /// <summary>Gets the upper bound (the maximum) of the <c>clamp()</c> expression.</summary>
    [IdlMember("upper", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmathclamp-upper")]
    public CssNumericValue Upper { get; }
}

/// <summary>The read-only list of the values of a math value (https://drafts.css-houdini.org/css-typed-om-1/#cssnumericarray).</summary>
[IdlInterface("CSSNumericArray", "https://drafts.css-houdini.org/css-typed-om-1/#cssnumericarray")]
public sealed class CssNumericArray : IReadOnlyList<CssNumericValue>
{
    private readonly CssNumericValue[] _values;

    internal CssNumericArray(List<CssNumericValue> values)
    {
        _values = [.. values];
    }

    /// <summary>Gets the number of values in the list.</summary>
    [IdlMember("length", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssnumericarray-length")]
    public uint Length => (uint)_values.Length;

    /// <summary>Gets the value at the given index.</summary>
    [IdlMember("getter", "https://drafts.css-houdini.org/css-typed-om-1/#cssnumericarray-indexed-property-getter")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public CssNumericValue this[uint index] => _values[index];

    /// <summary>Gets the value at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public CssNumericValue this[int index] => _values[index];

    /// <summary>Gets the number of values in the list.</summary>
    [NotInIdl]
    public int Count => _values.Length;

    /// <inheritdoc/>
    [NotInIdl]
    public IEnumerator<CssNumericValue> GetEnumerator() => ((IEnumerable<CssNumericValue>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
