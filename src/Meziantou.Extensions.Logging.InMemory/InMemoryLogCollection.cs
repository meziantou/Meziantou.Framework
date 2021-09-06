using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory
{
    public sealed class InMemoryLogCollection : IEnumerable<InMemoryLogEntry>
    {
        private readonly SingleLinkedList<InMemoryLogEntry> _items = new();

        internal void Add(InMemoryLogEntry entry)
        {
            _items.AddLast(entry);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            lock (_items)
            {
                foreach (var entry in _items)
                {
                    sb.Append(entry).AppendLine();
                }
            }

            return sb.ToString();
        }

        public IEnumerator<InMemoryLogEntry> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerable<InMemoryLogEntry> Debugs => _items.Where(item => item.LogLevel == LogLevel.Debug);
        public IEnumerable<InMemoryLogEntry> Traces => _items.Where(item => item.LogLevel == LogLevel.Trace);
        public IEnumerable<InMemoryLogEntry> Informations => _items.Where(item => item.LogLevel == LogLevel.Information);
        public IEnumerable<InMemoryLogEntry> Warnings => _items.Where(item => item.LogLevel == LogLevel.Warning);
        public IEnumerable<InMemoryLogEntry> Errors => _items.Where(item => item.LogLevel == LogLevel.Error);
        public IEnumerable<InMemoryLogEntry> Criticals => _items.Where(item => item.LogLevel == LogLevel.Critical);
    }
}
