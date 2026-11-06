namespace Meziantou.Framework.Charts;

/// <summary>A color scale with its domain.</summary>
internal abstract class ResolvedColorScale
{
    public abstract ChartColor Map(ChannelValues values, int index);
}
