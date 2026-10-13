using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The arithmetic encoding engine (Rec. ITU-T H.265, clause 9.3.5). It has two independent checks: the arithmetic decoder
/// of the library, which the conformance streams validate, decodes the bins that were encoded; and the arithmetic encoder
/// of the test harness (<see cref="ReferenceHevcCabacEncoder"/>), a transcription of the flowcharts of the specification
/// with its outstanding bits, which shares no code with the library, writes the same bytes.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class HevcCabacEncoderTests
{
    private const int Decision = 0;
    private const int Bypass = 1;
    private const int BypassBits = 2;
    private const int Terminate = 3;
    private const int Restart = 4;

    /// <param name="Kind">The kind of the operation.</param>
    /// <param name="Context">The context variable of a decision.</param>
    /// <param name="Value">The bin, the bins of several bypass bins, or the bytes that follow a terminated codeword.</param>
    /// <param name="Count">The number of bypass bins, or of bytes.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Operation(int Kind, int Context, uint Value, int Count);

    [Fact]
    public void ACodewordWithoutBinIsNineBits()
    {
        // ivlLow is 0 + 510 - 2 = 508 after the terminating bin: 9 bits 1 1111 1100, whose last bit is replaced by the
        // bit equal to 1 (Figure 9-15), then zero bits up to the next byte
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(ACodewordWithoutBinIsNineBits));
        using var output = new PooledPayloadWriter(scope);
        var encoder = new HevcCabacEncoder();
        Assert.False(encoder.IsActive);
        encoder.Initialize(output);
        Assert.True(encoder.IsActive);
        encoder.EncodeTerminate(1);
        Assert.False(encoder.IsActive);
        Assert.Equal([0xFE, 0x80], output.WrittenSpan.ToArray());
        Assert.Equal(1, encoder.BinCountsInNalUnits);

        var decoder = new HevcCabacDecoder();
        decoder.Initialize(output.WrittenSpan.ToArray(), 0, 2);
        Assert.Equal(1, decoder.DecodeTerminate());
        Assert.Equal(9, decoder.BitPosition);
    }

    [Fact]
    public void TheFirstBinsHaveTheBitsOfTheFlowcharts()
    {
        // A bypass bin equal to 1 doubles ivlLow and adds the range: 510, of 10 bits. The terminating bin adds 508:
        // 1018 = 11 1111 1010, and the flushing writes the bits of this number but the last one, then a bit equal to 1:
        // 1111 1110 11
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(TheFirstBinsHaveTheBitsOfTheFlowcharts));
        using var output = new PooledPayloadWriter(scope);
        var encoder = new HevcCabacEncoder();
        encoder.Initialize(output);
        encoder.EncodeBypass(1);
        encoder.EncodeTerminate(1);
        Assert.Equal([0xFE, 0xC0], output.WrittenSpan.ToArray());

        // A bypass bin equal to 0 only doubles ivlLow: 508 after the terminating bin, of 10 bits now: 0 1111 1110 1
        output.Reset();
        encoder.Initialize(output);
        encoder.EncodeBypass(0);
        encoder.EncodeTerminate(1);
        Assert.Equal([0x7F, 0x40], output.WrittenSpan.ToArray());

        // The least probable symbol of the state 0, whose range is 240 of 510 (qRangeIdx 3): ivlLow is 270 and the range
        // 240, doubled once: 540 and 480. The terminating bin adds 478: 1018 again
        output.Reset();
        encoder.Initialize(output);
        byte context = 0;
        encoder.EncodeDecision(ref context, 1);
        Assert.Equal((byte)((0 << 1) | 1), context);
        encoder.EncodeTerminate(1);
        Assert.Equal([0xFE, 0xC0], output.WrittenSpan.ToArray());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 9)]
    [InlineData(4, 60)]
    [InlineData(5, 500)]
    [InlineData(6, 4000)]
    [InlineData(7, 4000)]
    [InlineData(8, 60000)]
    public void BinsOfEveryKindAreDecodedAndAreTheBytesOfTheReferenceEncoder(int seed, int count)
    {
        var random = new Random(seed);
        var states = CreateStates(random);

        // A skew per context, so that the states move in both directions
        var skew = new int[states.Length];
        for (var i = 0; i < skew.Length; i++)
        {
            skew[i] = random.Next(1, 100);
        }

        var operations = new List<Operation>(count);
        for (var i = 0; i < count; i++)
        {
            var context = random.Next(states.Length);
            var bits = random.Next(1, 33);
            operations.Add(random.Next(100) switch
            {
                < 55 => new Operation(Decision, context, random.Next(100) < skew[context] ? 1u : 0u, 1),
                < 75 => new Operation(Bypass, 0, (uint)random.Next(2), 1),
                < 87 => new Operation(BypassBits, 0, (uint)random.NextInt64(1L << bits), bits),
                < 98 => new Operation(Terminate, 0, 0, 1),
                _ => new Operation(Restart, 0, (uint)random.Next(256), random.Next(0, 5)),
            });
        }

        AssertRoundTrip(states, operations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImprobableAndProbableBinsCarryIntoTheWrittenBytes(bool mostProbable)
    {
        // The most probable symbol of a context in its last state leaves ivlLow next to a carry for a long time; its least
        // probable symbol shrinks the range by 6 bits. Runs of bypass bins equal to 1 write bytes equal to 255, which a
        // carry then crosses
        var random = new Random(mostProbable ? 21 : 22);
        var states = new byte[4];
        states[0] = (62 << 1) | 1;
        states[1] = 62 << 1;
        var operations = new List<Operation>();
        for (var i = 0; i < 30000; i++)
        {
            var context = random.Next(2);
            var probable = context is 0 ? 1u : 0u;
            var bin = (random.Next(40) is 0) == mostProbable ? 1 - probable : probable;
            operations.Add(new Operation(Decision, context, bin, 1));
            if (random.Next(200) is 0)
            {
                var ones = random.Next(1, 40);
                for (var j = 0; j < ones; j++)
                {
                    operations.Add(random.Next(3) is 0 ? new Operation(BypassBits, 0, uint.MaxValue, 32) : new Operation(Bypass, 0, 1, 1));
                }
            }
        }

        AssertRoundTrip(states, operations);
    }

    [Fact]
    public void SeveralBypassBinsAreTheBinsOneByOne()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(SeveralBypassBinsAreTheBinsOneByOne));
        using var together = new PooledPayloadWriter(scope);
        using var separate = new PooledPayloadWriter(scope);
        for (var seed = 1; seed <= 20; seed++)
        {
            var random = new Random(seed);
            together.Reset();
            separate.Reset();
            var first = new HevcCabacEncoder();
            var second = new HevcCabacEncoder();
            first.Initialize(together);
            second.Initialize(separate);
            byte firstContext = (20 << 1) | 1;
            byte secondContext = (20 << 1) | 1;
            for (var i = 0; i < 2000; i++)
            {
                var count = random.Next(0, 33);
                var value = (uint)random.NextInt64(1L << count);
                first.EncodeBypassBits(value, count);
                for (var bit = count - 1; bit >= 0; bit--)
                {
                    second.EncodeBypass((int)((value >> bit) & 1));
                }

                // The range changes between the runs of bypass bins
                var bin = random.Next(4) is 0 ? 0 : 1;
                first.EncodeDecision(ref firstContext, bin);
                second.EncodeDecision(ref secondContext, bin);
            }

            first.EncodeTerminate(1);
            second.EncodeTerminate(1);
            Assert.Equal(separate.WrittenSpan.ToArray(), together.WrittenSpan.ToArray());
            Assert.Equal(second.BinCountsInNalUnits, first.BinCountsInNalUnits);
        }
    }

    [Fact]
    public void TheBinsOfAPictureAreCounted()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(TheBinsOfAPictureAreCounted));
        using var output = new PooledPayloadWriter(scope);
        var encoder = new HevcCabacEncoder();
        encoder.Initialize(output);
        byte context = 10;
        encoder.EncodeDecision(ref context, 0);
        encoder.EncodeDecision(ref context, 1);
        encoder.EncodeBypass(1);
        encoder.EncodeBypassBits(0x155, 9);
        encoder.EncodeTerminate(0);
        encoder.EncodeTerminate(1);
        Assert.Equal(14, encoder.BinCountsInNalUnits);

        // The counter goes on with the codewords that follow, until it is set to 0 for another picture
        encoder.Initialize(output);
        encoder.EncodeBypass(0);
        encoder.EncodeTerminate(1);
        Assert.Equal(16, encoder.BinCountsInNalUnits);
        encoder.BinCountsInNalUnits = 0;
        Assert.Equal(0, encoder.BinCountsInNalUnits);
    }

    [Fact]
    public void TheNumberOfCabacZeroWordsIsTheOneOfTheByteStuffingProcess()
    {
        // RawMinCuBits of 8x8 blocks of 8-bit 4:2:0 samples: 64 * ( 8 + 2 * 8 / 4 ) = 768. For 4 blocks, 100000 bins and
        // 1000 bytes: 3 * ( 3200000 - 3072 ) / 1024 = 9366; ( 9366 - 1000 ) / 3 = 2788.67, rounded up
        Assert.Equal(2789, HevcCabacEncoder.GetCabacZeroWordCount(100000, 1000, 768, 4));

        // One byte more is enough to need one word less: ( 9366 - 1001 ) / 3 = 2788.33; two more: 2788
        Assert.Equal(2789, HevcCabacEncoder.GetCabacZeroWordCount(100000, 1001, 768, 4));
        Assert.Equal(2788, HevcCabacEncoder.GetCabacZeroWordCount(100000, 1002, 768, 4));

        // The first division is rounded up too: 3 * ( 32 * 97 - 3072 ) / 1024 = 0.09, which is 1
        Assert.Equal(1, HevcCabacEncoder.GetCabacZeroWordCount(97, 0, 768, 4));
        Assert.Equal(0, HevcCabacEncoder.GetCabacZeroWordCount(96, 0, 768, 4));
        Assert.Equal(0, HevcCabacEncoder.GetCabacZeroWordCount(97, 1, 768, 4));

        // A picture of PCM coding units has few bins for its size: none
        Assert.Equal(0, HevcCabacEncoder.GetCabacZeroWordCount(12, 800, 768, 4));
        Assert.Equal(0, HevcCabacEncoder.GetCabacZeroWordCount(0, 0, 768, 4));
    }

    [Fact]
    public void TheStateTransitionIsTheOneOfTheDecoder()
    {
        // Every state and both bins: the context variable that the decoder has after it decoded the bin
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(TheStateTransitionIsTheOneOfTheDecoder));
        using var output = new PooledPayloadWriter(scope);
        var encoder = new HevcCabacEncoder();
        var decoder = new HevcCabacDecoder();
        for (var state = 0; state < 126; state++)
        {
            for (var bin = 0; bin < 2; bin++)
            {
                output.Reset();
                encoder.Initialize(output);
                var encoded = (byte)state;
                encoder.EncodeDecision(ref encoded, bin);
                encoder.EncodeTerminate(1);

                var updated = (byte)state;
                HevcCabacEncoder.UpdateContext(ref updated, bin);
                Assert.Equal(encoded, updated);

                decoder.Contexts[0] = (byte)state;
                decoder.Initialize(output.WrittenSpan.ToArray(), 0, output.Length);
                Assert.Equal(bin, decoder.DecodeDecision(0));
                Assert.Equal(decoder.Contexts[0], encoded);
                Assert.Equal(1, decoder.DecodeTerminate());
            }
        }
    }

    [Fact]
    public void ASinkEncodesMeasuresOrIgnoresTheBins()
    {
        var random = new Random(31);
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(ASinkEncodesMeasuresOrIgnoresTheBins));
        using var sinkOutput = new PooledPayloadWriter(scope);
        using var encoderOutput = new PooledPayloadWriter(scope);
        var states = CreateStates(random);
        var sinkStates = (byte[])states.Clone();
        var costStates = (byte[])states.Clone();
        var frozenStates = (byte[])states.Clone();
        var ignoredStates = (byte[])states.Clone();
        var encoder = new HevcCabacEncoder();
        var sink = new HevcBinSink { Mode = HevcBinSinkMode.Encode };
        var cost = new HevcBinSink { Mode = HevcBinSinkMode.Cost };
        var frozen = new HevcBinSink { Mode = HevcBinSinkMode.Cost, AdaptsContexts = false };
        var ignored = new HevcBinSink();
        Assert.Equal(HevcBinSinkMode.None, ignored.Mode);
        Assert.True(cost.AdaptsContexts);
        encoder.Initialize(encoderOutput);
        sink.Encoder.Initialize(sinkOutput);
        long expectedRate = 0;
        long expectedFrozenRate = 0;
        for (var i = 0; i < 3000; i++)
        {
            var context = random.Next(states.Length);
            switch (random.Next(5))
            {
                case 0:
                case 1:
                    var bin = random.Next(3) is 0 ? 1 : 0;
                    expectedRate += HevcCost.GetBinCost(states[context], bin);
                    expectedFrozenRate += HevcCost.GetBinCost(frozenStates[context], bin);
                    encoder.EncodeDecision(ref states[context], bin);
                    sink.EncodeDecision(ref sinkStates[context], bin);
                    cost.EncodeDecision(ref costStates[context], bin);
                    frozen.EncodeDecision(ref frozenStates[context], bin);
                    ignored.EncodeDecision(ref ignoredStates[context], bin);
                    break;

                case 2:
                    var bypass = random.Next(2);
                    expectedRate += HevcCost.Scale;
                    encoder.EncodeBypass(bypass);
                    sink.EncodeBypass(bypass);
                    cost.EncodeBypass(bypass);
                    ignored.EncodeBypass(bypass);
                    break;

                case 3:
                    var count = random.Next(0, 20);
                    var value = (uint)random.Next(1 << count);
                    var k = random.Next(0, 5);
                    expectedRate += (count * HevcCost.Scale) + HevcCost.GetExpGolombCost(value, k);
                    encoder.EncodeBypassBits(value, count);
                    WriteExpGolomb(encoder, value, k);
                    sink.EncodeBypassBits(value, count);
                    sink.EncodeExpGolomb(value, k);
                    cost.EncodeBypassBits(value, count);
                    cost.EncodeExpGolomb(value, k);
                    ignored.EncodeBypassBits(value, count);
                    ignored.EncodeExpGolomb(value, k);
                    break;

                default:
                    expectedRate += HevcCost.GetTerminateCost(0);
                    encoder.EncodeTerminate(0);
                    sink.EncodeTerminate(0);
                    cost.EncodeTerminate(0);
                    ignored.EncodeTerminate(0);
                    break;
            }
        }

        expectedRate += HevcCost.GetTerminateCost(1);
        encoder.EncodeTerminate(1);
        sink.EncodeTerminate(1);
        cost.EncodeTerminate(1);
        ignored.EncodeTerminate(1);

        // The sink that encodes writes what the encoder writes, and leaves the same context variables
        Assert.Equal(encoderOutput.WrittenSpan.ToArray(), sinkOutput.WrittenSpan.ToArray());
        Assert.Equal(states, sinkStates);
        Assert.Equal(encoder.BinCountsInNalUnits, sink.Encoder.BinCountsInNalUnits);
        Assert.Equal(0, sink.Rate);

        // The sink that measures adds the cost of each bin in the state that the bins before it left, and writes nothing
        Assert.Equal(expectedRate, cost.Rate);
        Assert.Equal(states, costStates);
        Assert.False(cost.Encoder.IsActive);
        Assert.Equal(0, cost.Encoder.BinCountsInNalUnits);

        // Without adaptation, the cost of a bin is the one of the initial state of its context variable
        Assert.Equal(expectedFrozenRate, frozen.Rate);
        Assert.Equal(ignoredStates, frozenStates);

        // The sink that ignores the bins changes nothing
        Assert.Equal(0, ignored.Rate);
        Assert.Equal(0, ignored.Encoder.BinCountsInNalUnits);
    }

    // Clause 9.3.3.3, written with the bypass bins of the encoder
    private static void WriteExpGolomb(HevcCabacEncoder encoder, uint value, int k)
    {
        while (value >= 1u << k)
        {
            encoder.EncodeBypass(1);
            value -= 1u << k;
            k++;
        }

        encoder.EncodeBypass(0);
        while (k-- > 0)
        {
            encoder.EncodeBypass((int)((value >> k) & 1));
        }
    }

    private static byte[] CreateStates(Random random)
    {
        // pStateIdx 0 to 62 and both values of valMps: the state 63 is the one of the terminating bin
        var states = new byte[random.Next(1, 40)];
        for (var i = 0; i < states.Length; i++)
        {
            states[i] = (byte)((random.Next(63) << 1) | random.Next(2));
        }

        return states;
    }

    private static void AssertRoundTrip(byte[] initialStates, List<Operation> operations)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(HevcCabacEncoderTests));
        using var output = new PooledPayloadWriter(scope);

        // The codeword does not start at the beginning of the output, like slice segment data after their header
        output.Write([0xFF, 0xFF, 0xFF]);
        var prefix = output.Length;
        var encoder = new HevcCabacEncoder();
        var reference = new ReferenceHevcCabacEncoder();
        var states = (byte[])initialStates.Clone();
        var referenceStates = (byte[])initialStates.Clone();
        encoder.Initialize(output);
        long bins = 0;
        foreach (var operation in operations)
        {
            switch (operation.Kind)
            {
                case Decision:
                    encoder.EncodeDecision(ref states[operation.Context], (int)operation.Value);
                    reference.EncodeDecision(ref referenceStates[operation.Context], (int)operation.Value);
                    bins++;
                    break;

                case Bypass:
                    encoder.EncodeBypass((int)operation.Value);
                    reference.EncodeBypass((int)operation.Value);
                    bins++;
                    break;

                case BypassBits:
                    encoder.EncodeBypassBits(operation.Value, operation.Count);
                    for (var bit = operation.Count - 1; bit >= 0; bit--)
                    {
                        reference.EncodeBypass((int)((operation.Value >> bit) & 1));
                    }

                    bins += operation.Count;
                    break;

                case Terminate:
                    encoder.EncodeTerminate(0);
                    reference.EncodeTerminate(0);
                    bins++;
                    break;

                default:
                    // pcm_flag, the alignment, bytes that are not arithmetic coded, then a new codeword
                    encoder.EncodeTerminate(1);
                    reference.EncodeTerminate(1);
                    reference.AlignToByte();
                    for (var i = 0; i < operation.Count; i++)
                    {
                        output.Write((byte)(operation.Value + i));
                        reference.WriteBits((byte)(operation.Value + i), 8);
                    }

                    encoder.Initialize(output);
                    reference.Restart();
                    bins++;
                    break;
            }
        }

        encoder.EncodeTerminate(1);
        reference.EncodeTerminate(1);
        reference.AlignToByte();
        bins++;
        Assert.Equal(bins, encoder.BinCountsInNalUnits);

        // The same bytes and the same context variables as the encoder of the flowcharts
        var data = output.WrittenSpan.ToArray();
        Assert.Equal([0xFF, 0xFF, 0xFF], data[..prefix]);
        Assert.Equal(Convert.ToHexString(reference.ToArray()), Convert.ToHexString(data.AsSpan(prefix)));
        Assert.Equal(referenceStates, states);

        // The decoder reads the bins that were written, and ends where each codeword ends
        var decoder = new HevcCabacDecoder();
        initialStates.CopyTo(decoder.Contexts);
        decoder.Initialize(data, prefix, data.Length);
        foreach (var operation in operations)
        {
            switch (operation.Kind)
            {
                case Decision:
                    Assert.Equal((int)operation.Value, decoder.DecodeDecision(operation.Context));
                    break;

                case Bypass:
                    Assert.Equal((int)operation.Value, decoder.DecodeBypass());
                    break;

                case BypassBits:
                    // The decoder reads at most 31 bypass bins at a time
                    Assert.Equal(operation.Value >> (operation.Count / 2), (uint)decoder.DecodeBypassBits(operation.Count - (operation.Count / 2)));
                    Assert.Equal(operation.Value & ((1u << (operation.Count / 2)) - 1), (uint)decoder.DecodeBypassBits(operation.Count / 2));
                    break;

                case Terminate:
                    Assert.Equal(0, decoder.DecodeTerminate());
                    break;

                default:
                    Assert.Equal(1, decoder.DecodeTerminate());
                    var position = (int)((decoder.BitPosition + 7) >> 3);
                    for (var i = 0; i < operation.Count; i++)
                    {
                        Assert.Equal((byte)(operation.Value + i), data[position + i]);
                    }

                    decoder.Initialize(data, position + operation.Count, data.Length);
                    break;
            }
        }

        Assert.Equal(1, decoder.DecodeTerminate());
        Assert.Equal(data.Length, (int)((decoder.BitPosition + 7) >> 3));
        Assert.Equal(states, decoder.Contexts[..states.Length].ToArray());
    }
}
