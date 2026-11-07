namespace Meziantou.Framework.Charts;

/// <summary>The extent of a rule along its length: the band of a category, an interval, or the whole scale.</summary>
internal static class RuleExtent
{
    public static (double Start, double End) Get(ResolvedScale? scale, ChannelValues? band, ChannelValues? start, ChannelValues? end, int index, double defaultStart, double defaultEnd)
    {
        if (start is null && end is null && band is not null && scale is not null)
        {
            var position = scale.Map(band, index);
            return (position, position + scale.Bandwidth);
        }

        return (
            start is null || scale is null ? defaultStart : PointSeries.Center(scale, scale.Map(start, index)),
            end is null || scale is null ? defaultEnd : PointSeries.Center(scale, scale.Map(end, index)));
    }
}
