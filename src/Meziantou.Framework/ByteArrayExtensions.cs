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
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            if (bytes.Length == 0)
                return string.Empty;

            return options switch
            {
                HexaOptions.LowerCase => ToHexaLowerCase(bytes),
                HexaOptions.UpperCase => ToHexaUpperCase(bytes),
                _ => throw new ArgumentOutOfRangeException(nameof(options)),
            };
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

        [Obsolete("Use ParseHexa")]
        public static byte[] FromHexa(string str)
        {
            return ParseHexa(str);
        }

        public static byte[] ParseHexa(string str)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));

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

        public static bool TryParseHexa(string str, out byte[] result)
        {
            if (str == null || str.Length % 2 != 0)
            {
                result = default;
                return false;
            }

            // handle 0x or 0X notation
            if (str.Length >= 2 && str[0] == '0' && (str[1] == 'x' || str[1] == 'X'))
            {
                const int prefixLength = 2;
                var length = (str.Length / 2) - 1;
                result = new byte[length];
                for (var i = 0; i < length; i++)
                {
                    int value1;
                    int value2;
                    if (!TryGetHexValue(str[i * 2 + prefixLength], out value1) || !TryGetHexValue(str[(i * 2) + 1 + prefixLength], out value2))
                    {
                        result = default;
                        return false;
                    }

                    result[i] = (byte)(value1 << 4);
                    result[i] += (byte)value2;
                }

                return true;
            }
            else
            {
                var length = str.Length / 2;
                result = new byte[length];
                int value1;
                int value2;
                for (var i = 0; i < length; i++)
                {
                    if (!TryGetHexValue(str[i * 2], out value1) || !TryGetHexValue(str[(i * 2) + 1], out value2))
                    {
                        result = default;
                        return false;
                    }

                    result[i] = (byte)(value1 << 4);
                    result[i] += (byte)value2;
                }

                return true;
            }
        }

#if NETCOREAPP2_1
        public static string ToHexa(this ReadOnlySpan<byte> bytes)
        {
            return ToHexa(bytes, default);
        }

        public static string ToHexa(this ReadOnlySpan<byte> bytes, HexaOptions options)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

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

        private static string ToHexaUpperCase(this ReadOnlySpan<byte> bytes)
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

        private static string ToHexaLowerCase(this ReadOnlySpan<byte> bytes)
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

        public static bool TryParseHexa(string str, Span<byte> bytes)
        {
            if (str == null || str.Length % 2 != 0)
            {
                bytes = default;
                return false;
            }

            // handle 0x or 0X notation
            if (str.Length >= 2 && str[0] == '0' && (str[1] == 'x' || str[1] == 'X'))
            {
                const int prefixLength = 2;
                var length = (str.Length / 2) - 1;
                if (length > bytes.Length)
                    return false;

                int value1;
                int value2;
                for (var i = 0; i < length; i++)
                {
                    if (!TryGetHexValue(str[i * 2 + prefixLength], out value1) || !TryGetHexValue(str[(i * 2) + 1 + prefixLength], out value2))
                        return false;

                    bytes[i] = (byte)(value1 << 4);
                    bytes[i] += (byte)value2;
                }

                return true;
            }
            else
            {
                var length = str.Length / 2;
                if (length > bytes.Length)
                    return false;

                int value1;
                int value2;
                for (var i = 0; i < length; i++)
                {
                    if (!TryGetHexValue(str[i * 2], out value1) || !TryGetHexValue(str[(i * 2) + 1], out value2))
                        return false;

                    bytes[i] = (byte)(value1 << 4);
                    bytes[i] += (byte)value2;
                }

                return true;
            }
        }
#endif

        private static bool TryGetHexValue(char c, out int value)
        {
            const int digit = '0';
            const int lowerCase = 'a' - 10;
            const int upperCase = 'A' - 10;

            if (c >= '0' && c <= '9')
            {
                value = c - digit;
                return true;
            }

            if (c >= 'A' && c <= 'F') // Upper case
            {
                value = c - upperCase;
                return true;
            }

            if (c >= 'a' && c <= 'f') // Upper case
            {
                value = c - lowerCase;
                return true;
            }

            value = default;
            return false;
        }
    }
}
