using System.Text.Json;

namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

public sealed class JsonSnapshotSerializer : SnapshotSerializer
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonSerializerOptions _options;

    internal static JsonSnapshotSerializer Instance { get; } = new();

    public JsonSnapshotSerializer()
        : this(DefaultOptions)
    {
    }

    public JsonSnapshotSerializer(JsonSerializerOptions options)
    {
        _options = options ?? DefaultOptions;
    }

    public override string Serialize(object? value)
    {
        return JsonSerializer.Serialize(value, options: _options);
    }
}
