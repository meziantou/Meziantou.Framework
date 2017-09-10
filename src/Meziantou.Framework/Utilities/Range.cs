namespace Meziantou.Framework.Utilities
{
    public static class Range
    {
        public static Range<T> Create<T>(T from, T to)
        {
            return new Range<T>(from, to);
        }
    }

    public struct Range<T>
    {
        public Range(T from, T to)
        {
            From = from;
            To = to;
        }

        public T From { get; set; }
        public T To { get; set; }
    }
}
