using System.Buffers.Binary;
using Meziantou.Framework.Compression.Internals.Ppmd;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.CompressionFuzz;

/// <summary>
/// The PPMd codecs: the 7z coder (PPMd var.H), zip method 98 (PPMd var.I rev. 1) and the PPMd blocks of RAR 2.9/4.x. The decoders
/// reject invalid data with an <see cref="InvalidDataException"/>, or decode bytes; the encoder produces data the decoder reads
/// back unchanged.
/// </summary>
public static partial class CompressionFuzzTargets
{
    private const int PpmdMaxMemoryUsage = 16 << 20;
    private const int PpmdMaxOutputLength = 1 << 20;
    private const int PpmdMaxCheckedLength = 64 << 10;

    /// <summary>The orders, the memory sizes, the zip headers (order, memory, restore method) and the bytes of the range coders.</summary>
    private static string[] PpmdTokens =>
    [
        "\u0002", "\u0006", "\u0010", " ", "@",
        "\0\0\u0001\0", "\0\0\u0010\0", "\0\0\0\u0001", "\0\u0008\0\0",
        "\u0007\0", "\u0007\u0010", "\u000F\u0010", "\u0001\0", "\uDCF7\u0010",
        "\0\0\0\0", "\uDCFF\uDCFF\uDCFF\uDCFF", "\0\uDCFF",
    ];

    private static FuzzTarget[] PpmdTargets =>
    [
        new("ppmd7-decode", Ppmd7DecodeTarget) { Seeds = () => GetPpmdSeeds(".ppmd7"), Dictionary = PpmdTokens },
        new("ppmd8-decode", Ppmd8DecodeTarget) { Seeds = () => GetPpmdSeeds(".ppmd8"), Dictionary = PpmdTokens },
        new("ppmd-rar-decode", PpmdRarDecodeTarget) { Seeds = () => GetPpmdSeeds(".ppmd7a"), Dictionary = PpmdTokens },
        new("ppmd7-roundtrip", Ppmd7RoundTripTarget) { Seeds = GetPpmd7RoundTripSeeds, Dictionary = PpmdTokens },
    ];

    /// <summary>The 5 bytes of the 7z coder properties (order, memory size), then the packed stream, decoded up to its end marker.</summary>
    private static void Ppmd7DecodeTarget(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
            return;

        int order = data[0];
        var memorySize = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
        var packed = data[5..].ToArray();
        byte[] decoded;
        try
        {
            using var decoder = new Ppmd7DecoderStream(new MemoryStream(packed), order, memorySize, outputLength: -1, PpmdMaxMemoryUsage, leaveOpen: false);
            decoded = ReadPpmdOutput(decoder);
        }
        catch (InvalidDataException)
        {
            return;
        }

        // The decoded bytes, encoded again with the same parameters, decode to themselves
        if (decoded.Length > PpmdMaxCheckedLength)
            return;

        var encoded = EncodePpmd7(decoded, order, memorySize);
        using var check = new Ppmd7DecoderStream(new MemoryStream(encoded), order, memorySize, decoded.Length, PpmdMaxMemoryUsage, leaveOpen: false);
        FuzzAssert.True(ReadPpmdOutput(check).AsSpan().SequenceEqual(decoded), "The decoded data does not round trip");
    }

    /// <summary>The data of a zip entry of method 98: 2 bytes of parameters (order, memory size, restore method), then the stream.</summary>
    private static void Ppmd8DecodeTarget(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return;

        var parameters = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var order = (parameters & 0xF) + 1;
        var memorySize = (uint)(((parameters >> 4) & 0xFF) + 1) << 20;
        var restoreMethod = parameters >> 12;
        var packed = data[2..].ToArray();
        byte[] decoded;
        try
        {
            using var decoder = new Ppmd8DecoderStream(new MemoryStream(packed), order, memorySize, restoreMethod, outputLength: -1, PpmdMaxMemoryUsage, leaveOpen: false);
            decoded = ReadPpmdOutput(decoder);
        }
        catch (InvalidDataException)
        {
            return;
        }

        // Decoding a known length gives the same bytes
        if (decoded.Length > PpmdMaxCheckedLength)
            return;

        using var again = new Ppmd8DecoderStream(new MemoryStream(packed), order, memorySize, restoreMethod, decoded.Length, PpmdMaxMemoryUsage, leaveOpen: false);
        FuzzAssert.True(ReadPpmdOutput(again).AsSpan().SequenceEqual(decoded), "Decoding a known length gives other bytes");
    }

