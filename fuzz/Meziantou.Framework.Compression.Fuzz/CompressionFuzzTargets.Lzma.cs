using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Compression;
using Meziantou.Framework.Compression.Internals.Filters;
using Meziantou.Framework.Compression.Internals.Lzma;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.CompressionFuzz;

/// <summary>
/// The XZ, .lzma and lzip readers, the raw LZMA and LZMA2 decoders and the BCJ2 decoder reject invalid data with an
/// <see cref="InvalidDataException"/> (a <see cref="NotSupportedException"/> for an XZ filter or option they do not support),
/// and return the same data whether they are read 1 byte at a time from an input read 1 byte at a time, or in large reads.
/// The XZ, .lzma and lzip encoders produce data that the decoders read back unchanged.
/// </summary>
public static partial class CompressionFuzzTargets
{
    private const string LzmaTestDataPrefix = "TestData/Lzma/";

    // The decoders stop after 64 KiB of output: LZMA can expand a few bytes to gigabytes
    private const int MaxLzmaDecodedLength = 1 << 16;

    private static string[] LzmaTokens =>
    [
        "\u00FD7zXZ\u0000", "YZ", "\u0000\u0004", "\u0000\u0001", "\u0000\u0000", "\u0000\u000A", "LZIP", "LZIP\u0001", "]\u0000\u0000\u0080\u0000",
        "!\u0001", "\u0003\u0001", "\u0004\u0000", "\u0005\u0000", "\u0006\u0000", "\u0007\u0000", "\u0008\u0000", "\u0009\u0000", "\u000A\u0000", "\u000B\u0000",
        "\u00FF\u00FF\u00FF\u00FF\u00FF\u00FF\u00FF\u00FF", "\u00E0", "\u00C0", "\u0080", "\u0001", "\u0002", "\u00E8", "\u00E9", "\u000F\u0080",
    ];

    // A property computed on each access: the initializer of All, in another file, may run before the initializers of this file
    private static IReadOnlyList<FuzzTarget> LzmaTargets =>
    [
        new("xz-decode", XzDecodeTarget) { Seeds = () => GetLzmaFileSeeds(".xz"), Dictionary = LzmaTokens },
        new("lzma-decode", LzmaDecodeTarget) { Seeds = () => GetLzmaFileSeeds(".lzma"), Dictionary = LzmaTokens },
        new("lzip-decode", LZipDecodeTarget) { Seeds = () => GetLzmaFileSeeds(".lz"), Dictionary = LzmaTokens },
        new("lzma-raw-decode", LzmaRawDecodeTarget) { Seeds = GetLzmaRawSeeds, Dictionary = LzmaTokens },
        new("bcj2-decode", Bcj2DecodeTarget) { Seeds = GetBcj2Seeds, Dictionary = LzmaTokens },
        new("xz-roundtrip", XzRoundTripTarget) { Seeds = GetRoundTripSeeds, Dictionary = LzmaTokens },
        new("lzma-roundtrip", LzmaRoundTripTarget) { Seeds = GetRoundTripSeeds, Dictionary = LzmaTokens },
        new("lzip-roundtrip", LZipRoundTripTarget) { Seeds = GetRoundTripSeeds, Dictionary = LzmaTokens },
    ];

    /// <summary><c>data</c>: an XZ file.</summary>
    private static void XzDecodeTarget(ReadOnlySpan<byte> data)
    {
        var input = data.ToArray();
        CheckDecoders(input, stream => new XzStream(stream, CompressionMode.Decompress), "XzStream");
    }

    /// <summary><c>data</c>: a .lzma file.</summary>
    private static void LzmaDecodeTarget(ReadOnlySpan<byte> data)
    {
        var input = data.ToArray();
        CheckDecoders(input, stream => new LzmaStream(stream, CompressionMode.Decompress), "LzmaStream");
    }

    /// <summary><c>data</c>: an lzip file.</summary>
    private static void LZipDecodeTarget(ReadOnlySpan<byte> data)
    {
        var input = data.ToArray();
        CheckDecoders(input, stream => new LZipStream(stream, CompressionMode.Decompress), "LZipStream");
    }

