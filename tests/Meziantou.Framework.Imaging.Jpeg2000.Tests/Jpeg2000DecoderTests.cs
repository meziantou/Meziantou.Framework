using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Jpeg2000;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// JPEG 2000 identification and decoding, on codestreams and files written by the reference tools
/// (<see cref="Jpeg2000TestFiles"/>) and on JP2 files assembled around these codestreams: the main header, the samples,
/// the channels, the metadata boxes, truncation and limits.
/// </summary>
public sealed class Jpeg2000DecoderTests
{
    public static TheoryData<string, int, int, PixelFormat, ImageColorModel, int, bool> Headers => new()
    {
        { nameof(Jpeg2000TestFiles.Gray), 5, 3, PixelFormat.Gray8, ImageColorModel.Grayscale, 8, false },
        { nameof(Jpeg2000TestFiles.Rgb), 5, 3, PixelFormat.Rgb24, ImageColorModel.Rgb, 8, false },
        { nameof(Jpeg2000TestFiles.RgbaFile), 5, 3, PixelFormat.Rgba32, ImageColorModel.Rgba, 8, true },
        { nameof(Jpeg2000TestFiles.GrayAlpha), 5, 3, PixelFormat.Rgba32, ImageColorModel.GrayscaleAlpha, 8, true },
        { nameof(Jpeg2000TestFiles.Gray16), 5, 3, PixelFormat.Gray16, ImageColorModel.Grayscale, 16, false },
        { nameof(Jpeg2000TestFiles.Rgb16File), 5, 3, PixelFormat.Rgba64, ImageColorModel.Rgb, 16, false },
        { nameof(Jpeg2000TestFiles.RgbIrreversible), 5, 3, PixelFormat.Rgb24, ImageColorModel.Rgb, 8, false },
        { nameof(Jpeg2000TestFiles.Tiled), 13, 11, PixelFormat.Rgb24, ImageColorModel.Rgb, 8, false },
        { nameof(Jpeg2000TestFiles.Subsampled), 6, 4, PixelFormat.Rgb24, ImageColorModel.YCbCr, 8, false },
        { nameof(Jpeg2000TestFiles.Gray4), 5, 3, PixelFormat.Gray8, ImageColorModel.Grayscale, 4, false },
        { nameof(Jpeg2000TestFiles.Gray12), 5, 3, PixelFormat.Gray16, ImageColorModel.Grayscale, 12, false },
        { nameof(Jpeg2000TestFiles.HighThroughputGray), 5, 3, PixelFormat.Gray8, ImageColorModel.Grayscale, 8, false },
        { nameof(Jpeg2000TestFiles.HighThroughputRgb), 5, 3, PixelFormat.Rgb24, ImageColorModel.Rgb, 8, false },
        { nameof(Jpeg2000TestFiles.HighThroughputLayers), 7, 6, PixelFormat.Gray8, ImageColorModel.Grayscale, 8, false },
    };

    public static TheoryData<string> Files =>
    [
        nameof(Jpeg2000TestFiles.Gray),
        nameof(Jpeg2000TestFiles.Rgb),
        nameof(Jpeg2000TestFiles.RgbaFile),
        nameof(Jpeg2000TestFiles.GrayAlpha),
        nameof(Jpeg2000TestFiles.Gray16),
        nameof(Jpeg2000TestFiles.Rgb16File),
        nameof(Jpeg2000TestFiles.RgbIrreversible),
        nameof(Jpeg2000TestFiles.Tiled),
        nameof(Jpeg2000TestFiles.Subsampled),
        nameof(Jpeg2000TestFiles.Gray4),
        nameof(Jpeg2000TestFiles.Gray12),
        nameof(Jpeg2000TestFiles.HighThroughputGray),
        nameof(Jpeg2000TestFiles.HighThroughputRgb),
        nameof(Jpeg2000TestFiles.HighThroughputLayers),
    ];

    public static TheoryData<string> LosslessEightBitFiles =>
    [
        nameof(Jpeg2000TestFiles.Gray),
        nameof(Jpeg2000TestFiles.Rgb),
        nameof(Jpeg2000TestFiles.RgbaFile),
        nameof(Jpeg2000TestFiles.GrayAlpha),
        nameof(Jpeg2000TestFiles.Tiled),
        nameof(Jpeg2000TestFiles.HighThroughputGray),
        nameof(Jpeg2000TestFiles.HighThroughputRgb),
    ];

