namespace Meziantou.Framework.Barcodes.Internal.DataMatrix;

// ISO/IEC 16022:2006, section 5.2 (encodation schemes) and Annex P (the look-ahead choice of the encodation schemes).
// The end of the data depends on the space the symbol has left: a decoder in C40, Text or X12 returns to ASCII by
// itself when one codeword is left, and in EDIFACT when two codewords or less are left. So the data is encoded for a
// given data capacity, and the encoding fails when it does not fit.
internal sealed class DataMatrixHighLevelEncoder
{
    /// <summary>The FNC1 function character in the message (the other values are bytes).</summary>
    public const int Fnc1 = 256;

    private const int AsciiMode = 0;
    private const int C40Mode = 1;
    private const int TextMode = 2;
    private const int X12Mode = 3;
    private const int EdifactMode = 4;
    private const int Base256Mode = 5;

    private const int LatchToC40 = 230;
    private const int LatchToBase256 = 231;
    private const int Fnc1Codeword = 232;
    private const int UpperShift = 235;
    private const int LatchToX12 = 238;
    private const int LatchToText = 239;
    private const int LatchToEdifact = 240;
    private const int EciCodeword = 241;
    private const int Unlatch = 254;
    private const int Pad = 129;
    private const int EdifactUnlatch = 31;

    // A Base 256 field of more characters cannot have a length field
    private const int MaxBase256Length = 1555;

    private readonly int[] _message;
    private readonly int _capacity;
    private readonly List<int> _codewords = [];
    private int _position;
    private bool _failed;

    // The characters before this position are encoded in ASCII: they end the data and did not fit in the other scheme
    private int _asciiUntil;

    private DataMatrixHighLevelEncoder(int[] message, int capacity)
    {
        _message = message;
        _capacity = capacity;
    }

    private bool IsUnbounded => _capacity == int.MaxValue;

    private int Remaining => _capacity - _codewords.Count;

    /// <summary>Encodes the message as data codewords, padded to <paramref name="capacity"/>.</summary>
    /// <param name="message">The bytes of the message, and <see cref="Fnc1"/> for the FNC1 separators of GS1 data.</param>
    /// <param name="gs1">Whether the data starts with the FNC1 that identifies GS1 Data Matrix.</param>
    /// <param name="eci">The ECI assignment number declared at the start of the data, or <see langword="null"/>.</param>
    /// <param name="capacity">The number of data codewords of the symbol, or <see cref="int.MaxValue"/> for no limit (and no padding).</param>
    /// <returns>The codewords, or <see langword="null"/> when they do not fit.</returns>
    public static List<int>? Encode(int[] message, bool gs1, int? eci, int capacity)
    {
        var encoder = new DataMatrixHighLevelEncoder(message, capacity);
        return encoder.Encode(gs1, eci) ? encoder._codewords : null;
    }

    private bool Encode(bool gs1, int? eci)
    {
        if (gs1)
        {
            _codewords.Add(Fnc1Codeword);
        }

        if (eci is not null)
        {
            WriteEci(eci.Value);
        }

        while (_position < _message.Length)
        {
            if (_failed || _codewords.Count > _capacity)
                return false;

            EncodeAsciiStep();
        }

        if (_failed || _codewords.Count > _capacity)
            return false;

        if (!IsUnbounded && _codewords.Count < _capacity)
        {
            _codewords.Add(Pad);
            while (_codewords.Count < _capacity)
            {
                // The 253-state algorithm randomizes the other pad codewords
                var pad = Pad + ((149 * (_codewords.Count + 1)) % 253) + 1;
                _codewords.Add(pad <= 254 ? pad : pad - 254);
            }
        }

        return true;
    }

    private void WriteEci(int value)
    {
        _codewords.Add(EciCodeword);
        if (value < 127)
        {
            _codewords.Add(value + 1);
        }
        else if (value < 16383)
        {
            _codewords.Add(((value - 127) / 254) + 128);
            _codewords.Add(((value - 127) % 254) + 1);
        }
        else
        {
            _codewords.Add(((value - 16383) / 64516) + 192);
            _codewords.Add((((value - 16383) / 254) % 254) + 1);
            _codewords.Add(((value - 16383) % 254) + 1);
        }
    }

