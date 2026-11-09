using System.Globalization;
using Meziantou.Framework.Charts;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Imaging.VectorGraphics.Tests;

public sealed class ChartImageTests
{
    private static readonly Fruit[] Fruits = [new("Apples", 12), new("Bananas", 7), new("Cherries", 18), new("Dates", 4), new("Figs", 9)];

    [Fact]
    public void ToImage()
    {
        #region ChartImage_ToImage
        Fruit[] fruits = [new("Apples", 12), new("Bananas", 7), new("Cherries", 18)];
        var plot = new Plot { Title = "Fruits sold", Width = 400, Height = 250 };
        plot.Add(new BarMark<Fruit>(fruits, x: fruit => fruit.Name, y: fruit => fruit.Count));

        using var image = plot.ToImage();
        Assert.Equal((400, 250), (image.Width, image.Height));

        // Save to a file (image.Save("fruits.png")) or to a stream, in any format
        using var png = new MemoryStream();
        image.Save(png, new PngEncoder());

        // Twice the pixels, for high-density screens
        using var retina = plot.ToImage(new SceneImageOptions { Scale = 2 });
        Assert.Equal((800, 500), (retina.Width, retina.Height));
        #endregion

        Snapshot.Validate(ToPng(image), SnapshotType.Png);
        ChartDocumentationImages.AssertImage("FruitsSold", image);
    }

    [Fact]
    public void DrawChart()
    {
        #region ChartImage_DrawOnCanvas
        using var report = new Image<Rgba32>(500, 300, Colors.White); // or Image.Load<Rgba32>("report.png")
        var plot = new Plot { Width = 300, Height = 200, Theme = ChartTheme.Light with { Background = null } };
        plot.Add(new ArcMark<Fruit>([new("Apples", 12), new("Bananas", 7)], value: fruit => fruit.Count) { Fill = fruit => fruit.Name });

        var size = SceneImageRenderer.MeasureScene(plot);
        Assert.Equal((300, 200), (size.Width, size.Height));

        // Drawn as vector shapes and text: the transform and the clip of the canvas apply
        report.Draw(canvas => canvas.DrawScene(plot, new PointD(180, 80)));
        #endregion

        Assert.Equal(Colors.White, report.Frames[0][10, 10]);
        Snapshot.Validate(ToPng(report), SnapshotType.Png);
    }

    [Fact]
    public void Cartesian_LineAndArea()
    {
        var plot = new Plot { Title = "Line and area", Width = 480, Height = 300 };
        plot.Add(new AreaMark<int>(Enumerable.Range(0, 24), x: i => i, y: i => 5 + (3 * Math.Sin(i / 3.0))) { FillOpacity = 0.3, Curve = Curve.MonotoneX });
        plot.Add(new LineMark<int>(Enumerable.Range(0, 24), x: i => i, y: i => 5 + (3 * Math.Sin(i / 3.0))) { Curve = Curve.MonotoneX, MarkerRadius = 2.5 });
        plot.Add(new RuleYMark(5) { StrokeDashArray = [4, 4], StrokeColor = "#e5484d" });
        plot.XAxis.Label = "Hour";
        plot.YAxis.Label = "Load";
        AssertImage(plot);
    }

    [Fact]
    public void Radial_DonutWithLabels()
    {
        var plot = new Plot { Title = "Donut", Width = 320, Height = 320, Coordinates = new PolarCoordinates { InnerRadius = 0.55 } };
        plot.Add(new ArcMark<Fruit>(Fruits, value: fruit => fruit.Count) { Fill = fruit => fruit.Name, Label = fruit => fruit.Name });
        plot.Legend.Visible = false;
        AssertImage(plot);
    }

    [Fact]
    public void Radial_Radar()
    {
        var plot = new Plot { Width = 320, Height = 320, Coordinates = new PolarCoordinates(), XScale = new PointScale(), YScale = new LinearScale { Minimum = 0 } };
        plot.Add(new AreaMark<Fruit>(Fruits, x: fruit => fruit.Name, y: fruit => fruit.Count) { Closed = true, FillOpacity = 0.3, StrokeColor = "#4269d0" });
        AssertImage(plot);
    }

