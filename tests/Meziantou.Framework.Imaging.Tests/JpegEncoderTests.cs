using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Jpeg;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Baseline JPEG encoding through the public save and writer APIs. Output is validated without the library
/// decoder: the harness reader <see cref="ReferenceJpeg"/> checks the marker structure strictly and decodes the entropy-coded
/// data to quantized coefficients, which must equal the independent transcription of the encoder contract
/// (<see cref="JpegEncoderModel"/>: edge replication, JFIF YCbCr, chroma box filter, direct 2-D DCT, Annex K tables scaled
/// by the quality) except within 1e-3 of a rounding boundary; its plain reconstruction (direct IDCT, replicated chroma)
/// measures the quality per setting. Metadata is read with <see cref="EncodedFieldInspector"/>. The library decoder is only
/// a supplementary round trip. FFmpeg decodes the same kinds of outputs in the interoperability tests.
/// </summary>
public sealed class JpegEncoderTests
{
    private static readonly PixelFormat[] AllFormats = [PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Rgba64, PixelFormat.Gray8, PixelFormat.Gray16];
    private static readonly JpegChromaSubsampling[] AllSubsamplings = [JpegChromaSubsampling.Auto, JpegChromaSubsampling.Ratio444, JpegChromaSubsampling.Ratio422, JpegChromaSubsampling.Ratio420];

    public static TheoryData<PixelFormat, JpegChromaSubsampling> FormatCases()
    {
        var data = new TheoryData<PixelFormat, JpegChromaSubsampling>();
        foreach (var format in AllFormats)
        {
            foreach (var subsampling in AllSubsamplings)
            {
                data.Add(format, subsampling);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FormatCases))]
    public void EveryPixelFormatSamplingAndSizeMatchesTheEncoderModel(PixelFormat format, JpegChromaSubsampling subsampling)
    {
        foreach (var (width, height) in new[] { (1, 1), (7, 5), (8, 8), (16, 16), (17, 9), (33, 31), (2, 40), (40, 3) })
        {
            var pixels = JpegTestPatterns.ToFormat(JpegTestPatterns.PhotoLike(width, height, seed: width + height), format, LowBits);
            var encoder = new JpegEncoder { Quality = 75, ChromaSubsampling = subsampling, AllowBitDepthReduction = true };
            var jpeg = Encode(pixels, format, encoder);
            AssertMatchesModel(jpeg, JpegTestPatterns.ToEncodedSamples(pixels, background: null), encoder, $"{format} {width}x{height} {subsampling}");
        }
    }

    [Fact]
    public void QualityScalesTheAnnexKTables()
    {
        using var image = Rgb(JpegTestPatterns.PhotoLike(16, 16, seed: 1));
        var previousSize = 0;
        for (var quality = 1; quality <= 100; quality++)
        {
            var jpeg = Encode(image, new JpegEncoder { Quality = quality, ChromaSubsampling = JpegChromaSubsampling.Ratio444 });
            var reference = ReferenceJpeg.Parse(jpeg);
            Assert.Equal(JpegEncoderModel.ScaleTable(JpegEncoderModel.AnnexKLuminance, quality), reference.GetQuantizationTable(0));
            Assert.Equal(JpegEncoderModel.ScaleTable(JpegEncoderModel.AnnexKChrominance, quality), reference.GetQuantizationTable(1));
            Assert.Equal(0, reference.Components[0].QuantizationTable);
            Assert.Equal(1, reference.Components[1].QuantizationTable);
            Assert.Equal(1, reference.Components[2].QuantizationTable);
            if (quality % 10 == 0)
            {
                // Finer quantization never makes this image smaller by more than a few bytes of entropy-coding noise
                Assert.True(jpeg.Length + 16 >= previousSize, string.Create(CultureInfo.InvariantCulture, $"Quality {quality}: {jpeg.Length} bytes after {previousSize}."));
                previousSize = jpeg.Length;
            }
        }

        // Quality 50 is the Annex K table itself, 100 is all ones, 1 clamps to 255
        Assert.Equal(JpegEncoderModel.AnnexKLuminance, ReferenceJpeg.Parse(Encode(image, new JpegEncoder { Quality = 50 })).GetQuantizationTable(0));
        Assert.All(ReferenceJpeg.Parse(Encode(image, new JpegEncoder { Quality = 100 })).GetQuantizationTable(1), value => Assert.Equal(1, value));
        Assert.All(ReferenceJpeg.Parse(Encode(image, new JpegEncoder { Quality = 1 })).GetQuantizationTable(1), value => Assert.Equal(255, value));
        Assert.Equal(5000, JpegEncodingTables.GetQualityScale(1));
        Assert.Equal(102, JpegEncodingTables.GetQualityScale(49));
        Assert.Equal(100, JpegEncodingTables.GetQualityScale(50));
        Assert.Equal(0, JpegEncodingTables.GetQualityScale(100));
    }

