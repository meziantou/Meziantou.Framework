using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// Represents a byte size value with support for various units and formatting options.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly partial struct ByteSize : IEquatable<ByteSize>, IComparable, IComparable<ByteSize>, IFormattable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ByteSize"/> struct with the specified byte value.
    /// </summary>
    /// <param name="length">The size in bytes.</param>
    public ByteSize(long length)
    {
        Value = length;
    }

    /// <summary>
    /// Gets the size value in bytes.
    /// </summary>
    public long Value { get; }

    /// <summary>
    /// Gets a <see cref="ByteSize"/> value representing zero bytes.
    /// </summary>
    public static ByteSize Zero => new(0L);

    /// <summary>
    /// Gets a <see cref="ByteSize"/> value representing the maximum possible byte size.
    /// </summary>
    public static ByteSize MaxValue => new(long.MaxValue);

    /// <summary>
    /// Gets a <see cref="ByteSize"/> value representing the minimum possible byte size.
    /// </summary>
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

    /// <summary>
    /// Converts the byte size to its string representation using the specified unit.
    /// </summary>
    /// <param name="unit">The unit to use for conversion.</param>
    public string ToString(ByteSizeUnit unit) => ToString(unit, formatProvider: null);

    /// <summary>
    /// Converts the byte size to its string representation using the specified unit and format provider.
    /// </summary>
    /// <param name="unit">The unit to use for conversion.</param>
    /// <param name="formatProvider">The format provider to use for formatting numbers.</param>
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
            if (c is > '0' and < '9')
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

    /// <summary>
    /// Gets the byte size value converted to the specified unit.
    /// </summary>
    /// <param name="unit">The unit to convert to.</param>
    /// <returns>The value in the specified unit.</returns>
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

    /// <summary>
    /// Adds two byte sizes together.
    /// </summary>
    /// <param name="other">The byte size to add.</param>
    /// <returns>The sum of the two byte sizes.</returns>
    public ByteSize Add(ByteSize other) => this + other;

    /// <summary>
    /// Subtracts a byte size from this byte size.
    /// </summary>
    /// <param name="other">The byte size to subtract.</param>
    /// <returns>The difference of the two byte sizes.</returns>
    public ByteSize Substract(ByteSize other) => this - other;

    /// <summary>
    /// Multiplies two byte sizes together.
    /// </summary>
    /// <param name="other">The byte size to multiply by.</param>
    /// <returns>The product of the two byte sizes.</returns>
    public ByteSize Multiply(ByteSize other) => this * other;

    /// <summary>
    /// Divides this byte size by another byte size.
    /// </summary>
    /// <param name="other">The byte size to divide by.</param>
    /// <returns>The quotient of the two byte sizes.</returns>
    public ByteSize Divide(ByteSize other) => this / other;

    private static bool TryParseUnit(string unit, out ByteSizeUnit result, out int parsedLength)
    {
        if (unit[^1] is not 'b' and not 'B')
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
            if (c is 'i' or 'I')
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

    /// <summary>
    /// Converts the string representation of a byte size to its <see cref="ByteSize"/> equivalent.
    /// </summary>
    /// <param name="text">A string containing a byte size to convert.</param>
    /// <param name="formatProvider">An object that supplies culture-specific formatting information.</param>
    /// <returns>A <see cref="ByteSize"/> equivalent to the byte size contained in <paramref name="text"/>.</returns>
    /// <exception cref="FormatException">The string is not a valid byte size representation.</exception>
    public static ByteSize Parse(string text, IFormatProvider? formatProvider)
    {
        if (TryParse(text, formatProvider, out var result))
            return result;

        throw new FormatException($"The value '{text}' is not valid");
    }

    /// <summary>
    /// Tries to convert the string representation of a byte size to its <see cref="ByteSize"/> equivalent.
    /// </summary>
    /// <param name="text">A string containing a byte size to convert.</param>
    /// <param name="result">When this method returns, contains the <see cref="ByteSize"/> value equivalent to the byte size contained in <paramref name="text"/>, if the conversion succeeded, or default if the conversion failed.</param>
    /// <returns><see langword="true"/> if the conversion was successful; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string text, out ByteSize result)
    {
        return TryParse(text, formatProvider: null, out result);
    }

    /// <summary>
    /// Tries to convert the string representation of a byte size to its <see cref="ByteSize"/> equivalent using the specified format provider.
    /// </summary>
    /// <param name="text">A string containing a byte size to convert.</param>
    /// <param name="formatProvider">An object that supplies culture-specific formatting information.</param>
    /// <param name="result">When this method returns, contains the <see cref="ByteSize"/> value equivalent to the byte size contained in <paramref name="text"/>, if the conversion succeeded, or default if the conversion failed.</param>
    /// <returns><see langword="true"/> if the conversion was successful; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string text, IFormatProvider? formatProvider, out ByteSize result)
    {
        text = text.Trim();

        // Find unit
        if (TryParseUnit(text, out var unit, out var unitLength))
        {
            text = text[..^unitLength];
        }
        else
        {
            unit = ByteSizeUnit.Byte;
        }

        // Convert number
        if (long.TryParse(text, NumberStyles.Integer, formatProvider, out var resultLong))
        {
            result = From(resultLong, unit);
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, formatProvider, out var resultDouble))
        {
            result = From(resultDouble, unit);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Creates a <see cref="ByteSize"/> from a byte value and unit.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="unit">The unit of the value.</param>
    public static ByteSize From(byte value, ByteSizeUnit unit) => new(value * (long)unit);

    /// <summary>
    /// Creates a <see cref="ByteSize"/> from a short value and unit.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="unit">The unit of the value.</param>
    public static ByteSize From(short value, ByteSizeUnit unit) => new(value * (long)unit);

    /// <summary>
    /// Creates a <see cref="ByteSize"/> from an int value and unit.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="unit">The unit of the value.</param>
    public static ByteSize From(int value, ByteSizeUnit unit) => new(value * (long)unit);

    /// <summary>
    /// Creates a <see cref="ByteSize"/> from a long value and unit.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="unit">The unit of the value.</param>
    public static ByteSize From(long value, ByteSizeUnit unit) => new(value * (long)unit);

    /// <summary>
    /// Creates a <see cref="ByteSize"/> from a float value and unit.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="unit">The unit of the value.</param>
    public static ByteSize From(float value, ByteSizeUnit unit) => new((long)(value * (long)unit));

    /// <summary>
    /// Creates a <see cref="ByteSize"/> from a double value and unit.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="unit">The unit of the value.</param>
    public static ByteSize From(double value, ByteSizeUnit unit) => new((long)(value * (long)unit));

    /// <summary>
    /// Creates a <see cref="ByteSize"/> from a file's length.
    /// </summary>
    /// <param name="fileInfo">The file information.</param>
    public static ByteSize FromFileLength(FileInfo fileInfo) => new(fileInfo.Length);

    /// <summary>
    /// Creates a <see cref="ByteSize"/> from a file's length.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    public static ByteSize FromFileLength(string filePath) => FromFileLength(new FileInfo(filePath));
}
