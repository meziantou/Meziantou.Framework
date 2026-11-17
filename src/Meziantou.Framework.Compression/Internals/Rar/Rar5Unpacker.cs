namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// The decoder of the RAR5 compression (RAR 5.0 and RAR 7.0 algorithms): blocks of LZ symbols coded with Huffman tables,
/// and the DELTA, E8, E8E9 and ARM filters. It follows the description of the format in the RAR 5.0 technote and in
/// libarchive's archive_read_support_format_rar5.c (BSD-2-Clause).
/// </summary>
internal sealed class Rar5Unpacker : RarUnpacker
{
    private const int MainCodeSize = 306;
    private const int DistanceCodeSize = 64;
    private const int ExtendedDistanceCodeSize = 80;
    private const int LowDistanceCodeSize = 16;
    private const int RepeatCodeSize = 44;
    private const int PrecodeSize = 20;
    private const int MaxFilterBlockSize = 0x400000;

    private readonly RarHuffmanTable _precode = new(PrecodeSize, 7);
    private readonly RarHuffmanTable _main = new(MainCodeSize, 10);
    private readonly RarHuffmanTable _distance = new(ExtendedDistanceCodeSize, 7);
    private readonly RarHuffmanTable _lowDistance = new(LowDistanceCodeSize, 7);
    private readonly RarHuffmanTable _repeat = new(RepeatCodeSize, 7);
    private readonly byte[] _lengths = new byte[MainCodeSize + ExtendedDistanceCodeSize + LowDistanceCodeSize + RepeatCodeSize];
    private readonly ulong[] _oldDistances = new ulong[4];
    private uint _lastLength;
    private bool _tablesRead;
    private bool _extendedDistances;

    // The current block: the bit position of its end, and the bit position from which the end must be checked
    private bool _hasBlock;
    private bool _lastBlock;
    private bool _tablePresent;
    private long _blockEnd;
    private long _blockCheck;

    public Rar5Unpacker(long maxMemoryUsage)
        : base(maxMemoryUsage)
    {
    }

    /// <summary>Gets or sets a value indicating whether the file uses the RAR 7.0 algorithm (the distances of more than 4 GiB).</summary>
    public bool ExtendedDistances
    {
        get => _extendedDistances;
        set => _extendedDistances = value;
    }

    private protected override bool IsRar5 => true;

    private protected override void ResetState()
    {
        Array.Clear(_oldDistances);
        _lastLength = 0;
        _tablesRead = false;
    }

    private protected override void StartFileCore(bool solid)
    {
        _hasBlock = false;
        _lastBlock = false;
        _blockEnd = 0;
        _blockCheck = 0;
    }

    private protected override void Decode(long limit)
    {
        var input = Input;
        var window = Window;
        var mask = WindowMask;
        var position = DecodedPosition;
        try
        {
            while (position < limit)
            {
                if (input.BitPosition >= _blockCheck && !NextBlock())
                {
                    FileEnded = true;
                    break;
                }

                var slot = _main.Decode(input);
                if (slot < 256)
                {
                    window[(int)position & mask] = (byte)slot;
                    position++;
                    continue;
                }

                if (slot >= 262)
                {
                    var length = SlotToLength(input, slot - 262);
                    var distanceSlot = _distance.Decode(input);
                    ulong distance;
                    if (distanceSlot < 4)
                    {
                        distance = (ulong)distanceSlot + 1;
                    }
                    else
                    {
                        var bitCount = (distanceSlot / 2) - 1;
                        distance = ((ulong)(2 | (distanceSlot & 1)) << bitCount) + 1;
                        if (bitCount >= 4)
                        {
                            if (bitCount > 4)
                            {
                                distance += input.ReadBits64(bitCount - 4) << 4;
                            }

                            distance += (ulong)_lowDistance.Decode(input);
                        }
                        else
                        {
                            distance += input.ReadBits(bitCount);
                        }
                    }

                    if (distance > 0x100)
                    {
                        length++;
                        if (distance > 0x2000)
                        {
                            length++;
                            if (distance > 0x40000)
                            {
                                length++;
                            }
                        }
                    }

                    _oldDistances[3] = _oldDistances[2];
                    _oldDistances[2] = _oldDistances[1];
                    _oldDistances[1] = _oldDistances[0];
                    _oldDistances[0] = distance;
                    _lastLength = (uint)length;
                    CheckDistance(distance, position);
                    CopyMatch(window, mask, position, length, (int)distance);
                    position += length;
                    continue;
                }

                if (slot == 256)
                {
                    DecodedPosition = position;
                    ReadFilter(input, position);
                    continue;
                }

                if (slot == 257)
                {
                    if (_lastLength is not 0)
                    {
                        var distance = _oldDistances[0];
                        CheckDistance(distance, position);
                        CopyMatch(window, mask, position, (int)_lastLength, (int)distance);
                        position += _lastLength;
                    }

                    continue;
                }

                {
                    var index = slot - 258;
                    var distance = _oldDistances[index];
                    for (var i = index; i > 0; i--)
                    {
                        _oldDistances[i] = _oldDistances[i - 1];
                    }

                    _oldDistances[0] = distance;
                    var length = SlotToLength(input, _repeat.Decode(input));
                    _lastLength = (uint)length;
                    CheckDistance(distance, position);
                    CopyMatch(window, mask, position, length, (int)distance);
                    position += length;
                }
            }
        }
        finally
        {
            DecodedPosition = position;
        }
    }

