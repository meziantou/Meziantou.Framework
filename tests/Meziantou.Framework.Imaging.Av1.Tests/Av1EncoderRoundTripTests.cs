using System.Buffers.Binary;
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

    public static TheoryData<int, int, string, string, int, int, int, int, bool, int> TiledCases() => new()
    {
        // Width, height, format, content, quality (-1 is lossless), effort, log2 of the tile columns and rows, deferred
        // symbols, and the number of tiles. Lossy efforts 0 to 2 restore loops, so their tiles are written once the frame
        // is filtered. 3 columns of superblocks in 4 columns of tiles are 3 columns of tiles
        { 256, 128, "420", "photo", 60, 0, 2, 1, false, 8 },
        { 256, 128, "420", "photo", 60, 5, 1, 1, false, 4 },
        { 256, 128, "420", "photo", 60, 5, 1, 1, true, 4 },
        { 256, 128, "420", "blocks", 70, 5, 2, 0, false, 4 },
        { 200, 136, "444", "photo", -1, 0, 1, 1, false, 4 },
        { 192, 128, "400", "photo", -1, 5, 2, 1, false, 6 },
        { 4160, 70, "420", "gradient", -1, 0, 0, 0, false, 2 },
    };

    /// <summary>
    /// The tiles of a frame are coded by up to <see cref="Av1EncoderSettings.MaxDegreeOfParallelism"/> workers: the bytes
    /// are the ones of one worker, for every layout of the tiles (the fewest that a frame wider than 4096 samples has, or
    /// more), lossless and lossy, with the symbols written with the blocks or after the frame is filtered, for a key frame
    /// and for the inter frames that follow it. The last pass codes the tiles from the last one, each by the worker of
    /// its index modulo 3 (<see cref="Av1TileWorkers.TestReverseOrder"/>).
    /// </summary>
    [Theory]
    [MemberData(nameof(TiledCases))]
    public void TilesEncodedByWorkersAreTheTilesOfOneWorker(int width, int height, string format, string content, int quality, int effort, int tileColsLog2, int tileRowsLog2, bool defersSymbols, int tileCount)
    {
        const int FrameCount = 3;
        var name = $"tiles-{width}x{height}-{format}-{content}-q{quality}-e{effort}-{tileColsLog2}x{tileRowsLog2}-{defersSymbols}";
        var expected = Encode(workers: 1, reverse: false, out var tiles);
        Assert.Equal(tileCount, tiles);
        Av1PostFilter.TestIgnoreMinimumSize = true;
        try
        {
            foreach (var workers in (int[])[2, 3, 16])
            {
                Assert.Equal(expected, Encode(workers, reverse: false, out _));
            }

            Assert.Equal(expected, Encode(workers: 3, reverse: true, out _));
        }
        finally
        {
            Av1PostFilter.TestIgnoreMinimumSize = false;
        }

        string[] Encode(int workers, bool reverse, out int frameTiles)
        {
            var lossless = quality < 0;
            var monochrome = format is "400";
            var subsamplingX = format is "420" ? 1 : 0;
            var scope = AllocationScope.Create(ImageConfiguration.Default, name);
            var settings = new Av1EncoderSettings(width, height, 8, monochrome, subsamplingX, subsamplingX, ColorPrimaries: 1, TransferCharacteristics: 13, MatrixCoefficients: lossless && format is "444" ? 0 : 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: false, lossless, Math.Max(quality, 0), effort, KeyFrameInterval: 0, workers, tileColsLog2, tileRowsLog2);
            using var encoder = new Av1Encoder(scope, settings) { DefersSymbols = defersSymbols };
            using var decoder = new Av1Decoder(scope);
            var payloads = new string[FrameCount];
            frameTiles = 0;
            Av1TileWorkers.TestReverseOrder = reverse;
            try
            {
                for (var index = 0; index < FrameCount; index++)
                {
                    for (var plane = 0; plane < (monochrome ? 1 : 3); plane++)
                    {
                        var buffer = encoder.GetSourcePlane(plane);
                        var samples = content is "photo" ? CreateFrame("pan", index, buffer.Width, buffer.Height, plane > 0 ? subsamplingX : 0, plane > 0 ? subsamplingX : 0, 8, seed: plane + 5) : CreateSamples(content, buffer.Width, buffer.Height, 8, seed: plane + index);
                        for (var y = 0; y < buffer.Height; y++)
                        {
                            samples.AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
                        }
                    }

                    encoder.BeginFrame();
                    while (!encoder.Encode(XunitCancellationToken))
                    {
                    }

                    var payload = encoder.Payload.ToArray();
                    payloads[index] = Convert.ToHexString(payload);

                    // The stream decodes to the reconstruction of the encoder
                    decoder.SetInput(payload, 0, payload.Length);
                    var frame = decoder.DecodeNextOutputFrame(XunitCancellationToken);
                    Assert.NotNull(frame);
                    try
                    {
                        frameTiles = frame.Header.TileCount;
                        for (var plane = 0; plane < frame.PlaneCount; plane++)
                        {
                            for (var y = 0; y < frame.GetPlane(plane).Height; y++)
                            {
                                Assert.True(encoder.Reconstruction.GetPlane(plane).GetRow(y).SequenceEqual(frame.GetPlane(plane).GetRow(y)), $"{name}, {workers} workers: row {y} of plane {plane} of frame {index} is not the row that the encoder reconstructed");
                            }
                        }
                    }
                    finally
                    {
                        frame.Release();
                    }
                }
            }
            finally
            {
                Av1TileWorkers.TestReverseOrder = false;
            }

            return payloads;
        }
    }

    [Fact]
    public void TileWorkersOfTheEncoderAreChargedToTheScopeAndObserveTheCancellation()
    {
        var settings = new Av1EncoderSettings(256, 128, 8, Monochrome: false, 1, 1, 1, 13, 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Lossless: false, Quality: 60, Effort: 0, MaxDegreeOfParallelism: 4, MinTileColsLog2: 1, MinTileRowsLog2: 1);
        Av1PostFilter.TestIgnoreMinimumSize = true;
        try
        {
            var one = MeasurePeak(settings with { MaxDegreeOfParallelism = 1 }, ImageConfiguration.Default.Limits);
            var four = MeasurePeak(settings, ImageConfiguration.Default.Limits);
            Assert.True(four > one, $"{four} bytes with 4 workers, {one} with one.");

            // The limit that one worker fits
            var scope = new AllocationScope(new ImageResourceLimits { MaxLiveAllocationBytes = one }, name: nameof(TileWorkersOfTheEncoderAreChargedToTheScopeAndObserveTheCancellation));
            using (var encoder = new Av1Encoder(scope, settings))
            {
                encoder.BeginFrame();
                Assert.Throws<ImageResourceLimitException>(() => EncodeFrame(encoder, XunitCancellationToken));
            }

            Assert.Equal(0, scope.LiveBytes);

            // A cancellation while the workers code the tiles: the second tile cancels the token as it starts
            var canceled = AllocationScope.Create(ImageConfiguration.Default, nameof(TileWorkersOfTheEncoderAreChargedToTheScopeAndObserveTheCancellation));
            using (var encoder = new Av1Encoder(canceled, settings))
            {
                using var cancellation = new CancellationTokenSource();
                encoder.BeginFrame();
                Av1TileWorkers.TestTileStarting = tile =>
                {
                    if (tile is 1)
                    {
                        cancellation.Cancel();
                    }
                };

                Assert.Throws<OperationCanceledException>(() => EncodeFrame(encoder, cancellation.Token));
            }

            Assert.Equal(0, canceled.LiveBytes);
        }
        finally
        {
            Av1PostFilter.TestIgnoreMinimumSize = false;
            Av1TileWorkers.TestTileStarting = null;
        }

        static long MeasurePeak(Av1EncoderSettings settings, ImageResourceLimits limits)
        {
            var scope = new AllocationScope(limits, name: nameof(TileWorkersOfTheEncoderAreChargedToTheScopeAndObserveTheCancellation));
            using (var encoder = new Av1Encoder(scope, settings))
            {
                encoder.BeginFrame();
                EncodeFrame(encoder, XunitCancellationToken);
            }

            Assert.Equal(0, scope.LiveBytes);
            return scope.GetDiagnostics().PeakLiveBytes;
        }

        static void EncodeFrame(Av1Encoder encoder, CancellationToken cancellationToken)
        {
            while (!encoder.Encode(cancellationToken))
            {
            }
        }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASequenceHeaderBetweenTheHeaderAndTheTilesOfAFrame(bool startsANewSequence)
    {
        // TD, SH, KEY, TD, FH (inter), SH, TILE_GROUP: a copy of the sequence header may be repeated anywhere, but a new
        // sequence would release the reference slots that the pending inter frame predicts from
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(ASequenceHeaderBetweenTheHeaderAndTheTilesOfAFrame));
        var settings = new Av1EncoderSettings(24, 16, 8, Monochrome: true, 1, 1, 1, 13, 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: false, Lossless: true, Quality: 100, Effort: 0, KeyFrameInterval: 0);
        using var encoder = new Av1Encoder(scope, settings);
        var payloads = new byte[2][];
        var samples = new ushort[2][];
        for (var index = 0; index < payloads.Length; index++)
        {
            var buffer = encoder.GetSourcePlane(0);
            samples[index] = CreateFrame("pan", index, buffer.Width, buffer.Height, 0, 0, 8, seed: 5);
            for (var y = 0; y < buffer.Height; y++)
            {
                samples[index].AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
            }

            encoder.BeginFrame();
            while (!encoder.Encode(TestContext.Current.CancellationToken))
            {
            }

            Assert.Equal(index is 0, encoder.IsKeyFrame);
            payloads[index] = encoder.Payload.ToArray();
        }

        var sequenceHeaderObu = Av1ObuHeader.Read(payloads[0]);
        Assert.Equal(Av1Constants.ObuSequenceHeader, sequenceHeaderObu.Type);
        var keyFrame = payloads[0].AsSpan(sequenceHeaderObu.TotalLength).ToArray();
        var interFrameObu = Av1ObuHeader.Read(payloads[1]);
        Assert.HasCount(Av1ObuHeader.Read(keyFrame).TotalLength, keyFrame);
        Assert.Equal((Av1Constants.ObuFrame, interFrameObu.TotalLength), (interFrameObu.Type, payloads[1].Length));
        var interFrame = payloads[1].AsSpan(interFrameObu.HeaderLength).ToArray();

        // The other sequence: frames of another size
        var otherSettings = settings with { Width = 32 };
        using var otherEncoder = new Av1Encoder(scope, otherSettings);
        otherEncoder.BeginFrame();
        while (!otherEncoder.Encode(TestContext.Current.CancellationToken))
        {
        }

        var otherPayload = otherEncoder.Payload.ToArray();
        var otherSequenceHeaderObu = Av1ObuHeader.Read(otherPayload);
        Assert.Equal(Av1Constants.ObuSequenceHeader, otherSequenceHeaderObu.Type);
        var sequenceHeader = payloads[0].AsSpan(sequenceHeaderObu.HeaderLength, sequenceHeaderObu.PayloadLength).ToArray();
        var otherSequenceHeader = otherPayload.AsSpan(otherSequenceHeaderObu.HeaderLength, otherSequenceHeaderObu.PayloadLength).ToArray();
        Assert.False(sequenceHeader.AsSpan().SequenceEqual(otherSequenceHeader));

        // The frame OBU of the inter frame is split into a frame header OBU and a tile group OBU: its frame header is
        // written again from the header that the decoder reads, then ended with trailing bits instead of a byte alignment
        byte[] frameHeader;
        int frameHeaderLength;
        using (var decoder = new Av1Decoder(scope))
        {
            var data = payloads[0].Concat(payloads[1]).ToArray();
            decoder.SetInput(data, 0, data.Length);
            decoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken)!.Release();
            var frame = decoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken);
            Assert.NotNull(frame);
            try
            {
                using var output = new PooledPayloadWriter(scope);
                var writer = new Av1BitWriter(output);
                Av1HeaderWriter.WriteFrameHeader(ref writer, frame.Sequence, frame.Header);
                writer.ByteAlign();
                frameHeaderLength = output.Length;
                Assert.True(interFrame.AsSpan(0, frameHeaderLength).SequenceEqual(output.WrittenSpan), "the frame header is not the one of the frame OBU");

                output.Reset();
                writer = new Av1BitWriter(output);
                Av1HeaderWriter.WriteFrameHeader(ref writer, frame.Sequence, frame.Header);
                writer.WriteTrailingBits();
                frameHeader = output.WrittenSpan.ToArray();
            }
            finally
            {
                frame.Release();
            }
        }

        using var stream = new PooledPayloadWriter(scope);
        Av1ObuWriter.WriteTemporalDelimiter(stream);
        Av1ObuWriter.Write(stream, Av1Constants.ObuSequenceHeader, sequenceHeader);
        stream.Write(keyFrame);
        Av1ObuWriter.WriteTemporalDelimiter(stream);
        Av1ObuWriter.Write(stream, Av1Constants.ObuFrameHeader, frameHeader);
        Av1ObuWriter.Write(stream, Av1Constants.ObuSequenceHeader, startsANewSequence ? otherSequenceHeader : sequenceHeader);
        Av1ObuWriter.Write(stream, Av1Constants.ObuTileGroup, interFrame.AsSpan(frameHeaderLength));
        var input = stream.WrittenSpan.ToArray();

        using var streamDecoder = new Av1Decoder(scope);
        streamDecoder.SetInput(input, 0, input.Length);
        streamDecoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken)!.Release();
        if (startsANewSequence)
        {
            Assert.Throws<InvalidImageContentException>(() => streamDecoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken));
            return;
        }

        var interOutput = streamDecoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken);
        Assert.NotNull(interOutput);
        try
        {
            var plane = interOutput.GetPlane(0);
            for (var y = 0; y < plane.Height; y++)
            {
                Assert.True(samples[1].AsSpan(y * plane.Width, plane.Width).SequenceEqual(plane.GetRow(y)), $"row {y} of the inter frame is not the encoded row");
            }
        }
        finally
        {
            interOutput.Release();
        }

        Assert.Null(streamDecoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("frame")]
    [InlineData("frame-with-tile-positions")]
    [InlineData("tile-group-with-tile-positions")]
    [InlineData("tile-group-per-tile")]
    public void TileStartAndEndPositionsOfAFrameWithTwoTiles(string layout)
    {
        // A frame of two columns of tiles (a tile is at most 4096 samples wide). The tile group of a frame OBU has no tile
        // positions (section 6.10.1), while tile group OBUs can split the tiles of a frame
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(TileStartAndEndPositionsOfAFrameWithTwoTiles));
        var settings = new Av1EncoderSettings(4100, 8, 8, Monochrome: true, 1, 1, 1, 13, 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: false, Lossless: true, Quality: 100, Effort: 0, KeyFrameInterval: 0);
        using var encoder = new Av1Encoder(scope, settings);
        var buffer = encoder.GetSourcePlane(0);
        var samples = CreateSamples("photo", buffer.Width, buffer.Height, 8, seed: 11);
        for (var y = 0; y < buffer.Height; y++)
        {
            samples.AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
        }

        encoder.BeginFrame();
        while (!encoder.Encode(TestContext.Current.CancellationToken))
        {
        }

        var payload = encoder.Payload.ToArray();
        var sequenceHeaderObu = Av1ObuHeader.Read(payload);
        Assert.Equal(Av1Constants.ObuSequenceHeader, sequenceHeaderObu.Type);
        var sequenceHeader = payload.AsSpan(sequenceHeaderObu.HeaderLength, sequenceHeaderObu.PayloadLength).ToArray();
        var frameObu = Av1ObuHeader.Read(payload.AsSpan(sequenceHeaderObu.TotalLength));
        Assert.Equal(Av1Constants.ObuFrame, frameObu.Type);
        var frameData = payload.AsSpan(sequenceHeaderObu.TotalLength + frameObu.HeaderLength, frameObu.PayloadLength).ToArray();

        // The frame header is written again from the header that the decoder reads: ended with a byte alignment in the frame
        // OBU, and with trailing bits in a frame header OBU
        byte[] frameHeader;
        int frameHeaderLength;
        int tileSizeBytes;
        using (var decoder = new Av1Decoder(scope))
        {
            decoder.SetInput(payload, 0, payload.Length);
            var frame = decoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken);
            Assert.NotNull(frame);
            try
            {
                // Without screen content tools: force_integer_mv, which the decoder sets for an intra frame, is not coded
                Assert.Equal((2, 1, false), (frame.Header.TileCount, frame.Header.TileColsLog2 + frame.Header.TileRowsLog2, frame.Header.AllowScreenContentTools));
                tileSizeBytes = frame.Header.TileSizeBytes;
                using var output = new PooledPayloadWriter(scope);
                var writer = new Av1BitWriter(output);
                Av1HeaderWriter.WriteFrameHeader(ref writer, frame.Sequence, frame.Header);
                writer.ByteAlign();
                frameHeaderLength = output.Length;
                Assert.True(frameData.AsSpan(0, frameHeaderLength).SequenceEqual(output.WrittenSpan), "the frame header is not the one of the frame OBU");

                output.Reset();
                writer = new Av1BitWriter(output);
                Av1HeaderWriter.WriteFrameHeader(ref writer, frame.Sequence, frame.Header);
                writer.WriteTrailingBits();
                frameHeader = output.WrittenSpan.ToArray();
            }
            finally
            {
                frame.Release();
            }
        }

        // The tile group of the frame OBU: tile_start_and_end_present_flag (0) and its byte alignment, the size of the first
        // tile, then the tiles
        var tileGroup = frameData.AsSpan(frameHeaderLength).ToArray();
        Assert.Equal(0, tileGroup[0]);
        var firstTileSize = 1;
        for (var i = 0; i < tileSizeBytes; i++)
        {
            firstTileSize += tileGroup[1 + i] << (8 * i);
        }

        var firstTile = tileGroup.AsSpan(1 + tileSizeBytes, firstTileSize).ToArray();
        var secondTile = tileGroup.AsSpan(1 + tileSizeBytes + firstTileSize).ToArray();

        // A tile group header with tile positions: the flag (1), tg_start and tg_end of one bit each, then the byte alignment
        static byte TileGroupHeader(int start, int end) => (byte)(0x80 | (start << 6) | (end << 5));

        using var stream = new PooledPayloadWriter(scope);
        Av1ObuWriter.WriteTemporalDelimiter(stream);
        Av1ObuWriter.Write(stream, Av1Constants.ObuSequenceHeader, sequenceHeader);
        switch (layout)
        {
            case "frame":
                Av1ObuWriter.Write(stream, Av1Constants.ObuFrame, frameData);
                break;
            case "frame-with-tile-positions":
                Av1ObuWriter.Write(stream, Av1Constants.ObuFrame, [.. frameData.AsSpan(0, frameHeaderLength), TileGroupHeader(0, 1), .. tileGroup.AsSpan(1)]);
                break;
            case "tile-group-with-tile-positions":
                Av1ObuWriter.Write(stream, Av1Constants.ObuFrameHeader, frameHeader);
                Av1ObuWriter.Write(stream, Av1Constants.ObuTileGroup, [TileGroupHeader(0, 1), .. tileGroup.AsSpan(1)]);
                break;
            case "tile-group-per-tile":
                Av1ObuWriter.Write(stream, Av1Constants.ObuFrameHeader, frameHeader);
                Av1ObuWriter.Write(stream, Av1Constants.ObuTileGroup, [TileGroupHeader(0, 0), .. firstTile]);
                Av1ObuWriter.Write(stream, Av1Constants.ObuTileGroup, [TileGroupHeader(1, 1), .. secondTile]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layout), layout, message: null);
        }

        var input = stream.WrittenSpan.ToArray();
        using var streamDecoder = new Av1Decoder(scope);
        streamDecoder.SetInput(input, 0, input.Length);
        if (layout is "frame-with-tile-positions")
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => streamDecoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken));
            Assert.Contains("tile start and end positions", exception.Message, StringComparison.Ordinal);
            return;
        }

        var decoded = streamDecoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken);
        Assert.NotNull(decoded);
        try
        {
            var plane = decoded.GetPlane(0);
            for (var y = 0; y < plane.Height; y++)
            {
                Assert.True(samples.AsSpan(y * plane.Width, plane.Width).SequenceEqual(plane.GetRow(y)), $"row {y} is not the encoded row");
            }
        }
        finally
        {
            decoded.Release();
        }

        Assert.Null(streamDecoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("none", "rest")]
    [InlineData("afterFrameHeader", "rest")]
    [InlineData("afterFrameHeader", "nothing")]
    [InlineData("afterFrameHeader", "frame")]
    [InlineData("betweenTileGroups", "rest")]
    [InlineData("betweenTileGroups", "nothing")]
    [InlineData("betweenTileGroups", "frame")]
    public void ATemporalDelimiterInsideAFrame(string position, string following)
    {
        // TD, SH, FH, TILE_GROUP (tile 0), TILE_GROUP (tile 1), TD: the units of a frame belong to one temporal unit, so a
        // temporal delimiter between the header and the last tile group of the frame is rejected, whatever follows it: the
        // rest of the frame, the end of the input or another frame, instead of dropping the pending frame. A frame wider than
        // 4096 samples has two tiles.
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(ATemporalDelimiterInsideAFrame));
        var settings = new Av1EncoderSettings(4104, 8, 8, Monochrome: true, 1, 1, 1, 13, 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: false, Lossless: true, Quality: 100, Effort: 0);
        using var encoder = new Av1Encoder(scope, settings);
        var buffer = encoder.GetSourcePlane(0);
        var samples = CreateSamples("noise", buffer.Width, buffer.Height, 8, seed: 9);
        for (var y = 0; y < buffer.Height; y++)
        {
            samples.AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
        }

        encoder.BeginFrame();
        while (!encoder.Encode(TestContext.Current.CancellationToken))
        {
        }

        var payload = encoder.Payload.ToArray();
        var sequenceHeaderObu = Av1ObuHeader.Read(payload);
        Assert.Equal(Av1Constants.ObuSequenceHeader, sequenceHeaderObu.Type);
        var sequenceHeader = payload.AsSpan(sequenceHeaderObu.HeaderLength, sequenceHeaderObu.PayloadLength).ToArray();
        var frameObu = Av1ObuHeader.Read(payload.AsSpan(sequenceHeaderObu.TotalLength));
        Assert.Equal((Av1Constants.ObuFrame, payload.Length), (frameObu.Type, sequenceHeaderObu.TotalLength + frameObu.TotalLength));
        var framePayload = payload.AsSpan(sequenceHeaderObu.TotalLength + frameObu.HeaderLength, frameObu.PayloadLength).ToArray();

        // The frame OBU is split into a frame header OBU and a tile group OBU per tile
        byte[] frameHeader;
        int frameHeaderLength;
        int tileSizeBytes;
        using (var decoder = new Av1Decoder(scope))
        {
            decoder.SetInput(payload, 0, payload.Length);
            var frame = decoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken);
            Assert.NotNull(frame);
            try
            {
                Assert.Equal((2, 1), (frame.Header.TileCount, frame.Header.TileColsLog2 + frame.Header.TileRowsLog2));
                tileSizeBytes = frame.Header.TileSizeBytes;
                using var output = new PooledPayloadWriter(scope);
                var writer = new Av1BitWriter(output);
                Av1HeaderWriter.WriteFrameHeader(ref writer, frame.Sequence, frame.Header);
                writer.ByteAlign();
                frameHeaderLength = output.Length;
                Assert.True(framePayload.AsSpan(0, frameHeaderLength).SequenceEqual(output.WrittenSpan), "the frame header is not the one of the frame OBU");

                output.Reset();
                writer = new Av1BitWriter(output);
                Av1HeaderWriter.WriteFrameHeader(ref writer, frame.Sequence, frame.Header);
                writer.WriteTrailingBits();
                frameHeader = output.WrittenSpan.ToArray();
            }
            finally
            {
                frame.Release();
            }
        }

        // The tile group of the frame OBU: tile_start_and_end_present_flag equal to 0, then the size of the first tile
        var tileGroup = framePayload.AsSpan(frameHeaderLength);
        Assert.Equal(0, tileGroup[0]);
        Span<byte> tileSize = stackalloc byte[4];
        tileGroup.Slice(1, tileSizeBytes).CopyTo(tileSize);
        var firstTileSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(tileSize) + 1;
        var firstTile = tileGroup.Slice(1 + tileSizeBytes, firstTileSize).ToArray();
        var secondTile = tileGroup[(1 + tileSizeBytes + firstTileSize)..].ToArray();

        var tileGroups = new[] { CreateTileGroup(scope, tile: 0, firstTile), CreateTileGroup(scope, tile: 1, secondTile) };
        var tilesBeforeDelimiter = position switch
        {
            "afterFrameHeader" => 0,
            "betweenTileGroups" => 1,
            _ => tileGroups.Length,
        };

        using var stream = new PooledPayloadWriter(scope);
        Av1ObuWriter.WriteTemporalDelimiter(stream);
        Av1ObuWriter.Write(stream, Av1Constants.ObuSequenceHeader, sequenceHeader);
        Av1ObuWriter.Write(stream, Av1Constants.ObuFrameHeader, frameHeader);
        for (var tile = 0; tile < tilesBeforeDelimiter; tile++)
        {
            Av1ObuWriter.Write(stream, Av1Constants.ObuTileGroup, tileGroups[tile]);
        }

        Av1ObuWriter.WriteTemporalDelimiter(stream);
        switch (following)
        {
            case "rest":
                for (var tile = tilesBeforeDelimiter; tile < tileGroups.Length; tile++)
                {
                    Av1ObuWriter.Write(stream, Av1Constants.ObuTileGroup, tileGroups[tile]);
                }

                break;
            case "frame":
                stream.Write(payload.AsSpan(sequenceHeaderObu.TotalLength));
                break;
        }

        var input = stream.WrittenSpan.ToArray();

        using var streamDecoder = new Av1Decoder(scope);
        streamDecoder.SetInput(input, 0, input.Length);
        if (position is not "none")
        {
            Assert.Throws<InvalidImageContentException>(() => streamDecoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken));
            return;
        }

        var decoded = streamDecoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken);
        Assert.NotNull(decoded);
        try
        {
            var plane = decoded.GetPlane(0);
            for (var y = 0; y < plane.Height; y++)
            {
                Assert.True(samples.AsSpan(y * plane.Width, plane.Width).SequenceEqual(plane.GetRow(y)), $"row {y} is not the encoded row");
            }
        }
        finally
        {
            decoded.Release();
        }

        Assert.Null(streamDecoder.DecodeNextOutputFrame(TestContext.Current.CancellationToken));

        // A tile group of one tile of a frame of two tiles: tile_start_and_end_present_flag, tg_start and tg_end on one bit each
        static byte[] CreateTileGroup(AllocationScope scope, int tile, byte[] data)
        {
            using var output = new PooledPayloadWriter(scope);
            var writer = new Av1BitWriter(output);
            writer.WriteFlag(true);
            writer.WriteBits((uint)tile, 1);
            writer.WriteBits((uint)tile, 1);
            writer.ByteAlign();
            output.Write(data);
            return output.WrittenSpan.ToArray();
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
        // A few luma samples that stand out of an area that is almost flat, which the identity transform codes with fewer
        // coefficients than a DCT (the area has too many values to be the content of a screen, which has palettes), and
        // chroma samples that follow the luma samples, which only they predict
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
                row[x] = (ushort)(x < Width / 2 ? (random.Next(12) is 0 ? 150 + random.Next(100) : 38 + random.Next(5)) : 60 + random.Next(180));
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
        Assert.False(decoder.Tools.HasFlag(Av1Tools.Palette));
    }

    public static TheoryData<int, int, int, string, int, int> PaletteCases()
    {
        var data = new TheoryData<int, int, int, string, int, int>();
        (int Width, int Height)[] sizes = [(40, 24), (64, 64), (70, 50), (130, 71)];
        string[] formats = ["400", "420", "422", "444"];
        int[] qualities = [-1, 40, -1, 90];
        var index = 0;
        foreach (var (width, height) in sizes)
        {
            foreach (var format in formats)
            {
                // Every size and format, with a bit depth, a quality (-1 is lossless) and an effort that rotate
                data.Add(width, height, 8 + (2 * (index % 3)), format, qualities[index % qualities.Length], (index * 3) % 10);
                index++;
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PaletteCases))]
    public void FramesOfFewColorsAreCodedWithPalettes(int width, int height, int bitDepth, string format, int quality, int effort)
    {
        var lossless = quality < 0;
        var name = $"palette-{width}x{height}-{bitDepth}-{format}-q{quality}-e{effort}";
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var monochrome = format is "400";
        var subsamplingX = format is "420" or "422" ? 1 : 0;
        var subsamplingY = format is "420" ? 1 : 0;
        var settings = new Av1EncoderSettings(width, height, bitDepth, monochrome, subsamplingX, subsamplingY, ColorPrimaries: 1, TransferCharacteristics: 13, MatrixCoefficients: lossless && format is "444" ? 0 : 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: false, lossless, Math.Max(quality, 0), effort);
        using var encoder = new Av1Encoder(scope, settings);
        using var decoder = new Av1Decoder(scope);
        var planeCount = monochrome ? 1 : 3;
        var max = (1 << bitDepth) - 1;
        var sizes = new int[3];
        var tools = Av1Tools.None;
        for (var index = 0; index < sizes.Length; index++)
        {
            // The content of a screen: areas of a few colors, with marks of one sample, which the next frame changes in
            // part. The chroma planes have their own areas: the same U value goes with several V values, and two V
            // values are far apart, across the values that wrap around
            var random = new Random(width + (31 * index));
            var source = new ushort[planeCount][];
            for (var plane = 0; plane < planeCount; plane++)
            {
                var buffer = encoder.GetSourcePlane(plane);
                ReadOnlySpan<int> levels = plane switch
                {
                    0 => [max / 16, max, max / 2, max / 3, 0, max - 3, max / 5, max - (max / 7), max / 9, 7],
                    1 => [max / 4, max / 4, max / 2, max / 2, 3, max - 1],
                    _ => [5, max - 5, max / 2, 2, max / 3, max / 3],
                };

                source[plane] = new ushort[buffer.Width * buffer.Height];
                for (var y = 0; y < buffer.Height; y++)
                {
                    for (var x = 0; x < buffer.Width; x++)
                    {
                        // An area has 2 to 4 of the levels, and the frames differ in a band of the plane. One area out of
                        // five has more values than a palette holds: a few steps around its levels
                        var area = (x / 23) + ((y / 13) * 3) + (index is 0 || y < buffer.Height / 2 ? 0 : index);
                        var mark = ((x * 7) + (y * 13) + (x * y)) % (5 + (area % 4));
                        var level = levels[(area + (mark is 0 ? 1 : 0) + (mark is 1 ? 2 : 0) + (plane > 0 && mark is 2 ? 3 : 0)) % levels.Length];
                        if ((area % 5) is 4 && plane is 0)
                            level = Math.Clamp(level + ((x + (3 * y)) % 7) - 3, 0, max);

                        source[plane][(y * buffer.Width) + x] = (ushort)level;
                    }

                    source[plane].AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
                }

                // A block of many values, which no palette holds
                for (var y = 0; y < Math.Min(8, buffer.Height); y++)
                {
                    for (var x = 0; x < Math.Min(8, buffer.Width); x++)
                    {
                        source[plane][(y * buffer.Width) + x] = (ushort)random.Next(max + 1);
                    }

                    source[plane].AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
                }
            }

            encoder.BeginFrame();
            while (!encoder.Encode(XunitCancellationToken))
            {
            }

            var payload = encoder.Payload.ToArray();
            sizes[index] = payload.Length;
            decoder.SetInput(payload, 0, payload.Length);
            var frame = decoder.DecodeNextOutputFrame(XunitCancellationToken);
            Assert.NotNull(frame);
            try
            {
                Assert.True(frame.Header.AllowScreenContentTools, $"{name}: frame {index} is not coded with the tools of screen content");
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
            }
            finally
            {
                frame.Release();
            }

            tools |= decoder.Tools;
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {string.Join(", ", sizes)} bytes, {tools}");
        Assert.True(tools.HasFlag(Av1Tools.Palette), $"{name}: no block has a palette");
    }

    [Theory]
    [InlineData(40, 24, 8, "444", "photo", -1, 4)]
    [InlineData(150, 77, 10, "420", "blocks", -1, 2)]
    [InlineData(4160, 70, 8, "400", "blocks", -1, 0)]
    [InlineData(4100, 80, 8, "420", "photo", 60, 3)]
    [InlineData(130, 70, 12, "422", "photo", 30, 5)]
    [InlineData(64, 64, 8, "444", "noise", 90, 7)]
    public void SyntaxElementsWrittenAfterTheFrameAreTheSameBytes(int width, int height, int bitDepth, string format, string content, int quality, int effort)
    {
        // The syntax elements of a frame are written as its superblocks are encoded, or kept and written once the frame
        // is filtered, when the units of the loop restoration are coded with them: the tiles are the same bytes either
        // way, in every tile of a frame that has several, and for the frames that follow the first one
        var lossless = quality < 0;
        var name = $"deferred-{width}x{height}-{bitDepth}-{format}-{content}-q{quality}-e{effort}";
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var monochrome = format is "400";
        var subsamplingX = format is "420" or "422" ? 1 : 0;
        var subsamplingY = format is "420" ? 1 : 0;
        var settings = new Av1EncoderSettings(width, height, bitDepth, monochrome, subsamplingX, subsamplingY, ColorPrimaries: 1, TransferCharacteristics: 13, MatrixCoefficients: lossless && format is "444" ? 0 : 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: false, Lossless: lossless, Quality: Math.Max(quality, 0), Effort: effort);
        var payloads = new List<byte[]>[2];
        for (var deferred = 0; deferred < 2; deferred++)
        {
            payloads[deferred] = [];
            using var encoder = new Av1Encoder(scope, settings) { DefersSymbols = deferred is 1 };
            for (var index = 0; index < 2; index++)
            {
                for (var plane = 0; plane < (monochrome ? 1 : 3); plane++)
                {
                    var buffer = encoder.GetSourcePlane(plane);
                    var samples = CreateSamples(content, buffer.Width, buffer.Height, bitDepth, seed: (plane * 7919) + width + index);
                    for (var y = 0; y < buffer.Height; y++)
                    {
                        samples.AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
                    }
                }

                encoder.BeginFrame();
                while (!encoder.Encode(XunitCancellationToken))
                {
                }

                payloads[deferred].Add(encoder.Payload.ToArray());
            }
        }

        for (var index = 0; index < 2; index++)
        {
            Assert.True(payloads[0][index].AsSpan().SequenceEqual(payloads[1][index]), $"{name}: frame {index} has {payloads[0][index].Length} bytes, and {payloads[1][index].Length} other bytes when its syntax elements are written last");
        }
    }

    [Theory]
    [InlineData(300, 283, 8, "420", 30, 0)]
    [InlineData(260, 261, 10, "444", 45, 1)]
    [InlineData(290, 250, 12, "400", 30, 2)]
    public void FramesDecodeToTheReconstructionWhateverTheNumberOfWorkers(int width, int height, int bitDepth, string format, int quality, int effort)
    {
        // A frame of more than 65,536 samples, coded coarsely at an effort that looks for the loop restoration, with heights
        // that are not a multiple of the 16 rows of a band of the post-processing filters: the frame of every number of
        // workers is the reconstruction of the encoder
        var name = $"workers-{width}x{height}-{bitDepth}-{format}-q{quality}-e{effort}";
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var monochrome = format is "400";
        var subsamplingX = format is "420" or "422" ? 1 : 0;
        var subsamplingY = format is "420" ? 1 : 0;
        var settings = new Av1EncoderSettings(width, height, bitDepth, monochrome, subsamplingX, subsamplingY, ColorPrimaries: 1, TransferCharacteristics: 13, MatrixCoefficients: 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Lossless: false, Quality: quality, Effort: effort);
        using var encoder = new Av1Encoder(scope, settings);
        var planeCount = monochrome ? 1 : 3;
        for (var plane = 0; plane < planeCount; plane++)
        {
            var buffer = encoder.GetSourcePlane(plane);
            var samples = CreateSamples("photo", buffer.Width, buffer.Height, bitDepth, seed: plane + height);
            for (var y = 0; y < buffer.Height; y++)
            {
                samples.AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
            }
        }

        encoder.BeginFrame();
        while (!encoder.Encode(XunitCancellationToken))
        {
        }

        var payload = encoder.Payload.ToArray();
        foreach (var workers in (int[])[1, 2, 3, 16])
        {
            using var decoder = new Av1Decoder(scope) { MaxDegreeOfParallelism = workers };
            decoder.SetInput(payload, 0, payload.Length);
            var frame = decoder.DecodeNextOutputFrame(XunitCancellationToken);
            Assert.NotNull(frame);
            try
            {
                var header = frame.Header;
                Assert.True(header.UsesLr, $"{name}: the frame has no loop restoration");
                Assert.True(header.CdefYPriStrength[0] is not 0 || header.CdefYSecStrength[0] is not 0 || header.CdefBits is not 0, $"{name}: the frame has no CDEF");
                for (var plane = 0; plane < planeCount; plane++)
                {
                    var decoded = frame.GetPlane(plane);
                    var reconstructed = encoder.Reconstruction.GetPlane(plane);
                    for (var y = 0; y < decoded.Height; y++)
                    {
                        Assert.True(reconstructed.GetRow(y).SequenceEqual(decoded.GetRow(y)), $"{name}: row {y} of plane {plane} decoded by {workers} workers is not the row that the encoder reconstructed");
                    }
                }
            }
            finally
            {
                frame.Release();
            }
        }
    }

    [Theory]
    [InlineData(96, 80, 8, "420", 30, 0)]
    [InlineData(200, 130, 8, "444", 60, 1)]
    [InlineData(300, 280, 10, "422", 45, 0)]
    [InlineData(260, 200, 12, "400", 30, 2)]
    [InlineData(150, 120, 12, "444", 50, 0)]
    public void UnitsOfALossyFrameAreRestoredByAWienerFilter(int width, int height, int bitDepth, string format, int quality, int effort)
    {
        // Edges on a smooth image, coded coarsely at an effort whose narrow search leaves errors that a filter of the
        // decoded frame reduces (the efforts 0 to 2 are the ones that look for it). The decoder applies the filters that
        // the units code: its frame is the reconstruction of the encoder, and it is closer to the source than the same
        // frame without the filters of its units
        var name = $"restoration-{width}x{height}-{bitDepth}-{format}-q{quality}-e{effort}";
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var monochrome = format is "400";
        var subsamplingX = format is "420" or "422" ? 1 : 0;
        var subsamplingY = format is "420" ? 1 : 0;
        var settings = new Av1EncoderSettings(width, height, bitDepth, monochrome, subsamplingX, subsamplingY, ColorPrimaries: 1, TransferCharacteristics: 13, MatrixCoefficients: 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Lossless: false, Quality: quality, Effort: effort);
        using var encoder = new Av1Encoder(scope, settings);
        using var decoder = new Av1Decoder(scope);
        using var unrestored = new Av1Decoder(scope) { InLoopFilters = Av1InLoopFilters.Deblock | Av1InLoopFilters.Cdef };
        var planeCount = monochrome ? 1 : 3;
        var max = (1 << bitDepth) - 1;
        var source = new ushort[planeCount][];
        for (var plane = 0; plane < planeCount; plane++)
        {
            var buffer = encoder.GetSourcePlane(plane);
            source[plane] = CreateSamples("photo", buffer.Width, buffer.Height, bitDepth, seed: plane + width);
            for (var y = 0; y < buffer.Height; y++)
            {
                for (var x = 0; x < buffer.Width; x++)
                {
                    if (((x / 19) + (y / 13)) % 3 is 0 || Math.Abs((2 * x) - y - 30) < 3)
                        source[plane][(y * buffer.Width) + x] = (ushort)Math.Clamp(source[plane][(y * buffer.Width) + x] + (max / 4) - (plane * max / 10), 0, max);
                }

                source[plane].AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
            }
        }

        encoder.BeginFrame();
        while (!encoder.Encode(XunitCancellationToken))
        {
        }

        var payload = encoder.Payload.ToArray();
        Assert.True(encoder.Sequence.EnableRestoration);
        Assert.True(encoder.RestoredUnitCount > 0, $"{name}: no unit has a filter");

        // A wider search does not look for filters, and a lossless frame has none
        using (var thorough = new Av1Encoder(scope, settings with { Effort = 5 }))
        using (var lossless = new Av1Encoder(scope, settings with { Lossless = true }))
        {
            Assert.False(thorough.Sequence.EnableRestoration);
            Assert.False(lossless.Sequence.EnableRestoration);
        }

        decoder.SetInput(payload, 0, payload.Length);
        unrestored.SetInput(payload, 0, payload.Length);
        var frame = decoder.DecodeNextOutputFrame(XunitCancellationToken);
        var plain = unrestored.DecodeNextOutputFrame(XunitCancellationToken);
        Assert.NotNull(frame);
        Assert.NotNull(plain);
        try
        {
            Assert.True(frame.Header.UsesLr);
            long restoredError = 0;
            long plainError = 0;
            for (var plane = 0; plane < planeCount; plane++)
            {
                var decoded = frame.GetPlane(plane);
                var reconstructed = encoder.Reconstruction.GetPlane(plane);
                var other = plain.GetPlane(plane);
                for (var y = 0; y < decoded.Height; y++)
                {
                    Assert.True(reconstructed.GetRow(y).SequenceEqual(decoded.GetRow(y)), $"{name}: row {y} of plane {plane} is not the row that the encoder reconstructed");
                    var expected = source[plane].AsSpan(y * decoded.Width, decoded.Width);
                    var actual = decoded.GetRow(y);
                    var unfiltered = other.GetRow(y);
                    for (var x = 0; x < expected.Length; x++)
                    {
                        restoredError += (long)(expected[x] - actual[x]) * (expected[x] - actual[x]);
                        plainError += (long)(expected[x] - unfiltered[x]) * (expected[x] - unfiltered[x]);
                    }
                }
            }

            TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {payload.Length} bytes, {encoder.RestoredUnitCount} units with a filter, squared error {plainError} without their filters and {restoredError} with them");
            Assert.True(restoredError < plainError, $"{name}: the squared error is {restoredError} with the filters of the units, {plainError} without");
        }
        finally
        {
            frame.Release();
            plain.Release();
        }
    }

    [Theory]
    [InlineData(448, 136, 8, "444", 5)]
    [InlineData(448, 136, 8, "420", 2)]
    [InlineData(520, 200, 10, "400", 9)]
    [InlineData(392, 190, 12, "422", 1)]
    public void BlocksOfALosslessScreenAreCopiedFromTheFrame(int width, int height, int bitDepth, string format, int effort)
    {
        var name = $"copy-{width}x{height}-{bitDepth}-{format}-e{effort}";
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var monochrome = format is "400";
        var subsamplingX = format is "420" or "422" ? 1 : 0;
        var subsamplingY = format is "420" ? 1 : 0;
        var settings = new Av1EncoderSettings(width, height, bitDepth, monochrome, subsamplingX, subsamplingY, ColorPrimaries: 1, TransferCharacteristics: 13, MatrixCoefficients: format is "444" ? 0 : 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Lossless: true, Quality: 100, Effort: effort);
        var planeCount = monochrome ? 1 : 3;
        var max = (1 << bitDepth) - 1;

        // A page of samples of three values, drawn at random. It repeats every 96 samples across and every 40 samples
        // down: no intra mode predicts it, and every block is somewhere else in the frame, at a distance that is not a
        // whole number of superblocks. The chroma planes repeat with the luma plane. The same page without repetition
        // is the same content for every other tool
        var sizes = new int[2];
        var tools = new Av1Tools[2];
        for (var repeats = 0; repeats < 2; repeats++)
        {
            using var encoder = new Av1Encoder(scope, settings);
            using var decoder = new Av1Decoder(scope);
            var source = new ushort[planeCount][];
            for (var plane = 0; plane < planeCount; plane++)
            {
                var buffer = encoder.GetSourcePlane(plane);
                var subX = plane > 0 ? subsamplingX : 0;
                var subY = plane > 0 ? subsamplingY : 0;
                var (periodX, periodY) = repeats is 1 ? (96 >> subX, 40 >> subY) : (buffer.Width, buffer.Height);
                var random = new Random(1234 + plane);
                var page = new ushort[periodX * periodY];
                ReadOnlySpan<int> levels = [max / 8, max - (max / 5), max / 2];
                for (var i = 0; i < page.Length; i++)
                {
                    page[i] = (ushort)levels[random.Next(levels.Length)];
                }

                source[plane] = new ushort[buffer.Width * buffer.Height];
                for (var y = 0; y < buffer.Height; y++)
                {
                    for (var x = 0; x < buffer.Width; x++)
                    {
                        source[plane][(y * buffer.Width) + x] = page[((y % periodY) * periodX) + (x % periodX)];
                    }

                    source[plane].AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
                }
            }

            encoder.BeginFrame();
            while (!encoder.Encode(XunitCancellationToken))
            {
            }

            var payload = encoder.Payload.ToArray();
            sizes[repeats] = payload.Length;
            decoder.SetInput(payload, 0, payload.Length);
            var frame = decoder.DecodeNextOutputFrame(XunitCancellationToken);
            Assert.NotNull(frame);
            try
            {
                Assert.True(frame.Header.AllowIntrabc, $"{name}: the frame does not allow intra block copy");
                Assert.True(frame.Header.CodedLossless);
                for (var plane = 0; plane < planeCount; plane++)
                {
                    var decoded = frame.GetPlane(plane);
                    var reconstructed = encoder.Reconstruction.GetPlane(plane);
                    for (var y = 0; y < decoded.Height; y++)
                    {
                        Assert.True(reconstructed.GetRow(y).SequenceEqual(decoded.GetRow(y)), $"{name}: row {y} of plane {plane} is not the row that the encoder reconstructed");
                        Assert.True(source[plane].AsSpan(y * decoded.Width, decoded.Width).SequenceEqual(decoded.GetRow(y)), $"{name}: row {y} of plane {plane} is not the encoded row");
                    }
                }
            }
            finally
            {
                frame.Release();
            }

            tools[repeats] = decoder.Tools;
        }

        // The first superblocks of the page that repeats have nothing to copy: the rest is most of the frame
        TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {sizes[0]} bytes ({tools[0]}), {sizes[1]} bytes when the page repeats ({tools[1]})");
        Assert.False(tools[0].HasFlag(Av1Tools.IntraBlockCopy), $"{name}: a block of a page that does not repeat is a copy");
        Assert.True(tools[1].HasFlag(Av1Tools.IntraBlockCopy), $"{name}: no block is a copy");
        Assert.True(sizes[1] * 2 < sizes[0], $"{name}: {sizes[1]} bytes when the page repeats, {sizes[0]} bytes otherwise");
    }

    [Theory]
    [InlineData(192, 144, 5, false)]
    [InlineData(256, 192, 5, false)]
    [InlineData(256, 256, 5, true)]
    [InlineData(1024, 64, 5, true)]
    [InlineData(1024, 64, 0, false)]
    public void IntraBlockCopyIsForFramesOfSixteenSuperblocks(int width, int height, int effort, bool expected)
    {
        // A block copies samples that are five superblocks behind it: a frame of a few superblocks does not pay for the
        // flag that each of its blocks would code, and the fastest effort does not look for copies
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(IntraBlockCopyIsForFramesOfSixteenSuperblocks));
        var settings = new Av1EncoderSettings(width, height, 8, Monochrome: true, 1, 1, 1, 13, 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Lossless: true, Quality: 100, Effort: effort);
        using var encoder = new Av1Encoder(scope, settings);
        using var decoder = new Av1Decoder(scope);
        var buffer = encoder.GetSourcePlane(0);
        for (var y = 0; y < buffer.Height; y++)
        {
            var row = buffer.GetRow(y);
            for (var x = 0; x < row.Length; x++)
            {
                row[x] = (ushort)((((x / 5) + (y / 7)) % 3) * 100);
            }
        }

        encoder.BeginFrame();
        while (!encoder.Encode(XunitCancellationToken))
        {
        }

        var payload = encoder.Payload.ToArray();
        decoder.SetInput(payload, 0, payload.Length);
        var frame = decoder.DecodeNextOutputFrame(XunitCancellationToken);
        Assert.NotNull(frame);
        try
        {
            Assert.True(frame.Header.AllowScreenContentTools);
            Assert.Equal(expected, frame.Header.AllowIntrabc);
            for (var y = 0; y < buffer.Height; y++)
            {
                Assert.True(buffer.GetRow(y).SequenceEqual(frame.GetPlane(0).GetRow(y)), $"row {y} is not the encoded row");
            }
        }
        finally
        {
            frame.Release();
        }
    }

    [Fact]
    public void APhotographIsNotCodedWithTheToolsOfScreenContent()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(APhotographIsNotCodedWithTheToolsOfScreenContent));
        var settings = new Av1EncoderSettings(96, 80, 8, Monochrome: true, 1, 1, 1, 13, 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Lossless: true, Quality: 100, Effort: 1);
        using var encoder = new Av1Encoder(scope, settings);
        var buffer = encoder.GetSourcePlane(0);
        var samples = CreateSamples("photo", buffer.Width, buffer.Height, 8, seed: 4);
        for (var y = 0; y < buffer.Height; y++)
        {
            samples.AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
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
            Assert.False(frame.Header.AllowScreenContentTools);
        }
        finally
        {
            frame.Release();
        }
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
