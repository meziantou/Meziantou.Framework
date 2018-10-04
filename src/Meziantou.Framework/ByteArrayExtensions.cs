using System;

namespace Meziantou.Framework
{
    public static class ByteArrayExtensions
    {
        public static string ToHexa(this byte[] bytes)
        {
            return ToHexa(bytes, default);
        }

        public static string ToHexa(this byte[] bytes, HexaOptions options)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            if (bytes.Length == 0)
                return string.Empty;

            switch (options)
            {
                case HexaOptions.LowerCase:
                    return ToHexaLowerCase(bytes);
                case HexaOptions.UpperCase:
                    return ToHexaUpperCase(bytes);
                default:
                    throw new ArgumentOutOfRangeException(nameof(options));
            }
        }

        private static string ToHexaUpperCase(this byte[] bytes)
        {
            const int addToAlpha = 55;
            const int addToDigit = -7;

            var c = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i] >> 4;
                c[i * 2] = (char)(addToAlpha + b + (((b - 10) >> 31) & addToDigit));

                b = bytes[i] & 0xF;
                c[(i * 2) + 1] = (char)(addToAlpha + b + (((b - 10) >> 31) & addToDigit));
            }

            return new string(c);
        }

        private static string ToHexaLowerCase(this byte[] bytes)
        {
            const int addToAlpha = 87;
            const int addToDigit = -39;

            var c = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i] >> 4;
                c[i * 2] = (char)(addToAlpha + b + (((b - 10) >> 31) & addToDigit));

                b = bytes[i] & 0xF;
                c[(i * 2) + 1] = (char)(addToAlpha + b + (((b - 10) >> 31) & addToDigit));
            }

            return new string(c);
        }

        public static byte[] FromHexa(string str)
        {
            if (str == null) throw new ArgumentNullException(nameof(str));

            if (str.Length % 2 != 0)
                throw new ArgumentException("Invalid string length", nameof(str));

            // handle 0x or 0X notation
            if (str.Length >= 2 && str[0] == '0' && (str[1] == 'x' || str[1] == 'X'))
            {
                const int prefixLength = 2;
                var b = new byte[(str.Length / 2) - 1];
                for (var i = 0; i < (str.Length / 2) - 1; i++)
                {
                    var c = str[(i * 2) + prefixLength];
                    b[i] = (byte)(GetInt(c) << 4);
                    c = str[(i * 2) + 1 + prefixLength];
                    b[i] += (byte)GetInt(c);
                }

                return b;
            }
            else
            {
                var b = new byte[(str.Length / 2)];
                for (var i = 0; i < str.Length / 2; i++)
                {
                    var c = str[i * 2];
                    b[i] = (byte)(GetInt(c) << 4);
                    c = str[(i * 2) + 1];
                    b[i] += (byte)GetInt(c);
                }

                return b;
            }

            int GetInt(char c)
            {
                const int digit = '0';
                const int lowerCase = 'a' - 10;
                const int upperCase = 'A' - 10;

                if (c >= '0' && c <= '9')
                    return c - digit;

                if (c >= 'A' && c <= 'F') // Upper case
                    return c - upperCase;

                if (c >= 'a' && c <= 'f') // Upper case
                    return c - lowerCase;

                throw new ArgumentException($"Invalid character '{c}'", nameof(str));
            }
        }
    }
}
