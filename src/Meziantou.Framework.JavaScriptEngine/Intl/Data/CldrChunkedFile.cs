using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.JavaScriptEngine.Intl.Data;

/// <summary>
/// A data file made of independently Brotli-compressed chunks, so that only the chunks in use are decompressed. The
/// chunks are decompressed on first use and kept; the instances are thread-safe.
/// <code>
/// "MFCD" formatVersion:u8 reserved:u8[3] chunkCount:u32 offset:u32[chunkCount + 1]   (little endian, from the file start)
/// chunk: uncompressedLength:u32 brotli-data
/// </code>
/// </summary>
internal sealed class CldrChunkedFile
{
    public const byte FormatVersion = 1;
    private const int HeaderSize = 12;

    private readonly string? _resourceName;
    private readonly byte[]? _content;
    private readonly uint[] _offsets;
    private readonly byte[]?[] _chunks;

    private CldrChunkedFile(string? resourceName, byte[]? content, ReadOnlySpan<byte> offsets)
    {
        _resourceName = resourceName;
        _content = content;
        var chunkCount = (offsets.Length / 4) - 1;
        _offsets = new uint[chunkCount + 1];
        for (var i = 0; i < _offsets.Length; i++)
        {
            _offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(offsets[(i * 4)..]);
        }

        _chunks = new byte[]?[chunkCount];
    }

    public int ChunkCount => _chunks.Length;

    /// <summary>Gets the size of the file, in bytes.</summary>
    public long Length => _offsets[^1];

    /// <summary>Opens a data file embedded in the engine assembly.</summary>
    public static CldrChunkedFile FromResource(string resourceName)
    {
        using var stream = OpenResource(resourceName);
        var header = new byte[HeaderSize];
        stream.ReadExactly(header);
        var offsets = new byte[GetOffsetsLength(header)];
        stream.ReadExactly(offsets);
        return new CldrChunkedFile(resourceName, content: null, offsets);
    }

    /// <summary>Opens a data file from its content.</summary>
    public static CldrChunkedFile FromBytes(byte[] content)
    {
        return new CldrChunkedFile(resourceName: null, content, content.AsSpan(HeaderSize, GetOffsetsLength(content)));
    }

    /// <summary>Gets the decompressed content of a chunk.</summary>
    public byte[] GetChunk(int index)
    {
        var chunk = Volatile.Read(ref _chunks[index]);
        if (chunk is null)
        {
            chunk = Decompress(ReadCompressedChunk(index));
            chunk = Interlocked.CompareExchange(ref _chunks[index], chunk, comparand: null) ?? chunk;
        }

        return chunk;
    }

    /// <summary>Decompresses a chunk without keeping it, for a chunk that is read once into another structure.</summary>
    public byte[] ReadChunk(int index) => Volatile.Read(ref _chunks[index]) ?? Decompress(ReadCompressedChunk(index));

    /// <summary>Gets the compressed size of a chunk, in bytes.</summary>
    public int GetCompressedChunkLength(int index) => (int)(_offsets[index + 1] - _offsets[index]);

    /// <summary>Gets whether a chunk was decompressed.</summary>
    public bool IsChunkLoaded(int index) => Volatile.Read(ref _chunks[index]) is not null;

    private byte[] ReadCompressedChunk(int index)
    {
        var start = _offsets[index];
        var length = GetCompressedChunkLength(index);
        if (_content is not null)
            return _content.AsSpan((int)start, length).ToArray();

        using var stream = OpenResource(_resourceName!);
        stream.Seek(start, SeekOrigin.Begin);
        var result = new byte[length];
        stream.ReadExactly(result);
        return result;
    }

    private static int GetOffsetsLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize || !header[..4].SequenceEqual("MFCD"u8))
            throw new InvalidDataException("The CLDR data file is invalid");

        if (header[4] != FormatVersion)
            throw new InvalidDataException("The format of the CLDR data file is not supported: " + header[4]);

        return (BinaryPrimitives.ReadInt32LittleEndian(header[8..]) + 1) * 4;
    }

    private static byte[] Decompress(byte[] compressed)
    {
        var length = BinaryPrimitives.ReadInt32LittleEndian(compressed);
        var result = new byte[length];
        if (!BrotliDecoder.TryDecompress(compressed.AsSpan(4), result, out var written) || written != length)
            throw new InvalidDataException("A chunk of the CLDR data file is invalid");

        return result;
    }

    private static Stream OpenResource(string resourceName)
    {
        return typeof(CldrChunkedFile).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The CLDR data is not embedded in the assembly: " + resourceName);
    }
}
