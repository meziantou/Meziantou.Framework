using System.Globalization;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The HEVC encoder against the HEVC decoder of the library, which the conformance bitstreams pin to the hashes of their
/// pictures: a stream of the encoder must decode, to the picture that the encoder reconstructed, and for a lossless stream
/// to the samples that were encoded. The independent decoders (FFmpeg, libde265 and the HEVC Test Model) decode the output
/// of the encoder in the interop tests.
/// </summary>
public sealed class HevcEncoderRoundTripTests
{
    private static readonly string[] Formats = ["400", "420", "422", "444"];

    public static TheoryData<int, int, int, string, string, int> LosslessCases()
    {
        var data = new TheoryData<int, int, int, string, string, int>();
        (int Width, int Height)[] sizes = [(1, 1), (7, 5), (8, 8), (17, 13), (32, 32), (33, 31), (64, 64), (65, 67), (130, 70)];
        string[] contents = ["flat", "gradient", "noise", "blocks", "photo", "text", "edges"];
        var index = 0;
        foreach (var (width, height) in sizes)
        {
            foreach (var format in Formats)
            {
                // Every size and format, with a bit depth, a content and an effort that rotate
                data.Add(width, height, 8 + (2 * (index % 3)), format, contents[index % contents.Length], index % 10);
                index++;
            }
        }

        // The whole effort ladder on a picture of several coding tree units, and every content at each bit depth
        for (var effort = 0; effort <= 9; effort++)
        {
            data.Add(72, 56, 8, "420", "photo", effort);
        }

        foreach (var content in contents)
        {
            data.Add(40, 24, 8, "444", content, 4);
            data.Add(40, 24, 10, "420", content, 6);
            data.Add(40, 24, 12, "422", content, 2);
            data.Add(40, 24, 10, "400", content, 8);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LosslessCases))]
    public void LosslessFramesDecodeToTheEncodedSamples(int width, int height, int bitDepth, string format, string content, int effort)
    {
        var name = $"{width}x{height}-{bitDepth}-{format}-{content}-e{effort}";
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var session = new HevcEncoderTestSession(HevcEncoderTestSession.CreateSettings(width, height, bitDepth, format, lossless: true, quality: 100, effort));
        session.Encode(scope, 1, (_, plane, planeWidth, planeHeight) => HevcEncoderTestSession.CreateSamples(content, planeWidth, planeHeight, bitDepth, seed: (plane * 7919) + width + (31 * height)), XunitCancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {session.PayloadLength} bytes for {session.Sources[0].Sum(plane => plane.Length)} samples");

        // A call to start, one for each row of coding tree units, and the call that finishes
        Assert.Equal(2 + ((height + 31) / 32), session.Steps[0]);
        Assert.True(session.KeyFrames[0]);
        var decoded = HevcTestDecoder.Decode(session.ToByteStream(), scope, out var tools);
        Assert.HasCount(1, decoded);
        AssertDecodesToTheReconstruction(session, decoded, name);
        AssertReconstructionIsTheSource(session, 0, name);
        Assert.True(tools.HasFlag(HevcTools.TransquantBypass));
        Assert.Equal(HevcTools.None, tools & (HevcTools.Deblocking | HevcTools.SaoBandOffset | HevcTools.SaoEdgeOffset | HevcTools.Pcm));
    }

    public static TheoryData<int, int, int, string, string, int, int> LossyCases()
    {
        var data = new TheoryData<int, int, int, string, string, int, int>();
        (int Width, int Height)[] sizes = [(1, 1), (7, 5), (16, 16), (17, 13), (32, 32), (33, 31), (64, 64), (65, 67), (130, 70)];
        string[] contents = ["photo", "gradient", "noise", "blocks", "flat", "text", "edges"];
        int[] qualities = [50, 10, 90, 30, 100, 0, 70];
        var index = 0;
        foreach (var (width, height) in sizes)
        {
            foreach (var format in Formats)
            {
                // Every size and format, with a bit depth, a content, a quality and an effort that rotate: the highest
                // effort gets the smallest pictures
                var effort = (index * 3) % 10;
                data.Add(effort is 9 ? Math.Min(width, 65) : width, effort is 9 ? Math.Min(height, 35) : height, 8 + (2 * (index % 3)), format, contents[index % contents.Length], qualities[index % qualities.Length], effort);
                index++;
            }
        }

        // The whole effort ladder at two qualities, and every content at each bit depth
        for (var effort = 0; effort <= 9; effort++)
        {
            data.Add(72, 56, 8, "420", "photo", 40, effort);
            data.Add(40, 40, 10, "444", "text", 85, effort);
        }

        foreach (var content in contents)
        {
            data.Add(40, 24, 8, "444", content, 60, 4);
            data.Add(40, 24, 10, "420", content, 35, 6);
            data.Add(40, 24, 12, "422", content, 80, 2);
            data.Add(40, 24, 12, "400", content, 20, 8);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LossyCases))]
    public void LossyFramesDecodeToTheReconstructionOfTheEncoder(int width, int height, int bitDepth, string format, string content, int quality, int effort)
    {
        var name = $"{width}x{height}-{bitDepth}-{format}-{content}-q{quality}-e{effort}";
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var session = new HevcEncoderTestSession(HevcEncoderTestSession.CreateSettings(width, height, bitDepth, format, lossless: false, quality, effort));
        session.Encode(scope, 1, (_, plane, planeWidth, planeHeight) => HevcEncoderTestSession.CreateSamples(content, planeWidth, planeHeight, bitDepth, seed: (plane * 7919) + width + (31 * height)), XunitCancellationToken);
        var psnr = session.GetPsnr();
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {session.PayloadLength} bytes for {session.Sources[0].Sum(plane => plane.Length)} samples, {psnr:F2} dB"));
        Assert.True(session.KeyFrames[0]);
        var decoded = HevcTestDecoder.Decode(session.ToByteStream(), scope, out var tools);
        AssertDecodesToTheReconstruction(session, decoded, name);
        Assert.Equal(HevcTools.None, tools & (HevcTools.TransquantBypass | HevcTools.Pcm));

        // The picture is the one that was encoded, at the precision of its quality: noise is the hardest content
        Assert.True(psnr >= (content is "noise" or "text" ? 7 : 16) + (quality / 5), string.Create(CultureInfo.InvariantCulture, $"{name}: the picture is not close to the one that was encoded ({psnr:F2} dB)"));
    }

    public static TheoryData<int, int, int, string, string, int, int> SequenceCases()
    {
        var data = new TheoryData<int, int, int, string, string, int, int>();
        (int Width, int Height)[] sizes = [(48, 40), (17, 13), (64, 64), (72, 56), (130, 70), (8, 8)];
        string[] motions = ["pan", "static", "object", "drift", "cut", "noise"];
        int[] qualities = [50, -1, 20, 85, -1, 65];
        var index = 0;
        foreach (var (width, height) in sizes)
        {
            foreach (var format in Formats)
            {
                // Every size and format, with a bit depth, a motion, a quality (-1 for lossless) and an effort that rotate:
                // the highest efforts get the smallest pictures
                var effort = (index * 3) % 10;
                var small = effort >= 7;
                data.Add(small ? Math.Min(width, 40) : width, small ? Math.Min(height, 24) : height, 8 + (2 * (index % 3)), format, motions[index % motions.Length], qualities[index % qualities.Length], effort);
                index++;
            }
        }

        // The whole effort ladder, lossy and lossless, and every motion
        for (var effort = 0; effort <= 9; effort++)
        {
            data.Add(effort >= 7 ? 40 : 72, effort >= 7 ? 24 : 56, 8, "420", "pan", 45, effort);
            data.Add(effort >= 7 ? 24 : 40, effort >= 7 ? 24 : 40, 10, "444", "object", -1, effort);
        }

        foreach (var motion in motions)
        {
            data.Add(56, 40, 8, "420", motion, 60, 5);
            data.Add(56, 40, 10, "422", motion, -1, 3);
            data.Add(40, 24, 12, "400", motion, 30, 7);
        }

        return data;
    }

    [Theory]
    [InlineData(8, "420", 0, false)]
    [InlineData(10, "422", 6, false)]
    [InlineData(12, "400", 5, true)]
    [InlineData(8, "444", 7, true)]
    public void OptimizedQuantizationDecodesToTheReconstruction(int bitDepth, string format, int effort, bool wavefront)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(OptimizedQuantizationDecodesToTheReconstruction));
        var settings = HevcEncoderTestSession.CreateSettings(40, 40, bitDepth, format, lossless: false, quality: 40, effort, stillPicture: false) with
        {
            OptimizeQuantization = true,
            Wavefront = wavefront,
        };
        var session = new HevcEncoderTestSession(settings);
        session.Encode(scope, 3, (index, plane, width, height) => HevcEncoderTestSession.CreateSamples(index is 0 ? "photo" : "text", width, height, bitDepth, seed: plane + index), XunitCancellationToken);
        var decoded = HevcTestDecoder.Decode(session.ToByteStream(), scope, out _);
        AssertDecodesToTheReconstruction(session, decoded, $"{bitDepth}-{format}-{effort}-{wavefront}");
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    public void OptimizedQuantizationImprovesAggregateRateDistortion(int bitDepth)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(OptimizedQuantizationImprovesAggregateRateDistortion));
        long baselineCost = 0;
        long optimizedCost = 0;
        foreach (var content in new[] { "photo", "gradient", "noise", "blocks", "flat", "text", "edges" })
        {
            foreach (var quality in new[] { 25, 50, 75 })
            {
                var settings = HevcEncoderTestSession.CreateSettings(48, 32, bitDepth, "420", lossless: false, quality, effort: 2);
                var decisions = HevcEncoderDecisions.Create(settings);
                var baseline = Encode(optimized: false);
                var optimized = Encode(optimized: true);
                baselineCost += Cost(baseline);
                optimizedCost += Cost(optimized);
                TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{content} q{quality}: {baseline.PayloadLength} -> {optimized.PayloadLength} bytes; {baseline.GetPsnr():F2} -> {optimized.GetPsnr():F2} dB"));

                HevcEncoderTestSession Encode(bool optimized)
                {
                    var session = new HevcEncoderTestSession(settings with { OptimizeQuantization = optimized });
                    session.Encode(scope, 1, (_, plane, width, height) => HevcEncoderTestSession.CreateSamples(content, width, height, bitDepth, seed: 11 + plane), XunitCancellationToken);
                    return session;
                }

                long Cost(HevcEncoderTestSession session) => decisions.GetCost(session.GetSquaredError(out _), session.PayloadLength * 8 * HevcCost.Scale);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{baselineCost} -> {optimizedCost}");
        Assert.True(optimizedCost < baselineCost, $"{baselineCost} -> {optimizedCost}");
    }

    [Fact]
    public void QuantizationOptimizationDoesNotChangeLosslessOutput()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(QuantizationOptimizationDoesNotChangeLosslessOutput));
        var settings = HevcEncoderTestSession.CreateSettings(24, 16, 10, "444", lossless: true, quality: 100, effort: 2);
        var baseline = new HevcEncoderTestSession(settings);
        var optimized = new HevcEncoderTestSession(settings with { OptimizeQuantization = true });
        foreach (var session in new[] { baseline, optimized })
        {
            session.Encode(scope, 1, (_, plane, width, height) => HevcEncoderTestSession.CreateSamples("noise", width, height, 10, seed: plane), XunitCancellationToken);
            AssertReconstructionIsTheSource(session, 0, nameof(QuantizationOptimizationDoesNotChangeLosslessOutput));
        }

        Assert.Equal(baseline.ToByteStream(), optimized.ToByteStream());
    }

    [Theory]
    [MemberData(nameof(SequenceCases))]
    public void FramesOfASequenceDecodeToTheReconstructionOfTheEncoder(int width, int height, int bitDepth, string format, string motion, int quality, int effort)
    {
        var lossless = quality < 0;
        var name = $"{width}x{height}-{bitDepth}-{format}-{motion}-{(lossless ? "lossless" : "q" + quality.ToString(CultureInfo.InvariantCulture))}-e{effort}";
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var session = new HevcEncoderTestSession(HevcEncoderTestSession.CreateSettings(width, height, bitDepth, format, lossless, Math.Max(quality, 0), effort, stillPicture: false));
        session.Encode(scope, 4, (index, plane, planeWidth, planeHeight) => HevcEncoderTestSession.CreateFrame(motion, index, planeWidth, planeHeight, plane is 0 ? 1 : session.SubWidthC, plane is 0 ? 1 : session.SubHeightC, bitDepth, seed: (plane * 7919) + width), XunitCancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {string.Join(" + ", session.Payloads.Select(payload => payload.Length))} bytes, {session.GetPsnr():F2} dB"));
        Assert.Equal([true, false, false, false], session.KeyFrames);
        var decoded = HevcTestDecoder.Decode(session.ToByteStream(), scope, out var tools);
        AssertDecodesToTheReconstruction(session, decoded, name);
        Assert.True(tools.HasFlag(HevcTools.PSlices));
        for (var index = 0; lossless && index < session.Sources.Count; index++)
        {
            AssertReconstructionIsTheSource(session, index, name);
        }

        // A picture that is the previous one, or the previous one a little further, costs less than a key frame
        if (motion is "static" or "pan" && width >= 48)
        {
            Assert.HasCountLessThan(session.Payloads[0].Length, session.Payloads[1], $"{name}: the second picture is not smaller than the key frame");
        }
    }

    [Fact]
    public void KeyFramesFollowTheInterval()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(KeyFramesFollowTheInterval));
        foreach (var (interval, expected) in new[] { (0, "KPPPPPP"), (1, "KKKKKKK"), (3, "KPPKPPK") })
        {
            var session = new HevcEncoderTestSession(HevcEncoderTestSession.CreateSettings(24, 16, 8, "400", lossless: true, quality: 100, effort: 0, stillPicture: false, keyFrameInterval: interval));
            session.Encode(scope, expected.Length, (index, _, planeWidth, planeHeight) => HevcEncoderTestSession.CreateFrame("pan", index, planeWidth, planeHeight, 1, 1, 8, seed: 11), XunitCancellationToken);
            Assert.Equal(expected, string.Concat(session.KeyFrames.Select(keyFrame => keyFrame ? "K" : "P")));

            // The access unit of a key frame is an IDR picture, which a decoder can start from; the other ones refer to the
            // picture before them
            for (var index = 0; index < expected.Length; index++)
            {
                var nalUnitType = (session.Payloads[index][HevcNalWriter.NalLengthSize] >> 1) & 0x3F;
                Assert.Equal(expected[index] is 'K' ? HevcConstants.NalIdrNLp : HevcConstants.NalTrailR, nalUnitType);
            }

            var decoded = HevcTestDecoder.Decode(session.ToByteStream(), scope, out _);
            AssertDecodesToTheReconstruction(session, decoded, $"interval {interval}");
            for (var index = 0; index < expected.Length; index++)
            {
                AssertReconstructionIsTheSource(session, index, $"interval {interval}");
            }
        }
    }

    [Fact]
    public void EncodingIsDeterministic()
    {
        var first = Encode(nameof(EncodingIsDeterministic), 77, 45, 8, "420", "pan", quality: 55, effort: 5, pictureCount: 3);
        var second = Encode(nameof(EncodingIsDeterministic), 77, 45, 8, "420", "pan", quality: 55, effort: 5, pictureCount: 3);
        Assert.Equal(first.ToByteStream(), second.ToByteStream());
    }

    [Fact]
    public void AHigherEffortDoesNotProduceALargerLosslessPicture()
    {
        var sizes = new[] { 0, 4, 8 }.Select(effort => Encode(nameof(AHigherEffortDoesNotProduceALargerLosslessPicture), 128, 96, 8, "420", "photo", quality: -1, effort).PayloadLength).ToArray();
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(" >= ", sizes));
        Assert.InRange(sizes[1], sizes[2], sizes[0]);
    }

    [Theory]
    [InlineData("photo", 1)]
    [InlineData("text", 1)]
    [InlineData("edges", 1)]
    [InlineData("pan", 3)]
    [InlineData("object", 3)]
    public void AHigherEffortDoesNotCostMore(string content, int pictureCount)
    {
        // What the search minimizes: the squared error and the size of the pictures, weighted as the encoder weighs them.
        // The efforts have the same quantization parameter: a higher one only finds better decisions.
        var costs = new List<long>();
        foreach (var effort in new[] { 0, 3, 6 })
        {
            var session = Encode(nameof(AHigherEffortDoesNotCostMore), 96, 80, 8, "420", content, quality: 50, effort, pictureCount);
            var cost = HevcEncoderDecisions.Create(session.Settings).GetCost(session.GetSquaredError(out _), session.PayloadLength * 8 * HevcCost.Scale);
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"effort {effort}: {session.PayloadLength} bytes, {session.GetPsnr():F2} dB, cost {cost}"));
            costs.Add(cost);
        }

        // Within the noise of decisions that are made block after block
        Assert.True(costs[1] <= costs[0] + (costs[0] / 50) && costs[2] <= costs[1] + (costs[1] / 50), $"The costs of the efforts 0, 3 and 6 are {string.Join(", ", costs)}");
    }

    [Theory]
    [InlineData("photo", "420", 8, 1)]
    [InlineData("gradient", "444", 10, 1)]
    [InlineData("text", "400", 12, 1)]
    [InlineData("pan", "422", 8, 3)]
    public void AHigherQualityIsCloserToTheSource(string content, string format, int bitDepth, int pictureCount)
    {
        var previousPsnr = 0.0;
        var previousLength = 0L;
        foreach (var quality in new[] { 5, 25, 45, 65, 85, 100 })
        {
            var session = Encode(nameof(AHigherQualityIsCloserToTheSource), 64, 48, bitDepth, format, content, quality, effort: 3, pictureCount);
            var psnr = session.GetPsnr();
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quality {quality}: {session.PayloadLength} bytes, {psnr:F2} dB"));
            Assert.True(psnr >= previousPsnr, string.Create(CultureInfo.InvariantCulture, $"The quality {quality} is further from the source ({psnr:F2} dB) than the quality before it ({previousPsnr:F2} dB)"));
            Assert.True(session.PayloadLength >= previousLength, $"The quality {quality} is smaller ({session.PayloadLength} bytes) than the quality before it ({previousLength} bytes)");
            previousPsnr = psnr;
            previousLength = session.PayloadLength;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void TheSearchUsesTheToolsOfItsEffort(int effort)
    {
        // Text over a photograph, which moves by a quarter of a sample from a picture to the next
        var session = Encode(nameof(TheSearchUsesTheToolsOfItsEffort), 48, 32, 8, "420", "drift", quality: 60, effort, pictureCount: 3);
        var decoded = HevcTestDecoder.Decode(session.ToByteStream(), AllocationScope.Create(ImageConfiguration.Default, nameof(TheSearchUsesTheToolsOfItsEffort)), out var tools);
        AssertDecodesToTheReconstruction(session, decoded, $"effort {effort}");
        TestContext.Current.TestOutputHelper?.WriteLine($"effort {effort}: {tools}");
        const HevcTools Always = HevcTools.PSlices | HevcTools.Deblocking | HevcTools.IntraReferenceSubstitution | HevcTools.ReferencePadding;
        const HevcTools HighEffort = HevcTools.SignDataHiding | HevcTools.TransformSkip | HevcTools.IntraNxN | HevcTools.FractionalInterpolation | HevcTools.InterMvd | HevcTools.InterTransformSplit;
        const HevcTools HighEffortOnly = HighEffort | HevcTools.SaoEdgeOffset | HevcTools.SaoBandOffset | HevcTools.SaoMerge | HevcTools.InterRectangularPartitions | HevcTools.InterAsymmetricPartitions | HevcTools.TemporalMvp;
        Assert.Equal(Always, tools & Always);
        if (effort is 0)
        {
            Assert.Equal(HevcTools.None, tools & (HighEffortOnly & ~HevcTools.InterMvd));
        }
        else
        {
            Assert.Equal(HighEffort, tools & HighEffort);
            Assert.NotEqual(HevcTools.None, tools & (HevcTools.SaoEdgeOffset | HevcTools.SaoBandOffset));
        }

        // Neither effort uses what a decoder in use may not have
        const HevcTools Never = HevcTools.Pcm | HevcTools.TransquantBypass | HevcTools.ScalingList | HevcTools.CuQpDelta | HevcTools.ImplicitRdpcm | HevcTools.ExplicitRdpcm | HevcTools.TransformSkipRotation | HevcTools.TransformSkipContext
            | HevcTools.CrossComponentPrediction | HevcTools.ChromaQpOffsetList | HevcTools.PersistentRiceAdaptation | HevcTools.SaoOffsetScale | HevcTools.IntraSmoothingDisabled | HevcTools.LargeTransformSkip | HevcTools.BSlices
            | HevcTools.InterBiPrediction | HevcTools.InterNxN | HevcTools.ExplicitWeightedPrediction | HevcTools.LongTermReferences | HevcTools.ReferenceListModification | HevcTools.MultipleReferences | HevcTools.PictureReordering
            | HevcTools.Tiles | HevcTools.Wavefront | HevcTools.MultipleSlices | HevcTools.DependentSliceSegments | HevcTools.ConstrainedIntraPred;
        Assert.Equal(HevcTools.None, tools & Never);
    }

    public static TheoryData<string, int, string, string> ProfileCases()
    {
        // Annex A: the profiles of a single picture, of the sequences of key frames, and of the other sequences
        return new TheoryData<string, int, string, string>
        {
            { "400", 8, "still", nameof(HevcProfile.Monochrome) },
            { "400", 10, "intra", nameof(HevcProfile.Monochrome10) },
            { "400", 12, "sequence", nameof(HevcProfile.Monochrome12) },
            { "420", 8, "still", nameof(HevcProfile.MainStillPicture) },
            { "420", 8, "intra", nameof(HevcProfile.Main) },
            { "420", 8, "sequence", nameof(HevcProfile.Main) },
            { "420", 10, "still", nameof(HevcProfile.Main10StillPicture) },
            { "420", 10, "sequence", nameof(HevcProfile.Main10) },
            { "420", 12, "still", nameof(HevcProfile.Main12Intra) },
            { "420", 12, "sequence", nameof(HevcProfile.Main12) },
            { "422", 8, "still", nameof(HevcProfile.Main42210Intra) },
            { "422", 10, "sequence", nameof(HevcProfile.Main42210) },
            { "422", 12, "intra", nameof(HevcProfile.Main42212Intra) },
            { "422", 12, "sequence", nameof(HevcProfile.Main42212) },
            { "444", 8, "still", nameof(HevcProfile.Main444StillPicture) },
            { "444", 8, "intra", nameof(HevcProfile.Main444Intra) },
            { "444", 8, "sequence", nameof(HevcProfile.Main444) },
            { "444", 10, "still", nameof(HevcProfile.Main44410Intra) },
            { "444", 10, "sequence", nameof(HevcProfile.Main44410) },
            { "444", 12, "intra", nameof(HevcProfile.Main44412Intra) },
            { "444", 12, "sequence", nameof(HevcProfile.Main44412) },
        };
    }

    [Theory]
    [MemberData(nameof(ProfileCases))]
    public void TheStreamOfEveryFormatHasTheNarrowestProfile(string format, int bitDepth, string kind, string expected)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(TheStreamOfEveryFormatHasTheNarrowestProfile));
        foreach (var lossless in new[] { false, true })
        {
            for (var effort = 0; effort <= 9; effort += 3)
            {
                using var encoder = new HevcEncoder(scope, HevcEncoderTestSession.CreateSettings(40, 24, bitDepth, format, lossless, quality: 50, effort, stillPicture: kind is "still", keyFrameInterval: kind is "intra" ? 1 : 0));
                Assert.Equal(expected, encoder.Parameters.Profile.ToString());
                Assert.Equal(HevcCodingTools.None, encoder.Parameters.Tools & HevcCodingTools.ExtendedTools);

                // The parameter sets, as a decoder reads them: none of the tools of the range extensions, whatever the profile
                var sps = HevcSequenceParameterSet.Parse(scope, encoder.SequenceParameterSet.Span);
                Assert.Equal(encoder.Parameters.GeneralProfileIdc, sps.ProfileTierLevel.GeneralProfileIdc);
                Assert.Equal(30, sps.ProfileTierLevel.GeneralLevelIdc);
                Assert.False(sps.TransformSkipRotationEnabledFlag || sps.TransformSkipContextEnabledFlag || sps.ImplicitRdpcmEnabledFlag || sps.ExplicitRdpcmEnabledFlag);
                Assert.False(sps.IntraSmoothingDisabledFlag || sps.HighPrecisionOffsetsEnabledFlag || sps.PersistentRiceAdaptationEnabledFlag || sps.PcmEnabledFlag || sps.ScalingListEnabledFlag);
                Assert.Equal((40, 24), (sps.OutputWidth, sps.OutputHeight));
            }
        }
    }

    public static TheoryData<int, int> SizeCases()
    {
        var data = new TheoryData<int, int>();
        foreach (var (width, height) in new[] { (1, 1), (2, 2), (1, 64), (64, 1), (9, 7), (31, 33), (32, 32), (33, 31), (63, 65), (64, 64), (65, 63), (127, 9), (129, 8) })
        {
            data.Add(width, height);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SizeCases))]
    public void PicturesOfEverySizeDecodeToTheReconstructionOfTheEncoder(int width, int height)
    {
        // A lossless picture, a lossy one, and two sequences, whose coding tree blocks have 64 samples
        foreach (var (quality, pictureCount, format) in new[] { (-1, 1, "420"), (40, 1, "444"), (70, 2, "420"), (-1, 2, "422") })
        {
            var name = $"{width}x{height}-{format}-{quality}-{pictureCount}";
            var session = Encode(name, width, height, 8, format, pictureCount > 1 ? "pan" : "photo", quality, effort: 3, pictureCount);
            var decoded = HevcTestDecoder.Decode(session.ToByteStream(), AllocationScope.Create(ImageConfiguration.Default, name), out _);
            AssertDecodesToTheReconstruction(session, decoded, name);
            for (var index = 0; quality < 0 && index < pictureCount; index++)
            {
                AssertReconstructionIsTheSource(session, index, name);
            }
        }
    }

    // Width, height, format, quality (negative for lossless), effort and number of pictures
    public static TheoryData<int, int, string, int, int, int> WavefrontCases => new()
    {
        // Lossy: the slice segment data are written once the picture is deblocked, with the sample adaptive offset
        { 300, 170, "420", 60, 5, 1 },
        { 72, 260, "400", 30, 8, 1 },
        { 130, 70, "422", 80, 0, 1 },

        // Lossless: each coding tree unit is written once it is searched
        { 300, 170, "444", -1, 2, 1 },
        { 40, 140, "420", -1, 4, 1 },

        // P pictures
        { 120, 140, "420", 50, 4, 3 },
        { 200, 100, "422", -1, 6, 2 },
    };

    [Theory]
    [MemberData(nameof(WavefrontCases))]
    public void RowsOfTheWavefrontDecodeToTheReconstructionOfTheEncoder(int width, int height, string format, int quality, int effort, int pictureCount)
    {
        var name = $"{width}x{height}-{format}-{quality}-e{effort}-{pictureCount}";
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var settings = HevcEncoderTestSession.CreateSettings(width, height, 8, format, quality < 0, Math.Max(quality, 0), effort, stillPicture: pictureCount is 1) with { Wavefront = true };
        var session = new HevcEncoderTestSession(settings);
        session.Encode(scope, pictureCount, (index, plane, planeWidth, planeHeight) => pictureCount is 1
            ? HevcEncoderTestSession.CreateSamples("photo", planeWidth, planeHeight, 8, seed: (plane * 7919) + width + (31 * height))
            : HevcEncoderTestSession.CreateFrame("pan", index, planeWidth, planeHeight, plane is 0 ? 1 : session.SubWidthC, plane is 0 ? 1 : session.SubHeightC, 8, seed: (plane * 7919) + width), XunitCancellationToken);
        var plain = Encode(name, width, height, 8, format, pictureCount is 1 ? "photo" : "pan", quality, effort, pictureCount);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {session.PayloadLength} bytes with the wavefront, {plain.PayloadLength} without, {session.GetPsnr():F2} and {plain.GetPsnr():F2} dB"));
        HevcDecoder.TestIgnoreMinimumSize = true;
        try
        {
            foreach (var workers in (int[])[1, 2, 3, 16])
            {
                var decoded = HevcTestDecoder.Decode(session.ToByteStream(), scope, out var tools, maxDegreeOfParallelism: workers);
                AssertDecodesToTheReconstruction(session, decoded, $"{name} by {workers} workers");
                Assert.True(tools.HasFlag(HevcTools.Wavefront), $"{name}: the rows are not substreams of the wavefront.");
            }
        }
        finally
        {
            HevcDecoder.TestIgnoreMinimumSize = false;
        }

        // A lossless picture is still its source
        for (var index = 0; quality < 0 && index < pictureCount; index++)
        {
            AssertReconstructionIsTheSource(session, index, name);
        }
    }

    [Fact]
    public void TheAccessUnitsDecodeAsTheSamplesOfAFile()
    {
        // The parameter sets of a configuration record, then the access unit of each picture: no hash, no byte stream
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(TheAccessUnitsDecodeAsTheSamplesOfAFile));
        var session = Encode(nameof(TheAccessUnitsDecodeAsTheSamplesOfAFile), 70, 50, 10, "420", "object", quality: 45, effort: 6, pictureCount: 4);
        using var decoder = new HevcDecoder(scope);
        foreach (var parameterSet in session.ParameterSets)
        {
            decoder.AddParameterSet(parameterSet);
        }

        for (var index = 0; index < session.Payloads.Count; index++)
        {
            decoder.SetInput(session.Payloads[index], 0, session.Payloads[index].Length, HevcNalWriter.NalLengthSize);
            if (index == session.Payloads.Count - 1)
            {
                decoder.EndOfSequence();
            }

            var picture = decoder.DecodeNextOutputPicture(XunitCancellationToken);
            Assert.NotNull(picture);
            try
            {
                for (var plane = 0; plane < picture.PlaneCount; plane++)
                {
                    var samples = picture.GetPlane(plane);
                    for (var y = 0; y < samples.Height; y++)
                    {
                        Assert.True(samples.GetRow(y).SequenceEqual(session.Reconstructions[index][plane].AsSpan(y * samples.Width, samples.Width)), $"The row {y} of the plane {plane} of the picture {index} is not the one that the encoder reconstructed");
                    }
                }
            }
            finally
            {
                picture.Release();
            }
        }
    }

    [Fact]
    public void TheEncoderChecksItsSettingsAndItsState()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(TheEncoderChecksItsSettingsAndItsState));
        var settings = HevcEncoderTestSession.CreateSettings(16, 16, 8, "420", lossless: false, quality: 50, effort: 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => new HevcEncoder(scope, settings with { Width = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HevcEncoder(scope, settings with { Height = 70000 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HevcEncoder(scope, settings with { BitDepth = 9 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HevcEncoder(scope, settings with { ChromaFormatIdc = 4 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HevcEncoder(scope, settings with { Quality = 101 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HevcEncoder(scope, settings with { KeyFrameInterval = -1 }));

        // An effort beyond the ladder is its closest end
        using (var clamped = new HevcEncoder(scope, settings with { Effort = 42 }))
        {
            using var highest = new HevcEncoder(scope, settings with { Effort = 9 });
            Assert.Equal(highest.PictureParameterSet.ToArray(), clamped.PictureParameterSet.ToArray());
            Assert.Equal(highest.SequenceParameterSet.ToArray(), clamped.SequenceParameterSet.ToArray());
        }

        using var encoder = new HevcEncoder(scope, settings);
        Assert.Throws<InvalidOperationException>(() => encoder.Payload);
        Assert.Throws<InvalidOperationException>(() => encoder.Reconstruction);
        Assert.Throws<InvalidOperationException>(() => encoder.Encode(XunitCancellationToken));
        encoder.BeginFrame();
        Assert.Throws<InvalidOperationException>(encoder.BeginFrame);
        while (!encoder.Encode(XunitCancellationToken))
        {
            Assert.Throws<InvalidOperationException>(() => encoder.Payload);
        }

        Assert.False(encoder.Payload.IsEmpty);
        Assert.Throws<InvalidOperationException>(() => encoder.Encode(XunitCancellationToken));

        // A still picture is one picture
        Assert.Throws<InvalidOperationException>(encoder.BeginFrame);
    }

    [Theory]
    [InlineData(-1, 0, "444", true)]
    [InlineData(60, 5, "420", true)]
    [InlineData(40, 9, "420", true)]
    [InlineData(60, 5, "420", false)]
    public void AResetEncoderCodesItsNextPictureAsANewEncoder(int quality, int effort, string format, bool stillPicture)
    {
        // The tiles of a grid are coded one after the other by one encoder: after a reset, a picture is coded as by a new
        // encoder, whatever the encoder coded before (a still picture, or the pictures of a sequence)
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(AResetEncoderCodesItsNextPictureAsANewEncoder));
        var settings = HevcEncoderTestSession.CreateSettings(66, 50, 8, format, quality < 0, Math.Max(quality, 0), effort, stillPicture);
        using var reused = new HevcEncoder(scope, settings);
        EncodePicture(reused, seed: 1);
        if (!stillPicture)
        {
            EncodePicture(reused, seed: 2);
            Assert.False(reused.IsKeyFrame);
        }

        reused.Reset();
        Assert.Throws<InvalidOperationException>(() => reused.Payload);
        Assert.Throws<InvalidOperationException>(() => reused.Reconstruction);
        var (payload, reconstruction) = EncodePicture(reused, seed: 3);
        Assert.True(reused.IsKeyFrame);

        using var fresh = new HevcEncoder(scope, settings);
        var (expectedPayload, expectedReconstruction) = EncodePicture(fresh, seed: 3);
        Assert.Equal(expectedPayload, payload);
        Assert.Equal(expectedReconstruction, reconstruction);

        // A picture that is not complete cannot be forgotten
        reused.Reset();
        reused.BeginFrame();
        Assert.Throws<InvalidOperationException>(reused.Reset);
        Assert.False(reused.Encode(XunitCancellationToken));
        Assert.Throws<InvalidOperationException>(reused.Reset);

        static (byte[] Payload, ushort[] Reconstruction) EncodePicture(HevcEncoder encoder, int seed)
        {
            var planeCount = encoder.Parameters.ChromaFormatIdc is 0 ? 1 : 3;
            for (var plane = 0; plane < planeCount; plane++)
            {
                var buffer = encoder.GetSourcePlane(plane);
                var samples = HevcEncoderTestSession.CreateSamples("photo", buffer.Width, buffer.Height, bitDepth: 8, seed: (seed * 7919) + plane);
                for (var y = 0; y < buffer.Height; y++)
                {
                    samples.AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
                }
            }

            encoder.BeginFrame();
            while (!encoder.Encode(XunitCancellationToken))
            {
            }

            var reconstruction = new List<ushort>();
            for (var plane = 0; plane < planeCount; plane++)
            {
                var samples = encoder.Reconstruction.GetPlane(plane);
                for (var y = 0; y < samples.Height; y++)
                {
                    reconstruction.AddRange(samples.GetRow(y));
                }
            }

            return (encoder.Payload.ToArray(), [.. reconstruction]);
        }
    }

    [Theory]
    [InlineData(-1, 4, 1)]
    [InlineData(50, 5, 2)]
    public void ACancelledTokenStopsTheEncoder(int quality, int effort, int pictureCount)
    {
        using var audit = new PoolAudit();
        var settings = HevcEncoderTestSession.CreateSettings(72, 72, 8, "420", quality < 0, Math.Max(quality, 0), effort, stillPicture: pictureCount is 1);

        // The number of calls that encode the pictures, then a cancellation before each of them
        var steps = EncodeWithAudit(audit, settings, pictureCount, cancelAt: -1);
        Assert.Null(audit.Verify());
        for (var step = 0; step < steps; step++)
        {
            var cancelAt = step;
            Assert.Throws<OperationCanceledException>(() => EncodeWithAudit(audit, settings, pictureCount, cancelAt));
            Assert.Null(audit.Verify());
        }
    }

    [Theory]
    [InlineData(-1, 0, 1)]
    [InlineData(-1, 6, 2)]
    [InlineData(45, 2, 2)]
    [InlineData(45, 7, 3)]
    public void AnAllocationFailureLeavesNothingBehind(int quality, int effort, int pictureCount)
    {
        using var audit = new PoolAudit();
        var settings = HevcEncoderTestSession.CreateSettings(40, 24, 8, "420", quality < 0, Math.Max(quality, 0), effort, stillPicture: pictureCount is 1);

        // The rentals of an encoding, then the failure of each of them
        audit.FailureInjector = _ => null;
        EncodeWithAudit(audit, settings, pictureCount, cancelAt: -1);
        var rentals = audit.Attempts;
        Assert.Null(audit.Verify());
        Assert.InRange(rentals, 10, 500);
        for (var failing = 1; failing <= rentals; failing++)
        {
            var target = failing;
            audit.FailureInjector = attempt => attempt == target ? new InjectedAllocationFailureException() : null;
            Assert.Throws<InjectedAllocationFailureException>(() => EncodeWithAudit(audit, settings, pictureCount, cancelAt: -1));
            Assert.Null(audit.Verify());
        }

        // The budget of the scope is the one of the limits
        audit.FailureInjector = null;
        Assert.Throws<ImageResourceLimitException>(() => EncodeWithAudit(audit, settings, pictureCount, cancelAt: -1, new ImageResourceLimits { MaxLiveAllocationBytes = 4096 }));
        Assert.Null(audit.Verify());
    }

    // Encodes pictures with the pool of an audit, and returns the number of calls: the token is cancelled before the call cancelAt
    private static int EncodeWithAudit(PoolAudit audit, HevcEncoderSettings settings, int pictureCount, int cancelAt, ImageResourceLimits? limits = null)
    {
        var scope = new AllocationScope(limits ?? ImageResourceLimits.Default, audit.Pool, nameof(EncodeWithAudit));
        using var cancellation = new CancellationTokenSource();
        using var encoder = new HevcEncoder(scope, settings);
        var steps = 0;
        for (var index = 0; index < pictureCount; index++)
        {
            for (var plane = 0; plane < (settings.ChromaFormatIdc is 0 ? 1 : 3); plane++)
            {
                var buffer = encoder.GetSourcePlane(plane);
                var samples = HevcEncoderTestSession.CreateFrame("object", index, buffer.Width, buffer.Height, 1, 1, settings.BitDepth, seed: plane);
                for (var y = 0; y < buffer.Height; y++)
                {
                    samples.AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
                }
            }

            encoder.BeginFrame();
            while (true)
            {
                if (steps == cancelAt)
                {
                    cancellation.Cancel();
                }

                steps++;
                if (encoder.Encode(cancellation.Token))
                    break;
            }

            Assert.False(encoder.Payload.IsEmpty);
        }

        return steps;
    }

    // Encodes a picture of a content, or the pictures of a motion; a negative quality is lossless
    private static HevcEncoderTestSession Encode(string name, int width, int height, int bitDepth, string format, string content, int quality, int effort, int pictureCount = 1)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        var session = new HevcEncoderTestSession(HevcEncoderTestSession.CreateSettings(width, height, bitDepth, format, quality < 0, Math.Max(quality, 0), effort, stillPicture: pictureCount is 1));
        session.Encode(scope, pictureCount, (index, plane, planeWidth, planeHeight) => pictureCount is 1
            ? HevcEncoderTestSession.CreateSamples(content, planeWidth, planeHeight, bitDepth, seed: (plane * 7919) + width + (31 * height))
            : HevcEncoderTestSession.CreateFrame(content, index, planeWidth, planeHeight, plane is 0 ? 1 : session.SubWidthC, plane is 0 ? 1 : session.SubHeightC, bitDepth, seed: (plane * 7919) + width), XunitCancellationToken);
        return session;
    }

    // The pictures that the decoder outputs are those of the encoder, and their hash is the one of the stream
    private static void AssertDecodesToTheReconstruction(HevcEncoderTestSession session, List<HevcTestDecoder.DecodedPicture> decoded, string name)
    {
        Assert.HasCount(session.Reconstructions.Count, decoded);
        for (var index = 0; index < decoded.Count; index++)
        {
            var picture = decoded[index];
            // The conformance window crops whole chroma samples: an odd size is rounded up
            var outputWidth = (session.Settings.Width + session.SubWidthC - 1) / session.SubWidthC * session.SubWidthC;
            var outputHeight = (session.Settings.Height + session.SubHeightC - 1) / session.SubHeightC * session.SubHeightC;
            Assert.Equal((outputWidth, outputHeight), (session.Parameters.OutputWidth, session.Parameters.OutputHeight));
            Assert.Equal((outputWidth, outputHeight, session.Settings.BitDepth, session.Settings.ChromaFormatIdc), (picture.Sps.OutputWidth, picture.Sps.OutputHeight, picture.Sps.BitDepth, picture.Sps.ChromaFormatIdc));
            Assert.HasCount(session.PlaneCount, picture.Planes);
            for (var plane = 0; plane < picture.Planes.Length; plane++)
            {
                var position = session.Reconstructions[index][plane].AsSpan().CommonPrefixLength(picture.Planes[plane]);
                var stride = picture.GetWidth(plane);
                Assert.True(position == picture.Planes[plane].Length && position == session.Reconstructions[index][plane].Length, $"{name}: the plane {plane} of the picture {index} is not the one that the encoder reconstructed, from the sample ({position % stride}, {position / stride})");
            }

            HevcTestDecoder.AssertMatchesHash(picture, name);
        }
    }

    // The samples of the output picture are those that were encoded
    private static void AssertReconstructionIsTheSource(HevcEncoderTestSession session, int index, string name)
    {
        for (var plane = 0; plane < session.PlaneCount; plane++)
        {
            var width = plane is 0 ? session.Settings.Width : (session.Settings.Width + session.SubWidthC - 1) / session.SubWidthC;
            var height = plane is 0 ? session.Settings.Height : (session.Settings.Height + session.SubHeightC - 1) / session.SubHeightC;
            var stride = plane is 0 ? session.Parameters.PicWidthInLumaSamples : session.Parameters.PicWidthInLumaSamples / session.SubWidthC;
            for (var y = 0; y < height; y++)
            {
                Assert.True(session.Sources[index][plane].AsSpan(y * width, width).SequenceEqual(session.Reconstructions[index][plane].AsSpan(y * stride, width)), $"{name}: the row {y} of the plane {plane} of the picture {index} is not the encoded row");
            }
        }
    }
}
