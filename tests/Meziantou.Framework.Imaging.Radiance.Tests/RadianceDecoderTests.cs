using System.Text;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Radiance;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Radiance HDR decoding of files assembled by <see cref="RadianceFileBuilder"/> (Radiance File Formats, "Picture File
/// Format"; "Real Pixels", Graphics Gems II): the information header and its variables, the eight orientations, the
/// three encodings of a scanline, the color labels, the errors, the limits and the readers. The expected colors are those of
/// <see cref="ReferenceRadiance"/>, <c>(m + 0.5) × 2^(e - 136)</c>.
/// </summary>
public sealed class RadianceDecoderTests
{
    // 3x2 pixels in the standard orientation, every one different, with exponents from 1 to 255
    private static readonly byte[] Pixels =
    [
        128, 64, 1, 129,
        255, 0, 10, 130,
        200, 100, 50, 1,
        0, 0, 0, 0,
        17, 255, 3, 255,
        150, 150, 150, 120,
    ];

    [Fact]
    public void FlatScanlinesLoadAsExactFloats()
    {
        var data = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat).Build();
        using var image = Image.Load(data);
        var floats = Assert.IsType<Image<RgbaFloat>>(image);
        Assert.Equal(new RgbaFloat(128.5f / 128, 64.5f / 128, 1.5f / 128, 1f), floats.Frames[0][0, 0]);
        Assert.Equal(new RgbaFloat(255.5f / 64, 0.5f / 64, 10.5f / 64, 1f), floats.Frames[0][1, 0]);
        Assert.Equal(new RgbaFloat(0f, 0f, 0f, 1f), floats.Frames[0][0, 1]);
        Assert.Equal(ImageFormat.Radiance, image.Metadata.SourceFormat);
        Assert.Equal(ColorTransferFunction.Linear, image.Metadata.TransferFunction);
        Assert.Null(image.Metadata.Cicp);
        Assert.Null(image.Metadata.IccProfile);
        AssertPixels(data, image);

        var info = Image.Identify(data);
        Assert.Equal(ImageFormat.Radiance, info.Format);
        Assert.Equal(new Size(3, 2), info.Size);
        Assert.Equal(PixelFormat.RgbaFloat, info.PixelFormat);
        Assert.Equal(ImageColorModel.Rgb, info.ColorModel);
        Assert.Equal(32, info.BitsPerComponent);
        Assert.Equal(1, info.FrameCount);
        Assert.Equal(false, info.MayHaveTransparency);
        Assert.Equal(ImageFormat.Radiance, Image.DetectFormat(data));
        Assert.Equal(ImageFormat.Radiance, Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).Format);
    }

    [Fact]
    public void ExtremeExponentsAreExactInSinglePrecision()
    {
        // The smallest color is a subnormal float, the largest one is close to 2^127; the exponent 0 is black whatever the mantissas
        byte[] pixels = [0, 1, 255, 1, 255, 254, 128, 255, 7, 8, 9, 0, 0, 0, 0, 200];
        var data = RadianceFileBuilder.Create(4, 1, pixels, RadianceScanlineEncoding.Flat).Build();
        using var image = Image.Load<RgbaFloat>(data);
        Assert.Equal(MathF.ScaleB(0.5f, -135), image.Frames[0][0, 0].R);
        Assert.Equal(MathF.ScaleB(255.5f, -135), image.Frames[0][0, 0].B);
        Assert.Equal(MathF.ScaleB(255.5f, 119), image.Frames[0][1, 0].R);
        Assert.Equal(new RgbaFloat(0f, 0f, 0f, 1f), image.Frames[0][2, 0]);
        Assert.Equal(MathF.ScaleB(0.5f, 200 - 136), image.Frames[0][3, 0].G);
        AssertPixels(data, image);
    }

    public static TheoryData<string, RadianceScanlineEncoding> Orientations
    {
        get
        {
            var data = new TheoryData<string, RadianceScanlineEncoding>();
            foreach (var order in RadianceFileBuilder.ScanOrders)
            {
                foreach (var encoding in Enum.GetValues<RadianceScanlineEncoding>())
                {
                    data.Add(order, encoding);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Orientations))]
    public void EveryOrientationGivesThePictureInTheStandardOrientation(string scanOrder, RadianceScanlineEncoding encoding)
    {
        // 9x10 pixels: scanlines of 9 and 10 pixels both use the new encoding, and runs repeat pixels in both directions
        var pixels = CreatePattern(9, 10);
        var data = RadianceFileBuilder.Create(9, 10, pixels, encoding, scanOrder).Build();
        Assert.Equal(new Size(9, 10), Image.Identify(data).Size);
        using var image = Image.Load<RgbaFloat>(data);
        Assert.Equal(new Size(9, 10), image.Size);
        AssertPixels(pixels, 9, image);
        Assert.Equal(scanOrder, image.Metadata.RadianceHeader!.ScanOrder);

        // The other pixel formats are converted from the staging image, and readers give the same picture
        using var rgba64 = Image.Load<Rgba64>(data, new ImageDecodeOptions { Conversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip } });
        using var expected = image.CloneAs<Rgba64>(new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip });
        Assert.Equal(GetBytes(expected), GetBytes(rgba64));
        using var reader = Image.OpenReader<RgbaFloat>(new MemoryStream(data));
        using var frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(GetBytes(image), GetBytes(frame));
    }

    [Fact]
    public void EncodingsMayBeMixedPerScanline()
    {
        var pixels = CreatePattern(12, 3);
        for (var x = 1; x < 6; x++)
        {
            pixels.AsSpan(12 * 4, 4).CopyTo(pixels.AsSpan((12 + x) * 4));
        }

        var builder = RadianceFileBuilder.Create(12, 3, pixels, RadianceScanlineEncoding.Flat);
        var scanlines = RadianceFileBuilder.GetScanlines(12, 3, pixels, "-Y +X");
        builder.Scanlines[0] = RadianceFileBuilder.EncodeNew(scanlines[0]);
        builder.Scanlines[1] = RadianceFileBuilder.EncodeOld(scanlines[1]);
        var data = builder.Build();
        Assert.Equal(new[] { RadianceScanlineEncoding.NewRunLength, RadianceScanlineEncoding.OldRunLength, RadianceScanlineEncoding.Flat }, ReferenceRadiance.Read(data).Encodings.ToArray());
        using var image = Image.Load<RgbaFloat>(data);
        AssertPixels(pixels, 12, image);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(300)]
    [InlineData(65_536)]
    [InlineData(70_000)]
    public void OldRepetitionsHoldTheHigherBytesOfTheirCount(int repeat)
    {
        // The first pixel, then "repeat" repetitions of it: the low byte first
        var width = repeat + 1;
        var pixels = new byte[width * 4];
        for (var i = 0; i < width; i++)
        {
            pixels[(i * 4) + 0] = 200;
            pixels[(i * 4) + 1] = 10;
            pixels[(i * 4) + 2] = 20;
            pixels[(i * 4) + 3] = 130;
        }

        var builder = RadianceFileBuilder.Create(width, 1, pixels, RadianceScanlineEncoding.Flat);
        builder.Scanlines[0] = RadianceFileBuilder.EncodeOld(pixels, minRun: 1);
        var options = new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 100_000 } } };
        using var image = Image.Load<RgbaFloat>(builder.Build(), options);
        AssertPixels(pixels, width, image);
    }

    [Fact]
    public void LongRunsAndLiteralsOfTheNewEncoding()
    {
        // A run of 127 (code 255), a literal of 128 (code 128) and the longest scanline that may use the encoding
        var width = 0x7FFF;
        var pixels = new byte[width * 4];
        for (var i = 0; i < width; i++)
        {
            pixels[i * 4] = (byte)(i < 1000 ? 200 : 128 + (i % 128));
            pixels[(i * 4) + 1] = (byte)(i % 3);
            pixels[(i * 4) + 2] = 0;
            pixels[(i * 4) + 3] = 131;
        }

        var builder = RadianceFileBuilder.Create(width, 1, pixels, RadianceScanlineEncoding.NewRunLength);
        Assert.Contains((byte)255, builder.Scanlines[0]);
        Assert.Contains((byte)128, builder.Scanlines[0]);
        using var image = Image.Load<RgbaFloat>(builder.Build());
        AssertPixels(pixels, width, image);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(0x8000)]
    public void ScanlinesOutsideTheLengthsOfTheNewEncodingAreFlatEvenWithItsMarker(int width)
    {
        // A first pixel 2, 2, 0, 9 is a pixel of a flat scanline that the new encoding cannot have
        var pixels = new byte[width * 4];
        pixels.AsSpan().Fill(5);
        pixels[0] = 2;
        pixels[1] = 2;
        pixels[2] = 0;
        pixels[3] = 9;
        var data = RadianceFileBuilder.Create(width, 1, pixels, RadianceScanlineEncoding.Flat).Build();
        using var image = Image.Load<RgbaFloat>(data);
        AssertPixels(pixels, width, image);
    }

    [Fact]
    public void HeaderVariablesAreReportedNotApplied()
    {
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat);
        builder.Signature = "#?RGBE";
        builder.HeaderLines.Add("# A comment");
        builder.HeaderLines.Add("rpict -vf view.vf scene.oct");
        builder.HeaderLines.Add("EXPOSURE=2");
        builder.HeaderLines.Add("EXPOSURE= 1.5e+00 ");
        builder.HeaderLines.Add("COLORCORR=1 0.5 2");
        builder.HeaderLines.Add("COLORCORR=\t2 2 0.25");
        builder.HeaderLines.Add("PIXASPECT=0.5");
        builder.HeaderLines.Add("PIXASPECT=3");
        builder.HeaderLines.Add("GAMMA=2.2");
        builder.HeaderLines.Add("VIEW= -vtv -vp 0 0 0");
        builder.HeaderLines.Add("VIEW= -vh 45");
        builder.HeaderLines.Add("SOFTWARE=RADIANCE 5.4a");
        builder.HeaderLines.Add("OTHER=value");
        var data = builder.Build();
        var expected = new RadianceHeader(isXyz: false, 3, [2, 1, 0.5], primaries: null, 1.5, 2.2, "-vtv -vp 0 0 0 -vh 45", "RADIANCE 5.4a", "-Y +X");
        Assert.Equal(expected, Image.Identify(data).Metadata.RadianceHeader);
        using var image = Image.Load<RgbaFloat>(data);
        Assert.Equal(expected, image.Metadata.RadianceHeader);
        AssertPixels(Pixels, 3, image);
        Assert.Equal(expected, image.Metadata.Clone().RadianceHeader);
    }

    [Fact]
    public void FormatIsOptional()
    {
        var data = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat, format: null).Build();
        using var image = Image.Load<RgbaFloat>(data);
        Assert.False(image.Metadata.RadianceHeader!.IsXyz);
        Assert.Equal(1d, image.Metadata.RadianceHeader.Exposure);
        Assert.Equal(new[] { 1d, 1d, 1d }, image.Metadata.RadianceHeader.ColorCorrection.ToArray());
        Assert.Equal(1d, image.Metadata.RadianceHeader.PixelAspectRatio);
        Assert.Null(image.Metadata.RadianceHeader.Gamma);
        Assert.Null(image.Metadata.RadianceHeader.View);
        Assert.Null(image.Metadata.RadianceHeader.Software);
        Assert.Null(image.Metadata.RadianceHeader.Primaries);
    }

    [Theory]
    [InlineData("0.640 0.330 0.290 0.600 0.150 0.060 0.333 0.333", 0)]
    [InlineData("0.6400 0.3300 0.3000 0.6000 0.1500 0.0600 0.3127 0.3290", 0)]
    [InlineData("0.708 0.292 0.170 0.797 0.131 0.046 0.3127 0.3290", 9)]
    [InlineData("0.680 0.320 0.265 0.690 0.150 0.060 0.314 0.351", 11)]
    [InlineData("0.680 0.320 0.265 0.690 0.150 0.060 0.3127 0.3290", 12)]
    public void PrimariesLabelThePixels(string primaries, byte cicp)
    {
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat);
        builder.HeaderLines.Add("PRIMARIES=" + primaries);
        using var image = Image.Load(builder.Build());
        Assert.Equal(cicp == 0 ? null : new CicpColorDescription(cicp, 8, 0, fullRange: true), image.Metadata.Cicp);
        Assert.Equal(cicp == 0 ? ColorTransferFunction.Linear : ColorTransferFunction.Srgb, image.Metadata.TransferFunction);
        Assert.Null(image.Metadata.IccProfile);
        Assert.HasCount(8, image.Metadata.RadianceHeader!.Primaries!);
    }

    [Fact]
    public void OtherPrimariesAreAnIccProfile()
    {
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat);
        builder.HeaderLines.Add("PRIMARIES=0.670 0.330 0.210 0.710 0.140 0.080 0.333 0.333");
        using var image = Image.Load(builder.Build());
        Assert.NotNull(image.Metadata.IccProfile);
        Assert.Null(image.Metadata.Cicp);
        Assert.Equal(new[] { 0.670, 0.330, 0.210, 0.710, 0.140, 0.080, 0.333, 0.333 }, image.Metadata.RadianceHeader!.Primaries!.ToArray());
    }

    [Fact]
    public void XyzeIsIdentifiedAndRejected()
    {
        var data = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat, format: "32-bit_rle_xyze").Build();
        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.True(info.Metadata.RadianceHeader!.IsXyz);
        Assert.Equal(new Size(3, 2), info.Size);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        Assert.Equal("XYZE samples", exception.Feature);
        Assert.Equal(ImageFormat.Radiance, exception.Format);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.OpenReader<RgbaFloat>(new MemoryStream(data)).Dispose());
    }

    [Theory]
    [InlineData("FORMAT=32-bit_rle_hsr")]
    [InlineData("FORMAT=ascii")]
    public void OtherFormatsAreUnsupported(string line)
    {
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat, format: null);
        builder.HeaderLines.Add(line);
        var data = builder.Build();
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data).Dispose());
    }

    [Fact]
    public void TwoFormatLinesAreRejected()
    {
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat);
        builder.HeaderLines.Add("FORMAT=32-bit_rle_rgbe");
        var data = builder.Build();
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
    }

    [Theory]
    [InlineData("EXPOSURE=")]
    [InlineData("EXPOSURE=0")]
    [InlineData("EXPOSURE=-1")]
    [InlineData("EXPOSURE=1 2")]
    [InlineData("EXPOSURE=abc")]
    [InlineData("EXPOSURE=Infinity")]
    [InlineData("EXPOSURE=1e400")]
    [InlineData("COLORCORR=1 1")]
    [InlineData("COLORCORR=1 1 0")]
    [InlineData("PIXASPECT=0")]
    [InlineData("GAMMA=-2.2")]
    [InlineData("PRIMARIES=0.64 0.33 0.3 0.6 0.15 0.06 0.3127")]
    [InlineData("PRIMARIES=0.64 0.33 0.64 0.33 0.15 0.06 0.3127 0.329")]
    [InlineData("PIXASPECT=inf")]
    public void UnreadableHeaderVariablesAreSkipped(string line)
    {
        // Like a metadata payload that cannot be read: FFmpeg writes PIXASPECT=inf
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat);
        builder.HeaderLines.Add(line);
        using var image = Image.Load<RgbaFloat>(builder.Build());
        AssertPixels(Pixels, 3, image);
        Assert.Equal(new RadianceHeader(isXyz: false, 1, [1, 1, 1], primaries: null, 1, gamma: null, view: null, software: null, "-Y +X"), image.Metadata.RadianceHeader);
        Assert.Equal(ColorTransferFunction.Linear, image.Metadata.TransferFunction);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Theory]
    [InlineData("-Y 2 +X")]
    [InlineData("-Y 2 +X 3 +Z 1")]
    [InlineData("-Y 2 -Y 3")]
    [InlineData("Y 2 +X 3")]
    [InlineData("-y 2 +x 3")]
    [InlineData("-Y 0 +X 3")]
    [InlineData("-Y 2 +X -3")]
    [InlineData("-Y 2 +X 3.0")]
    [InlineData("-Y 2 +X 0x3")]
    [InlineData("")]
    public void InvalidResolutionStringsAreRejected(string resolution)
    {
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat);
        builder.Resolution = resolution;
        var data = builder.Build();
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
    }

    [Fact]
    public void ResolutionStringsMaySeparateTheirTokensWithSeveralSpaces()
    {
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat);
        builder.Resolution = "-Y  2\t+X 3 ";
        using var image = Image.Load<RgbaFloat>(builder.Build());
        AssertPixels(Pixels, 3, image);
    }

    [Fact]
    public void ALongResolutionStringIsRejected()
    {
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat);
        builder.Resolution = "-Y 2 +X 3" + new string(' ', 100);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(builder.Build()));
    }

    [Theory]
    [InlineData("#?RADIANCE ")]
    [InlineData("#?RADIANC")]
    public void OtherIdentifiersAreNotRecognized(string signature)
    {
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat);
        builder.Signature = signature;
        Assert.Throws<UnknownImageFormatException>(() => Image.Identify(builder.Build()));
    }

    public static TheoryData<string, byte[]> MalformedScanlines => new()
    {
        // The new encoding: the length is not the one of the scanline, runs and literals past its end, a code 0
        { "length", [2, 2, 0, 9] },
        { "run past the end", [2, 2, 0, 8, 137, 1] },
        { "literal past the end", [2, 2, 0, 8, 9, 1, 2, 3, 4, 5, 6, 7, 8, 9] },
        { "code 0", [2, 2, 0, 8, 0] },

        // The old encoding: a repetition without a previous pixel, a repetition past the end of the scanline
        { "repetition first", [1, 1, 1, 2] },
        { "repetition past the end", [9, 9, 9, 130, 1, 1, 1, 8] },
    };

    [Theory]
    [MemberData(nameof(MalformedScanlines))]
    public void MalformedScanlinesAreRejected(string description, byte[] scanline)
    {
        var builder = RadianceFileBuilder.Create(8, 1, new byte[32], RadianceScanlineEncoding.Flat);
        builder.Scanlines[0] = [.. scanline, .. new byte[64]];
        var data = builder.Build();
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }), description);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data).Dispose(), description);
    }

    [Fact]
    public void EveryProperPrefixIsTruncated()
    {
        var data = RadianceFileBuilder.Create(9, 2, CreatePattern(9, 2), RadianceScanlineEncoding.NewRunLength).Build();
        for (var length = 1; length < data.Length; length++)
        {
            var prefix = data.AsSpan(0, length).ToArray();
            // The identifier is recognized once its line feed is there
            var expected = length < "#?RADIANCE\n".Length ? typeof(UnknownImageFormatException) : typeof(InvalidImageContentException);
            Assert.Throws(expected, () => Image.Load(prefix).Dispose(), $"A prefix of {length.ToString(CultureInfo.InvariantCulture)} bytes");
            Assert.Throws(expected, () => Image.Identify(prefix, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }), $"A prefix of {length.ToString(CultureInfo.InvariantCulture)} bytes");
        }
    }

    [Fact]
    public void BytesAfterTheLastScanlineAreNotRead()
    {
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat);
        builder.Trailer = new byte[] { 1, 2, 3 };
        using var image = Image.Load<RgbaFloat>(builder.Build());
        AssertPixels(Pixels, 3, image);
    }

    [Theory]
    [InlineData(3, 2, false)]
    [InlineData(2, 2, true)]
    [InlineData(3, 1, true)]
    public void DimensionLimitsAreInclusive(int maxWidth, int maxHeight, bool exceeded)
    {
        var data = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat).Build();
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = maxWidth, MaxHeight = maxHeight } };
        if (exceeded)
        {
            Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = configuration }));
            return;
        }

        Assert.Equal(new Size(3, 2), Image.Identify(data, new ImageIdentifyOptions { Configuration = configuration }).Size);
    }

    [Fact]
    public void ColumnMajorDimensionsAreCheckedOnTheirAxes()
    {
        // "-X 3 +Y 2": three columns of two pixels
        var data = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat, "-X +Y").Build();
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 3, MaxHeight = 2 } };
        Assert.Equal(new Size(3, 2), Image.Identify(data, new ImageIdentifyOptions { Configuration = configuration }).Size);
        var narrow = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 2 } };
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = narrow })).Kind);
    }

    [Fact]
    public void HugeSizesAreLimits()
    {
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat);
        builder.Resolution = "-Y 2 +X 99999999999999999999999";
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(builder.Build())).Kind);
    }

    [Fact]
    public void HeaderBytesAreChargedToTheMetadataLimit()
    {
        var builder = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat);
        builder.HeaderLines.Add("SOFTWARE=" + new string('x', 1000));
        var data = builder.Build();

        // Every line of the header is charged, its line feed and the empty line included
        var headerLength = Encoding.ASCII.GetByteCount("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n" + "SOFTWARE=" + new string('x', 1000) + "\n\n");
        Assert.NotNull(Image.Identify(data, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = headerLength } } }));
        var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = headerLength - 1 } } }));
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, exception.Kind);
    }

    [Fact]
    public void ALineWithoutLineFeedIsChargedAsItArrives()
    {
        // A header line that never ends reaches the metadata limit, not the end of the input
        var data = Encoding.ASCII.GetBytes("#?RADIANCE\nSOFTWARE=" + new string('x', 100_000));
        var options = new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = 1000 } } };
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(new MemoryStream(data), options)).Kind);
    }

    [Fact]
    public void ConversionsRejectHighDynamicRangeSamplesByDefault()
    {
        var data = RadianceFileBuilder.Create(3, 2, Pixels, RadianceScanlineEncoding.Flat).Build();
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Rgba32>(data));
        using var half = Image.Load<RgbaHalf>(data, new ImageDecodeOptions { Conversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip } });
        Assert.Equal((Half)(128.5f / 128), half.Frames[0][0, 0].R);
    }

    private static byte[] CreatePattern(int width, int height)
    {
        // Runs of equal pixels and of equal components, and pixels that differ
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                pixels[offset] = (byte)(128 + ((x / 4) * 13) + y);
                pixels[offset + 1] = (byte)(x < 5 ? 7 : x * y);
                pixels[offset + 2] = (byte)((x + y) % 3 == 0 ? 0 : 100);
                pixels[offset + 3] = (byte)(120 + (y % 2));
            }
        }

        return pixels;
    }

    private static void AssertPixels(byte[] data, Image image)
    {
        var picture = ReferenceRadiance.Read(data);
        Assert.Equal(new Size(picture.Width, picture.Height), image.Size);
        Assert.Equal(picture.GetFloatPixels(), GetBytes(image));
    }

    private static void AssertPixels(byte[] rgbe, int width, Image image)
    {
        var picture = new RadiancePicture(width, rgbe.Length / 4 / width, rgbe, [], "-Y +X", []);
        Assert.Equal(new Size(picture.Width, picture.Height), image.Size);
        Assert.Equal(picture.GetFloatPixels(), GetBytes(image));
    }

    private static byte[] GetBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }
}
