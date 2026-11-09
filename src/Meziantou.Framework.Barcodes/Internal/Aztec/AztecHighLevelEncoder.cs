using System.Globalization;

namespace Meziantou.Framework.Barcodes.Internal.Aztec;

// Converts the data to the bit stream of the character modes (ISO/IEC 24778, 7.3). The encoder follows every useful
// sequence of latches, shifts and binary shifts at once, and keeps the states that no other state beats, so that the bit
// stream is the shortest one.
internal static class AztecHighLevelEncoder
{
    internal const int ModeUpper = 0;
    internal const int ModeLower = 1;
    internal const int ModeDigit = 2;
    internal const int ModeMixed = 3;
    internal const int ModePunct = 4;
    private const int ModeCount = 5;

    // The bits that latch from a mode (first index) to another (second index): the number of bits in the upper 16 bits,
    // their value in the lower 16 bits
    private static readonly int[][] LatchTable =
    [
        [0, (5 << 16) + 28, (5 << 16) + 30, (5 << 16) + 29, (10 << 16) + (29 << 5) + 30],
        [(9 << 16) + (30 << 4) + 14, 0, (5 << 16) + 30, (5 << 16) + 29, (10 << 16) + (29 << 5) + 30],
        [(4 << 16) + 14, (9 << 16) + (14 << 5) + 28, 0, (9 << 16) + (14 << 5) + 29, (14 << 16) + (14 << 10) + (29 << 5) + 30],
        [(5 << 16) + 29, (5 << 16) + 28, (10 << 16) + (29 << 5) + 30, 0, (5 << 16) + 30],
        [(5 << 16) + 31, (10 << 16) + (31 << 5) + 28, (10 << 16) + (31 << 5) + 30, (10 << 16) + (31 << 5) + 29, 0],
    ];

    // The code of a byte in each mode, or 0 when the mode has no such character
    private static readonly byte[][] CharMap = CreateCharMap();

    // The code that shifts from a mode (first index) to another (second index) for one character, or -1
    private static readonly int[][] ShiftTable = CreateShiftTable();

    private static byte[][] CreateCharMap()
    {
        var map = new byte[ModeCount][];
        for (var i = 0; i < ModeCount; i++)
        {
            map[i] = new byte[256];
        }

        map[ModeUpper][' '] = 1;
        map[ModeLower][' '] = 1;
        map[ModeDigit][' '] = 1;
        for (var c = 'A'; c <= 'Z'; c++)
        {
            map[ModeUpper][c] = (byte)(c - 'A' + 2);
        }

        for (var c = 'a'; c <= 'z'; c++)
        {
            map[ModeLower][c] = (byte)(c - 'a' + 2);
        }

        for (var c = '0'; c <= '9'; c++)
        {
            map[ModeDigit][c] = (byte)(c - '0' + 2);
        }

        map[ModeDigit][','] = 12;
        map[ModeDigit]['.'] = 13;

        ReadOnlySpan<byte> mixed = [0x20, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, (byte)'@', (byte)'\\', (byte)'^', (byte)'_', (byte)'`', (byte)'|', (byte)'~', 0x7F];
        for (var i = 0; i < mixed.Length; i++)
        {
            map[ModeMixed][mixed[i]] = (byte)(i + 1);
        }

        // Codes 2 to 5 are the pairs CR LF, ". ", ", " and ": "
        ReadOnlySpan<byte> punct = [(byte)'\r', 0, 0, 0, 0, (byte)'!', (byte)'"', (byte)'#', (byte)'$', (byte)'%', (byte)'&', (byte)'\'', (byte)'(', (byte)')', (byte)'*', (byte)'+', (byte)',', (byte)'-', (byte)'.', (byte)'/', (byte)':', (byte)';', (byte)'<', (byte)'=', (byte)'>', (byte)'?', (byte)'[', (byte)']', (byte)'{', (byte)'}'];
        for (var i = 0; i < punct.Length; i++)
        {
            if (punct[i] is not 0)
            {
                map[ModePunct][punct[i]] = (byte)(i + 1);
            }
        }

        return map;
    }

    private static int[][] CreateShiftTable()
    {
        var table = new int[ModeCount][];
        for (var i = 0; i < ModeCount; i++)
        {
            table[i] = [-1, -1, -1, -1, -1];
        }

        table[ModeUpper][ModePunct] = 0;
        table[ModeLower][ModePunct] = 0;
        table[ModeLower][ModeUpper] = 28;
        table[ModeMixed][ModePunct] = 0;
        table[ModeDigit][ModePunct] = 0;
        table[ModeDigit][ModeUpper] = 15;
        return table;
    }

