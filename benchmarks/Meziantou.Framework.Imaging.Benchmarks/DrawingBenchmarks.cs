using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Vector drawing on a 1024x768 canvas: large shapes, many small antialiased shapes (the edges of a text), gradients,
/// images, clips, layers, shadows, blurs and color filters.
/// </summary>
/// <remarks>
/// Every workload draws on the same opaque image again and again: what it costs does not depend on what the previous
/// iteration left, except for the translucent paints, which keep the image opaque.
/// </remarks>
[Config(typeof(WorkloadConfig))]
public class DrawingBenchmarks : IDisposable
{
    private const int Width = 1024;
    private const int Height = 768;

    private static readonly GradientStop[] Stops = [new(0, new Rgba64(65535, 0, 0, 65535)), new(0.3, new Rgba64(0, 65535, 0, 65535)), new(0.7, new Rgba64(0, 0, 65535, 32768)), new(1, new Rgba64(65535, 65535, 0, 65535))];

    private static readonly BlurFilter BoxBlurFilter = new(6);
    private static readonly BlurFilter WeightedBlurFilter = new(1.5);
    private static readonly DropShadowFilter ShadowFilter = new(new Rgba64(0, 0, 0, 40000), new PointD(6, 8), 8);

    // The 320x240 image scaled to 960x720, centered on the canvas, then rotated around the center of the canvas
    private static readonly AffineTransform RotatedImageTransform = AffineTransform.CreateScale(3, 3).Then(AffineTransform.CreateTranslation(32, 24)).Then(AffineTransform.CreateRotation(20, new PointD(512, 384)));

    private Image<Rgba32> _image = null!;
    private Image<Rgba64> _image64 = null!;
    private Image<Rgba32> _source = null!;
    private Image<Rgba32> _photo = null!;
    private VectorPath _clipPath = null!;
    private Brush _linear = null!;
    private Brush _vertical = null!;
    private Brush _horizontal = null!;
    private Brush _radial = null!;
    private Brush _sweep = null!;
    private FilterGraph _turbulence = null!;
    private PointD[] _dots = null!;
    private ImageBrush _repeatBrush = null!;
    private ImageBrush _reflectBrush = null!;

