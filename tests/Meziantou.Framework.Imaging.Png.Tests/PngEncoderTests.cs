using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Png;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Static PNG encoding through the public save and writer APIs. Output is validated with the independent
/// harness reader (<see cref="ReferencePng"/>: strict chunk structure, bitwise CRCs, its own unfiltering and Adam7) and the
/// independent container walker (<see cref="EncodedFieldInspector"/>) for metadata; decoding with the library decoder is a
/// supplementary round trip only. FFmpeg decodes the same outputs in the interoperability tests.
/// </summary>
public sealed class PngEncoderTests
{
    private static readonly PixelFormat[] AllFormats = [PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Rgba64, PixelFormat.Gray8, PixelFormat.Gray16];

    public static TheoryData<PixelFormat, PngFilter, bool> FilterCases()
    {
        var data = new TheoryData<PixelFormat, PngFilter, bool>();
        foreach (var format in AllFormats)
        {
            foreach (var filter in Enum.GetValues<PngFilter>())
            {
                data.Add(format, filter, false);
                data.Add(format, filter, true);
            }
        }

        return data;
    }

    public static TheoryData<PixelFormat, CompressionLevel, bool> CompressionCases()
    {
        var data = new TheoryData<PixelFormat, CompressionLevel, bool>();
        foreach (var format in AllFormats)
        {
            foreach (var level in Enum.GetValues<CompressionLevel>())
            {
                data.Add(format, level, false);
                data.Add(format, level, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FilterCases))]
    public void EveryStorageTypeAndFilterDecodesIndependently(PixelFormat format, PngFilter filter, bool interlaced)
    {
        using var image = CreateRandom(format, 13, 11, seed: (int)format * 31 + (int)filter);
        var encoder = new PngEncoder { Filter = filter, Interlaced = interlaced };
        var png = Encode(image, encoder);
        var reference = AssertDecodesIndependently(png, image, interlaced);
        var types = reference.GetFilterTypes();
        Assert.Equal(interlaced ? CountAdam7Scanlines(13, 11) : 11, types.Count);
        if (filter != PngFilter.Adaptive)
        {
            Assert.All(types, type => Assert.Equal((int)filter - 1, type));
        }

        AssertRoundTripsThroughLibraryDecoder(png, image);
    }

    [Theory]
    [MemberData(nameof(CompressionCases))]
    public void EveryCompressionLevelDecodesIndependently(PixelFormat format, CompressionLevel level, bool interlaced)
    {
        using var image = CreateRandom(format, 17, 9, seed: (int)format * 7 + (int)level);
        var png = Encode(image, new PngEncoder { CompressionLevel = level, Interlaced = interlaced });
        AssertDecodesIndependently(png, image, interlaced);
        if (level == CompressionLevel.NoCompression)
        {
            // Stored deflate blocks: the zlib datastream is larger than the raw scanlines
            var reference = ReferencePng.Parse(png);
            Assert.True(reference.GetChunks("IDAT").Sum(chunk => chunk.Data.Length) > 9 * ((17 * PixelFormats.GetBytesPerPixel(format)) + 1));
        }
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Rgba64)]
    [InlineData(PixelFormat.Gray8)]
    [InlineData(PixelFormat.Gray16)]
    public void EveryAdam7GeometryIncludingEmptyPassesDecodesIndependently(PixelFormat format)
    {
        for (var width = 1; width <= 10; width++)
        {
            for (var height = 1; height <= 10; height++)
            {
                using var image = CreateRandom(format, width, height, seed: (width * 100) + height);
                var png = Encode(image, new PngEncoder { Interlaced = true, Filter = (PngFilter)((width + height) % 6) });
                var reference = AssertDecodesIndependently(png, image, interlaced: true);
                Assert.Equal(CountAdam7Scanlines(width, height), reference.GetFilterTypes().Count);
            }
        }
    }

    [Fact]
    public void SixteenBitOutputKeepsLowBitDifferences()
    {
        // Samples differ only in their low byte (an 8-bit bottleneck would merge them)
        using var color = new Image<Rgba64>(4, 1);
        color.Frames[0].ProcessPixelRows(pixels =>
        {
            var row = pixels.GetRowSpan(0);
            row[0] = new Rgba64(0x1200, 0x3401, 0x56FE, 0xFFFF);
            row[1] = new Rgba64(0x1201, 0x3400, 0x56FF, 0xFFFE);
            row[2] = new Rgba64(0x0001, 0x0002, 0x0003, 0x0004);
            row[3] = new Rgba64(0xFFFF, 0xFFFE, 0xFFFD, 0x8001);
        });

        var png = Encode(color, new PngEncoder());
        var reference = ReferencePng.Parse(png);
        Assert.Equal((byte)16, reference.BitDepth);
        Assert.Equal((byte)6, reference.ColorType);
        var pixels = reference.DecodePixels();
        Assert.Equal(0x3401, pixels.GetSample(0, 0, 1));
        Assert.Equal(0x56FF, pixels.GetSample(1, 0, 2));
        Assert.Equal(0xFFFE, pixels.GetSample(1, 0, 3));
        Assert.Equal(0x0004, pixels.GetSample(2, 0, 3));
        Assert.Equal(0x8001, pixels.GetSample(3, 0, 3));
        AssertDecodesIndependently(png, color, interlaced: false);

        using var gray = new Image<Gray16>(3, 1);
        gray.Frames[0].ProcessPixelRows(pixels =>
        {
            var row = pixels.GetRowSpan(0);
            row[0] = new Gray16(0x0100);
            row[1] = new Gray16(0x0101);
            row[2] = new Gray16(0x00FF);
        });

        png = Encode(gray, new PngEncoder { Interlaced = true });
        reference = ReferencePng.Parse(png);
        Assert.Equal((byte)16, reference.BitDepth);
        Assert.Equal((byte)0, reference.ColorType);
        Assert.Equal([0x0100, 0x0101, 0x00FF], Enumerable.Range(0, 3).Select(x => reference.DecodePixels().GetSample(x, 0, 0)));

        // Supplementary: the library decoder keeps all 16 bits too
        using var decoded = Image.Load<Gray16>(png);
        Assert.Equal(0x0101, decoded.Frames[0][1, 0].Value);
    }

    [Theory]
    [InlineData(PixelFormat.Gray8, 0, 8)]
    [InlineData(PixelFormat.Gray16, 0, 16)]
    [InlineData(PixelFormat.Rgb24, 2, 8)]
    [InlineData(PixelFormat.Rgba32, 6, 8)]
    [InlineData(PixelFormat.Bgra32, 6, 8)]
    [InlineData(PixelFormat.Rgba64, 6, 16)]
    public void ColorTypeAndBitDepthFollowThePixelFormat(PixelFormat format, byte colorType, byte bitDepth)
    {
        using var image = CreateRandom(format, 2, 2, seed: 1);
        var reference = ReferencePng.Parse(Encode(image, new PngEncoder()));
        Assert.Equal(colorType, reference.ColorType);
        Assert.Equal(bitDepth, reference.BitDepth);
        Assert.Equal((byte)0, reference.InterlaceMethod);
        Assert.Equal(["IHDR", "IDAT", "IEND"], reference.ChunkTypes);
    }

