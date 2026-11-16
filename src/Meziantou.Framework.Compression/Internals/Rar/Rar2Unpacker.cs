namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// The decoder of the RAR 2.0 compression (methods 20 and 26): LZ coded with Huffman tables, and the multimedia (audio)
/// blocks, which predict each byte from the previous bytes of its channel.
/// </summary>
internal sealed class Rar2Unpacker : RarUnpacker
{
    private const int MainCodeSize = 298;
    private const int DistanceCodeSize = 48;
    private const int RepeatCodeSize = 28;
    private const int PrecodeSize = 19;
    private const int AudioCodeSize = 257;
    private const int MaxTableSize = AudioCodeSize * 4;

    private static readonly byte[] LengthBases = [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224];
    private static readonly byte[] LengthBits = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5];
    private static readonly uint[] DistanceBases =
    [
        0, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 1536, 2048, 3072, 4096, 6144, 8192, 12288,
        16384, 24576, 32768, 49152, 65536, 98304, 131072, 196608, 262144, 327680, 393216, 458752, 524288, 589824, 655360, 720896, 786432,
        851968, 917504, 983040,
    ];

    private static readonly byte[] DistanceBits = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16];
    private static readonly byte[] ShortBases = [0, 4, 8, 16, 32, 64, 128, 192];
    private static readonly byte[] ShortBits = [2, 2, 3, 4, 5, 6, 6, 6];

    private readonly RarHuffmanTable _precode = new(PrecodeSize, 7);
    private readonly RarHuffmanTable _main = new(MainCodeSize, 10);
    private readonly RarHuffmanTable _distance = new(DistanceCodeSize, 7);
    private readonly RarHuffmanTable _repeat = new(RepeatCodeSize, 7);
    private readonly RarHuffmanTable[] _audio = [new(AudioCodeSize, 10), new(AudioCodeSize, 10), new(AudioCodeSize, 10), new(AudioCodeSize, 10)];
    private readonly byte[] _lengths = new byte[MaxTableSize];
    private readonly uint[] _oldDistances = new uint[4];
    private readonly AudioState[] _audioStates = [new(), new(), new(), new()];
    private int _oldDistancePointer;
    private uint _lastDistance;
    private int _lastLength;
    private bool _tablesRead;
    private bool _needTables;
    private bool _audioBlock;
    private int _channels = 1;
    private int _currentChannel;
    private int _channelDelta;

    public Rar2Unpacker(long maxMemoryUsage)
        : base(maxMemoryUsage)
    {
    }

    private protected override void ResetState()
    {
        Array.Clear(_oldDistances);
        Array.Clear(_lengths);
        _oldDistancePointer = 0;
        _lastDistance = 0;
        _lastLength = 0;
        _tablesRead = false;
        _audioBlock = false;
        _channels = 1;
        _currentChannel = 0;
        _channelDelta = 0;
        foreach (var state in _audioStates)
        {
            state.Reset();
        }
    }

    private protected override void StartFileCore(bool solid)
    {
        _needTables = !solid || !_tablesRead;
    }

    private protected override void Decode(long limit)
    {
        var input = Input;
        var fileEnd = FileSize < 0 ? long.MaxValue : FileStart + FileSize;
        if (_needTables)
        {
            _needTables = false;
            if (fileEnd == FileStart || input.IsEndOfStream)
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
                if (position >= fileEnd)
                {
                    DecodedPosition = position;
                    ReadLastTables();
                    FileEnded = true;
                    break;
                }

                if (input.IsEndOfStream)
                {
                    FileEnded = true;
                    break;
                }

                if (_audioBlock)
                {
                    var audioSymbol = _audio[_currentChannel].Decode(input);
                    if (audioSymbol == 256)
                    {
                        ReadTables();
                        continue;
                    }

                    window[(int)position & mask] = DecodeAudio(audioSymbol);
                    position++;
                    if (++_currentChannel == _channels)
                    {
                        _currentChannel = 0;
                    }

                    continue;
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
                if (symbol > 269)
                {
                    symbol -= 270;
                    length = LengthBases[symbol] + 3 + (int)input.ReadBits(LengthBits[symbol]);
                    var distanceSymbol = _distance.Decode(input);
                    distance = DistanceBases[distanceSymbol] + 1 + input.ReadBits(DistanceBits[distanceSymbol]);
                    if (distance >= 0x2000)
                    {
                        length++;
                        if (distance >= 0x40000)
                        {
                            length++;
                        }
                    }
                }
                else if (symbol == 269)
                {
                    ReadTables();
                    continue;
                }
                else if (symbol == 256)
                {
                    length = _lastLength;
                    distance = _lastDistance;
                }
                else if (symbol < 261)
                {
                    distance = _oldDistances[(_oldDistancePointer - (symbol - 256)) & 3];
                    var lengthSymbol = _repeat.Decode(input);
                    length = LengthBases[lengthSymbol] + 2 + (int)input.ReadBits(LengthBits[lengthSymbol]);
                    if (distance >= 0x101)
                    {
                        length++;
                        if (distance >= 0x2000)
                        {
                            length++;
                            if (distance >= 0x40000)
                            {
                                length++;
                            }
                        }
                    }
                }
                else
                {
                    symbol -= 261;
                    distance = ShortBases[symbol] + 1u + input.ReadBits(ShortBits[symbol]);
                    length = 2;
                }

                _oldDistances[_oldDistancePointer++ & 3] = distance;
                _lastDistance = distance;
                _lastLength = length;
                if (length is 0)
                    continue;

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

    private void ReadLastTables()
    {
        // The table of the next file of a solid stream may follow the last symbol of this file
        var input = Input;
        if (!input.HasBytes(5))
            return;

        if (_audioBlock)
        {
            if (_audio[_currentChannel].Decode(input) == 256)
            {
                ReadTables();
            }
        }
        else if (_main.Decode(input) == 269)
        {
            ReadTables();
        }
    }

    private void ReadTables()
    {
        var input = Input;
        var flags = input.ReadBits(2);
        _audioBlock = (flags & 2) != 0;
        if ((flags & 1) == 0)
        {
            Array.Clear(_lengths);
        }

        int tableSize;
        if (_audioBlock)
        {
            _channels = (int)input.ReadBits(2) + 1;
            if (_currentChannel >= _channels)
            {
                _currentChannel = 0;
            }

            tableSize = AudioCodeSize * _channels;
        }
        else
        {
            tableSize = MainCodeSize + DistanceCodeSize + RepeatCodeSize;
        }

        Span<byte> precodeLengths = stackalloc byte[PrecodeSize];
        for (var i = 0; i < PrecodeSize; i++)
        {
            precodeLengths[i] = (byte)input.ReadBits(4);
        }

        _precode.Build(precodeLengths);
        Span<byte> table = stackalloc byte[MaxTableSize];
        for (var i = 0; i < tableSize;)
        {
            var number = _precode.Decode(input);
            if (number < 16)
            {
                table[i] = (byte)((number + _lengths[i]) & 0xF);
                i++;
            }
            else if (number == 16)
            {
                var count = (int)input.ReadBits(2) + 3;
                if (i is 0)
                    throw new InvalidDataException("The Huffman tables of the compressed data are invalid.");

                while (count-- > 0 && i < tableSize)
                {
                    table[i] = table[i - 1];
                    i++;
                }
            }
            else
            {
                var count = number == 17 ? (int)input.ReadBits(3) + 3 : (int)input.ReadBits(7) + 11;
                while (count-- > 0 && i < tableSize)
                {
                    table[i++] = 0;
                }
            }
        }

        _tablesRead = true;
        if (_audioBlock)
        {
            for (var i = 0; i < _channels; i++)
            {
                _audio[i].Build(table.Slice(i * AudioCodeSize, AudioCodeSize));
            }
        }
        else
        {
            _main.Build(table[..MainCodeSize]);
            _distance.Build(table.Slice(MainCodeSize, DistanceCodeSize));
            _repeat.Build(table.Slice(MainCodeSize + DistanceCodeSize, RepeatCodeSize));
        }

        table[..tableSize].CopyTo(_lengths);
    }

    private byte DecodeAudio(int delta)
    {
        var state = _audioStates[_currentChannel];
        state.ByteCount++;
        state.D4 = state.D3;
        state.D3 = state.D2;
        state.D2 = state.LastDelta - state.D1;
        state.D1 = state.LastDelta;
        var predicted = (8 * state.LastChar) + (state.K1 * state.D1) + (state.K2 * state.D2) + (state.K3 * state.D3) + (state.K4 * state.D4) + (state.K5 * _channelDelta);
        predicted = (predicted >> 3) & 0xFF;
        var value = (uint)(predicted - delta);

        var d = (sbyte)(byte)delta * 8;
        var differences = state.Differences;
        differences[0] += (uint)Math.Abs(d);
        differences[1] += (uint)Math.Abs(d - state.D1);
        differences[2] += (uint)Math.Abs(d + state.D1);
        differences[3] += (uint)Math.Abs(d - state.D2);
        differences[4] += (uint)Math.Abs(d + state.D2);
        differences[5] += (uint)Math.Abs(d - state.D3);
        differences[6] += (uint)Math.Abs(d + state.D3);
        differences[7] += (uint)Math.Abs(d - state.D4);
        differences[8] += (uint)Math.Abs(d + state.D4);
        differences[9] += (uint)Math.Abs(d - _channelDelta);
        differences[10] += (uint)Math.Abs(d + _channelDelta);

        _channelDelta = state.LastDelta = (sbyte)(byte)(value - (uint)state.LastChar);
        state.LastChar = (int)value;
        if ((state.ByteCount & 0x1F) == 0)
        {
            var minimum = differences[0];
            var index = 0;
            differences[0] = 0;
            for (var i = 1; i < differences.Length; i++)
            {
                if (differences[i] < minimum)
                {
                    minimum = differences[i];
                    index = i;
                }

                differences[i] = 0;
            }

            switch (index)
            {
                case 1 when state.K1 >= -16: state.K1--; break;
                case 2 when state.K1 < 16: state.K1++; break;
                case 3 when state.K2 >= -16: state.K2--; break;
                case 4 when state.K2 < 16: state.K2++; break;
                case 5 when state.K3 >= -16: state.K3--; break;
                case 6 when state.K3 < 16: state.K3++; break;
                case 7 when state.K4 >= -16: state.K4--; break;
                case 8 when state.K4 < 16: state.K4++; break;
                case 9 when state.K5 >= -16: state.K5--; break;
                case 10 when state.K5 < 16: state.K5++; break;
            }
        }

        return (byte)value;
    }

    private sealed class AudioState
    {
        public uint[] Differences { get; } = new uint[11];

        public int K1 { get; set; }

        public int K2 { get; set; }

        public int K3 { get; set; }

        public int K4 { get; set; }

        public int K5 { get; set; }

        public int D1 { get; set; }

        public int D2 { get; set; }

        public int D3 { get; set; }

        public int D4 { get; set; }

        public int LastDelta { get; set; }

        public int LastChar { get; set; }

        public int ByteCount { get; set; }

        public void Reset()
        {
            Array.Clear(Differences);
            K1 = K2 = K3 = K4 = K5 = 0;
            D1 = D2 = D3 = D4 = 0;
            LastDelta = LastChar = ByteCount = 0;
        }
    }
}
