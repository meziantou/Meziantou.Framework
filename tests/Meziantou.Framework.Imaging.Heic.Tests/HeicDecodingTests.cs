using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Decoding of HEIC files without any tool: the lossless files of <see cref="HeicTestFiles"/>, whose pixels are known, as
/// they are and laid out differently by <see cref="HeifFileBuilder"/>. The files of the corpus and the reference decoders
/// cover the coding tools and the color conversion; these tests cover what the container asks of the decoder.
/// </summary>
public sealed class HeicDecodingTests
{
    private static void AssertBlocks(RawPixelBuffer pixels, int frame = 0, string? message = null)
    {
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                var expected = HeicTestFiles.GetBlockSample(x, y, frame);
                var actual = (pixels.GetSample(x, y, 0), pixels.GetSample(x, y, 1), pixels.GetSample(x, y, 2));
                Assert.True(expected == actual, $"{message}: pixel ({x}, {y}) is {actual}, {expected} expected.");
            }
        }
    }

    private static void AssertSource(RawPixelBuffer pixels, bool hasAlpha = false, string? message = null)
    {
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                var (r, g, b, a) = HeicTestFiles.GetSourceSample(x, y);
                var actual = (pixels.GetSample(x, y, 0), pixels.GetSample(x, y, 1), pixels.GetSample(x, y, 2), hasAlpha ? pixels.GetSample(x, y, 3) : -1);
                Assert.True((r, g, b, hasAlpha ? a : -1) == actual, $"{message}: pixel ({x}, {y}) is {actual}, ({r}, {g}, {b}, {(hasAlpha ? a : -1)}) expected.");
            }
        }
    }

    [Fact]
    public void ALosslessImageDecodesToItsSource()
    {
        var info = Image.Identify(HeicTestFiles.Rgb, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal((ImageFormat.Heic, 8, 6, PixelFormat.Rgb24, ImageColorModel.Rgb, 8), (info.Format, info.Width, info.Height, info.PixelFormat, info.ColorModel, info.BitsPerComponent));
        Assert.Equal(new CicpColorDescription(1, 13, 0, fullRange: true), info.Metadata.Cicp);

        using var image = Image.Load(HeicTestFiles.Rgb);
        Assert.Equal(ImageFormat.Heic, image.Metadata.SourceFormat);
        Assert.Equal((8, 6, PixelFormat.Rgb24), (image.Width, image.Height, image.PixelFormat));
        Assert.Single(image.Frames);
        AssertSource(ImageSnapshots.CaptureFrame(image.Frames[0]));
    }

    [Fact]
    public void TheAlphaPlaneIsAnAuxiliaryImage()
    {
        var info = Image.Identify(HeicTestFiles.Rgba);
        Assert.Equal((PixelFormat.Rgba32, ImageColorModel.Rgba, true), (info.PixelFormat, info.ColorModel, info.MayHaveTransparency));

        using var image = Image.Load(HeicTestFiles.Rgba);
        Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
        AssertSource(ImageSnapshots.CaptureFrame(image.Frames[0]), hasAlpha: true);

        // The alpha type of MIAF files instead of the one of HEVC
        var file = ReferenceHeif.Parse(HeicTestFiles.Rgba);
        Assert.Equal(ReferenceHeif.HevcAlphaAuxiliaryType, file.AlphaItem!.AuxiliaryType);
        var builder = HeifFileBuilder.From(HeicTestFiles.Rgba);
        var auxiliaryType = file.AlphaItem.Find("auxC")!.Index;
        builder.Properties[auxiliaryType - 1] = HeifFileBuilder.AuxiliaryType(ReferenceHeif.AlphaAuxiliaryType);
        using var generic = Image.Load(builder.Build());
        AssertSource(ImageSnapshots.CaptureFrame(generic.Frames[0]), hasAlpha: true);

        // An auxiliary image of another type is not an alpha plane
        builder.Properties[auxiliaryType - 1] = HeifFileBuilder.AuxiliaryType("urn:mpeg:hevc:2015:auxid:2");
        using var opaque = Image.Load(builder.Build());
        Assert.Equal(PixelFormat.Rgb24, opaque.PixelFormat);
        AssertSource(ImageSnapshots.CaptureFrame(opaque.Frames[0]));
    }

    [Fact]
    public void AGrayImageDecodesToItsSource()
    {
        using var image = Image.Load(HeicTestFiles.Gray);
        Assert.Equal((8, 6), (image.Width, image.Height));
        var pixels = ImageSnapshots.CaptureFrame(image.Frames[0]);
        for (var y = 0; y < 6; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var (r, g, _, _) = HeicTestFiles.GetSourceSample(x, y);
                Assert.Equal((r + g) & 255, pixels.GetSample(x, y, 0));
            }
        }
    }

    [Fact]
    public void TenBitSamplesAreScaledToSixteenBits()
    {
        var info = Image.Identify(HeicTestFiles.Deep);
        Assert.Equal((PixelFormat.Rgb48, 10), (info.PixelFormat, info.BitsPerComponent));

        using var image = Image.Load(HeicTestFiles.Deep);
        var pixels = ImageSnapshots.CaptureFrame(image.Frames[0]);
        for (var y = 0; y < 6; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var (r, g, b, _) = HeicTestFiles.GetSourceSample(x, y);
                var expected = (Scale(HeicTestFiles.ToTenBits(r, x)), Scale(HeicTestFiles.ToTenBits(g, x)), Scale(HeicTestFiles.ToTenBits(b, x)), 65535);
                Assert.Equal(expected, (pixels.GetSample(x, y, 0), pixels.GetSample(x, y, 1), pixels.GetSample(x, y, 2), pixels.GetSample(x, y, 3)));
            }
        }

        static int Scale(int sample) => ((sample * 65535) + 511) / 1023;
    }

    [Fact]
    public void TheTilesOfAGridAreAssembled()
    {
        var file = ReferenceHeif.Parse(HeicTestFiles.Grid);
        Assert.Equal("grid", file.PrimaryItem!.Type);
        Assert.Equal((2, 3, 130L, 70L), file.ReadGridDescriptor(file.PrimaryItem));

        using var image = Image.Load(HeicTestFiles.Grid);
        Assert.Equal((130, 70), (image.Width, image.Height));
        AssertBlocks(ImageSnapshots.CaptureFrame(image.Frames[0]));
    }

    [Fact]
    public void AGridFailsAtATileOfItsLastRow()
    {
        // The rows of a grid are converted as soon as their row of tiles is decoded: a tile of the last row that is truncated
        // still fails the load, with the error of a grid whose rows are converted at the end, and the image is released
        var file = ReferenceHeif.Parse(HeicTestFiles.Grid);
        var lastTile = file.GetGridTiles(file.PrimaryItem!)[^1];
        var builder = HeifFileBuilder.From(HeicTestFiles.Grid);
        var tile = builder.GetItem(lastTile.Id);
        tile.Data = tile.Data[..(tile.Data.Length / 2)];
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(builder.Build()));
        Assert.Contains("truncated", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLayoutOfTheFileDoesNotMatter(bool itemsInItemData)
    {
        foreach (var source in new[] { HeicTestFiles.Rgba, HeicTestFiles.Grid })
        {
            // The media data before the 'meta' box, or the data of the items in the 'idat' box
            var builder = HeifFileBuilder.From(source);
            if (itemsInItemData)
            {
                foreach (var item in builder.Items)
                {
                    if (item.Storage is HeifFileBuilder.ItemStorage.MediaData)
                        item.Storage = HeifFileBuilder.ItemStorage.ItemData;
                }
            }
            else
            {
                builder.Layout.Clear();
                builder.Layout.Add(HeifFileBuilder.FileTypeBox);
                builder.Layout.Add(HeifFileBuilder.MediaDataBox);
                builder.Layout.Add(HeifFileBuilder.MetaBox);
            }

            using var expected = Image.Load(source);
            using var image = Image.Load(builder.Build());
            Assert.Equal(ImageSnapshots.CaptureFrame(expected.Frames[0]).ToArray(), ImageSnapshots.CaptureFrame(image.Frames[0]).ToArray());

            // A stream that is read forward only gives the same image
            using var stream = new MemoryStream(builder.Build());
            using var streamed = Image.Load(stream);
            Assert.Equal(ImageSnapshots.CaptureFrame(expected.Frames[0]).ToArray(), ImageSnapshots.CaptureFrame(streamed.Frames[0]).ToArray());
        }
    }

    [Fact]
    public void TheTransformationsOfTheImageAreReportedOrApplied()
    {
        // A rotation is the orientation of the image, which is not applied
        var rotated = HeifFileBuilder.From(HeicTestFiles.Rgb);
        rotated.GetItem(1).Associations.Add((rotated.AddProperty(HeifFileBuilder.Rotation(1)), true));
        using (var image = Image.Load(rotated.Build()))
        {
            Assert.Equal((8, 6), (image.Width, image.Height));
            Assert.Equal(ExifOrientation.LeftBottom, image.Metadata.Orientation);
            AssertSource(ImageSnapshots.CaptureFrame(image.Frames[0]));
        }

        // A second clean aperture selects a part of the image: 4x2 samples
        var cropped = HeifFileBuilder.From(HeicTestFiles.Rgb);
        cropped.GetItem(1).Associations.Add((cropped.AddProperty(HeifFileBuilder.CleanAperture(4, 1, 2, 1, 0, 1, 2, 1)), true));
        using (var image = Image.Load(cropped.Build()))
        {
            Assert.Equal((4, 2), (image.Width, image.Height));
            var pixels = ImageSnapshots.CaptureFrame(image.Frames[0]);

            // The aperture of the file selects 8x6 samples of the picture; the center of this one is 2 rows below the
            // center of these
            var (left, top) = (2, 4);
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    var (r, g, b, _) = HeicTestFiles.GetSourceSample(left + x, top + y);
                    Assert.Equal((r, g, b), (pixels.GetSample(x, y, 0), pixels.GetSample(x, y, 1), pixels.GetSample(x, y, 2)));
                }
            }
        }
    }

    [Fact]
    public void TheImagesOfASequenceDecodeToTheirSources()
    {
        var info = Image.Identify(HeicTestFiles.Sequence, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal((ImageFormat.Heic, 72, 66, 3, true), (info.Format, info.Width, info.Height, info.FrameCount, info.IsAnimated));
        Assert.Equal(3, info.Animation!.TotalPlays);

        using var image = Image.Load(HeicTestFiles.Sequence);
        Assert.Equal(3, image.Frames.Count);
        Assert.Equal(3, image.Animation!.TotalPlays);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(new FrameDuration(40, 1000), image.Frames[i].Metadata.Duration);
            AssertBlocks(ImageSnapshots.CaptureFrame(image.Frames[i]), frame: i, message: $"image {i}");
        }

        // The frame limit stops the decoding
        using var first = Image.Load(HeicTestFiles.Sequence, new ImageDecodeOptions { FrameLimit = 1 });
        Assert.Single(first.Frames);
        AssertBlocks(ImageSnapshots.CaptureFrame(first.Frames[0]));
    }

    [Theory]
    [InlineData(61, 59, -11, -7, 0, 0)]
    [InlineData(60, 60, 0, 0, 6, 3)]
    [InlineData(40, 30, 10, 4, 21, 20)]
    public void TheCleanApertureOfASequenceIsApplied(int width, int height, int horizontalOffset2, int verticalOffset2, int left, int top)
    {
        // The images are the part of the 72x66 pictures that the 'clap' box of the sample entry keeps: its center is
        // at the offset from the center of the pictures (ISO/IEC 14496-12, section 12.1.4)
        var builder = HeifFileBuilder.From(HeicTestFiles.Sequence);
        builder.Tracks[0].SampleEntryBoxes.Add(HeifFileBuilder.CleanAperture((uint)width, 1, (uint)height, 1, horizontalOffset2, 2, verticalOffset2, 2));
        var data = builder.Build();

        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal((ImageFormat.Heic, width, height, 3), (info.Format, info.Width, info.Height, info.FrameCount));

        using var image = Image.Load(data);
        Assert.Equal((width, height, 3), (image.Width, image.Height, image.Frames.Count));
        for (var i = 0; i < 3; i++)
        {
            var pixels = ImageSnapshots.CaptureFrame(image.Frames[i]);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var expected = HeicTestFiles.GetBlockSample(x + left, y + top, i);
                    var actual = (pixels.GetSample(x, y, 0), pixels.GetSample(x, y, 1), pixels.GetSample(x, y, 2));
                    Assert.True(expected == actual, $"image {i}: pixel ({x}, {y}) is {actual}, {expected} expected.");
                }
            }
        }

        // A clean aperture that is not inside the pictures
        builder = HeifFileBuilder.From(HeicTestFiles.Sequence);
        builder.Tracks[0].SampleEntryBoxes.Add(HeifFileBuilder.CleanAperture(74, 1, 66, 1, 0, 1, 0, 1));
        var invalid = builder.Build();
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(invalid));
        Assert.Equal(ImageFormat.Heic, exception.Format);
    }

    [Fact]
    public void TheImagesOfASequenceAreReadOneByOne()
    {
        using var stream = new MemoryStream(HeicTestFiles.Sequence);
        using var reader = Image.OpenReader<Rgba32>(stream);
        Assert.Equal(ImageFormat.Heic, reader.Info.Format);
        var index = 0;
        while (reader.ReadFrame() is { } frame)
        {
            using (frame)
            {
                Assert.Equal(new FrameDuration(40, 1000), frame.Frames[0].Metadata.Duration);
                AssertBlocks(ImageSnapshots.CaptureFrame(frame.Frames[0]), frame: index, message: $"image {index}");
            }

            index++;
        }

        Assert.Equal(3, index);
    }

    [Fact]
    public void TheImagesOfASequenceArePresentedInTheOrderOfTheirCompositionTimes()
    {
        // The samples are in decoding order: the third one is a picture that is presented before the second one
        var track = ReferenceHeif.Parse(HeicTestFiles.ReorderedSequence).ColorTrack!;
        Assert.Equal([0L, 25L, -25L, 0L], track.CompositionOffsets);
        Assert.Equal([(0, 25L), (2, 25L), (1, 25L), (3, 25L)], track.PresentationOrder);

        using var image = Image.Load(HeicTestFiles.ReorderedSequence);
        Assert.Equal(4, image.Frames.Count);
        Assert.NotNull(image.Animation);
        Assert.Null(image.Animation.TotalPlays);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(new FrameDuration(25, 600), image.Frames[i].Metadata.Duration);
            AssertBlocks(ImageSnapshots.CaptureFrame(image.Frames[i]), frame: i, message: $"image {i}");
        }

        // A reader gives the images in the same order, and a frame limit stops after the images it asks for: the decoder
        // was given the samples that the second image needs, not the image that follows them
        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(HeicTestFiles.ReorderedSequence));
        var index = 0;
        while (reader.ReadFrame() is { } frame)
        {
            using (frame)
            {
                AssertBlocks(ImageSnapshots.CaptureFrame(frame.Frames[0]), frame: index, message: $"image {index} of the reader");
            }

            index++;
        }

        Assert.Equal(4, index);
        using var limited = Image.Load(HeicTestFiles.ReorderedSequence, new ImageDecodeOptions { FrameLimit = 2 });
        Assert.Equal(2, limited.Frames.Count);
        AssertBlocks(ImageSnapshots.CaptureFrame(limited.Frames[1]), frame: 1);

        var info = Image.Identify(HeicTestFiles.ReorderedSequence, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal((4, true), (info.FrameCount, info.IsAnimated));
    }

    [Fact]
    public void AnImageThatDoesNotMatchItsDescriptionIsInvalid()
    {
        // The size of the 'ispe' property is not the size of the picture, which is 64x64: the header of the file cannot tell
        var size = HeifFileBuilder.From(HeicTestFiles.Rgb);
        var item = ReferenceHeif.Parse(HeicTestFiles.Rgb).PrimaryItem!;
        Assert.Equal((64L, 64L), item.Size);
        size.Properties[item.Find("ispe")!.Index - 1] = HeifFileBuilder.ImageSize(80, 64);
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(size.Build()));
        Assert.Equal(ImageFormat.Heic, exception.Format);
        Assert.Contains("'ispe'", exception.Message, StringComparison.Ordinal);
        Assert.Equal(6, Image.Identify(size.Build()).Height);

        // The sample format of the configuration record is not the one of the bitstream: a full scan sees it too
        var configuration = item.HevcConfiguration!;
        var record = HeifFileBuilder.From(HeicTestFiles.Rgb);
        record.Properties[item.Find("hvcC")!.Index - 1] = HeifFileBuilder.HevcConfiguration(configuration.ChromaFormat, 10, 10, [.. configuration.Arrays.Select(array => (array.NalUnitType, array.NalUnits.Select(unit => unit.ToArray()).ToArray()))]);
        Assert.Equal(ImageFormat.Heic, Assert.Throws<InvalidImageContentException>(() => Image.Load(record.Build())).Format);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(record.Build(), new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));

        // The data of the item is cut inside a NAL unit, or holds the picture twice
        var data = ReferenceHeif.Parse(HeicTestFiles.Rgb).ReadItemData(item);
        var truncated = HeifFileBuilder.From(HeicTestFiles.Rgb);
        truncated.GetItem(item.Id).Data = data[..^3];
        Assert.Equal(ImageFormat.Heic, Assert.Throws<InvalidImageContentException>(() => Image.Load(truncated.Build())).Format);

        var twice = HeifFileBuilder.From(HeicTestFiles.Rgb);
        twice.GetItem(item.Id).Data = [.. data, .. data];
        Assert.Equal(ImageFormat.Heic, Assert.Throws<InvalidImageContentException>(() => Image.Load(twice.Build())).Format);

        // No picture at all: a NAL unit that is not a slice
        var empty = HeifFileBuilder.From(HeicTestFiles.Rgb);
        empty.GetItem(item.Id).Data = [0x00, 0x00, 0x00, 0x02, 0x46, 0x01];
        Assert.Equal(ImageFormat.Heic, Assert.Throws<InvalidImageContentException>(() => Image.Load(empty.Build())).Format);
    }

    [Fact]
    public void TheParameterSetThatTheIdentificationReadsDescribesThePicture()
    {
        // The configuration record has the sequence parameter set of another file (gray samples), and the data of the item
        // start with the parameter set of the picture, which replaces the one of the record for the decoder. The file is
        // described by the parameter set of its record: the identification rejects it, and so do the loads, which found
        // their picture decodable (a fuzzing finding: FuzzRegressions, heic/record-sps-differs-from-item-sps)
        var file = ReferenceHeif.Parse(HeicTestFiles.Rgb);
        var item = file.PrimaryItem!;
        var configuration = item.HevcConfiguration!;
        var grayConfiguration = ReferenceHeif.Parse(HeicTestFiles.Gray).PrimaryItem!.HevcConfiguration!;
        var sequenceParameterSet = configuration.Arrays.Single(array => array.NalUnitType is 33).NalUnits.Single().ToArray();
        var builder = HeifFileBuilder.From(HeicTestFiles.Rgb);
        builder.Properties[item.Find("hvcC")!.Index - 1] = HeifFileBuilder.HevcConfiguration(configuration.ChromaFormat, configuration.BitDepthLuma, configuration.BitDepthChroma,
            [.. configuration.Arrays.Select(array => (array.NalUnitType, (array.NalUnitType is 33 ? grayConfiguration.Arrays.Single(other => other.NalUnitType is 33) : array).NalUnits.Select(unit => unit.ToArray()).ToArray()))]);
        builder.GetItem(item.Id).Data = [.. HeifFileBuilder.U32((uint)sequenceParameterSet.Length), .. sequenceParameterSet, .. file.ReadItemData(item)];
        var data = builder.Build();
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
        Assert.Equal(ImageFormat.Heic, Assert.Throws<InvalidImageContentException>(() => Image.Load(data)).Format);

        // With the parameter set of the picture in the record too, the data may repeat it
        var repeated = HeifFileBuilder.From(HeicTestFiles.Rgb);
        repeated.GetItem(item.Id).Data = [.. HeifFileBuilder.U32((uint)sequenceParameterSet.Length), .. sequenceParameterSet, .. file.ReadItemData(item)];
        using var image = Image.Load(repeated.Build());
        AssertSource(ImageSnapshots.CaptureFrame(image.Frames[0]));
    }

    [Fact]
    public void TheTilesOfAGridShareAConfigurationRecordOfAnySize()
    {
        // The record of the tiles has its picture parameter set 3000 times: it is read once for the image, not once for
        // each tile (a file of 90 KB whose 1024 tiles each read 8000 parameter sets took a minute to decode)
        var file = ReferenceHeif.Parse(HeicTestFiles.Grid);
        var tile = file.GetGridTiles(file.PrimaryItem!)[0];
        var configuration = tile.HevcConfiguration!;
        var builder = HeifFileBuilder.From(HeicTestFiles.Grid);
        builder.Properties[tile.Find("hvcC")!.Index - 1] = HeifFileBuilder.HevcConfiguration(configuration.ChromaFormat, configuration.BitDepthLuma, configuration.BitDepthChroma,
            [.. configuration.Arrays.Select(array => (array.NalUnitType, (array.NalUnitType is 34 ? Enumerable.Repeat(array.NalUnits[0], 3000) : array.NalUnits).Select(unit => unit.ToArray()).ToArray()))]);
        using var image = Image.Load(builder.Build());
        Assert.Equal((130, 70), (image.Width, image.Height));
        AssertBlocks(ImageSnapshots.CaptureFrame(image.Frames[0]));
    }
}
