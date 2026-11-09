namespace Meziantou.Framework.Charts.Tests;

public sealed class TransformTests
{
    private static MarkData CreateStackData()
    {
        return new MarkData(4)
        {
            X = ChannelValues.FromCategories(["a", "a", "b", "b"]),
            Y = ChannelValues.FromNumbers([1, 2, 3, -4]),
            Fill = ChannelValues.FromCategories(["s1", "s2", "s1", "s2"]),
        };
    }

    [Fact]
    public void Stack_Diverging()
    {
        var result = new StackTransform().Apply(CreateStackData());
        Assert.Null(result.Y);
        Assert.Equal([0, 1, 0, 0], result.Y1!.Numbers);
        Assert.Equal([1, 3, 3, -4], result.Y2!.Numbers);
    }

    [Fact]
    public void Stack_None()
    {
        var result = new StackTransform { Offset = StackOffset.None }.Apply(CreateStackData());
        Assert.Equal([0, 1, 0, 3], result.Y1!.Numbers);
        Assert.Equal([1, 3, 3, -1], result.Y2!.Numbers);
    }

    [Fact]
    public void Stack_Expand()
    {
        var data = CreateStackData();
        data.Y = ChannelValues.FromNumbers([1, 2, 3, 1]);
        var result = new StackTransform { Offset = StackOffset.Expand }.Apply(data);
        Assert.Equal([0, 1 / 3.0, 0, 0.75], result.Y1!.Numbers);
        Assert.Equal([1 / 3.0, 1, 0.75, 1], result.Y2!.Numbers);
    }

    [Fact]
    public void Stack_Silhouette()
    {
        var data = CreateStackData();
        data.Y = ChannelValues.FromNumbers([1, 2, 3, 1]);
        var result = new StackTransform { Offset = StackOffset.Silhouette }.Apply(data);
        Assert.Equal([-1.5, -0.5, -2, 1], result.Y1!.Numbers);
        Assert.Equal([-0.5, 1.5, 1, 2], result.Y2!.Numbers);
    }

    [Theory]
    [InlineData(StackOrder.Given, new[] { 0.0, 1, 0, 3 })]
    [InlineData(StackOrder.Ascending, new[] { 2.0, 0, 1, 0 })]
    [InlineData(StackOrder.Descending, new[] { 0.0, 1, 0, 3 })]
    [InlineData(StackOrder.InsideOut, new[] { 2.0, 0, 1, 0 })]
    public void Stack_Order(StackOrder order, double[] expectedY1)
    {
        var data = new MarkData(4)
        {
            X = ChannelValues.FromNumbers([1, 1, 2, 2]),
            Y = ChannelValues.FromNumbers([1, 2, 3, 1]),
            Series = ChannelValues.FromCategories(["s1", "s2", "s1", "s2"]),
        };
        var result = new StackTransform { Order = order, Offset = StackOffset.None }.Apply(data);
        Assert.Equal(expectedY1, result.Y1!.Numbers);
    }

    [Fact]
    public void Stack_KeepsMissingValuesMissingAndIgnoresOtherData()
    {
        var data = new MarkData(2) { X = ChannelValues.FromCategories(["a", "a"]), Y = ChannelValues.FromNumbers([double.NaN, 2]) };
        var result = new StackTransform().Apply(data);
        Assert.True(double.IsNaN(result.Y1!.Numbers[0]));
        Assert.Equal(2, result.Y2!.Numbers[1]);

        var categories = new MarkData(1) { X = ChannelValues.FromCategories(["a"]), Y = ChannelValues.FromCategories(["b"]) };
        Assert.Same(categories, new StackTransform().Apply(categories));
    }

    [Fact]
    public void Bin_SturgesWithNiceEdges()
    {
        var transform = new BinTransform();
        var values = Enumerable.Range(0, 100).Select(i => (double)i).ToArray();
        Assert.Equal([0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100], transform.ComputeEdges(values));

        var data = new MarkData(values.Length) { X = ChannelValues.FromNumbers(values) };
        var result = transform.Apply(data);
        Assert.Equal(10, result.Count);
        Assert.All(result.Y2!.Numbers, count => Assert.Equal(10, count));
        Assert.All(result.Y1!.Numbers, count => Assert.Equal(0, count));
        Assert.Equal("[90, 100]: 10", result.Titles![^1]);
    }

