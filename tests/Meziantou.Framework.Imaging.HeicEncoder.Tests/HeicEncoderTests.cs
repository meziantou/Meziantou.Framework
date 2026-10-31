using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Framework.Imaging.TestHarness.Hevc;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using static Meziantou.Framework.Imaging.Tests.HeifEncoderTestImages;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// HEIC encoding through the public save API. The structure of the output is validated with the independent readers of
/// the harness: <see cref="ReferenceHeif"/> for the boxes, the items, the properties and the tracks, and
/// <see cref="ReferenceHevcStream"/> for the NAL units and the sample format of the sequence parameter sets. The profiles
/// are the ones of Rec. ITU-T H.265, Annex A, transcribed in <see cref="GetExpectedProfile"/>. The harness has no HEVC
/// decoder: the samples are read back with the decoder of the library, which the conformance vectors pin to the reference
/// decoders byte for byte, and the reference decoders themselves decode the output of the encoder in the interoperability
/// tests (<c>HeicEncoderInteropTests</c>, and <c>HevcEncoderInteropTests</c> for the bitstream alone).
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class HeicEncoderTests
{
    // nal_unit_type of the slice segments that the encoder writes (ITU-T H.265, Table 7-1): a picture that refers to the
    // picture before it, and an instantaneous decoding refresh picture without leading pictures
    private const int NalTrailR = 1;
    private const int NalIdrNLp = 20;

    // A TIFF structure with an orientation, which the library keeps equal to the orientation of the image, and a text field
    private static readonly byte[] Exif = CreateExif(ExifOrientation.TopLeft);

    private static readonly byte[] Xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>"u8.ToArray();

    /// <summary>What a stream is in its file: a profile depends on it.</summary>
    public enum StreamKind
    {
        /// <summary>The picture of a still image.</summary>
        Still,

        /// <summary>The pictures of an image sequence, predicted from one another.</summary>
        Sequence,

        /// <summary>The pictures of an image sequence that are all key frames.</summary>
        IntraSequence,
    }

    [Fact]
    public void SettingsHaveDocumentedDefaultsAndRanges()
    {
        var encoder = new HeicEncoder();
        Assert.Equal(ImageFormat.Heic, encoder.Format);
        Assert.Equal(HeicCompression.Lossless, encoder.Compression);
        Assert.Equal(75, encoder.Quality);
        Assert.False(encoder.OptimizeQuantization);
        Assert.Equal(5, encoder.Effort);
        Assert.Equal(HeicChromaSubsampling.Auto, encoder.ChromaSubsampling);
        Assert.Null(encoder.BitDepth);
        Assert.False(encoder.AllowBitDepthReduction);
        Assert.False(encoder.ClearTransparentColors);
        Assert.Null(encoder.GridTileSize);
        Assert.Equal(0, encoder.KeyFrameInterval);
        Assert.Equal(FrameDurationRounding.RequireExact, encoder.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);

        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { Compression = (HeicCompression)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { Compression = (HeicCompression)(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { Quality = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { Quality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { Effort = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { Effort = 10 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { ChromaSubsampling = (HeicChromaSubsampling)4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { BitDepth = 9 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { BitDepth = 16 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { KeyFrameInterval = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { DurationRounding = (FrameDurationRounding)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { GridTileSize = new Size(63, 64) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { GridTileSize = new Size(64, 63) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { GridTileSize = new Size(65529, 64) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { GridTileSize = new Size(64, 65529) });
        Assert.Equal(new Size(64, 65528), new HeicEncoder { GridTileSize = new Size(64, 65528) }.GridTileSize);
        Assert.Equal(12, new HeicEncoder { KeyFrameInterval = 12 }.KeyFrameInterval);
        Assert.Equal((0, 100), (new HeicEncoder { Quality = 0 }.Quality, new HeicEncoder { Quality = 100 }.Quality));
        Assert.Equal((0, 9), (new HeicEncoder { Effort = 0 }.Effort, new HeicEncoder { Effort = 9 }.Effort));
        foreach (var bitDepth in new int?[] { null, 8, 10, 12 })
        {
            Assert.Equal(bitDepth, new HeicEncoder { BitDepth = bitDepth }.BitDepth);
        }

        foreach (var subsampling in Enum.GetValues<HeicChromaSubsampling>())
        {
            Assert.Equal(subsampling, new HeicEncoder { ChromaSubsampling = subsampling }.ChromaSubsampling);
        }
    }

    [Theory]
    [InlineData("image.heic")]
    [InlineData("IMAGE.HEIC")]
    [InlineData("image.heif")]
    [InlineData("image.Heif")]
    [InlineData("image.heics")]
    [InlineData("image.heifs")]
    public void PathSavesInferHeicFromTheExtension(string name)
    {
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("meziantou-heic-"));
        try
        {
            using var image = CreateImage(5, 3, seed: 1, transparent: false);
            var path = directory / name;
            image.Save(path);

            // The default encoder: a lossless still image, whatever the extension
            var reader = ReferenceHeif.Parse(File.ReadAllBytes(path));
            Assert.Equal("heix", reader.MajorBrand);
            Assert.False(reader.IsSequence);
            Assert.Equal(ImageFormat.Heic, Image.Identify(path).Format);
            Assert.Equal(HeicCompression.Lossless, Assert.IsType<HeicEncoder>(ImageEncoder.FromPath(path)).Compression);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 5)]
    [InlineData(64, 64)]
    [InlineData(70, 33)]
    public void LosslessOutputPreservesEverySampleOfAnOpaqueImage(int width, int height)
    {
        using var image = CreateImage(width, height, seed: width + height, transparent: false);
        var data = Encode(image, new HeicEncoder());
        var reader = ReferenceHeif.Parse(data);

        // The structure: one HEVC item of the Main 4:4:4 Still Picture profile, 8-bit 4:4:4 with the identity matrix in
        // full range, which is a file of the 'heix' brand
        Assert.Equal("heix", reader.MajorBrand);
        Assert.Equal(["heix", "mif1", "miaf"], reader.CompatibleBrands);
        Assert.False(reader.IsSequence);
        Assert.Empty(reader.Tracks);
        var item = reader.PrimaryItem;
        Assert.NotNull(item);
        Assert.Equal("hvc1", item.Type);
        Assert.Equal((width, height), item.Size);
        Assert.Equal([8, 8, 8], item.PixelInformation);
        AssertStream(item.HevcConfiguration, StreamKind.Still, chromaFormat: 3, bitDepth: 8, codedSize: (width, height));
        Assert.Equal(new ReferenceHeif.ColorDescription(2, 2, 0, FullRange: true), item.Nclx);
        Assert.Null(item.IccProfile);
        Assert.Null(reader.AlphaItem);
        Assert.Single(reader.Items);
        Assert.Empty(reader.ItemReferences);

        // 4:4:4 pictures have any size: no clean aperture, whatever the parity of the size
        Assert.Empty(item.Transforms);
        Assert.Equal([("ispe", false), ("pixi", false), ("hvcC", true), ("colr", false)], item.Associations.Select(association => (association.Property.Type, association.IsEssential)));
        Assert.Equal(["ftyp", "meta", "mdat"], reader.TopLevelBoxes.Select(box => box.Type));

        // The item is the end of the file, and it is one slice segment of an instantaneous decoding refresh picture: the
        // parameter sets are in the configuration record only
        var (offset, length) = Assert.Single(item.Extents);
        Assert.Equal(data.Length, offset + length);
        Assert.Equal([NalIdrNLp], GetNalUnitTypes(item.HevcConfiguration!, reader.ReadItemData(item)));

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(GetBytes(image), GetBytes(decoded));
        Assert.Equal(new CicpColorDescription(2, 2, 0, fullRange: true), decoded.Metadata.Cicp);
        var info = Image.Identify(data);
        Assert.Equal((ImageFormat.Heic, new Size(width, height), PixelFormat.Rgb24, 8), (info.Format, info.Size, info.PixelFormat, info.BitsPerComponent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlphaIsALosslessAuxiliaryImageThatKeepsHiddenColorsUnlessClearingIsRequested(bool clear)
    {
        using var image = CreateImage(19, 11, seed: 2, transparent: true);
        var source = GetBytes(image);
        var data = Encode(image, new HeicEncoder { ClearTransparentColors = clear });
        var reader = ReferenceHeif.Parse(data);
        var color = reader.PrimaryItem;
        var alpha = reader.AlphaItem;
        Assert.NotNull(color);
        Assert.NotNull(alpha);
        Assert.Equal("hvc1", alpha.Type);

        // The auxiliary type of HEVC alpha planes (ISO/IEC 23008-12, annex B.2.2), which is the one HEIC readers know
        Assert.Equal(ReferenceHeif.HevcAlphaAuxiliaryType, alpha.AuxiliaryType);
        Assert.Equal((19, 11), alpha.Size);
        Assert.Equal([8], alpha.PixelInformation);
        AssertStream(alpha.HevcConfiguration, StreamKind.Still, chromaFormat: 0, bitDepth: 8, codedSize: (19, 11));
        Assert.Equal([("ispe", false), ("pixi", false), ("hvcC", true), ("auxC", false)], alpha.Associations.Select(association => (association.Property.Type, association.IsEssential)));
        Assert.Null(alpha.Nclx);
        Assert.HasCount(2, reader.Items);
        var reference = Assert.Single(reader.ItemReferences);
        Assert.Equal(("auxl", alpha.Id, color.Id), (reference.Type, reference.FromItemId, Assert.Single(reference.ToItemIds)));

        // The two items share the property of their size
        Assert.Same(color.Find("ispe"), alpha.Find("ispe"));

        // The alpha plane, a monochrome image of a format range extensions profile, does not change the brand of the file,
        // which is the one of the color image
        Assert.Equal("heix", reader.MajorBrand);

        // The alpha plane precedes the color image, and both are in the media data
        Assert.True(Assert.Single(alpha.Extents).Offset < Assert.Single(color.Extents).Offset);
        Assert.Equal(data.Length, color.Extents[0].Offset + color.Extents[0].Length);

        using var decoded = Image.Load<Rgba32>(data);
        var actual = GetBytes(decoded);
        for (var i = 0; i < source.Length; i += 4)
        {
            var expected = source.AsSpan(i, 4).ToArray();
            if (clear && expected[3] is 0)
                expected = [0, 0, 0, 0];

            Assert.Equal(expected, actual.AsSpan(i, 4).ToArray());
        }
    }

    [Fact]
    public void AnOpaqueImageOfAPixelFormatWithAlphaHasNoAlphaItem()
    {
        using var image = CreateImage(9, 9, seed: 3, transparent: false);
        var reader = ReferenceHeif.Parse(Encode(image, new HeicEncoder()));
        Assert.Null(reader.AlphaItem);
        Assert.Single(reader.Items);
    }

    [Theory]
    [InlineData(13, 6)]
    [InlineData(1, 1)]
    [InlineData(9, 17)]
    public void GrayPixelsAreAMonochromeImage(int width, int height)
    {
        var pixels = new Gray8[width * height];
        var random = new Random(4);
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Gray8((byte)random.Next(256));
        }

        using var image = Image.ImportPixelData<Gray8>(pixels, width, height);
        var data = Encode(image, new HeicEncoder());
        var reader = ReferenceHeif.Parse(data);
        var item = reader.PrimaryItem;
        Assert.NotNull(item);
        Assert.Equal([8], item.PixelInformation);
        AssertStream(item.HevcConfiguration, StreamKind.Still, chromaFormat: 0, bitDepth: 8, codedSize: (width, height));

        // A monochrome picture has no chroma sample: any size, and no matrix
        Assert.Equal((width, height), item.Size);
        Assert.Empty(item.Transforms);
        Assert.Equal(new ReferenceHeif.ColorDescription(2, 2, 2, FullRange: true), item.Nclx);
        Assert.Equal("heix", reader.MajorBrand);

        using var decoded = Image.Load(data);
        Assert.Equal(PixelFormat.Gray8, decoded.PixelFormat);
        var actual = new byte[pixels.Length];
        decoded.Frames[0].CopyPixelBytesTo(actual);
        Assert.Equal(pixels.Select(pixel => pixel.Value), actual);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    public void SixteenBitPixelsNeedABitDepthAndKeepTheSamplesOfThatDepth(int bitDepth)
    {
        // Samples of the bit depth scaled to 16 bits, as the decoder produces them: they are coded back exactly
        var max = (1 << bitDepth) - 1;
        var random = new Random(bitDepth);
        var pixels = new Rgba64[11 * 7];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba64(Scale(random.Next(max + 1)), Scale(random.Next(max + 1)), Scale(random.Next(max + 1)), Scale(i % 3 is 0 ? random.Next(max + 1) : max));
        }

        using var image = Image.ImportPixelData<Rgba64>(pixels, 11, 7);
        using var rejected = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(rejected, new HeicEncoder()));
        Assert.Equal((ImageFormat.Heic, "Bit depth reduction"), (exception.Format, exception.Feature));
        Assert.Contains("HeicEncoder.BitDepth", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, rejected.Length);

        var data = Encode(image, new HeicEncoder { BitDepth = bitDepth });
        var reader = ReferenceHeif.Parse(data);
        Assert.Equal([bitDepth, bitDepth, bitDepth], reader.PrimaryItem!.PixelInformation);
        Assert.Equal([bitDepth], reader.AlphaItem!.PixelInformation);
        AssertStream(reader.PrimaryItem.HevcConfiguration, StreamKind.Still, chromaFormat: 3, bitDepth, codedSize: (11, 7));
        AssertStream(reader.AlphaItem.HevcConfiguration, StreamKind.Still, chromaFormat: 0, bitDepth, codedSize: (11, 7));
        using var decoded = Image.Load<Rgba64>(data);
        var actual = new Rgba64[pixels.Length];
        decoded.Frames[0].CopyPixelDataTo(actual);
        Assert.Equal(pixels, actual);
        Assert.Equal(PixelFormat.Rgba64, Image.Identify(data).PixelFormat);

        // Without a bit depth, the consent to reduce the samples selects 12 bits
        var reduced = ReferenceHeif.Parse(Encode(image, new HeicEncoder { AllowBitDepthReduction = true }));
        Assert.Equal(12, reduced.PrimaryItem!.HevcConfiguration!.BitDepthLuma);
        Assert.Equal(12, reduced.AlphaItem!.HevcConfiguration!.BitDepthLuma);

        ushort Scale(int value) => (ushort)(((value * 65535L) + (max / 2)) / max);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    public void SixteenBitPixelsAreRoundedToTheNearestSampleOfTheBitDepth(int bitDepth)
    {
        // Every kind of sample: the ends of the range, and values on both sides of the middle of two coded samples
        var max = (1 << bitDepth) - 1;
        var random = new Random(100 + bitDepth);
        var pixels = new Rgba64[16 * 9];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba64((ushort)random.Next(65536), (ushort)(i * 455), (ushort)(65535 - (i * 455)), (ushort)(i % 4 is 0 ? random.Next(65536) : 65535));
        }

        using var image = Image.ImportPixelData<Rgba64>(pixels, 16, 9);
        var data = Encode(image, new HeicEncoder { BitDepth = bitDepth });
        Assert.Equal(bitDepth, ReferenceHeif.Parse(data).PrimaryItem!.HevcConfiguration!.BitDepthLuma);
        using var decoded = Image.Load<Rgba64>(data);
        var actual = new Rgba64[pixels.Length];
        decoded.Frames[0].CopyPixelDataTo(actual);

        // A 16-bit sample v is coded as round(v * max / 65535), which is decoded as round(q * 65535 / max)
        Assert.Equal(pixels.Select(pixel => new Rgba64(Round(pixel.R), Round(pixel.G), Round(pixel.B), Round(pixel.A))), actual);

        ushort Round(ushort value)
        {
            var coded = ((value * (long)max) + 32767) / 65535;
            return (ushort)(((coded * 65535) + (max / 2)) / max);
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    public void EightBitPixelsCanBeWrittenWithMoreBitsWithoutLoss(int bitDepth)
    {
        using var image = CreateImage(8, 5, seed: 6, transparent: true);
        var data = Encode(image, new HeicEncoder { BitDepth = bitDepth });
        var reader = ReferenceHeif.Parse(data);
        Assert.Equal(bitDepth, reader.PrimaryItem!.HevcConfiguration!.BitDepthLuma);
        Assert.Equal(bitDepth, reader.AlphaItem!.HevcConfiguration!.BitDepthLuma);
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(GetBytes(image), GetBytes(decoded));
    }

    [Fact]
    public void AStillImageIsWrittenToANonSeekableStreamWithTheSameBytes()
    {
        using var image = CreateImage(33, 17, seed: 7, transparent: true);
        var seekable = Encode(image, new HeicEncoder());
        using var stream = new TestOutputStream();
        image.Save(stream, new HeicEncoder());
        Assert.Equal(seekable, stream.ToArray());

        using var lossy = new TestOutputStream();
        image.Save(lossy, new HeicEncoder { Compression = HeicCompression.Lossy });
        Assert.Equal(Encode(image, new HeicEncoder { Compression = HeicCompression.Lossy }), lossy.ToArray());
    }

    [Theory]
    [InlineData(HeicCompression.Lossless)]
    [InlineData(HeicCompression.Lossy)]
    public async Task EncodingIsDeterministic(HeicCompression compression)
    {
        using var image = CreateSmoothImage(40, 23, transparent: true);
        using (var second = CreateSmoothImage(40, 23, transparent: true, phase: 3))
        {
            image.AppendFrame(second.Frames[0]);
        }

        var encoder = new HeicEncoder { Compression = compression };
        var expected = Encode(image, encoder);
        Assert.Equal(expected, Encode(image, encoder));

        // The synchronous and the asynchronous saves, and the sequential writer, write the same file
        using var asynchronous = new MemoryStream();
        await image.SaveAsync(asynchronous, encoder, XunitCancellationToken);
        Assert.Equal(expected, asynchronous.ToArray());

        using var sequential = new MemoryStream();
        using (var writer = Image.CreateWriter<Rgba32>(sequential, new ImageWriterOptions(40, 23) { Encoder = encoder, ExpectedFrameCount = 2, LeaveOpen = true }))
        {
            writer.WriteFrame((ImageFrame<Rgba32>)image.Frames[0]);
            writer.WriteFrame((ImageFrame<Rgba32>)image.Frames[1]);
            writer.Complete();
        }

        Assert.Equal(expected, sequential.ToArray());
    }

    [Theory]
    [InlineData(HeicCompression.Lossless, false)]
    [InlineData(HeicCompression.Lossless, true)]
    [InlineData(HeicCompression.Lossy, false)]
    public void TheEffortNeverChangesTheStructureNorThePixelsOfLosslessOutput(HeicCompression compression, bool gray)
    {
        using var color = CreateSmoothImage(37, 21, transparent: !gray);
        using var source = gray ? color.CloneAs<Gray8>() : (Image)color.CloneAs<Rgba32>();
        var expected = new byte[source.Width * source.Height * PixelFormats.GetBytesPerPixel(source.PixelFormat)];
        source.Frames[0].CopyPixelBytesTo(expected);
        var sizes = new List<int>();
        for (var effort = 0; effort <= 9; effort++)
        {
            var data = Encode(source, new HeicEncoder { Compression = compression, Effort = effort, Quality = 80 });
            sizes.Add(data.Length);
            var reader = ReferenceHeif.Parse(data);
            var lossy = compression is HeicCompression.Lossy;
            AssertStream(reader.PrimaryItem!.HevcConfiguration, StreamKind.Still, chromaFormat: gray ? 0 : lossy ? 1 : 3, bitDepth: 8, codedSize: lossy && !gray ? (38, 22) : (37, 21));
            using var decoded = Image.Load(data);
            Assert.Equal(source.PixelFormat, decoded.PixelFormat);
            Assert.Equal(new Size(37, 21), decoded.Size);
            var actual = new byte[expected.Length];
            decoded.Frames[0].CopyPixelBytesTo(actual);
            if (!lossy)
            {
                Assert.Equal(expected, actual);
                continue;
            }

            // Alpha is lossless at every effort
            for (var i = 3; i < expected.Length; i += 4)
            {
                Assert.Equal(expected[i], actual[i]);
            }
        }

        // The slowest search does not give a larger lossless file than the fastest one
        TestContext.Current.TestOutputHelper?.WriteLine($"{compression}{(gray ? " gray" : "")}: {string.Join(", ", sizes)}");
        if (compression is HeicCompression.Lossless)
            Assert.True(sizes[9] <= sizes[0], string.Join(", ", sizes));
    }

    [Theory]
    [InlineData(ExifOrientation.TopLeft, 0, -1)]
    [InlineData(ExifOrientation.TopRight, 0, 1)]
    [InlineData(ExifOrientation.BottomRight, 2, -1)]
    [InlineData(ExifOrientation.BottomLeft, 0, 0)]
    [InlineData(ExifOrientation.LeftTop, 1, 0)]
    [InlineData(ExifOrientation.RightTop, 3, -1)]
    [InlineData(ExifOrientation.RightBottom, 3, 0)]
    [InlineData(ExifOrientation.LeftBottom, 1, -1)]
    public void TheOrientationIsARotationThenAMirroring(ExifOrientation orientation, int angle, int axis)
    {
        // irot rotates anti-clockwise by quarter turns; imir then exchanges the top and the bottom (0) or the left and the
        // right (1) of the rotated image (ISO/IEC 23008-12, sections 6.5.10 and 6.5.12)
        Assert.Equal((angle, axis), HeifFileWriter.GetTransformations(orientation));

        using var image = CreateImage(6, 4, seed: 9, transparent: true);
        image.Metadata.Orientation = orientation;
        var data = Encode(image, new HeicEncoder());
        var reader = ReferenceHeif.Parse(data);
        var item = reader.PrimaryItem;
        Assert.NotNull(item);
        Assert.Equal((int)orientation, item.Orientation);
        var expected = new List<string>();
        if (angle is not 0)
            expected.Add("irot");

        if (axis >= 0)
            expected.Add("imir");

        Assert.Equal(expected, item.Transforms.Select(property => property.Type));
        Assert.All(item.Associations.Where(association => association.Property.Type is "irot" or "imir"), association => Assert.True(association.IsEssential));

        // The alpha plane has the transformations of the image it belongs to
        Assert.Equal(expected, reader.AlphaItem!.Transforms.Select(property => property.Type));
        if (angle is not 0)
            Assert.Equal(angle, item.Find("irot")!.Payload.Span[0]);

        if (axis >= 0)
            Assert.Equal(axis, item.Find("imir")!.Payload.Span[0]);

        // No EXIF block is created to hold the orientation, and the pixels are not transformed
        Assert.Empty(reader.FindDescriptions(item.Id));
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(orientation, decoded.Metadata.Orientation);
        Assert.Equal(new Size(6, 4), decoded.Size);
        Assert.Equal(GetBytes(image), GetBytes(decoded));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LightLevelsMasteringDisplayAndPixelShapeAreStoredInTheirBoxes(bool isSequence)
    {
        using var image = CreateImage(6, 4, seed: 12, transparent: true);
        if (isSequence)
        {
            using var second = CreateImage(6, 4, seed: 13, transparent: true);
            image.AppendFrame(second.Frames[0]);
            image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
            image.Frames[1].Metadata.Duration = new FrameDuration(1, 10);
        }

        var lightLevel = new ContentLightLevel(maxContentLightLevel: 1000, maxPictureAverageLightLevel: 400);
        var display = new MasteringDisplayColorVolume(redX: 35400, redY: 14600, greenX: 8500, greenY: 39850, blueX: 6550, blueY: 2300, whitePointX: 15635, whitePointY: 16450, maxLuminance: 10_000_000, minLuminance: 50);
        var shape = new PixelAspectRatio(4, 3);
        image.Metadata.ContentLightLevel = lightLevel;
        image.Metadata.MasteringDisplay = display;
        image.Metadata.PixelAspectRatio = shape;
        var data = Encode(image, new HeicEncoder());

        // ISO/IEC 14496-12, sections 12.1.6, 12.1.7 and 12.1.4, as for AVIF: the primaries of the display are stored in
        // the order green, blue, red, the luminances in units of 0.0001 cd/m2
        byte[] expectedLightLevel = [0x03, 0xE8, 0x01, 0x90];
        byte[] expectedDisplay = [0x21, 0x34, 0x9B, 0xAA, 0x19, 0x96, 0x08, 0xFC, 0x8A, 0x48, 0x39, 0x08, 0x3D, 0x13, 0x40, 0x42, 0x00, 0x98, 0x96, 0x80, 0x00, 0x00, 0x00, 0x32];
        byte[] expectedShape = [0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x03];
        var reader = ReferenceHeif.Parse(data);
        var item = reader.PrimaryItem;
        Assert.NotNull(item);
        Assert.Equal(expectedLightLevel, item.Find("clli")!.Payload.ToArray());
        Assert.Equal(expectedDisplay, item.Find("mdcv")!.Payload.ToArray());
        Assert.Equal(expectedShape, item.Find("pasp")!.Payload.ToArray());
        Assert.All(item.Associations.Where(association => association.Property.Type is "clli" or "mdcv" or "pasp"), association => Assert.False(association.IsEssential));

        // The light levels and the display describe colors: the alpha plane only has the shape of its pixels
        var alpha = reader.AlphaItem;
        Assert.NotNull(alpha);
        Assert.Equal(expectedShape, alpha.Find("pasp")!.Payload.ToArray());
        Assert.Null(alpha.Find("clli"));
        Assert.Null(alpha.Find("mdcv"));
        if (isSequence)
        {
            var track = reader.ColorTrack;
            Assert.NotNull(track);
            Assert.Equal(expectedLightLevel, track.FindSampleEntryBox("clli")!.Payload.ToArray());
            Assert.Equal(expectedDisplay, track.FindSampleEntryBox("mdcv")!.Payload.ToArray());
            Assert.Equal(expectedShape, track.FindSampleEntryBox("pasp")!.Payload.ToArray());
        }

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(isSequence ? 2 : 1, decoded.Frames.Count);
        Assert.Equal(lightLevel, decoded.Metadata.ContentLightLevel);
        Assert.Equal(display, decoded.Metadata.MasteringDisplay);
        Assert.Equal(shape, decoded.Metadata.PixelAspectRatio);
        Assert.Equal(GetBytes(image), GetBytes(decoded));

        var info = Image.Identify(data);
        Assert.Equal(lightLevel, info.Metadata.ContentLightLevel);
        Assert.Equal(display, info.Metadata.MasteringDisplay);
        Assert.Equal(shape, info.Metadata.PixelAspectRatio);

        // Re-encoding the decoded image keeps them
        using var reencoded = Image.Load(Encode(decoded, new HeicEncoder()));
        Assert.Equal(lightLevel, reencoded.Metadata.ContentLightLevel);
        Assert.Equal(display, reencoded.Metadata.MasteringDisplay);
        Assert.Equal(shape, reencoded.Metadata.PixelAspectRatio);
    }

    [Fact]
    public void TheTransformationsFollowTheCleanApertureOfAnImageOfAnOddSize()
    {
        // The clean aperture selects the image in its coded pictures; the rotation and the mirroring then apply to the
        // image (ISO/IEC 23000-22, section 7.3.6.7: clap, irot and imir, in this order)
        using var image = CreateSmoothImage(21, 13, transparent: true);
        image.Metadata.Orientation = ExifOrientation.RightBottom;
        var data = Encode(image, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 95 });
        var reader = ReferenceHeif.Parse(data);
        var color = reader.PrimaryItem!;
        var alpha = reader.AlphaItem!;
        Assert.Equal([("ispe", false), ("pixi", false), ("hvcC", true), ("colr", false), ("clap", true), ("irot", true), ("imir", true)], color.Associations.Select(association => (association.Property.Type, association.IsEssential)));
        Assert.Equal([("ispe", false), ("pixi", false), ("hvcC", true), ("auxC", false), ("clap", true), ("irot", true), ("imir", true)], alpha.Associations.Select(association => (association.Property.Type, association.IsEssential)));
        Assert.Equal((int)ExifOrientation.RightBottom, color.Orientation);
        Assert.Equal((0, 0, 21, 13), color.CleanAperture!.GetRectangle(22, 14));

        // The properties are shared by the two items
        foreach (var type in new[] { "ispe", "clap", "irot", "imir" })
        {
            Assert.Same(color.Find(type), alpha.Find(type));
        }

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(new Size(21, 13), decoded.Size);
        Assert.Equal(ExifOrientation.RightBottom, decoded.Metadata.Orientation);
        var source = GetBytes(image);
        var actual = GetBytes(decoded);
        for (var i = 3; i < source.Length; i += 4)
        {
            Assert.Equal(source[i], actual[i]);
        }
    }

    [Theory]
    [InlineData(HeicChromaSubsampling.Auto, 1, 76, 42, "heic")]
    [InlineData(HeicChromaSubsampling.Ratio420, 1, 76, 42, "heic")]
    [InlineData(HeicChromaSubsampling.Ratio422, 2, 76, 41, "heix")]
    [InlineData(HeicChromaSubsampling.Ratio444, 3, 75, 41, "heix")]
    public void LossyOutputIsYCbCrWithTheRequestedSubsamplingAndExactAlpha(HeicChromaSubsampling subsampling, int chromaFormat, int codedWidth, int codedHeight, string brand)
    {
        using var image = CreateSmoothImage(75, 41, transparent: true);
        var source = GetBytes(image);
        var data = Encode(image, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 90, ChromaSubsampling = subsampling });
        var reader = ReferenceHeif.Parse(data);
        var color = reader.PrimaryItem;
        Assert.NotNull(color);
        AssertStream(color.HevcConfiguration, StreamKind.Still, chromaFormat, bitDepth: 8, codedSize: (codedWidth, codedHeight));

        // The brand of the Main and Main Still Picture profiles for 8-bit 4:2:0 pictures, whatever the alpha plane is
        Assert.Equal(brand, reader.MajorBrand);
        Assert.Equal([brand, "mif1", "miaf"], reader.CompatibleBrands);

        // BT.601 in full range, with the primaries and the transfer function left unspecified
        Assert.Equal(new ReferenceHeif.ColorDescription(2, 2, 6, FullRange: true), color.Nclx);
        var alpha = reader.AlphaItem;
        Assert.NotNull(alpha);

        // The conformance window of a picture counts in chroma samples: an image of an odd size with subsampled chroma is
        // coded as pictures of the even size above, and the clean aperture is the image. The alpha plane has the size of
        // the pictures of the color image, and the same clean aperture
        Assert.Equal((codedWidth, codedHeight), color.Size);
        Assert.Equal((codedWidth, codedHeight), alpha.Size);
        AssertStream(alpha.HevcConfiguration, StreamKind.Still, chromaFormat: 0, bitDepth: 8, codedSize: (codedWidth, codedHeight));
        if ((codedWidth, codedHeight) == (75, 41))
        {
            Assert.Null(color.CleanAperture);
            Assert.Null(alpha.CleanAperture);
        }
        else
        {
            // The top-left part of the pictures: the center of the aperture is half the difference of the sizes before the
            // center of the picture
            Assert.Equal(new ReferenceHeif.CleanAperture(75, 1, 41, 1, 75 - codedWidth, 2, 41 - codedHeight, 2), color.CleanAperture);
            Assert.Equal((0, 0, 75, 41), color.CleanAperture!.GetRectangle(codedWidth, codedHeight));
            Assert.Same(color.Find("clap"), alpha.Find("clap"));
            Assert.True(color.Associations.Single(association => association.Property.Type is "clap").IsEssential);
        }

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(new Size(75, 41), decoded.Size);
        Assert.Equal(new Size(75, 41), Image.Identify(data).Size);
        var actual = GetBytes(decoded);
        double squaredError = 0;
        for (var i = 0; i < source.Length; i += 4)
        {
            Assert.Equal(source[i + 3], actual[i + 3]);
            for (var channel = 0; channel < 3; channel++)
            {
                double difference = source[i + channel] - actual[i + channel];
                squaredError += difference * difference;
            }
        }

        var psnr = 10 * Math.Log10(255.0 * 255.0 * (source.Length / 4 * 3) / Math.Max(squaredError, 1));
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{subsampling}: PSNR {psnr:F2} dB, {data.Length} bytes"));

        // Measured when the encoder was written: 49.4 dB (4:2:0) to 58.5 dB (12-bit 4:4:4) at this quality
        Assert.True(psnr >= 44, string.Create(CultureInfo.InvariantCulture, $"PSNR {psnr:F2} dB"));
    }

    [Theory]
    [InlineData(HeicChromaSubsampling.Ratio420, 10, 1, "heix")]
    [InlineData(HeicChromaSubsampling.Ratio420, 12, 1, "heix")]
    [InlineData(HeicChromaSubsampling.Ratio422, 10, 2, "heix")]
    [InlineData(HeicChromaSubsampling.Ratio422, 12, 2, "heix")]
    [InlineData(HeicChromaSubsampling.Ratio444, 10, 3, "heix")]
    [InlineData(HeicChromaSubsampling.Ratio444, 12, 3, "heix")]
    public void LossyOutputHasTheRequestedBitDepth(HeicChromaSubsampling subsampling, int bitDepth, int chromaFormat, string brand)
    {
        // 16-bit pixels: the samples of the file have more precision than 8-bit pixels would give them
        using var eightBit = CreateSmoothImage(48, 32, transparent: true);
        using var image = eightBit.CloneAs<Rgba64>();
        var source = new Rgba64[48 * 32];
        image.Frames[0].CopyPixelDataTo(source);
        var data = Encode(image, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 90, ChromaSubsampling = subsampling, BitDepth = bitDepth });
        var reader = ReferenceHeif.Parse(data);
        Assert.Equal(brand, reader.MajorBrand);
        Assert.Equal([bitDepth, bitDepth, bitDepth], reader.PrimaryItem!.PixelInformation);
        Assert.Equal([bitDepth], reader.AlphaItem!.PixelInformation);
        AssertStream(reader.PrimaryItem.HevcConfiguration, StreamKind.Still, chromaFormat, bitDepth, codedSize: (48, 32));
        AssertStream(reader.AlphaItem.HevcConfiguration, StreamKind.Still, chromaFormat: 0, bitDepth, codedSize: (48, 32));

        using var decoded = Image.Load(data);
        Assert.Equal(PixelFormat.Rgba64, decoded.PixelFormat);
        var actual = new Rgba64[source.Length];
        ((Image<Rgba64>)decoded).Frames[0].CopyPixelDataTo(actual);
        double squaredError = 0;
        var max = (1 << bitDepth) - 1;
        for (var i = 0; i < source.Length; i++)
        {
            // Alpha is lossless: the nearest sample of the bit depth, scaled back to 16 bits
            var coded = ((source[i].A * (long)max) + 32767) / 65535;
            Assert.Equal((ushort)(((coded * 65535) + (max / 2)) / max), actual[i].A);
            squaredError += Square(source[i].R - actual[i].R) + Square(source[i].G - actual[i].G) + Square(source[i].B - actual[i].B);
        }

        var psnr = 10 * Math.Log10(65535.0 * 65535.0 * source.Length * 3 / Math.Max(squaredError, 1));
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{subsampling} {bitDepth}-bit: PSNR {psnr:F2} dB, {data.Length} bytes"));

        // Measured when the encoder was written: 49.4 dB (4:2:0) to 58.5 dB (12-bit 4:4:4) at this quality
        Assert.True(psnr >= 44, string.Create(CultureInfo.InvariantCulture, $"PSNR {psnr:F2} dB"));

        static double Square(double value) => value * value;
    }

    [Fact]
    public void QualityTradesSizeForFidelity()
    {
        using var image = CreateSmoothImage(96, 64, transparent: false);
        var source = GetBytes(image);
        var previousSize = int.MaxValue;
        var previousError = -1.0;
        foreach (var quality in new[] { 100, 80, 50, 20, 0 })
        {
            var data = Encode(image, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = quality });
            using var decoded = Image.Load<Rgba32>(data);
            var actual = GetBytes(decoded);
            double error = 0;
            for (var i = 0; i < source.Length; i++)
            {
                error += Math.Abs(source[i] - actual[i]);
            }

            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quality {quality}: {data.Length} bytes, error {error}"));
            Assert.HasCountLessThan(previousSize, data);
            Assert.True(error > previousError, string.Create(CultureInfo.InvariantCulture, $"quality {quality}: error {error}, the previous quality {previousError}"));
            previousSize = data.Length;
            previousError = error;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OptimizedQuantizationIsUsedByTheContainer(bool lossless, bool grid)
    {
        using var image = CreateSmoothImage(96, 64, transparent: true);
        var baselineEncoder = new HeicEncoder
        {
            Compression = lossless ? HeicCompression.Lossless : HeicCompression.Lossy,
            Quality = 40,
            Effort = 2,
            GridTileSize = grid ? new Size(64, 64) : null,
        };
        var optimizedEncoder = new HeicEncoder
        {
            Compression = baselineEncoder.Compression,
            Quality = baselineEncoder.Quality,
            Effort = baselineEncoder.Effort,
            GridTileSize = baselineEncoder.GridTileSize,
            OptimizeQuantization = true,
        };
        var baseline = Encode(image, baselineEncoder);
        var optimized = Encode(image, optimizedEncoder);
        if (lossless)
        {
            Assert.Equal(baseline, optimized);
        }
        else
        {
            Assert.False(baseline.AsSpan().SequenceEqual(optimized));
        }

        var reader = ReferenceHeif.Parse(optimized);
        var color = reader.PrimaryItem!;
        foreach (var item in grid ? reader.GetGridTiles(color) : [color])
        {
            var configuration = item.HevcConfiguration!;
            Assert.Equal((30, false), (configuration.LevelIdc, configuration.TierFlag));
            AssertStream(configuration, StreamKind.Still, chromaFormat: lossless ? 3 : 1, bitDepth: 8, codedSize: grid ? (64, 64) : (96, 64));
        }

        using var decoded = Image.Load<Rgba32>(optimized);
        Assert.Equal(image.Size, decoded.Size);
        var source = GetBytes(image);
        var actual = GetBytes(decoded);
        for (var i = 3; i < source.Length; i += 4)
        {
            Assert.Equal(source[i], actual[i]);
        }
    }

    [Fact]
    public void LossyGrayPixelsAreAMonochromeImage()
    {
        var pixels = new Gray8[40 * 30];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Gray8((byte)(((i % 40) * 4) + (i / 40 * 2)));
        }

        using var image = Image.ImportPixelData<Gray8>(pixels, 40, 30);
        var data = Encode(image, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 95 });
        var reader = ReferenceHeif.Parse(data);
        var item = reader.PrimaryItem;
        AssertStream(item!.HevcConfiguration, StreamKind.Still, chromaFormat: 0, bitDepth: 8, codedSize: (40, 30));
        Assert.Equal("heix", reader.MajorBrand);
        using var decoded = Image.Load(data);
        Assert.Equal(PixelFormat.Gray8, decoded.PixelFormat);
        var actual = new byte[pixels.Length];
        decoded.Frames[0].CopyPixelBytesTo(actual);
        for (var i = 0; i < pixels.Length; i++)
        {
            Assert.True(Math.Abs(pixels[i].Value - actual[i]) <= 6, $"pixel {i}: {pixels[i].Value} was decoded as {actual[i]}");
        }
    }

    [Fact]
    public void TheMatrixAndTheRangeOfTheColorDescriptionAreKeptByLossyOutput()
    {
        using var image = CreateSmoothImage(32, 32, transparent: false);
        image.Metadata.Cicp = new CicpColorDescription(9, 16, 9, fullRange: false);
        var data = Encode(image, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 90, BitDepth = 10 });
        var item = ReferenceHeif.Parse(data).PrimaryItem!;
        Assert.Equal(new ReferenceHeif.ColorDescription(9, 16, 9, FullRange: false), item.Nclx);
        using var decoded = Image.Load(data);
        Assert.Equal(new CicpColorDescription(9, 16, 9, fullRange: false), decoded.Metadata.Cicp);

        // The bitstream says the same as the container
        AssertVideoSignal(item, new CicpColorDescription(9, 16, 9, fullRange: false));

        // The lossless coding has no matrix: the description keeps the primaries and the transfer function
        var lossless = Encode(image, new HeicEncoder());
        var losslessItem = ReferenceHeif.Parse(lossless).PrimaryItem!;
        Assert.Equal(new ReferenceHeif.ColorDescription(9, 16, 0, FullRange: true), losslessItem.Nclx);
        AssertVideoSignal(losslessItem, new CicpColorDescription(9, 16, 0, fullRange: true));
        using var decodedLossless = Image.Load<Rgba32>(lossless);
        Assert.Equal(new CicpColorDescription(9, 16, 0, fullRange: true), decodedLossless.Metadata.Cicp);
        Assert.Equal(GetBytes(image), GetBytes(decodedLossless));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(9, true)]
    public void EveryMatrixTheLibraryConvertsIsCodedAndReadBack(int matrix, bool fullRange)
    {
        using var image = CreateSmoothImage(40, 24, transparent: true);
        var source = GetBytes(image);
        var cicp = new CicpColorDescription(1, 13, (byte)matrix, fullRange);
        image.Metadata.Cicp = cicp;
        var data = Encode(image, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 100, ChromaSubsampling = HeicChromaSubsampling.Ratio444 });
        var reader = ReferenceHeif.Parse(data);
        Assert.Equal(new ReferenceHeif.ColorDescription(1, 13, matrix, fullRange), reader.PrimaryItem!.Nclx);
        AssertVideoSignal(reader.PrimaryItem, cicp);

        // The alpha plane is in full range whatever the range of the color image, and has no color description
        AssertVideoSignal(reader.AlphaItem!, new CicpColorDescription(2, 2, 2, fullRange: true));
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(cicp, decoded.Metadata.Cicp);
        var actual = GetBytes(decoded);
        for (var i = 0; i < source.Length; i++)
        {
            // The highest quality without chroma subsampling: the error is the one of the conversions, which round twice
            Assert.True(Math.Abs(source[i] - actual[i]) <= ((i & 3) is 3 ? 0 : 6), $"byte {i}: {source[i]} was decoded as {actual[i]}");
        }
    }

    [Fact]
    public void AColorDescriptionWithoutAMatrixTheLibraryConvertsIsWrittenWithBt601()
    {
        // The matrix of the description cannot be used (ICtCp): the samples are converted with BT.601 in full range, and
        // the file says so, with the primaries and the transfer function of the image
        using var image = CreateSmoothImage(16, 16, transparent: false);
        image.Metadata.Cicp = new CicpColorDescription(9, 16, 14, fullRange: false);
        var data = Encode(image, new HeicEncoder { Compression = HeicCompression.Lossy });
        var item = ReferenceHeif.Parse(data).PrimaryItem!;
        Assert.Equal(new ReferenceHeif.ColorDescription(9, 16, 6, FullRange: true), item.Nclx);
        AssertVideoSignal(item, new CicpColorDescription(9, 16, 6, fullRange: true));
    }

    [Theory]
    [InlineData(HeicCompression.Lossless)]
    [InlineData(HeicCompression.Lossy)]
    public void TheRangeOfTheColorDescriptionIsKeptByGrayLossyOutput(HeicCompression compression)
    {
        var pixels = new Gray8[32 * 24];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Gray8((byte)((i % 32 * 8) + (i / 32)));
        }

        using var image = Image.ImportPixelData<Gray8>(pixels, 32, 24);
        image.Metadata.Cicp = new CicpColorDescription(1, 13, 1, fullRange: false);
        var data = Encode(image, new HeicEncoder { Compression = compression, Quality = 100 });
        var item = ReferenceHeif.Parse(data).PrimaryItem!;

        // A monochrome image has no matrix to apply, but the description keeps the one of the image; the lossless coding
        // stores the samples as they are, in full range
        var lossless = compression is HeicCompression.Lossless;
        Assert.Equal(new ReferenceHeif.ColorDescription(1, 13, 1, FullRange: lossless), item.Nclx);
        AssertVideoSignal(item, new CicpColorDescription(1, 13, 1, fullRange: lossless));
        using var decoded = Image.Load(data);
        Assert.Equal(PixelFormat.Gray8, decoded.PixelFormat);
        Assert.Equal(new CicpColorDescription(1, 13, 1, fullRange: lossless), decoded.Metadata.Cicp);
        var actual = new byte[pixels.Length];
        decoded.Frames[0].CopyPixelBytesTo(actual);
        for (var i = 0; i < pixels.Length; i++)
        {
            // The limited range has 220 steps for 256 values: one step of error, and the one of the quantizer
            Assert.True(Math.Abs(pixels[i].Value - actual[i]) <= (lossless ? 0 : 3), $"pixel {i}: {pixels[i].Value} was decoded as {actual[i]}");
        }
    }

    [Theory]
    [InlineData(HeicCompression.Lossless, false, false)]
    [InlineData(HeicCompression.Lossless, true, false)]
    [InlineData(HeicCompression.Lossy, false, false)]
    [InlineData(HeicCompression.Lossy, true, true)]
    public void MetadataIsStoredAsPropertiesAndItems(HeicCompression compression, bool transparent, bool sequence)
    {
        using var image = CreateSmoothImage(18, 10, transparent);
        if (sequence)
        {
            using var second = CreateSmoothImage(18, 10, transparent, phase: 4);
            image.AppendFrame(second.Frames[0]);
        }

        image.Metadata.IccProfile = IccProfile.Srgb;
        image.Metadata.ExifProfile = new ExifProfile(new MetadataBlob(Exif));
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(Xmp));
        image.Metadata.Cicp = new CicpColorDescription(1, 13, 1, fullRange: true);
        var data = Encode(image, new HeicEncoder { Compression = compression });
        var reader = ReferenceHeif.Parse(data);
        var item = reader.PrimaryItem!;

        // The color description, then the profile, which takes precedence for the readers that apply it
        var matrix = compression is HeicCompression.Lossless ? 0 : 1;
        Assert.Equal(new ReferenceHeif.ColorDescription(1, 13, matrix, FullRange: true), item.Nclx);
        Assert.Equal(IccProfile.Srgb.Data.ToArray(), item.IccProfile?.ToArray());
        Assert.Equal(["ispe", "pixi", "hvcC", "colr", "colr"], item.Associations.Select(association => association.Property.Type));
        Assert.All(item.Associations.Where(association => association.Property.Type is "colr"), association => Assert.False(association.IsEssential));

        // An 'Exif' item, whose data start with the offset of the TIFF header, and an XMP item of the MIME type of RDF;
        // both describe the image ('cdsc') and precede the coded images in the media data
        var descriptions = reader.FindDescriptions(item.Id);
        Assert.Equal(["Exif", "mime"], descriptions.Select(description => description.Type));
        Assert.Equal([0, 0, 0, 0, .. Exif], reader.ReadItemData(descriptions[0]));
        Assert.Equal(Exif, reader.ReadExifPayload(descriptions[0]));
        Assert.Equal("application/rdf+xml", descriptions[1].ContentType);
        Assert.Equal(Xmp, reader.ReadItemData(descriptions[1]));
        Assert.Equal(transparent || sequence ? ["auxl", "cdsc", "cdsc"] : ["cdsc", "cdsc"], reader.ItemReferences.Select(reference => reference.Type));
        Assert.All(reader.ItemReferences, reference => Assert.Equal([item.Id], reference.ToItemIds));
        var first = reader.Items.Where(entry => entry.Type is "hvc1").Min(entry => entry.Extents[0].Offset);
        Assert.True(descriptions[0].Extents[0].Offset < descriptions[1].Extents[0].Offset && descriptions[1].Extents[0].Offset < first);

        // The alpha plane has no color information
        if (reader.AlphaItem is { } alpha)
        {
            Assert.Null(alpha.Nclx);
            Assert.Null(alpha.IccProfile);
        }

        if (sequence)
        {
            // The sample entry of the color track has the color information of the item
            Assert.Equal(new ReferenceHeif.ColorDescription(1, 13, matrix, FullRange: true), reader.ColorTrack!.Nclx);
            Assert.Equal(IccProfile.Srgb.Data.ToArray(), reader.ColorTrack.IccProfile?.ToArray());
            Assert.Null(reader.AlphaTrack!.Nclx);
            Assert.Null(reader.AlphaTrack.IccProfile);
        }

        using var decoded = Image.Load(data);
        Assert.Equal(sequence ? 2 : 1, decoded.Frames.Count);
        Assert.Equal(IccProfile.Srgb.Data.ToArray(), decoded.Metadata.IccProfile?.Data.ToArray());
        Assert.Equal(Exif, decoded.Metadata.ExifProfile?.Data.ToArray());
        Assert.Equal(Xmp, decoded.Metadata.XmpProfile?.Data.ToArray());
        Assert.Equal(new CicpColorDescription(1, 13, (byte)matrix, fullRange: true), decoded.Metadata.Cicp);

        // The properties are in the header of the file; the items are in its media data, which a full scan reads
        Assert.Equal(IccProfile.Srgb.Data.ToArray(), Image.Identify(data).Metadata.IccProfile?.Data.ToArray());
        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(IccProfile.Srgb.Data.ToArray(), info.Metadata.IccProfile?.Data.ToArray());
        Assert.Equal(Exif, info.Metadata.ExifProfile?.Data.ToArray());
        Assert.Equal(Xmp, info.Metadata.XmpProfile?.Data.ToArray());
    }

    [Fact]
    public void MetadataThatTheFormatCannotStoreFollowsTheMetadataPolicy()
    {
        using var image = CreateImage(6, 5, seed: 12, transparent: false);
        var source = GetBytes(image);
        image.Metadata.IccProfile = IccProfile.Srgb;
        image.Metadata.ExifProfile = new ExifProfile(new MetadataBlob(Exif));
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(Xmp));
        image.Metadata.Cicp = new CicpColorDescription(1, 13, 1, fullRange: true);
        image.Metadata.Orientation = ExifOrientation.RightTop;

        // HEIC has no field for the resolution nor for text
        foreach (var (feature, change) in new (string, Action<ImageMetadata>)[]
        {
            ("Metadata: resolution", metadata => metadata.Resolution = new ImageResolution(300, 300)),
            ("Metadata: text entry 'Comment'", metadata => metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "a comment"))),
        })
        {
            using var copy = image.CloneAs<Rgba32>();
            change(copy.Metadata);
            using var stream = new MemoryStream();
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => copy.Save(stream, new HeicEncoder()));
            Assert.Equal((ImageFormat.Heic, feature), (exception.Format, exception.Feature));
            Assert.Equal(0, stream.Length);

            // What the format stores is kept when the rest is discarded
            var discarded = Encode(copy, new HeicEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
            using var kept = Image.Load<Rgba32>(discarded);
            Assert.Null(kept.Metadata.Resolution);
            Assert.Empty(kept.Metadata.TextEntries);
            Assert.Equal(IccProfile.Srgb.Data.ToArray(), kept.Metadata.IccProfile?.Data.ToArray());

            // The orientation tag of the EXIF block is the orientation of the image, which the properties of the item hold
            Assert.Equal(CreateExif(ExifOrientation.RightTop), kept.Metadata.ExifProfile?.Data.ToArray());
            Assert.Equal(Xmp, kept.Metadata.XmpProfile?.Data.ToArray());
            Assert.Equal(new CicpColorDescription(1, 13, 0, fullRange: true), kept.Metadata.Cicp);
            Assert.Equal(ExifOrientation.RightTop, kept.Metadata.Orientation);
            Assert.Equal(source, GetBytes(kept));

            // Stripping leaves what is needed to decode the samples: no profile, no item, no transformation, and the
            // color description of the coding only
            var stripped = Encode(copy, new HeicEncoder { MetadataHandling = MetadataHandling.Strip });
            var reader = ReferenceHeif.Parse(stripped);
            var item = Assert.Single(reader.Items);
            Assert.Equal(["ispe", "pixi", "hvcC", "colr"], item.Associations.Select(association => association.Property.Type));
            Assert.Null(item.IccProfile);
            Assert.Equal(new ReferenceHeif.ColorDescription(2, 2, 0, FullRange: true), item.Nclx);
            Assert.Empty(reader.ItemReferences);
            using var bare = Image.Load<Rgba32>(stripped);
            Assert.Null(bare.Metadata.IccProfile);
            Assert.Null(bare.Metadata.ExifProfile);
            Assert.Null(bare.Metadata.XmpProfile);
            Assert.Equal(ExifOrientation.TopLeft, bare.Metadata.Orientation);
            Assert.Equal(source, GetBytes(bare));
        }
    }

    [Fact]
    public void TheOrientationOfAnImageSequenceFollowsTheMetadataPolicy()
    {
        // The transformations are properties of an item: a track has none
        using var image = CreateSmoothImage(8, 6, transparent: false);
        using (var second = CreateSmoothImage(8, 6, transparent: false, phase: 3))
        {
            image.AppendFrame(second.Frames[0]);
        }

        image.Metadata.Orientation = ExifOrientation.RightTop;
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new HeicEncoder()));
        Assert.Equal((ImageFormat.Heic, "Metadata: orientation of an image sequence"), (exception.Format, exception.Feature));
        Assert.Equal(0, stream.Length);

        foreach (var handling in new[] { MetadataHandling.DiscardUnsupported, MetadataHandling.Strip })
        {
            var data = Encode(image, new HeicEncoder { MetadataHandling = handling });
            var reader = ReferenceHeif.Parse(data);
            Assert.Empty(reader.PrimaryItem!.Transforms);
            using var decoded = Image.Load<Rgba32>(data);
            Assert.HasCount(2, decoded.Frames);
            Assert.Equal(ExifOrientation.TopLeft, decoded.Metadata.Orientation);
            Assert.Equal(GetBytes(image, 1), GetBytes(decoded, 1));
        }
    }

    [Theory]
    [InlineData(HeicCompression.Lossless, false, null)]
    [InlineData(HeicCompression.Lossless, true, 3)]
    [InlineData(HeicCompression.Lossy, true, 1)]
    public void AnAnimationIsAnImageSequenceWithExactDurationsAndPlayCount(HeicCompression compression, bool transparent, int? totalPlays)
    {
        FrameDuration[] durations = [new(1, 30), new(1, 25), new(0, 1), new(7, 3), new(1, 25)];
        using var image = CreateSmoothImage(40, 26, transparent);
        for (var i = 1; i < durations.Length; i++)
        {
            using var frame = CreateSmoothImage(40, 26, transparent, phase: i * 5);
            image.AppendFrame(frame.Frames[0]);
        }

        for (var i = 0; i < durations.Length; i++)
        {
            image.Frames[i].Metadata.Duration = durations[i];
        }

        image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
        var data = Encode(image, new HeicEncoder { Compression = compression, Quality = 95 });
        var reader = ReferenceHeif.Parse(data);

        // The brands of the sequence and of its first image, which is also a still image: 'hevc' and 'heic' for the Main
        // profile, 'hevx' and 'heix' otherwise
        var lossy = compression is HeicCompression.Lossy;
        Assert.Equal(lossy ? "hevc" : "hevx", reader.MajorBrand);
        Assert.True(reader.IsSequence);
        Assert.Equal([lossy ? "heic" : "heix", lossy ? "hevc" : "hevx", "msf1", "iso8", "mif1", "miaf"], reader.CompatibleBrands);
        Assert.False(reader.IsFragmented);

        // The boxes of the first image, the moov box in the room reserved for it, then the media data
        Assert.Equal(["ftyp", "meta", "moov", "free", "mdat"], reader.TopLevelBoxes.Select(box => box.Type));
        Assert.Equal(data.Length, reader.TopLevelBoxes[^1].Offset + reader.TopLevelBoxes[^1].Size);
        Assert.True(reader.TopLevelBoxes[^1].HasLargeSize);

        // The time scale is the smallest one that holds 1/30, 1/25, 0 and 7/3 exactly: 150
        var track = reader.ColorTrack;
        Assert.NotNull(track);
        Assert.Equal(150u, track.Timescale);
        Assert.Equal(150u, reader.MovieTimescale);
        Assert.Equal([5u, 6u, 0u, 350u, 6u], track.Samples.Select(sample => sample.Duration));
        Assert.Equal("hvc1", track.SampleEntryType);
        Assert.Equal("pict", track.HandlerType);
        Assert.Equal((40, 26), (track.Width, track.Height));
        Assert.Equal((40.0, 26.0), (track.PresentationWidth, track.PresentationHeight));
        Assert.Null(track.CleanAperture);
        Assert.Equal(totalPlays, track.TotalPlays);

        // The duration of the track is the duration of all the plays, in units of the time scale of the movie
        Assert.Equal(totalPlays is { } plays ? 367ul * (ulong)plays : null, track.Duration);

        // 32-bit chunk offsets: some readers do not read the 64-bit ones
        Assert.Equal("stco", track.ChunkOffsetBox);

        // The pictures follow each other in output order: no composition offset
        Assert.Null(track.CompositionOffsets);
        AssertStream(track.HevcConfiguration, StreamKind.Sequence, chromaFormat: lossy ? 1 : 3, bitDepth: 8, codedSize: (40, 26));

        // The first image is the only key frame of both tracks: the others are predicted from the image before them
        Assert.Equal([1u], track.SyncSamples);
        var alpha = reader.AlphaTrack;
        Assert.Equal([1u], alpha?.SyncSamples);

        // An opaque image of a pixel format with alpha still has an alpha track in a sequence
        Assert.NotNull(alpha);
        Assert.Equal("auxv", alpha.HandlerType);
        Assert.Equal(ReferenceHeif.HevcAlphaAuxiliaryType, alpha.AuxiliaryType);
        Assert.HasCount(durations.Length, alpha.Samples);
        Assert.Equal(("auxl", track.Id), (Assert.Single(alpha.References).Type, Assert.Single(Assert.Single(alpha.References).TrackIds)));
        Assert.Equal(track.Samples.Select(sample => sample.Duration), alpha.Samples.Select(sample => sample.Duration));
        Assert.Equal((150u, totalPlays, "stco"), (alpha.Timescale, alpha.TotalPlays, alpha.ChunkOffsetBox));
        AssertStream(alpha.HevcConfiguration, StreamKind.Sequence, chromaFormat: 0, bitDepth: 8, codedSize: (40, 26));

        // The samples are in the media data, one after the other, and the first image is also the primary item
        long position = track.Samples[0].Offset - alpha.Samples[0].Size;
        for (var i = 0; i < durations.Length; i++)
        {
            Assert.Equal(position, alpha.Samples[i].Offset);
            position += alpha.Samples[i].Size;
            Assert.Equal(position, track.Samples[i].Offset);
            position += track.Samples[i].Size;
        }

        Assert.Equal(data.Length, position);
        Assert.Equal((track.Samples[0].Offset, (long)track.Samples[0].Size), Assert.Single(reader.PrimaryItem!.Extents));
        Assert.Equal((alpha.Samples[0].Offset, (long)alpha.Samples[0].Size), Assert.Single(reader.AlphaItem!.Extents));

        // The items have the configuration of the tracks: the same stream
        Assert.Equal(track.ConfigurationRecord.ToArray(), reader.PrimaryItem.Find("hvcC")!.Payload.ToArray());
        Assert.Equal(alpha.ConfigurationRecord.ToArray(), reader.AlphaItem.Find("hvcC")!.Payload.ToArray());

        // A sample is the slice segment of its picture: an instantaneous decoding refresh picture, then pictures that
        // refer to the one before them
        foreach (var stream in new[] { track, alpha })
        {
            for (var i = 0; i < durations.Length; i++)
            {
                Assert.Equal([i is 0 ? NalIdrNLp : NalTrailR], GetNalUnitTypes(stream.HevcConfiguration!, reader.ReadSample(stream.Samples[i])));
            }
        }

        using var decoded = Image.Load<Rgba32>(data);
        Assert.HasCount(durations.Length, decoded.Frames);
        Assert.Equal(totalPlays, decoded.Animation?.TotalPlays);
        for (var i = 0; i < durations.Length; i++)
        {
            Assert.Equal(durations[i], decoded.Frames[i].Metadata.Duration);
            var expected = GetBytes(image, i);
            var actual = GetBytes(decoded, i);
            if (compression is HeicCompression.Lossless)
            {
                Assert.Equal(expected, actual);
            }
            else
            {
                for (var j = 0; j < expected.Length; j++)
                {
                    Assert.True(Math.Abs(expected[j] - actual[j]) <= (j % 4 is 3 ? 0 : 24), $"frame {i}, byte {j}: {expected[j]} was decoded as {actual[j]}");
                }
            }
        }

        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal<(ImageFormat, int?, bool?, int?)>((ImageFormat.Heic, durations.Length, true, totalPlays), (info.Format, info.FrameCount, info.IsAnimated, info.Animation?.TotalPlays));
    }

    [Theory]
    [InlineData(HeicCompression.Lossless)]
    [InlineData(HeicCompression.Lossy)]
    public void TheKeyFrameIntervalSelectsTheSyncSamples(HeicCompression compression)
    {
        // The same scene with a square that moves: most of an image is the image before it
        const int FrameCount = 5;
        using var image = CreateSmoothImage(48, 40, transparent: true);
        for (var i = 1; i < FrameCount; i++)
        {
            using var frame = CreateSmoothImage(48, 40, transparent: true);
            image.AppendFrame(frame.Frames[0]);
        }

        for (var i = 0; i < FrameCount; i++)
        {
            var left = 4 + (6 * i);
            image.Frames[i].ProcessPixelBytes(accessor =>
            {
                for (var y = 4; y < 14; y++)
                {
                    var row = accessor.GetRowSpan(y).Slice(4 * left, 4 * 10);
                    for (var x = 0; x < row.Length; x += 4)
                    {
                        row[x] = 250;
                        row[x + 1] = 20;
                        row[x + 2] = 40;
                        row[x + 3] = 255;
                    }
                }
            });
        }

        var sizes = new Dictionary<int, int>();
        foreach (var (interval, expected) in new (int, uint[]?)[] { (0, [1]), (1, null), (2, [1, 3, 5]), (4, [1, 5]), (100, [1]) })
        {
            var data = Encode(image, new HeicEncoder { Compression = compression, Quality = 90, KeyFrameInterval = interval, Effort = 2 });
            sizes[interval] = data.Length;
            var reader = ReferenceHeif.Parse(data);
            Assert.True(reader.IsSequence);

            // A track without sync sample box has only sync samples
            var color = reader.ColorTrack!;
            var alpha = reader.AlphaTrack!;
            Assert.Equal(expected, color.SyncSamples);
            Assert.Equal(expected, alpha.SyncSamples);

            // The sync samples are the instantaneous decoding refresh pictures of the stream; a sequence of key frames only
            // has the profile of the pictures that refer to no other one
            var kind = interval is 1 ? StreamKind.IntraSequence : StreamKind.Sequence;
            AssertStream(color.HevcConfiguration, kind, chromaFormat: compression is HeicCompression.Lossy ? 1 : 3, bitDepth: 8, codedSize: (48, 40));
            AssertStream(alpha.HevcConfiguration, kind, chromaFormat: 0, bitDepth: 8, codedSize: (48, 40));
            foreach (var track in new[] { color, alpha })
            {
                for (var i = 0; i < FrameCount; i++)
                {
                    var isKeyFrame = expected is null || expected.Contains((uint)(i + 1));
                    Assert.Equal([isKeyFrame ? NalIdrNLp : NalTrailR], GetNalUnitTypes(track.HevcConfiguration!, reader.ReadSample(track.Samples[i])));
                }
            }

            using var decoded = Image.Load<Rgba32>(data);
            Assert.HasCount(FrameCount, decoded.Frames);
            for (var i = 0; i < FrameCount; i++)
            {
                var expectedBytes = GetBytes(image, i);
                var actual = GetBytes(decoded, i);
                if (compression is HeicCompression.Lossless)
                {
                    Assert.Equal(expectedBytes, actual);
                    continue;
                }

                // Alpha is lossless, and the error of the color samples does not grow from an image to the next one
                double squaredError = 0;
                for (var j = 0; j < expectedBytes.Length; j++)
                {
                    if ((j & 3) is 3)
                    {
                        Assert.Equal(expectedBytes[j], actual[j]);
                        continue;
                    }

                    double difference = expectedBytes[j] - actual[j];
                    squaredError += difference * difference;
                }

                var psnr = 10 * Math.Log10(255.0 * 255.0 * expectedBytes.Length * 3 / 4 / Math.Max(squaredError, 1));
                TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"interval {interval}, frame {i}: {psnr:F2} dB"));
                Assert.True(psnr >= 30, string.Create(CultureInfo.InvariantCulture, $"interval {interval}: the PSNR of frame {i} is {psnr:F2} dB"));
            }
        }

        // The fewer key frames, the smaller the file
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(", ", sizes.Select(pair => $"{pair.Key}: {pair.Value}")));
        Assert.True(sizes[0] < sizes[4] && sizes[4] < sizes[2] && sizes[2] < sizes[1], string.Join(", ", sizes.Select(pair => $"{pair.Key}: {pair.Value}")));
        Assert.Equal(sizes[0], sizes[100]);
    }

    [Fact]
    public void AnImageSequenceWithoutDurationPlaysOnceOrForever()
    {
        using var image = CreateSmoothImage(8, 8, transparent: false);
        using (var second = CreateSmoothImage(8, 8, transparent: false, phase: 3))
        {
            image.AppendFrame(second.Frames[0]);
        }

        // An edit list gives the duration of one play: an edit without duration is not repeated, so a sequence that repeats
        // forever has no edit list, which is what a reader plays forever
        foreach (var totalPlays in new int?[] { null, 1 })
        {
            image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
            var data = Encode(image, new HeicEncoder());
            var reader = ReferenceHeif.Parse(data);
            foreach (var track in new[] { reader.ColorTrack!, reader.AlphaTrack! })
            {
                Assert.Equal([0u, 0u], track.Samples.Select(sample => sample.Duration));
                if (totalPlays is null)
                {
                    Assert.Null(track.EditList);
                }
                else
                {
                    Assert.NotNull(track.EditList);
                    Assert.False(track.EditList.IsRepeating);
                    Assert.Equal(0ul, Assert.Single(track.EditList.Entries).SegmentDuration);
                }
            }

            using var decoded = Image.Load<Rgba32>(data);
            Assert.HasCount(2, decoded.Frames);
            Assert.Equal(totalPlays, decoded.Animation?.TotalPlays);
            Assert.Equal(GetBytes(image, 1), GetBytes(decoded, 1));
        }

        // A number of plays is the ratio of two durations
        image.Animation = new AnimationMetadata { TotalPlays = 3 };
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new HeicEncoder()));
        Assert.Equal((ImageFormat.Heic, "HEIC play count"), (exception.Format, exception.Feature));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void ASequenceThatIsPlayedAFiniteNumberOfTimesHasARepeatedEdit(int totalPlays)
    {
        using var image = CreateSmoothImage(8, 8, transparent: false);
        using (var second = CreateSmoothImage(8, 8, transparent: false, phase: 3))
        {
            image.AppendFrame(second.Frames[0]);
        }

        image.Frames[0].Metadata.Duration = new FrameDuration(1, 4);
        image.Frames[1].Metadata.Duration = new FrameDuration(3, 4);
        image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
        var reader = ReferenceHeif.Parse(Encode(image, new HeicEncoder()));
        foreach (var track in new[] { reader.ColorTrack!, reader.AlphaTrack! })
        {
            // One edit of the duration of the media (1/4 + 3/4 of a second in quarters of a second), which is repeated
            // until the duration of the track
            Assert.Equal(4u, track.Timescale);
            Assert.NotNull(track.EditList);
            Assert.Equal(totalPlays > 1, track.EditList.IsRepeating);
            Assert.Equal(new ReferenceHeif.Edit(4, 0, 1, 0), Assert.Single(track.EditList.Entries));
            Assert.Equal(4ul * (ulong)totalPlays, track.Duration);
            Assert.Equal(totalPlays, track.TotalPlays);
        }
    }

    [Fact]
    public void APlayCountThatTheDurationOfATrackCannotHoldIsRejected()
    {
        // The duration of a track is the duration of all the plays, in 64 bits: 3 x 4,000,000,000 seconds, 2^31 - 1 times
        using var image = CreateSmoothImage(8, 8, transparent: false);
        for (var i = 1; i < 3; i++)
        {
            using var next = CreateSmoothImage(8, 8, transparent: false, phase: i);
            image.AppendFrame(next.Frames[0]);
        }

        foreach (var frame in image.Frames)
        {
            frame.Metadata.Duration = new FrameDuration(4_000_000_000, 1);
        }

        image.Animation = new AnimationMetadata { TotalPlays = int.MaxValue };
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new HeicEncoder { Effort = 0 }));
        Assert.Equal((ImageFormat.Heic, "HEIC play count"), (exception.Format, exception.Feature));

        // The largest play count of durations that fit is stored
        foreach (var frame in image.Frames)
        {
            frame.Metadata.Duration = new FrameDuration(1, 1);
        }

        var data = Encode(image, new HeicEncoder { Effort = 0 });
        Assert.Equal(3ul * int.MaxValue, ReferenceHeif.Parse(data).ColorTrack!.Duration);
        using var decoded = Image.Load(data);
        Assert.Equal(int.MaxValue, decoded.Animation?.TotalPlays);
    }

    [Fact]
    public void ALongSequenceFitsTheRoomReservedForItsIndex()
    {
        // The box that describes the samples is written last, in the room reserved for the least compressible tables of
        // this number of images; what it leaves is a free box
        const int FrameCount = 300;
        using var image = CreateImage(8, 6, seed: 20, transparent: true);
        for (var i = 1; i < FrameCount; i++)
        {
            using var next = CreateImage(8, 6, seed: 20 + (i / 3), transparent: true);
            image.AppendFrame(next.Frames[0]);
        }

        for (var i = 0; i < FrameCount; i++)
        {
            image.Frames[i].Metadata.Duration = new FrameDuration(1 + (i % 4 / 3), 30);
        }

        var data = Encode(image, new HeicEncoder { Effort = 0, KeyFrameInterval = 7 });
        var reader = ReferenceHeif.Parse(data);
        Assert.Equal(["ftyp", "meta", "moov", "free", "mdat"], reader.TopLevelBoxes.Select(box => box.Type));
        foreach (var track in new[] { reader.ColorTrack!, reader.AlphaTrack! })
        {
            Assert.HasCount(FrameCount, track.Samples);
            Assert.Equal(Enumerable.Range(0, FrameCount).Select(i => i % 4 is 3 ? 2u : 1u), track.Samples.Select(sample => sample.Duration));
            Assert.Equal(Enumerable.Range(0, FrameCount).Where(i => i % 7 is 0).Select(i => (uint)(i + 1)), track.SyncSamples);
            Assert.Equal("stco", track.ChunkOffsetBox);
        }

        // The last sample is the end of the file
        var last = reader.ColorTrack!.Samples[^1];
        Assert.Equal(data.Length, last.Offset + last.Size);
        using var decoded = Image.Load<Rgba32>(data);
        Assert.HasCount(FrameCount, decoded.Frames);
        foreach (var index in new[] { 0, 1, 6, 7, 150, FrameCount - 1 })
        {
            Assert.Equal(GetBytes(image, index), GetBytes(decoded, index));
            Assert.Equal(image.Frames[index].Metadata.Duration, decoded.Frames[index].Metadata.Duration);
        }
    }

    [Fact]
    public void AnImageSequenceNeedsASeekableStreamAndTheNumberOfItsFrames()
    {
        using var image = CreateSmoothImage(8, 8, transparent: false);
        using (var second = CreateSmoothImage(8, 8, transparent: false, phase: 3))
        {
            image.AppendFrame(second.Frames[0]);
        }

        using var stream = new TestOutputStream();
        Assert.Equal("stream", Assert.Throws<ArgumentException>(() => image.Save(stream, new HeicEncoder())).ParamName);
        Assert.Equal(0, stream.BytesWritten);

        using var seekable = new MemoryStream();
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(seekable, new ImageWriterOptions(8, 8) { Encoder = new HeicEncoder() }));
        Assert.Equal(0, seekable.Length);

        // A writer that announced two images does not complete with one, nor accept a third one
        using (var writer = Image.CreateWriter<Rgba32>(seekable, new ImageWriterOptions(8, 8) { Encoder = new HeicEncoder(), ExpectedFrameCount = 2, LeaveOpen = true }))
        {
            writer.WriteFrame((ImageFrame<Rgba32>)image.Frames[0]);
            Assert.Throws<InvalidOperationException>(writer.Complete);
            writer.WriteFrame((ImageFrame<Rgba32>)image.Frames[1]);
            Assert.Throws<InvalidOperationException>(() => writer.WriteFrame((ImageFrame<Rgba32>)image.Frames[0]));
            writer.Complete();
        }

        using var decoded = Image.Load<Rgba32>(seekable.ToArray());
        Assert.HasCount(2, decoded.Frames);
        Assert.Equal(GetBytes(image, 1), GetBytes(decoded, 1));
    }

    [Fact]
    public void AnImageOfOneFrameIsAStillImageAndCannotStoreAnimationSettings()
    {
        using var image = CreateImage(8, 8, seed: 14, transparent: false);
        image.Animation = new AnimationMetadata { TotalPlays = 2 };
        using var stream = new MemoryStream();
        Assert.Equal(ImageFormat.Heic, Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new HeicEncoder())).Format);
        Assert.Equal(0, stream.Length);

        // A writer of one image writes a still image
        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(8, 8) { Encoder = new HeicEncoder(), ExpectedFrameCount = 1, LeaveOpen = true }))
        {
            writer.WriteFrame((ImageFrame<Rgba32>)image.Frames[0]);
            writer.Complete();
        }

        image.Animation = null;
        Assert.Equal(Encode(image, new HeicEncoder()), stream.ToArray());
        Assert.False(ReferenceHeif.Parse(stream.ToArray()).IsSequence);
    }

    [Fact]
    public void DurationsWithoutACommonTimeScaleNeedARoundingPolicy()
    {
        // 1/4294967291 and 1/4294967279 (two primes below 2^32) have no common time scale of 32 bits
        using var image = CreateSmoothImage(8, 8, transparent: false);
        using (var second = CreateSmoothImage(8, 8, transparent: false, phase: 3))
        {
            image.AppendFrame(second.Frames[0]);
        }

        image.Frames[0].Metadata.Duration = new FrameDuration(1, 4294967291);
        image.Frames[1].Metadata.Duration = new FrameDuration(1073741820, 4294967279);
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new HeicEncoder()));
        Assert.Equal((ImageFormat.Heic, "HEIC frame duration precision"), (exception.Format, exception.Feature));
        Assert.Contains("HeicEncoder.DurationRounding", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, stream.Length);

        // Rounded to microseconds: 0 and 250,000 (0.25 s)
        var data = Encode(image, new HeicEncoder { DurationRounding = FrameDurationRounding.RoundToNearest });
        var track = ReferenceHeif.Parse(data).ColorTrack;
        Assert.Equal(1_000_000u, track!.Timescale);
        Assert.Equal([0u, 250_000u], track.Samples.Select(sample => sample.Duration));
        using var decoded = Image.Load(data);
        Assert.Equal([new FrameDuration(0, 1), new FrameDuration(1, 4)], decoded.Frames.Select(frame => frame.Metadata.Duration));
    }

    [Fact]
    public void ADurationThatTheTimeScaleCannotHoldIsRejected()
    {
        // 2^32 units of the time scale of the other duration
        using var image = CreateSmoothImage(8, 8, transparent: false);
        using (var second = CreateSmoothImage(8, 8, transparent: false, phase: 3))
        {
            image.AppendFrame(second.Frames[0]);
        }

        image.Frames[0].Metadata.Duration = new FrameDuration(1, 1000);
        image.Frames[1].Metadata.Duration = new FrameDuration(4294968, 1);
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new HeicEncoder()));
        Assert.Equal((ImageFormat.Heic, "HEIC frame duration range"), (exception.Format, exception.Feature));
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData(HeicChromaSubsampling.Ratio420, 1, 21, 13, 22, 14)]
    [InlineData(HeicChromaSubsampling.Ratio420, 1, 20, 13, 20, 14)]
    [InlineData(HeicChromaSubsampling.Ratio420, 1, 21, 12, 22, 12)]
    [InlineData(HeicChromaSubsampling.Ratio422, 2, 21, 13, 22, 13)]
    [InlineData(HeicChromaSubsampling.Ratio422, 2, 20, 13, 20, 13)]
    [InlineData(HeicChromaSubsampling.Ratio444, 3, 21, 13, 21, 13)]
    [InlineData(HeicChromaSubsampling.Ratio420, 1, 1, 1, 2, 2)]
    public void AnImageSequenceOfAnOddSizeHasACleanApertureInItsSampleEntries(HeicChromaSubsampling subsampling, int chromaFormat, int width, int height, int codedWidth, int codedHeight)
    {
        const int FrameCount = 3;
        using var image = CreateSmoothImage(width, height, transparent: true);
        for (var i = 1; i < FrameCount; i++)
        {
            using var frame = CreateSmoothImage(width, height, transparent: true, phase: 2 * i);
            image.AppendFrame(frame.Frames[0]);
        }

        var data = Encode(image, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 95, ChromaSubsampling = subsampling });
        var reader = ReferenceHeif.Parse(data);
        var hasAperture = (width, height) != (codedWidth, codedHeight);
        foreach (var (track, isAlpha) in new[] { (reader.ColorTrack!, false), (reader.AlphaTrack!, true) })
        {
            // The track header has the size of the images, the sample entry the size of the coded pictures, and its
            // clean aperture selects the images in them
            Assert.Equal((width, height), (track.PresentationWidth, track.PresentationHeight));
            Assert.Equal((codedWidth, codedHeight), (track.Width, track.Height));
            AssertStream(track.HevcConfiguration, StreamKind.Sequence, isAlpha ? 0 : chromaFormat, bitDepth: 8, codedSize: (codedWidth, codedHeight));
            if (hasAperture)
            {
                Assert.Equal(new ReferenceHeif.CleanAperture((uint)width, 1, (uint)height, 1, width - codedWidth, 2, height - codedHeight, 2), track.CleanAperture);
                Assert.Equal((0, 0, width, height), track.CleanAperture!.GetRectangle(codedWidth, codedHeight));
            }
            else
            {
                Assert.Null(track.CleanAperture);
            }
        }

        // The first image is also an item, with the same clean aperture as a property
        Assert.Equal((codedWidth, codedHeight), reader.PrimaryItem!.Size);
        Assert.Equal(reader.ColorTrack!.CleanAperture, reader.PrimaryItem.CleanAperture);
        Assert.Equal(reader.ColorTrack.CleanAperture, reader.AlphaItem!.CleanAperture);

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(new Size(width, height), decoded.Size);
        Assert.HasCount(FrameCount, decoded.Frames);
        Assert.Equal(new Size(width, height), Image.Identify(data).Size);
        for (var i = 0; i < FrameCount; i++)
        {
            var expected = GetBytes(image, i);
            var actual = GetBytes(decoded, i);
            for (var j = 0; j < expected.Length; j++)
            {
                // The samples outside the image are not the image: alpha is exact up to the last column and row, and
                // the colors of the last column and row are as close as the others
                Assert.True(Math.Abs(expected[j] - actual[j]) <= (j % 4 is 3 ? 0 : 24), $"frame {i}, byte {j}: {expected[j]} was decoded as {actual[j]}");
            }
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 3)]
    [InlineData(7, 9)]
    [InlineData(8, 8)]
    [InlineData(9, 7)]
    [InlineData(15, 17)]
    [InlineData(17, 15)]
    [InlineData(31, 33)]
    [InlineData(65, 1)]
    [InlineData(1, 65)]
    [InlineData(129, 67)]
    public void EverySizeIsWrittenInEverySampleFormat(int width, int height)
    {
        using var image = CreateImage(width, height, seed: (width * 100) + height, transparent: true);

        // Slopes that do not depend on the size of the image, so that subsampled chroma keeps them in the smallest images
        var gradient = new Rgba32[width * height];
        for (var i = 0; i < gradient.Length; i++)
        {
            var (x, y) = (i % width, i / width);
            gradient[i] = new Rgba32((byte)(40 + x), (byte)(200 - y), (byte)(90 + ((x + y) / 2)), (byte)((x * 7) ^ (y * 13)));
        }

        using var smooth = Image.ImportPixelData<Rgba32>(gradient, width, height);
        var source = GetBytes(image);
        var smoothSource = GetBytes(smooth);

        // Lossless: 4:4:4 pictures of the size of the image, exact
        using (var decoded = Image.Load<Rgba32>(Encode(image, new HeicEncoder { Effort = 2 })))
        {
            Assert.Equal(new Size(width, height), decoded.Size);
            Assert.Equal(source, GetBytes(decoded));
        }

        foreach (var subsampling in new[] { HeicChromaSubsampling.Ratio420, HeicChromaSubsampling.Ratio422, HeicChromaSubsampling.Ratio444 })
        {
            // The coded pictures have a whole number of chroma samples
            var codedWidth = subsampling is HeicChromaSubsampling.Ratio444 ? width : (width + 1) & ~1;
            var codedHeight = subsampling is HeicChromaSubsampling.Ratio420 ? (height + 1) & ~1 : height;
            var data = Encode(smooth, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 95, ChromaSubsampling = subsampling, Effort = 2 });
            var reader = ReferenceHeif.Parse(data);
            var context = $"{width}x{height} {subsampling}";
            Assert.Equal((codedWidth, codedHeight), reader.PrimaryItem!.Size, message: context);
            Assert.Equal((codedWidth, codedHeight), reader.AlphaItem!.Size, message: context);
            Assert.Equal((codedWidth, codedHeight) != (width, height), reader.PrimaryItem.CleanAperture is not null, message: context);
            Assert.Equal((0, 0, width, height), reader.PrimaryItem.CleanAperture?.GetRectangle(codedWidth, codedHeight) ?? (0, 0, width, height), message: context);
            using var decoded = Image.Load<Rgba32>(data);
            Assert.Equal(new Size(width, height), decoded.Size, message: context);
            var actual = GetBytes(decoded);
            for (var i = 0; i < smoothSource.Length; i++)
            {
                Assert.True(Math.Abs(smoothSource[i] - actual[i]) <= (i % 4 is 3 ? 0 : 8), $"{context}, byte {i}: {smoothSource[i]} was decoded as {actual[i]}");
            }
        }
    }

    [Fact]
    public void TheLevelIsTheLowestOneThatHoldsThePictures()
    {
        // ITU-T H.265, Table A.8 and clause A.4.1: a level holds pictures of at most MaxLumaPs luma samples, whose width and
        // height are at most Sqrt(8 x MaxLumaPs). Level 1 has 36,864 samples (543 in a direction), level 2 has 122,880
        // (991), level 2.1 has 245,760 (1,402), level 3 has 552,960 (2,103), level 3.1 has 983,040 (2,804), level 4 has
        // 2,228,224 (4,222), level 5 has 8,912,896 (8,444). general_level_idc is 30 times the number of the level
        foreach (var (width, height, level) in new[]
        {
            (64, 64, 30), (192, 192, 30), (200, 192, 60), (536, 8, 30), (544, 8, 60), (8, 544, 60), (984, 8, 60), (992, 8, 63), (1400, 8, 63), (1408, 8, 90),
            (2104, 8, 93), (2800, 8, 93), (2808, 8, 120), (4216, 8, 120), (4224, 8, 150), (8, 4224, 150), (8440, 8, 150), (8448, 8, 180),
        })
        {
            using var image = new Image<Gray8>(width, height, new Gray8(100), new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 9000, MaxHeight = 9000 } });
            var data = Encode(image, new HeicEncoder { Effort = 0 });
            var configuration = ReferenceHeif.Parse(data).PrimaryItem!.HevcConfiguration!;
            Assert.Equal(level, configuration.LevelIdc, message: $"{width}x{height}");
            Assert.False(configuration.TierFlag);
            var sequenceParameterSet = configuration.Arrays[1].NalUnits[0].Span;
            var sequence = ReferenceHevcStream.ReadSequenceParameterSet(sequenceParameterSet);
            Assert.Equal((width, height), (sequence.CropWidth, sequence.CropHeight));

            // Clause A.4.1, item d: the coding tree blocks of level 5 and above have 32 samples at least
            Assert.True(level < 150 || HevcSequenceParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(sequenceParameterSet)).CtbLog2SizeY >= 5, $"{width}x{height}: the size of the coding tree blocks");
            using var decoded = Image.Load(data);
            Assert.Equal(new Size(width, height), decoded.Size);
            var pixels = new byte[width * height];
            decoded.Frames[0].CopyPixelBytesTo(pixels);
            Assert.False(pixels.AsSpan().ContainsAnyExcept((byte)100), $"{width}x{height}");
        }
    }

    [Theory]
    [InlineData(256, 256, HeicCompression.Lossy, true)] // 65,536 samples: decoders decode the rows concurrently
    [InlineData(256, 256, HeicCompression.Lossless, true)]
    [InlineData(255, 256, HeicCompression.Lossless, false)] // fewer samples than the decoders decode concurrently
    [InlineData(2048, 32, HeicCompression.Lossless, false)] // one row of coding tree blocks
    [InlineData(2048, 33, HeicCompression.Lossless, true)]
    [InlineData(32, 2048, HeicCompression.Lossless, false)] // one column of coding tree blocks
    [InlineData(33, 2048, HeicCompression.Lossy, true)]
    public void TheRowsOfALargeStillPictureAreSubstreamsOfTheWavefront(int width, int height, HeicCompression compression, bool wavefront)
    {
        using var image = CreateImage(width, height, seed: width + height, transparent: true);
        var data = Encode(image, new HeicEncoder { Compression = compression, Effort = 0 });
        var reader = ReferenceHeif.Parse(data);

        // entropy_coding_sync_enabled_flag (ITU-T H.265, clause 7.4.3.3), for the color image and for the alpha plane
        Assert.Equal(wavefront, UsesWavefront(reader.PrimaryItem!.HevcConfiguration!));
        Assert.Equal(wavefront, UsesWavefront(reader.AlphaItem!.HevcConfiguration!));
        using var decoded = Image.Load<Rgba32>(data);
        using var decodedByWorkers = Image.Load<Rgba32>(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { MaxDegreeOfParallelism = 4 } });
        Assert.Equal(GetBytes(decoded), GetBytes(decodedByWorkers));
        if (compression is HeicCompression.Lossless)
        {
            Assert.Equal(GetBytes(image), GetBytes(decoded));
        }
    }

    [Fact]
    public void TheTilesOfAGridAndThePicturesOfASequenceHaveNoWavefront()
    {
        // Decoders decode the tiles of a grid concurrently, and the pictures of a sequence are predicted from one another
        using var image = CreateImage(512, 256, seed: 3, transparent: false);
        var reader = ReferenceHeif.Parse(Encode(image, new HeicEncoder { GridTileSize = new Size(256, 256), Effort = 0 }));
        var tiles = reader.GetGridTiles(reader.PrimaryItem!);
        Assert.HasCount(2, tiles);
        Assert.All(tiles, tile => Assert.False(UsesWavefront(tile.HevcConfiguration!)));

        using var frame = CreateImage(512, 256, seed: 4, transparent: false);
        image.AppendFrame(frame.Frames[0]);
        reader = ReferenceHeif.Parse(Encode(image, new HeicEncoder { Effort = 0 }));
        Assert.False(UsesWavefront(reader.ColorTrack!.HevcConfiguration!));
    }

    [Fact]
    public void TheLevelHoldsTheLargestCodedPicture()
    {
        // A lossless picture of noise has about one byte per sample: more than the Monochrome profile allows at level 1
        // (ITU-T H.265, clause A.4.2, item g: FormatCapabilityFactor 1.0 x 36,864 samples / MinCr 2 = 18,432 bytes), at
        // levels 2 and 2.1 (the same limit) and at level 3 (MaxLumaSr / 300 = 55,296 samples: 27,648 bytes), but not at
        // level 3.1 (110,592 samples: 55,296 bytes). A flat picture of that size is at level 1
        var configuration = new ImageConfiguration { MaxDegreeOfParallelism = 2 };
        var noise = new byte[192 * 192];
        new Random(42).NextBytes(noise);
        using var image = Image.ImportPixelBytes<Gray8>(noise, 192, 192, configuration: configuration);
        var data = Encode(image, new HeicEncoder { Effort = 0 });
        var reader = ReferenceHeif.Parse(data);
        var item = reader.PrimaryItem!;
        var length = reader.ReadItemData(item).Length;
        Assert.True(length is > 27_648 and <= 55_000, $"{length} bytes");
        Assert.Equal((93, false), (item.HevcConfiguration!.LevelIdc, item.HevcConfiguration.TierFlag));
        AssertStream(item.HevcConfiguration, StreamKind.Still, chromaFormat: 0, bitDepth: 8, codedSize: (192, 192));
        AssertPixels(data, noise);

        // The tiles of a grid share their configuration, which holds the largest tile, whichever worker codes it: the first
        // tile is flat, and the second one, which the second worker codes, is the noise
        var pixels = new byte[384 * 192];
        for (var y = 0; y < 192; y++)
        {
            pixels.AsSpan(y * 384, 192).Fill(100);
            noise.AsSpan(y * 192, 192).CopyTo(pixels.AsSpan((y * 384) + 192));
        }

        using var grid = Image.ImportPixelBytes<Gray8>(pixels, 384, 192, configuration: configuration);
        data = Encode(grid, new HeicEncoder { Effort = 0, GridTileSize = new Size(192, 192) });
        reader = ReferenceHeif.Parse(data);
        var tiles = reader.GetGridTiles(reader.PrimaryItem!);
        Assert.HasCount(2, tiles);
        Assert.HasCountLessThan(18_432, reader.ReadItemData(tiles[0]));
        Assert.HasCountGreaterThan(27_648, reader.ReadItemData(tiles[1]));
        foreach (var tile in tiles)
        {
            Assert.Equal((93, false), (tile.HevcConfiguration!.LevelIdc, tile.HevcConfiguration.TierFlag));
            AssertStream(tile.HevcConfiguration, StreamKind.Still, chromaFormat: 0, bitDepth: 8, codedSize: (192, 192));
        }

        AssertPixels(data, pixels);

        // A sequence whose first image is flat and whose second image is the noise: the track holds both, and the first
        // image, which is also the primary item, has the configuration of the track
        using var sequence = new Image<Gray8>(192, 192, new Gray8(100), configuration);
        sequence.AppendFrame(image.Frames[0]);
        data = Encode(sequence, new HeicEncoder { Effort = 0 });
        reader = ReferenceHeif.Parse(data);
        var track = reader.ColorTrack!;
        Assert.True(track.Samples[0].Size < 18_432);
        Assert.True(track.Samples[1].Size > 27_648);
        Assert.Equal((93, false), (track.HevcConfiguration!.LevelIdc, track.HevcConfiguration.TierFlag));
        AssertStream(track.HevcConfiguration, StreamKind.Sequence, chromaFormat: 0, bitDepth: 8, codedSize: (192, 192));
        Assert.Equal(track.ConfigurationRecord.ToArray(), reader.PrimaryItem!.Find("hvcC")!.Payload.ToArray());
        using var decoded = Image.Load<Gray8>(data);
        Assert.HasCount(2, decoded.Frames);
        var actual = new byte[192 * 192];
        decoded.Frames[1].CopyPixelBytesTo(actual);
        Assert.Equal(noise, actual);

        static void AssertPixels(byte[] data, byte[] expected)
        {
            using var decoded = Image.Load<Gray8>(data);
            var actual = new byte[expected.Length];
            decoded.Frames[0].CopyPixelBytesTo(actual);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ImagesLargerThanThePicturesOfTheFormatAreRejected()
    {
        // The sizes of the pictures are 16-bit fields of the sample entry of a track, and a picture is a whole number of
        // coding blocks of 8 samples: 65,528 samples per side at most
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 70000, MaxHeight = 70000 } };
        foreach (var (width, height) in new[] { (65529, 1), (1, 65529) })
        {
            using var image = new Image<Gray8>(width, height, configuration);
            using var stream = new MemoryStream();
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new HeicEncoder { Effort = 0 }));
            Assert.Equal(ImageFormat.Heic, exception.Format);
            Assert.Contains("65528", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, stream.Length);
            Assert.Throws<UnsupportedImageFeatureException>(() => Image.CreateWriter<Gray8>(stream, new ImageWriterOptions(width, height) { Encoder = new HeicEncoder(), ExpectedFrameCount = 2, Configuration = configuration }));
            Assert.Equal(0, stream.Length);
        }

        // The largest picture exceeds the limits of every level: it is labeled with level 8.5, in the High tier (ITU-T
        // H.265, clause A.4.1)
        using var largest = new Image<Gray8>(65528, 1, configuration);
        var data = Encode(largest, new HeicEncoder { Effort = 0 });
        var item = ReferenceHeif.Parse(data).PrimaryItem!;
        Assert.Equal((65528, 1), item.Size);
        Assert.Equal((255, true), (item.HevcConfiguration!.LevelIdc, item.HevcConfiguration.TierFlag));
        var sequence = ReferenceHevcStream.ReadSequenceParameterSet(item.HevcConfiguration.Arrays[1].NalUnits[0].Span);
        Assert.Equal((65528, 8, 65528, 1), (sequence.Width, sequence.Height, sequence.CropWidth, sequence.CropHeight));
        using var decoded = Image.Load(data, new ImageDecodeOptions { Configuration = configuration });
        Assert.Equal(new Size(65528, 1), decoded.Size);
    }

    [Theory]
    [InlineData(150, 100, 64, 64, 3, 2)] // the right column and the bottom row extend past the image
    [InlineData(128, 64, 64, 64, 2, 1)] // tiles that cover the image exactly
    [InlineData(65, 130, 64, 65, 2, 2)] // one column and one row of samples in the last tiles, tiles of 65 rows
    [InlineData(70, 200, 128, 64, 1, 4)] // a tile wider than the image: one column
    public void AnImageLargerThanATileIsAGridOfTiles(int width, int height, int tileWidth, int tileHeight, int columns, int rows)
    {
        using var image = CreateImage(width, height, seed: width + height, transparent: true);
        var data = Encode(image, new HeicEncoder { GridTileSize = new Size(tileWidth, tileHeight), Effort = 0 });
        var reader = ReferenceHeif.Parse(data);
        Assert.Equal("heix", reader.MajorBrand);

        // ISO/IEC 23008-12, section 6.6.2.3: the primary item is a grid, whose data are its descriptor, derived from its
        // tiles in row-major order; the tiles are hidden items of one size
        var grid = reader.PrimaryItem;
        Assert.NotNull(grid);
        Assert.Equal("grid", grid.Type);
        Assert.Equal((width, height), grid.Size);
        Assert.Equal((rows, columns, (long)width, (long)height), reader.ReadGridDescriptor(grid));
        Assert.Null(grid.HevcConfiguration);
        var tiles = reader.GetGridTiles(grid);
        Assert.HasCount(columns * rows, tiles);
        Assert.Equal(Enumerable.Range(2, columns * rows).Select(id => (uint)id), tiles.Select(tile => tile.Id));
        Assert.False(grid.IsHidden);

        // Each tile is a still picture of the size of the tile: the conformance window crops the coding blocks of 8
        // samples to the tiles of 65 rows
        foreach (var tile in tiles)
        {
            Assert.Equal("hvc1", tile.Type);
            Assert.True(tile.IsHidden);
            Assert.Equal((tileWidth, tileHeight), tile.Size);
            Assert.Null(tile.CleanAperture);
            AssertStream(tile.HevcConfiguration, StreamKind.Still, chromaFormat: 3, bitDepth: 8, codedSize: (tileWidth, tileHeight));
            Assert.Equal([NalIdrNLp], GetNalUnitTypes(tile.HevcConfiguration!, reader.ReadItemData(tile)));
        }

        // The alpha plane is a grid of the same tiles
        var alpha = reader.AlphaItem;
        Assert.NotNull(alpha);
        Assert.Equal("grid", alpha.Type);
        Assert.Equal((rows, columns, (long)width, (long)height), reader.ReadGridDescriptor(alpha));
        var alphaTiles = reader.GetGridTiles(alpha);
        Assert.HasCount(columns * rows, alphaTiles);
        foreach (var tile in alphaTiles)
        {
            Assert.Equal("hvc1", tile.Type);
            Assert.True(tile.IsHidden);
            Assert.Equal((tileWidth, tileHeight), tile.Size);
            AssertStream(tile.HevcConfiguration, StreamKind.Still, chromaFormat: 0, bitDepth: 8, codedSize: (tileWidth, tileHeight));
        }

        // Lossless: every sample of the image is decoded back, and nothing of what the tiles hold past the image, by one
        // worker or by several
        var source = GetBytes(image);
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal((width, height), (decoded.Width, decoded.Height));
        Assert.Equal(source, GetBytes(decoded));
        using var decodedByWorkers = Image.Load<Rgba32>(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { MaxDegreeOfParallelism = 4 } });
        Assert.Equal(source, GetBytes(decodedByWorkers));
        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal((ImageFormat.Heic, width, height), (info.Format, info.Width, info.Height));
        Assert.True(info.MayHaveTransparency);
    }

    [Theory]
    [InlineData(HeicChromaSubsampling.Ratio420, 130, 70, 66, 66, false, 1, "heic")]
    [InlineData(HeicChromaSubsampling.Ratio420, 150, 100, 64, 64, true, 1, "heic")]
    [InlineData(HeicChromaSubsampling.Ratio422, 130, 71, 66, 65, false, 2, "heix")]
    [InlineData(HeicChromaSubsampling.Ratio444, 131, 71, 65, 65, true, 3, "heix")]
    public void TheTilesOfALossyGridAreWholeChromaSamples(HeicChromaSubsampling subsampling, int width, int height, int tileWidth, int tileHeight, bool transparent, int chromaFormat, string brand)
    {
        using var image = CreateSmoothImage(width, height, transparent);
        var source = GetBytes(image);
        var data = Encode(image, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 90, ChromaSubsampling = subsampling, GridTileSize = new Size(tileWidth, tileHeight), Effort = 2 });
        var reader = ReferenceHeif.Parse(data);

        // 8-bit 4:2:0 tiles are Main Still Picture pictures, which every HEIC reader decodes, whatever the alpha plane is
        Assert.Equal(brand, reader.MajorBrand);
        var grid = reader.PrimaryItem!;
        Assert.Equal("grid", grid.Type);
        Assert.Equal(new ReferenceHeif.ColorDescription(2, 2, 6, FullRange: true), grid.Nclx);
        Assert.Null(grid.CleanAperture);

        // The tiles have a whole number of chroma samples: the conformance window crops the coding blocks to them, and no
        // clean aperture is needed
        foreach (var tile in reader.GetGridTiles(grid))
        {
            Assert.Equal((tileWidth, tileHeight), tile.Size);
            Assert.Null(tile.CleanAperture);
            AssertStream(tile.HevcConfiguration, StreamKind.Still, chromaFormat, bitDepth: 8, codedSize: (tileWidth, tileHeight));
        }

        Assert.Equal(transparent, reader.AlphaItem is not null);
        if (transparent)
        {
            Assert.Equal("grid", reader.AlphaItem!.Type);
            Assert.All(reader.GetGridTiles(reader.AlphaItem), tile => AssertStream(tile.HevcConfiguration, StreamKind.Still, chromaFormat: 0, bitDepth: 8, codedSize: (tileWidth, tileHeight)));
        }

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(new Size(width, height), decoded.Size);
        var actual = GetBytes(decoded);
        double squaredError = 0;
        for (var i = 0; i < source.Length; i += 4)
        {
            Assert.Equal(source[i + 3], actual[i + 3]);
            for (var channel = 0; channel < 3; channel++)
            {
                double difference = source[i + channel] - actual[i + channel];
                squaredError += difference * difference;
            }
        }

        var psnr = 10 * Math.Log10(255.0 * 255.0 * (source.Length / 4 * 3) / Math.Max(squaredError, 1));
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{subsampling}: PSNR {psnr:F2} dB, {data.Length} bytes"));
        Assert.True(psnr >= 40, string.Create(CultureInfo.InvariantCulture, $"PSNR {psnr:F2} dB"));
    }

    [Fact]
    public void AGridIsOnlyWrittenForAStillImageThatIsLargerThanATile()
    {
        // An image that one tile holds is one item, whatever the setting
        using var small = CreateSmoothImage(64, 48, transparent: false);
        Assert.Equal(Encode(small, new HeicEncoder { Effort = 0 }), Encode(small, new HeicEncoder { GridTileSize = new Size(64, 64), Effort = 0 }));

        // An opaque image of a pixel format with alpha has no alpha grid
        using var opaque = CreateSmoothImage(100, 70, transparent: false);
        var reader = ReferenceHeif.Parse(Encode(opaque, new HeicEncoder { GridTileSize = new Size(64, 64), Effort = 0 }));
        Assert.Equal("grid", reader.PrimaryItem!.Type);
        Assert.Null(reader.AlphaItem);
        Assert.Equal(["dimg"], reader.ItemReferences.Select(reference => reference.Type));

        // An image sequence is not a grid
        using var frame = CreateSmoothImage(100, 70, transparent: false, phase: 3);
        opaque.AppendFrame(frame.Frames[0]);
        opaque.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        opaque.Frames[1].Metadata.Duration = new FrameDuration(1, 10);
        var sequence = ReferenceHeif.Parse(Encode(opaque, new HeicEncoder { GridTileSize = new Size(64, 64), Effort = 0 }));
        Assert.True(sequence.IsSequence);
        Assert.Equal("hvc1", sequence.PrimaryItem!.Type);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(16)]
    public void TheTilesOfAGridAreCodedByWorkersAsByOne(int workers)
    {
        // 20 tiles of 64x64, whose last column and last row extend past the image: batches of 3 tiles span two rows of 5
        // tiles, and 16 workers code every tile at once. The file is the one of one worker, with its alpha grid
        using var transparent = CreateImage(300, 250, seed: 17, transparent: true);
        var lossless = new HeicEncoder { GridTileSize = new Size(64, 64), Effort = 0 };
        var expected = Encode(transparent, lossless);
        using var transparentWithWorkers = WithWorkers(transparent, workers);
        Assert.Equal(expected, Encode(transparentWithWorkers, lossless));

        // Lossy tiles of subsampled chroma, at the default effort, which searches more and filters the tiles
        using var opaque = CreateSmoothImage(300, 250, transparent: false);
        var lossy = new HeicEncoder { Compression = HeicCompression.Lossy, GridTileSize = new Size(64, 64) };
        using var opaqueWithWorkers = WithWorkers(opaque, workers);
        Assert.Equal(Encode(opaque, lossy), Encode(opaqueWithWorkers, lossy));

        // Every sample of the image is decoded back
        using var decoded = Image.Load<Rgba32>(expected);
        Assert.Equal(GetBytes(transparent), GetBytes(decoded));
    }

    [Fact]
    public void AGridOfSubsampledChromaNeedsEvenSizes()
    {
        static HeicEncoder Lossy(HeicChromaSubsampling subsampling, int tileWidth, int tileHeight) => new() { Compression = HeicCompression.Lossy, ChromaSubsampling = subsampling, GridTileSize = new Size(tileWidth, tileHeight), Effort = 0 };
        using var even = CreateSmoothImage(100, 70, transparent: false);
        using var oddWidth = CreateSmoothImage(101, 70, transparent: false);
        using var oddHeight = CreateSmoothImage(100, 71, transparent: false);

        // 4:2:0: the widths and the heights of the tiles and of the image are even
        Assert.Equal("grid", ReferenceHeif.Parse(Encode(even, Lossy(HeicChromaSubsampling.Ratio420, 64, 64))).PrimaryItem!.Type);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Encode(oddWidth, Lossy(HeicChromaSubsampling.Ratio420, 64, 64)));
        Assert.Equal(ImageFormat.Heic, exception.Format);
        Assert.Contains("HeicEncoder.ChromaSubsampling", exception.Message, StringComparison.Ordinal);
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(oddHeight, Lossy(HeicChromaSubsampling.Ratio420, 64, 64)));
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(even, Lossy(HeicChromaSubsampling.Auto, 65, 64)));
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(even, Lossy(HeicChromaSubsampling.Ratio420, 64, 65)));

        // 4:2:2: only the widths; 4:4:4, the lossless coding and gray pixels: any size
        Assert.Equal("grid", ReferenceHeif.Parse(Encode(oddHeight, Lossy(HeicChromaSubsampling.Ratio422, 64, 65))).PrimaryItem!.Type);
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(oddWidth, Lossy(HeicChromaSubsampling.Ratio422, 64, 64)));
        Assert.Equal("grid", ReferenceHeif.Parse(Encode(oddWidth, Lossy(HeicChromaSubsampling.Ratio444, 65, 65))).PrimaryItem!.Type);
        Assert.Equal("grid", ReferenceHeif.Parse(Encode(oddWidth, new HeicEncoder { GridTileSize = new Size(65, 65), Effort = 0 })).PrimaryItem!.Type);
        using var gray = oddWidth.CloneAs<Gray8>();
        Assert.Equal("grid", ReferenceHeif.Parse(Encode(gray, Lossy(HeicChromaSubsampling.Ratio420, 65, 65))).PrimaryItem!.Type);

        // At most 256 columns and 256 rows
        using var wide = new Image<Gray8>(64 * 257, 64);
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(wide, new HeicEncoder { GridTileSize = new Size(64, 64), Effort = 0 }));
    }

    [Fact]
    public void TheLargestTileIsTheLargestPicture()
    {
        // A tile is an HEVC picture: 65,528 samples per side at most, a whole number of coding blocks of 8 samples. A tile
        // taller than the image is one row of tiles, which extend past the image. Such a picture exceeds the limits of
        // every level: it is labeled with level 8.5, in the High tier (ITU-T H.265, clause A.4.1)
        using var image = new Image<Gray8>(100, 70, new Gray8(100));
        var data = Encode(image, new HeicEncoder { GridTileSize = new Size(64, 65528), Effort = 0 });
        var reader = ReferenceHeif.Parse(data);
        Assert.Equal((1, 2, 100L, 70L), reader.ReadGridDescriptor(reader.PrimaryItem!));
        foreach (var tile in reader.GetGridTiles(reader.PrimaryItem!))
        {
            Assert.Equal((64, 65528), tile.Size);
            var configuration = tile.HevcConfiguration!;
            Assert.Equal((255, true), (configuration.LevelIdc, configuration.TierFlag));
            var sequence = ReferenceHevcStream.ReadSequenceParameterSet(configuration.Arrays[1].NalUnits[0].Span);
            Assert.Equal((64, 65528, 64, 65528), (sequence.Width, sequence.Height, sequence.CropWidth, sequence.CropHeight));
        }

        // The decoder checks the size of the tiles against the canvas limits, which the default limits do not allow
        using var decoded = Image.Load<Gray8>(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxHeight = 70000 } } });
        Assert.Equal(new Size(100, 70), decoded.Size);
        var pixels = new byte[100 * 70];
        decoded.Frames[0].CopyPixelBytesTo(pixels);
        Assert.False(pixels.AsSpan().ContainsAnyExcept((byte)100));
    }

    [Fact]
    public void ALimitFailureWritesNothing()
    {
        using var image = CreateImage(64, 48, seed: 15, transparent: true);
        foreach (var compression in new[] { HeicCompression.Lossless, HeicCompression.Lossy })
        {
            // The pixels of the image, and less than the planes of one picture
            var limits = new ImageResourceLimits { MaxLiveAllocationBytes = (64 * 48 * 4) + 4096 };
            using var constrained = Image.ImportPixelBytes<Rgba32>(GetBytes(image), 64, 48, configuration: new ImageConfiguration { Limits = limits });
            using var stream = new MemoryStream();
            var exception = Assert.Throws<ImageResourceLimitException>(() => constrained.Save(stream, new HeicEncoder { Compression = compression }));
            Assert.Equal((ImageResourceLimitKind.LiveAllocationBytes, limits.MaxLiveAllocationBytes), (exception.Kind, exception.Limit));
            Assert.Equal(0, stream.Length);

            // The same image without the limit
            using var decoded = Image.Load<Rgba32>(Encode(image, new HeicEncoder { Compression = compression }));
            Assert.Equal(new Size(64, 48), decoded.Size);
        }
    }

    [Fact]
    public async Task CancellationStopsTheEncodingAndWritesNothing()
    {
        using var image = CreateSmoothImage(64, 64, transparent: true);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        using var stream = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsync(stream, new HeicEncoder(), canceled.Token));
        Assert.Equal(0, stream.Length);

        // A sequence: the token is canceled when the first bytes are written, which is after the first image is coded;
        // the second image is not coded to its end
        using (var second = CreateSmoothImage(64, 64, transparent: true, phase: 5))
        {
            image.AppendFrame(second.Frames[0]);
        }

        using var source = new CancellationTokenSource();
        await using var output = new TestOutputStream { Seekable = true, AllowPatching = true, CancellationSource = source, CancelAtPosition = 0 };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsync(output, new HeicEncoder { Compression = HeicCompression.Lossy }, source.Token));
        var complete = Encode(image, new HeicEncoder { Compression = HeicCompression.Lossy });
        Assert.True(output.BytesWritten < complete.Length, $"{output.BytesWritten} bytes were written; the file has {complete.Length} bytes");
    }

    [Theory]
    [InlineData(StreamKind.Still, 3, 8, 4, "111000111")]
    [InlineData(StreamKind.Still, 1, 8, 3, "000000000")]
    [InlineData(StreamKind.Still, 1, 10, 2, "000000010")]
    [InlineData(StreamKind.Sequence, 1, 8, 1, "000000000")]
    [InlineData(StreamKind.IntraSequence, 1, 8, 1, "000000000")]
    [InlineData(StreamKind.Sequence, 1, 10, 2, "000000000")]
    [InlineData(StreamKind.Sequence, 2, 12, 4, "100100001")]
    [InlineData(StreamKind.IntraSequence, 3, 10, 4, "110000101")]
    [InlineData(StreamKind.Sequence, 0, 8, 4, "111111001")]
    public void TheExpectedProfilesAreThoseOfAnnexA(StreamKind kind, int chromaFormat, int bitDepth, int profileIdc, string flags)
    {
        // The table of the tests against a few rows of Table A.2 and of clauses A.3.2 to A.3.4, read independently
        Assert.Equal((profileIdc, flags), (GetExpectedProfile(kind, chromaFormat, bitDepth).ProfileIdc, GetExpectedProfile(kind, chromaFormat, bitDepth).Flags));
    }

    /// <summary>
    /// Checks the configuration record of a stream against what the stream must be: its parameter sets, which must say the
    /// same as the record, its sample format, the size of its pictures, and the narrowest profile of Annex A that has them.
    /// </summary>
    private static void AssertStream(ReferenceHeif.HevcConfiguration? configuration, StreamKind kind, int chromaFormat, int bitDepth, (int Width, int Height) codedSize)
    {
        Assert.NotNull(configuration);

        // ISO/IEC 14496-15, section 8.3.3.1: one complete array per parameter set, and NAL units with a length of 4 bytes
        Assert.Equal(4, configuration.NalUnitLengthSize);
        Assert.Equal([ReferenceHevcStream.NalVps, ReferenceHevcStream.NalSps, ReferenceHevcStream.NalPps], configuration.Arrays.Select(array => array.NalUnitType));
        foreach (var array in configuration.Arrays)
        {
            Assert.True(array.IsComplete);
            var unit = Assert.Single(array.NalUnits);
            Assert.Equal(array.NalUnitType, ReferenceHevcStream.GetNalUnitType(unit.Span));
            Assert.Equal(0, ReferenceHevcStream.GetLayerId(unit.Span));
        }

        // The record repeats the general profile, tier and level of the parameter sets: the twelve bytes that follow the
        // first byte of the sequence parameter set, and the first four bytes of the video parameter set (ITU-T H.265,
        // clauses 7.3.2.1, 7.3.2.2 and 7.3.3)
        var sequenceParameterSet = configuration.Arrays[1].NalUnits[0].Span;
        Span<byte> general = stackalloc byte[12];
        general[0] = (byte)((configuration.ProfileSpace << 6) | (configuration.TierFlag ? 0x20 : 0) | configuration.ProfileIdc);
        BinaryPrimitives.WriteUInt32BigEndian(general[1..], configuration.ProfileCompatibilityFlags);
        BinaryPrimitives.WriteUInt16BigEndian(general[5..], (ushort)(configuration.ConstraintIndicatorFlags >> 32));
        BinaryPrimitives.WriteUInt32BigEndian(general[7..], (uint)configuration.ConstraintIndicatorFlags);
        general[11] = (byte)configuration.LevelIdc;
        Assert.Equal(general.ToArray(), ReferenceHevcStream.GetRbsp(sequenceParameterSet).AsSpan(1, 12).ToArray());
        Assert.Equal(general.ToArray(), ReferenceHevcStream.GetRbsp(configuration.Arrays[0].NalUnits[0].Span).AsSpan(4, 12).ToArray());

        // The sample format of the record is the one of the sequence parameter set, whose conformance window is the size
        // of the coded pictures: the size that the container gives to the item or to the sample entry
        var sequence = ReferenceHevcStream.ReadSequenceParameterSet(sequenceParameterSet);
        Assert.Equal((chromaFormat, bitDepth, bitDepth), (configuration.ChromaFormat, configuration.BitDepthLuma, configuration.BitDepthChroma));
        Assert.Equal((chromaFormat, bitDepth, bitDepth), (sequence.ChromaFormatIdc, sequence.BitDepthLuma, sequence.BitDepthChroma));
        Assert.Equal((0, 0, codedSize.Width, codedSize.Height), (sequence.CropLeft, sequence.CropTop, sequence.CropWidth, sequence.CropHeight));

        // The pictures are a whole number of minimum coding blocks, of 8 samples at least
        Assert.Equal((0, 0), (sequence.Width % 8, sequence.Height % 8));
        Assert.True(sequence.Width >= codedSize.Width && sequence.Width < codedSize.Width + 64 && sequence.Height >= codedSize.Height && sequence.Height < codedSize.Height + 64, $"pictures of {sequence.Width}x{sequence.Height} for a window of {codedSize}");

        // The profile: progressive frames (general_progressive_source_flag, general_non_packed_constraint_flag and
        // general_frame_only_constraint_flag), in the Main tier of the general profile space
        var (profileIdc, compatibility, flags) = GetExpectedProfile(kind, chromaFormat, bitDepth);
        Assert.Equal((0, false, profileIdc), (configuration.ProfileSpace, configuration.TierFlag, configuration.ProfileIdc));
        Assert.Equal(compatibility, configuration.ProfileCompatibilityFlags);
        Assert.Equal(0xBul, configuration.ConstraintIndicatorFlags >> 44);
        Assert.Equal(flags, Convert.ToString((long)((configuration.ConstraintIndicatorFlags >> 35) & 0x1FF), 2).PadLeft(9, '0'));
        Assert.Equal(0ul, configuration.ConstraintIndicatorFlags & 0x7FFFFFFFF);
    }

    /// <summary>
    /// The narrowest profile of Rec. ITU-T H.265, Annex A that has a sample format: <c>general_profile_idc</c>, the
    /// compatibility flags (the flag <c>j</c> is the bit <c>31 - j</c>), and the nine flags from
    /// <c>general_max_12bit_constraint_flag</c> to <c>general_lower_bit_rate_constraint_flag</c>, which Table A.2 gives
    /// for the format range extensions profiles (<c>general_profile_idc</c> 4) and whose eighth one,
    /// <c>general_one_picture_only_constraint_flag</c>, makes the Main 10 Still Picture profile of Main 10 (clause A.3.3).
    /// </summary>
    private static (int ProfileIdc, uint Compatibility, string Flags) GetExpectedProfile(StreamKind kind, int chromaFormat, int bitDepth)
    {
        const uint Main = 0x60000000;
        const uint Main10 = 0x20000000;
        const uint MainStillPicture = 0x70000000;
        const uint RangeExtensions = 0x08000000;
        var still = kind is StreamKind.Still;
        var intra = kind is not StreamKind.Sequence;
        return (chromaFormat, bitDepth) switch
        {
            // Monochrome, Monochrome 10 and Monochrome 12: there is no profile of intra pictures for monochrome pictures
            (0, 8) => (4, RangeExtensions, "111111001"),
            (0, 10) => (4, RangeExtensions, "110111001"),
            (0, _) => (4, RangeExtensions, "100111001"),

            // Main Still Picture and Main; Main 10 Still Picture and Main 10; Main 12 Intra and Main 12
            (1, 8) => still ? (3, MainStillPicture, "000000000") : (1, Main, "000000000"),
            (1, 10) => (2, Main10, still ? "000000010" : "000000000"),
            (1, _) => (4, RangeExtensions, intra ? "100110101" : "100110001"),

            // Main 4:2:2 10 and Main 4:2:2 12, for intra pictures or not: there is no 8-bit 4:2:2 profile
            (2, 8 or 10) => (4, RangeExtensions, intra ? "110100101" : "110100001"),
            (2, _) => (4, RangeExtensions, intra ? "100100101" : "100100001"),

            // Main 4:4:4 Still Picture, Main 4:4:4 Intra and Main 4:4:4; then the profiles of 10 and 12 bits
            (_, 8) => (4, RangeExtensions, still ? "111000111" : intra ? "111000101" : "111000001"),
            (_, 10) => (4, RangeExtensions, intra ? "110000101" : "110000001"),
            _ => (4, RangeExtensions, intra ? "100000101" : "100000001"),
        };
    }

    private static bool UsesWavefront(ReferenceHeif.HevcConfiguration configuration)
    {
        var unit = configuration.Arrays.Single(array => array.NalUnitType is ReferenceHevcStream.NalPps).NalUnits[0];
        return HevcPictureParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(unit.Span)).EntropyCodingSyncEnabledFlag;
    }

    private static int[] GetNalUnitTypes(ReferenceHeif.HevcConfiguration configuration, byte[] data)
        => [.. configuration.SplitNalUnits(data).Select(unit => ReferenceHevcStream.GetNalUnitType(unit.Span))];

    /// <summary>
    /// Checks the video signal description of the sequence parameter set of an item (ITU-T H.265, clause E.2.1) against the
    /// color description that the container gives to the image. The sequence parameter set is read by the parser of the
    /// decoder of the library; <c>trace_headers</c> of FFmpeg reads the same fields in the interoperability tests.
    /// </summary>
    private static void AssertVideoSignal(ReferenceHeif.Item item, CicpColorDescription expected)
    {
        var unit = item.HevcConfiguration!.Arrays.Single(array => array.NalUnitType is ReferenceHevcStream.NalSps).NalUnits[0];
        var vui = HevcSequenceParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(unit.Span)).Vui;
        Assert.NotNull(vui);

        // The range is always written: a stream that does not signal it is read as limited range by HEVC decoders
        Assert.True(vui.VideoSignalTypePresentFlag);
        Assert.Equal(expected.FullRange, vui.VideoFullRangeFlag);

        // The three code points, which are "unspecified" (2) when the description is absent
        Assert.Equal((expected.ColorPrimaries, expected.TransferCharacteristics, expected.MatrixCoefficients), ((byte)vui.ColourPrimaries, (byte)vui.TransferCharacteristics, (byte)vui.MatrixCoeffs));

        // The chroma samples of a 4:2:0 picture are at the left of their luma samples: the default location
        Assert.Equal(0, vui.ChromaLocInfoPresentFlag ? vui.ChromaSampleLocTypeTopField : 0);
    }

    private static byte[] CreateExif(ExifOrientation orientation)
        => new TiffBuilder(bigEndian: false).Ifd0(TiffBuilder.Short(ExifTiff.OrientationTag, (ushort)orientation), TiffBuilder.Ascii(0x0131, "software")).Build();

    private static Image<Rgba32> WithWorkers(Image<Rgba32> image, int workers)
    {
        var pixels = new Rgba32[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        return Image.ImportPixelData<Rgba32>(pixels, image.Width, image.Height, configuration: new ImageConfiguration { MaxDegreeOfParallelism = workers });
    }

    private static byte[] Encode(Image image, HeicEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);

        return stream.ToArray();
    }
}
