using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>Filters applied one after the other, each to the result of the one before it: a list of filter functions of CSS.</summary>
public sealed class FilterChain : Filter
{
    private readonly Filter[] _filters;

    /// <summary>Initializes a new instance of the <see cref="FilterChain"/> class.</summary>
    /// <param name="filters">The filters, in the order they are applied. They are copied.</param>
    /// <exception cref="ArgumentNullException">A filter is <see langword="null"/>.</exception>
    public FilterChain(params ReadOnlySpan<Filter> filters)
    {
        foreach (var filter in filters)
        {
            ArgumentNullException.ThrowIfNull(filter, nameof(filters));
        }

        _filters = filters.ToArray();
    }

    /// <summary>Gets the filters, in the order they are applied.</summary>
    public IReadOnlyList<Filter> Filters => _filters;

    internal override void Apply(FilterProcessor processor, FilterSurface surface)
    {
        foreach (var filter in _filters)
        {
            filter.Apply(processor, surface);
        }
    }

    internal override bool TryGetBoundsCore(RectangleD bounds, out RectangleD result)
    {
        result = bounds;
        foreach (var filter in _filters)
        {
            if (!filter.TryGetBoundsCore(result, out result))
                return false;
        }

        return true;
    }

    internal override (double X, double Y) GetReach()
    {
        double x = 0, y = 0;
        foreach (var filter in _filters)
        {
            var (fx, fy) = filter.GetReach();
            x += fx;
            y += fy;
        }

        return (x, y);
    }

    internal override int GetLocalSteps(bool isAfterReach)
    {
        var steps = 0;
        foreach (var filter in _filters)
        {
            var count = filter.GetLocalSteps(isAfterReach);
            if (count < 0)
                return -1;

            steps += count;
            var (x, y) = filter.GetReach();
            isAfterReach |= x > 0 || y > 0;
        }

        return steps;
    }
}
