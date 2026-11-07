using System.Buffers;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Encodings;

/// <summary>
/// Base class for the encoders of the Encoding Standard (https://encoding.spec.whatwg.org/#encoders).
/// </summary>
internal abstract class WebEncoder
{
    /// <summary>Maximum number of bytes produced for a single code point (ISO-2022-JP may emit an escape sequence followed by two bytes).</summary>
    public const int MaxBytesPerCodePoint = 8;

    /// <summary>
    /// Runs the encoder handler for a scalar value, including the processing of the code point when the handler "restores" it to the queue.
    /// Returns false when the code point cannot be encoded; bytes written before the error (ISO-2022-JP escape sequences) are still reported.
    /// </summary>
    /// <param name="codePoint">A scalar value.</param>
    /// <param name="output">Destination, at least <see cref="MaxBytesPerCodePoint"/> bytes.</param>
    /// <param name="bytesWritten">Number of bytes written to <paramref name="output"/>.</param>
    /// <param name="errorCodePoint">The code point reported by the error (U+FFFD for some ISO-2022-JP errors).</param>
    public bool Encode(int codePoint, Span<byte> output, out int bytesWritten, out int errorCodePoint)
    {
        bytesWritten = 0;
        while (true)
        {
            var result = Handle(codePoint, output[bytesWritten..], out var restored, out errorCodePoint);
            if (result < 0)
                return false;

            bytesWritten += result;
            if (!restored)
                return true;
        }
    }

    /// <summary>
    /// Runs the handler once. Returns the number of bytes written or -1 on error.
    /// <paramref name="restored"/> is true when the code point was restored to the queue and must be processed again.
    /// </summary>
    protected abstract int Handle(int codePoint, Span<byte> output, out bool restored, out int errorCodePoint);

    /// <summary>Handles end-of-queue. Returns the number of bytes written.</summary>
    public virtual int Flush(Span<byte> output) => 0;

    /// <summary>
    /// Implements "encode" with the "html" error mode: https://encoding.spec.whatwg.org/#encode
    /// Lone surrogates are converted to U+FFFD as the input must be scalar values.
    /// </summary>
    public void EncodeHtml(ReadOnlySpan<char> input, IBufferWriter<byte> output)
    {
        Span<byte> buffer = stackalloc byte[MaxBytesPerCodePoint];
        Span<byte> reference = stackalloc byte[16];
        foreach (var rune in input.EnumerateRunes())
        {
            var success = Encode(rune.Value, buffer, out var written, out var errorCodePoint);
            output.Write(buffer[..written]);
            if (!success)
            {
                // "&#" + decimal + ";" — the bytes are ASCII and valid in every encoder state.
                reference[0] = (byte)'&';
                reference[1] = (byte)'#';
                errorCodePoint.TryFormat(reference[2..], out var digits, provider: CultureInfo.InvariantCulture);
                reference[2 + digits] = (byte)';';
                output.Write(reference[..(3 + digits)]);
            }
        }

        var flushed = Flush(buffer);
        output.Write(buffer[..flushed]);
    }
}

/// <summary>https://encoding.spec.whatwg.org/#utf-8-encoder</summary>
internal sealed class Utf8Encoder : WebEncoder
{
    protected override int Handle(int codePoint, Span<byte> output, out bool restored, out int errorCodePoint)
    {
        restored = false;
        errorCodePoint = 0;
        return new Rune(codePoint).EncodeToUtf8(output);
    }
}

