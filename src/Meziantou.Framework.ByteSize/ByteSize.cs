using System.Runtime.InteropServices;

namespace Meziantou.Framework;

[StructLayout(LayoutKind.Auto)]
public readonly partial struct ByteSize : IEquatable<ByteSize>, IComparable, IComparable<ByteSize>, IFormattable
#if NET6_0_OR_GREATER
    , ISpanFormattable
#endif
#if NET7_0_OR_GREATER
    , ISpanParsable<ByteSize>
#endif
#if NET8_0_OR_GREATER
    , IUtf8SpanFormattable
    , IUtf8SpanParsable<ByteSize>
#endif
{
    public ByteSize(long length)
    {
        Value = length;
    }

    public long Value { get; }

    public static ByteSize Zero => new(0L);
    public static ByteSize MaxValue => new(long.MaxValue);
    public static ByteSize MinValue => new(long.MinValue);

    public override bool Equals(object? obj) => obj is ByteSize byteSize && Equals(byteSize);

    public bool Equals(ByteSize other) => Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public int CompareTo(ByteSize other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj)
    {
        if (obj is null)
            return 1;

        var fileLength = (ByteSize)obj;
        return CompareTo(fileLength);
    }

    public override string ToString() => ToString(format: null, formatProvider: null);

    public string ToString(ByteSizeUnit unit) => ToString(unit, formatProvider: null);

    public string ToString(ByteSizeUnit unit, IFormatProvider? formatProvider) => GetValue(unit).ToString(formatProvider) + UnitToString(unit);

    public string ToString(IFormatProvider? formatProvider)
    {
        return ToString(format: null, formatProvider);
    }

    /// <summary>
    /// Convert value to string
    /// </summary>
    /// <param name="format">
    /// Allowed formats:
    /// <list type="bullet">
    ///     <item><c>G</c>: Find the best unit (B, kB, MB, GB, etc.), and use G format for the value</item>
    ///     <item><c>G2</c>: Find the best unit (B, kB, MB, GB, etc.), and use F2 format for the value</item>
    ///     <item><c>Gi</c>: Find the best unit (B, kiB, MiB, GiB, etc.), and use G format for the value</item>
    ///     <item><c>Gi2</c>: Find the best unit (B, kiB, MiB, GiB, etc.), and use F2 format for the value</item>
    ///     <item>
    ///         <c>B</c>, <c>kB</c>, <c>kiB</c>, <c>MB</c>, <c>MiB</c>, <c>GB</c>, <c>GiB</c>, <c>TB</c>, <c>TiB</c>, <c>PB</c>, <c>PiB</c>, <c>EB</c>, <c>EiB</c>:
    ///         Use the provided unit, and use G format for the value. If a number is provided (e.g. <c>kB3</c>), it use Fn (e.g. F3) format to convert the value to string.
    ///     </item>
    /// </list>
    /// </param>
    /// <exception cref="ArgumentException">The provided format is not valid</exception>
    public string ToString(string? format)
    {
        return ToString(format, formatProvider: null);
    }

    /// <summary>
    /// Convert value to string
    /// </summary>
    /// <param name="format">
    /// Allowed formats:
    /// <list type="bullet">
    ///     <item><c>G</c>: Find the best unit (B, kB, MB, GB, etc.), and use G format for the value</item>
    ///     <item><c>G2</c>: Find the best unit (B, kB, MB, GB, etc.), and use F2 format for the value</item>
    ///     <item><c>Gi</c>: Find the best unit (B, kiB, MiB, GiB, etc.), and use G format for the value</item>
    ///     <item><c>Gi2</c>: Find the best unit (B, kiB, MiB, GiB, etc.), and use F2 format for the value</item>
    ///     <item>
    ///         <c>B</c>, <c>kB</c>, <c>kiB</c>, <c>MB</c>, <c>MiB</c>, <c>GB</c>, <c>GiB</c>, <c>TB</c>, <c>TiB</c>, <c>PB</c>, <c>PiB</c>, <c>EB</c>, <c>EiB</c>:
    ///         Use the provided unit, and use G format for the value. If a number is provided (e.g. <c>kB3</c>), it use Fn (e.g. F3) format to convert the value to string.
    ///     </item>
    /// </list>
    /// </param>
    /// <param name="formatProvider"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">The provided format is not valid</exception>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        if (string.IsNullOrEmpty(format))
        {
            format = "g";
        }

        var index = -1;
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c is >= '0' and <= '9')
            {
                index = i;
                break;
            }
        }

        var unitString = format;
        if (index >= 0)
        {
            unitString = format[..index];
        }

        if (!TryParseUnit(unitString, out var unit, out var parsedLength) || unitString.Length != parsedLength)
        {
            if (string.Equals(unitString, "gi", StringComparison.OrdinalIgnoreCase) || string.Equals(unitString, "fi", StringComparison.OrdinalIgnoreCase))
            {
                unit = FindBestUnitI();
            }
            else if (unitString is "g" or "f" or "G" or "F")
            {
                unit = FindBestUnit();
            }
            else
            {
                throw new ArgumentException($"format '{format}' is invalid", nameof(format));
            }
        }

        var numberFormat = "G";
        if (index > 0)
        {
            if (!int.TryParse(format[index..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                throw new ArgumentException($"format '{format}' is invalid", nameof(format));

            numberFormat = "F" + number.ToString(CultureInfo.InvariantCulture);
        }

        return GetValue(unit).ToString(numberFormat, formatProvider) + UnitToString(unit);
    }

#if NET6_0_OR_GREATER
    /// <summary>
    /// Tries to format the value into the provided span of characters.
    /// </summary>
    /// <param name="destination">The span in which to write this instance's value formatted as a span of characters.</param>
    /// <param name="charsWritten">When this method returns, contains the number of characters that were written in destination.</param>
    /// <param name="format">A span containing the characters that represent a standard or custom format string.</param>
    /// <param name="provider">An optional object that supplies culture-specific formatting information for destination.</param>
    /// <returns>true if the formatting was successful; otherwise, false.</returns>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        // Determine the format and unit
        var formatString = format.IsEmpty ? "g" : format;

        var index = -1;
        for (var i = 0; i < formatString.Length; i++)
        {
            var c = formatString[i];
            if (c is >= '0' and <= '9')
            {
                index = i;
                break;
            }
        }

        var unitString = formatString;
        if (index >= 0)
        {
            unitString = formatString[..index];
        }

        if (!TryParseUnit(unitString, out var unit, out var parsedLength) || unitString.Length != parsedLength)
        {
            if (unitString.Equals("gi", StringComparison.OrdinalIgnoreCase) || unitString.Equals("fi", StringComparison.OrdinalIgnoreCase))
            {
                unit = FindBestUnitI();
            }
            else if (unitString is "g" or "f" or "G" or "F")
            {
                unit = FindBestUnit();
            }
            else
            {
                charsWritten = 0;
                return false;
            }
        }

        var numberFormat = "G";
        if (index > 0)
        {
            if (!int.TryParse(formatString[index..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                charsWritten = 0;
                return false;
            }

            numberFormat = "F" + number.ToString(CultureInfo.InvariantCulture);
        }

        // Format the value
        var value = GetValue(unit);
        var unitStr = UnitToString(unit);

        // Try to format the number part
        if (!value.TryFormat(destination, out var numberCharsWritten, numberFormat, provider))
        {
            charsWritten = 0;
            return false;
        }

        // Check if there's enough space for the unit string
        if (destination.Length - numberCharsWritten < unitStr.Length)
        {
            charsWritten = 0;
            return false;
        }

        // Copy the unit string
        unitStr.AsSpan().CopyTo(destination[numberCharsWritten..]);
        charsWritten = numberCharsWritten + unitStr.Length;
        return true;
    }
#endif

    private ByteSizeUnit FindBestUnit()
    {
        if (Value >= (long)ByteSizeUnit.ExaByte)
            return ByteSizeUnit.ExaByte;

        if (Value >= (long)ByteSizeUnit.PetaByte)
            return ByteSizeUnit.PetaByte;

        else if (Value >= (long)ByteSizeUnit.TeraByte)
            return ByteSizeUnit.TeraByte;

        else if (Value >= (long)ByteSizeUnit.GigaByte)
            return ByteSizeUnit.GigaByte;

        else if (Value >= (long)ByteSizeUnit.MegaByte)
            return ByteSizeUnit.MegaByte;

        else if (Value >= (long)ByteSizeUnit.KiloByte)
            return ByteSizeUnit.KiloByte;

        return ByteSizeUnit.Byte;
    }

    private ByteSizeUnit FindBestUnitI()
    {
        if (Value >= (long)ByteSizeUnit.ExbiByte)
            return ByteSizeUnit.ExbiByte;

        if (Value >= (long)ByteSizeUnit.PebiByte)
            return ByteSizeUnit.PebiByte;

        if (Value >= (long)ByteSizeUnit.TebiByte)
            return ByteSizeUnit.TebiByte;

        if (Value >= (long)ByteSizeUnit.GibiByte)
            return ByteSizeUnit.GibiByte;

        if (Value >= (long)ByteSizeUnit.MebiByte)
            return ByteSizeUnit.MebiByte;

        if (Value >= (long)ByteSizeUnit.KibiByte)
            return ByteSizeUnit.KibiByte;

        return ByteSizeUnit.Byte;
    }

    public double GetValue(ByteSizeUnit unit)
    {
        return (double)Value / (long)unit;
    }

    private static string UnitToString(ByteSizeUnit unit)
    {
        return unit switch
        {
            ByteSizeUnit.Byte => "B",
            ByteSizeUnit.KiloByte => "kB",
            ByteSizeUnit.MegaByte => "MB",
            ByteSizeUnit.GigaByte => "GB",
            ByteSizeUnit.TeraByte => "TB",
            ByteSizeUnit.PetaByte => "PB",
            ByteSizeUnit.ExaByte => "EB",
            ByteSizeUnit.KibiByte => "kiB",
            ByteSizeUnit.MebiByte => "MiB",
            ByteSizeUnit.GibiByte => "GiB",
            ByteSizeUnit.TebiByte => "TiB",
            ByteSizeUnit.PebiByte => "PiB",
            ByteSizeUnit.ExbiByte => "EiB",
            _ => throw new ArgumentOutOfRangeException(nameof(unit)),
        };
    }

    public static bool operator ==(ByteSize value1, ByteSize value2) => value1.Equals(value2);

    public static bool operator !=(ByteSize value1, ByteSize value2) => !(value1 == value2);

    public static bool operator <=(ByteSize value1, ByteSize value2) => value1.CompareTo(value2) <= 0;

    public static bool operator >=(ByteSize value1, ByteSize value2) => value1.CompareTo(value2) >= 0;

    public static bool operator <(ByteSize value1, ByteSize value2) => value1.CompareTo(value2) < 0;

    public static bool operator >(ByteSize value1, ByteSize value2) => value1.CompareTo(value2) > 0;

    public static ByteSize operator +(ByteSize value1, ByteSize value2) => new(value1.Value + value2.Value);
    public static ByteSize operator -(ByteSize value1, ByteSize value2) => new(value1.Value - value2.Value);
    public static ByteSize operator *(ByteSize value1, ByteSize value2) => new(value1.Value * value2.Value);
    public static ByteSize operator /(ByteSize value1, ByteSize value2) => new(value1.Value / value2.Value);

    public static implicit operator ByteSize(long value) => new(value);

    public ByteSize Add(ByteSize other) => this + other;
    public ByteSize Substract(ByteSize other) => this - other;
    public ByteSize Multiply(ByteSize other) => this * other;
    public ByteSize Divide(ByteSize other) => this / other;

    private static bool TryParseUnit(ReadOnlySpan<char> unit, out ByteSizeUnit result, out int parsedLength)
    {
        if (unit.IsEmpty || unit[^1] is not 'b' and not 'B')
        {
            result = default;
            parsedLength = 0;
            return false;
        }

        if (unit.Length > 1)
        {
            parsedLength = 2;
            var isI = false;
            var c = char.ToUpperInvariant(unit[^2]);
            if (c is 'I')
            {
                parsedLength = 3;
                if (unit.Length > 2)
                {
                    c = char.ToUpperInvariant(unit[^3]);
                    isI = true;
                }
                else
                {
                    result = default;
                    return false;
                }
            }

            switch (c)
            {
                case 'K':
                    result = isI ? ByteSizeUnit.KibiByte : ByteSizeUnit.KiloByte;
                    return true;

                case 'M':
                    result = isI ? ByteSizeUnit.MebiByte : ByteSizeUnit.MegaByte;
                    return true;

                case 'G':
                    result = isI ? ByteSizeUnit.GibiByte : ByteSizeUnit.GigaByte;
                    return true;

                case 'T':
                    result = isI ? ByteSizeUnit.TebiByte : ByteSizeUnit.TeraByte;
                    return true;

                case 'P':
                    result = isI ? ByteSizeUnit.PebiByte : ByteSizeUnit.PetaByte;
                    return true;
            }
        }

        parsedLength = 1;
        result = ByteSizeUnit.Byte;
        return true;
    }

    [SuppressMessage("Naming", "CA1725:Parameter names should match base declaration", Justification = "Would be a breaking change")]
    public static ByteSize Parse(string text, IFormatProvider? formatProvider)
    {
        if (TryParse(text, formatProvider, out var result))
            return result;

        throw new FormatException($"The value '{text}' is not valid");
    }

    public static bool TryParse(string text, out ByteSize result)
    {
        return TryParse(text, formatProvider: null, out result);
    }

    [SuppressMessage("Naming", "CA1725:Parameter names should match base declaration", Justification = "Would be a breaking change")]
    public static bool TryParse(string text, IFormatProvider? formatProvider, out ByteSize result)
    {
        return TryParse(text.AsSpan(), formatProvider, out result);
    }

    /// <summary>
    /// Parses a span of characters into a ByteSize value.
    /// </summary>
    /// <param name="s">The span of characters to parse.</param>
    /// <param name="provider">An object that provides culture-specific formatting information about s.</param>
    /// <returns>The result of parsing s.</returns>
    /// <exception cref="FormatException">s is not in the correct format.</exception>
    public static ByteSize Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        if (TryParse(s, provider, out var result))
            return result;

        throw new FormatException($"The value is not valid");
    }

    /// <summary>
    /// Tries to parse a span of characters into a ByteSize value.
    /// </summary>
    /// <param name="s">The span of characters to parse.</param>
    /// <param name="provider">An object that provides culture-specific formatting information about s.</param>
    /// <param name="result">When this method returns, contains the result of successfully parsing s, or an undefined value on failure.</param>
    /// <returns>true if s was successfully parsed; otherwise, false.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ByteSize result)
    {
        s = s.Trim();

        // Find unit
        ByteSizeUnit unit;
        if (TryParseUnit(s, out unit, out var unitLength))
        {
            s = s[..^unitLength];
        }
        else
        {
            unit = ByteSizeUnit.Byte;
        }

        // Convert number
#if NET7_0_OR_GREATER
        var valueToParse = s;
#else
        var valueToParse = s.ToString();
#endif
        if (long.TryParse(valueToParse, NumberStyles.Integer, provider, out var resultLong))
        {
            result = From(resultLong, unit);
            return true;
        }

        if (double.TryParse(valueToParse, NumberStyles.Float, provider, out var resultDouble))
        {
            result = From(resultDouble, unit);
            return true;
        }

        result = default;
        return false;
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Tries to format the value as UTF-8 into the provided span of bytes.
    /// </summary>
    /// <param name="utf8Destination">The span in which to write this instance's value formatted as UTF-8.</param>
    /// <param name="bytesWritten">When this method returns, contains the number of bytes that were written in utf8Destination.</param>
    /// <param name="format">A span containing the characters that represent a standard or custom format string.</param>
    /// <param name="provider">An optional object that supplies culture-specific formatting information.</param>
    /// <returns>true if the formatting was successful; otherwise, false.</returns>
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        // Determine the format and unit
        var formatString = format.IsEmpty ? "g" : format;

        var index = -1;
        for (var i = 0; i < formatString.Length; i++)
        {
            var c = formatString[i];
            if (c is >= '0' and <= '9')
            {
                index = i;
                break;
            }
        }

        var unitString = formatString;
        if (index >= 0)
        {
            unitString = formatString[..index];
        }

        if (!TryParseUnit(unitString, out var unit, out var parsedLength) || unitString.Length != parsedLength)
        {
            if (unitString.Equals("gi", StringComparison.OrdinalIgnoreCase) || unitString.Equals("fi", StringComparison.OrdinalIgnoreCase))
            {
                unit = FindBestUnitI();
            }
            else if (unitString is "g" or "f" or "G" or "F")
            {
                unit = FindBestUnit();
            }
            else
            {
                bytesWritten = 0;
                return false;
            }
        }

        var numberFormat = "G";
        if (index > 0)
        {
            if (!int.TryParse(formatString[index..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                bytesWritten = 0;
                return false;
            }

            numberFormat = "F" + number.ToString(CultureInfo.InvariantCulture);
        }

        // Format the value
        var value = GetValue(unit);
        var unitStr = UnitToString(unit);

        // Try to format the number part as UTF-8
        if (!value.TryFormat(utf8Destination, out var numberBytesWritten, numberFormat, provider))
        {
            bytesWritten = 0;
            return false;
        }

        // Check if there's enough space for the unit string (ASCII characters)
        if (utf8Destination.Length - numberBytesWritten < unitStr.Length)
        {
            bytesWritten = 0;
            return false;
        }

        // Copy the unit string as UTF-8 bytes (ASCII)
        for (var i = 0; i < unitStr.Length; i++)
        {
            utf8Destination[numberBytesWritten + i] = (byte)unitStr[i];
        }

        bytesWritten = numberBytesWritten + unitStr.Length;
        return true;
    }

    static bool IUtf8SpanParsable<ByteSize>.TryParse(ReadOnlySpan<byte> s, IFormatProvider? provider, out ByteSize result) => TryParse(s, out result);
    static ByteSize IUtf8SpanParsable<ByteSize>.Parse(ReadOnlySpan<byte> s, IFormatProvider? provider) => Parse(s);

    /// <summary>
    /// Parses a span of UTF-8 characters into a ByteSize value.
    /// </summary>
    /// <param name="utf8Text">The span of UTF-8 characters to parse.</param>
    /// <returns>The result of parsing utf8Text.</returns>
    /// <exception cref="FormatException">utf8Text is not in the correct format.</exception>
    public static ByteSize Parse(ReadOnlySpan<byte> utf8Text)
    {
        if (TryParse(utf8Text, out var result))
            return result;

        throw new FormatException($"The value is not valid");
    }

    /// <summary>
    /// Tries to parse a span of UTF-8 characters into a ByteSize value.
    /// </summary>
    /// <param name="utf8Text">The span of UTF-8 characters to parse.</param>
    /// <param name="result">When this method returns, contains the result of successfully parsing utf8Text, or an undefined value on failure.</param>
    /// <returns>true if utf8Text was successfully parsed; otherwise, false.</returns>
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out ByteSize result)
    {
        // Trim leading and trailing whitespace
        while (utf8Text.Length > 0 && IsWhitespace(utf8Text[0]))
        {
            utf8Text = utf8Text[1..];
        }
        while (utf8Text.Length > 0 && IsWhitespace(utf8Text[^1]))
        {
            utf8Text = utf8Text[..^1];
        }

        // Find unit by checking from the end
        if (TryParseUtf8Unit(utf8Text, out var unit, out var unitLength))
        {
            utf8Text = utf8Text[..^unitLength];

            // Trim trailing whitespace after removing unit
            while (utf8Text.Length > 0 && IsWhitespace(utf8Text[^1]))
            {
                utf8Text = utf8Text[..^1];
            }
        }
        else
        {
            unit = ByteSizeUnit.Byte;
        }

        // Convert number from UTF-8
        if (System.Buffers.Text.Utf8Parser.TryParse(utf8Text, out long resultLong, out var bytesConsumed) && bytesConsumed == utf8Text.Length)
        {
            result = From(resultLong, unit);
            return true;
        }

        if (System.Buffers.Text.Utf8Parser.TryParse(utf8Text, out double resultDouble, out bytesConsumed) && bytesConsumed == utf8Text.Length)
        {
            result = From(resultDouble, unit);
            return true;
        }

        result = default;
        return false;
    }

    private static bool IsWhitespace(byte b)
    {
        return b is 0x20 or 0x09 or 0x0A or 0x0D; // space, tab, LF, CR
    }

    private static bool TryParseUtf8Unit(ReadOnlySpan<byte> unit, out ByteSizeUnit result, out int parsedLength)
    {
        if (unit.IsEmpty || unit[^1] is not (byte)'b' and not (byte)'B')
        {
            result = default;
            parsedLength = 0;
            return false;
        }

        if (unit.Length > 1)
        {
            parsedLength = 2;
            var isI = false;
            var c = (char)unit[^2];
            c = char.ToUpperInvariant(c);
            if (c is 'I')
            {
                parsedLength = 3;
                if (unit.Length > 2)
                {
                    c = (char)unit[^3];
                    c = char.ToUpperInvariant(c);
                    isI = true;
                }
                else
                {
                    result = default;
                    return false;
                }
            }

            switch (c)
            {
                case 'K':
                    result = isI ? ByteSizeUnit.KibiByte : ByteSizeUnit.KiloByte;
                    return true;

                case 'M':
                    result = isI ? ByteSizeUnit.MebiByte : ByteSizeUnit.MegaByte;
                    return true;

                case 'G':
                    result = isI ? ByteSizeUnit.GibiByte : ByteSizeUnit.GigaByte;
                    return true;

                case 'T':
                    result = isI ? ByteSizeUnit.TebiByte : ByteSizeUnit.TeraByte;
                    return true;

                case 'P':
                    result = isI ? ByteSizeUnit.PebiByte : ByteSizeUnit.PetaByte;
                    return true;
            }
        }

        parsedLength = 1;
        result = ByteSizeUnit.Byte;
        return true;
    }
#endif

    public static ByteSize From(byte value, ByteSizeUnit unit) => new(value * (long)unit);
    public static ByteSize From(short value, ByteSizeUnit unit) => new(value * (long)unit);
    public static ByteSize From(int value, ByteSizeUnit unit) => new(value * (long)unit);
    public static ByteSize From(long value, ByteSizeUnit unit) => new(value * (long)unit);
    public static ByteSize From(float value, ByteSizeUnit unit) => new((long)(value * (long)unit));
    public static ByteSize From(double value, ByteSizeUnit unit) => new((long)(value * (long)unit));

    public static ByteSize FromFileLength(FileInfo fileInfo) => new(fileInfo.Length);
    public static ByteSize FromFileLength(string filePath) => FromFileLength(new FileInfo(filePath));
}
