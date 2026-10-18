namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The stash of the WPT server (https://web-platform-tests.org/tools/wptserve/docs/stash.html), shared by the documents of a
/// test: a value put by a handler is taken (and removed) by another request. The values are strings, or the objects of the
/// emulated handlers (e.g. the state of a redirect chain, the message queue of <c>common/dispatcher/dispatcher.py</c>).
/// </summary>
internal sealed class WptStash
{
    // The handlers of the requests of the workers run on the threads of the workers
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _values = new(StringComparer.Ordinal);

    public void Put(string key, object value) => _values[key] = value;

    /// <summary>
    /// <c>stash.put(key, value, path)</c>: the value is only visible to the handlers that take it with the same path. The
    /// key is a UUID, whatever its format.
    /// </summary>
    public void Put(string key, object value, string path) => _values[GetKey(key, path)] = value;

    public string? Take(string key) => TakeValue(key) as string;

    /// <summary>Whether the stash has a value for the key (a handler that takes a value and puts it back under the lock of the stash).</summary>
    public bool Contains(string key) => _values.ContainsKey(key);

    public object? TakeValue(string key) => _values.TryRemove(key, out var value) ? value : null;

    /// <summary><c>stash.take(key, path)</c></summary>
    public object? TakeValue(string key, string path) => TakeValue(GetKey(key, path));

    private static string GetKey(string key, string path) => path + "|" + (Guid.TryParse(key, out var uuid) ? uuid.ToString("N") : key);
}
