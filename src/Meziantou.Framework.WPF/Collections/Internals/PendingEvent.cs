namespace Meziantou.Framework.WPF.Collections
{
    internal static class PendingEvent
    {
        public static PendingEvent<T> Add<T>(T item) => new(PendingEventType.Add, item);

        public static PendingEvent<T> Insert<T>(int index, T item) => new(PendingEventType.Insert, item, index);

        public static PendingEvent<T> Remove<T>(T item) => new(PendingEventType.Remove, item);

        public static PendingEvent<T> RemoveAt<T>(int index) => new(PendingEventType.RemoveAt, index);

        public static PendingEvent<T> Replace<T>(int index, T item) => new(PendingEventType.Replace, item, index);

        public static PendingEvent<T> Clear<T>() => new(PendingEventType.Clear);
    }
}
