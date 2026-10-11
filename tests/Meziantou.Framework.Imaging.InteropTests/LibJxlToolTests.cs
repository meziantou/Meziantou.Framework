using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Verifies the pinned JPEG XL tools (downloaded from meziantou/prebuilt) and the helpers that read what they write, without
/// the library under test. The behaviors the other tests rely on are pinned here: how frames are returned, how the two
/// reference decoders agree, and what <c>jxlinfo</c> prints.
/// </summary>
public sealed class LibJxlToolTests
{
    [Fact]
    public async Task LibJxlReportsThePinnedVersion()
    {
        var jxl = await RequiredTools.LibJxlAsync();

        // cjxl and djxl report the version the package declares (checked for each of them when they are located)
        Assert.Equal(PrebuiltTools.Cjxl.Version, jxl.Version);
        Assert.Equal(PrebuiltTools.Djxl.Version, jxl.Version);
        Assert.Equal(PrebuiltTools.Jxlinfo.Version, jxl.Version);
        Assert.Equal(PrebuiltTools.JxlFromTree.Version, jxl.Version);
    }

    [Fact]
    public async Task JxlOxideReportsThePinnedVersion()
    {
        var oxide = await RequiredTools.JxlOxideAsync();

        Assert.Equal(PrebuiltTools.JxlOxide.Version, oxide.Version);
    }