    [Fact]
    public void Hierarchy_Treemap()
    {
        var root = Hierarchy.FromPaths(["a/x", "a/y", "a/z", "b/u", "b/v", "c"], path => path, value: path => path.Length + (path[^1] - 'a')).SortByValue();
        var plot = new Plot { Width = 360, Height = 240 };
        plot.Add(new TreemapMark<string?>(root) { PaddingTop = 16, PaddingOuter = 2 });
        AssertImage(plot);
    }

    [Fact]
    public void Hierarchy_PackedCircles()
    {
        var root = Hierarchy.FromPaths(["a/x", "a/y", "a/z", "b/u", "b/v", "c"], path => path, value: path => 1 + (path[^1] - 'a')).SortByValue();
        var plot = new Plot { Width = 300, Height = 300 };
        plot.Add(new PackMark<string?>(root));
        plot.Legend.Visible = false;
        AssertImage(plot);
    }

    [Fact]
    public void Statistical_HeatmapWithGradientLegend()
    {
        var cells = Enumerable.Range(0, 6).SelectMany(row => Enumerable.Range(0, 8).Select(column => (Row: "r" + row.ToString(CultureInfo.InvariantCulture), Column: "c" + column.ToString(CultureInfo.InvariantCulture), Value: (double)(row * column))));
        var plot = new Plot { Width = 400, Height = 300 };
        plot.Add(new RectMark<(string Row, string Column, double Value)>(cells) { X = cell => cell.Column, Y = cell => cell.Row, Fill = cell => cell.Value, CornerRadius = 2 });
        plot.Legend.Title = "Value";
        AssertImage(plot);
    }

    [Fact]
    public void FacetsSankeyAndTrees()
    {
        var facets = new Plot { Width = 420, Height = 220 };
        facets.Add(new BarMark<Fruit>(Fruits, x: fruit => fruit.Name, y: fruit => fruit.Count) { FacetX = fruit => fruit.Count > 8 ? "Many" : "Few" });
        facets.Add(new LineMark<Fruit>(Fruits, x: fruit => fruit.Name, y: fruit => fruit.Count * 3) { FacetX = fruit => fruit.Count > 8 ? "Many" : "Few", UseSecondaryYScale = true, StrokeColor = "#e5484d" });
        AssertImage(facets, memberName: nameof(FacetsSankeyAndTrees) + "_Facets");

        var sankey = new Plot { Width = 360, Height = 220 };
        sankey.Add(new SankeyMark<(string Source, string Target, double Value)>([("A", "C", 5), ("B", "C", 3), ("C", "D", 6), ("C", "E", 2)], source: link => link.Source, target: link => link.Target, value: link => link.Value));
        AssertImage(sankey, memberName: nameof(FacetsSankeyAndTrees) + "_Sankey");

        var tree = new Plot { Width = 300, Height = 300, Coordinates = new PolarCoordinates() };
        tree.Add(new TreeMark<string?>(Hierarchy.FromPaths(["a/x", "a/y", "b/z", "c"], path => path)) { Layout = TreeLayoutKind.Cluster });
        tree.Legend.Visible = false;
        AssertImage(tree, memberName: nameof(FacetsSankeyAndTrees) + "_Tree");
    }

    [Fact]
    public void Clipping()
    {
        var plot = new Plot { Width = 300, Height = 200, Clip = true, YScale = new LinearScale { Minimum = 0, Maximum = 5 } };
        plot.Add(new BarMark<Fruit>(Fruits, x: fruit => fruit.Name, y: fruit => fruit.Count));
        AssertImage(plot);
    }

    [Fact]
    public void RotatedLabels()
    {
        var plot = new Plot { Width = 300, Height = 260 };
        plot.Add(new BarMark<Fruit>(Fruits, x: fruit => fruit.Name + " from the market", y: fruit => fruit.Count));
        AssertImage(plot);
    }

    [Fact]
    public void DoubleScaleAndDarkTheme()
    {
        var plot = new Plot { Title = "2x", Width = 240, Height = 160, Theme = ChartTheme.Dark };
        plot.Add(new DotMark<Fruit>(Fruits, x: fruit => fruit.Name, y: fruit => fruit.Count) { Symbol = DotSymbol.Diamond, Radius = 5, StrokeColor = SceneColor.White });
        using var image = plot.ToImage(new SceneImageOptions { Scale = 2 });
        Assert.Equal((480, 320), (image.Width, image.Height));
        Snapshot.Validate(ToPng(image), SnapshotType.Png);
    }

