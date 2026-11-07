using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Jpeg2000;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// JPEG 2000 encoding through the public save and writer APIs: the bytes of a hand-computed main header, the boxes of a
/// JP2 file, exact round trips of every pixel format through the library decoder (itself compared with OpenJPEG on files
/// written by opj_compress), the lossy mode, the metadata policy and streaming. OpenJPEG and FFmpeg decode the outputs in
/// the interop job.
/// </summary>
public sealed class Jpeg2000EncoderTests
{
    private static readonly byte[] FileSignature = [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    // A little-endian TIFF header and an image file directory without entry
    private static readonly byte[] EmptyExif = [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    [Fact]
    public void SettingsHaveDocumentedDefaults()
    {
        var encoder = new Jpeg2000Encoder();
        Assert.Equal(ImageFormat.Jpeg2000, encoder.Format);
        Assert.Equal(Jpeg2000Compression.Lossless, encoder.Compression);
        Assert.Equal(90, encoder.Quality);
        Assert.Equal(Jpeg2000Container.Jp2, encoder.Container);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);
        Assert.Empty(encoder.QualityLayers);
        Assert.Equal(Jpeg2000ProgressionOrder.Lrcp, encoder.ProgressionOrder);
        Assert.Equal((0, 0), (encoder.TileWidth, encoder.TileHeight));
        Assert.Equal(5, encoder.DecompositionLevels);
        Assert.Equal(Jpeg2000BlockCoder.Standard, encoder.BlockCoder);
    }

    [Fact]
    public void SettingsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { Quality = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { Quality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { Compression = (Jpeg2000Compression)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { Container = (Jpeg2000Container)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { BlockCoder = (Jpeg2000BlockCoder)2 });
        Assert.Equal(1, new Jpeg2000Encoder { Quality = 1 }.Quality);
        Assert.Equal(100, new Jpeg2000Encoder { Quality = 100 }.Quality);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { ProgressionOrder = (Jpeg2000ProgressionOrder)5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { TileWidth = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { TileHeight = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { DecompositionLevels = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { DecompositionLevels = 33 });
        Assert.Equal(32, new Jpeg2000Encoder { DecompositionLevels = 32 }.DecompositionLevels);
        Assert.Throws<ArgumentNullException>(() => new Jpeg2000Encoder { QualityLayers = null! });
        Assert.Throws<ArgumentNullException>(() => new Jpeg2000Encoder { QualityLayers = [null!] });
        Assert.Throws<ArgumentException>(() => new Jpeg2000Encoder { QualityLayers = [Jpeg2000QualityLayer.Complete, Jpeg2000QualityLayer.FromSize(100)] });
        Assert.Throws<ArgumentException>(() => new Jpeg2000Encoder { QualityLayers = Enumerable.Repeat(Jpeg2000QualityLayer.FromSize(100), 65536).ToArray() });
        Assert.Throws<ArgumentOutOfRangeException>(() => Jpeg2000QualityLayer.FromSize(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Jpeg2000QualityLayer.FromBitsPerPixel(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Jpeg2000QualityLayer.FromBitsPerPixel(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Jpeg2000QualityLayer.FromBitsPerPixel(double.PositiveInfinity));

        // The list is copied
        var layers = new List<Jpeg2000QualityLayer> { Jpeg2000QualityLayer.FromSize(100) };
        var encoder = new Jpeg2000Encoder { QualityLayers = layers };
        layers.Add(Jpeg2000QualityLayer.Complete);
        Assert.HasCount(1, encoder.QualityLayers);
        Assert.Equal((100L, null, false), (encoder.QualityLayers[0].Size, encoder.QualityLayers[0].BitsPerPixel, encoder.QualityLayers[0].IsComplete));
        Assert.Equal((null, 0.5, false), (Jpeg2000QualityLayer.FromBitsPerPixel(0.5).Size, Jpeg2000QualityLayer.FromBitsPerPixel(0.5).BitsPerPixel, Jpeg2000QualityLayer.FromBitsPerPixel(0.5).IsComplete));
        Assert.True(Jpeg2000QualityLayer.Complete.IsComplete);
    }

    /// <summary>
    /// The main header of an 8x8 black RGB image, marker segment by marker segment (T.800, Annex A): SOC; SIZ (no
    /// capability, a reference grid of 8x8 at the origin, one tile of 8x8, three unsigned 8-bit components that are not
    /// sub-sampled); COD (no precinct, no SOP or EPH marker, the LRCP order, one layer, the component transformation, three
    /// decomposition levels, code-blocks of 64x64 without style, the reversible filter); QCD (no quantization, two guard
    /// bits, and for each of the ten sub-bands the exponent 8 plus its gain: 0 for LL, 1 for HL and LH, 2 for HH); then the
    /// SOT marker segment of the only tile-part of the tile 0.
    /// </summary>
    [Fact]
    public void TheMainHeaderOfASmallImageIsWrittenByteByByte()
    {
        using var image = new Image<Rgb24>(8, 8);
        var data = Save(image, new Jpeg2000Encoder { Container = Jpeg2000Container.Codestream });
        byte[] expected =
        [
            0xFF, 0x4F,
            0xFF, 0x51, 0x00, 0x2F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x07, 0x01, 0x01, 0x07, 0x01, 0x01, 0x07, 0x01, 0x01,
            0xFF, 0x52, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x01, 0x01, 0x03, 0x04, 0x04, 0x00, 0x01,
            0xFF, 0x5C, 0x00, 0x0D, 0x40, 0x40, 0x48, 0x48, 0x50, 0x48, 0x48, 0x50, 0x48, 0x48, 0x50,
            0xFF, 0x90, 0x00, 0x0A, 0x00, 0x00,
        ];
        Assert.Equal(expected, data[..expected.Length]);

        // The length of the tile-part, from its SOT marker to the EOC marker, then its index and the number of tile-parts
        var tilePart = data.AsSpan(expected.Length - 6);
        Assert.Equal((uint)(tilePart.Length - 2), BinaryPrimitives.ReadUInt32BigEndian(tilePart[6..]));
        Assert.Equal([0x00, 0x01, 0xFF, 0x93], tilePart.Slice(10, 4).ToArray());
        Assert.Equal([0xFF, 0xD9], data[^2..]);
    }

    [Theory]
    [InlineData(PixelFormat.Gray8, 8)]
    [InlineData(PixelFormat.Gray16, 16)]
    [InlineData(PixelFormat.Rgb24, 8)]
    [InlineData(PixelFormat.Rgba32, 8)]
    [InlineData(PixelFormat.Bgra32, 8)]
    [InlineData(PixelFormat.Rgba64, 16)]
    public void EveryPixelFormatRoundTripsExactly(PixelFormat format, int bitsPerComponent)
    {
        // Sides of one sample, sizes without decomposition level, one code-block, several code-blocks with partial ones on
        // both sides, and a side of exactly one and two code-blocks at the lowest resolution level
        foreach (var (width, height) in new[] { (23, 11), (8, 16), (1, 1), (1, 70), (70, 1), (300, 270), (64, 64), (65, 2), (130, 129) })
        {
            foreach (var container in new[] { Jpeg2000Container.Jp2, Jpeg2000Container.Codestream })
            {
                using var source = CreateImage(width, height, seed: (int)format + width);
                using var image = ConvertTo(source, format);
                var data = Save(image, new Jpeg2000Encoder { Container = container });
                var context = $"{format} {width}x{height} {container}";

                var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
                var storedFormat = format == PixelFormat.Bgra32 ? PixelFormat.Rgba32 : format;
                Assert.Equal((ImageFormat.Jpeg2000, width, height, storedFormat, bitsPerComponent), (info.Format, info.Width, info.Height, info.PixelFormat, info.BitsPerComponent));
                Assert.Equal((1, false), (info.FrameCount, info.IsAnimated));
                Assert.Equal(format is PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgba64, info.MayHaveTransparency);

                using var decoded = Image.Load(data);
                Assert.Equal(storedFormat, decoded.PixelFormat);
                Assert.True(GetBytes(image, storedFormat).AsSpan().SequenceEqual(GetBytes(decoded, storedFormat)), $"{context}: the decoded samples differ.");
                Assert.Equal(container == Jpeg2000Container.Jp2, data.AsSpan().StartsWith(FileSignature));
            }
        }
    }

    [Fact]
    public void HiddenColorsOfTransparentPixelsArePreserved()
    {
        Rgba32[] pixels = [new(10, 20, 30, 0), new(200, 100, 50, 0), new(10, 20, 30, 0), new(0, 0, 0, 0)];
        using var image = Image.ImportPixelData<Rgba32>(pixels, 2, 2);
        using var decoded = Image.Load<Rgba32>(Save(image, new Jpeg2000Encoder()));
        Assert.Equal(pixels, GetPixels(decoded.Frames[0]));
    }

    [Theory]
    [InlineData(PixelFormat.Gray8)]
    [InlineData(PixelFormat.Gray16)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Rgba64)]
    public void QualityTradesSizeForFidelity(PixelFormat format)
    {
        using var source = CreateImage(160, 120, seed: 9);
        using var image = ConvertTo(source, format);
        var expected = GetBytes(image, format);
        var bytesPerSample = PixelFormats.GetBitsPerComponent(format) / 8;
        var hasAlpha = PixelFormats.HasAlpha(format);
        var channels = PixelFormats.GetBytesPerPixel(format) / bytesPerSample;
        var sizes = new List<int>();
        var errors = new List<double>();
        foreach (var quality in new[] { 10, 50, 90, 100 })
        {
            var encoder = new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy, Quality = quality };
            var data = Save(image, encoder);
            using var decoded = Image.Load(data);
            Assert.Equal(image.PixelFormat, decoded.PixelFormat);
            var actual = GetBytes(decoded, format);
            var sum = 0.0;
            for (var i = 0; i < expected.Length; i += bytesPerSample)
            {
                var difference = bytesPerSample == 1 ? expected[i] - actual[i] : (BitConverter.ToUInt16(expected, i) - BitConverter.ToUInt16(actual, i)) / 257.0;
                if (hasAlpha && (i / bytesPerSample) % channels == channels - 1)
                {
                    // The opacity is stored without loss, whatever the quality
                    Assert.True(difference == 0, $"{format}, quality {quality}: the opacity of the sample {i / bytesPerSample} differs.");
                }

                sum += difference * difference;
            }

            sizes.Add(data.Length);
            errors.Add(Math.Sqrt(sum / (expected.Length / bytesPerSample)));

            // The output only depends on the image and on the settings
            Assert.Equal(data, Save(image, encoder));
        }

        // A better quality costs more bytes and leaves a smaller error, in levels of an 8-bit sample
        for (var i = 1; i < sizes.Count; i++)
        {
            Assert.True(sizes[i] > sizes[i - 1], $"{format}: sizes {string.Join(", ", sizes)}.");
            Assert.True(errors[i] < errors[i - 1], $"{format}: errors {string.Join(", ", errors)}.");
        }

        Assert.True(errors[0] < 45 && errors[2] < 6 && errors[3] < 0.6, $"{format}: errors {string.Join(", ", errors)}.");
    }

    /// <summary>
    /// An image of one sample has no decomposition level: its only sub-band holds the samples, and the step size of that
    /// sub-band is the base step of the quality. With the scale s of the JPEG encoder (1 at the quality 100, 50 at 75, 100
    /// at 50 and 500 at 10), the base step is 0.47 * s^(3/4) levels of an 8-bit sample: 0.47, 8.837, 14.863 and 49.696.
    /// Equation E-3 writes a step size as 2^(Rb - exponent) * (1 + mantissa / 2^11), with Rb = 8 for samples of 8 bits:
    /// 0.47 = 2^-2 * 1.88 is the exponent 10 and the mantissa floor(0.88 * 2048) = 1802; 8.837 = 2^3 * 1.10468 is 5 and 214;
    /// 14.863 = 2^3 * 1.85784 is 5 and 1756; 49.696 = 2^5 * 1.55301 is 3 and 1132. Samples of 16 bits have steps 256 times
    /// larger, which are the same parameters with Rb = 16. The two chrominances are quantized twice as coarsely: one
    /// exponent less and the same mantissa, in a QCC marker segment each.
    /// </summary>
    [Theory]
    [InlineData(100, 10, 1802)]
    [InlineData(75, 5, 214)]
    [InlineData(50, 5, 1756)]
    [InlineData(10, 3, 1132)]
    public void TheStepSizesFollowTheQualityScale(int quality, int exponent, int mantissa)
    {
        const int MarkerQcd = 0xFF5C;
        const int MarkerQcc = 0xFF5D;
        const int ScalarExpounded = 2;
        var encoder = new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy, Quality = quality, Container = Jpeg2000Container.Codestream };
        foreach (var format in new[] { PixelFormat.Gray8, PixelFormat.Gray16, PixelFormat.Rgb24, PixelFormat.Rgba32, PixelFormat.Rgba64 })
        {
            using var source = new Image<Rgba32>(1, 1);
            using var image = ConvertTo(source, format);
            var segments = ReadMainHeader(Save(image, encoder));

            // Sqcd (the guard bits and the style), then one step size
            var defaults = segments.Single(segment => segment.Marker == MarkerQcd).Payload;
            Assert.Equal((3, ScalarExpounded), (defaults.Length, defaults[0] & 0x1F));
            Assert.Equal((exponent << 11) | mantissa, BinaryPrimitives.ReadUInt16BigEndian(defaults.AsSpan(1)));

            var components = segments.Where(segment => segment.Marker == MarkerQcc).Select(segment => segment.Payload).ToArray();
            if (PixelFormats.IsGrayscale(format))
            {
                Assert.Empty(components);
                continue;
            }

            // Cqcc, Sqcc, then one step size; an opacity has a third segment, without quantization
            Assert.HasCount(PixelFormats.HasAlpha(format) ? 3 : 2, components);
            for (var c = 1; c <= 2; c++)
            {
                var chrominance = components[c - 1];
                Assert.Equal((4, c, defaults[0]), (chrominance.Length, chrominance[0], chrominance[1]));
                Assert.Equal(((exponent - 1) << 11) | mantissa, BinaryPrimitives.ReadUInt16BigEndian(chrominance.AsSpan(2)));
            }
        }
    }

    /// <summary>
    /// The quality is calibrated on the one of <see cref="JpegEncoder"/>: at the same setting, the error of the
    /// luminance is about the same (within the bounds below; on photographs the structural similarity is the same), the
    /// chrominances are not worse than the sub-sampled ones of the JPEG file, and the output is smaller.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(90)]
    [InlineData(95)]
    public void TheQualityFollowsTheScaleOfTheJpegEncoder(int quality)
    {
        using var image = CreateLandscape();
        var expected = GetBytes(image, PixelFormat.Rgb24);
        var jpegData = Save(image, new JpegEncoder { Quality = quality });
        var data = Save(image, new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy, Quality = quality });
        using var jpegImage = Image.Load(jpegData);
        using var decoded = Image.Load(data);
        var jpeg = Measure(GetBytes(jpegImage, PixelFormat.Rgb24));
        var actual = Measure(GetBytes(decoded, PixelFormat.Rgb24));

        var message = $"quality {quality}: JPEG {jpegData.Length} bytes, luminance {jpeg.Luminance:0.000}, chrominance {jpeg.Chrominance:0.000}; JPEG 2000 {data.Length} bytes, luminance {actual.Luminance:0.000}, chrominance {actual.Chrominance:0.000}";
        Assert.InRange(actual.Luminance, 0.65 * jpeg.Luminance, 1.25 * jpeg.Luminance, message: message);
        Assert.InRange(actual.Chrominance, 0, jpeg.Chrominance, message: message);
        Assert.HasCountLessThan(jpegData.Length, data, message);

        // The root mean square errors of the luminance and of the two chrominances (BT.601), in levels
        (double Luminance, double Chrominance) Measure(byte[] pixels)
        {
            var luminance = 0.0;
            var chrominance = 0.0;
            for (var i = 0; i < expected.Length; i += 3)
            {
                double red = expected[i] - pixels[i];
                double green = expected[i + 1] - pixels[i + 1];
                double blue = expected[i + 2] - pixels[i + 2];
                var y = (0.299 * red) + (0.587 * green) + (0.114 * blue);
                var cb = (-0.168736 * red) - (0.331264 * green) + (0.5 * blue);
                var cr = (0.5 * red) - (0.418688 * green) - (0.081312 * blue);
                luminance += y * y;
                chrominance += (cb * cb) + (cr * cr);
            }

            return (Math.Sqrt(luminance / (expected.Length / 3)), Math.Sqrt(chrominance / (2 * (expected.Length / 3))));
        }
    }

    [Fact]
    public void AFileHoldsTheBoxesOfItsChannelsAndACodestreamHoldsNone()
    {
        using var source = CreateImage(7, 5, seed: 4);
        using var color = ConvertTo(source, PixelFormat.Rgb24);
        using var deepGray = ConvertTo(source, PixelFormat.Gray16);
        var opaque = ReadBoxes(Save(color, new Jpeg2000Encoder()));
        Assert.Equal(["ftyp", "jp2h", "jp2c"], opaque.Select(box => box.Type).ToArray());
        Assert.Equal("jp2 \0\0\0\0jp2 "u8.ToArray(), opaque[0].Payload);

        // The image header box: the height, the width, three components of 8 bits, the compression type 7; then the
        // enumerated color space 16 (sRGB)
        Assert.Equal(
            [
                0x00, 0x00, 0x00, 0x16, (byte)'i', (byte)'h', (byte)'d', (byte)'r', 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x07, 0x00, 0x03, 0x07, 0x07, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x0F, (byte)'c', (byte)'o', (byte)'l', (byte)'r', 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10,
            ],
            opaque[1].Payload);
        Assert.Equal([0xFF, 0x4F, 0xFF, 0x51], opaque[2].Payload[..4]);
        Assert.Equal([0xFF, 0xD9], opaque[2].Payload[^2..]);

        // An opacity is described by a channel definition box: the three colors, then the opacity of the whole image
        var transparent = ReadBoxes(Save(source, new Jpeg2000Encoder()));
        Assert.Equal(
            [0x00, 0x00, 0x00, 0x22, (byte)'c', (byte)'d', (byte)'e', (byte)'f', 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x02, 0x00, 0x00, 0x00, 0x03, 0x00, 0x03, 0x00, 0x01, 0x00, 0x00],
            transparent[1].Payload[37..]);

        // A gray image is in the enumerated color space 17
        var gray = ReadBoxes(Save(deepGray, new Jpeg2000Encoder()));
        Assert.Equal([0x00, 0x01, 0x0F, 0x07, 0x00, 0x00], gray[1].Payload[16..22]);
        Assert.Equal([0x00, 0x00, 0x00, 0x11], gray[1].Payload[^4..]);

        // The codestream of a file is the output of the codestream container
        Assert.Equal(opaque[2].Payload, Save(color, new Jpeg2000Encoder { Container = Jpeg2000Container.Codestream }));
    }

    [Fact]
    public void MetadataRoundTrips()
    {
        var icc = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        var xmp = "<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'/></x:xmpmeta>"u8.ToArray();
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Rgb24, PixelFormat.Rgba64 })
        {
            using var source = CreateImage(13, 6, seed: 3);
            using var image = ConvertTo(source, format);
            image.Metadata.IccProfile = icc;
            image.Metadata.ExifProfile = new ExifProfile(new MetadataBlob(EmptyExif));
            image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(xmp));
            image.Metadata.Orientation = ExifOrientation.LeftBottom;
            image.Metadata.Resolution = new ImageResolution(96, 300);
            var data = Save(image, new Jpeg2000Encoder());

            // A header identification reads everything that precedes the codestream
            foreach (var metadata in new[] { Image.Identify(data).Metadata, LoadMetadata(data) })
            {
                Assert.Equal(icc.Data.ToArray(), metadata.IccProfile?.Data.ToArray());
                Assert.Equal(xmp, metadata.XmpProfile?.Data.ToArray());
                Assert.Equal(ExifOrientation.LeftBottom, metadata.Orientation);
                Assert.NotNull(metadata.ExifProfile);
                Assert.Equal(new ImageResolution(96, 300), metadata.Resolution);
                Assert.Null(metadata.Cicp);
                Assert.Equal(ColorTransferFunction.Srgb, metadata.TransferFunction);
            }

            // The orientation is a label: the samples are stored as they are
            using var decoded = Image.Load(data);
            Assert.Equal((13, 6), (decoded.Width, decoded.Height));
            Assert.Equal(GetBytes(image, format), GetBytes(decoded, format));
        }

        static ImageMetadata LoadMetadata(byte[] data)
        {
            using var image = Image.Load(data);
            return image.Metadata.Clone();
        }
    }

    [Theory]
    [InlineData(72, 72)]
    [InlineData(96, 300)]
    [InlineData(72.009, 96.012)]
    [InlineData(0.5, 1200)]
    public void ResolutionsOfWholeDotsPerInchAndOfWholePixelsPerMeterAreExact(double horizontal, double vertical)
    {
        using var image = CreateImage(3, 2, seed: 1);
        image.Metadata.Resolution = new ImageResolution(horizontal, vertical);
        Assert.Equal(image.Metadata.Resolution, Image.Identify(Save(image, new Jpeg2000Encoder())).Metadata.Resolution);
    }

    [Fact]
    public void OtherResolutionsAreStoredWithFiveDigits()
    {
        using var image = CreateImage(3, 2, seed: 1);
        image.Metadata.Resolution = new ImageResolution(Math.PI * 30, 123.456789);
        var resolution = Image.Identify(Save(image, new Jpeg2000Encoder())).Metadata.Resolution;
        Assert.NotNull(resolution);
        Assert.True(Math.Abs(resolution.HorizontalDpi - (Math.PI * 30)) < Math.PI * 30 / 10000 && Math.Abs(resolution.VerticalDpi - 123.456789) < 123.456789 / 10000, resolution.ToString());
    }

    [Fact]
    public void AGrayProfileDescribesGraySamples()
    {
        var profile = IccTestProfiles.Gray(IccTestProfiles.Gamma(563));
        using var source = CreateImage(5, 4, seed: 2);
        using var image = ConvertTo(source, PixelFormat.Gray8);
        image.Metadata.IccProfile = profile;
        var data = Save(image, new Jpeg2000Encoder());
        Assert.Equal(profile.Data.ToArray(), Image.Identify(data).Metadata.IccProfile?.Data.ToArray());

        // A profile of another color space does not describe the samples
        using var color = ConvertTo(source, PixelFormat.Rgb24);
        color.Metadata.IccProfile = profile;
        using var stream = new TestOutputStream();
        Assert.Throws<UnsupportedImageFeatureException>(() => color.Save(stream, new Jpeg2000Encoder()));
        Assert.Equal(0, stream.BytesWritten);
    }

    [Fact]
    public void WhatAFileCannotStoreFollowsTheMetadataPolicy()
    {
        var cases = new (string Feature, Action<ImageMetadata> Set)[]
        {
            ("Metadata: text entry 'Comment'", metadata => metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "lorem ipsum"))),
            ("Metadata: linear transfer function (linear-light samples would be read as sRGB)", metadata => metadata.TransferFunction = ColorTransferFunction.Linear),
            ("Metadata: ICC color profile that is neither a grayscale profile nor a matrix-based RGB profile (the only ones a JP2 file allows)", metadata => metadata.IccProfile = IccTestProfiles.RgbLabLut8()),
        };
        foreach (var (feature, set) in cases)
        {
            using var image = CreateImage(3, 2, seed: 1);
            set(image.Metadata);
            using var stream = new TestOutputStream();
            Assert.Equal(feature, Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new Jpeg2000Encoder())).Feature);
            Assert.Equal(0, stream.BytesWritten);

