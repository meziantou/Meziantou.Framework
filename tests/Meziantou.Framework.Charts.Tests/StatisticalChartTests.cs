using System.Globalization;
using Meziantou.Framework.Geo.Maps;

namespace Meziantou.Framework.Charts.Tests;

public sealed class StatisticalChartTests
{
    [Fact]
    public void Histogram()
    {
        #region Charts_Histogram
        var values = SampleData.Normal(500, mean: 170, deviation: 8, seed: 42);
        var plot = new Plot { Title = "Height of 500 people" };
        plot.Add(new RectMark<double>(values) { X = height => height, Transform = new BinTransform() });
        plot.XAxis.Label = "Height (cm)";
        plot.YAxis.Label = "People";
        #endregion

        ChartAssert.Svg(plot, "Histogram");
    }

    [Theory]
    [InlineData(BinThresholds.Scott)]
    [InlineData(BinThresholds.FreedmanDiaconis)]
    public void HistogramThresholds(BinThresholds thresholds)
    {
        var plot = new Plot { Width = 400, Height = 250 };
        plot.Add(new RectMark<Measurement>(SampleData.Measurements) { X = measurement => measurement.Value, Fill = measurement => measurement.Group, Opacity = 0.6, Transform = new BinTransform { Thresholds = thresholds } });
        ChartAssert.Svg(plot, documentationImage: null, memberName: nameof(HistogramThresholds) + "_" + thresholds);
    }

