using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Meziantou.Framework
{
    [StructLayout(LayoutKind.Auto)]
    public readonly partial struct FileLength : IEquatable<FileLength>, IComparable, IComparable<FileLength>, IFormattable
    {
        public FileLength(long length)
        {
            Length = length;
        }

        public long Length { get; }

        public override bool Equals(object? obj) => obj is FileLength fileLength && Equals(fileLength);

        public bool Equals(FileLength other) => Length == other.Length;

        public override int GetHashCode() => Length.GetHashCode();

        public int CompareTo(FileLength other) => Length.CompareTo(other.Length);

        public int CompareTo(object? obj)
        {
            if (obj == null)
                return 1;

            var fileLength = (FileLength)obj;
            return CompareTo(fileLength);
        }

        public override string ToString() => ToString(format: null, formatProvider: null);

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                return Length.ToString(formatProvider);

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

        private FileLengthUnit FindUnit()
        {
            if (Length >= (long)FileLengthUnit.ExaByte)
                return FileLengthUnit.ExaByte;

            if (Length >= (long)FileLengthUnit.PetaByte)
                return FileLengthUnit.PetaByte;

            else if (Length >= (long)FileLengthUnit.TeraByte)
                return FileLengthUnit.TeraByte;

            else if (Length >= (long)FileLengthUnit.GigaByte)
                return FileLengthUnit.GigaByte;

            else if (Length >= (long)FileLengthUnit.MegaByte)
                return FileLengthUnit.MegaByte;

            else if (Length >= (long)FileLengthUnit.KiloByte)
                return FileLengthUnit.KiloByte;

            return FileLengthUnit.Byte;
        }

        private FileLengthUnit GetUnitI()
        {
            if (Length >= (long)FileLengthUnit.ExbiByte)
                return FileLengthUnit.ExbiByte;

            if (Length >= (long)FileLengthUnit.PebiByte)
                return FileLengthUnit.PebiByte;

            if (Length >= (long)FileLengthUnit.TebiByte)
                return FileLengthUnit.TebiByte;

            if (Length >= (long)FileLengthUnit.GibiByte)
                return FileLengthUnit.GibiByte;

            if (Length >= (long)FileLengthUnit.MebiByte)
                return FileLengthUnit.MebiByte;

            if (Length >= (long)FileLengthUnit.KibiByte)
                return FileLengthUnit.KibiByte;

            return FileLengthUnit.Byte;
        }

        public double GetLength(FileLengthUnit unit)
        {
            return (double)Length / (long)unit;
        }

        private static string UnitToString(FileLengthUnit unit)
        {
            return unit switch
            {
                FileLengthUnit.Byte => "B",
                FileLengthUnit.KiloByte => "kB",
                FileLengthUnit.MegaByte => "MB",
                FileLengthUnit.GigaByte => "GB",
                FileLengthUnit.TeraByte => "TB",
                FileLengthUnit.PetaByte => "PB",
                FileLengthUnit.ExaByte => "EB",
                FileLengthUnit.KibiByte => "kiB",
                FileLengthUnit.MebiByte => "MiB",
                FileLengthUnit.GibiByte => "GiB",
                FileLengthUnit.TebiByte => "TiB",
                FileLengthUnit.PebiByte => "PiB",
                FileLengthUnit.ExbiByte => "EiB",
                _ => throw new ArgumentOutOfRangeException(nameof(unit)),
            };
        }

        public static bool operator ==(FileLength length1, FileLength length2) => length1.Equals(length2);

        public static bool operator !=(FileLength length1, FileLength length2) => !(length1 == length2);

        public static bool operator <=(FileLength length1, FileLength length2) => length1.CompareTo(length2) <= 0;

        public static bool operator >=(FileLength length1, FileLength length2) => length1.CompareTo(length2) >= 0;

        public static bool operator <(FileLength length1, FileLength length2) => length1.CompareTo(length2) < 0;

        public static bool operator >(FileLength length1, FileLength length2) => length1.CompareTo(length2) > 0;

        public static implicit operator long(FileLength fileLength) => fileLength.Length;

        public static implicit operator FileLength(long fileLength) => new FileLength(fileLength);

        private static bool TryParseUnit(string unit, out FileLengthUnit result)
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
                        result = FileLengthUnit.KiloByte;
                        return true;

                    case "M":
                        result = FileLengthUnit.MegaByte;
                        return true;

                    case "G":
                        result = FileLengthUnit.GigaByte;
                        return true;

                    case "T":
                        result = FileLengthUnit.TeraByte;
                        return true;

                    case "P":
                        result = FileLengthUnit.PetaByte;
                        return true;

                    case "KI":
                        result = FileLengthUnit.KibiByte;
                        return true;

                    case "MI":
                        result = FileLengthUnit.MebiByte;
                        return true;

                    case "GI":
                        result = FileLengthUnit.GibiByte;
                        return true;

                    case "TI":
                        result = FileLengthUnit.TebiByte;
                        return true;

                    case "PI":
                        result = FileLengthUnit.PebiByte;
                        return true;
                }
            }

            result = FileLengthUnit.Byte;
            return true;
        }

        public static FileLength From(int value, FileLengthUnit unit) => new FileLength(value * (long)unit);
        public static FileLength From(long value, FileLengthUnit unit) => new FileLength(value * (long)unit);
        public static FileLength From(float value, FileLengthUnit unit) => new FileLength((long)(value * (long)unit));
        public static FileLength From(double value, FileLengthUnit unit) => new FileLength((long)(value * (long)unit));

        public static FileLength FromFile(FileInfo fileInfo) => new FileLength(fileInfo.Length);
        public static FileLength FromFile(string filePath) => FromFile(new FileInfo(filePath));
    }
}
