using System;
using System.Globalization;
using System.IO;

namespace Meziantou.Framework
{
    public readonly struct FileLength : IEquatable<FileLength>, IComparable, IComparable<FileLength>, IFormattable
    {
        public FileLength(long length)
        {
            Length = length;
        }

        public FileLength(string filePath)
        {
            Length = new FileInfo(filePath).Length;
        }

        public long Length { get; }

        public override bool Equals(object obj) => obj is FileLength fileLength && Equals(fileLength);

        public bool Equals(FileLength other) => Length == other.Length;

        public override int GetHashCode() => Length.GetHashCode();

        public int CompareTo(FileLength other) => Length.CompareTo(other.Length);

        public int CompareTo(object obj)
        {
            var fileLength = (FileLength)obj;
            return CompareTo(fileLength);
        }

        public override string ToString() => ToString(format: null, formatProvider: null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                return Length.ToString(formatProvider);

            var index = -1;
            for (var i = 0; i < format.Length; i++)
            {
                char c = format[i];
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
                throw new ArgumentException("format is invalid", nameof(format));

            var numberFormat = "G";
            if (index > 0)
            {
                if (!int.TryParse(format.Substring(index), out var number))
                    throw new ArgumentException("format is invalid", nameof(format));

                numberFormat = "F" + number.ToString(CultureInfo.InvariantCulture);
            }

            return GetLength(unit).ToString(numberFormat);
        }

        public double GetLength(FileLengthUnit unit)
        {
            return (double)Length / (long)unit;
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
    }
}
