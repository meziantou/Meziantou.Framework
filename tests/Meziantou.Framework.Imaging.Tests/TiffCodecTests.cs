using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// TIFF and BigTIFF decoding and encoding through the public APIs, on files assembled byte by byte by
/// <see cref="TiffFileBuilder"/> with literal expected pixels: both byte orders, both offset sizes, strips and tiles,
/// 8- and 16-bit samples, the supported photometric interpretations and compressions, the horizontal predictor,
/// metadata, the rejections of the unsupported variants, and the malformed structures that must not turn into unbounded
/// work.
/// </summary>
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
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TiffEncoder { Compression = (TiffCompression)42 });
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
    [InlineData(5, "LZW")]
    [InlineData(7, "JPEG")]
    [InlineData(32773, "PackBits")]
    [InlineData(4, "CCITT Group 4 fax")]
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
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    public void UnsupportedPhotometricInterpretationsAreRejected(int photometric)
    {
        var data = new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            Photometric = photometric,
            Samples = [1, 2],
        }).Build();

        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
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

        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 2,
            Height = 1,
            SampleFormat = 3,
            Samples = [1, 2],
        }).Build()));

        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(new TiffFileBuilder().AddPage(new TiffPageSpec
        {
            Width = 8,
            Height = 1,
            BitsPerSample = 1,
            Samples = [0b1010_1010],
        }).Build()));
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

    private static byte[] Gray(bool bigEndian, bool bigTiff) => new TiffFileBuilder { BigEndian = bigEndian, BigTiff = bigTiff, Gap = 16 }
        .AddPage(new TiffPageSpec { Width = 3, Height = 2, Samples = [10, 20, 30, 40, 50, 60] })
        .Build();

    private static byte[] TwoPages() => new TiffFileBuilder()
        .AddPage(new TiffPageSpec { Width = 2, Height = 1, Samples = [7, 9] })
        .AddPage(new TiffPageSpec { Width = 3, Height = 2, SamplesPerPixel = 3, Photometric = 2, Samples = [.. Enumerable.Range(1, 18).Select(static value => (byte)value)] })
        .Build();

    private static TPixel[] Decode<TPixel>(byte[] data)
        where TPixel : unmanaged
    {
        using var image = Image.Load<TPixel>(data);
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