    [GlobalSetup]
    public void Setup()
    {
        _image = new Image<Rgba32>(Width, Height, new Rgba32(255, 255, 255));
        _image64 = new Image<Rgba64>(Width, Height, new Rgba64(65535, 65535, 65535, 65535));
        _source = new Image<Rgba32>(320, 240);
        _source.Draw(static canvas =>
        {
            canvas.Fill(new LinearGradientBrush(new PointD(0, 0), new PointD(320, 240), Stops));
            canvas.FillCircle(new Rgba32(20, 40, 200, 180), new PointD(160, 120), 90);
        });

        _photo = new Image<Rgba32>(2048, 1536);
        _photo.Draw(static canvas =>
        {
            canvas.Fill(new LinearGradientBrush(new PointD(0, 0), new PointD(2048, 1536), Stops));
            canvas.FillCircle(new Rgba32(20, 40, 200, 180), new PointD(1024, 768), 600);
        });

        _repeatBrush = new ImageBrush(_source.Frames[0]) { WrapMode = ImageWrapMode.Repeat, Transform = AffineTransform.CreateTranslation(-100, -50) };
        _reflectBrush = new ImageBrush(_source.Frames[0]) { WrapMode = ImageWrapMode.Reflect, Transform = AffineTransform.CreateTranslation(-100, -50) };
        _clipPath = new VectorPathBuilder().AddEllipse(new PointD(512, 384), 480, 340).Build();
        _linear = new LinearGradientBrush(new PointD(40, 30), new PointD(980, 700), Stops) { SpreadMode = GradientSpreadMode.Reflect };
        _vertical = new LinearGradientBrush(new PointD(0, 0), new PointD(0, Height), Stops);
        _horizontal = new LinearGradientBrush(new PointD(0, 0), new PointD(Width, 0), Stops);
        _radial = new RadialGradientBrush(new PointD(512, 384), 300, Stops) { SpreadMode = GradientSpreadMode.Repeat };
        _sweep = new SweepGradientBrush(new PointD(512, 384), Stops);
        _turbulence = new FilterGraph(new TurbulencePrimitive(FilterTurbulenceType.FractalNoise, 0.02, 0.03, 4, 7) { Subregion = new RectangleD(0, 0, 512, 384) });

        // The dots of a text: small shapes on a grid, at positions that are not whole pixels
        var dots = new List<PointD>();
        for (var y = 12.3; y < Height - 8; y += 15.7)
        {
            for (var x = 9.1; x < Width - 8; x += 11.3)
            {
                dots.Add(new PointD(x, y));
            }
        }

        _dots = [.. dots];
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        _image?.Dispose();
        _image64?.Dispose();
        _source?.Dispose();
        _photo?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark(Baseline = true)]
    [Workload("Opaque ellipse covering most of a 1024x768 Rgba32 canvas: fill")]
    public int FillLarge()
    {
        _image.Draw(static canvas => canvas.FillEllipse(new Rgba32(30, 120, 200), new PointD(512, 384), 500, 370));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Translucent ellipse covering most of a 1024x768 Rgba32 canvas: fill")]
    public int FillLargeTranslucent()
    {
        _image.Draw(static canvas => canvas.FillEllipse(new Rgba32(30, 120, 200, 100), new PointD(512, 384), 500, 370));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Translucent ellipse covering most of a 1024x768 Rgba32 canvas: fill, composited in linear sRGB")]
    public int FillLargeTranslucentLinear()
    {
        _image.Draw(static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static canvas => canvas.FillEllipse(new Rgba32(30, 120, 200, 100), new PointD(512, 384), 500, 370)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Opaque ellipse covering most of a 1024x768 Rgba64 canvas: fill")]
    public int FillLarge64()
    {
        _image64.Draw(static canvas => canvas.FillEllipse(new Rgba64(10000, 30000, 50000, 65535), new PointD(512, 384), 500, 370));
        return _image64.Width;
    }

    [Benchmark]
    [Workload("Translucent ellipse covering most of a 1024x768 Rgba64 canvas: fill")]
    public int FillLargeTranslucent64()
    {
        _image64.Draw(static canvas => canvas.FillEllipse(new Rgba64(10000, 30000, 50000, 25700), new PointD(512, 384), 500, 370));
        return _image64.Width;
    }

    [Benchmark]
    [Workload("4,300 small antialiased shapes (circles and rectangles of 3 to 5 pixels): fill")]
    public int FillSmallShapes()
    {
        _image.Draw(_dots, static (canvas, dots) =>
        {
            for (var i = 0; i < dots.Length; i++)
            {
                var dot = dots[i];
                if ((i & 1) == 0)
                {
                    canvas.FillCircle(new Rgba32(0, 0, 0), dot, 2.4);
                }
                else
                {
                    canvas.FillRectangle(new Rgba32(40, 40, 40), new RectangleD(dot.X - 1.7, dot.Y - 2.2, 3.6, 4.7));
                }
            }
        });
        return _image.Width;
    }

    [Benchmark]
    [Workload("Linear gradient with four stops, reflected: fill of the canvas")]
    public int FillLinearGradient()
    {
        _image.Draw(_linear, static (canvas, brush) => canvas.Fill(brush));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Vertical linear gradient with four stops: fill of the canvas")]
    public int FillVerticalGradient()
    {
        _image.Draw(_vertical, static (canvas, brush) => canvas.Fill(brush));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Horizontal linear gradient with four stops: fill of an ellipse covering most of the canvas")]
    public int FillHorizontalGradient()
    {
        _image.Draw(_horizontal, static (canvas, brush) => canvas.FillEllipse(brush, new PointD(512, 384), 500, 370));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Radial gradient with four stops, repeated: fill of the canvas")]
    public int FillRadialGradient()
    {
        _image.Draw(_radial, static (canvas, brush) => canvas.Fill(brush));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Sweep gradient with four stops: fill of the canvas")]
    public int FillSweepGradient()
    {
        _image.Draw(_sweep, static (canvas, brush) => canvas.Fill(brush));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Ellipse stroked 12 pixels wide with a linear gradient: rows of two short runs far apart")]
    public int StrokeGradient()
    {
        _image.Draw(_linear, static (canvas, brush) => canvas.StrokeEllipse(new Pen(brush, 12), new PointD(512, 384), 480, 350));
        return _image.Width;
    }

    [Benchmark]
    [Workload("320x240 Rgba32 image scaled to 1000x740, bilinear: draw")]
    public int DrawImage()
    {
        _image.Draw(_source.Frames[0], static (canvas, source) => canvas.DrawImage(source, new RectangleD(12, 14, 1000, 740)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgba32 image reduced to 512x384, smooth: draw")]
    public int DrawImageReduced()
    {
        _image.Draw(_photo.Frames[0], static (canvas, source) => canvas.DrawImage(source, new RectangleD(100, 80, 512, 384), ImageSampling.Smooth));
        return _image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgba32 image drawn at a whole-pixel position, covering the canvas")]
    public int DrawImageAtPosition()
    {
        _image.Draw(_photo.Frames[0], static (canvas, source) => canvas.DrawImage(source, new PointD(-512, -384)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("320x240 Rgba32 image scaled 3 times and rotated by 20 degrees, bilinear: draw")]
    public int DrawImageRotated()
    {
        _image.Draw(_source.Frames[0], static (canvas, source) => canvas.DrawImage(source, RotatedImageTransform));
        return _image.Width;
    }

    [Benchmark]
    [Workload("320x240 Rgba32 image scaled 3 times and rotated by 20 degrees, bicubic: draw")]
    public int DrawImageRotatedBicubic()
    {
        _image.Draw(_source.Frames[0], static (canvas, source) => canvas.DrawImage(source, RotatedImageTransform, ImageSampling.Bicubic));
        return _image.Width;
    }

    [Benchmark]
    [Workload("320x240 Rgba32 image brush repeated, at a whole-pixel negative offset: fill of the canvas")]
    public int FillImageBrushRepeat()
    {
        _image.Draw(_repeatBrush, static (canvas, brush) => canvas.Fill(brush));
        return _image.Width;
    }

    [Benchmark]
    [Workload("320x240 Rgba32 image brush reflected, at a whole-pixel negative offset: fill of the canvas")]
    public int FillImageBrushReflect()
    {
        _image.Draw(_reflectBrush, static (canvas, brush) => canvas.Fill(brush));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Clear of a 1024x768 Rgba32 canvas")]
    public int Clear()
    {
        _image.Draw(static canvas => canvas.Clear(new Rgba64(65535, 65535, 65535, 65535)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Rectangle clipped by an ellipse: fill")]
    public int FillClipped()
    {
        _image.Draw(_clipPath, static (canvas, path) => canvas.Clip(path, static canvas => canvas.FillRectangle(new Rgba32(200, 60, 20, 200), new RectangleD(0, 0, Width, Height))));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Layer at 50% opacity holding three shapes over a quarter of the canvas")]
    public int Layer()
    {
        _image.Draw(static canvas => canvas.Layer(0.5, static canvas =>
        {
            canvas.FillCircle(new Rgba32(200, 20, 20), new PointD(200, 200), 120);
            canvas.FillRectangle(new Rgba32(20, 200, 20), new RectangleD(150, 150, 250, 180));
            canvas.FillCircle(new Rgba32(20, 20, 200), new PointD(330, 300), 90);
        }));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Drop shadow, blur 8, of a rounded rectangle of 300x200")]
    public int DropShadow()
    {
        _image.Draw(static canvas => canvas.Shadow(new Rgba64(0, 0, 0, 40000), new PointD(6, 8), 8, static canvas => canvas.FillRoundedRectangle(new Rgba32(250, 250, 250), new RectangleD(100, 100, 300, 200), 16)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Blur 6 of a circle of radius 150")]
    public int Blur()
    {
        _image.Draw(static canvas => canvas.Blur(6, static canvas => canvas.FillCircle(new Rgba32(250, 120, 0), new PointD(600, 400), 150)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Blur 32 of a circle of radius 150")]
    public int BlurLarge()
    {
        _image.Draw(static canvas => canvas.Blur(32, static canvas => canvas.FillCircle(new Rgba32(250, 120, 0), new PointD(600, 400), 150)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Blur 1.5 of the whole 1024x768 canvas: blur of the backdrop")]
    public int BlurBackdrop()
    {
        _image.Draw(static canvas => canvas.BlurBackdrop(1.5));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Sepia color matrix over the canvas: filter of the backdrop")]
    public int FilterBackdrop()
    {
        _image.Draw(static canvas => canvas.FilterBackdrop(ColorMatrix.CreateSepia(0.4)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Blur filter 6 of a circle of radius 150: three box blurs per axis")]
    public int FilterBlurBoxes()
    {
        _image.Draw(static canvas => canvas.Filter(BoxBlurFilter, static canvas => canvas.FillCircle(new Rgba32(250, 120, 0), new PointD(600, 400), 150)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Blur filter 1.5 of a circle of radius 150: Gaussian weights")]
    public int FilterBlurWeights()
    {
        _image.Draw(static canvas => canvas.Filter(WeightedBlurFilter, static canvas => canvas.FillCircle(new Rgba32(250, 120, 0), new PointD(600, 400), 150)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Drop shadow filter, blur 8, of a rounded rectangle of 300x200")]
    public int FilterDropShadow()
    {
        _image.Draw(static canvas => canvas.Filter(ShadowFilter, static canvas => canvas.FillRoundedRectangle(new Rgba32(250, 250, 250), new RectangleD(100, 100, 300, 200), 16)));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Fractal noise of four octaves over 512x384 pixels: filter graph")]
    public int Turbulence()
    {
        _image.Draw(_turbulence, static (canvas, graph) => canvas.Filter(graph, static _ => { }));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Multiply blend of a large ellipse")]
    public int FillMultiply()
    {
        _image.Draw(static canvas => canvas.FillEllipse(new SolidBrush(new Rgba64(60000, 40000, 20000, 65535)) { BlendMode = BlendMode.Multiply }, new PointD(512, 384), 400, 300));
        return _image.Width;
    }
}
