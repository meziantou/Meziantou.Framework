using System.Collections;
using System.Collections.Generic;

namespace Meziantou.Extensions.Logging.InMemory
{
    internal sealed class SingleLinkedList<T> : IReadOnlyCollection<T>
    {
        private readonly object _lock = new();
        private SingleLinkedListNode<T>? _first;
        private SingleLinkedListNode<T>? _last;

        public int Count { get; private set; }

        public void AddLast(T value)
        {
            lock (_lock)
            {
                var node = new SingleLinkedListNode<T>(value);
                if (_last is null)
                {
                    _first = _last = node;
                }
                else
                {
                    _last.Next = node;
                    _last = node;
                }

                Count++;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            var node = _first;
            while (node != null)
            {
                yield return node.Value;
                node = node.Next;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
