using System.Text.Json;
using Meziantou.Framework.Internals;

namespace Meziantou.Framework;

/// <summary>
/// Reads an icon set with System.Text.Json, and converts the document to the tree that <see cref="IconifyIconSetReader"/> reads.
/// </summary>
/// <remarks>
/// This file is not in the Internals folder, which the source generator links: the generator cannot depend on System.Text.Json.
/// </remarks>
internal static class IconifyJsonReader
{
    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 64 };

    public static IconifyIconSetData Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, DocumentOptions);
            return Read(document);
        }
        catch (JsonException ex)
        {
            throw CreateException(ex);
        }
    }

    public static IconifyIconSetData Read(Stream stream)
    {
        try
        {
            using var document = JsonDocument.Parse(stream, DocumentOptions);
            return Read(document);
        }
        catch (JsonException ex)
        {
            throw CreateException(ex);
        }
    }

    public static async Task<IconifyIconSetData> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(stream, DocumentOptions, cancellationToken).ConfigureAwait(false);
            return Read(document);
        }
        catch (JsonException ex)
        {
            throw CreateException(ex);
        }
    }

    private static IconifyIconSetData Read(JsonDocument document) => IconifyIconSetReader.Read(Convert(document.RootElement));

    private static FormatException CreateException(JsonException exception) => new("The icon set is not a valid JSON document: " + exception.Message, exception);

    private static object? Convert(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    result[property.Name] = Convert(property.Value);
                }

                return result;

            case JsonValueKind.Array:
                var array = new List<object?>(element.GetArrayLength());
                foreach (var item in element.EnumerateArray())
                {
                    array.Add(Convert(item));
                }

                return array;

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                if (!element.TryGetDouble(out var number) || double.IsInfinity(number))
                    throw new FormatException("The icon set contains a number that is out of range: " + element.GetRawText());

                return number;

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            default:
                return null;
        }
    }
}
