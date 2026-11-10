using System.Collections.ObjectModel;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// An area of a <see cref="VennDiagram"/>: a set, drawn as a circle (Mermaid <c>set</c>), or the overlap of several
/// sets (Mermaid <c>union</c>), with its label and its size.
/// </summary>
public sealed class VennArea
{
    private double? _size;

    /// <summary>Initializes a new instance of the <see cref="VennArea"/> class.</summary>
    /// <param name="sets">The identifiers of the sets: one for a set, several for an overlap. They are sorted in ordinal order, as Mermaid does.</param>
    /// <param name="label">The label, or <see langword="null"/> to show the identifier of a set and nothing on an overlap.</param>
    /// <param name="size">The size, or <see langword="null"/> for the size of Mermaid: 10 divided by the square of the number of sets.</param>
    /// <exception cref="ArgumentException"><paramref name="sets"/> is empty or contains <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is negative or not finite.</exception>
    public VennArea(IEnumerable<string> sets, string? label = null, double? size = null)
    {
        Sets = VennDiagram.SortIdentifiers(sets, nameof(sets));
        if (Sets.Count is 0)
            throw new ArgumentException("An area needs at least one set.", nameof(sets));

        Label = label;
        Size = size;
    }

    /// <summary>Gets the identifiers of the sets, sorted in ordinal order.</summary>
    public ReadOnlyCollection<string> Sets { get; }

    /// <summary>Gets a value indicating whether the area is a set, drawn as a circle, rather than an overlap.</summary>
    public bool IsSet => Sets.Count is 1;

    /// <summary>Gets or sets the label, or <see langword="null"/> to show the identifier of a set and nothing on an overlap.</summary>
    /// <remarks>An empty label of a set shows its identifier, as in Mermaid.</remarks>
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets the size: the area of the circle, or of the overlap, relative to the other sizes. A set of size 0 is not
    /// drawn, nor its overlaps. <see langword="null"/> is the size of Mermaid: 10 divided by the square of the number of
    /// sets, so 10 for a set and 2.5 for the overlap of two sets.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double? Size
    {
        get => _size;
        set
        {
            if (value is { } size && (!(size >= 0) || !double.IsFinite(size)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The size must be a positive finite number.");

            _size = value;
        }
    }

    /// <summary>Gets the size the layout uses: <see cref="Size"/>, or 10 divided by the square of the number of sets.</summary>
    public double EffectiveSize => Size ?? (10.0 / (Sets.Count * Sets.Count));

    /// <summary>Returns the sets of the area.</summary>
    /// <returns>The identifiers of the sets, separated by commas.</returns>
    public override string ToString() => string.Join(", ", Sets);
}
