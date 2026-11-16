using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Dpx;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// DPX decoding of files assembled by <see cref="DpxFileBuilder"/> (SMPTE ST 268:2014): the packing of every bit depth in both
/// byte orders, the descriptors, the headers and their metadata, the transfer labels, the orientation, the errors, the
/// limits and the readers. The expected samples are the code values scaled by <c>(v × 65535 + max / 2) / max</c>.
/// </summary>
public sealed class DpxDecoderTests
{
    [Theory]
    [InlineData(10, 1, true)]
    [InlineData(10, 1, false)]
    [InlineData(10, 2, true)]
    [InlineData(10, 0, false)]
    [InlineData(10, 0, true)]
    [InlineData(12, 1, true)]
    [InlineData(12, 2, false)]
    [InlineData(12, 0, true)]
    [InlineData(16, 0, false)]
    public void DeepRgbComponentsAreScaledToSixteenBits(int bits, int packing, bool bigEndian)
    {
        var max = (1u << bits) - 1;
        uint[] components = [0, max, 1, max - 1, max / 2, 3, 7, max / 3, 5, 9, max, 0, 100 % max, 200 % max, 300 % max, 1, 2, 3];
        var data = new DpxFileBuilder { Width = 3, Height = 2, Descriptor = 50, BitDepth = bits, Packing = packing, BigEndian = bigEndian, Components = components }.Build();
        using var image = Image.Load(data);
        Assert.Equal(PixelFormat.Rgb48, image.PixelFormat);
        using var pixels = image.CloneAs<Rgba64>();
        for (var i = 0; i < 6; i++)
        {
            var pixel = pixels.Frames[0][i % 3, i / 3];
            Assert.Equal(Scale(components[i * 3], bits), pixel.R);
            Assert.Equal(Scale(components[(i * 3) + 1], bits), pixel.G);
            Assert.Equal(Scale(components[(i * 3) + 2], bits), pixel.B);
            Assert.Equal(ushort.MaxValue, pixel.A);
        }

        var header = image.Metadata.DpxHeader!;
        Assert.Equal(bigEndian, header.IsBigEndian);
        Assert.Equal(bits, header.BitDepth);
        Assert.Equal((DpxPacking)packing, header.Packing);
        Assert.Equal(DpxDescriptor.Rgb, header.Descriptor);
    }

    [Fact]
    public void TenBitComponentsOfAFilledWordFollowTheDescriptor()
    {
        // A multi-component element puts its first component at the top of a word, a single-component one at the bottom
        var rgb = new DpxFileBuilder { Width = 1, Height = 1, Descriptor = 50, BitDepth = 10, Packing = 1, RawData = new byte[] { 0x00, 0x40, 0x10, 0x0C } }.Build();
        using var rgbImage = Image.Load<Rgba64>(rgb);
        Assert.Equal(new Rgba64(Scale(1, 10), Scale(1, 10), Scale(3, 10), 65535), rgbImage.Frames[0][0, 0]);

        var luma = new DpxFileBuilder { Width = 3, Height = 1, Descriptor = 6, BitDepth = 10, Packing = 1, RawData = new byte[] { 0x00, 0x40, 0x10, 0x0C } }.Build();
        using var lumaImage = Image.Load<Gray16>(luma);
        Assert.Equal(Scale(3, 10), lumaImage.Frames[0][0, 0].Value);
        Assert.Equal(Scale(1, 10), lumaImage.Frames[0][1, 0].Value);
        Assert.Equal(Scale(1, 10), lumaImage.Frames[0][2, 0].Value);
    }

    [Fact]
    public void OneBitComponentsStartAtTheLeastSignificantBit()
    {
        // Table 3B, note 1: the first pixel is the least significant bit of the word (the last byte of a big-endian word)
        var data = new DpxFileBuilder { Width = 9, Height = 1, Descriptor = 6, BitDepth = 1, RawData = new byte[] { 0x00, 0x00, 0x01, 0x0D } }.Build();
        using var image = Image.Load<Gray8>(data);
        Assert.Equal(PixelFormat.Gray8, Image.Identify(data).PixelFormat);
        byte[] expected = [255, 0, 255, 255, 0, 0, 0, 0, 255];
        for (var x = 0; x < 9; x++)
        {
            Assert.Equal(expected[x], image.Frames[0][x, 0].Value);
        }
    }