    /// <summary>
    /// The order, the memory size in MiB minus one (modulo 16), then the bytes of the blocks: the decoder decodes a block up to
    /// an error, then continues the model with a new range decoder on the remaining bytes, as RAR does for the next block.
    /// </summary>
    private static void PpmdRarDecodeTarget(ReadOnlySpan<byte> data)
    {
        int order = FuzzInput.ReadByte(ref data);
        var memorySize = ((FuzzInput.ReadByte(ref data) & 0xF) + 1) << 20;
        var source = new PpmdFuzzByteSource(data.ToArray());
        var decoder = new PpmdRarDecoder();
        try
        {
            for (var block = 0; block < 3; block++)
            {
                if (!decoder.Initialize(source, order, memorySize, resetModel: block == 0, PpmdMaxMemoryUsage))
                {
                    if (source.IsAtEnd)
                        return;

                    continue;
                }

                for (var i = 0; i < PpmdMaxOutputLength; i++)
                {
                    var symbol = decoder.DecodeSymbol();
                    FuzzAssert.True(symbol is >= -1 and <= 255, $"The symbol {symbol} is not a byte or -1");
                    if (symbol < 0)
                        break;
                }
            }
        }
        catch (InvalidDataException)
        {
        }
    }

    /// <summary>The order (2 to 64), the memory size (2 KiB to 1 MiB), then the data to encode and decode.</summary>
    private static void Ppmd7RoundTripTarget(ReadOnlySpan<byte> data)
    {
        var order = 2 + (FuzzInput.ReadByte(ref data) % 63);
        var memorySize = 2048u << (FuzzInput.ReadByte(ref data) % 10);
        var input = data.ToArray();
        var encoded = EncodePpmd7(input, order, memorySize);
        using var decoder = new Ppmd7DecoderStream(new MemoryStream(encoded), order, memorySize, input.Length, PpmdMaxMemoryUsage, leaveOpen: false);
        FuzzAssert.True(ReadPpmdOutput(decoder).AsSpan().SequenceEqual(input), "The data does not round trip");
        FuzzAssert.True(EncodePpmd7(input, order, memorySize).AsSpan().SequenceEqual(encoded), "The encoding is not deterministic");
    }

    private static byte[] EncodePpmd7(byte[] data, int order, uint memorySize)
    {
        using var output = new MemoryStream();
        using (var encoder = new Ppmd7EncoderStream(output, order, memorySize, leaveOpen: true))
        {
            encoder.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] ReadPpmdOutput(Stream decoder)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while (output.Length < PpmdMaxOutputLength && (read = decoder.Read(buffer)) > 0)
        {
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static IEnumerable<FuzzSeed> GetPpmdSeeds(string extension)
    {
        const string Prefix = "TestData/Ppmd/";
        var assembly = typeof(CompressionFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(extension, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            yield return new FuzzSeed(name[Prefix.Length..], ReadPpmdResource(assembly, name));
        }
    }

    private static IEnumerable<FuzzSeed> GetPpmd7RoundTripSeeds()
    {
        const string Prefix = "TestData/Ppmd/";
        var assembly = typeof(CompressionFuzzTargets).Assembly;
        var parameters = new (byte Order, byte Memory)[] { (4, 5), (0, 0), (30, 9), (62, 3) };
        var index = 0;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var (order, memory) = parameters[index++ % parameters.Length];
            yield return new FuzzSeed(name[Prefix.Length..], (byte[])[order, memory, .. ReadPpmdResource(assembly, name)]);
        }
    }

    private static byte[] ReadPpmdResource(System.Reflection.Assembly assembly, string name)
    {
        using var stream = FuzzResources.Open(assembly, name);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed class PpmdFuzzByteSource(byte[] data) : PpmdByteSource
    {
        private int _position;

        public bool IsAtEnd => _position >= data.Length;

        public override int ReadByte() => _position < data.Length ? data[_position++] : -1;
    }
}
