using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
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
    }

    [Fact]
    public void SettingsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { Quality = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { Quality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { Compression = (Jpeg2000Compression)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { Container = (Jpeg2000Container)2 });
        Assert.Equal(1, new Jpeg2000Encoder { Quality = 1 }.Quality);
        Assert.Equal(100, new Jpeg2000Encoder { Quality = 100 }.Quality);
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
            foreach (var (extension, isFile) in new[] { (".JP2", true), (".j2k", false), (".j2c", false), (".jpc", false) })
            {
                var path = directory / ("image" + extension);
                image.Save(path);
                Assert.Equal(ImageFormat.Jpeg2000, Image.Identify(path).Format);
                Assert.Equal(isFile, File.ReadAllBytes(path).AsSpan().StartsWith(FileSignature));
                using var decoded = Image.Load<Rgba32>(path);
                Assert.Equal(GetPixels(image.Frames[0]), GetPixels(decoded.Frames[0]));
            }

            // The files of these extensions are read, never written
            foreach (var extension in new[] { ".jph", ".jhc", ".jpx", ".jpf" })
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