            var discarded = Save(image, new Jpeg2000Encoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
            var stripped = Save(image, new Jpeg2000Encoder { MetadataHandling = MetadataHandling.Strip });
            Assert.Equal(discarded, stripped);
            using var decoded = Image.Load<Rgba32>(discarded);
            Assert.Equal(GetPixels(image.Frames[0]), GetPixels(decoded.Frames[0]));
        }
    }

    [Fact]
    public void ACodestreamStoresNoMetadata()
    {
        var cases = new (string Feature, Action<ImageMetadata> Set)[]
        {
            ("Metadata: ICC color profile", metadata => metadata.IccProfile = IccProfile.Srgb),
            ("Metadata: EXIF profile", metadata => metadata.ExifProfile = new ExifProfile(new MetadataBlob(EmptyExif))),
            ("Metadata: EXIF orientation other than TopLeft", metadata => metadata.Orientation = ExifOrientation.BottomRight),
            ("Metadata: XMP packet", metadata => metadata.XmpProfile = new XmpProfile(new MetadataBlob("<x:xmpmeta xmlns:x='adobe:ns:meta/'/>"u8))),
            ("Metadata: resolution", metadata => metadata.Resolution = new ImageResolution(72, 72)),
        };
        var encoder = new Jpeg2000Encoder { Container = Jpeg2000Container.Codestream };
        using var plain = CreateImage(3, 2, seed: 1);
        var expected = Save(plain, encoder);
        foreach (var (feature, set) in cases)
        {
            using var image = CreateImage(3, 2, seed: 1);
            set(image.Metadata);
            using var stream = new TestOutputStream();
            Assert.Equal(feature, Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, encoder)).Feature);
            Assert.Equal(0, stream.BytesWritten);
            Assert.Equal(expected, Save(image, new Jpeg2000Encoder { Container = Jpeg2000Container.Codestream, MetadataHandling = MetadataHandling.DiscardUnsupported }));
        }
    }

    [Fact]
    public void AnimationsAreNotStored()
    {
        using var image = CreateImage(4, 3, seed: 6);
        using var second = CreateImage(4, 3, seed: 7);
        image.AppendFrame(second.Frames[0]);
        using var stream = new TestOutputStream();
        Assert.Equal("Animation", Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new Jpeg2000Encoder())).Feature);
        Assert.Equal(0, stream.BytesWritten);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WritersStreamToNonSeekableOutput(bool asynchronous)
    {
        using var image = CreateImage(300, 200, seed: 12);
        foreach (var encoder in new[] { new Jpeg2000Encoder(), new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy, Container = Jpeg2000Container.Codestream } })
        {
            using var stream = new TestOutputStream();
            using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(300, 200) { Encoder = encoder, ExpectedFrameCount = 1 }))
            {
                if (asynchronous)
                {
                    await writer.WriteFrameAsync(image.Frames[0], XunitCancellationToken);
                    await writer.CompleteAsync(XunitCancellationToken);
                }
                else
                {
                    writer.WriteFrame(image.Frames[0]);
                    Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(image.Frames[0]));
                    writer.Complete();
                }

                Assert.Equal(0, writer.Core.Scope.LiveBytes);
            }

            // Nothing is patched: every length is known when it is written
            Assert.Equal(Save(image, encoder), stream.ToArray());
        }
    }

    [Fact]
    public void PathSavesInferJpeg2000FromTheExtension()
    {
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("meziantou-jp2-"));
        try
        {
            using var image = CreateImage(5, 3, seed: 1);
            foreach (var (extension, isFile, isHighThroughput) in new[] { (".JP2", true, false), (".j2k", false, false), (".j2c", false, false), (".jpc", false, false), (".jph", true, true), (".JHC", false, true) })
            {
                var path = directory / ("image" + extension);
                image.Save(path);
                Assert.Equal(ImageFormat.Jpeg2000, Image.Identify(path).Format);
                var data = File.ReadAllBytes(path);
                Assert.Equal(isFile, data.AsSpan().StartsWith(FileSignature));
                Assert.Equal(isHighThroughput, ReadMainHeader(isFile ? ReadBoxes(data).Single(box => box.Type == "jp2c").Payload : data).Any(segment => segment.Marker == 0xFF50));
                using var decoded = Image.Load<Rgba32>(path);
                Assert.Equal(GetPixels(image.Frames[0]), GetPixels(decoded.Frames[0]));
            }

            // The files of these extensions are read, never written
            foreach (var extension in new[] { ".jpx", ".jpf" })
            {
                var path = directory / ("image" + extension);
                var exception = Assert.Throws<ArgumentException>(() => image.Save(path));
                Assert.Contains("can be read but not written", exception.Message);
                Assert.False(File.Exists(path));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Reads the marker segments of the main header of a codestream, from the SOC marker to the first SOT marker.</summary>
    private static List<(int Marker, byte[] Payload)> ReadMainHeader(byte[] codestream)
    {
        const int MarkerSot = 0xFF90;
        var segments = new List<(int Marker, byte[] Payload)>();
        var position = 2;
        while (true)
        {
            var marker = BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(position));
            if (marker == MarkerSot)
                return segments;

            var length = BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(position + 2));
            segments.Add((marker, codestream[(position + 4)..(position + 2 + length)]));
            position += 2 + length;
        }
    }

    private static List<(string Type, byte[] Payload)> ReadBoxes(byte[] data)
    {
        Assert.Equal(FileSignature, data[..12]);
        var boxes = new List<(string, byte[])>();
        var offset = 12;
        while (offset < data.Length)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset)));
            Assert.InRange(size, 8, data.Length - offset);
            boxes.Add((Encoding.Latin1.GetString(data, offset + 4, 4), data[(offset + 8)..(offset + size)]));
            offset += size;
        }

        return boxes;
    }

    /// <summary>
    /// An image of at least 65,536 pixels is coded by several workers (the rows, the wavelet transformation and the
    /// code-blocks): the file is the one of one worker, for both wavelets, one to four components and both depths.
    /// </summary>
    [Theory]
    [InlineData(PixelFormat.Rgb24, Jpeg2000Compression.Lossless, 331, 223)]
    [InlineData(PixelFormat.Rgb24, Jpeg2000Compression.Lossy, 331, 223)]
    [InlineData(PixelFormat.Gray8, Jpeg2000Compression.Lossless, 170, 401)]
    [InlineData(PixelFormat.Gray16, Jpeg2000Compression.Lossy, 401, 170)]
    [InlineData(PixelFormat.Rgba32, Jpeg2000Compression.Lossy, 257, 256)]
    [InlineData(PixelFormat.Bgra32, Jpeg2000Compression.Lossless, 300, 230)]
    [InlineData(PixelFormat.Rgba64, Jpeg2000Compression.Lossless, 300, 230)]
    public void FilesWrittenByWorkersAreTheFilesOfOneWorker(PixelFormat format, Jpeg2000Compression compression, int width, int height)
    {
        var encoder = new Jpeg2000Encoder { Compression = compression };
        using var source = CreateImage(width, height, seed: width + (int)format);
        using var image = ConvertTo(source, format);
        var expected = Save(image, encoder);
        var bytes = GetBytes(image, format);
        foreach (var workers in (int[])[2, 3, 16])
        {
            using var copy = Import(bytes, width, height, format, new ImageConfiguration { MaxDegreeOfParallelism = workers });
            Assert.True(expected.AsSpan().SequenceEqual(Save(copy, encoder)), $"{format} {compression} {width}x{height}: {workers} workers write other bytes.");
        }
    }

    [Fact]
    public async Task AnEncodingCanceledBeforeItStartsThrowsWhateverTheWorkers()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (var workers in (int[])[1, 16])
        {
            using var source = CreateImage(300, 230, seed: 4);
            using var image = Import(GetBytes(source, PixelFormat.Rgba32), 300, 230, PixelFormat.Rgba32, new ImageConfiguration { MaxDegreeOfParallelism = workers });
            using var stream = new MemoryStream();
            await Assert.ThrowsAny<OperationCanceledException>(() => image.SaveAsync(stream, new Jpeg2000Encoder(), cancellation.Token));
        }
    }

    /// <summary>
    /// Tiles that divide the image or whose last column and row are cut (down to one sample, where every resolution level
    /// above the lowest is empty), three layers whose last one holds every coding pass, in every progression order: the
    /// headers say so, each tile is one tile-part, and the samples are those of the image.
    /// </summary>
    [Theory]
    [InlineData(Jpeg2000ProgressionOrder.Lrcp)]
    [InlineData(Jpeg2000ProgressionOrder.Rlcp)]
    [InlineData(Jpeg2000ProgressionOrder.Rpcl)]
    [InlineData(Jpeg2000ProgressionOrder.Pcrl)]
    [InlineData(Jpeg2000ProgressionOrder.Cprl)]
    public void TilesLayersAndProgressionOrdersRoundTripExactly(Jpeg2000ProgressionOrder order)
    {
        foreach (var (width, height, tileWidth, tileHeight) in new[] { (300, 230, 0, 0), (300, 230, 100, 115), (300, 230, 64, 40), (130, 129, 129, 128), (70, 1, 16, 0), (1, 1, 1, 1) })
        {
            using var source = CreateImage(width, height, seed: width + tileWidth + (int)order);
            var encoder = new Jpeg2000Encoder
            {
                ProgressionOrder = order,
                TileWidth = tileWidth,
                TileHeight = tileHeight,
                QualityLayers = [Jpeg2000QualityLayer.FromBitsPerPixel(0.5), Jpeg2000QualityLayer.FromBitsPerPixel(2), Jpeg2000QualityLayer.Complete],
            };
            var data = Save(source, encoder);
            var context = $"{order} {width}x{height} in tiles of {tileWidth}x{tileHeight}";

            var reference = ReferenceJpeg2000.Parse(data);
            var expectedTileWidth = tileWidth == 0 ? width : tileWidth;
            var expectedTileHeight = tileHeight == 0 ? height : tileHeight;
            var tileCount = ((width + expectedTileWidth - 1) / expectedTileWidth) * ((height + expectedTileHeight - 1) / expectedTileHeight);
            Assert.Equal(((int)order, 3), (reference.ProgressionOrder, reference.LayerCount));
            Assert.Equal((expectedTileWidth, expectedTileHeight, tileCount), (reference.TileWidth, reference.TileHeight, reference.TileCount));
            Assert.Equal(Enumerable.Range(0, tileCount), reference.TileParts.Select(part => part.Tile));
            Assert.All(reference.TileParts, part => Assert.Equal((0, 1), (part.Part, part.PartCount)));

            using var decoded = Image.Load<Rgba32>(data);
            Assert.True(GetPixels(source.Frames[0]).AsSpan().SequenceEqual(GetPixels(decoded.Frames[0])), $"{context}: the decoded samples differ.");
        }
    }

    /// <summary>
    /// The high-throughput block coder, in every pixel format, both containers, tiles and progression orders: the samples
    /// are those of the image.
    /// </summary>
    [Theory]
    [InlineData(PixelFormat.Gray8)]
    [InlineData(PixelFormat.Gray16)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Bgra32)]
    [InlineData(PixelFormat.Rgba64)]
    public void HighThroughputOutputsRoundTripExactly(PixelFormat format)
    {
        var storedFormat = format == PixelFormat.Bgra32 ? PixelFormat.Rgba32 : format;
        foreach (var (width, height, tileWidth, order) in new[] { (23, 11, 0, Jpeg2000ProgressionOrder.Lrcp), (1, 1, 0, Jpeg2000ProgressionOrder.Lrcp), (1, 70, 0, Jpeg2000ProgressionOrder.Rlcp), (70, 1, 16, Jpeg2000ProgressionOrder.Rpcl), (300, 270, 0, Jpeg2000ProgressionOrder.Lrcp), (130, 129, 64, Jpeg2000ProgressionOrder.Pcrl), (65, 2, 0, Jpeg2000ProgressionOrder.Cprl) })
        {
            foreach (var container in new[] { Jpeg2000Container.Jp2, Jpeg2000Container.Codestream })
            {
                using var source = CreateImage(width, height, seed: (int)format + width);
                using var image = ConvertTo(source, format);
                var data = Save(image, new Jpeg2000Encoder { BlockCoder = Jpeg2000BlockCoder.HighThroughput, Container = container, TileWidth = tileWidth, TileHeight = tileWidth, ProgressionOrder = order });
                var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
                Assert.Equal((ImageFormat.Jpeg2000, width, height, storedFormat), (info.Format, info.Width, info.Height, info.PixelFormat));
                using var decoded = Image.Load(data);
                Assert.True(GetBytes(image, storedFormat).AsSpan().SequenceEqual(GetBytes(decoded, storedFormat)), $"{format} {width}x{height} {container} in tiles of {tileWidth} ({order}): the decoded samples differ.");
            }
        }
    }

    /// <summary>
    /// A lossy output of the high-throughput block coder codes the quantization indices of the standard coder: it decodes
    /// to the same samples, the opacity included, in a file of a few percent more.
    /// </summary>
    [Theory]
    [InlineData(PixelFormat.Gray8)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Rgba64)]
    public void HighThroughputLossyOutputsDecodeToTheSamplesOfTheStandardCoder(PixelFormat format)
    {
        using var source = CreateImage(150, 97, seed: (int)format);
        using var image = ConvertTo(source, format);
        foreach (var quality in new[] { 20, 90, 100 })
        {
            var standard = Save(image, new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy, Quality = quality });
            var highThroughput = Save(image, new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy, Quality = quality, BlockCoder = Jpeg2000BlockCoder.HighThroughput });
            using var expected = Image.Load(standard);
            using var decoded = Image.Load(highThroughput);
            Assert.True(GetBytes(expected, format).AsSpan().SequenceEqual(GetBytes(decoded, format)), $"{format} at quality {quality}: the samples differ.");
            Assert.InRange(highThroughput.Length, standard.Length, standard.Length * 13 / 10);
        }
    }

    /// <summary>
    /// The high-throughput block coder is declared (T.814, Annex A): the bit 14 of Rsiz in SIZ, a CAP marker segment with
    /// the field of Part 15 (no other block coder, one HT set, no region of interest, the irreversible bit, and the
    /// magnitude bound of the sub-bands), and the bit 6 of the code-block style of COD and COC; a file has the brand
    /// <c>jph </c>.
    /// </summary>
    [Fact]
    public void HighThroughputIsDeclaredInTheCodestreamAndTheFile()
    {
        using var source = CreateImage(40, 30, seed: 3);
        using var gray = ConvertTo(source, PixelFormat.Gray8);
        var lossless = Save(gray, new Jpeg2000Encoder { BlockCoder = Jpeg2000BlockCoder.HighThroughput, Container = Jpeg2000Container.Codestream });
        var segments = ReadMainHeader(lossless);
        Assert.Equal([0xFF51, 0xFF50, 0xFF52, 0xFF5C], segments.Select(segment => segment.Marker));
        Assert.Equal(0x4000, BinaryPrimitives.ReadUInt16BigEndian(segments[0].Payload));
        var capabilities = segments[1].Payload;
        Assert.Equal([0x00, 0x02, 0x00, 0x00], capabilities[..4]);

        // MAGB: the largest number of magnitude bit-planes, from the exponents of QCD and its two guard bits, less 8
        var quantization = segments[3].Payload;
        var largest = quantization[1..].Max(exponent => (exponent >> 3) + (quantization[0] >> 5) - 1);
        Assert.Equal(largest - 8, BinaryPrimitives.ReadUInt16BigEndian(capabilities.AsSpan(4)));
        Assert.Equal(0x40, segments[2].Payload[8]);

        // A lossy output sets the irreversible bit; its opacity is reversible, in a COC marker segment of the same style
        var lossy = Save(source, new Jpeg2000Encoder { BlockCoder = Jpeg2000BlockCoder.HighThroughput, Compression = Jpeg2000Compression.Lossy });
        var boxes = ReadBoxes(lossy);
        Assert.Equal("ftyp", boxes[0].Type);
        Assert.Equal("jph \0\0\0\0jph "u8.ToArray(), boxes[0].Payload);
        segments = ReadMainHeader(boxes.Single(box => box.Type == "jp2c").Payload);
        Assert.Equal(0x20, BinaryPrimitives.ReadUInt16BigEndian(segments.Single(segment => segment.Marker == 0xFF50).Payload.AsSpan(4)) & 0xFFE0);
        Assert.Equal(0x40, segments.Single(segment => segment.Marker == 0xFF52).Payload[8]);
        Assert.Equal(0x40, segments.Single(segment => segment.Marker == 0xFF53).Payload[5]);

        // The standard coder declares none of it
        segments = ReadMainHeader(Save(gray, new Jpeg2000Encoder { Container = Jpeg2000Container.Codestream }));
        Assert.DoesNotContain(segments, segment => segment.Marker == 0xFF50);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(segments[0].Payload));
    }

    /// <summary>
    /// A target of the high-throughput block coder is reached by choosing the lowest bit-plane of each code-block: the
    /// output fits it, nearly reaches it (in steps of whole bit-planes), and a larger target leaves a smaller error. More
    /// than one layer is refused.
    /// </summary>
    [Theory]
    [InlineData(Jpeg2000Compression.Lossy)]
    [InlineData(Jpeg2000Compression.Lossless)]
    public void HighThroughputOutputsFitTheirTargets(Jpeg2000Compression compression)
    {
        using var source = CreateLandscape();
        var expected = GetBytes(source, PixelFormat.Rgb24);
        var previousError = double.MaxValue;
        foreach (var target in new[] { 1_500, 4_000, 9_000, 20_000 })
        {
            var data = Save(source, new Jpeg2000Encoder { BlockCoder = Jpeg2000BlockCoder.HighThroughput, Compression = compression, Quality = 100, QualityLayers = [Jpeg2000QualityLayer.FromSize(target)] });
            // A whole bit-plane of a code-block is the step of the rate control: a few percent of the smallest target
            Assert.InRange(data.Length, target * (target < 4_000 ? 85 : 95) / 100, target);
            using var decoded = Image.Load(data);
            var error = GetRootMeanSquareError(expected, GetBytes(decoded, PixelFormat.Rgb24));
            Assert.True(error < previousError, $"{compression}, {target} bytes: error {error} after {previousError}.");
            previousError = error;
        }

        // A target every bit-plane fits in keeps them all
        var complete = Save(source, new Jpeg2000Encoder { BlockCoder = Jpeg2000BlockCoder.HighThroughput, Compression = compression });
        Assert.Equal(complete, Save(source, new Jpeg2000Encoder { BlockCoder = Jpeg2000BlockCoder.HighThroughput, Compression = compression, QualityLayers = [Jpeg2000QualityLayer.FromSize(10_000_000)] }));

        var layers = new Jpeg2000Encoder { BlockCoder = Jpeg2000BlockCoder.HighThroughput, QualityLayers = [Jpeg2000QualityLayer.FromSize(2_000), Jpeg2000QualityLayer.Complete] };
        Assert.Throws<ArgumentException>(() => Save(source, layers));
    }

    [Theory]
    [InlineData(PixelFormat.Rgb24, Jpeg2000Compression.Lossless, false)]
    [InlineData(PixelFormat.Rgba32, Jpeg2000Compression.Lossy, false)]
    [InlineData(PixelFormat.Gray16, Jpeg2000Compression.Lossy, true)]
    [InlineData(PixelFormat.Rgba64, Jpeg2000Compression.Lossless, true)]
    public void HighThroughputFilesWrittenByWorkersAreTheFilesOfOneWorker(PixelFormat format, Jpeg2000Compression compression, bool hasTarget)
    {
        var encoder = new Jpeg2000Encoder { BlockCoder = Jpeg2000BlockCoder.HighThroughput, Compression = compression, QualityLayers = hasTarget ? [Jpeg2000QualityLayer.FromBitsPerPixel(1.5)] : [] };
        using var source = CreateImage(331, 223, seed: (int)format);
        using var image = ConvertTo(source, format);
        var expected = Save(image, encoder);
        var bytes = GetBytes(image, format);
        foreach (var workers in (int[])[2, 16])
        {
            using var copy = Import(bytes, 331, 223, format, new ImageConfiguration { MaxDegreeOfParallelism = workers });
            Assert.True(expected.AsSpan().SequenceEqual(Save(copy, encoder)), $"{format} {compression}: {workers} workers write other bytes.");
        }
    }

    [Fact]
    public void ADefaultEncoderAndACompleteLayerWriteTheSameBytes()
    {
        using var source = CreateLandscape();
        Assert.Equal(Save(source, new Jpeg2000Encoder()), Save(source, new Jpeg2000Encoder { QualityLayers = [Jpeg2000QualityLayer.Complete] }));

        // A target that every coding pass fits in keeps them all
        var lossy = new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy };
        Assert.Equal(Save(source, lossy), Save(source, new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy, QualityLayers = [Jpeg2000QualityLayer.FromSize(10_000_000)] }));
    }

    /// <summary>
    /// A size or a number of bits per pixel is an upper bound of the output that the rate control nearly reaches: the
    /// truncation points of the code-blocks are a few bytes apart. A larger target leaves a smaller error. A target that
    /// the headers and the empty packets exceed gives them alone.
    /// </summary>
    [Theory]
    [InlineData(Jpeg2000Compression.Lossy, Jpeg2000Container.Jp2)]
    [InlineData(Jpeg2000Compression.Lossy, Jpeg2000Container.Codestream)]
    [InlineData(Jpeg2000Compression.Lossless, Jpeg2000Container.Jp2)]
    public void OutputsFitTheirTargets(Jpeg2000Compression compression, Jpeg2000Container container)
    {
        using var source = CreateLandscape();
        var expected = GetBytes(source, PixelFormat.Rgb24);
        var previousError = double.MaxValue;
        foreach (var target in new[] { 1_500, 4_000, 9_000, 20_000 })
        {
            foreach (var layer in new[] { Jpeg2000QualityLayer.FromSize(target), Jpeg2000QualityLayer.FromBitsPerPixel(target * 8.0 / (source.Width * source.Height)) })
            {
                var data = Save(source, new Jpeg2000Encoder { Compression = compression, Quality = 100, Container = container, QualityLayers = [layer] });
                Assert.InRange(data.Length, target * 97 / 100, target);
                using var decoded = Image.Load(data);
                var error = GetRootMeanSquareError(expected, GetBytes(decoded, PixelFormat.Rgb24));
                if (layer.Size is not null)
                {
                    Assert.True(error < previousError, $"{compression}, {target} bytes: error {error} after {previousError}.");
                    previousError = error;
                }
            }
        }

        var tiny = Save(source, new Jpeg2000Encoder { Compression = compression, Container = container, QualityLayers = [Jpeg2000QualityLayer.FromSize(10)] });
        Assert.InRange(tiny.Length, 11, 400);
        using var empty = Image.Load(tiny);
        Assert.Equal((source.Width, source.Height), (empty.Width, empty.Height));
    }

    /// <summary>
    /// The layers of an LRCP output follow each other: the packets of the first layers are the start of the codestream,
    /// whose cumulative length up to each layer fits its target, and each layer adds coding passes.
    /// </summary>
    [Fact]
    public void EachLayerFitsItsTarget()
    {
        using var source = CreateLandscape();
        int[] targets = [1_000, 3_000, 8_000, 20_000];
        var data = Save(source, new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy, Quality = 100, Container = Jpeg2000Container.Codestream, QualityLayers = [.. targets.Select(target => Jpeg2000QualityLayer.FromSize(target))] });
        var reference = ReferenceJpeg2000.Parse(data);
        Assert.Equal(4, reference.LayerCount);

        // The packets of one tile in the LRCP order: with five levels and three components, 18 packets a layer. Each layer
        // ends where its packets end; the header of a packet gives the length of its body
        var lengths = GetPacketLengths(data, reference.TileParts[0], packetCount: 4 * 18, componentCount: 3, levels: 5);
        var end = reference.TileParts[0].Offset + Jpeg2000HeaderLength;
        for (var layer = 0; layer < targets.Length; layer++)
        {
            end += lengths.Skip(layer * 18).Take(18).Sum();
            Assert.InRange(end + 2, targets[layer] * 9 / 10, targets[layer]);
        }

        Assert.Equal(data.Length, end + 2);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(5, 0, 5)]
    [InlineData(7, 0, 7)]
    [InlineData(8, 0, 7)]
    [InlineData(32, 0, 7)]
    [InlineData(32, 64, 6)]
    public void DecompositionLevelsAreLimitedByTheSmallerSideOfATile(int levels, int tileSize, int expected)
    {
        // The smaller side, 230 pixels, has 7 levels of at least one pixel
        using var source = CreateImage(300, 230, seed: levels + tileSize);
        foreach (var compression in new[] { Jpeg2000Compression.Lossless, Jpeg2000Compression.Lossy })
        {
            var data = Save(source, new Jpeg2000Encoder { Compression = compression, DecompositionLevels = levels, TileWidth = tileSize, TileHeight = tileSize });
            Assert.All(ReferenceJpeg2000.Parse(data).CodingStyles, style => Assert.Equal(expected, style.DecompositionLevels));
            using var decoded = Image.Load<Rgba32>(data);
            var error = GetRootMeanSquareError(GetBytes(source, PixelFormat.Rgba32), GetBytes(decoded, PixelFormat.Rgba32));
            Assert.True(compression == Jpeg2000Compression.Lossless ? error == 0 : error < 6, $"{compression}, {levels} levels: error {error}.");
        }
    }

    /// <summary>
    /// The encoder holds the coefficients of one tile at a time: with tiles of 128 by 128, the peak of its live allocations
    /// is the pixels of the image and the coded bytes plus a small plane, instead of a plane of the whole image. Layers add
    /// a record of each coding pass of each code-block (about 2 MB here, for 3,000 code-blocks), less than a plane.
    /// </summary>
    [Fact]
    public void TilesBoundTheWorkingMemory()
    {
        using var created = CreateImage(1024, 768, seed: 3);
        using var source = Import(GetBytes(created, PixelFormat.Rgba32), 1024, 768, PixelFormat.Rgba32, new ImageConfiguration { MaxDegreeOfParallelism = 1 });
        var whole = MeasurePeak(new Jpeg2000Encoder());
        var tiled = MeasurePeak(new Jpeg2000Encoder { TileWidth = 128, TileHeight = 128 });
        var layered = MeasurePeak(new Jpeg2000Encoder { TileWidth = 128, TileHeight = 128, QualityLayers = [Jpeg2000QualityLayer.FromBitsPerPixel(1), Jpeg2000QualityLayer.Complete] });

        // The plane of the whole image alone is 1024 * 768 * 4 bytes
        Assert.True(whole - tiled > 2_500_000, $"Whole image: {whole} bytes; tiles: {tiled} bytes.");
        Assert.True(layered - tiled < 1024 * 768 * 4, $"Tiles: {tiled} bytes; with layers: {layered} bytes.");

        long MeasurePeak(Jpeg2000Encoder encoder)
        {
            using var tracking = new Internals.AllocationTracking();
            using var stream = new MemoryStream();
            source.Save(stream, encoder);
            return tracking.Scopes.Max(static scope => scope.GetDiagnostics().PeakLiveBytes);
        }
    }

    [Fact]
    public void AnImageHasAtMost65535Tiles()
    {
        using var image = new Image<Gray8>(256, 257);
        Assert.Throws<ArgumentException>(() => Save(image, new Jpeg2000Encoder { TileWidth = 1, TileHeight = 1 }));
        Assert.NotEmpty(Save(image, new Jpeg2000Encoder { TileWidth = 1, TileHeight = 256 }));
    }

    [Theory]
    [InlineData(Jpeg2000Compression.Lossless, Jpeg2000ProgressionOrder.Rpcl)]
    [InlineData(Jpeg2000Compression.Lossy, Jpeg2000ProgressionOrder.Lrcp)]
    public void TiledAndLayeredFilesWrittenByWorkersAreTheFilesOfOneWorker(Jpeg2000Compression compression, Jpeg2000ProgressionOrder order)
    {
        var encoder = new Jpeg2000Encoder { Compression = compression, ProgressionOrder = order, TileWidth = 200, TileHeight = 150, QualityLayers = [Jpeg2000QualityLayer.FromBitsPerPixel(0.5), Jpeg2000QualityLayer.FromBitsPerPixel(3)] };
        using var source = CreateImage(450, 300, seed: 11);
        var bytes = GetBytes(source, PixelFormat.Rgba32);
        using var single = Import(bytes, 450, 300, PixelFormat.Rgba32, new ImageConfiguration { MaxDegreeOfParallelism = 1 });
        var expected = Save(single, encoder);
        foreach (var workers in (int[])[2, 16])
        {
            using var copy = Import(bytes, 450, 300, PixelFormat.Rgba32, new ImageConfiguration { MaxDegreeOfParallelism = workers });
            Assert.True(expected.AsSpan().SequenceEqual(Save(copy, encoder)), $"{compression} {order}: {workers} workers write other bytes.");
        }
    }

    /// <summary>
    /// The working memory of every worker is charged before any code-block is coded: a limit that the pixels of the image fit
    /// in, and the plane of the encoder does not, fails the same way.
    /// </summary>
    [Fact]
    public void ALiveAllocationLimitFailsTheSameWayWhateverTheWorkers()
    {
        using var source = CreateImage(300, 230, seed: 6);
        var bytes = GetBytes(source, PixelFormat.Rgba32);
        foreach (var workers in (int[])[1, 16])
        {
            using var image = Import(bytes, 300, 230, PixelFormat.Rgba32, new ImageConfiguration { MaxDegreeOfParallelism = workers, Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 340_000 } });
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, Assert.Throws<ImageResourceLimitException>(() => Save(image, new Jpeg2000Encoder())).Kind);
        }
    }

    private const int Jpeg2000HeaderLength = 14;

    private static double GetRootMeanSquareError(byte[] expected, byte[] actual)
    {
        var sum = 0.0;
        for (var i = 0; i < expected.Length; i++)
        {
            sum += (expected[i] - actual[i]) * (expected[i] - actual[i]);
        }

        return Math.Sqrt(sum / expected.Length);
    }

    /// <summary>
    /// Reads the packet headers of a tile-part written without precincts or code-block styles and returns the length of each
    /// packet, header and body: the packet headers are read with the tag trees of B.10.2 and the length rules of B.10.7.
    /// </summary>
    private static List<int> GetPacketLengths(byte[] data, ReferenceJpeg2000TilePart tilePart, int packetCount, int componentCount, int levels)
    {
        var scope = new AllocationScope(new ImageResourceLimits());
        var siz = new Jpeg2000Siz(0, 256, 192, 0, 0, 256, 192, 0, 0, Enumerable.Repeat(new Jpeg2000ComponentSize(8, IsSigned: false, 1, 1), componentCount).ToArray());
        var headers = new Jpeg2000HeaderSet(componentCount)
        {
            CodingDefaults = new Jpeg2000CodingDefaults(mayUseSop: false, usesEph: false, Jpeg2000CodingDefaults.ProgressionLrcp, layers: packetCount / ((levels + 1) * componentCount), usesComponentTransform: true, new Jpeg2000CodingStyle(levels, 6, 6, codeBlockStyle: 0, isReversible: false, precinctSizes: null)),
            Quantization = new Jpeg2000Quantization(Jpeg2000Quantization.StyleExpounded, 2, new byte[(3 * levels) + 1], new ushort[(3 * levels) + 1]),
        };
        var coding = Jpeg2000TileCoding.Resolve(siz, headers, tile: null, capabilities: null);
        using var geometry = Jpeg2000TileGeometry.Create(scope, siz, coding, tileIndex: 0);
        var body = data.AsSpan(tilePart.Offset + Jpeg2000HeaderLength, tilePart.Length - Jpeg2000HeaderLength).ToArray();
        var lengths = new List<int>();
        var position = 0;
        var nodes = geometry.TagNodes;
        for (var packet = 0; packet < packetCount; packet++)
        {
            var layer = packet / ((levels + 1) * componentCount);
            var resolution = packet / componentCount % (levels + 1);
            var component = packet % componentCount;
            ref readonly var level = ref geometry.Resolutions[geometry.Components[component].FirstResolution + resolution];
            var start = position;
            var reader = new Jpeg2000PacketBitReader(body, position);
            var bodyLength = 0;
            if (reader.ReadBit() != 0)
            {
                for (var b = 0; b < level.BandCount; b++)
                {
                    ref readonly var precinctBand = ref geometry.PrecinctBands[level.FirstPrecinctBand + b];
                    ref readonly var band = ref geometry.Bands[level.FirstBand + b];
                    var inclusion = nodes.Slice(precinctBand.InclusionTree, precinctBand.TreeLength);
                    var zero = nodes.Slice(precinctBand.ZeroBitPlaneTree, precinctBand.TreeLength);
                    for (var by = 0; by < precinctBand.BlocksHigh; by++)
                    {
                        for (var bx = 0; bx < precinctBand.BlocksWide; bx++)
                        {
                            ref var block = ref geometry.Blocks[band.FirstBlock + ((precinctBand.BlockY + by) * band.BlocksWide) + precinctBand.BlockX + bx];
                            if (block.Included != 0)
                            {
                                if (reader.ReadBit() == 0)
                                    continue;
                            }
                            else
                            {
                                if (!Jpeg2000TagTree.IsLessThan(inclusion, precinctBand.BlocksWide, precinctBand.BlocksHigh, bx, by, layer + 1, ref reader))
                                    continue;

                                var threshold = 1;
                                while (!Jpeg2000TagTree.IsLessThan(zero, precinctBand.BlocksWide, precinctBand.BlocksHigh, bx, by, threshold, ref reader))
                                {
                                    threshold++;
                                }

                                block.Included = 1;
                                block.LengthBits = 3;
                            }

                            // Table B.4, then B.10.7.1
                            var passes = reader.ReadBit() == 0 ? 1 : reader.ReadBit() == 0 ? 2 : reader.ReadBits(2) is var two && two < 3 ? 3 + two : reader.ReadBits(5) is var five && five < 31 ? 6 + five : 37 + reader.ReadBits(7);
                            while (reader.ReadBit() != 0)
                            {
                                block.LengthBits++;
                            }

                            bodyLength += reader.ReadBits(block.LengthBits + System.Numerics.BitOperations.Log2((uint)passes));
                        }
                    }
                }
            }

            position = reader.Align() + bodyLength;
            lengths.Add(position - start);
        }

        Assert.Equal(body.Length, position);
        return lengths;
    }

    private static Image Import(byte[] bytes, int width, int height, PixelFormat format, ImageConfiguration configuration) => format switch
    {
        PixelFormat.Rgba32 => Image.ImportPixelBytes<Rgba32>(bytes, width, height, configuration: configuration),
        PixelFormat.Bgra32 => Image.ImportPixelBytes<Bgra32>(bytes, width, height, configuration: configuration),
        PixelFormat.Rgba64 => Image.ImportPixelBytes<Rgba64>(bytes, width, height, configuration: configuration),
        PixelFormat.Rgb24 => Image.ImportPixelBytes<Rgb24>(bytes, width, height, configuration: configuration),
        PixelFormat.Gray8 => Image.ImportPixelBytes<Gray8>(bytes, width, height, configuration: configuration),
        PixelFormat.Gray16 => Image.ImportPixelBytes<Gray16>(bytes, width, height, configuration: configuration),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Converts the test image (flattened on white when the format has no alpha).</summary>
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

    /// <summary>Gets the samples of the first frame in a pixel format (a conversion between Rgba32 and Bgra32 only reorders them).</summary>
    private static byte[] GetBytes(Image image, PixelFormat format)
    {
        return format switch
        {
            PixelFormat.Rgba32 => Copy<Rgba32>(),
            PixelFormat.Bgra32 => Copy<Bgra32>(),
            PixelFormat.Rgba64 => Copy<Rgba64>(),
            PixelFormat.Rgb24 => Copy<Rgb24>(),
            PixelFormat.Gray8 => Copy<Gray8>(),
            PixelFormat.Gray16 => Copy<Gray16>(),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        byte[] Copy<TPixel>()
            where TPixel : unmanaged
        {
            using var converted = image.CloneAs<TPixel>();
            var bytes = new byte[converted.Width * converted.Height * System.Runtime.CompilerServices.Unsafe.SizeOf<TPixel>()];
            converted.Frames[0].CopyPixelBytesTo(bytes);
            return bytes;
        }
    }

    /// <summary>
    /// An image with the content of a photograph, from integers only: a sky that changes slowly, hills, a textured ground,
    /// a bright disc with a sharp edge, and a little noise.
    /// </summary>
    private static Image<Rgb24> CreateLandscape()
    {
        const int Width = 256;
        const int Height = 192;
        var pixels = new Rgb24[Width * Height];
        var state = 12345u;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                state = (state * 1664525) + 1013904223;
                var noise = (int)(state >> 30) - 1;
                var hill = 96 + (Triangle(x, 160) / 6) + (Triangle(x + 40, 67) / 12);
                int red;
                int green;
                int blue;
                if (y < hill)
                {
                    red = 90 + (y / 2) + (Triangle(x + (2 * y), 190) / 10);
                    green = 140 + (y / 3) + (Triangle(x - y, 230) / 12);
                    blue = 220 - (y / 4);
                }
                else
                {
                    var texture = (Triangle((3 * x) + y, 13) + Triangle(x - (2 * y), 9) + Triangle(x + (5 * y), 31)) / 9;
                    red = 60 + texture + ((y - hill) / 2);
                    green = 90 + texture + (Triangle(x, 120) / 8);
                    blue = 40 + (texture / 2);
                }

                var dx = x - 190;
                var dy = y - 50;
                if ((dx * dx) + (dy * dy) < 24 * 24)
                {
                    red = 250 - (dx / 3);
                    green = 225 + (dy / 4);
                    blue = 120;
                }

                pixels[(y * Width) + x] = new Rgb24((byte)Math.Clamp(red + noise, 0, 255), (byte)Math.Clamp(green + noise, 0, 255), (byte)Math.Clamp(blue + (noise / 2), 0, 255));
            }
        }

        return Image.ImportPixelData<Rgb24>(pixels, Width, Height);

        // A wave between 0 and 255
        static int Triangle(int value, int period)
        {
            var phase = ((value % period) + period) % period;
            return Math.Abs((2 * phase) - period) * 255 / period;
        }
    }

    /// <summary>Flat areas, gradients, noise, translucent and fully transparent pixels with defined colors.</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image<Rgba32> CreateImage(int width, int height, int seed)
    {
        var random = new Random(seed);
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = (((x / 3) + (y / 40)) % 4) switch
                {
                    0 => new Rgba32(30, 60, 90, 255),
                    1 => new Rgba32((byte)(x * 5), (byte)(y * 7), (byte)(x + y), 255),
                    2 => new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)),
                    _ => new Rgba32((byte)random.Next(256), (byte)x, (byte)y, 0),
                };
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    private static TPixel[] GetPixels<TPixel>(ImageFrame<TPixel> frame)
        where TPixel : unmanaged
    {
        var pixels = new TPixel[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }
}
