namespace Meziantou.Framework.Charts;

/// <summary>Chooses the type and the domain of the scales of a plot from the values of its marks.</summary>
internal static class ScaleResolver
{
    public static ResolvedScale? ResolvePosition(Scale? scale, IReadOnlyList<ChannelValues> channels, bool band, bool zero, bool exact, double headroom = 0)
    {
        if (scale is null && channels.Count is 0)
            return null;

        switch (scale)
        {
            case BandScale bandScale:
                return new ResolvedOrdinalScale(bandScale.Domain ?? GetCategories(channels), isBand: true, bandScale.PaddingInner, bandScale.PaddingOuter, bandScale.Align, bandScale.Reverse);
            case PointScale pointScale:
                return new ResolvedOrdinalScale(pointScale.Domain ?? GetCategories(channels), isBand: false, 1, pointScale.Padding, pointScale.Align, pointScale.Reverse);
            case ContinuousScale continuous:
                return CreateContinuous(continuous, channels, zero, exact);
        }

        if (band)
            return new ResolvedOrdinalScale(GetCategories(channels), isBand: true, 0.1, 0.1, 0.5, reverse: false);

        if (channels.Any(channel => channel.Kind is ChannelKind.Category))
        {
            if (headroom > 0)
            {
                // The outer padding and the alignment leave the headroom before the first point and a quarter of a step after the last
                var padding = headroom + 0.25;
                return new ResolvedOrdinalScale(GetCategories(channels), isBand: false, 1, padding / 2, headroom / padding, reverse: false);
            }

            return new ResolvedOrdinalScale(GetCategories(channels), isBand: false, 1, 0.5, 0.5, reverse: false);
        }

        return channels.Any(channel => channel.Kind is ChannelKind.Time)
            ? CreateContinuous(new TimeScale(), channels, zero: false, exact)
            : CreateContinuous(null, channels, zero, exact);
    }

    public static ResolvedColorScale? ResolveColor(ColorScale? scale, IReadOnlyList<ChannelValues> channels, ChartTheme theme)
    {
        if (scale is null && channels.Count is 0)
            return null;

        var fallback = theme.GetPaletteColor(0);
        switch (scale)
        {
            case OrdinalColorScale ordinal:
                return new ResolvedOrdinalColorScale(ordinal.Domain ?? GetCategories(channels), ordinal.Palette ?? theme.Palette, fallback);
            case SequentialColorScale sequential:
                {
                    var (min, max) = GetNumericExtent(channels);
                    return new ResolvedContinuousColorScale(sequential.Scheme, sequential.Minimum ?? min, sequential.Maximum ?? max, midpoint: null, sequential.TickFormat, fallback)
                    {
                        HasFixedMinimum = sequential.Minimum is not null,
                        HasFixedMaximum = sequential.Maximum is not null,
                    };
                }

            case DivergingColorScale diverging:
                {
                    var (min, max) = GetNumericExtent(channels);
                    var low = diverging.Minimum ?? Math.Min(min, diverging.Midpoint);
                    var high = diverging.Maximum ?? Math.Max(max, diverging.Midpoint);
                    return new ResolvedContinuousColorScale(diverging.Scheme, low, high, diverging.Midpoint, diverging.TickFormat, fallback)
                    {
                        HasFixedMinimum = diverging.Minimum is not null,
                        HasFixedMaximum = diverging.Maximum is not null,
                    };
                }
        }

        if (channels.Any(channel => channel.Kind is ChannelKind.Category))
            return new ResolvedOrdinalColorScale(GetCategories(channels), theme.Palette, fallback);

        var (minimum, maximum) = GetNumericExtent(channels);
        return new ResolvedContinuousColorScale(ColorScheme.Viridis, minimum, maximum, midpoint: null, format: null, fallback);
    }

