namespace Meziantou.Framework.Charts;

/// <summary>The options of the grid of small charts of a plot whose marks have facets (<see cref="Mark{T}.FacetX"/>, <see cref="Mark{T}.FacetY"/>).</summary>
public sealed class FacetOptions
{
    private int? _columns;

    /// <summary>
    /// Gets or sets the number of columns when the marks only have <see cref="Mark{T}.FacetX"/>: the charts wrap onto
    /// new rows, each with its facet as a header. With <see langword="null"/>, the default, each facet is a column of a single row.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive number.</exception>
    public int? Columns
    {
        get => _columns;
        set
        {
            if (value is <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The number of columns must be positive.");

            _columns = value;
        }
    }

    /// <summary>Gets or sets the space between two charts of the grid. The default is 12.</summary>
    public double Gap { get; set; } = 12;

    /// <summary>Gets or sets the facets of the columns, in order, or <see langword="null"/> for the values of the data in the order they first appear.</summary>
    public IReadOnlyList<string>? XDomain { get; set; }

    /// <summary>Gets or sets the facets of the rows, in order, or <see langword="null"/> for the values of the data in the order they first appear.</summary>
    public IReadOnlyList<string>? YDomain { get; set; }
}
