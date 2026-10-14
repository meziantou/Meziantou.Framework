using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Color;
using Meziantou.Framework.Imaging.Tests.Codecs;
using Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// TIFF and BigTIFF decoding and encoding through the public APIs, on files assembled byte by byte by
/// <see cref="TiffFileBuilder"/> with literal expected pixels: both byte orders, both offset sizes, strips and tiles,
/// samples of 1 to 16 bits, the supported photometric interpretations and compressions, the horizontal predictor,
/// metadata, the rejections of the unsupported variants, and the malformed structures that must not turn into unbounded
/// work.
/// </summary>
/// <remarks>
/// The compressed strips never come from the encoders of the library: LZW and PackBits data is written by the reference
/// encoders of the tests (<see cref="TiffReferenceCodecs"/>) and by the examples of the specifications, CCITT data is
/// spelled out code by code from the tables of ITU-T T.4 and T.6 or was written by libtiff, and JPEG datastreams come
/// from the test-side JPEG encoders (<see cref="JpegTestImage"/>, and <see cref="JpegLosslessTestImage"/> for the
/// lossless ones), whose pixels are computed from the specification. The
/// colors of CMYK pages are checked against the decimal ICC reference of the test harness.
/// </remarks>
public sealed class TiffCodecTests
{
    [Fact]
    public void SettingsHaveDocumentedDefaults()
    {
        var encoder = new TiffEncoder();
        Assert.Equal(ImageFormat.Tiff, encoder.Format);
        Assert.Equal(TiffCompression.Deflate, encoder.Compression);
        Assert.False(encoder.BigTiff);
        Assert.False(encoder.BigEndian);
        Assert.Equal(0, encoder.RowsPerStrip);
        Assert.Equal(TiffPixelLayout.Auto, encoder.PixelLayout);
        Assert.Equal(90, encoder.JpegQuality);
        Assert.Equal(JpegChromaSubsampling.Auto, encoder.JpegChromaSubsampling);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TiffEncoder { Compression = (TiffCompression)42 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TiffEncoder { PixelLayout = (TiffPixelLayout)42 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TiffEncoder { JpegQuality = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TiffEncoder { JpegQuality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TiffEncoder { JpegChromaSubsampling = (JpegChromaSubsampling)42 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TiffEncoder { RowsPerStrip = -1 });
    }

    [Fact]
    public void DetectsClassicAndBigTiffInBothByteOrders()
    {
        Assert.Equal(ImageFormat.Tiff, Image.DetectFormat(Gray(bigEndian: false, bigTiff: false)));
        Assert.Equal(ImageFormat.Tiff, Image.DetectFormat(Gray(bigEndian: true, bigTiff: false)));
        Assert.Equal(ImageFormat.Tiff, Image.DetectFormat(Gray(bigEndian: false, bigTiff: true)));
        Assert.Equal(ImageFormat.Tiff, Image.DetectFormat(Gray(bigEndian: true, bigTiff: true)));

        // A BigTIFF must declare an 8-byte offset size and a zero reserved field
        var broken = Gray(bigEndian: false, bigTiff: true);
        broken[4] = 4;
        Assert.Equal(ImageFormat.Unknown, Image.DetectFormat(broken));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ByteOrderAndOffsetSizeProduceTheSamePixels(bool bigEndian, bool bigTiff)
    {
        var pixels = Decode<Gray8>(Gray(bigEndian, bigTiff));
        Assert.Equal([new Gray8(10), new Gray8(20), new Gray8(30), new Gray8(40), new Gray8(50), new Gray8(60)], pixels);
    }

    [Fact]
    public void WhiteIsZeroInvertsTheSamples()
    {
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            Photometric = 0,
            Samples = [0, 255],
        }).Build();

        Assert.Equal([new Gray8(255), new Gray8(0)], Decode<Gray8>(data));
    }

    [Fact]
    public void RgbAndRgbaSamplesDecodeToTheirLosslessRepresentation()
    {
        var rgb = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            SamplesPerPixel = 3,
            Photometric = 2,
            Samples = [1, 2, 3, 4, 5, 6],
        }).Build();

        using (var image = Image.Load(rgb))
        {
            Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        }

        Assert.Equal([new Rgb24(1, 2, 3), new Rgb24(4, 5, 6)], Decode<Rgb24>(rgb));

        var rgba = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            SamplesPerPixel = 4,
            Photometric = 2,
            ExtraSamples = [2],
            Samples = [1, 2, 3, 4, 5, 6, 7, 8],
        }).Build();

        using (var image = Image.Load(rgba))
        {
            Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
        }

