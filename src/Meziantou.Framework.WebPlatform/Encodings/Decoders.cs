using System.Buffers;
using System.Text;
using System.Text.Unicode;

namespace Meziantou.Framework.WebPlatform.Encodings;

/// <summary>https://encoding.spec.whatwg.org/#utf-8-decoder</summary>
internal sealed class Utf8Decoder(bool fatal) : WebDecoder(fatal)
{
    private int _codePoint;
    private int _bytesSeen;
    private int _bytesNeeded;
    private int _lowerBoundary = 0x80;
    private int _upperBoundary = 0xBF;

    protected override void ResetState()
    {
        _codePoint = 0;
        _bytesSeen = 0;
        _bytesNeeded = 0;
        _lowerBoundary = 0x80;
        _upperBoundary = 0xBF;
    }

    protected override bool TryDecodeFast(ReadOnlySpan<byte> input, Span<char> output, out int bytesConsumed, out int charsWritten)
    {
        if (_bytesNeeded != 0 || HasRestoredBytes)
        {
            bytesConsumed = 0;
            charsWritten = 0;
            return false;
        }

        // Valid UTF-8 is decoded identically by the BCL. Stop at the first invalid or incomplete sequence and let the
        // spec handler process it, so error handling exactly follows the Encoding Standard.
        var status = Utf8.ToUtf16(input, output, out bytesConsumed, out charsWritten, replaceInvalidSequences: false, isFinalBlock: false);
        _ = status;
        return bytesConsumed > 0;
    }

