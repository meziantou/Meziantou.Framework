namespace Meziantou.Framework.Diagrams;

/// <summary>A period of a timeline, such as a year, with its events.</summary>
public sealed class TimelinePeriod
{
    /// <summary>Initializes a new instance of the <see cref="TimelinePeriod"/> class.</summary>
    /// <param name="label">The text of the period.</param>
    public TimelinePeriod(DiagramText label)
    {
        ArgumentNullException.ThrowIfNull(label);
        Label = label;
    }

    /// <summary>Gets or sets the text of the period.</summary>
    public DiagramText Label { get; set; }

    /// <summary>Gets the events, in order.</summary>
    public IList<DiagramText> Events { get; } = new List<DiagramText>();

    /// <summary>Returns the text of the period.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Label.Text;
}
