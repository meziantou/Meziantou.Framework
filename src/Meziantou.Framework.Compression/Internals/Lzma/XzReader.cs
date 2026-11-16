// The XZ container, after the XZ file format specification and xz-utils (liblzma), which is in the public domain.
using System.Buffers.Binary;
using System.IO.Hashing;
using Meziantou.Framework.Compression.Internals.Filters;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// Decodes XZ data: concatenated streams with stream padding, blocks with a chain of filters (LZMA2, delta and the BCJ
/// filters), the checks of the blocks, and the index, which must describe the blocks.
/// </summary>
internal sealed class XzReader : ReadOnlyStreamBase
{
    private const int MaxFilters = 4;

    private readonly LzmaInputBuffer _input;
    private readonly List<(ulong UnpaddedSize, ulong UncompressedSize)> _records = [];
    private readonly byte[] _streamFlags = new byte[2];
    private readonly byte[] _scratch = new byte[1024];
    private State _state = State.FirstStreamHeader;
    private XzFormat.Check? _check;
    private int _checkType;
    private Stream? _blockStream;
    private Lzma2DecoderStream? _lzma2;
    private long _blockStart;
    private int _blockHeaderSize;
    private long _declaredCompressedSize;
    private long _declaredUncompressedSize;
    private long _uncompressedSize;
    private bool _disposed;
    private readonly long _maxMemoryUsage;

    public XzReader(Stream input, bool leaveOpen, long maxMemoryUsage = long.MaxValue)
    {
        _input = new LzmaInputBuffer(input, leaveOpen);
        _maxMemoryUsage = maxMemoryUsage;
    }

    private enum State
    {
        FirstStreamHeader,
        StreamPadding,
        BlockHeaderOrIndex,
        Block,
        End,
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
            return 0;

        while (true)
        {
            switch (_state)
            {
                case State.FirstStreamHeader:
                    ReadStreamHeader(_input.ReadByteOrThrow());
                    _state = State.BlockHeaderOrIndex;
                    break;

                case State.StreamPadding:
                    if (!ReadStreamPadding())
                    {
                        _state = State.End;
                        return 0;
                    }

                    _state = State.BlockHeaderOrIndex;
                    break;

                case State.BlockHeaderOrIndex:
                    var first = _input.ReadByteOrThrow();
                    if (first == 0)
                    {
                        ReadIndexAndFooter();
                        _state = State.StreamPadding;
                    }
                    else
                    {
                        ReadBlockHeader(first);
                        _state = State.Block;
                    }

                    break;

                case State.Block:
                    var read = _blockStream!.Read(buffer);
                    if (read > 0)
                    {
                        _uncompressedSize += read;
                        if (_declaredUncompressedSize >= 0 && _uncompressedSize > _declaredUncompressedSize)
                            throw new InvalidDataException("The XZ data is corrupt: a block is longer than its declared uncompressed size.");

                        _check!.Append(buffer[..read]);
                        return read;
                    }

                    EndBlock();
                    _state = State.BlockHeaderOrIndex;
                    break;

                default:
                    return 0;
            }
        }
    }

    private void ReadStreamHeader(byte first)
    {
        Span<byte> header = stackalloc byte[XzFormat.StreamHeaderSize];
        header[0] = first;
        _input.ReadExactly(header[1..]);
        if (!header[..6].SequenceEqual(XzFormat.HeaderMagic))
            throw new InvalidDataException("The data is not in the XZ format.");

        if (Crc32.HashToUInt32(header.Slice(6, 2)) != BinaryPrimitives.ReadUInt32LittleEndian(header[8..]))
            throw new InvalidDataException("The XZ data is corrupt: the CRC32 of the stream header does not match.");

        if (header[6] != 0 || (header[7] & 0xF0) != 0)
            throw new NotSupportedException("The XZ stream uses unsupported options.");

        header.Slice(6, 2).CopyTo(_streamFlags);
        _checkType = header[7];
        _check?.Dispose();
        _check = new XzFormat.Check(_checkType);
        _records.Clear();
    }

    /// <summary>Skips the stream padding, then reads the header of the next stream.</summary>
    /// <returns><see langword="false"/> at the end of the data.</returns>
    private bool ReadStreamPadding()
    {
        long padding = 0;
        while (true)
        {
            var value = _input.ReadByte();
            if (value < 0)
            {
                if (padding % 4 != 0)
                    throw new InvalidDataException("The XZ data is corrupt: the stream padding is not a multiple of 4 bytes.");

                return false;
            }

            if (value != 0)
            {
                if (padding % 4 != 0)
                    throw new InvalidDataException("The XZ data is corrupt: the stream padding is not a multiple of 4 bytes.");

                ReadStreamHeader((byte)value);
                return true;
            }

            padding++;
        }
    }