    /// <summary>
    /// <c>mode, ...</c>: with an even mode, raw LZMA data: the 5 bytes of the properties, the length of the output (2 bytes,
    /// little-endian, 0xFFFF for an unknown length that ends with the end marker), then the data. With an odd mode, LZMA2
    /// data: the dictionary property, then the data.
    /// </summary>
    private static void LzmaRawDecodeTarget(ReadOnlySpan<byte> data)
    {
        var mode = FuzzInput.ReadByte(ref data);
        if ((mode & 1) == 0)
        {
            if (data.Length < 7)
                return;

            var properties = data[..5].ToArray();
            var length = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);
            var outputLength = length == 0xFFFF ? -1 : length;
            var input = data[7..].ToArray();
            CheckDecoders(input, stream => new LzmaDecoderStream(stream, properties, outputLength, leaveOpen: false), "LzmaDecoderStream");
        }
        else
        {
            var property = FuzzInput.ReadByte(ref data);
            var input = data.ToArray();
            CheckDecoders(input, stream => new Lzma2DecoderStream(stream, property, leaveOpen: false), "Lzma2DecoderStream");
        }
    }

    /// <summary>
    /// <c>main length (2 bytes), call length, jump length, output length (2 bytes, 0xFFFF for unknown), main, call, jump,
    /// range coder</c>: the four streams of BCJ2 (the lengths are little-endian, and are truncated to the data).
    /// </summary>
    private static void Bcj2DecodeTarget(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
            return;

        var mainLength = Math.Min(BinaryPrimitives.ReadUInt16LittleEndian(data), data.Length - 6);
        var callLength = Math.Min(data[2], data.Length - 6 - mainLength);
        var jumpLength = Math.Min(data[3], data.Length - 6 - mainLength - callLength);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        var outputLength = length == 0xFFFF ? -1 : length;
        var streams = data[6..].ToArray();
        var main = streams[..mainLength];
        var call = streams.AsSpan(mainLength, callLength).ToArray();
        var jump = streams.AsSpan(mainLength + callLength, jumpLength).ToArray();
        var rangeCoder = streams[(mainLength + callLength + jumpLength)..];

        var expected = Decode(chunk => new Bcj2DecoderStream(new ChunkedReadStream(main, chunk), new ChunkedReadStream(call, chunk), new ChunkedReadStream(jump, chunk), new ChunkedReadStream(rangeCoder, chunk), outputLength, leaveOpen: false), inputChunkSize: int.MaxValue, readSize: 1 << 16);
        var actual = Decode(chunk => new Bcj2DecoderStream(new ChunkedReadStream(main, chunk), new ChunkedReadStream(call, chunk), new ChunkedReadStream(jump, chunk), new ChunkedReadStream(rangeCoder, chunk), outputLength, leaveOpen: false), inputChunkSize: 1, readSize: 1);
        Compare(expected, actual, "Bcj2DecoderStream");
        if (expected.Error is null && outputLength >= 0)
        {
            FuzzAssert.Equal(outputLength, expected.Output.Length, "Bcj2DecoderStream does not return the declared length");
        }
    }

    /// <summary><c>preset, options, data</c>: XZ compression with a check and a block size from <c>options</c>.</summary>
    private static void XzRoundTripTarget(ReadOnlySpan<byte> data)
    {
        var preset = FuzzInput.ReadByte(ref data) % 10;
        var options = FuzzInput.ReadByte(ref data);
        var compressionOptions = new XzCompressionOptions
        {
            Preset = preset,
            Check = (XzCheck)(options & 3),
            BlockSize = (options >> 2) & 7,
            Extreme = (options & 0x20) != 0,
            DictionarySize = (options & 0x40) != 0 ? 4096 : null,
        };
        compressionOptions.BlockSize *= 1000;
        CheckRoundTrip(data.ToArray(), stream => new XzStream(stream, compressionOptions, leaveOpen: true), stream => new XzStream(stream, CompressionMode.Decompress), "XzStream");
    }

    /// <summary><c>preset, options, data</c>: .lzma compression with lc, lp and pb from <c>options</c>.</summary>
    private static void LzmaRoundTripTarget(ReadOnlySpan<byte> data)
    {
        var preset = FuzzInput.ReadByte(ref data) % 10;
        var options = FuzzInput.ReadByte(ref data);
        var compressionOptions = new LzmaCompressionOptions
        {
            Preset = preset,
            LiteralContextBits = options % 9,
            LiteralPositionBits = (options / 9) % 5,
            PositionBits = (options / 45) % 5,
            DictionarySize = (options & 0x80) != 0 ? 4096 : null,
        };
        CheckRoundTrip(data.ToArray(), stream => new LzmaStream(stream, compressionOptions, leaveOpen: true), stream => new LzmaStream(stream, CompressionMode.Decompress), "LzmaStream");
    }

    /// <summary><c>preset, options, data</c>: lzip compression with a member size from <c>options</c>.</summary>
    private static void LZipRoundTripTarget(ReadOnlySpan<byte> data)
    {
        var preset = FuzzInput.ReadByte(ref data) % 10;
        var options = FuzzInput.ReadByte(ref data);
        var compressionOptions = new LZipCompressionOptions
        {
            Preset = preset,
            MemberSize = (options & 7) * 500,
            DictionarySize = (options & 0x80) != 0 ? 5000 : null,
        };
        CheckRoundTrip(data.ToArray(), stream => new LZipStream(stream, compressionOptions, leaveOpen: true), stream => new LZipStream(stream, CompressionMode.Decompress), "LZipStream");
    }

    private static void CheckRoundTrip(byte[] data, Func<Stream, Stream> createEncoder, Func<Stream, Stream> createDecoder, string name)
    {
        using var output = new MemoryStream();
        using (var encoder = createEncoder(output))
        {
            // Writes of various sizes
            var position = 0;
            var size = 1;
            while (position < data.Length)
            {
                var count = Math.Min(size, data.Length - position);
                encoder.Write(data, position, count);
                position += count;
                size = (size * 3) + 1;
            }
        }

        output.Position = 0;
        using var decoder = createDecoder(output);
        using var result = new MemoryStream();
        decoder.CopyTo(result);
        FuzzAssert.True(data.AsSpan().SequenceEqual(result.ToArray()), $"{name} does not decompress the data it compressed");
    }

    /// <summary>Decodes with large reads, then with 1-byte reads of an input read 1 byte at a time: the results must agree.</summary>
    private static void CheckDecoders(byte[] input, Func<Stream, Stream> createDecoder, string name)
    {
        var expected = Decode(chunk => createDecoder(new ChunkedReadStream(input, chunk)), inputChunkSize: int.MaxValue, readSize: 1 << 16);
        var actual = Decode(chunk => createDecoder(new ChunkedReadStream(input, chunk)), inputChunkSize: 1, readSize: 1);
        Compare(expected, actual, name);
    }

    private static void Compare(DecodeResult expected, DecodeResult actual, string name)
    {
        var common = Math.Min(expected.Output.Length, actual.Output.Length);
        FuzzAssert.True(expected.Output.AsSpan(0, common).SequenceEqual(actual.Output.AsSpan(0, common)), $"{name} returns other data with 1-byte reads");
        if (expected.Output.Length >= MaxLzmaDecodedLength || actual.Output.Length >= MaxLzmaDecodedLength)
            return;

        FuzzAssert.Equal(expected.Error?.GetType(), actual.Error?.GetType(), $"{name} does not fail the same way with 1-byte reads: {expected.Error?.Message} / {actual.Error?.Message}");
        if (expected.Error is null)
        {
            FuzzAssert.Equal(expected.Output.Length, actual.Output.Length, $"{name} returns another length with 1-byte reads");
        }
    }

    private static DecodeResult Decode(Func<int, Stream> createDecoder, int inputChunkSize, int readSize)
    {
        using var output = new MemoryStream();
        try
        {
            using var decoder = createDecoder(inputChunkSize);
            var buffer = new byte[readSize];
            while (output.Length < MaxLzmaDecodedLength)
            {
                var read = decoder.Read(buffer, 0, (int)Math.Min(buffer.Length, MaxLzmaDecodedLength - output.Length));
                if (read == 0)
                    break;

                output.Write(buffer, 0, read);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            return new DecodeResult(output.ToArray(), ex);
        }

        return new DecodeResult(output.ToArray(), Error: null);
    }

    private static IEnumerable<FuzzSeed> GetLzmaFileSeeds(string extension)
    {
        foreach (var (name, data) in GetLzmaTestFiles())
        {
            if (name.EndsWith(extension, StringComparison.Ordinal))
                yield return new FuzzSeed(name, data);
        }
    }

    private static IEnumerable<FuzzSeed> GetLzmaRawSeeds()
    {
        var files = GetLzmaTestFiles().ToDictionary(file => file.Name, file => file.Data, StringComparer.Ordinal);

        // The packed streams of the 7z archives (see TestData/Lzma/README.md)
        var lzma = files["7z-lzma.7z"];
        yield return new FuzzSeed("7z-lzma", (byte[])[0, 0x5D, 0x00, 0x20, 0x00, 0x00, 0x2F, 0x18, .. lzma.AsSpan(32, 1382)]);
        var lzma2 = files["7z-lzma2.7z"];
        yield return new FuzzSeed("7z-lzma2", (byte[])[1, 0x02, .. lzma2.AsSpan(32, 1389)]);

        // The LZMA data of the .lzma file made by xz, which ends with an end marker
        var alone = files["lzma-unknown-size.lzma"];
        yield return new FuzzSeed("lzma-end-marker", (byte[])[0, .. alone.AsSpan(0, 5), 0xFF, 0xFF, .. alone.AsSpan(13)]);
    }

    private static IEnumerable<FuzzSeed> GetBcj2Seeds()
    {
        var archive = GetLzmaTestFiles().Single(file => file.Name == "7z-bcj2.7z").Data;
        byte[] properties = [0x5D, 0x00, 0x10, 0x00, 0x00];
        var main = DecodeLzma(archive.AsSpan(32, 2004), properties, 2396);
        var call = DecodeLzma(archive.AsSpan(2047, 15), properties, 12);
        var jump = DecodeLzma(archive.AsSpan(2062, 25), properties, 24);
        var rangeCoder = archive.AsSpan(2036, 11);
        var header = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)main.Length);
        header[2] = (byte)call.Length;
        header[3] = (byte)jump.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 2432);
        yield return new FuzzSeed("7z-bcj2", (byte[])[.. header, .. main, .. call, .. jump, .. rangeCoder]);
        yield return new FuzzSeed("no-call", (byte[])[3, 0, 0, 0, 3, 0, 0xE8, 1, 2, 0, 0, 0, 0, 0]);

        static byte[] DecodeLzma(ReadOnlySpan<byte> data, byte[] properties, int length)
        {
            using var decoder = new LzmaDecoderStream(new MemoryStream(data.ToArray()), properties, length, leaveOpen: false);
            using var output = new MemoryStream();
            decoder.CopyTo(output);
            return output.ToArray();
        }
    }

    private static IEnumerable<FuzzSeed> GetRoundTripSeeds()
    {
        // The .txt files are not embedded: the input of the filters is binary data with text at its end
        var binary = GetLzmaTestFiles().Single(file => file.Name == "filter-input.bin").Data;
        var text = "LZMA uses a dictionary, a range coder, and an optimal parser. LZMA2 splits the data in chunks. "u8.ToArray();
        yield return new FuzzSeed("empty", (byte[])[6, 0]);
        yield return new FuzzSeed("one-byte", (byte[])[0, 0, (byte)'a']);
        yield return new FuzzSeed("text-preset-6", (byte[])[6, 0x05, .. text]);
        yield return new FuzzSeed("text-preset-1", (byte[])[1, 0x9A, .. text]);
        yield return new FuzzSeed("text-preset-9", (byte[])[9, 0x6E, .. text, .. text]);
        yield return new FuzzSeed("binary-preset-4", (byte[])[4, 0x1F, .. binary]);
        yield return new FuzzSeed("zeros", (byte[])[3, 0x0B, .. new byte[5000]]);
    }

    private static IEnumerable<(string Name, byte[] Data)> GetLzmaTestFiles()
    {
        var assembly = typeof(CompressionFuzzTargets).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.StartsWith(LzmaTestDataPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, resourceName);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return (resourceName[LzmaTestDataPrefix.Length..], memory.ToArray());
        }
    }

    private sealed record DecodeResult(byte[] Output, Exception? Error);

    /// <summary>A read-only stream whose reads return at most <c>chunkSize</c> bytes.</summary>
    private sealed class ChunkedReadStream(byte[] data, int chunkSize) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, chunkSize), data.Length - _position);
            data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
