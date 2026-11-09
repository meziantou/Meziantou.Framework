namespace Meziantou.Framework.Charts;

/// <summary>A color scale with its domain.</summary>
internal abstract class ResolvedColorScale
{
    public abstract SceneColor Map(ChannelValues values, int index);
}
