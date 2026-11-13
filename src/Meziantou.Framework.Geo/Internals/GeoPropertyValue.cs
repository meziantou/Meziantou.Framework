using System.Globalization;
using System.Text.Json;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Converts the values of <see cref="GeoFeature.Properties"/> for the writers of the formats.</summary>
internal static class GeoPropertyValue
{
    /// <summary>Gets the text of a value, independent of the culture, or <see langword="null"/> for <see langword="null"/>.</summary>
    public static string? ToText(object? value) => value switch
    {
        null => null,
        string text => text,
        bool boolean => boolean ? "true" : "false",
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement element => element.GetRawText(),
        IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>Gets whether the value is a decimal floating-point number, whose text keeps more digits than a double.</summary>
    public static bool IsDecimalNumber(object? value)
    {
#if NET11_0_OR_GREATER
        return value is decimal or System.Numerics.Decimal32 or System.Numerics.Decimal64 or System.Numerics.Decimal128;
#else
        return value is decimal;
#endif
    }

    /// <summary>Gets whether the value is a binary floating-point number.</summary>
    public static bool IsBinaryFloatingPointNumber(object? value)
    {
#if NET11_0_OR_GREATER
        return value is double or float or Half or System.Numerics.BFloat16;
#else
        return value is double or float or Half;
#endif
    }

    /// <summary>Gets the value of a number of any numeric type.</summary>
    public static bool TryGetNumber(object? value, out double number)
    {
        switch (value)
        {
            case double d: number = d; return true;
            case float f: number = f; return true;
            case decimal m: number = (double)m; return true;
            case int i: number = i; return true;
            case long l: number = l; return true;
            case short s: number = s; return true;
            case byte b: number = b; return true;
            case sbyte sb: number = sb; return true;
            case uint ui: number = ui; return true;
            case ulong ul: number = ul; return true;
            case ushort us: number = us; return true;
            case Half h: number = (double)h; return true;
#if NET11_0_OR_GREATER
            case System.Numerics.BFloat16 bf: number = (double)bf; return true;
            case System.Numerics.Decimal32 d32: number = (double)d32; return true;
            case System.Numerics.Decimal64 d64: number = (double)d64; return true;
            case System.Numerics.Decimal128 d128: number = (double)d128; return true;
#endif
            case JsonElement { ValueKind: JsonValueKind.Number } element: number = element.GetDouble(); return true;
            default: number = 0; return false;
        }
    }
}
