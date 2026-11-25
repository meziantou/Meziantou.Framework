using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Compression;
using Meziantou.Framework.Compression.Internals.BZip2;
using Meziantou.Framework.Compression.Internals.Deflate64;
using Meziantou.Framework.Compression.Internals.ZipLegacy;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.CompressionFuzz;

// The targets of the streams of BZip2, Unix compress (.Z), Deflate64 and the legacy zip methods (shrink, reduce, implode)
public static partial class CompressionFuzzTargets
{
    private const string BZip2TestDataPrefix = "TestData/";

    // A small input can expand a lot (a bzip2 block of 45 bytes gives up to 45 MB): the targets read at most this length
    private const int MaxDecodedLength = 1 << 20;

    // A decoder that reads 1 byte at a time reads at most this length
    private const int MaxOneByteDecodedLength = 1 << 16;

    // The magic numbers of bzip2: the header, the start of the blocks (31 41 59 26 53 59), the ASCII start of the end of
    // the stream (17 72 45 38 50). The dictionaries are UTF-8: the bytes from 0x80 cannot be tokens. The members are
    // properties, not fields: All, in another file of the class, may be initialized first.
    private static string[] BZip2Tokens => ["BZh9", "BZh1", "1AY&SY", "\u0017rE8P", "\u0000\u0000\u0000\u0000"];

    private static string[] UnixCompressTokens => ["\u001F", "\u0010", "\u000C", "\u0000\u0001"];

    private static string[] DeflateTokens => ["\u0001\u0000\u0000", "\u0003", "\u0005", "\u0000\u0000"];

    private static FuzzTarget[] BZip2Targets =>
    [
        new("bzip2-decode", BZip2DecodeTarget) { Seeds = GetBZip2Seeds, Dictionary = BZip2Tokens },
        new("bzip2-roundtrip", BZip2RoundTripTarget) { Seeds = GetBZip2RoundTripSeeds, Dictionary = BZip2Tokens },
        new("unix-compress-decode", UnixCompressDecodeTarget) { Seeds = GetUnixCompressSeeds, Dictionary = UnixCompressTokens },
        new("deflate64-decode", Deflate64DecodeTarget) { Seeds = GetDeflate64Seeds, Dictionary = DeflateTokens },
        new("zip-legacy-decode", ZipLegacyDecodeTarget) { Seeds = GetZipLegacySeeds },
    ];

    /// <summary>
    /// <c>data</c>: bzip2 data. The decoder rejects invalid data with an <see cref="InvalidDataException"/>, and returns the same
    /// bytes when it is read 1 byte at a time.
    /// </summary>
    private static void BZip2DecodeTarget(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        CheckDecoder(() => new BZip2Stream(new MemoryStream(bytes), CompressionMode.Decompress), "bzip2");
    }

    /// <summary>
    /// <c>options, data</c>: the encoder compresses the data with blocks of <c>1 + options % 9</c> hundreds of kB (randomized
    /// when bit 4 of <c>options</c> is set), and the decoder returns the data. The encoder writes the same data whether the
    /// input is written at once or in 2 parts.
    /// </summary>
    private static void BZip2RoundTripTarget(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        using var compressed = new MemoryStream();
        using (var encoder = new BZip2EncoderStream(compressed, 1 + (options % 9), leaveOpen: true, randomize: (options & 0x10) is not 0))
        {
            // Write in 2 parts, to cross the boundary of the writes
            var half = data.Length / 2;
            encoder.Write(data[..half]);
            encoder.Write(data[half..]);
        }

        using var compressedAtOnce = new MemoryStream();
        using (var encoder = new BZip2EncoderStream(compressedAtOnce, 1 + (options % 9), leaveOpen: true, randomize: (options & 0x10) is not 0))
        {
            encoder.Write(data);
        }

        FuzzAssert.True(compressed.GetBuffer().AsSpan(0, (int)compressed.Length).SequenceEqual(compressedAtOnce.GetBuffer().AsSpan(0, (int)compressedAtOnce.Length)), "The bzip2 encoder does not write the same data when the input is written in 2 parts");

        compressed.Position = 0;
        using var decoder = new BZip2Stream(compressed, CompressionMode.Decompress);
        var output = ReadAll(decoder, 81920, int.MaxValue, out _);
        FuzzAssert.True(data.SequenceEqual(output), "The bzip2 decoder does not return the data of the encoder");
    }

    /// <summary>
    /// <c>data</c>: .Z data. The decoder rejects invalid data with an <see cref="InvalidDataException"/>, and returns the same
    /// bytes when it is read 1 byte at a time.
    /// </summary>
    private static void UnixCompressDecodeTarget(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        CheckDecoder(() => new UnixCompressStream(new MemoryStream(bytes)), "compress");
    }