    private void EncodeAsciiStep()
    {
        var c = _message[_position];
        if (IsDigit(c) && _position + 1 < _message.Length && IsDigit(_message[_position + 1]))
        {
            _codewords.Add(130 + ((c - '0') * 10) + (_message[_position + 1] - '0'));
            _position += 2;
            return;
        }

        if (_position >= _asciiUntil)
        {
            switch (LookAhead(_position, AsciiMode))
            {
                case C40Mode:
                    EncodeC40OrText(text: false);
                    return;
                case TextMode:
                    EncodeC40OrText(text: true);
                    return;
                case X12Mode when AreNative(_position, 3, IsNativeX12):
                    EncodeX12();
                    return;
                case EdifactMode when AreNative(_position, 4, IsNativeEdifact):
                    EncodeEdifact();
                    return;
                case Base256Mode when c != Fnc1:
                    EncodeBase256();
                    return;
            }
        }

        WriteAsciiCharacter(c);
        _position++;
    }

    private void WriteAsciiCharacter(int c)
    {
        if (c == Fnc1)
        {
            _codewords.Add(Fnc1Codeword);
        }
        else if (c >= 128)
        {
            _codewords.Add(UpperShift);
            _codewords.Add(c - 127);
        }
        else
        {
            _codewords.Add(c + 1);
        }
    }

    private void EncodeC40OrText(bool text)
    {
        var mode = text ? TextMode : C40Mode;
        _codewords.Add(text ? LatchToText : LatchToC40);

        var values = new List<int>();
        var valueCounts = new List<int>();
        while (_position < _message.Length)
        {
            var count = values.Count;
            AppendC40Values(_message[_position], text, values);
            valueCounts.Add(values.Count - count);
            _position++;
            if (_position < _message.Length && values.Count % 3 == 0 && LookAhead(_position, mode) != mode)
                break;
        }

        if (_position == _message.Length && values.Count % 3 == 1)
        {
            // A single value left: when one codeword is left after the triplets, the decoder reads it in ASCII
            if (valueCounts[^1] == 1 && Remaining - ((values.Count / 3) * 2) == 1)
            {
                WriteTriplets(values, values.Count - 1);
                WriteAsciiCharacter(_message[_position - 1]);
                return;
            }

            // Otherwise, the last characters are encoded in ASCII after an unlatch, until the values fill triplets (with
            // a Shift 1 as padding for two values)
            var end = _position;
            while (values.Count % 3 == 1)
            {
                values.RemoveRange(values.Count - valueCounts[^1], valueCounts[^1]);
                valueCounts.RemoveAt(valueCounts.Count - 1);
                _position--;
            }

            _asciiUntil = end;
        }

        if (values.Count % 3 == 2)
        {
            values.Add(0);
        }

        WriteTriplets(values, values.Count);
        WriteUnlatch();
    }

    private void EncodeX12()
    {
        _codewords.Add(LatchToX12);
        var values = new List<int>();
        while (_position < _message.Length)
        {
            values.Add(GetX12Value(_message[_position]));
            _position++;
            if (_position < _message.Length && values.Count % 3 == 0 && LookAhead(_position, X12Mode) != X12Mode)
                break;
        }

        // X12 has no padding value: the last characters are encoded in ASCII
        var rest = values.Count % 3;
        if (rest != 0)
        {
            values.RemoveRange(values.Count - rest, rest);
            _asciiUntil = _position;
            _position -= rest;
        }

        WriteTriplets(values, values.Count);
        WriteUnlatch();
    }

    private void WriteTriplets(List<int> values, int count)
    {
        for (var i = 0; i + 2 < count; i += 3)
        {
            var value = (1600 * values[i]) + (40 * values[i + 1]) + values[i + 2] + 1;
            _codewords.Add(value / 256);
            _codewords.Add(value % 256);
        }
    }

    // The decoder of C40, Text and X12 returns to ASCII by itself when one codeword is left
    private void WriteUnlatch()
    {
        if (Remaining >= 2 && (!IsUnbounded || _position < _message.Length))
        {
            _codewords.Add(Unlatch);
        }
    }

    private void EncodeEdifact()
    {
        _codewords.Add(LatchToEdifact);
        var values = new List<int>();
        while (_position < _message.Length)
        {
            values.Add(_message[_position] & 0x3F);
            _position++;
            if (_position < _message.Length && values.Count % 4 == 0 && LookAhead(_position, EdifactMode) != EdifactMode)
                break;
        }

        var rest = values.Count % 4;
        WriteEdifactValues(values, 0, values.Count - rest);

        // The decoder returns to ASCII by itself when two codewords or less are left, and reads the next values otherwise
        if (rest == 0)
        {
            if (Remaining >= 3 && (!IsUnbounded || _position < _message.Length))
            {
                WriteEdifactValues([EdifactUnlatch], 0, 1);
            }

            return;
        }

        var restStart = _position - rest;
        if (Remaining <= 2)
        {
            if (GetAsciiLength(restStart, _position) > Remaining)
            {
                _failed = true;
                return;
            }

            _asciiUntil = _position;
            _position = restStart;
            return;
        }

        values.RemoveRange(0, values.Count - rest);
        values.Add(EdifactUnlatch);
        WriteEdifactValues(values, 0, values.Count);
    }

