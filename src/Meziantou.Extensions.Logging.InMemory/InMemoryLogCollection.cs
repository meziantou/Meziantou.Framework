using System.Collections;
using Meziantou.Framework.Collections;
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
    // Clear replaces the collection instead of emptying it, so the field is volatile: an Add that
    // starts after Clear returned reads the new collection. An Add that overlaps a Clear may still
    // append to the discarded one, which is indistinguishable from the entry being added just
    // before the Clear.
    private volatile AppendOnlyCollection<InMemoryLogEntry> _entries = new();

    /// <summary>Gets the number of log entries in the collection.</summary>
    /// <remarks>
    /// The count is incremented only after the entry is reachable, so a concurrent reader never sees
    /// a count that promises more entries than an enumeration would yield.
    /// </remarks>
    public int Count => _entries.Count;

    internal void Add(InMemoryLogEntry entry) => _entries.Add(entry);

    /// <summary>Removes all log entries from the collection.</summary>
    /// <remarks>
    /// An enumeration already in progress keeps walking the entries it started on; it is not invalidated.
    /// </remarks>
    public void Clear() => _entries = new AppendOnlyCollection<InMemoryLogEntry>();

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var entry in _entries)
        {
            sb.Append(entry).AppendLine();
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
    public bool Contains(Func<InMemoryLogEntry, bool> predicate) => _entries.Contains(predicate);

    /// <summary>Searches for the first log entry that matches the specified predicate.</summary>
    /// <param name="predicate">The function to test each log entry for a condition.</param>
    /// <returns>The first log entry that matches the predicate, or <see langword="null"/> if no match is found.</returns>
    public InMemoryLogEntry? Find(Func<InMemoryLogEntry, bool> predicate) => _entries.Find(predicate);

    private IEnumerable<InMemoryLogEntry> GetByLogLevel(LogLevel logLevel)
    {
        foreach (var entry in _entries)
        {
            if (entry.LogLevel == logLevel)
                yield return entry;
        }
    }

    /// <summary>Enumerates the elements of a <see cref="InMemoryLogCollection"/>.</summary>
    public struct Enumerator : IEnumerator<InMemoryLogEntry>
    {
        private AppendOnlyCollection<InMemoryLogEntry>.Enumerator _enumerator;

        public Enumerator(InMemoryLogCollection collection)
        {
            _enumerator = collection._entries.GetEnumerator();
        }

        public readonly InMemoryLogEntry Current => _enumerator.Current;

        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => _enumerator.MoveNext();

        public readonly void Dispose()
        {
        }

        public readonly void Reset() => throw new NotSupportedException();
    }
}
