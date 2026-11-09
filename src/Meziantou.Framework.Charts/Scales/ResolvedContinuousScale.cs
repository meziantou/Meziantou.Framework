namespace Meziantou.Framework.Charts;

/// <summary>A linear, log, power, symlog or time scale with its domain.</summary>
internal sealed class ResolvedContinuousScale : ResolvedScale
{
    private readonly double _parameter;
    private readonly bool _nice;
    private readonly bool _clamp;
    private readonly bool _reverse;
    private readonly double _dataStart;
    private readonly double _dataEnd;
    private readonly ContinuousScale? _options;
    private double _t0;
    private double _t1;
    private int _tickCount = 10;

    public ResolvedContinuousScale(ContinuousTransformKind transform, ChannelKind kind, double start, double end, ContinuousScale? options, double parameter, bool nice)
    {
        Transform = transform;
        Kind = kind;
        _dataStart = start;
        _dataEnd = end;
        _options = options;
        _parameter = parameter;
        _nice = nice;
        _clamp = options?.Clamp ?? false;
        _reverse = options?.Reverse ?? false;
        DomainStart = start;
        DomainEnd = end;
        UpdateTransformedDomain();
    }

    public ContinuousTransformKind Transform { get; }

    public ChannelKind Kind { get; }

    public double DomainStart { get; private set; }

    public double DomainEnd { get; private set; }

    public override bool IsOrdinal => false;

    public override double Baseline
    {
        get
        {
            var min = Math.Min(DomainStart, DomainEnd);
            var zero = Transform is ContinuousTransformKind.Log ? min : Math.Clamp(0, min, Math.Max(DomainStart, DomainEnd));
            var position = MapNumber(zero);

            // 0 maps to infinity on a power scale with a negative exponent
            return double.IsNaN(position) ? MapNumber(min) : position;
        }
    }

    public override double Map(ChannelValues values, int index) => values.IsContinuous ? MapNumber(values.Numbers[index]) : double.NaN;

    public double MapNumber(double value)
    {
        if (double.IsNaN(value))
            return double.NaN;

        // A value whose transform is infinite (0 on a power scale with a negative exponent, a huge value raised to a
        // power) has no position, as a value the transform does not accept
        var t = Apply(value);
        if (!double.IsFinite(t))
            return double.NaN;

        var ratio = _t1 == _t0 ? 0.5 : (t - _t0) / (_t1 - _t0);
        if (_clamp)
        {
            ratio = Math.Clamp(ratio, 0, 1);
        }

        var (start, end) = _reverse ? (RangeEnd, RangeStart) : (RangeStart, RangeEnd);
        var position = start + (ratio * (end - start));
        return double.IsFinite(position) ? position : double.NaN;
    }

    public override IReadOnlyList<Tick> GetTicks()
    {
        var min = Math.Min(DomainStart, DomainEnd);
        var max = Math.Max(DomainStart, DomainEnd);
        double[] values;
        if (_options?.Ticks is { } explicitTicks)
        {
            values = [.. explicitTicks];
        }
        else
        {
            values = Transform switch
            {
                ContinuousTransformKind.Log => TickGenerator.LogTicks(min, max, _parameter, _tickCount),
                _ when Kind is ChannelKind.Time => TickGenerator.TimeTicks(min, max, _tickCount),
                _ => TickGenerator.Ticks(min, max, _tickCount),
            };
        }

        var step = values.Length > 1 ? Math.Abs(values[1] - values[0]) : Math.Abs(max - min) / Math.Max(1, _tickCount);
        var tolerance = Math.Abs(max - min) * 1e-9;
        var result = new List<Tick>(values.Length);
        foreach (var value in values)
        {
            if (value < min - tolerance || value > max + tolerance)
                continue;

            var position = MapNumber(value);
            if (double.IsNaN(position))
                continue;

            result.Add(new Tick(position, Format(value, step)));
        }

        return result;
    }

    public string Format(double value, double step)
    {
        if (Kind is ChannelKind.Time)
        {
            if (_options is TimeScale { TimeFormat: { } timeFormat })
                return timeFormat(new DateTimeOffset(TimeFormats.FromMilliseconds(value), TimeSpan.Zero));

            return _options?.TickFormat?.Invoke(value) ?? TimeFormats.FormatTick(value);
        }

        if (_options?.TickFormat is { } format)
            return format(value);

        return Transform is ContinuousTransformKind.Log ? NumberFormats.Default(value) : NumberFormats.ForStep(value, step);
    }

    protected override void OnConfigure(int tickCount)
    {
        _tickCount = _options?.TickCount ?? tickCount;
        DomainStart = _dataStart;
        DomainEnd = _dataEnd;
        if (_nice)
        {
            ApplyNice();
        }

        UpdateTransformedDomain();
    }

    private void ApplyNice()
    {
        var start = DomainStart;
        var end = DomainEnd;
        switch (Transform)
        {
            case ContinuousTransformKind.Log:
                start = Math.Pow(_parameter, Math.Floor(Math.Log(start, _parameter) + 1e-9));
                end = Math.Pow(_parameter, Math.Ceiling(Math.Log(end, _parameter) - 1e-9));
                break;

            case not ContinuousTransformKind.Log when Kind is ChannelKind.Time:
                var interval = TimeInterval.Choose(start, end, _tickCount);
                (start, end) = (interval.Floor(start), interval.Ceiling(end));
                break;

            default:
                (start, end) = TickGenerator.Nice(start, end, _tickCount);
                break;
        }

        // A bound the user set is kept
        DomainStart = HasExplicitStart() ? DomainStart : start;
        DomainEnd = HasExplicitEnd() ? DomainEnd : end;
    }

    private bool HasExplicitStart() => _options is TimeScale { Start: not null } || _options?.Minimum is not null;

    private bool HasExplicitEnd() => _options is TimeScale { End: not null } || _options?.Maximum is not null;

    private void UpdateTransformedDomain()
    {
        _t0 = Apply(DomainStart);
        _t1 = Apply(DomainEnd);
    }

    private double Apply(double value)
    {
        return Transform switch
        {
            ContinuousTransformKind.Log => value > 0 ? Math.Log(value) : double.NaN,
            ContinuousTransformKind.Pow => value < 0 ? -Math.Pow(-value, _parameter) : Math.Pow(value, _parameter),
            ContinuousTransformKind.Symlog => double.IsNaN(value) ? double.NaN : Math.Sign(value) * Math.Log(1 + Math.Abs(value / _parameter)),
            _ => value,
        };
    }
}