    // Packs the 6-bit values in codewords, most significant bit first, the last codeword padded with zeros
    private void WriteEdifactValues(List<int> values, int start, int end)
    {
        var buffer = 0;
        var bitCount = 0;
        for (var i = start; i < end; i++)
        {
            buffer = (buffer << 6) | values[i];
            bitCount += 6;
            while (bitCount >= 8)
            {
                bitCount -= 8;
                _codewords.Add((buffer >> bitCount) & 0xFF);
            }
        }

        if (bitCount > 0)
        {
            _codewords.Add((buffer << (8 - bitCount)) & 0xFF);
        }
    }

    private void EncodeBase256()
    {
        _codewords.Add(LatchToBase256);
        var start = _position;
        while (_position < _message.Length && _message[_position] != Fnc1)
        {
            _position++;
            if (_position < _message.Length && LookAhead(_position, Base256Mode) != Base256Mode)
                break;
        }

        // A length of 0 means that the field ends with the symbol, and a longer field needs this
        var toEndOfSymbol = _position == _message.Length && !IsUnbounded && _codewords.Count + 1 + (_position - start) == _capacity;
        if (!toEndOfSymbol && _position - start > MaxBase256Length)
        {
            _position = start + MaxBase256Length;
        }

        var count = _position - start;
        if (toEndOfSymbol)
        {
            WriteBase256(0);
        }
        else if (count <= 249)
        {
            WriteBase256(count);
        }
        else
        {
            WriteBase256((count / 250) + 249);
            WriteBase256(count % 250);
        }

        for (var i = start; i < _position; i++)
        {
            WriteBase256(_message[i]);
        }
    }

    // The 255-state algorithm randomizes the codewords of Base 256
    private void WriteBase256(int value)
    {
        var randomized = value + ((149 * (_codewords.Count + 1)) % 255) + 1;
        _codewords.Add(randomized <= 255 ? randomized : randomized - 256);
    }

    private int GetAsciiLength(int start, int end)
    {
        var length = 0;
        for (var i = start; i < end; i++)
        {
            if (IsDigit(_message[i]) && i + 1 < end && IsDigit(_message[i + 1]))
            {
                i++;
                length++;
            }
            else
            {
                length += _message[i] is >= 128 and < Fnc1 ? 2 : 1;
            }
        }

        return length;
    }

    private bool AreNative(int start, int count, Func<int, bool> isNative)
    {
        var end = Math.Min(start + count, _message.Length);
        for (var i = start; i < end; i++)
        {
            if (!isNative(_message[i]))
                return false;
        }

        return true;
    }

    private int LookAhead(int start, int mode)
    {
        var result = LookAheadCore(start, mode);
        if (mode is X12Mode && result is X12Mode && !AreNative(start, 3, IsNativeX12))
            return AsciiMode;

        if (mode is EdifactMode && result is EdifactMode && !AreNative(start, 4, IsNativeEdifact))
            return AsciiMode;

        return result;
    }