    [Fact]
    public void LinesStartOnAThirtyTwoBitWord()
    {
        // 5 RGB pixels of 8 bits: 15 bytes, padded to 16
        var components = Enumerable.Range(0, 30).Select(i => (uint)(i * 8)).ToArray();
        var data = new DpxFileBuilder { Width = 5, Height = 2, Descriptor = 50, BitDepth = 8, Components = components }.Build();
        Assert.HasCount(2048 + 32, data);
        using var image = Image.Load<Rgb24>(data);
        Assert.Equal(new Rgb24(120, 128, 136), image.Frames[0][0, 1]);
    }

    [Fact]
    public void AbgrAndAlphaElementsAreRgba()
    {
        var abgr = new DpxFileBuilder { Width = 1, Height = 1, Descriptor = 52, BitDepth = 8, Components = [10, 20, 30, 40] }.Build();
        using var abgrImage = Image.Load(abgr);
        Assert.Equal(PixelFormat.Rgba32, abgrImage.PixelFormat);
        Assert.Equal(new Rgba32(40, 30, 20, 10), abgrImage.CloneAs<Rgba32>().Frames[0][0, 0]);

        var alpha = new DpxFileBuilder { Width = 2, Height = 1, Descriptor = 4, BitDepth = 16, Components = [0, 40000] }.Build();
        using var alphaImage = Image.Load(alpha);
        Assert.Equal(PixelFormat.La32, alphaImage.PixelFormat);
        Assert.Equal(new Rgba64(0, 0, 0, 40000), alphaImage.CloneAs<Rgba64>().Frames[0][1, 0]);
        Assert.Equal(true, Image.Identify(alpha).MayHaveTransparency);
    }

    [Fact]
    public void FloatingPointComponentsAreKept()
    {
        var data = new DpxFileBuilder { Width = 2, Height = 1, Descriptor = 50, BitDepth = 32, BigEndian = false, FloatComponents = [2.5, -0.25, 1e-30, 0, 1, 65504] }.Build();
        using var image = Image.Load<RgbaFloat>(data);
        Assert.Equal(new RgbaFloat(2.5f, -0.25f, 1e-30f, 1f), image.Frames[0][0, 0]);
        Assert.Equal(new RgbaFloat(0f, 1f, 65504f, 1f), image.Frames[0][1, 0]);
        Assert.Equal(32, Image.Identify(data).BitsPerComponent);
    }

