namespace Meziantou.Framework.Charts;

/// <summary>A position scale with its domain, ready to map the values of a plot once its range is known.</summary>
internal abstract class ResolvedScale
{
    public double RangeStart { get; private set; }

    public double RangeEnd { get; private set; }

    public bool IsPeriodic { get; private set; }

    public abstract bool IsOrdinal { get; }

    public virtual double Bandwidth => 0;

    public double RangeMin => Math.Min(RangeStart, RangeEnd);

    public double RangeMax => Math.Max(RangeStart, RangeEnd);

    /// <summary>Sets the range and the number of ticks. A periodic range is a full circle: its start and its end are the same angle.</summary>
    public void Configure(double start, double end, int tickCount, bool periodic = false)
    {
        RangeStart = start;
        RangeEnd = end;
        IsPeriodic = periodic;
        OnConfigure(Math.Max(1, tickCount));
    }

    /// <summary>Maps a value of a channel to a position, or NaN when it is missing or outside of what the scale accepts.</summary>
    public abstract double Map(ChannelValues values, int index);

    /// <summary>The position of zero, clamped to the domain: where bars and areas start.</summary>
    public abstract double Baseline { get; }

    public abstract IReadOnlyList<Tick> GetTicks();

    protected abstract void OnConfigure(int tickCount);
}