    [Fact]
    public void Bin_ThresholdRules()
    {
        var values = SampleData.Normal(1000, 0, 1, seed: 7);
        var scott = new BinTransform { Thresholds = BinThresholds.Scott }.ComputeEdges(values);
        var freedman = new BinTransform { Thresholds = BinThresholds.FreedmanDiaconis }.ComputeEdges(values);
        var count = new BinTransform { Count = 4 }.ComputeEdges(values);
        Assert.InRange(scott.Length - 1, 10, 40);
        Assert.InRange(freedman.Length - 1, 10, 40);
        Assert.InRange(count.Length - 1, 3, 8);
        Assert.Equal([4.5, 5.5], new BinTransform().ComputeEdges([5, 5]));
        Assert.Equal([0, 1], new BinTransform().ComputeEdges([double.NaN]));
    }

    [Fact]
    public void Bin_FindBin()
    {
        double[] edges = [0, 10, 20];
        Assert.Equal(0, BinTransform.FindBin(edges, 0));
        Assert.Equal(0, BinTransform.FindBin(edges, 9.99));
        Assert.Equal(1, BinTransform.FindBin(edges, 10));
        Assert.Equal(1, BinTransform.FindBin(edges, 20));
        Assert.Equal(-1, BinTransform.FindBin(edges, 20.1));
        Assert.Equal(-1, BinTransform.FindBin(edges, -1));
        Assert.Equal(-1, BinTransform.FindBin(edges, double.NaN));
    }

    [Fact]
    public void Bin_ExplicitEdgesAndGroups()
    {
        var data = new MarkData(4) { X = ChannelValues.FromNumbers([1, 2, 6, 7]), Fill = ChannelValues.FromCategories(["a", "a", "b", "b"]) };
        var result = new BinTransform { Edges = [0, 5, 10] }.Apply(data);
        Assert.Equal(4, result.Count);
        Assert.Equal([2, 0, 0, 2], result.Y2!.Numbers);
        Assert.Equal(["a", "a", "b", "b"], result.Fill!.Categories);

        var categories = new MarkData(1) { X = ChannelValues.FromCategories(["a"]) };
        Assert.Same(categories, new BinTransform().Apply(categories));
    }

    [Fact]
    public void KernelDensity_Gaussian()
    {
        // A single value: the density of the standard normal distribution
        Assert.Equal(1 / Math.Sqrt(2 * Math.PI), KernelDensity.Estimate([0], 1, 0), 1e-12);
        Assert.Equal(Math.Exp(-0.5) / Math.Sqrt(2 * Math.PI), KernelDensity.Estimate([0], 1, 1), 1e-12);
        Assert.Equal(Math.Exp(-0.5) / Math.Sqrt(2 * Math.PI) / 2, KernelDensity.Estimate([0], 2, 2), 1e-12);

        // The average of the kernels of two values
        Assert.Equal((1 + Math.Exp(-2)) / (2 * Math.Sqrt(2 * Math.PI)), KernelDensity.Estimate([0, 2], 1, 0), 1e-12);

        var (x, density) = KernelDensity.Evaluate([0], 1, -1, 1, 3);
        Assert.Equal([-1, 0, 1], x);
        Assert.Equal(density[0], density[2], 1e-12);
        Assert.HasCount(2, KernelDensity.Evaluate([0], 1, 0, 1, 1).X);
    }

