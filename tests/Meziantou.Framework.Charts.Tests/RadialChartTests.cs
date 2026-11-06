using System.Globalization;

namespace Meziantou.Framework.Charts.Tests;

public sealed class RadialChartTests
{
    private static readonly Share[] Browsers = [new("Chrome", 64), new("Safari", 19), new("Edge", 5), new("Firefox", 3), new("Other", 9)];

    [Fact]
    public void PieChart()
    {
        #region Charts_PieChart
        Share[] shares = [new("Chrome", 64), new("Safari", 19), new("Edge", 5), new("Firefox", 3), new("Other", 9)];
        var plot = new Plot { Title = "Browser market share", Width = 400, Height = 400 };
        plot.Add(new ArcMark<Share>(shares, value: share => share.Value)
        {
            Fill = share => share.Name,
            Label = share => share.Value.ToString(CultureInfo.InvariantCulture) + "%",
        });
        #endregion

        ChartAssert.Svg(plot, "PieChart");
    }

    [Fact]
    public void DonutChart()
    {
        #region Charts_DonutChart
        var plot = new Plot
        {
            Title = "Browser market share",
            Width = 400,
            Height = 400,
            Coordinates = new PolarCoordinates { InnerRadius = 0.6 },
        };
        plot.Add(new ArcMark<Share>(Browsers, value: share => share.Value) { Fill = share => share.Name, Label = share => share.Name });
        plot.Add(new TextMark<string>(["100%"], text: text => text) { FontSize = 28, Bold = true });
        plot.Legend.Position = LegendPosition.Bottom;
        #endregion

        ChartAssert.Svg(plot, "DonutChart");
    }

    [Fact]
    public void PieWithoutFillUsesThePalette()
    {
        var plot = new Plot { Width = 300, Height = 300 };
        plot.Add(new ArcMark<Share>(Browsers, value: share => share.Value) { Label = share => share.Name, StrokeWidth = 2 });
        plot.Add(new ArcMark<Share>([new("Nothing", 0), new("Negative", -5)], value: share => share.Value));
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void PolarAreaChart()
    {
        #region Charts_PolarAreaChart
        Share[] winds = [new("N", 12), new("NE", 7), new("E", 5), new("SE", 9), new("S", 14), new("SW", 20), new("W", 17), new("NW", 10)];
        var plot = new Plot { Title = "Wind directions", Width = 400, Height = 400 };
        plot.Add(new ArcMark<Share>(winds, value: _ => 1)
        {
            Radius = wind => wind.Value,
            Fill = wind => wind.Value,
            Label = wind => wind.Name,
        });
        plot.ColorScale = new SequentialColorScale { Scheme = ColorScheme.Blues, Minimum = 0 };
        #endregion

        ChartAssert.Svg(plot, "PolarAreaChart");
    }

    [Fact]
    public void RoseWithBars()
    {
        Share[] winds = [new("N", 12), new("E", 5), new("S", 14), new("W", 17)];
        var plot = new Plot { Title = "Rose with bars", Width = 360, Height = 360, Coordinates = new PolarCoordinates() };
        plot.Add(new BarMark<Share>(winds, x: wind => wind.Name, y: wind => wind.Value) { Fill = wind => wind.Name });
        plot.Legend.Visible = false;
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void RadialBarChart()
    {
        #region Charts_RadialBarChart
        var plot = new Plot
        {
            Title = "Sales by region",
            Width = 420,
            Height = 420,
            Coordinates = new PolarCoordinates { Transposed = true, EndAngle = 270, InnerRadius = 0.25 },
        };
        plot.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2024), x: sale => sale.Region, y: sale => sale.Amount)
        {
            Fill = sale => sale.Region,
        });
        plot.Legend.Visible = false;
        #endregion

