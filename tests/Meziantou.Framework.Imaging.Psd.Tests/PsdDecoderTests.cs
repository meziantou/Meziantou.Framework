using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Psd;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// PSD decoding of files assembled by <see cref="PsdFileBuilder"/> (Adobe Photoshop File Formats Specification): the color
/// modes and depths of the composite, raw and PackBits data, the transparency channel and its matte, PSB files, the image
/// resources, the rejections, the limits and the readers. The expected pixels are written out by hand or read by
/// <see cref="ReferencePsd"/>, an independent reader.
/// </summary>
public sealed class PsdDecoderTests
{
    private const int Bitmap = 0;
    private const int Grayscale = 1;
    private const int Indexed = 2;
    private const int Rgb = 3;
    private const int Cmyk = 4;
    private const int Multichannel = 7;
    private const int Duotone = 8;
    private const int Lab = 9;

    // 3x2 RGB pixels
    private static readonly byte[] RgbPixels =
    [
        255, 0, 0, 0, 255, 0, 0, 0, 255,
        10, 20, 30, 200, 200, 200, 0, 0, 0,
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RgbCompositeLoadsExactly(int compression)
    {
        var data = new PsdFileBuilder(3, 2, 8, Rgb, PsdFileBuilder.Planes8(RgbPixels, 3)) { Compression = compression }.Build();
        using var image = Image.Load(data);
        var rgb = Assert.IsType<Image<Rgb24>>(image);
        Assert.Equal(new Rgb24(255, 0, 0), rgb.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(10, 20, 30), rgb.Frames[0][0, 1]);
        Assert.Equal(RgbPixels, GetBytes(image));
        Assert.Equal(ImageFormat.Psd, image.Metadata.SourceFormat);

        var info = Image.Identify(data);
        Assert.Equal(ImageFormat.Psd, info.Format);
        Assert.Equal(new Size(3, 2), info.Size);
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        Assert.Equal(ImageColorModel.Rgb, info.ColorModel);
        Assert.Equal(8, info.BitsPerComponent);
        Assert.Equal(false, info.MayHaveTransparency);
        Assert.Equal(ImageFormat.Psd, Image.DetectFormat(data));
        Assert.Equal(ImageFormat.Psd, Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).Format);
    }

    [Fact]
    public void SixteenBitSamplesAreBigEndian()
    {
        ushort[] samples = [0x0102, 0xFFFF, 0x0000, 0x8000, 0x1234, 0xABCD];
        var data = new PsdFileBuilder(2, 1, 16, Rgb, PsdFileBuilder.Planes16(samples, 3)) { Compression = 1 }.Build();
        using var image = Image.Load<Rgba64>(data);
        Assert.Equal(new Rgba64(0x0102, 0xFFFF, 0x0000, 0xFFFF), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba64(0x8000, 0x1234, 0xABCD, 0xFFFF), image.Frames[0][1, 0]);
        Assert.Equal(PixelFormat.Rgb48, Image.Identify(data).PixelFormat);
    }

    [Fact]
    public void GrayscaleAndDuotoneLoadAsGray()
    {
        byte[] gray = [0, 64, 128, 255];
        foreach (var mode in new[] { Grayscale, Duotone })
        {
            var builder = new PsdFileBuilder(2, 2, 8, mode, [gray]) { Compression = 1 };
            if (mode == Duotone)
            {
                // The duotone specification is not used
                builder.ColorModeData = new byte[] { 1, 2, 3, 4, 5 };
            }

            var data = builder.Build();
            using var image = Image.Load(data);
            Assert.IsType<Image<Gray8>>(image);
            Assert.Equal(gray, GetBytes(image));
            Assert.Equal(ImageColorModel.Grayscale, Image.Identify(data).ColorModel);
        }
    }

    [Fact]
    public void BitmapSamplesOfOneAreBlack()
    {
        // Rows of 10 pixels: two bytes, the last 6 bits padding (set to 1 here, as ImageMagick writes them)
        byte[] plane = [0b1010_0000, 0b0111_1111, 0b0000_0000, 0b1011_1111];
        var data = new PsdFileBuilder(10, 2, 1, Bitmap, [plane]).Build();
        using var image = Image.Load<Gray8>(data);
        byte[] expected =
        [
            0, 255, 0, 255, 255, 255, 255, 255, 255, 0,
            255, 255, 255, 255, 255, 255, 255, 255, 0, 255,
        ];
        Assert.Equal(expected, GetBytes(image));
        var info = Image.Identify(data);
        Assert.Equal(PixelFormat.Gray8, info.PixelFormat);
        Assert.Equal(1, info.BitsPerComponent);
    }

