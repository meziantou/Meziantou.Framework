using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// JPEG XL identification and container rules, on files written by the reference tools (<see cref="JpegXlTestFiles"/>) and
/// on containers assembled around their codestreams: the image header, the frames a viewer shows, the metadata boxes, the
/// codestream split in parts, truncation and limits.
/// </summary>
public sealed class JpegXlDecoderTests
{
    public static TheoryData<string, int, int, PixelFormat, ImageColorModel, int, bool> Headers => new()
    {
        { nameof(JpegXlTestFiles.Rgb), 5, 3, PixelFormat.Rgb24, ImageColorModel.Rgb, 8, false },
        { nameof(JpegXlTestFiles.RgbaContainer), 5, 3, PixelFormat.Rgba32, ImageColorModel.Rgba, 8, true },
        { nameof(JpegXlTestFiles.Gray16Container), 5, 3, PixelFormat.Gray16, ImageColorModel.Grayscale, 16, false },
        { nameof(JpegXlTestFiles.SplitCodestreamWithXmp), 5, 3, PixelFormat.Rgb24, ImageColorModel.Rgb, 8, false },
        { nameof(JpegXlTestFiles.SplitCodestreamWithCompressedXmp), 5, 3, PixelFormat.Rgba32, ImageColorModel.Rgba, 8, true },
        { nameof(JpegXlTestFiles.Oriented12Bit), 3, 2, PixelFormat.Rgba64, ImageColorModel.Rgba, 12, true },
    };

    public static TheoryData<string> Files =>
    [
        nameof(JpegXlTestFiles.Rgb),
        nameof(JpegXlTestFiles.RgbaContainer),
        nameof(JpegXlTestFiles.Gray16Container),
        nameof(JpegXlTestFiles.SplitCodestreamWithXmp),
        nameof(JpegXlTestFiles.SplitCodestreamWithCompressedXmp),
        nameof(JpegXlTestFiles.Oriented12Bit),
        nameof(JpegXlTestFiles.LayeredAnimation),
    ];

    [Theory]
    [MemberData(nameof(Headers))]
    public void IdentifyReadsTheImageHeader(string file, int width, int height, PixelFormat pixelFormat, ImageColorModel colorModel, int bitsPerComponent, bool hasAlpha)
    {
        var data = GetFile(file);
        Assert.Equal(ImageFormat.JpegXl, Image.DetectFormat(data));
        foreach (var mode in new[] { ImageIdentifyMode.Header, ImageIdentifyMode.FullScan })
        {
            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = mode });
            Assert.Equal(ImageFormat.JpegXl, info.Format);
            Assert.Equal(ImageFormat.JpegXl, info.Metadata.SourceFormat);
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

    [Fact]
    public void LayersAreNotCountedAsFrames()
    {
        // Three coded frames, the second of which has no duration: a viewer shows two images
        var data = JpegXlTestFiles.LayeredAnimation;
        var header = Image.Identify(data);
        Assert.True(header.IsAnimated);
        Assert.Null(header.FrameCount);
        Assert.NotNull(header.Animation);
        Assert.Null(header.Animation.TotalPlays);

        var scan = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.True(scan.IsAnimated);
        Assert.Equal(2, scan.FrameCount);
        Assert.Equal((4, 4, PixelFormat.Rgb24), (scan.Width, scan.Height, scan.PixelFormat));
    }

    [Fact]
    public void TheOrientationIsTheOneOfTheImageHeader()
    {
        // The size is the stored one: the orientation is reported, never applied
        var info = Image.Identify(JpegXlTestFiles.Oriented12Bit);
        Assert.Equal(ExifOrientation.RightTop, info.Metadata.Orientation);
        Assert.Equal((3, 2), (info.Width, info.Height));
    }

