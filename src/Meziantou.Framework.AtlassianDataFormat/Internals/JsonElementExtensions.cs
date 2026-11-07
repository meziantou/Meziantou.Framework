using System.Text.Json;

namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>
/// Helpers to read optional ADF attributes. The receiver may be <see langword="default"/> when the
/// node has no <c>attrs</c> property, in which case every method returns <see langword="null"/>.
/// </summary>
internal static class JsonElementExtensions
{
    public static string? AttrString(this JsonElement attrs, string name)
    {
        return attrs.TryGet(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static int? AttrInt32(this JsonElement attrs, string name)
    {
        return attrs.TryGet(name, out var value) && value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;
    }

    public static double? AttrDouble(this JsonElement attrs, string name)
    {
        // A number out of the range of a double (1e400) reads as an infinity, which JSON cannot represent: it is ignored
        return attrs.TryGet(name, out var value) && value.ValueKind is JsonValueKind.Number && value.TryGetDouble(out var result) && double.IsFinite(result)
            ? result
            : null;
    }

    public static bool? AttrBoolean(this JsonElement attrs, string name)
    {
        return attrs.TryGet(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    public static JsonElement? AttrElement(this JsonElement attrs, string name)
    {
        return attrs.TryGet(name, out var value) ? value.CloneWritable() : null;
    }

    /// <summary>Clones a value that the document keeps as it is, and that the writer writes back.</summary>
    /// <exception cref="InvalidOperationException">The value has a string with an escaped unpaired surrogate.</exception>
    public static JsonElement CloneWritable(this JsonElement element)
    {
        // Writing the value unescapes its strings and its property names: one with an escaped unpaired surrogate
        // ("\ud800") is valid JSON but not text, so it could be read but not written. Only escaped strings can have one.
        if (element.GetRawText().Contains('\\', StringComparison.Ordinal))
        {
            using var writer = new Utf8JsonWriter(Stream.Null);
            element.WriteTo(writer);
        }

        return element.Clone();
    }

    private static bool TryGet(this JsonElement attrs, string name, out JsonElement value)
    {
        if (attrs.ValueKind is not JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        return attrs.TryGetProperty(name, out value) && value.ValueKind is not JsonValueKind.Null;
    }
}
