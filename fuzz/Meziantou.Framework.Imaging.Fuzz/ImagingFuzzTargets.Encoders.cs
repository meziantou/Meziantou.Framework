using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// The PSD, DNG and Cineon encoders. The input is a byte of settings, then a small image (<see cref="CreateImage"/>). An
/// encoder must reject what its format cannot store with an <see cref="UnsupportedImageFeatureException"/> (several frames,
/// transparency for DNG and Cineon, the samples DNG cannot store, a depth reduction Cineon is not allowed), write any other
/// image deterministically, and the decoders must read back the image: the same pixels for PSD (the colors of the pixels
/// that are not fully opaque are blended over white, so only their transparency is compared), the same stored samples for
/// DNG (<see cref="DngRawImage"/>), the same code values for Cineon.
/// </summary>
public static partial class ImagingFuzzTargets
{
    private static void PsdEncoderTarget(ReadOnlySpan<byte> data)
    {
        var settings = FuzzInput.ReadByte(ref data);
        using var image = CreateImage(ref data, maxSide: 12);
        var encoder = new PsdEncoder
        {
            Compression = (settings & 1) == 0 ? PsdCompression.RunLength : PsdCompression.None,
            LargeDocument = (settings & 2) != 0,
            MetadataHandling = MetadataHandling.Strip,
        };

        byte[]? encoded = null;
        var saved = Catch(() => encoded = Save(image, encoder));
        if (image.Frames.Count > 1 || image.PosterFrame is not null)
        {
            FuzzAssert.True(saved.Exception is UnsupportedImageFeatureException, "An animated image is saved as a PSD file");
            return;
        }

        FuzzAssert.True(encoded is not null, "The PSD encoder rejects an image: " + saved.Exception?.Message);
        FuzzAssert.True(encoded.AsSpan().SequenceEqual(Save(image, encoder)), "The PSD encoder is not deterministic");
        using var decoded = Image.Load(encoded);
        FuzzAssert.Equal(image.Size, decoded.Size, "The size of the PSD file");
        using var widened = Widen(image);
        if (PixelFormats.IsFloatingPoint(image.PixelFormat))
        {
            using var expected = widened.CloneAs<RgbaFloat>();
            using var actual = decoded.CloneAs<RgbaFloat>();
            for (var i = 0; i < image.Width * image.Height; i++)
            {
                var e = expected.Frames[0][i % image.Width, i / image.Width];
                var a = actual.Frames[0][i % image.Width, i / image.Width];
                FuzzAssert.True(SameFloat(e.A, a.A), "The PSD file stores another transparency");
                if (e.A == 1f)
                {
                    FuzzAssert.True(SameFloat(e.R, a.R) && SameFloat(e.G, a.G) && SameFloat(e.B, a.B), "The PSD file stores other colors");
                }
            }

            return;
        }

        using (var expected = widened.CloneAs<Rgba64>())
        using (var actual = decoded.CloneAs<Rgba64>())
        {
            for (var i = 0; i < image.Width * image.Height; i++)
            {
                var e = expected.Frames[0][i % image.Width, i / image.Width];
                var a = actual.Frames[0][i % image.Width, i / image.Width];
                FuzzAssert.Equal(e.A, a.A, "The transparency of the PSD file");
                if (e.A == ushort.MaxValue)
                {
                    FuzzAssert.Equal(e, a, "The colors of the PSD file");
                }
            }
        }
    }

