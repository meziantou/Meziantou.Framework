using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.Tests.Codecs;
using Meziantou.Framework.Imaging.TestHarness.Pnm;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Differential tests of the JPEG decoder against the ISO JPEG reference codec (<see cref="IsoJpegTool"/>), which shares no
/// code with libjpeg-turbo: files written at test time by its encoder, 8-bit and 12-bit, in every color model and with
/// Huffman and arithmetic coding, are decoded by the library and by its decoder. No file is committed.
/// </summary>
/// <remarks>
/// <para>
/// The encoder quantizes the same way whatever the process and the entropy coding, so every file of a case holds the same
/// coefficients: the library must decode them all to the pixels of the Huffman-coded sequential one, exactly. This checks
/// the arithmetic decoder and the progressive processes against an encoder that is not libjpeg-turbo, independently of how
/// the samples are reconstructed.
/// </para>
/// <para>
/// The reconstruction is then compared with the reference decoder, within the differences of two inverse DCTs, two color
/// conversions and two chroma upsampling filters. Pixels where a channel of either decoding is at an end of the range are
/// left out: the reference decoder clamps after the color conversion, where the library clamps the samples first, so
/// colors outside the gamut legitimately differ by more.
/// </para>
/// </remarks>
public sealed class JpegDecoderDifferentialTests
{
    private const int Width = 61;
    private const int Height = 37;

    /// <summary>
    /// The largest difference allowed with the reference decoder, in units of the sample precision, and the largest mean
    /// difference: the measured maximum plus one unit, and 1.5 times the measured mean plus 0.1. The chroma upsampling
    /// filters differ most, on the edges of the pattern.
    /// </summary>
    private static readonly Dictionary<(string Layout, int Precision), (int Max, double Mean)> Tolerances = new()
    {
        [("gray", 8)] = (2, 0.15),
        [("gray", 12)] = (3, 0.3),
        [("rgb", 8)] = (2, 0.15),
        [("rgb", 12)] = (4, 0.35),
        [("444", 8)] = (3, 0.7),
        [("444", 12)] = (5, 0.8),
        [("422", 8)] = (12, 1),
        [("422", 12)] = (181, 3.3),
        [("420", 8)] = (10, 0.9),
        [("420", 12)] = (116, 2.7),
    };

    /// <summary>Gets a DAC segment without any table: the marker and a length of 2 (the bytes FF CC never occur in entropy-coded data).</summary>
    private static ReadOnlySpan<byte> EmptyConditioningSegment => [0xFF, 0xCC, 0x00, 0x02];

    public static TheoryData<string, int, int> Cases
    {
        get
        {
            var data = new TheoryData<string, int, int>();
            foreach (var layout in new[] { "gray", "rgb", "444", "422", "420" })
            {
                foreach (var precision in new[] { 8, 12 })
                {
                    foreach (var quality in new[] { 50, 90 })
                    {
                        data.Add(layout, precision, quality);
                    }
                }
            }

            return data;
        }
    }

