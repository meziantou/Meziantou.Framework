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
/// like a call on that frame; the alpha of a pixel never decreases, unless the case uses the operators that erase
/// (a third of the cases); a format without alpha has nothing else to keep.
/// </para>
/// <para>
/// Two failures are documented for valid arguments, and expected here: an arc or a transformed path that goes beyond
/// the largest coordinate is rejected when it is built. The generator catches that exception there, and nowhere else.
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

    // In perspective, coordinates and lengths of the size of the canvas: a shape of 2^24 pixels whose far end is drawn
    // at the vanishing point of the canvas is millions of chords and dashes there, which the limits of the image refuse
    [ThreadStatic]
    private static bool t_isInPerspective;

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

        // A case out of three uses the operators that erase and replace pixels: the alpha of a pixel may then decrease
        var erases = random.Chance(33);

        // The image some drawings paint with: any pixel format, never the image that is drawn on
        using var sprite = DrawingTestSupport.CreateRandom(PixelFormats.All[random.Next(PixelFormats.All.Count)], random.Next(1, 9), random.Next(1, 9), random.Next);
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
            if (!hasAlpha || !checkAlpha)
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
                    // A filtered drawing, a layer that holds the pixels of a rectangle, a scope of 16-bit samples, or a
                    // layer that starts with the filtered backdrop of a rectangle
                    if (depth >= 3)
                    {
                        canvas.Fill(CreateBrush(random, canvas, context));
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
                    canvas.Stroke(CreateDashedPen(random, canvas, context), CreatePath(random, canvas));
                    break;
            }
        }
    }

    private static void PaintInPerspective(DrawingAction action, DrawingCanvas canvas)
    {
        var previous = t_isInPerspective;
        t_isInPerspective = true;
        try
        {
            action(canvas);
        }
        finally
        {
            t_isInPerspective = previous;
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
        0 or 1 or 2 when t_isInPerspective => (NextDouble(random) - 0.5) * 8 * (size + 1),
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

    /// <summary>A filter of a few pixels of reach: a blur, a color matrix, a shadow, or a chain of them.</summary>
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
        _ => new FilterChain(CreateFilter(random), CreateFilter(random)),
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
        1 or 2 when t_isInPerspective => NextDouble(random) * 60,
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

        return new Pen(pen.Brush, pen.Width)
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
                        builder.ArcTo(CreateLength(random), CreateLength(random), CreateAngle(random), random.Chance(50), random.Chance(50), CreatePoint(random, canvas));
                        break;
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