    private static void DngEncoderTarget(ReadOnlySpan<byte> data)
    {
        var settings = FuzzInput.ReadByte(ref data);
        using var image = CreateImage(ref data, maxSide: 12);
        var encoder = new DngEncoder
        {
            Compression = (settings & 3) switch
            {
                0 => null,
                1 => DngCompression.None,
                2 => DngCompression.LosslessJpeg,
                _ => DngCompression.Deflate,
            },
            TileSize = ((settings >> 2) & 3) switch
            {
                0 => null,
                1 => 16,
                _ => 32,
            },
            MetadataHandling = MetadataHandling.Strip,
        };

        byte[]? encoded = null;
        var saved = Catch(() => encoded = Save(image, encoder));
        if (encoded is null)
        {
            // Several frames, transparency, samples that are not linear values, a compression of other samples
            FuzzAssert.True(saved.Exception is UnsupportedImageFeatureException, "The DNG encoder fails: " + saved.Exception?.Message);
            return;
        }

        FuzzAssert.True(image.Frames.Count == 1 && image.PosterFrame is null, "An animated image is saved as a DNG file");
        FuzzAssert.True(encoded.AsSpan().SequenceEqual(Save(image, encoder)), "The DNG encoder is not deterministic");
        using var decoded = Image.Load(encoded);
        FuzzAssert.Equal(image.Size, decoded.Size, "The size of the DNG file");

        // The stored samples are those of the image: gray, or red, green and blue, at the depth of the pixel format
        using var raw = DngRawImage.Load(encoded);
        using var widened = Widen(image);
        var isGray = PixelFormats.IsGrayscale(widened.PixelFormat);
        var samples = isGray ? 1 : 3;
        FuzzAssert.Equal(samples, raw.SamplesPerPixel, "The samples of a pixel of the DNG file");
        using var floats = widened.CloneAs<RgbaFloat>();
        var isFloat = PixelFormats.IsFloatingPoint(image.PixelFormat);
        var max = isFloat ? 1f : Is16Bit(widened.PixelFormat) ? 65535f : 255f;
        var row = new float[image.Width * samples];
        for (var y = 0; y < image.Height; y++)
        {
            raw.CopyStoredRow(y, row);
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = floats.Frames[0][x, y];
                float[] expected = isGray ? [pixel.R] : [pixel.R, pixel.G, pixel.B];
                for (var c = 0; c < samples; c++)
                {
                    var value = isFloat ? expected[c] : MathF.Round(expected[c] * max, MidpointRounding.AwayFromZero);
                    FuzzAssert.True(value == row[(x * samples) + c], "The DNG file stores other samples");
                }
            }
        }
    }

    private static void CineonEncoderTarget(ReadOnlySpan<byte> data)
    {
        var settings = FuzzInput.ReadByte(ref data);
        using var image = CreateImage(ref data, maxSide: 12);
        var encoder = new CineonEncoder
        {
            BitDepth = (settings & 3) switch
            {
                0 => null,
                1 => 8,
                2 => 10,
                _ => 16,
            },
            AllowBitDepthReduction = (settings & 4) != 0,
            MetadataHandling = MetadataHandling.Strip,
        };

        byte[]? encoded = null;
        var saved = Catch(() => encoded = Save(image, encoder));
        if (encoded is null)
        {
            // Several frames, transparency, a depth reduction that is not allowed, floating-point pixels without conversion
            FuzzAssert.True(saved.Exception is UnsupportedImageFeatureException, "The Cineon encoder fails: " + saved.Exception?.Message);
            return;
        }

        FuzzAssert.True(image.Frames.Count == 1 && image.PosterFrame is null && !PixelFormats.IsFloatingPoint(image.PixelFormat), "The Cineon encoder writes an image it cannot store");
        FuzzAssert.True(encoded.AsSpan().SequenceEqual(Save(image, encoder)), "The Cineon encoder is not deterministic");
        using var decoded = Image.Load(encoded);
        FuzzAssert.Equal(image.Size, decoded.Size, "The size of the Cineon file");

        // The code values hold the samples when they have at least their bits
        using var widened = Widen(image);
        var sourceBits = Is16Bit(widened.PixelFormat) ? 16 : 8;
        var bits = encoder.BitDepth ?? (sourceBits == 8 ? 10 : 16);
        if (bits < sourceBits)
            return;

        var isGray = PixelFormats.IsGrayscale(widened.PixelFormat);
        using var expected = sourceBits == 8 ? (Image)widened.CloneAs<Rgba32>() : widened.CloneAs<Rgba64>();
        using var actual = sourceBits == 8 ? (Image)decoded.CloneAs<Rgba32>() : decoded.CloneAs<Rgba64>();
        var e = GetBytes8Or16(expected);
        var a = GetBytes8Or16(actual);
        var stride = sourceBits == 8 ? 4 : 8;
        var componentBytes = sourceBits / 8;
        for (var i = 0; i < image.Width * image.Height; i++)
        {
            // A gray image is compared on its gray level
            var length = (isGray ? 1 : 3) * componentBytes;
            FuzzAssert.True(e.AsSpan(i * stride, length).SequenceEqual(a.AsSpan(i * stride, length)), "The Cineon file stores other samples");
        }
    }

    /// <summary>
    /// Gets a copy of an image in the format the encoders receive it: 5/6/5-bit pixels are widened to 8 bits, and an alpha
    /// mask is black gray with alpha (they store gray with alpha).
    /// </summary>
    private static Image Widen(Image image) => image.PixelFormat switch
    {
        PixelFormat.Bgr565 => image.CloneAs<Rgb24>(),
        PixelFormat.A8 => image.CloneAs<La16>(),
        _ => image.CloneAs(image.PixelFormat, options: null),
    };

    private static bool Is16Bit(PixelFormat format) => format is PixelFormat.Gray16 or PixelFormat.La32 or PixelFormat.Rgb48 or PixelFormat.Rgba64;

    private static bool SameFloat(float expected, float actual) => expected.Equals(actual) || (float.IsNaN(expected) && float.IsNaN(actual));

    private static byte[] GetBytes8Or16(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static IEnumerable<FuzzSeed> GetPsdEncoderSeeds()
    {
        // Settings, format, width, height, frames, then the pixels: RGBA with transparency, gray, 16-bit RGB, floats, a PSB file
        yield return new FuzzSeed("rgba-rle", (byte[])[0, 0, 5, 3, 0, 10, 20, 30, 128, 200, 100, 50, 255, 0, 0, 0, 0]);
        yield return new FuzzSeed("gray-raw", (byte[])[1, 2, 7, 2, 0, 0, 64, 128, 255]);
        yield return new FuzzSeed("rgb48-psb", (byte[])[2, 11, 3, 3, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
        yield return new FuzzSeed("float", (byte[])[0, 6, 2, 2, 0, 0, 0, 0x80, 0x3F, 0, 0, 0, 0x3F]);
        yield return new FuzzSeed("la16", (byte[])[0, 5, 4, 4, 0, 90, 100, 30, 255]);
        yield return new FuzzSeed("animated", (byte[])[0, 0, 2, 2, 2, 1, 2, 3, 4]);
    }

    private static IEnumerable<FuzzSeed> GetDngEncoderSeeds()
    {
        // Settings (compression, tiles), format, width, height, frames, then the pixels
        yield return new FuzzSeed("rgb-ljpeg", (byte[])[0, 1, 9, 5, 0, 10, 20, 30, 40, 50, 60]);
        yield return new FuzzSeed("rgb-tiles", (byte[])[5, 1, 12, 12, 0, 1, 2, 3, 4, 5, 6, 7]);
        yield return new FuzzSeed("gray16", (byte[])[1, 3, 4, 3, 0, 0x34, 0x12, 0xFF, 0xFF, 0, 0]);
        yield return new FuzzSeed("float-deflate", (byte[])[3, 6, 2, 2, 0, 0, 0, 0x80, 0x3F, 0, 0, 0x80, 0x3F]);
        yield return new FuzzSeed("float-negative", (byte[])[0, 6, 1, 1, 0, 0, 0, 0x80, 0xBF]);
        yield return new FuzzSeed("transparent", (byte[])[0, 0, 2, 2, 0, 10, 20, 30, 40]);
    }

    private static IEnumerable<FuzzSeed> GetCineonEncoderSeeds()
    {
        // Settings (depth, reduction), format, width, height, frames, then the pixels
        yield return new FuzzSeed("rgb10", (byte[])[0, 1, 7, 3, 0, 0, 64, 128, 192, 255]);
        yield return new FuzzSeed("gray10", (byte[])[2, 2, 5, 1, 0, 1, 2, 3, 4, 5]);
        yield return new FuzzSeed("rgb16", (byte[])[3, 11, 3, 2, 0, 0x34, 0x12, 0xFF, 0xFF]);
        yield return new FuzzSeed("reduced", (byte[])[6, 4, 2, 2, 0, 0x34, 0x12, 0xFF, 0x7F, 0, 0, 0xFF, 0xFF]);
        yield return new FuzzSeed("eight-bits", (byte[])[1, 0, 3, 3, 0, 255]);
    }
}
