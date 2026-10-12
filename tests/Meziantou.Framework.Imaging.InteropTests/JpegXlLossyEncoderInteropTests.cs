using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Lossy and progressive JPEG XL encoder output read by libjxl's <c>djxl</c> and by jxl-oxide, which share no code:
/// <list type="bullet">
/// <item><description>the reference decoders read a lossy file like each other and like the library, within the tolerances of the decoder tests: a file that only the library can read is not a JPEG XL file;</description></item>
/// <item><description>the quality is measured on what djxl decodes, against the source samples, and the alpha channel is exact;</description></item>
/// <item><description>a progressive file holds the pixels of the file written in one pass, and a progressive lossless file holds the source samples.</description></item>
/// </list>
/// </summary>
public sealed class JpegXlLossyEncoderInteropTests
{
    /// <summary>
    /// The floors are measured on the output of djxl 0.12.0, minus a margin of 1 dB: an encoder change that loses more than
    /// that is a regression of the quality scale, which is part of the contract of <see cref="JpegXlEncoder.Quality"/>.
    /// The pattern of waves has what suits each transform the encoder chooses from: with it, the efforts 3, 7 and 8 each
    /// write other transforms than the effort below, up to 64x64 samples, which the reference decoders read like the library.
    /// The patterns are hard on purpose (texture, wrapping gradients, noise), so a low floor is a property of the pattern:
    /// on the two that were compared (photo-rgb16 and large-rgb8), cjxl at its effort 3 and a similar file size is within
    /// 1.5 dB of the library.
    /// </summary>
    [Theory]
    [InlineData("photo-rgb8", 30, 5, false, 29.0)]
    [InlineData("photo-rgb8", 75, 5, false, 31.2)]
    [InlineData("photo-rgb8", 90, 5, false, 33.7)]
    [InlineData("photo-rgb8", 100, 5, false, 39.5)]
    [InlineData("photo-rgb8", 75, 0, false, 30.4)]
    [InlineData("photo-rgb8", 75, 9, false, 31.2)]
    [InlineData("photo-rgb8", 75, 5, true, 31.2)]
    [InlineData("photo-rgba8", 75, 5, false, 31.2)]
    [InlineData("photo-rgba8", 50, 5, true, 29.9)]
    [InlineData("photo-gray8", 75, 5, false, 39.4)]
    [InlineData("photo-gray8", 90, 5, true, 40.9)]
    [InlineData("photo-rgb16", 75, 5, false, 31.3)]
    [InlineData("photo-rgba16", 90, 5, true, 33.7)]
    [InlineData("photo-gray16", 75, 5, false, 39.5)]
    [InlineData("photo-small-rgb16", 75, 5, false, 29.8)]
    [InlineData("photo-small-rgb16", 75, 5, true, 29.8)]
    [InlineData("photo-wide-rgb16", 75, 5, true, 32.8)]
    [InlineData("large-rgb8", 75, 5, false, 17.3)]
    [InlineData("large-rgba8", 60, 5, true, 16.5)]
    [InlineData("text-rgb8", 75, 5, false, 24.9)]
    [InlineData("noise-rgba8", 90, 5, false, 18.9)]
    [InlineData("waves-rgb8", 50, 2, false, 43.8)]
    [InlineData("waves-rgb8", 50, 3, false, 44.8)]
    [InlineData("waves-rgb8", 50, 7, false, 45.3)]
    [InlineData("waves-rgb8", 50, 9, false, 45.2)]
    [InlineData("waves-rgb8", 75, 5, true, 46.1)]
    [InlineData("waves-rgb8", 75, 7, false, 46.1)]
    [InlineData("waves-rgb8", 75, 9, true, 46.1)]
    [InlineData("waves-rgb8", 90, 8, false, 48.8)]
    [InlineData("waves-rgb8", 95, 9, false, 53.1)]
    [InlineData("photo-rgb8", 75, 7, false, 32.2)]
    [InlineData("photo-rgba16", 60, 9, false, 31.3)]
    [InlineData("large-rgb8", 75, 9, true, 18.0)]
    public async Task LossyOutputDecodesLikeTheReferenceDecoders(string patternName, int quality, int effort, bool progressive, double minimumPsnr)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var pattern = JpegXlPattern.Get(patternName);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(LossyOutputDecodesLikeTheReferenceDecoders));
        var name = $"{patternName}-q{quality}-e{effort}{(progressive ? "-progressive" : "")}";
        var path = directory / (name + ".jxl");
        bool storesAlpha;
        using (var source = CreateImage(pattern, rowOffset: 0))
        {
            storesAlpha = source.PixelFormat is PixelFormat.Rgba32 or PixelFormat.Rgba64;
            await source.SaveAsync(path, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = quality, Effort = effort, Progressive = progressive }, XunitCancellationToken);
        }

        var info = await jxl.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((pattern.Width, pattern.Height, pattern.MaxValue == 255 ? 8 : 16, pattern.Channels >= 3 ? 3 : 1, storesAlpha), (info.Width, info.Height, info.BitsPerSample, info.ColorChannelCount, info.HasAlpha));
        Assert.True(info.IsXybEncoded);
        Assert.False(info.IsAnimation);

        using var decoded = await JpegXlReferenceComparison.AssertDecodesLikeTheReferencesAsync(path, JpegXlReferenceComparison.DjxlTolerance, JpegXlReferenceComparison.OxideTolerance, name);

        // The quality, on what the reference decoder shows
        var reference = await jxl.DecodeFramesAsync(path, XunitCancellationToken);
        var psnr = GetPsnr(pattern, reference, frame: 0, rowOffset: 0, out var worstAlpha);
        Assert.True(psnr >= minimumPsnr, $"{name}: djxl {jxl.Version} decodes the file at {psnr:F2} dB of the source, less than {minimumPsnr:F1} dB.");
        Assert.True(worstAlpha < 1e-6, $"{name}: the alpha channel is not exact in djxl {jxl.Version} (distance {worstAlpha}).");
    }

    /// <summary>A better quality is a larger file that is closer to the source, as the reference decoder shows it.</summary>
    [Theory]
    [InlineData("photo-rgb8")]
    [InlineData("photo-gray16")]
    [InlineData("text-rgb8")]
    public async Task ABetterQualityIsCloserToTheSourceInTheReferenceDecoder(string patternName)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var pattern = JpegXlPattern.Get(patternName);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(ABetterQualityIsCloserToTheSourceInTheReferenceDecoder));
        using var source = CreateImage(pattern, rowOffset: 0);
        var previousPsnr = 0.0;
        var previousLength = 0L;
        foreach (var quality in new[] { 10, 30, 50, 70, 85, 95, 100 })
        {
            var path = directory / $"{patternName}-q{quality}.jxl";
            await source.SaveAsync(path, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = quality }, XunitCancellationToken);
            var psnr = GetPsnr(pattern, await jxl.DecodeFramesAsync(path, XunitCancellationToken), frame: 0, rowOffset: 0, out _);
            var length = new FileInfo(path).Length;
            Assert.True(psnr > previousPsnr, $"{patternName}: quality {quality} gives {psnr:F2} dB, not more than the quality below ({previousPsnr:F2} dB).");
            Assert.True(length > previousLength, $"{patternName}: quality {quality} gives {length} bytes, not more than the quality below ({previousLength} bytes).");
            previousPsnr = psnr;
            previousLength = length;
        }
    }

    /// <summary>
    /// A progressive lossy file distributes the coefficients of the single-pass file over passes: both reference decoders
    /// return the same samples for the two files, bit for bit.
    /// </summary>
    [Theory]
    [InlineData("photo-rgb8", 75)]
    [InlineData("photo-rgba16", 90)]
    [InlineData("photo-gray8", 40)]
    [InlineData("photo-wide-rgb16", 75)]
    public async Task AProgressiveLossyFileHoldsThePixelsOfTheSinglePassFile(string patternName, int quality)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        var pattern = JpegXlPattern.Get(patternName);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(AProgressiveLossyFileHoldsThePixelsOfTheSinglePassFile));
        var singlePath = directory / $"{patternName}-q{quality}-single.jxl";
        var progressivePath = directory / $"{patternName}-q{quality}-progressive.jxl";
        using (var source = CreateImage(pattern, rowOffset: 0))
        {
            await source.SaveAsync(singlePath, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = quality }, XunitCancellationToken);
            await source.SaveAsync(progressivePath, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = quality, Progressive = true }, XunitCancellationToken);
        }

        Assert.NotEqual(await File.ReadAllBytesAsync(singlePath, XunitCancellationToken), await File.ReadAllBytesAsync(progressivePath, XunitCancellationToken));
        AssertSameSamples(await jxl.DecodeFramesAsync(singlePath, XunitCancellationToken), await jxl.DecodeFramesAsync(progressivePath, XunitCancellationToken), $"{patternName}: djxl {jxl.Version}");
        AssertSameSamples(await oxide.DecodeFramesAsync(singlePath, XunitCancellationToken), await oxide.DecodeFramesAsync(progressivePath, XunitCancellationToken), $"{patternName}: jxl-oxide {oxide.Version}");
    }

    /// <summary>
    /// A progressive lossless file (the samples are squeezed into a pyramid of resolutions) holds exactly the source
    /// samples, for both reference decoders, at every effort.
    /// </summary>
    [Theory]
    [InlineData("smooth-rgb8", 0)]
    [InlineData("smooth-rgb8", 7)]
    [InlineData("smooth-gray8", 3)]
    [InlineData("smooth-gray16", 5)]
    [InlineData("smooth-rgba16", 9)]
    [InlineData("noise-rgba8", 5)]
    [InlineData("palette-rgb8", 5)]
    [InlineData("large-rgb8", 2)]
    [InlineData("large-rgba8", 5)]
    [InlineData("photo-rgba16", 5)]
    [InlineData("photo-wide-rgb16", 1)]
    public async Task AProgressiveLosslessFileDecodesExactlyInTheReferenceDecoders(string patternName, int effort)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        var pattern = JpegXlPattern.Get(patternName);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(AProgressiveLosslessFileDecodesExactlyInTheReferenceDecoders));
        var path = directory / $"{patternName}-e{effort}.jxl";
        using (var source = CreateImage(pattern, rowOffset: 0))
        {
            await source.SaveAsync(path, new JpegXlEncoder { Progressive = true, Effort = effort }, XunitCancellationToken);
        }

        var info = await jxl.GetInfoAsync(path, XunitCancellationToken);
        Assert.False(info.IsXybEncoded);
        Assert.Equal((pattern.Width, pattern.Height, pattern.MaxValue == 255 ? 8 : 16), (info.Width, info.Height, info.BitsPerSample));

        var context = $"{patternName} (effort {effort})";
        AssertExactSamples(pattern, await jxl.DecodeFramesAsync(path, XunitCancellationToken), $"{context}: djxl {jxl.Version}");
        AssertExactSamples(pattern, await oxide.DecodeFramesAsync(path, XunitCancellationToken), $"{context}: jxl-oxide {oxide.Version}");
    }

    /// <summary>Every frame of a lossy animation is a full frame: the reference decoders show the frames the library shows, with their durations.</summary>
    [Theory]
    [InlineData("photo-rgba8", false, 32.0)]
    [InlineData("photo-rgb8", true, 32.0)]
    [InlineData("photo-gray16", false, 39.5)]
    public async Task LossyAnimationsDecodeLikeTheReferenceDecoders(string patternName, bool progressive, double minimumPsnr)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var pattern = JpegXlPattern.Get(patternName);
        var path = InteropSettings.GetArtifactsDirectory(nameof(LossyAnimationsDecodeLikeTheReferenceDecoders)) / $"{patternName}{(progressive ? "-progressive" : "")}.jxl";
        int[] milliseconds = [100, 40, 250];
        using (var animation = CreateImage(pattern, rowOffset: 0))
        {
            for (var i = 1; i < milliseconds.Length; i++)
            {
                using var frame = CreateImage(pattern, rowOffset: i * 37);
                animation.AppendFrame(frame.Frames[0]);
            }

            for (var i = 0; i < milliseconds.Length; i++)
            {
                animation.Frames[i].Metadata.Duration = new FrameDuration(milliseconds[i], 1000);
            }

            animation.Animation = new AnimationMetadata { TotalPlays = 3 };
            await animation.SaveAsync(path, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 80, Progressive = progressive }, XunitCancellationToken);
        }

        var info = await jxl.GetInfoAsync(path, XunitCancellationToken);
        Assert.True(info.IsAnimation);
        Assert.True(info.IsXybEncoded);
        Assert.Equal(3, info.LoopCount);
        Assert.Equal(milliseconds.Select(value => (double)value), info.Frames.Select(frame => frame.DurationMilliseconds ?? -1));

        using var decoded = await JpegXlReferenceComparison.AssertDecodesLikeTheReferencesAsync(path, JpegXlReferenceComparison.DjxlTolerance, JpegXlReferenceComparison.OxideTolerance, patternName);
        Assert.Equal(milliseconds.Length, decoded.Frames.Count);

        // Every frame is the frame that was written, not a copy of the first one
        var reference = await jxl.DecodeFramesAsync(path, XunitCancellationToken);
        for (var i = 0; i < milliseconds.Length; i++)
        {
            var psnr = GetPsnr(pattern, reference, i, rowOffset: i * 37, out _);
            Assert.True(psnr >= minimumPsnr, $"{patternName}: frame {i} is at {psnr:F2} dB of its source in djxl {jxl.Version}, less than {minimumPsnr:F1} dB.");
        }
    }

    /// <summary>Computes the peak signal-to-noise ratio of the color channels of a decoded frame against the samples of the pattern.</summary>
    private static double GetPsnr(JpegXlPattern pattern, NpyFrames reference, int frame, int rowOffset, out double worstAlpha)
    {
        Assert.Equal((pattern.Width, pattern.Height), (reference.Width, reference.Height));
        var colorChannels = pattern.Channels >= 3 ? 3 : 1;
        Assert.True(reference.ChannelCount >= colorChannels);
        var sum = 0.0;
        worstAlpha = 0;
        for (var y = 0; y < pattern.Height; y++)
        {
            var row = (y + rowOffset) % pattern.Height * pattern.Width * pattern.Channels;
            for (var x = 0; x < pattern.Width; x++)
            {
                for (var c = 0; c < colorChannels; c++)
                {
                    var difference = Math.Clamp(reference.GetSample(frame, x, y, c), 0f, 1f) - ((double)pattern.Samples[row + (x * pattern.Channels) + c] / pattern.MaxValue);
                    sum += difference * difference;
                }

                if (reference.ChannelCount > colorChannels)
                {
                    // An image that is stored with an alpha channel it did not have is opaque
                    var expected = pattern.HasAlpha ? (double)pattern.Samples[row + (x * pattern.Channels) + colorChannels] / pattern.MaxValue : 1.0;
                    worstAlpha = Math.Max(worstAlpha, Math.Abs(reference.GetSample(frame, x, y, colorChannels) - expected));
                }
            }
        }

        var mse = sum / ((double)pattern.Width * pattern.Height * colorChannels);
        return mse == 0 ? double.PositiveInfinity : -10 * Math.Log10(mse);
    }

    private static void AssertSameSamples(NpyFrames expected, NpyFrames actual, string context)
    {
        Assert.Equal((expected.FrameCount, expected.Width, expected.Height, expected.ChannelCount), (actual.FrameCount, actual.Width, actual.Height, actual.ChannelCount));
        for (var frame = 0; frame < expected.FrameCount; frame++)
        {
            for (var y = 0; y < expected.Height; y++)
            {
                for (var x = 0; x < expected.Width; x++)
                {
                    for (var c = 0; c < expected.ChannelCount; c++)
                    {
                        if (BitConverter.SingleToInt32Bits(expected.GetSample(frame, x, y, c)) != BitConverter.SingleToInt32Bits(actual.GetSample(frame, x, y, c)))
                        {
                            Assert.Fail($"{context}: the progressive file differs at ({x}, {y}), channel {c}: {actual.GetSample(frame, x, y, c):R} instead of {expected.GetSample(frame, x, y, c):R}.");
                        }
                    }
                }
            }
        }
    }

    private static void AssertExactSamples(JpegXlPattern pattern, NpyFrames reference, string context)
    {
        Assert.Equal((1, pattern.Width, pattern.Height), (reference.FrameCount, reference.Width, reference.Height));
        var samples = reference.ToIntegerSamples(0, pattern.MaxValue, out var distance);
        Assert.True(distance < 0.01, $"{context} returned samples that are not integers at this depth (distance {distance}).");
        var colorChannels = pattern.Channels >= 3 ? 3 : 1;
        for (var y = 0; y < pattern.Height; y++)
        {
            for (var x = 0; x < pattern.Width; x++)
            {
                var source = ((y * pattern.Width) + x) * pattern.Channels;
                var actual = ((y * pattern.Width) + x) * reference.ChannelCount;
                for (var c = 0; c < reference.ChannelCount; c++)
                {
                    // An image that is stored with an alpha channel it did not have is opaque
                    var expected = c < pattern.Channels ? pattern.Samples[source + c] : pattern.MaxValue;
                    if (samples[actual + c] != expected)
                    {
                        Assert.Fail($"{context} decodes {samples[actual + c]} instead of {expected} at ({x}, {y}), channel {c} of {reference.ChannelCount} ({colorChannels} of color).");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Creates an image from a pattern of 8 or 16 bits, its rows rotated by an offset so that frames differ. The library has
    /// no 16-bit format without alpha: such a pattern is stored opaque.
    /// </summary>
    private static Image CreateImage(JpegXlPattern pattern, int rowOffset)
    {
        var rowLength = pattern.Width * pattern.Channels;
        if (pattern.MaxValue == 255)
        {
            var bytes = new byte[pattern.Samples.Length];
            for (var y = 0; y < pattern.Height; y++)
            {
                var source = (y + rowOffset) % pattern.Height * rowLength;
                for (var i = 0; i < rowLength; i++)
                {
                    bytes[(y * rowLength) + i] = (byte)pattern.Samples[source + i];
                }
            }

            return pattern.Channels switch
            {
                1 => Image.ImportPixelBytes<Gray8>(bytes, pattern.Width, pattern.Height),
                3 => Image.ImportPixelBytes<Rgb24>(bytes, pattern.Width, pattern.Height),
                4 => Image.ImportPixelBytes<Rgba32>(bytes, pattern.Width, pattern.Height),
                _ => throw new ArgumentException("The pattern has no pixel format.", nameof(pattern)),
            };
        }

        Assert.Equal(65535, pattern.MaxValue);
        var storedChannels = pattern.Channels == 1 ? 1 : 4;
        var samples = new ushort[pattern.Width * pattern.Height * storedChannels];
        for (var y = 0; y < pattern.Height; y++)
        {
            var source = (y + rowOffset) % pattern.Height * rowLength;
            for (var x = 0; x < pattern.Width; x++)
            {
                for (var c = 0; c < storedChannels; c++)
                {
                    samples[(((y * pattern.Width) + x) * storedChannels) + c] = c < pattern.Channels ? (ushort)pattern.Samples[source + (x * pattern.Channels) + c] : ushort.MaxValue;
                }
            }
        }

        // The samples of the 16-bit formats are stored in the endianness of the machine, as the array is
        var data = new byte[samples.Length * sizeof(ushort)];
        Buffer.BlockCopy(samples, 0, data, 0, data.Length);
        return pattern.Channels switch
        {
            1 => Image.ImportPixelBytes<Gray16>(data, pattern.Width, pattern.Height),
            3 or 4 => Image.ImportPixelBytes<Rgba64>(data, pattern.Width, pattern.Height),
            _ => throw new ArgumentException("The pattern has no pixel format.", nameof(pattern)),
        };
    }
}
