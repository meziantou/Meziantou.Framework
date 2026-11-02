using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>
/// Encodes a bilevel image as the embedded stream of the <c>JBIG2Decode</c> filter (ISO 32000-2, 7.4.7; ITU-T T.88): a
/// page information segment and one immediate generic region, coded with the arithmetic coder (template 0, its default
/// adaptive pixels, and typical prediction). The coding is lossless: the decoded image is the image.
/// </summary>
/// <remarks>
/// The file header, the end-of-page and end-of-file segments are not written: PDF does not use them (7.4.7). There are no
/// global segments.
/// </remarks>
internal static class Jbig2Encoder
{
    // The context of the bit that toggles typical prediction for template 0 (6.2.5.7)
    private const int TypicalPredictionContext = 0x9B25;

    /// <summary>Encodes an image.</summary>
    /// <param name="rows">The rows: one bit per pixel, most significant bit first, each row on whole bytes; 1 is black.</param>
    /// <param name="width">The number of pixels of a row.</param>
    /// <param name="height">The number of rows.</param>
    public static byte[] Encode(ReadOnlySpan<byte> rows, int width, int height)
    {
        var stride = (width + 7) / 8;

        // The rows without the bits after the last pixel, which typical prediction compares
        var bitmap = rows[..(stride * height)].ToArray();
        if ((width & 7) != 0)
        {
            var mask = (byte)(0xFF << (8 - (width & 7)));
            for (var y = 0; y < height; y++)
            {
                bitmap[(y * stride) + stride - 1] &= mask;
            }
        }

        var encoder = new Jbig2ArithmeticEncoder(Math.Max(64, bitmap.Length / 16));
        var contexts = new byte[1 << 16];
        var typical = 0;
        for (var y = 0; y < height; y++)
        {
            // Typical prediction (6.2.5.7): a row that is the row above (white for the first row) is one decision
            var row = bitmap.AsSpan(y * stride, stride);
            var same = y > 0 ? row.SequenceEqual(bitmap.AsSpan((y - 1) * stride, stride)) : !row.ContainsAnyExcept((byte)0);
            var predicted = same ? 1 : 0;
            encoder.Encode(predicted ^ typical, ref contexts[TypicalPredictionContext]);
            typical = predicted;
            if (same)
                continue;

            EncodeRow(ref encoder, contexts, bitmap, y, width, stride);
        }

        var coded = encoder.Flush();
        var output = new List<byte>(coded.Length + 64);

        // The page information segment (7.4.8): the size, no resolution, eventually lossless, white by default, OR
        WriteSegmentHeader(output, number: 0, type: 48, dataLength: 19);
        WriteUInt32(output, (uint)width);
        WriteUInt32(output, (uint)height);
        WriteUInt32(output, 0);
        WriteUInt32(output, 0);
        output.Add(0x01);
        output.Add(0);
        output.Add(0);

        // The immediate generic region segment (7.4.6): the region information of the page, then the template, the
        // typical prediction and the default adaptive pixels of template 0 (6.2.5.3)
        WriteSegmentHeader(output, number: 1, type: 38, dataLength: 17 + 1 + 8 + (uint)coded.Length);
        WriteUInt32(output, (uint)width);
        WriteUInt32(output, (uint)height);
        WriteUInt32(output, 0);
        WriteUInt32(output, 0);
        output.Add(0);
        output.Add(0x08);
        foreach (var (x, y) in Jbig2GenericRegion.GetDefaultAdaptivePixels(template: 0))
        {
            output.Add((byte)(sbyte)x);
            output.Add((byte)(sbyte)y);
        }

        output.AddRange(coded);
        return [.. output];
    }

    /// <summary>
    /// Encodes the pixels of a row with the contexts of template 0 (6.2.5.3, figure 3): the pixels at fixed positions of
    /// the two rows above slide in two windows, as in <see cref="Jbig2GenericRegion"/>; the default adaptive pixels are in them.
    /// </summary>
    private static void EncodeRow(ref Jbig2ArithmeticEncoder encoder, byte[] contexts, byte[] data, int y, int width, int stride)
    {
        var row = y * stride;
        var row1 = y >= 1 ? (y - 1) * stride : -1;
        var row2 = y >= 2 ? (y - 2) * stride : -1;
        var window1 = 0;
        var window2 = 0;
        for (var dx = -3; dx <= 3; dx++)
        {
            window1 = (window1 << 1) | Bit(data, row1, dx, width);
        }

        for (var dx = -2; dx <= 2; dx++)
        {
            window2 = (window2 << 1) | Bit(data, row2, dx, width);
        }

        var current = 0;
        for (var x = 0; x < width; x++)
        {
            var context = current | (window1 << 4) | (window2 << 11);
            var value = Bit(data, row, x, width);
            encoder.Encode(value, ref contexts[context]);
            current = ((current << 1) | value) & 0xF;
            window1 = ((window1 << 1) | Bit(data, row1, x + 4, width)) & 0x7F;
            window2 = ((window2 << 1) | Bit(data, row2, x + 3, width)) & 0x1F;
        }
    }

