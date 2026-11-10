namespace Meziantou.Framework.Diagrams;

/// <summary>A movement between two domains of a <see cref="CynefinDiagram"/>, drawn as a curved arrow between their centers.</summary>
/// <param name="from">The domain the movement starts from.</param>
/// <param name="to">The domain the movement goes to. A transition to the same domain is not drawn, as in Mermaid.</param>
/// <param name="label">The text drawn on the arrow, or <see langword="null"/> for none.</param>
public sealed class CynefinTransition(CynefinDomain from, CynefinDomain to, DiagramText? label = null)
{
    /// <summary>Gets or sets the domain the movement starts from.</summary>
    public CynefinDomain From { get; set; } = from;

    /// <summary>Gets or sets the domain the movement goes to.</summary>
    public CynefinDomain To { get; set; } = to;

    /// <summary>Gets or sets the text drawn on the arrow, or <see langword="null"/> for none.</summary>
    public DiagramText? Label { get; set; } = label;

    /// <summary>Returns the domains and the text of the transition.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Label is null ? $"{From} --> {To}" : $"{From} --> {To}: {Label.Text}";
}
