namespace Meziantou.Framework.Diagrams;

/// <summary>A relationship between two elements of an entity relationship diagram.</summary>
public sealed class ErRelationship
{
    /// <summary>Initializes a new instance of the <see cref="ErRelationship"/> class.</summary>
    /// <param name="from">The first element, written on the left in Mermaid.</param>
    /// <param name="to">The second element, written on the right in Mermaid.</param>
    public ErRelationship(ErElement from, ErElement to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        To = to;
    }

    /// <summary>Gets the first element.</summary>
    public ErElement From { get; }

    /// <summary>Gets the second element.</summary>
    public ErElement To { get; }

    /// <summary>Gets or sets how many instances of <see cref="From"/> an instance of <see cref="To"/> relates to, drawn at <see cref="From"/>.</summary>
    public ErCardinality FromCardinality { get; set; }

    /// <summary>Gets or sets how many instances of <see cref="To"/> an instance of <see cref="From"/> relates to, drawn at <see cref="To"/>.</summary>
    public ErCardinality ToCardinality { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the relationship is identifying, drawn with a solid line: the child
    /// entity cannot exist without the other one. A non-identifying relationship is dashed (Mermaid <c>..</c>). The default is <see langword="true"/>.
    /// </summary>
    public bool IsIdentifying { get; set; } = true;

    /// <summary>Gets or sets the text of the relationship, from the point of view of <see cref="From"/>, or <see langword="null"/> for none.</summary>
    public DiagramText? Label { get; set; }
}