    private static int Bit(byte[] data, int row, int x, int width) => row >= 0 && (uint)x < (uint)width ? (data[row + (x >> 3)] >> (7 - (x & 7))) & 1 : 0;

    /// <summary>Writes the header of a segment (7.2): its number, its type, no referred-to segment, page 1, the length of its data.</summary>
    private static void WriteSegmentHeader(List<byte> output, uint number, int type, uint dataLength)
    {
        WriteUInt32(output, number);
        output.Add((byte)type);
        output.Add(0);
        output.Add(1);
        WriteUInt32(output, dataLength);
    }

    private static void WriteUInt32(List<byte> output, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        output.AddRange(bytes);
    }

    /// <summary>
    /// The arithmetic encoder of T.88, Annex E (the MQ encoder of T.800, Annex C): INITENC, ENCODE (CODEMPS and CODELPS),
    /// RENORME, BYTEOUT and FLUSH. A context is one byte, as in <see cref="Jbig2ArithmeticDecoder"/>.
    /// </summary>
    private struct Jbig2ArithmeticEncoder(int capacity)
    {
        private readonly uint[] _transitions = Jpeg2000MqTables.ContextTransitions;
        private byte[] _buffer = new byte[capacity];
        private int _length;
        private uint _c;
        private uint _a = 0x8000;
        private int _ct = 12;

        public void Encode(int decision, ref byte context)
        {
            var current = context;
            var transitions = _transitions[current];
            var probability = transitions & 0xFFFF;
            var a = _a - probability;
            if (decision == (current & 1))
            {
                // CODEMPS
                if ((a & 0x8000) != 0)
                {
                    _a = a;
                    _c += probability;
                    return;
                }

                if (a < probability)
                {
                    a = probability;
                }
                else
                {
                    _c += probability;
                }

                context = (byte)(transitions >> 16);
            }
            else
            {
                // CODELPS
                if (a < probability)
                {
                    _c += probability;
                }
                else
                {
                    a = probability;
                }

                context = (byte)(transitions >> 24);
            }

            // RENORME: a shift of n bits equals n shifts of one bit, a byte being written whenever the count reaches zero
            var shift = BitOperations.LeadingZeroCount(a) - 16;
            while (shift > 0)
            {
                var count = Math.Min(shift, _ct);
                a <<= count;
                _c <<= count;
                _ct -= count;
                shift -= count;
                if (_ct == 0)
                {
                    ByteOut();
                }
            }

            _a = a;
        }

        /// <summary>
        /// Terminates the coded data (FLUSH), and ends it with the marker FF AC (T.88, E.2.9 and 7.2.7): from the marker,
        /// a decoder reads 1 bits. Some decoders read 0 bits past the end of the data instead.
        /// </summary>
        public byte[] Flush()
        {
            // SETBITS: as many 1 bits as possible in the code register, within the current interval
            var limit = _c + _a;
            _c |= 0xFFFF;
            if (_c >= limit)
            {
                _c -= 0x8000;
            }

            _c <<= _ct;
            ByteOut();
            _c <<= _ct;
            ByteOut();
            if (_length > 0 && _buffer[_length - 1] == 0xFF)
            {
                // The FF byte would be followed by the marker: it is 1 bits too
                _length--;
            }

            Append(0xFF);
            Append(0xAC);
            return _buffer.AsSpan(0, _length).ToArray();
        }

        // BYTEOUT: writes the next byte of the code register; the byte "before the first one" does not exist, no carry
        // reaches it because the code register starts at zero
        private void ByteOut()
        {
            var last = _length > 0 ? _buffer[_length - 1] : 0;
            if (last == 0xFF)
            {
                // The byte that follows an FF byte holds seven bits
                Append((byte)(_c >> 20));
                _c &= 0xFFFFF;
                _ct = 7;
                return;
            }

            if (_c >= 0x8000000 && _length > 0)
            {
                // The carry goes into the last byte
                _buffer[_length - 1] = (byte)++last;
                _c &= 0x7FFFFFF;
                if (last == 0xFF)
                {
                    Append((byte)(_c >> 20));
                    _c &= 0xFFFFF;
                    _ct = 7;
                    return;
                }
            }

            Append((byte)(_c >> 19));
            _c &= 0x7FFFF;
            _ct = 8;
        }

        private void Append(byte value)
        {
            if (_length == _buffer.Length)
            {
                Array.Resize(ref _buffer, checked(_buffer.Length * 2));
            }

            _buffer[_length++] = value;
        }
    }
}
