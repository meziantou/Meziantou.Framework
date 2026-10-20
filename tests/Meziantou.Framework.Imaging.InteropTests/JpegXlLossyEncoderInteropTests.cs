using Meziantou.Framework.Imaging.CorpusGenerator.Common;
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
    /// From the effort 3 each block has its own quantization factor, coarser in texture: the patterns that are texture
    /// everywhere (large, noise, text) are 0.3 to 1.1 dB further from their source than with one factor for the frame.
    /// The patterns are hard on purpose (texture, wrapping gradients, noise), so a low floor is a property of the pattern:
    /// on the two that were compared (photo-rgb16 and large-rgb8), cjxl at its effort 3 and a similar file size is within
    /// 1.5 dB of the library.
    /// </summary>
    [Theory]
    [InlineData("photo-rgb8", 30, 5, false, 29.3)]
    [InlineData("photo-rgb8", 75, 5, false, 32.5)]
    [InlineData("photo-rgb8", 90, 5, false, 34.2)]
    [InlineData("photo-rgb8", 100, 5, false, 40.2)]
    [InlineData("photo-rgb8", 75, 0, false, 31.3)]
    [InlineData("photo-rgb8", 75, 9, false, 33.0)]
    [InlineData("photo-rgb8", 75, 5, true, 32.5)]
    [InlineData("photo-rgba8", 75, 5, false, 32.5)]
    [InlineData("photo-rgba8", 50, 5, true, 30.6)]
    [InlineData("photo-gray8", 75, 5, false, 39.7)]
    [InlineData("photo-gray8", 90, 5, true, 42.5)]
    [InlineData("photo-rgb16", 75, 5, false, 32.6)]
    [InlineData("photo-rgba16", 90, 5, true, 34.3)]
    [InlineData("photo-gray16", 75, 5, false, 39.7)]
    [InlineData("photo-small-rgb16", 75, 5, false, 31.7)]
    [InlineData("photo-small-rgb16", 75, 5, true, 31.7)]
    [InlineData("photo-wide-rgb16", 75, 5, true, 34.0)]
    [InlineData("large-rgb8", 75, 5, false, 16.7)]
    [InlineData("large-rgba8", 60, 5, true, 15.9)]
    [InlineData("text-rgb8", 75, 5, false, 23.5)]
    [InlineData("noise-rgba8", 90, 5, false, 16.8)]
    [InlineData("waves-rgb8", 50, 2, false, 43.9)]
    [InlineData("waves-rgb8", 50, 3, false, 44.1)]
    [InlineData("waves-rgb8", 50, 7, false, 45.1)]
    [InlineData("waves-rgb8", 50, 9, false, 44.9)]
    [InlineData("waves-rgb8", 75, 5, true, 47.0)]
    [InlineData("waves-rgb8", 75, 7, false, 47.0)]
    [InlineData("waves-rgb8", 75, 9, true, 46.9)]
    [InlineData("waves-rgb8", 90, 8, false, 49.4)]
    [InlineData("waves-rgb8", 95, 9, false, 54.0)]
    [InlineData("photo-rgb8", 75, 7, false, 33.0)]
    [InlineData("photo-rgba16", 60, 9, false, 31.8)]
    [InlineData("large-rgb8", 75, 9, true, 16.7)]
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

    /// <summary>
    /// A lossy image labeled with an sRGB profile keeps that profile, byte for byte, and its samples mean the same colors
    /// for the reference decoders. djxl returns the samples of an image stored in XYB with an ICC profile in linear sRGB (a
    /// PNG output is labeled "sRGB Linear"), even when the profile is sRGB: they are compared as sRGB with
    /// <c>--color_space</c>, then as linear sRGB without it. djxl cannot convert to sRGB when the profile has no media white
    /// point (<c>wtpt</c>), which version 4 requires. jxl-oxide converts the samples to the profile. The D65 case has the
    /// media white point of the display, without an adaptation tag, as macOS writes in screenshots and wallpapers, which
    /// version 4 does not allow: djxl and the library read its colorants as adapted to D50, but jxl-oxide adapts them again
    /// from D65 for such a synthetic profile (up to 80 levels of 255), so it is only compared with djxl.
    /// </summary>
    [Theory]
    [InlineData("photo-rgb8", "srgb", false)]
    [InlineData("photo-rgb16", "srgb", false)]
    [InlineData("photo-rgb8", "srgb-d65-white", true)]
    [InlineData("photo-gray8", "gray", false)]
    public async Task ALossyImageWithAnSrgbProfileDecodesLikeTheReferenceDecoders(string patternName, string profileName, bool skipOxide)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var pattern = JpegXlPattern.Get(patternName);
        var curve = IccProfileBuilder.SrgbCurve();
        var profile = profileName switch
        {
            "srgb" => CreateSrgbProfile(D50White),
            "srgb-d65-white" => CreateSrgbProfile(IccProfileBuilder.Xyz(0.95045, 1, 1.08905)),
            "gray" => IccProfileBuilder.BuildBytes("GRAY", "XYZ ", [("wtpt", D50White), ("kTRC", curve)]),
            _ => throw new ArgumentOutOfRangeException(nameof(profileName)),
        };

        var name = $"{patternName}-{profileName}";
        var path = InteropSettings.GetArtifactsDirectory(nameof(ALossyImageWithAnSrgbProfileDecodesLikeTheReferenceDecoders)) / (name + ".jxl");
        using (var source = CreateImage(pattern, rowOffset: 0))
        {
            source.Metadata.IccProfile = new IccProfile(new MetadataBlob(profile));
            await source.SaveAsync(path, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 90 }, XunitCancellationToken);
        }

        var info = await jxl.GetInfoAsync(path, XunitCancellationToken);
        Assert.True(info.IsXybEncoded);
        Assert.Equal(profile.Length, info.IccProfileLength);
        Assert.Equal(profile, await jxl.GetOriginalIccProfileAsync(path, XunitCancellationToken));

        var colorSpace = pattern.Channels >= 3 ? "RGB_D65_SRG_Rel_SRG" : "Gra_D65_Rel_SRG";
        using var decoded = await JpegXlReferenceComparison.AssertDecodesLikeTheReferencesAsync(path, JpegXlReferenceComparison.DjxlTolerance, JpegXlReferenceComparison.Skip, name, colorSpace);
        Assert.Equal(profile, decoded.Metadata.IccProfile?.Data.ToArray());
        var srgb = await jxl.DecodeFramesAsync(path, colorSpace, XunitCancellationToken);
        if (!skipOxide)
        {
            AssertInGamutSamplesMatchOxide(decoded, srgb, await (await RequiredTools.JxlOxideAsync()).DecodeFramesAsync(path, XunitCancellationToken), name);
        }

        // The default output of djxl: the same colors, in linear sRGB
        var linear = await jxl.DecodeFramesAsync(path, XunitCancellationToken);
        var worst = 0.0;
        for (var y = 0; y < linear.Height; y++)
        {
            for (var x = 0; x < linear.Width; x++)
            {
                for (var c = 0; c < linear.ChannelCount; c++)
                {
                    double expected = Math.Clamp(srgb.GetSample(0, x, y, c), 0f, 1f);
                    expected = expected <= 0.04045 ? expected / 12.92 : Math.Pow((expected + 0.055) / 1.055, 2.4);
                    worst = Math.Max(worst, Math.Abs(Math.Clamp(linear.GetSample(0, x, y, c), 0f, 1f) - expected));
                }
            }
        }

        Assert.True(worst <= 0.002, $"{name}: the default output of djxl {jxl.Version} is not the linear sRGB of its sRGB output (distance {worst:F5}).");
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

    private static byte[] D50White => IccProfileBuilder.Xyz(0.9642, 1, 0.8249);

    /// <summary>
    /// Compares the colors of the library with jxl-oxide on the pixels in the gamut of the profile, as djxl decodes them
    /// without clamping. When it converts to an ICC profile, jxl-oxide maps the colors outside the gamut into it, where
    /// djxl and the library clip each channel, and moves the colors close to its boundary by up to 0.0015 (libjxl 0.12.0,
    /// jxl-oxide 0.12.6): the largest distance allows for that, and the mean distance stays within the usual tolerance and
    /// the mean rounding of the library's samples.
    /// </summary>
    private static void AssertInGamutSamplesMatchOxide(Image decoded, NpyFrames djxl, NpyFrames oxide, string context)
    {
        Assert.Equal((djxl.Width, djxl.Height, djxl.ChannelCount), (oxide.Width, oxide.Height, oxide.ChannelCount));
        using var typed = decoded.CloneAs<Rgba64>(new PixelConversionOptions { DiscardIncompatibleColorProfile = true });
        var pixels = new Rgba64[decoded.Width * decoded.Height];
        typed.Frames[0].CopyPixelDataTo(pixels);
        var is8Bit = decoded.PixelFormat is PixelFormat.Gray8 or PixelFormat.Rgb24 or PixelFormat.Rgba32 or PixelFormat.Bgra32;
        var rounding = (0.5 / (is8Bit ? byte.MaxValue : ushort.MaxValue)) + 1e-7;
        var colorChannels = djxl.ChannelCount >= 3 ? 3 : 1;
        var outOfGamut = 0;
        var worst = 0.0;
        var sum = 0.0;
        var count = 0;
        for (var y = 0; y < djxl.Height; y++)
        {
            for (var x = 0; x < djxl.Width; x++)
            {
                var isInGamut = true;
                for (var c = 0; c < colorChannels; c++)
                {
                    isInGamut &= djxl.GetSample(0, x, y, c) is >= 0 and <= 1;
                }

                if (!isInGamut)
                {
                    outOfGamut++;
                    continue;
                }

                var pixel = pixels[(y * djxl.Width) + x];
                for (var c = 0; c < colorChannels; c++)
                {
                    var actual = c switch
                    {
                        0 => pixel.R,
                        1 => pixel.G,
                        _ => pixel.B,
                    };

                    var difference = Math.Abs((actual / 65535.0) - Math.Clamp(oxide.GetSample(0, x, y, c), 0f, 1f));
                    worst = Math.Max(worst, difference);
                    sum += difference;
                    count++;
                }
            }
        }

        Assert.True(outOfGamut <= djxl.Width * djxl.Height / 50, $"{context}: {outOfGamut} pixels are out of gamut in djxl, more than 2% of the image.");
        Assert.True(worst <= 0.002 + rounding, $"{context}: the library and jxl-oxide differ by {worst:F5} on the pixels in gamut.");
        Assert.True(sum / count <= JpegXlReferenceComparison.OxideTolerance + (rounding / 2), $"{context}: the library and jxl-oxide differ by {sum / count:F6} on average on the pixels in gamut.");
    }

    /// <summary>An sRGB display profile of version 4 with the given media white point and no adaptation tag.</summary>
    private static byte[] CreateSrgbProfile(byte[] whitePoint)
    {
        var curve = IccProfileBuilder.SrgbCurve();
        return IccProfileBuilder.BuildBytes(
            "RGB ",
            "XYZ ",
            [
                ("wtpt", whitePoint),
                ("rXYZ", IccProfileBuilder.Xyz(IccProfileBuilder.SrgbColorants[0][0], IccProfileBuilder.SrgbColorants[0][1], IccProfileBuilder.SrgbColorants[0][2])),
                ("gXYZ", IccProfileBuilder.Xyz(IccProfileBuilder.SrgbColorants[1][0], IccProfileBuilder.SrgbColorants[1][1], IccProfileBuilder.SrgbColorants[1][2])),
                ("bXYZ", IccProfileBuilder.Xyz(IccProfileBuilder.SrgbColorants[2][0], IccProfileBuilder.SrgbColorants[2][1], IccProfileBuilder.SrgbColorants[2][2])),
                ("rTRC", curve),
                ("gTRC", curve),
                ("bTRC", curve),
            ]);
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
