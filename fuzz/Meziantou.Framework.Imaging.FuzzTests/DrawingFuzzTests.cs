using System.Diagnostics;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Tests;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Random drawings: any valid sequence of shapes, pens and brushes, with coordinates from a fraction of a pixel to the
/// largest ones the API accepts, on every pixel format but CMYK, must complete and keep the invariants of drawing.
/// </summary>
/// <remarks>
/// <para>
/// A drawing has no documented failure for valid arguments, so any exception is a defect. The invariants do not depend
/// on what is drawn: the working storage is released and no lease is left; a whole-image call paints every frame exactly
/// like a call on that frame; the alpha of a pixel never decreases, unless the case uses the operators that erase
/// (a third of the cases); a format without alpha has nothing else to keep.
/// </para>
/// <para>
/// Two failures are documented for valid arguments, and expected here: an arc or a transformed path that goes beyond
/// the largest coordinate is rejected when it is built. The generator catches that exception there, and nowhere else.
/// </para>
/// <para>
/// A third one depends on what was drawn before: a floating-point canvas keeps the samples that color matrices and
/// additive composites put outside [0, 1], and what reads its pixels back as 16-bit colors (filters, blurs, backdrops,
/// masks, groups) rejects them. That rejection is expected on those formats only; the working storage and the leases
/// must still be released, but the pixels are not compared, since the failing call may have painted some rows.
/// </para>
/// <para>
/// The time to draw a shape follows the number of points where its edges cross each other, which the area of their
/// union depends on. One family of shapes has millions of them: a pen far wider than the bends of a long curve it
/// follows, whose stroke is thousands of wedges that all cross near the center of the bends. The generator keeps the
/// widest pens for lines and rectangles, dashed ones included, so that a case stays a matter of milliseconds.
/// </para>
/// <para>
/// In perspective, a curve is cut finer wherever the outline of its stroke comes near the horizon of the plane, which a
/// filtered layer can bring into view (ProjectiveView.GetCurveScale): a pen 84 pixels wide on a circle of 14 pixels was
/// 195,000 crossing edges, for minutes, while the pen reached the horizon from every part of the circle (fuzz
/// regression). The round and square caps of dashes can end anywhere on a curve, and reach as far as the pen along it:
/// there, the dashed pens of these caps are at most about 20 pixels wide, like the lengths are at most 60.
/// </para>
/// </remarks>
public sealed class DrawingFuzzTests
{
    private const double MaxCoordinate = 16777216;

    // The CMYK formats are not drawn on: a drawing session rejects them
    private static readonly PixelFormat[] DrawableFormats = [.. PixelFormats.All.Where(format => !PixelFormats.IsCmyk(format))];

    // In perspective, coordinates and lengths of the size of the canvas: a shape of 2^24 pixels whose far end is drawn
    // at the vanishing point of the canvas is millions of chords and dashes there, which the limits of the image refuse
    [ThreadStatic]
    private static bool s_isInPerspective;

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

