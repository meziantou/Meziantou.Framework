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
        var icc = new IccProfile(new MetadataBlob(MetadataWritePlanTests.CreateIccProfile("RGB ")));
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
        var gray = new IccProfile(new MetadataBlob(MetadataWritePlanTests.CreateIccProfile("GRAY")));
        var rgb = new IccProfile(new MetadataBlob(MetadataWritePlanTests.CreateIccProfile("RGB ")));
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
        image.Metadata.IccProfile = new IccProfile(new MetadataBlob(MetadataWritePlanTests.CreateIccProfile("CMYK")));
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
        var directory = ImageWriterTests.CreateDirectory();
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
        var directory = ImageWriterTests.CreateDirectory();
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
