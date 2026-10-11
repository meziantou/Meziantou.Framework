using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Differential tests of the JPEG XL decoder: files written at test time by the pinned reference encoder, over a matrix
/// of its options, are decoded by the library and by two reference decoders that share no code (libjxl's <c>djxl</c> and
/// jxl-oxide). The coding tools of the format are reached through the options; no file is committed.
/// </summary>
/// <remarks>
/// For lossless files the expectation is the image given to the encoder: the library and both reference decoders must
/// return it exactly, the low bits of 16-bit samples included.
/// </remarks>
public sealed class JpegXlDecoderDifferentialTests
{
    /// <summary>
    /// Lossless encodings: the options select the effort (which chooses the entropy coder, the tree and the transforms),
    /// each predictor, each reversible color transform, the palette, the squeeze transform, the size of the groups and their
    /// order, and the container.
    /// </summary>
    public static TheoryData<string, string> LosslessCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var pattern in new[] { "smooth-rgb8", "noise-rgba8" })
            {
                foreach (var effort in new[] { 1, 2, 3, 4, 7, 9 })
                {
                    data.Add(pattern, $"-e {effort}");
                }
            }

            foreach (var pattern in new[] { "smooth-gray8", "smooth-graya8", "smooth-rgba16", "smooth-gray16", "palette-rgb8", "smooth-gray10", "smooth-rgb12" })
            {
                foreach (var effort in new[] { 1, 3, 7 })
                {
                    data.Add(pattern, $"-e {effort}");
                }
            }

            // -P: the predictor of every sample (6 is the weighted predictor, 14 and 15 let the encoder choose among several)
            for (var predictor = 0; predictor <= 15; predictor++)
            {
                data.Add("smooth-rgb8", $"-e 5 -I 0 -P {predictor}");
            }

            // -C: the reversible color transform (7 types and 6 permutations)
            foreach (var transform in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 13, 19, 25, 31, 37, 41 })
            {
                data.Add("smooth-rgb8", $"-e 5 -C {transform}");
            }

            // -R 1: the squeeze transform, whose channels of low resolution are coded in other sections
            foreach (var pattern in new[] { "smooth-rgb8", "noise-rgba8", "smooth-rgba16", "smooth-gray8", "large-rgb8" })
            {
                data.Add(pattern, "-R 1 -e 3");
            }

            data.Add("palette-rgb8", "--modular_palette_colors=0");
            data.Add("palette-rgb8", "-e 7 -X 100 -Y 100");
            data.Add("noise-rgba8", "-e 7 -E 3");

            // -g: the size of the groups, from 128 to 1024 samples; a large image is split in groups, each with its own section
            for (var groupSize = 0; groupSize <= 3; groupSize++)
            {
                data.Add("large-rgb8", $"-e 2 -g {groupSize}");
                data.Add("large-rgba8", $"-e 3 -g {groupSize}");
            }

            data.Add("large-rgb8", "-e 7");
            data.Add("large-rgba8", "-e 2 --group_order=1");
            data.Add("large-rgb8", "-e 3 -p");
            data.Add("smooth-rgb8", "--container=1");
            data.Add("smooth-rgb8", "--codestream_level=10");
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LosslessCases))]
    public async Task LosslessFilesDecodeToTheImageGivenToTheEncoder(string patternName, string options)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        var pattern = JpegXlPattern.Get(patternName);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(LosslessFilesDecodeToTheImageGivenToTheEncoder));
        var name = $"{patternName}{options.Replace(' ', '_').Replace('=', '_')}";
        var inputPath = directory / (name + ".pam");
        var jxlPath = directory / (name + ".jxl");
        await File.WriteAllBytesAsync(inputPath, pattern.ToPam(), XunitCancellationToken);

        // Patches are a separate coding tool, tested on its own; fully transparent pixels keep their color
        await jxl.EncodeAsync(inputPath, jxlPath, ["-d", "0", "--patches=0", "--keep_invisible=1", .. options.Split(' ')], XunitCancellationToken);

        // The two reference decoders return the image given to the encoder: the file is lossless and they agree
        var reference = await jxl.DecodeAsync(jxlPath, XunitCancellationToken);
        Assert.Equal((pattern.Width, pattern.Height, pattern.MaxValue), (reference.Width, reference.Height, reference.MaxValue));
        var independent = await oxide.DecodeFramesAsync(jxlPath, XunitCancellationToken);
        Assert.Equal((1, pattern.Height, pattern.Width, pattern.Channels), (independent.FrameCount, independent.Height, independent.Width, independent.ChannelCount));
        Assert.Equal(pattern.Samples, independent.ToIntegerSamples(0, pattern.MaxValue, out var roundingDistance));
        Assert.True(roundingDistance < 0.01, $"jxl-oxide returned samples that are not integers (distance {roundingDistance}).");

        var data = await File.ReadAllBytesAsync(jxlPath, XunitCancellationToken);
        using var image = Image.Load(data);
        Assert.Equal((pattern.Width, pattern.Height, 1), (image.Width, image.Height, image.Frames.Count));
        using var typed = image.CloneAs<Rgba64>();
        var pixels = new Rgba64[pattern.Width * pattern.Height];
        typed.Frames[0].CopyPixelDataTo(pixels);
        var referencePixels = reference.Rgba.Span;
        Assert.HasCount(pixels.Length * 4, referencePixels);
        for (var y = 0; y < pattern.Height; y++)
        {
            for (var x = 0; x < pattern.Width; x++)
            {
                var index = (y * pattern.Width) + x;
                var expected = pattern.GetExpectedPixel(x, y);
                var fromReference = new Rgba64(Scale(referencePixels[index * 4]), Scale(referencePixels[(index * 4) + 1]), Scale(referencePixels[(index * 4) + 2]), Scale(referencePixels[(index * 4) + 3]));
                if (fromReference != expected)
                {
                    Assert.Fail($"djxl does not return the image given to cjxl at ({x}, {y}): {fromReference} instead of {expected}.");
                }

                if (pixels[index] != expected)
                {
                    Assert.Fail($"The library returns {pixels[index]} at ({x}, {y}); the image given to the encoder has {expected} ({patternName}, {options}).");
                }
            }
        }

        // The PAM reader scales the samples of djxl, which have the depth of the file, to 8 bits (up to 8) or 16 bits, exactly
        ushort Scale(ushort sample) => reference.Is16Bit ? sample : (ushort)(sample * 257);
    }
}
