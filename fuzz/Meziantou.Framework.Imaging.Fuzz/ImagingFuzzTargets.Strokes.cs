using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>The command bytes of the paths of <see cref="StrokeTarget"/>, and a few commands with their points.</summary>
    /// <remarks>A property: <see cref="All"/>, in another part of the class, may be initialized first.</remarks>
    private static string[] StrokeTokens =>
    [
        "\0", "\u0001", "\u0002", "\u0003", "\u0004", "\u0005", "\u0006", "\u0007", "\u007F", "\u0080", "ÿ",
        "\u0001\u0010\u0010", "\u0002\u0010\u0000  ", "\u0005\u0000\u00000\u0000",
    ];

    /// <summary>The widths of the pens of <see cref="StrokeTarget"/>: hairlines, the outline of a heading, wide pens.</summary>
    private static readonly double[] StrokeWidths = [1.5, 1, 2, 0.25, 3.75, 5, 9.5, 24, 0.05, 0, 64];

    /// <summary>The scales of the canvas of <see cref="StrokeTarget"/>, around its center; a mirror among them.</summary>
    private static readonly double[] StrokeScales = [1, 2, 0.5, 1.25, -1];

    /// <summary>
    /// The perspectives of the canvas of <see cref="StrokeTarget"/>, after <see cref="StrokeScales"/>: a tilt and a horizon
    /// across the canvas, around its center, and a plane seen nearly edge-on, whose vanishing point is moved to the center
    /// (fuzz regression: a wide pen on a circle reached its horizon from every part of the circle).
    /// </summary>
    private static readonly ProjectiveTransform[] StrokePerspectives =
    [
        new(1, 0, 0.02, 0, 1, 0.01, 0, 0, 1),
        new(1, 0, 0.05, 0, 1, 0, 0, 0, 1),
        new(1.7422965749764903, 0.06924918865877217, 0.03463023903403511, 0, 7.710559872308175E-06, 0, 38.11527381090911, 1.5148101671147525, 0.7575883267617542),
    ];

    /// <summary>
    /// <see cref="DrawingCanvas.Stroke(Pen, VectorPath)"/> of a path read from the input on a small canvas, compared with the
    /// stroke of the same path whose pieces are kept apart (<see cref="Stroker.KeepsPiecesApartOnCurrentThread"/>): the
    /// merged pieces must paint the same pixels. The input is a byte of width and one of height (1 to 48), a byte of pen
    /// width, a byte of join, cap and antialiasing, a byte of miter limit, a byte of dash count (0 to 3) and the dash
    /// lengths, a byte of scale or perspective, then commands: a byte (move, line, quadratic and cubic curve, close,
    /// ellipse) and its points as pairs of signed bytes in half pixels around the center of the canvas.
    /// </summary>
    /// <remarks>
    /// The round and square caps of dashes can end anywhere on a curve, and each one is a shape of the width of the pen: their
    /// pens are at most 9.5 pixels wide, so that an input stays fast (fuzz regressions: in perspective, round caps of pens of
    /// 24 pixels on the plane seen nearly edge-on took 10 to 35 seconds, whatever the length of the dashes; mirrored, dashes
    /// of 0.125 pixels with round caps of a pen of 64 pixels took 2.4 seconds). The caps of the dashes within the width of the
    /// pen overlap, and the rasterizer computes every crossing of their edges: the pens are also at most 8 dash periods wide
    /// (at least 1.5 pixels), so that a point is not under more than a few dozen caps (fuzz regression: square caps of a pen
    /// of 9.5 pixels on dashes of 0.125 pixels without gaps, 76 caps over each point of the curves, took 23 seconds). Each dash
    /// adds two caps: the dash patterns are then at least 2 pixels long (fuzz regression: a pattern of 1.375 pixels with round
    /// caps of a pen of 9.5 pixels took 24 seconds).
    /// </remarks>
    private static void StrokeTarget(ReadOnlySpan<byte> data)
    {
        var width = 1 + (FuzzInput.ReadByte(ref data) % 48);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 48);
        var penWidth = FuzzInput.Pick(StrokeWidths, FuzzInput.ReadByte(ref data));
        var style = FuzzInput.ReadByte(ref data);
        var miterLimit = 1 + (FuzzInput.ReadByte(ref data) / 16.0);
        var dashes = new double[FuzzInput.ReadByte(ref data) % 4];
        for (var i = 0; i < dashes.Length; i++)
        {
            dashes[i] = FuzzInput.ReadByte(ref data) / 8.0;
        }

        var view = FuzzInput.ReadByte(ref data) % (StrokeScales.Length + StrokePerspectives.Length);
        var center = new PointD(width / 2.0, height / 2.0);
        var isInPerspective = view >= StrokeScales.Length;
        var transform = (view - StrokeScales.Length) switch
        {
            < 0 => (ProjectiveTransform)AffineTransform.CreateScale(StrokeScales[view], StrokeScales[view], center),
            2 => StrokePerspectives[2].Then(AffineTransform.CreateTranslation(center.X - 50.31, center.Y - 2)),
            var index => ((ProjectiveTransform)AffineTransform.CreateTranslation(-center.X, -center.Y)).Then(StrokePerspectives[index]).Then(AffineTransform.CreateTranslation(center.X, center.Y)),
        };

        var builder = new VectorPathBuilder();
        var started = false;
        for (var commands = 0; !data.IsEmpty && commands < 64; commands++)
        {
            var command = FuzzInput.ReadByte(ref data) % 6;
            switch (command)
            {
                case 0:
                    builder.MoveTo(ReadStrokePoint(ref data, center));
                    started = true;
                    break;
                case 1 when started:
                    builder.LineTo(ReadStrokePoint(ref data, center));
                    break;
                case 2 when started:
                    builder.QuadraticBezierTo(ReadStrokePoint(ref data, center), ReadStrokePoint(ref data, center));
                    break;
                case 3 when started:
                    builder.CubicBezierTo(ReadStrokePoint(ref data, center), ReadStrokePoint(ref data, center), ReadStrokePoint(ref data, center));
                    break;
                case 4 when started:
                    builder.Close();
                    break;
                case 5:
                    // An ellipse of no height is a curve that turns back on itself
                    builder.AddEllipse(ReadStrokePoint(ref data, center), FuzzInput.ReadByte(ref data) / 4.0, FuzzInput.ReadByte(ref data) / 4.0);
                    break;
                default:
                    builder.MoveTo(ReadStrokePoint(ref data, center));
                    started = true;
                    break;
            }
        }

        var path = builder.Build();
        var cap = (LineCap)((style / 3) % 3);
        if (dashes.Length != 0 && cap != LineCap.Butt)
        {
            penWidth = Math.Min(penWidth, Math.Min(9.5, Math.Max(1.5, 8 * dashes.Sum())));

            // (a pattern of at least 2 pixels: each dash adds two caps)
            var patternLength = dashes.Sum();
            if (patternLength is > 0 and < 2)
            {
                for (var i = 0; i < dashes.Length; i++)
                {
                    dashes[i] *= 2 / patternLength;
                }
            }
        }

        Pen pen;
        try
        {
            pen = new Pen(new SolidBrush(new Rgba32(20, 40, 160, 200)), penWidth)
            {
                LineJoin = (LineJoin)(style % 3),
                LineCap = cap,
                MiterLimit = miterLimit,
                DashPattern = dashes,
            };
        }
        catch (ArgumentException)
        {
            // A dash pattern without length
            return;
        }

        var antialiasing = (style & 0x80) == 0;
        var merged = Render(width, height, path, pen, antialiasing, transform, keepPiecesApart: false);
        var apart = Render(width, height, path, pen, antialiasing, transform, keepPiecesApart: true);
        FuzzAssert.True(merged.AsSpan().SequenceEqual(apart), "The merged pieces of a stroke paint other pixels than the pieces kept apart");

        static byte[] Render(int width, int height, VectorPath path, Pen pen, bool antialiasing, ProjectiveTransform transform, bool keepPiecesApart)
        {
            using var image = new Image<Rgba32>(width, height);
            Stroker.KeepsPiecesApartOnCurrentThread = keepPiecesApart;
            try
            {
                image.Draw(canvas => canvas.Antialiasing(antialiasing, canvas => canvas.Transform(transform, canvas => canvas.Stroke(pen, path))));
            }
            finally
            {
                Stroker.KeepsPiecesApartOnCurrentThread = false;
            }

            var bytes = new byte[width * height * 4];
            image.Frames[0].CopyPixelBytesTo(bytes);
            return bytes;
        }
    }

    private static PointD ReadStrokePoint(ref ReadOnlySpan<byte> data, PointD center)
        => new(center.X + ((sbyte)FuzzInput.ReadByte(ref data) / 2.0), center.Y + ((sbyte)FuzzInput.ReadByte(ref data) / 2.0));

    private static IEnumerable<FuzzSeed> GetStrokeSeeds()
    {
        // The cases of the unit tests: a zigzag with each join and cap, a closed curve, a curve that turns back (an ellipse
        // of no height), a dashed polyline, a hairline outline of curves, a pen wider than the canvas
        yield return new FuzzSeed("zigzag-miter", (byte[])[40, 40, 2, 0, 64, 0, 0, 0, 0xE0, 0xF0, 1, 0xF0, 0x10, 1, 0, 0xF0, 1, 0x10, 0x10, 1, 0x20, 0xF0]);
        yield return new FuzzSeed("zigzag-round-square", (byte[])[40, 40, 4, 7, 64, 0, 0, 0, 0xE0, 0xF0, 1, 0xF0, 0x10, 1, 0, 0xF0, 1, 0x10, 0x10]);
        yield return new FuzzSeed("closed-curve", (byte[])[47, 47, 0, 1, 64, 0, 0, 0, 0xD0, 0, 2, 0xD0, 0xD0, 0, 0xD0, 2, 0x30, 0xD0, 0x30, 0, 2, 0x30, 0x30, 0, 0x30, 4]);
        yield return new FuzzSeed("turn-back", (byte[])[41, 8, 5, 1, 124, 0, 0, 5, 0x10, 0xF8, 64, 0]);
        yield return new FuzzSeed("dashed", (byte[])[40, 40, 2, 3, 64, 2, 48, 16, 0, 0, 0xE0, 0xF0, 1, 0xF0, 0x10, 1, 0, 0xF0, 3, 0x10, 0x10, 0x20, 0, 0x30, 0x20]);
        yield return new FuzzSeed("heading-outline", (byte[])[47, 30, 0, 0, 64, 0, 0, 0, 0xE0, 0x10, 3, 0xE0, 0xE0, 0x20, 0xE0, 0x20, 0x10, 3, 0x20, 0x30, 0xE0, 0x30, 0xE0, 0x10, 4]);
        yield return new FuzzSeed("wide", (byte[])[16, 12, 10, 2, 16, 0, 1, 0, 0xF0, 0xF0, 2, 0x40, 0, 0x10, 0x10]);
        yield return new FuzzSeed("aliased-mirrored", (byte[])[30, 30, 1, 0x81, 64, 0, 4, 5, 0, 0, 40, 20, 1, 0x30, 0x30]);

        // In perspective: a tilted dashed curve, a closed curve across a horizon, and a wide dashed pen on a circle of a
        // plane seen nearly edge-on, with and without dashes (fuzz regression)
        yield return new FuzzSeed("perspective-tilt", (byte[])[40, 40, 7, 4, 64, 2, 48, 16, 5, 0, 0xE0, 0xF0, 3, 0x10, 0x10, 0x20, 0, 0x30, 0x20]);
        yield return new FuzzSeed("perspective-horizon", (byte[])[47, 47, 7, 1, 64, 0, 6, 5, 0, 0, 120, 80]);
        yield return new FuzzSeed("perspective-edge-on-dashed", (byte[])[47, 47, 10, 0, 64, 2, 54, 38, 7, 5, 0xE3, 0x04, 55, 55]);
        yield return new FuzzSeed("perspective-edge-on", (byte[])[47, 47, 10, 0, 64, 0, 7, 5, 0xE3, 0x04, 55, 55, 0, 0x10, 0x20, 2, 0xE0, 0xE0, 0x30, 0xD0]);
    }
}
