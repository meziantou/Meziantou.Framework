using System.Globalization;
using System.Numerics;
using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The BigInt type (https://tc39.es/ecma262/#sec-numeric-types-bigint) and the conversions between BigInt values and the other types.</summary>
internal static class BigIntOperations
{
    /// <summary>The maximum number of bits of a BigInt value (the limit of V8): larger results throw a RangeError.</summary>
    public const long MaxBitLength = 1L << 30;

    private const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

    // ------------------------------------------------------------------------------------------------------------------
    // Conversions
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The Number value of a BigInt value: the number closest to it (round half to even).</summary>
    public static double ToNumber(BigInteger value) => ToDouble(value, 0);

    /// <summary>The number closest to value \u00D7 2^binaryExponent (round half to even, \u00B1Infinity when it is too large).</summary>
    public static double ToDouble(BigInteger value, int binaryExponent)
    {
        if (value.IsZero)
            return 0;

        var negative = value.Sign < 0;
        var magnitude = negative ? BigInteger.Negate(value) : value;
        var bitLength = (long)magnitude.GetBitLength();

        // The weight of the most significant bit is 2^topExponent
        var topExponent = bitLength - 1 + binaryExponent;
        if (topExponent >= 1024)
            return negative ? double.NegativeInfinity : double.PositiveInfinity;

        // The number of significand bits: 53 for a normal number, fewer for a subnormal number
        var precision = topExponent >= -1022 ? 53 : topExponent + 1075;
        if (precision < 0)
            return negative ? -0.0 : 0.0;

        var shift = bitLength - precision;
        ulong significand;
        if (shift <= 0)
        {
            significand = (ulong)(magnitude << (int)-shift);
        }
        else
        {
            significand = (ulong)(magnitude >> (int)shift);
            var halfBit = !(magnitude >> (int)(shift - 1)).IsEven;
            if (halfBit)
            {
                var sticky = BigInteger.TrailingZeroCount(magnitude) < shift - 1;
                if (sticky || (significand & 1) != 0)
                {
                    significand++;
                }
            }
        }

        var result = Math.ScaleB(significand, (int)(binaryExponent + shift));
        return negative ? -result : result;
    }

    /// <summary>NumberToBigInt(number): a RangeError when the number is not an integer.</summary>
    public static BigInteger NumberToBigInt(double number)
    {
        if (!NumberBuiltins.IsIntegralNumber(number))
            return ThrowHelper.ThrowRangeError<BigInteger>($"The number {NumberFormatting.ToString(number)} cannot be converted to a BigInt because it is not an integer");

        return new BigInteger(number);
    }

    /// <summary>ToBigInt(argument)</summary>
    public static BigInteger ToBigInt(JsValue value)
    {
        var prim = JsOperations.ToPrimitive(value, JsOperations.PreferredType.Number);
        switch (prim.Cell)
        {
            case JsBigInt bigint:
                return bigint.Value;
            case BooleanCell boolean:
                return boolean.Value ? BigInteger.One : BigInteger.Zero;
            case JsString str:
                var result = JsOperations.StringToBigInt(str.ToString());
                if (result is null)
                {
                    ThrowHelper.ThrowSyntaxError($"Cannot convert {str} to a BigInt");
                }

                return result.Value;
            case JsSymbol:
                return ThrowHelper.ThrowTypeError<BigInteger>("Cannot convert a Symbol value to a BigInt");
            default:
                return ThrowHelper.ThrowTypeError<BigInteger>($"Cannot convert {prim} to a BigInt");
        }
    }

    /// <summary>ToBigInt64(argument)</summary>
    public static long ToBigInt64(JsValue value) => unchecked((long)(ulong)(ToBigInt(value) & ulong.MaxValue));

    /// <summary>ToBigUint64(argument)</summary>
    public static ulong ToBigUint64(JsValue value) => (ulong)(ToBigInt(value) & ulong.MaxValue);

    /// <summary>BigInt::toString(x, radix)</summary>
    public static string ToString(BigInteger value, int radix)
    {
        if (radix == 10)
            return value.ToString(CultureInfo.InvariantCulture);

        if (value.IsZero)
            return "0";

        var negative = value.Sign < 0;
        var magnitude = negative ? BigInteger.Negate(value) : value;

        // Convert by chunks of digits that fit in a ulong
        var digitsPerChunk = 1;
        ulong divisor = (ulong)radix;
        while (divisor <= ulong.MaxValue / (ulong)radix / (ulong)radix)
        {
            divisor *= (ulong)radix;
            digitsPerChunk++;
        }

        var chunks = new List<ulong>();
        var bigDivisor = new BigInteger(divisor);
        while (magnitude >= bigDivisor)
        {
            magnitude = BigInteger.DivRem(magnitude, bigDivisor, out var remainder);
            chunks.Add((ulong)remainder);
        }

        var builder = new StringBuilder((chunks.Count + 1) * digitsPerChunk + 1);
        if (negative)
        {
            builder.Append('-');
        }

        AppendDigits(builder, (ulong)magnitude, radix, minimumLength: 1);
        for (var i = chunks.Count - 1; i >= 0; i--)
        {
            AppendDigits(builder, chunks[i], radix, digitsPerChunk);
        }

        return builder.ToString();
    }

    private static void AppendDigits(StringBuilder builder, ulong value, int radix, int minimumLength)
    {
        Span<char> buffer = stackalloc char[64];
        var position = buffer.Length;
        while (value != 0 || buffer.Length - position < minimumLength)
        {
            buffer[--position] = Digits[(int)(value % (ulong)radix)];
            value /= (ulong)radix;
        }

        builder.Append(buffer[position..]);
    }

    /// <summary>BigInt.asIntN(bits, bigint) once the arguments are converted.</summary>
    public static BigInteger AsIntN(double bits, BigInteger value)
    {
        if (bits == 0)
            return BigInteger.Zero;

        // The value fits in the bits (two's complement)
        if ((long)value.GetBitLength() + 1 <= bits)
            return value;

        var n = (int)bits;
        var modulus = BigInteger.One << n;
        var mod = value & (modulus - 1);
        return mod >= modulus >> 1 ? mod - modulus : mod;
    }

    /// <summary>BigInt.asUintN(bits, bigint) once the arguments are converted.</summary>
    public static BigInteger AsUintN(double bits, BigInteger value)
    {
        if (bits == 0)
            return BigInteger.Zero;

        if (value.Sign >= 0 && (long)value.GetBitLength() <= bits)
            return value;

        if (bits > MaxBitLength)
            return ThrowHelper.ThrowRangeError<BigInteger>("Maximum BigInt size exceeded");

        return value & ((BigInteger.One << (int)bits) - 1);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Operators (https://tc39.es/ecma262/#sec-numeric-types-bigint)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>BigInt::unaryMinus(x)</summary>
    public static BigInteger UnaryMinus(BigInteger x) => -x;

    /// <summary>BigInt::bitwiseNOT(x)</summary>
    public static BigInteger BitwiseNot(BigInteger x) => -x - BigInteger.One;

    /// <summary>BigInt::exponentiate(base, exponent)</summary>
    public static BigInteger Exponentiate(BigInteger @base, BigInteger exponent)
    {
        if (exponent.Sign < 0)
            return ThrowHelper.ThrowRangeError<BigInteger>("Exponent must be non-negative");

        if (exponent.IsZero)
            return BigInteger.One;

        if (@base.IsZero || @base.IsOne)
            return @base;

        if (@base == BigInteger.MinusOne)
            return exponent.IsEven ? BigInteger.One : BigInteger.MinusOne;

        // |base| >= 2: the result has at least exponent + 1 bits
        if (exponent > MaxBitLength || ((long)BigInteger.Abs(@base).GetBitLength() - 1) * (long)exponent > MaxBitLength)
            return ThrowHelper.ThrowRangeError<BigInteger>("Maximum BigInt size exceeded");

        return BigInteger.Pow(@base, (int)exponent);
    }

    /// <summary>BigInt::multiply(x, y)</summary>
    public static BigInteger Multiply(BigInteger x, BigInteger y)
    {
        if ((long)x.GetBitLength() + (long)y.GetBitLength() > MaxBitLength)
            return ThrowHelper.ThrowRangeError<BigInteger>("Maximum BigInt size exceeded");

        return x * y;
    }

    /// <summary>BigInt::divide(x, y): truncated toward zero.</summary>
    public static BigInteger Divide(BigInteger x, BigInteger y)
    {
        if (y.IsZero)
            return ThrowHelper.ThrowRangeError<BigInteger>("Division by zero");

        return BigInteger.Divide(x, y);
    }

    /// <summary>BigInt::remainder(n, d): the sign of the dividend.</summary>
    public static BigInteger Remainder(BigInteger n, BigInteger d)
    {
        if (d.IsZero)
            return ThrowHelper.ThrowRangeError<BigInteger>("Division by zero");

        return BigInteger.Remainder(n, d);
    }

    /// <summary>BigInt::add(x, y)</summary>
    public static BigInteger Add(BigInteger x, BigInteger y) => x + y;

    /// <summary>BigInt::subtract(x, y)</summary>
    public static BigInteger Subtract(BigInteger x, BigInteger y) => x - y;

    /// <summary>BigInt::leftShift(x, y): a negative y shifts to the right (rounding toward -Infinity).</summary>
    public static BigInteger LeftShift(BigInteger x, BigInteger y)
    {
        if (x.IsZero || y.IsZero)
            return x;

        if (y.Sign < 0)
        {
            // Right shift: the result is 0 or -1 when all the bits are shifted out
            if (-y >= (long)x.GetBitLength() + 1)
                return x.Sign < 0 ? BigInteger.MinusOne : BigInteger.Zero;

            return x >> (int)-y;
        }

        if (y > MaxBitLength || (long)x.GetBitLength() + (long)y > MaxBitLength)
            return ThrowHelper.ThrowRangeError<BigInteger>("Maximum BigInt size exceeded");

        return x << (int)y;
    }

    /// <summary>BigInt::signedRightShift(x, y)</summary>
    public static BigInteger SignedRightShift(BigInteger x, BigInteger y) => LeftShift(x, -y);

    /// <summary>BigInt::unsignedRightShift(x, y): always a TypeError.</summary>
    public static BigInteger UnsignedRightShift() => ThrowHelper.ThrowTypeError<BigInteger>("BigInts have no unsigned right shift, use >> instead");

    /// <summary>BigInt::bitwiseAND(x, y)</summary>
    public static BigInteger BitwiseAnd(BigInteger x, BigInteger y) => x & y;

    /// <summary>BigInt::bitwiseOR(x, y)</summary>
    public static BigInteger BitwiseOr(BigInteger x, BigInteger y) => x | y;

    /// <summary>BigInt::bitwiseXOR(x, y)</summary>
    public static BigInteger BitwiseXor(BigInteger x, BigInteger y) => x ^ y;

    // ------------------------------------------------------------------------------------------------------------------
    // Comparisons
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Compares a BigInt with a number: negative, zero or positive; null when the number is NaN.</summary>
    public static int? Compare(BigInteger x, double y)
    {
        if (double.IsNaN(y))
            return null;

        if (double.IsPositiveInfinity(y))
            return -1;

        if (double.IsNegativeInfinity(y))
            return 1;

        var floor = Math.Floor(y);
        var comparison = x.CompareTo(new BigInteger(floor));
        if (comparison != 0)
            return comparison;

        // x == floor(y) <= y
        return floor == y ? 0 : -1;
    }

    /// <summary>
    /// IsLessThan(x, y, LeftFirst) (https://tc39.es/ecma262/#sec-islessthan): true, false, or null for undefined (a NaN
    /// operand, or a string that is not a valid BigInt compared with a BigInt).
    /// </summary>
    public static bool? IsLessThan(JsValue x, JsValue y, bool leftFirst)
    {
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

        if (px.Cell is JsString sx)
        {
            if (py.Cell is JsString sy)
                return string.CompareOrdinal(sx.ToString(), sy.ToString()) < 0;

            if (py.Cell is JsBigInt by)
            {
                var nx = JsOperations.StringToBigInt(sx.ToString());
                return nx is null ? null : nx.Value < by.Value;
            }
        }

        if (px.Cell is JsBigInt bx && py.Cell is JsString sy2)
        {
            var ny = JsOperations.StringToBigInt(sy2.ToString());
            return ny is null ? null : bx.Value < ny.Value;
        }

        var numericX = JsOperations.ToNumeric(px);
        var numericY = JsOperations.ToNumeric(py);
        if (numericX.IsNumber && numericY.IsNumber)
        {
            var a = numericX.NumberValue;
            var b = numericY.NumberValue;
            if (double.IsNaN(a) || double.IsNaN(b))
                return null;

            return a < b;
        }

        if (numericX.IsBigInt && numericY.IsBigInt)
            return numericX.AsBigInt() < numericY.AsBigInt();

        if (numericX.IsBigInt)
        {
            var comparison = Compare(numericX.AsBigInt(), numericY.NumberValue);
            return comparison is null ? null : comparison < 0;
        }

        var reverse = Compare(numericY.AsBigInt(), numericX.NumberValue);
        return reverse is null ? null : reverse > 0;
    }
}
