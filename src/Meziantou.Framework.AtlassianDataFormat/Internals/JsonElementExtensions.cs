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
        return attrs.TryGet(name, out var value) && value.ValueKind is JsonValueKind.Number && value.TryGetDouble(out var result)
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
        return attrs.TryGet(name, out var value) ? value.Clone() : null;
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
