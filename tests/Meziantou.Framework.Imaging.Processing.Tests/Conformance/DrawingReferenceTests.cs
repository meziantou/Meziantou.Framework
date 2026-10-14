using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Drawing;
using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Drawing checked against the independent exact references of the test harness: <see cref="ReferenceCoverage"/> (the
/// rational area of each pixel inside a set of contours, by brute force) and <see cref="ReferenceCompositor"/>
/// (source-over written from its definition with rational numbers).
/// </summary>
/// <remarks>
/// Tolerance: none beyond rounding. The coverage of a pixel must be its exact area rounded to the nearest 1/65536 (either
/// neighbor within <see cref="ReferenceCoverage.TieWindow"/> of a tie), and the pixel must be exactly the reference
/// compositing of the paint for that coverage: color and alpha, hidden colors of transparent pixels included.
/// </remarks>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class DrawingReferenceTests
{
    private const int Seed = 20261010;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<PixelFormat> AllFormats => DrawingTestSupport.AllFormats;

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RandomPolygonsMatchTheExactReference(PixelFormat format)
    {
        const int Cases = 160;
        var failures = new List<string>();
        for (var index = 0; index < Cases; index++)
        {
            var random = new Random(Seed + index);
            var width = random.Next(1, 10);
            var height = random.Next(1, 10);
            var polygons = CreatePolygons(random, width, height);
            var evenOdd = random.Next(2) == 0;
            var color = CreateColor(random);

            using var image = DrawingTestSupport.CreateRandom(format, width, height, new Random(Seed - index).Next);
            var frame = image.Frames[0];
            var before = DrawingTestSupport.ReadSamples(frame);
            var path = DrawingTestSupport.ToPath(polygons);
            var brush = new SolidBrush(color);
            frame.Draw(canvas => canvas.Fill(brush, path, evenOdd ? FillRule.EvenOdd : FillRule.NonZero), Ct);

            var areas = ReferenceCoverage.Compute(ReferenceCoverage.FromPolygons(polygons), width, height, evenOdd);
            var mismatch = DrawingTestSupport.Compare(frame, before, areas, DrawingTestSupport.ToPaint(color, format));
            if (mismatch is not null)
            {
                failures.Add($"case {index} ({width}x{height}, {(evenOdd ? "even-odd" : "non-zero")}, color {color}, polygons {Describe(polygons)}):{Environment.NewLine}{mismatch}");
            }
        }

        Assert.Empty(failures.Take(3));
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RandomPolygonsWithEveryOperatorMatchTheExactReference(PixelFormat format)
    {
        const int Cases = 330;
        var failures = new List<string>();
        for (var index = 0; index < Cases; index++)
        {
            var random = new Random(Seed + 7000 + index);
            var width = random.Next(1, 8);
            var height = random.Next(1, 8);
            var polygons = CreatePolygons(random, width, height);
            var evenOdd = random.Next(2) == 0;
            var color = CreateColor(random);
            var mode = (CompositeMode)(index % 11);

            using var image = DrawingTestSupport.CreateRandom(format, width, height, new Random(Seed - 7000 - index).Next);
            var frame = image.Frames[0];
            var before = DrawingTestSupport.ReadSamples(frame);
            var path = DrawingTestSupport.ToPath(polygons);
            var brush = new SolidBrush(color) { CompositeMode = mode };
            frame.Draw(canvas => canvas.Fill(brush, path, evenOdd ? FillRule.EvenOdd : FillRule.NonZero), Ct);

            var areas = ReferenceCoverage.Compute(ReferenceCoverage.FromPolygons(polygons), width, height, evenOdd);
            var mismatch = DrawingTestSupport.Compare(frame, before, areas, DrawingTestSupport.ToPaint(color, format), ToReference(mode));
            if (mismatch is not null)
            {
                failures.Add($"case {index} ({mode}, {width}x{height}, {(evenOdd ? "even-odd" : "non-zero")}, color {color}, polygons {Describe(polygons)}):{Environment.NewLine}{mismatch}");
            }
        }

        Assert.Empty(failures.Take(3));
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void WideShapesWhosePaintReplacesThePixelsMatchTheExactReference(PixelFormat format)
    {
        // Rows long enough for the runs of pixels covered entirely to be written at once: an opaque paint with
        // source-over, and any paint, transparent included, with the operator that replaces the pixels
        const int Cases = 120;
        var failures = new List<string>();
        for (var index = 0; index < Cases; index++)
        {
            var random = new Random(Seed + 9000 + index);
            var width = random.Next(16, 80);
            var height = random.Next(1, 6);
            var polygons = CreatePolygons(random, width, height);
            var evenOdd = random.Next(2) == 0;
            var color = CreateColor(random);
            var mode = (index % 3) switch
            {
                0 => CompositeMode.SourceOver,
                _ => CompositeMode.Source,
            };

            color = (index % 4) switch
            {
                0 or 1 => new Rgba64(color.R, color.G, color.B, ushort.MaxValue),
                2 => new Rgba64(color.R, color.G, color.B, 0),
                _ => color,
            };

            using var image = DrawingTestSupport.CreateRandom(format, width, height, new Random(Seed - 9000 - index).Next);
            var frame = image.Frames[0];
            var before = DrawingTestSupport.ReadSamples(frame);
            var path = DrawingTestSupport.ToPath(polygons);
            var brush = new SolidBrush(color) { CompositeMode = mode };
            frame.Draw(canvas => canvas.Fill(brush, path, evenOdd ? FillRule.EvenOdd : FillRule.NonZero), Ct);

            var areas = ReferenceCoverage.Compute(ReferenceCoverage.FromPolygons(polygons), width, height, evenOdd);
            var mismatch = DrawingTestSupport.Compare(frame, before, areas, DrawingTestSupport.ToPaint(color, format), ToReference(mode));
            if (mismatch is not null)
            {
                failures.Add($"case {index} ({mode}, {width}x{height}, {(evenOdd ? "even-odd" : "non-zero")}, color {color}, polygons {Describe(polygons)}):{Environment.NewLine}{mismatch}");
            }
        }

        Assert.Empty(failures.Take(3));
    }

    [Fact]
    public void TheReferenceOfTheOperatorsIsTheReferenceOfSourceOver()
    {
        // Two references written from two definitions: the operator with its factors, and source-over on its own
        var random = new Random(Seed + 8000);
        foreach (var format in PixelFormats.All)
        {
            var layout = ReferencePixelLayout.FromPixelFormat(format);
            for (var index = 0; index < 400; index++)
            {
                var destination = Enumerable.Range(0, layout.ChannelCount).Select(_ => random.Next(4) == 0 ? (random.Next(2) * layout.MaxValue) : random.Next(layout.MaxValue + 1)).ToArray();
                var paint = DrawingTestSupport.ToPaint(CreateColor(random), format);
                var coverage = random.Next(3) switch
                {
                    0 => ReferenceCoverage.FullCoverage,
                    1 => random.Next(4),
                    _ => random.Next(ReferenceCoverage.FullCoverage + 1),
                };
                Assert.Equal(ReferenceCompositor.Blend(destination, paint, coverage, layout), ReferenceCompositor.Blend(destination, paint, coverage, layout, ReferenceCompositeMode.SourceOver));
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void AClippedShapeHasTheProductOfTheExactCoverages(PixelFormat format)
    {
        const int Cases = 120;
        var layout = ReferencePixelLayout.FromPixelFormat(format);
        var failures = new List<string>();
        for (var index = 0; index < Cases; index++)
        {
            var random = new Random(Seed + 9000 + index);
            var width = random.Next(1, 8);
            var height = random.Next(1, 8);
            var clip = CreatePolygons(random, width, height);
            var shape = CreatePolygons(random, width, height);
            var clipEvenOdd = random.Next(2) == 0;
            var shapeEvenOdd = random.Next(2) == 0;
            var color = CreateColor(random);

            using var image = DrawingTestSupport.CreateRandom(format, width, height, new Random(Seed - 9000 - index).Next);
            var frame = image.Frames[0];
            var before = DrawingTestSupport.ReadSamples(frame);
            var clipPath = DrawingTestSupport.ToPath(clip);
            var shapePath = DrawingTestSupport.ToPath(shape);
            var brush = new SolidBrush(color);
            frame.Draw(canvas => canvas.Clip(clipPath, inner => inner.Fill(brush, shapePath, shapeEvenOdd ? FillRule.EvenOdd : FillRule.NonZero), clipEvenOdd ? FillRule.EvenOdd : FillRule.NonZero), Ct);
            var after = DrawingTestSupport.ReadSamples(frame);

            // The coverage of a pixel is the product of the two exact areas, each rounded to the nearest 1/65536, rounded
            // the same way: round(q m / 65536), ties upward
            var clipAreas = ReferenceCoverage.Compute(ReferenceCoverage.FromPolygons(clip), width, height, clipEvenOdd);
            var shapeAreas = ReferenceCoverage.Compute(ReferenceCoverage.FromPolygons(shape), width, height, shapeEvenOdd);
            var paint = DrawingTestSupport.ToPaint(color, format);
            var channels = layout.ChannelCount;
            for (var pixel = 0; pixel < width * height; pixel++)
            {
                var (clipLow, clipHigh) = ReferenceCoverage.GetAcceptedCoverage(clipAreas[pixel]);
                var (shapeLow, shapeHigh) = ReferenceCoverage.GetAcceptedCoverage(shapeAreas[pixel]);
                var low = (int)((((long)shapeLow * clipLow) + 32768) >> 16);
                var high = (int)((((long)shapeHigh * clipHigh) + 32768) >> 16);
                var destination = before.AsSpan(pixel * channels, channels).ToArray();
                var actual = after.AsSpan(pixel * channels, channels).ToArray();
                if (!ReferenceCompositor.IsAccepted(destination, actual, [paint], low, high, layout))
                {
                    failures.Add($"case {index} ({width}x{height}, color {color}, clip {Describe(clip)}, shape {Describe(shape)}), pixel ({pixel % width}, {pixel / width}): coverage {low}..{high}, before [{string.Join(' ', destination)}], actual [{string.Join(' ', actual)}]");
                    break;
                }
            }
        }

        Assert.Empty(failures.Take(3));
    }

    private static ReferenceCompositeMode ToReference(CompositeMode mode) => mode switch
    {
        CompositeMode.SourceOver => ReferenceCompositeMode.SourceOver,
        CompositeMode.Source => ReferenceCompositeMode.Source,
        CompositeMode.DestinationOver => ReferenceCompositeMode.DestinationOver,
        CompositeMode.SourceIn => ReferenceCompositeMode.SourceIn,
        CompositeMode.DestinationIn => ReferenceCompositeMode.DestinationIn,
        CompositeMode.SourceOut => ReferenceCompositeMode.SourceOut,
        CompositeMode.DestinationOut => ReferenceCompositeMode.DestinationOut,
        CompositeMode.SourceAtop => ReferenceCompositeMode.SourceAtop,
        CompositeMode.DestinationAtop => ReferenceCompositeMode.DestinationAtop,
        CompositeMode.Xor => ReferenceCompositeMode.Xor,
        CompositeMode.Clear => ReferenceCompositeMode.Clear,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    // -----------------------------------------------------------------------------------------------------------------
    // The reference itself
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheCoverageReferenceAgreesWithTheWindingNumberOfSamplePoints()
    {
        // The share of the points of a regular grid that are inside, by their exact winding number, converges to the
        // area: with 12x12 points per pixel the difference is bounded by the pixels of that grid the contours cross
        const int Samples = 12;
        for (var index = 0; index < 12; index++)
        {
            var random = new Random(Seed + 1000 + index);
            var polygons = CreatePolygons(random, 3, 3);
            var edges = ReferenceCoverage.FromPolygons(polygons);
            foreach (var evenOdd in new[] { false, true })
            {
                var areas = ReferenceCoverage.Compute(edges, 3, 3, evenOdd);
                for (var pixel = 0; pixel < 9; pixel++)
                {
                    var inside = 0;
                    for (var sy = 0; sy < Samples; sy++)
                    {
                        for (var sx = 0; sx < Samples; sx++)
                        {
                            // Odd multiples of 1/24 plus an offset no vertex has: never on a segment of the fuzzed polygons
                            var winding = ReferenceCoverage.GetWinding(edges, (pixel % 3) + ((sx + 0.5) / Samples) + 1e-7, (pixel / 3) + ((sy + 0.5) / Samples) + 1.3e-7);
                            if (evenOdd ? (winding & 1) != 0 : winding != 0)
                            {
                                inside++;
                            }
                        }
                    }

                    var sampled = (double)inside / (Samples * Samples);
                    var area = DrawingTestSupport.ToDouble(areas[pixel]);
                    Assert.InRange(area, 0, 1);
                    Assert.True(Math.Abs(area - sampled) <= 0.3, $"case {index}, pixel {pixel}, {(evenOdd ? "even-odd" : "non-zero")}: area {area}, sampled {sampled}, polygons {Describe(polygons)}");
                }
            }
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Curves: the chords the library draws stay within the tolerance of the curve
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void FlattenedBezierCurvesStayWithinTheToleranceOfTheCurve()
    {
        for (var index = 0; index < 200; index++)
        {
            var random = new Random(Seed + 2000 + index);
            var scale = Math.Pow(10, random.Next(-1, 4));
            var control = new (double X, double Y)[random.Next(2) == 0 ? 3 : 4];
            for (var i = 0; i < control.Length; i++)
            {
                control[i] = ((random.NextDouble() - 0.5) * scale, (random.NextDouble() - 0.5) * scale);
            }

            var sink = new RecordingSink();
            sink.MoveTo(control[0].X, control[0].Y);
            if (control.Length == 3)
            {
                PathFlattener.Quadratic(sink, control[0].X, control[0].Y, control[1].X, control[1].Y, control[2].X, control[2].Y);
            }
            else
            {
                PathFlattener.Cubic(sink, control[0].X, control[0].Y, control[1].X, control[1].Y, control[2].X, control[2].Y, control[3].X, control[3].Y);
            }

            sink.End();
            var contour = Assert.Single(sink.Contours);
            var points = contour.Points;
            var count = points.Count - 1;
            Assert.True(count >= 1);

            // The ends are the control points, exactly; the vertices are on the curve, at uniform parameters; every
            // vertex but the first and the last is flagged as created by the flattening
            Assert.Equal(control[0], points[0]);
            Assert.Equal(control[^1], points[^1]);
            for (var i = 0; i <= count; i++)
            {
                var expected = Evaluate(control, (double)i / count);
                Assert.True(Distance(expected, points[i]) <= 1e-9 * scale, $"case {index}: vertex {i} of {count} is not on the curve");
                Assert.Equal(i > 0 && i < count, contour.Smooth[i]);
            }

            // Between two vertices, the curve is within the tolerance of the chord
            for (var i = 0; i < count; i++)
            {
                for (var step = 1; step < 16; step++)
                {
                    var onCurve = Evaluate(control, (i + (step / 16.0)) / count);
                    var deviation = DistanceToSegment(onCurve, points[i], points[i + 1]);
                    Assert.True(deviation <= PathFlattener.Tolerance + (1e-9 * scale), $"case {index}: the curve is {deviation} away from chord {i} of {count}");
                }
            }

            // No more chords than needed: with half as many, a curve of this kind would exceed the tolerance somewhere
            // (a looser bound than the exact rule, which only catches a runaway subdivision)
            Assert.True(count <= 2 + (8 * Math.Sqrt(scale * 4 / PathFlattener.Tolerance)), $"case {index}: {count} chords at scale {scale}");
        }

        // A curve that is a straight line traveled at constant speed needs one chord
        var straight = new RecordingSink();
        straight.MoveTo(0, 0);
        PathFlattener.Cubic(straight, 0, 0, 10, 5, 20, 10, 30, 15);
        PathFlattener.Quadratic(straight, 30, 15, 40, 15, 50, 15);
        straight.End();
        Assert.Equal([(0, 0), (30, 15), (50, 15)], straight.Contours[0].Points);
    }

    [Theory]
    [InlineData(0.1, 0.1, 4)]
    [InlineData(1, 1, 16)]
    [InlineData(1, 0.25, 16)]
    [InlineData(3.2, 3.2, 16)]
    [InlineData(3.3, 3.3, 32)]
    [InlineData(100, 100, 128)]
    [InlineData(100, 2, 128)]
    [InlineData(5000, 5000, 1024)]
    public void FlattenedEllipsesStayWithinTheToleranceOfTheEllipse(double radiusX, double radiusY, int expectedVertices)
    {
        var sink = new RecordingSink();
        PathFlattener.Ellipse(sink, 0, 0, radiusX, radiusY);
        sink.End();
        var contour = Assert.Single(sink.Contours);
        Assert.True(contour.Closed);

        // The contour ends where it started: the last vertex is the first one
        var points = contour.Points;
        Assert.Equal(points[0], points[^1]);
        Assert.Equal(expectedVertices, points.Count - 1);
        Assert.All(contour.Smooth, smooth => smooth);
        Assert.Equal((radiusX, 0d), points[0]);

        var set = points.ToHashSet();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var (x, y) = points[i];

            // On the ellipse
            Assert.Equal(1, ((x / radiusX) * (x / radiusX)) + ((y / radiusY) * (y / radiusY)), 1e-12);

            // The polygon has the symmetries of the ellipse, exactly
            Assert.Contains((-x, y), set);
            Assert.Contains((x, -y), set);
            if (radiusX == radiusY)
            {
                Assert.Contains((y, x), set);
            }

            // The middle of a chord is the point of the polygon that is the farthest from the ellipse
            var middle = ((x + points[i + 1].X) / 2, (y + points[i + 1].Y) / 2);
            var angle = Math.Atan2(middle.Item2 / radiusY, middle.Item1 / radiusX);
            var deviation = Distance(middle, (radiusX * Math.Cos(angle), radiusY * Math.Sin(angle)));
            Assert.True(deviation <= PathFlattener.Tolerance, $"chord {i}: {deviation}");
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Strokes: the pieces the library fills cover the region a stroke is defined as, and are filled exactly
    // -----------------------------------------------------------------------------------------------------------------

    public static TheoryData<string> StrokeCases => [.. CreateStrokeCases().Select(item => item.Name)];

    [Theory]
    [MemberData(nameof(StrokeCases))]
    public void TheStrokeOfAPathIsTheRegionOfTheReferenceStroke(string name)
    {
        var (_, path, pen) = CreateStrokeCases().Single(item => item.Name == name);
        const int Size = 48;

        // What the library fills, and the polylines it strokes (the path once its curves are flattened)
        var edges = GetStrokeEdges(path, pen, Size, Size);
        var recording = new RecordingSink();
        PathFlattener.Flatten(path, recording);
        var references = recording.Contours
            .Select(contour => new ReferenceStroke(contour.Points, contour.Closed, pen.Width, pen.LineJoin switch
            {
                LineJoin.Miter => ReferenceLineJoin.Miter,
                LineJoin.Round => ReferenceLineJoin.Round,
                _ => ReferenceLineJoin.Bevel,
            }, pen.LineCap switch
            {
                LineCap.Butt => ReferenceLineCap.Butt,
                LineCap.Round => ReferenceLineCap.Round,
                _ => ReferenceLineCap.Square,
            }, pen.MiterLimit, contour.Smooth))
            .ToList();

        // Round joins and caps are arcs the library replaces by chords within the flattening tolerance: a point closer
        // than that to the boundary may be on either side. Everything else is exact.
        var rounded = pen.LineJoin == LineJoin.Round || pen.LineCap == LineCap.Round || recording.Contours.Any(contour => contour.Smooth.Any(smooth => smooth));
        var margin = rounded ? PathFlattener.Tolerance + 1e-6 : 1e-6;
        int inside = 0, outside = 0;
        for (var y = 0.113; y < Size; y += 0.371)
        {
            for (var x = 0.107; x < Size; x += 0.353)
            {
                var distance = references.Max(reference => reference.GetSignedDistance(x, y));
                if (Math.Abs(distance) <= margin)
                    continue;

                // Every piece turns the same way: where pieces overlap, their winding numbers add up and never cancel
                var winding = GetWinding(edges, x, y);
                Assert.True(winding <= 0, $"({x}, {y}): winding {winding}; the pieces of a stroke all have the same orientation");
                if (distance > 0)
                {
                    inside++;
                    Assert.NotEqual(0, winding, $"({x}, {y}) is {distance} inside the stroke and is not covered");
                }
                else
                {
                    outside++;
                    Assert.Equal(0, winding, $"({x}, {y}) is {-distance} outside the stroke and is covered");
                }
            }
        }

        Assert.True(inside > 20 && outside > 20, $"{inside} points inside, {outside} outside");
    }

    [Theory]
    [MemberData(nameof(StrokeCases))]
    public void AStrokeIsTheExactAreaOfItsPieces(string name)
    {
        var (_, path, pen) = CreateStrokeCases().Single(item => item.Name == name);

        // The same shapes at a quarter of the size, to keep the exact reference fast
        const int Size = 12;
        var builder = new VectorPathBuilder();
        var recording = new ScalingSink(builder, 0.25);
        PathFlattener.Flatten(path, recording);
        var small = builder.Build();
        var smallPen = new Pen(new SolidBrush(new Rgba32(255, 255, 255, 200)), pen.Width / 4) { LineJoin = pen.LineJoin, LineCap = pen.LineCap, MiterLimit = pen.MiterLimit };

        var edges = GetStrokeEdges(small, smallPen, Size, Size);
        var areas = ReferenceCoverage.Compute([.. edges.Select(edge => new ReferenceEdge(edge.X0, edge.Y0, edge.X1, edge.Y1))], Size, Size, evenOdd: false);
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Gray16 })
        {
            using var image = DrawingTestSupport.CreateRandom(format, Size, Size, new Random(Seed).Next);
            var before = DrawingTestSupport.ReadSamples(image.Frames[0]);
            image.Frames[0].Draw(canvas => canvas.Stroke(smallPen, small), Ct);
            var mismatch = DrawingTestSupport.Compare(image.Frames[0], before, areas, DrawingTestSupport.ToPaint(new Rgba32(255, 255, 255, 200), format));
            Assert.Null(mismatch);
        }

        Assert.Contains(areas, area => area > 0);
    }

    [Fact]
    public void AFilledCurveIsTheExactAreaOfItsChords()
    {
        var path = new VectorPathBuilder()
            .MoveTo(1, 1).CubicBezierTo(9, 0, 2, 9, 10.5, 8.5).QuadraticBezierTo(4, 11, 1.5, 6.25).Close()
            .AddEllipse(new PointD(6, 6), 3.5, 2.25)
            .Build();
        var recording = new RecordingSink();
        PathFlattener.Flatten(path, recording);
        var polygons = recording.Contours.Select(contour => (IReadOnlyList<(double X, double Y)>)contour.Points).ToList();
        foreach (var evenOdd in new[] { false, true })
        {
            var areas = ReferenceCoverage.Compute(ReferenceCoverage.FromPolygons(polygons), 12, 12, evenOdd);
            using var image = DrawingTestSupport.CreateRandom(PixelFormat.Rgba64, 12, 12, new Random(Seed).Next);
            var before = DrawingTestSupport.ReadSamples(image.Frames[0]);
            var color = new Rgba64(60000, 300, 20000, 51234);
            image.Frames[0].Draw(canvas => canvas.Fill(new SolidBrush(color), path, evenOdd ? FillRule.EvenOdd : FillRule.NonZero), Ct);
            var mismatch = DrawingTestSupport.Compare(image.Frames[0], before, areas, DrawingTestSupport.ToPaint(color, PixelFormat.Rgba64));
            Assert.Null(mismatch);
        }
    }

    [Fact]
    public void FilledArcsAreTheExactAreaOfTheirChords()
    {
        var path = new VectorPathBuilder()
            .AddRoundedRectangle(new RectangleD(0.75, 1.5, 9.5, 6.25), 2.75, 1.5)
            .AddPie(new PointD(6, 6.5), 5, 3.25, 20, 250)
            .MoveTo(1, 10).ArcTo(4, 2.5, 30, largeArc: true, clockwise: false, 9, 11.5).ArcTo(new PointD(3, 3), 2, 2.5, 200, -300).Close()
            .AddEllipse(new PointD(7, 4), 4, 1.25, 40)
            .Build();
        var recording = new RecordingSink();
        PathFlattener.Flatten(path, recording);
        var polygons = recording.Contours.Select(contour => (IReadOnlyList<(double X, double Y)>)contour.Points).ToList();
        Assert.HasCount(4, polygons);
        foreach (var evenOdd in new[] { false, true })
        {
            var areas = ReferenceCoverage.Compute(ReferenceCoverage.FromPolygons(polygons), 12, 12, evenOdd);
            using var image = DrawingTestSupport.CreateRandom(PixelFormat.Rgba64, 12, 12, new Random(Seed).Next);
            var before = DrawingTestSupport.ReadSamples(image.Frames[0]);
            var color = new Rgba64(300, 60000, 20000, 43210);
            image.Frames[0].Draw(canvas => canvas.Fill(new SolidBrush(color), path, evenOdd ? FillRule.EvenOdd : FillRule.NonZero), Ct);
            var mismatch = DrawingTestSupport.Compare(image.Frames[0], before, areas, DrawingTestSupport.ToPaint(color, PixelFormat.Rgba64));
            Assert.Null(mismatch);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Gradients
    // -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void RandomGradientsMatchTheExactReference(PixelFormat format)
    {
        // The last cases are linear gradients along a vertical axis, whose rows have one color
        const int Cases = 150;
        const int VerticalCases = 30;
        var layout = ReferencePixelLayout.FromPixelFormat(format);
        var failures = new List<string>();
        for (var index = 0; index < Cases; index++)
        {
            var random = new Random(Seed + 3000 + index);
            var width = random.Next(1, 9);
            var height = random.Next(2, 9);

            // Offsets on sixteenths, so that hard stops, single stops and pixel centers exactly on a stop all happen
            var offsets = Enumerable.Range(0, random.Next(1, 6)).Select(_ => random.Next(17) / 16.0).Order().ToArray();
            var stops = offsets.Select(offset => new GradientStop(offset, CreateColor(random))).ToArray();
            var spreadMode = (GradientSpreadMode)random.Next(3);
            double Coordinate(int size) => random.Next(3) == 0 ? random.Next(-2, size + 3) : (random.NextDouble() * (size + 4)) - 2;
            var isVertical = index >= Cases - VerticalCases;
            var radial = random.Next(2) == 0 && !isVertical;
            var (x0, y0, x1, y1) = (Coordinate(width), Coordinate(height), Coordinate(width), Coordinate(height));
            if (isVertical)
            {
                x1 = x0;
            }

            var radius = random.Next(4) == 0 ? random.Next(0, 6) : random.NextDouble() * 8;
            GradientBrush brush = radial
                ? new RadialGradientBrush(new PointD(x0, y0), radius, stops) { SpreadMode = spreadMode }
                : new LinearGradientBrush(new PointD(x0, y0), new PointD(x1, y1), stops) { SpreadMode = spreadMode };

            var referenceStops = stops.Select(stop => (stop.Offset, DrawingTestSupport.ToPaint(stop.Color, format))).ToList();
            var referenceSpread = spreadMode switch
            {
                GradientSpreadMode.Pad => ReferenceSpreadMode.Pad,
                GradientSpreadMode.Repeat => ReferenceSpreadMode.Repeat,
                _ => ReferenceSpreadMode.Reflect,
            };
            var reference = radial
                ? ReferenceGradient.Radial(x0, y0, radius, referenceStops, referenceSpread, layout)
                : ReferenceGradient.Linear(x0, y0, x1, y1, referenceStops, referenceSpread, layout);

            using var image = DrawingTestSupport.CreateRandom(format, width, height, new Random(Seed - index).Next);
            var frame = image.Frames[0];
            var before = DrawingTestSupport.ReadSamples(frame);

            // Every pixel entirely, but the last row: its top half only
            frame.Draw(canvas => canvas.FillRectangle(brush, new RectangleD(0, 0, width, height - 0.5)), Ct);
            var after = DrawingTestSupport.ReadSamples(frame);
            var channels = layout.ChannelCount;
            for (var pixel = 0; pixel < width * height; pixel++)
            {
                var (x, y) = (pixel % width, pixel / width);
                var coverage = y == height - 1 ? ReferenceCoverage.FullCoverage / 2 : ReferenceCoverage.FullCoverage;
                var destination = before.AsSpan(pixel * channels, channels).ToArray();
                var actual = after.AsSpan(pixel * channels, channels).ToArray();
                var paints = reference.GetAcceptedPaints(x, y);
                if (!ReferenceCompositor.IsAccepted(destination, actual, paints, coverage, coverage, layout))
                {
                    failures.Add($"case {index} ({(radial ? "radial" : "linear")} ({x0:R},{y0:R}) ({x1:R},{y1:R}) radius {radius:R}, {spreadMode}, stops {string.Join(' ', stops)}), pixel ({x}, {y}): before [{string.Join(' ', destination)}], actual [{string.Join(' ', actual)}], accepted paints {string.Join(" | ", paints.Select(paint => $"[{string.Join(' ', paint.Color)}] a{paint.Alpha}"))}");
                    break;
                }
            }
        }

        Assert.Empty(failures.Take(3));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Pixel formats: the same picture whatever the layout
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void PixelFormatsThatStoreTheSameSamplesGiveTheSameSamples()
    {
        for (var index = 0; index < 40; index++)
        {
            var random = new Random(Seed + 4000 + index);
            var path = DrawingTestSupport.ToPath(CreatePolygons(random, 6, 6));
            var color = CreateColor(random);
            var gradient = new LinearGradientBrush(new PointD(0, 1), new PointD(6, 4), [new GradientStop(0, CreateColor(random)), new GradientStop(1, CreateColor(random))]);
            void Paint(DrawingCanvas canvas)
            {
                canvas.Fill(new SolidBrush(color), path);
                canvas.Stroke(new Pen(gradient, 1.3) { LineJoin = LineJoin.Round }, path);
            }

            using var rgba = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 6, 6, new Random(index).Next);
            var source = DrawingTestSupport.ReadSamples(rgba.Frames[0]);
            rgba.Draw(Paint, Ct);
            var expected = DrawingTestSupport.ReadSamples(rgba.Frames[0]);

            // Bgra32: the same pixels with red and blue swapped in storage
            using var bgra = DrawingTestSupport.Create(PixelFormat.Bgra32, 6, 6);
            DrawingTestSupport.WriteSamples(bgra.Frames[0], SwapRedAndBlue(source));
            bgra.Draw(Paint, Ct);
            Assert.Equal(SwapRedAndBlue(expected), DrawingTestSupport.ReadSamples(bgra.Frames[0]));

            // Rgb24: the same samples as opaque Rgba32 pixels
            using var opaque = DrawingTestSupport.Create(PixelFormat.Rgba32, 6, 6);
            var opaqueSource = (int[])source.Clone();
            for (var i = 3; i < opaqueSource.Length; i += 4)
            {
                opaqueSource[i] = 255;
            }

            DrawingTestSupport.WriteSamples(opaque.Frames[0], opaqueSource);
            opaque.Draw(Paint, Ct);
            using var rgb = DrawingTestSupport.Create(PixelFormat.Rgb24, 6, 6);
            DrawingTestSupport.WriteSamples(rgb.Frames[0], DropAlpha(opaqueSource));
            rgb.Draw(Paint, Ct);
            var opaqueResult = DrawingTestSupport.ReadSamples(opaque.Frames[0]);
            Assert.Equal(DropAlpha(opaqueResult), DrawingTestSupport.ReadSamples(rgb.Frames[0]));
            for (var i = 3; i < opaqueResult.Length; i += 4)
            {
                Assert.Equal(255, opaqueResult[i]);
            }
        }

        static int[] SwapRedAndBlue(int[] samples)
        {
            var result = (int[])samples.Clone();
            for (var i = 0; i < result.Length; i += 4)
            {
                (result[i], result[i + 2]) = (result[i + 2], result[i]);
            }

            return result;
        }

        static int[] DropAlpha(int[] samples) => [.. samples.Where((_, i) => i % 4 != 3)];
    }

    private static List<(string Name, VectorPath Path, Pen Pen)> CreateStrokeCases()
    {
        var white = new SolidBrush(new Rgba32(255, 255, 255));
        var zigzag = new VectorPathBuilder().MoveTo(6, 8).LineTo(30, 10).LineTo(12, 22).LineTo(40, 30).LineTo(38, 12).LineTo(20, 40).Build();
        var sharp = new VectorPathBuilder().MoveTo(8, 10).LineTo(38, 14).LineTo(10, 17).LineTo(36, 40).LineTo(30, 20).Build();
        var closed = new VectorPathBuilder().AddPolygon([new PointD(10, 8), new PointD(38, 14), new PointD(30, 38), new PointD(22, 20), new PointD(8, 34)]).Build();
        var degenerate = new VectorPathBuilder()
            .MoveTo(8, 8).LineTo(20, 8).LineTo(20, 8).LineTo(32, 8) // a repeated point, then straight on
            .LineTo(14, 8) // back on itself
            .MoveTo(10, 20).LineTo(30, 20).LineTo(30, 34).LineTo(10, 34).LineTo(10, 20) // comes back to its start without closing
            .MoveTo(24, 40) // a single point
            .MoveTo(36, 28).LineTo(40, 40).Close() // two points, closed
            .Build();
        var curves = new VectorPathBuilder()
            .MoveTo(6, 40).CubicBezierTo(10, 2, 30, 46, 42, 8)
            .MoveTo(8, 8).QuadraticBezierTo(24, 30, 40, 36)
            .AddEllipse(new PointD(30, 26), 9, 5)
            .Build();
        var cases = new List<(string Name, VectorPath Path, Pen Pen)>();
        foreach (var (pathName, path) in new[] { ("zigzag", zigzag), ("sharp", sharp), ("closed", closed), ("degenerate", degenerate), ("curves", curves) })
        {
            foreach (var join in new[] { LineJoin.Miter, LineJoin.Round, LineJoin.Bevel })
            {
                foreach (var cap in new[] { LineCap.Butt, LineCap.Round, LineCap.Square })
                {
                    // One width per combination, and the miter limits around the usual corners
                    var width = (((int)join * 3) + (int)cap) switch
                    {
                        0 or 4 or 8 => 3.0,
                        1 or 5 or 6 => 1.25,
                        _ => 5.5,
                    };
                    cases.Add(($"{pathName}-{join}-{cap}-{width.ToString(CultureInfo.InvariantCulture)}", path, new Pen(white, width) { LineJoin = join, LineCap = cap, MiterLimit = cap == LineCap.Square ? 2 : 4 }));
                }
            }
        }

        cases.Add(("sharp-Miter-Butt-limit-1", sharp, new Pen(white, 3) { MiterLimit = 1 }));
        cases.Add(("sharp-Miter-Butt-limit-20", sharp, new Pen(white, 2) { MiterLimit = 20 }));
        return cases;
    }

    /// <summary>Runs the stroker of the library on a path and returns the directed edges of the pieces it fills.</summary>
    private static List<(double X0, double Y0, double X1, double Y1)> GetStrokeEdges(VectorPath path, Pen pen, int width, int height)
    {
        using var edges = new EdgeBuilder(AllocationScope.Create(configuration: null), width, height);
        var stroker = new Stroker(edges);
        stroker.Configure(pen);
        PathFlattener.Flatten(path, stroker);
        var result = new List<(double X0, double Y0, double X1, double Y1)>();
        foreach (var edge in edges.Edges)
        {
            // Stored from top to bottom, with the direction of the contour aside
            result.Add(edge.Direction > 0 ? (edge.X0, edge.Y0, edge.X1, edge.Y1) : (edge.X1, edge.Y1, edge.X0, edge.Y0));
        }

        return result;
    }

    private static int GetWinding(List<(double X0, double Y0, double X1, double Y1)> edges, double x, double y)
    {
        var winding = 0;
        foreach (var (x0, y0, x1, y1) in edges)
        {
            var (top, bottom) = y0 < y1 ? (y0, y1) : (y1, y0);
            if (!(top <= y && y < bottom))
                continue;

            if (x0 + ((y - y0) * (x1 - x0) / (y1 - y0)) < x)
            {
                winding += y0 < y1 ? 1 : -1;
            }
        }

        return winding;
    }

    private static (double X, double Y) Evaluate((double X, double Y)[] control, double t)
    {
        // De Casteljau: repeated interpolation of the control points
        var points = ((double X, double Y)[])control.Clone();
        for (var count = points.Length - 1; count > 0; count--)
        {
            for (var i = 0; i < count; i++)
            {
                points[i] = (points[i].X + (t * (points[i + 1].X - points[i].X)), points[i].Y + (t * (points[i + 1].Y - points[i].Y)));
            }
        }

        return points[0];
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static double DistanceToSegment((double X, double Y) point, (double X, double Y) a, (double X, double Y) b)
    {
        var lengthSquared = ((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y));
        if (lengthSquared == 0)
            return Distance(point, a);

        var t = Math.Clamp((((point.X - a.X) * (b.X - a.X)) + ((point.Y - a.Y) * (b.Y - a.Y))) / lengthSquared, 0, 1);
        return Distance(point, (a.X + (t * (b.X - a.X)), a.Y + (t * (b.Y - a.Y))));
    }

    /// <summary>One to three polygons, convex or not, self-intersecting or not, inside, across and outside the canvas.</summary>
    private static List<IReadOnlyList<(double X, double Y)>> CreatePolygons(Random random, int width, int height)
    {
        var polygons = new List<IReadOnlyList<(double X, double Y)>>();
        var count = random.Next(1, 4);

        // 0: eighths of a pixel (exact areas, many exact ties); 1: integers (edges on pixel boundaries); 2: any double;
        // 3: any double, far beyond the canvas
        var kind = random.Next(4);
        for (var i = 0; i < count; i++)
        {
            var points = new (double X, double Y)[random.Next(3, 8)];
            for (var j = 0; j < points.Length; j++)
            {
                points[j] = kind switch
                {
                    0 => (random.Next(-16, (width * 8) + 17) / 8.0, random.Next(-16, (height * 8) + 17) / 8.0),
                    1 => (random.Next(-2, width + 3), random.Next(-2, height + 3)),
                    2 => ((random.NextDouble() * (width + 4)) - 2, (random.NextDouble() * (height + 4)) - 2),
                    _ => ((random.NextDouble() * 4000) - 2000, (random.NextDouble() * 4000) - 2000),
                };
            }

            polygons.Add(points);
        }

        return polygons;
    }

    private static Rgba64 CreateColor(Random random)
    {
        var alpha = random.Next(4) switch
        {
            0 => ushort.MaxValue,
            1 => (ushort)random.Next(1, 600),
            _ => (ushort)random.Next(ushort.MaxValue + 1),
        };
        return new Rgba64((ushort)random.Next(ushort.MaxValue + 1), (ushort)random.Next(ushort.MaxValue + 1), (ushort)random.Next(ushort.MaxValue + 1), alpha);
    }

    private static string Describe(List<IReadOnlyList<(double X, double Y)>> polygons)
        => string.Join(" ; ", polygons.Select(polygon => string.Join(' ', polygon.Select(point => string.Create(CultureInfo.InvariantCulture, $"({point.X:R},{point.Y:R})")))));

    /// <summary>Records the contours the library sends when it flattens a path.</summary>
    private sealed class RecordingSink : PathSink
    {
        public List<(List<(double X, double Y)> Points, List<bool> Smooth, bool Closed)> Contours { get; } = [];

        public override void MoveTo(double x, double y) => Contours.Add(([(x, y)], [false], false));

        public override void LineTo(double x, double y, bool smooth)
        {
            var (points, flags, _) = Contours[^1];

            // The flag tells that the vertex the segment starts from was created by the flattening
            flags[^1] = smooth;
            points.Add((x, y));
            flags.Add(false);
        }

        public override void Close(bool smooth)
        {
            var (points, flags, _) = Contours[^1];
            flags[^1] = smooth;
            flags[0] = smooth;
            Contours[^1] = (points, flags, true);
        }

        public override void End()
        {
        }
    }

    /// <summary>Rebuilds a flattened path at another scale.</summary>
    private sealed class ScalingSink(VectorPathBuilder builder, double scale) : PathSink
    {
        public override void MoveTo(double x, double y) => builder.MoveTo(x * scale, y * scale);

        public override void LineTo(double x, double y, bool smooth) => builder.LineTo(x * scale, y * scale);

        public override void Close(bool smooth) => builder.Close();

        public override void End()
        {
        }
    }
}
