using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>
    /// <see cref="ImageProcessingExtensions.SeamCarve(Image, int, int, SeamCarvingOptions?, CancellationToken)"/> on an image
    /// (<see cref="CreateImage"/>) preceded by two bytes of target size, a byte of masks and a byte of hotspot. A carve that
    /// fails (a removed hotspot, a sample a format cannot store) leaves the image exactly as it was; one that succeeds gives
    /// the target size on every frame, keeps a hotspot inside the canvas, and gives the same pixels when it runs again on a
    /// copy (determinism). A reduction by one column of an opaque 8-bit image gives pixels that all come from the image.
    /// </summary>
    private static void SeamCarvingTarget(ReadOnlySpan<byte> data)
    {
        var targetWidth = 1 + (FuzzInput.ReadByte(ref data) % 24);
        var targetHeight = 1 + (FuzzInput.ReadByte(ref data) % 24);
        var masks = FuzzInput.ReadByte(ref data);
        var hotspot = FuzzInput.ReadByte(ref data);
        using var image = CreateImage(ref data, maxSide: 12);
        if (hotspot != 0)
        {
            image.Frames[0].Metadata.Hotspot = new Point(hotspot % 16 % image.Width, hotspot / 16 % image.Height);
        }

        var options = new SeamCarvingOptions
        {
            ProtectedRegion = (masks & 1) != 0 ? CreateMask(image.Size, masks >> 2, data) : null,
            RemovedRegion = (masks & 2) != 0 ? CreateMask(image.Size, masks >> 5, data) : null,
        };

        using var copy = image.Clone();
        var before = Snapshot(image);
        try
        {
            image.SeamCarve(targetWidth, targetHeight, options);
        }
        catch (UnsupportedImageFeatureException ex)
        {
            FuzzAssert.Equal(before, Snapshot(image), $"A failed seam carving changed the image: {ex.Message}");
            return;
        }

        var size = new Size(targetWidth, targetHeight);
        FuzzAssert.Equal(size, image.Size, "The image does not have the target size");
        FuzzAssert.True(image.PosterFrame is null || image.PosterFrame.Size == size, "The poster does not have the target size");
        if (image.Frames[0].Metadata.Hotspot is { } moved)
        {
            FuzzAssert.True(moved.X < targetWidth && moved.Y < targetHeight, $"The hotspot {moved} is outside the canvas");
        }

        copy.SeamCarve(targetWidth, targetHeight, options);
        FuzzAssert.Equal(Snapshot(image), Snapshot(copy), "Two seam carvings of the same image differ");
    }

    /// <summary>Creates a mask whose pixels are set by the bits of the input, after an offset.</summary>
    private static PixelMask CreateMask(Size size, int offset, ReadOnlySpan<byte> data)
    {
        var mask = new PixelMask(size);
        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                var bit = offset + (y * size.Width) + x;
                if (!data.IsEmpty && (data[bit / 8 % data.Length] & (1 << (bit % 8))) != 0)
                {
                    mask[x, y] = true;
                }
            }
        }

        return mask;
    }

    private static IEnumerable<FuzzSeed> GetSeamCarvingSeeds()
    {
        // The cases of the unit tests: a reduction of a row and of a column, an enlargement, masks, an animation with a
        // hotspot, a translucent blend, and the other pixel formats
        yield return new FuzzSeed("row-reduction", (byte[])[2, 0, 0, 0, 2, 3, 0, 0, 10, 30, 200, 210]);
        yield return new FuzzSeed("column-reduction", (byte[])[0, 2, 0, 0, 2, 0, 3, 0, 10, 30, 200, 210]);
        yield return new FuzzSeed("enlargement", (byte[])[4, 0, 0, 0, 2, 3, 0, 0, 10, 30, 200, 210]);
        yield return new FuzzSeed("masks", (byte[])[3, 5, 3 | (1 << 2), 0, 1, 7, 5, 0, 250, 250, 250, 255, 0, 0, 0x0F, 0xF0]);
        yield return new FuzzSeed("animation-hotspot", (byte[])[3, 2, 0, 0x21, 1, 4, 2, 0x81, 10, 10, 10, 250, 250, 250, 250, 250, 250, 10, 10, 10]);
        yield return new FuzzSeed("translucent", (byte[])[2, 0, 0, 0, 0, 1, 0, 0, 200, 100, 50, 255, 0, 0, 0, 0]);
        yield return new FuzzSeed("half-floats", (byte[])[1, 1, 0, 0, 7, 3, 1, 0, 0x00, 0x3C, 0x00, 0x38, 0xFF, 0x7F, 0x00, 0x3C]);
        yield return new FuzzSeed("packed", (byte[])[5, 5, 2, 0, 8, 6, 6, 1, 0x1F, 0x00, 0xE0, 0x07, 0x00, 0xF8]);
    }
}