    [Fact]
    public void KernelDensity_Silverman()
    {
        // σ = 1.2910 (sample standard deviation), IQR / 1.34 = 1.1194: 0.9 × 1.1194 × 4^(-1/5)
        double[] values = [1, 2, 3, 4];
        Assert.Equal(0.9 * (1.5 / 1.34) * Math.Pow(4, -0.2), KernelDensity.SilvermanBandwidth(values), 1e-12);
        Assert.Equal(0.9 * 0.5 * Math.Pow(2, -0.2), KernelDensity.SilvermanBandwidth([5, 5]), 1e-12);
        Assert.Equal(0.9, KernelDensity.SilvermanBandwidth([0]), 1e-12);
        Assert.Equal(1, KernelDensity.SilvermanBandwidth([]));

        // With a zero interquartile range, the standard deviation
        double[] spiky = [0, 0, 0, 0, 0, 0, 10];
        var deviation = Math.Sqrt(spiky.Sum(value => Math.Pow(value - spiky.Average(), 2)) / 6);
        Assert.Equal(0.9 * deviation * Math.Pow(7, -0.2), KernelDensity.SilvermanBandwidth(spiky), 1e-12);
    }

    [Fact]
    public void Density_PerSeries()
    {
        var data = new MarkData(4) { X = ChannelValues.FromNumbers([0, 1, 10, double.NaN]), Fill = ChannelValues.FromCategories(["a", "a", "b", "c"]) };
        var result = new DensityTransform { Bandwidth = 1, Samples = 5, Cut = 2 }.Apply(data);
        Assert.Equal(10, result.Count);
        Assert.Equal([-2, -0.75, 0.5, 1.75, 3], result.X!.Numbers[..5]);
        Assert.Equal(["a", "a", "a", "a", "a", "b", "b", "b", "b", "b"], result.Fill!.Categories);
        Assert.Equal(KernelDensity.Estimate([0, 1], 1, 0.5), result.Y!.Numbers[2], 1e-12);

        var categories = new MarkData(1) { X = ChannelValues.FromCategories(["a"]) };
        Assert.Same(categories, new DensityTransform().Apply(categories));
    }

    [Fact]
    public void Regression_Linear()
    {
        #region Charts_RegressionFit
        // The textbook example: a slope of 0.6, an intercept of 2.2, and 60% of the variance explained
        var fit = Regression.Fit([(1, 2), (2, 4), (3, 5), (4, 4), (5, 5)]);
        Assert.Equal(2.2, fit.Coefficients[0], 1e-12);
        Assert.Equal(0.6, fit.Coefficients[1], 1e-12);
        Assert.Equal(0.6, fit.RSquared, 1e-12);
        Assert.Equal(5.2, fit.Evaluate(5), 1e-12);
        Assert.Equal("y = 0.6x + 2.2 (R² = 0.6)", fit.ToString());
        #endregion
    }

    [Fact]
    public void Regression_Polynomials()
    {
        var parabola = Regression.Fit([(-1, 1), (0, 0), (1, 1), (2, 4), (double.NaN, 5)], degree: 2);
        Assert.Equal([0, 0, 1], parabola.Coefficients.Select(value => Math.Round(value, 9, MidpointRounding.AwayFromZero) + 0).ToArray());
        Assert.Equal(1, parabola.RSquared, 1e-12);
        Assert.Equal(2, parabola.Degree);
        Assert.Equal("y = x^2 (R² = 1)", parabola.ToString());

        // Large x values, such as years, stay accurate: the x values are centered before solving
        var years = Regression.Fit(Enumerable.Range(2000, 11).Select(year => ((double)year, Math.Pow(year - 2000, 2) - 3)), degree: 2);
        Assert.Equal(97, years.Evaluate(2010), 1e-6);

        var constant = Regression.Fit([(0, 1), (1, 3)], degree: 0);
        Assert.Equal(2, constant.Coefficients[0], 1e-12);
        Assert.Equal("y = 2 (R² = 0)", constant.ToString());
        Assert.Equal("y = -x + 1 (R² = 1)", Regression.Fit([(0, 1), (1, 0)]).ToString());
        Assert.Equal("y = 0 (R² = 1)", Regression.Fit([(0, 0), (1, 0)], degree: 0).ToString());
        Assert.Equal(1, Regression.Fit([(0, 5), (1, 5)]).RSquared);

        Assert.Throws<ArgumentException>("points", () => Regression.Fit([(1, 1), (1, 2)]));
        Assert.Throws<ArgumentOutOfRangeException>("degree", () => Regression.Fit([], degree: 11));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RegressionTransform { Degree = -1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RegressionTransform { Samples = 1 });
    }

