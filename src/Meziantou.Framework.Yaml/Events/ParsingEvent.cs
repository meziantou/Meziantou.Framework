namespace Meziantou.Framework.Yaml.Events;

/// <summary>Base class for parsing events.</summary>
public abstract class ParsingEvent
{
    /// <summary>
    /// Gets a value indicating the variation of depth caused by this event.
    /// The value can be either -1, 0 or 1. For start events, it will be 1,
    /// for end events, it will be -1, and for the remaining events, it will be 0.
    /// </summary>
    public abstract int NestingIncrease { get; }

    /// <summary>Gets the event type, which allows for simpler type comparisons.</summary>
    internal abstract EventType Type { get; }

    /// <summary>Gets the position in the input stream where the event starts.</summary>
    public Mark Start { get; }

    /// <summary>Gets the position in the input stream where the event ends.</summary>
    public Mark End { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ParsingEvent"/> class.
    /// </summary>
    /// <param name="start">The start position of the event.</param>
    /// <param name="end">The end position of the event.</param>
    protected ParsingEvent(Mark start, Mark end)
    {
        Start = start;
        End = end;
    }
}