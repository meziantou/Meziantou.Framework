using System.IO.Hashing;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// The decoder of the RAR 2.9 compression (methods 29 and 36 of RAR 2.9 to 4.x): LZ blocks coded with Huffman tables,
/// PPMd blocks, and the standard filters of the RarVM, identified by the CRC-32 and the length of their bytecode (generic
/// bytecode is not executed). It follows the description of the format in libarchive's
/// archive_read_support_format_rar.c (BSD-2-Clause).
/// </summary>
internal sealed class Rar3Unpacker : RarUnpacker
{
    private const int MainCodeSize = 299;
    private const int DistanceCodeSize = 60;
    private const int LowDistanceCodeSize = 17;
    private const int RepeatCodeSize = 28;
    private const int PrecodeSize = 20;
    private const int TableSize = MainCodeSize + DistanceCodeSize + LowDistanceCodeSize + RepeatCodeSize;
    private const int MaxFilterBlockSize = 0x3C000;
    private const int MaxPrograms = 1024;
    private const int MaxSymbolsWithoutOutput = 100_000;

    private static readonly byte[] LengthBases = [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224];
    private static readonly byte[] LengthBits = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5];
    private static readonly byte[] ShortBases = [0, 4, 8, 16, 32, 64, 128, 192];
    private static readonly byte[] ShortBits = [2, 2, 3, 4, 5, 6, 6, 6];
    private static readonly uint[] DistanceBases = CreateDistanceBases(out DistanceBits);
    private static readonly byte[] DistanceBits;

    private readonly RarHuffmanTable _precode = new(PrecodeSize, 7);
    private readonly RarHuffmanTable _main = new(MainCodeSize, 10);
    private readonly RarHuffmanTable _distance = new(DistanceCodeSize, 7);
    private readonly RarHuffmanTable _lowDistance = new(LowDistanceCodeSize, 7);
    private readonly RarHuffmanTable _repeat = new(RepeatCodeSize, 7);
    private readonly byte[] _lengths = new byte[TableSize];
    private readonly uint[] _oldDistances = new uint[4];
    private readonly List<RarProgram> _programs = [];
    private uint _lastDistance;
    private uint _lastLength;
    private uint _previousLowDistance;
    private int _lowDistanceRepeats;
    private bool _tablesRead;
    private bool _needTables;
    private int _lastProgram;
    private int _symbolsWithoutOutput;

    // PPMd
    private readonly RarPpmdState _ppmd;
    private bool _ppmBlock;
    private bool _ppmValid;
    private int _ppmEscape = 2;

    public Rar3Unpacker(long maxMemoryUsage)
        : base(maxMemoryUsage)
    {
        _ppmd = new RarPpmdState(Input, maxMemoryUsage);
    }

    private protected override void ResetState()
    {
        Array.Clear(_oldDistances);
        Array.Clear(_lengths);
        _lastDistance = 0;
        _lastLength = 0;
        _previousLowDistance = 0;
        _lowDistanceRepeats = 0;
        _tablesRead = false;
        _ppmBlock = false;
        _ppmValid = false;
        _ppmEscape = 2;
        _programs.Clear();
        _lastProgram = 0;
    }

    private protected override void StartFileCore(bool solid)
    {
        _needTables = !solid || !_tablesRead;
        _symbolsWithoutOutput = 0;
    }

    private protected override void Decode(long limit)
    {
        var input = Input;
        if (_needTables)
        {
            _needTables = false;
            if (input.IsEndOfStream)
            {
                FileEnded = true;
                return;
            }

            ReadTables();
        }

        var position = DecodedPosition;
        var window = Window;
        var mask = WindowMask;
        try
        {
            while (position < limit)
            {
                if (_ppmBlock)
                {
                    // The range decoder reads ahead: the last symbols are decoded after the end of the input
                    DecodedPosition = position;
                    if (!DecodePpm())
                    {
                        position = DecodedPosition;
                        break;
                    }

                    if (DecodedPosition == position)
                    {
                        if (++_symbolsWithoutOutput > MaxSymbolsWithoutOutput)
                            throw new InvalidDataException("The PPMd data of the compressed data do not produce any output.");
                    }
                    else
                    {
                        _symbolsWithoutOutput = 0;
                    }

                    position = DecodedPosition;
                    continue;
                }

                if (input.IsEndOfStream)
                {
                    FileEnded = true;
                    break;
                }

                var symbol = _main.Decode(input);
                if (symbol < 256)
                {
                    window[(int)position & mask] = (byte)symbol;
                    position++;
                    continue;
                }

                int length;
                uint distance;
                if (symbol >= 271)
                {
                    symbol -= 271;
                    length = LengthBases[symbol] + 3 + (int)input.ReadBits(LengthBits[symbol]);
                    var distanceSymbol = _distance.Decode(input);
                    distance = DistanceBases[distanceSymbol] + 1;
                    int bitCount = DistanceBits[distanceSymbol];
                    if (bitCount > 0)
                    {
                        if (distanceSymbol > 9)
                        {
                            if (bitCount > 4)
                            {
                                distance += input.ReadBits(bitCount - 4) << 4;
                            }

                            if (_lowDistanceRepeats > 0)
                            {
                                _lowDistanceRepeats--;
                                distance += _previousLowDistance;
                            }
                            else
                            {
                                var lowDistance = (uint)_lowDistance.Decode(input);
                                if (lowDistance == 16)
                                {
                                    _lowDistanceRepeats = 15;
                                    distance += _previousLowDistance;
                                }
                                else
                                {
                                    distance += lowDistance;
                                    _previousLowDistance = lowDistance;
                                }
                            }
                        }
                        else
                        {
                            distance += input.ReadBits(bitCount);
                        }
                    }

                    if (distance >= 0x2000)
                    {
                        length++;
                        if (distance >= 0x40000)
                        {
                            length++;
                        }
                    }

                    InsertOldDistance(distance);
                }
                else if (symbol == 256)
                {
                    // End of block: "1" a new table follows, "00" end of file, "01" end of file and a new table in the next file
                    if (input.ReadBits(1) is 1)
                    {
                        DecodedPosition = position;
                        ReadTables();
                        continue;
                    }

                    _tablesRead = input.ReadBits(1) is 0;
                    FileEnded = true;
                    break;
                }
                else if (symbol == 257)
                {
                    DecodedPosition = position;
                    ReadFilterFromLz(input, position);
                    continue;
                }
                else if (symbol == 258)
                {
                    if (_lastLength is 0)
                        continue;

                    length = (int)_lastLength;
                    distance = _lastDistance;
                }
                else if (symbol < 263)
                {
                    var index = symbol - 259;
                    distance = _oldDistances[index];
                    for (var i = index; i > 0; i--)
                    {
                        _oldDistances[i] = _oldDistances[i - 1];
                    }

                    _oldDistances[0] = distance;
                    var lengthSymbol = _repeat.Decode(input);
                    length = LengthBases[lengthSymbol] + 2 + (int)input.ReadBits(LengthBits[lengthSymbol]);
                }
                else
                {
                    symbol -= 263;
                    distance = ShortBases[symbol] + 1u + input.ReadBits(ShortBits[symbol]);
                    length = 2;
                    InsertOldDistance(distance);
                }

                _lastDistance = distance;
                _lastLength = (uint)length;
                CheckDistance(distance, position);
                CopyMatch(window, mask, position, length, (int)distance);
                position += length;
            }
        }
        finally
        {
            DecodedPosition = position;
        }
    }

    private void InsertOldDistance(uint distance)
    {
        _oldDistances[3] = _oldDistances[2];
        _oldDistances[2] = _oldDistances[1];
        _oldDistances[1] = _oldDistances[0];
        _oldDistances[0] = distance;
    }

    /// <summary>Decodes a PPMd symbol; returns <see langword="false"/> at the end of the file.</summary>
    private bool DecodePpm()
    {
        var symbol = _ppmd.DecodeSymbol();
        if (symbol != _ppmEscape)
        {
            WriteByte((byte)symbol);
            return true;
        }

        var code = _ppmd.DecodeSymbol();
        switch (code)
        {
            case 0:
                ReadTables();
                return true;

            case 2:
                FileEnded = true;
                return false;

            case 3:
                ReadFilter(_ppmd.DecodeSymbol, DecodedPosition);
                return true;

            case 4:
                {
                    uint distance = 0;
                    for (var i = 0; i < 3; i++)
                    {
                        distance = (distance << 8) | (uint)_ppmd.DecodeSymbol();
                    }

                    var length = _ppmd.DecodeSymbol();
                    WriteMatch(length + 32, distance + 2);
                    return true;
                }

            case 5:
                WriteMatch(_ppmd.DecodeSymbol() + 4, 1);
                return true;

            default:
                WriteByte((byte)symbol);
                return true;
        }
    }

    private void WriteByte(byte value)
    {
        Window[(int)DecodedPosition & WindowMask] = value;
        DecodedPosition++;
    }

    private void WriteMatch(int length, uint distance)
    {
        CheckDistance(distance, DecodedPosition);
        CopyMatch(Window, WindowMask, DecodedPosition, length, (int)distance);
        DecodedPosition += length;
    }

    private void ReadTables()
    {
        var input = Input;
        input.AlignToByte();
        if ((input.Peek(1) & 1) != 0)
        {
            var flags = (int)input.ReadBits(8) & 0x7F;
            var maxMegabytes = 0;
            if ((flags & 0x20) != 0)
            {
                maxMegabytes = (int)input.ReadBits(8);
            }

            if ((flags & 0x40) != 0)
            {
                _ppmEscape = (int)input.ReadBits(8);
            }

            if ((flags & 0x20) != 0)
            {
                var maxOrder = (flags & 0x1F) + 1;
                if (maxOrder > 16)
                {
                    maxOrder = 16 + ((maxOrder - 16) * 3);
                }

                if (maxOrder is 1)
                    throw new InvalidDataException("The PPMd block of the compressed data is invalid.");

                _ppmd.Initialize(maxOrder, (maxMegabytes + 1) << 20, resetModel: true);
                _ppmValid = true;
            }
            else
            {
                if (!_ppmValid)
                    throw new InvalidDataException("The PPMd block of the compressed data continues a model that does not exist.");

                _ppmd.Initialize(0, 0, resetModel: false);
            }

            _ppmBlock = true;
            return;
        }

        _ppmBlock = false;
        input.Skip(1);
        _previousLowDistance = 0;
        _lowDistanceRepeats = 0;
        if (input.ReadBits(1) is 0)
        {
            Array.Clear(_lengths);
        }

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
        for (var i = 0; i < TableSize;)
        {
            var number = _precode.Decode(input);
            if (number < 16)
            {
                _lengths[i] = (byte)((_lengths[i] + number) & 0xF);
                i++;
            }
            else if (number < 18)
            {
                var count = number is 16 ? (int)input.ReadBits(3) + 3 : (int)input.ReadBits(7) + 11;
                if (i is 0)
                    throw new InvalidDataException("The Huffman tables of the compressed data are invalid.");

                while (count-- > 0 && i < TableSize)
                {
                    _lengths[i] = _lengths[i - 1];
                    i++;
                }
            }
            else
            {
                var count = number is 18 ? (int)input.ReadBits(3) + 3 : (int)input.ReadBits(7) + 11;
                while (count-- > 0 && i < TableSize)
                {
                    _lengths[i++] = 0;
                }
            }
        }

        var lengths = _lengths.AsSpan();
        _main.Build(lengths[..MainCodeSize]);
        _distance.Build(lengths.Slice(MainCodeSize, DistanceCodeSize));
        _lowDistance.Build(lengths.Slice(MainCodeSize + DistanceCodeSize, LowDistanceCodeSize));
        _repeat.Build(lengths.Slice(MainCodeSize + DistanceCodeSize + LowDistanceCodeSize, RepeatCodeSize));
        _tablesRead = true;
    }

    private void ReadFilterFromLz(RarBitReader input, long position)
    {
        ReadFilter(() => (int)input.ReadBits(8), position);
    }

    /// <summary>Reads the code of a filter (its flags, its length and its data) from the LZ bits or the PPMd symbols.</summary>
    private void ReadFilter(Func<int> readByte, long position)
    {
        var flags = readByte();
        var length = (flags & 7) + 1;
        if (length is 7)
        {
            length = readByte() + 7;
        }
        else if (length is 8)
        {
            length = readByte() << 8;
            length |= readByte();
        }

        if (length is 0)
            throw new InvalidDataException("A filter of the compressed data is empty.");

        var code = new byte[length];
        for (var i = 0; i < length; i++)
        {
            code[i] = (byte)readByte();
        }

        ParseFilter(code, flags, position);
    }

    private void ParseFilter(byte[] code, int flags, long position)
    {
        var reader = new RarMemoryBitReader(code);
        int programIndex;
        if ((flags & 0x80) != 0)
        {
            var number = reader.ReadNumber();
            if (number is 0)
            {
                _programs.Clear();
            }
            else
            {
                number--;
            }

            if (number > (uint)_programs.Count || number >= MaxPrograms)
                throw new InvalidDataException("A filter of the compressed data refers to an unknown program.");

            programIndex = (int)number;
            _lastProgram = programIndex;
        }
        else
        {
            programIndex = _lastProgram;
            if (programIndex > _programs.Count)
                throw new InvalidDataException("A filter of the compressed data refers to an unknown program.");
        }

        var program = programIndex < _programs.Count ? _programs[programIndex] : null;
        long start = reader.ReadNumber() + position;
        if ((flags & 0x40) != 0)
        {
            start += 258;
        }

        uint blockLength;
        if ((flags & 0x20) != 0)
        {
            blockLength = reader.ReadNumber();
        }
        else
        {
            blockLength = program?.LastLength ?? 0;
        }

        Span<uint> registers = stackalloc uint[7];
        registers.Clear();
        if ((flags & 0x10) != 0)
        {
            var initMask = reader.ReadBits(7);
            for (var i = 0; i < 7; i++)
            {
                if ((initMask & (1u << i)) != 0)
                {
                    registers[i] = reader.ReadNumber();
                }
            }
        }

        if (program is null)
        {
            var codeLength = reader.ReadNumber();
            if (codeLength is 0 || codeLength >= 0x10000)
                throw new InvalidDataException("A filter of the compressed data has an invalid program.");

            var bytecode = new byte[codeLength];
            for (var i = 0; i < bytecode.Length; i++)
            {
                bytecode[i] = (byte)reader.ReadBits(8);
            }

            if (reader.IsOverrun)
                throw new InvalidDataException("A filter of the compressed data is truncated.");

            program = new RarProgram(IdentifyProgram(bytecode));
            _programs.Add(program);
        }

        program.LastLength = blockLength;
        if ((flags & 0x08) != 0)
        {
            var globalLength = reader.ReadNumber();
            if (globalLength > 0x2000)
                throw new InvalidDataException("A filter of the compressed data has invalid global data.");

            for (var i = 0; i < globalLength; i++)
            {
                reader.ReadBits(8);
            }
        }

        if (reader.IsOverrun)
            throw new InvalidDataException("A filter of the compressed data is truncated.");

        if (blockLength > MaxFilterBlockSize)
            throw new InvalidDataException("A filter block of the compressed data is too large.");

        var filter = new RarFilter
        {
            Start = start,
            Length = (int)blockLength,
            Type = program.Type,
            Channels = registers[0],
            RedPosition = registers[1],
        };

        var valid = filter.Type switch
        {
            RarFilterType.E8 or RarFilterType.E8E9 => blockLength >= 4,
            RarFilterType.Itanium => blockLength >= 21,
            RarFilterType.Delta => filter.Channels is >= 1 and <= 1024,
            RarFilterType.Rgb => blockLength >= 3 && filter.Channels >= 3 && filter.Channels - 3 <= blockLength && filter.RedPosition <= 2,
            RarFilterType.Audio => filter.Channels is >= 1 and <= 128,
            _ => false,
        };

        if (!valid)
            throw new InvalidDataException("A filter of the compressed data has invalid parameters.");

        AddFilter(filter);
    }

    private static RarFilterType IdentifyProgram(byte[] bytecode)
    {
        byte xor = 0;
        for (var i = 1; i < bytecode.Length; i++)
        {
            xor ^= bytecode[i];
        }

        if (xor != bytecode[0])
            throw new InvalidDataException("A filter of the compressed data has a corrupted program.");

        var crc = Crc32.HashToUInt32(bytecode);
        return (crc, bytecode.Length) switch
        {
            (0xAD576887, 53) => RarFilterType.E8,
            (0x3CD7E57E, 57) => RarFilterType.E8E9,
            (0x3769893F, 120) => RarFilterType.Itanium,
            (0x0E06077D, 29) => RarFilterType.Delta,
            (0x1C2C5DC8, 149) => RarFilterType.Rgb,
            (0xBC85E701, 216) => RarFilterType.Audio,
            _ => throw new NotSupportedException("The compressed data use a RarVM program that is not a standard filter, which is not supported."),
        };
    }

    private static uint[] CreateDistanceBases(out byte[] bits)
    {
        ReadOnlySpan<int> counts = [4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 14, 0, 12];
        var bases = new uint[DistanceCodeSize];
        bits = new byte[DistanceCodeSize];
        var slot = 0;
        uint distance = 0;
        for (var bitCount = 0; bitCount < counts.Length; bitCount++)
        {
            for (var j = 0; j < counts[bitCount]; j++)
            {
                bases[slot] = distance;
                bits[slot] = (byte)bitCount;
                slot++;
                distance += 1u << bitCount;
            }
        }

        return bases;
    }

    private sealed class RarProgram(RarFilterType type)
    {
        public RarFilterType Type { get; } = type;

        public uint LastLength { get; set; }
    }
}
