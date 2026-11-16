// The XZ container, after the XZ file format specification and xz-utils (liblzma), which is in the public domain.
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>Encodes an XZ stream of LZMA2 blocks of at most <c>blockSize</c> bytes of data (0 for a single block).</summary>
internal sealed class XzWriter : WriteOnlyStreamBase
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The stream does not own it")]
    private readonly Stream _output;
    private readonly LzmaEncoderSettings _settings;
    private readonly int _checkType;
    private readonly long _blockSize;
    private readonly XzFormat.Check _check;
    private readonly List<(ulong UnpaddedSize, ulong UncompressedSize)> _records = [];
    private Lzma2EncoderStream? _block;
    private int _blockHeaderSize;
    private long _blockUncompressedSize;
    private bool _disposed;

    public XzWriter(Stream output, LzmaEncoderSettings settings, int checkType, long blockSize)
    {
        _output = output;
        _settings = settings;
        _checkType = checkType;
        _blockSize = blockSize;
        _check = new XzFormat.Check(checkType);

        Span<byte> header = stackalloc byte[XzFormat.StreamHeaderSize];
        XzFormat.HeaderMagic.CopyTo(header);
        header[6] = 0;
        header[7] = (byte)checkType;
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], Crc32.HashToUInt32(header.Slice(6, 2)));
        _output.Write(header);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!buffer.IsEmpty)
        {
            if (_block is null)
            {
                StartBlock();
            }

            var count = buffer.Length;
            if (_blockSize > 0)
            {
                count = (int)Math.Min(count, _blockSize - _blockUncompressedSize);
            }

            _block!.Write(buffer[..count]);
            _check.Append(buffer[..count]);
            _blockUncompressedSize += count;
            buffer = buffer[count..];
            if (_blockSize > 0 && _blockUncompressedSize == _blockSize)
            {
                EndBlock();
            }
        }
    }

    /// <summary>Ends the current LZMA2 chunk, so that the data written so far can be decoded.</summary>
    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _block?.FlushChunk();
    }

    private void StartBlock()
    {
        _block = new Lzma2EncoderStream(_output, _settings, leaveOpen: true);

        // Block header: size, flags (one filter, no sizes), LZMA2 with its dictionary property, padding, CRC32
        Span<byte> header = stackalloc byte[12];
        header[0] = (byte)((header.Length / 4) - 1);
        header[1] = 0;
        header[2] = (byte)XzFormat.FilterLzma2;
        header[3] = 1;
        header[4] = _block.DictionaryProperty;
        header[5..8].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], Crc32.HashToUInt32(header[..8]));
        _output.Write(header);
        _blockHeaderSize = header.Length;
        _blockUncompressedSize = 0;
    }

    private void EndBlock()
    {
        var block = _block!;
        block.Dispose();
        _block = null;
        var compressedSize = block.CompressedLength;
        Span<byte> buffer = stackalloc byte[64];
        var padding = (int)((4 - (compressedSize & 3)) & 3);
        buffer[..padding].Clear();
        _output.Write(buffer[..padding]);

        var checkSize = _check.Size;
        _check.GetValueAndReset(buffer);
        _output.Write(buffer[..checkSize]);
        _records.Add(((ulong)(_blockHeaderSize + compressedSize + checkSize), (ulong)_blockUncompressedSize));
    }

    private void WriteIndexAndFooter()
    {
        var index = new List<byte>(16 + (_records.Count * 8)) { 0 };
        Span<byte> number = stackalloc byte[10];
        AddVarInt(index, number, (ulong)_records.Count);
        foreach (var (unpaddedSize, uncompressedSize) in _records)
        {
            AddVarInt(index, number, unpaddedSize);
            AddVarInt(index, number, uncompressedSize);
        }

        while (index.Count % 4 != 0)
        {
            index.Add(0);
        }

        var indexBytes = index.ToArray();
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32.HashToUInt32(indexBytes));
        _output.Write(indexBytes);
        _output.Write(crc);

        Span<byte> footer = stackalloc byte[XzFormat.StreamFooterSize];
        BinaryPrimitives.WriteUInt32LittleEndian(footer[4..], (uint)(((indexBytes.Length + 4) / 4) - 1));
        footer[8] = 0;
        footer[9] = (byte)_checkType;
        BinaryPrimitives.WriteUInt32LittleEndian(footer, Crc32.HashToUInt32(footer.Slice(4, 6)));
        XzFormat.FooterMagic.CopyTo(footer[10..]);
        _output.Write(footer);

        static void AddVarInt(List<byte> list, Span<byte> buffer, ulong value)
        {
            var length = XzFormat.WriteVarInt(buffer, value);
            foreach (var b in buffer[..length])
            {
                list.Add(b);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try
            {
                if (_block is not null)
                {
                    EndBlock();
                }

                WriteIndexAndFooter();
                _output.Flush();
            }
            finally
            {
                _block?.Dispose();
                _check.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
