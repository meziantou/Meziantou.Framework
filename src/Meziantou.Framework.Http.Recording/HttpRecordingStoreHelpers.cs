namespace Meziantou.Framework.Http.Recording;

/// <summary>Shared behavior for the file-backed <see cref="IHttpRecordingStore"/> implementations.</summary>
internal static class HttpRecordingStoreHelpers
{
    /// <summary>
    /// Writes to a temporary file next to the destination and moves it into place once the write has fully succeeded.
    /// </summary>
    /// <remarks>
    /// Opening the destination directly would truncate it before the first byte is produced, so a cancelled save, a
    /// serialization error, or a process kill would destroy a recording file that is often the only copy of the
    /// responses it holds.
    /// </remarks>
    public static async ValueTask WriteAtomicallyAsync(string filePath, Func<Stream, CancellationToken, ValueTask> writeAsync, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await writeAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <summary>Rejects entries that cannot be matched, naming the file and the index so the developer knows what to repair.</summary>
    public static void ValidateEntries(IReadOnlyList<HttpRecordingEntry> entries, string filePath)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry is null)
            {
                throw new InvalidDataException($"The recording file '{filePath}' contains a null entry at index {i}.");
            }

            // 'required' is not a null check: a JSON document can set these to null and deserialization accepts it.
            if (string.IsNullOrEmpty(entry.Method))
            {
                throw new InvalidDataException($"The entry at index {i} in the recording file '{filePath}' has no HTTP method.");
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
