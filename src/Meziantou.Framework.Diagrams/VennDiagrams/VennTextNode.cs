using System.Collections.ObjectModel;

namespace Meziantou.Framework.Diagrams;

/// <summary>A text placed inside an area of a <see cref="VennDiagram"/>: the texts of an area are laid out in a grid (Mermaid <c>text</c>).</summary>
public sealed class VennTextNode
{
    private string _id;

    /// <summary>Initializes a new instance of the <see cref="VennTextNode"/> class.</summary>
    /// <param name="sets">The identifiers of the sets of the area that holds the text. They are sorted in ordinal order, as Mermaid does.</param>
    /// <param name="id">The identifier of the text, shown when it has no label; a style of this identifier sets its color.</param>
    /// <param name="label">The text shown, or <see langword="null"/> to show the identifier.</param>
    /// <exception cref="ArgumentException"><paramref name="sets"/> is empty or contains <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
    public VennTextNode(IEnumerable<string> sets, string id, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        Sets = VennDiagram.SortIdentifiers(sets, nameof(sets));
        if (Sets.Count is 0)
            throw new ArgumentException("A text needs at least one set.", nameof(sets));

        _id = id;
        Label = label;
    }

    /// <summary>Gets the identifiers of the sets of the area that holds the text, sorted in ordinal order.</summary>
    public ReadOnlyCollection<string> Sets { get; }

    /// <summary>Gets or sets the identifier of the text, shown when it has no label.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the text shown, or <see langword="null"/> to show the identifier.</summary>
    public string? Label { get; set; }

    /// <summary>Returns the text shown.</summary>
    /// <returns>The label, or the identifier.</returns>
    public override string ToString() => Label ?? Id;
}
