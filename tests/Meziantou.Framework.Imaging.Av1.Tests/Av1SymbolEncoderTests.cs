using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Avif;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The symbol encoder of AV1. The specification only describes the decoder (section 8.2), so the oracle is
/// <see cref="ReferenceSymbolDecoder"/>, a bit-serial transcription of the specification that also checks the conformance
/// requirements of the exit process; the symbol decoder of the library decodes the same data as a supplementary check.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class Av1SymbolEncoderTests
{
    [Fact]
    public void ATileWithoutSymbolIsTheTrailingBit()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(ATileWithoutSymbolIsTheTrailingBit));
        using var output = new PooledPayloadWriter(scope);
        var encoder = new Av1SymbolEncoder();
        encoder.Initialize(output, disableCdfUpdate: false);
        Assert.Equal(1, encoder.Finish());

        // SymbolValue is 15 bits of the stream: the first one is the trailing bit, the others are padding
        Assert.Equal([0x80], output.WrittenSpan.ToArray());
        new ReferenceSymbolDecoder(output.WrittenSpan, disableCdfUpdate: false).Exit();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 7)]
    [InlineData(4, 40)]
    [InlineData(5, 300)]
    [InlineData(6, 5000)]
    [InlineData(7, 5000)]
    [InlineData(8, 60000)]
    public void SymbolsBitsAndLiteralsAreDecodedByTheSpecificationDecoder(int seed, int count)
    {
        var random = new Random(seed);
        var distributions = CreateDistributions(random, 12);
        var operations = new (int Kind, int Distribution, int Value, int Bits)[count];
        for (var i = 0; i < count; i++)
        {
            var kind = random.Next(10);
            if (kind < 7)
            {
                var distribution = random.Next(distributions.Length);
                operations[i] = (0, distribution, PickSymbol(random, distributions[distribution]), 0);
            }
            else if (kind < 9)
            {
                operations[i] = (1, 0, random.Next(2), 1);
            }
            else
            {
                var bits = random.Next(1, 21);
                operations[i] = (2, 0, random.Next(1 << bits), bits);
            }
        }

        AssertRoundTrip(distributions, operations, disableCdfUpdate: false);
        AssertRoundTrip(distributions, operations, disableCdfUpdate: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImprobableAndProbableSymbolsCarryIntoTheWrittenBytes(bool mostProbable)
    {
        // A distribution with one symbol of nearly all the probability: its least probable symbols shrink the range by
        // 13 bits each, and the most probable one leaves the low end of the interval next to a carry for a long time
        ushort[] skewed = [4, 8, 32760, 32764, 32768, 0];
        var operations = new (int Kind, int Distribution, int Value, int Bits)[20000];
        var random = new Random(mostProbable ? 11 : 12);
        for (var i = 0; i < operations.Length; i++)
        {
            var value = mostProbable ? (random.Next(50) is 0 ? random.Next(5) : 2) : (random.Next(50) is 0 ? 2 : new[] { 0, 1, 3, 4 }[random.Next(4)]);
            operations[i] = (0, 0, value, 0);
        }

        AssertRoundTrip([skewed], operations, disableCdfUpdate: true);
        AssertRoundTrip([skewed], operations, disableCdfUpdate: false);
    }

    [Fact]
    public void NonSymmetricNumbersAreDecodedByTheLibraryDecoder()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(NonSymmetricNumbersAreDecodedByTheLibraryDecoder));
        using var output = new PooledPayloadWriter(scope);
        var encoder = new Av1SymbolEncoder();
        encoder.Initialize(output, disableCdfUpdate: false);
        var values = new List<(int Value, int Count)>();
        for (var valueCount = 1; valueCount <= 40; valueCount++)
        {
            for (var value = 0; value < valueCount; value++)
            {
                values.Add((value, valueCount));
                encoder.WriteNonSymmetric(value, valueCount);
            }
        }

        var length = encoder.Finish();
        var decoder = new Av1SymbolDecoder();
        decoder.Initialize(output.WrittenSpan.ToArray(), 0, length, disableCdfUpdate: false);
        foreach (var (value, valueCount) in values)
        {
            Assert.Equal(value, decoder.ReadNonSymmetric(valueCount));
        }

        decoder.Exit();
    }

    [Fact]
    public void ATileIsAppendedWithoutChangingTheBytesBeforeIt()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(ATileIsAppendedWithoutChangingTheBytesBeforeIt));
        using var output = new PooledPayloadWriter(scope);
        byte[] prefix = [0xFF, 0xFF, 0xFF, 0x00, 0xFF];
        output.Write(prefix);
        var encoder = new Av1SymbolEncoder();
        encoder.Initialize(output, disableCdfUpdate: true);
        ushort[] skewed = [32760, 32768, 0];
        var random = new Random(3);
        var symbols = new int[4000];
        for (var i = 0; i < symbols.Length; i++)
        {
            symbols[i] = random.Next(8) is 0 ? 1 : 0;
            encoder.WriteSymbol(skewed, symbols[i]);
        }

        var length = encoder.Finish();
        Assert.Equal(output.Length - prefix.Length, length);
        Assert.Equal(prefix, output.WrittenSpan[..prefix.Length].ToArray());
        var reference = new ReferenceSymbolDecoder(output.WrittenSpan[prefix.Length..], disableCdfUpdate: true);
        foreach (var symbol in symbols)
        {
            Assert.Equal(symbol, reference.ReadSymbol(skewed));
        }

        reference.Exit();
    }

    private static void AssertRoundTrip(ushort[][] distributions, (int Kind, int Distribution, int Value, int Bits)[] operations, bool disableCdfUpdate)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(Av1SymbolEncoderTests));
        using var output = new PooledPayloadWriter(scope);
        var encoder = new Av1SymbolEncoder();
        encoder.Initialize(output, disableCdfUpdate);
        var encoded = Clone(distributions);
        foreach (var (kind, distribution, value, bits) in operations)
        {
            switch (kind)
            {
                case 0:
                    encoder.WriteSymbol(encoded[distribution], value);
                    break;
                case 1:
                    encoder.WriteBool(value);
                    break;
                default:
                    encoder.WriteLiteral(value, bits);
                    break;
            }
        }

        var length = encoder.Finish();
        Assert.Equal(output.Length, length);
        var data = output.WrittenSpan.ToArray();

        // The specification decoder: the values, the adapted distributions and the conformance of the exit process
        var reference = new ReferenceSymbolDecoder(data, disableCdfUpdate);
        var referenceDistributions = Clone(distributions);
        for (var i = 0; i < operations.Length; i++)
        {
            var (kind, distribution, value, bits) = operations[i];
            var actual = kind switch
            {
                0 => reference.ReadSymbol(referenceDistributions[distribution]),
                1 => reference.ReadBool(),
                _ => reference.ReadLiteral(bits),
            };

            Assert.Equal(value, actual, message: $"operation {i} of {operations.Length}");
        }

        reference.Exit();
        for (var i = 0; i < distributions.Length; i++)
        {
            Assert.Equal(referenceDistributions[i], encoded[i]);
            if (disableCdfUpdate)
                Assert.Equal(distributions[i], encoded[i]);
        }

        // The decoder of the library, as a supplementary check
        var decoder = new Av1SymbolDecoder();
        decoder.Initialize(data, 0, data.Length, disableCdfUpdate);
        var decoded = Clone(distributions);
        foreach (var (kind, distribution, value, bits) in operations)
        {
            var actual = kind switch
            {
                0 => decoder.ReadSymbol(decoded[distribution]),
                1 => decoder.ReadBool(),
                _ => decoder.ReadLiteral(bits),
            };

            Assert.Equal(value, actual);
        }

        decoder.Exit();
    }

    private static ushort[][] Clone(ushort[][] distributions) => [.. distributions.Select(distribution => (ushort[])distribution.Clone())];

    // Cumulative distributions of 2 to 16 symbols in the form of the specification: increasing values that end with 32768,
    // then the adaptation counter. Some are uniform, some give nearly all the probability to one symbol
    private static ushort[][] CreateDistributions(Random random, int count)
    {
        var result = new ushort[count][];
        for (var i = 0; i < count; i++)
        {
            var n = random.Next(2, 17);
            var cdf = new ushort[n + 1];
            var cuts = new SortedSet<int>();
            while (cuts.Count < n - 1)
            {
                cuts.Add(random.Next(3) is 0 ? random.Next(1, 64) * (random.Next(2) is 0 ? 1 : 500) : random.Next(1, 32768));
            }

            var index = 0;
            foreach (var cut in cuts)
            {
                cdf[index++] = (ushort)cut;
            }

            cdf[n - 1] = 32768;
            result[i] = cdf;
        }

        return result;
    }

    private static int PickSymbol(Random random, ushort[] cdf)
    {
        // Half of the symbols follow the distribution, the others are uniform: improbable symbols are coded too
        var n = cdf.Length - 1;
        if (random.Next(2) is 0)
            return random.Next(n);

        var value = random.Next(32768);
        for (var symbol = 0; symbol < n; symbol++)
        {
            if (value < cdf[symbol])
                return symbol;
        }

        return n - 1;
    }
}
