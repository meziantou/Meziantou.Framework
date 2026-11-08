using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Imaging.Metadata;
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
        var name = JpegXlPattern.GetFileName(patternName, options);
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

    /// <summary>
    /// Lossless files of floating-point samples (a PFM file with values outside [0, 1]): the library returns the numbers
    /// given to the encoder exactly, as both reference decoders do. cjxl 0.12.0 fails to encode a grayscale PFM file, and
    /// this one at efforts 1 to 3.
    /// </summary>
    [Theory]
    [InlineData("-e 4")]
    [InlineData("-e 7")]
    [InlineData("-e 9")]
    public async Task FloatingPointFilesDecodeToTheNumbersGivenToTheEncoder(string options)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        const int Width = 23;
        const int Height = 17;
        const int Channels = 3;
        var samples = new float[Width * Height * Channels];
        for (var i = 0; i < samples.Length; i++)
        {
            // Negative numbers, numbers above 1 and small numbers, from a few exponents
            samples[i] = (float)(((i * 37 % 101) - 30) / 25.0 * Math.Pow(2, (i % 7) - 3));
        }

        var directory = InteropSettings.GetArtifactsDirectory(nameof(FloatingPointFilesDecodeToTheNumbersGivenToTheEncoder));
        var name = JpegXlPattern.GetFileName("float-rgb", options);
        var inputPath = directory / (name + ".pfm");
        var jxlPath = directory / (name + ".jxl");
        await File.WriteAllBytesAsync(inputPath, WritePfm(Width, Height, Channels, samples), XunitCancellationToken);
        await jxl.EncodeAsync(inputPath, jxlPath, ["-d", "0", .. options.Split(' ')], XunitCancellationToken);

        foreach (var reference in new[] { await jxl.DecodeFramesAsync(jxlPath, XunitCancellationToken), await oxide.DecodeFramesAsync(jxlPath, XunitCancellationToken) })
        {
            Assert.Equal((Height, Width, Channels), (reference.Height, reference.Width, reference.ChannelCount));
            for (var i = 0; i < samples.Length; i++)
            {
                Assert.Equal(samples[i], reference.GetSample(0, i / Channels % Width, i / Channels / Width, i % Channels));
            }
        }

        using var image = Image.Load(await File.ReadAllBytesAsync(jxlPath, XunitCancellationToken));
        Assert.Equal(PixelFormat.RgbaFloat, image.PixelFormat);
        var pixels = new float[Width * Height * 4];
        CopyFloats(image, pixels);
        for (var pixel = 0; pixel < Width * Height; pixel++)
        {
            for (var c = 0; c < 3; c++)
            {
                Assert.Equal(samples[(pixel * Channels) + c], pixels[(pixel * 4) + c]);
            }

            Assert.Equal(1f, pixels[(pixel * 4) + 3]);
        }
    }

    /// <summary>
    /// A lossy file of floating-point samples (XYB, from a PFM file of values beyond [0, 1]): the library keeps the numbers
    /// outside the unit range, close to djxl. The transfer function of the image (sRGB) amplifies the differences of the
    /// floating-point computations of the two decoders on a few samples (up to 0.02, as for the integer samples of lossy
    /// files); on average they are much smaller.
    /// </summary>
    [Fact]
    public async Task LossyFloatingPointFilesKeepNumbersOutsideTheUnitRange()
    {
        var jxl = await RequiredTools.LibJxlAsync();
        const int Width = 40;
        const int Height = 30;
        var samples = new float[Width * Height * 3];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var index = ((y * Width) + x) * 3;
                samples[index] = (4f * x / Width) - 0.5f;
                samples[index + 1] = (float)(Math.Sin((x * 0.3) + (y * 0.2)) * 1.5);
                samples[index + 2] = x * y / 400f;
            }
        }

        var directory = InteropSettings.GetArtifactsDirectory(nameof(LossyFloatingPointFilesKeepNumbersOutsideTheUnitRange));
        var inputPath = directory / "hdr.pfm";
        var jxlPath = directory / "hdr.jxl";
        await File.WriteAllBytesAsync(inputPath, WritePfm(Width, Height, 3, samples), XunitCancellationToken);
        await jxl.EncodeAsync(inputPath, jxlPath, ["-d", "1"], XunitCancellationToken);
        var reference = await jxl.DecodeFramesAsync(jxlPath, XunitCancellationToken);

        using var image = Image.Load(await File.ReadAllBytesAsync(jxlPath, XunitCancellationToken));
        Assert.Equal(PixelFormat.RgbaFloat, image.PixelFormat);
        var pixels = new float[Width * Height * 4];
        CopyFloats(image, pixels);
        var largest = 0.0;
        var sum = 0.0;
        var outside = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    var expected = reference.GetSample(0, x, y, c);
                    var actual = pixels[(((y * Width) + x) * 4) + c];
                    var error = Math.Abs(expected - actual) / Math.Max(1, Math.Abs(expected));
                    largest = Math.Max(largest, error);
                    sum += error;
                    if (actual is < 0 or > 1)
                    {
                        outside++;
                    }
                }
            }
        }

        Assert.True(largest <= 0.02, $"The library is {largest} away from djxl (relative to the samples above 1).");
        Assert.True(sum / (Width * Height * 3) <= 1e-3, $"The library is {sum / (Width * Height * 3)} away from djxl on average.");
        Assert.True(outside > Width * Height, $"Only {outside} samples are outside [0, 1].");
    }

    /// <summary>
    /// Integer samples of 17 to 24 bits (<c>jxl_from_tree</c>): the library returns them as single-precision numbers, which
    /// give each sample back when multiplied by the largest value; both reference decoders agree to the precision of a
    /// single-precision number.
    /// </summary>
    [Theory]
    [InlineData(17)]
    [InlineData(20)]
    [InlineData(24)]
    public async Task IntegerSamplesOfMoreThan16BitsAreKept(int bits)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        var max = (1 << bits) - 1;
        int[] values = [0, 1, max / 3, (max / 2) + 1, max - 1, max];
        var tree = string.Create(CultureInfo.InvariantCulture, $"""
            Width 6
            Height 2
            Bitdepth {bits}
            if x > 4
              - Set {values[5]}
              if x > 3
                - Set {values[4]}
                if x > 2
                  - Set {values[3]}
                  if x > 1
                    - Set {values[2]}
                    if x > 0
                      - Set {values[1]}
                      - Set {values[0]}
            """);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(IntegerSamplesOfMoreThan16BitsAreKept));
        var jxlPath = directory / string.Create(CultureInfo.InvariantCulture, $"int{bits}.jxl");
        await jxl.EncodeFromTreeAsync(tree, jxlPath, XunitCancellationToken);

        using var image = Image.Load(await File.ReadAllBytesAsync(jxlPath, XunitCancellationToken));
        Assert.Equal(PixelFormat.RgbaFloat, image.PixelFormat);
        var pixels = new float[6 * 2 * 4];
        CopyFloats(image, pixels);
        var references = new[] { await jxl.DecodeFramesAsync(jxlPath, XunitCancellationToken), await oxide.DecodeFramesAsync(jxlPath, XunitCancellationToken) };
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    var actual = pixels[(((y * 6) + x) * 4) + c];
                    Assert.Equal(values[x], Math.Round(actual * (double)max, MidpointRounding.AwayFromZero));
                    foreach (var reference in references)
                    {
                        Assert.True(Math.Abs(reference.GetSample(0, x, y, c) - actual) <= 2e-7, $"({x}, {y}): {actual} instead of {reference.GetSample(0, x, y, c)}.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// CMYK files (a PAM file with the tuple types RGB, Black and, optionally, Alpha, and the CMYK profile of the library):
    /// the inks the reference decoders return are the samples given to the encoder; the library converts them with the
    /// profile of the file, which gives the colors of the same conversion applied to these samples exactly, and the colors
    /// of djxl, whose color management system interpolates the lookup tables of the profile otherwise, within 6 levels (4.45
    /// measured).
    /// </summary>
    [Theory]
    [InlineData(8, false)]
    [InlineData(8, true)]
    [InlineData(16, false)]
    [InlineData(16, true)]
    public async Task CmykFilesAreConvertedWithTheProfileOfTheFile(int bits, bool hasAlpha)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        const int Width = 19;
        const int Height = 13;
        var max = (1 << bits) - 1;
        var channels = hasAlpha ? 5 : 4;
        var samples = new int[Width * Height * channels];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    // JPEG XL samples: the largest value is no ink
                    samples[(((y * Width) + x) * channels) + c] = (int)((((x * 53) + (y * 97) + (c * 211) + (x * y * (c + 1))) % 1000) * (long)max / 999);
                }
            }
        }

        var directory = InteropSettings.GetArtifactsDirectory(nameof(CmykFilesAreConvertedWithTheProfileOfTheFile));
        var name = string.Create(CultureInfo.InvariantCulture, $"cmyk{bits}{(hasAlpha ? "a" : "")}");
        var inputPath = directory / (name + ".pam");
        var profilePath = directory / "cmyk.icc";
        var jxlPath = directory / (name + ".jxl");
        await File.WriteAllBytesAsync(inputPath, WriteCmykPam(Width, Height, hasAlpha, max, samples), XunitCancellationToken);
        await File.WriteAllBytesAsync(profilePath, IccProfile.Cgats001CompatibleCmyk.Data.ToArray(), XunitCancellationToken);
        await jxl.EncodeAsync(inputPath, jxlPath, ["-d", "0", "-x", "icc_pathname=" + profilePath], XunitCancellationToken);

        // The inks of both reference decoders are the samples given to the encoder (black after cyan, magenta and yellow)
        foreach (var reference in new[] { await jxl.DecodeFramesAsync(jxlPath, XunitCancellationToken), await oxide.DecodeFramesAsync(jxlPath, XunitCancellationToken) })
        {
            Assert.Equal((Height, Width, channels), (reference.Height, reference.Width, reference.ChannelCount));
            Assert.Equal(samples, reference.ToIntegerSamples(0, max, out _));
        }

        var conversion = new CmykConversionOptions();
        using var image = Image.Load(await File.ReadAllBytesAsync(jxlPath, XunitCancellationToken), new ImageDecodeOptions { CmykConversion = conversion });
        Assert.Equal(bits == 8 ? (hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24) : (hasAlpha ? PixelFormat.Rgba64 : PixelFormat.Rgb48), image.PixelFormat);
        Assert.Equal(conversion.DestinationProfile.Data.ToArray(), image.Metadata.IccProfile!.Data.ToArray());
        using var typed = image.CloneAs<Rgba64>();
        var pixels = new Rgba64[Width * Height];
        typed.Frames[0].CopyPixelDataTo(pixels);

        // The same conversion of the inks (0 is no ink in ICC), at the depth of the file
        var transform = IccColorTransform.Create(IccProfile.Cgats001CompatibleCmyk, conversion.DestinationProfile, conversion.Transform);
        var inks = new ushort[Width * Height * 4];
        var colors = new ushort[Width * Height * 3];
        for (var pixel = 0; pixel < Width * Height; pixel++)
        {
            for (var c = 0; c < 4; c++)
            {
                inks[(pixel * 4) + c] = (ushort)(max - samples[(pixel * channels) + c]);
            }
        }

        if (bits == 8)
        {
            var byteColors = new byte[colors.Length];
            transform.Convert(inks.Select(ink => (byte)ink).ToArray(), byteColors);
            for (var i = 0; i < colors.Length; i++)
            {
                colors[i] = (ushort)(byteColors[i] * 257);
            }
        }
        else
        {
            transform.Convert(inks, colors);
        }

        // The colors of djxl, converted by its own color management system to sRGB
        var djxl = await jxl.DecodeFramesAsync(jxlPath, "RGB_D65_SRG_Rel_SRG", XunitCancellationToken);
        var largest = 0.0;
        for (var pixel = 0; pixel < Width * Height; pixel++)
        {
            var expected = new Rgba64(colors[pixel * 3], colors[(pixel * 3) + 1], colors[(pixel * 3) + 2], hasAlpha ? (ushort)(samples[(pixel * channels) + 4] * 65535L / max) : ushort.MaxValue);
            Assert.Equal(expected, pixels[pixel]);
            for (var c = 0; c < 3; c++)
            {
                var actual = c switch { 0 => pixels[pixel].R, 1 => pixels[pixel].G, _ => pixels[pixel].B } / 65535.0;
                largest = Math.Max(largest, Math.Abs(Math.Clamp(djxl.GetSample(0, pixel % Width, pixel / Width, c), 0, 1) - actual));
            }
        }

        Assert.True(largest <= 6 / 255.0, $"The library is {largest * 255:0.##} levels away from the colors of djxl.");
    }

    /// <summary>Copies the samples of the first frame of an <see cref="PixelFormat.RgbaFloat"/> image.</summary>
    private static void CopyFloats(Image image, float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        image.Frames[0].CopyPixelBytesTo(bytes);
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
        }
    }

    /// <summary>Writes a PFM file: floating-point samples, rows from the bottom, little-endian.</summary>
    private static byte[] WritePfm(int width, int height, int channels, ReadOnlySpan<float> samples)
    {
        var header = System.Text.Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{(channels == 3 ? "PF" : "Pf")}\n{width} {height}\n-1.0\n"));
        var data = new byte[header.Length + (samples.Length * 4)];
        header.CopyTo(data, 0);
        var offset = header.Length;
        for (var y = height - 1; y >= 0; y--)
        {
            foreach (var sample in samples.Slice(y * width * channels, width * channels))
            {
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset), sample);
                offset += 4;
            }
        }

        return data;
    }

    /// <summary>Writes a PAM file of CMYK samples: the tuple types RGB (cyan, magenta, yellow), Black and, optionally, Alpha, which cjxl reads as extra channels.</summary>
    private static byte[] WriteCmykPam(int width, int height, bool hasAlpha, int maxValue, ReadOnlySpan<int> samples)
    {
        var channels = hasAlpha ? 5 : 4;
        var header = System.Text.Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"P7\nWIDTH {width}\nHEIGHT {height}\nDEPTH {channels}\nMAXVAL {maxValue}\nTUPLTYPE RGB\nTUPLTYPE Black\n{(hasAlpha ? "TUPLTYPE Alpha\n" : "")}ENDHDR\n"));
        var bytesPerSample = maxValue > 255 ? 2 : 1;
        var data = new byte[header.Length + (samples.Length * bytesPerSample)];
        header.CopyTo(data, 0);
        for (var i = 0; i < samples.Length; i++)
        {
            if (bytesPerSample == 2)
            {
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(header.Length + (i * 2)), (ushort)samples[i]);
            }
            else
            {
                data[header.Length + i] = (byte)samples[i];
            }
        }

        return data;
    }
}