/// <summary>https://encoding.spec.whatwg.org/#single-byte-encoder</summary>
internal sealed class SingleByteEncoder(int indexOffset) : WebEncoder
{
    protected override int Handle(int codePoint, Span<byte> output, out bool restored, out int errorCodePoint)
    {
        restored = false;
        errorCodePoint = 0;
        if (codePoint < 0x80)
        {
            output[0] = (byte)codePoint;
            return 1;
        }

        var index = EncodingIndexes.SingleByte.Slice(indexOffset, 128);
        var pointer = index.IndexOf((ushort)codePoint);
        if (codePoint > 0xFFFF || pointer < 0)
        {
            errorCodePoint = codePoint;
            return -1;
        }

        output[0] = (byte)(pointer + 0x80);
        return 1;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#gb18030-encoder (also used for GBK)</summary>
internal sealed class Gb18030Encoder(bool isGbk) : WebEncoder
{
    protected override int Handle(int codePoint, Span<byte> output, out bool restored, out int errorCodePoint)
    {
        restored = false;
        errorCodePoint = 0;
        if (codePoint < 0x80)
        {
            output[0] = (byte)codePoint;
            return 1;
        }

        if (codePoint == 0xE5E5)
        {
            errorCodePoint = codePoint;
            return -1;
        }

        if (isGbk && codePoint == 0x20AC)
        {
            output[0] = 0x80;
            return 1;
        }

        var special = codePoint switch
        {
            0xE78D => 0xA6D9,
            0xE78E => 0xA6DA,
            0xE78F => 0xA6DB,
            0xE790 => 0xA6DC,
            0xE791 => 0xA6DD,
            0xE792 => 0xA6DE,
            0xE793 => 0xA6DF,
            0xE794 => 0xA6EC,
            0xE795 => 0xA6ED,
            0xE796 => 0xA6F3,
            0xE81E => 0xFE59,
            0xE826 => 0xFE61,
            0xE82B => 0xFE66,
            0xE82C => 0xFE67,
            0xE832 => 0xFE6D,
            0xE843 => 0xFE7E,
            0xE854 => 0xFE90,
            0xE864 => 0xFEA0,
            _ => 0,
        };
        if (special != 0)
        {
            output[0] = (byte)(special >> 8);
            output[1] = (byte)special;
            return 2;
        }

        var pointer = ReverseIndexes.Gb18030.GetPointer(codePoint);
        if (pointer >= 0)
        {
            var leading = (pointer / 190) + 0x81;
            var trailing = pointer % 190;
            var offset = trailing < 0x3F ? 0x40 : 0x41;
            output[0] = (byte)leading;
            output[1] = (byte)(trailing + offset);
            return 2;
        }

        if (isGbk)
        {
            errorCodePoint = codePoint;
            return -1;
        }

        pointer = EncodingIndexHelpers.Gb18030RangesPointer(codePoint);
        var byte1 = pointer / (10 * 126 * 10);
        pointer %= 10 * 126 * 10;
        var byte2 = pointer / (10 * 126);
        pointer %= 10 * 126;
        var byte3 = pointer / 10;
        var byte4 = pointer % 10;
        output[0] = (byte)(byte1 + 0x81);
        output[1] = (byte)(byte2 + 0x30);
        output[2] = (byte)(byte3 + 0x81);
        output[3] = (byte)(byte4 + 0x30);
        return 4;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#big5-encoder</summary>
internal sealed class Big5Encoder : WebEncoder
{
    protected override int Handle(int codePoint, Span<byte> output, out bool restored, out int errorCodePoint)
    {
        restored = false;
        errorCodePoint = 0;
        if (codePoint < 0x80)
        {
            output[0] = (byte)codePoint;
            return 1;
        }

        var pointer = ReverseIndexes.Big5.GetPointer(codePoint);
        if (pointer < 0)
        {
            errorCodePoint = codePoint;
            return -1;
        }

        var leading = (pointer / 157) + 0x81;
        var trailing = pointer % 157;
        var offset = trailing < 0x3F ? 0x40 : 0x62;
        output[0] = (byte)leading;
        output[1] = (byte)(trailing + offset);
        return 2;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#euc-jp-encoder</summary>
internal sealed class EucJPEncoder : WebEncoder
{
    protected override int Handle(int codePoint, Span<byte> output, out bool restored, out int errorCodePoint)
    {
        restored = false;
        errorCodePoint = 0;
        if (codePoint < 0x80)
        {
            output[0] = (byte)codePoint;
            return 1;
        }

        if (codePoint == 0x00A5)
        {
            output[0] = 0x5C;
            return 1;
        }

        if (codePoint == 0x203E)
        {
            output[0] = 0x7E;
            return 1;
        }

        if (codePoint is >= 0xFF61 and <= 0xFF9F)
        {
            output[0] = 0x8E;
            output[1] = (byte)(codePoint - 0xFF61 + 0xA1);
            return 2;
        }

        if (codePoint == 0x2212)
        {
            codePoint = 0xFF0D;
        }

        var pointer = ReverseIndexes.Jis0208.GetPointer(codePoint);
        if (pointer < 0)
        {
            errorCodePoint = codePoint;
            return -1;
        }

        output[0] = (byte)((pointer / 94) + 0xA1);
        output[1] = (byte)((pointer % 94) + 0xA1);
        return 2;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#iso-2022-jp-encoder</summary>
internal sealed class Iso2022JPEncoder : WebEncoder
{
    private enum State : byte
    {
        Ascii,
        Roman,
        Jis0208,
    }

    private State _state;

    public override int Flush(Span<byte> output)
    {
        if (_state != State.Ascii)
        {
            _state = State.Ascii;
            output[0] = 0x1B;
            output[1] = 0x28;
            output[2] = 0x42;
            return 3;
        }

        return 0;
    }

    protected override int Handle(int codePoint, Span<byte> output, out bool restored, out int errorCodePoint)
    {
        restored = false;
        errorCodePoint = 0;
        if (_state is State.Ascii or State.Roman && codePoint is 0x0E or 0x0F or 0x1B)
        {
            errorCodePoint = 0xFFFD;
            return -1;
        }

        if (_state == State.Ascii && codePoint < 0x80)
        {
            output[0] = (byte)codePoint;
            return 1;
        }

        if (_state == State.Roman && ((codePoint < 0x80 && codePoint is not 0x5C and not 0x7E) || codePoint is 0xA5 or 0x203E))
        {
            output[0] = codePoint switch
            {
                0xA5 => 0x5C,
                0x203E => 0x7E,
                _ => (byte)codePoint,
            };
            return 1;
        }

        if (codePoint < 0x80 && _state != State.Ascii)
        {
            restored = true;
            _state = State.Ascii;
            output[0] = 0x1B;
            output[1] = 0x28;
            output[2] = 0x42;
            return 3;
        }

        if (codePoint is 0xA5 or 0x203E && _state != State.Roman)
        {
            restored = true;
            _state = State.Roman;
            output[0] = 0x1B;
            output[1] = 0x28;
            output[2] = 0x4A;
            return 3;
        }

        if (codePoint == 0x2212)
        {
            codePoint = 0xFF0D;
        }

        if (codePoint is >= 0xFF61 and <= 0xFF9F)
        {
            codePoint = EncodingIndexes.Iso2022JPKatakana[codePoint - 0xFF61];
        }

        var pointer = ReverseIndexes.Jis0208.GetPointer(codePoint);
        if (pointer < 0)
        {
            if (_state == State.Jis0208)
            {
                restored = true;
                _state = State.Ascii;
                output[0] = 0x1B;
                output[1] = 0x28;
                output[2] = 0x42;
                return 3;
            }

            errorCodePoint = codePoint;
            return -1;
        }

        if (_state != State.Jis0208)
        {
            restored = true;
            _state = State.Jis0208;
            output[0] = 0x1B;
            output[1] = 0x24;
            output[2] = 0x42;
            return 3;
        }

        output[0] = (byte)((pointer / 94) + 0x21);
        output[1] = (byte)((pointer % 94) + 0x21);
        return 2;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#shift_jis-encoder</summary>
internal sealed class ShiftJisEncoder : WebEncoder
{
    protected override int Handle(int codePoint, Span<byte> output, out bool restored, out int errorCodePoint)
    {
        restored = false;
        errorCodePoint = 0;
        if (codePoint <= 0x80)
        {
            output[0] = (byte)codePoint;
            return 1;
        }

        if (codePoint == 0x00A5)
        {
            output[0] = 0x5C;
            return 1;
        }

        if (codePoint == 0x203E)
        {
            output[0] = 0x7E;
            return 1;
        }

        if (codePoint is >= 0xFF61 and <= 0xFF9F)
        {
            output[0] = (byte)(codePoint - 0xFF61 + 0xA1);
            return 1;
        }

        if (codePoint == 0x2212)
        {
            codePoint = 0xFF0D;
        }

        var pointer = ReverseIndexes.ShiftJis.GetPointer(codePoint);
        if (pointer < 0)
        {
            errorCodePoint = codePoint;
            return -1;
        }

        var leading = pointer / 188;
        var leadingOffset = leading < 0x1F ? 0x81 : 0xC1;
        var trailing = pointer % 188;
        var offset = trailing < 0x3F ? 0x40 : 0x41;
        output[0] = (byte)(leading + leadingOffset);
        output[1] = (byte)(trailing + offset);
        return 2;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#euc-kr-encoder</summary>
internal sealed class EucKREncoder : WebEncoder
{
    protected override int Handle(int codePoint, Span<byte> output, out bool restored, out int errorCodePoint)
    {
        restored = false;
        errorCodePoint = 0;
        if (codePoint < 0x80)
        {
            output[0] = (byte)codePoint;
            return 1;
        }

        var pointer = ReverseIndexes.EucKR.GetPointer(codePoint);
        if (pointer < 0)
        {
            errorCodePoint = codePoint;
            return -1;
        }

        output[0] = (byte)((pointer / 190) + 0x81);
        output[1] = (byte)((pointer % 190) + 0x41);
        return 2;
    }
}

/// <summary>https://encoding.spec.whatwg.org/#x-user-defined-encoder</summary>
internal sealed class XUserDefinedEncoder : WebEncoder
{
    protected override int Handle(int codePoint, Span<byte> output, out bool restored, out int errorCodePoint)
    {
        restored = false;
        errorCodePoint = 0;
        if (codePoint < 0x80)
        {
            output[0] = (byte)codePoint;
            return 1;
        }

        if (codePoint is >= 0xF780 and <= 0xF7FF)
        {
            output[0] = (byte)(codePoint - 0xF780 + 0x80);
            return 1;
        }

        errorCodePoint = codePoint;
        return -1;
    }
}

/// <summary>
/// Lazily built reverse lookups implementing "index pointer" and its variants.
/// Encoders are rarely used (URL query and form encoding for legacy documents), so tables are only built on demand.
/// </summary>
internal sealed class ReverseIndex
{
    private readonly Dictionary<int, int> _pointers;

    private ReverseIndex(Dictionary<int, int> pointers)
    {
        _pointers = pointers;
    }

    public int GetPointer(int codePoint) => _pointers.TryGetValue(codePoint, out var pointer) ? pointer : -1;

    /// <summary>"index pointer": the first pointer, unless the code point is in <paramref name="lastPointerCodePoints"/>.</summary>
    public static ReverseIndex Create(ReadOnlySpan<ushort> index, int minimumPointer, int[] lastPointerCodePoints)
    {
        var result = new Dictionary<int, int>(index.Length);
        for (var pointer = minimumPointer; pointer < index.Length; pointer++)
        {
            var codePoint = index[pointer];
            if (codePoint == 0)
                continue;

            if (!result.TryAdd(codePoint, pointer) && lastPointerCodePoints.Contains(codePoint))
            {
                result[codePoint] = pointer;
            }
        }

        return new ReverseIndex(result);
    }

    public static ReverseIndex Create(ReadOnlySpan<int> index, int minimumPointer, int[] lastPointerCodePoints)
    {
        var result = new Dictionary<int, int>(index.Length);
        for (var pointer = minimumPointer; pointer < index.Length; pointer++)
        {
            var codePoint = index[pointer];
            if (codePoint == 0)
                continue;

            if (!result.TryAdd(codePoint, pointer) && lastPointerCodePoints.Contains(codePoint))
            {
                result[codePoint] = pointer;
            }
        }

        return new ReverseIndex(result);
    }

    /// <summary>"index pointer" with a range of pointers excluded (used by "index Shift_JIS pointer").</summary>
    public static ReverseIndex CreateExcluding(ReadOnlySpan<ushort> index, int excludedStart, int excludedEnd)
    {
        var result = new Dictionary<int, int>(index.Length);
        for (var pointer = 0; pointer < index.Length; pointer++)
        {
            if (pointer >= excludedStart && pointer <= excludedEnd)
                continue;

            var codePoint = index[pointer];
            if (codePoint != 0)
            {
                result.TryAdd(codePoint, pointer);
            }
        }

        return new ReverseIndex(result);
    }
}

internal static class ReverseIndexes
{
    private static readonly Lazy<ReverseIndex> Big5Index = new(() => ReverseIndex.Create(EncodingIndexes.Big5, (0xA1 - 0x81) * 157, [0x2550, 0x255E, 0x2561, 0x256A, 0x5341, 0x5345]));
    private static readonly Lazy<ReverseIndex> Jis0208Index = new(() => ReverseIndex.Create(EncodingIndexes.Jis0208, 0, []));
    private static readonly Lazy<ReverseIndex> ShiftJisIndex = new(() => ReverseIndex.CreateExcluding(EncodingIndexes.Jis0208, 8272, 8835));
    private static readonly Lazy<ReverseIndex> EucKRIndex = new(() => ReverseIndex.Create(EncodingIndexes.EucKR, 0, []));

    private static readonly Lazy<ReverseIndex> Gb18030Index = new(() => ReverseIndex.Create(EncodingIndexes.Gb18030, 0, []));

    public static ReverseIndex Gb18030 => Gb18030Index.Value;

    /// <summary>"index Big5 pointer"</summary>
    public static ReverseIndex Big5 => Big5Index.Value;

    public static ReverseIndex Jis0208 => Jis0208Index.Value;

    /// <summary>"index Shift_JIS pointer"</summary>
    public static ReverseIndex ShiftJis => ShiftJisIndex.Value;

    public static ReverseIndex EucKR => EucKRIndex.Value;
}