    /// <summary>
    /// <c>options, data</c>: raw Deflate64 data (Deflate when bit 0 of <c>options</c> is set). The decoder rejects invalid data
    /// with an <see cref="InvalidDataException"/>, and returns the same bytes when it is read 1 byte at a time. At the end of
    /// the data, the input it did not consume is the end of the input. The Deflate data it decodes are the data that
    /// <see cref="DeflateStream"/> decodes, when it decodes them.
    /// </summary>
    private static void Deflate64DecodeTarget(ReadOnlySpan<byte> data)
    {
        var deflate64 = (FuzzInput.ReadByte(ref data) & 1) is 0;
        var bytes = data.ToArray();
        CheckDecoder(() => new Deflate64DecoderStream(new MemoryStream(bytes), deflate64, leaveOpen: false), "deflate");

        using var decoder = new Deflate64DecoderStream(new MemoryStream(bytes), deflate64, leaveOpen: false);
        var output = ReadAll(decoder, 81920, MaxDecodedLength, out var error);
        if (error is not null || !decoder.IsCompleted)
            return;

        FuzzAssert.True(bytes.AsSpan().EndsWith(decoder.GetUnconsumedInput().Span), "The unconsumed input is not the end of the input");
        if (deflate64)
            return;

        byte[]? reference;
        try
        {
            using var deflate = new DeflateStream(new MemoryStream(bytes), CompressionMode.Decompress);
            reference = ReadAll(deflate, 81920, MaxDecodedLength + 1, out _);
        }
        catch (InvalidDataException)
        {
            reference = null;
        }

        if (reference is not null)
        {
            FuzzAssert.True(reference.AsSpan().SequenceEqual(output), "The Deflate decoder does not return what DeflateStream decodes");
        }
    }

    /// <summary>
    /// <c>method, length (2 bytes, little-endian), data</c>: the data of a legacy zip method and the length of the output.
    /// <c>method % 9</c> is shrink (0), reduce with the factors 1 to 4 (1 to 4), or implode (5 to 8: bit 0 for the window
    /// of 8 KiB, bit 1 for the literal tree). The decoder rejects invalid data with an <see cref="InvalidDataException"/>,
    /// returns at most the length, and returns the same bytes when it is read 1 byte at a time.
    /// </summary>
    private static void ZipLegacyDecodeTarget(ReadOnlySpan<byte> data)
    {
        var method = FuzzInput.ReadByte(ref data) % 9;
        var length = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
        var bytes = data.ToArray();
        CheckDecoder(() => method switch
        {
            0 => new ZipShrinkDecoderStream(new MemoryStream(bytes), length, leaveOpen: false),
            <= 4 => new ZipReduceDecoderStream(new MemoryStream(bytes), method, length, leaveOpen: false),
            _ => new ZipImplodeDecoderStream(new MemoryStream(bytes), largeWindow: ((method - 5) & 1) is not 0, literalTree: ((method - 5) & 2) is not 0, length, leaveOpen: false),
        }, "zip legacy", length);
    }

    /// <summary>Decodes with large reads and with reads of 1 byte: the outputs must agree, and so must the errors when both reach the end.</summary>
    private static void CheckDecoder(Func<Stream> create, string name, int? expectedLength = null)
    {
        byte[] expected;
        Exception? expectedError;
        using (var decoder = create())
        {
            expected = ReadAll(decoder, 81920, MaxDecodedLength, out expectedError);
        }

        if (expectedLength is { } length && expectedError is null)
        {
            FuzzAssert.Equal(length, expected.Length, $"The {name} decoder does not return the length of the output");
        }

        byte[] actual;
        Exception? actualError;
        using (var decoder = create())
        {
            actual = ReadAll(decoder, 1, MaxOneByteDecodedLength, out actualError);
        }

        var common = Math.Min(expected.Length, actual.Length);
        FuzzAssert.True(expected.AsSpan(0, common).SequenceEqual(actual.AsSpan(0, common)), $"The {name} decoder returns other bytes when it is read 1 byte at a time");
        // A read that fails loses the bytes it decoded: the lengths are compared when both reads succeed
        if (expected.Length < MaxDecodedLength && actual.Length < MaxOneByteDecodedLength)
        {
            FuzzAssert.Equal(expectedError is null, actualError is null, $"The {name} decoder rejects the data with one read size only: {expectedError?.Message} / {actualError?.Message}");
            if (expectedError is null)
            {
                FuzzAssert.Equal(expected.Length, actual.Length, $"The {name} decoder returns another length when it is read 1 byte at a time");
            }
        }
    }

