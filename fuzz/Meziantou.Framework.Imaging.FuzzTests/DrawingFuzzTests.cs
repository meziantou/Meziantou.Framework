using System.Diagnostics;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Tests;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Random drawings: any valid sequence of shapes, pens and brushes, with coordinates from a fraction of a pixel to the
/// largest ones the API accepts, on every pixel format, must complete and keep the invariants of drawing.
/// </summary>
/// <remarks>
/// <para>
/// A drawing has no documented failure for valid arguments, so any exception is a defect. The invariants do not depend
/// on what is drawn: the working storage is released and no lease is left; a whole-image call paints every frame exactly
/// like a call on that frame; the alpha of a pixel never decreases; a format without alpha has nothing else to keep.
/// </para>
/// <para>
/// The time to draw a shape follows the number of points where its edges cross each other, which the area of their
/// union depends on. One family of shapes has millions of them: a pen far wider than the bends of a long curve it
/// follows, whose stroke is thousands of wedges that all cross near the center of the bends. The generator keeps the
/// widest pens for lines and rectangles, so that a case stays a matter of milliseconds.
/// </para>
/// </remarks>
public sealed class DrawingFuzzTests
{
    private const double MaxCoordinate = 16777216;

    [Fact]
    public void RandomDrawingsCompleteAndKeepTheirInvariants()
    {
        var random = new FuzzRandom(FuzzSettings.Seed ^ 0x44726177696E6773UL);
        var stopwatch = Stopwatch.StartNew();
        var failures = new List<string>();
        var executed = 0;
        var painted = 0;
        var slowest = TimeSpan.Zero;
        var slowestSeed = 0UL;
        // A case draws several shapes several times: a third of the budget of the decoding campaigns
        var iterations = Math.Max(FuzzSettings.Iterations / 3, 1);
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            if (FuzzSettings.Duration is { } budget && stopwatch.Elapsed > budget)
                break;

            var caseSeed = random.NextUInt64();
            var started = stopwatch.Elapsed;
            var failure = RunWithWatchdog(caseSeed, out var changed);
            var elapsed = stopwatch.Elapsed - started;
            if (elapsed > slowest)
            {
                slowest = elapsed;
                slowestSeed = caseSeed;
            }

            executed++;
            painted += changed ? 1 : 0;
            if (failure is null)
                continue;

            failures.Add($"case seed {caseSeed}: {failure}");
            if (failures.Count >= 4)
                break;
        }

        TestContext.Current.TestOutputHelper?.WriteLine(FormattableString.Invariant($"drawing: {executed} cases (base seed {FuzzSettings.Seed}) in {stopwatch.Elapsed.TotalSeconds:F1} s, slowest {slowest.TotalMilliseconds:F0} ms (case seed {slowestSeed}); {painted} cases changed pixels"));
        if (failures.Count > 0)
            Assert.Fail($"{failures.Count} fuzz failure(s):{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failures)}");

