namespace Meziantou.Framework.Charts;

/// <summary>An ordinal color scale with its categories.</summary>
internal sealed class ResolvedOrdinalColorScale(IReadOnlyList<string> domain, IReadOnlyList<SceneColor> palette, SceneColor fallback) : ResolvedColorScale
{
    private readonly Dictionary<string, int> _indexes = domain.Select((value, index) => (value, index)).DistinctBy(item => item.value, StringComparer.Ordinal).ToDictionary(item => item.value, item => item.index, StringComparer.Ordinal);

    public IReadOnlyList<string> Domain { get; } = domain;

    public override SceneColor Map(ChannelValues values, int index)
    {
        var category = values.GetCategory(index);
        return category is null ? fallback : MapCategory(category);
    }

    public SceneColor MapCategory(string category)
    {
        if (!_indexes.TryGetValue(category, out var index) || palette.Count is 0)
            return fallback;

        return palette[index % palette.Count];
    }
}