    [Fact]
    public void HuffmanTablesAreTheTypicalAnnexKTables()
    {
        using var image = Rgb(JpegTestPatterns.PhotoLike(16, 16, seed: 1));
        var reference = ReferenceJpeg.Parse(Encode(image, new JpegEncoder()));
        Assert.Equal(new ReferenceJpegComponent(1, 2, 2, 0, 0, 0), reference.Components[0]);
        Assert.Equal(new ReferenceJpegComponent(2, 1, 1, 1, 1, 1), reference.Components[1]);
        Assert.Equal(new ReferenceJpegComponent(3, 1, 1, 1, 1, 1), reference.Components[2]);

        // Code lengths printed in T.81 tables K.3 to K.6
        var luminanceDc = reference.GetHuffmanCodeLengths(0, 0);
        Assert.Equal([2, 3, 3, 3, 3, 3, 4, 5, 6, 7, 8, 9], luminanceDc.Take(12));
        var chrominanceDc = reference.GetHuffmanCodeLengths(0, 1);
        Assert.Equal([2, 2, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], chrominanceDc.Take(12));
        var luminanceAc = reference.GetHuffmanCodeLengths(1, 0);
        Assert.Equal(4, luminanceAc[0x00]); // EOB 1010
        Assert.Equal(11, luminanceAc[0xF0]); // ZRL 11111111001
        Assert.Equal(2, luminanceAc[0x01]);
        Assert.Equal(16, luminanceAc[0xFA]);
        var chrominanceAc = reference.GetHuffmanCodeLengths(1, 1);
        Assert.Equal(2, chrominanceAc[0x00]); // EOB 00
        Assert.Equal(10, chrominanceAc[0xF0]); // ZRL 1111111010
        Assert.Equal(16, chrominanceAc[0xFA]);

        // Every baseline symbol has a code: DC categories 0-11; AC EOB, ZRL and every run/size with sizes 1-10
        foreach (var table in new[] { luminanceAc, chrominanceAc })
        {
            Assert.Equal(162, table.Count(length => length > 0));
            for (var run = 0; run < 16; run++)
            {
                for (var size = 1; size <= 10; size++)
                {
                    Assert.InRange(table[(run << 4) | size], 1, 16);
                }
            }
        }

        Assert.Throws<ArgumentException>(() => new JpegHuffmanEncoder([2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [0, 1, 2])); // 3 codes of length <= 2 plus one more: oversubscribed
        Assert.Throws<ArgumentException>(() => new JpegHuffmanEncoder([0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [5, 5]));
        Assert.Throws<ArgumentException>(() => new JpegHuffmanEncoder([0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [5]));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ForwardDctMatchesTheDirectTransform()
    {
        // The separable transform with literal basis constants equals the direct 2-D formula of T.81 A.3.3
        var random = new Random(15);
        Span<float> samples = stackalloc float[64];
        Span<int> coefficients = stackalloc int[64];
        var scratch = new double[64];
        var quantization = new ushort[64];
        quantization.AsSpan().Fill(1);
        var nearTies = 0;
        for (var iteration = 0; iteration < 5000; iteration++)
        {
            for (var i = 0; i < 64; i++)
            {
                samples[i] = iteration % 3 == 0 ? random.Next(256) - 128 : (float)((random.NextDouble() * 255.5) - 128);
            }

            JpegForwardDct.TransformAndQuantize(samples, quantization, scratch, coefficients);
            for (var v = 0; v < 8; v++)
            {
                for (var u = 0; u < 8; u++)
                {
                    var sum = 0d;
                    for (var y = 0; y < 8; y++)
                    {
                        for (var x = 0; x < 8; x++)
                        {
                            sum += samples[(y * 8) + x] * Math.Cos(((2 * x) + 1) * u * Math.PI / 16) * Math.Cos(((2 * y) + 1) * v * Math.PI / 16);
                        }
                    }

                    var exact = sum / 4 * (u == 0 ? 1 / Math.Sqrt(2) : 1) * (v == 0 ? 1 / Math.Sqrt(2) : 1);
                    var actual = coefficients[IndexOfZigZag((v * 8) + u)];
                    var expected = (int)Math.Round(exact, MidpointRounding.AwayFromZero);
                    if (actual != expected)
                    {
                        Assert.True(Math.Abs(Math.Abs(exact - Math.Truncate(exact)) - 0.5) < 1e-9, string.Create(CultureInfo.InvariantCulture, $"({u}, {v}): {actual} instead of {exact}."));
                        nearTies++;
                    }
                }
            }
        }

        // Integer samples make exact ties frequent (for example a DC sum of 8k + 4): either neighbor is accepted there only
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{nearTies} exact tie(s) rounded to the other neighbor."));
    }

    [Fact]
    public void ReconstructionQualityAndSizeFollowTheSettings()
    {
        // Photo-like content: PSNR of the plain reference reconstruction (replicated chroma) against the source
        var source = JpegTestPatterns.PhotoLike(96, 64, seed: 7);
        using var image = Rgb(source);
        var output = TestContext.Current.TestOutputHelper;
        foreach (var (subsampling, quality, minimumPsnr) in QualityThresholds)
        {
            var jpeg = Encode(image, new JpegEncoder { Quality = quality, ChromaSubsampling = subsampling });
            var error = ReconstructionError.Measure(source, ReferenceJpeg.Parse(jpeg).DecodePixels());
            output?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{subsampling} q{quality}: {jpeg.Length} bytes, {error.Describe()}"));
            Assert.True(error.Psnr >= minimumPsnr && (quality < 50 || error.MaxChannelBias < 0.5), string.Create(CultureInfo.InvariantCulture, $"{subsampling} q{quality}: {error.Describe()} (minimum {minimumPsnr} dB)."));
        }

        var gray = JpegTestPatterns.ToGray(source);
        using var grayImage = EncoderSources.CreateImage(gray, PixelFormat.Gray8);
        foreach (var (quality, minimumPsnr) in GrayQualityThresholds)
        {
            var jpeg = Encode(grayImage, new JpegEncoder { Quality = quality });
            var error = ReconstructionError.Measure(gray, ReferenceJpeg.Parse(jpeg).DecodePixels());
            output?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Gray q{quality}: {jpeg.Length} bytes, {error.Describe()}"));
            Assert.True(error.Psnr >= minimumPsnr, string.Create(CultureInfo.InvariantCulture, $"Gray q{quality}: {error.Describe()} (minimum {minimumPsnr} dB)."));
        }
    }

    /// <summary>
    /// Documented per-setting minimum PSNR (dB) of the 96x64 photo-like source through the reference reconstruction (direct
    /// IDCT, chroma replicated), about 1 dB below the values measured when the encoder was written. 4:2:0 and
    /// 4:2:2 are bounded by the chroma resolution of the saturated edges, not by the quantization.
    /// </summary>
    internal static IReadOnlyList<(JpegChromaSubsampling Subsampling, int Quality, double MinimumPsnr)> QualityThresholds { get; } =
    [
        (JpegChromaSubsampling.Ratio444, 1, 20),
        (JpegChromaSubsampling.Ratio444, 10, 25),
        (JpegChromaSubsampling.Ratio444, 25, 28),
        (JpegChromaSubsampling.Ratio444, 50, 30),
        (JpegChromaSubsampling.Ratio444, 75, 32),
        (JpegChromaSubsampling.Ratio444, 90, 36),
        (JpegChromaSubsampling.Ratio444, 95, 39),
        (JpegChromaSubsampling.Ratio444, 100, 49),
        (JpegChromaSubsampling.Ratio422, 10, 25),
        (JpegChromaSubsampling.Ratio422, 50, 29.5),
        (JpegChromaSubsampling.Ratio422, 75, 31),
        (JpegChromaSubsampling.Ratio422, 90, 34.5),
        (JpegChromaSubsampling.Ratio422, 100, 38.5),
        (JpegChromaSubsampling.Ratio420, 1, 20),
        (JpegChromaSubsampling.Ratio420, 10, 24),
        (JpegChromaSubsampling.Ratio420, 50, 28.5),
        (JpegChromaSubsampling.Ratio420, 75, 30),
        (JpegChromaSubsampling.Ratio420, 90, 32.5),
        (JpegChromaSubsampling.Ratio420, 100, 34.5),
        (JpegChromaSubsampling.Auto, 90, 32.5),
    ];

    internal static IReadOnlyList<(int Quality, double MinimumPsnr)> GrayQualityThresholds { get; } = [(1, 23), (10, 28), (50, 32), (75, 34.5), (90, 40), (100, 57)];

    [Theory]
    [InlineData(JpegChromaSubsampling.Ratio444)]
    [InlineData(JpegChromaSubsampling.Ratio422)]
    [InlineData(JpegChromaSubsampling.Ratio420)]
    public void SolidColorsKeepTheirChannelsAndRange(JpegChromaSubsampling subsampling)
    {
        // MCU-aligned solid patches: a channel swap, a limited-range conversion or a chroma offset moves the patch means
        var source = JpegTestPatterns.ColorPatches();
        using var image = Rgb(source);
        var decoded = ReferenceJpeg.Parse(Encode(image, new JpegEncoder { Quality = 95, ChromaSubsampling = subsampling })).DecodePixels();
        for (var patch = 0; patch < JpegTestPatterns.PatchColors.Count; patch++)
        {
            var expected = JpegTestPatterns.PatchColors[patch];
            var x0 = patch % 4 * 16;
            var y0 = patch / 4 * 16;
            for (var c = 0; c < 3; c++)
            {
                double sum = 0;
                for (var y = 0; y < 16; y++)
                {
                    for (var x = 0; x < 16; x++)
                    {
                        sum += decoded.GetSample(x0 + x, y0 + y, c);
                    }
                }

                var mean = sum / 256;
                var target = c switch { 0 => expected.R, 1 => expected.G, _ => expected.B };
                Assert.True(Math.Abs(mean - target) <= 2.5, string.Create(CultureInfo.InvariantCulture, $"Patch {patch} {expected} channel {c}: mean {mean:F2} ({subsampling})."));
            }
        }
    }

    [Fact]
    public void ChromaSubsamplingAveragesExactlyTheDocumentedDirections()
    {
        // Saturated red/blue one-pixel stripes: rows of one color survive 4:2:2 (horizontal halving only) but not 4:2:0;
        // columns of one color are averaged by both; 4:4:4 keeps both
        (byte, byte, byte) red = (230, 20, 30);
        (byte, byte, byte) blue = (20, 40, 230);
        var horizontalStripes = JpegTestPatterns.TwoColors(32, 32, red, blue, (_, y) => y % 2 == 1);
        var verticalStripes = JpegTestPatterns.TwoColors(32, 32, red, blue, (x, _) => x % 2 == 1);
        foreach (var (pattern, subsampling, preserved) in new[]
        {
            (horizontalStripes, JpegChromaSubsampling.Ratio444, true),
            (horizontalStripes, JpegChromaSubsampling.Ratio422, true),
            (horizontalStripes, JpegChromaSubsampling.Ratio420, false),
            (verticalStripes, JpegChromaSubsampling.Ratio444, true),
            (verticalStripes, JpegChromaSubsampling.Ratio422, false),
            (verticalStripes, JpegChromaSubsampling.Ratio420, false),
        })
        {
            using var image = Rgb(pattern);
            var encoder = new JpegEncoder { Quality = 100, ChromaSubsampling = subsampling };
            var jpeg = Encode(image, encoder);
            AssertMatchesModel(jpeg, pattern, encoder, $"stripes {subsampling}");
            var error = ReconstructionError.Measure(pattern, ReferenceJpeg.Parse(jpeg).DecodePixels());
            var context = string.Create(CultureInfo.InvariantCulture, $"{subsampling}, preserved {preserved}: {error.Describe()}");
            Assert.True(preserved ? error.Psnr > 38 : error.Psnr < 20, context);
        }
    }

    [Fact]
    public void ChannelOrderAndPrecisionDoNotChangeTheOutput()
    {
        // The same opaque pixels as RGB, RGBA and BGRA give identical files; 16-bit sources whose samples are exact
        // expansions of 8-bit values (v * 257) also do once reduced
        var source = JpegTestPatterns.PhotoLike(29, 19, seed: 3);
        var encoder = new JpegEncoder { Quality = 83, ChromaSubsampling = JpegChromaSubsampling.Ratio422, AllowBitDepthReduction = true };
        using var rgb = Rgb(source);
        var expected = Encode(rgb, encoder);
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgba64 })
        {
            Assert.Equal(expected, Encode(JpegTestPatterns.ToFormat(source, format), format, encoder));
        }

        using var gray8 = EncoderSources.CreateImage(JpegTestPatterns.ToFormat(source, PixelFormat.Gray8), PixelFormat.Gray8);
        using var gray16 = EncoderSources.CreateImage(JpegTestPatterns.ToFormat(source, PixelFormat.Gray16), PixelFormat.Gray16);
        Assert.Equal(Encode(gray8, encoder), Encode(gray16, encoder));
    }

    [Theory]
    [InlineData(PixelFormat.Rgba64)]
    [InlineData(PixelFormat.Gray16)]
    public void SixteenBitInputRequiresExplicitBitDepthReduction(PixelFormat format)
    {
        var source = JpegTestPatterns.ToFormat(JpegTestPatterns.PhotoLike(12, 10, seed: 4), format, LowBits);
        using var image = EncoderSources.CreateImage(source, format);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new JpegEncoder()));
        Assert.Equal("Bit depth reduction", exception.Feature);
        Assert.Equal(ImageFormat.Jpeg, exception.Format);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.CreateWriter<Rgba64>(new ThrowingStream(), new ImageWriterOptions(12, 10) { Encoder = new JpegEncoder() }));

