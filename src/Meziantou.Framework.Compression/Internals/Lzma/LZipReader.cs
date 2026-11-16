// The lzip format (https://www.nongnu.org/lzip/manual/lzip_manual.html#File-format).
using System.Buffers.Binary;
using System.IO.Hashing;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// Decodes lzip data: one or more members, each made of a header, LZMA data that ends with an end marker, and a trailer
/// with the CRC32 and the size of the data and the size of the member. Data after the last member that does not start
/// with the magic bytes of a member is ignored, like lzip does.
/// </summary>
internal sealed class LZipReader : ReadOnlyStreamBase
{
    public const int HeaderSize = 6;
    public const int TrailerSize = 20;

    private readonly LzmaInputBuffer _input;
    private readonly Crc32 _crc = new();
    private LzmaDecoderStream? _member;
    private long _memberStart;
    private long _dataSize;
    private bool _firstMember = true;
    private bool _end;
    private bool _disposed;
    private readonly long _maxMemoryUsage;

    public LZipReader(Stream input, bool leaveOpen, long maxMemoryUsage = long.MaxValue)
    {
        _input = new LzmaInputBuffer(input, leaveOpen);
        _maxMemoryUsage = maxMemoryUsage;
    }

    /// <summary>Gets the magic bytes of a member.</summary>
    public static ReadOnlySpan<byte> Magic => "LZIP"u8;

    /// <summary>Decodes the dictionary size of the header, or returns 0 when it is invalid.</summary>
    public static uint DecodeDictionarySize(byte value)
    {
        var log = value & 0x1F;
        if (log is < 12 or > 29)
            return 0;

        var size = 1u << log;
        size -= (size / 16) * (uint)(value >> 5);
        return size is < LzmaConstants.MinDictionarySize or > (1u << 29) ? 0 : size;
    }

    /// <summary>Encodes the smallest dictionary size of a header that is at least <paramref name="dictionarySize"/>.</summary>
    public static byte EncodeDictionarySize(int dictionarySize)
    {
        var best = 0u;
        byte bestValue = 0;
        for (var log = 12; log <= 29; log++)
        {
            for (var fraction = 0; fraction < 8; fraction++)
            {
                var value = (byte)(log | (fraction << 5));
                var size = DecodeDictionarySize(value);
                if (size >= (uint)dictionarySize && (best == 0 || size < best))
                {
                    best = size;
                    bestValue = value;
                }
            }
        }

        return bestValue;
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
            return 0;

        while (true)
        {
            if (_member is not null)
            {
                var read = _member.Read(buffer);
                if (read > 0)
                {
                    _crc.Append(buffer[..read]);
                    _dataSize += read;
                    return read;
                }

                EndMember();
                continue;
            }

            if (_end)
                return 0;

            if (!_firstMember)
            {
                // A member follows, or the data ends (trailing data that is not a member is ignored)
                if (!_input.Fill(Magic.Length) || !_input.Buffer.AsSpan(_input.Position, Magic.Length).SequenceEqual(Magic))
                {
                    _end = true;
                    return 0;
                }
            }

            StartMember();
        }
    }

    private void StartMember()
    {
        _memberStart = _input.TotalConsumed;
        Span<byte> header = stackalloc byte[HeaderSize];
        _input.ReadExactly(header);
        if (!header[..4].SequenceEqual(Magic))
            throw new InvalidDataException("The data is not in the lzip format.");

        if (header[4] != 1)
            throw new InvalidDataException("The lzip version is not supported.");

        var dictionarySize = DecodeDictionarySize(header[5]);
        if (dictionarySize == 0)
            throw new InvalidDataException("The lzip data is corrupt: the dictionary size is invalid.");

        _firstMember = false;
        _crc.Reset();
        _dataSize = 0;
        _member = new LzmaDecoderStream(_input, lc: 3, lp: 0, pb: 2, dictionarySize, outputLength: -1, _maxMemoryUsage);
    }

    private void EndMember()
    {
        _member!.Dispose();
        _member = null;
        Span<byte> trailer = stackalloc byte[TrailerSize];
        _input.ReadExactly(trailer);
        if (BinaryPrimitives.ReadUInt32LittleEndian(trailer) != _crc.GetCurrentHashAsUInt32())
            throw new InvalidDataException("The lzip data is corrupt: the CRC32 does not match the data.");

        if (BinaryPrimitives.ReadUInt64LittleEndian(trailer[4..]) != (ulong)_dataSize)
            throw new InvalidDataException("The lzip data is corrupt: the data size does not match the data.");

        if (BinaryPrimitives.ReadUInt64LittleEndian(trailer[12..]) != (ulong)(_input.TotalConsumed - _memberStart))
            throw new InvalidDataException("The lzip data is corrupt: the member size does not match the member.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _member?.Dispose();
            _input.Dispose();
        }

        base.Dispose(disposing);
    }
}