    [Fact]
    public void IndexesAreExpandedAndTheTransparentIndexIsTransparent()
    {
        var table = new byte[768];
        for (var i = 0; i < 256; i++)
        {
            table[i] = (byte)i;
            table[256 + i] = (byte)(255 - i);
            table[512 + i] = (byte)(i / 2);
        }

        byte[] indexes = [0, 7, 255, 7];
        var builder = new PsdFileBuilder(2, 2, 8, Indexed, [indexes]) { ColorModeData = table, Compression = 1 };
        using (var image = Image.Load(builder.Build()))
        {
            Assert.Equal(new Rgb24(7, 248, 3), Assert.IsType<Image<Rgb24>>(image).Frames[0][1, 0]);
        }

        builder.AddResource(1047, PsdFileBuilder.U16(7));
        var data = builder.Build();
        using var transparent = Image.Load(data);
        var rgba = Assert.IsType<Image<Rgba32>>(transparent);
        Assert.Equal(new Rgba32(0, 255, 0, 255), rgba.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(7, 248, 3, 0), rgba.Frames[0][1, 0]);
        Assert.Equal(new Rgba32(255, 0, 127, 255), rgba.Frames[0][0, 1]);
        var info = Image.Identify(data);
        Assert.Equal(ImageColorModel.Indexed, info.ColorModel);
        Assert.Equal(true, info.MayHaveTransparency);
    }

    [Fact]
    public void ColorsOfTheTransparencyChannelAreUnmattedFromWhite()
    {
        // A color c of alpha a is stored c × a / 255 + 255 - a: 200 at 128 is 227, 100 at 255 is 100, anything at 0 is 255 (kept)
        byte[] stored =
        [
            227, 227, 227, 128,
            100, 50, 0, 255,
            255, 255, 255, 0,
            10, 20, 30, 64,
        ];
        var builder = new PsdFileBuilder(4, 1, 8, Rgb, PsdFileBuilder.Planes8(stored, 4)) { LayerCount = -1, Compression = 1 };
        var data = builder.Build();
        using var image = Image.Load<Rgba32>(data);
        Assert.Equal(new Rgba32(199, 199, 199, 128), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(100, 50, 0, 255), image.Frames[0][1, 0]);
        Assert.Equal(new Rgba32(255, 255, 255, 0), image.Frames[0][2, 0]);

        // A stored sample below the white the matte adds is clipped to 0
        Assert.Equal(new Rgba32(0, 0, 0, 64), image.Frames[0][3, 0]);
        Assert.Equal(ReferencePsd.Read(data).GetRgba().Select(v => (byte)v).ToArray(), GetBytes(image));
        var info = Image.Identify(data);
        Assert.Equal(PixelFormat.Rgba32, info.PixelFormat);
        Assert.Equal(ImageColorModel.Rgba, info.ColorModel);
        Assert.Equal(true, info.MayHaveTransparency);
    }

    [Fact]
    public void ExtraChannelsAreNotTransparencyWithoutANegativeLayerCount()
    {
        // The fourth channel is a saved selection: the layer count is positive, or there is no layer section
        byte[] stored = [227, 227, 227, 128, 10, 20, 30, 0];
        foreach (short? count in new short?[] { null, 1 })
        {
            var data = new PsdFileBuilder(2, 1, 8, Rgb, PsdFileBuilder.Planes8(stored, 4)) { LayerCount = count }.Build();
            using var image = Image.Load(data);
            var rgb = Assert.IsType<Image<Rgb24>>(image);
            Assert.Equal(new Rgb24(227, 227, 227), rgb.Frames[0][0, 0]);
            Assert.Equal(new Rgb24(10, 20, 30), rgb.Frames[0][1, 0]);
            Assert.Equal(false, Image.Identify(data).MayHaveTransparency);
        }
    }

