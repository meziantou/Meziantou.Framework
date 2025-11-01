using System.Text.Json;

namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

/// <summary>
/// A snapshot serializer that uses System.Text.Json to serialize objects to JSON format.
/// </summary>
public sealed class JsonSnapshotSerializer : SnapshotSerializer
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Gets the default instance of the JsonSnapshotSerializer.
    /// </summary>
    internal static JsonSnapshotSerializer Instance { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSnapshotSerializer"/> class with default options.
    /// </summary>
    public JsonSnapshotSerializer()
        : this(DefaultOptions)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSnapshotSerializer"/> class with the specified options.
    /// </summary>
    /// <param name="options">The JSON serializer options to use.</param>
    public JsonSnapshotSerializer(JsonSerializerOptions options)
    {
        _options = options ?? DefaultOptions;
    }

    /// <inheritdoc/>
    public override string Serialize(object? value)
    {
        return JsonSerializer.Serialize(value, options: _options);
    }
}
