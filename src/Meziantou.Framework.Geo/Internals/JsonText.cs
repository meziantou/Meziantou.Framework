using System.Text.Json;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Parses the JSON text of the formats.</summary>
internal static class JsonText
{
    /// <summary>Parses a JSON text. A text that has an unpaired surrogate cannot be encoded as UTF-8: it is not valid JSON either.</summary>
    /// <exception cref="JsonException">The text is not valid JSON.</exception>
    /// <exception cref="FormatException">The text has an unpaired surrogate.</exception>
    public static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException("The text is not valid JSON: it has an unpaired surrogate, so it cannot be encoded as UTF-8.", ex);
        }
    }
}
