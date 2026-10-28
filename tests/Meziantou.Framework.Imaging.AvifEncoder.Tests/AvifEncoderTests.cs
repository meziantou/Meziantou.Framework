using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using static Meziantou.Framework.Imaging.Tests.HeifEncoderTestImages;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// AVIF encoding through the public save API. The structure of the output is validated with the independent harness reader
/// (<see cref="ReferenceHeif"/>: boxes, items, properties). The harness has no AV1 decoder: the samples are read back with
/// the decoder of the library, which the conformance vectors pin to the reference decoders byte for byte, and the reference
/// decoders themselves decode the output of the encoder in the interoperability tests.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class AvifEncoderTests
{
    [Fact]
    public void SettingsHaveDocumentedDefaultsAndRanges()
    {
        var encoder = new AvifEncoder();
        Assert.Equal(ImageFormat.Avif, encoder.Format);
        Assert.Equal(AvifCompression.Lossless, encoder.Compression);
        Assert.Equal(75, encoder.Quality);
        Assert.Equal(5, encoder.Effort);
        Assert.Equal(AvifChromaSubsampling.Auto, encoder.ChromaSubsampling);
        Assert.Null(encoder.BitDepth);
        Assert.False(encoder.AllowBitDepthReduction);
        Assert.False(encoder.ClearTransparentColors);
        Assert.Null(encoder.CleanAperture);
        Assert.False(encoder.PremultiplyAlpha);
        Assert.Null(encoder.GridTileSize);
        Assert.Equal(0, encoder.KeyFrameInterval);
        Assert.Equal(FrameDurationRounding.RequireExact, encoder.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);

        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { Compression = (AvifCompression)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { Quality = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { Quality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { Effort = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { Effort = 10 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { ChromaSubsampling = (AvifChromaSubsampling)4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { BitDepth = 9 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { BitDepth = 16 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { KeyFrameInterval = -1 });
        Assert.Equal(12, new AvifEncoder { KeyFrameInterval = 12 }.KeyFrameInterval);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { CleanAperture = new Rectangle(0, 0, 0, 1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { CleanAperture = new Rectangle(0, 0, 1, 0) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { CleanAperture = new Rectangle(-1, 0, 1, 1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { CleanAperture = new Rectangle(0, -1, 1, 1) });
        Assert.Equal(new Rectangle(1, 2, 3, 4), new AvifEncoder { CleanAperture = new Rectangle(1, 2, 3, 4) }.CleanAperture);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { GridTileSize = new Size(63, 64) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { GridTileSize = new Size(64, 63) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { GridTileSize = new Size(65537, 64) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { GridTileSize = new Size(64, 65537) });
        Assert.Equal(new Size(64, 65536), new AvifEncoder { GridTileSize = new Size(64, 65536) }.GridTileSize);
        foreach (var bitDepth in new int?[] { null, 8, 10, 12 })
        {
            Assert.Equal(bitDepth, new AvifEncoder { BitDepth = bitDepth }.BitDepth);
        }
    }

    [Theory]
    [InlineData("image.avif")]
    [InlineData("IMAGE.AVIF")]
    [InlineData("image.avifs")]
    public void PathSavesInferAvifFromTheExtension(string name)
    {
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("meziantou-avif-"));
        try
        {
            using var image = CreateImage(5, 3, seed: 1, transparent: false);
            var path = directory / name;
            image.Save(path);
            var reader = ReferenceHeif.Parse(File.ReadAllBytes(path));
            Assert.Equal("avif", reader.MajorBrand);
            Assert.Equal(ImageFormat.Avif, Image.Identify(path).Format);
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
        var data = Encode(image, new AvifEncoder());
        var reader = ReferenceHeif.Parse(data);

        // The structure: one AV1 item of the High profile, 8-bit 4:4:4 with the identity matrix in full range
        Assert.Equal("avif", reader.MajorBrand);
        Assert.Contains("mif1", reader.CompatibleBrands);
        Assert.Contains("miaf", reader.CompatibleBrands);
        Assert.Contains("MA1A", reader.CompatibleBrands);
        Assert.False(reader.IsSequence);
        var item = reader.PrimaryItem;
        Assert.NotNull(item);
        Assert.Equal("av01", item.Type);
        Assert.Equal((width, height), item.Size);
        Assert.Equal([8, 8, 8], item.PixelInformation);
        Assert.Equal(new ReferenceHeif.Av1Configuration(SequenceProfile: 1, Av1Levels.GetLevelIndex(width, height), BitDepth: 8, IsMonochrome: false, SubsamplingX: 0, SubsamplingY: 0, ChromaSamplePosition: 0), item.Configuration);
        Assert.Equal(new ReferenceHeif.ColorDescription(2, 2, 0, FullRange: true), item.Nclx);
        Assert.Null(item.IccProfile);
        Assert.Null(reader.AlphaItem);
        Assert.Single(reader.Items);
        Assert.Empty(item.Transforms);
        Assert.True(item.Associations.Single(association => association.Property.Type is "av1C").IsEssential);
        Assert.Equal(["ftyp", "meta", "mdat"], reader.TopLevelBoxes.Select(box => box.Type));

        // The item is the end of the file
        var (offset, length) = Assert.Single(item.Extents);
        Assert.Equal(data.Length, offset + length);

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(GetBytes(image), GetBytes(decoded));
        Assert.Equal(new CicpColorDescription(2, 2, 0, fullRange: true), decoded.Metadata.Cicp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlphaIsALosslessAuxiliaryImageThatKeepsHiddenColorsUnlessClearingIsRequested(bool clear)
    {
        using var image = CreateImage(19, 11, seed: 2, transparent: true);
        var source = GetBytes(image);
        var data = Encode(image, new AvifEncoder { ClearTransparentColors = clear });
        var reader = ReferenceHeif.Parse(data);
        var color = reader.PrimaryItem;
        var alpha = reader.AlphaItem;
        Assert.NotNull(color);
        Assert.NotNull(alpha);
        Assert.Equal("av01", alpha.Type);
        Assert.Equal(ReferenceHeif.AlphaAuxiliaryType, alpha.AuxiliaryType);
        Assert.Equal((19, 11), alpha.Size);
        Assert.Equal([8], alpha.PixelInformation);
        Assert.True(alpha.Configuration!.IsMonochrome);
        Assert.Equal(8, alpha.Configuration.BitDepth);
        Assert.HasCount(2, reader.Items);
        var reference = Assert.Single(reader.ItemReferences);
        Assert.Equal(("auxl", alpha.Id, color.Id), (reference.Type, reference.FromItemId, Assert.Single(reference.ToItemIds)));

        // The alpha plane precedes the color image, and both are in the media data
        Assert.True(Assert.Single(alpha.Extents).Offset < Assert.Single(color.Extents).Offset);

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
        var reader = ReferenceHeif.Parse(Encode(image, new AvifEncoder()));
        Assert.Null(reader.AlphaItem);
        Assert.Single(reader.Items);
    }

    [Fact]
    public void GrayPixelsAreAMonochromeImage()
    {
        var pixels = new Gray8[13 * 6];
        var random = new Random(4);
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Gray8((byte)random.Next(256));
        }

        using var image = Image.ImportPixelData<Gray8>(pixels, 13, 6);
        var data = Encode(image, new AvifEncoder());
        var item = ReferenceHeif.Parse(data).PrimaryItem;
        Assert.NotNull(item);
        Assert.Equal([8], item.PixelInformation);
        Assert.True(item.Configuration!.IsMonochrome);
        Assert.Equal(0, item.Configuration.SequenceProfile);
        Assert.Contains("MA1B", ReferenceHeif.Parse(data).CompatibleBrands);

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
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Encode(image, new AvifEncoder()));
        Assert.Equal((ImageFormat.Avif, "Bit depth reduction"), (exception.Format, exception.Feature));

        var data = Encode(image, new AvifEncoder { BitDepth = bitDepth });
        var reader = ReferenceHeif.Parse(data);
        Assert.Equal(bitDepth, reader.PrimaryItem!.Configuration!.BitDepth);
        Assert.Equal(bitDepth, reader.AlphaItem!.Configuration!.BitDepth);
        Assert.Equal(bitDepth is 12 ? 2 : 1, reader.PrimaryItem.Configuration.SequenceProfile);
        using var decoded = Image.Load<Rgba64>(data);
        var actual = new Rgba64[pixels.Length];
        decoded.Frames[0].CopyPixelDataTo(actual);
        Assert.Equal(pixels, actual);

        // Without a bit depth, the consent to reduce the samples selects 12 bits
        var reduced = ReferenceHeif.Parse(Encode(image, new AvifEncoder { AllowBitDepthReduction = true }));
        Assert.Equal(12, reduced.PrimaryItem!.Configuration!.BitDepth);

        ushort Scale(int value) => (ushort)(((value * 65535L) + (max / 2)) / max);
    }

    [Fact]
    public void EightBitPixelsCanBeWrittenWithMoreBitsWithoutLoss()
    {
        using var image = CreateImage(8, 5, seed: 6, transparent: true);
        var data = Encode(image, new AvifEncoder { BitDepth = 10 });
        Assert.Equal(10, ReferenceHeif.Parse(data).PrimaryItem!.Configuration!.BitDepth);
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(GetBytes(image), GetBytes(decoded));
    }

    [Fact]
    public void AStillImageIsWrittenToANonSeekableStreamWithTheSameBytes()
    {
        using var image = CreateImage(33, 17, seed: 7, transparent: true);
        var seekable = Encode(image, new AvifEncoder());
        using var stream = new TestOutputStream();
        image.Save(stream, new AvifEncoder());
        Assert.Equal(seekable, stream.ToArray());
    }

    [Fact]
    public void EncodingIsDeterministic()
    {
        using var image = CreateImage(40, 23, seed: 8, transparent: true);
        Assert.Equal(Encode(image, new AvifEncoder()), Encode(image, new AvifEncoder()));
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
        var data = Encode(image, new AvifEncoder());
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
        Assert.Equal(GetBytes(image), GetBytes(decoded));
    }

    [Theory]
    [InlineData(150, 100, 64, 64, 3, 2)] // the right column and the bottom row extend past the image
    [InlineData(128, 64, 64, 64, 2, 1)] // tiles that cover the image exactly
    [InlineData(65, 130, 64, 65, 2, 2)] // one column and one row of samples in the last tiles
    [InlineData(70, 200, 128, 64, 1, 4)] // a tile wider than the image: one column
    public void AnImageLargerThanATileIsAGridOfTiles(int width, int height, int tileWidth, int tileHeight, int columns, int rows)
    {
        using var image = CreateSmoothImage(width, height, transparent: true);
        var data = Encode(image, new AvifEncoder { GridTileSize = new Size(tileWidth, tileHeight), Effort = 0 });
        var reader = ReferenceHeif.Parse(data);

        // ISO/IEC 23008-12, section 6.6.2.3: the primary item is a grid, whose data are its descriptor, derived from its
        // tiles in row-major order; the tiles are hidden items of one size
        var grid = reader.PrimaryItem;
        Assert.NotNull(grid);
        Assert.Equal("grid", grid.Type);
        Assert.Equal((width, height), grid.Size);
        Assert.Equal((rows, columns, (long)width, (long)height), reader.ReadGridDescriptor(grid));
        Assert.Null(grid.Configuration);
        var tiles = reader.GetGridTiles(grid);
        Assert.HasCount(columns * rows, tiles);
        Assert.All(tiles, tile =>
        {
            Assert.Equal("av01", tile.Type);
            Assert.True(tile.IsHidden);
            Assert.Equal((tileWidth, tileHeight), tile.Size);
            Assert.NotNull(tile.Configuration);
        });
        Assert.Equal(Enumerable.Range(2, columns * rows).Select(id => (uint)id), tiles.Select(tile => tile.Id));
        Assert.False(grid.IsHidden);

        // The alpha plane is a grid of the same tiles
        var alpha = reader.AlphaItem;
        Assert.NotNull(alpha);
        Assert.Equal("grid", alpha.Type);
        Assert.Equal((rows, columns, (long)width, (long)height), reader.ReadGridDescriptor(alpha));
        var alphaTiles = reader.GetGridTiles(alpha);
        Assert.HasCount(columns * rows, alphaTiles);
        Assert.All(alphaTiles, tile => Assert.Equal((tileWidth, tileHeight), tile.Size));
        Assert.All(alphaTiles, tile => Assert.True(tile.IsHidden));

        // Lossless: every sample of the image is decoded back, and nothing of what the tiles hold past the image
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal((width, height), (decoded.Width, decoded.Height));
        Assert.Equal(GetBytes(image), GetBytes(decoded));
        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal((width, height), (info.Width, info.Height));
        Assert.True(info.MayHaveTransparency);
    }

    [Fact]
    public void AGridHasThePropertiesOfTheImageAndItsTilesTheOnesOfTheirData()
    {
        using var image = CreateSmoothImage(150, 100, transparent: true);
        var xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>"u8.ToArray();
        image.Metadata.Orientation = ExifOrientation.RightTop;
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(xmp));
        image.Metadata.Cicp = new CicpColorDescription(12, 13, 0, fullRange: true);
        image.Metadata.PixelAspectRatio = new PixelAspectRatio(4, 3);
        var aperture = new Rectangle(10, 20, 100, 70);
        var data = Encode(image, new AvifEncoder { GridTileSize = new Size(64, 64), CleanAperture = aperture, Effort = 0 });
        var reader = ReferenceHeif.Parse(data);
        var grid = reader.PrimaryItem!;
        var alpha = reader.AlphaItem!;

        // The grids describe the image: its size, its colors, its transformations, the shape of its pixels
        Assert.Equal(["ispe", "pixi", "colr", "pasp", "clap", "irot"], grid.Associations.Select(association => association.Property.Type));
        Assert.Equal(["ispe", "pixi", "auxC", "pasp", "clap", "irot"], alpha.Associations.Select(association => association.Property.Type));
        Assert.Equal(new ReferenceHeif.ColorDescription(12, 13, 0, FullRange: true), grid.Nclx);
        Assert.Equal((10, 20, 100, 70), grid.CleanAperture!.GetRectangle(150, 100));
        Assert.Equal((int)ExifOrientation.RightTop, grid.Orientation);

        // The tiles describe their coded data, which a reader must be able to decode
        foreach (var tile in reader.GetGridTiles(grid))
        {
            Assert.Equal(["ispe", "pixi", "av1C"], tile.Associations.Select(association => association.Property.Type));
            Assert.True(tile.Associations[2].IsEssential);
        }

        foreach (var tile in reader.GetGridTiles(alpha))
        {
            Assert.Equal(["ispe", "pixi", "av1C", "auxC"], tile.Associations.Select(association => association.Property.Type));
        }

        // The grids are derived from their tiles, the alpha grid belongs to the color grid, which the XMP item describes
        Assert.Equal(["dimg", "dimg", "auxl", "cdsc"], reader.ItemReferences.Select(reference => reference.Type));
        Assert.Equal((alpha.Id, grid.Id), (reader.ItemReferences[2].FromItemId, reader.ItemReferences[2].ToItemIds[0]));
        Assert.Equal(grid.Id, reader.ItemReferences[3].ToItemIds[0]);

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal((100, 70), (decoded.Width, decoded.Height));
        Assert.Equal(ExifOrientation.RightTop, decoded.Metadata.Orientation);
        Assert.Equal(new PixelAspectRatio(4, 3), decoded.Metadata.PixelAspectRatio);
        Assert.Equal(xmp, decoded.Metadata.XmpProfile!.Data.ToArray());
        using var expected = image.Clone();
        expected.Crop(aperture, TestContext.Current.CancellationToken);
        Assert.Equal(GetBytes(expected), GetBytes(decoded));
    }

    [Fact]
    public void AGridIsOnlyWrittenForAStillImageThatIsLargerThanATile()
    {
        // An image that one tile holds is one item, whatever the setting
        using var small = CreateSmoothImage(64, 48, transparent: false);
        Assert.Equal(Encode(small, new AvifEncoder { Effort = 0 }), Encode(small, new AvifEncoder { GridTileSize = new Size(64, 64), Effort = 0 }));

        // An opaque image of a pixel format with alpha has no alpha grid
        using var opaque = CreateSmoothImage(100, 70, transparent: false);
        var reader = ReferenceHeif.Parse(Encode(opaque, new AvifEncoder { GridTileSize = new Size(64, 64), Effort = 0 }));
        Assert.Equal("grid", reader.PrimaryItem!.Type);
        Assert.Null(reader.AlphaItem);
        Assert.Equal(["dimg"], reader.ItemReferences.Select(reference => reference.Type));

        // An image sequence is not a grid
        using var frame = CreateSmoothImage(100, 70, transparent: false, phase: 3);
        opaque.AppendFrame(frame.Frames[0]);
        opaque.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        opaque.Frames[1].Metadata.Duration = new FrameDuration(1, 10);
        var sequence = ReferenceHeif.Parse(Encode(opaque, new AvifEncoder { GridTileSize = new Size(64, 64), Effort = 0 }));
        Assert.True(sequence.IsSequence);
        Assert.Equal("av01", sequence.PrimaryItem!.Type);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(16)]
    [InlineData(64)]
    public void TheTilesOfAGridAreCodedByWorkersAsByOne(int workers)
    {
        // 20 tiles of 64x64, whose last column and last row extend past the image: batches of 3 tiles span two rows of 5
        // tiles, and 64 workers are more than the tiles. The file is the one of one worker, with its alpha grid
        using var transparent = CreateSmoothImage(300, 250, transparent: true);
        var lossless = new AvifEncoder { GridTileSize = new Size(64, 64), Effort = 0 };
        var expected = Encode(transparent, lossless);
        using var transparentWithWorkers = WithWorkers(transparent, workers);
        Assert.Equal(expected, Encode(transparentWithWorkers, lossless));

        // Lossy tiles of subsampled chroma, at an effort that searches more
        using var opaque = CreateSmoothImage(300, 250, transparent: false);
        var lossy = new AvifEncoder { Compression = AvifCompression.Lossy, GridTileSize = new Size(64, 64), Effort = 4 };
        using var opaqueWithWorkers = WithWorkers(opaque, workers);
        Assert.Equal(Encode(opaque, lossy), Encode(opaqueWithWorkers, lossy));

        // Every sample of the image is decoded back
        using var decoded = Image.Load<Rgba32>(expected);
        Assert.Equal(GetBytes(transparent), GetBytes(decoded));
    }

    [Fact]
    public void AGridOfSubsampledChromaNeedsEvenSizes()
    {
        static AvifEncoder Lossy(AvifChromaSubsampling subsampling, int tileWidth, int tileHeight) => new() { Compression = AvifCompression.Lossy, ChromaSubsampling = subsampling, GridTileSize = new Size(tileWidth, tileHeight), Effort = 0 };
        using var even = CreateSmoothImage(100, 70, transparent: false);
        using var oddWidth = CreateSmoothImage(101, 70, transparent: false);
        using var oddHeight = CreateSmoothImage(100, 71, transparent: false);

        // 4:2:0: the widths and the heights of the tiles and of the image are even
        Assert.Equal("grid", ReferenceHeif.Parse(Encode(even, Lossy(AvifChromaSubsampling.Ratio420, 64, 64))).PrimaryItem!.Type);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Encode(oddWidth, Lossy(AvifChromaSubsampling.Ratio420, 64, 64)));
        Assert.Equal(ImageFormat.Avif, exception.Format);
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(oddHeight, Lossy(AvifChromaSubsampling.Ratio420, 64, 64)));
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(even, Lossy(AvifChromaSubsampling.Ratio420, 65, 64)));
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(even, Lossy(AvifChromaSubsampling.Ratio420, 64, 65)));

        // 4:2:2: only the widths; 4:4:4, the lossless coding and gray pixels: any size
        Assert.Equal("grid", ReferenceHeif.Parse(Encode(oddHeight, Lossy(AvifChromaSubsampling.Ratio422, 64, 65))).PrimaryItem!.Type);
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(oddWidth, Lossy(AvifChromaSubsampling.Ratio422, 64, 64)));
        Assert.Equal("grid", ReferenceHeif.Parse(Encode(oddWidth, Lossy(AvifChromaSubsampling.Ratio444, 65, 65))).PrimaryItem!.Type);
        Assert.Equal("grid", ReferenceHeif.Parse(Encode(oddWidth, new AvifEncoder { GridTileSize = new Size(65, 65), Effort = 0 })).PrimaryItem!.Type);
        using var gray = oddWidth.CloneAs<Gray8>();
        Assert.Equal("grid", ReferenceHeif.Parse(Encode(gray, Lossy(AvifChromaSubsampling.Ratio420, 65, 65))).PrimaryItem!.Type);

        // At most 256 columns and 256 rows
        using var wide = new Image<Gray8>(64 * 257, 64);
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(wide, new AvifEncoder { GridTileSize = new Size(64, 64), Effort = 0 }));
    }

    [Theory]
    [InlineData(false, AvifCompression.Lossless)]
    [InlineData(true, AvifCompression.Lossless)]
    public void PremultipliedAlphaMultipliesTheColorsAndRefersToTheAlphaPlane(bool isSequence, AvifCompression compression)
    {
        // Alphas 0, 255, 128, 1 and 254 over random colors
        using var image = CreateImage(7, 5, seed: 33, transparent: true);
        if (isSequence)
        {
            using var second = CreateImage(7, 5, seed: 34, transparent: true);
            image.AppendFrame(second.Frames[0]);
            image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
            image.Frames[1].Metadata.Duration = new FrameDuration(1, 10);
        }

        var data = Encode(image, new AvifEncoder { Compression = compression, PremultiplyAlpha = true });
        var reader = ReferenceHeif.Parse(data);

        // ISO/IEC 23008-12, section 6.6.2.2: the color image refers to the alpha plane that its colors are multiplied by
        var item = reader.PrimaryItem;
        var alpha = reader.AlphaItem;
        Assert.NotNull(item);
        Assert.NotNull(alpha);
        var reference = Assert.Single(reader.ItemReferences, candidate => candidate.Type == "prem");
        Assert.Equal(item.Id, reference.FromItemId);
        Assert.Equal([alpha.Id], reference.ToItemIds);
        Assert.True(reader.IsPremultiplied);
        if (isSequence)
        {
            var premultiplied = Assert.Single(reader.ColorTrack!.References, candidate => candidate.Type == "prem");
            Assert.Equal([reader.AlphaTrack!.Id], premultiplied.TrackIds);
            Assert.DoesNotContain(reader.AlphaTrack.References, candidate => candidate.Type == "prem");
        }

        // The color is multiplied by the alpha and rounded once, coded exactly, then divided by the alpha and rounded:
        // an opaque pixel is unchanged, nothing is left of the color of a transparent pixel, and alpha is exact
        using var decoded = Image.Load<Rgba32>(data);
        for (var frame = 0; frame < image.Frames.Count; frame++)
        {
            var source = GetBytes(image, frame);
            var actual = GetBytes(decoded, frame);
            for (var i = 0; i < source.Length; i += 4)
            {
                var a = source[i + 3];
                Assert.Equal(a, actual[i + 3]);
                for (var channel = 0; channel < 3; channel++)
                {
                    var stored = ((source[i + channel] * a) + 127) / 255;
                    var expected = a is 0 ? 0 : Math.Min(255, ((stored * 255) + (a / 2)) / a);
                    Assert.Equal(expected, actual[i + channel]);
                    if (a is 255)
                        Assert.Equal(source[i + channel], actual[i + channel]);
                }
            }
        }
    }

    [Fact]
    public void PremultipliedAlphaIsNotDeclaredWithoutAnAlphaPlane()
    {
        // Colors and alpha are independent by default
        using var transparent = CreateImage(7, 5, seed: 33, transparent: true);
        var straight = ReferenceHeif.Parse(Encode(transparent, new AvifEncoder()));
        Assert.DoesNotContain(straight.ItemReferences, reference => reference.Type == "prem");
        Assert.False(straight.IsPremultiplied);

        // An opaque still image has no alpha plane, and its colors are not changed
        using var opaque = CreateImage(7, 5, seed: 35, transparent: false);
        var data = Encode(opaque, new AvifEncoder { PremultiplyAlpha = true });
        var reader = ReferenceHeif.Parse(data);
        Assert.Null(reader.AlphaItem);
        Assert.DoesNotContain(reader.ItemReferences, reference => reference.Type == "prem");
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(GetBytes(opaque), GetBytes(decoded));

        // A gray pixel format has no alpha
        using var gray = opaque.CloneAs<Gray8>();
        Assert.DoesNotContain(ReferenceHeif.Parse(Encode(gray, new AvifEncoder { PremultiplyAlpha = true })).ItemReferences, reference => reference.Type == "prem");

        // Clearing the transparent colors changes nothing: they are already black
        Assert.Equal(Encode(transparent, new AvifEncoder { PremultiplyAlpha = true }), Encode(transparent, new AvifEncoder { PremultiplyAlpha = true, ClearTransparentColors = true }));
    }

    [Theory]
    // ISO/IEC 14496-12, section 12.1.4: the size of the aperture, then the offset of its center from the center of the
    // image, as fractions. For a 9x6 image: (1, 2, 5, 3) is centered at 3 + 1/2 - 4 = -1/2 and 3 + 1 - 5/2 = 1/2... written
    // as the numerators and denominators below, computed by hand from left + (width - 9) / 2 and top + (height - 6) / 2
    [InlineData(1, 2, 5, 3, -1, 1, 1, 2)] // 1 + (5 - 9) / 2 = -1; 2 + (3 - 6) / 2 = 1/2
    [InlineData(0, 0, 4, 6, -5, 2, 0, 1)] // 0 + (4 - 9) / 2 = -5/2; 0
    [InlineData(4, 1, 5, 5, 2, 1, 1, 2)] // 4 + (5 - 9) / 2 = 2; 1 + (5 - 6) / 2 = 1/2
    [InlineData(8, 5, 1, 1, 4, 1, 5, 2)] // the last sample: 8 + (1 - 9) / 2 = 4; 5 + (1 - 6) / 2 = 5/2
    public void TheCleanApertureIsAPropertyThatHidesTheRestOfTheImage(int x, int y, int width, int height, int horizontalOffsetNumerator, uint horizontalOffsetDenominator, int verticalOffsetNumerator, uint verticalOffsetDenominator)
    {
        using var image = CreateImage(9, 6, seed: 21, transparent: true);
        image.Metadata.Orientation = ExifOrientation.RightTop;
        var aperture = new Rectangle(x, y, width, height);
        var data = Encode(image, new AvifEncoder { CleanAperture = aperture });
        var reader = ReferenceHeif.Parse(data);
        var item = reader.PrimaryItem;
        Assert.NotNull(item);

        // The whole image is coded; the aperture is a property a reader must apply, before the rotation
        Assert.Equal((9, 6), item.Size);
        Assert.Equal(new ReferenceHeif.CleanAperture((uint)width, 1, (uint)height, 1, horizontalOffsetNumerator, horizontalOffsetDenominator, verticalOffsetNumerator, verticalOffsetDenominator), item.CleanAperture);
        Assert.Equal((x, y, width, height), item.CleanAperture!.GetRectangle(9, 6));
        Assert.Equal(["clap", "irot"], item.Transforms.Select(property => property.Type));
        Assert.True(item.HasMiafTransformOrder);
        Assert.All(item.Associations.Where(association => association.Property.Type is "clap"), association => Assert.True(association.IsEssential));

        // The alpha plane has the same aperture
        var alpha = reader.AlphaItem;
        Assert.NotNull(alpha);
        Assert.Equal(["clap", "irot"], alpha.Transforms.Select(property => property.Type));
        Assert.Equal(item.Find("clap")!.Index, alpha.Find("clap")!.Index);

        // Loading the file gives the aperture, with the orientation of the image
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal((width, height), (decoded.Width, decoded.Height));
        Assert.Equal(ExifOrientation.RightTop, decoded.Metadata.Orientation);
        using var expected = image.Clone();
        expected.Crop(aperture, TestContext.Current.CancellationToken);
        Assert.Equal(GetBytes(expected), GetBytes(decoded));
    }

    [Fact]
    public void TheCleanApertureMustSelectWholeSamplesOfEveryPlaneOfAStillImage()
    {
        using var image = CreateImage(8, 6, seed: 22, transparent: false);

        // The whole image is no aperture
        var whole = ReferenceHeif.Parse(Encode(image, new AvifEncoder { CleanAperture = new Rectangle(0, 0, 8, 6) })).PrimaryItem;
        Assert.NotNull(whole);
        Assert.Null(whole.CleanAperture);

        // Outside the image
        Assert.Throws<ArgumentException>(() => Encode(image, new AvifEncoder { CleanAperture = new Rectangle(4, 0, 5, 6) }));
        Assert.Throws<ArgumentException>(() => Encode(image, new AvifEncoder { CleanAperture = new Rectangle(0, 3, 8, 4) }));

        // With subsampled chroma, the aperture starts on a chroma sample: an even column, and an even row for 4:2:0
        static AvifEncoder Lossy(Rectangle aperture, AvifChromaSubsampling subsampling = AvifChromaSubsampling.Auto) => new() { Compression = AvifCompression.Lossy, ChromaSubsampling = subsampling, CleanAperture = aperture };
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Encode(image, Lossy(new Rectangle(1, 0, 4, 4))));
        Assert.Equal(ImageFormat.Avif, exception.Format);
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(image, Lossy(new Rectangle(0, 1, 4, 4))));
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(image, Lossy(new Rectangle(1, 0, 4, 4), AvifChromaSubsampling.Ratio422)));
        Assert.Equal((0, 1, 4, 4), ReferenceHeif.Parse(Encode(image, Lossy(new Rectangle(0, 1, 4, 4), AvifChromaSubsampling.Ratio422))).PrimaryItem!.CleanAperture!.GetRectangle(8, 6));
        Assert.Equal((1, 1, 4, 4), ReferenceHeif.Parse(Encode(image, Lossy(new Rectangle(1, 1, 4, 4), AvifChromaSubsampling.Ratio444))).PrimaryItem!.CleanAperture!.GetRectangle(8, 6));
        Assert.Equal((2, 2, 3, 3), ReferenceHeif.Parse(Encode(image, Lossy(new Rectangle(2, 2, 3, 3)))).PrimaryItem!.CleanAperture!.GetRectangle(8, 6));

        // The lossless coding and the gray pixel formats have no subsampled plane
        Assert.Equal((1, 1, 4, 4), ReferenceHeif.Parse(Encode(image, new AvifEncoder { CleanAperture = new Rectangle(1, 1, 4, 4) })).PrimaryItem!.CleanAperture!.GetRectangle(8, 6));
        using var gray = image.CloneAs<Gray8>();
        Assert.Equal((1, 1, 4, 4), ReferenceHeif.Parse(Encode(gray, Lossy(new Rectangle(1, 1, 4, 4)))).PrimaryItem!.CleanAperture!.GetRectangle(8, 6));

    }

    [Theory]
    // ISO/IEC 14496-12, section 8.3.2: the matrix { a, b, u, c, d, v, x, y, w } maps the point (p, q) of an image to
    // (a p + c q + x, b p + d q + y). For images of 9x6, derived by hand: the translation brings the turned or mirrored
    // image back to positive coordinates (9 where a column is mirrored along the width, 6 where a row is)
    [InlineData(ExifOrientation.TopLeft, 1, 0, 0, 1, 0, 0)]
    [InlineData(ExifOrientation.TopRight, -1, 0, 0, 1, 9, 0)]
    [InlineData(ExifOrientation.BottomRight, -1, 0, 0, -1, 9, 6)]
    [InlineData(ExifOrientation.BottomLeft, 1, 0, 0, -1, 0, 6)]
    [InlineData(ExifOrientation.LeftTop, 0, 1, 1, 0, 0, 0)]
    [InlineData(ExifOrientation.RightTop, 0, 1, -1, 0, 6, 0)] // a quarter turn clockwise: p' = 6 - q, q' = p
    [InlineData(ExifOrientation.RightBottom, 0, -1, -1, 0, 6, 9)]
    [InlineData(ExifOrientation.LeftBottom, 0, -1, 1, 0, 0, 9)] // a quarter turn anti-clockwise: p' = q, q' = 9 - p
    public void TheOrientationOfASequenceIsTheMatrixOfItsTrackHeaders(ExifOrientation orientation, int a, int b, int c, int d, int x, int y)
    {
        const int One = 0x00010000;
        using var image = CreateImage(9, 6, seed: 61, transparent: true);
        using var second = CreateImage(9, 6, seed: 62, transparent: true);
        image.AppendFrame(second.Frames[0]);
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 10);
        image.Metadata.Orientation = orientation;
        var data = Encode(image, new AvifEncoder());
        var reader = ReferenceHeif.Parse(data);

        // Both tracks have the matrix: the orientation of the color images is the one of their alpha planes
        int[] expected = [a * One, b * One, 0, c * One, d * One, 0, x * One, y * One, 0x40000000];
        var color = reader.ColorTrack;
        var alpha = reader.AlphaTrack;
        Assert.NotNull(color);
        Assert.NotNull(alpha);
        Assert.Equal(expected, color.Matrix);
        Assert.Equal(expected, alpha.Matrix);
        Assert.Equal((int)orientation, color.Orientation);
        Assert.Equal((9u * One, 6u * One), (color.HeaderWidth, color.HeaderHeight));

        // The first image, which is the primary item of the file, has the orientation as its properties
        Assert.Equal((int)orientation, reader.PrimaryItem!.Orientation);

        // The pixels are stored as they are, and the orientation is read back
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(2, decoded.Frames.Count);
        Assert.Equal((9, 6), (decoded.Width, decoded.Height));
        Assert.Equal(orientation, decoded.Metadata.Orientation);
        Assert.Equal(GetBytes(image, 1), GetBytes(decoded, 1));
        Assert.Equal(orientation, Image.Identify(data).Metadata.Orientation);
    }

    [Fact]
    public void TheCleanApertureOfASequenceIsABoxOfItsSampleEntries()
    {
        using var image = CreateImage(9, 6, seed: 63, transparent: true);
        using var second = CreateImage(9, 6, seed: 64, transparent: true);
        image.AppendFrame(second.Frames[0]);
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 10);
        image.Metadata.Orientation = ExifOrientation.BottomRight;
        var aperture = new Rectangle(1, 2, 5, 3);
        var data = Encode(image, new AvifEncoder { CleanAperture = aperture });
        var reader = ReferenceHeif.Parse(data);

        // The images are coded whole, and the tracks show the aperture. The track headers keep the size of the coded
        // images, which libavif and FFmpeg take for the size the aperture applies to, and the half turn moves that size
        // back to positive coordinates
        const int One = 0x00010000;
        foreach (var track in new[] { reader.ColorTrack!, reader.AlphaTrack! })
        {
            Assert.Equal((9, 6), (track.Width, track.Height));
            Assert.Equal((1, 2, 5, 3), track.CleanAperture!.GetRectangle(9, 6));
            Assert.Equal((9u * One, 6u * One), (track.HeaderWidth, track.HeaderHeight));
            Assert.Equal([-One, 0, 0, 0, -One, 0, 9 * One, 6 * One, 0x40000000], track.Matrix);
        }

        // The first image has the aperture as a property
        Assert.Equal((1, 2, 5, 3), reader.PrimaryItem!.CleanAperture!.GetRectangle(9, 6));

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal((5, 3), (decoded.Width, decoded.Height));
        Assert.Equal(ExifOrientation.BottomRight, decoded.Metadata.Orientation);
        using var expected = image.Clone();
        expected.Crop(aperture, TestContext.Current.CancellationToken);
        for (var frame = 0; frame < 2; frame++)
        {
            Assert.Equal(GetBytes(expected, frame), GetBytes(decoded, frame));
        }
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
        var data = Encode(image, new AvifEncoder());

        // ISO/IEC 14496-12, sections 12.1.6, 12.1.7 and 12.1.4: big-endian fields, written by hand. The primaries of the
        // display are stored in the order green, blue, red; the luminances in units of 0.0001 cd/m2
        byte[] expectedLightLevel = [0x03, 0xE8, 0x01, 0x90];
        byte[] expectedDisplay = [0x21, 0x34, 0x9B, 0xAA, 0x19, 0x96, 0x08, 0xFC, 0x8A, 0x48, 0x39, 0x08, 0x3D, 0x13, 0x40, 0x42, 0x00, 0x98, 0x96, 0x80, 0x00, 0x00, 0x00, 0x32];
        byte[] expectedShape = [0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x03];

        // The primary item, which is the first image of a sequence, has the three properties; none is needed to decode it
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

            var alphaTrack = reader.AlphaTrack;
            Assert.NotNull(alphaTrack);
            Assert.Equal(expectedShape, alphaTrack.FindSampleEntryBox("pasp")!.Payload.ToArray());
            Assert.Null(alphaTrack.FindSampleEntryBox("clli"));
            Assert.Null(alphaTrack.FindSampleEntryBox("mdcv"));
        }

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(isSequence ? 2 : 1, decoded.Frames.Count);
        Assert.Equal(lightLevel, decoded.Metadata.ContentLightLevel);
        Assert.Equal(display, decoded.Metadata.MasteringDisplay);
        Assert.Equal(shape, decoded.Metadata.PixelAspectRatio);
        Assert.Equal(GetBytes(image), GetBytes(decoded));

        // An identification reads them too
        var info = Image.Identify(data);
        Assert.Equal(lightLevel, info.Metadata.ContentLightLevel);
        Assert.Equal(display, info.Metadata.MasteringDisplay);
        Assert.Equal(shape, info.Metadata.PixelAspectRatio);
    }

    [Fact]
    public void AnImageWithoutLightLevelsMasteringDisplayOrPixelShapeHasNoBoxForThem()
    {
        using var image = CreateImage(6, 4, seed: 12, transparent: false);
        var item = ReferenceHeif.Parse(Encode(image, new AvifEncoder())).PrimaryItem;
        Assert.NotNull(item);
        Assert.Null(item.Find("clli"));
        Assert.Null(item.Find("mdcv"));
        Assert.Null(item.Find("pasp"));

        // Stripping the metadata removes them
        image.Metadata.ContentLightLevel = new ContentLightLevel(1000, 400);
        image.Metadata.PixelAspectRatio = new PixelAspectRatio(2, 1);
        var data = Encode(image, new AvifEncoder { MetadataHandling = MetadataHandling.Strip });
        item = ReferenceHeif.Parse(data).PrimaryItem;
        Assert.NotNull(item);
        Assert.Null(item.Find("clli"));
        Assert.Null(item.Find("pasp"));
        using var decoded = Image.Load(data);
        Assert.Null(decoded.Metadata.ContentLightLevel);
        Assert.Null(decoded.Metadata.PixelAspectRatio);
    }

    [Theory]
    [InlineData(AvifChromaSubsampling.Auto, 1, 1, 0)]
    [InlineData(AvifChromaSubsampling.Ratio420, 1, 1, 0)]
    [InlineData(AvifChromaSubsampling.Ratio422, 1, 0, 2)]
    [InlineData(AvifChromaSubsampling.Ratio444, 0, 0, 1)]
    public void LossyOutputIsYCbCrWithTheRequestedSubsamplingAndExactAlpha(AvifChromaSubsampling subsampling, int subsamplingX, int subsamplingY, int profile)
    {
        using var image = CreateSmoothImage(75, 41, transparent: true);
        var source = GetBytes(image);
        var data = Encode(image, new AvifEncoder { Compression = AvifCompression.Lossy, Quality = 90, ChromaSubsampling = subsampling });
        var reader = ReferenceHeif.Parse(data);
        var color = reader.PrimaryItem;
        Assert.NotNull(color);
        Assert.Equal(new ReferenceHeif.Av1Configuration(profile, Av1Levels.GetLevelIndex(75, 41), BitDepth: 8, IsMonochrome: false, subsamplingX, subsamplingY, ChromaSamplePosition: 0), color.Configuration);

        // BT.601 in full range, with the primaries and the transfer function left unspecified
        Assert.Equal(new ReferenceHeif.ColorDescription(2, 2, 6, FullRange: true), color.Nclx);
        Assert.NotNull(reader.AlphaItem);

        using var decoded = Image.Load<Rgba32>(data);
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
        Assert.True(psnr >= 34, string.Create(CultureInfo.InvariantCulture, $"PSNR {psnr:F2} dB"));
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
            var data = Encode(image, new AvifEncoder { Compression = AvifCompression.Lossy, Quality = quality });
            using var decoded = Image.Load<Rgba32>(data);
            var actual = GetBytes(decoded);
            double error = 0;
            for (var i = 0; i < source.Length; i++)
            {
                error += Math.Abs(source[i] - actual[i]);
            }

            Assert.HasCountLessThan(previousSize, data);
            Assert.True(error > previousError, string.Create(CultureInfo.InvariantCulture, $"quality {quality}: error {error}, the previous quality {previousError}"));
            previousSize = data.Length;
            previousError = error;
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
        var data = Encode(image, new AvifEncoder { Compression = AvifCompression.Lossy, Quality = 95 });
        var item = ReferenceHeif.Parse(data).PrimaryItem;
        Assert.True(item!.Configuration!.IsMonochrome);
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
        var data = Encode(image, new AvifEncoder { Compression = AvifCompression.Lossy, Quality = 90, BitDepth = 10 });
        Assert.Equal(new ReferenceHeif.ColorDescription(9, 16, 9, FullRange: false), ReferenceHeif.Parse(data).PrimaryItem!.Nclx);
        using var decoded = Image.Load(data);
        Assert.Equal(new CicpColorDescription(9, 16, 9, fullRange: false), decoded.Metadata.Cicp);

        // The lossless coding has no matrix: the description keeps the primaries and the transfer function
        var lossless = Encode(image, new AvifEncoder());
        Assert.Equal(new ReferenceHeif.ColorDescription(9, 16, 0, FullRange: true), ReferenceHeif.Parse(lossless).PrimaryItem!.Nclx);
    }

    [Theory]
    [InlineData(AvifCompression.Lossless, false, null)]
    [InlineData(AvifCompression.Lossless, true, 3)]
    [InlineData(AvifCompression.Lossy, true, 1)]
    public void AnAnimationIsAnImageSequenceWithExactDurationsAndPlayCount(AvifCompression compression, bool transparent, int? totalPlays)
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
        var data = Encode(image, new AvifEncoder { Compression = compression, Quality = 95 });
        var reader = ReferenceHeif.Parse(data);
        Assert.Equal("avis", reader.MajorBrand);
        Assert.True(reader.IsSequence);
        Assert.Contains("avif", reader.CompatibleBrands);
        Assert.Contains("msf1", reader.CompatibleBrands);
        Assert.False(reader.IsFragmented);

        // The boxes of the first image, the moov box in the room reserved for it, then the media data
        Assert.Equal(["ftyp", "meta", "moov", "free", "mdat"], reader.TopLevelBoxes.Select(box => box.Type));
        Assert.Equal(data.Length, reader.TopLevelBoxes[^1].Offset + reader.TopLevelBoxes[^1].Size);
        Assert.True(reader.TopLevelBoxes[^1].HasLargeSize);

        // The time scale is the smallest one that holds 1/30, 1/25, 0 and 7/3 exactly: 150
        var track = reader.ColorTrack;
        Assert.NotNull(track);
        Assert.Equal(150u, track.Timescale);
        Assert.Equal([5u, 6u, 0u, 350u, 6u], track.Samples.Select(sample => sample.Duration));
        Assert.Equal("av01", track.SampleEntryType);
        Assert.Equal((40, 26), (track.Width, track.Height));
        Assert.Equal(totalPlays, track.TotalPlays);
        Assert.Equal("stco", track.ChunkOffsetBox);

        // The first image is the only key frame of both tracks: the others are predicted from the image before them
        Assert.Equal([1u], track.SyncSamples);
        var alpha = reader.AlphaTrack;
        Assert.Equal([1u], alpha?.SyncSamples);
        if (transparent)
        {
            Assert.NotNull(alpha);
            Assert.Equal(ReferenceHeif.AlphaAuxiliaryType, alpha.AuxiliaryType);
            Assert.HasCount(durations.Length, alpha.Samples);
            Assert.Equal(("auxl", 1u), (Assert.Single(alpha.References).Type, Assert.Single(Assert.Single(alpha.References).TrackIds)));
        }
        else
        {
            // An opaque image of a pixel format with alpha still has an alpha track in a sequence
            Assert.NotNull(alpha);
        }

        // The samples are in the media data, one after the other, and the first image is also the primary item
        long position = track.Samples[0].Offset - (alpha?.Samples[0].Size ?? 0);
        for (var i = 0; i < durations.Length; i++)
        {
            if (alpha is not null)
            {
                Assert.Equal(position, alpha.Samples[i].Offset);
                position += alpha.Samples[i].Size;
            }

            Assert.Equal(position, track.Samples[i].Offset);
            position += track.Samples[i].Size;
        }

        Assert.Equal(data.Length, position);
        Assert.Equal((track.Samples[0].Offset, (long)track.Samples[0].Size), Assert.Single(reader.PrimaryItem!.Extents));

        using var decoded = Image.Load<Rgba32>(data);
        Assert.HasCount(durations.Length, decoded.Frames);
        Assert.Equal(totalPlays, decoded.Animation?.TotalPlays);
        for (var i = 0; i < durations.Length; i++)
        {
            Assert.Equal(durations[i], decoded.Frames[i].Metadata.Duration);
            var expected = GetBytes(image, i);
            var actual = GetBytes(decoded, i);
            if (compression is AvifCompression.Lossless)
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
    }

    [Theory]
    [InlineData(AvifCompression.Lossless)]
    [InlineData(AvifCompression.Lossy)]
    public void TheKeyFrameIntervalSelectsTheSyncSamples(AvifCompression compression)
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
            var data = Encode(image, new AvifEncoder { Compression = compression, Quality = 90, KeyFrameInterval = interval });
            sizes[interval] = data.Length;
            var reader = ReferenceHeif.Parse(data);
            Assert.True(reader.IsSequence);

            // A track without sync sample box has only sync samples
            Assert.Equal(expected, reader.ColorTrack!.SyncSamples);
            Assert.Equal(expected, reader.AlphaTrack!.SyncSamples);

            using var decoded = Image.Load<Rgba32>(data);
            Assert.HasCount(FrameCount, decoded.Frames);
            for (var i = 0; i < FrameCount; i++)
            {
                var expectedBytes = GetBytes(image, i);
                var actual = GetBytes(decoded, i);
                if (compression is AvifCompression.Lossless)
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
            var data = Encode(image, new AvifEncoder());
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
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(image, new AvifEncoder()));
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
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new AvifEncoder { Effort = 0 }));
        Assert.Equal((ImageFormat.Avif, "AVIF play count"), (exception.Format, exception.Feature));

        // The largest play count of durations that fit is stored
        foreach (var frame in image.Frames)
        {
            frame.Metadata.Duration = new FrameDuration(1, 1);
        }

        var data = Encode(image, new AvifEncoder { Effort = 0 });
        Assert.Equal(3ul * int.MaxValue, ReferenceHeif.Parse(data).ColorTrack!.Duration);
        using var decoded = Image.Load(data);
        Assert.Equal(int.MaxValue, decoded.Animation?.TotalPlays);
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
        Assert.Equal("stream", Assert.Throws<ArgumentException>(() => image.Save(stream, new AvifEncoder())).ParamName);
        Assert.Equal(0, stream.BytesWritten);

        using var seekable = new MemoryStream();
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(seekable, new ImageWriterOptions(8, 8) { Encoder = new AvifEncoder() }));
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
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new AvifEncoder()));
        Assert.Equal("AVIF frame duration precision", exception.Feature);
        Assert.Equal(0, stream.Length);

        // Rounded to microseconds: 0 and 250,000 (0.25 s)
        var data = Encode(image, new AvifEncoder { DurationRounding = FrameDurationRounding.RoundToNearest });
        var track = ReferenceHeif.Parse(data).ColorTrack;
        Assert.Equal(1_000_000u, track!.Timescale);
        Assert.Equal([0u, 250_000u], track.Samples.Select(sample => sample.Duration));
    }

    // A copy of an image whose operations use up to that many workers
    private static Image<Rgba32> WithWorkers(Image<Rgba32> image, int workers)
    {
        var pixels = new Rgba32[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        return Image.ImportPixelData<Rgba32>(pixels, image.Width, image.Height, configuration: new ImageConfiguration { MaxDegreeOfParallelism = workers });
    }

    private static byte[] Encode(Image image, AvifEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);

        return stream.ToArray();
    }
}
