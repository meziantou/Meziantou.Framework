using System.Runtime.InteropServices;

namespace Meziantou.Framework
{
    public static class Range
    {
        public static Range<T> Create<T>(T from, T to) where T : IComparable<T>
        {
            return new Range<T>(from, to);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly struct Range<T> : IEquatable<Range<T>>
        where T : IComparable<T>
    {
        public Range(T from, T to)
        {
            From = from;
            To = to;
        }

        public T From { get; }
        public T To { get; }

        public bool IsInRangeInclusive(T value)
        {
            if (From != null && From.CompareTo(value) > 0)
                return false;

            if (To != null && To.CompareTo(value) < 0)
                return false;

            return true;
        }

        public bool IsInRangeExclusive(T value)
        {
            if (From != null && From.CompareTo(value) >= 0)
                return false;

            if (To != null && To.CompareTo(value) <= 0)
                return false;

            return true;
        }

        public bool IsInRangeLowerInclusive(T value)
        {
            if (From != null && From.CompareTo(value) > 0)
                return false;

            if (To != null && To.CompareTo(value) <= 0)
                return false;

            return true;
        }

        public bool IsInRangeUpperInclusive(T value)
        {
            if (From != null && From.CompareTo(value) >= 0)
                return false;

            if (To != null && To.CompareTo(value) < 0)
                return false;

            return true;
        }

        public bool IsInRangeInclusive(Range<T> range)
        {
            return IsInRangeInclusive(range.From) && IsInRangeInclusive(range.To);
        }

        public bool IsInRangeExclusive(Range<T> range)
        {
            return IsInRangeExclusive(range.From) && IsInRangeExclusive(range.To);
        }

        public bool IsInRangeLowerInclusive(Range<T> range)
        {
            return IsInRangeLowerInclusive(range.From) && IsInRangeLowerInclusive(range.To);
        }

        public bool IsInRangeUpperInclusive(Range<T> range)
        {
            return IsInRangeUpperInclusive(range.From) && IsInRangeUpperInclusive(range.To);
        }

        public bool Equals(Range<T> other)
        {
            if (From != null)
            {
                if (From.CompareTo(other.From) != 0)
                    return false;

                if (other.From != null && other.From.CompareTo(From) != 0)
                    return false;
            }

            if (To != null)
            {
                if (To.CompareTo(other.To) != 0)
                    return false;

                if (other.To != null && other.To.CompareTo(To) != 0)
                    return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(From, To);
        }

        public override bool Equals(object? obj)
        {
            return obj is Range<T> range && Equals(range);
        }

        public override string? ToString()
        {
            return $"Range {From}-{To}";
        }

        public static bool operator ==(Range<T> left, Range<T> right) => left.Equals(right);
        public static bool operator !=(Range<T> left, Range<T> right) => !(left == right);
    }
}
