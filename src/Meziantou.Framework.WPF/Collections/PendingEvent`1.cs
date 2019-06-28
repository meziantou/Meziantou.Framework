namespace Meziantou.Framework.Windows.Collections
{
    internal sealed class PendingEvent<T>
    {
        public PendingEvent(PendingEventType type)
        {
            Type = type;
        }

        public PendingEvent(PendingEventType type, int index)
        {
            Type = type;
            Index = index;
        }

        public PendingEvent(PendingEventType type, T item)
        {
            Type = type;
            Item = item;
        }

        public PendingEvent(PendingEventType type, T item, int index)
        {
            Type = type;
            Item = item;
            Index = index;
        }

        public PendingEventType Type { get; }
        public T Item { get; }
        public int Index { get; }
    }
}
