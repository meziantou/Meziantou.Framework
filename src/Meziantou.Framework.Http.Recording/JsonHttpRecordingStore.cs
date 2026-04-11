using System.Text.Json;

namespace Meziantou.Framework.Http.Recording;

/// <summary>Stores recorded HTTP entries as a JSON file.</summary>
public sealed class JsonHttpRecordingStore : IHttpRecordingStore
{
    private readonly string _filePath;

    public JsonHttpRecordingStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        _filePath = filePath;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<HttpRecordingEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_filePath);
        var entries = await JsonSerializer.DeserializeAsync(stream, HttpRecordingSerializerContext.Default.ListHttpRecordingEntry, cancellationToken).ConfigureAwait(false);
        return entries ?? [];
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(IReadOnlyList<HttpRecordingEntry> entries, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var list = entries as List<HttpRecordingEntry> ?? new List<HttpRecordingEntry>(entries);

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, list, HttpRecordingSerializerContext.Default.ListHttpRecordingEntry, cancellationToken).ConfigureAwait(false);
    }
}
