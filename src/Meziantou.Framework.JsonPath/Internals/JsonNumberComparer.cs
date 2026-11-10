using System.Numerics;

namespace Meziantou.Framework.Json.Internals;

/// <summary>Compares numbers written as text, such as <c>9007199254740993</c> or <c>1.5e-3</c>, without rounding them.</summary>
internal static class JsonNumberComparer
{
    /// <summary>Compares two numbers written in UTF-8.</summary>
    /// <param name="left">The first number.</param>
    /// <param name="right">The second number.</param>
    /// <param name="result">A negative number, zero or a positive number, as <see cref="IComparable.CompareTo(object)"/> does.</param>
    /// <returns>
    /// <see langword="false"/> when a text is not a number: an optional minus sign, digits, optionally a fraction and an
    /// exponent. Leading zeros and an uppercase <c>E</c> are accepted, so the text of a .NET number also compares.
    /// </returns>
    public static bool TryCompare(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, out int result)
    {
        if (left.SequenceEqual(right))
        {
            result = 0;
            return Number.TryParse(left, out _);
        }

        if (!Number.TryParse(left, out var leftNumber) || !Number.TryParse(right, out var rightNumber))
        {
            result = 0;
            return false;
        }

        result = Compare(leftNumber, rightNumber);
        return true;
    }

    private static int Compare(in Number left, in Number right)
    {
        var leftSign = left.Sign;
        var rightSign = right.Sign;
        if (leftSign != rightSign)
            return leftSign.CompareTo(rightSign);

        if (leftSign is 0)
            return 0;

        var magnitude = left.Exponent.CompareTo(right.Exponent);
        if (magnitude is 0)
        {
            magnitude = CompareDigits(left, right);
        }

        return leftSign * magnitude;
    }

    // Both numbers have the same exponent, so the digit sequences compare like decimal fractions 0.d1d2...
    private static int CompareDigits(in Number left, in Number right)
    {
        var shared = Math.Min(left.DigitCount, right.DigitCount);
        for (var i = 0; i < shared; i++)
        {
            var comparison = left.GetDigit(i).CompareTo(right.GetDigit(i));
            if (comparison is not 0)
                return comparison;
        }

        // The significant digits have no trailing zero, so the longer sequence is the larger number
        return left.DigitCount.CompareTo(right.DigitCount);
    }

    /// <summary>A number written as <c>0.d1d2...dn × 10^Exponent</c>, where d1 and dn are not zero.</summary>
    private readonly ref struct Number
    {
        private readonly ReadOnlySpan<byte> _integer;
        private readonly ReadOnlySpan<byte> _fraction;
        private readonly int _firstDigit;

        private Number(bool isNegative, ReadOnlySpan<byte> integer, ReadOnlySpan<byte> fraction, int firstDigit, int digitCount, BigInteger exponent)
        {
            IsNegative = isNegative;
            _integer = integer;
            _fraction = fraction;
            _firstDigit = firstDigit;
            DigitCount = digitCount;
            Exponent = exponent;
        }

        public bool IsNegative { get; }

        /// <summary>Gets the number of significant digits, zero for the number zero.</summary>
        public int DigitCount { get; }

        public BigInteger Exponent { get; }

        public int Sign => DigitCount is 0 ? 0 : IsNegative ? -1 : 1;

        /// <summary>Gets a significant digit, as an ASCII character.</summary>
        public byte GetDigit(int index)
        {
            index += _firstDigit;
            return index < _integer.Length ? _integer[index] : _fraction[index - _integer.Length];
        }

        public static bool TryParse(ReadOnlySpan<byte> text, out Number result)
        {
            result = default;
            var position = 0;
            var isNegative = false;
            if (position < text.Length && text[position] is (byte)'-')
            {
                isNegative = true;
                position++;
            }

            var integer = ReadDigits(text, ref position);
            if (integer.IsEmpty)
                return false;

            var fraction = ReadOnlySpan<byte>.Empty;
            if (position < text.Length && text[position] is (byte)'.')
            {
                position++;
                fraction = ReadDigits(text, ref position);
                if (fraction.IsEmpty)
                    return false;
            }

            BigInteger exponent = 0;
            if (position < text.Length && text[position] is (byte)'e' or (byte)'E')
            {
                position++;
                var negativeExponent = false;
                if (position < text.Length && text[position] is (byte)'+' or (byte)'-')
                {
                    negativeExponent = text[position] is (byte)'-';
                    position++;
                }

                var exponentDigits = ReadDigits(text, ref position);
                if (exponentDigits.IsEmpty)
                    return false;

                exponent = ParseInteger(exponentDigits);
                if (negativeExponent)
                {
                    exponent = -exponent;
                }
            }

            if (position != text.Length)
                return false;

            // Skip the leading and trailing zeros of the digits, which run on from the integer part to the fraction
            var length = integer.Length + fraction.Length;
            var first = 0;
            while (first < length && GetDigit(integer, fraction, first) is (byte)'0')
            {
                first++;
            }

            var last = length;
            while (last > first && GetDigit(integer, fraction, last - 1) is (byte)'0')
            {
                last--;
            }

            result = new Number(isNegative, integer, fraction, first, last - first, exponent + integer.Length - first);
            return true;
        }

        private static byte GetDigit(ReadOnlySpan<byte> integer, ReadOnlySpan<byte> fraction, int index)
        {
            return index < integer.Length ? integer[index] : fraction[index - integer.Length];
        }

        private static ReadOnlySpan<byte> ReadDigits(ReadOnlySpan<byte> text, scoped ref int position)
        {
            var start = position;
            while (position < text.Length && text[position] is >= (byte)'0' and <= (byte)'9')
            {
                position++;
            }

            return text[start..position];
        }

        // An exponent can have any number of digits, so it does not always fit in a long. It is read 18 digits at a
        // time, which always fit in a long.
        private static BigInteger ParseInteger(ReadOnlySpan<byte> digits)
        {
            BigInteger result = 0;
            while (!digits.IsEmpty)
            {
                var chunk = digits[..Math.Min(18, digits.Length)];
                long value = 0;
                foreach (var digit in chunk)
                {
                    value = (value * 10) + (digit - '0');
                }

                result = (result * BigInteger.Pow(10, chunk.Length)) + value;
                digits = digits[chunk.Length..];
            }

            return result;
        }
    }
}
