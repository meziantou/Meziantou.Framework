using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>
    /// <see cref="ImageProcessingExtensions.Inpaint(Image, PixelMask, InpaintingOptions?, CancellationToken)"/> on an image
    /// (<see cref="CreateImage"/>) preceded by a byte of radius and a byte of mask offset; the mask is read from the bits of
    /// the input. A mask of every pixel is rejected; a failure leaves the image unchanged. Otherwise the pixels outside the
    /// mask keep their bytes, the fill of a uniform opaque 8-bit image is that color, and a second run gives the same pixels.
    /// </summary>
    private static void InpaintingTarget(ReadOnlySpan<byte> data)
    {
        var radius = 1 + (FuzzInput.ReadByte(ref data) % 8);
        var offset = FuzzInput.ReadByte(ref data);
        using var image = CreateImage(ref data, maxSide: 16);
        var mask = CreateMask(image.Size, offset, data);
        using var copy = image.Clone();
        var before = Snapshot(image);
        var options = new InpaintingOptions { Radius = radius };
        try
        {
            image.Inpaint(mask, options);
        }
        catch (Exception ex) when (ex is ArgumentException or UnsupportedImageFeatureException)
        {
            FuzzAssert.True(ex is UnsupportedImageFeatureException || mask.Count == image.Width * image.Height, $"Unexpected rejection: {ex.Message}");
            FuzzAssert.Equal(before, Snapshot(image), $"A failed inpainting changed the image: {ex.Message}");
            return;
        }

        var frames = image.PosterFrame is null ? [.. image.Frames] : (ImageFrame[])[.. image.Frames, image.PosterFrame];
        var originals = copy.PosterFrame is null ? [.. copy.Frames] : (ImageFrame[])[.. copy.Frames, copy.PosterFrame];
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(image.PixelFormat);
        for (var i = 0; i < frames.Length; i++)
        {
            var after = new byte[image.Width * image.Height * bytesPerPixel];
            var original = new byte[after.Length];
            frames[i].CopyPixelBytesTo(after);
            originals[i].CopyPixelBytesTo(original);
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    if (mask[x, y])
                        continue;

                    var start = ((y * image.Width) + x) * bytesPerPixel;
                    FuzzAssert.True(after.AsSpan(start, bytesPerPixel).SequenceEqual(original.AsSpan(start, bytesPerPixel)), $"Pixel ({x}, {y}) of frame {i} is outside the mask and changed");
                }
            }
        }

        copy.Inpaint(mask, options);
        FuzzAssert.Equal(Snapshot(image), Snapshot(copy), "Two inpaintings of the same image differ");
    }

    private static IEnumerable<FuzzSeed> GetInpaintingSeeds()
    {
        // The cases of the unit tests: a scratch, a uniform region, a ramp, every frame with a poster, translucent pixels
        yield return new FuzzSeed("scratch", (byte[])[4, 0, 1, 15, 9, 0, 50, 40, 120, 54, 45, 120, 0xFF, 0x00, 0x00]);
        yield return new FuzzSeed("uniform-translucent", (byte[])[4, 3, 0, 8, 6, 0, 30, 60, 90, 200]);
        yield return new FuzzSeed("ramp-gray16", (byte[])[2, 1, 3, 15, 7, 0, 100, 0, 110, 0, 120, 0, 130, 0]);
        yield return new FuzzSeed("animation-poster", (byte[])[0, 5, 1, 4, 4, 0x81, 10, 20, 30, 200, 100, 50]);
        yield return new FuzzSeed("premultiplied", (byte[])[0, 1, 0, 2, 0, 0, 255, 0, 0, 255, 0, 0, 0, 0, 0, 255, 0, 0]);
        yield return new FuzzSeed("full-mask", (byte[])[0, 0, 2, 1, 1, 0, 0xFF]);
        yield return new FuzzSeed("floats", (byte[])[3, 2, 6, 3, 3, 0, 0, 0, 0x80, 0x3F, 0, 0, 0x80, 0x7F, 0, 0, 0, 0]);
    }
}
