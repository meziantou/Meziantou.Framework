using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.JavaScriptEngine;

namespace Meziantou.Framework.WebPlatform.JavaScript;

/// <summary>
/// The classic scripts parsed by the <see cref="JavaScriptScriptEngine"/> instances that share the cache (see
/// <see cref="JavaScriptScriptEngineOptions.ScriptCache"/>): a script whose source text, URL and position in its document
/// are the ones of a cached script is not parsed again, and the functions compiled to .NET code by an engine run compiled
/// in the others. The least recently used scripts are removed when the total length of the source texts exceeds the
/// capacity.
/// </summary>
/// <remarks>The cache is thread-safe: the engines of several threads can share it.</remarks>
public sealed class JavaScriptScriptCache
{
    /// <summary>The default capacity: 8 million characters of source text.</summary>
    public const long DefaultCapacity = 8 * 1024 * 1024;

    private readonly Lock _lock = new();
    private readonly Dictionary<ScriptKey, LinkedListNode<Entry>> _entries = new(ScriptKeyComparer.Instance);

    // The most recently used entry first
    private readonly LinkedList<Entry> _lru = new();
    private long _size;

    /// <summary>Creates a cache whose capacity is <see cref="DefaultCapacity"/>.</summary>
    public JavaScriptScriptCache()
        : this(DefaultCapacity)
    {
    }

    /// <summary>Creates a cache.</summary>
    /// <param name="capacity">The maximum total length of the source texts and the URLs of the cached scripts, in characters.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public JavaScriptScriptCache(long capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Capacity = capacity;
    }

    /// <summary>Gets the cache shared by the engines of the process: the default value of <see cref="JavaScriptScriptEngineOptions.ScriptCache"/>.</summary>
    public static JavaScriptScriptCache Shared { get; } = new();

    /// <summary>Gets the maximum total length of the source texts and the URLs of the cached scripts, in characters.</summary>
    public long Capacity { get; }

    /// <summary>Gets the number of cached scripts.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Gets the total length of the source texts and the URLs of the cached scripts, in characters.</summary>
    public long Size
    {
        get
        {
            lock (_lock)
            {
                return _size;
            }
        }
    }

    /// <summary>Removes all the scripts from the cache.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _lru.Clear();
            _size = 0;
        }
    }

    /// <summary>Gets the cached script parsed from the source text at the position, and marks it as the most recently used.</summary>
    internal bool TryGet(string sourceText, string? url, int line, int column, int unboundNameFunctionStart, [NotNullWhen(true)] out JsPreparedScript? script)
    {
        var key = new ScriptKey(sourceText, url, line, column, unboundNameFunctionStart);
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                if (node != _lru.First)
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                }

                script = node.Value.Script;
                return true;
            }
        }

        script = null;
        return false;
    }

    /// <summary>
    /// Adds a script parsed from the source text at the position, unless the cache has one (two threads parsed the same
    /// script), and removes the least recently used scripts that exceed the capacity.
    /// </summary>
    internal void Add(string sourceText, string? url, int line, int column, int unboundNameFunctionStart, JsPreparedScript script)
    {
        var size = (long)sourceText.Length + (url?.Length ?? 0);
        if (size > Capacity)
            return;

        var key = new ScriptKey(sourceText, url, line, column, unboundNameFunctionStart);
        lock (_lock)
        {
            if (_entries.ContainsKey(key))
                return;

            _entries.Add(key, _lru.AddFirst(new Entry(key, script, size)));
            _size += size;
            while (_size > Capacity && _lru.Last is { } last)
            {
                _lru.RemoveLast();
                _entries.Remove(last.Value.Key);
                _size -= last.Value.Size;
            }
        }
    }

    private sealed record Entry(ScriptKey Key, JsPreparedScript Script, long Size);

    // The position is part of the key: the locations of the errors and the stack traces are the ones of the document
    private readonly record struct ScriptKey(string SourceText, string? Url, int Line, int Column, int UnboundNameFunctionStart);

    private sealed class ScriptKeyComparer : IEqualityComparer<ScriptKey>
    {
        public static ScriptKeyComparer Instance { get; } = new();

        public bool Equals(ScriptKey x, ScriptKey y)
        {
            return x.Line == y.Line
                && x.Column == y.Column
                && x.UnboundNameFunctionStart == y.UnboundNameFunctionStart
                && string.Equals(x.Url, y.Url, StringComparison.Ordinal)
                && string.Equals(x.SourceText, y.SourceText, StringComparison.Ordinal);
        }

        // The source texts (and the data: URLs) can be large: the hash reads their length and their first and last
        // characters, and Equals compares them
        public int GetHashCode(ScriptKey obj) => HashCode.Combine(GetHashCode(obj.SourceText), GetHashCode(obj.Url), obj.Line, obj.Column, obj.UnboundNameFunctionStart);

        private static int GetHashCode(string? value)
        {
            const int SampleLength = 64;
            if (value is null)
                return 0;

            if (value.Length <= SampleLength * 2)
                return string.GetHashCode(value, StringComparison.Ordinal);

            return HashCode.Combine(value.Length, string.GetHashCode(value.AsSpan(0, SampleLength), StringComparison.Ordinal), string.GetHashCode(value.AsSpan(value.Length - SampleLength), StringComparison.Ordinal));
        }
    }
}
