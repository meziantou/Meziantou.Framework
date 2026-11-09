using System.Collections.ObjectModel;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A style applied to an area or to a text of a <see cref="VennDiagram"/> (Mermaid <c>style</c>): the targets are the sets
/// of an area, or the identifier of a text. The rules of the same targets are merged in order.
/// </summary>
public sealed class VennStyleRule
{
    private VennStyle _style;

    /// <summary>Initializes a new instance of the <see cref="VennStyleRule"/> class.</summary>
    /// <param name="targets">The identifiers of the sets of an area, or the identifier of a text. They are sorted in ordinal order, as Mermaid does.</param>
    /// <param name="style">The style.</param>
    /// <exception cref="ArgumentException"><paramref name="targets"/> is empty or contains <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is <see langword="null"/>.</exception>
    public VennStyleRule(IEnumerable<string> targets, VennStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Targets = VennDiagram.SortIdentifiers(targets, nameof(targets));
        if (Targets.Count is 0)
            throw new ArgumentException("A style needs at least one target.", nameof(targets));

        _style = style;
    }

    /// <summary>Gets the identifiers of the sets of the area, or the identifier of the text, sorted in ordinal order.</summary>
    public ReadOnlyCollection<string> Targets { get; }

    /// <summary>Gets or sets the style.</summary>
    public VennStyle Style
    {
        get => _style;
        set => _style = value ?? throw new ArgumentNullException(nameof(value));
    }
}
