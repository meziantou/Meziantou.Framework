using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Large draw calls on a 4096x4096 canvas with <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> from 1 to 8: the
/// rows of a shape are composited by several workers while the calling thread computes the coverages of the next ones.
/// </summary>
/// <remarks>The profile command runs them with the default number of workers, 8.</remarks>
[Config(typeof(WorkloadConfig))]
public class ParallelDrawingBenchmarks : IDisposable
{
    private const int Size = 4096;

    private static readonly GradientStop[] Stops = [new(0, new Rgba64(65535, 0, 0, 65535)), new(0.3, new Rgba64(0, 65535, 0, 65535)), new(0.7, new Rgba64(0, 0, 65535, 32768)), new(1, new Rgba64(65535, 65535, 0, 65535))];

    private Image<Rgba32> _image = null!;
    private Image<Rgba32> _photo = null!;
    private Brush _radial = null!;
    private AffineTransform _photoTransform;

    [Params(1, 2, 4, 8)]
    public int Workers { get; set; } = 8;

    [GlobalSetup]
    public void Setup()
    {
        _image = new Image<Rgba32>(Size, Size, new Rgba32(255, 255, 255), new ImageConfiguration { MaxDegreeOfParallelism = Workers });
        _photo = new Image<Rgba32>(1024, 768);
        _photo.Draw(static canvas =>
        {
            canvas.Fill(new LinearGradientBrush(new PointD(0, 0), new PointD(1024, 768), Stops));
            canvas.FillCircle(new Rgba32(20, 40, 200, 180), new PointD(512, 384), 300);
        });

        _radial = new RadialGradientBrush(new PointD(Size / 2, Size / 2), Size * 0.3, Stops) { SpreadMode = GradientSpreadMode.Repeat };
        _photoTransform = AffineTransform.CreateScale(Size / 1024.0, Size / 768.0).Then(AffineTransform.CreateRotation(15, new PointD(Size / 2, Size / 2)));
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        _image?.Dispose();
        _photo?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    [Workload("Translucent ellipse covering most of a 4096x4096 canvas")]
    public int FillTranslucentEllipse()
    {
        _image.Draw(static canvas => canvas.FillEllipse(new SolidBrush(new Rgba32(200, 40, 90, 140)), new PointD(Size / 2, Size / 2), Size * 0.48, Size * 0.45));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Repeating radial gradient over a 4096x4096 canvas")]
    public int FillRadialGradient()
    {
        _image.Draw(_radial, static (canvas, brush) => canvas.Fill(brush));
        return _image.Width;
    }

    [Benchmark]
    [Workload("1024x768 image scaled to the 4096x4096 canvas and turned by 15 degrees")]
    public int DrawTurnedImage()
    {
        _image.Draw(this, static (canvas, state) => canvas.DrawImage(state._photo.Frames[0], state._photoTransform));
        return _image.Width;
    }

    [Benchmark]
    [Workload("Layer at 50% opacity holding a translucent ellipse and a vertical gradient")]
    public int Layer()
    {
        _image.Draw(static canvas => canvas.Layer(0.5, static canvas =>
        {
            canvas.FillEllipse(new SolidBrush(new Rgba32(200, 40, 90, 140)), new PointD(Size / 2, Size / 2), Size * 0.48, Size * 0.45);
            canvas.FillRectangle(new LinearGradientBrush(new PointD(0, 0), new PointD(0, Size), Stops), new RectangleD(0, Size / 4, Size, Size / 2));
        }));
        return _image.Width;
    }

    [Benchmark]
    [Workload("40 px wide stroke of an ellipse across a 4096x4096 canvas")]
    public int StrokeWideEllipse()
    {
        _image.Draw(static canvas => canvas.StrokeEllipse(new Pen(new SolidBrush(new Rgba32(10, 100, 200, 200)), 40), new PointD(Size / 2, Size / 2), Size * 0.45, Size * 0.4));
        return _image.Width;
    }
}