    [Fact]
    public async Task ReferenceCodecRunsAndFailsLoudly()
    {
        var isoJpeg = await RequiredTools.IsoJpegAsync();
        Assert.True(File.Exists(isoJpeg.ToolPath));
        Assert.False(string.IsNullOrEmpty(IsoJpegTool.Version));

        // The tool exits with 0 when a conversion fails: the wrapper still reports the failure
        var directory = InteropSettings.GetArtifactsDirectory(nameof(ReferenceCodecRunsAndFailsLoudly));
        var notJpeg = directory / "not-a-jpeg.jpg";
        await File.WriteAllBytesAsync(notJpeg, "not a JPEG file"u8.ToArray(), XunitCancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => isoJpeg.DecodeAsync(notJpeg, XunitCancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => isoJpeg.EncodeAsync(directory / "missing.ppm", directory / "missing.jpg", ["-q", "85"], XunitCancellationToken));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FilesOfTheReferenceEncoderDecodeLikeTheReferenceDecoder(string layout, int precision, int quality)
    {
        var isoJpeg = await RequiredTools.IsoJpegAsync();
        var maxValue = (1 << precision) - 1;
        var channels = layout == "gray" ? 1 : 3;
        string[] layoutOptions = layout switch
        {
            "rgb" => ["-c"],
            "422" => ["-s", "1x1,2x1,2x1"],
            "420" => ["-s", "1x1,2x2,2x2"],
            _ => [],
        };

        var directory = InteropSettings.GetArtifactsDirectory(nameof(FilesOfTheReferenceEncoderDecodeLikeTheReferenceDecoder));
        var name = string.Create(CultureInfo.InvariantCulture, $"{layout}-{precision}bit-q{quality}");
        var inputPath = directory / (name + (channels == 1 ? ".pgm" : ".ppm"));
        await File.WriteAllBytesAsync(inputPath, WritePnm(Width, Height, channels, maxValue, CreateSamples(Width, Height, channels, maxValue)), XunitCancellationToken);

        // The Huffman-coded sequential file first (extended sequential, SOF1, with optimized tables), then the processes
        // the library must decode to the same pixels
        var variants = new (string Name, string[] Options, byte FrameMarker)[]
        {
            ("huffman", ["-h"], 0xC1),
            ("huffman-progressive", ["-v"], 0xC2),
            ("arithmetic", ["-a"], 0xC9),
            ("arithmetic-restart", ["-a", "-z", "2"], 0xC9),
            ("arithmetic-progressive", ["-a", "-v"], 0xCA),
            ("arithmetic-progressive-restart", ["-a", "-v", "-z", "3"], 0xCA),
            ("arithmetic-spectral-selection", ["-a", "-v", "-qv"], 0xCA),
        };

        int[]? expected = null;
        foreach (var (variantName, options, frameMarker) in variants)
        {
            var path = directory / (name + "-" + variantName + ".jpg");
            await isoJpeg.EncodeAsync(inputPath, path, ["-q", quality.ToString(CultureInfo.InvariantCulture), .. layoutOptions, .. options], XunitCancellationToken);
            var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
            var frame = FindFrameHeader(data);
            Assert.Equal((frameMarker, precision, Width, Height, channels), (data[frame + 1], (int)data[frame + 4], BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(frame + 7)), BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(frame + 5)), (int)data[frame + 9]));
            if (variantName == "arithmetic-progressive")
            {
                // The refinement scans of its progression use no conditioning: a DAC segment without any table precedes them
                Assert.True(data.AsSpan().IndexOf(EmptyConditioningSegment) >= 0, "The reference encoder no longer writes a DAC segment without any table.");
            }

            Assert.Equal(precision, Image.Identify(data).BitsPerComponent);
            using var image = Image.Load(data);
            var samples = GetSamples(image, maxValue);
            if (expected is null)
            {
                expected = samples;
                var reference = await isoJpeg.DecodeAsync(path, XunitCancellationToken);
                Assert.Equal((Width, Height, maxValue), (reference.Width, reference.Height, reference.MaxValue));
                var (max, mean, excluded) = Compare(samples, GetSamples(reference, maxValue), maxValue);
                var tolerance = Tolerances[(layout, precision)];
                var description = string.Create(CultureInfo.InvariantCulture, $"{name}: max {max}, mean {mean:F4} (allowed {tolerance.Max}, {tolerance.Mean}), {excluded} pixels at an end of the range left out");
                TestContext.Current.TestOutputHelper?.WriteLine(description);
                Assert.True(max <= tolerance.Max && mean <= tolerance.Mean && excluded <= Width * Height / 10, description);
            }
            else if (!samples.AsSpan().SequenceEqual(expected))
            {
                var index = samples.AsSpan().CommonPrefixLength(expected);
                Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"{name}-{variantName}: the library returns {samples[index]} for sample {index % 3} of pixel ({index / 3 % Width}, {index / 3 / Width}); the Huffman-coded file, which has the same coefficients, decodes to {expected[index]}."));
            }
        }
    }

    public static TheoryData<int, bool> ArithmeticLosslessCases
    {
        get
        {
            var data = new TheoryData<int, bool>();
            foreach (var precision in new[] { 8, 10, 12, 16 })
            {
                data.Add(precision, false);
                data.Add(precision, true);
            }

            return data;
        }
    }

    /// <summary>
    /// The lossless process with arithmetic coding (SOF11): no tool at hand writes it (the encoder of the reference codec
    /// fails on lossless frames), so the files are written by the test-side encoder (<see cref="JpegLosslessTestImage"/>),
    /// and the reference decoder must read them back to the samples given to that encoder, like the library. The point
    /// transform is left out: the reference decoder predicts the first sample of a scan with <c>2^(P - 1)</c> instead of
    /// <c>2^(P - Pt - 1)</c> (T.81 H.1.2.1), which libjpeg-turbo and the library use.
    /// </summary>
    [Theory]
    [MemberData(nameof(ArithmeticLosslessCases))]
    public async Task ArithmeticLosslessFilesDecodeToTheirSamplesInTheReferenceDecoder(int precision, bool rgb)
    {
        var isoJpeg = await RequiredTools.IsoJpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(ArithmeticLosslessFilesDecodeToTheirSamplesInTheReferenceDecoder));
        var maxValue = (1 << precision) - 1;
        (int H, int V)[] sampling = rgb ? [(1, 1), (1, 1), (1, 1)] : [(1, 1)];
        var image = JpegLosslessTestImage.CreateRandom(29, 13, sampling, precision, seed: precision * (rgb ? 3 : 1), rgb);
        var stored = image.GetStoredSamples();
        var expected = rgb ? stored : [.. stored.SelectMany(sample => new[] { sample, sample, sample })];
        int[] all = [.. Enumerable.Range(0, sampling.Length)];
        var encodings = new (string Name, JpegLosslessTestOptions Options)[]
        {
            ("p1", new JpegLosslessTestOptions { Arithmetic = true }),
            ("p4-restart", new JpegLosslessTestOptions { Arithmetic = true, Predictor = 4, RestartRows = 2 }),
            ("p5-conditioning", new JpegLosslessTestOptions { Arithmetic = true, Predictor = 5, Conditioning = [(2, 5), (0, 0)], TablePerComponent = true }),
            ("p6-zeros-omitted", new JpegLosslessTestOptions { Arithmetic = true, Predictor = 6, RestartRows = 1, DiscardFinalZeros = true, Conditioning = [(1, 15), (3, 3)] }),
            ("p7-separate-scans", new JpegLosslessTestOptions { Arithmetic = true, Scans = [.. all.Select(index => new JpegLosslessTestScan([index], 7 - (2 * index)))] }),
        };

        foreach (var (name, options) in encodings)
        {
            var path = directory / string.Create(CultureInfo.InvariantCulture, $"sof11-{precision}-{(rgb ? "rgb" : "gray")}-{name}.jpg");
            var data = image.Encode(options);
            await File.WriteAllBytesAsync(path, data, XunitCancellationToken);
            Assert.Equal(0xCB, data[FindFrameHeader(data) + 1]);

            // Without -c, the reference decoder converts three components from YCbCr: they are RGB samples here
            var reference = await isoJpeg.DecodeAsync(path, ["-c"], XunitCancellationToken);
            Assert.Equal((29, 13, maxValue), (reference.Width, reference.Height, reference.MaxValue));
            Assert.Equal(expected, GetSamples(reference, maxValue));

            using var decoded = Image.Load(data);
            Assert.Equal(expected, GetSamples(decoded, maxValue));
        }
    }

    /// <summary>
    /// Hierarchical files of the reference encoder (<c>-y N</c>: <c>N</c> frames of halved sizes, or with 0 a DCT-based frame
    /// and a lossless differential frame of the same size, <c>-y 1</c> with an expanded reference), with Huffman (optimized
    /// tables, <c>-h</c>) and arithmetic coding, sequential and progressive, decoded by the library and by the reference
    /// decoder. They differ by rounding only: the library reconstructs each frame in integers as T.81 J.1 describes (inverse
    /// DCT rounded and clamped, then expanded), the reference decoder keeps four fractional bits and unclamped values from one
    /// frame to the next. For the same reason, a lossless differential frame after a DCT-based frame gives back the source
    /// samples with the inverse DCT of the reference software only (T.81 does not specify the inverse DCT exactly).
    /// </summary>
    [Theory]
    [InlineData("gray", 8, "-y 2 -h", 2)]
    [InlineData("gray", 8, "-y 3 -h", 3)]
    [InlineData("gray", 12, "-y 2 -a", 3)]
    [InlineData("rgb", 8, "-y 2 -h", 4)]
    [InlineData("rgb", 8, "-y 2 -h -z 2", 4)]
    [InlineData("rgb", 8, "-y 2 -a", 4)]
    [InlineData("rgb", 8, "-y 2 -v -h", 4)]
    [InlineData("rgb", 8, "-y 2 -a -v", 4)]
    [InlineData("420", 8, "-y 2 -h", 10)]
    [InlineData("rgb", 12, "-y 2 -h", 6)]
    [InlineData("gray", 8, "-y 0 -c -h", 8)]
    [InlineData("gray", 8, "-y 1 -c -h", 2)]
    [InlineData("rgb", 8, "-y 0 -c -a", 10)]
    [InlineData("rgb", 8, "-y 1 -c -h", 2)]
    public async Task HierarchicalFilesDecodeLikeTheReferenceDecoder(string layout, int precision, string options, int tolerance)
    {
        var isoJpeg = await RequiredTools.IsoJpegAsync();
        var maxValue = (1 << precision) - 1;
        var channels = layout == "gray" ? 1 : 3;
        var directory = InteropSettings.GetArtifactsDirectory(nameof(HierarchicalFilesDecodeLikeTheReferenceDecoder));
        var name = string.Create(CultureInfo.InvariantCulture, $"{layout}-{precision}{options.Replace(" ", "", StringComparison.Ordinal)}");
        var inputPath = directory / (name + (channels == 1 ? ".pgm" : ".ppm"));
        await File.WriteAllBytesAsync(inputPath, WritePnm(Width, Height, channels, maxValue, CreateSamples(Width, Height, channels, maxValue)), XunitCancellationToken);
        var path = directory / (name + ".jpg");
        string[] layoutOptions = layout == "420" ? ["-s", "1x1,2x2,2x2"] : [];
        await isoJpeg.EncodeAsync(inputPath, path, ["-q", "85", .. layoutOptions, .. options.Split(' ')], XunitCancellationToken);
        var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        Assert.NotEqual(-1, data.AsSpan().IndexOf((ReadOnlySpan<byte>)[0xFF, 0xDE]));
        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal((Width, Height, precision), (info.Width, info.Height, info.BitsPerComponent));

        using var image = Image.Load(data);
        var reference = await isoJpeg.DecodeAsync(path, options.Contains("-c", StringComparison.Ordinal) ? ["-c"] : [], XunitCancellationToken);
        var (max, mean, excluded) = Compare(GetSamples(image, maxValue), GetSamples(reference, maxValue), maxValue);
        var description = string.Create(CultureInfo.InvariantCulture, $"{name}: max {max}, mean {mean:F4} (allowed {tolerance}), {excluded} pixels at an end of the range left out");
        TestContext.Current.TestOutputHelper?.WriteLine(description);
        Assert.True(max <= tolerance && excluded <= Width * Height / 10, description);
    }

    [Theory]
    [InlineData("-n", "JPEG DNL height")]
    public async Task ProcessesTheLibraryDoesNotReadAreRejected(string options, string feature)
    {
        var isoJpeg = await RequiredTools.IsoJpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(ProcessesTheLibraryDoesNotReadAreRejected));
        var name = feature.Replace(' ', '-');
        var inputPath = directory / (name + ".ppm");
        var path = directory / (name + ".jpg");
        await File.WriteAllBytesAsync(inputPath, WritePnm(32, 16, 3, 255, CreateSamples(32, 16, 3, 255)), XunitCancellationToken);
        await isoJpeg.EncodeAsync(inputPath, path, ["-q", "85", .. options.Split(' ')], XunitCancellationToken);

        // A valid file, which the reference decoder reads
        var reference = await isoJpeg.DecodeAsync(path, XunitCancellationToken);
        Assert.Equal((32, 16), (reference.Width, reference.Height));

        var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        Assert.Equal(feature, Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data)).Feature);
        Assert.Equal(feature, Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);
    }

    /// <summary>Gets the tolerance of a layout ("gray", "rgb", "444", "422" or "420") and a precision against the reference decoder.</summary>
    internal static (int Max, double Mean) GetTolerance(string layout, int precision) => Tolerances[(layout, precision)];

    /// <summary>Gets the samples of the first frame of an image, red, green and blue, at a precision of <paramref name="maxValue"/>.</summary>
    internal static int[] GetSamples(Image image, int maxValue)
    {
        using var typed = image.CloneAs<Rgba64>();
        var pixels = new Rgba64[image.Width * image.Height];
        typed.Frames[0].CopyPixelDataTo(pixels);
        var samples = new int[pixels.Length * 3];
        for (var i = 0; i < pixels.Length; i++)
        {
            samples[i * 3] = Narrow(pixels[i].R, maxValue);
            samples[(i * 3) + 1] = Narrow(pixels[i].G, maxValue);
            samples[(i * 3) + 2] = Narrow(pixels[i].B, maxValue);
        }

        return samples;
    }

    /// <summary>Gets the samples of a Netpbm file, red, green and blue, at a precision of <paramref name="maxValue"/>.</summary>
    internal static int[] GetSamples(ReferencePnm reference, int maxValue)
    {
        var rgba = reference.Rgba.Span;
        var samples = new int[rgba.Length / 4 * 3];
        for (var i = 0; i < rgba.Length / 4; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                var sample = rgba[(i * 4) + c];
                samples[(i * 3) + c] = reference.Is16Bit ? Narrow(sample, maxValue) : sample;
            }
        }

        return samples;
    }

    /// <summary>
    /// Measures the differences of two decodings, leaving out the pixels where a channel of either is 0 or
    /// <paramref name="maxValue"/> (colors outside the gamut, which decoders clamp at different stages).
    /// </summary>
    internal static (int Max, double Mean, int Excluded) Compare(int[] samples, int[] reference, int maxValue)
    {
        Assert.HasCount(reference.Length, samples);
        var max = 0;
        long sum = 0;
        var compared = 0;
        var excluded = 0;
        for (var i = 0; i < samples.Length; i += 3)
        {
            if (IsAtAnEnd(samples.AsSpan(i, 3)) || IsAtAnEnd(reference.AsSpan(i, 3)))
            {
                excluded++;
                continue;
            }

            for (var c = 0; c < 3; c++)
            {
                var difference = Math.Abs(samples[i + c] - reference[i + c]);
                max = Math.Max(max, difference);
                sum += difference;
                compared++;
            }
        }

        return (max, compared == 0 ? 0 : (double)sum / compared, excluded);

        bool IsAtAnEnd(ReadOnlySpan<int> pixel) => pixel.Contains(0) || pixel.Contains(maxValue);
    }

    /// <summary>Scales a 16-bit sample to <paramref name="maxValue"/>, to the nearest value: the inverse of the scaling of 8-bit and 12-bit samples to 16 bits.</summary>
    private static int Narrow(ushort sample, int maxValue) => (int)((((long)sample * maxValue) + 32767) / 65535);

    /// <summary>Gets the offset of the frame header (SOF marker) of a JPEG file, walking its segments.</summary>
    private static int FindFrameHeader(byte[] data)
    {
        var offset = 2;
        while (data[offset + 1] is not (>= 0xC0 and <= 0xCF) || data[offset + 1] is 0xC4 or 0xC8 or 0xCC)
        {
            offset += 2 + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2));
        }

        return offset;
    }

    /// <summary>
    /// Ramps, a hard-edged checkerboard, sawtooth wraps and noise in every channel: content for every band of the DCT, kept
    /// away from the ends of the range.
    /// </summary>
    private static int[] CreateSamples(int width, int height, int channels, int maxValue)
    {
        var samples = new int[width * height * channels];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    var value = (x * 4) + (y * 3) + (c * 50);
                    if (((x / 8) + (y / 8) + c) % 3 == 0)
                    {
                        value += 60;
                    }

                    value += (int)((uint)unchecked((x * 73856093) ^ (y * 19349663) ^ (c * 83492791)) % 25);
                    samples[(((y * width) + x) * channels) + c] = (24 + (value % 208)) * maxValue / 255;
                }
            }
        }

        return samples;
    }

    /// <summary>Writes a binary PGM (one channel) or PPM (three channels) file, with big-endian samples above 8 bits.</summary>
    private static byte[] WritePnm(int width, int height, int channels, int maxValue, int[] samples)
    {
        var header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{(channels == 1 ? "P5" : "P6")}\n{width} {height}\n{maxValue}\n"));
        var bytesPerSample = maxValue > 255 ? 2 : 1;
        var data = new byte[header.Length + (samples.Length * bytesPerSample)];
        header.CopyTo(data, 0);
        for (var i = 0; i < samples.Length; i++)
        {
            if (bytesPerSample == 1)
            {
                data[header.Length + i] = (byte)samples[i];
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(header.Length + (i * 2)), (ushort)samples[i]);
            }
        }

        return data;
    }
}