    public static ResolvedSizeScale? ResolveSize(SizeScale? scale, IReadOnlyList<ChannelValues> channels)
    {
        if (channels.Count is 0)
            return null;

        var (_, max) = GetNumericExtent(channels);
        return new ResolvedSizeScale(scale?.Maximum ?? max, scale?.MaxRadius ?? 12);
    }

    internal static List<string> GetCategories(IReadOnlyList<ChannelValues> channels)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var channel in channels)
        {
            for (var i = 0; i < channel.Count; i++)
            {
                if (channel.GetCategory(i) is { } category && seen.Add(category))
                {
                    result.Add(category);
                }
            }
        }

        return result;
    }

    private static (double Min, double Max) GetNumericExtent(IReadOnlyList<ChannelValues> channels, bool positiveOnly = false)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        foreach (var channel in channels)
        {
            if (!channel.IsContinuous)
                continue;

            foreach (var value in channel.Numbers)
            {
                // An infinite value has no position: it would make every position of the scale NaN
                if (!double.IsFinite(value) || (positiveOnly && value <= 0))
                    continue;

                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
        }

        return min > max ? (double.NaN, double.NaN) : (min, max);
    }

    private static ResolvedContinuousScale CreateContinuous(ContinuousScale? scale, IReadOnlyList<ChannelValues> channels, bool zero, bool exact)
    {
        var (transform, parameter) = scale switch
        {
            LogScale log => (ContinuousTransformKind.Log, log.Base),
            PowScale pow => (ContinuousTransformKind.Pow, pow.Exponent),
            SqrtScale => (ContinuousTransformKind.Pow, 0.5),
            SymlogScale symlog => (ContinuousTransformKind.Symlog, symlog.Constant),
            _ => (ContinuousTransformKind.Linear, 0.0),
        };
        var isTime = scale is TimeScale;
        var kind = isTime ? ChannelKind.Time : ChannelKind.Number;
        var isLog = transform is ContinuousTransformKind.Log;
        var (min, max) = GetNumericExtent(channels, positiveOnly: isLog);

        if ((scale?.Zero ?? zero) && !isLog && !isTime && !double.IsNaN(min))
        {
            min = Math.Min(min, 0);
            max = Math.Max(max, 0);
        }

        if (scale is TimeScale { Start: { } start })
        {
            min = ChannelValues.ToTime(start);
        }
        else if (scale?.Minimum is { } minimum)
        {
            min = minimum;
        }

        if (scale is TimeScale { End: { } end })
        {
            max = ChannelValues.ToTime(end);
        }
        else if (scale?.Maximum is { } maximum)
        {
            max = maximum;
        }

        (min, max) = FixDomain(min, max, isLog, isTime, scale?.Minimum is not null || scale is TimeScale { Start: not null }, scale?.Maximum is not null || scale is TimeScale { End: not null });
        var nice = scale?.Nice ?? (!isTime && !exact);
        return new ResolvedContinuousScale(transform, kind, min, max, scale, parameter, nice);
    }

    /// <summary>Gives an empty domain or a domain of a single value a width, so the values are drawn in its middle.</summary>
    private static (double Min, double Max) FixDomain(double min, double max, bool isLog, bool isTime, bool hasMinimum, bool hasMaximum)
    {
        if (double.IsNaN(min) && double.IsNaN(max))
            return isLog ? (1, 10) : isTime ? (0, 86_400_000) : (0, 1);

        if (double.IsNaN(min))
        {
            min = isLog ? max / 10 : max - 1;
        }

        if (double.IsNaN(max))
        {
            max = isLog ? min * 10 : min + 1;
        }

        if (min != max)
            return (min, max);

        if (isLog)
            return (min / 10, max * 10);

        var delta = isTime ? 86_400_000 : min == 0 ? 1 : Math.Abs(min) * 0.1;
        if (hasMinimum)
            return (min, max + delta);

        if (hasMaximum)
            return (min - delta, max);

        return min == 0 && !isTime ? (0, 1) : (min - delta, max + delta);
    }
}
