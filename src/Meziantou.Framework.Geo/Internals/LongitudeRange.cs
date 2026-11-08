namespace Meziantou.Framework.Geo.Internals;

/// <summary>Collects arcs of longitudes, and finds the shortest arc that covers them all, which can cross the 180th meridian.</summary>
internal sealed class LongitudeRange
{
    private readonly List<(double Start, double End)> _intervals = [];
    private bool _isFull;

    public bool IsEmpty => !_isFull && _intervals.Count == 0;

    /// <summary>Adds the arc that starts at a longitude and goes east for a length, in degrees.</summary>
    public void Add(double start, double length)
    {
        if (_isFull)
            return;

        if (length >= 360)
        {
            _isFull = true;
            return;
        }

        start = NormalizeStart(start);
        var end = start + length;
        if (end > 180)
        {
            _intervals.Add((start, 180));
            _intervals.Add((-180, end - 360));
        }
        else
        {
            _intervals.Add((start, end));
        }
    }

    public void AddFull() => _isFull = true;

    /// <summary>Gets the shortest arc that covers every arc, as the west and east sides of a box: west is east of east when the arc crosses the 180th meridian.</summary>
    public (double West, double East) GetRange()
    {
        if (_isFull)
            return (-180, 180);

        _intervals.Sort();
        var merged = new List<(double Start, double End)>();
        foreach (var interval in _intervals)
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, interval.End));
            }
            else
            {
                merged.Add(interval);
            }
        }

        // The arc is the complement of the largest gap between the merged arcs. The gap around the 180th meridian wins
        // ties, so that the box does not cross it without a reason.
        var bestGap = merged[0].Start + 360 - merged[^1].End;
        var bestIndex = -1;
        for (var i = 1; i < merged.Count; i++)
        {
            var gap = merged[i].Start - merged[i - 1].End;
            if (gap > bestGap)
            {
                bestGap = gap;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
            return (merged[0].Start, merged[^1].End);

        // An arc that ends on the 180th meridian ends at 180, not at -180
        var east = merged[bestIndex - 1].End;
        return (merged[bestIndex].Start, east == -180 ? 180 : east);
    }

    /// <summary>Normalizes a longitude to [-180, 180).</summary>
    private static double NormalizeStart(double longitude)
    {
        if (longitude is >= -180 and < 180)
            return longitude;

        return longitude - (360 * Math.Floor((longitude + 180) / 360));
    }
}
