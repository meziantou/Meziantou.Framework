using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Dng;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// DNG decoding of files assembled by <see cref="DngFileBuilder"/> from the DNG specification 1.7.1: detection, the
/// metadata, the raw image API, the opcodes, the previews, the errors, lenient decoding and the requests. The rendered
/// pixels are compared with <see cref="ReferenceDng"/>, the independent renderer of the test harness; the corpus
/// (<c>dng</c> fixtures) covers every scene, and the interop tests compare with the DNG SDK.
/// </summary>
public sealed class DngDecoderTests
{
    [Fact]
    public void ADngFileIsDetectedFromItsFirstDirectory()
    {
        var data = DngScenes.Get("bayer-rggb").Build();
        Assert.Equal(ImageFormat.Dng, Image.DetectFormat(data));

        // The first directory follows the samples: a prefix of 18 bytes only shows a TIFF file
        Assert.Equal(ImageFormat.Tiff, Image.DetectFormat(data.AsSpan(0, Image.FormatDetectionPrefixLength)));
        using var stream = new MemoryStream(data);
        Assert.Equal(ImageFormat.Dng, Image.Identify(stream).Format);
    }

    [Fact]
    public void ADngVersionTagAfterALargerTagIsDetected()
    {
        // The entries of IFD 0 are not sorted: the private tag 65000 comes first, before DNGVersion (50706)
        var builder = Variant(DngScenes.Get("bayer-rggb"));
        builder.ExtraIfd0Tags.Add(DngTag.UInt32s(65000, 1));
        var data = builder.Build();
        var ifd0 = (int)BitConverter.ToUInt32(data, 4);
        var count = BitConverter.ToUInt16(data, ifd0);
        var entries = data.AsSpan(ifd0 + 2, count * 12);
        Assert.Equal((ushort)65000, BitConverter.ToUInt16(entries[^12..]));
        var last = entries[^12..].ToArray();
        entries[..^12].CopyTo(entries[12..]);
        last.CopyTo(entries);

        Assert.Equal(ImageFormat.Dng, Image.DetectFormat(data));
        Assert.Equal(ImageFormat.Dng, Image.Identify(data).Format);
        using var raw = DngRawImage.Load(data);
        using var stream = new MemoryStream(data);
        using var fromStream = DngRawImage.Load(stream);
        Assert.Equal(raw.Size, fromStream.Size);
    }