    [Fact]
    public void Regression_Transform()
    {
        var data = new MarkData(7)
        {
            X = ChannelValues.FromNumbers([0, 1, 2, 0, 1, 2, 5]),
            Y = ChannelValues.FromNumbers([1, 3, 5, 0, 1, 4, 9]),
            Stroke = ChannelValues.FromCategories(["a", "a", "a", "b", "b", "b", "c"]),
        };
        var lines = new RegressionTransform().Apply(data);
        Assert.Equal(4, lines.Count);
        Assert.Equal([1, 5, -0.333333333, 3.666666667], lines.Y!.Numbers.Select(value => Math.Round(value, 9, MidpointRounding.AwayFromZero)).ToArray());
        Assert.Equal(["a", "a", "b", "b"], lines.Stroke!.Categories);
        Assert.Equal("y = 2x + 1 (R² = 1)", lines.Titles![0]);

        var curves = new RegressionTransform { Degree = 2, Samples = 5 }.Apply(data);
        Assert.Equal(10, curves.Count);
        var categories = new MarkData(1) { X = ChannelValues.FromCategories(["a"]), Y = ChannelValues.FromNumbers([1]) };
        Assert.Same(categories, new RegressionTransform().Apply(categories));
    }

    [Theory]
    [InlineData(WindowAnchor.Middle, 3, new[] { 1.5, 2, 3, 4, 4.5 })]
    [InlineData(WindowAnchor.End, 3, new[] { 1, 1.5, 2, 3, 4 })]
    [InlineData(WindowAnchor.Start, 3, new[] { 2, 3, 4, 4.5, 5 })]
    [InlineData(WindowAnchor.Middle, 2, new[] { 1.5, 2.5, 3.5, 4.5, 5 })]
    [InlineData(WindowAnchor.Middle, 1, new[] { 1.0, 2, 3, 4, 5 })]
    public void Window_Anchors(WindowAnchor anchor, int size, double[] expected)
    {
        var data = new MarkData(5) { Y = ChannelValues.FromNumbers([1, 2, 3, 4, 5]) };
        Assert.Equal(expected, new WindowTransform { Anchor = anchor, Size = size }.Apply(data).Y!.Numbers);
    }

