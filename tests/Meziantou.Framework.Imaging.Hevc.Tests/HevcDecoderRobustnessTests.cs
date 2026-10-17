using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Hevc;
using Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The HEVC decoder on damaged streams: it ends with one of its three documented exceptions or with pictures, it returns
/// every buffer it rented, and it never repairs: a stream that is cut is an error.
/// </summary>
public sealed class HevcDecoderRobustnessTests
{
    public static TheoryData<string> StreamNames => new()
    {
        nameof(HevcTestStreams.X265Main420),
        nameof(HevcTestStreams.X265Rext444TenBits),
        nameof(HevcTestStreams.X265Lossless422),
        nameof(HevcTestStreams.X265Inter),
        nameof(HevcTestStreams.HmPcm420),
        nameof(HevcTestStreams.HmSlices420),
        nameof(HevcTestStreams.HmTiles420),
        nameof(HevcTestStreams.HmWavefront420),
        nameof(HevcTestStreams.HmRangeExtensions444),
        nameof(HevcTestStreams.HmCrc420TenBits),
        nameof(HevcTestStreams.HmChecksum422TwelveBits),
        nameof(HevcTestStreams.HmSao422TenBits),
        nameof(HevcTestStreams.HmChromaQpOffsets444TenBits),
        nameof(HevcTestStreams.KvazaarTilesWavefront420),
        nameof(HevcTestStreams.X265P420),
        nameof(HevcTestStreams.X265B422TenBits),
        nameof(HevcTestStreams.HmWeighted422TenBits),
        nameof(HevcTestStreams.HmAmp444),
        nameof(HevcTestStreams.HmRandomAccess400TenBits),
        nameof(HevcTestStreams.HmInterSlices420TwelveBits),
        nameof(HevcTestStreams.KvazaarInterTiles420),
        nameof(HevcTestStreams.KvazaarInterWavefront400),
        nameof(HevcTestStreams.HmExplicitRdpcm420TwelveBits),
        nameof(HevcTestStreams.HmCrossComponentInter444TenBits),
        nameof(HevcTestStreams.HmLongTermRewritten400TenBits),
        nameof(HevcTestStreams.HmLongTermRewrittenB420),
        nameof(HevcTestStreams.X265OpenGop420),
        nameof(HevcTestStreams.X265Radl420),
    };

    private static byte[] GetStream(string name) => (byte[])typeof(HevcTestStreams).GetProperty(name)!.GetValue(null)!;

    /// <summary>Decodes NAL units, and gives the number of pictures or the exception of the decoder.</summary>
    private static (int PictureCount, ImageException? Exception) Decode(IEnumerable<ReadOnlyMemory<byte>> units, ImageResourceLimits? limits = null)
    {
        using var audit = new PoolAudit();
        var count = 0;
        ImageException? exception = null;
        try
        {
            var scope = new AllocationScope(limits ?? ImageResourceLimits.Default, audit.Pool, nameof(HevcDecoderRobustnessTests));
            using var decoder = new HevcDecoder(scope);
            var data = ReferenceHevcStream.ToLengthPrefixed(units);
            decoder.SetInput(data, 0, data.Length, nalLengthSize: 4);
            decoder.EndOfSequence();
            while (decoder.DecodeNextOutputPicture(XunitCancellationToken) is { } picture)
            {
                count++;
                picture.Release();
            }
        }
        catch (InvalidImageContentException ex)
        {
            Assert.Equal(ImageFormat.Heic, ex.Format);
            exception = ex;
        }
        catch (UnsupportedImageFeatureException ex)
        {
            Assert.Equal(ImageFormat.Heic, ex.Format);
            exception = ex;
        }
        catch (ImageResourceLimitException ex)
        {
            exception = ex;
        }

        // Whatever the outcome, every buffer is returned
        Assert.Null(audit.Verify());
        return (count, exception);
    }