    /// <summary>
    /// The plumbing of the JPEG XL references: samples written as a Netpbm file come back, bit for bit, from a lossless
    /// encode by cjxl decoded by djxl (integer Netpbm output and floating-point output) and by jxl-oxide. Distinct values
    /// per channel and pixel detect channel swaps, byte swaps and row-order errors.
    /// </summary>
    [Theory]
    [InlineData("rgb", 3, 255)]
    [InlineData("rgba", 4, 255)]
    [InlineData("gray", 1, 255)]
    [InlineData("graya", 2, 255)]
    [InlineData("rgb", 3, 65535)]
    [InlineData("rgba", 4, 65535)]
    [InlineData("gray", 1, 65535)]
    public async Task LosslessFilesAreReadExactlyByBothDecoders(string name, int channels, int maxValue)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(LosslessFilesAreReadExactlyByBothDecoders));
        var inputPath = directory / $"pattern-5x3-{name}-{maxValue}.pam";
        var jxlPath = directory / $"pattern-5x3-{name}-{maxValue}.jxl";

        const int Width = 5;
        const int Height = 3;
        var samples = new int[Width * Height * channels];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = maxValue == 255 ? ((i * 37) + 11) & 0xFF : ((i * 3701) + 11) & 0xFFFF;
        }

        await File.WriteAllBytesAsync(inputPath, JpegXlPattern.WritePam(Width, Height, channels, maxValue, samples), XunitCancellationToken);
        await jxl.EncodeAsync(inputPath, jxlPath, ["-d", "0", "--keep_invisible=1"], XunitCancellationToken);

        var info = await jxl.GetInfoAsync(jxlPath, XunitCancellationToken);
        Assert.Equal((Width, Height), (info.Width, info.Height));
        Assert.Equal(maxValue == 255 ? 8 : 16, info.BitsPerSample);
        Assert.Equal(0, info.ExponentBitsPerSample);
        Assert.False(info.IsXybEncoded);
        Assert.False(info.IsAnimation);
        Assert.Equal(channels <= 2 ? 1 : 3, info.ColorChannelCount);
        Assert.Equal(channels is 2 or 4, info.HasAlpha);
        Assert.Equal(1, info.Orientation);
        Assert.Null(info.IccProfileLength);
        Assert.Equal("sRGB", info.TransferFunction);
        if (info.HasAlpha)
        {
            Assert.Equal([new JxlInfoExtraChannel("Alpha", info.BitsPerSample, IsAlphaPremultiplied: false)], info.ExtraChannels);
        }

        // Integer output of djxl, at the depth of the file: the PAM reader expands it to RGBA
        var pam = await jxl.DecodeAsync(jxlPath, XunitCancellationToken);
        Assert.Equal((Width, Height, maxValue), (pam.Width, pam.Height, pam.MaxValue));
        var rgba = pam.Rgba.Span;
        for (var pixel = 0; pixel < Width * Height; pixel++)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                var expected = (channels, channel) switch
                {
                    (1 or 3, 3) => maxValue,
                    (2, 3) => samples[(pixel * 2) + 1],
                    (1 or 2, _) => samples[pixel * channels],
                    _ => samples[(pixel * channels) + channel],
                };

                Assert.Equal(expected, rgba[(pixel * 4) + channel], message: $"{name} {maxValue}: pixel {pixel} channel {channel}.");
            }
        }

        // Floating-point output of both decoders: the channels of the file, in order, and integers once scaled
        foreach (var (decoder, frames) in new[] { ("djxl", await jxl.DecodeFramesAsync(jxlPath, XunitCancellationToken)), ("jxl-oxide", await oxide.DecodeFramesAsync(jxlPath, XunitCancellationToken)) })
        {
            Assert.Equal((1, Height, Width, channels), (frames.FrameCount, frames.Height, frames.Width, frames.ChannelCount), message: decoder);
            var decoded = frames.ToIntegerSamples(0, maxValue, out var roundingDistance);
            Assert.Equal(samples, decoded, message: decoder);
            Assert.True(roundingDistance < 0.01, $"{decoder} returned samples that are not integers at this depth (distance {roundingDistance}).");
        }
    }

    /// <summary>
    /// A file written by jxl_from_tree, where every sample is a constant of the description: three coded frames, the second
    /// of which has no duration. Both decoders return two frames of the size of the image, the layer being composed
    /// (<c>kAdd</c>) with what the first frame left; jxlinfo lists the three coded frames with their positions.
    /// </summary>
    [Fact]
    public async Task LayersAreComposedIntoTheDisplayedFrames()
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(LayersAreComposedIntoTheDisplayedFrames));
        var jxlPath = directory / "layers.jxl";

        const string Tree = """
            Width 4
            Height 4
            Bitdepth 8
            Animation
            AnimationFPS 10 1
            Duration 3
            NotLast
            - Set 100
            Width 2
            Height 2
            FramePos 0 0
            BlendMode kAdd
            Duration 0
            NotLast
            - Set 20
            FramePos 2 2
            BlendMode kReplace
            Duration 5
            - Set 200
            """;
        await jxl.EncodeFromTreeAsync(Tree, jxlPath, XunitCancellationToken);

        var info = await jxl.GetInfoAsync(jxlPath, XunitCancellationToken);
        Assert.False(info.IsContainer);
        Assert.True(info.IsAnimation);
        Assert.Equal((4, 4, 8, 3), (info.Width, info.Height, info.BitsPerSample, info.ColorChannelCount));
        Assert.Equal((10, 1, 0), (info.TicksPerSecondNumerator, info.TicksPerSecondDenominator, info.LoopCount));
        Assert.Equal(
            [
                new JxlInfoFrame(IsLayer: false, IsFullSize: true, 4, 4, 0, 0, DurationMilliseconds: 300, Name: null),
                new JxlInfoFrame(IsLayer: true, IsFullSize: false, 2, 2, 0, 0, DurationMilliseconds: 0, Name: null),
                new JxlInfoFrame(IsLayer: false, IsFullSize: false, 2, 2, 2, 2, DurationMilliseconds: 500, Name: null),
            ],
            info.Frames);

        foreach (var (decoder, frames) in new[] { ("djxl", await jxl.DecodeFramesAsync(jxlPath, XunitCancellationToken)), ("jxl-oxide", await oxide.DecodeFramesAsync(jxlPath, XunitCancellationToken)) })
        {
            Assert.Equal((2, 4, 4, 3), (frames.FrameCount, frames.Height, frames.Width, frames.ChannelCount), message: decoder);
            var first = frames.ToIntegerSamples(0, 255, out _);
            var second = frames.ToIntegerSamples(1, 255, out _);
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    var expectedSecond = (x, y) switch
                    {
                        ( < 2, < 2) => 120,
                        ( >= 2, >= 2) => 200,
                        _ => 100,
                    };

                    for (var channel = 0; channel < 3; channel++)
                    {
                        var index = (((y * 4) + x) * 3) + channel;
                        Assert.Equal(100, first[index], message: $"{decoder}: first frame, ({x}, {y}), channel {channel}.");
                        Assert.Equal(expectedSecond, second[index], message: $"{decoder}: second frame, ({x}, {y}), channel {channel}.");
                    }
                }
            }
        }
    }

    /// <summary>Metadata given to cjxl is found by jxlinfo as boxes of a container, compressed or not, around a split codestream.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataBoxesAreListed(bool compressed)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(MetadataBoxesAreListed));
        var inputPath = directory / $"gray-{compressed}.pam";
        var xmpPath = directory / $"packet-{compressed}.xmp";
        var jxlPath = directory / $"metadata-{compressed}.jxl";

        var xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>"u8.ToArray();
        await File.WriteAllBytesAsync(inputPath, JpegXlPattern.WritePam(2, 2, 1, 255, [0, 85, 170, 255]), XunitCancellationToken);
        await File.WriteAllBytesAsync(xmpPath, xmp, XunitCancellationToken);
        await jxl.EncodeAsync(inputPath, jxlPath, ["-d", "0", "-x", "xmp=" + xmpPath, "--compress_boxes=" + (compressed ? "1" : "0")], XunitCancellationToken);

        var info = await jxl.GetInfoAsync(jxlPath, XunitCancellationToken);
        Assert.True(info.IsContainer);
        var metadata = Assert.Single(info.Metadata);
        Assert.Equal(("xml ", compressed), (metadata.Type, metadata.IsBrotliCompressed));
        Assert.Equal(["JXL ", "ftyp"], info.Boxes.Take(2).Select(box => box.Type));
        Assert.Contains(info.Boxes, box => box.Type == (compressed ? "brob" : "xml "));
        Assert.Contains(info.Boxes, box => box.Type is "jxlc" or "jxlp");

        var decoded = await jxl.DecodeAsync(jxlPath, XunitCancellationToken);
        Assert.Equal([0, 85, 170, 255], Enumerable.Range(0, 4).Select(pixel => (int)decoded.Rgba.Span[pixel * 4]));
    }
}