    [Fact]
    public void AnActiveAreaWithoutASampleOfEveryColorIsRejected()
    {
        // A 1x1 active area of an RGGB pattern holds a red sample only: no window of it holds green or blue
        var builder = new DngFileBuilder
        {
            Width = 8,
            Height = 8,
            Samples = DngScenes.Mosaic(8, 8, 2, 2, [0, 1, 1, 2], 0, 65535),
            ColorMatrix1 = DngScenes.CameraMatrixD65,
            ActiveArea = [0, 0, 1, 1],
            DefaultCropOrigin = [0, 0],
            DefaultCropSize = [1, 1],
        };

        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(builder.Build()));
        Assert.Contains("cannot be demosaiced", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentifyReportsTheDefaultCropAndTheProperties()
    {
        var info = Image.Identify(DngScenes.Get("active-area-crop").Build());
        Assert.Equal(ImageFormat.Dng, info.Format);
        Assert.Equal(new Size(40, 28), info.Size);
        Assert.Equal(PixelFormat.RgbaFloat, info.PixelFormat);
        Assert.Equal(32, info.BitsPerComponent);
        Assert.Equal(ColorTransferFunction.Linear, info.Metadata.TransferFunction);
        var properties = info.Metadata.DngProperties!;
        Assert.Equal(new Version(1, 4, 0, 0), properties.Version);
        Assert.Equal(new Version(1, 4, 0, 0), properties.BackwardVersion);
        Assert.Equal("Meziantou Synthetic", properties.UniqueCameraModel);
        Assert.Equal("Meziantou", properties.Make);
        Assert.Equal("Synthetic", properties.Model);
    }

    [Fact]
    public void MetadataOfTheFileIsReportedAndTheOrientationIsNotApplied()
    {
        var builder = new DngFileBuilder
        {
            Width = 32,
            Height = 24,
            Samples = DngScenes.Mosaic(32, 24, 2, 2, [0, 1, 1, 2], 0, 65535),
            ColorMatrix1 = DngScenes.CameraMatrixD65,
            Orientation = 6,
            CameraSerialNumber = "SN 7",
            ProfileName = "Neutral",
            BaselineExposure = 0.25,
            ExifTags = [(36867, "2026:11:15 10:00:00"), (42036, "Synthetic lens")],
            Xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8.ToArray(),
        };

        using var image = Image.Load(builder.Build());
        Assert.Equal(new Size(32, 24), image.Size);
        Assert.Equal(ExifOrientation.RightTop, image.Metadata.Orientation);
        Assert.Equal("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8.ToArray(), image.Metadata.XmpProfile!.Data.ToArray());
        var exif = image.Metadata.ExifProfile!.Data.ToArray();
        Assert.True(ExifTiff.IsValid(exif));
        Assert.True(exif.AsSpan().IndexOf("Synthetic lens"u8) >= 0, "The tags of the EXIF IFD are in the EXIF data.");
        Assert.True(exif.AsSpan().IndexOf("Meziantou"u8) >= 0, "The make of IFD 0 is in the EXIF data.");
        var properties = image.Metadata.DngProperties!;
        Assert.Equal("SN 7", properties.CameraSerialNumber);
        Assert.Equal("Neutral", properties.ProfileName);
        Assert.Equal(0.25, properties.BaselineExposure);
    }

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void ScenesRenderLikeTheReference(string name)
    {
        var data = DngScenes.Get(name).Build();
        using var image = Image.Load<RgbaFloat>(data);
        var reference = ReferenceDng.Render(data);
        Assert.Equal(new Size(reference.Width, reference.Height), image.Size);
        var pixels = new RgbaFloat[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        for (var i = 0; i < pixels.Length; i++)
        {
            Assert.Equal(reference.Rgba[i * 4], pixels[i].R);
            Assert.Equal(reference.Rgba[(i * 4) + 1], pixels[i].G);
            Assert.Equal(reference.Rgba[(i * 4) + 2], pixels[i].B);
            Assert.Equal(1f, pixels[i].A);
        }
    }

    public static TheoryData<string> SceneNames => [.. DngScenes.All.Select(scene => scene.Name)];

    [Fact]
    public void TheRawImageGivesTheStoredSamplesAndTheLinearValues()
    {
        var builder = DngScenes.Get("black-level-pattern");
        var data = builder.Build();
        using var raw = DngRawImage.Load(data);
        Assert.Equal(new Size(40, 24), raw.Size);
        Assert.True(raw.IsColorFilterArray);
        Assert.Equal(16, raw.BitsPerSample);
        Assert.False(raw.IsFloatingPoint);
        Assert.Equal(2, raw.CfaPatternRows);
        Assert.Equal(2, raw.CfaPatternColumns);
        Assert.Equal([0, 1, 2], raw.CfaPlaneColors);
        Assert.Equal(0, raw.GetCfaColor(0, 0));
        Assert.Equal(1, raw.GetCfaColor(1, 0));
        Assert.Equal(2, raw.GetCfaColor(3, 5));
        Assert.Equal(2, raw.BlackLevelRepeatRows);
        Assert.Equal([400.0, 404, 410, 414], raw.BlackLevels);
        Assert.HasCount(40, raw.BlackLevelDeltaH!);
        Assert.Equal([8000.0], raw.WhiteLevels);

        var reference = ReferenceDng.Render(data);
        var row = new float[40];
        for (var y = 0; y < 24; y++)
        {
            raw.CopyStoredRow(y, row);
            Assert.Equal(builder.Samples.AsSpan(y * 40, 40).ToArray().Select(value => (float)value), row);
            raw.CopyLinearRow(y, row);
            Assert.Equal(reference.Stage2.AsSpan(y * 40, 40).ToArray(), row);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => raw.CopyLinearRow(24, row));
        Assert.Throws<ArgumentException>(() => raw.CopyStoredRow(0, new float[39]));
        raw.Dispose();
        Assert.Throws<ObjectDisposedException>(() => raw.CopyStoredRow(0, row));
    }

    [Fact]
    public void TheRawImageOfLinearRawDataHasNoColorFilterArray()
    {
        using var raw = DngRawImage.Load(DngScenes.Get("linear-raw-rgb").Build());
        Assert.False(raw.IsColorFilterArray);
        Assert.Equal(3, raw.SamplesPerPixel);
        Assert.Empty(raw.CfaPlaneColors);
        Assert.Throws<InvalidOperationException>(() => raw.GetCfaColor(0, 0));
    }

    [Fact]
    public void TheRawImageApiRejectsOtherFormats()
    {
        Assert.Throws<UnknownImageFormatException>(() => DngRawImage.Load("not an image"u8));
        var tiff = new DngFileBuilder { Width = 16, Height = 16, Samples = new double[256], DngVersion = [1, 4, 0, 0] }.Build();
        Assert.Equal(ImageFormat.Dng, Image.DetectFormat(tiff));
    }

    [Fact]
    public void AnOptionalOpcodeThatIsNotImplementedIsSkipped()
    {
        var plain = DngScenes.Get("bayer-rggb");
        var optional = Variant(plain, opcodeList3: new DngOpcodeWriter().Add(6, new byte[16], optional: true).ToArray());
        AssertSamePixels(plain.Build(), optional.Build());
    }

    [Fact]
    public void ARequiredOpcodeThatIsNotImplementedIsRejectedStrictlyAndSkippedLeniently()
    {
        var plain = DngScenes.Get("bayer-rggb");
        var data = Variant(plain, opcodeList3: new DngOpcodeWriter().Add(6, new byte[16]).ToArray()).Build();
        var error = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data));
        Assert.Equal(ImageFormat.Dng, error.Format);
        Assert.Equal("Opcode: TrimBounds", error.Feature);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));