        ChartAssert.Svg(plot, "RadialBarChart");
    }

    [Fact]
    public void RadarChart()
    {
        #region Charts_RadarChart
        var plot = new Plot
        {
            Title = "Player skills",
            Width = 440,
            Height = 440,
            Coordinates = new PolarCoordinates(),
            XScale = new PointScale(),
            YScale = new LinearScale { Minimum = 0, Maximum = 100 },
        };
        plot.Add(new AreaMark<Score>(SampleData.Scores, x: score => score.Skill, y: score => score.Value)
        {
            Fill = score => score.Player,
            FillOpacity = 0.25,
            Closed = true,
        });
        plot.Add(new LineMark<Score>(SampleData.Scores, x: score => score.Skill, y: score => score.Value)
        {
            Stroke = score => score.Player,
            Closed = true,
            MarkerRadius = 3,
        });
        #endregion

        ChartAssert.Svg(plot, "RadarChart");
    }

    [Fact]
    public void RadarWithCircleGridAndCurves()
    {
        var plot = new Plot { Width = 360, Height = 360, Coordinates = new PolarCoordinates { GridShape = PolarGridShape.Circle }, XScale = new PointScale() };
        plot.Add(new AreaMark<Score>(SampleData.Scores, x: score => score.Skill, y: score => score.Value) { Fill = score => score.Player, FillOpacity = 0.3, Closed = true, Y1 = _ => 20, Curve = Curve.CatmullRom });
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void PolarLineWithContinuousAngle()
    {
        var plot = new Plot { Width = 360, Height = 360, Coordinates = new PolarCoordinates { GridShape = PolarGridShape.Polygon } };
        plot.Add(new LineMark<int>(Enumerable.Range(0, 73), x: i => i * 5.0, y: i => 1 + Math.Sin(i * 5 * Math.PI / 60)));
        plot.Add(new RuleYMark(1) { StrokeDashArray = [3, 3] });
        plot.Add(new RuleXMark(90, 270));
        plot.XScale = new LinearScale { Minimum = 0, Maximum = 360, Nice = false };
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void Gauge()
    {
        #region Charts_Gauge
        const double Value = 72;
        var plot = new Plot
        {
            Title = "CPU usage",
            Width = 360,
            Height = 240,
            Coordinates = new PolarCoordinates { StartAngle = -90, EndAngle = 90, InnerRadius = 0.7 },
        };
        plot.Add(new ArcMark<Share>([new("Used", Value), new("Free", 100 - Value)], value: part => part.Value)
        {
            Fill = part => part.Name,
        });
        plot.Add(new RuleXMark(Value / 100) { Y1 = _ => 0, Y2 = _ => 1, StrokeWidth = 3 }); // The slices span 0 to 1
        plot.Add(new TextMark<double>([Value], text: value => value.ToString(CultureInfo.InvariantCulture) + "%") { FontSize = 32, Bold = true, OffsetY = -24 });
        plot.ColorScale = new OrdinalColorScale { Palette = ["#4269d0", "#e3e6ea"] };
        plot.Legend.Visible = false;
        #endregion

        ChartAssert.Svg(plot, "Gauge");
    }

    [Fact]
    public void FacetedPies()
    {
        #region Charts_FacetedPies
        var plot = new Plot
        {
            Title = "Share of each region, by year",
            Height = 260,
            Coordinates = new PolarCoordinates { InnerRadius = 0.5 },
        };
        plot.Add(new ArcMark<Sale>(SampleData.Sales, value: sale => sale.Amount)
        {
            FacetX = sale => sale.Year,
            Fill = sale => sale.Region,
        });
        #endregion

        ChartAssert.Svg(plot, "FacetedPies");
    }

    [Fact]
    public void ArcMarkInCartesianCoordinates()
    {
        var plot = new Plot { Height = 120, Coordinates = new CartesianCoordinates() };
        plot.Add(new ArcMark<Share>(Browsers, value: share => share.Value) { Fill = share => share.Name, Label = share => share.Name });
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void ReversedAngles()
    {
        var plot = new Plot { Width = 300, Height = 300, Coordinates = new PolarCoordinates { StartAngle = 180, EndAngle = 0 } };
        plot.Add(new ArcMark<Share>(Browsers, value: share => share.Value) { Fill = share => share.Name });
        plot.Legend.Visible = false;
        ChartAssert.Svg(plot);
    }
}
