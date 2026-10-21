using System.Numerics;
using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>The operators of the language on any values (the interpreter has fast paths for the numbers).</summary>
internal static class Operators
{
    private static readonly JsString UndefinedString = JsString.Create("undefined");
    private static readonly JsString ObjectString = JsString.Create("object");
    private static readonly JsString BooleanString = JsString.Create("boolean");
    private static readonly JsString NumberString = JsString.Create("number");
    private static readonly JsString StringString = JsString.Create("string");
    private static readonly JsString SymbolString = JsString.Create("symbol");
    private static readonly JsString BigIntString = JsString.Create("bigint");
    private static readonly JsString FunctionString = JsString.Create("function");

    public static JsString TypeOf(JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => UndefinedString,
            JsValueKind.Null => ObjectString,
            JsValueKind.Boolean => BooleanString,
            JsValueKind.Number => NumberString,
            JsValueKind.String => StringString,
            JsValueKind.Symbol => SymbolString,
            JsValueKind.BigInt => BigIntString,
            _ => value.AsObject() switch
            {
                { IsHtmlDda: true } => UndefinedString,
                { IsCallable: true } => FunctionString,
                _ => ObjectString,
            },
        };
    }

    /// <summary>ApplyStringOrNumericBinaryOperator for +.</summary>
    public static JsValue Add(JsValue left, JsValue right)
    {
        if (left.Cell is JsString leftString && right.Cell is JsString rightString)
            return Concat(leftString, rightString);

        var leftPrimitive = JsOperations.ToPrimitive(left);
        var rightPrimitive = JsOperations.ToPrimitive(right);
        if (leftPrimitive.IsString || rightPrimitive.IsString)
            return Concat(JsOperations.ToJsString(leftPrimitive), JsOperations.ToJsString(rightPrimitive));

        var leftNumeric = JsOperations.ToNumeric(leftPrimitive);
        var rightNumeric = JsOperations.ToNumeric(rightPrimitive);
        if (leftNumeric.IsNumber && rightNumeric.IsNumber)
            return new JsValue(leftNumeric.NumberValue + rightNumeric.NumberValue);

        if (leftNumeric.IsBigInt && rightNumeric.IsBigInt)
            return JsValue.FromBigInt(leftNumeric.AsBigInt() + rightNumeric.AsBigInt());

        return ThrowHelper.ThrowTypeError<JsValue>("Cannot mix BigInt and other types, use explicit conversions");
    }

    private static JsValue Concat(JsString left, JsString right)
    {
        if (!JsString.CanConcat(left, right))
            return ThrowHelper.ThrowRangeError<JsValue>("Invalid string length");

        return JsValue.FromJsString(JsString.Concat(left, right));
    }

    /// <summary>ApplyStringOrNumericBinaryOperator for the numeric operators.</summary>
    public static JsValue Binary(OpCode op, JsValue left, JsValue right)
    {
        if (left.IsNumber && right.IsNumber)
            return new JsValue(NumberOperation(op, left.NumberValue, right.NumberValue));

        var leftNumeric = JsOperations.ToNumeric(left);
        var rightNumeric = JsOperations.ToNumeric(right);
        if (leftNumeric.IsNumber && rightNumeric.IsNumber)
            return new JsValue(NumberOperation(op, leftNumeric.NumberValue, rightNumeric.NumberValue));

        if (leftNumeric.IsBigInt && rightNumeric.IsBigInt)
            return JsValue.FromBigInt(BigIntOperation(op, leftNumeric.AsBigInt(), rightNumeric.AsBigInt()));

        return ThrowHelper.ThrowTypeError<JsValue>("Cannot mix BigInt and other types, use explicit conversions");
    }

    private static double NumberOperation(OpCode op, double left, double right)
    {
        return op switch
        {
            OpCode.Subtract => left - right,
            OpCode.Multiply => left * right,
            OpCode.Divide => left / right,
            OpCode.Modulo => left % right,
            OpCode.Exponentiate => Exponentiate(left, right),
            OpCode.LeftShift => JsOperations.DoubleToInt32(left) << (int)((uint)JsOperations.DoubleToInt32(right) & 0x1F),
            OpCode.RightShift => JsOperations.DoubleToInt32(left) >> (int)((uint)JsOperations.DoubleToInt32(right) & 0x1F),
            OpCode.UnsignedRightShift => (uint)JsOperations.DoubleToInt32(left) >> (int)((uint)JsOperations.DoubleToInt32(right) & 0x1F),
            OpCode.BitwiseAnd => JsOperations.DoubleToInt32(left) & JsOperations.DoubleToInt32(right),
            OpCode.BitwiseOr => JsOperations.DoubleToInt32(left) | JsOperations.DoubleToInt32(right),
            OpCode.BitwiseXor => JsOperations.DoubleToInt32(left) ^ JsOperations.DoubleToInt32(right),
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Not a numeric operator"),
        };
    }

    /// <summary>Number::exponentiate (https://tc39.es/ecma262/#sec-numeric-types-number-exponentiate).</summary>
    public static double Exponentiate(double baseValue, double exponent)
    {
        if (double.IsNaN(exponent))
            return double.NaN;

        if (exponent == 0)
            return 1;

        if (double.IsNaN(baseValue))
            return double.NaN;

        if (double.IsInfinity(exponent) && Math.Abs(baseValue) == 1)
            return double.NaN;

        return Math.Pow(baseValue, exponent);
    }

    private static BigInteger BigIntOperation(OpCode op, BigInteger left, BigInteger right)
    {
        switch (op)
        {
            case OpCode.Subtract:
                return left - right;
            case OpCode.Multiply:
                return left * right;
            case OpCode.Divide:
                if (right.IsZero)
                    return ThrowHelper.ThrowRangeError<BigInteger>("Division by zero");

                return BigInteger.Divide(left, right);
            case OpCode.Modulo:
                if (right.IsZero)
                    return ThrowHelper.ThrowRangeError<BigInteger>("Division by zero");

                return BigInteger.Remainder(left, right);
            case OpCode.Exponentiate:
                if (right.Sign < 0)
                    return ThrowHelper.ThrowRangeError<BigInteger>("Exponent must be non-negative");

                if (right > int.MaxValue)
                {
                    if (left.IsZero || left.IsOne)
                        return left;

                    if (left == BigInteger.MinusOne)
                        return right.IsEven ? BigInteger.One : BigInteger.MinusOne;

                    return ThrowHelper.ThrowRangeError<BigInteger>("Maximum BigInt size exceeded");
                }

                return BigInteger.Pow(left, (int)right);
            case OpCode.LeftShift:
                return ShiftLeft(left, right);
            case OpCode.RightShift:
                return ShiftLeft(left, -right);
            case OpCode.UnsignedRightShift:
                return ThrowHelper.ThrowTypeError<BigInteger>("BigInts have no unsigned right shift, use >> instead");
            case OpCode.BitwiseAnd:
                return left & right;
            case OpCode.BitwiseOr:
                return left | right;
            case OpCode.BitwiseXor:
                return left ^ right;
            default:
                throw new ArgumentOutOfRangeException(nameof(op), op, "Not a numeric operator");
        }
    }

    private static BigInteger ShiftLeft(BigInteger value, BigInteger shift)
    {
        if (shift.Sign >= 0)
        {
            if (shift > 1 << 30)
                return value.IsZero ? value : ThrowHelper.ThrowRangeError<BigInteger>("Maximum BigInt size exceeded");

            return value << (int)shift;
        }

        var right = BigInteger.Negate(shift);
        if (right > int.MaxValue)
            return value.Sign < 0 ? BigInteger.MinusOne : BigInteger.Zero;

        // >> rounds towards negative infinity (BigInteger >> does it for the negative values)
        return value >> (int)right;
    }

    public static JsValue Negate(JsValue value)
    {
        var numeric = JsOperations.ToNumeric(value);
        return numeric.IsBigInt ? JsValue.FromBigInt(-numeric.AsBigInt()) : new JsValue(-numeric.NumberValue);
    }

    public static JsValue BitwiseNot(JsValue value)
    {
        var numeric = JsOperations.ToNumeric(value);
        return numeric.IsBigInt ? JsValue.FromBigInt(-numeric.AsBigInt() - 1) : new JsValue(~JsOperations.DoubleToInt32(numeric.NumberValue));
    }

    /// <summary>++ and -- on a numeric value (Number::add or BigInt::add with 1 or -1).</summary>
    public static JsValue Increment(JsValue numeric, int delta)
    {
        if (numeric.IsBigInt)
            return JsValue.FromBigInt(numeric.AsBigInt() + delta);

        return new JsValue(JsOperations.ToNumber(numeric) + delta);
    }

    /// <summary>IsLessThan(x, y, LeftFirst): true, false, or null (undefined: a NaN is involved).</summary>
    public static bool? LessThan(JsValue x, JsValue y, bool leftFirst)
    {
        if (x.IsNumber && y.IsNumber)
        {
            var a = x.NumberValue;
            var b = y.NumberValue;
            if (double.IsNaN(a) || double.IsNaN(b))
                return null;

            return a < b;
        }

        JsValue px;
        JsValue py;
        if (leftFirst)
        {
            px = JsOperations.ToPrimitive(x, JsOperations.PreferredType.Number);
            py = JsOperations.ToPrimitive(y, JsOperations.PreferredType.Number);
        }
        else
        {
            py = JsOperations.ToPrimitive(y, JsOperations.PreferredType.Number);
            px = JsOperations.ToPrimitive(x, JsOperations.PreferredType.Number);
        }

        if (px.Cell is JsString sx && py.Cell is JsString sy)
            return string.CompareOrdinal(sx.ToString(), sy.ToString()) < 0;

        if (px.IsBigInt && py.Cell is JsString syBigInt)
        {
            var parsed = JsOperations.StringToBigInt(syBigInt.ToString());
            return parsed is null ? null : px.AsBigInt() < parsed.Value;
        }

        if (px.Cell is JsString sxBigInt && py.IsBigInt)
        {
            var parsed = JsOperations.StringToBigInt(sxBigInt.ToString());
            return parsed is null ? null : parsed.Value < py.AsBigInt();
        }

        var nx = JsOperations.ToNumeric(px);
        var ny = JsOperations.ToNumeric(py);
        if (nx.IsNumber && ny.IsNumber)
        {
            var a = nx.NumberValue;
            var b = ny.NumberValue;
            if (double.IsNaN(a) || double.IsNaN(b))
                return null;

            return a < b;
        }

        if (nx.IsBigInt && ny.IsBigInt)
            return nx.AsBigInt() < ny.AsBigInt();

        // A BigInt and a Number
        if (nx.IsBigInt)
        {
            var number = ny.NumberValue;
            if (double.IsNaN(number))
                return null;

            if (double.IsInfinity(number))
                return number > 0;

            return CompareBigIntNumber(nx.AsBigInt(), number) < 0;
        }
        else
        {
            var number = nx.NumberValue;
            if (double.IsNaN(number))
                return null;

            if (double.IsInfinity(number))
                return number < 0;

            return CompareBigIntNumber(ny.AsBigInt(), number) > 0;
        }
    }

    /// <summary>Compares a BigInt and a finite number exactly.</summary>
    private static int CompareBigIntNumber(BigInteger bigint, double number)
    {
        var floor = Math.Floor(number);
        var integer = new BigInteger(floor);
        var comparison = bigint.CompareTo(integer);
        if (comparison != 0)
            return comparison;

        // Equal integer parts: the fraction of the number makes it greater
        return floor < number ? -1 : 0;
    }
}
