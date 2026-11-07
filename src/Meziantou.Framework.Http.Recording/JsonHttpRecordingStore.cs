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

        List<HttpRecordingEntry>? entries;
        await using (var stream = File.OpenRead(_filePath))
        {
            try
            {
                entries = await JsonSerializer.DeserializeAsync(stream, HttpRecordingSerializerContext.Default.ListHttpRecordingEntry, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"The recording file '{_filePath}' is not valid JSON. It may have been truncated by an interrupted save.", ex);
            }
        }

        if (entries is null)
        {
            throw new InvalidDataException($"The recording file '{_filePath}' does not contain a list of recordings. Delete it to start a new recording.");
        }

        HttpRecordingStoreHelpers.ValidateEntries(entries, _filePath);
        return entries;
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(IReadOnlyList<HttpRecordingEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var list = entries as List<HttpRecordingEntry> ?? new List<HttpRecordingEntry>(entries);
        return HttpRecordingStoreHelpers.WriteAtomicallyAsync(
            _filePath,
            async (stream, token) => await JsonSerializer.SerializeAsync(stream, list, HttpRecordingSerializerContext.Default.ListHttpRecordingEntry, token).ConfigureAwait(false),
            cancellationToken);
    }
}
