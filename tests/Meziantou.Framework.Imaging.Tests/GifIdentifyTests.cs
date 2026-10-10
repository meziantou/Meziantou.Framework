using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>GIF structure identification on synthetic inputs: no guessed frame counts, block validation, metadata and limits.</summary>
public sealed class GifIdentifyTests
{
    private static readonly ImageIdentifyOptions FullScan = new() { Mode = ImageIdentifyMode.FullScan };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void HeaderNeverGuessesTheFrameCount(int images)
    {
        var data = SyntheticImages.Gif(3, 2, images);
        var header = Image.Identify(data);
        Assert.Equal(ImageFormat.Gif, header.Format);
        Assert.Equal(new Size(3, 2), header.Size);
        Assert.Equal(PixelFormat.Rgba32, header.PixelFormat);
        Assert.Equal(ImageColorModel.Indexed, header.ColorModel);
        Assert.Equal(8, header.BitsPerComponent);
        Assert.Null(header.FrameCount);
        Assert.Null(header.IsAnimated);
        Assert.Null(header.Animation);
        Assert.False(header.HasPosterFrame);

        var full = Image.Identify(data, FullScan);
        Assert.Equal(images, full.FrameCount);
        Assert.Equal(images > 1, full.IsAnimated);
        Assert.Equal(images > 1 ? 1 : null, full.Animation?.TotalPlays);
    }

    [Fact]
    public void LoopExtensionBeforeTheFirstImageMakesTheFileAnimated()
    {
        // NETSCAPE2.0 loop count L means L + 1 plays; 0 means infinite
        var finite = SyntheticImages.Gif(2, 2, images: 1, loop: 2);
        var header = Image.Identify(finite);
        Assert.True(header.IsAnimated);
        Assert.Null(header.FrameCount);
        Assert.Equal(3, header.Animation?.TotalPlays);
        var full = Image.Identify(finite, FullScan);
        Assert.Equal(1, full.FrameCount);
        Assert.True(full.IsAnimated);

        var infinite = Image.Identify(SyntheticImages.Gif(2, 2, images: 3, loop: 0), FullScan);
        Assert.NotNull(infinite.Animation);
        Assert.Null(infinite.Animation.TotalPlays);
    }

    [Fact]
    public void TransparencyCapabilityIsReportedWithoutAPixelScan()
    {
        Assert.True(Image.Identify(SyntheticImages.Gif(2, 2, images: 1, transparent: true)).MayHaveTransparency);
        Assert.Null(Image.Identify(SyntheticImages.Gif(2, 2, images: 1)).MayHaveTransparency);
        Assert.False(Image.Identify(SyntheticImages.Gif(2, 2, images: 2), FullScan).MayHaveTransparency);

        // A partial first image leaves the (transparent) initial canvas visible
        var partial = new SyntheticImages.GifBuilder().Header(4, 4).Image(1, 1, 2, 2).Trailer().ToArray();
        Assert.True(Image.Identify(partial, FullScan).MayHaveTransparency);

        // Restoring a non-final image to the background shows transparency
        var disposed = new SyntheticImages.GifBuilder().Header(2, 2).GraphicControl(disposal: 2, delay: 5).Image(0, 0, 2, 2).GraphicControl(disposal: 0, delay: 5).Image(0, 0, 1, 1).Trailer().ToArray();
        Assert.True(Image.Identify(disposed, FullScan).MayHaveTransparency);
    }

