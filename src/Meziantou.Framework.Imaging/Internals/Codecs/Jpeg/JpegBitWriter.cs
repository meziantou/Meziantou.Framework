using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bits of the entropy-coded data of a JPEG scan (ITU-T T.81, section F.1.2): most significant bit first, a zero byte
/// stuffed after each byte 0xFF. The bytes are written to a span the caller provides; the bits of an incomplete byte stay
/// in the writer.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct JpegBitWriter
{
    private ulong _bitBuffer;
    private int _bitCount;

    /// <summary>Gets the number of bits of the incomplete byte: 0 to 7.</summary>
    public readonly int PendingBitCount => _bitCount;

    /// <summary>Appends bits, emitting whole bytes with a stuffed zero after each 0xFF.</summary>
    /// <param name="destination">Receives the bytes: two for each eight bits, at most.</param>
    /// <param name="position">The position in <paramref name="destination"/>, advanced.</param>
    /// <param name="bits">The bits, in the low bits of the value.</param>
    /// <param name="length">The number of bits, at most 32.</param>
    public void WriteBits(Span<byte> destination, ref int position, uint bits, int length)
    {
        _bitBuffer = (_bitBuffer << length) | bits;
        _bitCount += length;
        while (_bitCount >= 8)
        {
            _bitCount -= 8;
            var value = (byte)(_bitBuffer >> _bitCount);
            destination[position++] = value;
            if (value == 0xFF)
            {
                destination[position++] = 0;
            }
        }
    }

    /// <summary>Writes the code of a symbol.</summary>
    /// <exception cref="InvalidOperationException">The table has no code for the symbol.</exception>
    public void WriteSymbol(Span<byte> destination, ref int position, JpegHuffmanEncoder table, int symbol)
    {
        var length = table.GetLength(symbol);
        if (length == 0)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The Huffman table has no code for the symbol 0x{symbol:X2}."));

        WriteBits(destination, ref position, table.GetCode(symbol), length);
    }

    /// <summary>Writes the symbol <c>(symbolHigh &lt;&lt; 4) | SSSS</c> for a value, then its SSSS additional bits (negative values as <c>value - 1</c>).</summary>
    public void WriteValue(Span<byte> destination, ref int position, JpegHuffmanEncoder table, int symbolHigh, int value)
    {
        var magnitude = (uint)Math.Abs(value);
        var size = magnitude == 0 ? 0 : 32 - BitOperations.LeadingZeroCount(magnitude);
        WriteSymbol(destination, ref position, table, (symbolHigh << 4) | size);
        if (size > 0)
        {
            var bits = value < 0 ? (uint)(value - 1) & ((1u << size) - 1) : (uint)value;
            WriteBits(destination, ref position, bits, size);
        }
    }

    /// <summary>Forgets the bits of the incomplete byte: the start of a scan.</summary>
    public void Reset()
    {
        _bitBuffer = 0;
        _bitCount = 0;
    }
}
