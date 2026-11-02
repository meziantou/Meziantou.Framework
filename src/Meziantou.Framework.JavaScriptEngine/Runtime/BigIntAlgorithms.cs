using System.Buffers;
using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The algorithms on large BigInt values whose cost grows faster than their size: multiplication, exponentiation and
/// the conversions from and to strings. A large operation is split into parts of about 100 ms, and the limits of the
/// engine (<see cref="JsEngineOptions.Timeout"/>, <see cref="JsEngineOptions.MemoryLimit"/>, the cancellation) are
/// checked between the parts. When none of these limits applies, the operation is computed at once by BigInteger when
/// that is faster: on .NET 11, splitting costs up to 50% on the largest values; on .NET 10, only the parsing is faster at
/// once (its squares are slower than the split ones, and its decimal formatting is quadratic).
/// </summary>
internal static class BigIntAlgorithms
{
    // A product of at most this number of bits is computed at once by BigInteger (about 100 ms; .NET 11 multiplies about
    // 3 times faster). The work counted for a part is its number of 32-bit words, so the limits are checked after each
    // part of this size.
#if NET11_0_OR_GREATER
    private const long LeafProductBits = 1 << 23;
#else
    private const long LeafProductBits = 1 << 22;
#endif

    // A product by a value of at most this number of bits is computed at once whatever the size of the other value
    private const long SmallFactorBits = 128;

    // A decimal string of at most this number of digits is parsed at once by BigInteger (about 40 ms on .NET 10, 100 ms
    // on .NET 11)
#if NET11_0_OR_GREATER
    private const int LeafParseDigits = 1 << 20;
#else
    private const int LeafParseDigits = 1 << 18;
#endif

    // BigInteger formats a number of at most this number of bits to decimal at once. Its algorithm is quadratic before
    // .NET 11 and divide and conquer since (about 35 ms for 2^20 bits).
#if NET11_0_OR_GREATER
    private const long LeafDecimalFormatBits = 1 << 20;
#else
    private const long LeafDecimalFormatBits = 1 << 14;
#endif

    private const string DigitChars = "0123456789abcdefghijklmnopqrstuvwxyz";

    // ------------------------------------------------------------------------------------------------------------------
    // Multiplication
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>x × y</summary>
    public static BigInteger Multiply(BigInteger x, BigInteger y)
    {
        if ((long)x.GetBitLength() + (long)y.GetBitLength() <= LeafProductBits)
            return x * y;

#if NET11_0_OR_GREATER
        if (!JsEngine.CurrentWorkCanBeInterrupted)
            return x * y;
#endif

        var negative = (x.Sign < 0) != (y.Sign < 0);
        var magnitudeX = BigInteger.Abs(x);
        var magnitudeY = BigInteger.Abs(y);
        JsEngine.EnsureCurrentCanAllocate(((long)magnitudeX.GetBitLength() + (long)magnitudeY.GetBitLength()) / 8);
        var result = magnitudeX == magnitudeY ? SquareMagnitude(magnitudeX) : MultiplyMagnitudes(magnitudeX, magnitudeY);

        return negative ? -result : result;
    }

    /// <summary>x × y for non-negative values (Karatsuba above the size of a part).</summary>
    private static BigInteger MultiplyMagnitudes(BigInteger x, BigInteger y)
    {
        var xBits = (long)x.GetBitLength();
        var yBits = (long)y.GetBitLength();
        if (xBits < yBits)
        {
            (x, y) = (y, x);
            (xBits, yBits) = (yBits, xBits);
        }

        // The cost of a product by a small value is linear
        if (xBits + yBits <= LeafProductBits || yBits <= SmallFactorBits)
            return MultiplyPart(x, y, xBits + yBits);

        var split = (int)(xBits / 2) & ~31;
        var x1 = x >> split;
        var x0 = LowBits(x, split);
        if (yBits <= split)
        {
            // y is at most half of x: x1 × y and x0 × y
            return (MultiplyMagnitudes(x1, y) << split) + MultiplyMagnitudes(x0, y);
        }

        var y1 = y >> split;
        var y0 = LowBits(y, split);
        var z2 = MultiplyMagnitudes(x1, y1);
        var z0 = MultiplyMagnitudes(x0, y0);
        var z1 = MultiplyMagnitudes(x1 + x0, y1 + y0) - z2 - z0;
        return (((z2 << split) + z1) << split) + z0;
    }