    public static AztecBitList Encode(ReadOnlySpan<byte> data, int? eci)
    {
        var states = new List<State> { State.Initial };
        if (eci is { } value)
        {
            states[0] = states[0].AppendEci(value);
        }

        var next = new List<State>();
        for (var index = 0; index < data.Length; index++)
        {
            var nextChar = index + 1 < data.Length ? (char)data[index + 1] : '\0';
            var pairCode = ((char)data[index], nextChar) switch
            {
                ('\r', '\n') => 2,
                ('.', ' ') => 3,
                (',', ' ') => 4,
                (':', ' ') => 5,
                _ => 0,
            };

            next.Clear();
            if (pairCode > 0)
            {
                foreach (var state in states)
                {
                    UpdateStateForPair(state, index, pairCode, next);
                }

                index++;
            }
            else
            {
                foreach (var state in states)
                {
                    UpdateStateForChar(state, data[index], index, next);
                }
            }

            states = SimplifyStates(next);
        }

        var best = states[0];
        foreach (var state in states)
        {
            if (state.BitCount < best.BitCount)
            {
                best = state;
            }
        }

        return best.ToBits(data);
    }

    private static void UpdateStateForChar(State state, byte ch, int index, List<State> result)
    {
        var charInCurrentTable = CharMap[state.Mode][ch] > 0;
        State? stateNoBinary = null;
        for (var mode = 0; mode < ModeCount; mode++)
        {
            int charInMode = CharMap[mode][ch];
            if (charInMode > 0)
            {
                stateNoBinary ??= state.EndBinaryShift(index);

                // When the character is in the current mode, latching to another mode (except to the 4-bit digit mode)
                // costs the same bits after this character, so it is never better
                if (!charInCurrentTable || mode == state.Mode || mode == ModeDigit)
                {
                    result.Add(stateNoBinary.LatchAndAppend(mode, charInMode));
                }

                if (!charInCurrentTable && ShiftTable[state.Mode][mode] >= 0)
                {
                    result.Add(stateNoBinary.ShiftAndAppend(mode, charInMode));
                }
            }
        }

        // Starting a binary shift is never better when the character is in the current mode
        if (state.BinaryShiftByteCount > 0 || !charInCurrentTable)
        {
            result.Add(state.AddBinaryShiftByte(index));
        }
    }

    private static void UpdateStateForPair(State state, int index, int pairCode, List<State> result)
    {
        var stateNoBinary = state.EndBinaryShift(index);
        result.Add(stateNoBinary.LatchAndAppend(ModePunct, pairCode));
        if (state.Mode != ModePunct)
        {
            result.Add(stateNoBinary.ShiftAndAppend(ModePunct, pairCode));
        }

        if (pairCode is 3 or 4)
        {
            // The period or the comma, then the space, in the digit mode
            result.Add(stateNoBinary.LatchAndAppend(ModeDigit, 16 - pairCode).LatchAndAppend(ModeDigit, 1));
        }

        if (state.BinaryShiftByteCount > 0)
        {
            result.Add(state.AddBinaryShiftByte(index).AddBinaryShiftByte(index + 1));
        }
    }

    private static List<State> SimplifyStates(List<State> states)
    {
        var result = new List<State>();
        foreach (var newState in states)
        {
            var add = true;
            for (var i = result.Count - 1; i >= 0; i--)
            {
                var oldState = result[i];
                if (oldState.IsBetterThanOrEqualTo(newState))
                {
                    add = false;
                    break;
                }

                if (newState.IsBetterThanOrEqualTo(oldState))
                {
                    result.RemoveAt(i);
                }
            }

            if (add)
            {
                result.Add(newState);
            }
        }

        return result;
    }

    // A sequence of bits, or (when BinaryShiftLength > 0) a binary shift of the bytes from BinaryShiftStart
    private sealed record Token(Token? Previous, int Value, int BitCount, int BinaryShiftStart, int BinaryShiftLength)
    {
        public void AppendTo(AztecBitList bits, ReadOnlySpan<byte> data)
        {
            if (BinaryShiftLength is 0)
            {
                bits.Append(Value, BitCount);
                return;
            }

            for (var i = 0; i < BinaryShiftLength; i++)
            {
                // A length of 32 to 62 bytes is cheaper as two binary shifts (31 bytes, then the rest) than as one
                // binary shift with an 11-bit length
                if (i == 0 || (i == 31 && BinaryShiftLength <= 62))
                {
                    bits.Append(31, 5); // B/S
                    if (BinaryShiftLength > 62)
                    {
                        bits.Append(BinaryShiftLength - 31, 16);
                    }
                    else if (i == 0)
                    {
                        bits.Append(Math.Min(BinaryShiftLength, 31), 5);
                    }
                    else
                    {
                        bits.Append(BinaryShiftLength - 31, 5);
                    }
                }

                bits.Append(data[BinaryShiftStart + i], 8);
            }
        }
    }