        var lenient = new ImageDecodeOptions { Configuration = new ImageConfiguration { DecodingStrictness = DecodingStrictness.Lenient } };
        using var expected = Image.Load<RgbaFloat>(plain.Build());
        using var actual = Image.Load<RgbaFloat>(data, lenient);
        Assert.Equal(GetPixels(expected), GetPixels(actual));
    }

    [Fact]
    public void AnOpcodeOfALaterVersionIsNotUnderstood()
    {
        var data = Variant(DngScenes.Get("bayer-rggb"), opcodeList2: new DngOpcodeWriter().Add(12, new byte[36], version: [1, 8, 0, 0]).ToArray()).Build();
        Assert.Equal("Opcode: ScalePerRow", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data)).Feature);
    }

    [Fact]
    public void AnOpcodeWhoseCountDoesNotMatchItsAreaIsMalformed()
    {
        var data = Variant(DngScenes.Get("bayer-rggb"), opcodeList2: new DngOpcodeWriter().PerLine(12, new DngOpcodeArea(0, 0, 32, 48), [1f, 1f]).ToArray()).Build();
        Assert.Equal(ImageFormat.Dng, Assert.Throws<InvalidImageContentException>(() => Image.Identify(data)).Format);
    }

    [Fact]
    public void ThePreviewsAreAuxiliaryImages()
    {
        var data = DngScenes.Get("thumbnail-exif-xmp").Build();
        var images = AuxiliaryImageCollection.Load(data);
        var thumbnail = Assert.Single(images);
        Assert.Equal(AuxiliaryImageKind.Thumbnail, thumbnail.Kind);
        Assert.Equal(new Size(16, 12), thumbnail.Size);
        using var decoded = thumbnail.Decode<Rgb24>();
        var pixels = new Rgb24[16 * 12];
        decoded.Frames[0].CopyPixelDataTo(pixels);
        for (var i = 0; i < pixels.Length; i++)
        {
            Assert.Equal(new Rgb24((byte)((i * 3 * 7) % 251), (byte)((((i * 3) + 1) * 7) % 251), (byte)((((i * 3) + 2) * 7) % 251)), pixels[i]);
        }

        // The raw image is the primary image, not an auxiliary one
        Assert.Empty(AuxiliaryImageCollection.Load(DngScenes.Get("bayer-rggb").Build()));
    }

    [Fact]
    public void LossyJpegLinearRawDataIsTheSamplesOfItsDatastream()
    {
        // DNG 1.4 compresses 8-bit linear raw data with baseline JPEG (compression 34892): the samples are the decoded
        // colors of the datastream, which codes three samples as JFIF does
        using var source = new Image<Rgb24>(32, 16);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                source.Frames[0][x, y] = new Rgb24((byte)(40 + (x * 5)), (byte)(120 - (y * 2)), (byte)(200 - (x * 4) - (y * 3)));
            }
        }

        using var jpeg = new MemoryStream();
        source.Save(jpeg, new JpegEncoder { Quality = 95 });
        var builder = new DngFileBuilder
        {
            Width = 32,
            Height = 16,
            Photometric = 34892,
            SamplesPerPixel = 3,
            BitsPerSample = 8,
            Samples = new double[32 * 16 * 3],
            Compression = 34892,
            EncodedBlocks = [jpeg.ToArray()],
            WhiteLevel = [255, 255, 255],
            ColorMatrix1 = DngScenes.CameraMatrixD65,
        };

        using var decoded = Image.Load<Rgb24>(jpeg.ToArray());
        var expected = new Rgb24[32 * 16];
        decoded.Frames[0].CopyPixelDataTo(expected);
        using var raw = DngRawImage.Load(builder.Build());
        var row = new float[32 * 3];
        for (var y = 0; y < 16; y++)
        {
            raw.CopyStoredRow(y, row);
            for (var x = 0; x < 32; x++)
            {
                var pixel = expected[(y * 32) + x];
                Assert.Equal((pixel.R, pixel.G, pixel.B), ((byte)row[x * 3], (byte)row[(x * 3) + 1], (byte)row[(x * 3) + 2]));
            }
        }
    }

    [Fact]
    public void ADngFileIsNotACollection()
    {
        var error = Assert.Throws<UnsupportedImageFeatureException>(() => ImageCollection.Load(DngScenes.Get("bayer-rggb").Build()));
        Assert.Equal(ImageFormat.Dng, error.Format);
    }

    [Fact]
    public void ARegionHasThePixelsOfTheFullDecodeInThatArea()
    {
        var data = DngScenes.Get("cfa-6x6").Build();
        using var full = Image.Load<RgbaFloat>(data);
        var area = new Rectangle(5, 3, 20, 11);
        using var region = Image.Load<RgbaFloat>(data, new ImageDecodeOptions { Region = area });
        Assert.Equal(area.Size, region.Size);
        var fullPixels = GetPixels(full);
        var regionPixels = GetPixels(region);
        for (var y = 0; y < area.Height; y++)
        {
            Assert.Equal(fullPixels.AsSpan(((area.Y + y) * full.Width) + area.X, area.Width).ToArray(), regionPixels.AsSpan(y * area.Width, area.Width).ToArray());
        }
    }

    [Fact]
    public void AScaledDecodeIsAtFullResolution()
    {
        var data = DngScenes.Get("bayer-rggb").Build();
        using var target = Image.Load(data, new ImageDecodeOptions { TargetSize = new Size(8, 8) });
        Assert.Equal(new Size(48, 32), target.Size);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data, new ImageDecodeOptions { ScaleDenominator = 2 }));
    }

    [Fact]
    public void TheRenderingConvertsToOtherPixelFormatsOnRequest()
    {
        var data = DngScenes.Get("bayer-rggb").Build();
        using var floats = Image.Load<RgbaFloat>(data);
        using var half = Image.Load<RgbaHalf>(data);
        var expected = GetPixels(floats);
        var actual = new RgbaHalf[expected.Length];
        half.Frames[0].CopyPixelDataTo(actual);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal((Half)expected[i].G, actual[i].G);
        }
    }

    [Fact]
    public void DngIsReadAndWritten()
    {
        Assert.IsType<DngEncoder>(ImageEncoder.Create(ImageFormat.Dng));
        Assert.IsType<DngEncoder>(ImageEncoder.FromFileExtension(".dng"));
        Assert.Equal(ImageFormat.Dng, ImageFormatInfo.Get(ImageFormat.Dng).Format);
        Assert.Contains(".dng", ImageFormatInfo.Get(ImageFormat.Dng).FileExtensions);
    }

    [Fact]
    public void TheCanvasLimitsApplyToTheStoredRawImage()
    {
        // The raw image (52x36) is allocated, then cropped to the image (40x28)
        var data = DngScenes.Get("active-area-crop").Build();
        using (Image.Load(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFramePixels = 52 * 36, MaxWidth = 52, MaxHeight = 36 } } }))
        {
        }

        Assert.Equal(ImageResourceLimitKind.FramePixels, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFramePixels = (52 * 36) - 1 } } })).Kind);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 51 } } })).Kind);
    }

    [Fact]
    public void ASequentialReaderRejectsADngFile()
    {
        using var stream = new MemoryStream(DngScenes.Get("bayer-rggb").Build());
        var error = Assert.Throws<UnsupportedImageFeatureException>(() => Image.OpenReader<RgbaFloat>(stream));
        Assert.Equal("Sequential reading", error.Feature);
    }

    [Fact]
    public void TheLiveAllocationLimitBoundsTheStages()
    {
        var data = DngScenes.Get("bayer-rggb").Build();
        Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 4096 } } }));
    }

    [Fact]
    public void BadPixelsWithoutAGoodNeighborAreLeftAsTheyAre()
    {
        // A rectangle larger than the image marks every pixel: none has a good neighbor to be repaired from
        var scene = DngScenes.Get("bayer-rggb");
        var fixedData = new DngFileBuilder
        {
            Width = scene.Width,
            Height = scene.Height,
            Samples = scene.Samples,
            BlackLevel = scene.BlackLevel,
            WhiteLevel = scene.WhiteLevel,
            ColorMatrix1 = scene.ColorMatrix1,
            AsShotNeutral = scene.AsShotNeutral,
            OpcodeList1 = new DngOpcodeWriter().FixBadPixelsList(0, [], [(0, 0, scene.Height + 10, scene.Width + 10)]).ToArray(),
        }.Build();
        AssertSamePixels(Variant(scene).Build(), fixedData);
    }

    [Fact]
    public void TheOpcodesOfALargeListAreChargedToTheLiveAllocationLimit()
    {
        // A gain map of 256x256 points takes 256 KiB: the bytes read from the tag, their copy and the parsed gains are all
        // live while the list is parsed
        var gains = new float[256 * 256];
        gains.AsSpan().Fill(1);
        var list = new DngOpcodeWriter().GainMap(new DngOpcodeArea(0, 0, 32, 48), 256, 256, 1 / 255.0, 1 / 255.0, 0, 0, 1, gains).ToArray();
        var data = Variant(DngScenes.Get("bayer-rggb"), opcodeList2: list).Build();
        var limit = 2L * list.Length;
        var error = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = limit } } }));
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, error.Kind);

        var info = Image.Identify(data, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 2 * limit } } });
        Assert.Equal(ImageFormat.Dng, info.Format);
    }

    [Theory]
    [InlineData(0.3127, 0.3290, 6504)]
    [InlineData(0.44757, 0.40745, 2856)]
    [InlineData(0.34567, 0.35850, 5003)]
    public void TheCorrelatedColorTemperatureIsTheOneOfRobertson(double x, double y, double kelvins)
    {
        Assert.True(Math.Abs(kelvins - DngColorProfile.GetTemperature(x, y)) < 5, "The temperature of a CIE illuminant is its nominal one.");
        Assert.True(Math.Abs(DngColorProfile.GetTemperature(x, y) - ReferenceDng.Cct(x, y)) < 1e-9, "The reference computes the same temperature.");
    }

    [Fact]
    public void TheNeutralOfTheCameraIsWhite()
    {
        // A pixel whose camera values are the neutral renders as the white of D65: (1, 1, 1)
        var neutral = new[] { 0.5, 1, 0.7 };
        var samples = new double[16 * 16 * 3];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = Math.Round(neutral[i % 3] * 60000, MidpointRounding.AwayFromZero);
        }

        var builder = new DngFileBuilder { Width = 16, Height = 16, Photometric = 34892, SamplesPerPixel = 3, Samples = samples, WhiteLevel = [60000, 60000, 60000], ColorMatrix1 = DngScenes.CameraMatrixD65, AsShotNeutral = neutral };
        using var image = Image.Load<RgbaFloat>(builder.Build());
        var pixel = GetPixels(image)[0];
        Assert.True(Math.Abs(pixel.R - 1) < 1e-5 && Math.Abs(pixel.G - 1) < 1e-5 && Math.Abs(pixel.B - 1) < 1e-5, $"The neutral renders as ({pixel.R}, {pixel.G}, {pixel.B}).");
    }

    [Fact]
    public void TheExposureOfTheProfileScalesTheRendering()
    {
        var plain = DngScenes.Get("bayer-rggb");
        var brighter = Variant(plain, baselineExposure: 1);
        var a = GetPixels(Image.Load<RgbaFloat>(plain.Build()));
        var b = GetPixels(Image.Load<RgbaFloat>(brighter.Build()));
        for (var i = 0; i < a.Length; i++)
        {
            Assert.True(Math.Abs((a[i].G * 2) - b[i].G) <= a[i].G * 1e-5f, "One stop doubles the linear values.");
        }
    }

    [Theory]
    [InlineData(32767, "BitsPerSample", 32)]
    [InlineData(259, "Compression", 5)]
    [InlineData(262, "PhotometricInterpretation", 2)]
    public void UnsupportedVariantsOfTheRawImageAreReportedForDng(ushort tag, string name, int value)
    {
        _ = name;
        var builder = Variant(DngScenes.Get("bayer-rggb"));
        if (tag == 32767)
        {
            builder.OmittedRawTags.Add(258);
            builder.ExtraRawTags.Add(DngTag.Shorts(258, (ushort)value));
        }
        else
        {
            builder.OmittedRawTags.Add(tag);
            builder.ExtraRawTags.Add(DngTag.Shorts(tag, (ushort)value));
        }

        var error = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(builder.Build()));
        Assert.Equal(ImageFormat.Dng, error.Format);
    }

    [Fact]
    public void ASubIfdTreeWithACycleIsMalformed()
    {
        // IFD 0 is written last, after the samples: adding an entry to it does not move it
        var first = Variant(DngScenes.Get("bayer-rggb")).Build();
        var ifd0 = BitConverter.ToUInt32(first, 4);
        var builder = Variant(DngScenes.Get("bayer-rggb"));
        builder.ExtraIfd0Tags.Add(DngTag.UInt32s(330, ifd0));
        var data = builder.Build();
        Assert.Equal(ifd0, BitConverter.ToUInt32(data, 4));
        Assert.Equal(ImageFormat.Dng, Assert.Throws<InvalidImageContentException>(() => Image.Identify(data)).Format);
    }

    [Fact]
    public void ToleratesTilesThatAreNotMultiplesOf16()
    {
        var builder = new DngFileBuilder { Width = 30, Height = 21, Samples = DngScenes.Mosaic(30, 21, 2, 2, [0, 1, 1, 2], 0, 65535), Tile = (30, 21), ColorMatrix1 = DngScenes.CameraMatrixD65 };
        var data = builder.Build();
        using var image = Image.Load<RgbaFloat>(data);
        Assert.Equal(ReferenceDng.Render(data).Rgba, GetPixels(image).SelectMany(pixel => new[] { pixel.R, pixel.G, pixel.B, pixel.A }));
    }

    private static DngFileBuilder Variant(DngFileBuilder builder, byte[]? opcodeList2 = null, byte[]? opcodeList3 = null, double? baselineExposure = null) => new()
    {
        Width = builder.Width,
        Height = builder.Height,
        Samples = builder.Samples,
        BlackLevel = builder.BlackLevel,
        WhiteLevel = builder.WhiteLevel,
        ColorMatrix1 = builder.ColorMatrix1,
        AsShotNeutral = builder.AsShotNeutral,
        OpcodeList2 = opcodeList2,
        OpcodeList3 = opcodeList3,
        BaselineExposure = baselineExposure,
    };

    private static RgbaFloat[] GetPixels(Image<RgbaFloat> image)
    {
        var pixels = new RgbaFloat[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        return pixels;
    }

    private static void AssertSamePixels(byte[] expected, byte[] actual)
    {
        using var a = Image.Load<RgbaFloat>(expected);
        using var b = Image.Load<RgbaFloat>(actual);
        Assert.Equal(GetPixels(a), GetPixels(b));
    }
}