    protected override HandlerResult Handle(int value, out int codePoint1, out int codePoint2)
    {
        codePoint2 = 0;
        codePoint1 = 0;
        if (value == EndOfQueue)
        {
            if (_bytesNeeded != 0)
            {
                _bytesNeeded = 0;
                return HandlerResult.Error;
            }

            return HandlerResult.Finished;
        }

        if (_bytesNeeded == 0)
        {
            switch (value)
            {
                case <= 0x7F:
                    codePoint1 = value;
                    return HandlerResult.CodePoint;

                case >= 0xC2 and <= 0xDF:
                    _bytesNeeded = 1;
                    _codePoint = value & 0x1F;
                    break;

                case >= 0xE0 and <= 0xEF:
                    if (value == 0xE0)
                    {
                        _lowerBoundary = 0xA0;
                    }

                    if (value == 0xED)
                    {
                        _upperBoundary = 0x9F;
                    }

                    _bytesNeeded = 2;
                    _codePoint = value & 0xF;
                    break;

                case >= 0xF0 and <= 0xF4:
                    if (value == 0xF0)
                    {
                        _lowerBoundary = 0x90;
                    }

                    if (value == 0xF4)
                    {
                        _upperBoundary = 0x8F;
                    }

                    _bytesNeeded = 3;
                    _codePoint = value & 0x7;
                    break;

                default:
                    return HandlerResult.Error;
            }

            return HandlerResult.Continue;
        }

        if (value < _lowerBoundary || value > _upperBoundary)
        {
            _codePoint = 0;
            _bytesNeeded = 0;
            _bytesSeen = 0;
            _lowerBoundary = 0x80;
            _upperBoundary = 0xBF;
            Restore((byte)value);
            return HandlerResult.Error;
        }

        _lowerBoundary = 0x80;
        _upperBoundary = 0xBF;
        _codePoint = (_codePoint << 6) | (value & 0x3F);
        _bytesSeen++;
        if (_bytesSeen != _bytesNeeded)
            return HandlerResult.Continue;

        codePoint1 = _codePoint;
        _codePoint = 0;
        _bytesNeeded = 0;
        _bytesSeen = 0;
        return HandlerResult.CodePoint;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#single-byte-decoder</summary>
internal sealed class SingleByteDecoder(bool fatal, int indexOffset) : WebDecoder(fatal)
{
    protected override void ResetState()
    {
    }

    protected override bool TryDecodeFast(ReadOnlySpan<byte> input, Span<char> output, out int bytesConsumed, out int charsWritten)
    {
        var index = EncodingIndexes.SingleByte.Slice(indexOffset, 128);
        var length = Math.Min(input.Length, output.Length);
        var i = 0;
        for (; i < length; i++)
        {
            var value = input[i];
            if (value < 0x80)
            {
                output[i] = (char)value;
                continue;
            }

            var codePoint = index[value - 0x80];
            if (codePoint == 0)
                break;

            output[i] = (char)codePoint;
        }

        bytesConsumed = i;
        charsWritten = i;
        return i > 0;
    }

    protected override HandlerResult Handle(int value, out int codePoint1, out int codePoint2)
    {
        codePoint2 = 0;
        codePoint1 = 0;
        if (value == EndOfQueue)
            return HandlerResult.Finished;

        if (value < 0x80)
        {
            codePoint1 = value;
            return HandlerResult.CodePoint;
        }

        var codePoint = EncodingIndexes.SingleByte[indexOffset + value - 0x80];
        if (codePoint == 0)
            return HandlerResult.Error;

        codePoint1 = codePoint;
        return HandlerResult.CodePoint;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#gb18030-decoder (also used for GBK)</summary>
internal sealed class Gb18030Decoder(bool fatal) : WebDecoder(fatal)
{
    private byte _first;
    private byte _second;
    private byte _third;

    protected override void ResetState()
    {
        _first = 0;
        _second = 0;
        _third = 0;
    }

    protected override bool TryDecodeFast(ReadOnlySpan<byte> input, Span<char> output, out int bytesConsumed, out int charsWritten)
    {
        return AsciiFastPath.TryDecode(_first == 0 && !HasRestoredBytes, input, output, out bytesConsumed, out charsWritten);
    }

    protected override HandlerResult Handle(int value, out int codePoint1, out int codePoint2)
    {
        codePoint1 = 0;
        codePoint2 = 0;
        if (value == EndOfQueue)
        {
            if (_first == 0 && _second == 0 && _third == 0)
                return HandlerResult.Finished;

            _first = 0;
            _second = 0;
            _third = 0;
            return HandlerResult.Error;
        }

        if (_third != 0)
        {
            if (value is < 0x30 or > 0x39)
            {
                Restore(_second, _third, (byte)value);
                _first = 0;
                _second = 0;
                _third = 0;
                return HandlerResult.Error;
            }

            var codePoint = EncodingIndexHelpers.Gb18030RangesCodePoint(((_first - 0x81) * (10 * 126 * 10)) + ((_second - 0x30) * (10 * 126)) + ((_third - 0x81) * 10) + value - 0x30);
            _first = 0;
            _second = 0;
            _third = 0;
            if (codePoint < 0)
                return HandlerResult.Error;

            codePoint1 = codePoint;
            return HandlerResult.CodePoint;
        }

        if (_second != 0)
        {
            if (value is >= 0x81 and <= 0xFE)
            {
                _third = (byte)value;
                return HandlerResult.Continue;
            }

            Restore(_second, (byte)value);
            _first = 0;
            _second = 0;
            return HandlerResult.Error;
        }

        if (_first != 0)
        {
            if (value is >= 0x30 and <= 0x39)
            {
                _second = (byte)value;
                return HandlerResult.Continue;
            }

            var leading = _first;
            _first = 0;
            var pointer = -1;
            var offset = value < 0x7F ? 0x40 : 0x41;
            if (value is (>= 0x40 and <= 0x7E) or (>= 0x80 and <= 0xFE))
            {
                pointer = ((leading - 0x81) * 190) + (value - offset);
            }

            var codePoint = pointer < 0 ? 0 : EncodingIndexHelpers.IndexCodePoint(EncodingIndexes.Gb18030, pointer);
            if (codePoint != 0)
            {
                codePoint1 = codePoint;
                return HandlerResult.CodePoint;
            }

            if (value < 0x80)
            {
                Restore((byte)value);
            }

            return HandlerResult.Error;
        }

        if (value < 0x80)
        {
            codePoint1 = value;
            return HandlerResult.CodePoint;
        }

        if (value == 0x80)
        {
            codePoint1 = 0x20AC;
            return HandlerResult.CodePoint;
        }

        if (value is >= 0x81 and <= 0xFE)
        {
            _first = (byte)value;
            return HandlerResult.Continue;
        }

        return HandlerResult.Error;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#big5-decoder</summary>
internal sealed class Big5Decoder(bool fatal) : WebDecoder(fatal)
{
    private byte _leading;

    protected override void ResetState() => _leading = 0;

    protected override bool TryDecodeFast(ReadOnlySpan<byte> input, Span<char> output, out int bytesConsumed, out int charsWritten)
    {
        return AsciiFastPath.TryDecode(_leading == 0 && !HasRestoredBytes, input, output, out bytesConsumed, out charsWritten);
    }

    protected override HandlerResult Handle(int value, out int codePoint1, out int codePoint2)
    {
        codePoint1 = 0;
        codePoint2 = 0;
        if (value == EndOfQueue)
        {
            if (_leading != 0)
            {
                _leading = 0;
                return HandlerResult.Error;
            }

            return HandlerResult.Finished;
        }

        if (_leading != 0)
        {
            var leading = _leading;
            _leading = 0;
            var pointer = -1;
            var offset = value < 0x7F ? 0x40 : 0x62;
            if (value is (>= 0x40 and <= 0x7E) or (>= 0xA1 and <= 0xFE))
            {
                pointer = ((leading - 0x81) * 157) + (value - offset);
            }

            switch (pointer)
            {
                case 1133:
                    codePoint1 = 0x00CA;
                    codePoint2 = 0x0304;
                    return HandlerResult.TwoCodePoints;
                case 1135:
                    codePoint1 = 0x00CA;
                    codePoint2 = 0x030C;
                    return HandlerResult.TwoCodePoints;
                case 1164:
                    codePoint1 = 0x00EA;
                    codePoint2 = 0x0304;
                    return HandlerResult.TwoCodePoints;
                case 1166:
                    codePoint1 = 0x00EA;
                    codePoint2 = 0x030C;
                    return HandlerResult.TwoCodePoints;
            }

            var codePoint = pointer < 0 ? 0 : EncodingIndexHelpers.IndexCodePoint(EncodingIndexes.Big5, pointer);
            if (codePoint != 0)
            {
                codePoint1 = codePoint;
                return HandlerResult.CodePoint;
            }

            if (value < 0x80)
            {
                Restore((byte)value);
            }

            return HandlerResult.Error;
        }

        if (value < 0x80)
        {
            codePoint1 = value;
            return HandlerResult.CodePoint;
        }

        if (value is >= 0x81 and <= 0xFE)
        {
            _leading = (byte)value;
            return HandlerResult.Continue;
        }

        return HandlerResult.Error;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#euc-jp-decoder</summary>
internal sealed class EucJPDecoder(bool fatal) : WebDecoder(fatal)
{
    private bool _jis0212;
    private byte _leading;

    protected override void ResetState()
    {
        _jis0212 = false;
        _leading = 0;
    }

    protected override bool TryDecodeFast(ReadOnlySpan<byte> input, Span<char> output, out int bytesConsumed, out int charsWritten)
    {
        return AsciiFastPath.TryDecode(_leading == 0 && !HasRestoredBytes, input, output, out bytesConsumed, out charsWritten);
    }

    protected override HandlerResult Handle(int value, out int codePoint1, out int codePoint2)
    {
        codePoint1 = 0;
        codePoint2 = 0;
        if (value == EndOfQueue)
        {
            if (_leading != 0)
            {
                _leading = 0;
                return HandlerResult.Error;
            }

            return HandlerResult.Finished;
        }

        if (_leading == 0x8E && value is >= 0xA1 and <= 0xDF)
        {
            _leading = 0;
            codePoint1 = 0xFF61 - 0xA1 + value;
            return HandlerResult.CodePoint;
        }

        if (_leading == 0x8F && value is >= 0xA1 and <= 0xFE)
        {
            _jis0212 = true;
            _leading = (byte)value;
            return HandlerResult.Continue;
        }

        if (_leading != 0)
        {
            var leading = _leading;
            _leading = 0;
            var codePoint = 0;
            if (leading is >= 0xA1 and <= 0xFE && value is >= 0xA1 and <= 0xFE)
            {
                var pointer = ((leading - 0xA1) * 94) + value - 0xA1;
                codePoint = EncodingIndexHelpers.IndexCodePoint(_jis0212 ? EncodingIndexes.Jis0212 : EncodingIndexes.Jis0208, pointer);
            }

            _jis0212 = false;
            if (codePoint != 0)
            {
                codePoint1 = codePoint;
                return HandlerResult.CodePoint;
            }

            if (value < 0x80)
            {
                Restore((byte)value);
            }

            return HandlerResult.Error;
        }

        if (value < 0x80)
        {
            codePoint1 = value;
            return HandlerResult.CodePoint;
        }

        if (value is 0x8E or 0x8F or (>= 0xA1 and <= 0xFE))
        {
            _leading = (byte)value;
            return HandlerResult.Continue;
        }

        return HandlerResult.Error;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#iso-2022-jp-decoder</summary>
internal sealed class Iso2022JPDecoder(bool fatal) : WebDecoder(fatal)
{
    private enum State : byte
    {
        Ascii,
        Roman,
        Katakana,
        LeadingByte,
        TrailingByte,
        EscapeStart,
        Escape,
    }

    private State _state;
    private State _outputState;
    private byte _leading;
    private bool _output;

    protected override void ResetState()
    {
        _state = State.Ascii;
        _outputState = State.Ascii;
        _leading = 0;
        _output = false;
    }

    protected override HandlerResult Handle(int value, out int codePoint1, out int codePoint2)
    {
        codePoint1 = 0;
        codePoint2 = 0;
        switch (_state)
        {
            case State.Ascii:
                if (value == 0x1B)
                {
                    _state = State.EscapeStart;
                    return HandlerResult.Continue;
                }

                if (value is >= 0x00 and <= 0x7F and not 0x0E and not 0x0F)
                {
                    _output = false;
                    codePoint1 = value;
                    return HandlerResult.CodePoint;
                }

                if (value == EndOfQueue)
                    return HandlerResult.Finished;

                _output = false;
                return HandlerResult.Error;

            case State.Roman:
                if (value == 0x1B)
                {
                    _state = State.EscapeStart;
                    return HandlerResult.Continue;
                }

                if (value == 0x5C)
                {
                    _output = false;
                    codePoint1 = 0x00A5;
                    return HandlerResult.CodePoint;
                }

                if (value == 0x7E)
                {
                    _output = false;
                    codePoint1 = 0x203E;
                    return HandlerResult.CodePoint;
                }

                if (value is >= 0x00 and <= 0x7F and not 0x0E and not 0x0F)
                {
                    _output = false;
                    codePoint1 = value;
                    return HandlerResult.CodePoint;
                }

                if (value == EndOfQueue)
                    return HandlerResult.Finished;

                _output = false;
                return HandlerResult.Error;

            case State.Katakana:
                if (value == 0x1B)
                {
                    _state = State.EscapeStart;
                    return HandlerResult.Continue;
                }

                if (value is >= 0x21 and <= 0x5F)
                {
                    _output = false;
                    codePoint1 = 0xFF61 - 0x21 + value;
                    return HandlerResult.CodePoint;
                }

                if (value == EndOfQueue)
                    return HandlerResult.Finished;

                _output = false;
                return HandlerResult.Error;

            case State.LeadingByte:
                if (value == 0x1B)
                {
                    _state = State.EscapeStart;
                    return HandlerResult.Continue;
                }

                if (value is >= 0x21 and <= 0x7E)
                {
                    _output = false;
                    _leading = (byte)value;
                    _state = State.TrailingByte;
                    return HandlerResult.Continue;
                }

                if (value == EndOfQueue)
                    return HandlerResult.Finished;

                _output = false;
                return HandlerResult.Error;

            case State.TrailingByte:
                if (value == 0x1B)
                {
                    _state = State.EscapeStart;
                    return HandlerResult.Error;
                }

                if (value is >= 0x21 and <= 0x7E)
                {
                    _state = State.LeadingByte;
                    var pointer = ((_leading - 0x21) * 94) + value - 0x21;
                    var codePoint = EncodingIndexHelpers.IndexCodePoint(EncodingIndexes.Jis0208, pointer);
                    if (codePoint == 0)
                        return HandlerResult.Error;

                    codePoint1 = codePoint;
                    return HandlerResult.CodePoint;
                }

                _state = State.LeadingByte;
                return HandlerResult.Error;

            case State.EscapeStart:
                if (value is 0x24 or 0x28)
                {
                    _leading = (byte)value;
                    _state = State.Escape;
                    return HandlerResult.Continue;
                }

                if (value != EndOfQueue)
                {
                    Restore((byte)value);
                }

                _output = false;
                _state = _outputState;
                return HandlerResult.Error;

            case State.Escape:
            {
                var leading = _leading;
                _leading = 0;
                State? state = null;
                if (leading == 0x28 && value == 0x42)
                {
                    state = State.Ascii;
                }
                else if (leading == 0x28 && value == 0x4A)
                {
                    state = State.Roman;
                }
                else if (leading == 0x28 && value == 0x49)
                {
                    state = State.Katakana;
                }
                else if (leading == 0x24 && value is 0x40 or 0x42)
                {
                    state = State.LeadingByte;
                }

                if (state is not null)
                {
                    _state = state.Value;
                    _outputState = state.Value;
                    var output = _output;
                    _output = true;
                    return output ? HandlerResult.Error : HandlerResult.Continue;
                }

                if (value == EndOfQueue)
                {
                    Restore(leading);
                }
                else
                {
                    Restore(leading, (byte)value);
                }

                _output = false;
                _state = _outputState;
                return HandlerResult.Error;
            }

            default:
                throw new InvalidOperationException("Invalid state");
        }
    }
}

/// <summary>https://encoding.spec.whatwg.org/#shift_jis-decoder</summary>
internal sealed class ShiftJisDecoder(bool fatal) : WebDecoder(fatal)
{
    private byte _leading;

    protected override void ResetState() => _leading = 0;

    protected override bool TryDecodeFast(ReadOnlySpan<byte> input, Span<char> output, out int bytesConsumed, out int charsWritten)
    {
        return AsciiFastPath.TryDecode(_leading == 0 && !HasRestoredBytes, input, output, out bytesConsumed, out charsWritten);
    }

    protected override HandlerResult Handle(int value, out int codePoint1, out int codePoint2)
    {
        codePoint1 = 0;
        codePoint2 = 0;
        if (value == EndOfQueue)
        {
            if (_leading != 0)
            {
                _leading = 0;
                return HandlerResult.Error;
            }

            return HandlerResult.Finished;
        }

        if (_leading != 0)
        {
            var leading = _leading;
            _leading = 0;
            var pointer = -1;
            var offset = value < 0x7F ? 0x40 : 0x41;
            var leadingOffset = leading < 0xA0 ? 0x81 : 0xC1;
            if (value is (>= 0x40 and <= 0x7E) or (>= 0x80 and <= 0xFC))
            {
                pointer = ((leading - leadingOffset) * 188) + value - offset;
            }

            if (pointer is >= 8836 and <= 10715)
            {
                codePoint1 = 0xE000 - 8836 + pointer;
                return HandlerResult.CodePoint;
            }

            var codePoint = pointer < 0 ? 0 : EncodingIndexHelpers.IndexCodePoint(EncodingIndexes.Jis0208, pointer);
            if (codePoint != 0)
            {
                codePoint1 = codePoint;
                return HandlerResult.CodePoint;
            }

            if (value < 0x80)
            {
                Restore((byte)value);
            }

            return HandlerResult.Error;
        }

        if (value <= 0x80)
        {
            codePoint1 = value;
            return HandlerResult.CodePoint;
        }

        if (value is >= 0xA1 and <= 0xDF)
        {
            codePoint1 = 0xFF61 - 0xA1 + value;
            return HandlerResult.CodePoint;
        }

        if (value is (>= 0x81 and <= 0x9F) or (>= 0xE0 and <= 0xFC))
        {
            _leading = (byte)value;
            return HandlerResult.Continue;
        }

        return HandlerResult.Error;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#euc-kr-decoder</summary>
internal sealed class EucKRDecoder(bool fatal) : WebDecoder(fatal)
{
    private byte _leading;

    protected override void ResetState() => _leading = 0;

    protected override bool TryDecodeFast(ReadOnlySpan<byte> input, Span<char> output, out int bytesConsumed, out int charsWritten)
    {
        return AsciiFastPath.TryDecode(_leading == 0 && !HasRestoredBytes, input, output, out bytesConsumed, out charsWritten);
    }

    protected override HandlerResult Handle(int value, out int codePoint1, out int codePoint2)
    {
        codePoint1 = 0;
        codePoint2 = 0;
        if (value == EndOfQueue)
        {
            if (_leading != 0)
            {
                _leading = 0;
                return HandlerResult.Error;
            }

            return HandlerResult.Finished;
        }

        if (_leading != 0)
        {
            var leading = _leading;
            _leading = 0;
            var codePoint = 0;
            if (value is >= 0x41 and <= 0xFE)
            {
                codePoint = EncodingIndexHelpers.IndexCodePoint(EncodingIndexes.EucKR, ((leading - 0x81) * 190) + (value - 0x41));
            }

            if (codePoint != 0)
            {
                codePoint1 = codePoint;
                return HandlerResult.CodePoint;
            }

            if (value < 0x80)
            {
                Restore((byte)value);
            }

            return HandlerResult.Error;
        }

        if (value < 0x80)
        {
            codePoint1 = value;
            return HandlerResult.CodePoint;
        }

        if (value is >= 0x81 and <= 0xFE)
        {
            _leading = (byte)value;
            return HandlerResult.Continue;
        }

        return HandlerResult.Error;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#replacement-decoder</summary>
internal sealed class ReplacementDecoder(bool fatal) : WebDecoder(fatal)
{
    private bool _errorReturned;

    protected override void ResetState() => _errorReturned = false;

    protected override HandlerResult Handle(int value, out int codePoint1, out int codePoint2)
    {
        codePoint1 = 0;
        codePoint2 = 0;
        if (value == EndOfQueue)
            return HandlerResult.Finished;

        if (!_errorReturned)
        {
            _errorReturned = true;
            return HandlerResult.Error;
        }

        return HandlerResult.Finished;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#shared-utf-16-decoder</summary>
internal sealed class Utf16Decoder(bool fatal, bool bigEndian) : WebDecoder(fatal)
{
    private int _leadingByte = -1;
    private int _leadingSurrogate = -1;

    protected override void ResetState()
    {
        _leadingByte = -1;
        _leadingSurrogate = -1;
    }

    protected override bool TryDecodeFast(ReadOnlySpan<byte> input, Span<char> output, out int bytesConsumed, out int charsWritten)
    {
        bytesConsumed = 0;
        charsWritten = 0;
        if (_leadingByte >= 0 || _leadingSurrogate >= 0 || HasRestoredBytes)
            return false;

        // Copy code units that are not surrogates; surrogates go through the handler.
        var count = Math.Min(input.Length / 2, output.Length);
        var i = 0;
        for (; i < count; i++)
        {
            var codeUnit = bigEndian ? (input[i * 2] << 8) | input[(i * 2) + 1] : (input[(i * 2) + 1] << 8) | input[i * 2];
            if (codeUnit is >= 0xD800 and <= 0xDFFF)
                break;

            output[i] = (char)codeUnit;
        }

        bytesConsumed = i * 2;
        charsWritten = i;
        return i > 0;
    }

    protected override HandlerResult Handle(int value, out int codePoint1, out int codePoint2)
    {
        codePoint1 = 0;
        codePoint2 = 0;
        if (value == EndOfQueue)
        {
            if (_leadingByte >= 0 || _leadingSurrogate >= 0)
            {
                _leadingByte = -1;
                _leadingSurrogate = -1;
                return HandlerResult.Error;
            }

            return HandlerResult.Finished;
        }

        if (_leadingByte < 0)
        {
            _leadingByte = value;
            return HandlerResult.Continue;
        }

        var codeUnit = bigEndian ? (_leadingByte << 8) + value : (value << 8) + _leadingByte;
        _leadingByte = -1;

        if (_leadingSurrogate >= 0)
        {
            var leadingSurrogate = _leadingSurrogate;
            _leadingSurrogate = -1;
            if (codeUnit is >= 0xDC00 and <= 0xDFFF)
            {
                codePoint1 = 0x10000 + ((leadingSurrogate - 0xD800) << 10) + (codeUnit - 0xDC00);
                return HandlerResult.CodePoint;
            }

            var byte1 = (byte)(codeUnit >> 8);
            var byte2 = (byte)(codeUnit & 0xFF);
            if (bigEndian)
            {
                Restore(byte1, byte2);
            }
            else
            {
                Restore(byte2, byte1);
            }

            return HandlerResult.Error;
        }

        if (codeUnit is >= 0xD800 and <= 0xDBFF)
        {
            _leadingSurrogate = codeUnit;
            return HandlerResult.Continue;
        }

        if (codeUnit is >= 0xDC00 and <= 0xDFFF)
            return HandlerResult.Error;

        codePoint1 = codeUnit;
        return HandlerResult.CodePoint;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#x-user-defined-decoder</summary>
internal sealed class XUserDefinedDecoder(bool fatal) : WebDecoder(fatal)
{
    protected override void ResetState()
    {
    }

    protected override bool TryDecodeFast(ReadOnlySpan<byte> input, Span<char> output, out int bytesConsumed, out int charsWritten)
    {
        var length = Math.Min(input.Length, output.Length);
        for (var i = 0; i < length; i++)
        {
            var value = input[i];
            output[i] = value < 0x80 ? (char)value : (char)(0xF780 + value - 0x80);
        }

        bytesConsumed = length;
        charsWritten = length;
        return length > 0;
    }

    protected override HandlerResult Handle(int value, out int codePoint1, out int codePoint2)
    {
        codePoint2 = 0;
        codePoint1 = 0;
        if (value == EndOfQueue)
            return HandlerResult.Finished;

        codePoint1 = value < 0x80 ? value : 0xF780 + value - 0x80;
        return HandlerResult.CodePoint;
    }
}

internal static class AsciiFastPath
{
    /// <summary>Decodes the leading ASCII bytes. Used by multi-byte decoders whose ASCII bytes map to themselves in their initial state.</summary>
    public static bool TryDecode(bool isInitialState, ReadOnlySpan<byte> input, Span<char> output, out int bytesConsumed, out int charsWritten)
    {
        if (!isInitialState)
        {
            bytesConsumed = 0;
            charsWritten = 0;
            return false;
        }

        var length = Math.Min(input.Length, output.Length);
        var status = Ascii.ToUtf16(input[..length], output, out charsWritten);
        _ = status;
        bytesConsumed = charsWritten;
        return charsWritten > 0;
    }
}

internal static class EncodingIndexHelpers
{
    /// <summary>Returns the index code point for pointer in index, or 0 when the pointer is not in the index.</summary>
    public static int IndexCodePoint(ReadOnlySpan<ushort> index, int pointer) => (uint)pointer < (uint)index.Length ? index[pointer] : 0;

    /// <summary>Returns the index code point for pointer in index, or 0 when the pointer is not in the index.</summary>
    public static int IndexCodePoint(ReadOnlySpan<int> index, int pointer) => (uint)pointer < (uint)index.Length ? index[pointer] : 0;

    /// <summary>
    /// https://encoding.spec.whatwg.org/#index-gb18030-ranges-code-point. Returns -1 for null.
    /// </summary>
    public static int Gb18030RangesCodePoint(int pointer)
    {
        if ((pointer > 39419 && pointer < 189000) || pointer > 1237575)
            return -1;

        if (pointer == 7457)
            return 0xE7C7;

        var pointers = EncodingIndexes.Gb18030RangesPointers;
        var index = pointers.BinarySearch(pointer);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return EncodingIndexes.Gb18030RangesCodePoints[index] + pointer - pointers[index];
    }

    /// <summary>
    /// https://encoding.spec.whatwg.org/#index-gb18030-ranges-pointer
    /// </summary>
    public static int Gb18030RangesPointer(int codePoint)
    {
        if (codePoint == 0xE7C7)
            return 7457;

        var codePoints = EncodingIndexes.Gb18030RangesCodePoints;
        var index = codePoints.BinarySearch(codePoint);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return EncodingIndexes.Gb18030RangesPointers[index] + codePoint - codePoints[index];
    }
}
