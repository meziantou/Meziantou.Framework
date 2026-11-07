using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
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

    // Vectors whose slice segments have several substreams that start at an entry point: tiles (uniform and explicit
    // spacing, in-loop filters across them or not, inter pictures), rows of the wavefront (one or several slices, inter
    // pictures), and both
    public static TheoryData<string> SubstreamVectorIds => new()
    {
        "hevc/hm-tiles-2x2-inter-512x128",
        "hevc/hm-tiles-explicit-ctb32-832x64",
        "hevc/kvazaar-tiles-2x2-explicit-768x128",
        "hevc/kvazaar-tiles-small-3x3-inter-192x192",
        "hevc/x265-intra-wpp-ctb64-136x136",
        "hevc/x265-intra-slices-3-wpp-64x96",
        "hevc/kvazaar-wpp-inter-192x128",
        "hevc/hm-wpp-slices-ctb32-444-96x96",
    };

    /// <summary>
    /// Decodes NAL units by <paramref name="workers"/> workers, whatever the size of the pictures, and gives the digest of
    /// every plane of every picture, or the type and the message of the exception of the decoder.
    /// </summary>
    private static string DecodeOutcome(IEnumerable<ReadOnlyMemory<byte>> units, int workers, ImageResourceLimits? limits = null, PoolAudit? audit = null, CancellationToken cancellationToken = default)
    {
        using var owned = audit is null ? new PoolAudit() : null;
        audit ??= owned!;
        var outcome = new StringBuilder();
        HevcDecoder.TestIgnoreMinimumSize = true;
        try
        {
            var scope = new AllocationScope(limits ?? ImageResourceLimits.Default, audit.Pool, nameof(HevcDecoderRobustnessTests));
            using var decoder = new HevcDecoder(scope) { MaxDegreeOfParallelism = workers };
            var data = ReferenceHevcStream.ToLengthPrefixed(units);
            decoder.SetInput(data, 0, data.Length, nalLengthSize: 4);
            decoder.EndOfSequence();
            while (decoder.DecodeNextOutputPicture(cancellationToken) is { } picture)
            {
                try
                {
                    for (var plane = 0; plane < picture.PlaneCount; plane++)
                    {
                        var samples = picture.GetPlane(plane);
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        var bytes = new byte[2 * samples.Width];
                        for (var y = 0; y < samples.Height; y++)
                        {
                            var row = samples.GetRow(y);
                            for (var x = 0; x < row.Length; x++)
                            {
                                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 * x), row[x]);
                            }

                            hash.AppendData(bytes);
                        }

                        outcome.Append(Convert.ToHexString(hash.GetHashAndReset())).Append(' ');
                    }
                }
                finally
                {
                    picture.Release();
                }
            }
        }
        catch (Exception exception) when (exception is ImageException or OperationCanceledException)
        {
            outcome.Append(exception.GetType().Name).Append(": ").Append(exception.Message);
        }
        finally
        {
            HevcDecoder.TestIgnoreMinimumSize = false;
        }

        // Whatever the outcome, every buffer is returned
        Assert.Null(audit.Verify());
        return outcome.ToString();
    }

    private static IReadOnlyList<ReadOnlyMemory<byte>> GetVectorUnits(string id)
    {
        var corpus = GoldenCorpus.Default;
        return ReferenceHevcStream.SplitAnnexB(File.ReadAllBytes(corpus.GetHevcVectorPath(corpus.GetHevcVector(id))));
    }

    /// <summary>
    /// Damaged slice segments by several workers: a substream that fails, a slice segment that ends before its last entry
    /// point, an entry point that is not where the substream before it ends. The workers decode the substreams from their
    /// entry points, whatever happens before them; the pictures and the error are still those of one worker, the error of the
    /// first substream that fails.
    /// </summary>
    [Theory]
    [MemberData(nameof(SubstreamVectorIds))]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ADamagedStreamDecodedByWorkersIsTheStreamDecodedByOneWorker(string id)
    {
        var units = GetVectorUnits(id);
        var slices = Enumerable.Range(0, units.Count).Where(index => ReferenceHevcStream.GetNalUnitType(units[index].Span) < 32).ToArray();
        var random = new Random(id.Length * 7919);
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        Assert.Equal(DecodeOutcome(units, workers: 1), DecodeOutcome(units, workers: 16));
        for (var iteration = 0; iteration < 150; iteration++)
        {
            var damaged = units.ToList();
            for (var change = 0; change < 1 + (iteration % 3); change++)
            {
                var index = slices[random.Next(slices.Length)];
                var bytes = damaged[index].ToArray();

                // Mostly in the slice segment data, where the substreams are
                var position = random.Next(random.Next(4) is 0 ? 2 : bytes.Length / 4, bytes.Length);
                bytes[position] = random.Next(3) switch
                {
                    0 => (byte)random.Next(256),
                    1 => (byte)(bytes[position] ^ (1 << random.Next(8))),
                    _ => 0,
                };
                damaged[index] = bytes;
            }

            var expected = DecodeOutcome(damaged, workers: 1);
            foreach (var workers in (int[])[2, 16])
            {
                Assert.Equal(expected, DecodeOutcome(damaged, workers), $"{id}: the damage {iteration} gives another outcome with {workers} workers.");
            }

            var kind = expected.Contains(':', StringComparison.Ordinal) ? expected[..expected.IndexOf(':', StringComparison.Ordinal)] : "decoded";
            outcomes[kind] = outcomes.GetValueOrDefault(kind) + 1;
        }

        Assert.True(outcomes.GetValueOrDefault(nameof(InvalidImageContentException)) > 50, $"{id}: {string.Join(", ", outcomes.Select(pair => $"{pair.Key} {pair.Value}"))}");
    }

    /// <summary>
    /// kvazaar writes one slice segment per row of the wavefront, with the entry points of the rows that follow it, beyond
    /// its data. With <c>cabac_zero_words</c> after its trailing bits, the first of these entry points is inside the data:
    /// a worker decodes the row that follows from the zero words, and fails, while the slice segment ends with its first
    /// row. The next slice segment decodes that row: the state that the worker left in it is not the state of the picture.
    /// </summary>
    [Theory]
    [InlineData("hevc/kvazaar-wpp-dependent-slices-128x192")]
    [InlineData("hevc/kvazaar-wpp-one-ctb-wide-dependent-slices-48x256")]
    public void ASliceSegmentThatEndsBeforeItsEntryPointsIsDecodedAsByOneWorker(string id)
    {
        var units = GetVectorUnits(id);
        var expected = DecodeOutcome(units, workers: 1);
        var padded = units.Select(unit =>
        {
            if (ReferenceHevcStream.GetNalUnitType(unit.Span) >= 32)
                return unit;

            // cabac_zero_word, with its emulation prevention byte
            var bytes = new byte[unit.Length + (3 * 40)];
            unit.Span.CopyTo(bytes);
            for (var i = unit.Length; i < bytes.Length; i += 3)
            {
                bytes[i + 2] = 3;
            }

            return (ReadOnlyMemory<byte>)bytes;
        }).ToList();
        Assert.DoesNotContain("Exception", expected);
        Assert.Equal(expected, DecodeOutcome(padded, workers: 1));
        foreach (var workers in (int[])[2, 3, 16])
        {
            Assert.Equal(expected, DecodeOutcome(padded, workers), $"{id}: {workers} workers do not decode the pictures of one worker.");
        }

        // The first entry point moved into the data of the first row: a worker decodes the next row from data that are not
        // its own, and writes the state of its blocks before it fails, or ends
        var moved = units.Select(unit => IsFirstSliceSegmentOfPicture(unit) ? RewriteEntryPoint(units, unit, 0, value => value / 2) ?? unit : unit).ToList();
        Assert.Equal(expected, DecodeOutcome(moved, workers: 1));
        foreach (var workers in (int[])[2, 3, 16])
        {
            Assert.Equal(expected, DecodeOutcome(moved, workers), $"{id}: {workers} workers do not decode the pictures of one worker when the entry points are inside the data.");
        }
    }

    /// <summary>
    /// An entry point that is not where the substream before it ends: the workers decode the substream from the entry
    /// point, and the error is still the one of one worker, which finds it where the substream before it ends.
    /// </summary>
    [Theory]
    [MemberData(nameof(SubstreamVectorIds))]
    public void AWrongEntryPointDecodedByWorkersIsTheErrorOfOneWorker(string id)
    {
        var units = GetVectorUnits(id);
        var first = Enumerable.Range(0, units.Count).First(index => IsFirstSliceSegmentOfPicture(units[index]));
        var errors = 0;
        for (var entryPoint = 0; ; entryPoint++)
        {
            var found = false;
            foreach (var delta in (int[])[-1, 1, 7])
            {
                if (RewriteEntryPoint(units, units[first], entryPoint, value => value + delta) is not { } rewritten)
                    break;

                found = true;
                var damaged = units.ToList();
                damaged[first] = rewritten;
                var expected = DecodeOutcome(damaged, workers: 1);
                errors += expected.Contains(nameof(InvalidImageContentException), StringComparison.Ordinal) ? 1 : 0;
                foreach (var workers in (int[])[2, 16])
                {
                    Assert.Equal(expected, DecodeOutcome(damaged, workers), $"{id}: the entry point {entryPoint} moved by {delta} gives another outcome with {workers} workers.");
                }
            }

            if (!found)
                break;
        }

        Assert.True(errors > 0, $"{id}: no entry point is checked.");
    }

    private static bool IsFirstSliceSegmentOfPicture(ReadOnlyMemory<byte> unit) => ReferenceHevcStream.GetNalUnitType(unit.Span) < 32 && (unit.Span[2] & 0x80) is not 0;

    // The first slice segment of a picture with one of its entry points rewritten in place, or null when it has no entry
    // point at that index: the entry points are the last fields of the header, of offset_len_minus1 + 1 bits each, before
    // the alignment bits
    private static ReadOnlyMemory<byte>? RewriteEntryPoint(IReadOnlyList<ReadOnlyMemory<byte>> units, ReadOnlyMemory<byte> unit, int index, Func<int, int> rewrite)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(RewriteEntryPoint));
        var sps = HevcSequenceParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(units.First(nal => ReferenceHevcStream.GetNalUnitType(nal.Span) is 33).Span));
        var pps = HevcPictureParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(units.First(nal => ReferenceHevcStream.GetNalUnitType(nal.Span) is 34).Span));
        using var layout = HevcPictureLayout.Create(scope, sps, pps);
        var rbsp = ReferenceHevcStream.GetRbsp(unit.Span);
        var header = HevcSliceSegmentHeader.Parse(rbsp, HevcNalUnitHeader.Read(unit.Span), layout, sliceHeader: null);
        var entryPoints = header.EntryPointOffsetMinus1.ToArray();
        Assert.False(pps.SliceSegmentHeaderExtensionPresentFlag);
        if (index >= entryPoints.Length)
            return null;

        var alignment = (header.DataOffset * 8) - 1;
        while (((rbsp[alignment >> 3] >> (7 - (alignment & 7))) & 1) is 0)
        {
            alignment--;
        }

        for (var length = 1; length <= 32; length++)
        {
            var start = alignment - (entryPoints.Length * length);
            if (start < 0 || !entryPoints.Select((value, i) => ReadBits(rbsp, start + (i * length), length) == (uint)value).All(match => match))
                continue;

            WriteBits(rbsp, start + (index * length), length, (uint)Math.Clamp(rewrite(entryPoints[index]), 0, (int)Math.Min(int.MaxValue, (1L << length) - 1)));

            // The header of the NAL unit, then the payload with its emulation prevention bytes
            var nal = new List<byte> { unit.Span[0], unit.Span[1] };
            var zeros = 0;
            foreach (var value in rbsp)
            {
                if (zeros >= 2 && value <= 3)
                {
                    nal.Add(3);
                    zeros = 0;
                }

                zeros = value is 0 ? zeros + 1 : 0;
                nal.Add(value);
            }

            return nal.ToArray();
        }

        throw new InvalidOperationException("The entry points are not the last fields of the header.");

        static uint ReadBits(byte[] data, int position, int count)
        {
            var value = 0u;
            for (var i = 0; i < count; i++)
            {
                value = (value << 1) | (uint)((data[(position + i) >> 3] >> (7 - ((position + i) & 7))) & 1);
            }

            return value;
        }

        static void WriteBits(byte[] data, int position, int count, uint value)
        {
            for (var i = 0; i < count; i++)
            {
                var bit = (value >> (count - 1 - i)) & 1;
                var index = (position + i) >> 3;
                var mask = 1 << (7 - ((position + i) & 7));
                data[index] = (byte)(bit is 0 ? data[index] & ~mask : data[index] | mask);
            }
        }
    }

    /// <summary>
    /// A cancellation at any point of a decoding by several workers, before the workers start, while they decode, or after
    /// them: the decoder throws <see cref="OperationCanceledException"/>, or outputs the pictures of a decoding without
    /// cancellation, and returns every buffer.
    /// </summary>
    [Fact]
    public void ACancellationStopsTheWorkers()
    {
        var units = GetVectorUnits("hevc/kvazaar-wpp-inter-192x128");
        var expected = DecodeOutcome(units, workers: 1);
        int rentals;
        using (var counting = new PoolAudit())
        {
            counting.FailureInjector = _ => null;
            Assert.Equal(expected, DecodeOutcome(units, workers: 16, audit: counting));
            rentals = counting.Attempts;
        }

        var canceled = 0;
        for (var rental = 1; rental <= rentals; rental++)
        {
            using var cancellation = new CancellationTokenSource();
            using var audit = new PoolAudit();
            var at = rental;
            audit.FailureInjector = attempt =>
            {
                if (attempt == at)
                {
                    cancellation.Cancel();
                }

                return null;
            };
            var outcome = DecodeOutcome(units, workers: 16, audit: audit, cancellationToken: cancellation.Token);
            var cancellationIndex = outcome.IndexOf(nameof(OperationCanceledException), StringComparison.Ordinal);
            if (cancellationIndex >= 0)
            {
                // The pictures output before the cancellation are those of a decoding without cancellation
                Assert.StartsWith(outcome[..cancellationIndex], expected, $"the cancellation at the rental {rental} outputs other pictures.");
                canceled++;
            }
            else
            {
                Assert.Equal(expected, outcome, $"the cancellation at the rental {rental} gives another outcome.");
            }
        }

        Assert.True(canceled > rentals / 2, $"{canceled} of {rentals} cancellations stop the decoder.");
    }

    /// <summary>
    /// The memory of the workers is rented on the calling thread before they start: the smallest live-allocation limit that
    /// a decoding by several workers needs does not depend on how the workers are scheduled, and one byte less always fails.
    /// </summary>
    [Fact]
    public void TheWorkersAreWithinTheLiveAllocationLimit()
    {
        var units = GetVectorUnits("hevc/hm-tiles-2x2-inter-512x128");
        var expected = DecodeOutcome(units, workers: 1);
        var one = FindSmallestLimit(units, workers: 1);
        var sixteen = FindSmallestLimit(units, workers: 16);
        Assert.True(sixteen > one, $"16 workers need {sixteen} bytes, one worker {one}.");
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(expected, DecodeOutcome(units, workers: 16, limits: new ImageResourceLimits { MaxLiveAllocationBytes = sixteen }));
            Assert.Contains(nameof(ImageResourceLimitException), DecodeOutcome(units, workers: 16, limits: new ImageResourceLimits { MaxLiveAllocationBytes = sixteen - 1 }));
        }

        static long FindSmallestLimit(IReadOnlyList<ReadOnlyMemory<byte>> units, int workers)
        {
            long low = 0;
            long high = 64L * 1024 * 1024;
            while (low + 1 < high)
            {
                var middle = (low + high) / 2;
                var outcome = DecodeOutcome(units, workers, limits: new ImageResourceLimits { MaxLiveAllocationBytes = middle });
                if (outcome.Contains(nameof(ImageResourceLimitException), StringComparison.Ordinal))
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return high;
        }
    }
}
