using System.Collections.Concurrent;

namespace Meziantou.Framework.Http.Recording;

/// <summary>
/// Holds the recordings for one handler instance.
/// </summary>
/// <remarks>
/// Loaded entries and entries recorded during the session are tracked separately. Only loaded entries are replayable,
/// so a freshly recorded response is never handed back to a later request in the same session — that would consume the
/// recording the session is in the middle of producing, and the resulting file could not replay its own scenario.
/// </remarks>
internal sealed class RecordingSession
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<HttpRecordingEntry>> _replayQueues = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<HttpRecordingEntry> _loadedEntries = new();
    private readonly ConcurrentQueue<HttpRecordingEntry> _recordedEntries = new();
    private readonly IHttpRequestMatcher _matcher;

    public RecordingSession(IHttpRequestMatcher matcher)
    {
        _matcher = matcher;
    }

    public void LoadEntries(IReadOnlyList<HttpRecordingEntry> entries)
    {
        // Fingerprint everything first: if the matcher throws on one entry, the session must be left untouched rather
        // than half-populated, because initialization is retried and would otherwise duplicate the prefix.
        var fingerprinted = new List<(string Fingerprint, HttpRecordingEntry Entry)>(entries.Count);
        foreach (var entry in entries)
        {
            fingerprinted.Add((_matcher.ComputeFingerprint(entry), entry));
        }

        foreach (var (fingerprint, entry) in fingerprinted)
        {
            var queue = _replayQueues.GetOrAdd(fingerprint, static _ => new ConcurrentQueue<HttpRecordingEntry>());
            queue.Enqueue(entry);
            _loadedEntries.Enqueue(entry);
        }
    }

    public bool TryGetRecordedResponse(HttpRecordingEntry requestEntry, out HttpRecordingEntry? match)
    {
        var fingerprint = _matcher.ComputeFingerprint(requestEntry);

        if (_replayQueues.TryGetValue(fingerprint, out var queue) && queue.TryDequeue(out match))
        {
            return true;
        }

        match = null;
        return false;
    }

    public void AddRecordedEntry(HttpRecordingEntry entry)
    {
        _recordedEntries.Enqueue(entry);
    }

    /// <summary>Gets the entries to persist: everything that was loaded, plus everything recorded during this session.</summary>
    public IReadOnlyList<HttpRecordingEntry> GetEntriesToPersist()
    {
        var loaded = _loadedEntries.ToArray();
        var recorded = _recordedEntries.ToArray();
        if (recorded.Length is 0)
            return loaded;

        if (loaded.Length is 0)
            return recorded;

        var result = new HttpRecordingEntry[loaded.Length + recorded.Length];
        loaded.CopyTo(result, 0);
        recorded.CopyTo(result, loaded.Length);
        return result;
    }
}