    public static TheoryData<PixelFormat, int, int, bool> IndexedCases()
    {
        // The number of colors and the smallest bit depth whose indexes address them
        (int Colors, int BitDepth)[] palettes = [(1, 1), (2, 1), (3, 2), (4, 2), (5, 4), (16, 4), (17, 8), (255, 8), (256, 8)];
        var data = new TheoryData<PixelFormat, int, int, bool>();
        foreach (var format in AllFormats)
        {
            foreach (var (colors, bitDepth) in palettes)
            {
                data.Add(format, colors, bitDepth, false);
                data.Add(format, colors, bitDepth, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(IndexedCases))]
    public void IndexedLayoutStoresTheColorsExactlyAtTheSmallestBitDepth(PixelFormat format, int colors, int bitDepth, bool interlaced)
    {
        // 19 columns: the last byte of the packed scanlines has unused bits at every bit depth below 8
        using var image = CreateWithColors(format, 19, 14, colors, seed: ((int)format * 1000) + colors);
        var png = Encode(image, new PngEncoder { PixelLayout = PngPixelLayout.Indexed, Interlaced = interlaced });
        var expectation = IndexedPngExpectation.Create(ImageSnapshots.CaptureFrame(image.Frames[0]));
        Assert.Equal(colors, expectation.ColorCount);
        var reference = AssertIndexedDecodesIndependently(png, image, expectation);
        Assert.Equal(bitDepth, reference.BitDepth);
        Assert.Equal(interlaced ? 1 : 0, reference.InterlaceMethod);

        // Differences between indexes say nothing about colors: the adaptive strategy never filters indexed rows
        Assert.All(reference.GetFilterTypes(), type => Assert.Equal(0, type));

        // Supplementary: the library identifies the layout and decodes the same pixels
        var info = Image.Identify(png);
        Assert.Equal(ImageColorModel.Indexed, info.ColorModel);
        Assert.Equal(bitDepth, info.BitsPerComponent);
        using var decoded = Image.Load<Rgba32>(png);
        var result = PixelBufferComparer.Compare(IndexedPngExpectation.ToRgba8(ImageSnapshots.CaptureFrame(image.Frames[0])), ImageSnapshots.CaptureFrame(decoded.Frames[0]), ComparisonPolicy.Exact, "library decoder round trip");
        Assert.True(result.IsMatch, result.Describe());
    }

    [Fact]
    public void IndexedScanlinesPackTheIndexesFromTheHighOrderBits()
    {
        var indexed = new PngEncoder { PixelLayout = PngPixelLayout.Indexed };

        // 1 bit: black is entry 0 and white entry 1; nine pixels need two bytes, the last seven bits are padding
        using (var image = Image.ImportPixelBytes<Gray8>([255, 0, 255, 255, 0, 0, 255, 0, 255], 9, 1))
        {
            var reference = ReferencePng.Parse(Encode(image, indexed));
            Assert.Equal((3, 1), (reference.ColorType, reference.BitDepth));
            Assert.Equal(["IHDR", "PLTE", "IDAT", "IEND"], reference.ChunkTypes);
            Assert.Equal([0, 0, 0, 255, 255, 255], reference.Palette.ToArray());
            Assert.Equal([0, 0b1011_0010, 0b1000_0000], InflateImageData(reference));
        }

        // 2 bits: the entries are sorted by alpha first, so the two that are not opaque come first and tRNS has two values
        using (var image = new Image<Rgba32>(3, 2))
        {
            var red = new Rgba32(255, 0, 0, 255);
            var blue = new Rgba32(0, 0, 255, 128);
            var clear = new Rgba32(0, 0, 0, 0);
            image.Frames[0].ProcessPixelRows(pixels =>
            {
                Rgba32[] first = [red, blue, clear];
                Rgba32[] second = [clear, clear, red];
                first.CopyTo(pixels.GetRowSpan(0));
                second.CopyTo(pixels.GetRowSpan(1));
            });

            var reference = ReferencePng.Parse(Encode(image, indexed));
            Assert.Equal((3, 2), (reference.ColorType, reference.BitDepth));
            Assert.Equal(["IHDR", "PLTE", "tRNS", "IDAT", "IEND"], reference.ChunkTypes);
            Assert.Equal([0, 0, 0, 0, 0, 255, 255, 0, 0], reference.Palette.ToArray());
            Assert.Equal([0, 128], reference.Transparency.ToArray());
            Assert.Equal([0, 0b10_01_00_00, 0, 0b00_00_10_00], InflateImageData(reference));
        }

        // 4 bits: five colors sorted by red, green, blue when their alpha is equal
        using (var image = Image.ImportPixelBytes<Rgb24>([50, 0, 0, 10, 0, 0, 40, 0, 0, 20, 9, 0, 20, 0, 7], 5, 1))
        {
            var reference = ReferencePng.Parse(Encode(image, indexed));
            Assert.Equal((3, 4), (reference.ColorType, reference.BitDepth));
            Assert.Equal([10, 0, 0, 20, 0, 7, 20, 9, 0, 40, 0, 0, 50, 0, 0], reference.Palette.ToArray());
            Assert.Equal([0, 0x40, 0x32, 0x10], InflateImageData(reference));
        }

        // 8 bits: one byte per index
        using (var image = Image.ImportPixelBytes<Gray8>([.. Enumerable.Range(0, 17).Select(i => (byte)(200 - (i * 10)))], 17, 1))
        {
            var reference = ReferencePng.Parse(Encode(image, indexed));
            Assert.Equal((3, 8), (reference.ColorType, reference.BitDepth));
            Assert.Equal([0, .. Enumerable.Range(0, 17).Select(i => (byte)(16 - i))], InflateImageData(reference));
        }

        // The default layout never writes a palette, however few colors the image has
        using (var image = new Image<Rgba32>(4, 4, new Rgba32(1, 2, 3, 255)))
        {
            var reference = ReferencePng.Parse(Encode(image, new PngEncoder()));
            Assert.Equal((6, 8), (reference.ColorType, reference.BitDepth));
            Assert.Equal(["IHDR", "IDAT", "IEND"], reference.ChunkTypes);
        }
    }

    public static TheoryData<PngFilter, int, bool> IndexedFilterCases()
    {
        var data = new TheoryData<PngFilter, int, bool>();
        foreach (var filter in Enum.GetValues<PngFilter>())
        {
            foreach (var colors in (int[])[2, 4, 16, 200])
            {
                data.Add(filter, colors, false);
                data.Add(filter, colors, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(IndexedFilterCases))]
    public void IndexedLayoutFiltersThePackedBytesWithAFixedFilter(PngFilter filter, int colors, bool interlaced)
    {
        using var image = CreateWithColors(PixelFormat.Rgba32, 21, 13, colors, seed: ((int)filter * 100) + colors);
        var png = Encode(image, new PngEncoder { PixelLayout = PngPixelLayout.Indexed, Filter = filter, Interlaced = interlaced });
        var reference = AssertIndexedDecodesIndependently(png, image, IndexedPngExpectation.Create(ImageSnapshots.CaptureFrame(image.Frames[0])));
        var types = reference.GetFilterTypes();
        Assert.Equal(interlaced ? CountAdam7Scanlines(21, 13) : 13, types.Count);
        var expectedType = filter == PngFilter.Adaptive ? 0 : (int)filter - 1;
        Assert.All(types, type => Assert.Equal(expectedType, type));
    }

    [Fact]
    public void IndexedLayoutEncodesEverySmallAdam7Geometry()
    {
        // Empty passes, one-pixel passes and every count of padding bits, at each bit depth
        foreach (var colors in (int[])[2, 3, 9, 20])
        {
            for (var width = 1; width <= 9; width++)
            {
                for (var height = 1; height <= 9; height++)
                {
                    using var image = CreateWithColors(PixelFormat.Rgba32, width, height, Math.Min(colors, width * height), seed: (colors * 100) + (width * 10) + height);
                    var expectation = IndexedPngExpectation.Create(ImageSnapshots.CaptureFrame(image.Frames[0]));
                    foreach (var interlaced in (bool[])[false, true])
                    {
                        var png = Encode(image, new PngEncoder { PixelLayout = PngPixelLayout.Indexed, Interlaced = interlaced });
                        var reference = AssertIndexedDecodesIndependently(png, image, expectation);
                        Assert.Equal(interlaced ? CountAdam7Scanlines(width, height) : height, reference.GetFilterTypes().Count);
                    }
                }
            }
        }
    }

    [Fact]
    public void IndexedLayoutNeverReducesColors()
    {
        // 256 colors fit; one more is rejected before the first byte, whatever distinguishes the colors
        using var fits = CreateWithColors(PixelFormat.Rgba32, 20, 13, colors: 256, seed: 1);
        Assert.Equal((byte)8, ReferencePng.Parse(Encode(fits, new PngEncoder { PixelLayout = PngPixelLayout.Indexed })).BitDepth);

        using var tooMany = CreateWithColors(PixelFormat.Rgba32, 20, 13, colors: 257, seed: 1);
        AssertRejected(tooMany, "Color reduction");

        // Fully transparent pixels keep their color, so 257 of them are 257 entries
        var hidden = new byte[257 * 4];
        for (var i = 0; i < 257; i++)
        {
            hidden[i * 4] = (byte)i;
            hidden[(i * 4) + 1] = (byte)(i >> 8);
        }

        using var transparent = Image.ImportPixelBytes<Rgba32>(hidden, 257, 1);
        AssertRejected(transparent, "Color reduction");

        // The same color at two alpha values is two entries
        using var alpha = Image.ImportPixelBytes<Rgba32>([9, 9, 9, 255, 9, 9, 9, 254], 2, 1);
        var reference = ReferencePng.Parse(Encode(alpha, new PngEncoder { PixelLayout = PngPixelLayout.Indexed }));
        Assert.Equal([9, 9, 9, 9, 9, 9], reference.Palette.ToArray());
        Assert.Equal([254], reference.Transparency.ToArray());
        Assert.Equal([0, 0b1000_0000], InflateImageData(reference));
    }

    [Fact]
    public void IndexedLayoutNeverReducesPrecision()
    {
        // A palette stores 8-bit samples: 16-bit samples are only accepted when they are multiples of 257
        using var color = new Image<Rgba64>(2, 1);
        color.Frames[0].ProcessPixelRows(pixels =>
        {
            pixels.GetRowSpan(0)[0] = new Rgba64(0x1212, 0x3434, 0x5656, 0xFFFF);
            pixels.GetRowSpan(0)[1] = new Rgba64(0x0000, 0xFFFF, 0x8080, 0x7F7F);
        });

        var reference = ReferencePng.Parse(Encode(color, new PngEncoder { PixelLayout = PngPixelLayout.Indexed }));
        Assert.Equal([0x00, 0xFF, 0x80, 0x12, 0x34, 0x56], reference.Palette.ToArray());
        Assert.Equal([0x7F], reference.Transparency.ToArray());

        foreach (var channel in Enumerable.Range(0, 4))
        {
            using var inexact = color.Clone();
            inexact.Frames[0].ProcessPixelRows(pixels =>
            {
                ref var pixel = ref pixels.GetRowSpan(0)[1];
                pixel = channel switch
                {
                    0 => pixel with { R = 0x0001 },
                    1 => pixel with { G = 0xFFFE },
                    2 => pixel with { B = 0x8081 },
                    _ => pixel with { A = 0x7F80 },
                };
            });

            AssertRejected(inexact, "Bit depth reduction");
        }

        using var gray = new Image<Gray16>(3, 1);
        gray.Frames[0].ProcessPixelRows(pixels =>
        {
            pixels.GetRowSpan(0)[0] = new Gray16(0x0101);
            pixels.GetRowSpan(0)[1] = new Gray16(0xFFFF);
            pixels.GetRowSpan(0)[2] = new Gray16(0x0100);
        });

        AssertRejected(gray, "Bit depth reduction");
        gray.Frames[0].ProcessPixelRows(pixels => pixels.GetRowSpan(0)[2] = new Gray16(0x0000));
        Assert.Equal([0, 0, 0, 1, 1, 1, 255, 255, 255], ReferencePng.Parse(Encode(gray, new PngEncoder { PixelLayout = PngPixelLayout.Indexed })).Palette.ToArray());
    }

    [Fact]
    public void ARejectedIndexedFrameKeepsTheWriterUsableAndLeavesNoColorInThePalette()
    {
        using var tooMany = CreateWithColors<Rgba32>(20, 13, colors: 257, seed: 3);
        using var image = CreateWithColors<Rgba32>(20, 13, colors: 3, seed: 4);
        using var stream = new TestOutputStream();
        using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(20, 13) { Encoder = new PngEncoder { PixelLayout = PngPixelLayout.Indexed }, ExpectedFrameCount = 1 });
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => writer.WriteFrame(tooMany.Frames[0]));
        Assert.Equal("Color reduction", exception.Feature);
        Assert.Equal(0, stream.BytesWritten);
        Assert.Equal(0, writer.FramesWritten);

        writer.WriteFrame(image.Frames[0]);
        writer.Complete();

        // The 256 colors collected from the rejected frame are gone: the palette is the one of the written frame
        var reference = AssertIndexedDecodesIndependently(stream.ToArray(), image, IndexedPngExpectation.Create(ImageSnapshots.CaptureFrame(image.Frames[0])));
        Assert.Equal((byte)2, reference.BitDepth);
    }

    [Fact]
    public void IndexedLayoutWritesThePaletteAfterTheMetadata()
    {
        // iCCP must precede PLTE, and PLTE and tRNS the image data
        using var image = CreateWithColors(PixelFormat.Rgba32, 6, 5, colors: 7, seed: 9);
        image.Metadata.IccProfile = new IccProfile(new MetadataBlob(IccTestBlobs.Create("RGB ")));
        image.Metadata.Resolution = new ImageResolution(300, 72);
        image.Metadata.Orientation = ExifOrientation.RightTop;
        image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Indexed"));
        var png = Encode(image, new PngEncoder { PixelLayout = PngPixelLayout.Indexed });
        var expectation = IndexedPngExpectation.Create(ImageSnapshots.CaptureFrame(image.Frames[0]));
        Assert.False(expectation.Transparency.IsEmpty);
        var reference = ReferencePng.Parse(png);
        Assert.Equal(["IHDR", "iCCP", "pHYs", "eXIf", "tEXt", "PLTE", "tRNS", "IDAT", "IEND"], reference.ChunkTypes);
        Assert.Equal(image.Metadata.IccProfile.Data.ToArray(), EncodedFieldInspector.Inspect("png", png).Icc!.Value.ToArray());
    }

    [Fact]
    public void ColorDescriptionsAndLightLevelsAreWrittenWithoutProfile()
    {
        // cICP takes precedence over iCCP: it is written when no profile is, with matrix 0 and full range (the samples are
        // RGB and full range, whatever the file they came from), then mDCV (which needs it) and cLLI (0.0001 cd/m2)
        using var image = CreateWithColors(PixelFormat.Rgba64, 3, 2, colors: 4, seed: 5);
        image.Metadata.Cicp = new CicpColorDescription(9, 16, 9, fullRange: false);
        image.Metadata.MasteringDisplay = new MasteringDisplayColorVolume(35400, 14600, 8500, 39850, 6550, 2300, 15635, 16450, 10_000_000, 50);
        image.Metadata.ContentLightLevel = new ContentLightLevel(1000, 400);
        var png = Encode(image, new PngEncoder());
        var reference = ReferencePng.Parse(png);
        Assert.Equal(["IHDR", "cICP", "mDCV", "cLLI", "IDAT", "IEND"], reference.ChunkTypes);
        Assert.Equal([9, 16, 0, 1], reference.GetChunks("cICP").Single().Data.ToArray());
        Assert.Equal([0x8A, 0x48, 0x39, 0x08, 0x21, 0x34, 0x9B, 0xAA, 0x19, 0x96, 0x08, 0xFC, 0x3D, 0x13, 0x40, 0x42, 0x00, 0x98, 0x96, 0x80, 0x00, 0x00, 0x00, 0x32], reference.GetChunks("mDCV").Single().Data.ToArray());
        Assert.Equal([0x00, 0x98, 0x96, 0x80, 0x00, 0x3D, 0x09, 0x00], reference.GetChunks("cLLI").Single().Data.ToArray());
        using (var reloaded = Image.Load(png))
        {
            Assert.Equal(new CicpColorDescription(9, 16, 0, fullRange: true), reloaded.Metadata.Cicp);
            Assert.Equal(image.Metadata.MasteringDisplay, reloaded.Metadata.MasteringDisplay);
            Assert.Equal(image.Metadata.ContentLightLevel, reloaded.Metadata.ContentLightLevel);
        }

        // With a profile, the profile describes the colors: no cICP chunk, so no mDCV chunk
        image.Metadata.IccProfile = new IccProfile(new MetadataBlob(IccTestBlobs.Create("RGB ")));
        Assert.Equal("Metadata: mastering display color volume", Assert.Throws<UnsupportedImageFeatureException>(() => Encode(image, new PngEncoder())).Feature);
        var discarded = Encode(image, new PngEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        Assert.Equal(["IHDR", "cLLI", "iCCP", "IDAT", "IEND"], ReferencePng.Parse(discarded).ChunkTypes);
    }

    [Fact]
    public void IndexedLayoutLabelsItsRgbPaletteWhateverThePixelFormat()
    {
        // A palette holds RGB colors: a grayscale profile cannot describe an indexed file, an RGB profile can
        var gray = new IccProfile(new MetadataBlob(IccTestBlobs.Create("GRAY")));
        var rgb = new IccProfile(new MetadataBlob(IccTestBlobs.Create("RGB ")));
        var indexed = new PngEncoder { PixelLayout = PngPixelLayout.Indexed };
        using var image = CreateWithColors(PixelFormat.Gray8, 5, 4, colors: 6, seed: 2);
        image.Metadata.IccProfile = rgb;
        Assert.Equal(rgb.Data.ToArray(), EncodedFieldInspector.Inspect("png", Encode(image, indexed)).Icc!.Value.ToArray());

        image.Metadata.IccProfile = gray;
        using var stream = new TestOutputStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, indexed));
        Assert.StartsWith("Metadata: ICC color profile", exception.Feature);
        Assert.Equal(0, stream.BytesWritten);

        var discarded = Encode(image, new PngEncoder { PixelLayout = PngPixelLayout.Indexed, MetadataHandling = MetadataHandling.DiscardUnsupported });
        Assert.Null(EncodedFieldInspector.Inspect("png", discarded).Icc);
        Assert.Equal(["IHDR", "PLTE", "IDAT", "IEND"], ReferencePng.Parse(discarded).ChunkTypes);
    }

    [Fact]
    public void IndexedLayoutStreamsLargeImagesWithBoundedMemory()
    {
        // The palette pass and the image data read the frame row by row: no index plane, whatever the image size
        const int SourceWidth = 1500;
        const int SourceHeight = 1100;
        using var image = CreateWithColors<Rgba32>(SourceWidth, SourceHeight, colors: 200, seed: 6);
        using var stream = new TestOutputStream();
        var options = new ImageWriterOptions(SourceWidth, SourceHeight) { Encoder = new PngEncoder { PixelLayout = PngPixelLayout.Indexed, CompressionLevel = CompressionLevel.NoCompression, Interlaced = true }, ExpectedFrameCount = 1 };
        long peak;
        using (var writer = Image.CreateWriter<Rgba32>(stream, options))
        {
            writer.WriteFrame(image.Frames[0]);
            writer.Complete();
            peak = writer.Core.Scope.GetDiagnostics().PeakLiveBytes;
            Assert.Equal(0, writer.Core.Scope.LiveBytes);
        }

        Assert.True(peak < (long)SourceWidth * SourceHeight / 4, $"Peak writer memory {peak} bytes for {SourceWidth * SourceHeight} pixels.");
        var png = stream.ToArray();
        var chunks = ReferencePng.Parse(png).GetChunks("IDAT").ToList();
        Assert.HasCountGreaterThan(40, chunks);
        AssertIndexedDecodesIndependently(png, image, IndexedPngExpectation.Create(ImageSnapshots.CaptureFrame(image.Frames[0])));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ExactPaletteCollectorKeepsTheSetOfKeysAndSortsThemOnce()
    {
        // The working memory is one small rental charged to the scope, whatever is collected
        var scope = new AllocationScope(new ImageResourceLimits());
        using var palette = new ExactPaletteCollector(scope);
        Assert.InRange(scope.LiveBytes, 3072, 4096);
        Assert.Equal(1, scope.GetDiagnostics().TotalAllocations);

        // Keys that collide in every hash that looks at a few bits only, added several times in a scattered order
        var random = new Random(4);
        var keys = Enumerable.Range(0, ExactPaletteCollector.MaxColors).Select(i => (uint)(i << 24) | (uint)random.Next(4)).Distinct().ToArray();
        Assert.HasCount(ExactPaletteCollector.MaxColors, keys);
        var shuffled = keys.OrderBy(_ => random.Next()).ToArray();
        foreach (var key in shuffled.Concat(shuffled))
        {
            Assert.True(palette.TryAdd(key));
        }

        Assert.Equal(shuffled, palette.Colors.ToArray());
        Assert.True(palette.TryAdd(shuffled[17])); // a known key is accepted when the set is full
        Assert.False(palette.TryAdd(0x1234_5678));
        Assert.False(palette.TryGetIndex(0x1234_5678, out _));
        Assert.Equal(ExactPaletteCollector.MaxColors, palette.Count);

        // A rollback forgets exactly the keys added since the mark
        palette.Rollback(100);
        Assert.Equal(shuffled[..100], palette.Colors.ToArray());
        Assert.All(shuffled[100..], key => Assert.False(palette.TryGetIndex(key, out _)));
        Assert.True(palette.TryAdd(0x1234_5678));
        Assert.True(palette.TryGetIndex(0x1234_5678, out var index));
        Assert.Equal(100, index);
        palette.Rollback(100);
        Assert.Throws<ArgumentOutOfRangeException>(() => palette.Rollback(101));

        // Freezing sorts the keys: the index of a key is its rank
        palette.Freeze();
        Assert.True(palette.IsFrozen);
        var sorted = shuffled[..100].Order().ToArray();
        Assert.Equal(sorted, palette.Colors.ToArray());
        for (var i = 0; i < sorted.Length; i++)
        {
            Assert.True(palette.TryGetIndex(sorted[i], out index));
            Assert.Equal(i, index);
        }

        Assert.Throws<InvalidOperationException>(() => palette.TryAdd(1));
        palette.Rollback(100); // nothing to remove
        Assert.Throws<InvalidOperationException>(() => palette.Rollback(99));

        palette.Dispose();
        Assert.Equal(0, scope.LiveBytes);
        Assert.Throws<ObjectDisposedException>(() => { _ = palette.TryGetIndex(1, out _); });
    }

    [Fact]
    public void BgraPixelsAreWrittenInRgbaOrder()
    {
        using var image = new Image<Bgra32>(1, 1, new Bgra32(10, 20, 30, 40));
        var pixels = ReferencePng.Parse(Encode(image, new PngEncoder())).DecodePixels();
        Assert.Equal([10, 20, 30, 40], Enumerable.Range(0, 4).Select(channel => pixels.GetSample(0, 0, channel)));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void AdaptiveFilteringPicksTheSmallestSumOfAbsoluteDifferences()
    {
        // Identical rows favor Up, horizontal ramps with large row offsets favor Sub
        const int SourceWidth = 32;
        const int SourceHeight = 12;
        var random = new Random(5);
        var noise = new byte[SourceWidth];
        random.NextBytes(noise);
        var data = new byte[SourceWidth * SourceHeight];
        for (var y = 0; y < SourceHeight; y++)
        {
            for (var x = 0; x < SourceWidth; x++)
            {
                data[(y * SourceWidth) + x] = y < SourceHeight / 2 ? noise[x] : (byte)((y * 100) + x);
            }
        }

        using var image = Image.ImportPixelBytes<Gray8>(data, SourceWidth, SourceHeight);
        var types = ReferencePng.Parse(Encode(image, new PngEncoder())).GetFilterTypes();
        var previous = new byte[SourceWidth];
        for (var y = 0; y < SourceHeight; y++)
        {
            var row = data.AsSpan(y * SourceWidth, SourceWidth).ToArray();
            Assert.Equal(SelectAdaptiveFilter(row, previous, bytesPerPixel: 1), types[y]);
            previous = row;
        }

        Assert.Contains((byte)2, types);
        Assert.Contains((byte)1, types);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeImagesAreStreamedInBoundedChunksWithoutBufferingTheImage(bool asynchronous)
    {
        // 1.6 MB of incompressible 16-bit samples, stored without compression: many IDAT chunks and many writes
        const int SourceWidth = 512;
        const int SourceHeight = 400;
        using var image = CreateRandom<Rgba64>(SourceWidth, SourceHeight, seed: 3);
        using var stream = new TestOutputStream { ForbidSynchronousWrites = asynchronous, ForbidAsynchronousWrites = !asynchronous };
        var options = new ImageWriterOptions(SourceWidth, SourceHeight) { Encoder = new PngEncoder { CompressionLevel = CompressionLevel.NoCompression }, ExpectedFrameCount = 1 };
        long peak;
        using (var writer = Image.CreateWriter<Rgba64>(stream, options))
        {
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
            Assert.Equal(0, writer.Core.Scope.LiveBytes); // session buffers released by Complete
        }

        var imageBytes = (long)SourceWidth * SourceHeight * 8;
        Assert.True(peak < imageBytes / 4, $"Peak writer memory {peak} bytes for a {imageBytes}-byte image.");
        Assert.InRange(asynchronous ? stream.AsynchronousWriteCount : stream.SynchronousWriteCount, 11, int.MaxValue);
        var png = stream.ToArray();
        var reference = ReferencePng.Parse(png);
        var chunks = reference.GetChunks("IDAT").ToList();
        Assert.HasCountGreaterThan(40, chunks);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Data.Length, 1, PngImageDataEncoder.ChunkCapacity));
        AssertDecodesIndependently(png, image, interlaced: false);
    }

    [Fact]
    public void SupportedMetadataIsWrittenBeforeTheImageData()
    {
        var icc = new IccProfile(new MetadataBlob(IccTestBlobs.Create("RGB ")));
        var xmp = new XmpProfile(new MetadataBlob("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8.ToArray()));
        var exif = new ExifProfile(MetadataBlob.FromOwnedArray(new TiffBuilder(bigEndian: true)
            .Ifd0(TiffBuilder.Short(ExifTiff.OrientationTag, 1), TiffBuilder.Short(ExifTiff.ImageWidthTag, 99), TiffBuilder.Short(ExifTiff.ImageLengthTag, 99))
            .ExifIfd(TiffBuilder.Short(ExifTiff.PixelXDimensionTag, 99), TiffBuilder.Long(ExifTiff.PixelYDimensionTag, 99))
            .Build()));
        var longLatin1 = string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet. ", 80));
        var longUnicode = string.Concat(Enumerable.Repeat("Été 中文 ", 300));
        using var image = CreateRandom<Rgba32>(6, 5, seed: 9);
        image.Metadata.IccProfile = icc;
        image.Metadata.XmpProfile = xmp;
        image.Metadata.ExifProfile = exif;
        image.Metadata.Orientation = ExifOrientation.RightTop;
        image.Metadata.Resolution = new ImageResolution(300, 72);
        image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Café ©"));
        image.Metadata.TextEntries.Add(new ImageTextEntry("Author", "中文"));
        image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Un titre", "fr-CA", "Titre"));
        image.Metadata.TextEntries.Add(new ImageTextEntry("Comment", longLatin1));
        image.Metadata.TextEntries.Add(new ImageTextEntry("Description", longUnicode));
        var png = Encode(image, new PngEncoder());

        var reference = ReferencePng.Parse(png);
        Assert.Equal(["IHDR", "iCCP", "pHYs", "eXIf", "iTXt", "tEXt", "iTXt", "iTXt", "zTXt", "iTXt", "IDAT", "IEND"], reference.ChunkTypes);
        Assert.Equal(1, reference.GetChunks("iTXt").Skip(3).Single().Data.Span[12]); // long Unicode text: compressed iTXt
        var fields = EncodedFieldInspector.Inspect("png", png);
        Assert.Equal(icc.Data.ToArray(), fields.Icc!.Value.ToArray());
        Assert.Equal(xmp.Data.ToArray(), fields.Xmp!.Value.ToArray());
        Assert.Equal(ResolutionExpectation.Meter, fields.Resolution!.Unit);
        Assert.Equal(11811, fields.Resolution.X); // round(300 / 0.0254)
        Assert.Equal(2835, fields.Resolution.Y);
        Assert.Equal(["Title: Café ©", "Author: 中文", "Title[fr-CA]: Un titre", "Comment: " + longLatin1, "Description: " + longUnicode], fields.Text.Select(entry => entry.ToString()));
        Assert.Equal("Titre", fields.Text[2].TranslatedKeyword);

        // The typed orientation and the canvas size are reconciled into the EXIF payload
        var writtenExif = fields.Exif!.Value.ToArray();
        Assert.Equal(ExifOrientation.RightTop, ExifTiff.ReadOrientation(writtenExif));
        Assert.Equal((6u, 5u), ExifTiff.ReadPixelDimensions(writtenExif));
        Assert.True(ExifTiff.ExifStructure.TryParse(writtenExif, out var structure, out _));
        Assert.True(structure.TryReadUnsigned(writtenExif, ExifTiff.ExifStructure.Find(structure.Ifd0, ExifTiff.ImageWidthTag)!.Value, out var ifd0Width));
        Assert.Equal(6u, ifd0Width);

        // Supplementary round trip through the library decoder
        using var decoded = Image.Load(png);
        Assert.Equal(ExifOrientation.RightTop, decoded.Metadata.Orientation);
        Assert.Equal(icc.Data.ToArray(), decoded.Metadata.IccProfile!.Data.ToArray());
        Assert.Equal(xmp.Data.ToArray(), decoded.Metadata.XmpProfile!.Data.ToArray());
        Assert.Equal(image.Metadata.TextEntries, decoded.Metadata.TextEntries);
        Assert.Equal(300, decoded.Metadata.Resolution!.HorizontalDpi, 0.01);
        Assert.Equal(72, decoded.Metadata.Resolution.VerticalDpi, 0.01);
    }

    [Fact]
    public void OrientationWithoutExifSynthesizesAMinimalExifChunk()
    {
        using var image = CreateRandom<Gray8>(3, 2, seed: 4);
        image.Metadata.Orientation = ExifOrientation.BottomRight;
        var png = Encode(image, new PngEncoder());
        var fields = EncodedFieldInspector.Inspect("png", png);
        Assert.Equal(ExifOrientation.BottomRight, ExifTiff.ReadOrientation(fields.Exif!.Value.Span));
        Assert.Equal(["IHDR", "eXIf", "IDAT", "IEND"], ReferencePng.Parse(png).ChunkTypes);
    }

    [Fact]
    public void GrayImagesStoreGrayProfilesAndRejectIncompatibleProfiles()
    {
        var gray = new IccProfile(new MetadataBlob(IccTestBlobs.Create("GRAY")));
        var rgb = new IccProfile(new MetadataBlob(IccTestBlobs.Create("RGB ")));
        using var image = CreateRandom<Gray16>(3, 2, seed: 4);
        image.Metadata.IccProfile = gray;
        Assert.Equal(gray.Data.ToArray(), EncodedFieldInspector.Inspect("png", Encode(image, new PngEncoder())).Icc!.Value.ToArray());

        image.Metadata.IccProfile = rgb;
        using var stream = new TestOutputStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new PngEncoder()));
        Assert.StartsWith("Metadata: ICC color profile", exception.Feature, StringComparison.Ordinal);
        Assert.Equal(0, stream.BytesWritten);

        var discarded = Encode(image, new PngEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        Assert.Equal(["IHDR", "IDAT", "IEND"], ReferencePng.Parse(discarded).ChunkTypes);

        using var color = CreateRandom<Rgb24>(3, 2, seed: 4);
        color.Metadata.IccProfile = gray;
        Assert.Throws<UnsupportedImageFeatureException>(() => color.Save(new TestOutputStream(), new PngEncoder()));
    }

    public static TheoryData<string, string, string?, string?> UnsupportedTextEntries => new()
    {
        { "Bad  keyword", "value", null, null },
        { " Leading", "value", null, null },
        { new string('k', 80), "value", null, null },
        { "Tab\tKeyword", "value", null, null },
        { "KeywordĀ", "value", null, null },
        { "XML:com.adobe.xmp", "<x/>", null, null },
        { "Title", "null\0char", null, null },
        { "Title", "lone \ud800 surrogate", null, null },
        { "Title", "value", "fr_FR", null },
        { "Title", "value", "fr", "bad\0translation" },
    };

    [Theory]
    [MemberData(nameof(UnsupportedTextEntries))]
    public void UnsupportedTextEntriesFollowTheMetadataPolicy(string keyword, string value, string? language, string? translated)
    {
        using var image = CreateRandom<Rgb24>(3, 2, seed: 4);
        image.Metadata.TextEntries.Add(new ImageTextEntry("Kept", "kept"));
        image.Metadata.TextEntries.Add(new ImageTextEntry(keyword, value, language, translated));
        using var stream = new TestOutputStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new PngEncoder()));
        Assert.Equal(ImageFormat.Png, exception.Format);
        Assert.StartsWith("Metadata: text entry", exception.Feature, StringComparison.Ordinal);
        Assert.Equal(0, stream.BytesWritten);

        var png = Encode(image, new PngEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        Assert.Equal(["Kept: kept"], EncodedFieldInspector.Inspect("png", png).Text.Select(entry => entry.ToString()));
    }

    [Fact]
    public void StripWritesNoOptionalMetadata()
    {
        using var image = CreateRandom<Rgba32>(3, 2, seed: 4);
        image.Metadata.Orientation = ExifOrientation.LeftBottom;
        image.Metadata.Resolution = new ImageResolution(96, 96);
        image.Metadata.IccProfile = new IccProfile(new MetadataBlob(IccTestBlobs.Create("CMYK")));
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob([0xFF, 0xFE])); // malformed: not validated when stripped
        image.Metadata.TextEntries.Add(new ImageTextEntry("Bad  keyword", "x"));
        var png = Encode(image, new PngEncoder { MetadataHandling = MetadataHandling.Strip });
        Assert.Equal(["IHDR", "IDAT", "IEND"], ReferencePng.Parse(png).ChunkTypes);
        AssertDecodesIndependently(png, image, interlaced: false);
    }

    [Fact]
    public void MalformedPayloadsThrowInvalidContentBeforeAnyOutput()
    {
        using var image = CreateRandom<Rgba32>(3, 2, seed: 4);
        foreach (var handling in new[] { MetadataHandling.Strict, MetadataHandling.DiscardUnsupported })
        {
            image.Metadata.ExifProfile = new ExifProfile(new MetadataBlob([1, 2, 3, 4]));
            using var stream = new TestOutputStream();
            Assert.Equal(ImageFormat.Png, Assert.Throws<InvalidImageContentException>(() => image.Save(stream, new PngEncoder { MetadataHandling = handling })).Format);
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob([0xFF, 0xFE]));
            Assert.Throws<InvalidImageContentException>(() => image.Save(stream, new PngEncoder { MetadataHandling = handling }));
            image.Metadata.XmpProfile = null;
            Assert.Equal(0, stream.BytesWritten);
        }
    }