    private sealed class State
    {
        public static readonly State Initial = new(token: null, ModeUpper, binaryShiftByteCount: 0, bitCount: 0);

        private readonly Token? _token;
        private readonly int _binaryShiftCost;

        private State(Token? token, int mode, int binaryShiftByteCount, int bitCount)
        {
            _token = token;
            Mode = mode;
            BinaryShiftByteCount = binaryShiftByteCount;
            BitCount = bitCount;
            _binaryShiftCost = binaryShiftByteCount switch
            {
                > 62 => 21,
                > 31 => 20,
                > 0 => 10,
                _ => 0,
            };
        }

        public int Mode { get; }

        public int BinaryShiftByteCount { get; }

        public int BitCount { get; }

        private static Token Add(Token? token, int value, int bitCount) => new(token, value, bitCount, BinaryShiftStart: 0, BinaryShiftLength: 0);

        // P/S FLG(n), the number of digits, and the digits of the ECI assignment number
        public State AppendEci(int eci)
        {
            var result = ShiftAndAppend(ModePunct, 0);
            var digits = eci.ToString(CultureInfo.InvariantCulture);
            var token = Add(result._token, digits.Length, 3);
            foreach (var digit in digits)
            {
                token = Add(token, digit - '0' + 2, 4);
            }

            return new State(token, Mode, 0, result.BitCount + 3 + (digits.Length * 4));
        }

        public State LatchAndAppend(int mode, int value)
        {
            var bitCount = BitCount;
            var token = _token;
            if (mode != Mode)
            {
                var latch = LatchTable[Mode][mode];
                token = Add(token, latch & 0xFFFF, latch >> 16);
                bitCount += latch >> 16;
            }

            var modeBitCount = mode == ModeDigit ? 4 : 5;
            token = Add(token, value, modeBitCount);
            return new State(token, mode, 0, bitCount + modeBitCount);
        }

        // The shifts go to the upper and the punctuation modes, whose codes have 5 bits
        public State ShiftAndAppend(int mode, int value)
        {
            var modeBitCount = Mode == ModeDigit ? 4 : 5;
            var token = Add(_token, ShiftTable[Mode][mode], modeBitCount);
            token = Add(token, value, 5);
            return new State(token, Mode, 0, BitCount + modeBitCount + 5);
        }

        public State AddBinaryShiftByte(int index)
        {
            var token = _token;
            var mode = Mode;
            var bitCount = BitCount;
            if (mode is ModePunct or ModeDigit)
            {
                // These modes have no B/S
                var latch = LatchTable[mode][ModeUpper];
                token = Add(token, latch & 0xFFFF, latch >> 16);
                bitCount += latch >> 16;
                mode = ModeUpper;
            }

            var deltaBitCount = BinaryShiftByteCount switch
            {
                0 or 31 => 18,
                62 => 9,
                _ => 8,
            };

            var result = new State(token, mode, BinaryShiftByteCount + 1, bitCount + deltaBitCount);
            if (result.BinaryShiftByteCount == 2047 + 31)
            {
                // The longest binary shift
                result = result.EndBinaryShift(index + 1);
            }

            return result;
        }

        public State EndBinaryShift(int index)
        {
            if (BinaryShiftByteCount is 0)
                return this;

            var token = new Token(_token, Value: 0, BitCount: 0, index - BinaryShiftByteCount, BinaryShiftByteCount);
            return new State(token, Mode, 0, BitCount);
        }

        public bool IsBetterThanOrEqualTo(State other)
        {
            var newModeBitCount = BitCount + (LatchTable[Mode][other.Mode] >> 16);
            if (BinaryShiftByteCount < other.BinaryShiftByteCount)
            {
                newModeBitCount += other._binaryShiftCost - _binaryShiftCost;
            }
            else if (BinaryShiftByteCount > other.BinaryShiftByteCount && other.BinaryShiftByteCount > 0)
            {
                // This state may cross the 31-byte boundary where the other one stays below it
                newModeBitCount += 10;
            }

            return newModeBitCount <= other.BitCount;
        }

        public AztecBitList ToBits(ReadOnlySpan<byte> data)
        {
            var tokens = new List<Token>();
            for (var token = EndBinaryShift(data.Length)._token; token is not null; token = token.Previous)
            {
                tokens.Add(token);
            }

            var bits = new AztecBitList();
            for (var i = tokens.Count - 1; i >= 0; i--)
            {
                tokens[i].AppendTo(bits, data);
            }

            return bits;
        }
    }
}
