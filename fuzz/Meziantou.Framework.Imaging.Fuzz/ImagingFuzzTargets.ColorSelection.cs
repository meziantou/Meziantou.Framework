using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>
    /// <see cref="PixelMask.FromSimilarColors(ImageFrame, Point, ColorSelectionOptions?, CancellationToken)"/> on the first
    /// frame of an image (<see cref="CreateImage"/>) preceded by a byte of flags (bit 0: every pixel of the color instead of
    /// the connected ones, bit 1: diagonals), a byte of tolerance (in 255ths), the two bytes of the seed and a byte of
    /// offset of a second mask read from the bits of the input. Only floating-point samples outside [0, 1] are rejected.
    /// Otherwise the frame is unchanged, the selection is the set of pixels whose 16-bit color is within the tolerance of
    /// the seed (compared through an <see cref="Rgba64"/> clone) or, when contiguous, the connected component of the seed
    /// in that set, and the component with sides only is inside the one with diagonals. The combinations with the second
    /// mask agree with the pixel by pixel operations, and the alpha image of the selection is read back as the same mask.
    /// </summary>
    private static void ColorSelectionTarget(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var tolerance = FuzzInput.ReadByte(ref data) / 255.0;
        var seedX = FuzzInput.ReadByte(ref data);
        var seedY = FuzzInput.ReadByte(ref data);
        var offset = FuzzInput.ReadByte(ref data);
        using var image = CreateImage(ref data, maxSide: 16);
        var frame = image.Frames[0];
        var seed = new Point(seedX % image.Width, seedY % image.Height);
        var contiguous = (flags & 1) == 0;
        var diagonals = (flags & 2) != 0;
        var options = new ColorSelectionOptions
        {
            Tolerance = tolerance,
            Contiguous = contiguous,
            Connectivity = diagonals ? PixelConnectivity.Eight : PixelConnectivity.Four,
        };

        var before = Snapshot(image);
        PixelMask selection;
        try
        {
            selection = PixelMask.FromSimilarColors(frame, seed, options);
        }
        catch (UnsupportedImageFeatureException ex)
        {
            FuzzAssert.True(PixelFormats.IsFloatingPoint(image.PixelFormat), $"Unexpected rejection of {image.PixelFormat} pixels: {ex.Message}");
            FuzzAssert.Equal(before, Snapshot(image), "A failed selection changed the image");
            return;
        }

        FuzzAssert.Equal(before, Snapshot(image), "The selection changed the image");
        FuzzAssert.Equal(image.Size, selection.Size, "The selection does not have the size of the frame");
        FuzzAssert.True(selection[seed.X, seed.Y], "The seed is not selected");

        var matches = GetMatches(image, seed, tolerance);
        if (!contiguous)
        {
            ForEachPixel(selection, (x, y) => FuzzAssert.Equal(matches[x, y], selection[x, y], $"Pixel ({x}, {y}) of a selection of the whole frame"));
        }
        else
        {
            CheckComponent(selection, matches, seed, diagonals);
            var other = PixelMask.FromSimilarColors(frame, seed, new ColorSelectionOptions { Tolerance = tolerance, Connectivity = diagonals ? PixelConnectivity.Four : PixelConnectivity.Eight });
            var (sides, corners) = diagonals ? (other, selection) : (selection, other);
            ForEachPixel(sides, (x, y) => FuzzAssert.True(!sides[x, y] || corners[x, y], $"Pixel ({x}, {y}) is selected by its sides and not with diagonals"));
        }

        CheckCombinations(selection, CreateMask(image.Size, offset, data));

        using var alpha = selection.ToImage();
        var roundTrip = PixelMask.FromFrame(alpha.Frames[0]);
        ForEachPixel(selection, (x, y) => FuzzAssert.Equal(selection[x, y], roundTrip[x, y], $"Pixel ({x}, {y}) changed through the alpha image"));
    }

    /// <summary>Gets the pixels whose four 16-bit samples are within the tolerance of those of the seed, read from an <see cref="Rgba64"/> clone of the first frame.</summary>
    private static PixelMask GetMatches(Image image, Point seed, double tolerance)
    {
        var steps = (int)Math.Floor((tolerance * ushort.MaxValue) + 0.5 + (1.0 / 1048576));
        using var clone = image.CloneAs<Rgba64>();
        var pixels = clone.Frames[0];
        var color = pixels[seed.X, seed.Y];
        var matches = new PixelMask(image.Size);
        ForEachPixel(matches, (x, y) =>
        {
            var pixel = pixels[x, y];
            matches[x, y] = Math.Abs(pixel.R - color.R) <= steps && Math.Abs(pixel.G - color.G) <= steps && Math.Abs(pixel.B - color.B) <= steps && Math.Abs(pixel.A - color.A) <= steps;
        });

        return matches;
    }

    /// <summary>Checks that a selection is the connected component of the seed in the matching pixels: inside them, closed under the neighbors, and reached from the seed.</summary>
    private static void CheckComponent(PixelMask selection, PixelMask matches, Point seed, bool diagonals)
    {
        var reached = new PixelMask(selection.Size);
        var pending = new Queue<Point>();
        reached[seed.X, seed.Y] = true;
        pending.Enqueue(seed);
        while (pending.TryDequeue(out var point))
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || (!diagonals && dx != 0 && dy != 0))
                        continue;

                    var (x, y) = (point.X + dx, point.Y + dy);
                    if (x < 0 || y < 0 || x >= selection.Width || y >= selection.Height || reached[x, y] || !matches[x, y])
                        continue;

                    reached[x, y] = true;
                    pending.Enqueue(new Point(x, y));
                }
            }
        }

        ForEachPixel(selection, (x, y) => FuzzAssert.Equal(reached[x, y], selection[x, y], $"Pixel ({x}, {y}) of a contiguous selection"));
    }

    /// <summary>Checks the combinations of two masks against the operations on each pixel.</summary>
    private static void CheckCombinations(PixelMask mask, PixelMask other)
    {
        var union = mask.Clone();
        union.UnionWith(other);
        var intersection = mask.Clone();
        intersection.IntersectWith(other);
        var difference = mask.Clone();
        difference.ExceptWith(other);
        var symmetric = mask.Clone();
        symmetric.SymmetricExceptWith(other);
        var inverse = mask.Clone();
        inverse.Invert();
        ForEachPixel(mask, (x, y) =>
        {
            var (a, b) = (mask[x, y], other[x, y]);
            FuzzAssert.Equal(a || b, union[x, y], $"Union at ({x}, {y})");
            FuzzAssert.Equal(a && b, intersection[x, y], $"Intersection at ({x}, {y})");
            FuzzAssert.Equal(a && !b, difference[x, y], $"Difference at ({x}, {y})");
            FuzzAssert.Equal(a != b, symmetric[x, y], $"Symmetric difference at ({x}, {y})");
            FuzzAssert.Equal(!a, inverse[x, y], $"Inverse at ({x}, {y})");
        });

        var total = mask.Width * mask.Height;
        FuzzAssert.Equal(total - mask.Count, inverse.Count, "The inverse does not count the other pixels");
        FuzzAssert.Equal(union.Count + intersection.Count, mask.Count + other.Count, "The counts of the union and the intersection");
        inverse.Invert();
        FuzzAssert.Equal(mask.Count, inverse.Count, "Inverting twice changed the mask");
    }

    private static void ForEachPixel(PixelMask mask, Action<int, int> action)
    {
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                action(x, y);
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetColorSelectionSeeds()
    {
        // The cases of the unit tests: a gray grid with sides, diagonals, a tolerance and the whole frame, transparent
        // colors, 16-bit samples, floats outside [0, 1], and an animation with a poster
        byte[] grid = [2, 4, 2, 0, 10, 10, 90, 10, 10, 10, 90, 10, 90, 10, 10, 10, 90, 10, 12];
        yield return new FuzzSeed("gray-sides", (byte[])[0, 0, 0, 0, 0, .. grid]);
        yield return new FuzzSeed("gray-diagonals", (byte[])[2, 2, 0, 0, 3, .. grid]);
        yield return new FuzzSeed("gray-whole-frame", (byte[])[1, 0, 1, 1, 7, .. grid]);
        yield return new FuzzSeed("transparent", (byte[])[0, 10, 1, 0, 0, 0, 3, 0, 0, 255, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0, 0]);
        yield return new FuzzSeed("gray16", (byte[])[0, 1, 0, 0, 0, 3, 2, 0, 0, 0xE8, 0x03, 0x4C, 0x04, 0xE8, 0x03]);
        yield return new FuzzSeed("floats", (byte[])[2, 0, 1, 0, 0, 6, 1, 0, 0, 0, 0, 0, 0x40, 0, 0, 0, 0x3F]);
        yield return new FuzzSeed("animation-poster", (byte[])[2, 40, 3, 2, 5, 0, 7, 5, 0x82, 10, 20, 30, 255, 10, 20, 31, 255, 200, 100, 50, 255]);
    }
}
