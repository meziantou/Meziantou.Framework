namespace Meziantou.Framework.Diagrams;

/// <summary>An item of a C4 diagram that relationships can link: an element or a boundary.</summary>
public abstract class C4Item
{
    private protected C4Item(string id, DiagramText label)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(label);
        Id = id;
        Label = label;
    }

    /// <summary>Gets the identifier of the item, unique in the diagram (Mermaid alias).</summary>
    public string Id { get; }

    /// <summary>Gets or sets the name.</summary>
    public DiagramText Label { get; set; }

    /// <summary>Gets or sets the description, or <see langword="null"/> for none.</summary>
    public DiagramText? Description { get; set; }

    /// <summary>Gets or sets the colors of the item: <see cref="DiagramStyle.Fill"/>, <see cref="DiagramStyle.Stroke"/> and <see cref="DiagramStyle.TextColor"/> (Mermaid <c>UpdateElementStyle</c>).</summary>
    public DiagramStyle? Style { get; set; }

    /// <summary>Gets or sets the tags, written to Mermaid but not drawn, or <see langword="null"/> for none.</summary>
    public string? Tags { get; set; }

    /// <summary>Gets or sets the address the item links to in the SVG, or <see langword="null"/> for none.</summary>
    public string? Link { get; set; }

    /// <summary>Returns the identifier of the item.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