    [Fact]
    public void HistogramWithExplicitEdges()
    {
        var plot = new Plot { Width = 400, Height = 250 };
        plot.Add(new RectMark<Measurement>(SampleData.Measurements) { X = measurement => measurement.Value, Transform = new BinTransform { Edges = [0, 25, 50, 75, 100] }, Inset = 0, StrokeColor = SceneColor.White });
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void BoxPlot()
    {
        #region Charts_BoxPlot
        var plot = new Plot { Title = "Distribution by group" };
        plot.Add(new BoxMark<Measurement>(SampleData.Measurements, x: measurement => measurement.Group, y: measurement => measurement.Value)
        {
            Fill = measurement => measurement.Group,
        });
        plot.Legend.Visible = false;
        #endregion

        ChartAssert.Svg(plot, "BoxPlot");
    }

    [Fact]
    public void HorizontalBoxPlot()
    {
        var plot = new Plot { Height = 260, Coordinates = new CartesianCoordinates { Transposed = true } };
        plot.Add(new BoxMark<Measurement>(SampleData.Measurements, x: measurement => measurement.Group, y: measurement => measurement.Value) { BoxWidth = 0.4, StrokeColor = SceneColor.Black });
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void Heatmap()
    {
        #region Charts_Heatmap
        string[] days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        var cells = days.SelectMany((day, d) => Enumerable.Range(8, 12).Select(hour => new HeatCell(
            day,
            hour.ToString("00", CultureInfo.InvariantCulture) + "h",
            Math.Round((d < 5 ? 40 : 15) + (30 * Math.Sin((hour - 8) * Math.PI / 11)), MidpointRounding.AwayFromZero))));

        var plot = new Plot { Title = "Visits by hour", Height = 320 };
        plot.Add(new RectMark<HeatCell>(cells)
        {
            X = cell => cell.Hour,
            Y = cell => cell.Day,
            Fill = cell => cell.Value,
            Text = cell => cell.Value.ToString(CultureInfo.InvariantCulture),
        });
        plot.ColorScale = new SequentialColorScale { Scheme = ColorScheme.Blues };
        plot.Legend.Title = "Visits";
        #endregion

        ChartAssert.Svg(plot, "Heatmap");
    }

    [Fact]
    public void DivergingHeatmap()
    {
        var cells = Enumerable.Range(0, 5).SelectMany(row => Enumerable.Range(0, 5).Select(column => new HeatCell("r" + row, "c" + column, (row - column) * 10)));
        var plot = new Plot { Width = 360, Height = 300 };
        plot.Add(new RectMark<HeatCell>(cells) { X = cell => cell.Hour, Y = cell => cell.Day, Fill = cell => cell.Value, Inset = 2, CornerRadius = 3 });
        plot.ColorScale = new DivergingColorScale { TickFormat = NumberFormats.Si };
        plot.Legend.Position = LegendPosition.Right;
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void Waterfall()
    {
        #region Charts_Waterfall
        Step[] steps =
        [
            new("Revenue", 420), new("Cost of sales", -180), new("Gross profit", 0, IsTotal: true),
            new("Marketing", -60), new("Salaries", -90), new("Other income", 25), new("Net profit", 0, IsTotal: true),
        ];

        var plot = new Plot { Title = "From revenue to net profit" };
        plot.Add(new WaterfallMark<Step>(steps, x: step => step.Label, y: step => step.Change) { IsTotal = step => step.IsTotal });
        #endregion

        ChartAssert.Svg(plot, "Waterfall");
    }

    [Fact]
    public void Funnel()
    {
        #region Charts_Funnel
        Share[] stages = [new("Visits", 12000), new("Sign-ups", 4200), new("Trials", 1800), new("Purchases", 650)];
        var plot = new Plot { Title = "Conversion funnel", Height = 300 };
        plot.Add(new FunnelMark<Share>(stages, stage: stage => stage.Name, value: stage => stage.Value));
        #endregion

        ChartAssert.Svg(plot, "Funnel");
    }

    [Fact]
    public void FunnelWithColorsAndLabels()
    {
        Share[] stages = [new("Visits", 12000), new("Sign-ups", 4200), new("Purchases", 650)];
        var plot = new Plot { Height = 240 };
        plot.Add(new FunnelMark<Share>(stages, stage: stage => stage.Name, value: stage => stage.Value) { Fill = stage => stage.Name, Label = stage => NumberFormats.Si(stage.Value) });
        plot.Legend.Visible = false;
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void Candlestick()
    {
        #region Charts_Candlestick
        var plot = new Plot { Title = "Daily prices" };
        plot.Add(new CandlestickMark<Candle>(
            SampleData.Candles,
            x: candle => candle.Date,
            open: candle => candle.Open,
            high: candle => candle.High,
            low: candle => candle.Low,
            close: candle => candle.Close));
        #endregion

        ChartAssert.Svg(plot, "Candlestick");
    }

    [Fact]
    public void CandlestickWithCategories()
    {
        var plot = new Plot { Height = 250 };
        plot.Add(new CandlestickMark<Candle>(SampleData.Candles.Take(5), x: candle => candle.Date.ToString("MMM d", CultureInfo.InvariantCulture), open: candle => candle.Open, high: candle => candle.High, low: candle => candle.Low, close: candle => candle.Close) { CandleWidth = 0.4 });
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void Sparkline()
    {
        #region Charts_Sparkline
        double[] values = [3, 5, 4, 6, 8, 7, 9, 12, 10, 11, 14, 13];
        var plot = new Plot { Width = 120, Height = 32, Margin = new ChartMargin(4), Theme = ChartTheme.Light with { Background = null } };
        plot.Add(new LineMark<int>(Enumerable.Range(0, values.Length), x: i => i, y: i => values[i]) { Curve = Curve.MonotoneX });
        plot.Add(new DotMark<int>([values.Length - 1], x: i => i, y: i => values[i]) { Radius = 2, FillColor = "#e5484d" });
        plot.XAxis.Visible = false;
        plot.YAxis.Visible = false;
        #endregion

        ChartAssert.Svg(plot, "Sparkline");
    }

    [Fact]
    public void BulletChart()
    {
        #region Charts_BulletChart
        Kpi[] kpis = [new("Revenue", 82, 90), new("Profit", 64, 60), new("New customers", 91, 85), new("Satisfaction", 48, 75)];
        var plot = new Plot
        {
            Title = "Results, in % of the year's goal",
            Height = 240,
            Coordinates = new CartesianCoordinates { Transposed = true },
            YScale = new LinearScale { Minimum = 0, Maximum = 100 },
        };

        // The qualitative ranges, the value as a narrower bar, and the target as a line across the band
        plot.Add(new BarMark<Kpi>(kpis, x: kpi => kpi.Name, y: _ => 100) { FillColor = "#eceef1" });
        plot.Add(new BarMark<Kpi>(kpis, x: kpi => kpi.Name, y: _ => 70) { FillColor = "#d5d9de" });
        plot.Add(new BarMark<Kpi>(kpis, x: kpi => kpi.Name, y: kpi => kpi.Value) { BarWidth = 0.35, FillColor = "#2f3740" });
        plot.Add(new RuleYMark<Kpi>(kpis, y: kpi => kpi.Target) { X = kpi => kpi.Name, StrokeWidth = 3, StrokeColor = "#e5484d" });
        #endregion

        ChartAssert.Svg(plot, "BulletChart");
    }

    [Fact]
    public void CalendarHeatmap()
    {
        #region Charts_CalendarHeatmap
        string[] dayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        var start = new DateOnly(2024, 1, 1); // a Monday
        var days = Enumerable.Range(0, 91).Select(start.AddDays).ToArray();

        var plot = new Plot { Title = "Commits per day", Height = 220 };
        plot.Add(new RectMark<DateOnly>(days)
        {
            X = day => "W" + ((day.DayNumber - start.DayNumber) / 7 + 1).ToString(CultureInfo.InvariantCulture),
            Y = day => dayNames[((int)day.DayOfWeek + 6) % 7],
            Fill = day => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? day.Day % 3 : (day.DayNumber * 7 % 11) + 2,
            Inset = 2,
            CornerRadius = 2,
            Title = day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        });
        plot.YScale = new BandScale { Domain = dayNames };
        plot.ColorScale = new SequentialColorScale { Scheme = ColorScheme.Greens };
        #endregion

        ChartAssert.Svg(plot, "CalendarHeatmap");
    }

    [Fact]
    public void MarimekkoChart()
    {
        #region Charts_MarimekkoChart
        // The width of a region is its share of the total; its height is split by year
        var totals = SampleData.Sales.GroupBy(sale => sale.Region).ToDictionary(group => group.Key, group => group.Sum(sale => sale.Amount));
        var grandTotal = totals.Values.Sum();
        var starts = new Dictionary<string, double>();
        var cursor = 0.0;
        foreach (var (region, total) in totals)
        {
            starts[region] = cursor;
            cursor += total / grandTotal;
        }

        var plot = new Plot
        {
            Title = "Sales by region and year",
            XScale = new LinearScale { TickFormat = NumberFormats.Percent() },
            YScale = new LinearScale { TickFormat = NumberFormats.Percent() },
        };
        plot.Add(new RectMark<Sale>(SampleData.Sales)
        {
            X1 = sale => starts[sale.Region],
            X2 = sale => starts[sale.Region] + (totals[sale.Region] / grandTotal),
            Y = sale => sale.Amount,
            Fill = sale => sale.Year.ToString(CultureInfo.InvariantCulture),
            Transform = new StackTransform { Offset = StackOffset.Expand },
            Text = sale => sale.Region,
        });
        #endregion

        ChartAssert.Svg(plot, "MarimekkoChart");
    }

    [Fact]
    public void DensityPlot()
    {
        #region Charts_DensityPlot
        var plot = new Plot { Title = "Distribution by group" };
        plot.Add(new AreaMark<Measurement>(SampleData.Measurements, x: measurement => measurement.Value, y: _ => null)
        {
            Fill = measurement => measurement.Group,
            FillOpacity = 0.35,
            Curve = Curve.MonotoneX,
            Transform = new DensityTransform(),
        });
        plot.YAxis.Label = "Density";
        #endregion

        ChartAssert.Svg(plot, "DensityPlot");
    }

    [Fact]
    public void ViolinPlot()
    {
        #region Charts_ViolinPlot
        var plot = new Plot { Title = "Distribution by group" };
        plot.Add(new ViolinMark<Measurement>(SampleData.Measurements, x: measurement => measurement.Group, y: measurement => measurement.Value)
        {
            Fill = measurement => measurement.Group,
        });
        plot.Legend.Visible = false;
        #endregion

        ChartAssert.Svg(plot, "ViolinPlot");
    }

    [Fact]
    public void ViolinOptions()
    {
        var measurements = SampleData.Measurements.Append(new Measurement("D", 50)).ToArray();
        var plot = new Plot { Height = 260, Coordinates = new CartesianCoordinates { Transposed = true } };
        plot.Add(new ViolinMark<Measurement>(measurements, x: measurement => measurement.Group, y: measurement => measurement.Value) { ShowBox = false, Bandwidth = 4, Samples = 20, ViolinWidth = 0.6, StrokeColor = SceneColor.Black });
        ChartAssert.Svg(plot);

        var lines = new Plot { Height = 220 };
        lines.Add(new LineMark<Measurement>(measurements, x: measurement => measurement.Value, y: _ => null) { Transform = new DensityTransform { Bandwidth = 5, Samples = 40, Cut = 0 }, Stroke = measurement => measurement.Group });
        ChartAssert.Svg(lines, documentationImage: null, memberName: nameof(ViolinOptions) + "_DensityLines");
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new DensityTransform { Samples = 1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new ViolinMark<int>([], x: i => i, y: i => i) { Samples = 1 });
    }

    [Fact]
    public void Ridgeline()
    {
        #region Charts_Ridgeline
        string[] months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
        var days = months
            .SelectMany((month, index) => SampleData.Normal(90, mean: 12 - (9 * Math.Cos(index * Math.PI / 6)), deviation: 2.5 + (index % 3), seed: index)
                .Select(temperature => (Month: month, Temperature: temperature)))
            .ToArray();

        var plot = new Plot { Title = "Daily temperatures by month", Height = 480 };
        plot.Add(new RidgelineMark<(string Month, double Temperature)>(days, x: day => day.Temperature, y: day => day.Month) { FillOpacity = 0.85 });
        plot.XAxis.Label = "Temperature (°C)";
        #endregion

        ChartAssert.Svg(plot, "Ridgeline");
    }

    [Fact]
    public void RidgelineOptions()
    {
        var measurements = SampleData.Measurements.Append(new Measurement("D", double.NaN)).Append(new Measurement(null!, 3)).ToArray();
        var plot = new Plot { Height = 260, Width = 400 };
        plot.Add(new RidgelineMark<Measurement>(measurements, x: measurement => measurement.Value, y: measurement => measurement.Group)
        {
            Bandwidth = 4,
            Samples = 30,
            Cut = 1,
            Overlap = 0.8,
            Curve = Curve.Linear,
            StrokeColor = SceneColor.Black,
            StrokeWidth = 0.5,
            Title = measurement => measurement.Group,
        });
        ChartAssert.Svg(plot);

        // A y scale that is not ordinal draws nothing
        var linear = new Plot { YScale = new LinearScale() };
        linear.Add(new RidgelineMark<Measurement>(SampleData.Measurements, x: measurement => measurement.Value, y: measurement => measurement.Group));
        Assert.DoesNotContain("<path", linear.ToSvg());

        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RidgelineMark<int>([], x: i => i, y: i => i) { Samples = 1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RidgelineMark<int>([], x: i => i, y: i => i) { Overlap = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RidgelineMark<int>([], x: i => i, y: i => i) { Overlap = double.PositiveInfinity });
    }

    [Fact]
    public void Hexbin()
    {
        #region Charts_Hexbin
        var xs = SampleData.Normal(2000, mean: 50, deviation: 12, seed: 1);
        var ys = SampleData.Normal(2000, mean: 30, deviation: 8, seed: 2);
        var points = xs.Zip(ys, (x, y) => (X: x + (y / 2), Y: y)).ToArray();

        var plot = new Plot { Title = "2,000 points in hexagonal bins" };
        plot.Add(new HexbinMark<(double X, double Y)>(points, x: point => point.X, y: point => point.Y) { Radius = 8 });
        plot.Legend.Title = "Points";
        #endregion

        ChartAssert.Svg(plot, "Hexbin");
    }

    [Fact]
    public void HexbinOptions()
    {
        // In a facet grid, the hexagons of every chart extend the color scale; a scale that sets its domain keeps it
        var points = SampleData.Normal(300, mean: 0, deviation: 1, seed: 7).Select((value, index) => (X: value, Y: index % 10, Group: index % 2 is 0 ? "Even" : "Odd")).ToArray();
        var plot = new Plot { Width = 400, Height = 220 };
        plot.Add(new HexbinMark<(double X, int Y, string Group)>(points, x: point => point.X, y: point => point.Y)
        {
            Radius = 12,
            FacetX = point => point.Group,
            StrokeColor = SceneColor.Black,
            StrokeWidth = 1,
            Title = point => point.Group,
        });
        ChartAssert.Svg(plot);

        var fixedScale = new Plot { ColorScale = new SequentialColorScale { Minimum = 0, Maximum = 100 } };
        fixedScale.Add(new HexbinMark<(double X, int Y, string Group)>(points, x: point => point.X, y: point => point.Y));
        Assert.Contains(">100</text>", fixedScale.ToSvg());

        // Without a continuous color scale, the hexagons have the fill color
        var ordinal = new Plot { ColorScale = new OrdinalColorScale() };
        ordinal.Add(new HexbinMark<(double X, int Y, string Group)>(points, x: point => point.X, y: point => point.Y) { FillColor = "#123456" });
        Assert.Contains("#123456", ordinal.ToSvg());
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new HexbinMark<int>([], x: i => i, y: i => i) { Radius = 0 });
    }

    [Fact]
    public void Contour()
    {
        #region Charts_Contour
        var first = SampleData.Normal(300, mean: 2, deviation: 0.8, seed: 3).Zip(SampleData.Normal(300, mean: 3, deviation: 0.6, seed: 4));
        var second = SampleData.Normal(200, mean: 5, deviation: 1, seed: 5).Zip(SampleData.Normal(200, mean: 6, deviation: 0.9, seed: 6));
        var points = first.Concat(second).Select(point => (X: point.First, Y: point.Second)).ToArray();

        var plot = new Plot { Title = "Density of two clusters", ColorScale = new SequentialColorScale { Scheme = ColorScheme.Blues } };
        plot.Add(new ContourMark<(double X, double Y)>(points, x: point => point.X, y: point => point.Y));
        plot.Add(new DotMark<(double X, double Y)>(points, x: point => point.X, y: point => point.Y) { Radius = 1.2, FillColor = "#1f2328", FillOpacity = 0.5 });
        plot.Legend.Title = "Density";
        #endregion

        ChartAssert.Svg(plot, "Contour");
    }

    [Fact]
    public void ContourOptions()
    {
        var points = SampleData.Normal(100, mean: 0, deviation: 1, seed: 8).Zip(SampleData.Normal(100, mean: 0, deviation: 2, seed: 9)).Append((double.NaN, 1)).ToArray();
        var plot = new Plot { Width = 300, Height = 300 };
        plot.Add(new ContourMark<(double First, double Second)>(points, x: point => point.First, y: point => point.Second)
        {
            Filled = false,
            BandwidthX = 0.5,
            BandwidthY = 1,
            GridSize = 32,
            Thresholds = 4,
            StrokeWidth = 2,
            Title = _ => "contour",
        });
        ChartAssert.Svg(plot);

        var stroked = new Plot { Width = 300, Height = 300 };
        stroked.Add(new ContourMark<(double First, double Second)>(points, x: point => point.First, y: point => point.Second) { StrokeColor = SceneColor.White, Thresholds = 3 });
        Assert.Contains("stroke=\"#ffffff\"", stroked.ToSvg());

        // Without points, or with categories, there is nothing to draw
        var empty = new Plot();
        empty.Add(new ContourMark<(double, double)>([(double.NaN, 0)], x: point => point.Item1, y: point => point.Item2));
        empty.Add(new ContourMark<string>(["a"], x: value => value, y: value => value));
        Assert.DoesNotContain("<path", empty.ToSvg());
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new ContourMark<int>([], x: i => i, y: i => i) { GridSize = 1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new ContourMark<int>([], x: i => i, y: i => i) { Thresholds = 0 });
    }

    [Fact]
    public void ParallelCoordinates()
    {
        #region Charts_ParallelCoordinates
        var plot = new Plot { Title = "Cars on four dimensions" };
        plot.Add(new ParallelCoordinatesMark<Car>(SampleData.Cars,
        [
            new("Weight (kg)", car => car.Weight),
            new("Horsepower", car => car.Horsepower),
            new("MPG", car => car.Mpg),
            new("Origin", car => car.Origin),
        ])
        {
            Stroke = car => car.Origin,
        });
        #endregion

        ChartAssert.Svg(plot, "ParallelCoordinates");
    }

    [Fact]
    public void ParallelCoordinatesOptions()
    {
        // A scale per axis: a reversed axis and a log axis; a missing value breaks the line
        Car[] cars = [.. SampleData.Cars.Take(4), new("Gap", double.NaN, 30, 120, "Europe")];
        var plot = new Plot { Width = 360, Height = 240 };
        plot.Add(new ParallelCoordinatesMark<Car>(cars,
        [
            new("Weight", car => car.Weight),
            new("MPG", car => car.Mpg) { Scale = new LinearScale { Reverse = true } },
            new("Power", car => car.Horsepower) { Scale = new LogScale() },
        ])
        {
            LineOpacity = 1,
            StrokeColor = "#e5484d",
            StrokeWidth = 1,
            Title = car => car.Name,
        });
        ChartAssert.Svg(plot);

        var single = new Plot { Width = 200, Height = 200 };
        single.Add(new ParallelCoordinatesMark<Car>(cars, [new("Weight", car => car.Weight)]));
        Assert.Contains("Weight", single.ToSvg());
        Assert.Throws<ArgumentException>("dimensions", () => new ParallelCoordinatesMark<Car>(cars, []));
        Assert.Throws<ArgumentNullException>("name", () => new ParallelDimension<Car>(null!, car => car.Weight));
    }

    [Fact]
    public void TrendLine()
    {
        #region Charts_TrendLine
        var plot = new Plot { Title = "Fuel efficiency by weight" };
        plot.Add(new DotMark<Car>(SampleData.Cars, x: car => car.Weight, y: car => car.Mpg) { FillOpacity = 0.7 });
        plot.Add(new LineMark<Car>(SampleData.Cars, x: car => car.Weight, y: car => car.Mpg)
        {
            Transform = new RegressionTransform(),
            StrokeColor = "#e5484d",
            StrokeWidth = 2,
        });
        plot.Add(new LineMark<Car>(SampleData.Cars, x: car => car.Weight, y: car => car.Mpg)
        {
            Transform = new RegressionTransform { Degree = 2 },
            StrokeColor = "#8c959f",
            StrokeDashArray = [4, 4],
        });
        #endregion

        ChartAssert.Svg(plot, "TrendLine");
    }

    [Fact]
    public void MovingAverage()
    {
        #region Charts_MovingAverage
        var start = new DateOnly(2024, 1, 1);
        var visits = Enumerable.Range(0, 90).Select(day => (Date: start.AddDays(day), Count: 200 + day + (60 * Math.Sin(day / 8.0)) + (day % 7 is 5 or 6 ? -70 : 15 * Math.Cos(day * 2.3)))).ToArray();

        var plot = new Plot { Title = "Daily visits and their 7-day average", Height = 300 };
        plot.Add(new LineMark<(DateOnly Date, double Count)>(visits, x: day => day.Date, y: day => day.Count) { StrokeColor = "#c3cbd5", StrokeWidth = 1 });
        plot.Add(new LineMark<(DateOnly Date, double Count)>(visits, x: day => day.Date, y: day => day.Count)
        {
            Transform = new WindowTransform { Size = 7, Anchor = WindowAnchor.End },
            StrokeWidth = 2.5,
        });
        #endregion

        ChartAssert.Svg(plot, "MovingAverage");
    }

    [Fact]
    public void Sankey()
    {
        #region Charts_Sankey
        Flow[] flows =
        [
            new("Coal", "Electricity", 25), new("Gas", "Electricity", 30), new("Nuclear", "Electricity", 20), new("Solar", "Electricity", 10),
            new("Gas", "Heating", 35), new("Oil", "Transport", 45),
            new("Electricity", "Homes", 30), new("Electricity", "Industry", 25), new("Electricity", "Losses", 30),
            new("Heating", "Homes", 25), new("Heating", "Losses", 10), new("Transport", "Losses", 30), new("Transport", "Mobility", 15),
        ];

        var plot = new Plot { Title = "Energy flows", Height = 420 };
        plot.Add(new SankeyMark<Flow>(flows, source: flow => flow.Source, target: flow => flow.Target, value: flow => flow.Value));
        #endregion

        ChartAssert.Svg(plot, "Sankey");
    }

    [Theory]
    [InlineData(SankeyAlignment.Left)]
    [InlineData(SankeyAlignment.Right)]
    [InlineData(SankeyAlignment.Center)]
    public void SankeyAlignments(SankeyAlignment alignment)
    {
        Flow[] flows = [new("A", "B", 10), new("B", "C", 6), new("B", "D", 4), new("E", "C", 3), new("F", "D", double.NaN)];
        var plot = new Plot { Width = 400, Height = 240 };
        plot.Add(new SankeyMark<Flow>(flows, source: flow => flow.Source, target: flow => flow.Target, value: flow => flow.Value)
        {
            Alignment = alignment,
            NodeWidth = 10,
            NodePadding = 20,
            LinkOpacity = 0.6,
            ShowLabels = alignment is not SankeyAlignment.Center,
            Fill = flow => flow.Target,
            Title = flow => flow.Source + flow.Target,
        });
        ChartAssert.Svg(plot, documentationImage: null, memberName: nameof(SankeyAlignments) + "_" + alignment);
    }

    [Fact]
    public void Chord()
    {
        #region Charts_Chord
        Flow[] commutes =
        [
            new("Downtown", "Harbor", 120), new("Harbor", "Downtown", 90), new("Downtown", "Hills", 40), new("Hills", "Downtown", 150),
            new("Downtown", "University", 70), new("University", "Downtown", 60), new("Harbor", "Hills", 30), new("Hills", "Harbor", 45),
            new("University", "Hills", 25), new("Hills", "University", 80), new("Airport", "Downtown", 55), new("Downtown", "Airport", 35),
            new("Airport", "Harbor", 20), new("University", "University", 30),
        ];

        var plot = new Plot { Title = "Daily commutes between districts", Height = 460 };
        plot.Add(new ChordMark<Flow>(commutes, source: flow => flow.Source, target: flow => flow.Target, value: flow => flow.Value));
        #endregion

        ChartAssert.Svg(plot, "Chord");
    }

    [Fact]
    public void ChordOptions()
    {
        Flow[] flows = [new("A", "B", 10), new("B", "A", 4), new("A", "B", 2), new("B", "C", 6), new("C", "C", double.NaN), new("C", "A", -3), new(null!, "A", 1)];
        var plot = new Plot { Width = 300, Height = 300 };
        plot.Add(new ChordMark<Flow>(flows, source: flow => flow.Source, target: flow => flow.Target, value: flow => flow.Value)
        {
            ArcWidth = 20,
            NodePadding = 0,
            LinkOpacity = 1,
            ShowLabels = false,
            Fill = flow => flow.Target,
            Title = flow => flow.Source + flow.Target,
        });
        ChartAssert.Svg(plot);

        var empty = new Plot();
        empty.Add(new ChordMark<Flow>([], source: flow => flow.Source, target: flow => flow.Target, value: flow => flow.Value));
        Assert.DoesNotContain("<path", empty.ToSvg());
    }

    [Fact]
    public void Network()
    {
        #region Charts_Network
        (string From, string To)[] collaborations =
        [
            ("Alice", "Bob"), ("Alice", "Carol"), ("Bob", "Carol"), ("Carol", "Dave"), ("Alice", "Dave"),
            ("Dave", "Erin"), ("Erin", "Frank"), ("Frank", "Grace"), ("Erin", "Grace"), ("Grace", "Heidi"),
            ("Heidi", "Ivan"), ("Ivan", "Judy"), ("Heidi", "Judy"), ("Judy", "Mallory"), ("Ivan", "Mallory"),
            ("Bob", "Niaj"), ("Niaj", "Olivia"), ("Carol", "Olivia"), ("Frank", "Peggy"), ("Mallory", "Trent"),
        ];
        var teams = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Alice"] = "Design", ["Bob"] = "Design", ["Carol"] = "Design", ["Niaj"] = "Design", ["Olivia"] = "Design",
            ["Dave"] = "Product", ["Erin"] = "Product", ["Frank"] = "Product", ["Grace"] = "Product", ["Peggy"] = "Product",
            ["Heidi"] = "Engineering", ["Ivan"] = "Engineering", ["Judy"] = "Engineering", ["Mallory"] = "Engineering", ["Trent"] = "Engineering",
        };

        var plot = new Plot { Title = "Who works with whom", Height = 420 };
        plot.Add(new NetworkMark<(string From, string To)>(collaborations, source: link => link.From, target: link => link.To)
        {
            NodeFill = name => teams[name],
            NodeRadius = 7,
        });
        #endregion

        ChartAssert.Svg(plot, "Network");
    }

    [Fact]
    public void NetworkOptions()
    {
        (string?, string?)[] links = [("a", "b"), ("b", "c"), ("c", "a"), ("c", "d"), (null, "a"), ("d", "d")];
        var plot = new Plot { Width = 300, Height = 240 };
        plot.Add(new NetworkMark<(string?, string?)>(links, source: link => link.Item1, target: link => link.Item2)
        {
            LinkDistance = 60,
            Charge = -100,
            Iterations = 50,
            LinkOpacity = 1,
            ShowLabels = false,
            FillColor = "#e5484d",
            Stroke = link => link.Item1,
            StrokeWidth = 3,
            Title = link => link.Item1 + link.Item2,
        });
        ChartAssert.Svg(plot);

        // A single node, or no node at all
        var single = new Plot { Width = 200, Height = 200 };
        single.Add(new NetworkMark<(string, string)>([("x", "x")], source: link => link.Item1, target: link => link.Item2) { Iterations = 0 });
        Assert.Contains("<circle", single.ToSvg());
        var empty = new Plot();
        empty.Add(new NetworkMark<(string, string)>([], source: link => link.Item1, target: link => link.Item2));
        Assert.DoesNotContain("<circle", empty.ToSvg());
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new NetworkMark<int>([], source: i => i, target: i => i) { Iterations = -1 });
    }

    [Fact]
    public void Sankey_DataAttributesOnTheLinks()
    {
        Flow[] flows = [new("A", "B", 2), new("A", "C", 1), new("B", "C", 1)];
        var plot = new Plot();
        plot.Add(new SankeyMark<Flow>(flows, source: flow => flow.Source, target: flow => flow.Target, value: flow => flow.Value)
        {
            DataAttributes = flow => [new("source", flow.Source), new("target", flow.Target)],
        });

        var items = ChartAssert.DataAttributes(plot.ToSvg());
        Assert.All(items, item => item.Element is "path");
        Assert.Equal(["source=A;target=B", "source=A;target=C", "source=B;target=C"], items.Select(item => item.Attributes).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void StatisticalMarks_LeaveOutValuesWithoutPosition()
    {
        // Infinite values, and values out of the domain of a log scale, have no position
        (string Group, double Value)[] rows = [("a", 1), ("a", 2), ("a", 4), ("a", double.PositiveInfinity), ("b", double.NegativeInfinity), ("c", -1), ("c", 3), ("c", 5)];
        Mark[] marks =
        [
            new BoxMark<(string Group, double Value)>(rows, row => row.Group, row => row.Value),
            new ViolinMark<(string Group, double Value)>(rows, row => row.Group, row => row.Value),
            new RidgelineMark<(string Group, double Value)>(rows, row => row.Value, row => row.Group),
            new ContourMark<(string Group, double Value)>(rows, row => row.Value, row => row.Value * 2),
            new ArcMark<(string Group, double Value)>(rows, row => row.Value) { Label = row => row.Group },
        ];

        foreach (var mark in marks)
        {
            AssertFiniteScene(new Plot().Add(mark));
            AssertFiniteScene(new Plot { XScale = new LogScale(), YScale = new LogScale() }.Add(mark));
        }
    }

    [Fact]
    public void StatisticalMarks_FacetWithoutNumbers()
    {
        // The values of the facet "f2" are not numbers, so its rows are not summarized
        (string Facet, string Group, object Value)[] rows = [("f1", "a", 1), ("f1", "a", 3), ("f1", "b", 2), ("f2", "a", "x"), ("f2", "b", "y")];
        AssertFiniteScene(new Plot().Add(new BoxMark<(string Facet, string Group, object Value)>(rows, row => row.Group, row => row.Value) { FacetX = row => row.Facet }));
        AssertFiniteScene(new Plot().Add(new ViolinMark<(string Facet, string Group, object Value)>(rows, row => row.Group, row => row.Value) { FacetX = row => row.Facet }));
        AssertFiniteScene(new Plot().Add(new RidgelineMark<(string Facet, string Group, object Value)>(rows, row => row.Value, row => row.Group) { FacetX = row => row.Facet }));
    }

    [Fact]
    public void Sankey_FacetsWithoutRows()
    {
        var plot = new Plot().Add(new SankeyMark<(string Source, string Target, double Value)>([], link => link.Source, link => link.Target, link => link.Value) { FacetX = link => link.Source });
        AssertFiniteScene(plot);
    }

    [Fact]
    public void FlowMarks_InfiniteAndHugeValues()
    {
        // An infinite flow counts as zero, and huge flows whose sums overflow are drawn as flows of the same ratios
        (string Source, string Target, double Value)[] infinite = [("a", "b", double.PositiveInfinity), ("a", "c", 2), ("c", "d", double.NegativeInfinity)];
        (string Source, string Target, double Value)[] huge = [("a", "b", 1e308), ("a", "c", 1e308), ("b", "d", 1e308), ("c", "d", 1e308)];
        foreach (var rows in new[] { infinite, huge })
        {
            AssertFiniteScene(new Plot().Add(new SankeyMark<(string Source, string Target, double Value)>(rows, row => row.Source, row => row.Target, row => row.Value)));
            AssertFiniteScene(new Plot().Add(new ChordMark<(string Source, string Target, double Value)>(rows, row => row.Source, row => row.Target, row => row.Value)));
        }
    }

    private static void AssertFiniteScene(Plot plot)
    {
        var groups = new Stack<SceneGroup>([plot.CreateScene().Root]);
        while (groups.TryPop(out var group))
        {
            foreach (var child in group.Children)
            {
                double[] values = child switch
                {
                    SceneGroup childGroup => [],
                    SceneLine line => [line.X1, line.Y1, line.X2, line.Y2],
                    SceneCircle circle => [circle.CenterX, circle.CenterY, circle.Radius],
                    SceneRectangle rectangle => [rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height],
                    ScenePath path => [.. path.Data.Commands.SelectMany(command => new[] { command.X, command.Y, command.X1, command.Y1, command.X2, command.Y2 })],
                    SceneText text => [text.X, text.Y, text.Rotation],
                    _ => [],
                };

                Assert.All(values, value => Assert.True(double.IsFinite(value), child.GetType().Name));
                if (child is SceneGroup nested)
                {
                    groups.Push(nested);
                }
            }
        }
    }

    [Fact]
    public void SankeyRejectsCycles()
    {
        var plot = new Plot();
        plot.Add(new SankeyMark<Flow>([new("A", "B", 1), new("B", "A", 1)], source: flow => flow.Source, target: flow => flow.Target, value: flow => flow.Value));
        Assert.Throws<InvalidOperationException>(() => plot.ToSvg());
    }

    [Fact]
    public void GroupedAndCumulativeTransforms()
    {
        var plot = new Plot { Height = 260 };
        plot.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Year, y: sale => sale.Amount) { Transform = new GroupTransform { Reducer = GroupReducer.Mean } });
        plot.Add(new LineMark<Sale>(SampleData.Sales.Where(sale => sale.Region == "North"), x: sale => sale.Year, y: sale => sale.Amount) { Transform = new CumulativeTransform(), MarkerRadius = 3 });
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void Choropleth()
    {
        #region Charts_Choropleth
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "countries-110m.geojson");
        var countries = GeoJson.Parse(File.ReadAllText(path))
            .Where(country => country.Properties["continent"] is not "Antarctica")
            .ToList();

        var plot = new Plot
        {
            Title = "GDP per person",
            Subtitle = "Natural Earth, 2019 estimates",
            Height = 380,
            Coordinates = new GeoCoordinates { Projection = new EqualEarthProjection(), Outline = true },

            // The fill is the logarithm of the GDP per person: one step of the scale per factor of 10, labeled in
            // dollars rounded to the hundreds
            ColorScale = new SequentialColorScale
            {
                Minimum = 3,
                Maximum = 5,
                TickFormat = value => "$" + NumberFormats.Si(Math.Round(Math.Pow(10, value) / 100, MidpointRounding.AwayFromZero) * 100),
            },
        };
        plot.Add(new GeoMark<GeoFeature>(countries, country => country.Geometry)
        {
            Fill = country => Math.Log10((double)country.Properties["gdp_md"]! * 1_000_000 / (double)country.Properties["pop_est"]!),
            Title = country => (string?)country.Properties["name"],
        });
        #endregion

        ChartAssert.Svg(plot, "Choropleth");
    }

    [Fact]
    public void MapPoints()
    {
        #region Charts_MapPoints
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "countries-110m.geojson");
        var countries = GeoJson.Parse(File.ReadAllText(path));
        City[] cities =
        [
            new("London", -0.13, 51.51, 9.0), new("Paris", 2.35, 48.86, 11.1), new("Madrid", -3.70, 40.42, 6.7),
            new("Berlin", 13.40, 52.52, 3.7), new("Rome", 12.50, 41.90, 4.3), new("Warsaw", 21.01, 52.23, 1.8),
            new("Stockholm", 18.07, 59.33, 1.7), new("Athens", 23.73, 37.98, 3.2), new("Lisbon", -9.14, 38.72, 2.9),
            new("Vienna", 16.37, 48.21, 2.0), new("Kyiv", 30.52, 50.45, 3.0), new("Bucharest", 26.10, 44.43, 1.8),
        ];

        var plot = new Plot
        {
            Title = "Capitals of Europe",
            Height = 440,
            Clip = true,
            Coordinates = new GeoCoordinates
            {
                Projection = new MercatorProjection(),
                Extent = new GeoBounds(West: -12, South: 35, East: 34, North: 62),
                GraticuleStep = 10,
            },
        };
        plot.SizeLegend.Title = "Population (millions)";
        plot.Add(new GeoMark<GeoFeature>(countries, country => country.Geometry));

        // On a map, x is the longitude and y the latitude
        plot.Add(new DotMark<City>(cities, x: city => city.Longitude, y: city => city.Latitude)
        {
            Size = city => city.Population,
            FillColor = "#e5484d",
            FillOpacity = 0.8,
        });
        plot.Add(new TextMark<City>(cities, text: city => city.Name, x: city => city.Longitude, y: city => city.Latitude) { OffsetY = -12 });
        #endregion

        ChartAssert.Svg(plot, "MapPoints");
    }

    [Fact]
    public void RegionMap()
    {
        #region Charts_RegionMap
        // The map bundle of Switzerland, in the maps folder next to the application
        var map = MapCatalog.Default.GetMap("Switzerland");
        var cantons = map.Load(MapLayer.StatesProvinces);
        var lakes = map.Load(MapLayer.Lakes);
        var rivers = map.Load(MapLayer.Rivers);
        var borders = map.Load(MapLayer.Borders);

        // The largest cities: the rank (0 to 14) orders the places, then their population
        var cities = map.Load(MapLayer.Cities)
            .OrderByDescending(city => (int)city.Properties["rank"]!)
            .ThenByDescending(city => (long)city.Properties["population"]!)
            .Take(8)
            .Select(city => (Name: (string)city.Properties["name"]!, Position: ((GeoPoint)city.Geometry!).Position))
            .ToList();

        var plot = new Plot
        {
            Title = map.Name,
            Subtitle = "© OpenStreetMap contributors, Natural Earth",
            Height = 420,
            Clip = true,
            Coordinates = new GeoCoordinates { Projection = new MercatorProjection(), Extent = map.Bounds },
        };

        // The layers from the bottom: the cantons, the lakes and rivers, then the border of the country
        plot.Add(new GeoMark<GeoFeature>(cantons, canton => canton.Geometry)
        {
            FillColor = "#ebe6da",
            StrokeColor = "#ffffff",
            Title = canton => (string?)canton.Properties["name"],
        });
        plot.Add(new GeoMark<GeoFeature>(lakes, lake => lake.Geometry) { FillColor = "#a9cbe8", StrokeColor = SceneColor.Transparent });
        plot.Add(new GeoMark<GeoFeature>(rivers, river => river.Geometry) { StrokeColor = "#6fa5d6" });
        plot.Add(new GeoMark<GeoFeature>(borders, border => border.Geometry) { FillColor = SceneColor.Transparent, StrokeColor = "#6b6255", StrokeWidth = 1 });
        plot.Add(new DotMark<(string Name, GeoPosition Position)>(cities, x: city => city.Position.Longitude, y: city => city.Position.Latitude) { FillColor = "#2b2b2b", Radius = 3 });
        plot.Add(new TextMark<(string Name, GeoPosition Position)>(cities, text: city => city.Name, x: city => city.Position.Longitude, y: city => city.Position.Latitude) { OffsetY = -10 });
        #endregion

        ChartAssert.Svg(plot, "RegionMap");
    }

    [Fact]
    public void PolarMap()
    {
        #region Charts_PolarMap
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "countries-110m.geojson");
        var countries = GeoJson.Parse(File.ReadAllText(path));

        var plot = new Plot
        {
            Title = "Antarctica and the Southern Ocean",
            Width = 480,
            Height = 520,
            Clip = true,
            Coordinates = new GeoCoordinates
            {
                // Centered on the South Pole: the meridians are lines from the pole and the parallels circles around it
                Projection = new AzimuthalEquidistantProjection { SouthPole = true },
                Extent = new GeoBounds(West: -180, South: -90, East: 180, North: -40),
                GraticuleStep = 15,
            },
        };
        plot.Add(new GeoMark<GeoFeature>(countries, country => country.Geometry) { Fill = country => country.Properties["continent"] });
        #endregion

        ChartAssert.Svg(plot, "PolarMap");
    }

    [Theory]
    [InlineData("Equirectangular")]
    [InlineData("Mercator")]
    [InlineData("EqualEarth")]
    [InlineData("Albers")]
    [InlineData("AzimuthalEquidistant")]
    public void MapProjections(string name)
    {
        GeoProjection projection = name switch
        {
            "Equirectangular" => new EquirectangularProjection(),
            "Mercator" => new MercatorProjection(),
            "EqualEarth" => new EqualEarthProjection(),
            "AzimuthalEquidistant" => new AzimuthalEquidistantProjection { SouthPole = true },
            _ => new AlbersProjection(),
        };
        IEnumerable<GeoFeature> countries = GeoJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "countries-110m.geojson")));
        var plot = new Plot { Width = 480, Height = 300, Coordinates = new GeoCoordinates { Projection = projection, GraticuleStep = 30, Outline = true } };
        if (projection is AlbersProjection)
        {
            // A regional projection: the map of North America, without the regions that span the opposite meridian
            countries = countries.Where(country => country.Properties["continent"] is "North America");
            plot.Coordinates = new GeoCoordinates { Projection = projection, GraticuleStep = 10, Extent = new GeoBounds(-125, 24, -66, 50) };
            plot.Clip = true;
        }

        plot.Add(new GeoMark<GeoFeature>(countries, country => country.Geometry) { Fill = country => country.Properties["continent"] });
        ChartAssert.Svg(plot.ToSvg(), documentationImage: null, memberName: nameof(MapProjections) + "_" + name);
    }

    [Fact]
    public void GeoMark_PositionsFarOutOfRange()
    {
        // A region out of the range of the world has no graticule, and an edge along a parallel out of range gets no
        // more points than a full turn: both used to add points until memory ran out
        GeoFeature[] features =
        [
            new(new GeoLineString([new(-2.3e80, 90)])),
            new(new GeoPolygon([[new(-180, -90), new(1.8e80, -90), new(180, 90), new(-180, 90), new(-180, -90)]])),
        ];
        foreach (var feature in features)
        {
            var plot = new Plot { Coordinates = new GeoCoordinates { Projection = new AlbersProjection(), GraticuleStep = 30, Outline = true } };
            plot.Add(new GeoMark<GeoFeature>([feature], feature => feature.Geometry));
            AssertFiniteScene(plot);
        }
    }

    [Fact]
    public void GeoMarkOptions()
    {
        var square = new GeoPolygon([[new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)], [new(3, 3), new(7, 3), new(7, 7), new(3, 7)]]);
        GeoFeature[] features =
        [
            new(square, new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = 1.0, ["kind"] = "area" }, "square"),
            new(new GeoLineString([new(-5, -5), new(15, 15)]), new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = 2.0, ["kind"] = "line" }),
            new(new GeoMultiPoint([new(-5, 12), new(14, -4)]), new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = 3.0, ["kind"] = "point" }),
            new(new GeoPolygon([[new(12, 0), new(16, 0), new(16, 4)]])),
            new(geometry: null),
        ];
        var plot = new Plot
        {
            Width = 320,
            Height = 300,
            Margin = new ChartMargin(20),
            Coordinates = new GeoCoordinates { Projection = new AlbersProjection { Parallel1 = 0, Parallel2 = 20, CentralMeridian = 5 }, GraticuleStep = 5 },
        };
        plot.Add(new RectMark<int>([0]) { X1 = _ => 20, X2 = _ => 25, Y1 = _ => 0, Y2 = _ => 5, FillColor = "#ffd33d" });
        plot.Add(new GeoMark<GeoFeature>(features, feature => feature.Geometry)
        {
            Fill = feature => feature.Properties.GetValueOrDefault("value"),
            Stroke = feature => feature.Properties.GetValueOrDefault("kind"),
            StrokeWidth = 2,
            PointRadius = 5,
            Title = feature => feature.Id,
        });
        ChartAssert.Svg(plot);

        // Constant colors, and facets
        var faceted = new Plot { Width = 400, Height = 200 };
        faceted.Add(new GeoMark<GeoFeature>(features, feature => feature.Geometry)
        {
            FillColor = "#3ca951",
            StrokeColor = "#1f2328",
            FacetX = feature => feature.Properties.GetValueOrDefault("kind") ?? "other",
        });
        Assert.Contains("class=\"facet\"", faceted.ToSvg());

        // A map without positions shows the whole world; a geo mark draws nothing in Cartesian coordinates
        var world = new Plot { Coordinates = new GeoCoordinates { GraticuleStep = 90 } };
        Assert.Contains("graticule", world.ToSvg());
        var cartesian = new Plot { Coordinates = new CartesianCoordinates() };
        cartesian.Add(new GeoMark<GeoFeature>(features, feature => feature.Geometry));
        Assert.DoesNotContain("<path", cartesian.ToSvg());

        Assert.Throws<ArgumentOutOfRangeException>("value", () => new GeoCoordinates { GraticuleStep = 0 });
        Assert.Throws<ArgumentNullException>("value", () => new GeoCoordinates { Projection = null! });
    }
}
