using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.JpegXl;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Lossy (VarDCT) and progressive JPEG XL encoding through the public save API: the settings, the structure of the
/// output read by the independent reader of the harness (<see cref="ReferenceJpegXl"/>), the distance to the source once
/// decoded by the library decoder (itself compared with djxl and jxl-oxide in the conformance and interop suites), the
/// alpha channel, which is exact, and what the encoder refuses to store. djxl and jxl-oxide decode the outputs in the
/// interop job.
/// </summary>
public sealed class JpegXlLossyEncoderTests
{
    private static readonly byte[] ContainerSignature = [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    [Fact]
    public void SettingsHaveDocumentedDefaults()
    {
        var encoder = new JpegXlEncoder();
        Assert.Equal(JpegXlCompression.Lossless, encoder.Compression);
        Assert.Equal(75, encoder.Quality);
        Assert.False(encoder.Progressive);
    }

    [Fact]
    public void SettingsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegXlEncoder { Quality = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegXlEncoder { Quality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegXlEncoder { Compression = (JpegXlCompression)2 });
        Assert.Equal(0, new JpegXlEncoder { Quality = 0 }.Quality);
        Assert.Equal(100, new JpegXlEncoder { Quality = 100 }.Quality);
        Assert.Equal(JpegXlCompression.Lossy, new JpegXlEncoder { Compression = JpegXlCompression.Lossy }.Compression);
    }

    [Theory]
    [InlineData(PixelFormat.Gray8, false)]
    [InlineData(PixelFormat.Gray16, false)]
    [InlineData(PixelFormat.Rgb24, false)]
    [InlineData(PixelFormat.Rgba32, false)]
    [InlineData(PixelFormat.Bgra32, true)]
    [InlineData(PixelFormat.Rgba64, false)]
    [InlineData(PixelFormat.Rgb24, true)]
    [InlineData(PixelFormat.Rgba64, true)]
    public void LossyOutputIsCloseToTheSourceAndItsAlphaIsExact(PixelFormat format, bool progressive)
    {
        // One block, a partial block, one group, several groups with partial ones on both sides, and a side of one sample
        foreach (var (width, height) in new[] { (8, 8), (13, 9), (1, 1), (70, 1), (1, 70), (256, 256), (300, 270), (520, 31) })
        {
            using var source = CreatePhoto(width, height, seed: width + (int)format);
            using var image = ConvertTo(source, format);
            var data = Save(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 90, Progressive = progressive });
            var context = $"{format} {width}x{height}{(progressive ? " progressive" : "")}";

            // The structure: an image stored in XYB, one VarDCT frame, of one or three passes
            var bitsPerComponent = format is PixelFormat.Gray16 or PixelFormat.Rgba64 ? 16 : 8;
            var hasAlpha = format is PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgba64;
            var reference = ReferenceJpegXl.Parse(data);
            Assert.True(reference.IsXybEncoded, context);
            Assert.Equal((width, height, bitsPerComponent), (reference.Width, reference.Height, reference.BitsPerSample));
            Assert.HasCount(hasAlpha ? 1 : 0, reference.ExtraChannels);
            Assert.Equal(bitsPerComponent == 16, data.AsSpan().StartsWith(ContainerSignature));
            var frame = Assert.Single(reference.Frames!);
            Assert.Equal((0, false, false, progressive ? 3 : 1, true), (frame.Type, frame.IsModular, frame.IsYCbCr, frame.PassCount, frame.IsLast));

            var storedFormat = format == PixelFormat.Bgra32 ? PixelFormat.Rgba32 : format;
            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
            Assert.Equal((ImageFormat.JpegXl, width, height, storedFormat, bitsPerComponent, 1), (info.Format, info.Width, info.Height, info.PixelFormat, info.BitsPerComponent, info.FrameCount));

            using var decoded = Image.Load(data);
            Assert.Equal(storedFormat, decoded.PixelFormat);
            var (psnr, alphaIsExact) = Measure(image, decoded);
            Assert.True(alphaIsExact, $"{context}: the alpha channel differs.");

            // A side of a few samples has too few of them for the measure to mean much
            if (width >= 8 && height >= 8)
            {
                Assert.True(psnr >= 36, $"{context}: PSNR {psnr:F2} dB.");
            }
        }
    }