    private void ReadBlockHeader(byte sizeByte)
    {
        var headerSize = (sizeByte + 1) * 4;
        var header = _scratch.AsSpan(0, headerSize);
        header[0] = sizeByte;
        _input.ReadExactly(header[1..]);
        if (Crc32.HashToUInt32(header[..^4]) != BinaryPrimitives.ReadUInt32LittleEndian(header[^4..]))
            throw new InvalidDataException("The XZ data is corrupt: the CRC32 of a block header does not match.");

        var flags = header[1];
        if ((flags & 0x3C) != 0)
            throw new NotSupportedException("The XZ block header uses unsupported options.");

        var content = header[..^4];
        var position = 2;
        _declaredCompressedSize = -1;
        _declaredUncompressedSize = -1;
        if ((flags & 0x40) != 0)
        {
            var value = XzFormat.ReadVarInt(content, ref position);
            if (value == 0 || value > long.MaxValue)
                throw new InvalidDataException("The XZ data is corrupt: the compressed size of a block is invalid.");

            _declaredCompressedSize = (long)value;
        }

        if ((flags & 0x80) != 0)
        {
            var value = XzFormat.ReadVarInt(content, ref position);
            if (value > long.MaxValue)
                throw new InvalidDataException("The XZ data is corrupt: the uncompressed size of a block is invalid.");

            _declaredUncompressedSize = (long)value;
        }

        var filterCount = (flags & 3) + 1;
        Span<ulong> ids = stackalloc ulong[MaxFilters];
        Span<int> propertiesStarts = stackalloc int[MaxFilters];
        Span<int> propertiesLengths = stackalloc int[MaxFilters];
        for (var i = 0; i < filterCount; i++)
        {
            ids[i] = XzFormat.ReadVarInt(content, ref position);
            var size = XzFormat.ReadVarInt(content, ref position);
            if (size > (ulong)(content.Length - position))
                throw new InvalidDataException("The XZ data is corrupt: the properties of a filter are truncated.");

            propertiesStarts[i] = position;
            propertiesLengths[i] = (int)size;
            position += (int)size;
        }

        foreach (var b in content[position..])
        {
            if (b != 0)
                throw new InvalidDataException("The XZ data is corrupt: the padding of a block header is not zero.");
        }

        // The last filter is LZMA2, the others are the delta and BCJ filters
        if (ids[filterCount - 1] != XzFormat.FilterLzma2)
        {
            if (ids[filterCount - 1] is XzFormat.FilterDelta or (>= XzFormat.FilterX86 and <= XzFormat.FilterRiscV))
                throw new InvalidDataException("The XZ data is corrupt: the last filter of a block is not LZMA2.");

            throw new NotSupportedException($"The XZ filter 0x{ids[filterCount - 1]:X} is not supported.");
        }

        var lzma2Properties = content.Slice(propertiesStarts[filterCount - 1], propertiesLengths[filterCount - 1]);
        if (lzma2Properties.Length != 1 || lzma2Properties[0] > 40)
            throw new InvalidDataException("The XZ data is corrupt: the properties of the LZMA2 filter are invalid.");

        _blockStart = _input.TotalConsumed;
        _blockHeaderSize = headerSize;
        _lzma2 = new Lzma2DecoderStream(_input, lzma2Properties[0], ownsInput: false, _maxMemoryUsage);
        Stream stream = _lzma2;
        for (var i = filterCount - 2; i >= 0; i--)
        {
            var properties = content.Slice(propertiesStarts[i], propertiesLengths[i]);
            stream = ids[i] switch
            {
                XzFormat.FilterDelta => properties.Length == 1 ? new DeltaDecoderStream(stream, properties[0] + 1, leaveOpen: false) : throw new InvalidDataException("The XZ data is corrupt: the properties of the delta filter are invalid."),
                XzFormat.FilterX86 => CreateBranchFilter(stream, BranchFilterKind.X86, properties),
                XzFormat.FilterPowerPC => CreateBranchFilter(stream, BranchFilterKind.PowerPC, properties),
                XzFormat.FilterIA64 => CreateBranchFilter(stream, BranchFilterKind.IA64, properties),
                XzFormat.FilterArm => CreateBranchFilter(stream, BranchFilterKind.Arm, properties),
                XzFormat.FilterArmThumb => CreateBranchFilter(stream, BranchFilterKind.ArmThumb, properties),
                XzFormat.FilterSparc => CreateBranchFilter(stream, BranchFilterKind.Sparc, properties),
                XzFormat.FilterArm64 => CreateBranchFilter(stream, BranchFilterKind.Arm64, properties),
                XzFormat.FilterRiscV => CreateBranchFilter(stream, BranchFilterKind.RiscV, properties),
                XzFormat.FilterLzma2 => throw new InvalidDataException("The XZ data is corrupt: LZMA2 is not the last filter of a block."),
                _ => throw new NotSupportedException($"The XZ filter 0x{ids[i]:X} is not supported."),
            };
        }

        _blockStream = stream;
        _uncompressedSize = 0;
    }