    [Fact]
    public void DoublePrecisionComponentsAreReadWhenSinglePrecisionHoldsThem()
    {
        var exact = new DpxFileBuilder { Width = 1, Height = 1, Descriptor = 6, BitDepth = 64, FloatComponents = [0.375] }.Build();
        using var image = Image.Load<RgbaFloat>(exact);
        Assert.Equal(new RgbaFloat(0.375f, 0.375f, 0.375f, 1f), image.Frames[0][0, 0]);
        Assert.Equal(32, Image.Identify(exact).BitsPerComponent);
        Assert.Equal(64, Image.Identify(exact).Metadata.DpxHeader!.BitDepth);

        var inexact = new DpxFileBuilder { Width = 1, Height = 1, Descriptor = 6, BitDepth = 64, FloatComponents = [0.1] }.Build();
        Assert.NotNull(Image.Identify(inexact, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
        Assert.Equal("64-bit floating-point components that single precision does not hold", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(inexact)).Feature);
    }

    [Fact]
    public void YCbCrComponentsAreConvertedToRgb()
    {
        // BT.601 nominal range: Y 16 is black, Y 235 white, Cr 240 (with Y 81 and Cb 90) is red
        var data = new DpxFileBuilder { Width = 3, Height = 1, Descriptor = 102, BitDepth = 8, Colorimetric = 7, Transfer = 7, Components = [128, 16, 128, 128, 235, 128, 90, 81, 240] }.Build();
        using var image = Image.Load(data);
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        Assert.Equal(ImageColorModel.YCbCr, Image.Identify(data).ColorModel);
        var frame = image.CloneAs<Rgb24>().Frames[0];
        Assert.Equal(new Rgb24(0, 0, 0), frame[0, 0]);
        Assert.Equal(new Rgb24(255, 255, 255), frame[1, 0]);
        var red = frame[2, 0];
        Assert.True(red.R >= 253 && red.G <= 2 && red.B <= 2, red.ToString());
        Assert.Equal(new CicpColorDescription(5, 6, 0, fullRange: true), image.Metadata.Cicp);
    }

    [Fact]
    public void SubsampledChromaIsInterpolatedOnOddColumns()
    {
        // Cb, Y, Cr, Y: full-range BT.709, a gray pixel pair then a pair of a higher Cr; the odd pixel between takes their mean
        uint[] components = [512, 512, 512, 512, 512, 512, 712, 512];
        var data = new DpxFileBuilder { Width = 4, Height = 1, Descriptor = 100, BitDepth = 10, Packing = 1, Colorimetric = 6, ReferenceLowCode = 0, ReferenceHighCode = 1023, Components = components }.Build();
        using var image = Image.Load<Rgba64>(data);
        var gray = image.Frames[0][0, 0];
        var between = image.Frames[0][1, 0];
        var reddest = image.Frames[0][2, 0];
        Assert.Equal(gray, new Rgba64(gray.R, gray.R, gray.R, 65535));
        Assert.True(reddest.R > between.R && between.R > gray.R, $"{gray} {between} {reddest}");
        Assert.Equal(reddest, image.Frames[0][3, 0]);
    }

    [Theory]
    [InlineData(0, ExifOrientation.TopLeft)]
    [InlineData(1, ExifOrientation.TopRight)]
    [InlineData(2, ExifOrientation.BottomLeft)]
    [InlineData(3, ExifOrientation.BottomRight)]
    [InlineData(4, ExifOrientation.LeftTop)]
    [InlineData(5, ExifOrientation.RightTop)]
    [InlineData(6, ExifOrientation.LeftBottom)]
    [InlineData(7, ExifOrientation.RightBottom)]
    public void OrientationIsReportedNotApplied(int code, ExifOrientation expected)
    {
        var data = new DpxFileBuilder { Width = 2, Height = 1, Descriptor = 6, BitDepth = 8, Orientation = code, Components = [10, 20] }.Build();
        using var image = Image.Load<Gray8>(data);
        Assert.Equal(expected, image.Metadata.Orientation);
        Assert.Equal(new Size(2, 1), image.Size);
        Assert.Equal(10, image.Frames[0][0, 0].Value);
    }

    [Theory]
    [InlineData(2, 6, true, false)]
    [InlineData(6, 6, false, true)]
    [InlineData(5, 5, false, true)]
    [InlineData(1, 1, false, false)]
    [InlineData(255, 0, false, false)]
    [InlineData(6, 1, false, false)]
    public void TheTransferCharacteristicIsALabel(int transfer, int colorimetric, bool linear, bool bt709)
    {
        var data = new DpxFileBuilder { Width = 1, Height = 1, Descriptor = 6, BitDepth = 16, Transfer = transfer, Colorimetric = colorimetric, Components = [12345] }.Build();
        using var image = Image.Load<Gray16>(data);
        Assert.Equal(12345, image.Frames[0][0, 0].Value);
        Assert.Equal(linear ? ColorTransferFunction.Linear : ColorTransferFunction.Srgb, image.Metadata.TransferFunction);
        Assert.Equal(bt709 ? new CicpColorDescription(1, 1, 0, fullRange: true) : null, image.Metadata.Cicp);
        Assert.Equal((DpxTransferCharacteristic)transfer, image.Metadata.DpxHeader!.Transfer);
        Assert.Equal((DpxColorimetricSpecification)colorimetric, image.Metadata.DpxHeader.Colorimetric);
    }

    [Fact]
    public void HeaderFieldsAreReported()
    {
        var builder = new DpxFileBuilder { Width = 1, Height = 1, Descriptor = 6, BitDepth = 8, Components = [1], Version = "V1.0", ReferenceHighQuantity = 2.048f, PixelAspectRatio = (3, 2) };
        builder.Text[(160, 100)] = "Creator";
        builder.Text[(260, 200)] = "Project";
        builder.Text[(460, 200)] = "Copyright";
        builder.Text[(1664, 2)] = "12";
        builder.Text[(1666, 2)] = "34";
        builder.Text[(1668, 2)] = "05";
        builder.Text[(1670, 6)] = "678901";
        builder.Text[(1676, 4)] = "2345";
        builder.Text[(1764, 100)] = "Slate";
        builder.Words[1712] = 1001;
        builder.Words[1724] = BitConverter.SingleToUInt32Bits(24f);
        builder.Words[1920] = 0x10203040;
        builder.Words[1940] = BitConverter.SingleToUInt32Bits(25f);
        builder.ByteFields[1928] = 1;
        var info = Image.Identify(builder.Build());
        var header = info.Metadata.DpxHeader!;
        Assert.Equal("V1.0", header.Version);
        Assert.Equal("Creator", header.Creator);
        Assert.Equal("Project", header.Project);
        Assert.Equal("Copyright", header.Copyright);
        Assert.Equal("1234056789012345", header.FilmEdgeCode);
        Assert.Equal("Slate", header.SlateInformation);
        Assert.Equal(1001u, header.FramePosition);
        Assert.Equal(24f, header.FrameRate);
        Assert.Equal("10:20:30:40", header.TimeCode);
        Assert.Equal(25f, header.TemporalSamplingRate);
        Assert.Equal(true, header.IsInterlaced);
        Assert.Equal(2.048f, header.ReferenceHighQuantity);
        Assert.Null(header.ReferenceLowQuantity);
        Assert.Null(header.SequenceLength);
        Assert.Null(header.Gamma);
        Assert.Equal(1, header.ImageElementCount);
        Assert.Equal(new PixelAspectRatio(3, 2), info.Metadata.PixelAspectRatio);
    }

    [Fact]
    public void AFileWithoutIndustryHeadersHasNoFilmOrTelevisionFields()
    {
        var builder = new DpxFileBuilder { Width = 2, Height = 1, Descriptor = 6, BitDepth = 8, Components = [1, 2], DataOffset = 1664 };
        var data = builder.Build();
        using var image = Image.Load<Gray8>(data);
        Assert.Equal(2, image.Frames[0][1, 0].Value);
        Assert.Null(image.Metadata.DpxHeader!.TimeCode);
        Assert.Null(image.Metadata.DpxHeader.FilmEdgeCode);
    }

    [Theory]
    [InlineData(0x01020304u, "01:02:03:04")]
    [InlineData(0x23595929u, "23:59:59:29")]
    [InlineData(0x0000000Au, null)]
    [InlineData(null, null)]
    public void TimeCodesAreBinaryCodedDecimals(uint? value, string? expected) => Assert.Equal(expected, Internals.DpxStructureParser.FormatTimeCode(value));

    [Fact]
    public void UserDataAndEndOfLinePaddingAreSkipped()
    {
        var data = new DpxFileBuilder { Width = 1, Height = 3, Descriptor = 6, BitDepth = 8, Components = [1, 2, 3], DataOffset = 4096, EndOfLinePadding = 3 }.Build();
        using var image = Image.Load<Gray8>(data);
        Assert.Equal([1, 2, 3], Enumerable.Range(0, 3).Select(y => (int)image.Frames[0][0, y].Value).ToArray());
    }

    [Fact]
    public void OnlyTheFirstElementIsDecoded()
    {
        var data = new DpxFileBuilder { Width = 1, Height = 1, Descriptor = 6, BitDepth = 8, Components = [7], ElementCount = 3, Trailer = new byte[16] }.Build();
        using var image = Image.Load<Gray8>(data);
        Assert.Equal(7, image.Frames[0][0, 0].Value);
        Assert.Equal(3, image.Metadata.DpxHeader!.ImageElementCount);
    }

    [Theory]
    [InlineData("SDPX")]
    [InlineData("XPDS")]
    public void BothMagicNumbersAreDetected(string magic)
    {
        var data = new DpxFileBuilder { Width = 1, Height = 1, Descriptor = 6, BitDepth = 8, Components = [7], BigEndian = magic == "SDPX" }.Build();
        Assert.Equal(magic, System.Text.Encoding.ASCII.GetString(data, 0, 4));
        Assert.Equal(ImageFormat.Dpx, Image.DetectFormat(data));
    }

    public static TheoryData<string, Type, string?> Rejections() => new()
    {
        { "run-length", typeof(UnsupportedImageFeatureException), "Run-length encoding" },
        { "encrypted", typeof(UnsupportedImageFeatureException), "Encrypted image data" },
        { "signed", typeof(UnsupportedImageFeatureException), "Signed components" },
        { "descriptor-8", typeof(UnsupportedImageFeatureException), "Descriptor 8" },
        { "packing-3", typeof(UnsupportedImageFeatureException), "Packing 3" },
        { "one-bit-ycbcr", typeof(UnsupportedImageFeatureException), "Cb, Y, Cr components of 1 bits" },
        { "elements-0", typeof(InvalidImageContentException), null },
        { "orientation-8", typeof(InvalidImageContentException), null },
        { "bit-depth-11", typeof(InvalidImageContentException), null },
        { "odd-422", typeof(InvalidImageContentException), null },
        { "data-offset", typeof(InvalidImageContentException), null },
        { "height-0", typeof(InvalidImageContentException), null },
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public void InvalidAndUnsupportedFilesAreRejectedByTheHeader(string name, Type exception, string? feature)
    {
        var builder = new DpxFileBuilder { Width = 2, Height = 1, Descriptor = 50, BitDepth = 8, Components = [1, 2, 3, 4, 5, 6] };
        Action<DpxFileBuilder> edit = name switch
        {
            "run-length" => b => b.DataEncoding = 1,
            "encrypted" => b => b.EncryptionKey = 0,
            "signed" => b => b.DataSign = 1,
            "descriptor-8" => b => b.Descriptor = 8,
            "packing-3" => b => b.Packing = 3,
            "one-bit-ycbcr" => b => (b.Descriptor, b.BitDepth, b.Components) = (102, 1, [0, 1, 0, 1, 0, 1]),
            "elements-0" => b => b.ElementCount = 0,
            "orientation-8" => b => b.Orientation = 8,
            "bit-depth-11" => b => (b.BitDepth, b.RawData) = (11, new byte[16]),
            "odd-422" => b => (b.Descriptor, b.Width, b.Components) = (100, 3, [1, 2, 3, 4, 5, 6]),
            "data-offset" => b => b.ElementDataOffset = 1000,
            _ => b => b.Height = 0,
        };
        edit(builder);
        var data = builder.Build();
        var identifyError = Record(() => Image.Identify(data));
        Assert.Equal(exception, identifyError?.GetType());
        var loadError = Record(() => Image.Load(data).Dispose());
        Assert.Equal(exception, loadError?.GetType());
        if (feature is not null)
        {
            Assert.Equal(feature, ((UnsupportedImageFeatureException)loadError!).Feature);
        }

        Assert.Equal(ImageFormat.Dpx, loadError switch { InvalidImageContentException invalid => invalid.Format, UnsupportedImageFeatureException unsupported => unsupported.Format, _ => ImageFormat.Unknown });
    }

    [Fact]
    public void TruncatedDataIsInvalid()
    {
        var data = new DpxFileBuilder { Width = 4, Height = 2, Descriptor = 50, BitDepth = 10, Packing = 1, Components = new uint[24] }.Build();
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data.AsSpan(0, data.Length - 1).ToArray()));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data.AsSpan(0, data.Length - 4).ToArray(), new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data.AsSpan(0, 1000).ToArray()));
        Assert.Equal(new Size(4, 2), Image.Identify(data.AsSpan(0, 2048).ToArray()).Size);
    }

    [Theory]
    [InlineData(4, 2, false)]
    [InlineData(3, 2, true)]
    [InlineData(4, 1, true)]
    public void SizeLimitsAreInclusive(int maxWidth, int maxHeight, bool rejected)
    {
        var data = new DpxFileBuilder { Width = 4, Height = 2, Descriptor = 6, BitDepth = 8, Components = new uint[8] }.Build();
        var options = new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = maxWidth, MaxHeight = maxHeight } } };
        var error = Record(() => Image.Load(data, options).Dispose());
        Assert.Equal(rejected, error is ImageResourceLimitException);
        Assert.Equal(rejected, error is not null);
    }

    [Fact]
    public void TheSequentialReaderGivesTheImage()
    {
        var components = Enumerable.Range(0, 12).Select(i => (uint)(i * 5000)).ToArray();
        var data = new DpxFileBuilder { Width = 2, Height = 2, Descriptor = 50, BitDepth = 16, Components = components }.Build();
        using var stream = new MemoryStream(data);
        using var reader = Image.OpenReader<Rgba64>(stream);
        Assert.Equal(ImageFormat.Dpx, reader.Info.Format);
        using var frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(new Rgba64(45000, 50000, 55000, 65535), frame.Frames[0][1, 1]);
        Assert.Null(reader.ReadFrame());
    }

    [Fact]
    public void FloatingPointPixelsConvertOnceTheImageIsRead()
    {
        // Out-of-range samples are rejected by the conversion to integers, after the whole image is read
        var data = new DpxFileBuilder { Width = 1, Height = 2, Descriptor = 6, BitDepth = 32, FloatComponents = [2, 0.5] }.Build();
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Rgba64>(data).Dispose());
        using var clipped = Image.Load<Rgba64>(data, new ImageDecodeOptions { Conversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip } });
        Assert.Equal(new Rgba64(65535, 65535, 65535, 65535), clipped.Frames[0][0, 0]);
        Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba64>(data.AsSpan(0, data.Length - 2).ToArray()).Dispose());
    }

    private static ushort Scale(uint value, int bits)
    {
        var max = (1UL << bits) - 1;
        return bits == 16 ? (ushort)value : (ushort)(((value * 65535UL) + (max / 2)) / max);
    }

    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
