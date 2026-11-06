namespace Meziantou.Framework.Charts;

/// <summary>Derives the rows a mark draws from its data: stacking, binning, grouping...</summary>
public abstract class Transform
{
    private protected Transform()
    {
    }

    internal abstract MarkData Apply(MarkData data);
}