    [Fact]
    public void StaticModeRejectsAnimatedImagesInsteadOfDroppingFrames()
    {
        using var twoFrames = CreateRandom<Rgba32>(3, 2, seed: 1);
        twoFrames.AppendFrame();
        using var poster = CreateRandom<Rgba32>(3, 2, seed: 1);
        using (var posterSource = CreateRandom<Rgba32>(3, 2, seed: 2))
        {
            poster.SetPosterFrame(posterSource.Frames[0]);
        }

        using var singleFrameAnimation = CreateRandom<Rgba32>(3, 2, seed: 1);
        singleFrameAnimation.Animation = new AnimationMetadata { TotalPlays = 2 };
        foreach (var image in new[] { twoFrames, poster, singleFrameAnimation })
        {
            using var stream = new TestOutputStream();
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new PngEncoder { AnimationMode = PngAnimationMode.Static }));
            Assert.Equal(0, stream.BytesWritten);

            // Automatic and animated modes select APNG output (ApngEncoderTests), never a silently truncated static PNG
            image.Save(stream, new PngEncoder());
            Assert.NotNull(ReferencePng.Parse(stream.ToArray()).Animation);
        }

        using var still = CreateRandom<Rgba32>(3, 2, seed: 1);
        Assert.NotNull(ReferencePng.Parse(Encode(still, new PngEncoder { AnimationMode = PngAnimationMode.Animated })).Animation);
        Assert.Null(ReferencePng.Parse(Encode(still, new PngEncoder())).Animation);
        AssertDecodesIndependently(Encode(still, new PngEncoder { AnimationMode = PngAnimationMode.Auto }), still, interlaced: false);
        AssertDecodesIndependently(Encode(still, new PngEncoder { AnimationMode = PngAnimationMode.Static }), still, interlaced: false);
    }

    [Fact]
    public void PathSavesInferTheModeFromTheExtensionAndExplicitEncodersOverrideIt()
    {
        var directory = TestFiles.CreateDirectory();
        try
        {
            using var image = CreateRandom<Rgb24>(4, 3, seed: 8);
            var png = directory / "still.png";
            image.Save(png);
            AssertDecodesIndependently(File.ReadAllBytes(png), image, interlaced: false);

            var apng = directory / "still.apng";
            image.Save(apng);
            Assert.Equal(1, ReferencePng.Parse(File.ReadAllBytes(apng)).Animation!.NumFrames); // .apng: always animated
            var misleading = directory / "really-a-png.gif";
            image.Save(misleading, new PngEncoder { Interlaced = true });
            AssertDecodesIndependently(File.ReadAllBytes(misleading), image, interlaced: true);
            Assert.Equal(["really-a-png.gif", "still.apng", "still.png"], Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));

            Assert.Throws<ArgumentNullException>(() => image.Save(new TestOutputStream(), encoder: null!));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WritersRequireAnExpectedCountOfOneEvenForNonSeekableOutput()
    {
        using var image = CreateRandom<Rgba32>(3, 2, seed: 1);
        var encoder = new PngEncoder { AnimationMode = PngAnimationMode.Static };
        using var stream = new TestOutputStream();
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(3, 2) { Encoder = encoder }));
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(3, 2) { Encoder = encoder, ExpectedFrameCount = 2 }));
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(3, 2) { Encoder = encoder, ExpectedFrameCount = 1, Animation = new AnimationMetadata() }));
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(3, 2) { Encoder = new PngEncoder() })); // Auto: still needs a count
        Assert.Equal(0, stream.BytesWritten);

        using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(3, 2) { Encoder = new PngEncoder(), ExpectedFrameCount = 1 });
        Assert.Equal(0, stream.BytesWritten); // nothing before the first frame
        Assert.Throws<InvalidOperationException>(() => writer.Complete()); // no frame: usable
        Assert.Throws<InvalidOperationException>(() => writer.WritePosterFrame(image.Frames[0]));
        using var other = new Image<Rgba32>(4, 2);
        Assert.Throws<ArgumentException>(() => writer.WriteFrame(other.Frames[0]));
        writer.WriteFrame(image.Frames[0]);
        Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(image.Frames[0])); // count reached: still usable
        writer.Complete();
        writer.Complete(); // repeated completion is harmless
        Assert.Equal(0, writer.Core.Scope.LiveBytes);
        AssertDecodesIndependently(stream.ToArray(), image, interlaced: false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(33)]
    [InlineData(60)]
    [InlineData(5000)]
    public async Task FailingStreamsFaultTheWriterAndReleaseItsState(long failAt)
    {
        using var image = CreateRandom<Rgba32>(64, 64, seed: 2);
        image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "value"));
        foreach (var asynchronous in new[] { false, true })
        {
            using var stream = new TestOutputStream { FailAtPosition = failAt };
            var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(64, 64) { Encoder = new PngEncoder { CompressionLevel = CompressionLevel.NoCompression }, ExpectedFrameCount = 1, Metadata = image.Metadata });
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
                Assert.Throws<InvalidOperationException>(() => writer.Complete()); // faulted
                Assert.True(stream.BytesWritten <= failAt);
            }
            finally
            {
                writer.Dispose();
            }

            Assert.False(stream.IsDisposed); // caller stream left open
        }
    }

    [Fact]
    public async Task CancellationDuringEncodingFaultsTheWriter()
    {
        using var image = CreateRandom<Rgba64>(300, 200, seed: 2);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        using var stream = new TestOutputStream { CancellationSource = source, CancelAtPosition = 70_000 };
        await using var writer = Image.CreateWriter<Rgba64>(stream, new ImageWriterOptions(300, 200) { Encoder = new PngEncoder { CompressionLevel = CompressionLevel.NoCompression }, ExpectedFrameCount = 1 });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteFrameAsync(image.Frames[0], source.Token).AsTask());
        Assert.InRange(stream.BytesWritten, 70_000, 300 * 200 * 8);
        Assert.Equal(0, writer.Core.Scope.LiveBytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync(XunitCancellationToken).AsTask());
    }

    [Fact]
    public async Task FailedPathSavesPreserveTheDestinationAndPublishNothing()
    {
        var directory = TestFiles.CreateDirectory();
        try
        {
            var path = directory / "out.png";
            await File.WriteAllBytesAsync(path, "previous"u8.ToArray(), XunitCancellationToken);
            using var image = CreateRandom<Rgba32>(8, 8, seed: 2);

            // Late failure: the header was written to the temporary file, then the frame cannot be leased
            image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.Save(path)));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                Task? save = null;
                image.Frames[0].ProcessPixelRows(_ => save = image.SaveAsync(path, cancellationToken: XunitCancellationToken));
                await save!;
            });

            // Early failures: unsupported metadata, pre-canceled token, allocation limit
            image.Metadata.TextEntries.Add(new ImageTextEntry("Bad  keyword", "x"));
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(path));
            image.Metadata.TextEntries.Clear();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsync(path, cancellationToken: new CancellationToken(canceled: true)));
            var tight = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 1024 } };
            Assert.Throws<ImageResourceLimitException>(() => Image.CreateWriter<Rgba32>(path, new ImageWriterOptions(8, 8) { ExpectedFrameCount = 1, Configuration = tight }));

            Assert.Equal("previous"u8.ToArray(), await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.Equal(["out.png"], Directory.GetFileSystemEntries(directory).Select(Path.GetFileName));

            // An uncompleted writer publishes nothing either
            using (var writer = Image.CreateWriter<Rgba32>(path, new ImageWriterOptions(8, 8) { ExpectedFrameCount = 1 }))
            {
                writer.WriteFrame(image.Frames[0]);
            }

            Assert.Equal("previous"u8.ToArray(), await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.Equal(["out.png"], Directory.GetFileSystemEntries(directory).Select(Path.GetFileName));

            await image.SaveAsync(path, cancellationToken: XunitCancellationToken);
            AssertDecodesIndependently(await File.ReadAllBytesAsync(path, XunitCancellationToken), image, interlaced: false);
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
        // Small slabs and padded rows: the frame spans several storage segments
        var data = new byte[70 * 30 * 4];
        new Random(12).NextBytes(data);
        using var image = Image.ImportPixelBytesCore<Rgba32>(data, 70, 30, 0, ImageConfiguration.Default, new PixelStorageLayoutOptions { RowAlignment = 16, TargetSlabBytes = 1024 });
        Assert.True(image.Frames[0].Storage.SlabCount > 1);
        Assert.True(image.Frames[0].Storage.Layout.Stride > image.Frames[0].Storage.RowLength);
        AssertDecodesIndependently(Encode(image, new PngEncoder { Interlaced = true }), image, interlaced: true);
        AssertDecodesIndependently(Encode(image, new PngEncoder()), image, interlaced: false);
    }

    internal static Image CreateRandom(PixelFormat format, int width, int height, int seed)
    {
        var data = new byte[width * height * PixelFormats.GetBytesPerPixel(format)];
        FillRandom(data, seed);
        return format switch
        {
            PixelFormat.Rgba32 => Image.ImportPixelBytes<Rgba32>(data, width, height),
            PixelFormat.Bgra32 => Image.ImportPixelBytes<Bgra32>(data, width, height),
            PixelFormat.Rgb24 => Image.ImportPixelBytes<Rgb24>(data, width, height),
            PixelFormat.Rgba64 => Image.ImportPixelBytes<Rgba64>(data, width, height),
            PixelFormat.Gray8 => Image.ImportPixelBytes<Gray8>(data, width, height),
            _ => Image.ImportPixelBytes<Gray16>(data, width, height),
        };
    }

    internal static Image<TPixel> CreateRandom<TPixel>(int width, int height, int seed)
        where TPixel : unmanaged
    {
        var data = new byte[width * height * PixelFormats.GetBytesPerPixel(PixelFormats.GetPixelFormat<TPixel>())];
        FillRandom(data, seed);
        return Image.ImportPixelBytes<TPixel>(data, width, height);
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static void FillRandom(byte[] data, int seed) => new Random(seed).NextBytes(data);

    private static byte[] Encode(Image image, PngEncoder encoder)
    {
        using var stream = new TestOutputStream();
        image.Save(stream, encoder);
        Assert.False(stream.IsDisposed);
        return stream.ToArray();
    }

    private static ReferencePng AssertDecodesIndependently(byte[] png, Image image, bool interlaced)
    {
        var reference = ReferencePng.Parse(png);
        Assert.Equal(image.Width, reference.Width);
        Assert.Equal(image.Height, reference.Height);
        Assert.Equal(interlaced ? (byte)1 : (byte)0, reference.InterlaceMethod);
        var expected = ImageSnapshots.CaptureFrame(image.Frames[0]);
        var result = PixelBufferComparer.Compare(expected, reference.DecodePixels(), ComparisonPolicy.Exact, $"{image.PixelFormat} PNG decoded by the reference reader");
        Assert.True(result.IsMatch, result.Describe());
        return reference;
    }

    private static void AssertRoundTripsThroughLibraryDecoder(byte[] png, Image image)
    {
        using var decoded = Image.Load(png);
        var result = PixelBufferComparer.Compare(ImageSnapshots.CaptureFrame(image.Frames[0]), ImageSnapshots.CaptureFrame(decoded.Frames[0]), ComparisonPolicy.Exact, "library decoder round trip");
        Assert.True(result.IsMatch, result.Describe());
    }

    internal static Image<TPixel> CreateWithColors<TPixel>(int width, int height, int colors, int seed)
        where TPixel : unmanaged
        => (Image<TPixel>)CreateWithColors(PixelFormats.GetPixelFormat<TPixel>(), width, height, colors, seed);

    /// <summary>
    /// Creates an image of seeded pixels that has exactly <paramref name="colors"/> distinct colors: distinct combinations of
    /// color and alpha for the pixel formats with alpha (fully transparent, partially transparent and opaque entries), gray
    /// levels for the grayscale ones. The samples of the 16-bit pixel formats are multiples of 257.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    internal static Image CreateWithColors(PixelFormat format, int width, int height, int colors, int seed)
    {
        Assert.True(colors <= width * height, "The image is too small for its colors.");
        var random = new Random(seed);
        var palette = new HashSet<(byte R, byte G, byte B, byte A)>();
        while (palette.Count < colors)
        {
            var gray = (byte)random.Next(256);
            var alpha = random.Next(4) switch
            {
                0 => (byte)0,
                1 => (byte)random.Next(1, 255),
                _ => (byte)255,
            };

            palette.Add(format switch
            {
                PixelFormat.Gray8 or PixelFormat.Gray16 => (gray, gray, gray, 255),
                PixelFormat.Rgb24 => ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255),
                _ => ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), alpha),
            });
        }

        // Every color appears at least once, then in runs of random lengths
        var entries = palette.ToArray();
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        var data = new byte[width * height * bytesPerPixel];
        var current = entries[0];
        for (var i = 0; i < width * height; i++)
        {
            if (i < colors)
            {
                current = entries[i];
            }
            else if (random.Next(3) == 0)
            {
                current = entries[random.Next(colors)];
            }

            var pixel = data.AsSpan(i * bytesPerPixel, bytesPerPixel);
            switch (format)
            {
                case PixelFormat.Gray8:
                    pixel[0] = current.R;
                    break;
                case PixelFormat.Gray16:
                    BitConverter.TryWriteBytes(pixel, (ushort)(current.R * 257));
                    break;
                case PixelFormat.Rgb24:
                    (pixel[0], pixel[1], pixel[2]) = (current.R, current.G, current.B);
                    break;
                case PixelFormat.Rgba32:
                    (pixel[0], pixel[1], pixel[2], pixel[3]) = (current.R, current.G, current.B, current.A);
                    break;
                case PixelFormat.Bgra32:
                    (pixel[0], pixel[1], pixel[2], pixel[3]) = (current.B, current.G, current.R, current.A);
                    break;
                default:
                    BitConverter.TryWriteBytes(pixel, (ushort)(current.R * 257));
                    BitConverter.TryWriteBytes(pixel[2..], (ushort)(current.G * 257));
                    BitConverter.TryWriteBytes(pixel[4..], (ushort)(current.B * 257));
                    BitConverter.TryWriteBytes(pixel[6..], (ushort)(current.A * 257));
                    break;
            }
        }

        return format switch
        {
            PixelFormat.Rgba32 => Image.ImportPixelBytes<Rgba32>(data, width, height),
            PixelFormat.Bgra32 => Image.ImportPixelBytes<Bgra32>(data, width, height),
            PixelFormat.Rgb24 => Image.ImportPixelBytes<Rgb24>(data, width, height),
            PixelFormat.Rgba64 => Image.ImportPixelBytes<Rgba64>(data, width, height),
            PixelFormat.Gray8 => Image.ImportPixelBytes<Gray8>(data, width, height),
            _ => Image.ImportPixelBytes<Gray16>(data, width, height),
        };
    }

    /// <summary>
    /// Checks an indexed PNG with the reference reader: color type 3, the palette and the alpha values computed from the
    /// pixels by the harness, zero padding bits, and the pixels expanded through the palette.
    /// </summary>
    private static ReferencePng AssertIndexedDecodesIndependently(byte[] png, Image image, IndexedPngExpectation expectation)
    {
        var reference = ReferencePng.Parse(png);
        Assert.Equal(image.Width, reference.Width);
        Assert.Equal(image.Height, reference.Height);
        Assert.Equal((byte)3, reference.ColorType);
        Assert.Equal(expectation.BitDepth, reference.BitDepth);
        string[] expectedTypes = ["IHDR", .. expectation.PaletteChunkTypes, "IDAT", "IEND"];
        Assert.Equal(expectedTypes, reference.ChunkTypes.Where((type, index) => index == 0 || type != "IDAT" || reference.ChunkTypes[index - 1] != "IDAT"));
        Assert.Equal(expectation.Palette.ToArray(), reference.Palette.ToArray());
        Assert.Equal(expectation.Transparency.ToArray(), reference.Transparency.ToArray());
        Assert.True(reference.ArePaddingBitsZero());
        var expected = IndexedPngExpectation.ToRgba8(ImageSnapshots.CaptureFrame(image.Frames[0]));
        var result = PixelBufferComparer.Compare(expected, reference.DecodePixels(), ComparisonPolicy.Exact, $"{image.PixelFormat} indexed PNG decoded by the reference reader");
        Assert.True(result.IsMatch, result.Describe());
        return reference;
    }

    private static void AssertRejected(Image image, string feature)
    {
        using var stream = new TestOutputStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new PngEncoder { PixelLayout = PngPixelLayout.Indexed }));
        Assert.Equal(ImageFormat.Png, exception.Format);
        Assert.Equal(feature, exception.Feature);
        Assert.Equal(0, stream.BytesWritten);
    }

    /// <summary>Inflates the image data of a PNG: for each scanline, its filter type byte and its filtered bytes.</summary>
    private static byte[] InflateImageData(ReferencePng reference)
    {
        using var compressed = new MemoryStream([.. reference.GetChunks("IDAT").SelectMany(chunk => chunk.Data.ToArray())]);
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();
        inflater.CopyTo(inflated);
        return inflated.ToArray();
    }

    private static int CountAdam7Scanlines(int width, int height)
    {
        (int X0, int Y0, int Dx, int Dy)[] passes = [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];
        var count = 0;
        foreach (var (x0, y0, dx, dy) in passes)
        {
            if (width > x0 && height > y0)
            {
                count += ((height - y0 - 1) / dy) + 1;
            }
        }

        return count;
    }

    private static byte SelectAdaptiveFilter(byte[] row, byte[] previous, int bytesPerPixel)
    {
        // Written from the PNG specification (section 12.8): minimum sum of absolute signed differences, lowest type on ties
        var bestType = 0;
        var bestCost = long.MaxValue;
        for (var type = 0; type <= 4; type++)
        {
            long cost = 0;
            for (var i = 0; i < row.Length; i++)
            {
                int a = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                int b = previous[i];
                int c = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                var predictor = type switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    _ => Paeth(a, b, c),
                };

                cost += Math.Abs((int)(sbyte)(byte)(row[i] - predictor));
            }

            if (cost < bestCost)
            {
                bestCost = cost;
                bestType = type;
            }
        }

        return (byte)bestType;

        static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }
}