    [Fact]
    public void CustomFonts()
    {
        var options = new SceneImageOptions { Font = SceneFonts.SansBold, BoldFont = SceneFonts.Sans };

        var plot = new Plot { Title = "Bold body, regular title", Width = 300, Height = 160 };
        plot.Add(new BarMark<Fruit>(Fruits, x: fruit => fruit.Name, y: fruit => fruit.Count));
        using var image = plot.ToImage(options);
        Snapshot.Validate(ToPng(image), SnapshotType.Png);
    }

    [Fact]
    public void SceneIsLaidOutWithTheMeasurerOfTheOptions()
    {
        var plot = new Plot { Width = 300, Height = 200 };
        plot.Add(new BarMark<Fruit>(Fruits, x: fruit => fruit.Name, y: fruit => fruit.Count));
        var scene = plot.CreateScene(SceneImageRenderer.CreateMeasurer());
        Assert.Equal("Liberation Sans", scene.FontFamily);

        using var image = scene.ToImage();
        using var fromPlot = plot.ToImage();
        Assert.Equal(ToPng(fromPlot), ToPng(image));

        using var canvasImage = new Image<Rgba32>(320, 220, Colors.Transparent);
        var bounds = default(RectangleD);
        canvasImage.Draw(canvas => bounds = canvas.DrawScene(scene, new PointD(10, 10), new SceneImageOptions()));
        Assert.Equal(new RectangleD(10, 10, 300, 200), bounds);
    }

    [Fact]
    public void LiberationSansHasTheWidthsOfHelvetica()
    {
        var image = SceneImageRenderer.CreateMeasurer();
        var afm = AfmTextMeasurer.Default;
        // Kerning differs: Helvetica kerns pairs such as AV more than Liberation Sans, so the texts have few kerned pairs
        foreach (var text in new[] { "Sales by region", "0123456789", "Height (cm)", "The quick brown fox jumps over the lazy dog" })
        {
            var expected = afm.Measure(text, new SceneFont(12)).Width;
            var actual = image.Measure(text, new SceneFont(12)).Width;
            Assert.True(Math.Abs(expected - actual) <= expected * 0.02, $"{text}: {expected} vs {actual}");

            var expectedBold = afm.Measure(text, new SceneFont(16, Bold: true)).Width;
            var actualBold = image.Measure(text, new SceneFont(16, Bold: true)).Width;
            Assert.True(Math.Abs(expectedBold - actualBold) <= expectedBold * 0.02, $"{text}: {expectedBold} vs {actualBold}");
        }

        Assert.Equal(0, image.Measure(string.Empty, new SceneFont(12)).Width);
        Assert.True(image.Measure("x", new SceneFont(12)).Ascent > 0);
    }

    [Fact]
    public void InvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SceneImageOptions { Scale = 0 });
        Assert.Throws<ArgumentNullException>("source", () => ((Plot)null!).ToImage());
        Assert.Throws<ArgumentNullException>("scene", () => ((Scene)null!).ToImage());
        Assert.Throws<ArgumentNullException>("source", () => SceneImageRenderer.MeasureScene((ISceneSource)null!));
        Assert.Throws<ArgumentNullException>("scene", () => SceneImageRenderer.MeasureScene((Scene)null!));
        Assert.Throws<ArgumentNullException>("regular", () => new ImageTextMeasurer(null!));
        Assert.Throws<ArgumentNullException>("text", () => SceneImageRenderer.CreateMeasurer().Measure(null!, new SceneFont(12)));
    }

    internal static byte[] ToPng(Image<Rgba32> image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    private static void AssertImage(Plot plot, [System.Runtime.CompilerServices.CallerFilePath] string? filePath = null, [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = -1, [System.Runtime.CompilerServices.CallerMemberName] string? memberName = null)
    {
        using var image = plot.ToImage();
        Snapshot.Validate(ToPng(image), SnapshotType.Png, settings: null, filePath, lineNumber, memberName);
    }
}
