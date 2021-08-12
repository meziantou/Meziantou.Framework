using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

[StructLayout(LayoutKind.Auto)]
public readonly partial struct ByteSize : IEquatable<ByteSize>, IComparable, IComparable<ByteSize>, IFormattable
{
    public ByteSize(long length)
    {
        Value = length;
    }

    public long Value { get; }

    public override bool Equals(object? obj) => obj is ByteSize byteSize && Equals(byteSize);

    public bool Equals(ByteSize other) => Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public int CompareTo(ByteSize other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj)
    {
        if (obj == null)
            return 1;

        var fileLength = (ByteSize)obj;
        return CompareTo(fileLength);
    }

    public override string ToString() => ToString(format: null, formatProvider: null);

    public string ToString(ByteSizeUnit unit) => ToString(unit, formatProvider: null);

    public string ToString(ByteSizeUnit unit, IFormatProvider? formatProvider) => GetValue(unit).ToString(formatProvider) + UnitToString(unit);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        if (string.IsNullOrEmpty(format))
            return Value.ToString(formatProvider) + "B";

        var index = -1;
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c > '0' && c < '9')
            {
                index = i;
                break;
            }
        }

        var unitString = format;
        if (index >= 0)
        {
            unitString = format.Substring(0, index);
        }

        if (!TryParseUnit(unitString, out var unit, out var parsedLength) || unitString.Length != parsedLength)
        {
            if (unitString == "fi")
            {
                unit = FindBestUnitI();
            }
            else if (unitString == "f")
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

    public static implicit operator ByteSize(long value) => new(value);

    private static bool TryParseUnit(string unit, out ByteSizeUnit result, out int parsedLength)
    {
        var last = unit[^1];
        if (last != 'b' && last != 'B')
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
            if (c == 'i' || c == 'I')
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

    public static bool TryParse(string text, IFormatProvider? formatProvider, out ByteSize result)
    {
        text = text.Trim();

        // Find unit
        if (TryParseUnit(text, out var unit, out var unitLength))
        {
            text = text.Substring(0, text.Length - unitLength);
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

    public static ByteSize From(byte value, ByteSizeUnit unit) => new(value * (long)unit);
    public static ByteSize From(short value, ByteSizeUnit unit) => new(value * (long)unit);
    public static ByteSize From(int value, ByteSizeUnit unit) => new(value * (long)unit);
    public static ByteSize From(long value, ByteSizeUnit unit) => new(value * (long)unit);
    public static ByteSize From(float value, ByteSizeUnit unit) => new((long)(value * (long)unit));
    public static ByteSize From(double value, ByteSizeUnit unit) => new((long)(value * (long)unit));

    public static ByteSize FromFileLength(FileInfo fileInfo) => new(fileInfo.Length);
    public static ByteSize FromFileLength(string filePath) => FromFileLength(new FileInfo(filePath));
}