    /// <summary>x² for a non-negative value.</summary>
    private static BigInteger SquareMagnitude(BigInteger x)
    {
        var bits = (long)x.GetBitLength();
        if (bits * 2 <= LeafProductBits)
            return MultiplyPart(x, x, bits * 2);

        var split = (int)(bits / 2) & ~31;
        var x1 = x >> split;
        var x0 = LowBits(x, split);
        var z2 = SquareMagnitude(x1);
        var z0 = SquareMagnitude(x0);
        var z1 = SquareMagnitude(x1 + x0) - z2 - z0;
        return (((z2 << split) + z1) << split) + z0;
    }

    private static BigInteger MultiplyPart(BigInteger x, BigInteger y, long productBits)
    {
        // BigInteger squares the value when both operands share their storage (x * x)
        var result = x * y;
        JsEngine.CountCurrentWork(productBits / 32);
        return result;
    }

    private static BigInteger LowBits(BigInteger value, int bitCount) => value & ((BigInteger.One << bitCount) - BigInteger.One);

    // ------------------------------------------------------------------------------------------------------------------
    // Exponentiation
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>base^exponent, for |base| ≥ 2 and a result whose size is checked by the caller.</summary>
    public static BigInteger Pow(BigInteger @base, int exponent)
    {
        var magnitude = BigInteger.Abs(@base);
        var negative = @base.Sign < 0 && (exponent & 1) != 0;
        if (((long)magnitude.GetBitLength() - 1) * exponent < LeafProductBits)
            return BigInteger.Pow(@base, exponent);

        // base = odd × 2^t: base^exponent = odd^exponent × 2^(t × exponent)
        var trailingZeros = (long)BigInteger.TrailingZeroCount(magnitude);
        var odd = magnitude >> (int)trailingZeros;
        var shift = trailingZeros * exponent;

        // The result has at least this number of bits
        var minimumBits = (((long)odd.GetBitLength() - 1) * exponent) + shift;
        JsEngine.EnsureCurrentCanAllocate(minimumBits / 8);

        BigInteger result;
        if (odd.IsOne)
        {
            result = odd;
        }
#if NET11_0_OR_GREATER
        else if (!JsEngine.CurrentWorkCanBeInterrupted)
        {
            result = BigInteger.Pow(odd, exponent);
        }
#endif
        else
        {
            result = odd;

            // Left-to-right binary exponentiation
            for (var bit = 30 - BitOperations.LeadingZeroCount((uint)exponent); bit >= 0; bit--)
            {
                result = SquareMagnitude(result);
                if ((exponent & (1 << bit)) != 0)
                {
                    result = MultiplyMagnitudes(result, odd);
                }
            }
        }

        if (shift > 0)
        {
            result <<= (int)shift;
        }

        return negative ? -result : result;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Parsing
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The value of digits in a radix (2, 8, 10 or 16): the digits are valid, without sign nor separators.</summary>
    public static BigInteger Parse(ReadOnlySpan<char> digits, int radix)
    {
        var firstSignificant = digits.IndexOfAnyExcept('0');
        if (firstSignificant < 0)
            return BigInteger.Zero;

        digits = digits[firstSignificant..];
        if (radix == 10)
            return ParseDecimal(digits);

        return ParsePowerOfTwoRadix(digits, BitOperations.Log2((uint)radix));
    }

    private static BigInteger ParseDecimal(ReadOnlySpan<char> digits)
    {
        if (digits.Length <= 18)
            return ulong.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

        if (digits.Length <= LeafParseDigits || !JsEngine.CurrentWorkCanBeInterrupted)
            return ParseDecimalPart(digits);

        // A decimal digit is log2(10) bits
        JsEngine.EnsureCurrentCanAllocate((long)(digits.Length * 3.33 / 8));
        var powers = new List<BigInteger> { BigInteger.Pow(10, LeafParseDigits) };
        return ParseDecimal(digits, powers);
    }

    /// <summary>
    /// Divide and conquer: value = high × 10^n + low, where n = LeafParseDigits × 2^k; powers[k] is 10^n.
    /// </summary>
    private static BigInteger ParseDecimal(ReadOnlySpan<char> digits, List<BigInteger> powers)
    {
        if (digits.Length <= LeafParseDigits)
            return ParseDecimalPart(digits);

        var level = 0;
        while ((long)LeafParseDigits << (level + 1) < digits.Length)
        {
            level++;
        }

        while (powers.Count <= level)
        {
            powers.Add(SquareMagnitude(powers[^1]));
        }

        var lowLength = LeafParseDigits << level;
        var high = ParseDecimal(digits[..^lowLength], powers);
        var low = ParseDecimal(digits[^lowLength..], powers);
        return MultiplyMagnitudes(high, powers[level]) + low;
    }

    private static BigInteger ParseDecimalPart(ReadOnlySpan<char> digits)
    {
        var result = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        JsEngine.CountCurrentWork(digits.Length);
        return result;
    }

    /// <summary>Packs the bits of the digits (radix 2^bitsPerDigit) directly.</summary>
    private static BigInteger ParsePowerOfTwoRadix(ReadOnlySpan<char> digits, int bitsPerDigit)
    {
        var bitCount = (long)digits.Length * bitsPerDigit;
        if (bitCount <= 64)
        {
            ulong small = 0;
            foreach (var c in digits)
            {
                small = (small << bitsPerDigit) | (uint)GetDigitValue(c);
            }

            return small;
        }

        JsEngine.EnsureCurrentCanAllocate(bitCount / 4);
        JsEngine.CountCurrentWork(digits.Length);

        // Little-endian bytes, from the last digit
        var byteCount = (int)((bitCount + 7) / 8);
        var bytes = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var byteIndex = 0;
            var accumulator = 0u;
            var accumulatedBits = 0;
            for (var i = digits.Length - 1; i >= 0; i--)
            {
                accumulator |= (uint)GetDigitValue(digits[i]) << accumulatedBits;
                accumulatedBits += bitsPerDigit;
                if (accumulatedBits >= 8)
                {
                    bytes[byteIndex++] = (byte)accumulator;
                    accumulator >>= 8;
                    accumulatedBits -= 8;
                }
            }

            if (accumulatedBits > 0)
            {
                bytes[byteIndex++] = (byte)accumulator;
            }

            return new BigInteger(bytes.AsSpan(0, byteIndex), isUnsigned: true, isBigEndian: false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private static int GetDigitValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    // ------------------------------------------------------------------------------------------------------------------
    // Formatting
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The digits of a value in a radix (2 to 36), lowercase, with a '-' sign for a negative value.</summary>
    public static string ToString(BigInteger value, int radix)
    {
        if (value.IsZero)
            return "0";

        var negative = value.Sign < 0;
        var magnitude = negative ? BigInteger.Negate(value) : value;
        var bitLength = (long)magnitude.GetBitLength();
        if (BitOperations.IsPow2(radix))
            return FormatPowerOfTwoRadix(magnitude, bitLength, BitOperations.Log2((uint)radix), negative);

        if (radix == 10 && bitLength <= LeafDecimalFormatBits)
            return value.ToString(CultureInfo.InvariantCulture);

#if NET11_0_OR_GREATER
        if (radix == 10 && !JsEngine.CurrentWorkCanBeInterrupted)
            return value.ToString(CultureInfo.InvariantCulture);
#endif

        // An upper bound of the number of digits, and the sign
        var maximumLength = (int)(bitLength / Math.Log2(radix)) + 2;
        JsEngine.EnsureCurrentCanAllocate(maximumLength * 2L * sizeof(char));

        // The largest power of the radix that fits in a ulong: the digits are written by chunks of this size
        var chunkDigits = 1;
        var chunkDivisor = (ulong)radix;
        while (chunkDivisor <= ulong.MaxValue / (ulong)radix)
        {
            chunkDivisor *= (ulong)radix;
            chunkDigits++;
        }

        // powers[k] = chunkDivisor^(2^k), until the next one is larger than the value
        var powers = new List<BigInteger> { chunkDivisor };
        while (true)
        {
            var last = powers[^1];
            var lastBits = (long)last.GetBitLength();
            if ((lastBits * 2) - 1 > bitLength)
                break;

            var next = SquareMagnitude(last);
            if (next > magnitude)
                break;

            powers.Add(next);
        }

        var buffer = ArrayPool<char>.Shared.Rent(maximumLength);
        try
        {
            var position = maximumLength;
            WriteDigits(magnitude, powers.Count - 1, padded: false, buffer, ref position, radix, chunkDigits, powers);
            if (negative)
            {
                buffer[--position] = '-';
            }

            return new string(buffer, position, maximumLength - position);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Writes the digits of value (value &lt; powers[level + 1]) before position, padded with zeros to
    /// chunkDigits × 2^(level + 1) digits when padded is true.
    /// </summary>
    private static void WriteDigits(BigInteger value, int level, bool padded, char[] buffer, ref int position, int radix, int chunkDigits, List<BigInteger> powers)
    {
        if (level < 0)
        {
            WriteChunk((ulong)value, padded ? chunkDigits : 1, buffer, ref position, radix);
            JsEngine.CountCurrentWork(chunkDigits);
            return;
        }

        if (radix == 10 && (long)value.GetBitLength() <= LeafDecimalFormatBits)
        {
            WriteDecimalPart(value, padded ? chunkDigits << (level + 1) : 1, buffer, ref position);
            return;
        }

        var high = BigInteger.DivRem(value, powers[level], out var low);
        if (!padded && high.IsZero)
        {
            WriteDigits(low, level - 1, padded: false, buffer, ref position, radix, chunkDigits, powers);
            return;
        }

        WriteDigits(low, level - 1, padded: true, buffer, ref position, radix, chunkDigits, powers);
        WriteDigits(high, level - 1, padded, buffer, ref position, radix, chunkDigits, powers);
    }

    /// <summary>Writes the decimal digits of a value formatted by BigInteger before position, padded with zeros to minimumLength digits.</summary>
    private static void WriteDecimalPart(BigInteger value, int minimumLength, char[] buffer, ref int position)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        digits.CopyTo(buffer.AsSpan(position - digits.Length));
        position -= digits.Length;
        if (digits.Length < minimumLength)
        {
            buffer.AsSpan(position - (minimumLength - digits.Length), minimumLength - digits.Length).Fill('0');
            position -= minimumLength - digits.Length;
        }

        JsEngine.CountCurrentWork(digits.Length);
    }

    private static void WriteChunk(ulong value, int minimumLength, char[] buffer, ref int position, int radix)
    {
        var end = position;
        while (value != 0 || end - position < minimumLength)
        {
            buffer[--position] = DigitChars[(int)(value % (ulong)radix)];
            value /= (ulong)radix;
        }
    }

    /// <summary>Extracts the digits from the bits directly (radix 2^bitsPerDigit).</summary>
    private static string FormatPowerOfTwoRadix(BigInteger magnitude, long bitLength, int bitsPerDigit, bool negative)
    {
        var digitCount = (int)((bitLength + bitsPerDigit - 1) / bitsPerDigit);
        var length = digitCount + (negative ? 1 : 0);
        JsEngine.EnsureCurrentCanAllocate(length * sizeof(char));
        JsEngine.CountCurrentWork(length);

        var byteCount = magnitude.GetByteCount(isUnsigned: true);
        var bytes = ArrayPool<byte>.Shared.Rent(byteCount + 1);
        try
        {
            magnitude.TryWriteBytes(bytes, out _, isUnsigned: true, isBigEndian: false);
            bytes[byteCount] = 0;
            return string.Create(length, (Bytes: bytes, BitsPerDigit: bitsPerDigit, Negative: negative), (span, state) =>
            {
                var mask = (1 << state.BitsPerDigit) - 1;
                var offset = state.Negative ? 1 : 0;
                if (state.Negative)
                {
                    span[0] = '-';
                }

                var digitCount = span.Length - offset;
                for (var i = 0; i < digitCount; i++)
                {
                    // A digit has at most 5 bits: it spans at most 2 bytes
                    var bitPosition = (long)i * state.BitsPerDigit;
                    var byteIndex = (int)(bitPosition >> 3);
                    var word = state.Bytes[byteIndex] | (state.Bytes[byteIndex + 1] << 8);
                    span[span.Length - 1 - i] = DigitChars[(word >> (int)(bitPosition & 7)) & mask];
                }
            });
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }
}
