namespace Meziantou.Framework.Diagrams;

/// <summary>A change of the numbering of the messages of a sequence diagram (Mermaid <c>autonumber</c>).</summary>
public sealed class SequenceAutonumber : SequenceStep
{
    /// <summary>Gets or sets a value that indicates whether the next messages are numbered. The default is <see langword="true"/>.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets or sets the number of the next message, or <see langword="null"/> to continue the numbering.</summary>
    public decimal? Start { get; set; }

    /// <summary>Gets or sets the increment, or <see langword="null"/> to keep the current one (1 at first).</summary>
    public decimal? Increment { get; set; }
}
