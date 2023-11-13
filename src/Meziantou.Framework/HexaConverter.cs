namespace Meziantou.Framework;

public static class HexaConverter
{
    public static string ToHexaString(this byte[] bytes)
    {
        return ToHexaString(bytes, default);
    }

    public static string ToHexaString(this byte[] bytes, HexaOptions options)
    {
        return ToHexaString(bytes.AsSpan(), options);
    }

    public static string ToHexaString(this ReadOnlySpan<byte> bytes)
    {
        return ToHexaString(bytes, default);
    }

    public static string ToHexaString(this ReadOnlySpan<byte> bytes, HexaOptions options)
    {
        if (bytes.Length == 0)
            return string.Empty;

        return options switch
        {
            HexaOptions.LowerCase => ToHexaLowerCase(bytes),
            HexaOptions.UpperCase => ToHexaUpperCase(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
    }

    [SuppressMessage("Usage", "MA0015:Specify the parameter name in ArgumentException")]
    public static byte[] ParseHexaString(string str)
    {
        ArgumentNullException.ThrowIfNull(str);

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

        static int GetInt(char c)
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

    public static bool TryParseHexaString(string? str, [NotNullWhen(returnValue: true)] out byte[]? result)
    {
        if (str is null)
        {
            result = default;
            return false;
        }

        return TryParseHexaString(str.AsSpan(), out result);
    }

    public static bool TryParseHexaString(ReadOnlySpan<char> str, [NotNullWhen(returnValue: true)] out byte[]? result)
    {
        if (str.Length % 2 != 0)
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

    public static bool TryParseHexaString(string? str, Span<byte> bytes, out int writtenBytes)
    {
        if (str is null || str.Length % 2 != 0)
        {
            writtenBytes = 0;
            return false;
        }

        // handle 0x or 0X notation
        if (str.Length >= 2 && str[0] == '0' && (str[1] == 'x' || str[1] == 'X'))
        {
            const int PrefixLength = 2;
            var length = (str.Length / 2) - 1;
            if (length > bytes.Length)
            {
                writtenBytes = 0;
                return false;
            }

            for (var i = 0; i < length; i++)
            {
                if (!TryGetHexValue(str[i * 2 + PrefixLength], out var value1) || !TryGetHexValue(str[(i * 2) + 1 + PrefixLength], out var value2))
                {
                    writtenBytes = i;
                    return false;
                }

                bytes[i] = (byte)(value1 << 4);
                bytes[i] += (byte)value2;
            }

            writtenBytes = length;
            return true;
        }
        else
        {
            var length = str.Length / 2;
            if (length > bytes.Length)
            {
                writtenBytes = 0;
                return false;
            }

            for (var i = 0; i < length; i++)
            {
                if (!TryGetHexValue(str[i * 2], out var value1) || !TryGetHexValue(str[(i * 2) + 1], out var value2))
                {
                    writtenBytes = i;
                    return false;
                }

                bytes[i] = (byte)(value1 << 4);
                bytes[i] += (byte)value2;
            }

            writtenBytes = length;
            return true;
        }
    }

    private static string ToHexaUpperCase(ReadOnlySpan<byte> bytes)
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

    private static string ToHexaLowerCase(ReadOnlySpan<byte> bytes)
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