    [Theory]
    [MemberData(nameof(Headers))]
    public void IdentifyReadsTheMainHeader(string file, int width, int height, PixelFormat pixelFormat, ImageColorModel colorModel, int bitsPerComponent, bool hasAlpha)
    {
        var data = GetFile(file);
        Assert.Equal(ImageFormat.Jpeg2000, Image.DetectFormat(data));
        foreach (var mode in new[] { ImageIdentifyMode.Header, ImageIdentifyMode.FullScan })
        {
            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = mode });
            Assert.Equal(ImageFormat.Jpeg2000, info.Format);
            Assert.Equal(ImageFormat.Jpeg2000, info.Metadata.SourceFormat);
            Assert.Equal((width, height), (info.Width, info.Height));
            Assert.Equal(pixelFormat, info.PixelFormat);
            Assert.Equal(colorModel, info.ColorModel);
            Assert.Equal(bitsPerComponent, info.BitsPerComponent);
            Assert.Equal(hasAlpha, info.MayHaveTransparency);
            Assert.Equal(1, info.FrameCount);
            Assert.False(info.IsAnimated);
            Assert.False(info.HasPosterFrame);
            Assert.Null(info.Animation);
            Assert.Null(info.Metadata.IccProfile);
            Assert.Null(info.Metadata.Cicp);
            Assert.Equal(mode, info.IdentifyMode);
        }
    }

    [Theory]
    [MemberData(nameof(LosslessEightBitFiles))]
    public void LosslessEightBitFilesDecodeToTheSamplesGivenToTheEncoder(string file)
    {
        using var image = Image.Load(GetFile(file));
        var pixels = GetBytes(image);
        var expected = file switch
        {
            // The gray level and its opacity of each pixel become a gray RGBA pixel
            nameof(Jpeg2000TestFiles.GrayAlpha) => ExpandGrayAlpha(Jpeg2000TestFiles.GetPattern8(image.Width * image.Height * 2)),
            _ => Jpeg2000TestFiles.GetPattern8(pixels.Length),
        };

        Assert.Equal(expected, pixels);

        static byte[] ExpandGrayAlpha(byte[] samples)
        {
            var result = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length / 2; i++)
            {
                result.AsSpan(4 * i, 3).Fill(samples[2 * i]);
                result[(4 * i) + 3] = samples[(2 * i) + 1];
            }

            return result;
        }
    }

    [Fact]
    public void LosslessSixteenBitFilesDecodeToTheSamplesGivenToTheEncoder()
    {
        using (var gray = Image.Load(Jpeg2000TestFiles.Gray16))
        {
            Assert.Equal(PixelFormat.Gray16, gray.PixelFormat);
            Assert.Equal(Jpeg2000TestFiles.GetPattern16(15), GetWords(gray));
        }

        // There is no 16-bit RGB pixel format: the image is opaque RGBA
        using var color = Image.Load(Jpeg2000TestFiles.Rgb16File);
        Assert.Equal(PixelFormat.Rgba64, color.PixelFormat);
        var samples = Jpeg2000TestFiles.GetPattern16(45);
        var expected = new ushort[60];
        for (var i = 0; i < 15; i++)
        {
            samples.AsSpan(3 * i, 3).CopyTo(expected.AsSpan(4 * i));
            expected[(4 * i) + 3] = ushort.MaxValue;
        }

        Assert.Equal(expected, GetWords(color));
    }

    [Fact]
    public void OtherBitDepthsAreScaledExactly()
    {
        // 4 bits become 8 bits and 12 bits become 16 bits: (v * targetMax + max / 2) / max
        using (var four = Image.Load(Jpeg2000TestFiles.Gray4))
        {
            var expected = new byte[15];
            for (var i = 0; i < 15; i++)
            {
                expected[i] = (byte)(((((i * 7) + 3) % 16) * 255 + 7) / 15);
            }

            Assert.Equal(PixelFormat.Gray8, four.PixelFormat);
            Assert.Equal(expected, GetBytes(four));
        }

        using var twelve = Image.Load(Jpeg2000TestFiles.Gray12);
        var words = new ushort[15];
        for (var i = 0; i < 15; i++)
        {
            words[i] = (ushort)(((((i * 331) + 5) % 4096) * 65535 + 2047) / 4095);
        }

        Assert.Equal(PixelFormat.Gray16, twelve.PixelFormat);
        Assert.Equal(words, GetWords(twelve));
    }

    [Fact]
    public void ScaleTablesGiveTheScaledValueOfEverySample()
    {
        // The row writer scales through a table when the image has enough pixels: every depth below the 8-bit and 16-bit
        // targets, every value of the depth
        foreach (var targetMax in new[] { (int)byte.MaxValue, ushort.MaxValue })
        {
            for (var precision = 1; precision < (targetMax == byte.MaxValue ? 8 : 16); precision++)
            {
                var count = 1 << precision;
                var expected = new int[count];
                for (var value = 0; value < count; value++)
                {
                    expected[value] = (int)((((long)value * targetMax) + ((count - 1) / 2)) / (count - 1));
                }

                var divided = Enumerable.Range(0, count).ToArray();
                Jpeg2000RowWriter.Scale(divided, precision, targetMax);
                var table = new ushort[count];
                Jpeg2000RowWriter.BuildScaleTable(table, precision, targetMax);
                var tabulated = Enumerable.Range(0, count).ToArray();
                Jpeg2000RowWriter.Scale(tabulated, table);
                Assert.Equal(expected, divided, message: $"{precision} bits to {targetMax}, division");
                Assert.Equal(expected, tabulated, message: $"{precision} bits to {targetMax}, table");
            }
        }
    }

    [Fact]
    public void SubsampledColumnsTakeTheSampleUnderEachPixel()
    {
        // The pixel at the column x of the reference grid takes the sample floor(x / XRsiz) of the tile-component, clamped
        // to its first and last samples
        for (var subsampling = 1; subsampling <= 5; subsampling++)
        {
            for (var left = 0L; left <= 13; left++)
            {
                for (var width = 1; width <= 23; width++)
                {
                    var first = (left + subsampling - 1) / subsampling;
                    var end = (left + width + subsampling - 1) / subsampling;
                    for (var x0 = Math.Max(0, first - 1); x0 <= first + 1; x0++)
                    {
                        for (var sampleCount = 1; sampleCount <= end - first + 1; sampleCount++)
                        {
                            var samples = Enumerable.Range(100, sampleCount).ToArray();
                            var expected = new int[width];
                            for (var i = 0; i < width; i++)
                            {
                                expected[i] = samples[(int)(Math.Clamp((left + i) / subsampling, x0, x0 + sampleCount - 1) - x0)];
                            }

                            var actual = new int[width];
                            Jpeg2000RowWriter.ReplicateColumns(samples, actual, left, subsampling, x0);
                            Assert.Equal(expected, actual, message: $"XRsiz {subsampling}, left {left}, width {width}, x0 {x0}, {sampleCount} samples");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void IrreversibleFilesDecodeToTheSamplesOfTheReferenceDecoders()
    {
        // The file is coded at its full rate with the 9-7 filter: what it lost is at most two levels. The expectation is
        // what opj_decompress and FFmpeg both decode from it, as the difference from the samples given to the encoder
        ReadOnlySpan<sbyte> differences = [0, 1, 0, 0, -1, 1, 0, 0, 1, 1, 0, 0, -1, 0, -1, -1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1, 0, 0, -2, 0, 0];
        using var image = Image.Load(Jpeg2000TestFiles.RgbIrreversible);
        var pixels = GetBytes(image);
        var expected = Jpeg2000TestFiles.GetPattern8(45);
        for (var i = 0; i < expected.Length; i++)
        {
            // The irreversible path is compared with a tolerance of one level: its arithmetic is not the one of the references
            Assert.True(Math.Abs(pixels[i] - (expected[i] + differences[i])) <= 1, $"sample {i}: expected {expected[i] + differences[i]}, got {pixels[i]}");
        }
    }

    [Fact]
    public void SubsampledChrominancesAreReplicatedAndConvertedToRgb()
    {
        // Three components whose last two are sub-sampled alike are a luminance and two chrominances (sYCC). The
        // expectation follows the documented conversion, computed here with decimal numbers
        using var image = Image.Load(Jpeg2000TestFiles.Subsampled);
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        var pixels = GetBytes(image);
        var luminance = Jpeg2000TestFiles.GetPattern8(24);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                var chrominanceIndex = ((y / 2) * 3) + (x / 2);
                var blueDifference = (((chrominanceIndex * 53) + 7) % 256) - 128;
                var redDifference = (((chrominanceIndex * 71) + 19) % 256) - 128;
                decimal luma = luminance[(y * 6) + x];
                var red = Round(luma + (91881m * redDifference / 65536m));
                var green = Round(luma - (22553m * blueDifference / 65536m) - (46802m * redDifference / 65536m));
                var blue = Round(luma + (116130m * blueDifference / 65536m));
                Assert.Equal([red, green, blue], pixels.AsSpan(((y * 6) + x) * 3, 3).ToArray(), message: $"pixel ({x}, {y})");
            }
        }

        static byte Round(decimal value) => (byte)Math.Clamp(Math.Floor(value + 0.5m), 0, 255);
    }

    [Fact]
    public void SignedSamplesAreNotSupported()
    {
        var data = Jpeg2000TestFiles.Signed;
        Assert.Equal("signed samples", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data)).Feature);
        Assert.Equal("signed samples", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task EveryInputVariantLoadsTheSameImage(string file)
    {
        var data = GetFile(file);
        using var expected = Image.Load<Rgba64>(data);
        var expectedPixels = GetPixels(expected.Frames[0]);
        foreach (var variant in InputVariants.All)
        {
            using var image = await InputVariants.LoadAsync<Rgba64>(variant, data, ImageFormat.Jpeg2000, options: null, XunitCancellationToken);
            Assert.Equal(expectedPixels, GetPixels(image.Frames[0]));
            var info = await InputVariants.IdentifyAsync(variant, data, ImageFormat.Jpeg2000, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }, XunitCancellationToken);
            Assert.Equal((expected.Width, expected.Height), (info.Width, info.Height));
            await InputVariants.AssertEncodedByteLimitBoundaryAsync(variant, data, ImageFormat.Jpeg2000, XunitCancellationToken);
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task ReadersProduceTheSameFramesAsEagerLoads(string file)
    {
        var data = GetFile(file);
        using var eager = Image.Load<Rgba64>(data);
        using var stream = new MemoryStream(data);
        await using var reader = await Image.OpenReaderAsync<Rgba64>(stream, cancellationToken: XunitCancellationToken);
        Assert.Equal((eager.Width, eager.Height), (reader.Info.Width, reader.Info.Height));
        using var frame = await reader.ReadFrameAsync(XunitCancellationToken);
        Assert.NotNull(frame);
        Assert.Equal(GetPixels(eager.Frames[0]), GetPixels(frame.Frames[0]));
        Assert.Null(await reader.ReadFrameAsync(XunitCancellationToken));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void TruncationIsNeverACleanEnd(string file)
    {
        // A full scan and a load need every byte of these files: each proper prefix that is still recognized is invalid
        var data = GetFile(file);
        var options = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan };
        for (var length = 12; length < data.Length; length++)
        {
            var prefix = data.AsSpan(0, length).ToArray();
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(prefix, options));
            using var stream = new MemoryStream(prefix);
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(stream, options));
            Assert.Throws<InvalidImageContentException>(() => Image.Load(prefix));
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void AHeaderIdentificationStopsBeforeTheTiles(string file)
    {
        // The SOT marker of the first tile-part ends the main header: the bytes that follow it are not needed
        var data = GetFile(file);
        var firstTilePart = data.AsSpan().IndexOf([(byte)0xFF, (byte)0x90]);
        var info = Image.Identify(data.AsSpan(0, firstTilePart + 2).ToArray());
        Assert.Equal(ImageIdentifyMode.Header, info.IdentifyMode);
        Assert.Equal(Image.Identify(data).Size, info.Size);
    }

    [Fact]
    public void LimitsAreCheckedOnTheMainHeader()
    {
        var data = Jpeg2000TestFiles.Rgb;
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, Limits(new ImageResourceLimits { MaxWidth = 4 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.Height, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, Limits(new ImageResourceLimits { MaxHeight = 2 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.FramePixels, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, Limits(new ImageResourceLimits { MaxFramePixels = 14 }))).Kind);
        _ = Image.Identify(data, Limits(new ImageResourceLimits { MaxWidth = 5, MaxHeight = 3, MaxFramePixels = 15 }));
        _ = Image.Identify(data, Limits(new ImageResourceLimits { MaxFrames = 1 }, ImageIdentifyMode.FullScan));
    }

    [Fact]
    public void ContainerRulesAreEnforced()
    {
        var codestream = Jpeg2000TestFiles.Rgb;
        var header = Jp2FileBuilder.Box("jp2h", Jp2FileBuilder.ImageHeader(5, 3, 3, 7), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb));
        var codestreamBox = Jp2FileBuilder.Box("jp2c", codestream);

        // The reference: a file type box of the brand 'jp2 ', the header box, the codestream box
        using (var image = Image.Load(Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), Jp2FileBuilder.FileType("jp2 ", "jp2 "), header, codestreamBox)))
        {
            Assert.Equal(Jpeg2000TestFiles.GetPattern8(45), GetBytes(image));
        }

        // A compatible brand is enough; the extended format with one codestream is read
        _ = Image.Identify(Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), Jp2FileBuilder.FileType("xyz ", "abc ", "jp2 "), header, codestreamBox));
        _ = Image.Identify(Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), Jp2FileBuilder.FileType("jpx ", "jpx "), header, codestreamBox));

        // The file type box follows the signature box, and names a brand of still images
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), header, codestreamBox)));
        Assert.Equal("brand 'mjp2'", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), Jp2FileBuilder.FileType("mjp2", "mjp2"), header, codestreamBox))).Feature);
        Assert.Equal("brand 'jpm '", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), Jp2FileBuilder.FileType("jpm ", "abc "), header, codestreamBox))).Feature);

        // The header box precedes the codestream box, once; there is one codestream
        var fileType = Jp2FileBuilder.FileType("jp2 ", "jp2 ");
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, codestreamBox)));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, header, header, codestreamBox)));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, header, codestreamBox, header), FullScan));
        Assert.Equal("several codestreams", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, header, codestreamBox, codestreamBox), FullScan)).Feature);

        // The image header box is required and agrees with the codestream; the color specification box is required
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.File(codestream, Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb))));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.File(codestream, Jp2FileBuilder.ImageHeader(5, 3, 3, 7))));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.File(codestream, Jp2FileBuilder.ImageHeader(6, 3, 3, 7), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb))));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.File(codestream, Jp2FileBuilder.ImageHeader(5, 3, 4, 7), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb))));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.File(codestream, Jp2FileBuilder.ImageHeader(5, 3, 3, 7, compressionType: 6), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb))));

        // A box cannot be smaller than its header, and a codestream box holds its whole codestream
        var badBox = Jp2FileBuilder.Box("free");
        badBox[3] = 7;
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, badBox, header, codestreamBox)));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, header, Jp2FileBuilder.Box("jp2c", codestream.AsSpan(0, codestream.Length - 1).ToArray()), Jp2FileBuilder.Box("free")), FullScan));
    }

    [Fact]
    public async Task BoxesMayUseTheLargeSizeAndTheLastOneMayExtendToTheEndOfTheInput()
    {
        var codestream = Jpeg2000TestFiles.Rgb;
        var fileType = Jp2FileBuilder.FileType("jp2 ", "jp2 ");
        var header = Jp2FileBuilder.Box("jp2h", Jp2FileBuilder.ImageHeader(5, 3, 3, 7), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb));
        var xmp = XmpPacket;
        byte[][] files =
        [
            Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, Jp2FileBuilder.LargeBox("jp2h", Jp2FileBuilder.ImageHeader(5, 3, 3, 7), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb)), Jp2FileBuilder.LargeBox("jp2c", codestream)),
            Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, header, Jp2FileBuilder.BoxToEnd("jp2c", codestream)),
            Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, header, Jp2FileBuilder.Box("jp2c", codestream), Jp2FileBuilder.BoxToEnd("uuid", Jp2FileBuilder.XmpUuid(), xmp)),
            Jp2FileBuilder.Concat(Jp2FileBuilder.Signature(), fileType, header, Jp2FileBuilder.Box("jp2c", codestream), Jp2FileBuilder.BoxToEnd("free", new byte[5])),
        ];

        for (var i = 0; i < files.Length; i++)
        {
            foreach (var variant in InputVariants.All)
            {
                using var image = await InputVariants.LoadAsync(variant, files[i], ImageFormat.Jpeg2000, options: null, XunitCancellationToken);
                Assert.Equal(Jpeg2000TestFiles.GetPattern8(45), GetBytes(image), message: $"file {i}, {variant}");
                Assert.Equal(i == 2 ? xmp.Length : null, image.Metadata.XmpProfile?.Data.Length);
            }
        }
    }

    [Fact]
    public void MetadataBoxesAreRead()
    {
        // The resolution of the header box, then the XMP packet and the EXIF data of the 'uuid' boxes: 3780 grid points
        // per meter are 96.012 dots per inch
        var exif = ExifTiff.CreateMinimal(ExifOrientation.RightTop);
        var xmp = XmpPacket;
        var header = Jp2FileBuilder.Box(
            "jp2h",
            Jp2FileBuilder.ImageHeader(5, 3, 3, 7),
            Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb),
            Jp2FileBuilder.Box("res ", Jp2FileBuilder.Resolution("resd", 1, 1, 0, 1, 1, 0), Jp2FileBuilder.Resolution("resc", 378, 1, 1, 48000, 127, 1)));
        var before = Jp2FileBuilder.Concat(
            Jp2FileBuilder.Signature(),
            Jp2FileBuilder.FileType("jp2 ", "jp2 "),
            header,
            Jp2FileBuilder.Uuid(Jp2FileBuilder.XmpUuid(), xmp),
            Jp2FileBuilder.Uuid(Jp2FileBuilder.ExifUuid(), exif),
            Jp2FileBuilder.Box("xml ", "<a/>"u8.ToArray()),
            Jp2FileBuilder.Box("jp2c", Jpeg2000TestFiles.Rgb));

        // A header identification reads the boxes that precede the codestream
        var info = Image.Identify(before);
        Assert.Equal(xmp, info.Metadata.XmpProfile!.Data.ToArray());
        Assert.Equal(exif, info.Metadata.ExifProfile!.Data.ToArray());
        Assert.Equal(ExifOrientation.RightTop, info.Metadata.Orientation);
        Assert.Equal(new ImageResolution(96, 96012 / 1000.0), info.Metadata.Resolution);

        // The boxes that follow the codestream are read by a full scan and by a load
        var after = Jp2FileBuilder.Concat(
            Jp2FileBuilder.Signature(),
            Jp2FileBuilder.FileType("jp2 ", "jp2 "),
            Jp2FileBuilder.Box("jp2h", Jp2FileBuilder.ImageHeader(5, 3, 3, 7), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb)),
            Jp2FileBuilder.Box("jp2c", Jpeg2000TestFiles.Rgb),
            Jp2FileBuilder.Uuid(Jp2FileBuilder.XmpUuid(), xmp),
            Jp2FileBuilder.Uuid(Jp2FileBuilder.XmpUuid(), "<second/>"u8.ToArray()));
        Assert.Null(Image.Identify(after).Metadata.XmpProfile);
        Assert.Equal(xmp, Image.Identify(after, FullScan).Metadata.XmpProfile!.Data.ToArray());
        using var image = Image.Load(after);
        Assert.Equal(xmp, image.Metadata.XmpProfile!.Data.ToArray());

        // The metadata is charged to its limit before it is kept
        _ = Image.Identify(before, Limits(new ImageResourceLimits { MaxMetadataBytes = xmp.Length + exif.Length }));
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(before, Limits(new ImageResourceLimits { MaxMetadataBytes = xmp.Length + exif.Length - 1 }))).Kind);
    }

    [Fact]
    public void AnIccProfileLabelsTheImage()
    {
        var profile = IccProfile.Srgb.Data.ToArray();
        foreach (var method in new[] { 2, 3 })
        {
            using var image = Image.Load(Jp2FileBuilder.File(Jpeg2000TestFiles.Rgb, Jp2FileBuilder.ImageHeader(5, 3, 3, 7), Jp2FileBuilder.ProfileColor(method, profile)));
            Assert.Equal(profile, image.Metadata.IccProfile!.Data.ToArray());
            Assert.Equal(Jpeg2000TestFiles.GetPattern8(45), GetBytes(image));
        }

        // The color space of the profile says what the colors are: a gray profile leaves two of the three channels undefined
        Assert.Equal("undefined channel", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Jp2FileBuilder.File(Jpeg2000TestFiles.Rgb, Jp2FileBuilder.ImageHeader(5, 3, 3, 7), Jp2FileBuilder.ProfileColor(2, IccProfile.SrgbGray.Data.ToArray())))).Feature);

        // The first color specification that is understood gives the colors: here the second one
        using var second = Image.Load(Jp2FileBuilder.File(Jpeg2000TestFiles.Rgb, Jp2FileBuilder.ImageHeader(5, 3, 3, 7), Jp2FileBuilder.EnumeratedColor(21), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb)));
        Assert.Null(second.Metadata.IccProfile);
        Assert.Equal("enumerated color space 14", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(Jp2FileBuilder.File(Jpeg2000TestFiles.Rgb, Jp2FileBuilder.ImageHeader(5, 3, 3, 7), Jp2FileBuilder.EnumeratedColor(14)))).Feature);
    }

    [Fact]
    public void APaletteGivesTheChannelsOfAnIndexComponent()
    {
        // Four entries of three 8-bit columns, then with a fourth column of 4 bits used as the opacity
        int[][] entries = [[10, 20, 30, 0], [40, 50, 60, 5], [70, 80, 90, 10], [100, 110, 120, 15]];
        var indexes = new int[15];
        for (var i = 0; i < 15; i++)
        {
            indexes[i] = ((i * 3) + 1) % 4;
        }

        var opaque = Jp2FileBuilder.File(
            Jpeg2000TestFiles.Index,
            Jp2FileBuilder.ImageHeader(5, 3, 1, 1),
            Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb),
            Jp2FileBuilder.Palette([7, 7, 7], entries),
            Jp2FileBuilder.ComponentMapping((0, 1, 0), (0, 1, 1), (0, 1, 2)));
        var info = Image.Identify(opaque);
        Assert.Equal((PixelFormat.Rgb24, ImageColorModel.Indexed, 8, false), (info.PixelFormat, info.ColorModel, info.BitsPerComponent, info.MayHaveTransparency));
        using (var image = Image.Load(opaque))
        {
            var pixels = GetBytes(image);
            for (var i = 0; i < 15; i++)
            {
                Assert.Equal(entries[indexes[i]].AsSpan(0, 3).ToArray(), [pixels[3 * i], pixels[(3 * i) + 1], pixels[(3 * i) + 2]]);
            }
        }

        var transparent = Jp2FileBuilder.File(
            Jpeg2000TestFiles.Index,
            Jp2FileBuilder.ImageHeader(5, 3, 1, 1),
            Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb),
            Jp2FileBuilder.Palette([7, 7, 7, 3], entries),
            Jp2FileBuilder.ComponentMapping((0, 1, 0), (0, 1, 1), (0, 1, 2), (0, 1, 3)),
            Jp2FileBuilder.ChannelDefinition((0, 0, 1), (1, 0, 2), (2, 0, 3), (3, 1, 0)));
        using (var image = Image.Load(transparent))
        {
            Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
            var pixels = GetBytes(image);
            for (var i = 0; i < 15; i++)
            {
                var entry = entries[indexes[i]];
                Assert.Equal([(byte)entry[0], (byte)entry[1], (byte)entry[2], (byte)(entry[3] * 17)], pixels.AsSpan(4 * i, 4).ToArray());
            }
        }

        // An index outside the palette is an error of the samples: the header is accepted
        var shortPalette = Jp2FileBuilder.File(
            Jpeg2000TestFiles.Index,
            Jp2FileBuilder.ImageHeader(5, 3, 1, 1),
            Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb),
            Jp2FileBuilder.Palette([7, 7, 7], entries.AsSpan(0, 3)),
            Jp2FileBuilder.ComponentMapping((0, 1, 0), (0, 1, 1), (0, 1, 2)));
        _ = Image.Identify(shortPalette, FullScan);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(shortPalette));

        // A palette needs a component mapping, which refers to its columns and to the components of the codestream
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.File(Jpeg2000TestFiles.Index, Jp2FileBuilder.ImageHeader(5, 3, 1, 1), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb), Jp2FileBuilder.Palette([7, 7, 7], entries))));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.File(Jpeg2000TestFiles.Index, Jp2FileBuilder.ImageHeader(5, 3, 1, 1), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb), Jp2FileBuilder.ComponentMapping((0, 0, 0)))));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.File(Jpeg2000TestFiles.Index, Jp2FileBuilder.ImageHeader(5, 3, 1, 1), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb), Jp2FileBuilder.Palette([7, 7, 7], entries), Jp2FileBuilder.ComponentMapping((0, 1, 0), (0, 1, 1), (0, 1, 3)))));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(Jp2FileBuilder.File(Jpeg2000TestFiles.Index, Jp2FileBuilder.ImageHeader(5, 3, 1, 1), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb), Jp2FileBuilder.Palette([7, 7, 7], entries), Jp2FileBuilder.ComponentMapping((1, 1, 0), (0, 1, 1), (0, 1, 2)))));
    }

    [Fact]
    public void ChannelDefinitionsOrderTheColorsAndNameTheOpacity()
    {
        var samples = Jpeg2000TestFiles.GetPattern8(45);

        // The three components are the blue, green and red colors
        using (var image = Image.Load(Jp2FileBuilder.File(Jpeg2000TestFiles.Rgb, Jp2FileBuilder.ImageHeader(5, 3, 3, 7), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceSrgb), Jp2FileBuilder.ChannelDefinition((0, 0, 3), (1, 0, 2), (2, 0, 1)))))
        {
            var pixels = GetBytes(image);
            for (var i = 0; i < 15; i++)
            {
                Assert.Equal([samples[(3 * i) + 2], samples[(3 * i) + 1], samples[3 * i]], pixels.AsSpan(3 * i, 3).ToArray());
            }
        }

        // The first component of a gray image is its opacity
        var pairs = Jpeg2000TestFiles.GetPattern8(30);
        using (var image = Image.Load(Jp2FileBuilder.File(Jpeg2000TestFiles.GrayAlpha, Jp2FileBuilder.ImageHeader(5, 3, 2, 7), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceGray), Jp2FileBuilder.ChannelDefinition((0, 1, 0), (1, 0, 1)))))
        {
            var pixels = GetBytes(image);
            for (var i = 0; i < 15; i++)
            {
                Assert.Equal([pairs[(2 * i) + 1], pairs[(2 * i) + 1], pairs[(2 * i) + 1], pairs[2 * i]], pixels.AsSpan(4 * i, 4).ToArray());
            }
        }

        // What an image cannot hold is rejected by the header
        Assert.Equal("premultiplied alpha", UnsupportedDefinition((0, 0, 1), (1, 2, 0)));
        Assert.Equal("opacity of a single color", UnsupportedDefinition((0, 0, 1), (1, 1, 1)));
        Assert.Equal("channel type", UnsupportedDefinition((0, 0, 1), (1, 65535, 65535)));
        Assert.Equal("undefined channel", UnsupportedDefinition((0, 0, 1)));
        Assert.Equal("unassociated color channel", UnsupportedDefinition((0, 0, 1), (1, 0, 2)));
        Assert.Equal("several opacity channels", UnsupportedDefinition((0, 1, 0), (1, 1, 0)));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(GrayAlphaFile((1, 1, 0))));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(GrayAlphaFile((0, 0, 1), (0, 1, 0))));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(GrayAlphaFile((0, 0, 1), (2, 1, 0))));

        static string? UnsupportedDefinition(params ReadOnlySpan<(int Channel, int Type, int Association)> channels)
        {
            var file = GrayAlphaFile(channels);
            return Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(file)).Feature;
        }

        static byte[] GrayAlphaFile(params ReadOnlySpan<(int Channel, int Type, int Association)> channels)
            => Jp2FileBuilder.File(Jpeg2000TestFiles.GrayAlpha, Jp2FileBuilder.ImageHeader(5, 3, 2, 7), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceGray), Jp2FileBuilder.ChannelDefinition(channels));
    }

    [Fact]
    public void CmykIsOnlyLoadedWithAConversion()
    {
        // Four inks, 0 for none: identified as they are, rejected by a load without a conversion
        var file = Jp2FileBuilder.File(Jpeg2000TestFiles.FourComponents, Jp2FileBuilder.ImageHeader(5, 3, 4, 7), Jp2FileBuilder.EnumeratedColor(Jp2FileBuilder.ColorSpaceCmyk));
        var info = Image.Identify(file, FullScan);
        Assert.Equal((PixelFormat.Rgb24, ImageColorModel.Cmyk, 8, false), (info.PixelFormat, info.ColorModel, info.BitsPerComponent, info.MayHaveTransparency));
        Assert.Equal("JPEG 2000 CMYK", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(file)).Feature);
        Assert.Equal("JPEG 2000 CMYK without color profile", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(file, new ImageDecodeOptions { CmykConversion = new CmykConversionOptions { AssumedSourceProfile = null } })).Feature);

        // The inks go through the conversion of the options, and the image is labelled with its destination profile
        var conversion = new CmykConversionOptions();
        using var image = Image.Load(file, new ImageDecodeOptions { CmykConversion = conversion });
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        Assert.Equal(conversion.DestinationProfile.Data.ToArray(), image.Metadata.IccProfile!.Data.ToArray());
        var transform = IccColorTransform.Create(conversion.AssumedSourceProfile!, conversion.DestinationProfile, conversion.Transform);
        var inks = new byte[60];
        for (var i = 0; i < 15; i++)
        {
            inks[4 * i] = (byte)((i * 37) + 11);
            inks[(4 * i) + 1] = (byte)((i * 41) + 3);
            inks[(4 * i) + 2] = (byte)((i * 43) + 5);
            inks[(4 * i) + 3] = (byte)((i * 13) + 2);
        }

        var expected = new byte[45];
        transform.Convert(inks, expected);
        Assert.Equal(expected, GetBytes(image));
    }

    [Fact]
    public void TheTileGridIsCheckedBeforeAnyTileIsRead()
    {
        // The SIZ marker segment is rewritten to declare an image of 65535x1 in tiles of one sample: 65535 tiles, of which
        // the codestream holds one. The fields follow the marker, its length and Rsiz: Xsiz, Ysiz, the offsets, XTsiz, YTsiz
        var data = Jpeg2000TestFiles.Gray;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 65535);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), 1);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data)).Kind);

        var limits = new ImageResourceLimits { MaxWidth = 65536 };
        Assert.Equal(65535, Image.Identify(data, Limits(limits)).Width);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, Limits(limits, ImageIdentifyMode.FullScan)));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = limits } }));

        // One more tile than the 16-bit index of a tile-part can name
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 65536);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, Limits(limits)));
    }

    private static ImageIdentifyOptions FullScan => new() { Mode = ImageIdentifyMode.FullScan };

    private static byte[] XmpPacket => "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>\n"u8.ToArray();

    private static byte[] GetBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static ushort[] GetWords(Image image) => unsafe(MemoryMarshal.Cast<byte, ushort>(GetBytes(image))).ToArray();

    private static Rgba64[] GetPixels(ImageFrame<Rgba64> frame)
    {
        var pixels = new Rgba64[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }

    private static byte[] GetFile(string name) => name switch
    {
        nameof(Jpeg2000TestFiles.Gray) => Jpeg2000TestFiles.Gray,
        nameof(Jpeg2000TestFiles.Rgb) => Jpeg2000TestFiles.Rgb,
        nameof(Jpeg2000TestFiles.RgbaFile) => Jpeg2000TestFiles.RgbaFile,
        nameof(Jpeg2000TestFiles.GrayAlpha) => Jpeg2000TestFiles.GrayAlpha,
        nameof(Jpeg2000TestFiles.Gray16) => Jpeg2000TestFiles.Gray16,
        nameof(Jpeg2000TestFiles.Rgb16File) => Jpeg2000TestFiles.Rgb16File,
        nameof(Jpeg2000TestFiles.RgbIrreversible) => Jpeg2000TestFiles.RgbIrreversible,
        nameof(Jpeg2000TestFiles.Tiled) => Jpeg2000TestFiles.Tiled,
        nameof(Jpeg2000TestFiles.Subsampled) => Jpeg2000TestFiles.Subsampled,
        nameof(Jpeg2000TestFiles.Gray4) => Jpeg2000TestFiles.Gray4,
        nameof(Jpeg2000TestFiles.Gray12) => Jpeg2000TestFiles.Gray12,
        nameof(Jpeg2000TestFiles.HighThroughputGray) => Jpeg2000TestFiles.HighThroughputGray,
        nameof(Jpeg2000TestFiles.HighThroughputRgb) => Jpeg2000TestFiles.HighThroughputRgb,
        nameof(Jpeg2000TestFiles.HighThroughputLayers) => Jpeg2000TestFiles.HighThroughputLayers,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown file."),
    };

    private static ImageIdentifyOptions Limits(ImageResourceLimits limits, ImageIdentifyMode mode = ImageIdentifyMode.Header)
        => new() { Mode = mode, Configuration = new ImageConfiguration { Limits = limits } };
}