    private static BranchFilterDecoderStream CreateBranchFilter(Stream stream, BranchFilterKind kind, ReadOnlySpan<byte> properties)
    {
        var startOffset = properties.Length switch
        {
            0 => 0u,
            4 => BinaryPrimitives.ReadUInt32LittleEndian(properties),
            _ => throw new InvalidDataException("The XZ data is corrupt: the properties of a BCJ filter are invalid."),
        };

        return new BranchFilterDecoderStream(stream, kind, startOffset, leaveOpen: false);
    }

    private void EndBlock()
    {
        if (!_lzma2!.IsFinished)
            throw new InvalidDataException("The XZ data is corrupt: a block ends before the end of its LZMA2 data.");

        _blockStream!.Dispose();
        _blockStream = null;
        _lzma2 = null;

        var compressedSize = _input.TotalConsumed - _blockStart;
        if (_declaredCompressedSize >= 0 && compressedSize != _declaredCompressedSize)
            throw new InvalidDataException("The XZ data is corrupt: the compressed size of a block does not match its header.");

        if (_declaredUncompressedSize >= 0 && _uncompressedSize != _declaredUncompressedSize)
            throw new InvalidDataException("The XZ data is corrupt: the uncompressed size of a block does not match its header.");

        var padding = (int)((4 - (compressedSize & 3)) & 3);
        for (var i = 0; i < padding; i++)
        {
            if (_input.ReadByteOrThrow() != 0)
                throw new InvalidDataException("The XZ data is corrupt: the padding of a block is not zero.");
        }

        var checkSize = _check!.Size;
        var expected = _scratch.AsSpan(0, checkSize);
        _input.ReadExactly(expected);
        if (_check.IsSupported)
        {
            Span<byte> actual = stackalloc byte[64];
            _check.GetValueAndReset(actual);
            if (!expected.SequenceEqual(actual[..checkSize]))
                throw new InvalidDataException("The XZ data is corrupt: the check of a block does not match its data.");
        }

        var unpaddedSize = (ulong)(_blockHeaderSize + compressedSize + checkSize);
        _records.Add((unpaddedSize, (ulong)_uncompressedSize));
    }

    private void ReadIndexAndFooter()
    {
        var crc = new Crc32();
        crc.Append([0]);
        var indexStart = _input.TotalConsumed - 1;
        var count = XzFormat.ReadVarInt(_input, crc);
        if (count != (ulong)_records.Count)
            throw new InvalidDataException("The XZ data is corrupt: the index does not have a record per block.");

        foreach (var record in _records)
        {
            var unpaddedSize = XzFormat.ReadVarInt(_input, crc);
            var uncompressedSize = XzFormat.ReadVarInt(_input, crc);
            if (unpaddedSize != record.UnpaddedSize || uncompressedSize != record.UncompressedSize)
                throw new InvalidDataException("The XZ data is corrupt: the index does not match the blocks.");
        }

        var padding = (int)((4 - ((_input.TotalConsumed - indexStart) & 3)) & 3);
        for (var i = 0; i < padding; i++)
        {
            var b = _input.ReadByteOrThrow();
            if (b != 0)
                throw new InvalidDataException("The XZ data is corrupt: the padding of the index is not zero.");

            crc.Append([b]);
        }

        Span<byte> crcBytes = stackalloc byte[4];
        _input.ReadExactly(crcBytes);
        if (crc.GetCurrentHashAsUInt32() != BinaryPrimitives.ReadUInt32LittleEndian(crcBytes))
            throw new InvalidDataException("The XZ data is corrupt: the CRC32 of the index does not match.");

        var indexSize = _input.TotalConsumed - indexStart;
        Span<byte> footer = stackalloc byte[XzFormat.StreamFooterSize];
        _input.ReadExactly(footer);
        if (!footer[10..].SequenceEqual(XzFormat.FooterMagic))
            throw new InvalidDataException("The XZ data is corrupt: the stream footer is invalid.");

        if (Crc32.HashToUInt32(footer.Slice(4, 6)) != BinaryPrimitives.ReadUInt32LittleEndian(footer))
            throw new InvalidDataException("The XZ data is corrupt: the CRC32 of the stream footer does not match.");

        if (!footer.Slice(8, 2).SequenceEqual(_streamFlags))
            throw new InvalidDataException("The XZ data is corrupt: the flags of the stream footer do not match the stream header.");

        var backwardSize = ((long)BinaryPrimitives.ReadUInt32LittleEndian(footer[4..]) + 1) * 4;
        if (backwardSize != indexSize)
            throw new InvalidDataException("The XZ data is corrupt: the size of the index does not match the stream footer.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _blockStream?.Dispose();
            _lzma2?.Dispose();
            _check?.Dispose();
            _input.Dispose();
        }

        base.Dispose(disposing);
    }
}