    [Fact]
    public void WholePixelRectanglesPaintLikeTheirPolygons()
    {
        // A rectangle of whole pixels is not swept (CoverageRasterizer.TryGetWholePixelRectangle): the same rectangle as a
        // polygon with one more corner on its left side is (the left side is never dropped, unlike a right side beyond the
        // canvas), and must paint the same pixels, whatever the paint, the operator, the clip, the transformation, the
        // antialiasing and the number of workers
        var random = new FuzzRandom(FuzzSettings.Seed ^ 0x52656374616E676CUL);
        var stopwatch = Stopwatch.StartNew();
        var iterations = Math.Max(FuzzSettings.Iterations / 10, 1);
        var failures = new List<string>();
        var executed = 0;
        for (var iteration = 0; iteration < iterations && failures.Count < 4; iteration++)
        {
            if (FuzzSettings.Duration is { } budget && stopwatch.Elapsed > budget)
                break;

            var caseSeed = random.NextUInt64();
            executed++;
            var failure = RunWithWatchdog(() => CheckWholePixelRectangle(caseSeed));
            if (failure is not null)
            {
                failures.Add($"case seed {caseSeed}: {failure}");
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(FormattableString.Invariant($"whole-pixel rectangles: {executed} cases (base seed {FuzzSettings.Seed}) in {stopwatch.Elapsed.TotalSeconds:F1} s"));
        if (failures.Count > 0)
            Assert.Fail($"{failures.Count} fuzz failure(s):{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failures)}");
    }

    [Fact]
    public void StrokesPaintLikeTheirSeparatePieces()
    {
        // The pieces of a stroke that go to edges share the halves of their sides around each vertex, which cancel
        // (EdgeBuilder.CancelOpposites), and the round joins of a curve that the bodies of its chords contain are not made
        // (Stroker.CanDeferSmoothJoin). The union of the pieces is the same: the pixels must be those of the pieces kept
        // apart, whatever the path, the pen, its dashes, the transformation and the antialiasing.
        var random = new FuzzRandom(FuzzSettings.Seed ^ 0x5374726F6B655069UL);
        var stopwatch = Stopwatch.StartNew();
        var iterations = Math.Max(FuzzSettings.Iterations / 5, 1);
        var failures = new List<string>();
        var executed = 0;
        for (var iteration = 0; iteration < iterations && failures.Count < 4; iteration++)
        {
            if (FuzzSettings.Duration is { } budget && stopwatch.Elapsed > budget)
                break;

            var caseSeed = random.NextUInt64();
            executed++;
            var failure = RunWithWatchdog(() => CheckStrokeAgainstSeparatePieces(caseSeed));
            if (failure is not null)
            {
                failures.Add($"case seed {caseSeed}: {failure}");
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(FormattableString.Invariant($"strokes and their separate pieces: {executed} cases (base seed {FuzzSettings.Seed}) in {stopwatch.Elapsed.TotalSeconds:F1} s"));
        if (failures.Count > 0)
            Assert.Fail($"{failures.Count} fuzz failure(s):{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failures)}");
    }

    private static string? CheckStrokeAgainstSeparatePieces(ulong seed)
    {
        var random = new FuzzRandom(seed);
        var width = random.Next(1, 48);
        var height = random.Next(1, 48);
        using var sprite = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 2, 2, random.Next);
        var context = new FuzzContext(sprite.Frames[0], Erases: false);
        using var merged = DrawingTestSupport.Create(PixelFormat.Rgba32, width, height, new ImageConfiguration { MaxDegreeOfParallelism = 1 });
        using var separate = DrawingTestSupport.Create(PixelFormat.Rgba32, width, height, new ImageConfiguration { MaxDegreeOfParallelism = 1 });
        var drawSeed = random.NextUInt64();
        try
        {
            merged.Draw(canvas => Draw(canvas, new FuzzRandom(drawSeed), context));
            Stroker.KeepsPiecesApartOnCurrentThread = true;
            try
            {
                separate.Draw(canvas => Draw(canvas, new FuzzRandom(drawSeed), context));
            }
            finally
            {
                Stroker.KeepsPiecesApartOnCurrentThread = false;
            }
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or UnsupportedImageFeatureException or ImageResourceLimitException)
        {
            // The stroke goes beyond the largest coordinate, what the session rejects, or edges beyond the allocation limit
            // (the pieces kept apart have more of them)
            return null;
        }
        catch (Exception exception)
        {
            return $"exception: {exception}";
        }

        var expected = DrawingTestSupport.ReadSamples(separate.Frames[0]);
        var actual = DrawingTestSupport.ReadSamples(merged.Frames[0]);
        if (expected.AsSpan().SequenceEqual(actual))
            return null;

        var differences = 0;
        var largest = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
            {
                differences++;
                largest = Math.Max(largest, Math.Abs(expected[i] - actual[i]));
            }
        }

        return FormattableString.Invariant($"the stroke and its separate pieces differ on {differences} samples (at most {largest}) in {width}x{height}");

        static void Draw(DrawingCanvas canvas, FuzzRandom random, FuzzContext context)
        {
            // The pens far wider than the canvas cost seconds a case, twice: their strokes are fuzzed by Paint
            var pen = random.Chance(25) ? CreateDashedPen(random, canvas, context) : CreatePen(random, canvas, context, widest: false);
            if (pen.Width > 200)
            {
                pen = CreatePen(random, canvas, context, widest: false);
            }

            var path = random.Chance(30) ? CreatePolyline(random, canvas) : CreatePath(random, canvas);
            var antialiasing = !random.Chance(20);
            var transform = random.Chance(30) ? CreateTransform(random, canvas) : (AffineTransform?)null;
            if (transform is { } shrinking && GetSmallestScale(shrinking) is var scale and < 1)
            {
                // A scaling by 1e-4, or by 0, brings a path of millions of pixels a few pixels long on the canvas, or onto
                // a line, with every piece of its stroke: kept apart, the thousands of pieces of a curve of 2^24 pixels all
                // cross each other there, which took minutes (fuzz regression). A polyline is a few pieces, however long.
                if (scale < 1e-3)
                {
                    path = CreatePolyline(random, canvas);
                }

                pen = KeepDashLengthsOnTheCanvas(pen, scale);
            }

            canvas.Antialiasing(antialiasing, canvas =>
            {
                if (transform is { } value)
                {
                    canvas.Transform(value, canvas => canvas.Stroke(pen, path));
                }
                else
                {
                    canvas.Stroke(pen, path);
                }
            });
        }
    }

    /// <summary>Gets the smallest factor by which a transformation scales a length: its smallest singular value, 0 when it is singular.</summary>
    private static double GetSmallestScale(AffineTransform transform)
    {
        var sum = (transform.M11 * transform.M11) + (transform.M12 * transform.M12) + (transform.M21 * transform.M21) + (transform.M22 * transform.M22);
        var determinant = transform.Determinant;
        var largest = Math.Sqrt((sum + Math.Sqrt(Math.Max((sum * sum) - (4 * determinant * determinant), 0))) / 2);
        return largest > 0 ? Math.Abs(determinant) / largest : 0;
    }

    /// <summary>
    /// Lengthens the dashes of a pen that a transformation of the canvas shrinks by <paramref name="scale"/>. The pen
    /// measures its dashes before the transformation: a scaling by 1e-4 makes them a million slivers on the canvas, which
    /// all cross each other (fuzz regression: minutes a case). As <see cref="CreateDashedPen"/> keeps them without a
    /// transformation, the dashes are at least as long on the canvas as in the pattern, within the largest length a
    /// pattern accepts.
    /// </summary>
    private static Pen KeepDashLengthsOnTheCanvas(Pen pen, double scale)
    {
        if (pen.DashPattern.IsEmpty)
            return pen;

        var pattern = pen.DashPattern.ToArray();
        var factor = Math.Min(1 / scale, MaxCoordinate / pattern.Max());
        for (var i = 0; i < pattern.Length; i++)
        {
            pattern[i] = Math.Min(pattern[i] * factor, MaxCoordinate);
        }

        return new Pen(pen.Brush, pen.Width)
        {
            LineJoin = pen.LineJoin,
            LineCap = pen.LineCap,
            MiterLimit = pen.MiterLimit,
            DashPattern = pattern,
            DashOffset = pen.DashOffset,
        };
    }

    [Fact]
    public void TheSweepSkipsOnlyWhatCannotChange()
    {
        // After the events of a height, the sweep passes quickly over the edges whose winding number did not change and
        // computes again only the crossings of the pairs of neighbors that are new or that cross
        // (CoverageRasterizer.WalksEveryEdge): the coverages must be those of the walk that updates every edge and every
        // pair it passes, bit for bit. The pieces of boundary of the rows, accumulated apart as the workers of a large shape
        // do (CoverageRasterizer.RecordPieces), with a limit that sends some rows back to the sweep, must give them too. The
        // contours are on grids of few heights, as the glyphs of a line of text share
        // their baseline and their x-height, or anywhere, crossing each other or not
        var random = new FuzzRandom(FuzzSettings.Seed ^ 0x5377656570577BCAUL);
        var stopwatch = Stopwatch.StartNew();
        var iterations = Math.Max(FuzzSettings.Iterations / 10, 1);
        var failures = new List<string>();
        var executed = 0;
        for (var iteration = 0; iteration < iterations && failures.Count < 4; iteration++)
        {
            if (FuzzSettings.Duration is { } budget && stopwatch.Elapsed > budget)
                break;

            var caseSeed = random.NextUInt64();
            executed++;
            var failure = RunWithWatchdog(() => CheckSweepWalk(caseSeed));
            if (failure is not null)
            {
                failures.Add($"case seed {caseSeed}: {failure}");
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(FormattableString.Invariant($"sweep walks: {executed} cases (base seed {FuzzSettings.Seed}) in {stopwatch.Elapsed.TotalSeconds:F1} s"));
        if (failures.Count > 0)
            Assert.Fail($"{failures.Count} fuzz failure(s):{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failures)}");
    }

    private static string? CheckSweepWalk(ulong seed)
    {
        var random = new FuzzRandom(seed);
        var width = random.Next(1, 200);
        var height = random.Next(1, 60);
        var fillRule = random.Chance(50) ? FillRule.NonZero : FillRule.EvenOdd;
        var isAliased = random.Chance(10);

        // 0: a row of small contours on a few heights in eighths of a pixel, as glyphs; 1: anywhere in the frame;
        // 2: anywhere, beyond the frame too
        var kind = random.Next(3);
        var levels = new double[random.Next(2, 6)];
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = random.Next(-8, (height * 8) + 8) / 8.0;
        }

        var contours = new List<(double X, double Y)[]>();
        for (var count = random.Next(1, 40); count > 0; count--)
        {
            var points = new (double X, double Y)[random.Next(3, 9)];
            var left = random.Next(-4, width + 4);
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = kind switch
                {
                    0 => (left + (random.Next(0, 80) / 8.0), levels[random.Next(levels.Length)]),
                    1 => (random.Next(0, width * 64) / 64.0, random.Next(0, height * 64) / 64.0),
                    _ => ((random.NextUInt64() % 1_000_000 / 1_000_000.0 * (width + 40)) - 20, (random.NextUInt64() % 1_000_000 / 1_000_000.0 * (height + 40)) - 20),
                };
            }

            contours.Add(points);
        }

        var pieceLimit = random.Chance(50) ? CoverageRasterizer.MaxPiecesPerRow : random.Next(0, 12);
        var exhaustive = Rasterize(contours, width, height, fillRule, isAliased, walksEveryEdge: true, pieceLimit: -1);
        var skipping = Rasterize(contours, width, height, fillRule, isAliased, walksEveryEdge: false, pieceLimit: -1);
        var pieces = Rasterize(contours, width, height, fillRule, isAliased, walksEveryEdge: false, pieceLimit);
        return Compare(exhaustive, skipping, "the skipping walk") ?? Compare(exhaustive, pieces, FormattableString.Invariant($"the pieces with a limit of {pieceLimit}"));

        string? Compare(List<string> expected, List<string> actual, string name)
        {
            if (expected.Count != actual.Count)
                return $"{name}: {actual.Count} runs instead of {expected.Count} ({width}x{height}, {fillRule})";

            for (var i = 0; i < expected.Count; i++)
            {
                if (expected[i] != actual[i])
                    return $"{name}: run {i} differs: {actual[i]} instead of {expected[i]} ({width}x{height}, {fillRule})";
            }

            return null;
        }

        // A piece limit of -1 gives the coverages of the sweep
        static List<string> Rasterize(List<(double X, double Y)[]> contours, int width, int height, FillRule fillRule, bool isAliased, bool walksEveryEdge, int pieceLimit)
        {
            var scope = AllocationScope.Create(configuration: null);
            using var edges = new EdgeBuilder(scope, width, height);
            foreach (var points in contours)
            {
                for (var i = 0; i < points.Length; i++)
                {
                    var next = points[(i + 1) % points.Length];
                    edges.Add(points[i].X, points[i].Y, next.X, next.Y);
                }
            }

            using var rasterizer = new CoverageRasterizer(scope, width, height) { WalksEveryEdge = walksEveryEdge };
            var runs = new List<string>();
            if (!rasterizer.Begin(edges, fillRule, isAliased, CancellationToken.None))
                return runs;

            if (pieceLimit >= 0)
            {
                rasterizer.PieceLimit = pieceLimit;
                rasterizer.RecordPieces();
            }

            var accumulation = new double[width + 2];
            var coverage = new uint[width];
            while (rasterizer.MoveNext())
            {
                if (!rasterizer.HasPieces)
                {
                    runs.Add(FormattableString.Invariant($"{rasterizer.Y}:{rasterizer.X}:{string.Join(',', rasterizer.Coverage.ToArray())}"));
                }
                else if (CoverageRasterizer.ReplayPieces(rasterizer.Pieces, accumulation, coverage, width, out var x, out var count))
                {
                    runs.Add(FormattableString.Invariant($"{rasterizer.Y}:{x}:{string.Join(',', coverage.AsSpan(x, count).ToArray())}"));
                }
            }

            return runs;
        }
    }

    private static string? CheckWholePixelRectangle(ulong seed)
    {
        var random = new FuzzRandom(seed);
        var format = DrawableFormats[random.Next(DrawableFormats.Length)];
        var erases = random.Chance(33);

        // Large enough, sometimes, for the rows to be composited by several workers
        var isLarge = random.Chance(15);
        var width = isLarge ? random.Next(380, 520) : random.Next(1, 40);
        var height = isLarge ? random.Next(300, 400) : random.Next(1, 40);
        var configuration = new ImageConfiguration { MaxDegreeOfParallelism = random.Chance(50) ? 1 : random.Next(2, 5) };
        using var sprite = DrawingTestSupport.CreateRandom(DrawableFormats[random.Next(DrawableFormats.Length)], random.Next(1, 9), random.Next(1, 9), random.Next);
        using var source = DrawingTestSupport.CreateRandom(format, width, height, random.Next);
        var samples = DrawingTestSupport.ReadSamples(source.Frames[0]);
        using var rectangle = DrawingTestSupport.Create(format, width, height, configuration);
        using var polygon = DrawingTestSupport.Create(format, width, height, configuration);
        DrawingTestSupport.WriteSamples(rectangle.Frames[0], samples);
        DrawingTestSupport.WriteSamples(polygon.Frames[0], samples);

        // Several frames receive every row of the rectangle: the rows that copy the first one are copied on each frame
        var hasSecondFrame = random.Chance(20);
        if (hasSecondFrame)
        {
            rectangle.AppendFrame();
            polygon.AppendFrame();
        }

        var context = new FuzzContext(sprite.Frames[0], erases);
        var drawSeed = random.NextUInt64();
        try
        {
            rectangle.Draw(canvas => Draw(canvas, new FuzzRandom(drawSeed), context, asPolygon: false));
            polygon.Draw(canvas => Draw(canvas, new FuzzRandom(drawSeed), context, asPolygon: true));
        }
        catch (UnsupportedImageFeatureException exception) when (PixelFormats.IsFloatingPoint(format) && exception.Feature == DrawingKernels.OutOfRangeColorFeature)
        {
            return null;
        }
        catch (Exception exception)
        {
            return $"exception: {exception}";
        }

        for (var i = 0; i < rectangle.Frames.Count; i++)
        {
            if (!DrawingTestSupport.ReadSamples(rectangle.Frames[i]).AsSpan().SequenceEqual(DrawingTestSupport.ReadSamples(polygon.Frames[i])))
                return $"the rectangle and the polygon differ on frame {i} of {format} {width}x{height}";
        }

        return null;

        static void Draw(DrawingCanvas canvas, FuzzRandom random, FuzzContext context, bool asPolygon)
        {
            var left = random.Next(-5, canvas.Width + 5);
            var top = random.Next(-5, canvas.Height + 5);
            var right = left + random.Next(0, canvas.Width + 5);
            var bottom = top + random.Next(0, canvas.Height + 5);
            // An opaque color, or a color that replaces the pixels: the first row of a rectangle is copied to the others
            // (DrawingSession.TryPaintUniformRectangle)
            var brush = random.Chance(25)
                ? new SolidBrush(CreateColor(random) with { A = random.Chance(70) ? ushort.MaxValue : (ushort)random.Next(65536) }) { CompositeMode = random.Chance(50) ? CompositeMode.SourceOver : CompositeMode.Source }
                : CreateBrush(random, canvas, context);
            var antialiasing = !random.Chance(30);
            var clip = random.Next(3) switch
            {
                0 => (RectangleD?)CreateRectangle(random, canvas),
                1 => CreateWholeRectangle(random, canvas),
                _ => null,
            };
            var translation = random.Chance(30) ? AffineTransform.CreateTranslation(random.Next(-4, 5), random.Next(-4, 5)) : (AffineTransform?)null;
            canvas.Antialiasing(antialiasing, canvas =>
            {
                if (clip is { } bounds)
                {
                    canvas.Clip(bounds, canvas => Fill(canvas));
                }
                else
                {
                    Fill(canvas);
                }
            });

            void Fill(DrawingCanvas canvas)
            {
                if (translation is { } transform)
                {
                    canvas.Transform(transform, canvas => FillShape(canvas));
                }
                else
                {
                    FillShape(canvas);
                }
            }

            void FillShape(DrawingCanvas canvas)
            {
                if (asPolygon)
                {
                    // One more corner, halfway up the left side (a whole or a half pixel, exact either way)
                    canvas.FillPolygon(brush, [new PointD(left, top), new PointD(right, top), new PointD(right, bottom), new PointD(left, bottom), new PointD(left, (top + bottom) / 2.0)]);
                }
                else
                {
                    canvas.FillRectangle(brush, new RectangleD(left, top, right - left, bottom - top));
                }
            }
        }
    }

    private static string? RunWithWatchdog(ulong seed, out bool changed)
    {
        var caseChanged = false;
        var outcome = RunWithWatchdog(() =>
        {
            var failure = Check(seed, out var result);
            caseChanged = result;
            return failure;
        });
        changed = caseChanged;
        return outcome;
    }

    /// <summary>
    /// Runs one case on another thread: a case that never ends is reported as a failure of its seed, instead of blocking
    /// the campaign until the hang dump kills the test host (the case keeps running, the campaign stops after a few failures).
    /// </summary>
    private static string? RunWithWatchdog(Func<string?> check)
    {
        var task = Task.Run(check);
        if (!task.Wait(FuzzSettings.CaseTimeout))
            return $"timeout after {FuzzSettings.CaseTimeout.TotalSeconds:F0} s";

        return task.Result;
    }

    /// <summary>Runs one random drawing, entirely determined by its seed.</summary>
    private static string? Check(ulong seed, out bool changed)
    {
        var random = new FuzzRandom(seed);
        var format = DrawableFormats[random.Next(DrawableFormats.Length)];

        // The commands are generated once and replayed on every frame, whatever the frame
        var commandSeed = random.NextUInt64();

        // A case out of three uses the operators that erase and replace pixels: the alpha of a pixel may then decrease
        var erases = random.Chance(33);

        // The image some drawings paint with: any pixel format, never the image that is drawn on
        using var sprite = DrawingTestSupport.CreateRandom(DrawableFormats[random.Next(DrawableFormats.Length)], random.Next(1, 9), random.Next(1, 9), random.Next);
        var context = new FuzzContext(sprite.Frames[0], erases);
        return CheckInvariants(format, (canvas, _) => Paint(canvas.Canvas, new FuzzRandom(commandSeed), context, depth: 0), out changed, random, checkAlpha: !erases);
    }

    /// <summary>Draws on a whole image and frame by frame on a copy, and checks the invariants.</summary>
    private static string? CheckInvariants(PixelFormat format, Action<DrawingCanvasProxy, int> paint, out bool changed, FuzzRandom? random = null, bool checkAlpha = true)
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
        var rejectsOutOfRangeSamples = false;
        try
        {
            whole.Draw(canvas => paint(new DrawingCanvasProxy(canvas), 0));
            for (var i = 0; i < frameCount; i++)
            {
                var index = i;
                separate.Frames[i].Draw(canvas => paint(new DrawingCanvasProxy(canvas), index));
            }
        }
        catch (UnsupportedImageFeatureException exception) when (PixelFormats.IsFloatingPoint(format) && exception.Feature == DrawingKernels.OutOfRangeColorFeature)
        {
            rejectsOutOfRangeSamples = true;
        }
        catch (Exception exception)
        {
            return $"exception: {exception}";
        }

        if (whole.Owner.Scope.LiveBytes != live)
            return $"scratch not released: {whole.Owner.Scope.LiveBytes - live} bytes";

        if (whole.Owner.ActiveLeaseCount != 0 || separate.Owner.ActiveLeaseCount != 0)
            return "a lease is still active";

        if (rejectsOutOfRangeSamples)
            return null;

        var hasAlpha = PixelFormats.HasAlpha(format);
        for (var i = 0; i < frameCount; i++)
        {
            var after = DrawingTestSupport.ReadSamples(whole.Frames[i]);
            if (!after.AsSpan().SequenceEqual(DrawingTestSupport.ReadSamples(separate.Frames[i])))
                return $"frame {i} differs between a whole-image call and a call on the frame";

            changed |= !after.AsSpan().SequenceEqual(before[i]);
            if (!hasAlpha || !checkAlpha)
                continue;

            var alphasBefore = GetAlphas(format, before[i]);
            var alphasAfter = GetAlphas(format, after);
            for (var pixel = 0; pixel < alphasAfter.Length; pixel++)
            {
                if (alphasAfter[pixel] < alphasBefore[pixel])
                    return $"frame {i}: the alpha of pixel {pixel} went from {alphasBefore[pixel]} to {alphasAfter[pixel]}";
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the alpha of each pixel from the samples of <see cref="DrawingTestSupport.ReadSamples"/>: the last sample of a
    /// pixel, or, for the interoperability formats, whose samples are their bytes, the alpha stored in them.
    /// </summary>
    private static int[] GetAlphas(PixelFormat format, int[] samples)
    {
        var size = DrawingTestSupport.IsInteropFormat(format) ? PixelFormats.GetBytesPerPixel(format) : PixelFormats.GetComponentCount(format);
        var alphas = new int[samples.Length / size];
        for (var pixel = 0; pixel < alphas.Length; pixel++)
        {
            var offset = pixel * size;
            alphas[pixel] = format switch
            {
                // Alpha is the first byte of both layouts
                PixelFormat.Argb32 or PixelFormat.Abgr32 => samples[offset],

                // Alpha is the 2 high bits of the native-endian packed value
                PixelFormat.Rgba1010102 => (int)(BitConverter.ToUInt32([(byte)samples[offset], (byte)samples[offset + 1], (byte)samples[offset + 2], (byte)samples[offset + 3]]) >> 30),
                _ => samples[offset + size - 1],
            };
        }

        return alphas;
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

    private static void Paint(DrawingCanvas canvas, FuzzRandom random, FuzzContext context, int depth)
    {
        var count = random.Next(1, 7);
        for (var i = 0; i < count; i++)
        {
            switch (random.Next(24))
            {
                case 0:
                    canvas.Fill(CreateBrush(random, canvas, context));
                    break;
                case 1:
                    canvas.Fill(CreateBrush(random, canvas, context), CreatePath(random, canvas), random.Chance(50) ? FillRule.EvenOdd : FillRule.NonZero);
                    break;
                case 2:
                    canvas.Stroke(CreatePen(random, canvas, context, widest: false), CreatePath(random, canvas));
                    break;
                case 3:
                    canvas.FillRectangle(CreateBrush(random, canvas, context), CreateRectangle(random, canvas));
                    break;
                case 4:
                    canvas.StrokeRectangle(CreatePen(random, canvas, context, widest: true), CreateRectangle(random, canvas));
                    break;
                case 5:
                    canvas.FillEllipse(CreateBrush(random, canvas, context), CreatePoint(random, canvas), CreateLength(random), CreateLength(random));
                    break;
                case 6:
                    canvas.StrokeEllipse(CreatePen(random, canvas, context, widest: false), CreatePoint(random, canvas), CreateLength(random), CreateLength(random));
                    break;
                case 7:
                    canvas.FillCircle(CreateBrush(random, canvas, context), CreatePoint(random, canvas), CreateLength(random));
                    break;
                case 8:
                    canvas.StrokeCircle(CreatePen(random, canvas, context, widest: false), CreatePoint(random, canvas), CreateLength(random));
                    break;
                case 9:
                    canvas.StrokeLine(CreatePen(random, canvas, context, widest: true), CreatePoint(random, canvas), CreatePoint(random, canvas));
                    break;
                case 10:
                    if (random.Chance(50))
                    {
                        canvas.StrokeBezier(CreatePen(random, canvas, context, widest: false), CreatePoint(random, canvas), CreatePoint(random, canvas), CreatePoint(random, canvas));
                    }
                    else
                    {
                        canvas.StrokeBezier(CreatePen(random, canvas, context, widest: false), CreatePoint(random, canvas), CreatePoint(random, canvas), CreatePoint(random, canvas), CreatePoint(random, canvas));
                    }

                    break;
                case 11:
                    canvas.FillRoundedRectangle(CreateBrush(random, canvas, context), CreateRectangle(random, canvas), CreateLength(random));
                    break;
                case 12:
                    canvas.StrokeRoundedRectangle(CreatePen(random, canvas, context, widest: false), CreateRectangle(random, canvas), CreateLength(random));
                    break;
                case 13:
                    canvas.FillPolygon(CreateBrush(random, canvas, context), CreatePoints(random, canvas), random.Chance(50) ? FillRule.EvenOdd : FillRule.NonZero);
                    break;
                case 14:
                    if (random.Chance(50))
                    {
                        canvas.StrokePolygon(CreatePen(random, canvas, context, widest: true), CreatePoints(random, canvas));
                    }
                    else
                    {
                        canvas.StrokePolyline(CreatePen(random, canvas, context, widest: true), CreatePoints(random, canvas));
                    }

                    break;
                case 15:
                    canvas.FillPie(CreateBrush(random, canvas, context), CreatePoint(random, canvas), CreateLength(random), CreateAngle(random), CreateAngle(random));
                    break;
                case 16:
                    canvas.StrokeArc(CreatePen(random, canvas, context, widest: false), CreatePoint(random, canvas), CreateLength(random), CreateAngle(random), CreateAngle(random));
                    break;
                case 17:
                    // The drawings of a clip are other drawings; the clips are closed in every case, and three deep at most
                    if (depth >= 3)
                    {
                        canvas.Fill(CreateBrush(random, canvas, context));
                    }
                    else if (random.Chance(50))
                    {
                        canvas.Clip(random.Chance(50) ? CreateRectangle(random, canvas) : CreateWholeRectangle(random, canvas), inner => Paint(inner, random, context, depth + 1));
                    }
                    else
                    {
                        canvas.Clip(CreatePath(random, canvas), inner => Paint(inner, random, context, depth + 1), random.Chance(50) ? FillRule.EvenOdd : FillRule.NonZero);
                    }

                    break;
                case 18:
                    switch (random.Next(4))
                    {
                        case 3:
                            {
                                // A rectangle of the sprite that is not made of whole pixels
                                var left = NextDouble(random) * context.Sprite.Width;
                                var top = NextDouble(random) * context.Sprite.Height;
                                var region = new RectangleD(left, top, NextDouble(random) * (context.Sprite.Width - left), NextDouble(random) * (context.Sprite.Height - top));
                                canvas.DrawImage(context.Sprite, region, CreateRectangle(random, canvas), (ImageSampling)random.Next(4), CreateOpacity(random));
                                break;
                            }

                        case 0:
                            canvas.DrawImage(context.Sprite, random.Chance(50) ? CreatePoint(random, canvas) : new PointD(random.Next(-4, canvas.Width + 2), random.Next(-4, canvas.Height + 2)), CreateOpacity(random));
                            break;
                        case 1:
                            canvas.DrawImage(context.Sprite, CreateRectangle(random, canvas), CreateOpacity(random));
                            break;
                        default:
                            var x = random.Next(context.Sprite.Width);
                            var y = random.Next(context.Sprite.Height);
                            canvas.DrawImage(context.Sprite, new Rectangle(x, y, random.Next(0, context.Sprite.Width - x + 1), random.Next(0, context.Sprite.Height - y + 1)), CreateRectangle(random, canvas), CreateOpacity(random));
                            break;
                    }

                    break;
                case 19:
                    if (context.Erases)
                    {
                        canvas.Clear(CreateColor(random));
                    }
                    else
                    {
                        canvas.Fill(CreateBrush(random, canvas, context));
                    }

                    break;
                case 20:
                    Measure(canvas, random, context);
                    break;
                case 22:
                    // A filtered drawing, a blurred drawing, its shadow or a blurred backdrop, a layer that holds the
                    // pixels of a rectangle, a scope of 16-bit samples, or a layer that starts with the filtered backdrop
                    // of a rectangle
                    if (depth >= 3)
                    {
                        canvas.Fill(CreateBrush(random, canvas, context));
                    }
                    else if (random.Chance(20))
                    {
                        // Sometimes wide enough that more than 32 rows add up to each row of the result. Blurring the
                        // backdrop replaces the pixels: only the cases that erase may lower an alpha
                        var blur = random.Chance(20) ? 5 + (NextDouble(random) * 4) : NextDouble(random) * 3;
                        switch (random.Next(context.Erases ? 3 : 2))
                        {
                            case 0:
                                canvas.Blur(blur, inner => Paint(inner, random, context, depth + 1));
                                break;
                            case 1:
                                canvas.Shadow(CreateColor(random), new PointD((NextDouble(random) - 0.5) * 8, (NextDouble(random) - 0.5) * 8), blur, inner => Paint(inner, random, context, depth + 1));
                                break;
                            default:
                                canvas.BlurBackdrop(blur);
                                break;
                        }
                    }
                    else if (random.Chance(40))
                    {
                        canvas.Filter(CreateFilter(random), inner => Paint(inner, random, context, depth + 1));
                    }
                    else if (random.Chance(40))
                    {
                        canvas.Layer(CreateRectangle(random, canvas), CreateOpacity(random), inner => Paint(inner, random, context, depth + 1), CreateCompositeMode(random, context), (BlendMode)random.Next(16));
                    }
                    else if (random.Chance(30))
                    {
                        canvas.HighPrecision(inner => Paint(inner, random, context, depth + 1));
                    }
                    else
                    {
                        canvas.BackdropLayer(random.Chance(10) ? null : CreateFilter(random), CreateRectangle(random, canvas), CreateOpacity(random), inner => Paint(inner, random, context, depth + 1));
                    }

                    break;
                case 21:
                    // A plane seen in perspective: what crosses its horizon is cut, nothing is rejected
                    if (depth >= 3)
                    {
                        canvas.Fill(CreateBrush(random, canvas, context));
                    }
                    else if (random.Chance(50))
                    {
                        var transform = CreateProjectiveTransform(random, canvas);
                        PaintInPerspective(inner => inner.Transform(transform, plane => Paint(plane, random, context, depth + 1)), canvas);
                    }
                    else
                    {
                        var center = Transform3D.CreateTranslation(-canvas.Width / 2.0, -canvas.Height / 2.0, 0);
                        var back = Transform3D.CreateTranslation(canvas.Width / 2.0, canvas.Height / 2.0, 0);
                        // Not a multiple of half a turn, which makes the transformation affine (it rejects what it sends
                        // beyond the largest coordinate), and not near a quarter turn, which shows the plane from its
                        // edge: what is long in the plane is then a few pixels long on the canvas, and a dashed path
                        // millions of dashes
                        var angle = 1 + (NextDouble(random) * 74);
                        angle = random.Chance(50) ? angle : -angle;
                        var turn = random.Chance(50) ? Transform3D.CreateRotationX(angle) : Transform3D.CreateRotationY(angle);
                        var perspective = center.Then(Transform3D.CreatePerspective(10 + (NextDouble(random) * 100))).Then(back);
                        PaintInPerspective(outer => outer.Transform(perspective, viewer => viewer.Transform(center.Then(turn).Then(back), plane => Paint(plane, random, context, depth + 1))), canvas);
                    }

                    break;
                default:
                    {
                        // A pen of the largest width follows lines only (see the remarks): along an ellipse of 2^24 by 14
                        // pixels, no piece of its stroke covers a canvas near the center alone, and the area of their
                        // union took over a minute (fuzz regression)
                        var pen = CreateDashedPen(random, canvas, context);
                        canvas.Stroke(pen, pen.Width >= MaxCoordinate ? CreatePolyline(random, canvas) : CreatePath(random, canvas));
                        break;
                    }
            }
        }
    }

    private static void PaintInPerspective(DrawingAction action, DrawingCanvas canvas)
    {
        var previous = s_isInPerspective;
        s_isInPerspective = true;
        try
        {
            action(canvas);
        }
        finally
        {
            s_isInPerspective = previous;
        }
    }

    /// <summary>Asks a random path what a drawing asks about a shape, and draws its stroke as a path.</summary>
    private static void Measure(DrawingCanvas canvas, FuzzRandom random, FuzzContext context)
    {
        var path = CreatePath(random, canvas);
        _ = path.Contains(CreatePoint(random, canvas), random.Chance(50) ? FillRule.EvenOdd : FillRule.NonZero);
        var length = path.GetLength();
        if (!(length >= 0) || double.IsInfinity(length))
            throw new InvalidOperationException($"The length of a path is {length}.");

        if (path.TryGetPointAtDistance((NextDouble(random) * 1.5 * length) - (0.25 * length), out var point, out var tangent))
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs((tangent.X * tangent.X) + (tangent.Y * tangent.Y) - 1) > 1e-9)
                throw new InvalidOperationException($"The point at a distance is {point}, with the direction {tangent}.");
        }

        var bounds = path.Bounds;
        var tight = path.GetTightBounds();
        var slack = 1e-6 * (1 + Math.Abs(bounds.Left) + Math.Abs(bounds.Top) + Math.Abs(bounds.Right) + Math.Abs(bounds.Bottom));
        if (!(tight.Left >= bounds.Left - slack && tight.Top >= bounds.Top - slack && tight.Right <= bounds.Right + slack && tight.Bottom <= bounds.Bottom + slack))
            throw new InvalidOperationException($"The tight bounds {tight} are not inside the bounds {bounds}.");

        // The stroke as a path, when it is not too large to keep: of a path around the canvas, without dashes
        if (!(bounds.Width <= 500 && bounds.Height <= 500))
            return;

        var pen = new Pen(CreateBrush(random, canvas, context), 0.25 + (NextDouble(random) * 8)) { LineJoin = (LineJoin)random.Next(3), LineCap = (LineCap)random.Next(3) };
        try
        {
            path = path.Widen(pen);
        }
        catch (ArgumentOutOfRangeException)
        {
            // The stroke goes beyond the largest coordinate
            return;
        }

        canvas.Fill(pen.Brush, path);
    }

    private static double NextDouble(FuzzRandom random) => (random.NextUInt64() >> 11) / 9007199254740992d;

    /// <summary>A coordinate: mostly around the canvas, sometimes on a pixel boundary, sometimes far away, sometimes an extreme.</summary>
    private static double CreateCoordinate(FuzzRandom random, int size) => random.Next(20) switch
    {
        0 or 1 or 2 when s_isInPerspective => (NextDouble(random) - 0.5) * 8 * (size + 1),
        0 => random.Chance(50) ? MaxCoordinate : -MaxCoordinate,
        1 => (NextDouble(random) - 0.5) * 2 * MaxCoordinate,
        2 => (NextDouble(random) - 0.5) * 4000,
        3 or 4 or 5 => random.Next(-2, size + 3),
        6 => random.Next(-8, (size * 4) + 9) / 4.0,
        7 => 0,
        _ => (NextDouble(random) * (size + 6)) - 3,
    };

    private static PointD CreatePoint(FuzzRandom random, DrawingCanvas canvas) => new(CreateCoordinate(random, canvas.Width), CreateCoordinate(random, canvas.Height));

    private static PointD[] CreatePoints(FuzzRandom random, DrawingCanvas canvas)
    {
        var points = new PointD[random.Next(1, 7)];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = i > 0 && random.Chance(15) ? points[i - 1] : CreatePoint(random, canvas);
        }

        return points;
    }

    /// <summary>An angle, in degrees: mostly a turn or two, sometimes a quarter turn exactly, sometimes many turns.</summary>
    private static double CreateAngle(FuzzRandom random) => random.Next(8) switch
    {
        0 => 0,
        1 => random.Next(-8, 9) * 90,
        2 => (NextDouble(random) - 0.5) * 1e6,
        _ => (NextDouble(random) - 0.5) * 900,
    };

    private static double CreateOpacity(FuzzRandom random) => random.Next(5) switch
    {
        0 => 1,
        1 => 0,
        _ => NextDouble(random),
    };

    /// <summary>A rectangle of whole pixels, around the canvas.</summary>
    private static Rectangle CreateWholeRectangle(FuzzRandom random, DrawingCanvas canvas)
    {
        var x = random.Next(-3, canvas.Width + 3);
        var y = random.Next(-3, canvas.Height + 3);
        return new Rectangle(x, y, random.Next(0, canvas.Width + 4), random.Next(0, canvas.Height + 4));
    }

    /// <summary>A transformation: mostly gentle, sometimes one that mirrors, flattens or sends everything far away.</summary>
    private static AffineTransform CreateTransform(FuzzRandom random, DrawingCanvas canvas)
    {
        var transform = AffineTransform.Identity;
        for (var i = random.Next(1, 4); i > 0; i--)
        {
            transform = transform.Then(random.Next(8) switch
            {
                0 => AffineTransform.CreateRotation(CreateAngle(random), CreatePoint(random, canvas)),
                1 => AffineTransform.CreateRotation(random.Next(-4, 5) * 90),
                2 => AffineTransform.CreateScale((NextDouble(random) * 6) - 3, (NextDouble(random) * 6) - 3),
                3 => AffineTransform.CreateScale(random.Next(-2, 3), random.Next(-2, 3)),
                4 => AffineTransform.CreateSkew((NextDouble(random) * 170) - 85, (NextDouble(random) * 170) - 85),
                5 => AffineTransform.CreateTranslation(random.Next(-8, 9), random.Next(-8, 9)),
                6 => AffineTransform.CreateScale(NextDouble(random) * 1e-4, 1 + NextDouble(random)),
                _ => AffineTransform.CreateTranslation((NextDouble(random) - 0.5) * 80, (NextDouble(random) - 0.5) * 80),
            });
        }

        return transform;
    }

    /// <summary>
    /// A projective transformation that is never affine: a turn, a scaling by up to 2, a translation, then a perspective
    /// whose horizon may cross the canvas. What the transformation enlarges or reduces a lot near its horizon costs what
    /// a pen or a pattern of that size costs, as the generator of pens keeps in mind.
    /// </summary>
    private static ProjectiveTransform CreateProjectiveTransform(FuzzRandom random, DrawingCanvas canvas)
    {
        var scale = 0.5 + (NextDouble(random) * 1.5);
        var plane = AffineTransform.CreateRotation(CreateAngle(random), CreatePoint(random, canvas))
            .Then(AffineTransform.CreateScale(scale, random.Chance(50) ? scale : 0.5 + (NextDouble(random) * 1.5)))
            .Then(AffineTransform.CreateTranslation(random.Next(-8, 9), random.Next(-8, 9)));
        var p = ((NextDouble(random) * 0.1) - 0.05) + (random.Chance(50) ? 1e-3 : -1e-3);
        var q = random.Chance(30) ? 0 : (NextDouble(random) * 0.1) - 0.05;
        return ((ProjectiveTransform)plane).Then(new ProjectiveTransform(1, 0, p, 0, 1, q, 0, 0, 1));
    }

    /// <summary>A filter of a few pixels of reach: a blur, a color matrix, a shadow, or a chain of them (possibly ending with a quantization).</summary>
    private static Filter CreateFilter(FuzzRandom random) => random.Next(5) switch
    {
        3 => CreateFilterGraph(random),
        0 => new BlurFilter(NextDouble(random) * 4, random.Chance(30) ? 0 : NextDouble(random) * 4) { EdgeMode = (FilterEdgeMode)random.Next(4) },
        1 => new ColorMatrixFilter(random.Next(3) switch
        {
            0 => ColorMatrix.CreateInvert(),
            1 => ColorMatrix.CreateSaturation(NextDouble(random) * 3),
            _ => ColorMatrix.CreateOpacity(NextDouble(random)),
        }),
        2 => new DropShadowFilter(CreateColor(random), new PointD((NextDouble(random) - 0.5) * 8, (NextDouble(random) - 0.5) * 8), NextDouble(random) * 3) { ShadowOnly = random.Chance(30) },
        _ => random.Chance(25) ? new FilterChain(CreateFilter(random), new QuantizeFilter(1 + random.Next(16))) : new FilterChain(CreateFilter(random), CreateFilter(random)),
    };

    private static FilterGraph CreateFilterGraph(FuzzRandom random)
    {
        var primitives = new FilterPrimitive[random.Next(1, 5)];
        for (var index = 0; index < primitives.Length; index++)
        {
            var input = CreateInput(index);
            var input2 = CreateInput(index);
            RectangleD? subregion = random.Chance(3) ? null : new RectangleD((NextDouble(random) - 0.2) * 40, (NextDouble(random) - 0.2) * 40, NextDouble(random) * 40, NextDouble(random) * 40);
            var space = (FilterColorSpace)random.Next(2);
            var drawn = new RectangleD((NextDouble(random) - 0.2) * 40, (NextDouble(random) - 0.2) * 40, NextDouble(random) * 30, NextDouble(random) * 30);
            var drawnColor = CreateColor(random);
            primitives[index] = random.Next(17) switch
            {
                16 => new ImagePrimitive(canvas => canvas.FillEllipse(drawnColor, new PointD(drawn.X, drawn.Y), drawn.Width, drawn.Height)) { Subregion = subregion },
                14 => new DiffuseLightingPrimitive(CreateLight(), CreateColor(random)) { Input = input, Subregion = subregion, ColorSpace = space, SurfaceScale = (NextDouble(random) - 0.5) * 20, DiffuseConstant = NextDouble(random) * 3 },
                15 => new SpecularLightingPrimitive(CreateLight(), CreateColor(random)) { Input = input, Subregion = subregion, ColorSpace = space, SurfaceScale = (NextDouble(random) - 0.5) * 20, SpecularConstant = NextDouble(random) * 3, SpecularExponent = 1 + (NextDouble(random) * 127) },
                13 => new TurbulencePrimitive((FilterTurbulenceType)random.Next(2), NextDouble(random) * 0.5, NextDouble(random) * 0.5, random.Next(5), random.Next(-5, 100)) { Subregion = subregion, ColorSpace = space, StitchTiles = random.Chance(3) },
                10 => new MorphologyPrimitive((FilterMorphologyOperator)random.Next(2), NextDouble(random) * 4, NextDouble(random) * 4) { Input = input, Subregion = subregion, ColorSpace = space },
                11 => CreateConvolveMatrix(input, subregion, space),
                12 => new DisplacementMapPrimitive((NextDouble(random) - 0.5) * 20, input2) { Input = input, Subregion = subregion, ColorSpace = space, XChannel = (FilterChannel)random.Next(4), YChannel = (FilterChannel)random.Next(4) },
                8 => new GaussianBlurPrimitive(NextDouble(random) * 4, random.Chance(30) ? 0 : NextDouble(random) * 4) { Input = input, Subregion = subregion, ColorSpace = space, EdgeMode = (FilterEdgeMode)random.Next(4) },
                9 => new DropShadowPrimitive(CreateColor(random), new PointD((NextDouble(random) - 0.5) * 8, (NextDouble(random) - 0.5) * 8), NextDouble(random) * 3, NextDouble(random) * 3) { Input = input, Subregion = subregion, ColorSpace = space },
                0 => new FloodPrimitive(CreateColor(random)) { Subregion = subregion, ColorSpace = space },
                1 => new OffsetPrimitive(new PointD((NextDouble(random) - 0.5) * 8, (NextDouble(random) - 0.5) * 8)) { Input = input, Subregion = subregion, ColorSpace = space },
                2 => new MergePrimitive(input, input2) { Subregion = subregion, ColorSpace = space },
                3 => new TilePrimitive { Input = input, Subregion = subregion, ColorSpace = space },
                4 => new ColorMatrixPrimitive(random.Chance(2) ? ColorMatrix.LuminanceToAlpha : ColorMatrix.CreateHueRotation(NextDouble(random) * 360)) { Input = input, Subregion = subregion, ColorSpace = space },
                5 => new ComponentTransferPrimitive
                {
                    Input = input,
                    Subregion = subregion,
                    ColorSpace = space,
                    Red = TransferFunction.CreateTable(NextDouble(random), NextDouble(random), NextDouble(random)),
                    Green = TransferFunction.CreateDiscrete(NextDouble(random), NextDouble(random)),
                    Blue = TransferFunction.CreateLinear((NextDouble(random) - 0.5) * 4, NextDouble(random) - 0.5),
                    Alpha = TransferFunction.CreateGamma(NextDouble(random) * 2, NextDouble(random) * 4, NextDouble(random) - 0.5),
                },
                6 => new CompositePrimitive((FilterCompositeOperator)random.Next(7), input2)
                {
                    Input = input,
                    Subregion = subregion,
                    ColorSpace = space,
                    K1 = (NextDouble(random) - 0.5) * 4,
                    K2 = (NextDouble(random) - 0.5) * 4,
                    K3 = (NextDouble(random) - 0.5) * 4,
                    K4 = NextDouble(random) - 0.5,
                },
                _ => new BlendPrimitive((BlendMode)random.Next(16), input2) { Input = input, Subregion = subregion, ColorSpace = space },
            };
        }

        return random.Chance(3) ? new FilterGraph(primitives) { Region = new RectangleD((NextDouble(random) - 0.2) * 40, (NextDouble(random) - 0.2) * 40, NextDouble(random) * 60, NextDouble(random) * 60) } : new FilterGraph(primitives);

        FilterLightSource CreateLight() => random.Next(3) switch
        {
            0 => new DistantLight(NextDouble(random) * 360, (NextDouble(random) - 0.2) * 120),
            1 => new PointLight((NextDouble(random) - 0.2) * 40, (NextDouble(random) - 0.2) * 40, (NextDouble(random) - 0.2) * 100),
            _ => new SpotLight((NextDouble(random) - 0.2) * 40, (NextDouble(random) - 0.2) * 40, (NextDouble(random) - 0.2) * 100, NextDouble(random) * 30, NextDouble(random) * 30, 0)
            {
                SpecularExponent = NextDouble(random) * 40,
                LimitingConeAngle = random.Chance(2) ? NextDouble(random) * 90 : null,
            },
        };

        ConvolveMatrixPrimitive CreateConvolveMatrix(FilterInput input, RectangleD? subregion, FilterColorSpace space)
        {
            var orderX = random.Next(1, 5);
            var orderY = random.Next(1, 5);
            var kernel = new double[orderX * orderY];
            for (var i = 0; i < kernel.Length; i++)
            {
                kernel[i] = (NextDouble(random) - 0.3) * 4;
            }

            return new ConvolveMatrixPrimitive(orderX, orderY, kernel)
            {
                Input = input,
                Subregion = subregion,
                ColorSpace = space,
                Bias = random.Chance(3) ? NextDouble(random) - 0.5 : 0,
                Divisor = random.Chance(3) ? NextDouble(random) + 0.5 : null,
                TargetX = random.Next(orderX),
                TargetY = random.Next(orderY),
                EdgeMode = (FilterEdgeMode)random.Next(4),
                PreserveAlpha = random.Chance(2),
            };
        }

        FilterInput CreateInput(int index) => random.Next(4) switch
        {
            0 => FilterInput.SourceGraphic,
            1 => FilterInput.SourceAlpha,
            2 when index > 0 => FilterInput.FromResult(random.Next(index)),
            _ => FilterInput.Previous,
        };
    }

    private static RectangleD CreateRectangle(FuzzRandom random, DrawingCanvas canvas)
    {
        var a = CreatePoint(random, canvas);
        var b = random.Chance(10) ? a : CreatePoint(random, canvas);
        return RectangleD.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    private static double CreateLength(FuzzRandom random) => random.Next(12) switch
    {
        0 => 0,
        1 or 2 when s_isInPerspective => NextDouble(random) * 60,
        1 => MaxCoordinate,
        2 => NextDouble(random) * 5000,
        3 => NextDouble(random) * 1e-6,
        _ => NextDouble(random) * 20,
    };

    /// <summary>
    /// The radii of an arc to a point, which are scaled up until the arc reaches the point. In perspective, they are of the
    /// same order: a thin ellipse through two points of the canvas is millions of pixels long, and a dashed pen draws as
    /// many dashes where it converges on a horizon that a filtered layer shows.
    /// </summary>
    private static (double X, double Y) CreateArcRadii(FuzzRandom random)
    {
        var radiusX = CreateLength(random);
        var radiusY = CreateLength(random);
        if (s_isInPerspective && radiusX > 0 && radiusY > 0)
        {
            radiusX = Math.Max(radiusX, radiusY / 10);
            radiusY = Math.Max(radiusY, radiusX / 10);
        }

        return (radiusX, radiusY);
    }

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

    /// <summary>Creates an operator: one that never lowers the alpha of a pixel, unless the case erases.</summary>
    private static CompositeMode CreateCompositeMode(FuzzRandom random, FuzzContext context) => context.Erases ? (CompositeMode)random.Next(12) : random.Next(6) switch
    {
        0 => CompositeMode.DestinationOver,
        1 => CompositeMode.SourceAtop,
        _ => CompositeMode.SourceOver,
    };

    private static Brush CreateBrush(FuzzRandom random, DrawingCanvas canvas, FuzzContext context)
    {
        var mode = CreateCompositeMode(random, context);
        if (random.Chance(35))
            return new SolidBrush(CreateColor(random)) { CompositeMode = mode, Opacity = random.Chance(20) ? CreateOpacity(random) : 1 };

        if (random.Chance(20))
        {
            var x = random.Next(context.Sprite.Width);
            var y = random.Next(context.Sprite.Height);
            return new ImageBrush(context.Sprite)
            {
                CompositeMode = mode,
                SourceRegion = random.Chance(30) ? new Rectangle(x, y, random.Next(0, context.Sprite.Width - x + 1), random.Next(0, context.Sprite.Height - y + 1)) : null,
                Transform = random.Chance(60) ? CreateTransform(random, canvas) : AffineTransform.Identity,
                WrapModeX = (ImageWrapMode)random.Next(4),
                WrapModeY = (ImageWrapMode)random.Next(4),
                Sampling = random.Chance(20) ? ImageSampling.Bicubic : (ImageSampling)random.Next(2),
                Opacity = CreateOpacity(random),
            };
        }

        var offsets = new double[random.Next(1, 7)];
        for (var i = 0; i < offsets.Length; i++)
        {
            offsets[i] = random.Chance(30) ? random.Next(5) / 4.0 : NextDouble(random);
        }

        Array.Sort(offsets);
        var stops = offsets.Select(offset => new GradientStop(offset, CreateColor(random))).ToArray();
        var spreadMode = (GradientSpreadMode)random.Next(4);
        var transform = random.Chance(25) ? CreateTransform(random, canvas) : AffineTransform.Identity;
        switch (random.Next(5))
        {
            case 4:
                // Two circles anywhere: a cone, or the same circle twice
                var first = CreatePoint(random, canvas);
                return new RadialGradientBrush(random.Chance(10) ? first : CreatePoint(random, canvas), CreateLength(random), stops) { SpreadMode = spreadMode, CompositeMode = mode, Opacity = random.Chance(20) ? CreateOpacity(random) : 1, Transform = transform, FocalPoint = first, FocalRadius = CreateLength(random) };

            case 0:
                return new LinearGradientBrush(CreatePoint(random, canvas), random.Chance(10) ? default : CreatePoint(random, canvas), stops) { SpreadMode = spreadMode, CompositeMode = mode, Opacity = random.Chance(20) ? CreateOpacity(random) : 1, Transform = transform };

            case 1:
                return new RadialGradientBrush(CreatePoint(random, canvas), CreateLength(random), stops) { SpreadMode = spreadMode, CompositeMode = mode, Opacity = random.Chance(20) ? CreateOpacity(random) : 1, Transform = transform };

            case 2:
                // A focal point strictly inside the circle, sometimes very close to it
                var center = CreatePoint(random, canvas);
                var radius = 0.5 + (NextDouble(random) * 40);
                var distance = radius * (random.Chance(20) ? 0.999999 : NextDouble(random) * 0.95);
                var (sin, cos) = Math.SinCos(NextDouble(random) * 2 * Math.PI);
                var focalPoint = new PointD(center.X + (distance * cos), center.Y + (distance * sin));
                if (!(focalPoint.DistanceTo(center) < radius) || Math.Abs(focalPoint.X) > MaxCoordinate || Math.Abs(focalPoint.Y) > MaxCoordinate)
                {
                    focalPoint = center;
                }

                return new RadialGradientBrush(center, radius, stops) { SpreadMode = spreadMode, CompositeMode = mode, Opacity = random.Chance(20) ? CreateOpacity(random) : 1, Transform = transform, FocalPoint = focalPoint };

            default:
                var start = CreateAngle(random);
                return new SweepGradientBrush(CreatePoint(random, canvas), start, start + (random.Chance(10) ? 0 : NextDouble(random) * 800), stops) { SpreadMode = spreadMode, CompositeMode = mode, Opacity = random.Chance(20) ? CreateOpacity(random) : 1, Transform = transform };
        }
    }

    /// <summary>
    /// A pen that cuts its stroke into dashes, of any width: what is outside the canvas is not cut, so the patterns can
    /// be short on the longest paths. The dashes themselves stay a fraction of a pixel long at least, or the canvas would
    /// show thousands of them.
    /// </summary>
    private static Pen CreateDashedPen(FuzzRandom random, DrawingCanvas canvas, FuzzContext context)
    {
        var pattern = new double[random.Next(1, 6)];
        double total = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            pattern[i] = random.Next(6) switch
            {
                0 => 0,
                1 => random.Next(1, 9),
                2 => NextDouble(random) * 5000,
                _ => 0.25 + (NextDouble(random) * 12),
            };
            total += pattern[i];
        }

        if (total < 0.5)
        {
            pattern[0] = 1 + NextDouble(random);
        }

        var pen = CreatePen(random, canvas, context, widest: random.Chance(10));

        // In perspective, the round and square caps of the dashes of a curve stay narrower than most of its bends (see
        // the remarks): the widths of 1 to 101 pixels become widths of 1 to 21 pixels
        var width = s_isInPerspective && pen.LineCap != LineCap.Butt && pen.Width is > 21 and <= 101 ? 1 + ((pen.Width - 1) / 5) : pen.Width;

        // A pen far wider than the canvas with dashes far shorter than its width cuts every curve near the canvas into
        // slivers across the whole canvas, at as many angles: the exact area of their union costs the square of their
        // number, legitimately. Its pattern is at least a thousandth of its width.
        if (pen.Width > 100000 && total < pen.Width / 1000)
        {
            var factor = pen.Width / 1000 / Math.Max(total, 0.5);
            for (var i = 0; i < pattern.Length; i++)
            {
                pattern[i] *= factor;
            }
        }

        return new Pen(pen.Brush, width)
        {
            LineJoin = pen.LineJoin,
            LineCap = pen.LineCap,
            MiterLimit = pen.MiterLimit,
            DashPattern = pattern,
            DashOffset = random.Chance(30) ? 0 : (NextDouble(random) - 0.5) * 200,
        };
    }

    private static Pen CreatePen(FuzzRandom random, DrawingCanvas canvas, FuzzContext context, bool widest)
    {
        var width = random.Next(10) switch
        {
            0 => widest ? MaxCoordinate : 1 + (NextDouble(random) * 100),
            1 => random.Chance(3) ? 0 : 1e-9 + (NextDouble(random) * 1e-3),
            2 => 1 + (NextDouble(random) * (widest ? 100000 : 100)),
            _ => 0.05 + (NextDouble(random) * 6),
        };
        return new Pen(CreateBrush(random, canvas, context), width)
        {
            LineJoin = (LineJoin)random.Next(3),
            LineCap = (LineCap)random.Next(3),
            MiterLimit = random.Chance(20) ? 1 + (NextDouble(random) * 1000) : 1 + (NextDouble(random) * 9),
        };
    }

    /// <summary>A path of lines: an open or closed polyline, of a few points around the canvas.</summary>
    private static VectorPath CreatePolyline(FuzzRandom random, DrawingCanvas canvas)
    {
        var points = CreatePoints(random, canvas);
        var builder = new VectorPathBuilder().MoveTo(points[0]);
        foreach (var point in points.AsSpan(1))
        {
            builder.LineTo(point);
        }

        if (random.Chance(50))
        {
            builder.Close();
        }

        return builder.Build();
    }

    private static VectorPath CreatePath(FuzzRandom random, DrawingCanvas canvas)
    {
        var builder = new VectorPathBuilder();
        var elements = random.Next(0, 16);
        var started = false;
        for (var i = 0; i < elements; i++)
        {
            var kind = started ? random.Next(18) : random.Next(9);
            try
            {
                switch (kind)
                {
                    case 0:
                        builder.MoveTo(CreatePoint(random, canvas));
                        break;
                    case 1:
                        builder.AddRectangle(CreateRectangle(random, canvas));
                        break;
                    case 2:
                        builder.AddEllipse(CreatePoint(random, canvas), CreateLength(random), CreateLength(random));
                        break;
                    case 3:
                        builder.AddCircle(CreatePoint(random, canvas), CreateLength(random));
                        break;
                    case 4:
                        builder.AddPolygon(CreatePoints(random, canvas));
                        break;
                    case 5:
                        builder.AddRoundedRectangle(CreateRectangle(random, canvas), CreateLength(random), CreateLength(random));
                        break;
                    case 6:
                        builder.AddArc(CreatePoint(random, canvas), CreateLength(random), CreateLength(random), CreateAngle(random), CreateAngle(random));
                        break;
                    case 7:
                        builder.AddPie(CreatePoint(random, canvas), CreateLength(random), CreateLength(random), CreateAngle(random), CreateAngle(random));
                        break;
                    case 8:
                        builder.AddEllipse(CreatePoint(random, canvas), CreateLength(random), CreateLength(random), CreateAngle(random));
                        break;
                    case 9 or 10 or 11:
                        builder.LineTo(CreatePoint(random, canvas));
                        break;
                    case 12:
                        builder.QuadraticBezierTo(CreatePoint(random, canvas), CreatePoint(random, canvas));
                        break;
                    case 13:
                        builder.CubicBezierTo(CreatePoint(random, canvas), CreatePoint(random, canvas), CreatePoint(random, canvas));
                        break;
                    case 14 or 15:
                    {
                        var (radiusX, radiusY) = CreateArcRadii(random);
                        builder.ArcTo(radiusX, radiusY, CreateAngle(random), random.Chance(50), random.Chance(50), CreatePoint(random, canvas));
                        break;
                    }

                    case 16:
                        builder.ArcTo(CreatePoint(random, canvas), CreateLength(random), CreateLength(random), CreateAngle(random), CreateAngle(random));
                        break;
                    default:
                        builder.Close();
                        break;
                }

                started = true;
            }
            catch (ArgumentOutOfRangeException) when (kind is 6 or 7 or 8 or 14 or 15 or 16)
            {
                // An arc, or an ellipse that is turned, which goes beyond the largest coordinate: it is not added
            }
        }

        var path = builder.Build();
        if (random.Chance(20))
        {
            try
            {
                path = path.Transform(CreateTransform(random, canvas));
            }
            catch (ArgumentOutOfRangeException)
            {
                // The transformed path goes beyond the largest coordinate
            }
            catch (ArgumentException)
            {
                // The transformation itself has a coefficient that is not a finite number
            }
        }

        return path;
    }

    /// <summary>What the drawings of a case share: the image some of them paint with, and whether they may erase.</summary>
    private sealed record FuzzContext(ImageFrame Sprite, bool Erases);

    /// <summary>Carries a canvas, which is a ref struct, to a delegate that also takes the index of the frame.</summary>
    private readonly ref struct DrawingCanvasProxy(DrawingCanvas canvas)
    {
        public DrawingCanvas Canvas { get; } = canvas;

        public void FillRectangle(Brush brush, RectangleD rectangle) => Canvas.FillRectangle(brush, rectangle);
    }
}
