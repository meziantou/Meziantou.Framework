using System;
using System.Diagnostics.CodeAnalysis;

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

#if NETCOREAPP3_1
        private static string ToHexaUpperCase(this byte[] bytes)
        {
            return string.Create(bytes.Length * 2, bytes, (span, state) =>
            {
                const int AddToAlpha = 55;
                const int AddToDigit = -7;

                for (var i = 0; i < state.Length; i++)
                {
                    var b = state[i] >> 4;
                    span[i * 2] = (char)(AddToAlpha + b + (((b - 10) >> 31) & AddToDigit));

                    b = state[i] & 0xF;
                    span[(i * 2) + 1] = (char)(AddToAlpha + b + (((b - 10) >> 31) & AddToDigit));
                }
            });
        }

        private static string ToHexaLowerCase(this byte[] bytes)
        {
            return string.Create(bytes.Length * 2, bytes, (span, state) =>
            {
                const int AddToAlpha = 87;
                const int AddToDigit = -39;

                for (var i = 0; i < state.Length; i++)
                {
                    var b = state[i] >> 4;
                    span[i * 2] = (char)(AddToAlpha + b + (((b - 10) >> 31) & AddToDigit));

                    b = state[i] & 0xF;
                    span[(i * 2) + 1] = (char)(AddToAlpha + b + (((b - 10) >> 31) & AddToDigit));
                }
            });
        }
#elif NET461 || NETSTANDARD2_0
        private static string ToHexaUpperCase(this byte[] bytes)
        {
            const int AddToAlpha = 55;
            const int AddToDigit = -7;

            var c = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i] >> 4;
                c[i * 2] = (char)(AddToAlpha + b + (((b - 10) >> 31) & AddToDigit));

                b = bytes[i] & 0xF;
                c[(i * 2) + 1] = (char)(AddToAlpha + b + (((b - 10) >> 31) & AddToDigit));
            }

            return new string(c);
        }

        private static string ToHexaLowerCase(this byte[] bytes)
        {
            const int AddToAlpha = 87;
            const int AddToDigit = -39;

            var c = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i] >> 4;
                c[i * 2] = (char)(AddToAlpha + b + (((b - 10) >> 31) & AddToDigit));

                b = bytes[i] & 0xF;
                c[(i * 2) + 1] = (char)(AddToAlpha + b + (((b - 10) >> 31) & AddToDigit));
            }

            return new string(c);
        }
#else
#error plateform not supported
#endif
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
                const int PrefixLength = 2;
                var b = new byte[(str.Length / 2) - 1];
                for (var i = 0; i < (str.Length / 2) - 1; i++)
                {
                    var c = str[(i * 2) + PrefixLength];
                    b[i] = (byte)(GetInt(c) << 4);
                    c = str[(i * 2) + 1 + PrefixLength];
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
                const int Digit = '0';
                const int LowerCase = 'a' - 10;
                const int UpperCase = 'A' - 10;

                if (c >= '0' && c <= '9')
                    return c - Digit;

                if (c >= 'A' && c <= 'F') // Upper case
                    return c - UpperCase;

                if (c >= 'a' && c <= 'f') // Upper case
                    return c - LowerCase;

                throw new ArgumentException($"Invalid character '{c}'", nameof(str));
            }
        }

        public static bool TryParseHexa(string? str, [NotNullWhen(returnValue: true)]out byte[]? result)
        {
            if (str == null || str.Length % 2 != 0)
            {
                result = default;
                return false;
            }

            // handle 0x or 0X notation
            if (str.Length >= 2 && str[0] == '0' && (str[1] == 'x' || str[1] == 'X'))
            {
                const int PrefixLength = 2;
                var length = (str.Length / 2) - 1;
                result = new byte[length];
                for (var i = 0; i < length; i++)
                {
                    if (!TryGetHexValue(str[i * 2 + PrefixLength], out var value1) || !TryGetHexValue(str[(i * 2) + 1 + PrefixLength], out var value2))
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
                for (var i = 0; i < length; i++)
                {
                    if (!TryGetHexValue(str[i * 2], out var value1) || !TryGetHexValue(str[(i * 2) + 1], out var value2))
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

#if NETCOREAPP3_1
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

            return options switch
            {
                HexaOptions.LowerCase => ToHexaLowerCase(bytes),
                HexaOptions.UpperCase => ToHexaUpperCase(bytes),
                _ => throw new ArgumentOutOfRangeException(nameof(options)),
            };
        }

        private static string ToHexaUpperCase(this ReadOnlySpan<byte> bytes)
        {
            const int AddToAlpha = 55;
            const int AddToDigit = -7;

            var c = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i] >> 4;
                c[i * 2] = (char)(AddToAlpha + b + (((b - 10) >> 31) & AddToDigit));

                b = bytes[i] & 0xF;
                c[(i * 2) + 1] = (char)(AddToAlpha + b + (((b - 10) >> 31) & AddToDigit));
            }

            return new string(c);
        }

        private static string ToHexaLowerCase(this ReadOnlySpan<byte> bytes)
        {
            const int AddToAlpha = 87;
            const int AddToDigit = -39;

            var c = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i] >> 4;
                c[i * 2] = (char)(AddToAlpha + b + (((b - 10) >> 31) & AddToDigit));

                b = bytes[i] & 0xF;
                c[(i * 2) + 1] = (char)(AddToAlpha + b + (((b - 10) >> 31) & AddToDigit));
            }

            return new string(c);
        }

        public static bool TryParseHexa(string? str, Span<byte> bytes)
        {
            if (str == null || str.Length % 2 != 0)
                return false;

            // handle 0x or 0X notation
            if (str.Length >= 2 && str[0] == '0' && (str[1] == 'x' || str[1] == 'X'))
            {
                const int PrefixLength = 2;
                var length = (str.Length / 2) - 1;
                if (length > bytes.Length)
                    return false;

                for (var i = 0; i < length; i++)
                {
                    if (!TryGetHexValue(str[i * 2 + PrefixLength], out var value1) || !TryGetHexValue(str[(i * 2) + 1 + PrefixLength], out var value2))
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

                for (var i = 0; i < length; i++)
                {
                    if (!TryGetHexValue(str[i * 2], out var value1) || !TryGetHexValue(str[(i * 2) + 1], out var value2))
                        return false;

                    bytes[i] = (byte)(value1 << 4);
                    bytes[i] += (byte)value2;
                }

                return true;
            }
        }
#elif NETSTANDARD2_0 || NET461
#else
#error Platform not supported
#endif

        private static bool TryGetHexValue(char c, out int value)
        {
            const int Digit = '0';
            const int LowerCase = 'a' - 10;
            const int UpperCase = 'A' - 10;

            if (c >= '0' && c <= '9')
            {
                value = c - Digit;
                return true;
            }

            if (c >= 'A' && c <= 'F') // Upper case
            {
                value = c - UpperCase;
                return true;
            }

            if (c >= 'a' && c <= 'f') // Upper case
            {
                value = c - LowerCase;
                return true;
            }

            value = default;
            return false;
        }
    }
}
