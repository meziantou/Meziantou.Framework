using System.Collections.Concurrent;

namespace Meziantou.Framework.Http.Recording;

internal sealed class RecordingSession
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<HttpRecordingEntry>> _entriesByFingerprint = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<HttpRecordingEntry> _allEntries = new();
    private readonly IHttpRequestMatcher _matcher;

    public RecordingSession(IHttpRequestMatcher matcher)
    {
        _matcher = matcher;
    }

    public void LoadEntries(IReadOnlyList<HttpRecordingEntry> entries)
    {
        foreach (var entry in entries)
        {
            var fingerprint = _matcher.ComputeFingerprint(entry);
            var queue = _entriesByFingerprint.GetOrAdd(fingerprint, static _ => new ConcurrentQueue<HttpRecordingEntry>());
            queue.Enqueue(entry);
            _allEntries.Enqueue(entry);
        }
    }

    public bool TryGetRecordedResponse(HttpRecordingEntry requestEntry, out HttpRecordingEntry? match)
    {
        var fingerprint = _matcher.ComputeFingerprint(requestEntry);

        if (_entriesByFingerprint.TryGetValue(fingerprint, out var queue) && queue.TryDequeue(out match))
        {
            return true;
        }

        match = null;
        return false;
    }

    public void AddRecordedEntry(HttpRecordingEntry entry)
    {
        var fingerprint = _matcher.ComputeFingerprint(entry);
        var queue = _entriesByFingerprint.GetOrAdd(fingerprint, static _ => new ConcurrentQueue<HttpRecordingEntry>());
        queue.Enqueue(entry);
        _allEntries.Enqueue(entry);
    }

    public IReadOnlyList<HttpRecordingEntry> GetAllEntries()
    {
        return _allEntries.ToArray();
    }
}
