using System.Globalization;

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
        plot.Add(new RectMark<Measurement>(SampleData.Measurements) { X = measurement => measurement.Value, Transform = new BinTransform { Edges = [0, 25, 50, 75, 100] }, Inset = 0, StrokeColor = ChartColor.White });
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
        plot.Add(new BoxMark<Measurement>(SampleData.Measurements, x: measurement => measurement.Group, y: measurement => measurement.Value) { BoxWidth = 0.4, StrokeColor = ChartColor.Black });
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
    public void GroupedAndCumulativeTransforms()
    {
        var plot = new Plot { Height = 260 };
        plot.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Year, y: sale => sale.Amount) { Transform = new GroupTransform { Reducer = GroupReducer.Mean } });
        plot.Add(new LineMark<Sale>(SampleData.Sales.Where(sale => sale.Region == "North"), x: sale => sale.Year, y: sale => sale.Amount) { Transform = new CumulativeTransform(), MarkerRadius = 3 });
        ChartAssert.Svg(plot);
    }
}
