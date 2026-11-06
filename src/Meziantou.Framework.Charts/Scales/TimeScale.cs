namespace Meziantou.Framework.Charts;

/// <summary>
/// A scale of times, in UTC. Its ticks fall on calendar boundaries (seconds, minutes, hours, days, weeks, months,
/// years), and each label shows the largest unit the tick does not fall on: <c>2024</c>, <c>Mar</c>, <c>Mar 15</c>, <c>06:00</c>.
/// </summary>
public sealed class TimeScale : ContinuousScale
{
    /// <summary>Gets or sets the start of the domain, or <see langword="null"/> to use <see cref="ContinuousScale.Minimum"/> or the earliest time of the data.</summary>
    public DateTimeOffset? Start { get; set; }

    /// <summary>Gets or sets the end of the domain, or <see langword="null"/> to use <see cref="ContinuousScale.Maximum"/> or the latest time of the data.</summary>
    public DateTimeOffset? End { get; set; }

    /// <summary>Gets or sets the format of the tick labels, or <see langword="null"/> for the default format. It takes precedence over <see cref="ContinuousScale.TickFormat"/>.</summary>
    public Func<DateTimeOffset, string>? TimeFormat { get; set; }
}
