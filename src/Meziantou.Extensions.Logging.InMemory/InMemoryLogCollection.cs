using System.Collections;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

public sealed class InMemoryLogCollection : IEnumerable<InMemoryLogEntry>
{
    private const int MaxChunkSize = 8000;

    private readonly Lock _lock = new();
    private Chunk<InMemoryLogEntry>? _firstChunk;
    private Chunk<InMemoryLogEntry>? _lastChunk;

    internal void Add(InMemoryLogEntry entry)
    {
        lock (_lock)
        {
            if (_lastChunk is null)
            {
                _firstChunk = _lastChunk = new Chunk<InMemoryLogEntry>(16);
            }
            else if (_lastChunk.Count == _lastChunk.Items.Length)
            {
                var newCapacity = Math.Min(MaxChunkSize, _lastChunk.Count * 2);
                var newChunk = new Chunk<InMemoryLogEntry>(newCapacity);
                _lastChunk.Next = newChunk;
                _lastChunk = newChunk;
            }

            _lastChunk.Items[_lastChunk.Count] = entry;
            _lastChunk.Count++;
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        var chunk = _firstChunk;
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

    public IEnumerable<InMemoryLogEntry> Debugs => GetByLogLevel(LogLevel.Debug);
    public IEnumerable<InMemoryLogEntry> Traces => GetByLogLevel(LogLevel.Trace);
    public IEnumerable<InMemoryLogEntry> Informations => GetByLogLevel(LogLevel.Information);
    public IEnumerable<InMemoryLogEntry> Warnings => GetByLogLevel(LogLevel.Warning);
    public IEnumerable<InMemoryLogEntry> Errors => GetByLogLevel(LogLevel.Error);
    public IEnumerable<InMemoryLogEntry> Criticals => GetByLogLevel(LogLevel.Critical);

    public bool Contains(Func<InMemoryLogEntry, bool> predicate)
    {
        return Find(predicate) is not null;
    }

    public InMemoryLogEntry? Find(Func<InMemoryLogEntry, bool> predicate)
    {
        var chunk = _firstChunk;
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
        var chunk = _firstChunk;
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

    public struct Enumerator : IEnumerator<InMemoryLogEntry>
    {
        private Chunk<InMemoryLogEntry>? _chunk;
        private int _index = -1;

        public Enumerator(InMemoryLogCollection collection)
        {
            _chunk = collection._firstChunk;
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
            if (_chunk is null)
                return false;

            _index++;
            if (_index >= _chunk.Count)
            {
                _chunk = _chunk.Next;
                _index = 0;
                return _chunk is not null;
            }

            return true;
        }

        public readonly void Dispose()
        {
        }

        public readonly void Reset() => throw new NotSupportedException();
    }
}