    private static int SlotToLength(RarBitReader input, int slot)
    {
        if (slot < 8)
            return 2 + slot;

        var bitCount = (slot / 4) - 1;
        return 2 + ((4 | (slot & 3)) << bitCount) + (int)input.ReadBits(bitCount);
    }

    private void ReadFilter(RarBitReader input, long position)
    {
        var start = ReadFilterData(input);
        var length = ReadFilterData(input);
        var type = input.ReadBits(3);
        uint channels = 0;
        if (type is 0)
        {
            channels = input.ReadBits(5) + 1;
        }

        if (length > MaxFilterBlockSize)
        {
            length = 0;
        }

        var filterType = type switch
        {
            0 => RarFilterType.Delta,
            1 => RarFilterType.E8,
            2 => RarFilterType.E8E9,
            3 => RarFilterType.Arm,
            _ => throw new InvalidDataException("The compressed data use an unknown filter."),
        };

        AddFilter(new RarFilter { Start = position + start, Length = (int)length, Type = filterType, Channels = channels });

        static uint ReadFilterData(RarBitReader input)
        {
            var byteCount = (int)input.ReadBits(2) + 1;
            uint data = 0;
            for (var i = 0; i < byteCount; i++)
            {
                data |= input.ReadBits(8) << (i * 8);
            }

            return data;
        }
    }

    /// <summary>Moves to the next block when the current one ends; returns <see langword="false"/> at the end of the last block.</summary>
    private bool NextBlock()
    {
        var input = Input;
        while (!_hasBlock || input.BitPosition >= _blockEnd)
        {
            if (_hasBlock && _lastBlock)
                return false;

            if (_hasBlock && input.BitPosition > _blockEnd + 64)
                throw new InvalidDataException("A block of the compressed data is longer than its declared size.");

            if (!_hasBlock && input.IsEndOfStream)
            {
                // A file whose compressed data are empty
                return false;
            }

            ReadBlockHeader(input);
            if (_tablePresent)
            {
                ReadTables(input);
            }
            else if (!_tablesRead)
            {
                throw new InvalidDataException("A block of the compressed data has no Huffman tables.");
            }
        }

        _blockCheck = _blockEnd;
        return true;
    }

    private void ReadBlockHeader(RarBitReader input)
    {
        input.AlignToByte();
        var flags = input.ReadBits(8);
        var byteCount = (int)((flags >> 3) & 3) + 1;
        if (byteCount is 4)
            throw new InvalidDataException("The header of a block of the compressed data is invalid.");

        var checksum = input.ReadBits(8);
        uint size = 0;
        for (var i = 0; i < byteCount; i++)
        {
            size |= input.ReadBits(8) << (i * 8);
        }

        if (checksum != (byte)(0x5A ^ flags ^ size ^ (size >> 8) ^ (size >> 16)))
            throw new InvalidDataException("The header of a block of the compressed data is corrupted.");

        if (size is 0)
            throw new InvalidDataException("A block of the compressed data is empty.");

        var bitSize = (int)(flags & 7) + 1;
        _blockEnd = input.BitPosition + ((size - 1) * 8L) + bitSize;
        _hasBlock = true;
        _lastBlock = (flags & 0x40) != 0;
        _tablePresent = (flags & 0x80) != 0;
    }

    private void ReadTables(RarBitReader input)
    {
        Span<byte> precodeLengths = stackalloc byte[PrecodeSize];
        for (var i = 0; i < PrecodeSize; i++)
        {
            var length = (byte)input.ReadBits(4);
            if (length is 15)
            {
                var zeroCount = (int)input.ReadBits(4);
                if (zeroCount is 0)
                {
                    precodeLengths[i] = 15;
                }
                else
                {
                    zeroCount += 2;
                    while (zeroCount-- > 0 && i < PrecodeSize)
                    {
                        precodeLengths[i++] = 0;
                    }

                    i--;
                }
            }
            else
            {
                precodeLengths[i] = length;
            }
        }

        _precode.Build(precodeLengths);
        var distanceCodeSize = _extendedDistances ? ExtendedDistanceCodeSize : DistanceCodeSize;
        var tableSize = MainCodeSize + distanceCodeSize + LowDistanceCodeSize + RepeatCodeSize;
        var lengths = _lengths.AsSpan(0, tableSize);
        for (var i = 0; i < tableSize;)
        {
            var number = _precode.Decode(input);
            if (number < 16)
            {
                lengths[i++] = (byte)number;
            }
            else if (number < 18)
            {
                var count = number is 16 ? (int)input.ReadBits(3) + 3 : (int)input.ReadBits(7) + 11;
                if (i is 0)
                    throw new InvalidDataException("The Huffman tables of the compressed data are invalid.");

                while (count-- > 0 && i < tableSize)
                {
                    lengths[i] = lengths[i - 1];
                    i++;
                }
            }
            else
            {
                var count = number is 18 ? (int)input.ReadBits(3) + 3 : (int)input.ReadBits(7) + 11;
                while (count-- > 0 && i < tableSize)
                {
                    lengths[i++] = 0;
                }
            }
        }

        _main.Build(lengths[..MainCodeSize]);
        _distance.Build(lengths.Slice(MainCodeSize, distanceCodeSize));
        _lowDistance.Build(lengths.Slice(MainCodeSize + distanceCodeSize, LowDistanceCodeSize));
        _repeat.Build(lengths.Slice(MainCodeSize + distanceCodeSize + LowDistanceCodeSize, RepeatCodeSize));
        _tablesRead = true;
    }
}