    [Fact]
    public void Window_SeriesReducersAndMissingValues()
    {
        var data = new MarkData(6)
        {
            Y = ChannelValues.FromNumbers([1, double.NaN, 3, 10, 20, 30]),
            Series = ChannelValues.FromCategories(["a", "a", "a", "b", "b", "b"]),
        };
        Assert.Equal([1, 2, 3, 15, 20, 25], new WindowTransform().Apply(data).Y!.Numbers);
        Assert.Equal([1, 4, 3, 30, 60, 50], new WindowTransform { Reducer = GroupReducer.Sum }.Apply(data).Y!.Numbers);
        Assert.Equal([2, 3, 2, 2, 3, 2], new WindowTransform { Reducer = GroupReducer.Count }.Apply(data).Y!.Numbers);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new WindowTransform { Size = 0 });
        var categories = new MarkData(1) { Y = ChannelValues.FromCategories(["a"]) };
        Assert.Same(categories, new WindowTransform().Apply(categories));
    }

    [Fact]
    public void Quantile_R7()
    {
        double[] values = [1, 2, 3, 4];
        Assert.Equal(1.75, BoxStatistics.Quantile(values, 0.25));
        Assert.Equal(2.5, BoxStatistics.Quantile(values, 0.5));
        Assert.Equal(3.25, BoxStatistics.Quantile(values, 0.75));
        Assert.Equal(1, BoxStatistics.Quantile(values, -1));
        Assert.Equal(4, BoxStatistics.Quantile(values, 2));
        Assert.True(double.IsNaN(BoxStatistics.Quantile([], 0.5)));
    }

    [Fact]
    public void BoxSummary_TukeyWhiskers()
    {
        var summary = BoxStatistics.Summarize([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 100, double.NaN])!;
        Assert.Equal(11, summary.Count);
        Assert.Equal(3.5, summary.LowerQuartile);
        Assert.Equal(6, summary.Median);
        Assert.Equal(8.5, summary.UpperQuartile);
        Assert.Equal(1, summary.LowerWhisker);
        Assert.Equal(10, summary.UpperWhisker);
        Assert.Equal([100], summary.Outliers);
        Assert.Null(BoxStatistics.Summarize([]));
    }

    [Fact]
    public void Quantile_InfiniteAndHugeValues()
    {
        Assert.Equal(2, BoxStatistics.Quantile([1, 2, double.PositiveInfinity], 0.5));
        Assert.Equal(double.NegativeInfinity, BoxStatistics.Quantile([double.NegativeInfinity, 1], 0.5));
        Assert.Equal(double.PositiveInfinity, BoxStatistics.Quantile([double.PositiveInfinity, double.PositiveInfinity], 0.5));
        Assert.Equal(0, BoxStatistics.Quantile([-1e308, 1e308], 0.5));
        Assert.True(double.IsNaN(BoxStatistics.Quantile([double.NegativeInfinity, double.PositiveInfinity], 0.5)));
    }

    [Fact]
    public void BoxSummary_InfiniteValues()
    {
        var summary = BoxStatistics.Summarize([double.NegativeInfinity])!;
        Assert.Equal(double.NegativeInfinity, summary.Median);
        Assert.Equal(double.NegativeInfinity, summary.LowerWhisker);
        Assert.Equal(double.NegativeInfinity, summary.UpperWhisker);
        Assert.Empty(summary.Outliers);

        summary = BoxStatistics.Summarize([1, 2, 3, 4, double.PositiveInfinity])!;
        Assert.Equal(3, summary.Median);
        Assert.Equal(1, summary.LowerWhisker);
        Assert.Equal(4, summary.UpperWhisker);
        Assert.Equal([double.PositiveInfinity], summary.Outliers);
    }

    [Theory]
    [InlineData(GroupReducer.Sum, 6)]
    [InlineData(GroupReducer.Mean, 2)]
    [InlineData(GroupReducer.Count, 4)]
    [InlineData(GroupReducer.Min, 1)]
    [InlineData(GroupReducer.Max, 3)]
    [InlineData(GroupReducer.Median, 2)]
    public void Group_Reducers(GroupReducer reducer, double expected)
    {
        var data = new MarkData(5)
        {
            X = ChannelValues.FromCategories(["a", "a", "a", "a", "b"]),
            Y = ChannelValues.FromNumbers([1, 2, 3, double.NaN, 10]),
        };
        var result = new GroupTransform { Reducer = reducer }.Apply(data);
        Assert.Equal(2, result.Count);
        Assert.Equal(expected, result.Y!.Numbers[0]);
        Assert.Equal(["a", "b"], result.X!.Categories);
    }

    [Fact]
    public void Group_EmptyGroups()
    {
        Assert.Equal(0, GroupTransform.Reduce(GroupReducer.Sum, 1, []));
        Assert.True(double.IsNaN(GroupTransform.Reduce(GroupReducer.Mean, 1, [])));
        var noX = new MarkData(1);
        Assert.Same(noX, new GroupTransform().Apply(noX));
    }

    [Fact]
    public void Cumulative_RunningTotalsPerSeries()
    {
        var data = new MarkData(5)
        {
            Y = ChannelValues.FromNumbers([1, 2, double.NaN, 3, 10]),
            Series = ChannelValues.FromCategories(["a", "a", "a", "a", "b"]),
        };
        var result = new CumulativeTransform().Apply(data);
        Assert.Equal([0, 1, 3, 3, 0], result.Y1!.Numbers);
        Assert.Equal([1, 3, 3, 6, 10], result.Y2!.Numbers);

        var (y1, y2) = CumulativeTransform.Accumulate([5, -2, 0], [[0, 1, 2]], [false, false, true]);
        Assert.Equal([0, 5, 0], y1);
        Assert.Equal([5, 3, 3], y2);

        var categories = new MarkData(1) { Y = ChannelValues.FromCategories(["a"]) };
        Assert.Same(categories, new CumulativeTransform().Apply(categories));
    }

    [Fact]
    public void ChannelValues_ConvertObjects()
    {
        var numbers = ChannelValues.FromObjects([1, 2.5f, 3m, 4L, (short)5, (byte)6, (sbyte)7, 8u, 9ul, (ushort)10, (Half)11, (Int128)12, (UInt128)13, new System.Numerics.BigInteger(14), null, double.NaN]);
        Assert.Equal(ChannelKind.Number, numbers.Kind);
        Assert.Equal(14, numbers.Numbers[13]);
        Assert.True(numbers.IsMissing(14));
        Assert.True(numbers.IsMissing(15));

        var times = ChannelValues.FromObjects([new DateOnly(1970, 1, 2), new DateTime(1970, 1, 1, 0, 0, 1, DateTimeKind.Utc), new DateTimeOffset(1970, 1, 1, 1, 0, 0, TimeSpan.FromHours(1)), new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Local)]);
        Assert.Equal(ChannelKind.Time, times.Kind);
        Assert.Equal(86_400_000, times.Numbers[0]);
        Assert.Equal(1000, times.Numbers[1]);
        Assert.Equal(0, times.Numbers[2]);
        Assert.Equal("1970-01-02", times.GetCategory(0));

        var mixed = ChannelValues.FromObjects([1, "a", true, DayOfWeek.Monday, 2.5, new DateOnly(2024, 1, 2), new object(), float.NaN, Guid.Empty]);
        Assert.Equal(ChannelKind.Category, mixed.Kind);
        Assert.Equal(["1", "a", "true", "Monday", "2.5", "2024-01-02", "System.Object", null, "00000000-0000-0000-0000-000000000000"], mixed.Categories);
        Assert.True(double.IsNaN(mixed.GetNumber(0)));
        Assert.Same(mixed, mixed.ToCategorical());
        Assert.Equal(["1", null], ChannelValues.FromNumbers([1, double.NaN]).ToCategorical().Categories);
        Assert.Null(ChannelValues.FromNumbers([double.NaN], ChannelKind.Time).GetCategory(0));
        Assert.Equal(["2", "1"], ChannelValues.FromObjects(["1", "2"]).Select([1, 0]).Categories);
        Assert.Equal(ChannelKind.Number, ChannelValues.FromObjects([null, null]).Kind);
        Assert.Equal("2,024", NumberFormats.Default(2024));
        Assert.Equal("2024", ChannelValues.FromObjects([2024]).GetCategory(0));
    }

    [Fact]
    public void MarkData_SelectAndGroupBy()
    {
        var data = new MarkData(3)
        {
            X = ChannelValues.FromNumbers([1, 2, 3]),
            Text = ["a", "b", "c"],
            Titles = ["x", "y", "z"],
            State = 1,
        };
        data.Extra["e"] = ChannelValues.FromNumbers([7, 8, 9]);
        data.ExtraX.Add(ChannelValues.FromNumbers([0]));
        var selected = data.Select([2, 0]);
        Assert.Equal([3, 1], selected.X!.Numbers);
        Assert.Equal(["c", "a"], selected.Text);
        Assert.Equal(["z", "x"], selected.Titles);
        Assert.Equal([9, 7], selected.Extra["e"].Numbers);
        Assert.Single(selected.ExtraX);

        Assert.Equal([[0, 2], [1]], MarkData.GroupBy(3, ChannelValues.FromCategories(["a", "b", "a"])));
        Assert.Equal([[0, 1, 2]], MarkData.GroupBy(3, key: null));
        Assert.Equal([[0, 1]], MarkData.GroupBy(2, ChannelValues.FromCategories([null, null])));
    }
}
