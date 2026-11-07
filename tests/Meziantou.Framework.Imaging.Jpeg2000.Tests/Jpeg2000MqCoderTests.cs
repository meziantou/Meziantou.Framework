using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The arithmetic coder of Rec. ITU-T T.800, Annex C, against the test sequence the Recommendations publish for it, and
/// against itself on random decisions.
/// </summary>
public sealed class Jpeg2000MqCoderTests
{
    // The test sequence of the MQ coder (Rec. ITU-T T.88, H.2; the coder of T.800 is the same one): 256 decisions, coded
    // in one context that starts in the state 0
    private static readonly byte[] TestSequence =
    [
        0x00, 0x02, 0x00, 0x51, 0x00, 0x00, 0x00, 0xC0, 0x03, 0x52, 0x87, 0x2A, 0xAA, 0xAA, 0xAA, 0xAA,
        0x82, 0xC0, 0x20, 0x00, 0xFC, 0xD7, 0x9E, 0xF6, 0xBF, 0x7F, 0xED, 0x90, 0x4F, 0x46, 0xA3, 0xBF,
    ];

    // The codeword the Recommendation gives for it, without the two bytes FF AC that the termination of T.88 appends
    private static readonly byte[] EncodedTestSequence =
    [
        0x84, 0xC7, 0x3B, 0xFC, 0xE1, 0xA1, 0x43, 0x04, 0x02, 0x20, 0x00, 0x00, 0x41, 0x0D, 0xBB, 0x86,
        0xF4, 0x31, 0x7F, 0xFF, 0x88, 0xFF, 0x37, 0x47, 0x1A, 0xDB, 0x6A, 0xDF,
    ];

    [Fact]
    public void EncodesTheTestSequenceOfTheSpecification()
    {
        var scope = new AllocationScope(new ImageResourceLimits());
        using var output = new PooledPayloadWriter(scope);
        var encoder = default(Jpeg2000MqEncoder);
        encoder.Initialize(output);
        byte context = 0;
        foreach (var value in TestSequence)
        {
            for (var bit = 7; bit >= 0; bit--)
            {
                encoder.Encode((value >> bit) & 1, ref context);
            }
        }

        var length = encoder.Flush();
        Assert.Equal(EncodedTestSequence, output.WrittenSpan.ToArray());
        Assert.Equal(EncodedTestSequence.Length, length);
    }

    [Fact]
    public void DecodesTheTestSequenceOfTheSpecification()
    {
        var decoder = default(Jpeg2000MqDecoder);
        decoder.Initialize(EncodedTestSequence, 0, EncodedTestSequence.Length);
        byte context = 0;
        var decoded = new byte[TestSequence.Length];
        for (var i = 0; i < decoded.Length * 8; i++)
        {
            decoded[i / 8] |= (byte)(decoder.Decode(ref context) << (7 - (i % 8)));
        }

        Assert.Equal(TestSequence, decoded);
    }

    [Fact]
    public void TheStateTableIsTheOneOfTheSpecification()
    {
        // Table C.2: 47 states; the first state, the two states the fast adaptation starts again from, a state of each
        // column of the table, and the two states that end it
        Assert.Equal(47, Jpeg2000MqTables.StateCount);
        Assert.Equal((47, 47, 47, 47), (Jpeg2000MqTables.Probability.Length, Jpeg2000MqTables.NextMoreProbable.Length, Jpeg2000MqTables.NextLessProbable.Length, Jpeg2000MqTables.Switch.Length));
        Assert.Equal((0x5601, 1, 1, 1), Row(0));
        Assert.Equal((0x0221, 38, 33, 0), Row(5));
        Assert.Equal((0x5601, 7, 6, 1), Row(6));
        Assert.Equal((0x1601, 29, 21, 0), Row(13));
        Assert.Equal((0x5601, 15, 14, 1), Row(14));
        Assert.Equal((0x1101, 30, 27, 0), Row(29));
        Assert.Equal((0x0001, 45, 43, 0), Row(45));
        Assert.Equal((0x5601, 46, 46, 0), Row(46));

        // The probability of the less probable symbol never exceeds one half of the interval, and only decreases along the
        // states a more probable symbol leads to
        for (var state = 0; state < 47; state++)
        {
            Assert.InRange(Jpeg2000MqTables.Probability[state], 1, 0x5601);
            Assert.InRange(Jpeg2000MqTables.NextMoreProbable[state], 0, 46);
            Assert.InRange(Jpeg2000MqTables.NextLessProbable[state], 0, 46);
            Assert.True(Jpeg2000MqTables.Probability[Jpeg2000MqTables.NextMoreProbable[state]] <= Jpeg2000MqTables.Probability[state], $"state {state}");
            Assert.Equal(state is 0 or 6 or 14 ? 1 : 0, Jpeg2000MqTables.Switch[state]);
        }

        static (int Probability, int NextMoreProbable, int NextLessProbable, int Switch) Row(int state)
            => (Jpeg2000MqTables.Probability[state], Jpeg2000MqTables.NextMoreProbable[state], Jpeg2000MqTables.NextLessProbable[state], Jpeg2000MqTables.Switch[state]);
    }

    [Theory]
    [InlineData(1, 1, 50)]
    [InlineData(2, 19, 50)]
    [InlineData(3, 19, 97)]
    [InlineData(4, 19, 3)]
    [InlineData(5, 4, 100)]
    [InlineData(6, 4, 0)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void RandomDecisionsRoundTrip(int seed, int contextCount, int percentOfZeros)
    {
        // Skewed and balanced decisions in several contexts, long enough for the bytes FF and the carries to occur
        var random = new Random(seed);
        var decisions = new (int Context, int Decision)[20000];
        for (var i = 0; i < decisions.Length; i++)
        {
            decisions[i] = (random.Next(contextCount), random.Next(100) < percentOfZeros ? 0 : 1);
        }

        var scope = new AllocationScope(new ImageResourceLimits());
        using var output = new PooledPayloadWriter(scope);
        output.Write([1, 2, 3]);
        var encoder = default(Jpeg2000MqEncoder);
        encoder.Initialize(output);
        var contexts = new byte[contextCount];
        foreach (var (context, decision) in decisions)
        {
            encoder.Encode(decision, ref contexts[context]);
        }

        var length = encoder.Flush();
        Assert.Equal(output.Length - 3, length);
        var data = output.WrittenSpan.ToArray();

        // No marker above FF8F in the codeword, and no FF byte at its end
        for (var i = 3; i + 1 < data.Length; i++)
        {
            Assert.False(data[i] == 0xFF && data[i + 1] > 0x8F, $"marker at {i}");
        }

        Assert.NotEqual(0xFF, data[^1]);

        var decoder = default(Jpeg2000MqDecoder);
        decoder.Initialize(data, 3, length);
        Array.Clear(contexts);
        for (var i = 0; i < decisions.Length; i++)
        {
            Assert.Equal(decisions[i].Decision, decoder.Decode(ref contexts[decisions[i].Context]), message: $"decision {i}");
        }
    }
}