    [Theory]
    [InlineData("Lr16")]
    [InlineData("Lr32")]
    [InlineData("Layr")]
    public void TheLayerCountOfATaggedBlockGivesTheTransparency(string key)
    {
        // 16-bit samples: 0xBFFF at alpha 0x8000 is un-matted to 0xFFFF × (0xBFFF − 0xFFFF + 0x8000) / 0x8000 = 32767.5, rounded up
        ushort[] stored = [0xBFFF, 0x8000, 0x1234, 0xFFFF];
        var data = new PsdFileBuilder(2, 1, 16, Grayscale, PsdFileBuilder.Planes16(stored, 2)) { LayerCount = -1, LayerBlockKey = key, Compression = 1 }.Build();
        using var image = Image.Load(data);
        var gray = Assert.IsType<Image<La32>>(image);
        Assert.Equal(new La32(0x8000, 0x8000), gray.Frames[0][0, 0]);
        Assert.Equal(new La32(0x1234, 0xFFFF), gray.Frames[0][1, 0]);
        Assert.Equal(ImageColorModel.GrayscaleAlpha, Image.Identify(data).ColorModel);
        Assert.Equal(ReferencePsd.Read(data).GetRgba(), GetRgba64(image));
    }

    [Fact]
    public void FloatingPointSamplesAreLinearLight()
    {
        float[] gray = [0f, 0.5f, 1f, 7.25f];
        var data = new PsdFileBuilder(2, 2, 32, Grayscale, PsdFileBuilder.Planes32(gray, 1)) { Compression = 1 }.Build();
        using var image = Image.Load(data);
        // A grayscale document loads as floating-point gray levels
        var floats = Assert.IsType<Image<GrayFloat>>(image);
        Assert.Equal(new GrayFloat(0.5f), floats.Frames[0][1, 0]);
        Assert.Equal(new GrayFloat(7.25f), floats.Frames[0][1, 1]);
        Assert.Equal(ColorTransferFunction.Linear, image.Metadata.TransferFunction);
        var info = Image.Identify(data);
        Assert.Equal(PixelFormat.GrayFloat, info.PixelFormat);
        using var colors = Image.Load<RgbaFloat>(data);
        Assert.Equal(new RgbaFloat(7.25f, 7.25f, 7.25f, 1f), colors.Frames[0][1, 1]);
        Assert.Equal(32, info.BitsPerComponent);

        // Values above 1 are rejected by a conversion to integers, as for every floating-point image
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Rgba32>(data).Dispose());
    }

    [Fact]
    public void FloatingPointColorsAreUnmattedWithoutClipping()
    {
        // 0.5 at alpha 0.5 is stored 0.75; 0.25 at 0.5 is 0.625; at alpha 0 the stored colors are kept
        float[] stored = [0.75f, 0.625f, 1f, 0.5f, 3f, 3f, 3f, 1f, 1f, 1f, 1f, 0f];
        var data = new PsdFileBuilder(3, 1, 32, Rgb, PsdFileBuilder.Planes32(stored, 4)) { LayerCount = -1, LayerBlockKey = "Lr32" }.Build();
        using var image = Image.Load<RgbaFloat>(data);
        Assert.Equal(new RgbaFloat(0.5f, 0.25f, 1f, 0.5f), image.Frames[0][0, 0]);
        Assert.Equal(new RgbaFloat(3f, 3f, 3f, 1f), image.Frames[0][1, 0]);
        Assert.Equal(new RgbaFloat(1f, 1f, 1f, 0f), image.Frames[0][2, 0]);
        Assert.Equal(ReferencePsd.Read(data).GetRgbaFloat(), GetFloats(image));
    }

    [Fact]
    public void LargeDocumentsHaveWiderLengths()
    {
        var builder = new PsdFileBuilder(3, 2, 8, Rgb, PsdFileBuilder.Planes8(RgbPixels, 3)) { Version = 2, Compression = 1, LayerCount = 0, LayerBlockKey = "Lr16" };
        var data = builder.Build();
        using var image = Image.Load(data);
        Assert.Equal(RgbPixels, GetBytes(image));

        // PSB files may be wider than 30,000 pixels; PSD files may not
        var wide = new PsdFileBuilder(30_001, 1, 8, Grayscale, [new byte[30_001]]) { Version = 2, Compression = 1 }.Build();
        Assert.Equal(new Size(30_001, 1), Image.Identify(wide, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 300_000 } } }).Size);
        var narrow = new PsdFileBuilder(30_001, 1, 8, Grayscale, [new byte[30_001]]) { Compression = 1 }.Build();
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(narrow, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 300_000 } } }));
    }

    [Fact]
    public void ImageResourcesAreReadAsMetadata()
    {
        var icc = IccProfile.Srgb.Data.ToArray();
        var xmp = Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>");
        byte[] exif = [(byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0];
        var builder = new PsdFileBuilder(3, 2, 8, Rgb, PsdFileBuilder.Planes8(RgbPixels, 3)) { Compression = 1 };
        builder.AddResource(1036, new byte[11], "thumbnail")
            .AddResolution(300 << 16, (150 << 16) + (1 << 15))
            .AddResource(1039, icc)
            .AddResource(1058, exif)
            .AddResource(1060, xmp)
            .AddVersionInfo(hasRealMergedData: true);
        var data = builder.Build();
        using var image = Image.Load(data);
        Assert.Equal(new ImageResolution(300, 150.5), image.Metadata.Resolution);
        Assert.Equal(icc, image.Metadata.IccProfile!.Data.ToArray());
        Assert.Equal(exif, image.Metadata.ExifProfile!.Data.ToArray());
        Assert.Equal(xmp, image.Metadata.XmpProfile!.Data.ToArray());
        Assert.Equal(RgbPixels, GetBytes(image));

        // A profile of another color space than the samples is not adopted
        var gray = new PsdFileBuilder(1, 1, 8, Grayscale, [[9]]).AddResource(1039, icc).Build();
        Assert.Null(Image.Identify(gray).Metadata.IccProfile);
    }

    [Fact]
    public void ImageResourcesAreChargedToTheMetadataLimit()
    {
        var builder = new PsdFileBuilder(3, 2, 8, Rgb, PsdFileBuilder.Planes8(RgbPixels, 3)).AddResource(1060, new byte[1001]).AddResource(1036, new byte[5000]);
        var data = builder.Build();

        // The data of the resources the decoder reads are charged, not the ones it skips
        Assert.NotNull(Image.Identify(data, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = 1001 } } }));
        var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = 1000 } } }));
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, exception.Kind);
    }

    [Fact]
    public void CmykSamplesLoadAsInksOrWithAConversion()
    {
        // Stored inverted: 255 is no ink
        byte[] stored = [255, 255, 255, 255, 0, 255, 255, 255, 255, 255, 255, 0];
        var data = new PsdFileBuilder(3, 1, 8, Cmyk, PsdFileBuilder.Planes8(stored, 4)) { Compression = 1 }.Build();
        Assert.Equal("CMYK samples without color conversion", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);
        Assert.Equal(ImageColorModel.Cmyk, Image.Identify(data).ColorModel);

        using (var inks = Image.Load<Cmyk32>(data))
        {
            Assert.Equal(new Cmyk32(0, 0, 0, 0), inks.Frames[0][0, 0]);
            Assert.Equal(new Cmyk32(255, 0, 0, 0), inks.Frames[0][1, 0]);
            Assert.Equal(new Cmyk32(0, 0, 0, 255), inks.Frames[0][2, 0]);
        }

        var options = new ImageDecodeOptions { CmykConversion = CmykConversionOptions.Default };
        using var converted = Image.Load(data, options);
        var rgb = Assert.IsType<Image<Rgb24>>(converted);
        Assert.Equal(CmykConversionOptions.Default.DestinationProfile, converted.Metadata.IccProfile);

        // No ink is white, and black ink is darker than cyan ink
        Assert.Equal(new Rgb24(255, 255, 255), rgb.Frames[0][0, 0]);
        Assert.True(rgb.Frames[0][1, 0].R < 100);
        Assert.True(rgb.Frames[0][2, 0].G < 60);

        // A CMYK image with transparency is only loaded with a conversion
        var transparent = new PsdFileBuilder(1, 1, 8, Cmyk, [[255], [255], [255], [255], [128]]) { LayerCount = -1 }.Build();
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Cmyk32>(transparent).Dispose());
        using var rgba = Image.Load(transparent, options);
        Assert.Equal(new Rgba32(255, 255, 255, 128), Assert.IsType<Image<Rgba32>>(rgba).Frames[0][0, 0]);
    }

    [Fact]
    public void LabSamplesAreOnlyLoadedWithAConversion()
    {
        // L* 100 and 0 with a* = b* = 0
        byte[] stored = [255, 128, 128, 0, 128, 128];
        var data = new PsdFileBuilder(2, 1, 8, Lab, PsdFileBuilder.Planes8(stored, 3)).Build();
        Assert.Equal("Lab samples without color conversion", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);
        Assert.Equal(ImageColorModel.CieLab, Image.Identify(data).ColorModel);
        using var image = Image.Load(data, new ImageDecodeOptions { CieLabConversion = CieLabConversionOptions.Default });
        var rgb = Assert.IsType<Image<Rgb24>>(image);
        Assert.Equal(new Rgb24(255, 255, 255), rgb.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(0, 0, 0), rgb.Frames[0][1, 0]);
    }

    [Fact]
    public void AFileWithoutCompositeIsIdentifiedAndRejected()
    {
        var data = new PsdFileBuilder(3, 2, 8, Rgb, PsdFileBuilder.Planes8(new byte[18], 3)).AddVersionInfo(hasRealMergedData: false).Build();
        Assert.Equal(new Size(3, 2), Image.Identify(data).Size);
        Assert.Equal("No composite image", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ZipCompressedImageDataIsRejected(int compression)
    {
        var data = new PsdFileBuilder(3, 2, 8, Rgb, []) { ChannelCount = 3, Compression = compression, CompressedData = new byte[] { 0x78, 0x9C, 1, 2 } }.Build();
        Assert.Equal(new Size(3, 2), Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).Size);
        Assert.Equal("ZIP-compressed image data", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);
    }

    [Theory]
    [InlineData(Multichannel, 8, "Multichannel color mode")]
    [InlineData(Cmyk, 32, "32-bit CMYK samples")]
    [InlineData(Lab, 16, "16-bit Lab samples")]
    [InlineData(Duotone, 32, "32-bit duotone samples")]
    public void UnsupportedDocumentsAreRejectedByTheirHeader(int mode, int depth, string feature)
    {
        var data = new PsdFileBuilder(1, 1, depth, mode, [.. Enumerable.Range(0, 4).Select(_ => new byte[depth / 8])]).Build();
        Assert.Equal(feature, Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data)).Feature);
    }

    public static TheoryData<string, int, byte[]> MalformedHeaders => new()
    {
        { "version 3", 4, [0, 3] },
        { "reserved byte", 11, [1] },
        { "no channel", 12, [0, 0] },
        { "57 channels", 12, [0, 57] },
        { "height 0", 14, [0, 0, 0, 0] },
        { "depth 4", 22, [0, 4] },
        { "mode 5", 24, [0, 5] },
        { "bitmap of 8 bits", 24, [0, 0] },
        { "indexed without color table", 24, [0, 2] },
    };

    [Theory]
    [MemberData(nameof(MalformedHeaders))]
    public void MalformedHeadersAreRejected(string description, int offset, byte[] bytes)
    {
        var data = new PsdFileBuilder(3, 2, 8, Rgb, PsdFileBuilder.Planes8(RgbPixels, 3)).Build();
        bytes.CopyTo(data, offset);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data).Dispose());
        _ = description;
    }

    public static TheoryData<string, byte[]> MalformedRows => new()
    {
        { "literal past the stored length", [4, 1, 2] },
        { "literal past the row", [3, 1, 2, 3, 4, 0, 9] },
        { "run past the row", [0xFC, 1] },
        { "run without byte", [1, 1, 2, 0xFF] },
        { "row too short", [1, 1, 2] },
        { "empty row", [] },
    };

    [Theory]
    [MemberData(nameof(MalformedRows))]
    public void MalformedPackBitsRowsAreRejected(string description, byte[] row)
    {
        var builder = new PsdFileBuilder(3, 2, 8, Rgb, PsdFileBuilder.Planes8(RgbPixels, 3)) { Compression = 1, EditPackedRow = (channel, y, packed) => channel == 2 && y == 1 ? row : packed };
        var data = builder.Build();
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data).Dispose());
        _ = description;
    }

    [Fact]
    public void ThePackBitsNoOperationIsSkipped()
    {
        var builder = new PsdFileBuilder(3, 1, 8, Grayscale, [[1, 2, 3]]) { Compression = 1, EditPackedRow = (_, _, _) => [0x80, 0, 1, 0x80, 0xFF, 2, 0x80] };
        using var image = Image.Load(builder.Build());
        Assert.Equal(new byte[] { 1, 2, 2 }, GetBytes(image));
    }

    [Fact]
    public void EveryProperPrefixIsTruncated()
    {
        var builder = new PsdFileBuilder(3, 2, 8, Rgb, PsdFileBuilder.Planes8(RgbPixels, 3)) { Compression = 1, LayerCount = -1, LayerBlockKey = "Lr16" };
        builder.AddResolution(72 << 16, 72 << 16);
        var data = builder.Build();
        for (var length = 1; length < data.Length; length++)
        {
            var prefix = data.AsSpan(0, length).ToArray();

            // Detection needs the first 8 bytes
            var expected = length < 8 ? typeof(UnknownImageFormatException) : typeof(InvalidImageContentException);
            Assert.Throws(expected, () => Image.Load(prefix).Dispose(), $"A prefix of {length.ToString(CultureInfo.InvariantCulture)} bytes");
            Assert.Throws(expected, () => Image.Identify(prefix, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }), $"A prefix of {length.ToString(CultureInfo.InvariantCulture)} bytes");
        }
    }

    [Fact]
    public void BytesAfterTheImageDataAreNotRead()
    {
        var data = new PsdFileBuilder(3, 2, 8, Rgb, PsdFileBuilder.Planes8(RgbPixels, 3)).Build();
        using var image = Image.Load([.. data, 1, 2, 3]);
        Assert.Equal(RgbPixels, GetBytes(image));
    }

    [Theory]
    [InlineData(3, 2, false)]
    [InlineData(2, 2, true)]
    [InlineData(3, 1, true)]
    public void DimensionLimitsAreInclusive(int maxWidth, int maxHeight, bool exceeded)
    {
        var data = new PsdFileBuilder(3, 2, 8, Rgb, PsdFileBuilder.Planes8(RgbPixels, 3)).Build();
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = maxWidth, MaxHeight = maxHeight } };
        if (exceeded)
        {
            Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = configuration }));
            return;
        }

        Assert.Equal(new Size(3, 2), Image.Identify(data, new ImageIdentifyOptions { Configuration = configuration }).Size);
    }

    [Fact]
    public async Task ReadersAndStreamsGiveTheImageOfALoad()
    {
        var data = new PsdFileBuilder(3, 2, 16, Rgb, PsdFileBuilder.Planes16([.. RgbPixels.Select(v => (ushort)(v * 257))], 3)) { Compression = 1 }.Build();
        using var expected = Image.Load<Rgba64>(data);
        using (var stream = new MemoryStream(data))
        {
            using var fromStream = await Image.LoadAsync<Rgba64>(stream, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(GetBytes(expected), GetBytes(fromStream));
        }

        using var reader = Image.OpenReader<Rgba64>(new MemoryStream(data));
        Assert.Equal(ImageFormat.Psd, reader.Info.Format);
        using var frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(GetBytes(expected), GetBytes(frame));
        Assert.Null(reader.ReadFrame());
    }

    [Fact]
    public void TheExtensionsSelectTheEncoder()
    {
        Assert.IsType<PsdEncoder>(ImageEncoder.Create(ImageFormat.Psd));
        Assert.False(Assert.IsType<PsdEncoder>(ImageEncoder.FromFileExtension(".psd")).LargeDocument);
        Assert.True(Assert.IsType<PsdEncoder>(ImageEncoder.FromFileExtension("PSB")).LargeDocument);
    }

    private static byte[] GetBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static int[] GetRgba64(Image image)
    {
        using var rgba = image.CloneAs<Rgba64>();
        var bytes = GetBytes(rgba);
        return [.. Enumerable.Range(0, bytes.Length / 2).Select(i => (int)BitConverter.ToUInt16(bytes, i * 2))];
    }

    private static float[] GetFloats(Image image)
    {
        var bytes = GetBytes(image);
        return [.. Enumerable.Range(0, bytes.Length / 4).Select(i => BitConverter.ToSingle(bytes, i * 4))];
    }
}
