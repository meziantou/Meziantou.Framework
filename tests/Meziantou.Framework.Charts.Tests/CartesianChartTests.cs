using System.Globalization;

namespace Meziantou.Framework.Charts.Tests;

public sealed class CartesianChartTests
{
    [Fact]
    public void BarChart()
    {
        #region Charts_BarChart
        Sale[] sales =
        [
            new("North", 2024, 150),
            new("South", 2024, 95),
            new("East", 2024, 105),
            new("West", 2024, 160),
        ];

        var plot = new Plot { Title = "Sales by region", Subtitle = "2024, in thousands of euros" };
        plot.Add(new BarMark<Sale>(sales, x: sale => sale.Region, y: sale => sale.Amount));
        var svg = plot.ToSvg();
        #endregion

        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", svg);
        ChartAssert.Svg(svg, "BarChart");
    }

    [Fact]
    public void GroupedBarChart()
    {
        #region Charts_GroupedBarChart
        var plot = new Plot { Title = "Sales by region and year" };
        plot.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount)
        {
            Group = sale => sale.Year,
            Fill = sale => sale.Year,
        });
        #endregion

        ChartAssert.Svg(plot, "GroupedBarChart");
    }

    [Fact]
    public void StackedBarChart()
    {
        #region Charts_StackedBarChart
        var plot = new Plot { Title = "Sales by region, stacked by year" };
        plot.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount)
        {
            Fill = sale => sale.Year,
            Transform = new StackTransform(),
        });
        #endregion

        ChartAssert.Svg(plot, "StackedBarChart");
    }

    [Fact]
    public void NormalizedStackedBarChart()
    {
        #region Charts_NormalizedStackedBarChart
        var plot = new Plot
        {
            Title = "Share of each year",
            YScale = new LinearScale { TickFormat = NumberFormats.Percent() },
        };
        plot.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount)
        {
            Fill = sale => sale.Year,
            Transform = new StackTransform { Offset = StackOffset.Expand },
        });
        #endregion

        ChartAssert.Svg(plot, "NormalizedStackedBarChart");
    }

    [Fact]
    public void HorizontalBarChart()
    {
        #region Charts_HorizontalBarChart
        var plot = new Plot
        {
            Title = "Sales by region",
            Coordinates = new CartesianCoordinates { Transposed = true },
            Height = 260,
        };
        plot.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2024), x: sale => sale.Region, y: sale => sale.Amount)
        {
            CornerRadius = 3,
        });
        plot.YAxis.Label = "Amount";
        #endregion

        ChartAssert.Svg(plot, "HorizontalBarChart");
    }

    [Fact]
    public void LineChart()
    {
        #region Charts_LineChart
        var paris = SampleData.Readings.Where(reading => reading.City == "Paris");
        var plot = new Plot { Title = "Monthly temperature in Paris" };
        plot.Add(new LineMark<Reading>(paris, x: reading => reading.Date, y: reading => reading.Temperature) { MarkerRadius = 3 });
        plot.YAxis.Label = "°C";
        #endregion

        ChartAssert.Svg(plot, "LineChart");
    }

    [Fact]
    public void MultiSeriesLineChart()
    {
        #region Charts_MultiSeriesLineChart
        var plot = new Plot { Title = "Monthly temperature" };
        plot.Add(new RuleYMark(0));
        plot.Add(new LineMark<Reading>(SampleData.Readings, x: reading => reading.Date, y: reading => reading.Temperature)
        {
            Stroke = reading => reading.City,
            Curve = Curve.MonotoneX,
        });
        #endregion

        ChartAssert.Svg(plot, "MultiSeriesLineChart");
    }

    [Fact]
    public void StepLineChart()
    {
        var plot = new Plot { Title = "Step line" };
        plot.Add(new LineMark<Reading>(SampleData.Readings.Where(reading => reading.City == "Oslo"), x: reading => reading.Date, y: reading => reading.Temperature) { Curve = Curve.Step });
        plot.Add(new LineMark<Reading>(SampleData.Readings.Where(reading => reading.City == "Paris"), x: reading => reading.Date, y: reading => reading.Temperature) { Curve = Curve.StepBefore, StrokeColor = "tomato" });
        plot.Add(new LineMark<Reading>(SampleData.Readings.Where(reading => reading.City == "Madrid"), x: reading => reading.Date, y: reading => reading.Temperature) { Curve = Curve.StepAfter, StrokeColor = "seagreen", StrokeDashArray = [4, 2] });
        ChartAssert.Svg(plot, "StepLineChart");
    }

    [Fact]
    public void CatmullRomLineChart()
    {
        var plot = new Plot { Title = "Catmull-Rom" };
        plot.Add(new LineMark<Reading>(SampleData.Readings, x: reading => reading.Date, y: reading => reading.Temperature) { Stroke = reading => reading.City, Curve = Curve.CatmullRom });
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void AreaChart()
    {
        #region Charts_AreaChart
        var oslo = SampleData.Readings.Where(reading => reading.City == "Madrid");
        var plot = new Plot { Title = "Monthly temperature in Madrid" };
        plot.Add(new AreaMark<Reading>(oslo, x: reading => reading.Date, y: reading => reading.Temperature)
        {
            FillOpacity = 0.3,
            StrokeColor = "#4269d0",
            Curve = Curve.MonotoneX,
        });
        #endregion

        ChartAssert.Svg(plot, "AreaChart");
    }

    [Fact]
    public void StackedAreaChart()
    {
        #region Charts_StackedAreaChart
        var plot = new Plot { Title = "Sales by year, stacked by region" };
        plot.Add(new AreaMark<Sale>(SampleData.Sales, x: sale => sale.Year, y: sale => sale.Amount)
        {
            Fill = sale => sale.Region,
            Transform = new StackTransform(),
        });
        plot.XScale = new LinearScale { Ticks = [2022, 2023, 2024], TickFormat = year => year.ToString("0", CultureInfo.InvariantCulture) };
        #endregion

        ChartAssert.Svg(plot, "StackedAreaChart");
    }

    [Fact]
    public void Streamgraph()
    {
        #region Charts_Streamgraph
        var plot = new Plot { Title = "Streamgraph" };
        plot.Add(new AreaMark<Reading>(SampleData.Readings, x: reading => reading.Date, y: reading => reading.Temperature + 15)
        {
            Fill = reading => reading.City,
            Curve = Curve.MonotoneX,
            Transform = new StackTransform { Offset = StackOffset.Silhouette, Order = StackOrder.InsideOut },
        });
        plot.YAxis.Visible = false;
        #endregion

        ChartAssert.Svg(plot, "Streamgraph");
    }

    [Fact]
    public void ScatterPlot()
    {
        #region Charts_ScatterPlot
        var plot = new Plot { Title = "Fuel efficiency by weight" };
        plot.Add(new DotMark<Car>(SampleData.Cars, x: car => car.Weight, y: car => car.Mpg)
        {
            Fill = car => car.Origin,
            Title = car => car.Name,
        });
        plot.XAxis.Label = "Weight (kg)";
        plot.YAxis.Label = "Miles per gallon";
        plot.XAxis.Grid = true;
        #endregion

        ChartAssert.Svg(plot, "ScatterPlot");
    }

    [Fact]
    public void BubbleChart()
    {
        #region Charts_BubbleChart
        var plot = new Plot { Title = "Power, weight and efficiency" };
        plot.Add(new DotMark<Car>(SampleData.Cars, x: car => car.Weight, y: car => car.Mpg)
        {
            Size = car => car.Horsepower,
            Fill = car => car.Origin,
            FillOpacity = 0.7,
            StrokeColor = ChartColor.White,
        });
        plot.SizeScale = new SizeScale { MaxRadius = 20 };
        #endregion

        ChartAssert.Svg(plot, "BubbleChart");
    }

    [Fact]
    public void ReferenceLines()
    {
        #region Charts_ReferenceLines
        var paris = SampleData.Readings.Where(reading => reading.City == "Paris");
        var plot = new Plot { Title = "Months above 15 °C" };
        plot.Add(new LineMark<Reading>(paris, x: reading => reading.Date, y: reading => reading.Temperature));
        plot.Add(new RuleYMark(15) { StrokeColor = "#e5484d", StrokeDashArray = [4, 4] });
        plot.Add(new RuleXMark<DateOnly>([new DateOnly(2024, 7, 1)], x: date => date) { StrokeColor = "#8c959f" });
        #endregion

        ChartAssert.Svg(plot, "ReferenceLines");
    }

    [Fact]
    public void LollipopChart()
    {
        #region Charts_LollipopChart
        var sales = SampleData.Sales.Where(sale => sale.Year == 2024).ToArray();
        var plot = new Plot { Title = "Lollipop" };
        plot.Add(new RuleXMark<Sale>(sales, x: sale => sale.Region) { Y1 = _ => 0, Y2 = sale => sale.Amount, StrokeColor = "#8c959f" });
        plot.Add(new DotMark<Sale>(sales, x: sale => sale.Region, y: sale => sale.Amount) { Radius = 6 });
        plot.YScale = new LinearScale { Zero = true };
        #endregion

        ChartAssert.Svg(plot, "LollipopChart");
    }

    [Fact]
    public void DumbbellChart()
    {
        #region Charts_DumbbellChart
        var start = SampleData.Sales.Where(sale => sale.Year == 2022).ToDictionary(sale => sale.Region, sale => sale.Amount);
        var end = SampleData.Sales.Where(sale => sale.Year == 2024).ToArray();
        var plot = new Plot { Title = "From 2022 to 2024", Coordinates = new CartesianCoordinates { Transposed = true }, Height = 240 };
        plot.Add(new RuleXMark<Sale>(end, x: sale => sale.Region) { Y1 = sale => start[sale.Region], Y2 = sale => sale.Amount, StrokeColor = "#8c959f", StrokeWidth = 2 });
        plot.Add(new DotMark<Sale>(end, x: sale => sale.Region, y: sale => start[sale.Region]) { FillColor = "#9498a0", Radius = 5 });
        plot.Add(new DotMark<Sale>(end, x: sale => sale.Region, y: sale => sale.Amount) { Radius = 5 });
        #endregion

        ChartAssert.Svg(plot, "DumbbellChart");
    }

    [Fact]
    public void GanttChart()
    {
        #region Charts_GanttChart
        GanttTask[] tasks =
        [
            new("Specification", "Product", new(2024, 1, 8), new(2024, 1, 26)),
            new("Design", "Design", new(2024, 1, 22), new(2024, 2, 16)),
            new("Backend", "Engineering", new(2024, 2, 5), new(2024, 3, 22)),
            new("Frontend", "Engineering", new(2024, 2, 19), new(2024, 4, 5)),
            new("Testing", "Quality", new(2024, 3, 18), new(2024, 4, 19)),
            new("Release", "Product", new(2024, 4, 15), new(2024, 4, 26)),
        ];

        var plot = new Plot { Title = "Project plan", Height = 300 };
        plot.Add(new RectMark<GanttTask>(tasks)
        {
            X1 = task => task.Start,
            X2 = task => task.End,
            Y = task => task.Name,
            Fill = task => task.Team,
            Inset = 4,
            CornerRadius = 3,
            Title = task => $"{task.Name}: {task.Start:MMM d} - {task.End:MMM d}",
        });
        plot.Add(new RuleXMark<DateOnly>([new DateOnly(2024, 3, 1)], x: today => today) { StrokeColor = "#e5484d", StrokeDashArray = [4, 3] });
        plot.XAxis.Grid = true;
        #endregion

        ChartAssert.Svg(plot, "GanttChart");
    }

    [Fact]
    public void ComboChart()
    {
        #region Charts_ComboChart
        var plot = new Plot { Title = "Sales in 2024, compared with 2023" };
        plot.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2024), x: sale => sale.Region, y: sale => sale.Amount)
        {
            Fill = _ => "2024",
        });
        plot.Add(new LineMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2023), x: sale => sale.Region, y: sale => sale.Amount)
        {
            Stroke = _ => "2023",
            MarkerRadius = 4,
            StrokeWidth = 2,
        });
        #endregion

        ChartAssert.Svg(plot, "ComboChart");
    }

    [Fact]
    public void ErrorBars()
    {
        #region Charts_ErrorBars
        var summaries = SampleData.Measurements
            .GroupBy(measurement => measurement.Group)
            .Select(group =>
            {
                var mean = group.Average(measurement => measurement.Value);
                var deviation = Math.Sqrt(group.Average(measurement => Math.Pow(measurement.Value - mean, 2)));
                return new GroupSummary(group.Key, mean, deviation);
            })
            .ToArray();

        var plot = new Plot { Title = "Mean and standard deviation", Height = 300 };
        plot.Add(new RuleXMark<GroupSummary>(summaries, x: summary => summary.Group)
        {
            Y1 = summary => summary.Mean - summary.Deviation,
            Y2 = summary => summary.Mean + summary.Deviation,
            StrokeWidth = 2,
        });
        plot.Add(new DotMark<GroupSummary>(summaries, x: summary => summary.Group, y: summary => summary.Mean) { Radius = 5 });
        #endregion

        ChartAssert.Svg(plot, "ErrorBars");
    }

    [Fact]
    public void PopulationPyramid()
    {
        #region Charts_PopulationPyramid
        string[] ages = ["80+", "60-79", "40-59", "20-39", "0-19"];
        double[] men = [1.6, 7.1, 8.4, 7.9, 8.0];
        double[] women = [2.7, 7.9, 8.6, 7.9, 7.6];
        var groups = ages.SelectMany((age, i) => new[] { new AgeGroup(age, "Men", men[i]), new AgeGroup(age, "Women", women[i]) });

        var plot = new Plot
        {
            Title = "Population by age, in millions",
            Height = 280,
            Coordinates = new CartesianCoordinates { Transposed = true },
            YScale = new LinearScale { TickFormat = value => Math.Abs(value).ToString(CultureInfo.InvariantCulture) },
        };

        // The bars of the men go to the left: their values are negative
        plot.Add(new BarMark<AgeGroup>(groups, x: group => group.Age, y: group => group.Sex == "Men" ? -group.Population : group.Population)
        {
            Fill = group => group.Sex,
        });
        plot.Add(new RuleYMark(0));
        #endregion

        ChartAssert.Svg(plot, "PopulationPyramid");
    }

    [Fact]
    public void SlopeChart()
    {
        #region Charts_SlopeChart
        var sales = SampleData.Sales.Where(sale => sale.Year is 2022 or 2024).ToArray();
        var plot = new Plot { Title = "From 2022 to 2024", Width = 360, Height = 380, XScale = new PointScale { Padding = 0.3 } };
        plot.Add(new LineMark<Sale>(sales, x: sale => sale.Year.ToString(CultureInfo.InvariantCulture), y: sale => sale.Amount)
        {
            Stroke = sale => sale.Region,
            MarkerRadius = 4,
        });
        plot.Add(new TextMark<Sale>(sales.Where(sale => sale.Year == 2024), text: sale => sale.Region, x: sale => "2024", y: sale => sale.Amount)
        {
            Anchor = SceneTextAnchor.Start,
            OffsetX = 8,
            Fill = sale => sale.Region,
        });
        plot.Legend.Visible = false;
        #endregion

        ChartAssert.Svg(plot, "SlopeChart");
    }

    [Fact]
    public void Facets()
    {
        #region Charts_Facets
        var plot = new Plot { Title = "Sales by region, one chart per year", Height = 300 };
        plot.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount)
        {
            FacetX = sale => sale.Year,
            Fill = sale => sale.Region,
        });
        plot.Legend.Visible = false;
        plot.YAxis.Label = "Amount";
        #endregion

        ChartAssert.Svg(plot, "Facets");
    }

    [Fact]
    public void FacetWrap()
    {
        #region Charts_FacetWrap
        var plot = new Plot { Title = "Monthly temperature", Height = 420, Facets = { Columns = 2 } };
        plot.Add(new RuleYMark(0));
        plot.Add(new AreaMark<Reading>(SampleData.Readings, x: reading => reading.Date, y: reading => reading.Temperature)
        {
            FacetX = reading => reading.City,
            FillOpacity = 0.3,
            StrokeColor = "#4269d0",
            Curve = Curve.MonotoneX,
        });
        #endregion

        ChartAssert.Svg(plot, "FacetWrap");
    }

    [Fact]
    public void FacetGrid()
    {
        #region Charts_FacetGrid
        var plot = new Plot { Title = "Efficiency by weight", Height = 420, Clip = true };
        plot.Add(new DotMark<Car>(SampleData.Cars, x: car => car.Weight, y: car => car.Mpg)
        {
            FacetX = car => car.Origin,
            FacetY = car => car.Horsepower >= 150 ? "Powerful" : "Economical",
            Fill = car => car.Origin,
            Radius = 5,
        });
        plot.Facets.YDomain = ["Economical", "Powerful"];
        plot.XAxis.Label = "Weight (kg)";
        plot.Legend.Visible = false;
        #endregion

        ChartAssert.Svg(plot, "FacetGrid");
    }

    [Fact]
    public void FacetsTransformEachFacet()
    {
        // Each facet is stacked and binned on its own
        var plot = new Plot { Height = 300, Facets = { Gap = 24, XDomain = ["C", "A", "B"] } };
        plot.Add(new RectMark<Measurement>(SampleData.Measurements) { X = measurement => measurement.Value, FacetX = measurement => measurement.Group, Transform = new BinTransform { Edges = [0, 20, 40, 60, 80, 100] } });
        plot.Add(new TextMark<string>(["C", "A", "B"], text: group => "group " + group) { FacetX = group => group, OffsetY = -120 });
        plot.XAxis.TickRotation = 0;
        var svg = plot.ToSvg();
        Assert.Contains(">C</text>", svg);
        ChartAssert.Svg(svg);

        var stacked = new Plot { Height = 260, Coordinates = new CartesianCoordinates { Transposed = true } };
        stacked.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount) { FacetY = sale => sale.Year, Fill = sale => sale.Year > 2022 ? "recent" : "old", Transform = new StackTransform() });
        stacked.YAxis.Label = "Amount";
        stacked.XAxis.Label = "Region";
        ChartAssert.Svg(stacked, documentationImage: null, memberName: nameof(FacetsTransformEachFacet) + "_Stacked");
    }

    [Fact]
    public void SecondaryAxis()
    {
        #region Charts_SecondaryAxis
        Share[] revenue = [new("Q1", 120), new("Q2", 150), new("Q3", 140), new("Q4", 190)];
        Share[] growth = [new("Q1", 0.04), new("Q2", 0.25), new("Q3", -0.07), new("Q4", 0.36)];

        var plot = new Plot
        {
            Title = "Revenue and growth",
            SecondaryYScale = new LinearScale { TickFormat = NumberFormats.Percent() },
        };
        plot.Add(new BarMark<Share>(revenue, x: quarter => quarter.Name, y: quarter => quarter.Value) { Fill = _ => "Revenue" });
        plot.Add(new LineMark<Share>(growth, x: quarter => quarter.Name, y: quarter => quarter.Value)
        {
            UseSecondaryYScale = true,
            Stroke = _ => "Growth",
            StrokeWidth = 2.5,
            MarkerRadius = 4,
        });
        plot.YAxis.Label = "Revenue (k€)";
        plot.SecondaryYAxis.Label = "Growth";
        #endregion

        ChartAssert.Svg(plot, "SecondaryAxis");
    }

    [Fact]
    public void SecondaryAxisInFacetsAndOtherCoordinates()
    {
        var faceted = new Plot { Height = 300 };
        faceted.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount) { FacetX = sale => sale.Year });
        faceted.Add(new LineMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount / 1000) { FacetX = sale => sale.Year, UseSecondaryYScale = true, StrokeColor = "#e5484d" });
        faceted.SecondaryYAxis.Grid = true;
        faceted.SecondaryYAxis.Line = true;
        faceted.SecondaryYAxis.Label = "Thousands";
        ChartAssert.Svg(faceted);

        // Transposed and polar coordinates map the secondary scale, without an axis
        var transposed = new Plot { Height = 200, Coordinates = new CartesianCoordinates { Transposed = true } };
        transposed.Add(new DotMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount * sale.Amount) { UseSecondaryYScale = true });
        transposed.Add(new DotMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount) { FillColor = "tomato" });
        ChartAssert.Svg(transposed, documentationImage: null, memberName: nameof(SecondaryAxisInFacetsAndOtherCoordinates) + "_Transposed");

        var polar = new Plot { Width = 300, Height = 300, Coordinates = new PolarCoordinates(), XScale = new PointScale() };
        polar.Add(new LineMark<Score>(SampleData.Scores.Where(score => score.Player == "Ada"), x: score => score.Skill, y: score => score.Value) { Closed = true });
        polar.Add(new LineMark<Score>(SampleData.Scores.Where(score => score.Player == "Bob"), x: score => score.Skill, y: score => score.Value / 10) { Closed = true, UseSecondaryYScale = true, StrokeColor = "tomato" });
        ChartAssert.Svg(polar, documentationImage: null, memberName: nameof(SecondaryAxisInFacetsAndOtherCoordinates) + "_Polar");

        var radialBars = new Plot { Width = 300, Height = 300, Coordinates = new PolarCoordinates { Transposed = true } };
        radialBars.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2024), x: sale => sale.Region, y: sale => sale.Amount) { UseSecondaryYScale = true });
        ChartAssert.Svg(radialBars, documentationImage: null, memberName: nameof(SecondaryAxisInFacetsAndOtherCoordinates) + "_RadialBars");
    }

    [Fact]
    public void Symbols()
    {
        var symbols = Enum.GetValues<DotSymbol>();
        var plot = new Plot { Height = 200, Title = "Symbols" };
        plot.Add(new DotMark<DotSymbol>(symbols, x: symbol => symbol.ToString(), y: _ => 1) { Radius = 8, Fill = symbol => symbol.ToString() });
        foreach (var symbol in symbols)
        {
            plot.Add(new DotMark<DotSymbol>([symbol], x: s => s.ToString(), y: _ => 2) { Radius = 8, Symbol = symbol, StrokeColor = ChartColor.Black });
        }

        plot.Legend.Visible = false;
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void EmptyData()
    {
        var plot = new Plot { Title = "No data" };
        plot.Add(new BarMark<Sale>([], x: sale => sale.Region, y: sale => sale.Amount));
        plot.Add(new LineMark<Reading>([], x: reading => reading.Date, y: reading => reading.Temperature));
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void NoMarks()
    {
        ChartAssert.Svg(new Plot { Width = 200, Height = 100 });
    }

    [Fact]
    public void SinglePoint()
    {
        var plot = new Plot();
        plot.Add(new DotMark<Car>(SampleData.Cars.Take(1), x: car => car.Weight, y: car => car.Mpg));
        plot.Add(new LineMark<Car>(SampleData.Cars.Take(1), x: car => car.Weight, y: car => car.Mpg));
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void AllZeros()
    {
        var plot = new Plot();
        plot.Add(new BarMark<Sale>(SampleData.Sales.Select(sale => sale with { Amount = 0 }).Where(sale => sale.Year == 2024), x: sale => sale.Region, y: sale => sale.Amount));
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void NegativeValues()
    {
        var plot = new Plot { Title = "Profit by quarter" };
        Share[] profits = [new("Q1", 12), new("Q2", -8), new("Q3", 5), new("Q4", -15)];
        plot.Add(new BarMark<Share>(profits, x: profit => profit.Name, y: profit => profit.Value) { Fill = profit => profit.Value >= 0 ? "profit" : "loss" });
        plot.Add(new RuleYMark(0));
        plot.ColorScale = new OrdinalColorScale { Domain = ["profit", "loss"], Palette = ["#3ca951", "#e5484d"] };
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void DivergingStackedBars()
    {
        Sale[] changes = [new("North", 1, 10), new("North", 2, -4), new("South", 1, -6), new("South", 2, 8), new("East", 1, 3), new("East", 2, -9)];
        var plot = new Plot();
        plot.Add(new BarMark<Sale>(changes, x: sale => sale.Region, y: sale => sale.Amount) { Fill = sale => sale.Year, Transform = new StackTransform() });
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void MissingValuesBreakLines()
    {
        double[] values = [1, 3, double.NaN, 4, 2, double.NaN, double.NaN, 5, 6];
        var plot = new Plot { Title = "Gaps" };
        plot.Add(new LineMark<int>(Enumerable.Range(0, values.Length), x: i => i, y: i => values[i]) { MarkerRadius = 2 });
        plot.Add(new AreaMark<int>(Enumerable.Range(0, values.Length), x: i => i, y: i => values[i]) { FillOpacity = 0.2 });
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void LongLabelsAreRotated()
    {
        Share[] values =
        [
            new("Information technology", 30), new("Health care and social assistance", 25), new("Manufacturing", 20),
            new("Retail trade", 18), new("Professional, scientific and technical services", 15), new("Construction", 12),
            new("Transportation and warehousing", 10), new("Accommodation and food services", 8),
        ];
        var plot = new Plot { Title = "Long labels", Width = 480 };
        plot.Add(new BarMark<Share>(values, x: value => value.Name, y: value => value.Value));
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void ManyLabelsAreThinned()
    {
        var plot = new Plot { Title = "Thinned labels", Width = 300, Height = 200 };
        plot.Add(new BarMark<int>(Enumerable.Range(1, 60), x: day => "Day " + day.ToString(CultureInfo.InvariantCulture), y: day => day % 7));
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void ExplicitTickRotation()
    {
        var plot = new Plot { Height = 250 };
        plot.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2023), x: sale => sale.Region, y: sale => sale.Amount));
        plot.XAxis.TickRotation = -90;
        plot.YAxis.Line = true;
        plot.YAxis.TickSize = 0;
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void ExplicitZeroRotationThinsLabels()
    {
        var plot = new Plot { Width = 260, Height = 200 };
        plot.Add(new BarMark<int>(Enumerable.Range(1, 30), x: day => "Day " + day.ToString(CultureInfo.InvariantCulture), y: day => day));
        plot.XAxis.TickRotation = 0;
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void LogScale()
    {
        var plot = new Plot { Title = "Log scale" };
        plot.Add(new LineMark<int>(Enumerable.Range(0, 7), x: i => i, y: i => Math.Pow(10, i) * 3) { MarkerRadius = 3 });
        plot.Add(new DotMark<int>([0], x: i => i, y: _ => -5));
        plot.YScale = new LogScale();
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void LogScaleWithFewDecades()
    {
        var plot = new Plot { Height = 250 };
        plot.Add(new DotMark<int>(Enumerable.Range(1, 30), x: i => i, y: i => i * 3.5));
        plot.YScale = new LogScale { Base = 10 };
        plot.XScale = new LogScale { Base = 2 };
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void PowAndSymlogScales()
    {
        var plot = new Plot { Height = 250 };
        plot.Add(new DotMark<int>(Enumerable.Range(-10, 21), x: i => i * 100, y: i => i * i));
        plot.XScale = new SymlogScale { Constant = 10 };
        plot.YScale = new PowScale { Exponent = 2 };
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void SqrtScale()
    {
        var plot = new Plot { Height = 250 };
        plot.Add(new BarMark<Share>([new("a", 1), new("b", 4), new("c", 9), new("d", 16)], x: share => share.Name, y: share => share.Value));
        plot.YScale = new SqrtScale { Clamp = true };
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void TimeScaleOfHours()
    {
        var start = new DateTimeOffset(2024, 5, 6, 0, 0, 0, TimeSpan.Zero);
        var plot = new Plot { Title = "Hourly load", Height = 250 };
        plot.Add(new LineMark<int>(Enumerable.Range(0, 49), x: i => start.AddHours(i), y: i => 50 + (30 * Math.Sin(i * Math.PI / 12))));
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void TimeScaleOfMinutesWithFormat()
    {
        var start = new DateTime(2024, 5, 6, 9, 0, 0, DateTimeKind.Utc);
        var plot = new Plot { Height = 250 };
        plot.Add(new LineMark<int>(Enumerable.Range(0, 61), x: i => start.AddMinutes(i), y: i => i % 10));
        plot.XScale = new TimeScale { Nice = true, TimeFormat = time => time.ToString("HH:mm", CultureInfo.InvariantCulture) };
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void TimeScaleOfYears()
    {
        var plot = new Plot { Height = 250 };
        plot.Add(new LineMark<int>(Enumerable.Range(0, 40), x: i => new DateOnly(1990 + i, 1, 1), y: i => i * i));
        plot.XScale = new TimeScale { Start = new DateTimeOffset(1985, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void TimeScaleOfSeconds()
    {
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var plot = new Plot { Height = 200 };
        plot.Add(new LineMark<int>(Enumerable.Range(0, 30), x: i => start.AddSeconds(i * 2), y: i => i));
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void ExplicitDomainAndClip()
    {
        var plot = new Plot { Title = "Zoomed", Clip = true, Height = 250 };
        plot.Add(new LineMark<Reading>(SampleData.Readings, x: reading => reading.Date, y: reading => reading.Temperature) { Stroke = reading => reading.City });
        plot.YScale = new LinearScale { Minimum = 0, Maximum = 20 };
        plot.XScale = new TimeScale { Start = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero), End = new DateTimeOffset(2024, 9, 1, 0, 0, 0, TimeSpan.Zero) };
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void ReversedAndPointScales()
    {
        var plot = new Plot { Height = 250 };
        plot.Add(new DotMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount) { Fill = sale => sale.Year });
        plot.XScale = new PointScale { Domain = ["West", "East", "South", "North"], Padding = 0.2 };
        plot.YScale = new LinearScale { Reverse = true, TickCount = 4 };
        plot.Legend.Position = LegendPosition.Right;
        plot.Legend.Title = "Year";
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void BandScaleOptions()
    {
        var plot = new Plot { Height = 250 };
        plot.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2022), x: sale => sale.Region, y: sale => sale.Amount));
        plot.XScale = new BandScale { PaddingInner = 0.5, PaddingOuter = 0, Align = 0, Reverse = true };
        plot.Legend.Position = LegendPosition.Bottom;
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void BarsOnContinuousScale()
    {
        var plot = new Plot { Height = 250 };
        plot.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Region == "North"), x: sale => sale.Year, y: sale => sale.Amount));
        plot.XScale = new LinearScale();
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void DarkTheme()
    {
        #region Charts_DarkTheme
        var plot = new Plot { Title = "Dark theme", Theme = ChartTheme.Dark };
        plot.Add(new LineMark<Reading>(SampleData.Readings, x: reading => reading.Date, y: reading => reading.Temperature)
        {
            Stroke = reading => reading.City,
        });
        #endregion

        ChartAssert.Svg(plot, "DarkTheme");
    }

    [Fact]
    public void CustomTheme()
    {
        #region Charts_CustomTheme
        var theme = ChartTheme.Light with
        {
            FontFamily = "Georgia, serif",
            Palette = ["#264653", "#2a9d8f", "#e9c46a", "#f4a261", "#e76f51"],
            Background = null,
        };
        var plot = new Plot { Title = "Custom theme", Theme = theme };
        plot.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount)
        {
            Fill = sale => sale.Year,
            Transform = new StackTransform(),
        });
        #endregion

        ChartAssert.Svg(plot);
    }

    [Fact]
    public void Accessibility()
    {
        #region Charts_Accessibility
        var plot = new Plot
        {
            Title = "Sales by region",
            Description = "North sold 150, South 95, East 105 and West 160 thousand euros in 2024.",
        };
        plot.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2024), x: sale => sale.Region, y: sale => sale.Amount)
        {
            Title = sale => $"{sale.Region}: {sale.Amount}",
            CssClass = "sales",
        });
        var svg = plot.ToSvg(new SvgRenderOptions { IdPrefix = "sales", IncludeNamespace = false });
        #endregion

        Assert.Contains("<desc>North sold 150", svg);
        Assert.Contains("<title>North: 150</title>", svg);
        Assert.Contains("role=\"img\"", svg);
        ChartAssert.Svg(svg);
    }

    [Fact]
    public void ExplicitMarginAndNoAxes()
    {
        var plot = new Plot { Width = 300, Height = 120, Margin = new ChartMargin(20) };
        plot.Add(new RectMark<int>([0]) { FillColor = "#eeeeee" });
        plot.Add(new DotMark<int>(Enumerable.Range(0, 10), x: i => i, y: i => i));
        plot.XAxis.Visible = false;
        plot.YAxis.Visible = false;
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void TickMarkStripPlot()
    {
        var plot = new Plot { Title = "Strip plot", Height = 220 };
        plot.Add(new TickMark<Measurement>(SampleData.Measurements, x: measurement => measurement.Value, y: measurement => measurement.Group) { Stroke = measurement => measurement.Group });
        plot.Add(new TickMark<double>([25, 75], x: value => value) { StrokeColor = "#e5484d", StrokeWidth = 2 });
        plot.Legend.Visible = false;
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void TextLabels()
    {
        var sales = SampleData.Sales.Where(sale => sale.Year == 2024).ToArray();
        var plot = new Plot { Title = "Bars with labels", Height = 260 };
        plot.Add(new BarMark<Sale>(sales, x: sale => sale.Region, y: sale => sale.Amount));
        plot.Add(new TextMark<Sale>(sales, text: sale => sale.Amount.ToString(CultureInfo.InvariantCulture), x: sale => sale.Region, y: sale => sale.Amount) { OffsetY = -8, Bold = true });
        plot.Add(new TextMark<string>(["draft"], text: text => text) { FontSize = 40, FillColor = "#00000022", Rotation = -20 });
        plot.Add(new TextMark<int>([0], text: _ => "east side", x: _ => "East") { Anchor = SceneTextAnchor.Start, Fill = _ => "label" });
        plot.Legend.Visible = false;
        ChartAssert.Svg(plot);
    }

    [Fact]
    public void WriteSvgToStreamAndWriter()
    {
        var plot = new Plot();
        plot.Add(new RuleYMark(1, 2));
        var expected = plot.ToSvg();

        using var stream = new MemoryStream();
        plot.WriteSvg(stream);
        Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(stream.ToArray()));

        using var writer = new StringWriter();
        plot.WriteSvg(writer, new SvgRenderOptions { Precision = 4 });
        Assert.StartsWith("<svg", writer.ToString());
    }
}
