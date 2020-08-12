using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Meziantou.Framework
{
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

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                return Value.ToString(formatProvider);

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

            if (!TryParseUnit(unitString, out var unit))
            {
                if (unitString == "fi")
                {
                    unit = GetUnitI();
                }
                else if (unitString == "f")
                {
                    unit = FindUnit();
                }
                else
                {
                    throw new ArgumentException($"format '{format}' is invalid", nameof(format));
                }
            }

            var numberFormat = "G";
            if (index > 0)
            {
                if (!int.TryParse(format.Substring(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    throw new ArgumentException($"format '{format}' is invalid", nameof(format));

                numberFormat = "F" + number.ToString(CultureInfo.InvariantCulture);
            }

            return GetLength(unit).ToString(numberFormat, formatProvider) + UnitToString(unit);
        }

        private ByteSizeUnit FindUnit()
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

        private ByteSizeUnit GetUnitI()
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

        public double GetLength(ByteSizeUnit unit)
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

        public static implicit operator ByteSize(long value) => new ByteSize(value);

        private static bool TryParseUnit(string unit, out ByteSizeUnit result)
        {
            var last = unit[unit.Length - 1];
            if (last != 'b' && last != 'B')
            {
                result = default;
                return false;
            }

            if (unit.Length > 1)
            {
                var multiple = unit.Substring(0, unit.Length - 1);
                switch (multiple.ToUpperInvariant())
                {
                    case "K":
                        result = ByteSizeUnit.KiloByte;
                        return true;

                    case "M":
                        result = ByteSizeUnit.MegaByte;
                        return true;

                    case "G":
                        result = ByteSizeUnit.GigaByte;
                        return true;

                    case "T":
                        result = ByteSizeUnit.TeraByte;
                        return true;

                    case "P":
                        result = ByteSizeUnit.PetaByte;
                        return true;

                    case "KI":
                        result = ByteSizeUnit.KibiByte;
                        return true;

                    case "MI":
                        result = ByteSizeUnit.MebiByte;
                        return true;

                    case "GI":
                        result = ByteSizeUnit.GibiByte;
                        return true;

                    case "TI":
                        result = ByteSizeUnit.TebiByte;
                        return true;

                    case "PI":
                        result = ByteSizeUnit.PebiByte;
                        return true;
                }
            }

            result = ByteSizeUnit.Byte;
            return true;
        }

        public static ByteSize From(byte value, ByteSizeUnit unit) => new ByteSize(value * (long)unit);
        public static ByteSize From(short value, ByteSizeUnit unit) => new ByteSize(value * (long)unit);
        public static ByteSize From(int value, ByteSizeUnit unit) => new ByteSize(value * (long)unit);
        public static ByteSize From(long value, ByteSizeUnit unit) => new ByteSize(value * (long)unit);
        public static ByteSize From(float value, ByteSizeUnit unit) => new ByteSize((long)(value * (long)unit));
        public static ByteSize From(double value, ByteSizeUnit unit) => new ByteSize((long)(value * (long)unit));

        public static ByteSize FromFileLength(FileInfo fileInfo) => new ByteSize(fileInfo.Length);
        public static ByteSize FromFileLength(string filePath) => FromFileLength(new FileInfo(filePath));
    }
}
