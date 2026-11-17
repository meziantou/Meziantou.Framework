using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>The bytes of the contours of <see cref="SweepPiecesTarget"/>: point counts, the center, the ends of the signed range, and the piece limits.</summary>
    /// <remarks>A property: <see cref="All"/>, in another part of the class, may be initialized first.</remarks>
    private static string[] SweepPiecesTokens =>
    [
        "\0", "\u0001", "\u0003", "\u0004", "\u0008", "\u000C", "\u007F", "\u0080", "\u0081", "ÿ",
        "\u0003\u0000\u0000@\u0000\u0000@", "\u0004ðð\u0010ð\u0010\u0010ð\u0010",
    ];

    /// <summary>
    /// The coverages of contours read from the input, computed by the sweep and from the pieces of boundary of the rows
    /// (<see cref="CoverageRasterizer.RecordPieces"/>, as the workers of a large shape compute them): they must be the
    /// same, bit for bit, and the difference row must be all zeros after each row. The input is a byte of width (1 to 64),
    /// one of height (1 to 48), a byte of options (the fill rule, the piece limit that sends the rows with more pieces back
    /// to the sweep), then contours: a byte of point count (3 to 10) and the points as pairs of signed bytes in quarters of
    /// a pixel around the center of the frame, which may be outside it.
    /// </summary>
    private static void SweepPiecesTarget(ReadOnlySpan<byte> data)
    {
        var width = 1 + (FuzzInput.ReadByte(ref data) % 64);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 48);
        var options = FuzzInput.ReadByte(ref data);
        var fillRule = (options & 1) == 0 ? FillRule.NonZero : FillRule.EvenOdd;
        var pieceLimit = (options >> 1) is var limit && limit >= 64 ? CoverageRasterizer.MaxPiecesPerRow : limit;
        var contours = new List<(double X, double Y)[]>();
        while (!data.IsEmpty && contours.Count < 32)
        {
            var points = new (double X, double Y)[3 + (FuzzInput.ReadByte(ref data) % 8)];
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = ((width / 2.0) + ((sbyte)FuzzInput.ReadByte(ref data) / 4.0), (height / 2.0) + ((sbyte)FuzzInput.ReadByte(ref data) / 4.0));
            }

            contours.Add(points);
        }

        var swept = Rasterize(contours, width, height, fillRule, pieceLimit: -1);
        var pieces = Rasterize(contours, width, height, fillRule, pieceLimit);
        FuzzAssert.Equal(swept.Count, pieces.Count, "The pieces of boundary give another number of rows than the sweep");
        for (var i = 0; i < swept.Count; i++)
        {
            FuzzAssert.Equal(swept[i], pieces[i], "The pieces of boundary give other coverages than the sweep");
        }

        // A piece limit of -1 gives the coverages of the sweep
        static List<string> Rasterize(List<(double X, double Y)[]> contours, int width, int height, FillRule fillRule, int pieceLimit)
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

            using var rasterizer = new CoverageRasterizer(scope, width, height);
            var runs = new List<string>();
            if (!rasterizer.Begin(edges, fillRule, CancellationToken.None))
                return runs;

            if (pieceLimit >= 0)
            {
                rasterizer.PieceLimit = pieceLimit;
                FuzzAssert.True(rasterizer.RecordPieces() == rasterizer.CanRecordPieces, "RecordPieces disagrees with CanRecordPieces");
            }

            var accumulation = new double[width + 2];
            var coverage = new uint[width];
            while (rasterizer.MoveNext())
            {
                if (!rasterizer.HasPieces)
                {
                    runs.Add(FormattableString.Invariant($"{rasterizer.Y}:{rasterizer.X}:{string.Join(',', rasterizer.Coverage.ToArray())}"));
                    continue;
                }

                FuzzAssert.True(rasterizer.Pieces.Length % CoverageRasterizer.ValuesPerPiece == 0, "A piece of boundary is cut");
                FuzzAssert.True(rasterizer.PieceColumns >= 1 && rasterizer.PieceColumns <= width, "The columns of the pieces are outside the frame");
                if (CoverageRasterizer.ReplayPieces(rasterizer.Pieces, accumulation, coverage, width, out var x, out var count))
                {
                    FuzzAssert.True(x >= 0 && count >= 1 && x + count <= width, "The run of the pieces is outside the frame");
                    runs.Add(FormattableString.Invariant($"{rasterizer.Y}:{x}:{string.Join(',', coverage.AsSpan(x, count).ToArray())}"));
                }

                FuzzAssert.True(Array.TrueForAll(accumulation, value => value == 0), "The difference row is not all zeros after a row");
            }

            return runs;
        }
    }

    private static IEnumerable<FuzzSeed> GetSweepPiecesSeeds()
    {
        // The shapes of the unit tests: a triangle, a square in each fill rule, two overlapping squares, a star that
        // crosses itself, a zigzag with more pieces than a small limit, contours beyond the frame
        yield return new FuzzSeed("triangle", (byte[])[40, 30, 0x80, 3, 0xD0, 0xD8, 0x30, 0xE0, 0x00, 0x28]);
        yield return new FuzzSeed("square-evenodd", (byte[])[32, 32, 0x81, 4, 0xE1, 0xE3, 0x1F, 0xE3, 0x1F, 0x1D, 0xE1, 0x1D]);
        yield return new FuzzSeed("overlapping-squares", (byte[])[48, 40, 0x80, 4, 0xC0, 0xC8, 0x10, 0xC8, 0x10, 0x18, 0xC0, 0x18, 4, 0xF0, 0xE0, 0x40, 0xE0, 0x40, 0x30, 0xF0, 0x30]);
        yield return new FuzzSeed("star", (byte[])[64, 48, 0x01, 5, 0x00, 0xA0, 0x38, 0x48, 0xA8, 0xE8, 0x58, 0xE8, 0xC8, 0x48]);
        yield return new FuzzSeed("zigzag-small-limit", (byte[])[64, 32, 0x04, 10, 0x81, 0xC4, 0x9A, 0x3C, 0xB3, 0xC4, 0xCC, 0x3C, 0xE5, 0xC4, 0xFE, 0x3C, 0x17, 0xC4, 0x30, 0x3C, 0x49, 0xC4, 0x7F, 0x50]);
        yield return new FuzzSeed("beyond-the-frame", (byte[])[20, 16, 0x02, 4, 0x80, 0x80, 0x7F, 0x90, 0x70, 0x7F, 0x88, 0x60]);
    }
}
