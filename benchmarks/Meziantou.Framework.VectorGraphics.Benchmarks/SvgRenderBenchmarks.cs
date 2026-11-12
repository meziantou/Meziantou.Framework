using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.VectorGraphics.Benchmarks;

[MemoryDiagnoser]
public class SvgRenderBenchmarks
{
    private Scene _chart = null!;
    private Scene _path = null!;

    [GlobalSetup]
    public void Setup()
    {
        _chart = CreateChart();
        _path = CreatePath();
    }

    // A bar chart: grid lines, bars, labels and a line series, the elements a chart lays out
    [Benchmark]
    public string Chart() => SvgSceneRenderer.Render(_chart);

    // A path of many cubic segments, the shape of a smoothed series or of an outlined glyph
    [Benchmark]
    public string LargePath() => SvgSceneRenderer.Render(_path);

    internal static Scene CreateChart()
    {
        var scene = new Scene(800, 600, AfmTextMeasurer.Default.FontFamily) { Title = "Chart", Background = SceneColor.White };
        var grid = new SceneGroup { CssClass = "grid" };
        for (var i = 0; i <= 20; i++)
        {
            grid.Add(new SceneLine(50, 50 + (i * 25.123), 780, 50 + (i * 25.123)) { Stroke = (SceneColor)"#e5e7eb", StrokeDashArray = [2, 3] });
        }

        scene.Root.Add(grid);
        var bars = new SceneGroup { Transform = SceneTransform.Translate(50, 50) };
        for (var i = 0; i < 300; i++)
        {
            var height = 10 + (i * 7.3 % 450.7);
            bars.Add(new SceneRectangle(i * 2.41, 500 - height, 2.1, height) { Fill = (SceneColor)"#3b82f6", Stroke = (SceneColor)"#1d4ed8", StrokeWidth = 0.5, Tooltip = "Value " + i });
        }

        scene.Root.Add(bars);
        for (var i = 0; i < 100; i++)
        {
            scene.Root.Add(new SceneText(50 + (i * 7.31), 580.5, "Label " + i, new SceneFont(10), (SceneColor)"#374151") { Anchor = SceneTextAnchor.Middle, Rotation = -45 });
        }

        var builder = new PathBuilder().MoveTo(50, 300);
        for (var i = 1; i < 200; i++)
        {
            builder.LineTo(50 + (i * 3.651), 300 + (Math.Sin(i / 10.0) * 120.123456));
        }

        scene.Root.Add(new ScenePath(builder.Build()) { Fill = null, Stroke = (SceneColor)"#ef4444", StrokeWidth = 2 });
        return scene;
    }

    private static Scene CreatePath()
    {
        var scene = new Scene(1000, 1000, AfmTextMeasurer.Default.FontFamily);
        var builder = new PathBuilder().MoveTo(0, 500);
        for (var i = 0; i < 2000; i++)
        {
            var x = i * 0.5;
            builder.CubicTo(x + 0.1666, 500 + (Math.Sin(i * 0.1) * 300), x + 0.3333, 500 + (Math.Cos(i * 0.1) * 300), x + 0.5, 500 + (Math.Sin(i * 0.1 + 0.05) * 300));
        }

        builder.Close();
        scene.Root.Add(new ScenePath(builder.Build()) { Fill = (SceneColor)"#10b981" });
        return scene;
    }
}
