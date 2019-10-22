using System;

namespace Meziantou.Framework.Scheduling
{
    public sealed class ByDay : IEquatable<ByDay>
    {
        public ByDay()
        {
        }

        public ByDay(DayOfWeek dayOfWeek)
            : this(dayOfWeek, ordinal: null)
        {
        }

        public ByDay(DayOfWeek dayOfWeek, int? ordinal)
        {
            DayOfWeek = dayOfWeek;
            Ordinal = ordinal;
        }

        public DayOfWeek DayOfWeek { get; set; }
        public int? Ordinal { get; set; }

        public bool Equals(ByDay other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return DayOfWeek == other.DayOfWeek && Ordinal == other.Ordinal;
        }

        public override bool Equals(object? obj)
        {
            if (obj is null)
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != GetType())
                return false;
            return Equals((ByDay)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)DayOfWeek * 397) ^ Ordinal.GetHashCode();
            }
        }

        public static bool operator ==(ByDay left, ByDay right) => Equals(left, right);

        public static bool operator !=(ByDay left, ByDay right) => !Equals(left, right);

        public override string ToString()
        {
            return Ordinal + Utilities.DayOfWeekToString(DayOfWeek);
        }
    }
}
