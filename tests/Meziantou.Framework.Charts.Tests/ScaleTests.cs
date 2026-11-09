namespace Meziantou.Framework.Charts.Tests;

public sealed class ScaleTests
{
    [Fact]
    public void Ticks_MatchD3()
    {
        Assert.Equal([0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1], TickGenerator.Ticks(0, 1, 10));
        Assert.Equal([0, 0.2, 0.4, 0.6, 0.8, 1], TickGenerator.Ticks(0, 1, 5));
        Assert.Equal([0, 2, 4, 6, 8, 10], TickGenerator.Ticks(0, 10, 5));
        Assert.Equal([-10, -8, -6, -4, -2, 0, 2, 4, 6, 8, 10], TickGenerator.Ticks(-10, 10, 10));
        Assert.Equal([1, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2, 0.1, 0], TickGenerator.Ticks(1, 0, 10));
        Assert.Equal([10, 5, 0], TickGenerator.Ticks(10, 0, 2));
        Assert.Equal([1], TickGenerator.Ticks(1, 1, 10));
        Assert.Empty(TickGenerator.Ticks(0, 1, 0));
        Assert.Empty(TickGenerator.Ticks(double.NaN, 1, 10));
        Assert.Equal([0, 100], TickGenerator.Ticks(0, 100, 1));
    }

    [Fact]
    public void TickIncrementAndStep()
    {
        Assert.Equal(-10, TickGenerator.TickIncrement(0, 1, 10));
        Assert.Equal(1, TickGenerator.TickIncrement(0, 10, 10));
        Assert.Equal(20, TickGenerator.TickIncrement(0, 100, 5));
        Assert.Equal(0.1, TickGenerator.TickStep(0, 1, 10));
        Assert.Equal(-0.1, TickGenerator.TickStep(1, 0, 10));
        Assert.Equal(20, TickGenerator.TickStep(0, 100, 5));
    }

    [Theory]
    [InlineData(0, 0.96, 10, 0, 1)]
    [InlineData(0, 96, 10, 0, 100)]
    [InlineData(0.201479, 0.996679, 10, 0.2, 1)]
    [InlineData(1.1, 10.9, 10, 1, 11)]
    [InlineData(10.9, 1.1, 10, 11, 1)]
    [InlineData(123.1, 6.7, 10, 130, 0)]
    [InlineData(0, 0.49, 10, 0, 0.5)]
    [InlineData(-0.1, 51.1, 5, -10, 60)]
    public void Nice_MatchesD3(double start, double stop, double count, double expectedStart, double expectedStop)
    {
        var (niceStart, niceStop) = TickGenerator.Nice(start, stop, count);
        Assert.Equal(expectedStart, niceStart, 1e-12);
        Assert.Equal(expectedStop, niceStop, 1e-12);
    }

    [Fact]
    public void Nice_OfAnEmptyDomainDoesNotChangeIt()
    {
        Assert.Equal((5.0, 5.0), TickGenerator.Nice(5, 5, 10));
    }

    [Fact]
    public void LogTicks()
    {
        Assert.Equal([1, 2, 5, 10, 20, 50, 100, 200, 500, 1000], TickGenerator.LogTicks(1, 1000, 10, 10));
        Assert.Equal([1, 100, 10000, 1000000], TickGenerator.LogTicks(1, 1e6, 10, 4));
        Assert.Equal([1000, 500, 200, 100], TickGenerator.LogTicks(1000, 100, 10, 10));
        Assert.Equal([0.5, 1, 2], TickGenerator.LogTicks(0.3, 3, 10, 20));
        Assert.Equal([2, 4, 8, 16, 32], TickGenerator.LogTicks(2, 32, 2, 10));
        Assert.Empty(TickGenerator.LogTicks(-1, 10, 10, 10));
    }

    [Fact]
    public void TimeTicks_ChooseTheClosestInterval()
    {
        var start = ToMilliseconds(2024, 1, 1);
        var ticks = TickGenerator.TimeTicks(start, start + 86_400_000, 10);
        Assert.Equal(["00:00", "03:00", "06:00", "09:00", "12:00", "15:00", "18:00", "21:00", "00:00"], ticks.Select(tick => TimeFormats.FromMilliseconds(tick).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)).ToArray());

