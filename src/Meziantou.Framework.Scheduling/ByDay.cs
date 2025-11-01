namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a day of the week with an optional ordinal position for recurrence rules.</summary>
public sealed class ByDay : IEquatable<ByDay>
{
    /// <summary>Initializes a new instance of the <see cref="ByDay"/> class.</summary>
    public ByDay()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ByDay"/> class with the specified day of the week.</summary>
    /// <param name="dayOfWeek">The day of the week.</param>
    public ByDay(DayOfWeek dayOfWeek)
        : this(dayOfWeek, ordinal: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ByDay"/> class with the specified day of the week and ordinal position.</summary>
    /// <param name="dayOfWeek">The day of the week.</param>
    /// <param name="ordinal">The ordinal position (e.g., 1 for first, -1 for last).</param>
    public ByDay(DayOfWeek dayOfWeek, int? ordinal)
    {
        DayOfWeek = dayOfWeek;
        Ordinal = ordinal;
    }

    /// <summary>Gets or sets the day of the week.</summary>
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Gets or sets the ordinal position within the month or year.</summary>
    public int? Ordinal { get; set; }

    public bool Equals(ByDay? other)
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

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            return ((int)DayOfWeek * 397) ^ Ordinal.GetHashCode();
        }
    }

    /// <summary>Determines whether two <see cref="ByDay"/> instances are equal.</summary>
    public static bool operator ==(ByDay left, ByDay right) => Equals(left, right);

    /// <summary>Determines whether two <see cref="ByDay"/> instances are not equal.</summary>
    public static bool operator !=(ByDay left, ByDay right) => !Equals(left, right);

    /// <inheritdoc />
    public override string ToString()
    {
        return Ordinal + Utilities.DayOfWeekToString(DayOfWeek);
    }
}
