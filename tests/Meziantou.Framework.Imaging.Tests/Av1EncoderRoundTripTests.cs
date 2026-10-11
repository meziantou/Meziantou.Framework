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

    public static TheoryData<int, int, int, string, string, int, int> SequenceCases()
    {
        var data = new TheoryData<int, int, int, string, string, int, int>();
        (int Width, int Height)[] sizes = [(8, 8), (33, 47), (64, 64), (130, 70)];
        string[] formats = ["400", "420", "422", "444"];
        string[] motions = ["static", "pan", "object", "cut", "noise", "fade"];
        int[] qualities = [-1, 30, 60, 85, 100];
        var index = 0;
        foreach (var (width, height) in sizes)
        {
            foreach (var format in formats)
            {
                // Every size and format, with a bit depth, a motion, a quality (-1 is lossless) and an effort that rotate
                data.Add(width, height, 8 + (2 * (index % 3)), format, motions[index % motions.Length], qualities[index % qualities.Length], index % 10);
                index++;
            }
        }

        // Every motion, lossless and lossy, and the effort ladder on a frame of several superblocks
        foreach (var motion in motions)
        {
            data.Add(96, 80, 8, "420", motion, -1, 4);
            data.Add(96, 80, 8, "420", motion, 60, 5);
            data.Add(70, 50, 10, "444", motion, 20, 2);
        }

        for (var effort = 0; effort <= 9; effort++)
        {
            data.Add(144, 100, 8, "420", "pan", 70, effort);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SequenceCases))]
    public void FramesOfASequenceDecodeToTheReconstructionOfTheEncoder(int width, int height, int bitDepth, string format, string motion, int quality, int effort)
    {
        const int FrameCount = 5;
        var lossless = quality < 0;
        var name = $"sequence-{width}x{height}-{bitDepth}-{format}-{motion}-q{quality}-e{effort}";
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var monochrome = format is "400";
        var subsamplingX = format is "420" or "422" ? 1 : 0;
        var subsamplingY = format is "420" ? 1 : 0;
        var settings = new Av1EncoderSettings(width, height, bitDepth, monochrome, subsamplingX, subsamplingY, ColorPrimaries: 1, TransferCharacteristics: 13, MatrixCoefficients: lossless && format is "444" ? 0 : 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: false, lossless, Math.Max(quality, 0), effort);
        using var encoder = new Av1Encoder(scope, settings);
        using var decoder = new Av1Decoder(scope);
        var planeCount = monochrome ? 1 : 3;
        var sizes = new int[FrameCount];
        var tools = Av1Tools.None;
        for (var index = 0; index < FrameCount; index++)
        {
            var source = new ushort[planeCount][];
            for (var plane = 0; plane < planeCount; plane++)
            {
                var buffer = encoder.GetSourcePlane(plane);
                source[plane] = CreateFrame(motion, index, buffer.Width, buffer.Height, plane > 0 ? subsamplingX : 0, plane > 0 ? subsamplingY : 0, bitDepth, seed: (plane * 7919) + width + (31 * height));
                for (var y = 0; y < buffer.Height; y++)
                {
                    source[plane].AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
                }
            }

            encoder.BeginFrame();
            while (!encoder.Encode(XunitCancellationToken))
            {
            }

            // The first frame is the only key frame: a decoder starts from it
            Assert.Equal(index is 0, encoder.IsKeyFrame);
            var payload = encoder.Payload.ToArray();
            sizes[index] = payload.Length;
            decoder.SetInput(payload, 0, payload.Length);
            var frame = decoder.DecodeNextOutputFrame(XunitCancellationToken);
            Assert.NotNull(frame);
            try
            {
                Assert.Equal(index is 0 ? Av1Constants.KeyFrame : Av1Constants.InterFrame, frame.Header.FrameType);
                Assert.Equal(lossless, frame.Header.CodedLossless);
                for (var plane = 0; plane < planeCount; plane++)
                {
                    var decoded = frame.GetPlane(plane);
                    var reconstructed = encoder.Reconstruction.GetPlane(plane);
                    for (var y = 0; y < decoded.Height; y++)
                    {
                        Assert.True(reconstructed.GetRow(y).SequenceEqual(decoded.GetRow(y)), $"{name}: row {y} of plane {plane} of frame {index} is not the row that the encoder reconstructed");
                        if (lossless)
                            Assert.True(source[plane].AsSpan(y * decoded.Width, decoded.Width).SequenceEqual(decoded.GetRow(y)), $"{name}: row {y} of plane {plane} of frame {index} is not the encoded row");
                    }
                }

                Assert.Null(decoder.DecodeNextOutputFrame(XunitCancellationToken));
            }
            finally
            {
                frame.Release();
            }

            if (index > 0)
                tools |= decoder.Tools;
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {string.Join(", ", sizes)} bytes, {tools}");

        // A frame that the previous one predicts is a fraction of a key frame: the frames of "static" are the same frame,
        // the ones of "pan" the same scene moved by whole samples, with new samples along two edges
        if (motion is "static" or "pan" && width >= 64)
        {
            Assert.True(tools.HasFlag(Av1Tools.InterBlock), $"{name}: no block is predicted from the previous frame");
            for (var index = 1; index < FrameCount; index++)
            {
                Assert.True(sizes[index] * 2 <= sizes[0], $"{name}: frame {index} has {sizes[index]} bytes, the key frame {sizes[0]} bytes");
            }
        }

        if (motion is "pan" && width >= 64)
            Assert.True(tools.HasFlag(Av1Tools.NewMotionVector), $"{name}: no motion vector is coded");
    }

    [Fact]
    public void KeyFramesFollowTheInterval()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(KeyFramesFollowTheInterval));
        foreach (var (interval, expected) in new[] { (0, "KPPPPPP"), (1, "KKKKKKK"), (3, "KPPKPPK") })
        {
            var settings = new Av1EncoderSettings(24, 16, 8, Monochrome: true, 1, 1, 1, 13, 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: false, Lossless: true, Quality: 100, Effort: 0, interval);
            using var encoder = new Av1Encoder(scope, settings);
            using var decoder = new Av1Decoder(scope);
            var actual = "";
            for (var index = 0; index < expected.Length; index++)
            {
                var buffer = encoder.GetSourcePlane(0);
                var samples = CreateFrame("pan", index, buffer.Width, buffer.Height, 0, 0, 8, seed: 11);
                for (var y = 0; y < buffer.Height; y++)
                {
                    samples.AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
                }

                encoder.BeginFrame();
                while (!encoder.Encode(TestContext.Current.CancellationToken))
                {
                }

                actual += encoder.IsKeyFrame ? "K" : "P";

                // A key frame carries the sequence header: a decoder can start from it
                var payload = encoder.Payload.ToArray();
                Assert.Equal(encoder.IsKeyFrame, (payload[0] >> 3) is Av1Constants.ObuSequenceHeader);
                decoder.SetInput(payload, 0, payload.Length);
                var frame = decoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken);
                Assert.NotNull(frame);
                try
                {
                    for (var y = 0; y < buffer.Height; y++)
                    {
                        Assert.True(samples.AsSpan(y * buffer.Width, buffer.Width).SequenceEqual(frame.GetPlane(0).GetRow(y)), $"interval {interval}: row {y} of frame {index} is not the encoded row");
                    }
                }
                finally
                {
                    frame.Release();
                }
            }

            Assert.Equal(expected, actual);
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

    [Theory]
    [InlineData(0, false)]
    [InlineData(7, true)]
    public void TheSearchUsesTheToolsOfItsEffort(int effort, bool expected)
    {
        // A few luma samples that stand out of a flat area, which the identity transform codes with fewer coefficients than
        // a DCT, and chroma samples that follow the luma samples, which only they predict
        const int Width = 96;
        const int Height = 64;
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(TheSearchUsesTheToolsOfItsEffort));
        var settings = new Av1EncoderSettings(Width, Height, 8, Monochrome: false, 1, 1, 1, 13, 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Lossless: false, Quality: 80, effort);
        using var encoder = new Av1Encoder(scope, settings);
        var random = new Random(17);
        var luma = encoder.GetSourcePlane(0);
        for (var y = 0; y < Height; y++)
        {
            var row = luma.GetRow(y);
            for (var x = 0; x < Width; x++)
            {
                row[x] = (ushort)(x < Width / 2 ? (random.Next(12) is 0 ? 230 : 40) : 60 + (random.Next(4) * 45));
            }
        }

        for (var plane = 1; plane < 3; plane++)
        {
            var chroma = encoder.GetSourcePlane(plane);
            for (var y = 0; y < chroma.Height; y++)
            {
                var row = chroma.GetRow(y);
                for (var x = 0; x < chroma.Width; x++)
                {
                    var average = (luma.GetRow(2 * y)[2 * x] + luma.GetRow(2 * y)[(2 * x) + 1] + luma.GetRow((2 * y) + 1)[2 * x] + luma.GetRow((2 * y) + 1)[(2 * x) + 1]) / 4;
                    row[x] = (ushort)(plane is 1 ? 64 + (average / 2) : 200 - (average / 2));
                }
            }
        }

        encoder.BeginFrame();
        while (!encoder.Encode(TestContext.Current.CancellationToken))
        {
        }

        var payload = encoder.Payload.ToArray();
        using var decoder = new Av1Decoder(scope);
        decoder.SetInput(payload, 0, payload.Length);
        var frame = decoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken);
        Assert.NotNull(frame);
        try
        {
            // The size of the transform blocks is coded when the search splits them
            Assert.Equal(expected ? Av1Constants.TxModeSelect : Av1Constants.TxModeLargest, frame.Header.TxMode);
            for (var plane = 0; plane < 3; plane++)
            {
                var decoded = frame.GetPlane(plane);
                var reconstructed = encoder.Reconstruction.GetPlane(plane);
                for (var y = 0; y < decoded.Height; y++)
                {
                    Assert.True(reconstructed.GetRow(y).SequenceEqual(decoded.GetRow(y)), $"effort {effort}: row {y} of plane {plane} is not the row that the encoder reconstructed");
                }
            }
        }
        finally
        {
            frame.Release();
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"effort {effort}: {payload.Length} bytes, {decoder.Tools}");
        Assert.Equal(expected, decoder.Tools.HasFlag(Av1Tools.ChromaFromLuma));
        Assert.Equal(expected, decoder.Tools.HasFlag(Av1Tools.IdentityTransform));
        Assert.Equal(expected, decoder.Tools.HasFlag(Av1Tools.FilterIntra));
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

    // The plane of a frame of a sequence: a scene that does not change ("static"), that moves by whole samples ("pan"), that
    // a square moves across ("object"), that another scene replaces ("cut"), that is noise, or that gets brighter ("fade")
    private static ushort[] CreateFrame(string motion, int index, int width, int height, int subsamplingX, int subsamplingY, int bitDepth, int seed)
    {
        const int Margin = 64;
        var max = (1 << bitDepth) - 1;
        if (motion is "noise")
            return CreateSamples("noise", width, height, bitDepth, seed + index);

        if (motion is "cut" && index >= 2)
            return CreateSamples("blocks", width, height, bitDepth, seed);

        // The motion of the luma plane is a multiple of the subsampling, so that the chroma planes move by whole samples
        var scene = CreateSamples("photo", width + (2 * Margin), height + (2 * Margin), bitDepth, seed);
        var offsetX = motion is "pan" ? (6 * index) >> subsamplingX : 0;
        var offsetY = motion is "pan" ? (-4 * index) >> subsamplingY : 0;
        var samples = new ushort[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int value = scene[((y + Margin + offsetY) * (width + (2 * Margin))) + x + Margin + offsetX];
                if (motion is "fade")
                    value = Math.Min(max, value + (index * max / 32));

                samples[(y * width) + x] = (ushort)value;
            }
        }

        if (motion is "object")
        {
            var size = Math.Max(2, Math.Min(width, height) / 3);
            var left = Math.Min(width - 1, ((10 * index) >> subsamplingX) % width);
            var top = Math.Min(height - 1, (4 * index) >> subsamplingY);
            for (var y = top; y < Math.Min(height, top + size); y++)
            {
                samples.AsSpan((y * width) + left, Math.Min(size, width - left)).Fill((ushort)(max * 3 / 4));
            }
        }

        return samples;
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
