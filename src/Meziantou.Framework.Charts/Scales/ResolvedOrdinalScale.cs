namespace Meziantou.Framework.Charts;

/// <summary>A band or point scale with its categories, laid out as d3's <c>scaleBand</c>.</summary>
internal sealed class ResolvedOrdinalScale : ResolvedScale
{
    private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);
    private readonly double _paddingInner;
    private readonly double _paddingOuter;
    private readonly double _align;
    private readonly bool _reverse;
    private double _start;
    private double _step;
    private double _bandwidth;

    public ResolvedOrdinalScale(IReadOnlyList<string> domain, bool isBand, double paddingInner, double paddingOuter, double align, bool reverse)
    {
        Domain = domain;
        IsBand = isBand;
        _paddingInner = isBand ? Math.Clamp(paddingInner, 0, 1) : 1;
        _paddingOuter = Math.Max(0, paddingOuter);
        _align = Math.Clamp(align, 0, 1);
        _reverse = reverse;
        for (var i = 0; i < domain.Count; i++)
        {
            _indexes.TryAdd(domain[i], i);
        }
    }

    public IReadOnlyList<string> Domain { get; }

    public bool IsBand { get; }

    public override bool IsOrdinal => true;

    public override double Bandwidth => _bandwidth;

    public double Step => _step;

    public override double Baseline => RangeStart;

    public override double Map(ChannelValues values, int index)
    {
        var category = values.GetCategory(index);
        return category is null ? double.NaN : MapCategory(category);
    }

    public double MapCategory(string category)
    {
        if (!_indexes.TryGetValue(category, out var index))
            return double.NaN;

        if (_reverse)
        {
            index = Domain.Count - 1 - index;
        }

        return _start + (_step * index);
    }

    public int IndexOf(string category) => _indexes.TryGetValue(category, out var index) ? index : -1;

    public override IReadOnlyList<Tick> GetTicks()
    {
        var result = new List<Tick>(Domain.Count);
        foreach (var category in Domain)
        {
            result.Add(new Tick(MapCategory(category) + (_bandwidth / 2), category));
        }

        return result;
    }

    protected override void OnConfigure(int tickCount)
    {
        var count = Domain.Count;
        var start = RangeStart;
        var stop = RangeEnd;
        if (IsPeriodic)
        {
            // Around a full circle, the last band ends where the first starts: no outer padding, one step per category
            _step = (stop - start) / Math.Max(1, count);
            _bandwidth = IsBand ? _step * (1 - _paddingInner) : 0;
            _start = start + (IsBand ? _step * _paddingInner / 2 : 0);
            return;
        }

        _step = (stop - start) / Math.Max(1, count - _paddingInner + (_paddingOuter * 2));
        _start = start + ((stop - start - (_step * (count - _paddingInner))) * _align);
        _bandwidth = _step * (1 - _paddingInner);
    }
}
