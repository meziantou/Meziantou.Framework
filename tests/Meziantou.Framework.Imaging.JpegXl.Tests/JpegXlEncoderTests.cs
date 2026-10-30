using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// JPEG XL encoding through the public save and writer APIs: the bytes of a hand-computed image header, the boxes of the
/// container and when it is used, exact round trips of every pixel format through the library decoder (itself compared with
/// djxl and jxl-oxide on files written by cjxl in the interop job), the metadata and animation policies, and streaming.
/// djxl, jxl-oxide and FFmpeg decode the outputs in the interop job.
/// </summary>
public sealed class JpegXlEncoderTests
{
    private static readonly byte[] ContainerSignature = [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    // A little-endian TIFF header and an image file directory without entry
    private static readonly byte[] EmptyExif = [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    [Fact]
    public void SettingsHaveDocumentedDefaults()
    {
        var encoder = new JpegXlEncoder();
        Assert.Equal(ImageFormat.JpegXl, encoder.Format);
        Assert.Equal(5, encoder.Effort);
        Assert.Equal(FrameDurationRounding.RoundToNearest, encoder.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);
    }

    [Fact]
    public void SettingsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegXlEncoder { Effort = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegXlEncoder { Effort = 10 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegXlEncoder { DurationRounding = (FrameDurationRounding)2 });
        Assert.Equal(0, new JpegXlEncoder { Effort = 0 }.Effort);
        Assert.Equal(9, new JpegXlEncoder { Effort = 9 }.Effort);
    }

    /// <summary>
    /// The image header of an 8x8 opaque sRGB image, bit by bit, least significant bit first: the signature; a small size
    /// (1), a height of 8 (five bits, 0), no aspect ratio (three bits, 0) and a width of 8 (five bits, 0); metadata that are
    /// not all default (0) and have no extra field (0); integer samples (0) of 8 bits (selector 0, two bits); 16-bit buffers
    /// are sufficient (1); no extra channel (selector 0, two bits); not XYB (0); the default color encoding (1); no extension
    /// (selector 0, two bits); the default transform data (1); zero bits up to the byte boundary where the frame starts.
    /// </summary>
    [Fact]
    public void TheImageHeaderOfASmallImageIsWrittenBitByBit()
    {
        using var image = new Image<Rgb24>(8, 8);
        var data = Save(image, new JpegXlEncoder());
        Assert.Equal([0xFF, 0x0A, 0x01, 0x00, 0x88, 0x04], data[..6]);
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
        // One group, a size the header stores in its short form, sides of one sample, groups of 256 samples with partial
        // groups on both sides, and a side of exactly one and two groups
        foreach (var (width, height) in new[] { (23, 11), (8, 16), (1, 1), (1, 70), (70, 1), (300, 270), (256, 256), (512, 3), (257, 2) })
        {
            using var source = CreateImage(width, height, seed: (int)format + width);
            using var image = ConvertTo(source, format);
            var data = Save(image, new JpegXlEncoder());
            var context = $"{format} {width}x{height}";

            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
            var storedFormat = format == PixelFormat.Bgra32 ? PixelFormat.Rgba32 : format;
            Assert.Equal((ImageFormat.JpegXl, width, height, storedFormat, bitsPerComponent), (info.Format, info.Width, info.Height, info.PixelFormat, info.BitsPerComponent));
            Assert.Equal((1, false), (info.FrameCount, info.IsAnimated));
            Assert.Equal(format is PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgba64, info.MayHaveTransparency);

            using var decoded = Image.Load(data);
            Assert.Equal(storedFormat, decoded.PixelFormat);
            Assert.True(GetBytes(image, storedFormat).AsSpan().SequenceEqual(GetBytes(decoded, storedFormat)), $"{context}: the decoded samples differ.");

            // Samples of more than 8 bits need the level 10, which only the container declares
            Assert.Equal(bitsPerComponent == 16, data.AsSpan().StartsWith(ContainerSignature));
        }
    }

    [Fact]
    public void HiddenColorsOfTransparentPixelsArePreserved()
    {
        Rgba32[] pixels = [new(10, 20, 30, 0), new(200, 100, 50, 0), new(10, 20, 30, 0), new(0, 0, 0, 0)];
        using var image = Image.ImportPixelData<Rgba32>(pixels, 2, 2);
        using var decoded = Image.Load<Rgba32>(Save(image, new JpegXlEncoder()));
        Assert.Equal(pixels, GetPixels(decoded.Frames[0]));
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Gray16)]
    [InlineData(PixelFormat.Rgba64)]
    public void TheEffortNeverChangesTheDecodedPixels(PixelFormat format)
    {
        // Smooth content and noise, so that the searched transforms and predictors differ between the groups
        using var source = CreateImage(300, 270, seed: 21);
        using var image = ConvertTo(source, format);
        var expected = GetBytes(image, format);
        var sizes = new List<int>();
        for (var effort = 0; effort <= 9; effort++)
        {
            var data = Save(image, new JpegXlEncoder { Effort = effort });
            sizes.Add(data.Length);
            using var decoded = Image.Load(data);
            Assert.True(expected.AsSpan().SequenceEqual(GetBytes(decoded, format)), $"{format}, effort {effort}: the decoded samples differ.");

            // The output only depends on the image and on the settings
            Assert.Equal(data, Save(image, new JpegXlEncoder { Effort = effort }));
        }

        // A search never selects a larger estimate than the fixed choice it starts from; estimates are close to the sizes
        Assert.True(sizes[5] <= sizes[0] * 1.01, $"{format}: effort 5 writes {sizes[5]} bytes, effort 0 writes {sizes[0]} bytes.");
        Assert.True(sizes[9] <= sizes[5] * 1.01, $"{format}: effort 9 writes {sizes[9]} bytes, effort 5 writes {sizes[5]} bytes.");
    }

    [Fact]
    public void SmoothAndFlatImagesAreCompressed()
    {
        var pixels = new Rgb24[256 * 256];
        for (var i = 0; i < pixels.Length; i++)
        {
            var (x, y) = (i % 256, i / 256);
            pixels[i] = new Rgb24((byte)x, (byte)y, (byte)((x + y) / 2));
        }

        // The fixed color transform and predictor of the lowest efforts leave more in the residuals of this image than the
        // ones that are searched from effort 3
        using var image = Image.ImportPixelData<Rgb24>(pixels, 256, 256);
        var sampleCount = pixels.Length * 3;
        foreach (var (effort, maxBitsPerSample) in new[] { (0, 1.25), (2, 1.25), (3, 0.5), (5, 0.5), (9, 0.5) })
        {
            var data = Save(image, new JpegXlEncoder { Effort = effort });
            Assert.True(data.Length * 8.0 / sampleCount < maxBitsPerSample, $"Effort {effort}: {data.Length} bytes for {sampleCount} samples.");
        }

        // A flat image only holds the descriptions of its codes, at every effort: a prefix code would spend a bit on each
        // of the samples of a channel that has a first sample and then no residual
        using var flat = new Image<Rgba32>(256, 256);
        var color = new Rgba32[256 * 256];
        color.AsSpan().Fill(new Rgba32(100, 150, 200, 255));
        using var solid = Image.ImportPixelData<Rgba32>(color, 256, 256);
        foreach (var effort in new[] { 0, 2, 5 })
        {
            Assert.InRange(Save(flat, new JpegXlEncoder { Effort = effort }).Length, 1, 200);
            Assert.InRange(Save(solid, new JpegXlEncoder { Effort = effort }).Length, 1, 200);
        }
    }

    /// <summary>
    /// The alpha channel of an opaque image is constant: it costs the description of a code at every effort, not a bit
    /// per pixel.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void AnOpaqueAlphaChannelIsAlmostFree(int effort)
    {
        using var source = CreateImage(300, 270, seed: 5);
        using var opaque = source.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0, ushort.MaxValue) });
        using var withAlpha = opaque.CloneAs<Rgba32>();
        var encoder = new JpegXlEncoder { Effort = effort };
        var colors = Save(opaque, encoder).Length;
        var all = Save(withAlpha, encoder).Length;

        // Two groups in each direction: a few bytes per group for the code of the channel, when a bit per pixel is 10,125 bytes
        Assert.True(all - colors < 256, $"Effort {effort}: {all} bytes with an opaque alpha channel, {colors} bytes without it.");
    }

    /// <summary>
    /// More effort does not write a larger file for a small image either: the ANS coder needs a longer description than a
    /// prefix code, which is what a stream of a few hundred samples is made of.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 3)]
    [InlineData(16, 16)]
    [InlineData(33, 21)]
    [InlineData(64, 48)]
    public void MoreEffortIsNotLargerForASmallImage(int width, int height)
    {
        using var source = CreateImage(width, height, seed: width + height);
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Rgb24, PixelFormat.Gray16 })
        {
            using var image = ConvertTo(source, format);
            var fastest = Save(image, new JpegXlEncoder { Effort = 0 }).Length;
            var searched = Save(image, new JpegXlEncoder { Effort = 5 }).Length;

            // The predictors are searched on an estimate of the size of the residuals, which leaves the descriptions out
            Assert.True(searched <= fastest + 4, $"{width}x{height} {format}: effort 5 writes {searched} bytes, effort 0 writes {fastest} bytes.");
        }
    }

    public static TheoryData<string, int> EntropyStreams => new()
    {
        { "empty", 0 },
        { "single", 1 },
        { "constant", 5000 },
        { "almost-constant", 50000 },
        { "two-tokens", 3000 },
        { "geometric", 20000 },
        { "geometric", 300 },
        { "uniform", 40 },
        { "uniform", 20000 },
        { "large-values", 2000 },
        { "skewed-contexts", 10000 },
    };

    /// <summary>
    /// The three ways to write a stream are read back by the decoder, and <see cref="JpegXlEntropyCoding.Smallest"/>
    /// writes one of the two others: the smaller one, up to the few bits that the final state of the ANS coder takes.
    /// </summary>
    [Theory]
    [MemberData(nameof(EntropyStreams))]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void TheEntropyEncoderWritesTheSmallerOfPrefixCodesAndAns(string kind, int count)
    {
        const int ContextCount = 3;
        var config = new JpegXlHybridUintConfig(4, 1, 0);
        var random = new Random(count + kind.Length);
        var values = new (int Context, uint Value)[count];
        for (var i = 0; i < count; i++)
        {
            var context = kind == "skewed-contexts" ? (i % 7 == 0 ? 1 : 0) : i % ContextCount;
            var value = kind switch
            {
                "constant" => 0u,
                "almost-constant" => random.Next(500) == 0 ? (uint)random.Next(1, 40) : 0u,
                "two-tokens" => (uint)random.Next(2),
                "geometric" => (uint)Math.Min(1_000_000, -8 * Math.Log(1 - random.NextDouble())),
                "uniform" => (uint)random.Next(64),
                "large-values" => (uint)random.NextInt64(1L << 32),
                "skewed-contexts" => context == 1 ? (uint)random.Next(1 << 20) : (random.Next(50) == 0 ? 3u : 0u),
                _ => 7u,
            };
            values[i] = (context, value);
        }

        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(TheEntropyEncoderWritesTheSmallerOfPrefixCodesAndAns));
        var prefix = Write(JpegXlEntropyCoding.PrefixCodes, out var prefixBits);
        var ans = Write(JpegXlEntropyCoding.Ans, out var ansBits);
        var smallest = Write(JpegXlEntropyCoding.Smallest, out var smallestBits);

        Assert.True(smallest.AsSpan().SequenceEqual(prefix) || smallest.AsSpan().SequenceEqual(ans), $"{kind} x{count}: the smallest stream is neither of the two others.");
        Assert.True(smallestBits <= Math.Min(prefixBits, ansBits) + 32, $"{kind} x{count}: {smallestBits} bits are written, prefix codes take {prefixBits} bits and the ANS coder {ansBits} bits.");

        // What each coding is for: a value that is almost certain costs a fraction of a bit with the ANS coder only, and a
        // short stream is mostly the description of its codes
        switch (kind, count)
        {
            case ("almost-constant", _):
                Assert.True(ansBits * 10 < prefixBits, $"{ansBits} bits with the ANS coder, {prefixBits} bits with prefix codes.");
                Assert.Equal(ansBits, smallestBits);
                break;

            case ("uniform", 40) or ("geometric", 300):
                Assert.True(prefixBits < ansBits, $"{ansBits} bits with the ANS coder, {prefixBits} bits with prefix codes.");
                Assert.Equal(prefixBits, smallestBits);
                break;
        }

        byte[] Write(JpegXlEntropyCoding coding, out long bitCount)
        {
            using var stream = new JpegXlEntropyEncoder(ContextCount, config, scope, coding);
            foreach (var (context, value) in values)
            {
                stream.Add(context, value);
            }

            using var writer = new LsbBitWriter(scope);
            stream.Write(writer);
            bitCount = writer.BitCount;
            writer.Finish();
            var data = writer.WrittenSpan.ToArray();

            // The decoder reads the values that were written, and ends in the state the stream started from
            var reader = new JpegXlBitReader(data, 0, data.Length);
            using var code = JpegXlEntropyCode.Read(reader, ContextCount, scope);
            if (coding != JpegXlEntropyCoding.Smallest)
            {
                Assert.Equal(coding == JpegXlEntropyCoding.PrefixCodes, code.UsesPrefixCodes);
            }

            using var decoder = new JpegXlEntropyDecoder(code, scope);
            decoder.Begin(reader);
            foreach (var (context, value) in values)
            {
                Assert.Equal(value, decoder.Read(reader, context));
            }

            decoder.EnsureFinalState();
            return data;
        }
    }

    [Fact]
    public void ABareCodestreamIsWrittenUnlessTheContainerIsNeeded()
    {
        using var image = CreateImage(9, 7, seed: 2);
        var bare = Save(image, new JpegXlEncoder());
        Assert.Equal([0xFF, 0x0A], bare[..2]);

        // EXIF and XMP metadata are boxes: after the first part of the codestream (the image header), before the frame
        image.Metadata.ExifProfile = new ExifProfile(new MetadataBlob(EmptyExif));
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob("<x:xmpmeta xmlns:x='adobe:ns:meta/'/>"u8));
        var boxes = ReadBoxes(Save(image, new JpegXlEncoder()));
        Assert.Equal(["JXL ", "ftyp", "jxlp", "Exif", "xml ", "jxlp"], boxes.Select(box => box.Type));
        Assert.Equal("jxl \0\0\0\0jxl "u8.ToArray(), boxes[1].Payload);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(boxes[2].Payload));
        Assert.Equal(0x80000001, BinaryPrimitives.ReadUInt32BigEndian(boxes[5].Payload));

        // The parts of the codestream, one after the other, are the bare codestream of the same image
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        Assert.Equal(bare, [.. boxes[2].Payload[4..], .. boxes[5].Payload[4..]]);

        // The EXIF payload follows the offset of its TIFF header; the XMP packet is stored as it is
        Assert.Equal([0, 0, 0, 0, 0x49, 0x49, 0x2A, 0x00], boxes[3].Payload[..8]);
        Assert.Equal("<x:xmpmeta xmlns:x='adobe:ns:meta/'/>"u8.ToArray(), boxes[4].Payload);

        // 16-bit samples: the level box follows the file type box
        using var deep = image.CloneAs<Rgba64>();
        boxes = ReadBoxes(Save(deep, new JpegXlEncoder()));
        Assert.Equal(["JXL ", "ftyp", "jxll", "jxlp", "jxlp"], boxes.Select(box => box.Type));
        Assert.Equal([10], boxes[2].Payload);
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
            var data = Save(image, new JpegXlEncoder());

            // A header identification reads everything that precedes the frame
            foreach (var metadata in new[] { Image.Identify(data).Metadata, LoadMetadata(data) })
            {
                Assert.Equal(icc.Data.ToArray(), metadata.IccProfile?.Data.ToArray());
                Assert.Equal(xmp, metadata.XmpProfile?.Data.ToArray());
                Assert.Equal(ExifOrientation.LeftBottom, metadata.Orientation);
                Assert.NotNull(metadata.ExifProfile);
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

    [Fact]
    public void AGrayProfileDescribesGraySamples()
    {
        using var source = CreateImage(6, 5, seed: 4);
        using var image = ConvertTo(source, PixelFormat.Gray8);
        var icc = IccTestProfiles.Gray(IccTestProfiles.Gamma(563));
        image.Metadata.IccProfile = icc;
        var data = Save(image, new JpegXlEncoder());
        using var decoded = Image.Load(data);
        Assert.Equal(PixelFormat.Gray8, decoded.PixelFormat);
        Assert.Equal(icc.Data.ToArray(), decoded.Metadata.IccProfile?.Data.ToArray());
        Assert.Equal(GetBytes(image, PixelFormat.Gray8), GetBytes(decoded, PixelFormat.Gray8));
    }

    [Theory]
    [InlineData(ExifOrientation.TopLeft)]
    [InlineData(ExifOrientation.TopRight)]
    [InlineData(ExifOrientation.BottomRight)]
    [InlineData(ExifOrientation.BottomLeft)]
    [InlineData(ExifOrientation.LeftTop)]
    [InlineData(ExifOrientation.RightTop)]
    [InlineData(ExifOrientation.RightBottom)]
    [InlineData(ExifOrientation.LeftBottom)]
    public void TheOrientationIsStoredInTheImageHeader(ExifOrientation orientation)
    {
        using var image = CreateImage(5, 3, seed: 5);
        image.Metadata.Orientation = orientation;
        var data = Save(image, new JpegXlEncoder());

        // No EXIF profile is created for it
        Assert.Equal([0xFF, 0x0A], data[..2]);
        var info = Image.Identify(data);
        Assert.Equal(orientation, info.Metadata.Orientation);
        Assert.Null(info.Metadata.ExifProfile);
        Assert.Equal((5, 3), (info.Width, info.Height));
    }

    [Fact]
    public void TheLinearTransferFunctionIsAnEnumeratedColorEncoding()
    {
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Gray8, PixelFormat.Rgba64 })
        {
            using var source = CreateImage(4, 4, seed: 9);
            using var image = ConvertTo(source, format);
            image.Metadata.TransferFunction = ColorTransferFunction.Linear;
            var linear = Save(image, new JpegXlEncoder());
            using var decoded = Image.Load(linear);
            Assert.Equal(ColorTransferFunction.Linear, decoded.Metadata.TransferFunction);
            Assert.Null(decoded.Metadata.Cicp);
            Assert.Null(decoded.Metadata.IccProfile);
            Assert.Equal(GetBytes(image, format), GetBytes(decoded, format));

            // Strip writes no optional metadata: the samples are labeled sRGB
            using var stripped = Image.Load(Save(image, new JpegXlEncoder { MetadataHandling = MetadataHandling.Strip }));
            Assert.Equal(ColorTransferFunction.Srgb, stripped.Metadata.TransferFunction);
        }
    }

    [Theory]
    [InlineData(9, 16)] // BT.2020 primaries, PQ
    [InlineData(9, 18)] // BT.2020 primaries, HLG
    [InlineData(12, 13)] // Display P3
    [InlineData(11, 17)] // DCI P3
    [InlineData(1, 1)] // BT.709
    [InlineData(9, 8)] // BT.2020 primaries, linear
    public void ACicpDescriptionIsAnEnumeratedColorEncoding(byte primaries, byte transfer)
    {
        foreach (var format in new[] { PixelFormat.Rgb24, PixelFormat.Rgba64 })
        {
            using var source = CreateImage(4, 4, seed: 10);
            using var image = ConvertTo(source, format);

            // The matrix and the range describe an encoded source: the format stores R'G'B' samples of the full range
            image.Metadata.Cicp = new CicpColorDescription(primaries, transfer, matrixCoefficients: 9, fullRange: false);
            var data = Save(image, new JpegXlEncoder());
            foreach (var metadata in new[] { Image.Identify(data).Metadata, LoadMetadata(data) })
            {
                Assert.Equal(new CicpColorDescription(primaries, transfer, matrixCoefficients: 0, fullRange: true), metadata.Cicp);
                Assert.Null(metadata.IccProfile);
            }

            using var decoded = Image.Load(data);
            Assert.Equal(GetBytes(image, format), GetBytes(decoded, format));
        }

        static ImageMetadata LoadMetadata(byte[] data)
        {
            using var image = Image.Load(data);
            return image.Metadata.Clone();
        }
    }

    [Fact]
    public void ACicpDescriptionTheFormatCannotEnumerateFollowsTheMetadataPolicy()
    {
        // BT.601 primaries, and a transfer function that JPEG XL does not number
        foreach (var cicp in new[] { new CicpColorDescription(5, 13, 0, fullRange: true), new CicpColorDescription(9, 14, 0, fullRange: true) })
        {
            using var image = CreateImage(4, 4, seed: 11);
            image.Metadata.Cicp = cicp;
            using var stream = new TestOutputStream();
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new JpegXlEncoder()));
            Assert.StartsWith("Metadata: CICP color description", exception.Feature, StringComparison.Ordinal);
            Assert.Equal(0, stream.BytesWritten);

            using var decoded = Image.Load(Save(image, new JpegXlEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported }));
            Assert.Null(decoded.Metadata.Cicp);
        }

        // A description that is displayed like sRGB is not stored: the default encoding says the same
        using var srgb = CreateImage(4, 4, seed: 12);
        srgb.Metadata.Cicp = new CicpColorDescription(1, 13, 0, fullRange: true);
        using var reloaded = Image.Load(Save(srgb, new JpegXlEncoder()));
        Assert.Null(reloaded.Metadata.Cicp);
    }

    [Fact]
    public void OtherMetadataFollowsTheMetadataPolicy()
    {
        var cases = new (string Feature, Action<ImageMetadata> Set)[]
        {
            ("Metadata: resolution", metadata => metadata.Resolution = new ImageResolution(72, 72)),
            ("Metadata: text entry 'Comment'", metadata => metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "lorem ipsum"))),
        };
        foreach (var (feature, set) in cases)
        {
            using var image = CreateImage(3, 2, seed: 1);
            set(image.Metadata);
            using var stream = new TestOutputStream();
            Assert.Equal(feature, Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new JpegXlEncoder())).Feature);
            Assert.Equal(0, stream.BytesWritten);

            var discarded = Save(image, new JpegXlEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
            var stripped = Save(image, new JpegXlEncoder { MetadataHandling = MetadataHandling.Strip });
            Assert.Equal(discarded, stripped);
            using var decoded = Image.Load<Rgba32>(discarded);
            Assert.Equal(GetPixels(image.Frames[0]), GetPixels(decoded.Frames[0]));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void AnimationsRoundTrip(int? totalPlays)
    {
        // Milliseconds are exact; the largest duration is 2^32 - 1 milliseconds
        FrameDuration[] durations = [new(1, 10), new(7, 1000), new(1, 1), new(uint.MaxValue, 1000), new(1, 1000)];
        using var image = CreateAnimation(40, 30, durations);
        image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Gray8, PixelFormat.Rgba64 })
        {
            using var converted = ConvertTo(image, format);
            var data = Save(converted, new JpegXlEncoder());
            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
            Assert.Equal((durations.Length, true, totalPlays), (info.FrameCount, info.IsAnimated, info.Animation?.TotalPlays));

            using var decoded = Image.Load(data);
            Assert.Equal(totalPlays, decoded.Animation?.TotalPlays);
            Assert.Equal(durations.Length, decoded.Frames.Count);
            for (var i = 0; i < durations.Length; i++)
            {
                Assert.Equal(0, durations[i].CompareTo(decoded.Frames[i].Metadata.Duration));
                Assert.True(GetFrameBytes(converted, i, format).AsSpan().SequenceEqual(GetFrameBytes(decoded, i, format)), $"{format}: the samples of frame {i} differ.");
            }
        }
    }

    [Theory]
    [InlineData(1, 3, 333)]
    [InlineData(2, 3, 667)]
    [InlineData(1, 2000, 1)] // ties are rounded up
    [InlineData(1001, 30000, 33)]
    public void InexactDurationsAreRoundedToTheNearestMillisecondUnlessExactnessIsRequired(long numerator, long denominator, int milliseconds)
    {
        using var image = CreateAnimation(4, 4, [new(numerator, denominator), new(1, 10)]);
        using var decoded = Image.Load(Save(image, new JpegXlEncoder()));
        Assert.Equal(0, new FrameDuration(milliseconds, 1000).CompareTo(decoded.Frames[0].Metadata.Duration));

        using var stream = new TestOutputStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new JpegXlEncoder { DurationRounding = FrameDurationRounding.RequireExact }));
        Assert.Equal("JPEG XL frame duration precision", exception.Feature);
        Assert.Equal(0, stream.BytesWritten);
    }

    [Theory]
    [InlineData(4294967296, 1000)]
    [InlineData(long.MaxValue, 1)]
    public void OutOfRangeDurationsThrowInsteadOfClamping(long numerator, long denominator)
    {
        using var image = CreateAnimation(4, 4, [new(1, 10), new(numerator, denominator)]);
        foreach (var rounding in new[] { FrameDurationRounding.RoundToNearest, FrameDurationRounding.RequireExact })
        {
            using var stream = new TestOutputStream();
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new JpegXlEncoder { DurationRounding = rounding }));
            Assert.Equal("JPEG XL frame duration range", exception.Feature);
            Assert.Equal(0, stream.BytesWritten);
        }
    }

    /// <summary>JPEG XL defines a frame without duration as a layer of the next frame: it would be merged into it, not shown.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2001)] // rounded to zero milliseconds
    public void AFrameOfAnAnimationMustHaveADuration(long numerator, long denominator)
    {
        foreach (var index in new[] { 0, 1, 2 })
        {
            FrameDuration[] durations = [new(1, 10), new(1, 10), new(1, 10)];
            durations[index] = new FrameDuration(numerator, denominator);
            using var image = CreateAnimation(4, 4, durations);
            using var stream = new TestOutputStream();
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new JpegXlEncoder()));
            Assert.Equal("JPEG XL frame without duration", exception.Feature);
            Assert.Equal(0, stream.BytesWritten);
        }

        // A still image has no duration; an animation of one frame shows it whatever its duration
        using var still = CreateImage(4, 4, seed: 1);
        using (var decoded = Image.Load(Save(still, new JpegXlEncoder())))
        {
            Assert.Null(decoded.Animation);
        }

        still.Animation = new AnimationMetadata { TotalPlays = 2 };
        using (var decoded = Image.Load(Save(still, new JpegXlEncoder())))
        {
            Assert.Equal(2, decoded.Animation?.TotalPlays);
            Assert.Equal(1, decoded.Frames.Count);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WritersStreamFramesToNonSeekableOutput(bool asynchronous)
    {
        FrameDuration[] durations = [new(1, 10), new(1, 5), new(3, 10)];
        using var image = CreateAnimation(300, 200, durations);
        using var stream = new TestOutputStream();
        var written = new List<long>();
        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(300, 200) { Encoder = new JpegXlEncoder() }))
        {
            for (var i = 0; i < durations.Length; i++)
            {
                if (asynchronous)
                {
                    await writer.WriteFrameAsync(image.Frames[i], XunitCancellationToken);
                }
                else
                {
                    writer.WriteFrame(image.Frames[i]);
                }

                written.Add(stream.BytesWritten);
            }

            if (asynchronous)
            {
                await writer.CompleteAsync(XunitCancellationToken);
            }
            else
            {
                writer.Complete();
            }

            Assert.Equal(0, writer.Core.Scope.LiveBytes);
        }

        // A frame header tells whether the frame is the last one: a frame is written when the next one, or the end, is known
        Assert.True(written[0] < 64, $"{written[0]} bytes were written after the first frame.");
        Assert.True(written[1] > written[0] + 1000 && written[2] > written[1] + 1000 && stream.BytesWritten > written[2] + 1000, $"Bytes written after each frame: {string.Join(", ", written)}, then {stream.BytesWritten}.");

        var data = stream.ToArray();
        Assert.Equal(Save(image, new JpegXlEncoder()), data);
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(durations.Length, decoded.Frames.Count);
        for (var i = 0; i < durations.Length; i++)
        {
            Assert.Equal(0, durations[i].CompareTo(decoded.Frames[i].Metadata.Duration));
            Assert.Equal(GetPixels(image.Frames[i]), GetPixels(decoded.Frames[i]));
        }
    }

    [Fact]
    public void AStillImageStoresOneFrame()
    {
        using var still = CreateImage(4, 3, seed: 6);
        using var output = new MemoryStream();
        using var writer = Image.CreateWriter<Rgba32>(output, new ImageWriterOptions(4, 3) { Encoder = new JpegXlEncoder(), ExpectedFrameCount = 1 });
        writer.WriteFrame(still.Frames[0]);
        Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(still.Frames[0]));
        writer.Complete();
        using var decoded = Image.Load<Rgba32>(output.ToArray());
        Assert.Null(decoded.Animation);
        Assert.Equal(GetPixels(still.Frames[0]), GetPixels(decoded.Frames[0]));
    }

    [Fact]
    public void PathSavesInferJpegXlFromTheExtension()
    {
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("meziantou-jxl-"));
        try
        {
            using var image = CreateImage(5, 3, seed: 1);
            var path = directory / "image.JXL";
            image.Save(path);
            Assert.Equal(ImageFormat.JpegXl, Image.Identify(path).Format);
            using var decoded = Image.Load<Rgba32>(path);
            Assert.Equal(GetPixels(image.Frames[0]), GetPixels(decoded.Frames[0]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// From the effort 3, a lossless frame is coded with a tree learned from its samples, a palette or a color transform, and,
    /// from the effort 5, copies of previous values: screenshots (few colors, repeated glyphs), tiles of smooth gradients
    /// and noise, in every kind of pixel format, are decoded exactly, and the workers write the file of one worker.
    /// </summary>
    [Theory]
    [InlineData("screenshot", PixelFormat.Rgb24)]
    [InlineData("screenshot", PixelFormat.Rgba32)]
    [InlineData("screenshot", PixelFormat.Gray8)]
    [InlineData("screenshot", PixelFormat.Rgba64)]
    [InlineData("tiles", PixelFormat.Rgb24)]
    [InlineData("tiles", PixelFormat.Gray16)]
    [InlineData("noise", PixelFormat.Rgba32)]
    [InlineData("noise", PixelFormat.Gray16)]
    public void LearnedTreesRoundTripExactly(string kind, PixelFormat format)
    {
        using var source = CreateContent(kind, 520, 280, seed: 3);
        using var image = ConvertTo(source, format);
        var expected = GetBytes(image, format);
        var pixels = GetPixels(source.Frames[0]);
        foreach (var effort in (int[])[3, 5, 7, 9])
        {
            var encoder = new JpegXlEncoder { Effort = effort };
            var data = Save(image, encoder);
            using (var decoded = Image.Load(data))
            {
                Assert.True(expected.AsSpan().SequenceEqual(GetBytes(decoded, format)), $"{kind} {format}, effort {effort}: the decoded samples differ.");
            }

            using var copy = Image.ImportPixelData<Rgba32>(pixels, source.Width, source.Height, configuration: new ImageConfiguration { MaxDegreeOfParallelism = 4 });
            using var converted = ConvertTo(copy, format);
            Assert.Equal(data, Save(converted, encoder));
        }
    }

    /// <summary>
    /// What the learned tree is for: a screenshot is coded with a palette and copies of its repeated glyphs, tiles of
    /// gradients with a tree that tells the tiles apart and copies of the tiles; both are much smaller than with a leaf per
    /// channel (the efforts below 3), and more effort is not larger.
    /// </summary>
    [Theory]
    [InlineData("screenshot", 0.5)]
    [InlineData("tiles", 0.25)]
    public void ScreenshotsAndRepeatedTilesAreSmallerWithALearnedTree(string kind, double maxRatio)
    {
        using var image = CreateContent(kind, 600, 400, seed: 8);
        using var opaque = image.CloneAs<Rgb24>();
        var simple = Save(opaque, new JpegXlEncoder { Effort = 2 }).Length;
        var learned = Save(opaque, new JpegXlEncoder { Effort = 5 }).Length;
        var most = Save(opaque, new JpegXlEncoder { Effort = 9 }).Length;
        Assert.True(learned <= simple * maxRatio, $"{kind}: effort 5 writes {learned} bytes, effort 2 writes {simple} bytes.");
        Assert.True(most <= learned * 1.02, $"{kind}: effort 9 writes {most} bytes, effort 5 writes {learned} bytes.");
    }

    /// <summary>
    /// The entropy code of a learned tree: dozens of contexts in clusters, a configuration of hybrid integers per cluster,
    /// and copies of previous values found in a stream that repeats itself, at distances that are and are not among the
    /// short distances of a two-dimensional stream; the decoder reads the values back.
    /// </summary>
    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void TheModularEntropyCodeIsReadBackWithClustersConfigurationsAndCopies()
    {
        const int ContextCount = 48;
        const int RowLength = 40;
        var random = new Random(12);
        var values = new ulong[20_000];
        for (var i = 0; i < values.Length; i++)
        {
            var context = random.Next(ContextCount);
            uint value = (i % 2000) switch
            {
                // Repeated runs: the row above, a value far back, and the same value
                < 300 when i >= 4000 => (uint)values[i - RowLength],
                < 600 when i >= 4000 => (uint)values[i - 3999],
                < 700 => 5,
                // A value of its own in a third of the contexts, geometric values or large uniform values in the others
                _ when context % 3 == 0 => (uint)(context / 3),
                _ when context % 3 == 1 => (uint)(-(1 << (context % 24 / 2)) * Math.Log(1 - random.NextDouble())),
                _ => (uint)random.Next(1 << 18),
            };
            values[i] = ((ulong)context << 32) | value;
        }

        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(TheModularEntropyCodeIsReadBackWithClustersConfigurationsAndCopies));
        using var literal = new JpegXlModularEntropyCode(ContextCount, 256, searchConfigs: true, useLz77: false, scope);
        using (var counter = literal.CreateCounter())
        {
            foreach (var value in values)
            {
                counter.Add((int)(value >> 32), (uint)value);
            }

            literal.AddCounts(counter);
        }

        var parser = new JpegXlLz77Parser(literal.GetTokenCosts(), maxChain: 32);
        literal.Build();
        using var code = new JpegXlModularEntropyCode(ContextCount, 256, searchConfigs: true, useLz77: true, scope);
        using (var counter = code.CreateCounter())
        {
            var sink = new CountingSink(counter);
            parser.Parse(values, RowLength, ref sink);
            code.AddCounts(counter);
            Assert.True(sink.CopyCount > 0);
        }

        code.Build();
        Assert.True(code.ClusterCount > 8, $"{code.ClusterCount} clusters.");
        Assert.True(code.EstimatedBits < literal.EstimatedBits, $"{code.EstimatedBits} bits with copies, {literal.EstimatedBits} bits without.");

        using var writer = new LsbBitWriter(scope);
        code.WriteDescription(writer);
        using (var stream = new JpegXlEntropyCodeBuilder.StreamBuffer(scope))
        {
            var sink = new WritingSink(code, stream);
            parser.Parse(values, RowLength, ref sink);
            code.WriteStream(stream, writer);
        }

        writer.Finish();
        var data = writer.WrittenSpan.ToArray();
        var reader = new JpegXlBitReader(data, 0, data.Length);
        using var read = JpegXlEntropyCode.Read(reader, ContextCount, scope);
        Assert.True(read.IsLz77Enabled);
        using var decoder = new JpegXlEntropyDecoder(read, scope);
        decoder.Begin(reader);
        foreach (var value in values)
        {
            Assert.Equal((uint)value, decoder.Read(reader, (int)(value >> 32), RowLength));
        }

        decoder.EnsureFinalState();
        reader.EnsureNotOverrun();
    }

    /// <summary>
    /// The groups of a frame are coded by several workers when the configuration allows it (a frame of 600x300 pixels has
    /// six groups and more than the 65,536 pixels that several workers need): the file is the one of a single worker, for
    /// lossy and lossless output, the alpha channel that each group codes, and several passes.
    /// </summary>
    [Theory]
    [InlineData(JpegXlCompression.Lossy, 0, false)]
    [InlineData(JpegXlCompression.Lossy, 5, false)]
    [InlineData(JpegXlCompression.Lossy, 8, false)]
    [InlineData(JpegXlCompression.Lossy, 5, true)]
    [InlineData(JpegXlCompression.Lossless, 0, false)]
    [InlineData(JpegXlCompression.Lossless, 3, false)]
    [InlineData(JpegXlCompression.Lossless, 5, false)]
    [InlineData(JpegXlCompression.Lossless, 9, false)]
    public void FilesWrittenByWorkersAreTheFilesOfOneWorker(JpegXlCompression compression, int effort, bool progressive)
    {
        var encoder = new JpegXlEncoder { Compression = compression, Effort = effort, Progressive = progressive };
        using var image = CreateImage(600, 300, seed: 9);
        var expected = Save(image, encoder);
        var pixels = GetPixels(image.Frames[0]);
        foreach (var workers in (int[])[2, 4, 16])
        {
            using var copy = Image.ImportPixelData<Rgba32>(pixels, 600, 300, configuration: new ImageConfiguration { MaxDegreeOfParallelism = workers });
            Assert.Equal(expected, Save(copy, encoder));
        }
    }

    /// <summary>
    /// A failure of a worker is the failure of a sequential run: the one of the first group that fails, whichever worker
    /// fails first, a cancellation included; every state a worker created is disposed.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void GroupWorkersFailWithTheErrorOfTheFirstFailingGroup(int workers)
    {
        var states = new System.Collections.Concurrent.ConcurrentBag<TrackedState>();
        var exception = Assert.Throws<InvalidOperationException>(() => JpegXlGroupWorkers.Run(workers, 40, () => Track(states), (_, group) =>
        {
            if (group is 7 or 23 or 31)
                throw new InvalidOperationException($"group {group}");
        }));
        Assert.Equal("group 7", exception.Message);
        Assert.All(states, state => Assert.True(state.IsDisposed));

        Assert.Throws<OperationCanceledException>(() => JpegXlGroupWorkers.Run(workers, 40, () => Track(states), (_, group) =>
        {
            if (group == 3)
                throw new OperationCanceledException();

            if (group == 30)
                throw new InvalidOperationException($"group {group}");
        }));
        exception = Assert.Throws<InvalidOperationException>(() => JpegXlGroupWorkers.Run(workers, 40, () => Track(states), (_, group) =>
        {
            if (group == 3)
                throw new InvalidOperationException($"group {group}");

            if (group == 30)
                throw new OperationCanceledException();
        }));
        Assert.Equal("group 3", exception.Message);
        Assert.All(states, state => Assert.True(state.IsDisposed));

        // Every group runs once
        var runs = new int[40];
        JpegXlGroupWorkers.Run(workers, runs.Length, group => Interlocked.Increment(ref runs[group]));
        Assert.All(runs, count => Assert.Equal(1, count));

        static TrackedState Track(System.Collections.Concurrent.ConcurrentBag<TrackedState> states)
        {
            var state = new TrackedState();
            states.Add(state);
            return state;
        }
    }

    /// <summary>Walks the boxes of a container (ISO/IEC 18181-2): a 32-bit big-endian size that includes the header, and a type.</summary>
    private static List<(string Type, byte[] Payload)> ReadBoxes(byte[] data)
    {
        var boxes = new List<(string, byte[])>();
        var offset = 0;
        while (offset < data.Length)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset)));
            Assert.InRange(size, 8, data.Length - offset);
            boxes.Add((Encoding.Latin1.GetString(data, offset + 4, 4), data[(offset + 8)..(offset + size)]));
            offset += size;
        }

        Assert.Equal(ContainerSignature, data[..12]);
        return boxes;
    }

    private static Image<Rgba32> CreateAnimation(int width, int height, FrameDuration[] durations)
    {
        var image = CreateImage(width, height, seed: 100);
        for (var i = 1; i < durations.Length; i++)
        {
            using var frame = CreateImage(width, height, seed: 100 + i);
            image.AppendFrame(frame.Frames[0]);
        }

        for (var i = 0; i < durations.Length; i++)
        {
            image.Frames[i].Metadata.Duration = durations[i];
        }

        return image;
    }

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
    private static byte[] GetBytes(Image image, PixelFormat format) => GetFrameBytes(image, 0, format);

    private static byte[] GetFrameBytes(Image image, int frame, PixelFormat format)
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
            converted.Frames[frame].CopyPixelBytesTo(bytes);
            return bytes;
        }
    }

    /// <summary>
    /// A screenshot (text of a few glyphs in three colors with four levels of coverage on a light background, buttons, and a
    /// gradient band that gives it more than 64 colors), tiles of four gradient patterns, or the noisy test image.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image<Rgba32> CreateContent(string kind, int width, int height, int seed)
    {
        if (kind == "noise")
            return CreateImage(width, height, seed);

        var random = new Random(seed);
        var pixels = new Rgba32[width * height];
        if (kind == "tiles")
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var tile = (((x / 64) * 7) + ((y / 48) * 3)) % 4;
                    var (u, v) = (x % 64, y % 48);
                    var inside = ((u - 32) * (u - 32)) + ((v - 24) * (v - 24)) < 300;
                    pixels[(y * width) + x] = inside
                        ? new Rgba32((byte)(u * 4), (byte)(v * 5), (byte)(tile * 60), 255)
                        : u < 2 || v < 2 ? new Rgba32(40, 40, 40, 255) : new Rgba32((byte)(tile * 50), 120, (byte)(200 - (tile * 30)), 255);
                }
            }

            return Image.ImportPixelData<Rgba32>(pixels, width, height);
        }

        var background = new Rgba32(245, 247, 250, 255);
        pixels.AsSpan().Fill(background);
        var glyphs = new byte[12][];
        for (var i = 0; i < glyphs.Length; i++)
        {
            glyphs[i] = new byte[7 * 10];
            for (var j = 0; j < glyphs[i].Length; j++)
            {
                glyphs[i][j] = (byte)Math.Max(0, random.Next(-3, 5));
            }
        }

        Rgba32[] inks = [new(20, 24, 31, 255), new(9, 105, 218, 255), new(207, 34, 46, 255)];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < Math.Min(20, height); y++)
            {
                pixels[(y * width) + x] = new Rgba32((byte)(30 + (x * 200 / width)), 41, 59, 255);
            }
        }

        for (var line = 0; (line * 14) + 30 < height - 10; line++)
        {
            var ink = inks[line % 3 == 2 ? random.Next(inks.Length) : 0];
            var x = 10;
            while (x + 8 < width - 10)
            {
                var length = random.Next(2, 9);
                for (var i = 0; i < length && x + 8 < width - 10; i++, x += 8)
                {
                    var glyph = glyphs[random.Next(glyphs.Length)];
                    for (var gy = 0; gy < 10; gy++)
                    {
                        for (var gx = 0; gx < 7; gx++)
                        {
                            var coverage = glyph[(gy * 7) + gx];
                            ref var pixel = ref pixels[(((line * 14) + 30 + gy) * width) + x + gx];
                            pixel = new Rgba32(Mix(background.R, ink.R, coverage), Mix(background.G, ink.G, coverage), Mix(background.B, ink.B, coverage), 255);
                        }
                    }
                }

                x += 8;
            }

            if (line % 5 == 4)
            {
                for (var by = 0; by < 12; by++)
                {
                    for (var bx = width - 70; bx < width - 12; bx++)
                    {
                        pixels[(((line * 14) + 29 + by) * width) + bx] = new Rgba32(37, 99, 235, 255);
                    }
                }
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);

        static byte Mix(byte from, byte to, int coverage) => (byte)(from + ((to - from) * coverage / 4));
    }

    /// <summary>Counts what the LZ77 parser finds.</summary>
    private struct CountingSink(JpegXlModularEntropyCode.Counter counter) : JpegXlLz77Parser.IStreamSink
    {
        public int CopyCount { get; private set; }

        public readonly void AddValue(int context, uint value) => counter.Add(context, value);

        public void AddCopy(int context, int length, uint distance)
        {
            CopyCount++;
            counter.AddCopy(context, length, distance);
        }
    }

    /// <summary>Adds what the LZ77 parser finds to a stream.</summary>
    private readonly struct WritingSink(JpegXlModularEntropyCode code, JpegXlEntropyCodeBuilder.StreamBuffer stream) : JpegXlLz77Parser.IStreamSink
    {
        public void AddValue(int context, uint value) => code.Add(stream, context, value);

        public void AddCopy(int context, int length, uint distance) => code.AddCopy(stream, context, length, distance);
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

    private sealed class TrackedState : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
