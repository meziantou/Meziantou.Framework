using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WPF.Collections
{
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct PendingEvent<T>
    {
        public PendingEvent(PendingEventType type)
        {
            Type = type;
            Item = default!;
            Index = -1;
            Items = default;
        }

        public PendingEvent(PendingEventType type, int index)
        {
            Type = type;
            Item = default!;
            Index = index;
            Items = default;
        }

        public PendingEvent(PendingEventType type, T item)
        {
            Type = type;
            Item = item;
            Index = -1;
            Items = default;
        }

        public PendingEvent(PendingEventType type, T item, int index)
        {
            Type = type;
            Item = item;
            Index = index;
            Items = default;
        }

        public PendingEvent(PendingEventType type, ImmutableList<T> items)
        {
            Type = type;
            Items = items;
            Item = default!;
            Index = default;
        }

        public PendingEvent(PendingEventType type, ImmutableList<T> items, int index)
        {
            Type = type;
            Items = items;
            Item = default!;
            Index = index;
        }

        public PendingEventType Type { get; }
        public T Item { get; }
        public int Index { get; }
        public ImmutableList<T>? Items { get; }
    }
}