    [Fact]
    public void AHigherQualityIsCloserToTheSourceAndLarger()
    {
        using var image = CreatePhoto(300, 270, seed: 5);
        var previousLength = 0;
        var previousPsnr = 0.0;
        foreach (var quality in new[] { 0, 25, 50, 75, 90, 100 })
        {
            var data = Save(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = quality });
            using var decoded = Image.Load(data);
            var (psnr, _) = Measure(image, decoded);
            var length = data.Length;
            Assert.True(length > previousLength, $"quality {quality}: {length} bytes after {previousLength}.");
            Assert.True(psnr > previousPsnr, $"quality {quality}: {psnr:F2} dB after {previousPsnr:F2} dB.");
            previousLength = length;
            previousPsnr = psnr;
        }

        // The finest quantization is still lossy, and close
        Assert.True(previousPsnr >= 45, $"quality 100: {previousPsnr:F2} dB.");
    }

    [Theory]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Gray16)]
    public void ProgressiveLossyOutputDecodesToThePixelsOfASinglePass(PixelFormat format)
    {
        // The passes split the coefficients of a frame of one pass: their sum is the same coefficients
        foreach (var (width, height) in new[] { (13, 9), (300, 270) })
        {
            using var source = CreatePhoto(width, height, seed: 31);
            using var image = ConvertTo(source, format);
            var single = Save(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy });
            var progressive = Save(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Progressive = true });
            Assert.False(single.AsSpan().SequenceEqual(progressive));
            using var first = Image.Load<Rgba64>(single);
            using var second = Image.Load<Rgba64>(progressive);
            Assert.True(GetPixels(first).AsSpan().SequenceEqual(GetPixels(second)), $"{format} {width}x{height}: the decoded pixels differ.");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(9)]
    public void EveryEffortGivesAValidLossyFile(int effort)
    {
        using var source = CreatePhoto(300, 270, seed: 44);
        using var image = ConvertTo(source, PixelFormat.Rgba32);
        var data = Save(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 90, Effort = effort });
        using var decoded = Image.Load(data);
        var (psnr, alphaIsExact) = Measure(image, decoded);
        Assert.True(alphaIsExact);
        Assert.True(psnr >= 36, $"effort {effort}: PSNR {psnr:F2} dB.");

        // The same input gives the same bytes
        Assert.Equal(data, Save(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 90, Effort = effort }));
    }

    [Theory]
    [InlineData(PixelFormat.Gray8)]
    [InlineData(PixelFormat.Gray16)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Bgra32)]
    [InlineData(PixelFormat.Rgba64)]
    public void ProgressiveLosslessOutputRoundTripsExactly(PixelFormat format)
    {
        // An image that is not squeezed, one that is squeezed in one direction, one group, several groups, and sides of one sample
        foreach (var (width, height) in new[] { (8, 8), (5, 23), (1, 1), (70, 1), (1, 70), (256, 256), (300, 270), (520, 31), (257, 2) })
        {
            using var source = CreateNoisyImage(width, height, seed: width + (int)format);
            using var image = ConvertTo(source, format);
            foreach (var effort in new[] { 1, 5 })
            {
                var data = Save(image, new JpegXlEncoder { Progressive = true, Effort = effort });
                var context = $"{format} {width}x{height} effort {effort}";
                var reference = ReferenceJpegXl.Parse(data);
                Assert.False(reference.IsXybEncoded, context);
                var frame = Assert.Single(reference.Frames!);
                Assert.Equal((0, true, 3, true), (frame.Type, frame.IsModular, frame.PassCount, frame.IsLast));

                var storedFormat = format == PixelFormat.Bgra32 ? PixelFormat.Rgba32 : format;
                using var decoded = Image.Load(data);
                Assert.Equal(storedFormat, decoded.PixelFormat);
                using var expected = image.CloneAs<Rgba64>();
                using var actual = decoded.CloneAs<Rgba64>();
                Assert.True(GetPixels(expected).AsSpan().SequenceEqual(GetPixels(actual)), $"{context}: the decoded samples differ.");
            }
        }
    }

    [Fact]
    public void LossyAnimationsKeepTheirFramesAndTheirTiming()
    {
        using var image = CreatePhoto(40, 30, seed: 1);
        using (var second = CreatePhoto(40, 30, seed: 2))
        {
            image.AppendFrame(second.Frames[0]);
        }

        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 4);
        image.Animation = new AnimationMetadata { TotalPlays = 3 };
        var data = Save(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 90 });
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(2, decoded.Frames.Count);
        Assert.Equal(3, decoded.Animation?.TotalPlays);
        Assert.Equal((new FrameDuration(1, 10), new FrameDuration(1, 4)), (decoded.Frames[0].Metadata.Duration, decoded.Frames[1].Metadata.Duration));
        for (var i = 0; i < 2; i++)
        {
            using var expected = image.CloneFrame(i);
            using var actual = decoded.CloneFrame(i);
            var (psnr, alphaIsExact) = Measure(expected, actual);
            Assert.True(alphaIsExact && psnr >= 36, $"frame {i}: PSNR {psnr:F2} dB.");
        }
    }

    [Fact]
    public void AnSrgbProfileIsStoredWithLossyOutputAndAnotherProfileIsRefused()
    {
        using var image = CreatePhoto(16, 16, seed: 3);
        image.Metadata.IccProfile = IccProfile.Srgb;
        using (var decoded = Image.Load(Save(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy })))
        {
            Assert.Equal(IccProfile.Srgb.Data.ToArray(), decoded.Metadata.IccProfile?.Data.ToArray());
        }

        // A decoder converts XYB samples to the color space of the image, which it does not know for another profile
        image.Metadata.IccProfile = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new JpegXlEncoder { Compression = JpegXlCompression.Lossy }));
        Assert.Equal(ImageFormat.JpegXl, exception.Format);
        Assert.Equal(0, stream.Length);

        // The lossless coding stores the samples as they are, with their profile
        using var lossless = Image.Load(Save(image, new JpegXlEncoder()));
        Assert.Equal(image.Metadata.IccProfile.Data.ToArray(), lossless.Metadata.IccProfile?.Data.ToArray());
    }

    [Theory]
    [InlineData(9, 16)]
    [InlineData(9, 18)]
    public void LossyOutputRefusesTheTransferFunctionsItDoesNotImplement(byte primaries, byte transfer)
    {
        using var image = CreatePhoto(16, 16, seed: 3);
        image.Metadata.Cicp = new CicpColorDescription(primaries, transfer, 0, fullRange: true);
        using var stream = new MemoryStream();
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new JpegXlEncoder { Compression = JpegXlCompression.Lossy }));
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData(1, 13)]
    [InlineData(1, 8)]
    [InlineData(12, 13)]
    [InlineData(9, 1)]
    [InlineData(11, 17)]
    public void LossyOutputKeepsTheEnumeratedColorEncoding(byte primaries, byte transfer)
    {
        using var image = CreatePhoto(64, 48, seed: 9);
        var cicp = new CicpColorDescription(primaries, transfer, 0, fullRange: true);
        if (primaries != 1 || transfer != 13)
        {
            image.Metadata.Cicp = cicp;
        }

        using var decoded = Image.Load(Save(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 90 }));
        if (primaries == 1 && transfer == 8)
        {
            Assert.Equal(ColorTransferFunction.Linear, decoded.Metadata.TransferFunction);
        }
        else if (primaries != 1 || transfer != 13)
        {
            Assert.Equal((primaries, transfer), (decoded.Metadata.Cicp?.ColorPrimaries, decoded.Metadata.Cicp?.TransferCharacteristics));
        }

        // The samples are those of the color encoding: converting to XYB and back does not move them
        var (psnr, _) = Measure(image, decoded);
        Assert.True(psnr >= 34, $"primaries {primaries}, transfer {transfer}: PSNR {psnr:F2} dB.");
    }

    /// <summary>Measures the peak signal-to-noise ratio of the color samples, at 16 bits, and whether the alpha samples are equal.</summary>
    private static (double Psnr, bool AlphaIsExact) Measure(Image expected, Image actual)
    {
        using var first = expected.CloneAs<Rgba64>();
        using var second = actual.CloneAs<Rgba64>(new PixelConversionOptions { DiscardIncompatibleColorProfile = true });
        var a = GetPixels(first);
        var b = GetPixels(second);
        Assert.HasCount(a.Length, b);
        double squares = 0;
        var alphaIsExact = true;
        for (var i = 0; i < a.Length; i++)
        {
            double red = a[i].R - b[i].R;
            double green = a[i].G - b[i].G;
            double blue = a[i].B - b[i].B;
            squares += (red * red) + (green * green) + (blue * blue);
            alphaIsExact &= a[i].A == b[i].A;
        }

        var psnr = squares == 0 ? double.PositiveInfinity : 10 * Math.Log10(65535.0 * 65535.0 * a.Length * 3 / squares);
        return (psnr, alphaIsExact);
    }

    private static Rgba64[] GetPixels(Image<Rgba64> image)
    {
        var pixels = new Rgba64[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        return pixels;
    }

    private static Image ConvertTo(Image<Rgba32> source, PixelFormat format)
    {
        var flatten = new PixelConversionOptions { BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) };
        return format switch
        {
            PixelFormat.Rgba32 => source.Clone(),
            PixelFormat.Bgra32 => source.CloneAs<Bgra32>(),
            PixelFormat.Rgba64 => source.CloneAs<Rgba64>(),
            PixelFormat.Rgb24 => source.CloneAs<Rgb24>(flatten),
            PixelFormat.Gray8 => source.CloneAs<Gray8>(flatten),
            PixelFormat.Gray16 => source.CloneAs<Gray16>(flatten),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>Smooth colors with a soft texture, as a photograph has, and an alpha channel with opaque, translucent and transparent areas.</summary>
    private static Image<Rgba32> CreatePhoto(int width, int height, int seed)
    {
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var red = 128 + (90 * Math.Sin((x + seed) / 23.0) * Math.Cos(y / 17.0));
                var green = 128 + (70 * Math.Sin((x + (2 * y)) / 31.0)) + (20 * Math.Sin(x / 3.0) * Math.Sin(y / 4.0));
                var blue = 128 + (100 * Math.Cos((y + seed) / 29.0));
                var alpha = (x / 16) % 3 == 0 ? 255 : (x / 16) % 3 == 1 ? (x * 7) + (y * 3) : 0;
                pixels[(y * width) + x] = new Rgba32((byte)Math.Clamp(red, 0, 255), (byte)Math.Clamp(green, 0, 255), (byte)Math.Clamp(blue, 0, 255), (byte)alpha);
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    /// <summary>Flat areas, gradients and noise, with every alpha value.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image<Rgba32> CreateNoisyImage(int width, int height, int seed)
    {
        var random = new Random(seed);
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = (((x / 5) + (y / 30)) % 3) switch
                {
                    0 => new Rgba32(30, 60, 90, 255),
                    1 => new Rgba32((byte)(x * 5), (byte)(y * 7), (byte)(x + y), (byte)(x * 3)),
                    _ => new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)),
                };
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }
}
