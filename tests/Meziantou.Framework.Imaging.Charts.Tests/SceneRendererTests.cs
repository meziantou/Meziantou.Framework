using Meziantou.Framework.Charts;
using Meziantou.Framework.Imaging.Charts.Internal;

namespace Meziantou.Framework.Imaging.Charts.Tests;

public sealed class SceneRendererTests
{
    private static readonly Rgba32 Red = new(255, 0, 0);
    private static readonly Rgba32 Blue = new(0, 0, 255);

    [Fact]
    public void RectanglesAreFilledAndStroked()
    {
        var scene = new ChartScene(40, 20, "sans-serif") { Background = ChartColor.White };
        scene.Root.Add(new SceneRectangle(0, 0, 20, 20) { Fill = (ChartColor)"#ff0000" });
        scene.Root.Add(new SceneRectangle(22, 2, 16, 16) { Fill = (ChartColor)"#0000ff", CornerRadius = 3, Stroke = (ChartColor)"#ff0000", StrokeWidth = 2 });
        scene.Root.Add(new SceneRectangle(20, 0, 2, 0) { Stroke = (ChartColor)"#00ff00" });
        scene.Root.Add(new SceneRectangle(0, 0, 0, 0) { Fill = (ChartColor)"#00ff00" });
        using var image = scene.ToImage();
        Assert.Equal(Red, image.Frames[0][10, 10]);
        Assert.Equal(Blue, image.Frames[0][30, 10]);
        Assert.Equal(Red, image.Frames[0][30, 2]);
        Assert.Equal(Colors.White, image.Frames[0][39, 0]);
    }

    [Fact]
    public void CirclesLinesAndPaths()
    {
        var scene = new ChartScene(60, 20, "sans-serif");
        scene.Root.Add(new SceneCircle(10, 10, 8) { Fill = (ChartColor)"#ff0000", Stroke = (ChartColor)"#0000ff" });
        scene.Root.Add(new SceneCircle(10, 10, 0) { Fill = (ChartColor)"#00ff00" });
        scene.Root.Add(new SceneLine(20, 10, 40, 10) { Stroke = (ChartColor)"#0000ff", StrokeWidth = 4, StrokeDashArray = [2, 2], LineCap = SceneLineCap.Square, LineJoin = SceneLineJoin.Bevel });
        scene.Root.Add(new SceneLine(20, 0, 40, 0));
        var path = new PathBuilder().MoveTo(42, 2).LineTo(58, 2).QuadraticTo(58, 18, 50, 18).CubicTo(45, 18, 42, 12, 42, 2).Close().Build();
        scene.Root.Add(new ScenePath(path) { Fill = (ChartColor)"#ff0000", Stroke = (ChartColor)"#0000ff", LineCap = SceneLineCap.Round, LineJoin = SceneLineJoin.Round });
        scene.Root.Add(new ScenePath(new PathBuilder().Build()) { Fill = (ChartColor)"#00ff00" });
        using var image = scene.ToImage();
        Assert.Equal(Red, image.Frames[0][10, 10]);
        Assert.Equal(Blue, image.Frames[0][21, 10]);
        Assert.Equal(Red, image.Frames[0][50, 8]);
    }

    [Fact]
    public void EvenOddRingHasAHole()
    {
        var scene = new ChartScene(40, 40, "sans-serif");
        var ring = new PathBuilder().AddCircle(20, 20, 18).AddCircle(20, 20, 8).Build();
        scene.Root.Add(new ScenePath(ring) { Fill = (ChartColor)"#ff0000", FillRule = SceneFillRule.EvenOdd });
        using var image = scene.ToImage();
        Assert.Equal(Red, image.Frames[0][20, 6]);
        Assert.Equal(0, image.Frames[0][20, 20].A);
    }

    [Fact]
    public void ArcsAreTranslatedOneToOne()
    {
        var scene = new ChartScene(40, 40, "sans-serif");
        var half = new PathBuilder().MoveTo(20, 2).ArcTo(18, 18, 0, largeArc: false, sweep: true, 20, 38).Close().Build();
        scene.Root.Add(new ScenePath(half) { Fill = (ChartColor)"#ff0000" });
        using var image = scene.ToImage();
        Assert.Equal(Red, image.Frames[0][30, 20]);
        Assert.Equal(0, image.Frames[0][10, 20].A);
    }

    [Fact]
    public void GradientsGoFromTheFirstStopToTheLast()
    {
        var scene = new ChartScene(100, 10, "sans-serif");
        scene.Root.Add(new SceneRectangle(0, 0, 100, 10) { Fill = new LinearGradientPaint(0, 0, 100, 0, [new SceneGradientStop(0, ChartColor.Parse("#ff0000")), new SceneGradientStop(1, ChartColor.Parse("#0000ff"))]) });
        using var image = scene.ToImage();
        Assert.True(image.Frames[0][1, 5].R > 240);
        Assert.True(image.Frames[0][98, 5].B > 240);
    }