    [Theory]
    [InlineData(nameof(JpegXlTestFiles.SplitCodestreamWithXmp))]
    [InlineData(nameof(JpegXlTestFiles.SplitCodestreamWithCompressedXmp))]
    public void MetadataBoxesBeforeTheFramesAreReadByAHeaderIdentification(string file)
    {
        // The first part of the codestream holds the image header only; the XMP box, compressed or not, comes before the frames
        var data = GetFile(file);
        foreach (var mode in new[] { ImageIdentifyMode.Header, ImageIdentifyMode.FullScan })
        {
            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = mode });
            Assert.NotNull(info.Metadata.XmpProfile);
            Assert.Equal(JpegXlTestFiles.XmpPacket, info.Metadata.XmpProfile.Data.Span);
            Assert.Null(info.Metadata.ExifProfile);
        }
    }

    [Fact]
    public void MetadataBoxesAfterTheCodestreamAreReadByAFullScan()
    {
        // 'Exif' payload: the offset of the TIFF header (2 bytes are skipped), then a little-endian TIFF header and an empty directory
        byte[] tiff = [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        byte[] data =
        [
            .. JpegXlTestFiles.ContainerStart,
            .. JpegXlTestFiles.Box("jxlc", JpegXlTestFiles.Rgb),
            .. JpegXlTestFiles.Box("Exif", [0, 0, 0, 2, 0xAA, 0xBB, .. tiff]),
            .. JpegXlTestFiles.Box("xml ", JpegXlTestFiles.XmpPacket),
            .. JpegXlTestFiles.Box("xml ", "<second/>"u8),
            .. JpegXlTestFiles.Box("free", [1, 2, 3]),
        ];

        var header = Image.Identify(data);
        Assert.Null(header.Metadata.ExifProfile);
        Assert.Null(header.Metadata.XmpProfile);

        var scan = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.NotNull(scan.Metadata.ExifProfile);
        Assert.Equal(tiff, scan.Metadata.ExifProfile.Data.Span);
        Assert.NotNull(scan.Metadata.XmpProfile);
        Assert.Equal(JpegXlTestFiles.XmpPacket, scan.Metadata.XmpProfile.Data.Span);

        // The orientation comes from the image header, whatever the EXIF block says
        Assert.Equal(ExifOrientation.TopLeft, scan.Metadata.Orientation);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task EveryInputVariantIdentifiesTheSameImage(string file)
    {
        var data = GetFile(file);
        foreach (var mode in new[] { ImageIdentifyMode.Header, ImageIdentifyMode.FullScan })
        {
            var options = new ImageIdentifyOptions { Mode = mode };
            var expected = Describe(Image.Identify(data, options));
            foreach (var variant in InputVariants.All)
            {
                var info = await InputVariants.IdentifyAsync(variant, data, ImageFormat.JpegXl, options, XunitCancellationToken);
                Assert.Equal(expected, Describe(info), message: $"{variant}, {mode}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void ACodestreamSplitAnywhereIsTheSameImage(string file)
    {
        // Parts of the codestream may end anywhere: inside the image header, inside a table of contents, inside a section
        var data = GetFile(file);
        if (!data.AsSpan().StartsWith([(byte)0xFF, (byte)0x0A]))
            return;

        var options = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan };
        var expected = Describe(Image.Identify(data, options));
        for (var split = 0; split <= data.Length; split++)
        {
            var container = JpegXlTestFiles.WrapInParts(data, split);
            Assert.Equal(expected, Describe(Image.Identify(container, options)), message: $"split at {split}");
        }

        var three = JpegXlTestFiles.WrapInParts(data, 1, 1, data.Length / 2);
        Assert.Equal(expected, Describe(Image.Identify(three, options)));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void TruncationIsNeverACleanEnd(string file)
    {
        // A full scan needs every byte of these files: each proper prefix that is still recognized is invalid
        var data = GetFile(file);
        var options = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan };
        for (var length = 12; length < data.Length; length++)
        {
            var prefix = data.AsSpan(0, length).ToArray();
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(prefix, options));
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(new MemoryStream(prefix), options));
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void AHeaderIdentificationStopsBeforeTheFrames(string file)
    {
        // The smallest encoded-byte limit that identifies the header is smaller than the file, and one byte less fails with the limit
        var data = GetFile(file);
        var required = -1;
        for (var limit = 1; limit <= data.Length && required < 0; limit++)
        {
            var options = new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxEncodedBytes = limit } } };
            try
            {
                _ = Image.Identify(data, options);
                required = limit;
            }
            catch (ImageResourceLimitException exception)
            {
                Assert.Equal(ImageResourceLimitKind.EncodedBytes, exception.Kind);
            }
        }

        Assert.InRange(required, 3, data.Length - 1);
    }

    [Fact]
    public void LimitsAreCheckedOnTheImageHeader()
    {
        var data = JpegXlTestFiles.Rgb;
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, Limits(new ImageResourceLimits { MaxWidth = 4 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.Height, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, Limits(new ImageResourceLimits { MaxHeight = 2 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.FramePixels, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, Limits(new ImageResourceLimits { MaxFramePixels = 14 }))).Kind);
        _ = Image.Identify(data, Limits(new ImageResourceLimits { MaxWidth = 5, MaxHeight = 3, MaxFramePixels = 15 }));

        // The frames of an animation are counted by a full scan only, and a layer is not a frame
        var animation = JpegXlTestFiles.LayeredAnimation;
        _ = Image.Identify(animation, Limits(new ImageResourceLimits { MaxFrames = 1 }));
        _ = Image.Identify(animation, Limits(new ImageResourceLimits { MaxFrames = 2 }, ImageIdentifyMode.FullScan));
        Assert.Equal(ImageResourceLimitKind.Frames, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(animation, Limits(new ImageResourceLimits { MaxFrames = 1 }, ImageIdentifyMode.FullScan))).Kind);

        // The XMP packet of 38 bytes is charged to the metadata limit before it is kept
        var metadata = JpegXlTestFiles.SplitCodestreamWithXmp;
        _ = Image.Identify(metadata, Limits(new ImageResourceLimits { MaxMetadataBytes = 38 }));
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(metadata, Limits(new ImageResourceLimits { MaxMetadataBytes = 37 }))).Kind);
        var compressed = JpegXlTestFiles.SplitCodestreamWithCompressedXmp;
        _ = Image.Identify(compressed, Limits(new ImageResourceLimits { MaxMetadataBytes = 38 }));
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(compressed, Limits(new ImageResourceLimits { MaxMetadataBytes = 37 }))).Kind);
    }

    [Fact]
    public void ContainerRulesAreEnforced()
    {
        var codestream = JpegXlTestFiles.Rgb;
        var options = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan };
        byte[] signature = [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];
        byte[] part0 = [0, 0, 0, 0, .. codestream[..10]];
        byte[] part1Last = [0x80, 0, 0, 1, .. codestream[10..]];
        byte[] part1 = [0, 0, 0, 1, .. codestream[10..]];
        byte[] part2Last = [0x80, 0, 0, 2, .. codestream[10..]];

        // The reference: the same boxes in a valid order
        byte[] valid = [.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlp", part0), .. JpegXlTestFiles.Box("jxlp", part1Last)];
        Assert.Equal(1, Image.Identify(valid, options).FrameCount);

        AssertInvalid([.. signature, .. JpegXlTestFiles.Box("jxlc", codestream)], "no file type box");
        AssertInvalid([.. signature, .. JpegXlTestFiles.Box("ftyp", "avif\0\0\0\0jxl "u8), .. JpegXlTestFiles.Box("jxlc", codestream)], "the brand is not 'jxl '");
        AssertInvalid([.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlc", codestream), .. JpegXlTestFiles.Box("jxlc", codestream)], "two codestreams");
        AssertInvalid([.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlp", part0), .. JpegXlTestFiles.Box("jxlc", codestream)], "a codestream box after a part");
        AssertInvalid([.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlp", part0), .. JpegXlTestFiles.Box("jxlp", part2Last)], "a part is missing");
        AssertInvalid([.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlp", part1), .. JpegXlTestFiles.Box("jxlp", part0)], "the parts are not in order");
        AssertInvalid([.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlp", part0), .. JpegXlTestFiles.Box("jxlp", part1Last), .. JpegXlTestFiles.Box("jxlp", part2Last)], "a part after the last one");
        AssertInvalid([.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlp", [0, 0, 0])], "a part without index");
        AssertInvalid([.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlp", part0), .. JpegXlTestFiles.Box("free", [1, 2, 3])], "the codestream is not complete");
        AssertInvalid([.. JpegXlTestFiles.ContainerStart, 0, 0, 0, 7, (byte)'j', (byte)'x', (byte)'l', (byte)'c', .. codestream], "a box smaller than its header");
        AssertInvalid([.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("brob", [(byte)'j', (byte)'x', (byte)'l', (byte)'c', 0x0B, 0x00, 0x80]), .. JpegXlTestFiles.Box("jxlc", codestream)], "a compressed codestream box");
        AssertInvalid([.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlc", codestream), 0, 0, 0], "bytes that are not a box after the last box");

        void AssertInvalid(byte[] data, string reason)
        {
            try
            {
                _ = Image.Identify(data, options);
            }
            catch (InvalidImageContentException)
            {
                return;
            }

            Assert.Fail($"The file is accepted: {reason}.");
        }
    }

    [Fact]
    public async Task BoxesMayUseTheLargeSizeAndTheLastOneMayExtendToTheEndOfTheInput()
    {
        var codestream = JpegXlTestFiles.Rgb;
        var options = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan };
        var expected = Describe(Image.Identify(codestream, options));

        // A 64-bit size: the 32-bit field is 1 and the size follows the type
        byte[] large = [.. JpegXlTestFiles.ContainerStart, 0, 0, 0, 1, (byte)'j', (byte)'x', (byte)'l', (byte)'c', 0, 0, 0, 0, 0, 0, 0, (byte)(16 + codestream.Length), .. codestream];
        Assert.Equal(expected, Describe(Image.Identify(large, options)));

        // A size of 0: the box is the rest of the file, a codestream box or a box that is skipped
        byte[] toEnd = [.. JpegXlTestFiles.ContainerStart, 0, 0, 0, 0, (byte)'j', (byte)'x', (byte)'l', (byte)'c', .. codestream];
        Assert.Equal(expected, Describe(Image.Identify(toEnd, options)));
        byte[] skippedToEnd = [.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlc", codestream), 0, 0, 0, 0, (byte)'f', (byte)'r', (byte)'e', (byte)'e', 1, 2, 3];
        Assert.Equal(expected, Describe(Image.Identify(skippedToEnd, options)));

        // An XMP box that extends to the end of the input is read to that end
        byte[] metadataToEnd = [.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlc", codestream), 0, 0, 0, 0, (byte)'x', (byte)'m', (byte)'l', (byte)' ', .. JpegXlTestFiles.XmpPacket];
        foreach (var variant in InputVariants.All)
        {
            var info = await InputVariants.IdentifyAsync(variant, metadataToEnd, ImageFormat.JpegXl, options, XunitCancellationToken);
            Assert.NotNull(info.Metadata.XmpProfile);
            Assert.Equal(JpegXlTestFiles.XmpPacket, info.Metadata.XmpProfile.Data.Span);
        }
    }

    [Theory]
    [InlineData(nameof(JpegXlTestFiles.Rgb), 3)]
    [InlineData(nameof(JpegXlTestFiles.SplitCodestreamWithXmp), 3)]
    [InlineData(nameof(JpegXlTestFiles.RgbaContainer), 4)]
    [InlineData(nameof(JpegXlTestFiles.SplitCodestreamWithCompressedXmp), 4)]
    public void LosslessEightBitFilesDecodeToTheSamplesGivenToTheEncoder(string file, int channels)
    {
        // Sample i of the source (row by row, channel after channel) is (i * 37 + 11) mod 256
        using var image = Image.Load(GetFile(file));
        Assert.Equal(channels == 4 ? PixelFormat.Rgba32 : PixelFormat.Rgb24, image.PixelFormat);
        Assert.Equal((5, 3, 1), (image.Width, image.Height, image.Frames.Count));
        Assert.Null(image.Animation);
        using var typed = image.CloneAs<Rgba32>();
        for (var pixel = 0; pixel < 15; pixel++)
        {
            var expected = new Rgba32(Sample(pixel, 0), Sample(pixel, 1), Sample(pixel, 2), channels == 4 ? Sample(pixel, 3) : byte.MaxValue);
            Assert.Equal(expected, typed.Frames[0][pixel % 5, pixel / 5], message: $"pixel {pixel}");
        }

        byte Sample(int pixel, int channel) => (byte)((((pixel * channels) + channel) * 37) + 11);
    }

    [Fact]
    public void LosslessSixteenBitGrayDecodesToTheSamplesGivenToTheEncoder()
    {
        // Sample i of the source is (i * 3701 + 11) mod 65536: the low bits are kept
        using var image = Image.Load<Gray16>(JpegXlTestFiles.Gray16Container);
        Assert.Equal((5, 3, 1), (image.Width, image.Height, image.Frames.Count));
        for (var pixel = 0; pixel < 15; pixel++)
        {
            Assert.Equal((ushort)((pixel * 3701) + 11), image.Frames[0][pixel % 5, pixel / 5].Value, message: $"pixel {pixel}");
        }
    }

    [Fact]
    public void OtherBitDepthsAreScaledExactly()
    {
        // Every sample of the 12-bit file is 7 (color and alpha): round(7 * 65535 / 4095) = 112. The pixels are the stored ones
        using var image = Image.Load(JpegXlTestFiles.Oriented12Bit);
        Assert.Equal(PixelFormat.Rgba64, image.PixelFormat);
        Assert.Equal((3, 2), (image.Width, image.Height));
        Assert.Equal(ExifOrientation.RightTop, image.Metadata.Orientation);
        using var typed = image.CloneAs<Rgba64>();
        for (var pixel = 0; pixel < 6; pixel++)
        {
            Assert.Equal(new Rgba64(112, 112, 112, 112), typed.Frames[0][pixel % 3, pixel / 3]);
        }
    }

    [Theory]
    [MemberData(nameof(StillFiles))]
    public async Task EveryInputVariantLoadsTheSameImage(string file)
    {
        var data = GetFile(file);
        using var expected = Image.Load<Rgba64>(data);
        var expectedPixels = GetPixels(expected.Frames[0]);
        foreach (var variant in InputVariants.All)
        {
            using var image = await InputVariants.LoadAsync<Rgba64>(variant, data, ImageFormat.JpegXl, options: null, XunitCancellationToken);
            Assert.Equal(expectedPixels, GetPixels(image.Frames[0]));
            Assert.Equal(expected.Metadata.XmpProfile?.Data.Length, image.Metadata.XmpProfile?.Data.Length);
            await InputVariants.AssertEncodedByteLimitBoundaryAsync(variant, data, ImageFormat.JpegXl, XunitCancellationToken);
        }
    }

    [Theory]
    [MemberData(nameof(StillFiles))]
    public async Task ReadersProduceTheSameFramesAsEagerLoads(string file)
    {
        var data = GetFile(file);
        using var eager = Image.Load<Rgba64>(data);
        await using var reader = await Image.OpenReaderAsync<Rgba64>(new MemoryStream(data), cancellationToken: XunitCancellationToken);
        Assert.Equal((eager.Width, eager.Height), (reader.Info.Width, reader.Info.Height));
        using var frame = await reader.ReadFrameAsync(XunitCancellationToken);
        Assert.NotNull(frame);
        Assert.Equal(GetPixels(eager.Frames[0]), GetPixels(frame.Frames[0]));
        Assert.Null(await reader.ReadFrameAsync(XunitCancellationToken));
    }

    [Theory]
    [MemberData(nameof(StillFiles))]
    public void TruncatedFilesDoNotLoad(string file)
    {
        var data = GetFile(file);
        for (var length = 12; length < data.Length; length++)
        {
            var prefix = data.AsSpan(0, length).ToArray();
            Assert.Throws<InvalidImageContentException>(() => Image.Load(prefix).Dispose());
        }
    }

    [Fact]
    public void MetadataBoxesAfterTheCodestreamAreLoaded()
    {
        byte[] data = [.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlc", JpegXlTestFiles.Rgb), .. JpegXlTestFiles.Box("xml ", JpegXlTestFiles.XmpPacket)];
        using var image = Image.Load(data);
        Assert.NotNull(image.Metadata.XmpProfile);
        Assert.Equal(JpegXlTestFiles.XmpPacket, image.Metadata.XmpProfile.Data.Span);

        // A reader gives the metadata that follows the image with the image
        using var reader = Image.OpenReader<Rgb24>(new MemoryStream(data));
        using var frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.NotNull(frame.Metadata.XmpProfile);
        Assert.Equal(JpegXlTestFiles.XmpPacket, frame.Metadata.XmpProfile.Data.Span);
    }

    [Fact]
    public void PixelsThatAreDecodedWithoutBeingShownCountTowardTheTotal()
    {
        // Two frames of 4x4 pixels are shown; the layer between them has 2x2 pixels
        var animation = JpegXlTestFiles.LayeredAnimation;
        using (var image = Image.Load(animation, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxTotalPixels = 36 } } }))
        {
            Assert.HasCount(2, image.Frames);
        }

        var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Load(animation, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxTotalPixels = 35 } } }));
        Assert.Equal(ImageResourceLimitKind.TotalPixels, exception.Kind);

        // The frame that is not shown has 16 pixels; the one that is shown has 36, 16 of them in the image
        var still = JpegXlTestFiles.FrameLargerThanTheImage;
        using (var image = Image.Load<Rgb24>(still, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxTotalPixels = 52 } } }))
        {
            Assert.Equal((4, 4, 1), (image.Width, image.Height, image.Frames.Count));
            for (var pixel = 0; pixel < 16; pixel++)
            {
                Assert.Equal(new Rgb24(120, 120, 120), image.Frames[0][pixel % 4, pixel / 4]);
            }
        }

        exception = Assert.Throws<ImageResourceLimitException>(() => Image.Load(still, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxTotalPixels = 51 } } }));
        Assert.Equal(ImageResourceLimitKind.TotalPixels, exception.Kind);

        // Every frame is checked against the limits of a frame, shown or not
        exception = Assert.Throws<ImageResourceLimitException>(() => Image.Load(still, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 5 } } }));
        Assert.Equal(ImageResourceLimitKind.Width, exception.Kind);
        exception = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(still, Limits(new ImageResourceLimits { MaxFramePixels = 35 }, ImageIdentifyMode.FullScan)));
        Assert.Equal(ImageResourceLimitKind.FramePixels, exception.Kind);
        _ = Image.Identify(still, Limits(new ImageResourceLimits { MaxFramePixels = 16 }));
    }

    [Fact]
    public void CustomPrimariesAndGammaAreDescribedByAnIccProfile()
    {
        // The samples are the stored ones: sample i of the source is (i * 37 + 11) mod 256
        using var image = Image.Load<Rgb24>(JpegXlTestFiles.CustomPrimariesAndGamma);
        for (var pixel = 0; pixel < 15; pixel++)
        {
            Assert.Equal(new Rgb24((byte)((pixel * 111) + 11), (byte)((pixel * 111) + 48), (byte)((pixel * 111) + 85)), image.Frames[0][pixel % 5, pixel / 5]);
        }

        Assert.Null(image.Metadata.Cicp);
        Assert.Equal(ColorTransferFunction.Srgb, image.Metadata.TransferFunction);
        var profile = image.Metadata.IccProfile;
        Assert.NotNull(profile);
        Assert.True(MetadataValidation.TryValidateIccProfile(profile.Data.Span, out _));
        Assert.Equal(IccProfileColorSpace.Rgb, profile.ColorSpace);
        Assert.Equal(IccProfileClass.Display, profile.ProfileClass);
        Assert.Equal(IccRenderingIntent.RelativeColorimetric, profile.RenderingIntent);

        // The primaries of Adobe RGB (1998) adapted from D65 to D50 with the linear Bradford transform, as published for
        // the D50 of ASTM E308 (Z = 0.82521; the ICC illuminant has Z = 0.8249, hence the tolerance)
        AssertNumbers(GetIccTag(profile, "rXYZ")[8..], 0.0005, 0.6097559, 0.3111242, 0.0194811);
        AssertNumbers(GetIccTag(profile, "gXYZ")[8..], 0.0005, 0.2052401, 0.6256560, 0.0608902);
        AssertNumbers(GetIccTag(profile, "bXYZ")[8..], 0.0005, 0.1492240, 0.0632197, 0.7448387);
        AssertNumbers(GetIccTag(profile, "chad")[8..], 0.0005, 1.0478112, 0.0228866, -0.0501270, 0.0295424, 0.9904844, -0.0170491, -0.0092345, 0.0150436, 0.7521316);

        // A parametricCurveType of function type 0: the decoding exponent is 1 / 0.4547
        foreach (var signature in (string[])["rTRC", "gTRC", "bTRC"])
        {
            var curve = GetIccTag(profile, signature);
            Assert.True(curve[..4].SequenceEqual("para"u8));
            Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(curve[8..]));
            AssertNumbers(curve[12..], 0.00002, 1 / 0.4547);
        }

        // Identification reports the same profile
        var info = Image.Identify(JpegXlTestFiles.CustomPrimariesAndGamma);
        Assert.NotNull(info.Metadata.IccProfile);
        Assert.Equal(profile.Data.Span, info.Metadata.IccProfile.Data.Span);
    }

    [Fact]
    public void GrayscaleWithACustomWhitePointIsDescribedByAGrayIccProfile()
    {
        using var image = Image.Load<Gray8>(JpegXlTestFiles.GrayCustomWhitePointAndGamma);
        for (var pixel = 0; pixel < 15; pixel++)
        {
            Assert.Equal((byte)((pixel * 37) + 11), image.Frames[0][pixel % 5, pixel / 5].Value);
        }

        Assert.Null(image.Metadata.Cicp);
        var profile = image.Metadata.IccProfile;
        Assert.NotNull(profile);
        Assert.True(MetadataValidation.TryValidateIccProfile(profile.Data.Span, out _));
        Assert.Equal(IccProfileColorSpace.Gray, profile.ColorSpace);
        Assert.Equal(IccRenderingIntent.Perceptual, profile.RenderingIntent);

        // The white of the profile is the illuminant of the connection space; the adaptation tag keeps the one of the image:
        // it maps the white point (x = 0.34, y = 0.35), that is X = 0.34 / 0.35 and Z = 0.31 / 0.35, to that illuminant
        AssertNumbers(GetIccTag(profile, "wtpt")[8..], 0.00002, 0.9642, 1, 0.8249);
        var adaptation = GetIccTag(profile, "chad")[8..];
        double[] white = [0.34 / 0.35, 1, 0.31 / 0.35];
        double[] illuminant = [0.9642, 1, 0.8249];
        for (var row = 0; row < 3; row++)
        {
            var value = 0.0;
            for (var column = 0; column < 3; column++)
            {
                value += BinaryPrimitives.ReadInt32BigEndian(adaptation[(4 * ((row * 3) + column))..]) / 65536.0 * white[column];
            }

            Assert.True(Math.Abs(value - illuminant[row]) < 0.0001, $"row {row}: {value}");
        }

        var curve = GetIccTag(profile, "kTRC");
        Assert.True(curve[..4].SequenceEqual("para"u8));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(curve[8..]));
        AssertNumbers(curve[12..], 0, 2);
    }

    [Fact]
    public void TheWhitePointEAndTheBt709CurveAreDescribedByAnIccProfile()
    {
        using var image = Image.Load(JpegXlTestFiles.WhitePointE);
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        Assert.Null(image.Metadata.Cicp);
        var profile = image.Metadata.IccProfile;
        Assert.NotNull(profile);
        Assert.True(MetadataValidation.TryValidateIccProfile(profile.Data.Span, out _));
        Assert.Equal(IccRenderingIntent.Saturation, profile.RenderingIntent);

        // The colorants sum to the illuminant of the profile: white converts to the white of the connection space
        var sum = new int[3];
        foreach (var signature in (string[])["rXYZ", "gXYZ", "bXYZ"])
        {
            var colorant = GetIccTag(profile, signature);
            for (var i = 0; i < 3; i++)
            {
                sum[i] += BinaryPrimitives.ReadInt32BigEndian(colorant[(8 + (4 * i))..]);
            }
        }

        Assert.Equal([0x0000F6D6, 0x00010000, 0x0000D32D], sum);

        // Rec. ITU-R BT.709 as a parametricCurveType of function type 3: g = 1 / 0.45, a = 1 / 1.099, b = 0.099 / 1.099,
        // c = 1 / 4.5 and d = 0.081
        var curve = GetIccTag(profile, "rTRC");
        Assert.True(curve[..4].SequenceEqual("para"u8));
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(curve[8..]));
        AssertNumbers(curve[12..], 0.00001, 1 / 0.45, 1 / 1.099, 0.099 / 1.099, 1 / 4.5, 0.081);
    }

    [Fact]
    public void PqWithANonStandardWhitePointIsNotSupported()
    {
        // Neither code points nor a parametric curve of an ICC profile describe it
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(JpegXlTestFiles.PqWithWhitePointE));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(JpegXlTestFiles.PqWithWhitePointE));
    }

    private static ReadOnlySpan<byte> GetIccTag(IccProfile profile, string signature)
    {
        var data = profile.Data.Span;
        var count = BinaryPrimitives.ReadInt32BigEndian(data[128..]);
        for (var i = 0; i < count; i++)
        {
            var entry = data.Slice(132 + (12 * i), 12);
            if (entry[..4].SequenceEqual(Encoding.ASCII.GetBytes(signature)))
                return data.Slice(BinaryPrimitives.ReadInt32BigEndian(entry[4..]), BinaryPrimitives.ReadInt32BigEndian(entry[8..]));
        }

        throw new InvalidOperationException($"Tag '{signature}' not found.");
    }

    /// <summary>Compares s15Fixed16 numbers with their expected values.</summary>
    private static void AssertNumbers(ReadOnlySpan<byte> data, double tolerance, params ReadOnlySpan<double> expected)
    {
        for (var i = 0; i < expected.Length; i++)
        {
            var actual = BinaryPrimitives.ReadInt32BigEndian(data[(4 * i)..]) / 65536.0;
            Assert.True(Math.Abs(actual - expected[i]) <= tolerance + (0.5 / 65536), $"number {i}: expected {expected[i]}, got {actual}");
        }
    }

    public static TheoryData<int> ModularTreeSeeds
    {
        get
        {
            var data = new TheoryData<int>();
            for (var seed = 0; seed < 60; seed++)
            {
                data.Add(seed);
            }

            return data;
        }
    }

    /// <summary>
    /// A Modular sub-bitstream whose tree is drawn at random (the first 14 seeds: one leaf with each predictor; the next 14:
    /// decisions on one property, 2 to 15) is decoded like a transcription of the section H.3 of the standard does: every
    /// sample computes the properties, walks the tree and predicts from its neighbors, at the edges of channels of 1 to 40
    /// samples, with the previous channels of the same size as the source of the properties from 16 on.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModularTreeSeeds))]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ModularSamplesFollowTheirTree(int seed)
    {
        const int StreamIndex = 5;
        var random = new Random(seed);
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(ModularSamplesFollowTheirTree));
        var sizes = new (int Width, int Height, int Shift)[random.Next(1, 5)];
        var width = seed % 3 == 0 ? random.Next(1, 4) : random.Next(1, 41);
        var height = seed % 5 == 0 ? random.Next(1, 4) : random.Next(1, 41);
        for (var i = 0; i < sizes.Length; i++)
        {
            sizes[i] = random.Next(4) == 0 ? (random.Next(1, 9), random.Next(1, 9), random.Next(2)) : (width, height, 0);
        }

        // The tree, in breadth-first order: (property, value, left, right) for a decision, (-1, offset, context, predictor, multiplier) for a leaf
        var nodes = new List<JpegXlMaTreeNode>();
        if (seed < JpegXlMaTree.PredictorCount)
        {
            nodes.Add(new JpegXlMaTreeNode { Property = -1, ValueOrOffset = random.Next(-3, 4), RightOrPredictor = seed, Multiplier = 1 });
        }
        else
        {
            // Then trees whose decisions all test the same property, of the sample and its neighbors, then any
            BuildTree(random, nodes, sizes.Length, seed < JpegXlMaTree.PredictorCount + 14 ? seed - JpegXlMaTree.PredictorCount + 2 : -1);
        }

        var leafCount = 0;
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsLeaf)
            {
                var node = nodes[i];
                node.LeftOrContext = leafCount++;
                nodes[i] = node;
            }
        }

        // The samples: the reference decoder draws the residual of each sample and records the token it reads
        var tokens = new List<(int Context, uint Token)>();
        var expected = new int[sizes.Length][];
        for (var c = 0; c < sizes.Length; c++)
        {
            expected[c] = DecodeReferenceChannel(c);
        }

        using var writer = new LsbBitWriter(scope);
        JpegXlModularEncoder.WriteHeader(writer);
        using (var tree = new JpegXlEntropyEncoder(JpegXlMaTree.TreeContextCount, new JpegXlHybridUintConfig(4, 0, 0), scope))
        {
            foreach (var node in nodes)
            {
                if (node.IsLeaf)
                {
                    var multiplierLog = System.Numerics.BitOperations.TrailingZeroCount(node.Multiplier);
                    tree.Add(1, 0);
                    tree.Add(2, (uint)node.RightOrPredictor);
                    tree.Add(3, Pack(node.ValueOrOffset));
                    tree.Add(4, (uint)multiplierLog);
                    tree.Add(5, (uint)((node.Multiplier >> multiplierLog) - 1));
                }
                else
                {
                    tree.Add(1, (uint)node.Property + 1);
                    tree.Add(0, Pack(node.ValueOrOffset));
                }
            }

            tree.Write(writer);
        }

        using (var samples = new JpegXlEntropyEncoder(leafCount, new JpegXlHybridUintConfig(4, 1, 0), scope, seed % 2 == 0 ? JpegXlEntropyCoding.Ans : JpegXlEntropyCoding.PrefixCodes))
        {
            foreach (var (context, token) in tokens)
            {
                samples.Add(context, token);
            }

            samples.Write(writer);
        }

        writer.Finish();
        var data = writer.WrittenSpan.ToArray();
        using var image = new JpegXlModularImage(8);
        foreach (var (channelWidth, channelHeight, shift) in sizes)
        {
            image.AddChannel(channelWidth, channelHeight, shift, 0);
        }

        var reader = new JpegXlBitReader(data, 0, data.Length);
        JpegXlModularDecoder.Decode(reader, image, StreamIndex, globalTree: null, int.MaxValue, undoTransforms: false, scope, TestContext.Current.CancellationToken);
        reader.EnsureNotOverrun();
        for (var c = 0; c < sizes.Length; c++)
        {
            Assert.Equal(expected[c], image.Channels[c].Samples.ToArray());
        }

        static uint Pack(int value) => value >= 0 ? (uint)value * 2 : ((uint)-value * 2) - 1;

        int[] DecodeReferenceChannel(int channelIndex)
        {
            var (w, h, shift) = sizes[channelIndex];
            var samples = new int[w * h];
            var references = new List<int>();
            for (var i = channelIndex - 1; i >= 0; i--)
            {
                if (sizes[i] == sizes[channelIndex])
                {
                    references.Add(i);
                }
            }

            var properties = new int[16 + (4 * 64)];
            properties[0] = channelIndex;
            properties[1] = StreamIndex;
            using var weighted = new JpegXlWeightedPredictor(JpegXlWeightedPredictorHeader.Default, w, scope);
            for (var y = 0; y < h; y++)
            {
                properties[2] = y;
                properties[9] = 0;
                for (var x = 0; x < w; x++)
                {
                    long Get(int column, int row) => samples[(row * w) + column];
                    long left = x > 0 ? Get(x - 1, y) : y > 0 ? Get(x, y - 1) : 0;
                    long top = y > 0 ? Get(x, y - 1) : left;
                    long topLeft = x > 0 && y > 0 ? Get(x - 1, y - 1) : left;
                    long topRight = x + 1 < w && y > 0 ? Get(x + 1, y - 1) : top;
                    long leftLeft = x > 1 ? Get(x - 2, y) : left;
                    long topTop = y > 1 ? Get(x, y - 2) : top;
                    long topRightRight = x + 2 < w && y > 0 ? Get(x + 2, y - 1) : topRight;
                    var weightedPrediction = weighted.Predict(x, y, top, left, topRight, topLeft, topTop, out var weightedProperty);
                    properties[3] = x;
                    properties[4] = (int)Math.Abs(top);
                    properties[5] = (int)Math.Abs(left);
                    properties[6] = (int)top;
                    properties[7] = (int)left;
                    properties[8] = (int)(left - properties[9]);
                    properties[9] = (int)(left + top - topLeft);
                    properties[10] = (int)(left - topLeft);
                    properties[11] = (int)(topLeft - top);
                    properties[12] = (int)(top - topRight);
                    properties[13] = (int)(top - topTop);
                    properties[14] = (int)(left - leftLeft);
                    properties[15] = weightedProperty;
                    for (var i = 0; i < references.Count; i++)
                    {
                        var other = expected[references[i]];
                        long value = other[(y * w) + x];
                        long otherLeft = x > 0 ? other[(y * w) + x - 1] : 0;
                        long otherTop = y > 0 ? other[((y - 1) * w) + x] : otherLeft;
                        long otherTopLeft = x > 0 && y > 0 ? other[((y - 1) * w) + x - 1] : otherLeft;
                        long predicted = JpegXlModularTransform.ClampedGradient((int)otherLeft, (int)otherTop, (int)otherTopLeft);
                        properties[16 + (i * 4)] = (int)Math.Abs(value);
                        properties[17 + (i * 4)] = (int)value;
                        properties[18 + (i * 4)] = (int)Math.Abs(value - predicted);
                        properties[19 + (i * 4)] = (int)(value - predicted);
                    }

                    var index = 0;
                    while (!nodes[index].IsLeaf)
                    {
                        index = properties[nodes[index].Property] > nodes[index].ValueOrOffset ? nodes[index].LeftOrContext : nodes[index].RightOrPredictor;
                    }

                    var leaf = nodes[index];
                    var token = random.Next(8) == 0 ? (uint)random.Next(1 << 12) : (uint)random.Next(24);
                    tokens.Add((leaf.LeftOrContext, token));
                    var guess = leaf.ValueOrOffset + JpegXlModularTransform.Predict(leaf.RightOrPredictor, left, top, topTop, topLeft, topRight, leftLeft, topRightRight, weightedPrediction);
                    var sample = (int)(((long)JpegXlBitReader.UnpackSigned(token) * leaf.Multiplier) + guess);
                    samples[(y * w) + x] = sample;
                    weighted.Update(sample, x, y);
                }
            }

            return samples;
        }

        static void BuildTree(Random random, List<JpegXlMaTreeNode> nodes, int channelCount, int onlyProperty)
        {
            // Breadth first: each queued node keeps the range of values its ancestors leave to each property
            var queue = new Queue<(int Index, int Depth, Dictionary<int, (int Low, int High)> Ranges)>();
            nodes.Add(default);
            queue.Enqueue((0, 0, []));
            while (queue.TryDequeue(out var item))
            {
                var property = onlyProperty >= 0 ? onlyProperty : random.Next(4) switch
                {
                    0 => random.Next(16),
                    1 => 16 + random.Next(4 * channelCount),
                    _ => random.Next(2, 16),
                };
                var (low, high) = item.Ranges.GetValueOrDefault(property, (-20, 20));
                if (item.Depth >= 6 || random.Next(4) == 0 || low >= high)
                {
                    var multiplierLog = random.Next(3) == 0 ? random.Next(3) : 0;
                    var multiplier = random.Next(3) == 0 ? (random.Next(3) + 1) << multiplierLog : 1;
                    nodes[item.Index] = new JpegXlMaTreeNode { Property = -1, ValueOrOffset = random.Next(-5, 6), RightOrPredictor = random.Next(JpegXlMaTree.PredictorCount), Multiplier = multiplier };
                    continue;
                }

                // The left child takes the values above the split, the right one the others
                var value = random.Next(low, high);
                var left = nodes.Count;
                nodes.Add(default);
                nodes.Add(default);
                nodes[item.Index] = new JpegXlMaTreeNode { Property = property, ValueOrOffset = value, LeftOrContext = left, RightOrPredictor = left + 1, Multiplier = 1 };
                var leftRanges = new Dictionary<int, (int Low, int High)>(item.Ranges) { [property] = (value + 1, high) };
                var rightRanges = new Dictionary<int, (int Low, int High)>(item.Ranges) { [property] = (low, value) };
                queue.Enqueue((left, item.Depth + 1, leftRanges));
                queue.Enqueue((left + 1, item.Depth + 1, rightRanges));
            }
        }
    }

    public static TheoryData<string> StillFiles =>
    [
        nameof(JpegXlTestFiles.Rgb),
        nameof(JpegXlTestFiles.RgbaContainer),
        nameof(JpegXlTestFiles.Gray16Container),
        nameof(JpegXlTestFiles.SplitCodestreamWithXmp),
        nameof(JpegXlTestFiles.SplitCodestreamWithCompressedXmp),
        nameof(JpegXlTestFiles.Oriented12Bit),
    ];

    private static Rgba64[] GetPixels(ImageFrame<Rgba64> frame)
    {
        var pixels = new Rgba64[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }

    private static byte[] GetFile(string name) => name switch
    {
        nameof(JpegXlTestFiles.Rgb) => JpegXlTestFiles.Rgb,
        nameof(JpegXlTestFiles.RgbaContainer) => JpegXlTestFiles.RgbaContainer,
        nameof(JpegXlTestFiles.Gray16Container) => JpegXlTestFiles.Gray16Container,
        nameof(JpegXlTestFiles.LayeredAnimation) => JpegXlTestFiles.LayeredAnimation,
        nameof(JpegXlTestFiles.SplitCodestreamWithXmp) => JpegXlTestFiles.SplitCodestreamWithXmp,
        nameof(JpegXlTestFiles.SplitCodestreamWithCompressedXmp) => JpegXlTestFiles.SplitCodestreamWithCompressedXmp,
        nameof(JpegXlTestFiles.Oriented12Bit) => JpegXlTestFiles.Oriented12Bit,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown file."),
    };

    private static ImageIdentifyOptions Limits(ImageResourceLimits limits, ImageIdentifyMode mode = ImageIdentifyMode.Header)
        => new() { Mode = mode, Configuration = new ImageConfiguration { Limits = limits } };

    private static string Describe(ImageInfo info)
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{info.Format} {info.Width}x{info.Height} {info.PixelFormat} {info.ColorModel} {info.BitsPerComponent} frames={info.FrameCount} animated={info.IsAnimated} alpha={info.MayHaveTransparency} plays={info.Animation?.TotalPlays} orientation={info.Metadata.Orientation} xmp={info.Metadata.XmpProfile?.Data.Length} exif={info.Metadata.ExifProfile?.Data.Length} icc={info.Metadata.IccProfile?.Data.Length} cicp={info.Metadata.Cicp} transfer={info.Metadata.TransferFunction} mode={info.IdentifyMode}");
}