        // The campaign must paint, not only draw outside the canvas
        Assert.True(painted > executed / 2, $"Only {painted} of {executed} cases changed a pixel.");
    }

    [Fact]
    public void TheOracleReportsExceptionsAndBrokenInvariants()
    {
        // The self-test of the oracle: a deliberate defect is reported
        Assert.Null(Check(12345, out _));
        Assert.StartsWith("exception: System.InvalidOperationException", CheckInvariants(PixelFormat.Rgba32, static (_, _) => throw new InvalidOperationException("defect"), out _), StringComparison.Ordinal);
        Assert.StartsWith("frame 1 differs", CheckInvariants(PixelFormat.Rgba32, static (canvas, frame) => canvas.FillRectangle(new SolidBrush(new Rgba32(1, 2, 3)), new Rectangle(frame, 0, 1, 1)), out _), StringComparison.Ordinal);
    }

    private static string? RunWithWatchdog(ulong seed, out bool changed)
    {
        var caseChanged = false;
        var task = Task.Run(() =>
        {
            var failure = Check(seed, out var result);
            caseChanged = result;
            return failure;
        });
        if (!task.Wait(FuzzSettings.CaseTimeout))
        {
            changed = false;
            return $"timeout after {FuzzSettings.CaseTimeout.TotalSeconds:F0} s";
        }

        changed = caseChanged;
        return task.Result;
    }

    /// <summary>Runs one random drawing, entirely determined by its seed.</summary>
    private static string? Check(ulong seed, out bool changed)
    {
        var random = new FuzzRandom(seed);
        var format = PixelFormats.All[random.Next(PixelFormats.All.Count)];

        // The commands are generated once and replayed on every frame, whatever the frame
        var commandSeed = random.NextUInt64();
        return CheckInvariants(format, (canvas, _) => Paint(canvas, new FuzzRandom(commandSeed)), out changed, random);
    }

    /// <summary>Draws on a whole image and frame by frame on a copy, and checks the invariants.</summary>
    private static string? CheckInvariants(PixelFormat format, Action<DrawingCanvasProxy, int> paint, out bool changed, FuzzRandom? random = null)
    {
        changed = false;
        // Without a generator (the self-test of the oracle): three frames of 8x8 pixels
        var width = random?.Next(1, 33) ?? 8;
        var height = random?.Next(1, 33) ?? 8;
        var frameCount = random?.Next(1, 4) ?? 3;
        random ??= new FuzzRandom(1);
        using var whole = CreateImage(format, width, height, frameCount, random.NextUInt64());
        using var separate = whole.Clone();
        var before = Enumerable.Range(0, frameCount).Select(i => DrawingTestSupport.ReadSamples(whole.Frames[i])).ToArray();
        var live = whole.Owner.Scope.LiveBytes;
        try
        {
            whole.Draw(canvas => paint(new DrawingCanvasProxy(canvas), 0));
            for (var i = 0; i < frameCount; i++)
            {
                var index = i;
                separate.Frames[i].Draw(canvas => paint(new DrawingCanvasProxy(canvas), index));
            }
        }
        catch (Exception exception)
        {
            return $"exception: {exception}";
        }

        if (whole.Owner.Scope.LiveBytes != live)
            return $"scratch not released: {whole.Owner.Scope.LiveBytes - live} bytes";

        if (whole.Owner.ActiveLeaseCount != 0 || separate.Owner.ActiveLeaseCount != 0)
            return "a lease is still active";

        var channels = PixelFormats.GetComponentCount(format);
        var hasAlpha = PixelFormats.HasAlpha(format);
        for (var i = 0; i < frameCount; i++)
        {
            var after = DrawingTestSupport.ReadSamples(whole.Frames[i]);
            if (!after.AsSpan().SequenceEqual(DrawingTestSupport.ReadSamples(separate.Frames[i])))
                return $"frame {i} differs between a whole-image call and a call on the frame";

            changed |= !after.AsSpan().SequenceEqual(before[i]);
            if (!hasAlpha)
                continue;

            for (var sample = channels - 1; sample < after.Length; sample += channels)
            {
                if (after[sample] < before[i][sample])
                    return $"frame {i}: the alpha of pixel {sample / channels} went from {before[i][sample]} to {after[sample]}";
            }
        }

        return null;
    }

    private static Image CreateImage(PixelFormat format, int width, int height, int frameCount, ulong seed)
    {
        var random = new FuzzRandom(seed);
        var image = DrawingTestSupport.CreateRandom(format, width, height, random.Next);
        for (var i = 1; i < frameCount; i++)
        {
            using var other = DrawingTestSupport.CreateRandom(format, width, height, random.Next);
            image.AppendFrame(other.Frames[0]);
        }

        return image;
    }

    private static void Paint(DrawingCanvasProxy proxy, FuzzRandom random)
    {
        var canvas = proxy.Canvas;
        var count = random.Next(1, 7);
        for (var i = 0; i < count; i++)
        {
            switch (random.Next(11))
            {
                case 0:
                    canvas.Fill(CreateBrush(random, canvas));
                    break;
                case 1:
                    canvas.Fill(CreateBrush(random, canvas), CreatePath(random, canvas), random.Chance(50) ? FillRule.EvenOdd : FillRule.NonZero);
                    break;
                case 2:
                    canvas.Stroke(CreatePen(random, canvas, widest: false), CreatePath(random, canvas));
                    break;
                case 3:
                    canvas.FillRectangle(CreateBrush(random, canvas), CreateRectangle(random, canvas));
                    break;
                case 4:
                    canvas.StrokeRectangle(CreatePen(random, canvas, widest: true), CreateRectangle(random, canvas));
                    break;
                case 5:
                    canvas.FillEllipse(CreateBrush(random, canvas), CreatePoint(random, canvas), CreateLength(random), CreateLength(random));
                    break;
                case 6:
                    canvas.StrokeEllipse(CreatePen(random, canvas, widest: false), CreatePoint(random, canvas), CreateLength(random), CreateLength(random));
                    break;
                case 7:
                    canvas.FillCircle(CreateBrush(random, canvas), CreatePoint(random, canvas), CreateLength(random));
                    break;
                case 8:
                    canvas.StrokeCircle(CreatePen(random, canvas, widest: false), CreatePoint(random, canvas), CreateLength(random));
                    break;
                case 9:
                    canvas.StrokeLine(CreatePen(random, canvas, widest: true), CreatePoint(random, canvas), CreatePoint(random, canvas));
                    break;
                default:
                    if (random.Chance(50))
                    {
                        canvas.StrokeBezier(CreatePen(random, canvas, widest: false), CreatePoint(random, canvas), CreatePoint(random, canvas), CreatePoint(random, canvas));
                    }
                    else
                    {
                        canvas.StrokeBezier(CreatePen(random, canvas, widest: false), CreatePoint(random, canvas), CreatePoint(random, canvas), CreatePoint(random, canvas), CreatePoint(random, canvas));
                    }

                    break;
            }
        }
    }

    private static double NextDouble(FuzzRandom random) => (random.NextUInt64() >> 11) / 9007199254740992d;

    /// <summary>A coordinate: mostly around the canvas, sometimes on a pixel boundary, sometimes far away, sometimes an extreme.</summary>
    private static double CreateCoordinate(FuzzRandom random, int size) => random.Next(20) switch
    {
        0 => random.Chance(50) ? MaxCoordinate : -MaxCoordinate,
        1 => (NextDouble(random) - 0.5) * 2 * MaxCoordinate,
        2 => (NextDouble(random) - 0.5) * 4000,
        3 or 4 or 5 => random.Next(-2, size + 3),
        6 => random.Next(-8, (size * 4) + 9) / 4.0,
        7 => 0,
        _ => (NextDouble(random) * (size + 6)) - 3,
    };

    private static PointD CreatePoint(FuzzRandom random, DrawingCanvas canvas) => new(CreateCoordinate(random, canvas.Width), CreateCoordinate(random, canvas.Height));

    private static RectangleD CreateRectangle(FuzzRandom random, DrawingCanvas canvas)
    {
        var a = CreatePoint(random, canvas);
        var b = random.Chance(10) ? a : CreatePoint(random, canvas);
        return RectangleD.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    private static double CreateLength(FuzzRandom random) => random.Next(12) switch
    {
        0 => 0,
        1 => MaxCoordinate,
        2 => NextDouble(random) * 5000,
        3 => NextDouble(random) * 1e-6,
        _ => NextDouble(random) * 20,
    };

    private static Rgba64 CreateColor(FuzzRandom random)
    {
        var alpha = random.Next(5) switch
        {
            0 => ushort.MaxValue,
            1 => (ushort)0,
            2 => (ushort)random.Next(1, 700),
            _ => (ushort)random.Next(65536),
        };
        return new Rgba64((ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536), alpha);
    }

    private static Brush CreateBrush(FuzzRandom random, DrawingCanvas canvas)
    {
        if (random.Chance(40))
            return new SolidBrush(CreateColor(random));

        var offsets = new double[random.Next(1, 7)];
        for (var i = 0; i < offsets.Length; i++)
        {
            offsets[i] = random.Chance(30) ? random.Next(5) / 4.0 : NextDouble(random);
        }

        Array.Sort(offsets);
        var stops = offsets.Select(offset => new GradientStop(offset, CreateColor(random))).ToArray();
        var spreadMode = (GradientSpreadMode)random.Next(3);
        return random.Chance(50)
            ? new LinearGradientBrush(CreatePoint(random, canvas), random.Chance(10) ? default : CreatePoint(random, canvas), stops) { SpreadMode = spreadMode }
            : new RadialGradientBrush(CreatePoint(random, canvas), CreateLength(random), stops) { SpreadMode = spreadMode };
    }

    private static Pen CreatePen(FuzzRandom random, DrawingCanvas canvas, bool widest)
    {
        var width = random.Next(10) switch
        {
            0 => widest ? MaxCoordinate : 1 + (NextDouble(random) * 100),
            1 => 1e-9 + (NextDouble(random) * 1e-3),
            2 => 1 + (NextDouble(random) * (widest ? 100000 : 100)),
            _ => 0.05 + (NextDouble(random) * 6),
        };
        return new Pen(CreateBrush(random, canvas), width)
        {
            LineJoin = (LineJoin)random.Next(3),
            LineCap = (LineCap)random.Next(3),
            MiterLimit = random.Chance(20) ? 1 + (NextDouble(random) * 1000) : 1 + (NextDouble(random) * 9),
        };
    }

    private static VectorPath CreatePath(FuzzRandom random, DrawingCanvas canvas)
    {
        var builder = new VectorPathBuilder();
        var elements = random.Next(0, 16);
        var started = false;
        for (var i = 0; i < elements; i++)
        {
            var kind = started ? random.Next(11) : random.Next(5);
            switch (kind)
            {
                case 0:
                    builder.MoveTo(CreatePoint(random, canvas));
                    started = true;
                    break;
                case 1:
                    builder.AddRectangle(CreateRectangle(random, canvas));
                    started = true;
                    break;
                case 2:
                    builder.AddEllipse(CreatePoint(random, canvas), CreateLength(random), CreateLength(random));
                    started = true;
                    break;
                case 3:
                    builder.AddCircle(CreatePoint(random, canvas), CreateLength(random));
                    started = true;
                    break;
                case 4:
                    var points = new PointD[random.Next(1, 7)];
                    for (var j = 0; j < points.Length; j++)
                    {
                        points[j] = j > 0 && random.Chance(15) ? points[j - 1] : CreatePoint(random, canvas);
                    }

                    builder.AddPolygon(points);
                    started = true;
                    break;
                case 5 or 6 or 7:
                    builder.LineTo(CreatePoint(random, canvas));
                    break;
                case 8:
                    builder.QuadraticBezierTo(CreatePoint(random, canvas), CreatePoint(random, canvas));
                    break;
                case 9:
                    builder.CubicBezierTo(CreatePoint(random, canvas), CreatePoint(random, canvas), CreatePoint(random, canvas));
                    break;
                default:
                    builder.Close();
                    break;
            }
        }

        return builder.Build();
    }

    /// <summary>Carries a canvas, which is a ref struct, to a delegate that also takes the index of the frame.</summary>
    private readonly ref struct DrawingCanvasProxy(DrawingCanvas canvas)
    {
        public DrawingCanvas Canvas { get; } = canvas;

        public void FillRectangle(Brush brush, RectangleD rectangle) => Canvas.FillRectangle(brush, rectangle);
    }
}