        // Allowed: identical to encoding the explicit nearest reduction (v * 255 + 32767) / 65535
        var encoder = new JpegEncoder { AllowBitDepthReduction = true, Quality = 92 };
        var reduced = JpegTestPatterns.ToEncodedSamples(source, background: null);
        using var explicitReduction = EncoderSources.CreateImage(reduced, format == PixelFormat.Gray16 ? PixelFormat.Gray8 : PixelFormat.Rgb24);
        Assert.Equal(Encode(explicitReduction, encoder), Encode(image, encoder));
        using var cloned = format == PixelFormat.Gray16 ? (Image)image.CloneAs<Gray8>() : image.CloneAs<Rgb24>();
        Assert.Equal(Encode(cloned, encoder), Encode(image, encoder));
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Bgra32)]
    [InlineData(PixelFormat.Rgba64)]
    public void NonOpaquePixelsRequireAnExplicitBackground(PixelFormat format)
    {
        var source = JpegTestPatterns.ToFormat(JpegTestPatterns.PhotoLike(20, 12, seed: 5), format);
        var maxAlpha = format == PixelFormat.Rgba64 ? 65535 : 255;

        // Alpha ramp with fully transparent pixels hiding different colors
        for (var x = 0; x < 20; x++)
        {
            source = source.WithSample(x, 3, 3, maxAlpha * x / 19).WithSample(x, 7, 3, 0).WithSample(x, 7, 0, x * 13);
        }

        using var image = EncoderSources.CreateImage(source, format);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new JpegEncoder { AllowBitDepthReduction = true }));
        Assert.Equal("Alpha removal", exception.Feature);

        // A writer rejects the frame before writing anything of it and stays usable
        using (var stream = new TestOutputStream())
        {
            using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(20, 12) { Encoder = new JpegEncoder() });
            using var rgba = image.CloneAs<Rgba32>();
            Assert.Throws<UnsupportedImageFeatureException>(() => writer.WriteFrame(rgba.Frames[0]));
            Assert.Equal(0, stream.BytesWritten);
            using var opaque = new Image<Rgba32>(20, 12, new Rgba32(10, 200, 30));
            writer.WriteFrame(opaque.Frames[0]); // fully opaque RGBA needs no background
            writer.Complete();
            ReferenceJpeg.Parse(stream.ToArray());
        }

        // With a background: flattened at the source precision in the encoded color space, then reduced to 8 bits
        var background = new Rgba64(0xFFFF, 0x8000, 0x0101);
        var encoder = new JpegEncoder { BackgroundColor = background, AllowBitDepthReduction = true, ChromaSubsampling = JpegChromaSubsampling.Ratio444, Quality = 97 };
        var jpeg = Encode(image, encoder);
        var flattened = JpegTestPatterns.ToEncodedSamples(source, (background.R, background.G, background.B));
        AssertMatchesModel(jpeg, flattened, encoder, $"{format} flattened");
        using var flattenedImage = EncoderSources.CreateImage(flattened, PixelFormat.Rgb24);
        Assert.Equal(Encode(flattenedImage, encoder), jpeg);
    }

    [Fact]
    public void AnimatedImagesAndPostersAreRejectedAndFramesExportExplicitly()
    {
        using var animated = new Image<Rgb24>(8, 8, new Rgb24(1, 2, 3));
        animated.AppendFrame();
        Assert.Equal("Animation", Assert.Throws<UnsupportedImageFeatureException>(() => animated.Save(new ThrowingStream(), new JpegEncoder())).Feature);
        using var still = new Image<Rgb24>(8, 8, new Rgb24(1, 2, 3));
        still.SetPosterFrame(still.Frames[0]);
        Assert.Throws<UnsupportedImageFeatureException>(() => still.Save(new ThrowingStream(), new JpegEncoder()));
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgb24>(new ThrowingStream(), new ImageWriterOptions(8, 8) { Encoder = new JpegEncoder(), ExpectedFrameCount = 2 }));
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgb24>(new ThrowingStream(), new ImageWriterOptions(8, 8) { Encoder = new JpegEncoder(), Animation = new AnimationMetadata() }));

        using var frame = animated.CloneFrame(1);
        var reference = ReferenceJpeg.Parse(Encode(frame, new JpegEncoder()));
        Assert.Equal(8, reference.Width);
    }

    [Fact]
    public void SupportedMetadataIsWrittenBeforeTheTables()
    {
        using var image = Rgb(JpegTestPatterns.PhotoLike(24, 16, seed: 6));
        var icc = CreateIccProfile("RGB ", 150_000); // three APP2 chunks
        var exif = CreateExif(orientation: 1, width: 1000, height: 1000);
        var xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8.ToArray();
        image.Metadata.IccProfile = new IccProfile(new MetadataBlob(icc));
        image.Metadata.ExifProfile = new ExifProfile(new MetadataBlob(exif));
        image.Metadata.Orientation = ExifOrientation.RightTop;
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(xmp));
        image.Metadata.Resolution = new ImageResolution(300, 150);
        image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "first comment"));
        image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "café ÿ"));
        var jpeg = Encode(image, new JpegEncoder());

        var reference = ReferenceJpeg.Parse(jpeg);
        Assert.Equal(["APP0", "APP1", "APP1", "APP2", "APP2", "APP2", "COM", "COM", "DQT", "SOF0", "DHT", "SOS"], reference.SegmentNames);
        Assert.True(reference.HasJfifFirst);
        Assert.False(reference.HasAdobeSegment);
        var jfif = reference.Segments[0].Payload.ToArray();
        Assert.Equal([1, 2, 1], jfif[5..8]); // JFIF 1.02, dots per inch
        Assert.Equal(300, BinaryPrimitives.ReadUInt16BigEndian(jfif.AsSpan(8)));
        Assert.Equal(150, BinaryPrimitives.ReadUInt16BigEndian(jfif.AsSpan(10)));
        var chunks = reference.GetSegments(0xE2).Select(segment => segment.Payload.ToArray()).ToList();
        Assert.Equal([1, 2, 3], chunks.Select(chunk => (int)chunk[12]));
        Assert.All(chunks, chunk => Assert.Equal(3, chunk[13]));
        Assert.All(chunks.Take(2), chunk => Assert.HasCount(65_535 - 2, chunk));

        var fields = EncodedFieldInspector.Inspect("jpeg", jpeg);
        Assert.Equal(icc, fields.Icc!.Value.ToArray());
        Assert.Equal(xmp, fields.Xmp!.Value.ToArray());
        Assert.Equal(["Comment: first comment", "Comment: café ÿ"], fields.Text.Select(entry => entry.ToString()));
        Assert.Equal(ResolutionExpectation.Inch, fields.Resolution!.Unit);

        // EXIF: the typed orientation is authoritative and the pixel dimensions are reconciled with the canvas
        var writtenExif = fields.Exif!.Value.ToArray();
        Assert.Equal(6, ReadExifShort(writtenExif, 0x0112));
        Assert.Equal(24, ReadExifShort(writtenExif, 0x0100));
        Assert.Equal(16, ReadExifShort(writtenExif, 0x0101));

        // Supplementary: the library decoder reads everything back
        using var decoded = Image.Load(jpeg);
        Assert.Equal(ExifOrientation.RightTop, decoded.Metadata.Orientation);
        Assert.Equal(icc, decoded.Metadata.IccProfile!.Data.ToArray());
        Assert.Equal(xmp, decoded.Metadata.XmpProfile!.Data.ToArray());
        Assert.Equal(new ImageResolution(300, 150), decoded.Metadata.Resolution);
        Assert.Equal(["first comment", "café ÿ"], decoded.Metadata.TextEntries.Select(entry => entry.Value));
    }

    [Fact]
    public void DensityUsesCentimetersWhenExactAndAspectRatioWithoutResolution()
    {
        using var image = Rgb(JpegTestPatterns.PhotoLike(8, 8, seed: 6));
        var plain = ReferenceJpeg.Parse(Encode(image, new JpegEncoder()));
        Assert.Equal(["APP0", "DQT", "SOF0", "DHT", "SOS"], plain.SegmentNames);
        Assert.Equal([0, 0, 1, 0, 1, 0, 0], plain.Segments[0].Payload.ToArray()[7..]); // units 0, 1:1, no thumbnail

        image.Metadata.Resolution = new ImageResolution(118 * 2.54, 59 * 2.54); // decoded from dots per centimeter
        var jfif = ReferenceJpeg.Parse(Encode(image, new JpegEncoder())).Segments[0].Payload.ToArray();
        Assert.Equal(2, jfif[7]);
        Assert.Equal(118, BinaryPrimitives.ReadUInt16BigEndian(jfif.AsSpan(8)));
        Assert.Equal(59, BinaryPrimitives.ReadUInt16BigEndian(jfif.AsSpan(10)));

        // A non-default orientation without EXIF synthesizes a minimal EXIF block
        image.Metadata.Resolution = null;
        image.Metadata.Orientation = ExifOrientation.BottomRight;
        var fields = EncodedFieldInspector.Inspect("jpeg", Encode(image, new JpegEncoder()));
        Assert.Equal(3, ReadExifShort(fields.Exif!.Value.ToArray(), 0x0112));
    }

    public static TheoryData<string> UnsupportedMetadata => ["title", "comment-language", "comment-non-latin1", "comment-too-long", "xmp-too-large", "exif-too-large", "gray-icc-on-rgb", "resolution-out-of-range"];

    [Theory]
    [MemberData(nameof(UnsupportedMetadata))]
    public void UnsupportedMetadataFollowsThePolicy(string item)
    {
        using var image = Rgb(JpegTestPatterns.PhotoLike(8, 8, seed: 6));
        switch (item)
        {
            case "title":
                image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "x"));
                break;
            case "comment-language":
                image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "x", "en", null));
                break;
            case "comment-non-latin1":
                image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "中文"));
                break;
            case "comment-too-long":
                image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, new string('a', 65_534)));
                break;
            case "xmp-too-large":
                image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(Enumerable.Repeat((byte)'a', 65_505).ToArray()));
                break;
            case "exif-too-large":
                image.Metadata.ExifProfile = new ExifProfile(new MetadataBlob(CreateExif(1, 8, 8, padding: 65_528)));
                break;
            case "gray-icc-on-rgb":
                image.Metadata.IccProfile = new IccProfile(new MetadataBlob(CreateIccProfile("GRAY", 132)));
                break;
            default:
                image.Metadata.Resolution = new ImageResolution(70_000, 72);
                break;
        }

        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new JpegEncoder()));
        Assert.StartsWith("Metadata: ", exception.Feature, StringComparison.Ordinal);
        var discarded = ReferenceJpeg.Parse(Encode(image, new JpegEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported }));
        Assert.Equal(["APP0", "DQT", "SOF0", "DHT", "SOS"], discarded.SegmentNames);
        var stripped = ReferenceJpeg.Parse(Encode(image, new JpegEncoder { MetadataHandling = MetadataHandling.Strip }));
        Assert.Equal(["APP0", "DQT", "SOF0", "DHT", "SOS"], stripped.SegmentNames);
    }

    [Fact]
    public void StripWritesOnlyTheJfifSegment()
    {
        using var image = Rgb(JpegTestPatterns.PhotoLike(8, 8, seed: 6));
        image.Metadata.IccProfile = new IccProfile(new MetadataBlob(CreateIccProfile("RGB ", 132)));
        image.Metadata.Orientation = ExifOrientation.LeftBottom;
        image.Metadata.Resolution = new ImageResolution(96, 96);
        image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "x"));
        var reference = ReferenceJpeg.Parse(Encode(image, new JpegEncoder { MetadataHandling = MetadataHandling.Strip }));
        Assert.Equal(["APP0", "DQT", "SOF0", "DHT", "SOS"], reference.SegmentNames);
        Assert.Equal(0, reference.Segments[0].Payload.Span[7]); // no density unit

        // Gray profiles label gray output
        using var gray = new Image<Gray8>(8, 8, new Gray8(100));
        var grayIcc = CreateIccProfile("GRAY", 132);
        gray.Metadata.IccProfile = new IccProfile(new MetadataBlob(grayIcc));
        Assert.Equal(grayIcc, EncodedFieldInspector.Inspect("jpeg", Encode(gray, new JpegEncoder())).Icc!.Value.ToArray());
    }

    [Fact]
    public void MalformedPayloadsThrowInvalidContentBeforeAnyOutput()
    {
        using var image = Rgb(JpegTestPatterns.PhotoLike(8, 8, seed: 6));
        image.Metadata.ExifProfile = new ExifProfile(new MetadataBlob([1, 2, 3, 4]));
        Assert.Equal(ImageFormat.Jpeg, Assert.Throws<InvalidImageContentException>(() => image.Save(new ThrowingStream(), new JpegEncoder())).Format);
        Assert.Throws<InvalidImageContentException>(() => image.Save(new ThrowingStream(), new JpegEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported }));
        ReferenceJpeg.Parse(Encode(image, new JpegEncoder { MetadataHandling = MetadataHandling.Strip }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeImagesAreStreamedByMcuRowsWithoutBufferingTheImage(bool asynchronous)
    {
        const int Width = 1200;
        const int Height = 800;
        var source = JpegTestPatterns.PhotoLike(Width, Height, seed: 9);
        using var image = Rgb(source);
        using var stream = new TestOutputStream { ForbidSynchronousWrites = asynchronous, ForbidAsynchronousWrites = !asynchronous };
        var encoder = new JpegEncoder { Quality = 100, ChromaSubsampling = JpegChromaSubsampling.Ratio444 };
        long peak;
        using (var writer = Image.CreateWriter<Rgb24>(stream, new ImageWriterOptions(Width, Height) { Encoder = encoder }))
        {
            Assert.Equal(0, stream.BytesWritten);
            if (asynchronous)
            {
                await writer.WriteFrameAsync(image.Frames[0], XunitCancellationToken);
                await writer.CompleteAsync(XunitCancellationToken);
            }
            else
            {
                writer.WriteFrame(image.Frames[0]);
                writer.Complete();
            }

            peak = writer.Core.Scope.GetDiagnostics().PeakLiveBytes;
            Assert.Equal(0, writer.Core.Scope.LiveBytes);
        }

        // One MCU row of float samples per component plus the 64 KiB output buffer, never the image
        var imageBytes = (long)Width * Height * 3;
        Assert.True(peak < imageBytes / 4, $"Peak writer memory {peak} bytes for a {imageBytes}-byte image.");
        Assert.InRange(asynchronous ? stream.AsynchronousWriteCount : stream.SynchronousWriteCount, 4, int.MaxValue);
        var jpeg = stream.ToArray();
        var reference = ReferenceJpeg.Parse(jpeg);
        Assert.Equal((Width, Height), (reference.Width, reference.Height));
        var error = ReconstructionError.Measure(source, reference.DecodePixels());
        Assert.True(error.Psnr > 45, error.Describe());
    }

    [Fact]
    public void WritersAcceptOneFrameWithOrWithoutAnExpectedCount()
    {
        using var image = Rgb(JpegTestPatterns.PhotoLike(9, 7, seed: 2));
        foreach (int? count in new int?[] { null, 1 })
        {
            using var stream = new TestOutputStream();
            using var writer = Image.CreateWriter<Rgb24>(stream, new ImageWriterOptions(9, 7) { Encoder = new JpegEncoder(), ExpectedFrameCount = count });
            Assert.Equal(ImageFormat.Jpeg, writer.Format);
            Assert.Throws<InvalidOperationException>(() => writer.Complete()); // no frame: usable
            Assert.Throws<InvalidOperationException>(() => writer.WritePosterFrame(image.Frames[0]));
            writer.WriteFrame(image.Frames[0]);
            Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(image.Frames[0])); // single frame: still usable
            var beforeComplete = stream.ToArray();
            Assert.NotEqual(0xD9, beforeComplete[^1]);
            writer.Complete();
            writer.Complete();
            Assert.Equal(0, writer.Core.Scope.LiveBytes);
            var jpeg = stream.ToArray();
            Assert.Equal([0xFF, 0xD9], jpeg[^2..]);
            Assert.HasCount(beforeComplete.Length + 2, jpeg);
            ReferenceJpeg.Parse(jpeg);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(25)]
    [InlineData(700)]
    [InlineData(5000)]
    public async Task FailingStreamsFaultTheWriterAndReleaseItsState(long failAt)
    {
        using var image = Rgb(JpegTestPatterns.PhotoLike(120, 90, seed: 2));
        image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "value"));
        foreach (var asynchronous in new[] { false, true })
        {
            using var stream = new TestOutputStream { FailAtPosition = failAt };
            var writer = Image.CreateWriter<Rgb24>(stream, new ImageWriterOptions(120, 90) { Encoder = new JpegEncoder { Quality = 100 }, Metadata = image.Metadata });
            try
            {
                if (asynchronous)
                {
                    await Assert.ThrowsAsync<InjectedIOException>(async () =>
                    {
                        await writer.WriteFrameAsync(image.Frames[0], XunitCancellationToken);
                        await writer.CompleteAsync(XunitCancellationToken);
                    });
                }
                else
                {
                    Assert.Throws<InjectedIOException>(() =>
                    {
                        writer.WriteFrame(image.Frames[0]);
                        writer.Complete();
                    });
                }

                Assert.Equal(0, writer.Core.Scope.LiveBytes);
                Assert.Throws<InvalidOperationException>(() => writer.Complete());
                Assert.True(stream.BytesWritten <= failAt);
            }
            finally
            {
                writer.Dispose();
            }

            Assert.False(stream.IsDisposed);
        }
    }

    [Fact]
    public async Task CancellationDuringEncodingFaultsTheWriter()
    {
        using var image = Rgb(JpegTestPatterns.PhotoLike(800, 600, seed: 2));
        using var source = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        using var stream = new TestOutputStream { CancellationSource = source, CancelAtPosition = 70_000 };
        await using var writer = Image.CreateWriter<Rgb24>(stream, new ImageWriterOptions(800, 600) { Encoder = new JpegEncoder { Quality = 100, ChromaSubsampling = JpegChromaSubsampling.Ratio444 } });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteFrameAsync(image.Frames[0], source.Token).AsTask());
        Assert.InRange(stream.BytesWritten, 70_000, 800 * 600 * 3);
        Assert.Equal(0, writer.Core.Scope.LiveBytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync(XunitCancellationToken).AsTask());
    }

    [Fact]
    public async Task FailedPathSavesPreserveTheDestinationAndPublishNothing()
    {
        var directory = ImageWriterTests.CreateDirectory();
        try
        {
            var path = directory / "out.jpg";
            await File.WriteAllBytesAsync(path, "previous"u8.ToArray(), XunitCancellationToken);
            using var image = Rgb(JpegTestPatterns.PhotoLike(16, 16, seed: 2));

            // Late failure: the headers were written to the temporary file, then the frame cannot be leased
            image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.Save(path)));

            // Early failures: transparency without background, 16-bit without reduction, unsupported metadata, cancellation
            using (var transparent = new Image<Rgba32>(4, 4))
            {
                Assert.Throws<UnsupportedImageFeatureException>(() => transparent.Save(path));
            }

            using (var deep = new Image<Gray16>(4, 4))
            {
                Assert.Throws<UnsupportedImageFeatureException>(() => deep.Save(path));
            }

            image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "x"));
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(path));
            image.Metadata.TextEntries.Clear();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsync(path, cancellationToken: new CancellationToken(canceled: true)));
            Assert.Equal("previous"u8.ToArray(), await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.Equal(["out.jpg"], Directory.GetFileSystemEntries(directory).Select(Path.GetFileName));

            // .jpg and .jpeg infer the JPEG encoder
            await image.SaveAsync(path, cancellationToken: XunitCancellationToken);
            image.Save(directory / "out.jpeg");
            Assert.Equal(16, ReferenceJpeg.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken)).Width);
            Assert.Equal(await File.ReadAllBytesAsync(path, XunitCancellationToken), await File.ReadAllBytesAsync(directory / "out.jpeg", XunitCancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void SegmentedStorageIsEncodedRowByRow()
    {
        var data = new byte[70 * 30 * 4];
        new Random(12).NextBytes(data);
        for (var i = 3; i < data.Length; i += 4)
        {
            data[i] = 255;
        }

        using var segmented = Image.ImportPixelBytesCore<Rgba32>(data, 70, 30, 70 * 4, ImageConfiguration.Default, new PixelStorageLayoutOptions { RowAlignment = 16, TargetSlabBytes = 1024 });
        Assert.True(segmented.Frames[0].Storage.SlabCount > 1);
        using var contiguous = Image.ImportPixelBytes<Rgba32>(data, 70, 30);
        Assert.Equal(Encode(contiguous, new JpegEncoder()), Encode(segmented, new JpegEncoder()));
    }

    [Fact]
    public void LibraryDecoderRoundTripIsSupplementary()
    {
        var source = JpegTestPatterns.PhotoLike(50, 34, seed: 8);
        using var image = Rgb(source);
        foreach (var subsampling in AllSubsamplings)
        {
            var jpeg = Encode(image, new JpegEncoder { ChromaSubsampling = subsampling });
            using var decoded = Image.Load<Rgb24>(jpeg);
            var error = ReconstructionError.Measure(source, ImageSnapshots.CaptureFrame(decoded.Frames[0]));
            Assert.True(error.Psnr > 33, $"{subsampling}: {error.Describe()}");
        }
    }

    [Fact]
    public void HarnessDetectsEncoderDefects()
    {
        // Self-test of the independent checks: the model comparison rejects swapped channels, a one-unit range shift, a
        // different quality and transposed sampling; the reader rejects truncation, trailing data and data after the last MCU
        var source = JpegTestPatterns.PhotoLike(32, 16, seed: 2);
        using var image = Rgb(source);
        var encoder = new JpegEncoder { Quality = 90, ChromaSubsampling = JpegChromaSubsampling.Ratio422 };
        var jpeg = Encode(image, encoder);
        var actual = ReferenceJpeg.Parse(jpeg).DecodeCoefficients();
        var luminance = JpegEncoderModel.ScaleTable(JpegEncoderModel.AnnexKLuminance, 90);
        var chrominance = JpegEncoderModel.ScaleTable(JpegEncoderModel.AnnexKChrominance, 90);
        Assert.True(JpegEncoderModel.Compare(JpegEncoderModel.ComputeCoefficients(source, 2, 1, luminance, chrominance), actual).IsMatch);
        Assert.False(JpegEncoderModel.Compare(JpegEncoderModel.ComputeCoefficients(source.SwapChannels(0, 2), 2, 1, luminance, chrominance), actual).IsMatch);
        var shifted = RawPixelBuffer.Create(32, 16, RawPixelLayout.Rgb8, [.. source.Span.ToArray().Select(value => (byte)Math.Min(255, value + 1))]);
        Assert.False(JpegEncoderModel.Compare(JpegEncoderModel.ComputeCoefficients(shifted, 2, 1, luminance, chrominance), actual).IsMatch);
        Assert.False(JpegEncoderModel.Compare(JpegEncoderModel.ComputeCoefficients(source, 2, 1, JpegEncoderModel.ScaleTable(JpegEncoderModel.AnnexKLuminance, 89), chrominance), actual).IsMatch);
        Assert.False(JpegEncoderModel.Compare(JpegEncoderModel.ComputeCoefficients(source, 1, 2, luminance, chrominance), actual).IsMatch);

        Assert.Throws<InvalidDataException>(() => ReferenceJpeg.Parse(jpeg.AsMemory(0, jpeg.Length - 2)));
        Assert.Throws<InvalidDataException>(() => ReferenceJpeg.Parse((byte[])[.. jpeg, 0]));
        var truncatedScan = (byte[])[.. jpeg.AsSpan(0, jpeg.Length - 12), 0xFF, 0xD9];
        Assert.Throws<InvalidDataException>(() => ReferenceJpeg.Parse(truncatedScan).DecodeCoefficients());
        var extraByte = (byte[])[.. jpeg.AsSpan(0, jpeg.Length - 2), 0x00, 0xFF, 0xD9]; // entropy-coded data after the last MCU
        Assert.Throws<InvalidDataException>(() => ReferenceJpeg.Parse(extraByte).DecodeCoefficients());
    }

    internal static void AssertMatchesModel(byte[] jpeg, RawPixelBuffer samples, JpegEncoder encoder, string context)
    {
        var reference = ReferenceJpeg.Parse(jpeg);
        Assert.Equal(samples.Width, reference.Width);
        Assert.Equal(samples.Height, reference.Height);
        Assert.True(reference.HasJfifFirst, context);
        var gray = samples.Layout == RawPixelLayout.Gray8;
        var (h, v) = gray ? (1, 1) : encoder.ChromaSubsampling switch
        {
            JpegChromaSubsampling.Ratio444 => (1, 1),
            JpegChromaSubsampling.Ratio422 => (2, 1),
            _ => (2, 2),
        };

        Assert.Equal(gray ? 1 : 3, reference.Components.Count);
        Assert.Equal((h, v), (reference.Components[0].H, reference.Components[0].V));
        Assert.All(reference.Components.Skip(1), component => Assert.Equal((1, 1), (component.H, component.V)));
        var luminance = JpegEncoderModel.ScaleTable(JpegEncoderModel.AnnexKLuminance, encoder.Quality);
        var chrominance = JpegEncoderModel.ScaleTable(JpegEncoderModel.AnnexKChrominance, encoder.Quality);
        var expected = JpegEncoderModel.ComputeCoefficients(samples, h, v, luminance, chrominance);
        var comparison = JpegEncoderModel.Compare(expected, reference.DecodeCoefficients());
        Assert.True(comparison.IsMatch, context + ": " + comparison.Describe());
    }

    internal static byte[] Encode(Image image, JpegEncoder encoder)
    {
        using var stream = new TestOutputStream();
        image.Save(stream, encoder);
        Assert.False(stream.IsDisposed);
        return stream.ToArray();
    }

    internal static byte[] Encode(RawPixelBuffer pixels, PixelFormat format, JpegEncoder encoder)
    {
        using var image = EncoderSources.CreateImage(pixels, format);
        return Encode(image, encoder);
    }

    internal static byte[] CreateIccProfile(string colorSpace, int length)
    {
        var data = new byte[length];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
        "mntr"u8.CopyTo(data.AsSpan(12));
        Encoding.ASCII.GetBytes(colorSpace).CopyTo(data.AsSpan(16));
        "XYZ "u8.CopyTo(data.AsSpan(20));
        "acsp"u8.CopyTo(data.AsSpan(36));
        for (var i = 132; i < length; i++)
        {
            data[i] = (byte)(i * 7);
        }

        return data;
    }

    private static int LowBits(int x, int y, int channel) => ((x * 31) + (y * 17) + (channel * 5)) % 401 - 200;

    private static Image<Rgb24> Rgb(RawPixelBuffer pixels) => Image.ImportPixelBytes<Rgb24>(pixels.Span, pixels.Width, pixels.Height);

    private static int IndexOfZigZag(int natural) => JpegIdct.ZigZag.IndexOf((byte)natural);

    /// <summary>Creates a little-endian TIFF/EXIF block with IFD0 entries Orientation, ImageWidth and ImageLength (SHORT), optionally followed by padding.</summary>
    private static byte[] CreateExif(int orientation, int width, int height, int padding = 0)
    {
        var data = new byte[8 + 2 + (3 * 12) + 4 + padding];
        "II"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 3);
        (ushort Tag, int Value)[] entries = [(0x0100, width), (0x0101, height), (0x0112, orientation)];
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = data.AsSpan(10 + (12 * i));
            BinaryPrimitives.WriteUInt16LittleEndian(entry, entries[i].Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], 3); // SHORT
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[8..], (ushort)entries[i].Value);
        }

        return data;
    }

    private static int ReadExifShort(byte[] exif, ushort tag)
    {
        var little = exif[0] == (byte)'I';
        int Read16(int offset) => little ? BinaryPrimitives.ReadUInt16LittleEndian(exif.AsSpan(offset)) : BinaryPrimitives.ReadUInt16BigEndian(exif.AsSpan(offset));
        long Read32(int offset) => little ? BinaryPrimitives.ReadUInt32LittleEndian(exif.AsSpan(offset)) : BinaryPrimitives.ReadUInt32BigEndian(exif.AsSpan(offset));
        var ifd = (int)Read32(4);
        var count = Read16(ifd);
        for (var i = 0; i < count; i++)
        {
            var entry = ifd + 2 + (12 * i);
            if (Read16(entry) == tag)
                return Read16(entry + 2) == 4 ? (int)Read32(entry + 8) : Read16(entry + 8);
        }

        return -1;
    }
}