    [Fact]
    public void PlainTextExtensionsAreUnsupported()
    {
        var data = new SyntheticImages.GifBuilder().Header(2, 2).Extension(0x01, new byte[12]).Image(0, 0, 2, 2).Trailer().ToArray();
        foreach (var options in new[] { ImageIdentifyOptions.Default, FullScan })
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data, options));
            Assert.Equal(ImageFormat.Gif, exception.Format);
            Assert.Equal("GIF plain text extension", exception.Feature);
        }

        // After the first image, only a full scan sees it
        var late = new SyntheticImages.GifBuilder().Header(2, 2).Image(0, 0, 2, 2).Extension(0x01, new byte[12]).Image(0, 0, 2, 2).Trailer().ToArray();
        Assert.Null(Image.Identify(late).FrameCount);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(late, FullScan));
    }

    [Fact]
    public void UnknownAndApplicationExtensionsAreSkipped()
    {
        var data = new SyntheticImages.GifBuilder()
            .Header(2, 2)
            .Extension(0xFF, [.. "XMP DataXMP"u8])
            .Extension(0x77, [1, 2, 3])
            .Image(0, 0, 2, 2)
            .Trailer()
            .ToArray();
        var info = Image.Identify(data, FullScan);
        Assert.Equal(1, info.FrameCount);
        Assert.False(info.IsAnimated);
        Assert.Null(info.Metadata.XmpProfile);
    }

    [Fact]
    public void CommentsBecomeTextEntriesWithinTheMetadataLimit()
    {
        var comment = "Café " + new string('x', 600);
        var data = new SyntheticImages.GifBuilder().Header(2, 2).Comment("first").Image(0, 0, 2, 2).Comment(comment).Trailer().ToArray();
        var header = Image.Identify(data).Metadata;
        Assert.Equal("first", Assert.Single(header.TextEntries).Value);
        var full = Image.Identify(data, FullScan).Metadata;
        Assert.Equal(["first", comment], full.TextEntries.Select(entry => entry.Value));
        Assert.All(full.TextEntries, entry => Assert.Equal("Comment", entry.Keyword));

        var limited = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan, Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = 5 + comment.Length - 1 } } };
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, limited)).Kind);
    }

    public static TheoryData<string, byte[]> InvalidHeaders => new()
    {
        { "empty logical screen", new SyntheticImages.GifBuilder().Header(0, 2).Image(0, 0, 1, 1).Trailer().ToArray() },
        { "unknown block", new SyntheticImages.GifBuilder().Header(2, 2).Raw([0x42]).Image(0, 0, 2, 2).Trailer().ToArray() },
        { "graphic control size", new SyntheticImages.GifBuilder().Header(2, 2).Raw([0x21, 0xF9, 3, 0, 0, 0, 0]).Image(0, 0, 2, 2).Trailer().ToArray() },
        { "no image", new SyntheticImages.GifBuilder().Header(2, 2).Trailer().ToArray() },
    };

    [Theory]
    [MemberData(nameof(InvalidHeaders))]
    public void MalformedHeadersAreInvalidInBothModes(string name, byte[] data)
    {
        Assert.NotNull(name);
        foreach (var options in new[] { ImageIdentifyOptions.Default, FullScan })
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, options));
            Assert.Equal(ImageFormat.Gif, exception.Format);
        }

        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
    }

    public static TheoryData<string, byte[]> InvalidAfterTheFirstImage => new()
    {
        { "missing trailer", new SyntheticImages.GifBuilder().Header(2, 2).Image(0, 0, 2, 2).ToArray() },
        { "truncated sub-block", new SyntheticImages.GifBuilder().Header(2, 2).Image(0, 0, 2, 2).Trailer().ToArray()[..^4] },
        { "LZW code size 0", new SyntheticImages.GifBuilder().Header(2, 2).Image(0, 0, 2, 2, codeSize: 0).Trailer().ToArray() },
        { "LZW code size 12", new SyntheticImages.GifBuilder().Header(2, 2).Image(0, 0, 2, 2, codeSize: 12).Trailer().ToArray() },
        { "garbage after an image", new SyntheticImages.GifBuilder().Header(2, 2).Image(0, 0, 2, 2).Raw([0x00]).Trailer().ToArray() },
    };

    [Theory]
    [MemberData(nameof(InvalidAfterTheFirstImage))]
    public void MalformedBlocksAreFoundByAFullScan(string name, byte[] data)
    {
        Assert.NotNull(name);
        Assert.Null(Image.Identify(data).FrameCount);
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, FullScan));
        Assert.Equal(ImageFormat.Gif, exception.Format);
    }

    [Fact]
    public void FullScanChargesEveryImage()
    {
        var data = SyntheticImages.Gif(2, 2, images: 4);
        var options = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan, Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 4 } } };
        Assert.Equal(4, Image.Identify(data, options).FrameCount);
        var limited = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan, Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 3 } } };
        var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, limited));
        Assert.Equal(ImageResourceLimitKind.Frames, exception.Kind);
        Assert.Equal(4, exception.Requested);
    }

    [Fact]
    public void GifWithoutGlobalColorTableIsStructurallyValid()
    {
        var data = new SyntheticImages.GifBuilder().Header(2, 2, version: "87a", globalColorTable: false).Image(0, 0, 2, 2).Trailer().ToArray();
        Assert.Equal(1, Image.Identify(data, FullScan).FrameCount);
    }
}