    // The counts are in twelfths of codewords, so that the thirds (C40, Text, X12) and the quarters (EDIFACT) are exact
    private int LookAheadCore(int start, int mode)
    {
        if (start >= _message.Length)
            return mode;

        Span<int> counts = mode is AsciiMode ? [0, 12, 12, 12, 12, 15] : [12, 24, 24, 24, 24, 27];
        counts[mode] = 0;
        Span<int> ceilings = stackalloc int[6];
        var processed = 0;
        while (true)
        {
            if (start + processed == _message.Length)
            {
                var minimum = int.MaxValue;
                for (var i = 0; i < 6; i++)
                {
                    ceilings[i] = (counts[i] + 11) / 12;
                    minimum = Math.Min(minimum, ceilings[i]);
                }

                if (ceilings[AsciiMode] == minimum)
                    return AsciiMode;

                if (ceilings.Count(minimum) == 1)
                {
                    if (ceilings[Base256Mode] == minimum)
                        return Base256Mode;

                    if (ceilings[EdifactMode] == minimum)
                        return EdifactMode;

                    if (ceilings[TextMode] == minimum)
                        return TextMode;

                    if (ceilings[X12Mode] == minimum)
                        return X12Mode;
                }

                return C40Mode;
            }

            var c = _message[start + processed];
            processed++;

            var extended = c is >= 128 and < Fnc1;
            counts[AsciiMode] = IsDigit(c) ? counts[AsciiMode] + 6 : ((counts[AsciiMode] + 11) / 12 * 12) + (extended ? 24 : 12);
            counts[C40Mode] += IsNativeC40(c) ? 8 : extended ? 32 : 16;
            counts[TextMode] += IsNativeText(c) ? 8 : extended ? 32 : 16;
            counts[X12Mode] += IsNativeX12(c) ? 8 : extended ? 52 : 40;
            counts[EdifactMode] += IsNativeEdifact(c) ? 9 : extended ? 51 : 39;
            counts[Base256Mode] += c == Fnc1 ? 48 : 12;

            if (processed >= 4)
            {
                for (var i = 0; i < 6; i++)
                {
                    ceilings[i] = (counts[i] + 11) / 12;
                }

                var ascii = ceilings[AsciiMode];
                var c40 = ceilings[C40Mode];
                var text = ceilings[TextMode];
                var x12 = ceilings[X12Mode];
                var edifact = ceilings[EdifactMode];
                var base256 = ceilings[Base256Mode];
                if (ascii < Min(base256, c40, text, x12, edifact))
                    return AsciiMode;

                if (base256 < ascii || base256 + 1 < Min(c40, text, x12, edifact))
                    return Base256Mode;

                if (edifact + 1 < Min(base256, c40, text, x12, ascii))
                    return EdifactMode;

                if (text + 1 < Min(base256, c40, edifact, x12, ascii))
                    return TextMode;

                if (x12 + 1 < Min(base256, c40, edifact, text, ascii))
                    return X12Mode;

                if (c40 + 1 < Min(ascii, base256, edifact, text))
                {
                    if (c40 < x12)
                        return C40Mode;

                    if (c40 == x12)
                    {
                        // X12 when an X12 terminator or separator comes before a character X12 cannot encode
                        for (var i = start + processed; i < _message.Length && IsNativeX12(_message[i]); i++)
                        {
                            if (_message[i] is '\r' or '*' or '>')
                                return X12Mode;
                        }

                        return C40Mode;
                    }
                }
            }
        }
    }

    private static int Min(int a, int b, int c, int d) => Math.Min(Math.Min(a, b), Math.Min(c, d));

    private static int Min(int a, int b, int c, int d, int e) => Math.Min(Min(a, b, c, d), e);

    private static void AppendC40Values(int c, bool text, List<int> values)
    {
        if (c == Fnc1)
        {
            values.Add(1);
            values.Add(27);
            return;
        }

        if (c >= 128)
        {
            // Upper Shift, then the character minus 128
            values.Add(1);
            values.Add(30);
            c -= 128;
        }

        switch (c)
        {
            case ' ':
                values.Add(3);
                break;
            case >= '0' and <= '9':
                values.Add(c - '0' + 4);
                break;
            case >= 'A' and <= 'Z' when !text:
                values.Add(c - 'A' + 14);
                break;
            case >= 'a' and <= 'z' when text:
                values.Add(c - 'a' + 14);
                break;
            case >= 'A' and <= 'Z':
                values.Add(2);
                values.Add(c - 'A' + 1);
                break;
            case >= 'a' and <= 'z':
                values.Add(2);
                values.Add(c - 'a' + 1);
                break;
            case < 32:
                values.Add(0);
                values.Add(c);
                break;
            case <= 47:
                values.Add(1);
                values.Add(c - 33);
                break;
            case <= 64:
                values.Add(1);
                values.Add(c - 58 + 15);
                break;
            case <= 95:
                values.Add(1);
                values.Add(c - 91 + 22);
                break;
            case 96:
                values.Add(2);
                values.Add(0);
                break;
            default:
                values.Add(2);
                values.Add(c - 123 + 27);
                break;
        }
    }

    private static int GetX12Value(int c) => c switch
    {
        '\r' => 0,
        '*' => 1,
        '>' => 2,
        ' ' => 3,
        >= '0' and <= '9' => c - '0' + 4,
        _ => c - 'A' + 14,
    };

    private static bool IsDigit(int c) => c is >= '0' and <= '9';

    private static bool IsNativeC40(int c) => c is ' ' or (>= '0' and <= '9') or (>= 'A' and <= 'Z');

    private static bool IsNativeText(int c) => c is ' ' or (>= '0' and <= '9') or (>= 'a' and <= 'z');

    private static bool IsNativeX12(int c) => c is '\r' or '*' or '>' or ' ' or (>= '0' and <= '9') or (>= 'A' and <= 'Z');

    private static bool IsNativeEdifact(int c) => c is >= 32 and <= 94;
}