        Assert.Equal([new Rgba32(1, 2, 3, 4), new Rgba32(5, 6, 7, 8)], Decode<Rgba32>(rgba));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SixteenBitSamplesAreReadInTheByteOrderOfTheFile(bool bigEndian)
    {
        var samples = new ushort[] { 0x0102, 0xFFFE, 0x8000 };
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2), samples[i]);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
            }
        }

        var data = new TiffFileBuilder { BigEndian = bigEndian }.AddPage(new TiffPageSpec
        {
            Width = 3,
            Height = 1,
            BitsPerSample = 16,
            Samples = bytes,
        }).Build();

        using var image = Image.Load(data);
        Assert.Equal(PixelFormat.Gray16, image.PixelFormat);
        Assert.Equal([new Gray16(0x0102), new Gray16(0xFFFE), new Gray16(0x8000)], Decode<Gray16>(data));
    }

    [Fact]
    public void SixteenBitRgbIsWidenedToRgba64BecauseThereIsNoSixteenBitRgbFormat()
    {
        var bytes = new byte[6 * 2];
        for (var i = 0; i < 6; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), (ushort)((i + 1) * 1000));
        }

        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            BitsPerSample = 16,
            SamplesPerPixel = 3,
            Photometric = 2,
            Samples = bytes,
        }).Build();

        Assert.Equal(
            [new Rgba64(1000, 2000, 3000), new Rgba64(4000, 5000, 6000)],
            Decode<Rgba64>(data));
    }

    [Fact]
    public void GrayscaleWithAlphaDecodesToRgba32()
    {
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            SamplesPerPixel = 2,
            Photometric = 1,
            ExtraSamples = [2],
            Samples = [10, 200, 20, 100],
        }).Build();

        Assert.Equal([new Rgba32(10, 10, 10, 200), new Rgba32(20, 20, 20, 100)], Decode<Rgba32>(data));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(1, 8)]
    [InlineData(2, 8)]
    [InlineData(2, 32946)]
    public void StripsAndCompressionProduceTheSamePixels(int rowsPerStrip, int compression)
    {
        var samples = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 3,
            Height = 3,
            RowsPerStrip = rowsPerStrip,
            Compression = compression,
            Samples = samples,
        }).Build();

        Assert.Equal([.. samples.Select(static value => new Gray8(value))], Decode<Gray8>(data));
    }

    [Fact]
    public void TilesAreAssembledAndTheirPaddingIsIgnored()
    {
        // A 20x20 page of 16x16 tiles: four tiles, three of which extend past the edges
        var samples = new byte[20 * 20];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (byte)(i % 251);
        }

        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 20,
            Height = 20,
            TileWidth = 16,
            TileLength = 16,
            Samples = samples,
        }).Build();

        Assert.Equal([.. samples.Select(static value => new Gray8(value))], Decode<Gray8>(data));
    }

    [Fact]
    public void TilesWorkWithDeflateAndThePredictor()
    {
        var samples = new byte[32 * 20 * 3];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (byte)(i * 7 % 256);
        }

        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 32,
            Height = 20,
            SamplesPerPixel = 3,
            Photometric = 2,
            TileWidth = 16,
            TileLength = 16,
            Compression = 8,
            Predictor = 2,
            Samples = samples,
        }).Build();

        var expected = new Rgb24[32 * 20];
        for (var i = 0; i < expected.Length; i++)
        {
            expected[i] = new Rgb24(samples[i * 3], samples[(i * 3) + 1], samples[(i * 3) + 2]);
        }

        Assert.Equal(expected, Decode<Rgb24>(data));
    }

    [Fact]
    public void TheHorizontalPredictorIsUndoneForEightAndSixteenBitSamples()
    {
        var eightBit = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 4,
            Height = 2,
            Compression = 8,
            Predictor = 2,
            Samples = [10, 20, 30, 40, 200, 100, 50, 25],
        }).Build();

        Assert.Equal(
            [new Gray8(10), new Gray8(20), new Gray8(30), new Gray8(40), new Gray8(200), new Gray8(100), new Gray8(50), new Gray8(25)],
            Decode<Gray8>(eightBit));

        var values = new ushort[] { 1000, 1001, 60000, 5 };
        var bytes = new byte[values.Length * 2];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), values[i]);
        }

        var sixteenBit = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 4,
            Height = 1,
            BitsPerSample = 16,
            Predictor = 2,
            Samples = bytes,
        }).Build();

        Assert.Equal([.. values.Select(static value => new Gray16(value))], Decode<Gray16>(sixteenBit));
    }

    [Fact]
    public void ResolutionAndOrientationAreReportedAsMetadataAndNeverApplied()
    {
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            Orientation = 6,
            Resolution = new TiffResolutionSpec(300, 600, 1, 2),
            Samples = [1, 2],
        }).Build();

        using var image = Image.Load(data);
        Assert.Equal(new Size(2, 1), image.Size);
        Assert.Equal(ExifOrientation.RightTop, image.Metadata.Orientation);
        Assert.Equal(300, image.Metadata.Resolution!.HorizontalDpi);
        Assert.Equal(600, image.Metadata.Resolution.VerticalDpi);

        // A resolution in centimeters is converted to dots per inch
        var centimeters = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            Resolution = new TiffResolutionSpec(100, 100, 1, 3),
            Samples = [1, 2],
        }).Build();

        using var metric = Image.Load(centimeters);
        Assert.Equal(254, metric.Metadata.Resolution!.HorizontalDpi);
    }

    [Fact]
    public void IdentifyReportsTheFirstPageAndNeverTurnsPagesIntoFrames()
    {
        var data = TwoPages();
        var header = Image.Identify(data);
        Assert.Equal(ImageFormat.Tiff, header.Format);
        Assert.Equal(new Size(2, 1), header.Size);
        Assert.Equal(1, header.FrameCount);
        Assert.False(header.IsAnimated);
        Assert.Null(header.CollectionEntryCount);

        var full = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(2, full.CollectionEntryCount);
        Assert.Equal(1, full.FrameCount);
    }

    [Fact]
    public void LoadDecodesTheFirstPageOnly()
    {
        using var image = Image.Load(TwoPages());
        Assert.Equal(new Size(2, 1), image.Size);
        Assert.Equal(1, image.Frames.Count);
        Assert.False(image.IsAnimated);
    }

    [Fact]
    public void TheCollectionExposesEveryPageWithItsOwnSizeAndPixelFormat()
    {
        using var collection = ImageCollection.Load(TwoPages());
        Assert.Equal(ImageFormat.Tiff, collection.Format);
        Assert.Equal(ImageCollectionKind.Pages, collection.Kind);
        Assert.Equal(2, collection.Count);
        Assert.Equal(new Size(2, 1), collection[0].Size);
        Assert.Equal(PixelFormat.Gray8, collection[0].PixelFormat);
        Assert.Equal(new Size(3, 2), collection[1].Size);
        Assert.Equal(PixelFormat.Rgb24, collection[1].PixelFormat);

        using var page1 = collection[1].Decode();
        Assert.Equal(new Size(3, 2), page1.Size);
        Assert.Equal(PixelFormat.Rgb24, page1.PixelFormat);
        Assert.Equal(1, page1.Frames.Count);
        Assert.False(page1.IsAnimated);
    }

    [Fact]
    public void ExtractingOnePageDoesNotReadTheOtherPages()
    {
        // A tiny first page followed by a large one: neither describing the document nor decoding the first page may
        // read the samples of the second
        var large = new byte[200 * 200 * 3];
        for (var i = 0; i < large.Length; i++)
        {
            large[i] = (byte)i;
        }

        var data = new TiffFileBuilder()
            .AddPage(new TiffPageSpec { Width = 2, Height = 1, Samples = [7, 9] })
            .AddPage(new TiffPageSpec { Width = 200, Height = 200, SamplesPerPixel = 3, Photometric = 2, Samples = large })
            .Build();

        using var stream = new CountingStream(data);
        using var collection = ImageCollection.Load(stream);
        Assert.Equal(2, collection.Count);
        var afterLoad = stream.BytesRead;
        using (var page = collection[0].Decode())
        {
            Assert.Equal(new Size(2, 1), page.Size);
        }

        var total = stream.BytesRead;
        Assert.True(afterLoad < 1024, $"Describing the document read {afterLoad} of {data.Length} bytes.");
        Assert.True(total < 1024, $"Describing the document and decoding its first page read {total} of {data.Length} bytes.");

        // The second page is read only when it is asked for
        using var second = collection[1].Decode();
        Assert.True(stream.BytesRead > large.Length, "Decoding the large page must read its samples.");
    }

    [Theory]
    [InlineData(6, "old-style JPEG")]
    [InlineData(32771, "compression 32771")]
    [InlineData(34713, "compression 34713")]
    public void UnsupportedCompressionIsRecognizedAndRejected(int compression, string name)
    {
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            Compression = compression,
            Samples = [1, 2],
        }).Build();

        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        Assert.Contains(name, exception.Message, StringComparison.Ordinal);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"Compression: {compression}"), exception.Feature);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(32803)]
    public void UnsupportedPhotometricInterpretationsAreRejected(int photometric)
    {
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            Photometric = photometric,
            Samples = [1, 2],
        }).Build();

        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"PhotometricInterpretation: {photometric}"), exception.Feature);
    }

    [Fact]
    public void PlanarStorageSignedAndFloatingPointSamplesAndUnusualDepthsAreRejected()
    {
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            SamplesPerPixel = 3,
            Photometric = 2,
            PlanarConfiguration = 2,
            Samples = [1, 2, 3, 4, 5, 6],
        }).Build()));

        // With one sample per pixel there is one plane, however it is called
        Assert.Equal(Grays(1, 2), Decode<Gray8>(new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            PlanarConfiguration = 2,
            Samples = [1, 2],
        }).Build()));

        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            SampleFormat = 3,
            Samples = [1, 2],
        }).Build()));

        // Sample widths no working pixel format stores exactly, and widths a photometric interpretation is not read with
        foreach (var (bits, photometric, samplesPerPixel) in new[] { (3, 1, 1), (12, 1, 1), (32, 1, 1), (4, 2, 3), (1, 2, 3) })
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec
            {
                Width = 8,
                Height = 1,
                BitsPerSample = bits,
                SamplesPerPixel = samplesPerPixel,
                Photometric = photometric,
                Samples = new byte[8 * samplesPerPixel * 4],
            }).Build()));
            Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"BitsPerSample: {bits}"), exception.Feature);
        }

        // An alpha channel next to packed grayscale samples
        var alpha = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 4,
            Height = 1,
            BitsPerSample = 4,
            SamplesPerPixel = 2,
            ExtraSamples = [2],
            Samples = [1, 2, 3, 4],
        }).Build()));
        Assert.Equal("ExtraSamples: alpha with this sample layout", alpha.Feature);
    }

    [Fact]
    public void AssociatedAndUnspecifiedExtraSamplesAreRejectedInsteadOfBeingGuessed()
    {
        foreach (var extra in new[] { 0, 1 })
        {
            var data = new TiffFileBuilder().AddPage(new TiffPageSpec
            {
                Width = 1,
                Height = 1,
                SamplesPerPixel = 4,
                Photometric = 2,
                ExtraSamples = [extra],
                Samples = [1, 2, 3, 4],
            }).Build();

            Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        }
    }

    [Fact]
    public void AnExtraSampleTagWithoutAValueIsReportedAsMalformedContent()
    {
        // A four-sample page whose ExtraSamples tag declares no value says nothing about its fourth channel
        var page = new TiffPageSpec
        {
            Width = 1,
            Height = 1,
            SamplesPerPixel = 4,
            Photometric = 2,
            Samples = [1, 2, 3, 4],
        };

        page.ExtraFields.Add(new TiffFieldSpec(338, 3, []) { CountOverride = 0 });
        var data = new TiffFileBuilder().AddPage(page).Build();
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Contains("ExtraSamples", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACyclicDirectoryChainIsRejected()
    {
        var data = new TiffFileBuilder { CycleToFirstDirectory = true }.AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            Samples = [1, 2],
        }).Build();

        // The chain points back at the first directory
        var exception = Assert.Throws<InvalidImageContentException>(() => ImageCollection.Load(data));
        Assert.Contains("cyclic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OffsetsAndCountsOutsideTheInputAreRejected()
    {
        // A first-IFD offset past the end of the file
        var data = new TiffFileBuilder { FirstDirectoryOffsetOverride = 1_000_000 }.AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            Samples = [1, 2],
        }).Build();

        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));

        // A strip-offset array whose declared count does not match the geometry
        var mismatched = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 4,
            RowsPerStrip = 1,
            OmitByteCounts = true,
            Samples = [1, 2, 3, 4, 5, 6, 7, 8],
        }).Build();

        Assert.Throws<InvalidImageContentException>(() => Image.Load(mismatched));
    }

    [Fact]
    public void AnImpossibleEntryCountIsRejectedBeforeAnythingIsAllocated()
    {
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            Samples = [1, 2],
        }).Build();

        // The directory of a classic TIFF starts with a 16-bit entry count; claiming 60,000 entries cannot fit
        var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan((int)directoryOffset), 60_000);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
    }

    [Fact]
    public void AMissingRequiredTagIsReportedAsMalformedContent()
    {
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            OmitPhotometric = true,
            Samples = [1, 2],
        }).Build();

        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Contains("PhotometricInterpretation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNumberOfPagesIsBoundedByTheFrameLimit()
    {
        var builder = new TiffFileBuilder();
        for (var i = 0; i < 4; i++)
        {
            builder.AddPage(new TiffPageSpec { Width = 1, Height = 1, Samples = [(byte)i] });
        }

        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 2 } };
        Assert.Throws<ImageResourceLimitException>(() => ImageCollection.Load(builder.Build(), configuration));
    }

    /// <summary>The inputs a TIFF is buffered whole from: every one that is not a synchronous read of a seekable source.</summary>
    public static TheoryData<InputVariant> BufferedVariants =>
        [InputVariant.Span, InputVariant.NonSeekableStream, InputVariant.ShortReadStream, InputVariant.AsyncPath, InputVariant.AsyncStream, InputVariant.AsyncShortReadStream];

    [Theory]
    [MemberData(nameof(BufferedVariants))]
    public async Task ABufferedInputNeedsExactlyItsLengthInEncodedBytes(InputVariant variant)
    {
        // The whole input is buffered up to its end, which a stream only reports once it is read past
        await InputVariants.AssertEncodedByteLimitBoundaryAsync(variant, Gray(bigEndian: false, bigTiff: false), ImageFormat.Tiff, XunitCancellationToken);
        await InputVariants.AssertEncodedByteLimitBoundaryAsync(variant, Gray(bigEndian: true, bigTiff: true), ImageFormat.Tiff, XunitCancellationToken);
        await InputVariants.AssertEncodedByteLimitBoundaryAsync(variant, TwoPages(), ImageFormat.Tiff, XunitCancellationToken);
    }

    [Fact]
    public void ASequentialReaderRejectsTiffExplicitly()
    {
        using var stream = new MemoryStream(TwoPages());
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.OpenReader<Gray8>(stream));
        Assert.Contains("ImageCollection", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACollectionCannotBeLoadedFromASingleImageFormat()
    {
        var png = Codecs.SyntheticImages.Png(2, 2);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => ImageCollection.Load(png.AsSpan()));
        Assert.Equal(ImageFormat.Png, exception.Format);
    }

    [Fact]
    public void LzwDecodesTheExampleOfTheSpecification()
    {
        // TIFF 6.0, section 13: the strip 7 7 7 8 8 7 7 6 6 is written as the codes 256 7 258 8 8 258 6 6 257
        var strip = TiffReferenceCodecs.PackBitString(string.Concat(new[] { 256, 7, 258, 8, 8, 258, 6, 6, 257 }.Select(static code => Convert.ToString(code, 2).PadLeft(9, '0'))));
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 9, Height = 1, Compression = 5, EncodedBlocks = [strip], Samples = [] }).Build();
        Assert.Equal(Grays(7, 7, 7, 8, 8, 7, 7, 6, 6), Decode<Gray8>(data));
    }

    [Theory]
    [InlineData("256 7 258 257", 2, new byte[] { 7, 7 })]
    [InlineData("256 7 258 257", 3, new byte[] { 7, 7, 7 })]
    [InlineData("256 7 8 258 257", 3, new byte[] { 7, 8, 7 })]
    [InlineData("256 7 8 258 260 257", 6, new byte[] { 7, 8, 7, 8, 7, 8 })]
    [InlineData("256 7 8 258 260 257", 7, new byte[] { 7, 8, 7, 8, 7, 8, 7 })]
    [InlineData("256 7 8 258 260 256 9 258 257", 9, new byte[] { 7, 8, 7, 8, 7, 8, 7, 9, 9 })]
    public void LzwStringsAreCopiedAndTheLastOneIsCutAtTheEndOfTheBlock(string codes, int width, byte[] expected)
    {
        // Table codes are strings the block already wrote: 258 is the entry being defined (7 7) or an earlier pair (7 8),
        // 260 is the entry being defined after 258 (7 8 7 8); a string past the end of the block is cut there. After a
        // clear, 258 is defined again
        var strip = TiffReferenceCodecs.PackBitString(string.Concat(codes.Split(' ').Select(static code => Convert.ToString(int.Parse(code, CultureInfo.InvariantCulture), 2).PadLeft(9, '0'))));
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = width, Height = 1, Compression = 5, EncodedBlocks = [strip], Samples = [] }).Build();
        Assert.Equal(Grays(expected), Decode<Gray8>(data));
    }

    [Theory]
    [InlineData(5, 600, 1)]
    [InlineData(5, 5000, 1)]
    [InlineData(5, 20000, 7)]
    [InlineData(32773, 600, 1)]
    [InlineData(32773, 20000, 7)]
    public void LzwAndPackBitsStripsDecodeToTheirSamples(int compression, int length, int rowsPerStrip)
    {
        // Noise makes a new LZW string of almost every byte (the codes grow to 12 bits and the table is cleared), runs
        // make the strings of a table that refers to itself, and a row of noise is a row of PackBits literals
        var width = 100;
        var height = length / width;
        var samples = new byte[width * height];
        Noise(samples, seed: length);
        samples.AsSpan(width * (height / 2), width * 2).Fill(42);
        samples.AsSpan(0, 31).Fill(200);
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = width, Height = height, Compression = compression, RowsPerStrip = rowsPerStrip * 20, Samples = samples }).Build();
        Assert.Equal(Grays(samples), Decode<Gray8>(data));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(32773)]
    public void LzwAndPackBitsWorkWithTilesThePredictorAndSixteenBitSamples(int compression)
    {
        // 40x24 RGB pixels in 16x16 tiles: the tiles of the right and bottom edges are padded
        var samples = new byte[40 * 24 * 3];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (byte)((i / 3) + (i % 3 * 50));
        }

        var tiled = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 40, Height = 24, SamplesPerPixel = 3, Photometric = 2, Compression = compression, Predictor = 2, TileWidth = 16, TileLength = 16, Samples = samples }).Build();
        var decoded = Decode<Rgb24>(tiled);
        for (var i = 0; i < decoded.Length; i++)
        {
            Assert.Equal(new Rgb24(samples[i * 3], samples[(i * 3) + 1], samples[(i * 3) + 2]), decoded[i]);
        }

        var words = new ushort[] { 0, 1, 65535, 32768, 513, 514, 40000, 3 };
        var bytes = new byte[words.Length * 2];
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), words[i]);
        }

        var deep = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 4, Height = 2, BitsPerSample = 16, Compression = compression, Predictor = 2, Samples = bytes }).Build();
        Assert.Equal([.. words.Select(static value => new Gray16(value))], Decode<Gray16>(deep));
    }

    [Fact]
    public void LzwCodeSizesChangeWhereTheSpecificationSaysSo()
    {
        // A sequence without a repeated pair of bytes makes one code of every byte, so the number of bytes says which
        // entry the table has reached when the strip ends: the lengths cover the strips whose last codes are read just
        // before, at and after each change of code size (511, 1023 and 2047), and the clearing of the table (4093)
        var sequence = SequenceWithoutRepeatedPair(4200);
        foreach (var boundary in new[] { 254, 766, 1790, 3836 })
        {
            for (var length = boundary - 4; length <= boundary + 4; length++)
            {
                var samples = sequence[..length];
                var data = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = length, Height = 1, Compression = 5, Samples = samples }).Build();
                Assert.Equal(Grays(samples), Decode<Gray8>(data));
            }
        }
    }

    [Fact]
    public void MalformedLzwDataIsReportedAndTheOldBitOrderIsRejectedExplicitly()
    {
        static byte[] Page(string bits) => new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 4, Height = 1, Compression = 5, EncodedBlocks = [TiffReferenceCodecs.PackBitString(bits)], Samples = [] }).Build();

        // A code the table does not hold yet: 300 after one byte (the next code is 258)
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page("100000000 000000111 100101100")));

        // A table code as the first code after a clear: there is no string to extend
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page("100000000 100000010")));

        // The end of information code before the last sample, and data that simply stops
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page("100000000 000000111 100000001")));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page("100000000 000000111")));

        // The clear code of the LZW of TIFF 5.0 drafts, least significant bit first: 00 01
        var old = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 4, Height = 1, Compression = 5, EncodedBlocks = [[0x00, 0x01, 0x1C, 0x10]], Samples = [] }).Build();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(old));
        Assert.Equal("Compression: LZW with the old bit order", exception.Feature);
    }

    [Fact]
    public void PackBitsDecodesTheExampleOfItsSpecification()
    {
        // Apple Technical Note TN1023, which TIFF 6.0 section 9 refers to; the no-operation header (0x80) is skipped
        byte[] packed = [0xFE, 0xAA, 0x02, 0x80, 0x00, 0x2A, 0x80, 0xFD, 0xAA, 0x03, 0x80, 0x00, 0x2A, 0x22, 0xF7, 0xAA];
        byte[] unpacked = [0xAA, 0xAA, 0xAA, 0x80, 0x00, 0x2A, 0xAA, 0xAA, 0xAA, 0xAA, 0x80, 0x00, 0x2A, 0x22, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA];
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 24, Height = 1, Compression = 32773, EncodedBlocks = [packed], Samples = [] }).Build();
        Assert.Equal(Grays(unpacked), Decode<Gray8>(data));

        // A strip that ends before its last row, in a literal run or before the byte of a repeated run
        foreach (var truncated in new byte[][] { [0xFE, 0xAA, 0x02, 0x80], [0xFE, 0xAA, 0xFD], [0xFE, 0xAA] })
        {
            var broken = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 24, Height = 1, Compression = 32773, EncodedBlocks = [truncated], Samples = [] }).Build();
            Assert.Throws<InvalidImageContentException>(() => Image.Load(broken));
        }
    }

    // Six rows of eight pixels that need every kind of code: . is a pixel of a white run, # of a black run
    internal static readonly string[] FaxRows = ["........", "...##...", "########", "#.......", ".##.....", "........"];

    // The rows above in one dimension (ITU-T T.4, tables 2 and 3): white 8; white 3, black 2, white 3; white 0, black 8;
    // white 0, black 1, white 7; white 1, black 2, white 5
    internal static readonly string[] FaxRowsInOneDimension = ["10011", "1000 11 1000", "00110101 000101", "00110101 010 1111", "000111 11 1100", "10011"];

    // The same rows in two dimensions (ITU-T T.6, table 1), each one relative to the row above it (a white row for the
    // first one): V0; horizontal (white 3, black 2), V0; VL3, VR3; V0, horizontal (black 1, white 7); VR1, horizontal
    // (black 2, white 5); pass, V0
    internal static readonly string[] FaxRowsInTwoDimensions = ["1", "001 1000 11 1", "0000010 0000011", "1 001 010 1111", "011 001 11 1100", "0001 1"];

    internal const string FaxEndOfLine = "000000000001";

    [Fact]
    public void ModifiedHuffmanRowsStartOnByteBoundaries()
    {
        var strip = FaxRowsInOneDimension.SelectMany(TiffReferenceCodecs.PackBitString).ToArray();
        AssertFaxRows(strip, compression: 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupThreeRowsFollowEndOfLineCodes(bool fillBits)
    {
        // With fill bits, zeros are added before each end-of-line code so that it ends on a byte boundary
        var bits = new StringBuilder();
        foreach (var row in FaxRowsInOneDimension)
        {
            AppendEndOfLine(bits, fillBits);
            bits.Append(row.Replace(" ", "", StringComparison.Ordinal));
        }

        AssertFaxRows(TiffReferenceCodecs.PackBitString(bits.ToString()), compression: 3, options: fillBits ? 4u : 0u);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupThreeTwoDimensionalRowsAreTaggedAfterTheEndOfLineCode(bool fillBits)
    {
        // The bit after an end-of-line code is 1 for a row coded in one dimension: the first and the fourth here
        var bits = new StringBuilder();
        for (var row = 0; row < FaxRows.Length; row++)
        {
            var oneDimension = row is 0 or 3;
            AppendEndOfLine(bits, fillBits);
            bits.Append(oneDimension ? '1' : '0');
            bits.Append((oneDimension ? FaxRowsInOneDimension : FaxRowsInTwoDimensions)[row].Replace(" ", "", StringComparison.Ordinal));
        }

        AssertFaxRows(TiffReferenceCodecs.PackBitString(bits.ToString()), compression: 3, options: fillBits ? 5u : 1u);
    }

    [Fact]
    public void GroupFourRowsReferToTheRowAbove()
    {
        // The end-of-facsimile-block sequence (two end-of-line codes) follows the last row
        var bits = string.Concat(FaxRowsInTwoDimensions) + FaxEndOfLine + FaxEndOfLine;
        AssertFaxRows(TiffReferenceCodecs.PackBitString(bits), compression: 4);

        // The bits of each byte in the other order (FillOrder 2), the order of fax devices
        AssertFaxRows(TiffReferenceCodecs.ReverseBits(TiffReferenceCodecs.PackBitString(bits)), compression: 4, fillOrder: 2);

        // BlackIsZero displays the pixels of the white runs as black
        var reversed = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 8, Height = FaxRows.Length, BitsPerSample = 1, Photometric = 1, Compression = 4, EncodedBlocks = [TiffReferenceCodecs.PackBitString(bits)], Samples = [] }).Build();
        Assert.Equal([.. FaxRows.SelectMany(static row => row.Select(static pixel => new Gray8(pixel == '#' ? (byte)255 : (byte)0)))], Decode<Gray8>(reversed));
    }

    [Fact]
    public void FaxRunsLongerThanTheLongestMakeUpCodeRepeatIt()
    {
        // 5,200 pixels: 2560 + 2560 + 64 + 16 white; the same in black after a white run of 0; then 2623 white (2560 + 63)
        // and 2577 black (2560 + 17). The make-up codes from 1792 are shared by both colors
        const string Run2560 = "000000011111";
        string[] rows =
        [
            Run2560 + Run2560 + "11011" + "101010",
            "00110101" + Run2560 + Run2560 + "0000001111" + "0000010111",
            Run2560 + "00110100" + Run2560 + "0000011000",
        ];

        var strip = rows.SelectMany(TiffReferenceCodecs.PackBitString).ToArray();
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 5200, Height = 3, BitsPerSample = 1, Photometric = 0, Compression = 2, EncodedBlocks = [strip], Samples = [] }).Build();
        var pixels = Decode<Gray8>(data);
        for (var x = 0; x < 5200; x++)
        {
            Assert.Equal(255, pixels[x].Value);
            Assert.Equal(0, pixels[5200 + x].Value);
            Assert.Equal(x < 2623 ? 255 : 0, pixels[10400 + x].Value);
        }
    }

    /// <summary>
    /// The strips of a 53x12 bilevel image (<see cref="IsFaxPatternBlack"/>) as libtiff wrote them (<c>tiffcp -c</c> of
    /// libtiff 4.7.2, and the libtiff of Pillow 11.3.0 for modified Huffman), an encoder that shares nothing with the
    /// library: modified Huffman, Group 3 in one and two dimensions with and without fill bits, and Group 4 in both fill
    /// orders. libtiff wrote <c>BlackIsZero</c> pages, so the black pixels of the pattern are the white runs.
    /// </summary>
    private static readonly (string Name, int Compression, uint Options, int FillOrder, string Strip)[] LibTiffFaxStripCases =
    [
        ("modified Huffman", 2, 0, 1, "350370241d0e8743a1d0e8743a1d0e8743a1d0e8743a1d0e8743a1d0e8743a1d0e8735c7c743a1d3a74f8f8f0e9d3c3a1f1d0e9d0e87354e874f0f8e8743a74e9f1f1e1d3a78743e383543a1f1e743a787c743a1d3a74f8f8f0e9d381d3a743a1d0f8f3a1d3c3e3a1d0e9d3a7c7c7435c7c7dd3a743a1d0f8f3a1d3c3e3a1d0e9d3a354e9d0f0f8fba74e8743a1f1e743a787c74383543e3a1e74e8787c7dd3a743a1d0f8f3a1d38743a743a1f1d0f3a743c3e3ee9d3a1d0e87c741d0f8fc21d3a1d0f8e879d3a1e1f1f74e9d0"),
        ("Group 3, one dimension", 3, 0, 1, "00135037001240011d0e8743a1d0e8743a1d0e8743a1d0e8743a1d0e8743a1d0e8743a1d0e8700135c7c743a1d3a74f8f8f0e9d3c3a1f1d0e9d0e87001354e874f0f8e8743a74e9f1f1e1d3a78743e38009aa1d0f8f3a1d3c3e3a1d0e9d3a7c7c7874e9c00474e9d0e8743e3ce874f0f8e8743a74e9f1f1d0009ae3e3ee9d3a1d0e87c79d0e9e1f1d0e874e9d0009aa74e8787c7dd3a743a1d0f8f3a1d3c3e3a1c004d50f8e879d3a1e1f1f74e9d0e8743e3ce874e002e874e8743e3a1e74e8787c7dd3a743a1d0f8e8004743e3f0874e8743e3a1e74e8787c7dd3a740"),
        ("Group 3, two dimensions", 3, 1, 1, "0019a81b80089206e0031d0e8743a1d0e8743a1d0e8743a1d0e8743a1d0e8743a1d0e8743a1d0e870012da4925b56b1ed47da497c0066a9d0e9e1f1d0e874e9d3e3e3c3a74f0e87c7001692a54946a9040aaa12aa49540063a74e8743a1f1e743a787c743a1d3a74f8f8e8004aaad542054952a4a354820555800cd53a743c3e3ee9d3a1d0e87c79d0e9e1f1d0e002d524aa82aab550815254a92800dd0e9d0e87c743ce9d0f0f8fba74e8743a1f1d00092551a4a102aa495505556ab0"),
        ("Group 3, two dimensions, fill bits", 3, 5, 1, "00019a81b800011240dc00018e8743a1d0e8743a1d0e8743a1d0e8743a1d0e8743a1d0e8743a1d0e874380012da4925b56b1ed47da497c00019aa743a787c743a1d3a74f8f8f0e9d3c3a1f1c0001692a54946a9040aaa12aa49540018e9d3a1d0e87c79d0e9e1f1d0e874e9d3e3e3a00012aab550815254a928d5208155600019aa74e8787c7dd3a743a1d0f8f3a1d3c3e3a1c00016a925541555aa840a92a54940001ba1d3a1d0f8e879d3a1e1f1f74e9d0e8743e3a000124aa34942055492aa0aaad56"),
        ("Group 4", 4, 0, 1, "26a06e4903723a23a23a23a23a23a23a23a23a23a23a23a23a23a23a23a23a23a23a23a23a23a23a23a23a0956d2492dab58f6a3ed24bf54946a9040aaa12aa4954692f4952a4a354820555095524aad542054952a4a35482055509555555aa840a92a54946a9040aabaaa0aaad542054952a4a354baa495505556aa102a4a95251a4a102aa495505556aa102a4aa92a8d250815524aa82aab5580080080"),
        ("Group 4, least significant bit first", 4, 0, 2, "64057692c04e5cc4455cc4455cc4455cc4455cc4455cc4455cc4455cc4455cc4455cc4455cc4455cc4455c906a4b92b4d51a6fc5b724fd2a2956090255855425a962492fa95452ac1204aa0aa94a52b52a042aa95452ac1204aa0aa9aaaa5a150295542a29560902555d555055ab42a0924a25c52a5d25a90aaa6a55085452a9a45852085425a90aaa6a550854529554b1a410a84a521554d5aa01100001"),
    ];

    public static TheoryData<string> LibTiffFaxStrips => [.. LibTiffFaxStripCases.Select(static item => item.Name)];

    [Theory]
    [MemberData(nameof(LibTiffFaxStrips))]
    public void FaxStripsWrittenByLibTiffDecodeToTheirPattern(string name)
    {
        var (_, compression, options, fillOrder, strip) = LibTiffFaxStripCases.Single(item => item.Name == name);
        var page = new TiffPageSpec { Width = 53, Height = 12, BitsPerSample = 1, Photometric = 1, Compression = compression, FillOrder = fillOrder == 1 ? null : fillOrder, EncodedBlocks = [Convert.FromHexString(strip)], Samples = [] };
        if (compression == 3)
        {
            page.ExtraFields.Add(TiffFieldSpec.Long(292, options));
        }

        var pixels = Decode<Gray8>(new TiffFileBuilder().AddPage(page).Build());
        for (var y = 0; y < 12; y++)
        {
            for (var x = 0; x < 53; x++)
            {
                if (pixels[(y * 53) + x].Value != (IsFaxPatternBlack(x, y) ? 0 : 255))
                    Assert.Fail($"{name}: the pixel ({x}, {y}) is {pixels[(y * 53) + x].Value}.");
            }
        }
    }

    [Fact]
    public void MalformedFaxDataIsReportedAndTheUncompressedModeIsRejectedExplicitly()
    {
        static byte[] Page(int compression, string bits, int bitsPerSample = 1, uint? options = null)
        {
            var page = new TiffPageSpec { Width = 8, Height = 2, BitsPerSample = bitsPerSample, Photometric = 0, Compression = compression, EncodedBlocks = [TiffReferenceCodecs.PackBitString(bits)], Samples = [] };
            if (options is { } value)
            {
                page.ExtraFields.Add(TiffFieldSpec.Long(compression == 3 ? (ushort)292 : (ushort)293, value));
            }

            return new TiffFileBuilder().AddPage(page).Build();
        }

        // The data ends, or the end-of-facsimile-block sequence comes, before the second row
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(4, "1")));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(4, "1" + FaxEndOfLine + FaxEndOfLine)));

        // A two-dimensional code that does not exist, a run that passes the end of the row (white 9), a run code that
        // does not exist (nine zeros and a one), and a vertical code that puts a change before the first pixel (VL1 under a
        // black row, whose first change is at 0)
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(4, "0000000001 1111")));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(2, "10100000 10011000")));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(2, "0000000001000000 10011000")));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(4, "001 00110101 000101 010")));

        // A Group 3 row coded in two dimensions needs the tag bit of an end-of-line code
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(3, "1 10011 1 10011", options: 1)));

        // The CCITT compressions code bilevel data only
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(4, "11", bitsPerSample: 8)));

        // The uncompressed mode extension, declared by the options or met in the data
        var declared = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(4, "11", options: 2)));
        Assert.Equal("T6Options: uncompressed mode", declared.Feature);
        declared = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(3, FaxEndOfLine + "10011", options: 2)));
        Assert.Equal("T4Options: uncompressed mode", declared.Feature);
        var met = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(4, "1 0000001111")));
        Assert.Equal("Compression: CCITT uncompressed mode", met.Feature);

        // The predictor is defined for samples of 8 bits and more
        var predicted = new TiffPageSpec { Width = 8, Height = 2, BitsPerSample = 1, Photometric = 0, Compression = 4, EncodedBlocks = [[0xC0]], Samples = [] };
        predicted.ExtraFields.Add(TiffFieldSpec.Short(317, 2));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(new TiffFileBuilder().AddPage(predicted).Build()));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 1)]
    public void JpegStripsOfYCbCrSamplesDecodeLikeAJpegFile(int horizontal, int vertical)
    {
        // Three strips, each one a JPEG datastream of its own written by the test-side encoder, whose pixels are computed
        // from the specifications; the last strip holds the rows that are left
        (int H, int V)[] sampling = [(horizontal, vertical), (1, 1), (1, 1)];
        var strips = new[] { JpegTestImage.CreateRandom(21, 16, sampling, seed: 1), JpegTestImage.CreateRandom(21, 16, sampling, seed: 2), JpegTestImage.CreateRandom(21, 5, sampling, seed: 3) };
        var page = new TiffPageSpec { Width = 21, Height = 37, SamplesPerPixel = 3, Photometric = 6, Compression = 7, RowsPerStrip = 16, EncodedBlocks = [.. strips.Select(static strip => strip.Encode())], Samples = [] };
        page.ExtraFields.Add(TiffFieldSpec.Shorts(530, [(ushort)horizontal, (ushort)vertical]));
        var data = new TiffFileBuilder().AddPage(page).Build();

        var info = Image.Identify(data);
        Assert.Equal(ImageColorModel.YCbCr, info.ColorModel);
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        using var image = Image.Load(data);
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        Assert.Equal([.. strips.SelectMany(static strip => strip.GetExpectedPixels())], GetBytes(image));
    }

    [Fact]
    public void JpegStripsUseTheTablesOfTheJpegTablesTag()
    {
        // TIFF Technical Note 2: the quantization and Huffman tables are stored once, in a datastream of their own, and
        // each strip is an abbreviated datastream without tables
        (int H, int V)[] sampling = [(2, 2), (1, 1), (1, 1)];
        var first = JpegTestImage.CreateRandom(19, 16, sampling, seed: 11);
        var (tables, firstStrip) = SplitJpegTables(first.Encode());
        var second = JpegTestImage.FromCoefficients(19, 9, sampling, first.QuantizationTables, (component, blockX, blockY) => first.Components[component].Blocks[(blockY * first.Components[component].BlocksX) + blockX]);
        var (_, secondStrip) = SplitJpegTables(second.Encode());

        TiffPageSpec Page(byte[]? jpegTables)
        {
            var page = new TiffPageSpec { Width = 19, Height = 25, SamplesPerPixel = 3, Photometric = 6, Compression = 7, RowsPerStrip = 16, EncodedBlocks = [firstStrip, secondStrip], Samples = [] };
            if (jpegTables is not null)
            {
                page.ExtraFields.Add(TiffFieldSpec.Bytes(347, 7, jpegTables));
            }

            return page;
        }

        using (var image = Image.Load(new TiffFileBuilder().AddPage(Page(tables)).Build()))
        {
            Assert.Equal([.. first.GetExpectedPixels(), .. second.GetExpectedPixels()], GetBytes(image));
        }

        // Without the tag the strips refer to tables nothing defines, and a tag that is not a datastream of tables is malformed
        Assert.Throws<InvalidImageContentException>(() => Image.Load(new TiffFileBuilder().AddPage(Page(jpegTables: null)).Build()));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(new TiffFileBuilder().AddPage(Page([0xFF, 0xD8, 0x12, 0x34])).Build()));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(new TiffFileBuilder().AddPage(Page([.. tables.AsSpan(0, tables.Length - 6)])).Build()));
    }

    [Fact]
    public void JpegTilesAreDecodedOneDatastreamEach()
    {
        // 40x24 pixels in 16x16 tiles: six datastreams, those of the right and bottom edges extend past the page
        (int H, int V)[] sampling = [(2, 1), (1, 1), (1, 1)];
        var tiles = Enumerable.Range(0, 6).Select(index => JpegTestImage.CreateRandom(16, 16, sampling, seed: 40 + index)).ToArray();
        var page = new TiffPageSpec { Width = 40, Height = 24, SamplesPerPixel = 3, Photometric = 6, Compression = 7, TileWidth = 16, TileLength = 16, EncodedBlocks = [.. tiles.Select(static tile => tile.Encode())], Samples = [] };
        page.ExtraFields.Add(TiffFieldSpec.Shorts(530, [2, 1]));
        using var image = Image.Load(new TiffFileBuilder().AddPage(page).Build());
        var actual = GetBytes(image);
        var expected = tiles.Select(static tile => tile.GetExpectedPixels()).ToArray();
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                var tile = expected[((y / 16) * 3) + (x / 16)].AsSpan((((y % 16) * 16) + (x % 16)) * 3, 3);
                if (!tile.SequenceEqual(actual.AsSpan(((y * 40) + x) * 3, 3)))
                    Assert.Fail($"The pixel ({x}, {y}) differs from the pixel of its tile.");
            }
        }
    }

    [Fact]
    public void ThePhotometricInterpretationSaysWhatTheComponentsOfAJpegStripAre()
    {
        // Grayscale: one component, inverted when 0 is white
        var gray = JpegTestImage.CreateRandom(13, 9, [(1, 1)], seed: 5);
        var grayPixels = gray.GetExpectedPixels();
        using (var image = Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 13, Height = 9, Photometric = 1, Compression = 7, EncodedBlocks = [gray.Encode()], Samples = [] }).Build()))
        {
            Assert.Equal(PixelFormat.Gray8, image.PixelFormat);
            Assert.Equal(grayPixels, GetBytes(image));
        }

        using (var image = Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 13, Height = 9, Photometric = 0, Compression = 7, EncodedBlocks = [gray.Encode()], Samples = [] }).Build()))
        {
            Assert.Equal([.. grayPixels.Select(static value => (byte)(255 - value))], GetBytes(image));
        }

        // RGB: the three components are the samples, whatever the datastream would mean as a JPEG file (this one has no
        // Adobe segment and no R, G, B component identifiers: a JPEG file would convert it from YCbCr)
        (int H, int V)[] full = [(1, 1), (1, 1), (1, 1)];
        var color = JpegTestImage.CreateRandom(13, 9, full, seed: 6);
        var stored = Enumerable.Range(0, 3).Select(color.GetComponentSamples).ToArray();
        using (var image = Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 13, Height = 9, SamplesPerPixel = 3, Photometric = 2, Compression = 7, EncodedBlocks = [color.Encode()], Samples = [] }).Build()))
        {
            var actual = GetBytes(image);
            for (var i = 0; i < 13 * 9; i++)
            {
                Assert.Equal((stored[0][i], stored[1][i], stored[2][i]), (actual[i * 3], actual[(i * 3) + 1], actual[(i * 3) + 2]));
            }
        }

        // CMYK: the four components are ink amounts as TIFF stores them (0 is no ink), not the complements of a JPEG file
        var inks = JpegTestImage.CreateRandom(13, 9, [(1, 1), (1, 1), (1, 1), (1, 1)], seed: 7, inks: JpegTestInkCoding.CmykWithoutAdobe);
        var amounts = inks.GetExpectedInks().Select(static value => (byte)(255 - value)).ToArray();
        var cmyk = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 13, Height = 9, SamplesPerPixel = 4, Photometric = 5, Compression = 7, EncodedBlocks = [inks.Encode()], Samples = [] }).Build();
        using (var image = Image.Load(cmyk, InkOptions))
        {
            Assert.Empty(InkReference.Compare(amounts, GetBytes(image)));
        }
    }

    [Fact]
    public void Jpeg2000StripsDecodeToTheSamplesGivenToTheEncoderOfTheCodestream()
    {
        // The codestreams are written by opj_compress (Jpeg2000TestFiles): the page says what their components are.
        // Grayscale, inverted when 0 is white
        var gray = Jpeg2000TestFiles.GetPattern8(15);
        using (var image = Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 5, Height = 3, Photometric = 1, Compression = 34712, EncodedBlocks = [Jpeg2000TestFiles.Gray], Samples = [] }).Build()))
        {
            Assert.Equal(PixelFormat.Gray8, image.PixelFormat);
            Assert.Equal(gray, GetBytes(image));
        }

        using (var image = Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 5, Height = 3, Photometric = 0, Compression = 34712, EncodedBlocks = [Jpeg2000TestFiles.Gray], Samples = [] }).Build()))
        {
            Assert.Equal([.. gray.Select(static value => (byte)(255 - value))], GetBytes(image));
        }

        // RGB: the component transformation of the codestream is undone by its decoder
        using (var image = Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 5, Height = 3, SamplesPerPixel = 3, Photometric = 2, Compression = 34712, EncodedBlocks = [Jpeg2000TestFiles.Rgb], Samples = [] }).Build()))
        {
            Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
            Assert.Equal(Jpeg2000TestFiles.GetPattern8(45), GetBytes(image));
        }

        // 16-bit samples, whatever the byte order of the file
        foreach (var bigEndian in new[] { false, true })
        {
            using var image = Image.Load(new TiffFileBuilder { BigEndian = bigEndian }.AddPage(new TiffPageSpec { Width = 5, Height = 3, BitsPerSample = 16, Photometric = 1, Compression = 34712, EncodedBlocks = [Jpeg2000TestFiles.Gray16], Samples = [] }).Build());
            Assert.Equal(PixelFormat.Gray16, image.PixelFormat);
            Assert.Equal(Jpeg2000TestFiles.GetPattern16(15), unsafe(MemoryMarshal.Cast<byte, ushort>(GetBytes(image))).ToArray());
        }

        // A block may hold a whole JP2 file: its boxes are not interpreted. Here its fourth component is the unassociated
        // alpha of the page
        using (var image = Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 5, Height = 3, SamplesPerPixel = 4, Photometric = 2, Compression = 34712, ExtraSamples = [2], EncodedBlocks = [Jpeg2000TestFiles.RgbaFile], Samples = [] }).Build()))
        {
            Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
            Assert.Equal(Jpeg2000TestFiles.GetPattern8(60), GetBytes(image));
        }
    }

    [Fact]
    public void Jpeg2000StripsAndTilesAreDecodedOneCodestreamEach()
    {
        // Three strips of three rows: the last one holds the two rows the page still has, and a third one that is not kept
        var gray = Jpeg2000TestFiles.GetPattern8(15);
        using (var image = Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 5, Height = 8, Photometric = 1, Compression = 34712, RowsPerStrip = 3, EncodedBlocks = [Jpeg2000TestFiles.Gray, Jpeg2000TestFiles.Gray, Jpeg2000TestFiles.Gray], Samples = [] }).Build()))
        {
            var actual = GetBytes(image);
            for (var y = 0; y < 8; y++)
            {
                Assert.Equal(gray.AsSpan((y % 3) * 5, 5).ToArray(), actual.AsSpan(y * 5, 5).ToArray());
            }
        }

        // 20x14 pixels in tiles of 16x16, each of which is the codestream of a 13x11 image in four tiles of its own: the
        // codestream of a block must have the size of the block
        var tile = Jpeg2000TestFiles.Tiled;
        Assert.Throws<InvalidImageContentException>(() => Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 20, Height = 14, SamplesPerPixel = 3, Photometric = 2, Compression = 34712, TileWidth = 16, TileLength = 16, EncodedBlocks = [tile, tile], Samples = [] }).Build()));

        // One strip that is this codestream: its four tiles fill the rows of the strip in any order
        using (var image = Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 13, Height = 11, SamplesPerPixel = 3, Photometric = 2, Compression = 34712, EncodedBlocks = [tile], Samples = [] }).Build()))
        {
            Assert.Equal(Jpeg2000TestFiles.GetPattern8(13 * 11 * 3), GetBytes(image));
        }
    }

    [Fact]
    public void CmykJpeg2000StripsAreConvertedLikeTheOtherCmykPages()
    {
        // Four components are the ink amounts of the page (0 is no ink)
        var inks = new byte[60];
        for (var i = 0; i < 15; i++)
        {
            inks[4 * i] = (byte)((i * 37) + 11);
            inks[(4 * i) + 1] = (byte)((i * 41) + 3);
            inks[(4 * i) + 2] = (byte)((i * 43) + 5);
            inks[(4 * i) + 3] = (byte)((i * 13) + 2);
        }

        var data = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 5, Height = 3, SamplesPerPixel = 4, Photometric = 5, Compression = 34712, EncodedBlocks = [Jpeg2000TestFiles.FourComponents], Samples = [] }).Build();
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        using var image = Image.Load(data, InkOptions);
        Assert.Empty(InkReference.Compare(inks, GetBytes(image)));
    }

    [Fact]
    public void AJpeg2000StripMustMatchThePageItBelongsTo()
    {
        static byte[] Page(int width, int height, int samplesPerPixel, int photometric, byte[] block, int bitsPerSample = 8, Action<TiffPageSpec>? configure = null)
        {
            var page = new TiffPageSpec { Width = width, Height = height, SamplesPerPixel = samplesPerPixel, BitsPerSample = bitsPerSample, Photometric = photometric, Compression = 34712, EncodedBlocks = [block], Samples = [] };
            configure?.Invoke(page);
            return new TiffFileBuilder().AddPage(page).Build();
        }

        var strip = Jpeg2000TestFiles.Rgb;
        Image.Load(Page(5, 3, 3, 2, strip)).Dispose();

        // A codestream taller than the strip is not the codestream of the strip; another width, too few rows, another
        // number of components, another depth
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(5, 2, 3, 2, strip)));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(4, 3, 3, 2, strip)));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(5, 4, 3, 2, strip)));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(5, 3, 1, 1, strip)));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(5, 3, 3, 2, strip, bitsPerSample: 16)));

        // A codestream that is cut, or that is not one
        var truncated = Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(5, 3, 3, 2, strip[..^20])));
        Assert.Equal(ImageFormat.Tiff, truncated.Format);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(5, 3, 3, 2, strip[..^2])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(5, 3, 3, 2, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14])));

        // What the codestream cannot be: a JPEG 2000 feature that is not supported is one of the page
        var signed = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(5, 3, 1, 1, Jpeg2000TestFiles.Signed)));
        Assert.Equal((ImageFormat.Tiff, "Compression: signed samples"), (signed.Format, signed.Feature));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(6, 4, 3, 2, Jpeg2000TestFiles.Subsampled)));

        // What the page cannot declare with this compression: these are known from the directory, without reading a block
        Assert.Equal("Compression: JPEG 2000 with 4-bit samples", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Page(5, 3, 1, 1, Jpeg2000TestFiles.Gray4, bitsPerSample: 4))).Feature);
        Assert.Equal("Compression: JPEG 2000 with this sample layout", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Page(5, 3, 3, 6, strip))).Feature);
        var predicted = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 5, Height = 3, SamplesPerPixel = 3, Photometric = 2, Compression = 34712, Predictor = 2, EncodedBlocks = [strip], Samples = [] }).Build();
        Assert.Equal("Predictor: 2 with this sample layout or compression", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(predicted)).Feature);
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, false)]
    [InlineData(2, 2, true)]
    public void TwelveBitJpegStripsDecodeLikeATwelveBitJpegFile(int horizontal, int vertical, bool bigEndian)
    {
        // The datastreams of a page of 12-bit samples have 12-bit samples: they are decoded like a JPEG file of that
        // precision, each sample scaled to 16 bits. The expected pixels are those of the test-side encoder
        (int H, int V)[] sampling = [(horizontal, vertical), (1, 1), (1, 1)];
        var strips = new[] { JpegTestImage.CreateRandom(21, 16, sampling, seed: 81, precision: 12), JpegTestImage.CreateRandom(21, 16, sampling, seed: 82, precision: 12), JpegTestImage.CreateRandom(21, 5, sampling, seed: 83, precision: 12) };
        byte[] Build(TiffFieldSpec? referenceBlackWhite)
        {
            var page = new TiffPageSpec { Width = 21, Height = 37, SamplesPerPixel = 3, BitsPerSample = 12, Photometric = 6, Compression = 7, RowsPerStrip = 16, EncodedBlocks = [.. strips.Select(static strip => strip.Encode())], Samples = [] };
            page.ExtraFields.Add(TiffFieldSpec.Shorts(530, [(ushort)horizontal, (ushort)vertical]));
            if (referenceBlackWhite is not null)
            {
                page.ExtraFields.Add(referenceBlackWhite);
            }

            return new TiffFileBuilder { BigEndian = bigEndian }.AddPage(page).Build();
        }

        var data = Build(referenceBlackWhite: null);
        var info = Image.Identify(data);
        Assert.Equal(ImageColorModel.YCbCr, info.ColorModel);
        Assert.Equal(PixelFormat.Rgba64, info.PixelFormat);
        Assert.Equal(12, info.BitsPerComponent);
        byte[] expected = [.. strips.SelectMany(static strip => strip.GetExpectedPixels())];
        using (var image = Image.Load(data))
        {
            Assert.Equal(PixelFormat.Rgba64, image.PixelFormat);
            Assert.Equal(expected, GetBytes(image));
        }

        // The full range of 12-bit samples, which libtiff writes: black at 0, white at 4095, chroma centered on 2048
        using (var image = Image.Load(Build(new TiffFieldSpec(532, 5, [0, 1, 4095, 1, 2048, 1, 4095, 1, 2048, 1, 4095, 1]))))
        {
            Assert.Equal(expected, GetBytes(image));
        }

        // The full range of 8-bit samples is another conversion for 12-bit ones
        Assert.Equal("Compression: JPEG with other YCbCr coefficients", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Build(new TiffFieldSpec(532, 5, [0, 1, 255, 1, 128, 1, 255, 1, 128, 1, 255, 1])))).Feature);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwelveBitJpegTilesAreDecodedOneDatastreamEach(bool bigEndian)
    {
        // 40x24 pixels in 16x16 tiles of 12-bit grayscale samples: the samples of a tile are kept in the byte order of the
        // file until the rows of the page are assembled
        var tiles = Enumerable.Range(0, 6).Select(index => JpegTestImage.CreateRandom(16, 16, [(1, 1)], seed: 90 + index, precision: 12)).ToArray();
        var expected = tiles.Select(static tile => unsafe(MemoryMarshal.Cast<byte, ushort>(tile.GetExpectedPixels().AsSpan())).ToArray()).ToArray();
        foreach (var photometric in new[] { 1, 0 })
        {
            var page = new TiffPageSpec { Width = 40, Height = 24, BitsPerSample = 12, Photometric = photometric, Compression = 7, TileWidth = 16, TileLength = 16, EncodedBlocks = [.. tiles.Select(static tile => tile.Encode())], Samples = [] };
            using var image = Image.Load(new TiffFileBuilder { BigEndian = bigEndian }.AddPage(page).Build());
            Assert.Equal(PixelFormat.Gray16, image.PixelFormat);
            var actual = unsafe(MemoryMarshal.Cast<byte, ushort>(GetBytes(image).AsSpan()));
            for (var y = 0; y < 24; y++)
            {
                for (var x = 0; x < 40; x++)
                {
                    // 0 is white: the scaled samples are inverted
                    var sample = expected[((y / 16) * 3) + (x / 16)][((y % 16) * 16) + (x % 16)];
                    if (actual[(y * 40) + x] != (photometric == 0 ? ushort.MaxValue - sample : sample))
                        Assert.Fail($"The pixel ({x}, {y}) differs from the pixel of its tile.");
                }
            }
        }
    }

    [Fact]
    public void ThePhotometricInterpretationSaysWhatTheComponentsOfATwelveBitJpegStripAre()
    {
        // RGB: the three components are the samples, scaled to 16 bits (this datastream has no Adobe segment and no R, G,
        // B component identifiers: a JPEG file would convert it from YCbCr)
        (int H, int V)[] full = [(1, 1), (1, 1), (1, 1)];
        var color = JpegTestImage.CreateRandom(13, 9, full, seed: 6, precision: 12);
        var stored = Enumerable.Range(0, 3).Select(color.GetComponentValues).ToArray();
        using (var image = Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 13, Height = 9, SamplesPerPixel = 3, BitsPerSample = 12, Photometric = 2, Compression = 7, EncodedBlocks = [color.Encode()], Samples = [] }).Build()))
        {
            Assert.Equal(PixelFormat.Rgba64, image.PixelFormat);
            var actual = unsafe(MemoryMarshal.Cast<byte, ushort>(GetBytes(image).AsSpan()));
            for (var i = 0; i < 13 * 9; i++)
            {
                Assert.Equal((Scale(stored[0][i]), Scale(stored[1][i]), Scale(stored[2][i]), ushort.MaxValue), (actual[i * 4], actual[(i * 4) + 1], actual[(i * 4) + 2], actual[(i * 4) + 3]));
            }
        }

        // CMYK: the four components are ink amounts as TIFF stores them (0 is no ink), not the complements of a JPEG file
        var inks = JpegTestImage.CreateRandom(13, 9, [(1, 1), (1, 1), (1, 1), (1, 1)], seed: 7, inks: JpegTestInkCoding.CmykWithoutAdobe, precision: 12);
        var amounts = inks.GetExpectedDeepInks().Select(static value => (ushort)(ushort.MaxValue - value)).ToArray();
        var cmyk = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 13, Height = 9, SamplesPerPixel = 4, BitsPerSample = 12, Photometric = 5, Compression = 7, EncodedBlocks = [inks.Encode()], Samples = [] }).Build();
        Assert.Equal(12, Image.Identify(cmyk).BitsPerComponent);
        using (var image = Image.Load(cmyk, InkOptions))
        {
            Assert.Equal(PixelFormat.Rgba64, image.PixelFormat);
            var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(GetBytes(image).AsSpan()));
            var colors = new ushort[13 * 9 * 3];
            for (var pixel = 0; pixel < 13 * 9; pixel++)
            {
                pixels.Slice(4 * pixel, 3).CopyTo(colors.AsSpan(3 * pixel));
                Assert.Equal(ushort.MaxValue, pixels[(4 * pixel) + 3]);
            }

            Assert.Empty(InkReference.Compare(amounts, colors));
        }

        // The nearest 16-bit value of the same fraction of the range
        static ushort Scale(int sample) => (ushort)(((sample * 65535) + 2047) / 4095);
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(10, false)]
    [InlineData(12, true)]
    [InlineData(16, false)]
    [InlineData(16, true)]
    public void LosslessJpegStripsDecodeToTheirSamples(int bitsPerSample, bool bigEndian)
    {
        // The datastreams of a page may be lossless ones (T.81 Annex H), of any precision from 8 to 16 bits: the samples of
        // the page are the ones given to the test-side encoder, scaled to 16 bits when they have more than 8
        ushort[] Words(Image image) => bitsPerSample == 8 ? [.. GetBytes(image).Select(static value => (ushort)value)] : unsafe(MemoryMarshal.Cast<byte, ushort>(GetBytes(image).AsSpan())).ToArray();
        var deep = bitsPerSample != 8;

        // Three strips of grayscale samples; 0 is white on the second page
        var strips = new[] { JpegLosslessTestImage.CreateRandom(21, 8, [(1, 1)], bitsPerSample, seed: 31), JpegLosslessTestImage.CreateRandom(21, 8, [(1, 1)], bitsPerSample, seed: 32), JpegLosslessTestImage.CreateRandom(21, 3, [(1, 1)], bitsPerSample, seed: 33) };
        ushort[] graySamples = [.. strips.SelectMany(strip => strip.GetStoredSamples().Select(sample => deep ? strip.ScaleToSixteenBits(sample) : (ushort)sample))];
        foreach (var photometric in new[] { 1, 0 })
        {
            var page = new TiffPageSpec { Width = 21, Height = 19, BitsPerSample = bitsPerSample, Photometric = photometric, Compression = 7, RowsPerStrip = 8, EncodedBlocks = [.. strips.Select((strip, index) => strip.Encode(new JpegLosslessTestOptions { Predictor = 1 + index, RestartRows = index }))], Samples = [] };
            var data = new TiffFileBuilder { BigEndian = bigEndian }.AddPage(page).Build();
            Assert.Equal(bitsPerSample, Image.Identify(data).BitsPerComponent);
            using var image = Image.Load(data);
            Assert.Equal(deep ? PixelFormat.Gray16 : PixelFormat.Gray8, image.PixelFormat);
            var white = deep ? ushort.MaxValue : byte.MaxValue;
            Assert.Equal(photometric == 0 ? [.. graySamples.Select(sample => (ushort)(white - sample))] : graySamples, Words(image));
        }

        // RGB tiles: the three components are the samples (the datastream is the one of a YCbCr JPEG file)
        var tiles = Enumerable.Range(0, 4).Select(index => JpegLosslessTestImage.CreateRandom(16, 16, [(1, 1), (1, 1), (1, 1)], bitsPerSample, seed: 40 + index)).ToArray();
        var tiled = new TiffPageSpec { Width = 30, Height = 20, SamplesPerPixel = 3, BitsPerSample = bitsPerSample, Photometric = 2, Compression = 7, TileWidth = 16, TileLength = 16, EncodedBlocks = [.. tiles.Select(static tile => tile.Encode(new JpegLosslessTestOptions { Predictor = 4 }))], Samples = [] };
        using (var image = Image.Load(new TiffFileBuilder { BigEndian = bigEndian }.AddPage(tiled).Build()))
        {
            Assert.Equal(deep ? PixelFormat.Rgba64 : PixelFormat.Rgb24, image.PixelFormat);
            var actual = Words(image);
            var channels = deep ? 4 : 3;
            var stored = tiles.Select(static tile => tile.GetStoredSamples()).ToArray();
            for (var y = 0; y < 20; y++)
            {
                for (var x = 0; x < 30; x++)
                {
                    var tile = ((y / 16) * 2) + (x / 16);
                    for (var c = 0; c < 3; c++)
                    {
                        var sample = stored[tile][((((y % 16) * 16) + (x % 16)) * 3) + c];
                        if (actual[(((y * 30) + x) * channels) + c] != (deep ? tiles[tile].ScaleToSixteenBits(sample) : sample))
                            Assert.Fail($"The sample {c} of the pixel ({x}, {y}) differs from the sample of its tile.");
                    }
                }
            }
        }

        // YCbCr with subsampled chroma: converted like the components of a JPEG file, at the precision of the samples
        var color = JpegLosslessTestImage.CreateRandom(17, 11, [(2, 2), (1, 1), (1, 1)], bitsPerSample, seed: 50);
        var ycbcr = new TiffPageSpec { Width = 17, Height = 11, SamplesPerPixel = 3, BitsPerSample = bitsPerSample, Photometric = 6, Compression = 7, EncodedBlocks = [color.Encode(new JpegLosslessTestOptions { Predictor = 7, PaddingSeed = 1 })], Samples = [] };
        ycbcr.ExtraFields.Add(TiffFieldSpec.Shorts(530, [2, 2]));
        using (var image = Image.Load(new TiffFileBuilder { BigEndian = bigEndian }.AddPage(ycbcr).Build()))
        {
            Assert.Equal(color.GetExpectedPixels(), GetBytes(image));
        }

        // A datastream of another precision is not the one of the page
        var other = JpegLosslessTestImage.CreateRandom(21, 19, [(1, 1)], bitsPerSample == 16 ? 15 : bitsPerSample + 1, seed: 34);
        var mismatch = new TiffPageSpec { Width = 21, Height = 19, BitsPerSample = bitsPerSample, Photometric = 1, Compression = 7, EncodedBlocks = [other.Encode()], Samples = [] };
        Assert.Contains($"the page declares {bitsPerSample}", Assert.Throws<InvalidImageContentException>(() => Image.Load(new TiffFileBuilder().AddPage(mismatch).Build())).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AJpegStripMustMatchThePageItBelongsTo()
    {
        (int H, int V)[] sampling = [(1, 1), (1, 1), (1, 1)];
        var strip = JpegTestImage.CreateRandom(16, 8, sampling, seed: 9).Encode();
        byte[] Page(int width, int height, int samplesPerPixel, int photometric, byte[] block, int bitsPerSample = 8, Action<TiffPageSpec>? configure = null)
        {
            var page = new TiffPageSpec { Width = width, Height = height, SamplesPerPixel = samplesPerPixel, BitsPerSample = bitsPerSample, Photometric = photometric, Compression = 7, EncodedBlocks = [block], Samples = [] };
            page.ExtraFields.Add(TiffFieldSpec.Shorts(530, [1, 1]));
            configure?.Invoke(page);
            return new TiffFileBuilder().AddPage(page).Build();
        }

        Image.Load(Page(16, 8, 3, 6, strip)).Dispose();

        // Some writers give the last strip the height of the others: the rows past the page are not kept
        var lastStrip = new TiffPageSpec { Width = 16, Height = 13, SamplesPerPixel = 3, Photometric = 6, Compression = 7, RowsPerStrip = 8, EncodedBlocks = [strip, strip], Samples = [] };
        lastStrip.ExtraFields.Add(TiffFieldSpec.Shorts(530, [1, 1]));
        using (var padded = Image.Load(new TiffFileBuilder().AddPage(lastStrip).Build()))
        {
            Assert.Equal(new Size(16, 13), padded.Size);
        }

        // A frame taller than a strip would be decoded to be thrown away: it is not the frame of the strip
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(16, 5, 3, 6, strip)));

        // Another width, too few rows, another number of components
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(15, 8, 3, 6, strip)));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(16, 9, 3, 6, strip)));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(16, 8, 1, 1, strip)));

        // A datastream that is cut, or that is not one
        var truncated = Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(16, 8, 3, 6, strip[..^20])));
        Assert.Equal(ImageFormat.Tiff, truncated.Format);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(16, 8, 3, 6, [1, 2, 3, 4])));

        // What a JPEG file cannot be either is reported for the page: here a hierarchical frame (SOF5)
        var hierarchical = (byte[])strip.Clone();
        hierarchical[hierarchical.AsSpan().IndexOf((ReadOnlySpan<byte>)[0xFF, 0xC0]) + 1] = 0xC5;
        var unsupported = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(16, 8, 3, 6, hierarchical)));
        Assert.Equal(ImageFormat.Tiff, unsupported.Format);
        Assert.Equal("Compression: JPEG hierarchical", unsupported.Feature);

        // And what a JPEG file can be is decoded: an arithmetic-coded datastream has the samples of the Huffman-coded one
        var image = JpegTestImage.CreateRandom(16, 8, sampling, seed: 9);
        using (var huffman = Image.Load(Page(16, 8, 3, 6, strip)))
        using (var arithmetic = Image.Load(Page(16, 8, 3, 6, image.Encode(new JpegTestEncodeOptions { Arithmetic = true, RestartInterval = 1 }))))
        {
            Assert.Equal(GetBytes(huffman), GetBytes(arithmetic));
        }

        // The samples of a datastream have the precision the page declares: the page is described without its strips
        var deep = JpegTestImage.CreateRandom(16, 8, sampling, seed: 9, precision: 12).Encode();
        Image.Load(Page(16, 8, 3, 6, deep, bitsPerSample: 12)).Dispose();
        Assert.Contains("samples of 12 bits; the page declares 8", Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(16, 8, 3, 6, deep))).Message, StringComparison.Ordinal);
        Assert.Contains("samples of 8 bits; the page declares 12", Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(16, 8, 3, 6, strip, bitsPerSample: 12))).Message, StringComparison.Ordinal);
        Assert.Contains("samples of 8 bits; the page declares 16", Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(16, 8, 3, 2, strip, bitsPerSample: 16))).Message, StringComparison.Ordinal);
        Assert.Equal(16, Image.Identify(Page(16, 8, 3, 2, strip, bitsPerSample: 16)).BitsPerComponent);

        // JPEG stores samples of 8 to 16 bits without alpha, no palette indexes, and YCbCr samples as JFIF defines them
        Assert.Equal("Compression: JPEG with 4-bit samples", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Page(16, 8, 1, 1, strip, bitsPerSample: 4))).Feature);
        Assert.Equal("BitsPerSample: 3", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Page(16, 8, 1, 1, strip, bitsPerSample: 3))).Feature);
        Assert.Equal("Compression: JPEG with this sample layout", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(16, 8, 4, 2, strip, configure: static page => page.ExtraFields.Add(TiffFieldSpec.Short(338, 2))))).Feature);
        Assert.Equal("Compression: JPEG with other YCbCr coefficients", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(16, 8, 3, 6, strip, configure: static page => page.ExtraFields.Add(new TiffFieldSpec(529, 5, [2126, 10000, 7152, 10000, 722, 10000]))))).Feature);
        Assert.Equal("Compression: JPEG with other YCbCr coefficients", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(16, 8, 3, 6, strip, configure: static page => page.ExtraFields.Add(new TiffFieldSpec(532, 5, [16, 1, 235, 1, 128, 1, 240, 1, 128, 1, 240, 1]))))).Feature);

        // The full-range values libtiff writes for JPEG are the ones of JFIF
        Image.Load(Page(16, 8, 3, 6, strip, configure: static page => page.ExtraFields.Add(new TiffFieldSpec(532, 5, [0, 1, 255, 1, 128, 1, 255, 1, 128, 1, 255, 1])))).Dispose();
    }

    [Fact]
    public void BilevelAndSmallGraySamplesAreScaledExactlyToEightBits()
    {
        // One bit per pixel, most significant bit first; a row is padded to a whole byte
        var bilevel = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 10, Height = 2, BitsPerSample = 1, Photometric = 1, Samples = [0b1010_0000, 0b1100_0000, 0b0000_1111, 0b0100_0000] }).Build();
        var info = Image.Identify(bilevel);
        Assert.Equal(1, info.BitsPerComponent);
        Assert.Equal(ImageColorModel.Grayscale, info.ColorModel);
        Assert.Equal(PixelFormat.Gray8, info.PixelFormat);
        Assert.Equal(Grays(255, 0, 255, 0, 0, 0, 0, 0, 255, 255, 0, 0, 0, 0, 255, 255, 255, 255, 0, 255), Decode<Gray8>(bilevel));

        // WhiteIsZero inverts; BitsPerSample defaults to 1
        var inverted = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 4, Height = 1, BitsPerSample = 1, Photometric = 0, OmitBitsPerSample = true, Samples = [0b1010_0000] }).Build();
        Assert.Equal(Grays(0, 255, 0, 255), Decode<Gray8>(inverted));

        // Two bits: 0, 1, 2, 3 are 0, 85, 170, 255. Four bits: n is 17 n
        var two = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 5, Height = 1, BitsPerSample = 2, Photometric = 1, Samples = [0b00_01_10_11, 0b10_00_00_00] }).Build();
        Assert.Equal(2, Image.Identify(two).BitsPerComponent);
        Assert.Equal(Grays(0, 85, 170, 255, 170), Decode<Gray8>(two));
        var four = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 3, Height = 1, BitsPerSample = 4, Photometric = 0, Samples = [0x0F, 0x70] }).Build();
        Assert.Equal(Grays(255, 0, 136), Decode<Gray8>(four));

        // The bits of each byte in the other order (FillOrder 2), only defined for bilevel data
        var reversed = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 10, Height = 1, BitsPerSample = 1, Photometric = 1, FillOrder = 2, Samples = [0b0000_0101, 0b0000_0011] }).Build();
        Assert.Equal(Grays(255, 0, 255, 0, 0, 0, 0, 0, 255, 255), Decode<Gray8>(reversed));
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 2, Height = 1, FillOrder = 2, Samples = [1, 2] }).Build()));
        Assert.Equal("FillOrder: 2", exception.Feature);

        // Bilevel tiles: a tile starts on a byte of the row whatever the sample width
        var samples = new byte[3 * 20];
        Noise(samples, seed: 4);
        var tiled = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 20, Height = 20, BitsPerSample = 1, Photometric = 1, Compression = 5, TileWidth = 16, TileLength = 16, Samples = samples }).Build();
        var pixels = Decode<Gray8>(tiled);
        for (var i = 0; i < 400; i++)
        {
            var (x, y) = (i % 20, i / 20);
            Assert.Equal(((samples[(y * 3) + (x / 8)] >> (7 - (x % 8))) & 1) * 255, pixels[i].Value);
        }
    }

    [Fact]
    public void PaletteIndexesDecodeToTheColorsOfTheColorMap()
    {
        // A color map stores all the red values, then the green ones, then the blue ones. Writers store an 8-bit color c
        // as 257 c or as 256 c: both are 8-bit colors
        ushort[] Map(int entries, int factor, params (int Index, byte R, byte G, byte B)[] colors)
        {
            var map = new ushort[entries * 3];
            foreach (var (index, r, g, b) in colors)
            {
                map[index] = (ushort)(r * factor);
                map[entries + index] = (ushort)(g * factor);
                map[(2 * entries) + index] = (ushort)(b * factor);
            }

            return map;
        }

        foreach (var factor in new[] { 257, 256 })
        {
            var page = new TiffPageSpec { Width = 3, Height = 1, Photometric = 3, Samples = [2, 0, 255] };
            page.ExtraFields.Add(TiffFieldSpec.Shorts(320, Map(256, factor, (0, 10, 20, 30), (2, 255, 0, 128), (255, 1, 2, 3))));
            var data = new TiffFileBuilder().AddPage(page).Build();
            var info = Image.Identify(data);
            Assert.Equal(ImageColorModel.Indexed, info.ColorModel);
            Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
            Assert.Equal(8, info.BitsPerComponent);
            Assert.Equal([new Rgb24(255, 0, 128), new Rgb24(10, 20, 30), new Rgb24(1, 2, 3)], Decode<Rgb24>(data));
        }

        // Indexes of 4, 2 and 1 bits are packed, the first one in the most significant bits
        var four = new TiffPageSpec { Width = 3, Height = 1, BitsPerSample = 4, Photometric = 3, Compression = 32773, Samples = [0xF1, 0x00] };
        four.ExtraFields.Add(TiffFieldSpec.Shorts(320, Map(16, 257, (0, 1, 1, 1), (1, 9, 8, 7), (15, 200, 100, 50))));
        Assert.Equal([new Rgb24(200, 100, 50), new Rgb24(9, 8, 7), new Rgb24(1, 1, 1)], Decode<Rgb24>(new TiffFileBuilder().AddPage(four).Build()));
        var two = new TiffPageSpec { Width = 4, Height = 1, BitsPerSample = 2, Photometric = 3, Samples = [0b11_00_01_10] };
        two.ExtraFields.Add(TiffFieldSpec.Shorts(320, Map(4, 257, (0, 0, 0, 0), (1, 1, 1, 1), (2, 2, 2, 2), (3, 3, 3, 3))));
        Assert.Equal([new Rgb24(3, 3, 3), new Rgb24(0, 0, 0), new Rgb24(1, 1, 1), new Rgb24(2, 2, 2)], Decode<Rgb24>(new TiffFileBuilder().AddPage(two).Build()));
        var one = new TiffPageSpec { Width = 3, Height = 1, BitsPerSample = 1, Photometric = 3, Samples = [0b101_00000] };
        one.ExtraFields.Add(TiffFieldSpec.Shorts(320, Map(2, 257, (0, 255, 255, 0), (1, 0, 0, 255))));
        Assert.Equal(1, Image.Identify(new TiffFileBuilder().AddPage(one).Build()).BitsPerComponent);
        Assert.Equal([new Rgb24(0, 0, 255), new Rgb24(255, 255, 0), new Rgb24(0, 0, 255)], Decode<Rgb24>(new TiffFileBuilder().AddPage(one).Build()));

        // Entries that are neither all multiples of 257 nor all multiples of 256 are colors that 8 bits cannot store:
        // the page keeps them, as opaque 16-bit pixels
        var deep = new TiffPageSpec { Width = 2, Height = 1, BitsPerSample = 1, Photometric = 3, Samples = [0b01_000000] };
        deep.ExtraFields.Add(TiffFieldSpec.Shorts(320, [1000, 65535, 2000, 257, 3000, 256]));
        var deepData = new TiffFileBuilder().AddPage(deep).Build();
        Assert.Equal(PixelFormat.Rgba64, Image.Identify(deepData).PixelFormat);
        Assert.Equal([new Rgba64(1000, 2000, 3000, 65535), new Rgba64(65535, 257, 256, 65535)], Decode<Rgba64>(deepData));
    }

    [Fact]
    public void AMalformedOrUnsupportedPaletteIsReported()
    {
        // No color map, a color map of another size, an alpha channel, 16-bit indexes
        Assert.Throws<InvalidImageContentException>(() => Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 1, Height = 1, Photometric = 3, Samples = [0] }).Build()));
        var wrongSize = new TiffPageSpec { Width = 1, Height = 1, Photometric = 3, Samples = [0] };
        wrongSize.ExtraFields.Add(TiffFieldSpec.Shorts(320, new ushort[48]));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(new TiffFileBuilder().AddPage(wrongSize).Build()));

        var alpha = new TiffPageSpec { Width = 1, Height = 1, SamplesPerPixel = 2, Photometric = 3, ExtraSamples = [2], Samples = [0, 255] };
        alpha.ExtraFields.Add(TiffFieldSpec.Shorts(320, new ushort[768]));
        Assert.Equal("ExtraSamples: alpha with this sample layout", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(new TiffFileBuilder().AddPage(alpha).Build())).Feature);

        var deep = new TiffPageSpec { Width = 1, Height = 1, BitsPerSample = 16, Photometric = 3, Samples = [0, 0] };
        Assert.Equal("BitsPerSample: 16", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(new TiffFileBuilder().AddPage(deep).Build())).Feature);
    }

    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(5, false, 3)]
    [InlineData(5, true, 3)]
    [InlineData(8, false, 1)]
    [InlineData(32773, true, 1)]
    public void CmykSamplesAreConvertedFromTheirInkAmounts(int compression, bool tiled, int rowsPerStrip)
    {
        // The samples of a separated page are ink amounts (0 is no ink): they are what the ICC conversion expects
        const int Width = 19;
        const int Height = 17;
        var inks = new byte[Width * Height * 4];
        Noise(inks, seed: compression);
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = Width,
            Height = Height,
            SamplesPerPixel = 4,
            Photometric = 5,
            Compression = compression,
            RowsPerStrip = rowsPerStrip,
            TileWidth = tiled ? 16 : null,
            TileLength = tiled ? 16 : null,
            Samples = inks,
        }).Build();

        // Identification needs no conversion; a load does
        var info = Image.Identify(data);
        Assert.Equal(ImageColorModel.Cmyk, info.ColorModel);
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        Assert.Null(info.Metadata.IccProfile);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        Assert.Equal("PhotometricInterpretation: 5 without color conversion", exception.Feature);
        Assert.Equal(ImageFormat.Tiff, exception.Format);

        using var image = Image.Load(data, InkOptions);
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        Assert.Equal(IccProfile.Srgb.Data.ToArray(), image.Metadata.IccProfile?.Data.ToArray());
        Assert.Empty(InkReference.Compare(inks, GetBytes(image)));
    }

    [Fact]
    public void CmykSamplesKeepTheirAlphaAndTheirSixteenBits()
    {
        // Eight bits with an unassociated alpha sample: Rgba32, the alpha sample copied
        var inks = new byte[7 * 5 * 5];
        Noise(inks, seed: 21);
        var withAlpha = new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 7, Height = 5, SamplesPerPixel = 5, Photometric = 5, ExtraSamples = [2], Samples = inks }).Build();
        Assert.True(Image.Identify(withAlpha).MayHaveTransparency is true);
        using (var image = Image.Load(withAlpha, InkOptions))
        {
            Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
            var actual = GetBytes(image);
            var source = new byte[7 * 5 * 4];
            var colors = new byte[7 * 5 * 3];
            for (var i = 0; i < 7 * 5; i++)
            {
                inks.AsSpan(i * 5, 4).CopyTo(source.AsSpan(i * 4));
                actual.AsSpan(i * 4, 3).CopyTo(colors.AsSpan(i * 3));
                Assert.Equal(inks[(i * 5) + 4], actual[(i * 4) + 3]);
            }

            Assert.Empty(InkReference.Compare(source, colors));
        }

        // Sixteen bits, in both byte orders: Rgba64, opaque, converted with the precision of the samples
        foreach (var bigEndian in new[] { false, true })
        {
            var words = new ushort[5 * 3 * 4];
            for (var i = 0; i < words.Length; i++)
            {
                words[i] = (ushort)((i * 7919) + (i % 4 * 12345));
            }

            var bytes = new byte[words.Length * 2];
            for (var i = 0; i < words.Length; i++)
            {
                if (bigEndian)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2), words[i]);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), words[i]);
                }
            }

            var deep = new TiffFileBuilder { BigEndian = bigEndian }.AddPage(new TiffPageSpec { Width = 5, Height = 3, SamplesPerPixel = 4, BitsPerSample = 16, Photometric = 5, Compression = 5, Samples = bytes }).Build();
            var pixels = Decode<Rgba64>(deep, InkOptions);
            var colors = new ushort[5 * 3 * 3];
            for (var i = 0; i < pixels.Length; i++)
            {
                (colors[i * 3], colors[(i * 3) + 1], colors[(i * 3) + 2]) = (pixels[i].R, pixels[i].G, pixels[i].B);
                Assert.Equal(ushort.MaxValue, pixels[i].A);
            }

            Assert.Empty(InkReference.Compare(words, colors));
        }
    }

    [Fact]
    public void CmykSamplesUseTheProfileOfThePageAndTheOptionsOfTheConversion()
    {
        var profile = IccTestProfiles.CmykLabLut16().Data.ToArray();
        var inks = new byte[11 * 6 * 4];
        Noise(inks, seed: 33);
        TiffPageSpec Page(byte[]? icc)
        {
            var page = new TiffPageSpec { Width = 11, Height = 6, SamplesPerPixel = 4, Photometric = 5, Samples = inks };
            if (icc is not null)
            {
                page.ExtraFields.Add(TiffFieldSpec.Bytes(34675, 7, icc));
            }

            return page;
        }

        // Identification reports the profile of the inks; the loaded pixels are labeled with the destination
        var data = new TiffFileBuilder().AddPage(Page(profile)).Build();
        Assert.Equal(profile, Image.Identify(data).Metadata.IccProfile?.Data.ToArray());
        var destination = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        var conversion = new CmykConversionOptions { DestinationProfile = destination, Transform = new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual, BlackPointCompensation = false } };
        using (var image = Image.Load(data, new ImageDecodeOptions { CmykConversion = conversion }))
        {
            Assert.Equal(destination.Data.ToArray(), image.Metadata.IccProfile?.Data.ToArray());
            Assert.Empty(ReferenceIccTransform.Create(profile, destination.Data.Span, (int)IccRenderingIntent.Perceptual, blackPointCompensation: false).Compare(inks, GetBytes(image)));
        }

        // The pages of a document are converted like the first one, with the options of each decode
        using (var pages = ImageCollection.Load(new MemoryStream(data)))
        {
            Assert.Equal(ImageColorModel.Cmyk, pages[0].ColorModel);
            Assert.Throws<UnsupportedImageFeatureException>(() => pages[0].Decode());
            using var page = pages[0].Decode(new ImageDecodeOptions { CmykConversion = conversion });
            Assert.Equal(destination.Data.ToArray(), page.Metadata.IccProfile?.Data.ToArray());
        }

        // Without profile the assumed one is used, and nothing is assumed when there is none. A profile of another color
        // space does not describe inks: it is not the profile of the page
        var withoutProfile = new TiffFileBuilder().AddPage(Page(icc: null)).Build();
        var assumed = IccTestProfiles.CmykLabLutAToB();
        using (var image = Image.Load(withoutProfile, new ImageDecodeOptions { CmykConversion = new CmykConversionOptions { AssumedSourceProfile = assumed } }))
        {
            Assert.Empty(ReferenceIccTransform.Create(assumed.Data.Span, IccProfile.Srgb.Data.Span, intent: 1, blackPointCompensation: true).Compare(inks, GetBytes(image)));
        }

        var nothingAssumed = new ImageDecodeOptions { CmykConversion = new CmykConversionOptions { AssumedSourceProfile = null } };
        Assert.Equal("PhotometricInterpretation: 5 without color profile", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(withoutProfile, nothingAssumed)).Feature);
        var withRgbProfile = new TiffFileBuilder().AddPage(Page(IccProfile.Srgb.Data.ToArray())).Build();
        Assert.Null(Image.Identify(withRgbProfile).Metadata.IccProfile);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(withRgbProfile, nothingAssumed));

        // Other pages are not affected by the options
        using (var gray = Image.Load(Gray(bigEndian: false, bigTiff: false), InkOptions))
        {
            Assert.Equal(PixelFormat.Gray8, gray.PixelFormat);
            Assert.Null(gray.Metadata.IccProfile);
        }
    }

    [Fact]
    public void SeparationsThatAreNotCmykAreRejected()
    {
        byte[] Page(Action<TiffPageSpec> configure, int samplesPerPixel = 4, int bitsPerSample = 8)
        {
            var page = new TiffPageSpec { Width = 1, Height = 1, SamplesPerPixel = samplesPerPixel, BitsPerSample = bitsPerSample, Photometric = 5, Samples = new byte[samplesPerPixel * bitsPerSample / 8] };
            configure(page);
            return new TiffFileBuilder().AddPage(page).Build();
        }

        Assert.Equal("InkSet: 2", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(static page => page.ExtraFields.Add(TiffFieldSpec.Short(332, 2))), InkOptions)).Feature);
        Assert.Equal("NumberOfInks: 6", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(static page => page.ExtraFields.Add(TiffFieldSpec.Short(334, 6))), InkOptions)).Feature);
        Assert.Equal("DotRange", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(static page => page.ExtraFields.Add(TiffFieldSpec.Shorts(336, [10, 240]))), InkOptions)).Feature);
        Assert.Equal("BitsPerSample: 4", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(static _ => { }, bitsPerSample: 4), InkOptions)).Feature);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(static _ => { }, samplesPerPixel: 3), InkOptions));

        // The values that describe CMYK inks over the whole range of the samples are accepted
        var described = Page(static page =>
        {
            page.ExtraFields.Add(TiffFieldSpec.Short(332, 1));
            page.ExtraFields.Add(TiffFieldSpec.Short(334, 4));
            page.ExtraFields.Add(TiffFieldSpec.Shorts(336, [0, 255]));
        });
        Image.Load(described, InkOptions).Dispose();
    }

    [Fact]
    public void YCbCrSamplesAreConvertedWithTheFormulaOfTheSpecification()
    {
        // Full range, ITU-R BT.601 (the defaults), one chroma sample per pixel. With Y' = Y, Cb' = Cb - 128, Cr' = Cr - 128:
        // R = Y' + 1.402 Cr', B = Y' + 1.772 Cb', G = (Y' - 0.114 B - 0.299 R) / 0.587, rounded to nearest and clamped
        var page = new TiffPageSpec
        {
            Width = 8,
            Height = 1,
            SamplesPerPixel = 3,
            Photometric = 6,
            Samples = [255, 128, 128, 0, 128, 128, 76, 85, 255, 150, 44, 21, 29, 255, 107, 128, 128, 128, 200, 100, 150, 60, 160, 90],
        };

        page.ExtraFields.Add(TiffFieldSpec.Shorts(530, [1, 1]));
        var data = new TiffFileBuilder().AddPage(page).Build();
        var info = Image.Identify(data);
        Assert.Equal(ImageColorModel.YCbCr, info.ColorModel);
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        Assert.Equal(
            [new Rgb24(255, 255, 255), new Rgb24(0, 0, 0), new Rgb24(254, 0, 0), new Rgb24(0, 255, 1), new Rgb24(0, 0, 254), new Rgb24(128, 128, 128), new Rgb24(231, 194, 150), new Rgb24(7, 76, 117)],
            Decode<Rgb24>(data));

        // The same samples with the luma range 16 to 235 and the chroma range 128 to 240, then with ITU-R BT.709
        var video = Clone(page);
        video.ExtraFields.Add(new TiffFieldSpec(532, 5, [16, 1, 235, 1, 128, 1, 240, 1, 128, 1, 240, 1]));
        Assert.Equal(
            [new Rgb24(255, 255, 255), new Rgb24(0, 0, 0), new Rgb24(255, 0, 0), new Rgb24(0, 255, 0), new Rgb24(0, 0, 255), new Rgb24(130, 130, 130), new Rgb24(249, 207, 158), new Rgb24(0, 70, 116)],
            Decode<Rgb24>(new TiffFileBuilder().AddPage(video).Build()));
        var hdtv = Clone(page);
        hdtv.ExtraFields.Add(new TiffFieldSpec(529, 5, [2126, 10000, 7152, 10000, 722, 10000]));
        Assert.Equal(
            [new Rgb24(255, 255, 255), new Rgb24(0, 0, 0), new Rgb24(255, 25, 0), new Rgb24(0, 216, 0), new Rgb24(0, 15, 255), new Rgb24(128, 128, 128), new Rgb24(235, 195, 148), new Rgb24(0, 72, 119)],
            Decode<Rgb24>(new TiffFileBuilder().AddPage(hdtv).Build()));

        static TiffPageSpec Clone(TiffPageSpec source)
        {
            var copy = new TiffPageSpec { Width = source.Width, Height = source.Height, SamplesPerPixel = 3, Photometric = 6, Samples = source.Samples };
            foreach (var field in source.ExtraFields)
            {
                copy.ExtraFields.Add(field);
            }

            return copy;
        }
    }

    [Theory]
    [InlineData(1, 1, 1, false)]
    [InlineData(2, 1, 5, false)]
    [InlineData(2, 2, 32773, false)]
    [InlineData(4, 1, 8, false)]
    [InlineData(4, 4, 5, false)]
    [InlineData(1, 2, 1, false)]
    [InlineData(2, 2, 5, true)]
    [InlineData(4, 2, 1, true)]
    public void SubsampledYCbCrDataUnitsReplicateTheirChromaSamples(int horizontal, int vertical, int compression, bool tiled)
    {
        // A data unit is h x v luma samples followed by one Cb and one Cr sample; a strip or a tile stores whole units,
        // so the samples of the units of the right and bottom edges that are outside the page are padding
        const int Width = 21;
        const int Height = 19;
        var blockWidth = tiled ? 16 : Width;
        var blockHeight = tiled ? 16 : 8;
        var blocks = new List<byte[]>();
        var expected = new Rgb24[Width * Height];
        var seed = 0;
        for (var top = 0; top < Height; top += blockHeight)
        {
            for (var left = 0; left < Width; left += blockWidth)
            {
                var rows = tiled ? blockHeight : Math.Min(blockHeight, Height - top);
                var unitsAcross = (blockWidth + horizontal - 1) / horizontal;
                var unitsDown = (rows + vertical - 1) / vertical;
                var unitLength = (horizontal * vertical) + 2;
                var units = new byte[unitsAcross * unitsDown * unitLength];
                Noise(units, seed: 100 + seed++);
                for (var y = 0; y < rows && top + y < Height; y++)
                {
                    for (var x = 0; x < blockWidth && left + x < Width; x++)
                    {
                        var unit = units.AsSpan((((y / vertical) * unitsAcross) + (x / horizontal)) * unitLength, unitLength);
                        expected[((top + y) * Width) + left + x] = ConvertYCbCr(unit[((y % vertical) * horizontal) + (x % horizontal)], unit[^2], unit[^1]);
                    }
                }

                blocks.Add(compression switch
                {
                    5 => TiffReferenceCodecs.EncodeLzw(units),
                    8 => TiffFileBuilder.Deflate(units),
                    32773 => TiffReferenceCodecs.EncodePackBits(units, unitsAcross * unitLength),
                    _ => units,
                });
            }
        }

        var page = new TiffPageSpec
        {
            Width = Width,
            Height = Height,
            SamplesPerPixel = 3,
            Photometric = 6,
            Compression = compression,
            RowsPerStrip = blockHeight,
            TileWidth = tiled ? 16 : null,
            TileLength = tiled ? 16 : null,
            EncodedBlocks = [.. blocks],
            Samples = [],
        };

        page.ExtraFields.Add(TiffFieldSpec.Shorts(530, [(ushort)horizontal, (ushort)vertical]));
        Assert.Equal(expected, Decode<Rgb24>(new TiffFileBuilder().AddPage(page).Build()));
    }

    [Fact]
    public void MalformedOrUnsupportedYCbCrPagesAreReported()
    {
        byte[] Page(Action<TiffPageSpec> configure, int bitsPerSample = 8, int rowsPerStrip = 2, int samplesPerPixel = 3)
        {
            var page = new TiffPageSpec { Width = 2, Height = 4, SamplesPerPixel = samplesPerPixel, BitsPerSample = bitsPerSample, Photometric = 6, RowsPerStrip = rowsPerStrip, Samples = new byte[2 * 4 * samplesPerPixel * bitsPerSample / 8] };
            configure(page);
            return new TiffFileBuilder().AddPage(page).Build();
        }

        // Subsampling factors other than 1, 2 and 4, a strip that ends in the middle of a data unit, a white equal to its
        // black, a coefficient of zero
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(static page => page.ExtraFields.Add(TiffFieldSpec.Shorts(530, [3, 1])))));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(static page => page.ExtraFields.Add(TiffFieldSpec.Shorts(530, [2, 2])), rowsPerStrip: 3)));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(static page => page.ExtraFields.Add(new TiffFieldSpec(532, 5, [0, 1, 255, 1, 128, 1, 128, 1, 128, 1, 255, 1])))));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Page(static page => page.ExtraFields.Add(new TiffFieldSpec(529, 5, [299, 1000, 0, 1000, 114, 1000])))));

        // The default subsampling is 2 x 2: four rows of two pixels are two data units of six bytes each
        var units = new TiffPageSpec { Width = 2, Height = 4, SamplesPerPixel = 3, Photometric = 6, EncodedBlocks = [[255, 255, 255, 255, 128, 128, 0, 0, 0, 0, 128, 128]], Samples = [] };
        Assert.Equal([.. Enumerable.Repeat(new Rgb24(255, 255, 255), 4), .. Enumerable.Repeat(new Rgb24(0, 0, 0), 4)], Decode<Rgb24>(new TiffFileBuilder().AddPage(units).Build()));
        var tooShort = new TiffPageSpec { Width = 2, Height = 4, SamplesPerPixel = 3, Photometric = 6, EncodedBlocks = [new byte[11]], Samples = [] };
        Assert.Throws<InvalidImageContentException>(() => Image.Load(new TiffFileBuilder().AddPage(tooShort).Build()));

        // 16-bit samples, an alpha channel, the predictor
        Assert.Equal("BitsPerSample: 16", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(static _ => { }, bitsPerSample: 16))).Feature);
        Assert.Equal("ExtraSamples: alpha with this sample layout", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(static page => page.ExtraFields.Add(TiffFieldSpec.Short(338, 2)), samplesPerPixel: 4))).Feature);
        Assert.Equal("Predictor: 2 with this sample layout or compression", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Page(static page => page.ExtraFields.Add(TiffFieldSpec.Short(317, 2))))).Feature);
    }

    [Fact]
    public void MutatedPagesFailOnlyWithDocumentedErrorsAndLeakNothing()
    {
        // One page per decoder, a few bytes changed or the end cut off: whatever the tags and the compressed data
        // become, a load succeeds or fails with an image exception, and returns every buffer it rented
        (int H, int V)[] sampling = [(2, 2), (1, 1), (1, 1)];
        var (jpegTables, jpegStrip) = SplitJpegTables(JpegTestImage.CreateRandom(16, 16, sampling, seed: 71).Encode());
        var samples = new byte[20 * 12 * 8];
        Noise(samples, seed: 70);
        samples.AsSpan(100, 300).Fill(7);
        var colorMap = new ushort[48];
        for (var i = 0; i < colorMap.Length; i++)
        {
            colorMap[i] = (ushort)(i * 257);
        }

        var pages = new List<TiffPageSpec>
        {
            new() { Width = 20, Height = 12, Compression = 5, Predictor = 2, RowsPerStrip = 5, Samples = samples[..240] },
            new() { Width = 20, Height = 12, SamplesPerPixel = 3, Photometric = 2, Compression = 32773, TileWidth = 16, TileLength = 16, Samples = samples[..720] },
            new() { Width = 20, Height = 12, BitsPerSample = 16, SamplesPerPixel = 4, Photometric = 5, Compression = 5, Samples = samples[..1920] },
            new() { Width = 20, Height = 12, SamplesPerPixel = 5, Photometric = 5, ExtraSamples = [2], Compression = 8, Samples = samples[..1200] },
            new() { Width = 20, Height = 12, SamplesPerPixel = 3, Photometric = 6, RowsPerStrip = 4, EncodedBlocks = [samples[..120], samples[120..240], samples[240..360]], Samples = [] },
            new() { Width = 20, Height = 12, BitsPerSample = 4, Photometric = 3, Compression = 32773, Samples = samples[..120] },
            new() { Width = 53, Height = 12, BitsPerSample = 1, Photometric = 1, Compression = 2, EncodedBlocks = [Convert.FromHexString(LibTiffFaxStripCases[0].Strip)], Samples = [] },
            new() { Width = 53, Height = 12, BitsPerSample = 1, Photometric = 1, Compression = 3, EncodedBlocks = [Convert.FromHexString(LibTiffFaxStripCases[3].Strip)], Samples = [] },
            new() { Width = 53, Height = 12, BitsPerSample = 1, Photometric = 1, Compression = 4, EncodedBlocks = [Convert.FromHexString(LibTiffFaxStripCases[4].Strip)], Samples = [] },
            new() { Width = 16, Height = 16, SamplesPerPixel = 3, Photometric = 6, Compression = 7, EncodedBlocks = [jpegStrip], Samples = [] },
            new() { Width = 16, Height = 16, SamplesPerPixel = 3, BitsPerSample = 12, Photometric = 6, Compression = 7, EncodedBlocks = [JpegTestImage.CreateRandom(16, 16, sampling, seed: 72, precision: 12).Encode()], Samples = [] },
            new() { Width = 16, Height = 16, SamplesPerPixel = 3, BitsPerSample = 16, Photometric = 2, Compression = 7, TileWidth = 16, TileLength = 16, EncodedBlocks = [JpegLosslessTestImage.CreateRandom(16, 16, [(1, 1), (1, 1), (1, 1)], 16, seed: 73).Encode(new JpegLosslessTestOptions { Predictor = 4, RestartRows = 4 })], Samples = [] },
        };

        pages[5].ExtraFields.Add(TiffFieldSpec.Shorts(320, colorMap));
        pages[7].ExtraFields.Add(TiffFieldSpec.Long(292, 5));
        pages[9].ExtraFields.Add(TiffFieldSpec.Bytes(347, 7, jpegTables));

        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 4096, MaxHeight = 4096, MaxFramePixels = 1 << 20, MaxLiveAllocationBytes = 64L << 20 } };
        var options = new ImageDecodeOptions { Configuration = configuration, CmykConversion = CmykConversionOptions.Default };
        var state = 99991u;
        uint Next(int exclusiveMaximum)
        {
            state = (state * 1664525) + 1013904223;
            return (uint)(((ulong)(state >> 8) * (ulong)exclusiveMaximum) >> 24);
        }

        var decoded = 0;
        for (var index = 0; index < pages.Count; index++)
        {
            var seed = new TiffFileBuilder { BigTiff = index % 4 == 3, BigEndian = index % 3 == 2 && pages[index].BitsPerSample != 16 }.AddPage(pages[index]).Build();
            Image.Load(seed, options).Dispose();
            using var audit = new PoolAudit();
            for (var iteration = 0; iteration < 300; iteration++)
            {
                var mutated = iteration % 10 == 9 ? seed[..(int)Next(seed.Length)] : (byte[])seed.Clone();
                for (var change = Next(4); mutated.Length > 0 && change < 4; change++)
                {
                    mutated[Next(mutated.Length)] = (byte)Next(256);
                }

                try
                {
                    Image.Identify(mutated, new ImageIdentifyOptions { Configuration = configuration, Mode = ImageIdentifyMode.FullScan });
                    Image.Load(mutated, options).Dispose();
                    using var document = ImageCollection.Load(mutated, configuration);
                    document[document.Count - 1].Decode<Rgba64>(options).Dispose();
                    decoded++;
                }
                catch (ImageException)
                {
                }
            }

            audit.AssertClean(string.Create(CultureInfo.InvariantCulture, $"mutations of the page {index}"));
        }

        // The mutations reach the decoders, not only the validation of the tags
        Assert.True(decoded > 100, string.Create(CultureInfo.InvariantCulture, $"Only {decoded} mutated pages decoded."));
    }

    private static readonly ImageDecodeOptions InkOptions = new() { CmykConversion = CmykConversionOptions.Default };

    // The independent decimal reference of the default conversion of inks: the built-in CMYK profile to sRGB, relative
    // colorimetric with black point compensation
    private static readonly ReferenceIccTransform InkReference = ReferenceIccTransform.Create(IccProfile.Cgats001CompatibleCmyk.Data.Span, IccProfile.Srgb.Data.Span, intent: 1, blackPointCompensation: true);

    private static Gray8[] Grays(params byte[] values) => [.. values.Select(static value => new Gray8(value))];

    /// <summary>Fills a buffer with a fixed pseudo-random sequence (a linear congruential generator).</summary>
    private static void Noise(Span<byte> destination, int seed)
    {
        var state = (uint)((seed * 2654435761L) + 12345);
        for (var i = 0; i < destination.Length; i++)
        {
            state = (state * 1664525) + 1013904223;
            destination[i] = (byte)(state >> 24);
        }
    }

    /// <summary>Builds a sequence of bytes in which no pair of consecutive bytes appears twice.</summary>
    internal static byte[] SequenceWithoutRepeatedPair(int length)
    {
        var used = new HashSet<(byte, byte)>();
        var sequence = new List<byte> { 0 };
        while (sequence.Count < length)
        {
            var previous = sequence[^1];
            for (var candidate = 0; candidate < 256; candidate++)
            {
                var value = (byte)((candidate * 37) + (sequence.Count * 11));
                if (used.Add((previous, value)))
                {
                    sequence.Add(value);
                    break;
                }
            }
        }

        return [.. sequence];
    }

    private static void AppendEndOfLine(StringBuilder bits, bool fillBits)
    {
        while (fillBits && (bits.Length + FaxEndOfLine.Length) % 8 != 0)
        {
            bits.Append('0');
        }

        bits.Append(FaxEndOfLine);
    }

    /// <summary>Decodes a strip that codes <see cref="FaxRows"/> and compares the pixels: <c>WhiteIsZero</c> displays a white run as white.</summary>
    private static void AssertFaxRows(byte[] strip, int compression, uint options = 0, int fillOrder = 1)
    {
        var page = new TiffPageSpec { Width = 8, Height = FaxRows.Length, BitsPerSample = 1, Photometric = 0, Compression = compression, FillOrder = fillOrder == 1 ? null : fillOrder, EncodedBlocks = [strip], Samples = [] };
        if (compression == 3)
        {
            page.ExtraFields.Add(TiffFieldSpec.Long(292, options));
        }

        var expected = FaxRows.SelectMany(static row => row.Select(static pixel => new Gray8(pixel == '#' ? (byte)0 : (byte)255))).ToArray();
        Assert.Equal(expected, Decode<Gray8>(new TiffFileBuilder().AddPage(page).Build()));
    }

    /// <summary>The 53x12 pattern of <see cref="LibTiffFaxStripCases"/>: a white row, a black row, a row of alternating pixels, then rows of irregular runs.</summary>
    private static bool IsFaxPatternBlack(int x, int y) => y switch
    {
        0 => false,
        1 => true,
        2 => x % 2 == 0,
        _ => (((x * 7) + (y * 3)) % 11 < 4) != ((x / 9) % 2 == y % 2),
    };

    /// <summary>
    /// Splits a JPEG datastream as TIFF Technical Note 2 stores it: the quantization and Huffman tables in a datastream
    /// of their own (the <c>JPEGTables</c> tag), and the rest as an abbreviated datastream.
    /// </summary>
    private static (byte[] Tables, byte[] Stream) SplitJpegTables(byte[] jpeg)
    {
        var tables = new List<byte> { 0xFF, 0xD8 };
        var stream = new List<byte> { 0xFF, 0xD8 };
        var position = 2;
        while (jpeg[position + 1] != 0xDA)
        {
            var length = 2 + ((jpeg[position + 2] << 8) | jpeg[position + 3]);
            (jpeg[position + 1] is 0xDB or 0xC4 ? tables : stream).AddRange(jpeg.AsSpan(position, length));
            position += length;
        }

        tables.AddRange([0xFF, 0xD9]);
        stream.AddRange(jpeg.AsSpan(position));
        return ([.. tables], [.. stream]);
    }

    /// <summary>
    /// Converts one full-range ITU-R BT.601 YCbCr color with the formula of TIFF 6.0 (section 21), in decimal arithmetic:
    /// an implementation that shares nothing with the library.
    /// </summary>
    private static Rgb24 ConvertYCbCr(byte luma, byte blue, byte red)
    {
        var r = luma + ((red - 128) * (2 - (2 * 0.299m)));
        var b = luma + ((blue - 128) * (2 - (2 * 0.114m)));
        var g = (luma - (0.114m * b) - (0.299m * r)) / 0.587m;
        return new Rgb24(Round(r), Round(g), Round(b));

        static byte Round(decimal value) => (byte)Math.Clamp(Math.Floor(value + 0.5m), 0, 255);
    }

    private static byte[] GetBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static byte[] Gray(bool bigEndian, bool bigTiff) => new TiffFileBuilder { BigEndian = bigEndian, BigTiff = bigTiff, Gap = 16 }
        .AddPage(new TiffPageSpec { Width = 3, Height = 2, Samples = [10, 20, 30, 40, 50, 60] })
        .Build();

    private static byte[] TwoPages() => new TiffFileBuilder()
        .AddPage(new TiffPageSpec { Width = 2, Height = 1, Samples = [7, 9] })
        .AddPage(new TiffPageSpec { Width = 3, Height = 2, SamplesPerPixel = 3, Photometric = 2, Samples = [.. Enumerable.Range(1, 18).Select(static value => (byte)value)] })
        .Build();

    private static TPixel[] Decode<TPixel>(byte[] data, ImageDecodeOptions? options = null)
        where TPixel : unmanaged
    {
        using var image = Image.Load<TPixel>(data, options ?? ImageDecodeOptions.Default);
        var pixels = new TPixel[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        return pixels;
    }

    /// <summary>A seekable stream that counts the bytes actually read, to show that one page is read without the others.</summary>
    private sealed class CountingStream(byte[] data) : MemoryStream(data, writable: false)
    {
        public long BytesRead { get; private set; }

        public override int Read(Span<byte> buffer)
        {
            var read = base.Read(buffer);
            BytesRead += read;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
    }
}
