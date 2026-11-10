namespace Meziantou.Framework.Ntp;

/// <summary>
/// The selection and combine algorithms of RFC 5905 (sections 11.2.1 and 11.2.3, appendix A.5.5): find the largest
/// group of servers whose correctness intervals intersect, discard the others (falsetickers), and average the offsets of
/// the survivors weighted by the inverse of their root distance.
/// </summary>
internal static class NtpClockSelection
{
    /// <param name="offsets">The clock offset of each candidate, in ticks.</param>
    /// <param name="rootDistances">The root distance of each candidate, in ticks: its correctness interval is <c>offset ± rootDistance</c>. Must not be negative.</param>
    /// <param name="selected">Receives, for each candidate, whether it survived.</param>
    /// <param name="low">The lower bound of the intersection of the correctness intervals of the majority.</param>
    /// <param name="high">The upper bound of the intersection of the correctness intervals of the majority.</param>
    /// <returns><see langword="false"/> when no majority of the candidates has intersecting correctness intervals.</returns>
    public static bool TrySelect(ReadOnlySpan<long> offsets, ReadOnlySpan<long> rootDistances, Span<bool> selected, out long low, out long high)
    {
        var count = offsets.Length;
        selected.Clear();
        low = 0;
        high = 0;
        if (count is 0)
            return false;

        // Each candidate contributes the lower bound (+1), the offset (0) and the upper bound (-1) of its interval. On
        // equal values, the lower bounds come first and the upper bounds last, so touching intervals intersect.
        var endpoints = new (long Value, int Type)[count * 3];
        for (var i = 0; i < count; i++)
        {
            endpoints[(i * 3) + 0] = (offsets[i] - rootDistances[i], +1);
            endpoints[(i * 3) + 1] = (offsets[i], 0);
            endpoints[(i * 3) + 2] = (offsets[i] + rootDistances[i], -1);
        }

        Array.Sort(endpoints, static (a, b) => a.Value != b.Value ? a.Value.CompareTo(b.Value) : b.Type.CompareTo(a.Type));

        var isFound = false;
        for (var allowedFalsetickers = 0; 2 * allowedFalsetickers < count; allowedFalsetickers++)
        {
            var required = count - allowedFalsetickers;
            var midpointsOutside = 0;
            var hasLow = false;
            var hasHigh = false;

            var chime = 0;
            for (var i = 0; i < endpoints.Length; i++)
            {
                chime += endpoints[i].Type;
                if (chime >= required)
                {
                    low = endpoints[i].Value;
                    hasLow = true;
                    break;
                }

                if (endpoints[i].Type is 0)
                {
                    midpointsOutside++;
                }
            }

            chime = 0;
            for (var i = endpoints.Length - 1; i >= 0; i--)
            {
                chime -= endpoints[i].Type;
                if (chime >= required)
                {
                    high = endpoints[i].Value;
                    hasHigh = true;
                    break;
                }

                if (endpoints[i].Type is 0)
                {
                    midpointsOutside++;
                }
            }

            // More offsets lie outside the intersection than falsetickers are allowed: allow one more
            if (!hasLow || !hasHigh || midpointsOutside > allowedFalsetickers || low > high)
                continue;

            isFound = true;
            break;
        }

        if (!isFound)
        {
            low = 0;
            high = 0;
            return false;
        }

        // The truechimers are the candidates whose interval intersects the intersection of the majority
        for (var i = 0; i < count; i++)
        {
            selected[i] = offsets[i] - rootDistances[i] <= high && offsets[i] + rootDistances[i] >= low;
        }

        return true;
    }

    /// <summary>
    /// Averages the offsets of the selected candidates, weighted by the inverse of their root distance, and returns the
    /// root mean square of their differences to that average (the selection jitter).
    /// </summary>
    public static (long Offset, long Jitter) Combine(ReadOnlySpan<long> offsets, ReadOnlySpan<long> rootDistances, ReadOnlySpan<bool> selected)
    {
        var weightSum = 0d;
        var weightedOffsetSum = 0d;
        var minOffset = long.MaxValue;
        var maxOffset = long.MinValue;
        var selectedCount = 0;
        for (var i = 0; i < offsets.Length; i++)
        {
            if (!selected[i])
                continue;

            // A zero root distance is a perfect clock: give it the largest finite weight rather than an infinite one
            var weight = 1d / Math.Max(rootDistances[i], 1);
            weightSum += weight;
            weightedOffsetSum += weight * offsets[i];
            minOffset = Math.Min(minOffset, offsets[i]);
            maxOffset = Math.Max(maxOffset, offsets[i]);
            selectedCount++;
        }

        if (selectedCount is 0)
            throw new ArgumentException("No candidate is selected.", nameof(selected));

        // Rounding cannot move the average outside of the range of the offsets it averages
        var offset = Math.Clamp((long)Math.Round(weightedOffsetSum / weightSum, MidpointRounding.AwayFromZero), minOffset, maxOffset);

        var squareSum = 0d;
        for (var i = 0; i < offsets.Length; i++)
        {
            if (selected[i])
            {
                var difference = (double)offsets[i] - offset;
                squareSum += difference * difference;
            }
        }

        var jitter = (long)Math.Sqrt(squareSum / selectedCount);
        return (offset, jitter);
    }
}
