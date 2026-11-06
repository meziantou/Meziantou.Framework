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
            case JsonElement { ValueKind: JsonValueKind.Number } element: number = element.GetDouble(); return true;
            default: number = 0; return false;
        }
    }
}
