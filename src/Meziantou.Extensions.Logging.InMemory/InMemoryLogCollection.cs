using System.Collections;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

/// <summary>Represents a thread-safe collection of log entries captured by in-memory loggers.</summary>
/// <example>
/// <code>
/// var logger = InMemoryLogger.CreateLogger("sample");
/// 
/// // Filter by log level
/// var errors = logger.Logs.Errors;
/// var infos = logger.Logs.Informations;
/// 
/// // Search for specific entries
/// var entry = logger.Logs.Find(log => log.Message.Contains("Error"));
/// </code>
/// </example>
public sealed class InMemoryLogCollection : IEnumerable<InMemoryLogEntry>
{
    private const int MaxChunkSize = 8000;

    private readonly Lock _lock = new();
    private Chunk<InMemoryLogEntry>? _firstChunk;
    private Chunk<InMemoryLogEntry>? _lastChunk;
    private int _count;

    /// <summary>Gets the number of log entries in the collection.</summary>
    /// <remarks>
    /// The count is incremented only after the entry is reachable, so a concurrent reader never sees
    /// a count that promises more entries than an enumeration would yield.
    /// </remarks>
    public int Count => Volatile.Read(ref _count);

    // Readers walk the chunks without taking the lock, so the head is published with a release
    // and read with an acquire, exactly like Chunk.Count and Chunk.Next.
    private Chunk<InMemoryLogEntry>? FirstChunk => Volatile.Read(ref _firstChunk);

    internal void Add(InMemoryLogEntry entry)
    {
        lock (_lock)
        {
            var lastChunk = _lastChunk;
            if (lastChunk is null)
            {
                var firstChunk = new Chunk<InMemoryLogEntry>(16);
                firstChunk.Items[0] = entry;
                firstChunk.Count = 1;
                _lastChunk = firstChunk;
                Volatile.Write(ref _firstChunk, firstChunk);
                Volatile.Write(ref _count, _count + 1);
                return;
            }

            if (lastChunk.Count == lastChunk.Items.Length)
            {
                var newCapacity = Math.Min(MaxChunkSize, lastChunk.Count * 2);
                var newChunk = new Chunk<InMemoryLogEntry>(newCapacity);

                // Populate the chunk before linking it, so a reader that observes the new Next
                // never sees a chunk whose first entry has not been written yet.
                newChunk.Items[0] = entry;
                newChunk.Count = 1;
                _lastChunk = newChunk;
                lastChunk.Next = newChunk;
                Volatile.Write(ref _count, _count + 1);
                return;
            }

            lastChunk.Items[lastChunk.Count] = entry;
            lastChunk.Count++;
            Volatile.Write(ref _count, _count + 1);
        }
    }

    /// <summary>Removes all log entries from the collection.</summary>
    /// <remarks>
    /// An enumeration already in progress keeps walking the entries it started on; it is not invalidated.
    /// </remarks>
    public void Clear()
    {
        lock (_lock)
        {
            _lastChunk = null;
            Volatile.Write(ref _count, 0);
            Volatile.Write(ref _firstChunk, null);
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        var chunk = FirstChunk;
        while (chunk is not null)
        {
            for (var i = 0; i < chunk.Count; i++)
            {
                sb.Append(chunk.Items[i]).AppendLine();
            }

            chunk = chunk.Next;
        }

        return sb.ToString();
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<InMemoryLogEntry> IEnumerable<InMemoryLogEntry>.GetEnumerator() => new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Gets all log entries with log level <see cref="LogLevel.Debug"/>.</summary>
    public IEnumerable<InMemoryLogEntry> Debugs => GetByLogLevel(LogLevel.Debug);

    /// <summary>Gets all log entries with log level <see cref="LogLevel.Trace"/>.</summary>
    public IEnumerable<InMemoryLogEntry> Traces => GetByLogLevel(LogLevel.Trace);

    /// <summary>Gets all log entries with log level <see cref="LogLevel.Information"/>.</summary>
    public IEnumerable<InMemoryLogEntry> Informations => GetByLogLevel(LogLevel.Information);

    /// <summary>Gets all log entries with log level <see cref="LogLevel.Warning"/>.</summary>
    public IEnumerable<InMemoryLogEntry> Warnings => GetByLogLevel(LogLevel.Warning);

    /// <summary>Gets all log entries with log level <see cref="LogLevel.Error"/>.</summary>
    public IEnumerable<InMemoryLogEntry> Errors => GetByLogLevel(LogLevel.Error);

    /// <summary>Gets all log entries with log level <see cref="LogLevel.Critical"/>.</summary>
    public IEnumerable<InMemoryLogEntry> Criticals => GetByLogLevel(LogLevel.Critical);

    /// <summary>Determines whether the collection contains any log entry that matches the specified predicate.</summary>
    /// <param name="predicate">The function to test each log entry for a condition.</param>
    /// <returns><see langword="true"/> if any log entry matches the predicate; otherwise, <see langword="false"/>.</returns>
    public bool Contains(Func<InMemoryLogEntry, bool> predicate)
    {
        return Find(predicate) is not null;
    }

    /// <summary>Searches for the first log entry that matches the specified predicate.</summary>
    /// <param name="predicate">The function to test each log entry for a condition.</param>
    /// <returns>The first log entry that matches the predicate, or <see langword="null"/> if no match is found.</returns>
    public InMemoryLogEntry? Find(Func<InMemoryLogEntry, bool> predicate)
    {
        var chunk = FirstChunk;
        while (chunk is not null)
        {
            for (var i = 0; i < chunk.Count; i++)
            {
                var entry = chunk.Items[i];
                if (predicate(entry))
                    return entry;
            }

            chunk = chunk.Next;
        }

        return null;
    }

    private IEnumerable<InMemoryLogEntry> GetByLogLevel(LogLevel logLevel)
    {
        var chunk = FirstChunk;
        while (chunk is not null)
        {
            for (var i = 0; i < chunk.Count; i++)
            {
                var entry = chunk.Items[i];
                if (entry.LogLevel == logLevel)
                    yield return entry;
            }

            chunk = chunk.Next;
        }
    }

    /// <summary>Enumerates the elements of a <see cref="InMemoryLogCollection"/>.</summary>
    public struct Enumerator : IEnumerator<InMemoryLogEntry>
    {
        private Chunk<InMemoryLogEntry>? _chunk;
        private int _index = -1;

        public Enumerator(InMemoryLogCollection collection)
        {
            _chunk = collection.FirstChunk;
        }

        public readonly InMemoryLogEntry Current
        {
            get
            {
                Debug.Assert(_chunk is not null);
                return _chunk.Items[_index];
            }
        }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            // A chunk is only ever linked once it holds an entry, but the loop tolerates an empty
            // one anyway: returning true for a chunk whose Count is still 0 would hand out a
            // default(InMemoryLogEntry) as if it were a real entry.
            while (_chunk is not null)
            {
                _index++;
                if (_index < _chunk.Count)
                    return true;

                _chunk = _chunk.Next;
                _index = -1;
            }

            return false;
        }

        public readonly void Dispose()
        {
        }

        public readonly void Reset() => throw new NotSupportedException();
    }
}
