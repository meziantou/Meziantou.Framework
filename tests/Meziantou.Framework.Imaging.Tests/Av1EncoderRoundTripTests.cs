using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The AV1 encoder against the AV1 decoder of the library, which the conformance vectors pin to the reference decoders
/// byte for byte: a stream of the encoder must decode, to the frame that the encoder reconstructed, and for a lossless
/// stream to the samples that were encoded. The independent decoders (dav1d and libaom) decode the output of the encoder in
/// the interop tests.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class Av1EncoderRoundTripTests
{
    public static TheoryData<int, int, int, string, string, int> LosslessCases()
    {
        var data = new TheoryData<int, int, int, string, string, int>();
        (int Width, int Height)[] sizes = [(1, 1), (3, 2), (8, 8), (17, 13), (64, 64), (65, 67), (130, 70)];
        string[] formats = ["400", "420", "422", "444"];
        string[] contents = ["flat", "gradient", "noise", "blocks", "photo"];
        var index = 0;
        foreach (var (width, height) in sizes)
        {
            foreach (var format in formats)
            {
                // Every size and format, with a bit depth, a content and an effort that rotate
                var bitDepth = 8 + (2 * (index % 3));
                data.Add(width, height, bitDepth, format, contents[index % contents.Length], index % 10);
                index++;
            }
        }

        // The whole effort ladder on a frame of several superblocks, and every content at each bit depth
        for (var effort = 0; effort <= 9; effort++)
        {
            data.Add(96, 80, 8, "420", "photo", effort);
        }

        foreach (var content in contents)
        {
            data.Add(40, 24, 8, "444", content, 4);
            data.Add(40, 24, 10, "420", content, 6);
            data.Add(40, 24, 12, "422", content, 2);
        }

        // Two columns of tiles (a tile is at most 4096 samples wide), then two rows of tiles as well
        data.Add(4100, 20, 8, "420", "gradient", 0);
        data.Add(4160, 70, 8, "400", "blocks", 0);
        return data;
    }

    [Theory]
    [MemberData(nameof(LosslessCases))]
    public void LosslessFramesDecodeToTheEncodedSamples(int width, int height, int bitDepth, string format, string content, int effort)
    {
        var name = $"{width}x{height}-{bitDepth}-{format}-{content}-e{effort}";
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var monochrome = format is "400";
        var subsamplingX = format is "420" or "422" ? 1 : 0;
        var subsamplingY = format is "420" ? 1 : 0;
        var settings = new Av1EncoderSettings(width, height, bitDepth, monochrome, subsamplingX, subsamplingY, ColorPrimaries: 1, TransferCharacteristics: 13, MatrixCoefficients: format is "444" ? 0 : 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Lossless: true, Quality: 100, effort);
        using var encoder = new Av1Encoder(scope, settings);
        var planeCount = monochrome ? 1 : 3;
        var source = new ushort[planeCount][];
        for (var plane = 0; plane < planeCount; plane++)
        {
            var buffer = encoder.GetSourcePlane(plane);
            source[plane] = CreateSamples(content, buffer.Width, buffer.Height, bitDepth, seed: (plane * 7919) + width + (31 * height));
            for (var y = 0; y < buffer.Height; y++)
            {
                source[plane].AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
            }
        }

        encoder.BeginFrame();
        var steps = 0;
        while (!encoder.Encode(XunitCancellationToken))
        {
            steps++;
        }

        // A call to start, one per row of superblocks of each tile, and the call that finishes: a tile is at most 4096
        // samples wide
        Assert.Equal(1 + (((width + 4095) / 4096) * ((height + 63) / 64)), steps);
        var payload = encoder.Payload.ToArray();
        TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {payload.Length} bytes for {source.Sum(plane => plane.Length)} samples");

        using var decoder = new Av1Decoder(scope);
        decoder.SetInput(payload, 0, payload.Length);
        var frame = decoder.DecodeNextOutputFrame(XunitCancellationToken);
        Assert.NotNull(frame);
        try
        {
            Assert.Equal((width, height, bitDepth, monochrome), (frame.Header.FrameWidth, frame.Header.FrameHeight, frame.Sequence.BitDepth, frame.Sequence.Monochrome));
            Assert.True(frame.Header.CodedLossless);
            for (var plane = 0; plane < planeCount; plane++)
            {
                var decoded = frame.GetPlane(plane);
                var reconstructed = encoder.Reconstruction.GetPlane(plane);
                Assert.Equal((reconstructed.Width, reconstructed.Height), (decoded.Width, decoded.Height));
                for (var y = 0; y < decoded.Height; y++)
                {
                    var expected = source[plane].AsSpan(y * decoded.Width, decoded.Width);
                    Assert.True(expected.SequenceEqual(decoded.GetRow(y)), $"{name}: row {y} of plane {plane} is not the encoded row");
                    Assert.True(expected.SequenceEqual(reconstructed.GetRow(y)), $"{name}: row {y} of plane {plane} of the reconstruction is not the encoded row");
                }
            }

            Assert.Null(decoder.DecodeNextOutputFrame(XunitCancellationToken));
        }
        finally
        {
            frame.Release();
        }
    }

    public static TheoryData<int, int, int, string, string, int, int> LossyCases()
    {
        var data = new TheoryData<int, int, int, string, string, int, int>();
        (int Width, int Height)[] sizes = [(1, 1), (5, 3), (16, 16), (33, 47), (64, 64), (130, 70)];
        string[] formats = ["400", "420", "422", "444"];
        string[] contents = ["flat", "gradient", "noise", "blocks", "photo"];
        int[] qualities = [0, 30, 60, 85, 100];
        var index = 0;
        foreach (var (width, height) in sizes)
        {
            foreach (var format in formats)
            {
                var bitDepth = 8 + (2 * (index % 3));
                data.Add(width, height, bitDepth, format, contents[index % contents.Length], qualities[index % qualities.Length], index % 10);
                index++;
            }
        }

        // Every transform size: blocks of 128 down to 4 samples, and the effort ladder
        for (var effort = 0; effort <= 9; effort++)
        {
            data.Add(200, 136, 8, "420", "photo", 70, effort);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LossyCases))]
    public void LossyFramesDecodeToTheReconstructionOfTheEncoder(int width, int height, int bitDepth, string format, string content, int quality, int effort)
    {
        var name = $"lossy-{width}x{height}-{bitDepth}-{format}-{content}-q{quality}-e{effort}";
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var monochrome = format is "400";
        var settings = new Av1EncoderSettings(width, height, bitDepth, monochrome, format is "420" or "422" ? 1 : 0, format is "420" ? 1 : 0, ColorPrimaries: 1, TransferCharacteristics: 13, MatrixCoefficients: 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Lossless: false, quality, effort);
        using var encoder = new Av1Encoder(scope, settings);
        var planeCount = monochrome ? 1 : 3;
        var source = new ushort[planeCount][];
        for (var plane = 0; plane < planeCount; plane++)
        {
            var buffer = encoder.GetSourcePlane(plane);
            source[plane] = CreateSamples(content, buffer.Width, buffer.Height, bitDepth, seed: (plane * 7919) + width + (31 * height));
            for (var y = 0; y < buffer.Height; y++)
            {
                source[plane].AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
            }
        }

        encoder.BeginFrame();
        while (!encoder.Encode(XunitCancellationToken))
        {
        }

        var payload = encoder.Payload.ToArray();

        using var decoder = new Av1Decoder(scope);
        decoder.SetInput(payload, 0, payload.Length);
        var frame = decoder.DecodeNextOutputFrame(XunitCancellationToken);
        Assert.NotNull(frame);
        try
        {
            Assert.False(frame.Header.CodedLossless);
            Assert.Equal(Av1Encoder.GetQuantizerIndex(quality), frame.Header.BaseQIdx);
            double squaredError = 0;
            long count = 0;
            for (var plane = 0; plane < planeCount; plane++)
            {
                var decoded = frame.GetPlane(plane);
                var reconstructed = encoder.Reconstruction.GetPlane(plane);
                for (var y = 0; y < decoded.Height; y++)
                {
                    Assert.True(reconstructed.GetRow(y).SequenceEqual(decoded.GetRow(y)), $"{name}: row {y} of plane {plane} is not the row that the encoder reconstructed");
                    var expected = source[plane].AsSpan(y * decoded.Width, decoded.Width);
                    var actual = decoded.GetRow(y);
                    for (var x = 0; x < expected.Length; x++)
                    {
                        double difference = expected[x] - actual[x];
                        squaredError += difference * difference;
                    }

                    count += expected.Length;
                }
            }

            // The fidelity follows the quality: a loose floor, which a broken transform or quantizer does not reach
            double max = (1 << bitDepth) - 1;
            var psnr = squaredError is 0 ? 100 : 10 * Math.Log10(max * max * count / squaredError);
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {payload.Length} bytes, PSNR {psnr:F2} dB"));
            var floor = quality switch
            {
                100 => 44,
                >= 85 => 34,
                >= 60 => 28,
                >= 30 => 22,
                _ => 12,
            };

            Assert.True(psnr >= floor, string.Create(CultureInfo.InvariantCulture, $"{name}: PSNR {psnr:F2} dB is below {floor} dB"));
        }
        finally
        {
            frame.Release();
        }
    }

    [Fact]
    public void EncodingIsDeterministic()
    {
        var first = EncodeLossless(77, 45, 8, effort: 5, seed: 3);
        var second = EncodeLossless(77, 45, 8, effort: 5, seed: 3);
        Assert.Equal(first, second);
    }

    [Fact]
    public void AHigherEffortDoesNotProduceALargerFrame()
    {
        var fast = EncodeLossless(128, 96, 8, effort: 0, seed: 5);
        var slow = EncodeLossless(128, 96, 8, effort: 7, seed: 5);
        Assert.HasCountLessThanOrEqual(fast.Length, slow);
    }

    private static byte[] EncodeLossless(int width, int height, int bitDepth, int effort, int seed)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(EncodeLossless));
        var settings = new Av1EncoderSettings(width, height, bitDepth, Monochrome: false, 1, 1, 1, 13, 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Lossless: true, Quality: 100, effort);
        using var encoder = new Av1Encoder(scope, settings);
        for (var plane = 0; plane < 3; plane++)
        {
            var buffer = encoder.GetSourcePlane(plane);
            var samples = CreateSamples("photo", buffer.Width, buffer.Height, bitDepth, seed + plane);
            for (var y = 0; y < buffer.Height; y++)
            {
                samples.AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
            }
        }

        encoder.BeginFrame();
        while (!encoder.Encode(TestContext.Current.CancellationToken))
        {
        }

        return encoder.Payload.ToArray();
    }

    private static ushort[] CreateSamples(string content, int width, int height, int bitDepth, int seed)
    {
        var random = new Random(seed);
        var max = (1 << bitDepth) - 1;
        var samples = new ushort[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = content switch
                {
                    "flat" => max / 3,
                    "gradient" => ((x * 3) + (y * 5)) * max / Math.Max(1, (width * 3) + (height * 5)),
                    "noise" => random.Next(max + 1),
                    "blocks" => (((x / 7) + (y / 5) + seed) % 4) * max / 3,
                    _ => (int)((max / 2.0) + (max / 4.0 * Math.Sin((x + seed) / 9.0)) + (max / 5.0 * Math.Cos(y / 6.0)) + random.Next(-max / 64, (max / 64) + 1) + ((x / 23) % 2 * max / 6.0)),
                };

                samples[(y * width) + x] = (ushort)Math.Clamp(value, 0, max);
            }
        }

        return samples;
    }
}
