using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Jpeg2000;
using Meziantou.Framework.Imaging.TestHarness.Streams;

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
        { nameof(Jpeg2000TestFiles.GrayAlpha), 5, 3, PixelFormat.La16, ImageColorModel.GrayscaleAlpha, 8, true },
        { nameof(Jpeg2000TestFiles.Gray16), 5, 3, PixelFormat.Gray16, ImageColorModel.Grayscale, 16, false },
        { nameof(Jpeg2000TestFiles.Rgb16File), 5, 3, PixelFormat.Rgb48, ImageColorModel.Rgb, 16, false },
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

        // The gray level and its opacity of each pixel are a La16 pixel: every format stores the samples as they are
        Assert.Equal(Jpeg2000TestFiles.GetPattern8(pixels.Length), pixels);
    }

    [Fact]
    public void LosslessSixteenBitFilesDecodeToTheSamplesGivenToTheEncoder()
    {
        using (var gray = Image.Load(Jpeg2000TestFiles.Gray16))
        {
            Assert.Equal(PixelFormat.Gray16, gray.PixelFormat);
            Assert.Equal(Jpeg2000TestFiles.GetPattern16(15), GetWords(gray));
        }

        // 16-bit RGB without opacity is Rgb48
        using var color = Image.Load(Jpeg2000TestFiles.Rgb16File);
        Assert.Equal(PixelFormat.Rgb48, color.PixelFormat);
        Assert.Equal(Jpeg2000TestFiles.GetPattern16(45), GetWords(color));
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
            Assert.Equal(PixelFormat.La16, image.PixelFormat);
            for (var i = 0; i < 15; i++)
            {
                Assert.Equal([pairs[(2 * i) + 1], pairs[2 * i]], pixels.AsSpan(2 * i, 2).ToArray());
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

    [Fact]
    public void RegionOfInterestMarkerSegmentsAreValidated()
    {
        // A RGN marker segment (A.6.3) of this codestream of one component: Crgn on one byte, Srgn (0 for the Maxshift
        // method) and SPrgn, the shift. With a shift of 0, every coefficient is in the region already: nothing changes.
        var data = Jpeg2000TestFiles.Gray;
        using var expected = Image.Load(data);
        foreach (var valid in (byte[][])[InsertRgnInMainHeader(data, [0, 0, 0]), InsertRgnInTilePartHeader(data, [0, 0, 0])])
        {
            using var image = Image.Load(valid);
            Assert.Equal(GetBytes(expected), GetBytes(image));
        }

        // A segment one byte too short or too long
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(InsertRgnInMainHeader(data, [0, 0])));
        Assert.Contains("the length of the RGN marker segment is not the one of its fields", exception.Message);
        exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(InsertRgnInTilePartHeader(data, [0, 0, 3, 0])));
        Assert.Contains("the length of the RGN marker segment is not the one of its fields", exception.Message);

        // A component the image does not have
        exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(InsertRgnInMainHeader(data, [1, 0, 3])));
        Assert.Contains("a RGN marker segment names the component 1; the image has 1", exception.Message);

        // A style other than Maxshift (the only one T.800 defines; the others are of Part 2)
        Assert.Equal("region of interest style", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(InsertRgnInMainHeader(data, [0, 1, 3]))).Feature);
        Assert.Equal("region of interest style", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(InsertRgnInTilePartHeader(data, [0, 1, 3]))).Feature);

        // Two segments for the same component in one header; one in each header is allowed (the tile-part overrides)
        exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(InsertRgnInMainHeader(InsertRgnInMainHeader(data, [0, 0, 0]), [0, 0, 0])));
        Assert.Contains("the main header has two RGN marker segments for the component 0", exception.Message);
        exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(InsertRgnInTilePartHeader(InsertRgnInTilePartHeader(data, [0, 0, 0]), [0, 0, 0])));
        Assert.Contains("a tile-part header has two RGN marker segments for the component 0", exception.Message);
        using var both = Image.Load(InsertRgnInTilePartHeader(InsertRgnInMainHeader(data, [0, 0, 0]), [0, 0, 0]));
        Assert.Equal(GetBytes(expected), GetBytes(both));

        static byte[] InsertRgnInMainHeader(byte[] codestream, byte[] content)
        {
            // After the SIZ marker segment, which follows the SOC marker
            var end = 4 + BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(4));
            return InsertRgn(codestream, end, content);
        }

        static byte[] InsertRgnInTilePartHeader(byte[] codestream, byte[] content)
        {
            // After the SOT marker segment of the only tile-part, whose length (Psot) grows with it
            var sot = codestream.AsSpan().IndexOf([(byte)0xFF, (byte)0x90]);
            var result = InsertRgn(codestream, sot + 12, content);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(sot + 6), BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(sot + 6)) + 4 + (uint)content.Length);
            return result;
        }

        static byte[] InsertRgn(byte[] codestream, int position, byte[] content)
        {
            var segment = new byte[4 + content.Length];
            segment[0] = 0xFF;
            segment[1] = 0x5E;
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(2 + content.Length));
            content.CopyTo(segment, 4);
            return [.. codestream.AsSpan(0, position), .. segment, .. codestream.AsSpan(position)];
        }
    }

    /// <summary>
    /// The code-blocks of a tile of at least 65,536 pixels are decoded by several workers, and its planes transformed by
    /// rows and strips of columns: the samples are those of one worker, for both wavelets, one to four components, both
    /// depths and sizes that leave partial code-blocks and strips.
    /// </summary>
    [Theory]
    [InlineData(PixelFormat.Rgb24, Jpeg2000Compression.Lossless, 331, 223)]
    [InlineData(PixelFormat.Rgb24, Jpeg2000Compression.Lossy, 331, 223)]
    [InlineData(PixelFormat.Gray8, Jpeg2000Compression.Lossless, 401, 170)]
    [InlineData(PixelFormat.Gray8, Jpeg2000Compression.Lossy, 170, 401)]
    [InlineData(PixelFormat.Rgba32, Jpeg2000Compression.Lossy, 257, 256)]
    [InlineData(PixelFormat.Rgba64, Jpeg2000Compression.Lossless, 300, 230)]
    public void CodeBlocksDecodedByWorkersGiveTheSamplesOfOneWorker(PixelFormat format, Jpeg2000Compression compression, int width, int height)
    {
        var data = EncodeLargeImage(format, compression, width, height, seed: width + (int)format);
        using var expected = Image.Load(data, WithWorkers(1));
        var expectedBytes = GetBytes(expected);
        foreach (var workers in (int[])[2, 3, 16])
        {
            using var decoded = Image.Load(data, WithWorkers(workers));
            Assert.True(expectedBytes.AsSpan().SequenceEqual(GetBytes(decoded)), $"{format} {compression} {width}x{height}: {workers} workers decode other samples.");
        }
    }

    /// <summary>
    /// The bytes of a code-block are not checked: a corrupted code-block decodes to other coefficients, and the result, a
    /// failure included, is the one of one worker.
    /// </summary>
    [Theory]
    [InlineData(Jpeg2000Compression.Lossless)]
    [InlineData(Jpeg2000Compression.Lossy)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void CorruptedCodeBlocksDecodeAsWithOneWorker(Jpeg2000Compression compression)
    {
        var data = EncodeLargeImage(PixelFormat.Rgb24, compression, 320, 240, seed: 3);
        var body = data.AsSpan().IndexOf([(byte)0xFF, (byte)0x93]) + 2;
        var random = new Random(11);
        foreach (var position in (double[])[0.1, 0.4, 0.75])
        {
            var corrupted = (byte[])data.Clone();
            var start = body + (int)((data.Length - body) * position);
            for (var i = start; i < start + 48; i += 3)
            {
                // A value below FF keeps the bytes free of markers
                corrupted[i] = (byte)random.Next(0x90);
            }

            var expected = Decode(corrupted, workers: 1);
            Assert.NotEqual(Decode(data, workers: 1), expected);
            foreach (var workers in (int[])[2, 16])
            {
                Assert.Equal(expected, Decode(corrupted, workers));
            }
        }

        static string Decode(byte[] data, int workers)
        {
            try
            {
                using var image = Image.Load(data, WithWorkers(workers));
                return Convert.ToHexString(SHA256.HashData(GetBytes(image)));
            }
            catch (ImageException exception)
            {
                return $"{exception.GetType().Name}: {exception.Message}";
            }
        }
    }

    /// <summary>
    /// The segmentation symbols that the code-block style announces are missing from every code-block of a codestream of
    /// the encoder: several workers fail with the error of one worker.
    /// </summary>
    [Fact]
    public void ACodeBlockThatFailsFailsTheDecodingWhateverTheWorkers()
    {
        var data = EncodeLargeImage(PixelFormat.Rgb24, Jpeg2000Compression.Lossless, 300, 240, seed: 5);

        // COD: the marker, Lcod, Scod, the progression order, the number of layers, the component transformation, the
        // number of decomposition levels and the two sizes of the code-blocks, then their style
        var cod = data.AsSpan().IndexOf([(byte)0xFF, (byte)0x52]);
        data[cod + 12] |= 0x20;
        var expected = Assert.Throws<InvalidImageContentException>(() => Image.Load(data, WithWorkers(1)).Dispose()).Message;
        foreach (var workers in (int[])[2, 3, 16])
        {
            Assert.Equal(expected, Assert.Throws<InvalidImageContentException>(() => Image.Load(data, WithWorkers(workers)).Dispose()).Message);
        }
    }

    [Fact]
    public void ATileGetsSeveralWorkersFromItsSizeAndItsCodeBlocks()
    {
        Assert.Equal(1, Jpeg2000Workers.GetWorkerCount(16, RowBands.MinPixelsForParallelism - 1, 100));
        Assert.Equal(16, Jpeg2000Workers.GetWorkerCount(16, RowBands.MinPixelsForParallelism, 100));
        Assert.Equal(1, Jpeg2000Workers.GetWorkerCount(1, 1L << 30, 100));
        Assert.Equal(1, Jpeg2000Workers.GetWorkerCount(16, 1L << 30, 1));
        Assert.Equal(5, Jpeg2000Workers.GetWorkerCount(16, 1L << 30, 5));

        // A pass of the transformations is split into bands of at least the given number of rows
        Assert.Equal(1, Jpeg2000Workers.GetBandCount(8, RowBands.MinPixelsForParallelism - 1, 1000, 32));
        Assert.Equal(3, Jpeg2000Workers.GetBandCount(8, RowBands.MinPixelsForParallelism, 100, 32));
        Assert.Equal(8, Jpeg2000Workers.GetBandCount(8, RowBands.MinPixelsForParallelism, 1000, 32));
    }

    /// <summary>
    /// The error of the code-blocks decoded by several workers is the one of the first code-block that fails, a
    /// cancellation first; the code-blocks after a failure are skipped and the ones before it all run.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void WorkersFailWithTheErrorOfTheFirstFailingUnit(int workers)
    {
        foreach (var interleaved in (bool[])[false, true])
        {
            var ran = new int[40];
            var exception = Assert.Throws<InvalidImageContentException>(() => Run(interleaved, workers, 40, (_, unit) =>
            {
                Interlocked.Increment(ref ran[unit]);
                if (unit is 7 or 23 or 31)
                    throw new InvalidImageContentException($"unit {unit}");
            }));
            Assert.Equal("unit 7", exception.Message);
            Assert.All(ran.AsSpan(0, 8).ToArray(), count => Assert.Equal(1, count));
            Assert.All(ran, count => Assert.InRange(count, 0, 1));

            // A unit that fails after a cancellation does not hide it
            using var cancellation = new CancellationTokenSource();
            Assert.Throws<OperationCanceledException>(() => Run(interleaved, workers, 40, (_, unit) =>
            {
                if (unit is 2)
                {
                    cancellation.Cancel();
                }

                if (unit is 10)
                    throw new InvalidImageContentException("unit 10");

                cancellation.Token.ThrowIfCancellationRequested();
            }));
        }

        static void Run(bool interleaved, int workers, int count, Action<int, int> body)
        {
            if (interleaved)
            {
                Jpeg2000Workers.RunInterleaved(workers, count, body);
            }
            else
            {
                Jpeg2000Workers.Run(workers, count, body);
            }
        }
    }

    /// <summary>
    /// The decoding is canceled once the input is read, before the tile is decoded: its few packets do not check the
    /// token, the code-blocks do, by one worker and by several.
    /// </summary>
    [Fact]
    public async Task ADecodingCanceledBeforeTheCodeBlocksThrowsWhateverTheWorkers()
    {
        var data = EncodeLargeImage(PixelFormat.Rgb24, Jpeg2000Compression.Lossless, 300, 240, seed: 7);
        foreach (var workers in (int[])[1, 16])
        {
            using var cancellation = new CancellationTokenSource();
            await using var stream = new TestInputStream(data) { CancellationSource = cancellation, CancelAtPosition = data.Length };
            await Assert.ThrowsAny<OperationCanceledException>(async () => (await Image.LoadAsync(stream, WithWorkers(workers), cancellation.Token)).Dispose());
        }
    }

    /// <summary>The planes of a tile are charged to the live-allocation limit before any worker starts: a limit too small for them fails the same way.</summary>
    [Fact]
    public void ALiveAllocationLimitFailsTheSameWayWhateverTheWorkers()
    {
        var data = EncodeLargeImage(PixelFormat.Rgb24, Jpeg2000Compression.Lossless, 300, 240, seed: 9);
        foreach (var workers in (int[])[1, 16])
        {
            var options = new ImageDecodeOptions { Configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers, Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 512 * 1024 } } };
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, options).Dispose()).Kind);
        }
    }

    /// <summary>
    /// A scaled decode discards the highest resolution levels: the image has the size of a lower resolution level of the
    /// reference grid (B-14), up to the decomposition levels of the main header. OpenJPEG decodes the same samples with
    /// <c>-r</c> in the interop job.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(128, 96)]
    [InlineData(77, 33)]
    public void AScaledDecodeHasTheSizeOfALowerResolutionLevel(int tileWidth, int tileHeight)
    {
        var data = EncodeImage(301, 213, new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy, TileWidth = tileWidth, TileHeight = tileHeight }, seed: tileWidth);
        var levels = ReferenceJpeg2000.Parse(data).CodingStyles[0].DecompositionLevels;
        using var full = Image.Load(data);
        for (var reduction = 1; reduction <= levels; reduction++)
        {
            var denominator = 1 << reduction;
            using var scaled = Image.Load(data, new ImageDecodeOptions { ScaleDenominator = denominator });
            Assert.Equal(((301 + denominator - 1) / denominator, (213 + denominator - 1) / denominator), (scaled.Width, scaled.Height));
            Assert.Equal(full.PixelFormat, scaled.PixelFormat);

            // The low-pass sub-band has the colors of the image: the mean of a scaled image is close to the full one
            Assert.InRange(Math.Abs(GetMean(scaled) - GetMean(full)), 0, 8);
        }

        var tooSmall = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data, new ImageDecodeOptions { ScaleDenominator = 2 << levels }).Dispose());
        Assert.Contains("decomposition levels", tooSmall.Message, StringComparison.Ordinal);

        // A target size takes the largest denominator whose size is still large enough: 38x27 is not
        using var thumbnail = Image.Load(data, new ImageDecodeOptions { TargetSize = new Size(40, 20) });
        Assert.Equal((76, 54), (thumbnail.Width, thumbnail.Height));
        using var smallest = Image.Load(data, new ImageDecodeOptions { TargetSize = new Size(1, 1) });
        Assert.Equal(((301 + (1 << levels) - 1) >> levels, (213 + (1 << levels) - 1) >> levels), (smallest.Width, smallest.Height));

        static double GetMean(Image image) => GetBytes(image).Average(value => (double)value);
    }

    /// <summary>
    /// A region is decoded from the tiles it meets and the code-blocks that reach it: its samples are those of a full
    /// decode, at every scale, with one worker or several, on the edges of the image and of the tiles.
    /// </summary>
    [Theory]
    [InlineData(Jpeg2000Compression.Lossless, 0, 0)]
    [InlineData(Jpeg2000Compression.Lossless, 100, 64)]
    [InlineData(Jpeg2000Compression.Lossy, 0, 0)]
    [InlineData(Jpeg2000Compression.Lossy, 128, 128)]
    public void ARegionHasTheSamplesOfAFullDecode(Jpeg2000Compression compression, int tileWidth, int tileHeight)
    {
        var data = EncodeImage(300, 230, new Jpeg2000Encoder { Compression = compression, TileWidth = tileWidth, TileHeight = tileHeight }, seed: 3);
        Rectangle[] regions = [new(0, 0, 300, 230), new(0, 0, 1, 1), new(299, 229, 1, 1), new(101, 63, 77, 41), new(250, 10, 400, 30), new(63, 0, 2, 230), new(128, 128, 64, 64)];
        foreach (var denominator in (int[])[1, 2, 8])
        {
            foreach (var workers in (int[])[1, 16])
            {
                using var full = Image.Load<Rgba32>(data, new ImageDecodeOptions { ScaleDenominator = denominator });
                foreach (var region in regions)
                {
                    var context = $"{compression}, tiles {tileWidth}x{tileHeight}, 1/{denominator}, {workers} workers, region {region}";
                    using var decoded = Image.Load<Rgba32>(data, new ImageDecodeOptions { ScaleDenominator = denominator, Region = region, Configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers } });

                    // The region is cut to the image, then divided like the image; at least one pixel is kept
                    var x0 = (region.X + denominator - 1) / denominator;
                    var y0 = (region.Y + denominator - 1) / denominator;
                    var x1 = (Math.Min(region.Right, 300) + denominator - 1) / denominator;
                    var y1 = (Math.Min(region.Bottom, 230) + denominator - 1) / denominator;
                    x0 = x1 > x0 ? x0 : Math.Min(x0, full.Width - 1);
                    y0 = y1 > y0 ? y0 : Math.Min(y0, full.Height - 1);
                    x1 = Math.Max(x1, x0 + 1);
                    y1 = Math.Max(y1, y0 + 1);
                    Assert.Equal((x1 - x0, y1 - y0), (decoded.Width, decoded.Height));
                    for (var y = 0; y < decoded.Height; y++)
                    {
                        for (var x = 0; x < decoded.Width; x++)
                        {
                            if (decoded.Frames[0][x, y] != full.Frames[0][x0 + x, y0 + y])
                            {
                                Assert.Fail($"{context}: the pixel ({x}, {y}) is {decoded.Frames[0][x, y]}, not {full.Frames[0][x0 + x, y0 + y]}.");
                            }
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void RegionsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageDecodeOptions { Region = new Rectangle(0, 0, 0, 10) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageDecodeOptions { Region = new Rectangle(-1, 0, 10, 10) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageDecodeOptions { Region = new Rectangle(0, 0, 10, -2) });
        Assert.Null(new ImageDecodeOptions().Region);

        // An area out of the image, and a format that only decodes whole images
        var data = EncodeImage(30, 20, new Jpeg2000Encoder(), seed: 1);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data, new ImageDecodeOptions { Region = new Rectangle(30, 0, 5, 5) }).Dispose());
        using var image = Image.Load(data);
        using var png = new MemoryStream();
        image.Save(png, new PngEncoder());
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(png.ToArray(), new ImageDecodeOptions { Region = new Rectangle(0, 0, 5, 5) }).Dispose());
    }

    [Fact]
    public void ASequentialReaderReadsTheScaledImage()
    {
        var data = EncodeImage(301, 213, new Jpeg2000Encoder(), seed: 5);
        using var expected = Image.Load<Rgba32>(data, new ImageDecodeOptions { ScaleDenominator = 4 });
        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data), new ImageReaderOptions { ScaleDenominator = 4 });
        Assert.Equal(new Size(76, 54), reader.Info.Size);
        using var frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(GetBytes(expected), GetBytes(frame));
        Assert.Null(reader.ReadFrame());
    }

    /// <summary>
    /// The memory of a decode follows what it produces: a decode at a quarter of the size allocates a sixteenth of the
    /// planes, and a region inside one tile decodes that tile only.
    /// </summary>
    [Fact]
    public void ScaledDecodesAndRegionsAllocateWhatTheyProduce()
    {
        var data = EncodeImage(1024, 768, new Jpeg2000Encoder { TileWidth = 256, TileHeight = 256 }, seed: 8);
        var full = MeasurePeak(scaleDenominator: 1, region: null);
        var quarter = MeasurePeak(scaleDenominator: 4, region: null);
        var region = MeasurePeak(scaleDenominator: 1, new Rectangle(300, 300, 100, 100));

        // The full decode holds the image (4 bytes a pixel) and the planes of a tile (4 bytes a sample); a region holds the
        // planes of the one tile it meets, which is transformed whole
        Assert.True(quarter < full / 4, $"Full: {full} bytes; a quarter: {quarter} bytes.");
        Assert.True(region < full / 2, $"Full: {full} bytes; a region: {region} bytes.");

        long MeasurePeak(int scaleDenominator, Rectangle? region)
        {
            using var tracking = new AllocationTracking();
            using var image = Image.Load(data, new ImageDecodeOptions { ScaleDenominator = scaleDenominator, Region = region, Configuration = new ImageConfiguration { MaxDegreeOfParallelism = 1 } });
            return tracking.Scopes.Max(static scope => scope.GetDiagnostics().PeakLiveBytes);
        }
    }

    /// <summary>Codes an image of flat areas, gradients, noise and translucent pixels with the encoder of the library.</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static byte[] EncodeImage(int width, int height, Jpeg2000Encoder encoder, int seed)
    {
        var random = new Random(seed);
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = (((x / 23) + (y / 17)) % 3) switch
                {
                    0 => new Rgba32((byte)(x * 3), (byte)(y * 5), (byte)(x + y), 255),
                    1 => new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)),
                    _ => new Rgba32(40, 120, 200, (byte)x),
                };
            }
        }

        using var image = Image.ImportPixelData<Rgba32>(pixels, width, height);
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static ImageDecodeOptions WithWorkers(int workers) => new() { Configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers } };

    /// <summary>Codes an image of flat areas, gradients and noise with the encoder of the library: one tile of 64x64 code-blocks.</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static byte[] EncodeLargeImage(PixelFormat format, Jpeg2000Compression compression, int width, int height, int seed)
    {
        Assert.InRange((long)width * height, RowBands.MinPixelsForParallelism, long.MaxValue);
        var random = new Random(seed);
        var pixels = new Rgba64[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = (((x / 37) + (y / 29)) % 3) switch
                {
                    0 => new Rgba64((ushort)(x * 190), (ushort)(y * 270), (ushort)((x + y) * 120), 65535),
                    1 => new Rgba64((ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536)),
                    _ => new Rgba64(9000, 30000, 52000, (ushort)(x * 211)),
                };
            }
        }

        using var source = Image.ImportPixelData<Rgba64>(pixels, width, height);
        var flatten = new PixelConversionOptions { BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) };
        using Image image = format switch
        {
            PixelFormat.Gray8 => source.CloneAs<Gray8>(flatten),
            PixelFormat.Rgb24 => source.CloneAs<Rgb24>(flatten),
            PixelFormat.Rgba32 => source.CloneAs<Rgba32>(),
            PixelFormat.Rgba64 => source.Clone(),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        using var stream = new MemoryStream();
        image.Save(stream, new Jpeg2000Encoder { Compression = compression, Container = Jpeg2000Container.Codestream });
        return stream.ToArray();
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