    /// <summary>Reads at most <paramref name="maxLength"/> bytes; an <see cref="InvalidDataException"/> stops the read.</summary>
    private static byte[] ReadAll(Stream stream, int readSize, int maxLength, out Exception? error)
    {
        using var output = new MemoryStream();
        var buffer = new byte[readSize];
        error = null;
        try
        {
            while (output.Length < maxLength)
            {
                var read = stream.Read(buffer, 0, (int)Math.Min(readSize, maxLength - output.Length));
                if (read is 0)
                    break;

                output.Write(buffer, 0, read);
            }
        }
        catch (InvalidDataException ex)
        {
            error = ex;
        }

        return output.ToArray();
    }

    private static IEnumerable<FuzzSeed> GetBZip2Seeds() => GetCompressionTestFiles("BZip2/", ".bz2").Select(file => new FuzzSeed(file.Name, file.Data));

    private static IEnumerable<FuzzSeed> GetBZip2RoundTripSeeds()
    {
        yield return new FuzzSeed("empty", new byte[] { 8 });
        yield return new FuzzSeed("runs", (byte[])[0x10, .. Enumerable.Repeat((byte)'a', 300), .. "abcabcabc"u8, .. Enumerable.Repeat((byte)0, 5)]);
        foreach (var (name, data) in GetCompressionTestFiles("BZip2/", ".bz2"))
        {
            yield return new FuzzSeed(name, (byte[])[0, .. data]);
        }
    }

    private static IEnumerable<FuzzSeed> GetUnixCompressSeeds() => GetCompressionTestFiles("UnixCompress/", ".Z").Select(file => new FuzzSeed(file.Name, file.Data));

    private static IEnumerable<FuzzSeed> GetDeflate64Seeds()
    {
        foreach (var (name, entry) in GetZipEntries())
        {
            if (entry.Method is 9)
            {
                yield return new FuzzSeed(name, (byte[])[0, .. entry.Data]);
            }
        }

        // A fixed Huffman block of Deflate64: 'a', the length code 285 with 16 extra bits (997), the distance 1
        yield return new FuzzSeed("deflate64-length285", (byte[])[0, 0x4B, 0x1C, 0x2D, 0x1F, 0x00, 0x00]);

        byte[] text = [.. "Deflate64 decodes Deflate too: Deflate, Deflate64, Deflate, Deflate64."u8];
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(text);
        }

        yield return new FuzzSeed("deflate", (byte[])[1, .. output.ToArray(), .. "trailing"u8]);
    }

    private static IEnumerable<FuzzSeed> GetZipLegacySeeds()
    {
        foreach (var (name, entry) in GetZipEntries())
        {
            byte? method = entry.Method switch
            {
                1 => 0,
                6 => (byte)(5 + ((entry.Flags & 2) is not 0 ? 1 : 0) + ((entry.Flags & 4) is not 0 ? 2 : 0)),
                _ => null,
            };

            if (method is { } value && entry.UncompressedSize <= ushort.MaxValue)
            {
                yield return new FuzzSeed(name, (byte[])[value, (byte)entry.UncompressedSize, (byte)(entry.UncompressedSize >> 8), .. entry.Data]);
            }
        }

        // Reduce (factor 1): empty follower sets (256 times 6 bits of 0), then the literals 'a', 'b', 'c'
        yield return new FuzzSeed("reduce", (byte[])[1, 3, 0, .. new byte[192], (byte)'a', (byte)'b', (byte)'c']);
    }

    /// <summary>The entries of the zip files of the test data, from their local headers.</summary>
    private static IEnumerable<(string Name, (int Method, int Flags, int UncompressedSize, byte[] Data) Entry)> GetZipEntries()
    {
        foreach (var (fileName, data) in GetCompressionTestFiles("ZipLegacy/", ".zip").Concat(GetCompressionTestFiles("ZipLegacy/", ".ZIP")))
        {
            var offset = 0;
            var index = 0;
            while (offset + 30 <= data.Length && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) is 0x04034B50)
            {
                var header = data.AsSpan(offset, 30);
                var flags = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
                var method = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
                var compressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[18..]);
                var uncompressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[22..]);
                var start = offset + 30 + BinaryPrimitives.ReadUInt16LittleEndian(header[26..]) + BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
                yield return ($"{fileName}-{index++}", (method, flags, uncompressedSize, data.AsSpan(start, compressedSize).ToArray()));
                offset = start + compressedSize;
            }
        }
    }

    private static IEnumerable<(string Name, byte[] Data)> GetCompressionTestFiles(string folder, string extension)
    {
        var assembly = typeof(CompressionFuzzTargets).Assembly;
        var prefix = BZip2TestDataPrefix + folder;
        foreach (var resourceName in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            // The separator of the folders is that of the operating system that built the project
            var name = resourceName.Replace('\\', '/');
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(extension, StringComparison.Ordinal))
                continue;

            using var stream = FuzzResources.Open(assembly, resourceName);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return (name[BZip2TestDataPrefix.Length..].Replace('/', '-'), memory.ToArray());
        }
    }
}