    [Theory]
    [MemberData(nameof(StreamNames))]
    public void ATruncatedNalUnitIsAnErrorOrIsDecoded(string name)
    {
        var units = ReferenceHevcStream.SplitAnnexB(GetStream(name));
        for (var index = 0; index < units.Count; index++)
        {
            var type = ReferenceHevcStream.GetNalUnitType(units[index].Span);
            var step = Math.Max(1, units[index].Length / (units.Count > 12 ? 13 : 97));
            for (var length = 0; length < units[index].Length; length += step)
            {
                var damaged = units.ToList();
                damaged[index] = units[index][..length];
                var (_, exception) = Decode(damaged);

                // A parameter set or a slice segment that is cut is never accepted: the trailing bits of a parameter set and
                // the end of the last codeword of a slice segment are where the data end. Supplemental enhancement
                // information is optional, and its messages are bounded by their sizes.
                if (type is not (ReferenceHevcStream.NalSuffixSei or 39))
                {
                    Assert.IsAssignableTo<InvalidImageContentException>(exception, $"{name}: the NAL unit {index} of type {type} cut to {length} bytes gives {exception?.GetType().Name ?? "pictures"}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(StreamNames))]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ADamagedStreamIsAnErrorOrIsDecoded(string name)
    {
        var stream = GetStream(name);
        var units = ReferenceHevcStream.SplitAnnexB(stream);
        var random = new Random(name.Length * 7919);
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        // The streams of several pictures take longer to decode
        var iterations = units.Count > 12 ? 120 : 400;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var damaged = units.Select(unit => (ReadOnlyMemory<byte>)unit.ToArray()).ToList();
            var changes = 1 + (iteration % 3);
            for (var change = 0; change < changes; change++)
            {
                var index = random.Next(damaged.Count);
                var bytes = damaged[index].ToArray();

                // The header of the NAL unit is kept in most of the cases, so that the damage reaches the syntax of its type
                var first = random.Next(8) is 0 ? 0 : Math.Min(2, bytes.Length - 1);
                var position = random.Next(first, bytes.Length);
                switch (random.Next(4))
                {
                    case 0:
                        bytes[position] = (byte)random.Next(256);
                        break;
                    case 1:
                        bytes[position] = random.Next(2) is 0 ? (byte)0x00 : (byte)0xFF;
                        break;
                    default:
                        bytes[position] ^= (byte)(1 << random.Next(8));
                        break;
                }

                damaged[index] = bytes;
            }

            var (_, exception) = Decode(damaged);
            var outcome = exception?.GetType().Name ?? "decoded";
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
        }

        // Most of the changes are found: the syntax is dense, and a slice segment ends where its last codeword ends
        Assert.True(outcomes.GetValueOrDefault(nameof(InvalidImageContentException)) > iterations / 4, $"{name}: {string.Join(", ", outcomes.Select(pair => $"{pair.Key} {pair.Value}"))}");
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void RandomDataAreNotAStream()
    {
        // NAL units of every type whose payload is random, alone and after the parameter sets of a stream
        var random = new Random(26500);
        var parameterSets = ReferenceHevcStream.SplitAnnexB(HevcTestStreams.HmTiles420).Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) is >= 32 and <= 34).ToList();
        for (var iteration = 0; iteration < 600; iteration++)
        {
            var units = iteration % 2 is 0 ? [] : parameterSets.ToList();
            var count = random.Next(1, 4);
            for (var i = 0; i < count; i++)
            {
                var unit = new byte[random.Next(2, 200)];
                random.NextBytes(unit);
                var type = random.Next(3) is 0 ? random.Next(64) : random.Next(4) switch
                {
                    0 => 19,
                    1 => 33,
                    2 => 34,
                    _ => 1,
                };
                unit[0] = (byte)(type << 1);
                unit[1] = 1;
                units.Add(unit);
            }

            Decode(units);
        }
    }

    [Fact]
    public void AnEmptyInputHasNoPicture()
    {
        Assert.Equal((0, null), Decode([]));

        // A NAL unit has a header of two bytes; the length of a NAL unit is within the input
        Assert.IsAssignableTo<InvalidImageContentException>(Decode([new byte[] { 0x40 }]).Exception);
        using var decoder = new HevcDecoder(AllocationScope.Create(ImageConfiguration.Default, nameof(AnEmptyInputHasNoPicture)));
        decoder.SetInput([0, 0, 0, 9, 0x40, 0x01, 0x0C], 0, 7, nalLengthSize: 4);
        Assert.Throws<InvalidImageContentException>(() => decoder.DecodeNextOutputPicture(XunitCancellationToken));
        using var other = new HevcDecoder(AllocationScope.Create(ImageConfiguration.Default, nameof(AnEmptyInputHasNoPicture)));
        other.SetInput([0, 0, 0], 0, 3, nalLengthSize: 4);
        Assert.Throws<InvalidImageContentException>(() => other.DecodeNextOutputPicture(XunitCancellationToken));
    }

    [Fact]
    public void APictureIsWithinTheResourceLimits()
    {
        // 48 x 32 samples: 1536 pixels
        var units = ReferenceHevcStream.SplitAnnexB(HevcTestStreams.HmTiles420);
        Assert.Equal((1, null), Decode(units, new ImageResourceLimits { MaxFramePixels = 48 * 32 }));
        Assert.IsAssignableTo<ImageResourceLimitException>(Decode(units, new ImageResourceLimits { MaxFramePixels = (48 * 32) - 1 }).Exception);
        Assert.IsAssignableTo<ImageResourceLimitException>(Decode(units, new ImageResourceLimits { MaxWidth = 47 }).Exception);
        Assert.IsAssignableTo<ImageResourceLimitException>(Decode(units, new ImageResourceLimits { MaxHeight = 31 }).Exception);
        Assert.Equal((1, null), Decode(units, new ImageResourceLimits { MaxWidth = 48, MaxHeight = 32 }));

        // The memory of the decoder is rented from the scope, whose budget is the one of the limits
        Assert.IsAssignableTo<ImageResourceLimitException>(Decode(units, new ImageResourceLimits { MaxLiveAllocationBytes = 1024 }).Exception);
    }

    [Fact]
    public void ACancelledTokenStopsTheDecoder()
    {
        using var audit = new PoolAudit();
        var scope = new AllocationScope(ImageResourceLimits.Default, audit.Pool, nameof(ACancelledTokenStopsTheDecoder));
        using (var decoder = new HevcDecoder(scope))
        {
            var data = ReferenceHevcStream.ToLengthPrefixed(ReferenceHevcStream.SplitAnnexB(HevcTestStreams.HmSlices420));
            decoder.SetInput(data, 0, data.Length, nalLengthSize: 4);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => decoder.DecodeNextOutputPicture(cancellation.Token));
        }

        Assert.Null(audit.Verify());
    }
}