        var months = TickGenerator.TimeTicks(ToMilliseconds(2024, 1, 1), ToMilliseconds(2024, 12, 31), 4);
        Assert.Equal(["2024", "Apr", "Jul", "Oct"], months.Select(TimeFormats.FormatTick).ToArray());

        var years = TickGenerator.TimeTicks(ToMilliseconds(1950, 1, 1), ToMilliseconds(2024, 1, 1), 5);
        Assert.Equal(["1960", "1980", "2000", "2020"], years.Select(TimeFormats.FormatTick).ToArray());

        var reversed = TickGenerator.TimeTicks(start + 86_400_000, start, 2);
        Assert.True(reversed[0] > reversed[^1]);

        var milliseconds = TickGenerator.TimeTicks(start, start + 10, 5);
        Assert.HasCount(6, milliseconds);
    }

    [Fact]
    public void TimeInterval_FloorAndCeiling()
    {
        var march15 = ToMilliseconds(2024, 3, 15) + 3_600_000;
        var month = TimeInterval.Choose(ToMilliseconds(2024, 1, 1), ToMilliseconds(2024, 12, 31), 12);
        Assert.Equal(TimeUnit.Month, month.Unit);
        Assert.Equal(ToMilliseconds(2024, 3, 1), month.Floor(march15));
        Assert.Equal(ToMilliseconds(2024, 4, 1), month.Ceiling(march15));
        Assert.Equal(ToMilliseconds(2024, 4, 1), month.Ceiling(ToMilliseconds(2024, 4, 1)));

        var quarter = TimeInterval.Choose(ToMilliseconds(2020, 1, 1), ToMilliseconds(2024, 12, 31), 20);
        Assert.Equal((TimeUnit.Month, 3.0), (quarter.Unit, quarter.Step));
        Assert.Equal(ToMilliseconds(2024, 1, 1), quarter.Floor(march15));
        Assert.Equal(ToMilliseconds(2024, 4, 1), quarter.Ceiling(march15));

        var week = TimeInterval.Choose(0, 7 * 86_400_000.0 * 10, 10);
        Assert.Equal(TimeUnit.Week, week.Unit);
        Assert.Equal(DayOfWeek.Sunday, TimeFormats.FromMilliseconds(week.Floor(march15)).DayOfWeek);

        var millisecond = TimeInterval.Choose(0, 10, 10);
        Assert.Equal(TimeUnit.Millisecond, millisecond.Unit);
        Assert.Equal(4, millisecond.Floor(4.5));
        Assert.Equal(5, millisecond.Ceiling(4.5));

        foreach (var (count, unit) in new[] { (60, TimeUnit.Second), (60, TimeUnit.Minute), (24, TimeUnit.Hour), (3, TimeUnit.Day) })
        {
            var interval = TimeInterval.Choose(0, unit switch { TimeUnit.Second => 60_000, TimeUnit.Minute => 3_600_000, TimeUnit.Hour => 86_400_000, _ => 3 * 86_400_000 }, count);
            Assert.Equal(unit, interval.Unit);
            Assert.Equal(interval.Floor(interval.Ceiling(1234567)), interval.Ceiling(1234567));
        }
    }

    [Fact]
    public void TimeFormats_ShowTheLargestUnitTheTickDoesNotFallOn()
    {
        Assert.Equal("2024", TimeFormats.FormatTick(ToMilliseconds(2024, 1, 1)));
        Assert.Equal("Feb", TimeFormats.FormatTick(ToMilliseconds(2024, 2, 1)));
        Assert.Equal("Feb 3", TimeFormats.FormatTick(ToMilliseconds(2024, 2, 3)));
        Assert.Equal("06:00", TimeFormats.FormatTick(ToMilliseconds(2024, 2, 3) + 21_600_000));
        Assert.Equal(":30", TimeFormats.FormatTick(ToMilliseconds(2024, 2, 3) + 30_000));
        Assert.Equal(".500", TimeFormats.FormatTick(ToMilliseconds(2024, 2, 3) + 500));
        Assert.Equal("2024-02-03", TimeFormats.FormatCategory(ToMilliseconds(2024, 2, 3)));
        Assert.Equal("2024-02-03 06:00", TimeFormats.FormatCategory(ToMilliseconds(2024, 2, 3) + 21_600_000));
    }

    [Fact]
    public void BandScale_MatchesD3()
    {
        var scale = new ResolvedOrdinalScale(["a", "b", "c"], isBand: true, paddingInner: 0.1, paddingOuter: 0.1, align: 0.5, reverse: false);
        scale.Configure(0, 120, 10);
        var step = 120 / (3 - 0.1 + 0.2);
        Assert.Equal(step, scale.Step, 1e-9);
        Assert.Equal(step * 0.9, scale.Bandwidth, 1e-9);
        Assert.Equal((120 - (step * 2.9)) / 2, scale.MapCategory("a"), 1e-9);
        Assert.Equal(scale.MapCategory("a") + (2 * step), scale.MapCategory("c"), 1e-9);
        Assert.True(double.IsNaN(scale.MapCategory("z")));
        Assert.Equal(-1, scale.IndexOf("z"));
        Assert.Equal(["a", "b", "c"], scale.GetTicks().Select(tick => tick.Label).ToArray());

        var reversed = new ResolvedOrdinalScale(["a", "b", "c"], isBand: true, 0, 0, 0.5, reverse: true);
        reversed.Configure(0, 30, 10);
        Assert.Equal(20, reversed.MapCategory("a"), 1e-9);
        Assert.Equal(0, reversed.MapCategory("c"), 1e-9);
    }

    [Fact]
    public void PointScale_MatchesD3()
    {
        var scale = new ResolvedOrdinalScale(["a", "b", "c"], isBand: false, 1, 0.5, 0.5, reverse: false);
        scale.Configure(0, 100, 10);
        Assert.Equal(100 / 3.0 / 2, scale.MapCategory("a"), 1e-9);
        Assert.Equal(50, scale.MapCategory("b"), 1e-9);
        Assert.Equal(0, scale.Bandwidth);

        // Around a full circle, the categories are one step apart and the last one does not meet the first
        scale.Configure(0, 2 * Math.PI, 10, periodic: true);
        Assert.Equal(0, scale.MapCategory("a"), 1e-9);
        Assert.Equal(4 * Math.PI / 3, scale.MapCategory("c"), 1e-9);

        var band = new ResolvedOrdinalScale(["a", "b"], isBand: true, 0.2, 0, 0.5, reverse: false);
        band.Configure(0, 100, 10, periodic: true);
        Assert.Equal(5, band.MapCategory("a"), 1e-9);
        Assert.Equal(40, band.Bandwidth, 1e-9);
    }

    [Fact]
    public void ContinuousScales_MapValues()
    {
        var linear = new ResolvedContinuousScale(ContinuousTransformKind.Linear, ChannelKind.Number, 0, 10, options: null, 0, nice: false);
        linear.Configure(100, 200, 5);
        Assert.Equal(150, linear.MapNumber(5));
        Assert.Equal(220, linear.MapNumber(12));
        Assert.True(double.IsNaN(linear.MapNumber(double.NaN)));
        Assert.Equal(100, linear.Baseline);

        var clamped = new ResolvedContinuousScale(ContinuousTransformKind.Linear, ChannelKind.Number, 0, 10, new LinearScale { Clamp = true, Reverse = true }, 0, nice: false);
        clamped.Configure(100, 200, 5);
        Assert.Equal(100, clamped.MapNumber(12));
        Assert.Equal(200, clamped.MapNumber(-5));

        var log = new ResolvedContinuousScale(ContinuousTransformKind.Log, ChannelKind.Number, 1, 100, null, 10, nice: false);
        log.Configure(0, 100, 5);
        Assert.Equal(50, log.MapNumber(10), 1e-9);
        Assert.True(double.IsNaN(log.MapNumber(0)));
        Assert.Equal(0, log.Baseline, 1e-9);

        var pow = new ResolvedContinuousScale(ContinuousTransformKind.Pow, ChannelKind.Number, -2, 2, null, 2, nice: false);
        pow.Configure(0, 80, 5);
        Assert.Equal(50, pow.MapNumber(1), 1e-9);
        Assert.Equal(30, pow.MapNumber(-1), 1e-9);

        var symlog = new ResolvedContinuousScale(ContinuousTransformKind.Symlog, ChannelKind.Number, -10, 10, null, 1, nice: false);
        symlog.Configure(0, 100, 5);
        Assert.Equal(50, symlog.MapNumber(0), 1e-9);
        Assert.Equal(100 * (0.5 + (0.5 * Math.Log(2) / Math.Log(11))), symlog.MapNumber(1), 1e-9);

        var single = new ResolvedContinuousScale(ContinuousTransformKind.Linear, ChannelKind.Number, 3, 3, null, 0, nice: false);
        single.Configure(0, 100, 5);
        Assert.Equal(50, single.MapNumber(3));
        Assert.True(double.IsNaN(single.Map(ChannelValues.FromCategories(["a"]), 0)));
    }

    [Fact]
    public void ContinuousScales_ValuesThatMapToInfinityHaveNoPosition()
    {
        // 0 raised to a negative power is infinite: it has no position, so its tick is dropped and the bars start at the bottom
        var inverse = new ResolvedContinuousScale(ContinuousTransformKind.Pow, ChannelKind.Number, -5, 20, new PowScale { Exponent = -2 }, -2, nice: false);
        inverse.Configure(0, 100, 10);
        Assert.True(double.IsNaN(inverse.MapNumber(0)));
        Assert.True(double.IsNaN(inverse.MapNumber(-0.0)));
        Assert.Equal(inverse.MapNumber(-5), inverse.Baseline);
        var ticks = inverse.GetTicks();
        Assert.NotEmpty(ticks);
        Assert.All(ticks, tick => Assert.True(double.IsFinite(tick.Position), tick.Label));
        Assert.DoesNotContain("0", ticks.Select(tick => tick.Label));

        var square = new ResolvedContinuousScale(ContinuousTransformKind.Pow, ChannelKind.Number, 0, 1, new PowScale { Exponent = 2 }, 2, nice: false);
        square.Configure(0, 100, 10);
        Assert.True(double.IsNaN(square.MapNumber(1e200)));

        var plot = new Plot { YScale = new PowScale { Exponent = -2 } };
        plot.Add(new BarMark<(string Label, double Value)>([("c", -5), ("d", 15.5)], row => row.Label, row => row.Value));
        var texts = new List<SceneText>();
        var groups = new Stack<SceneGroup>([plot.CreateScene().Root]);
        while (groups.TryPop(out var group))
        {
            foreach (var child in group.Children)
            {
                if (child is SceneGroup childGroup)
                {
                    groups.Push(childGroup);
                }
                else if (child is SceneText text)
                {
                    texts.Add(text);
                }
            }
        }

        Assert.NotEmpty(texts);
        Assert.All(texts, text => Assert.True(double.IsFinite(text.X) && double.IsFinite(text.Y), text.Text));
    }

    [Fact]
    public void ContinuousScales_FormatTicks()
    {
        var custom = new ResolvedContinuousScale(ContinuousTransformKind.Linear, ChannelKind.Number, 0, 1, new LinearScale { Ticks = [0, 0.25, 2], TickFormat = NumberFormats.Percent() }, 0, nice: false);
        custom.Configure(0, 100, 5);
        Assert.Equal(["0%", "25%"], custom.GetTicks().Select(tick => tick.Label).ToArray());

        var time = new ResolvedContinuousScale(ContinuousTransformKind.Linear, ChannelKind.Time, 0, 86_400_000, new TimeScale { TickFormat = value => "t" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) }, 0, nice: false);
        time.Configure(0, 100, 2);
        Assert.Equal("t0", time.GetTicks()[0].Label);

        var niceLog = new ResolvedContinuousScale(ContinuousTransformKind.Log, ChannelKind.Number, 3, 300, new LogScale(), 10, nice: true);
        niceLog.Configure(0, 100, 10);
        Assert.Equal((1.0, 1000.0), (niceLog.DomainStart, niceLog.DomainEnd));
    }

    [Fact]
    public void ScaleResolver_InfersTheTypeAndTheDomain()
    {
        var numbers = ChannelValues.FromNumbers([2, 8]);
        var resolved = Assert.IsType<ResolvedContinuousScale>(ScaleResolver.ResolvePosition(null, [numbers], band: false, zero: true, exact: false));
        resolved.Configure(0, 100, 10);
        Assert.Equal((0.0, 8.0), (resolved.DomainStart, resolved.DomainEnd));

        Assert.IsType<ResolvedOrdinalScale>(ScaleResolver.ResolvePosition(null, [ChannelValues.FromCategories(["a"])], band: false, zero: false, exact: false));
        Assert.True(Assert.IsType<ResolvedOrdinalScale>(ScaleResolver.ResolvePosition(null, [numbers], band: true, zero: false, exact: false)).IsBand);
        Assert.Equal(ChannelKind.Time, Assert.IsType<ResolvedContinuousScale>(ScaleResolver.ResolvePosition(null, [ChannelValues.FromNumbers([0], ChannelKind.Time)], band: false, zero: false, exact: false)).Kind);
        Assert.Null(ScaleResolver.ResolvePosition(null, [], band: false, zero: false, exact: false));

        // An empty domain, a single value, one explicit bound
        var empty = Assert.IsType<ResolvedContinuousScale>(ScaleResolver.ResolvePosition(new LinearScale(), [], band: false, zero: false, exact: false));
        Assert.Equal((0.0, 1.0), (empty.DomainStart, empty.DomainEnd));
        var emptyLog = Assert.IsType<ResolvedContinuousScale>(ScaleResolver.ResolvePosition(new LogScale(), [], band: false, zero: false, exact: false));
        Assert.Equal((1.0, 10.0), (emptyLog.DomainStart, emptyLog.DomainEnd));
        var one = Assert.IsType<ResolvedContinuousScale>(ScaleResolver.ResolvePosition(new LinearScale { Nice = false }, [ChannelValues.FromNumbers([10])], band: false, zero: false, exact: false));
        Assert.Equal((9.0, 11.0), (one.DomainStart, one.DomainEnd));
        var oneLog = Assert.IsType<ResolvedContinuousScale>(ScaleResolver.ResolvePosition(new LogScale { Nice = false }, [ChannelValues.FromNumbers([10])], band: false, zero: false, exact: false));
        Assert.Equal((1.0, 100.0), (oneLog.DomainStart, oneLog.DomainEnd));
        var minimum = Assert.IsType<ResolvedContinuousScale>(ScaleResolver.ResolvePosition(new LinearScale { Minimum = 10, Nice = false }, [ChannelValues.FromNumbers([10])], band: false, zero: false, exact: false));
        Assert.Equal((10.0, 11.0), (minimum.DomainStart, minimum.DomainEnd));
        var maximum = Assert.IsType<ResolvedContinuousScale>(ScaleResolver.ResolvePosition(new LinearScale { Maximum = 10, Nice = false }, [ChannelValues.FromNumbers([10])], band: false, zero: false, exact: false));
        Assert.Equal((9.0, 10.0), (maximum.DomainStart, maximum.DomainEnd));
        var onlyMaximum = Assert.IsType<ResolvedContinuousScale>(ScaleResolver.ResolvePosition(new LinearScale { Maximum = 5, Nice = false }, [], band: false, zero: false, exact: false));
        Assert.Equal((4.0, 5.0), (onlyMaximum.DomainStart, onlyMaximum.DomainEnd));
        var onlyMinimum = Assert.IsType<ResolvedContinuousScale>(ScaleResolver.ResolvePosition(new LogScale { Minimum = 5, Nice = false }, [], band: false, zero: false, exact: false));
        Assert.Equal((5.0, 50.0), (onlyMinimum.DomainStart, onlyMinimum.DomainEnd));
        var zeroValue = Assert.IsType<ResolvedContinuousScale>(ScaleResolver.ResolvePosition(new LinearScale { Nice = false }, [ChannelValues.FromNumbers([0])], band: false, zero: false, exact: false));
        Assert.Equal((0.0, 1.0), (zeroValue.DomainStart, zeroValue.DomainEnd));
        var timeValue = Assert.IsType<ResolvedContinuousScale>(ScaleResolver.ResolvePosition(new TimeScale(), [ChannelValues.FromNumbers([0], ChannelKind.Time)], band: false, zero: false, exact: false));
        Assert.Equal((-86_400_000.0, 86_400_000.0), (timeValue.DomainStart, timeValue.DomainEnd));
    }

    [Fact]
    public void ScaleResolver_ColorAndSize()
    {
        var theme = ChartTheme.Light;
        var ordinal = Assert.IsType<ResolvedOrdinalColorScale>(ScaleResolver.ResolveColor(null, [ChannelValues.FromCategories(["a", "b", null])], theme));
        Assert.Equal(theme.Palette[1], ordinal.MapCategory("b"));
        Assert.Equal(theme.Palette[0], ordinal.MapCategory("unknown"));
        Assert.Equal(theme.Palette[0], ordinal.Map(ChannelValues.FromCategories([null]), 0));

        var sequential = Assert.IsType<ResolvedContinuousColorScale>(ScaleResolver.ResolveColor(null, [ChannelValues.FromNumbers([0, 10])], theme));
        Assert.Equal(ColorScheme.Viridis.Stops[^1], sequential.MapNumber(10));
        Assert.Equal(theme.Palette[0], sequential.Map(ChannelValues.FromNumbers([double.NaN]), 0));
        Assert.Equal("5", sequential.Format(5, 1));

        var diverging = Assert.IsType<ResolvedContinuousColorScale>(ScaleResolver.ResolveColor(new DivergingColorScale { Midpoint = 0 }, [ChannelValues.FromNumbers([-5, 20])], theme));
        Assert.Equal(0.25, diverging.GetPosition(-2.5));
        Assert.Equal(0.75, diverging.GetPosition(10));
        var flat = new ResolvedContinuousColorScale(ColorScheme.Blues, 0, 0, 0, null, SceneColor.Black);
        Assert.Equal(0.5, flat.GetPosition(0));
        Assert.Equal(0.5, flat.GetPosition(-1));

        var custom = Assert.IsType<ResolvedOrdinalColorScale>(ScaleResolver.ResolveColor(new OrdinalColorScale { Domain = ["x", "y"], Palette = [] }, [], theme));
        Assert.Equal(theme.Palette[0], custom.MapCategory("x"));
        Assert.Null(ScaleResolver.ResolveColor(null, [], theme));

        var size = ScaleResolver.ResolveSize(new SizeScale { MaxRadius = 10 }, [ChannelValues.FromNumbers([25, 100])])!;
        Assert.Equal(5, size.Map(25));
        Assert.Equal(0, size.Map(-1));
        Assert.Null(ScaleResolver.ResolveSize(null, []));
    }

    [Fact]
    public void PointScale_Headroom()
    {
        // 1.5 steps before the first point and 0.25 after the last one: 100 = (2 + 1.5 + 0.25) steps
        var scale = Assert.IsType<ResolvedOrdinalScale>(ScaleResolver.ResolvePosition(null, [ChannelValues.FromCategories(["a", "b", "c"])], band: false, zero: false, exact: false, headroom: 1.5));
        scale.Configure(0, 100, 3);
        Assert.False(scale.IsBand);
        Assert.Equal(100 / 3.75, scale.Step, 9);
        Assert.Equal(40, scale.MapCategory("a"), 9);
        Assert.Equal(40 + (100 / 3.75), scale.MapCategory("b"), 9);
        Assert.Equal(100 - (25 / 3.75), scale.MapCategory("c"), 9);
    }

    [Fact]
    public void SizeLegend_Values()
    {
        // d3's ticks of [0, max] for a count of 3, without zero, the largest first, at most three
        Assert.Equal([200, 100], SizeLegendRenderer.GetValues(260, values: null));
        Assert.Equal([6, 4, 2], SizeLegendRenderer.GetValues(7, values: null));
        Assert.Equal([1, 0.5], SizeLegendRenderer.GetValues(1, values: null));
        Assert.Equal([0.4, 0.2], SizeLegendRenderer.GetValues(0.5, values: null));
        Assert.Empty(SizeLegendRenderer.GetValues(0, values: null));
        Assert.Empty(SizeLegendRenderer.GetValues(double.NaN, values: null));
        Assert.Equal([10, 5], SizeLegendRenderer.GetValues(100, values: [5, -1, 5, 10, double.PositiveInfinity]));
    }

    [Fact]
    public void ScaleProperties_ValidateTheirValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new LogScale { Base = 1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PowScale { Exponent = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SymlogScale { Constant = 0 });
        Assert.Equal(2, new LogScale { Base = 2 }.Base);
        Assert.Equal(3, new PowScale { Exponent = 3 }.Exponent);
        Assert.Equal(5, new SymlogScale { Constant = 5 }.Constant);
    }

    private static double ToMilliseconds(int year, int month, int day) => TimeFormats.ToMilliseconds(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));
}