    [Fact]
    public void GroupsApplyOpacityClipAndTransform()
    {
        var scene = new ChartScene(40, 40, "sans-serif");
        var faded = scene.Root.Add(new SceneGroup { Opacity = 0.5 });
        faded.Add(new SceneRectangle(0, 0, 10, 10) { Fill = (ChartColor)"#ff0000" });
        var hidden = scene.Root.Add(new SceneGroup { Opacity = 0 });
        hidden.Add(new SceneRectangle(0, 20, 10, 10) { Fill = (ChartColor)"#ff0000" });
        var clipped = scene.Root.Add(new SceneGroup { Clip = new ChartRectangle(20, 0, 5, 40), Transform = SceneTransform.Translate(10, 0) });
        clipped.Add(new SceneRectangle(5, 0, 20, 10) { Fill = (ChartColor)"#0000ff" });
        var rotated = scene.Root.Add(new SceneGroup { Transform = SceneTransform.Rotate(90, 30, 30) });
        rotated.Add(new SceneRectangle(30, 25, 10, 2) { Fill = (ChartColor)"#ff0000" });
        using var image = scene.ToImage();
        Assert.InRange(image.Frames[0][5, 5].A, 120, 135);
        Assert.Equal(0, image.Frames[0][5, 25].A);
        Assert.Equal(Blue, image.Frames[0][22, 5]);
        Assert.Equal(0, image.Frames[0][27, 5].A);
        Assert.Equal(Red, image.Frames[0][34, 35]);
    }

    [Fact]
    public void TextIsDrawnFromItsBaselineAndAnchor()
    {
        var scene = new ChartScene(200, 100, "sans-serif");
        scene.Root.Add(new SceneText(100, 30, "MMMM", new ChartFont(20, Bold: true), ChartColor.Black) { Anchor = SceneTextAnchor.Middle });
        scene.Root.Add(new SceneText(200, 60, "MMMM", new ChartFont(20), ChartColor.Black) { Anchor = SceneTextAnchor.End });
        scene.Root.Add(new SceneText(20, 95, "MM", new ChartFont(20), ChartColor.Black) { Rotation = -90 });
        scene.Root.Add(new SceneText(0, 0, string.Empty, new ChartFont(20), ChartColor.Black));
        using var image = scene.ToImage();

        // Centered on 100, with its baseline at 30
        var (left, top, right, bottom) = GetInkBounds(image, 0, 0, 200, 40);
        Assert.InRange((left + right) / 2.0, 98, 102);
        Assert.InRange(bottom, 28, 30);
        Assert.InRange(top, 14, 18);

        // Ends at the right edge
        (_, _, right, bottom) = GetInkBounds(image, 0, 40, 200, 62);
        Assert.InRange(right, 196, 199);
        Assert.InRange(bottom, 58, 60);

        // Rotated by -90 degrees around its anchor: it goes up from (20, 95), its glyphs on the left of x = 20
        (left, top, right, bottom) = GetInkBounds(image, 0, 62, 30, 100);
        Assert.InRange(right, 18, 20);
        Assert.InRange(bottom, 90, 95); // The side bearing of the first glyph
        Assert.True(left < right);
        Assert.True(top < 80);
    }

    private static (int Left, int Top, int Right, int Bottom) GetInkBounds(Image<Rgba32> image, int x0, int y0, int x1, int y1)
    {
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, -1, -1);
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (image.Frames[0][x, y].A > 128)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return (left, top, right, bottom);
    }

    [Fact]
    public void ScaleMultipliesThePixels()
    {
        var scene = new ChartScene(10, 10, "sans-serif") { Background = (ChartColor)"#0000ff" };
        scene.Root.Add(new SceneRectangle(0, 0, 5, 10) { Fill = (ChartColor)"#ff0000" });
        using var image = scene.ToImage(new ChartImageOptions { Scale = 3 });
        Assert.Equal((30, 30), (image.Width, image.Height));
        Assert.Equal(Red, image.Frames[0][14, 15]);
        Assert.Equal(Blue, image.Frames[0][16, 15]);
    }

    [Fact]
    public void ToVectorPathOfEmptyPathIsNull()
    {
        Assert.Null(SceneRenderer.ToVectorPath(new PathBuilder().Build()));
        Assert.NotNull(SceneRenderer.ToVectorPath(new PathBuilder().AddRectangle(0, 0, 1, 1).Build()));
    }
}
