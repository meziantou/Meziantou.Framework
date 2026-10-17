using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bit accumulator of an entropy-coded JPEG segment (ITU-T T.81 section F.1.2.3): bits are appended most significant
/// first, whole bytes are emitted with a stuffed zero byte after each <c>0xFF</c>, and the last byte is completed with 1-bits.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct JpegBitWriter
{
    private ulong _buffer;
    private int _count;

    /// <summary>Gets the number of bits of the incomplete byte: 0 to 7.</summary>
    public readonly int PendingBitCount => _count;

    /// <summary>Appends bits, emitting the whole bytes.</summary>
    /// <param name="destination">The destination; two bytes per completed byte must be available.</param>
    /// <param name="position">The write position in <paramref name="destination"/>, advanced.</param>
    /// <param name="bits">The bits, right-aligned.</param>
    /// <param name="length">The number of bits, 0 to 32.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(Span<byte> destination, ref int position, uint bits, int length)
    {
        _buffer = (_buffer << length) | bits;
        _count += length;
        if (_count >= 8)
        {
            WriteBytes(destination, ref position);
        }
    }

    private void WriteBytes(Span<byte> destination, ref int position)
    {
        while (_count >= 8)
        {
            _count -= 8;
            var value = (byte)(_buffer >> _count);
            destination[position++] = value;
            if (value == 0xFF)
            {
                destination[position++] = 0;
            }
        }
    }

    /// <summary>Writes the code of a symbol, then <paramref name="length"/> additional bits, as one <see cref="Write"/>.</summary>
    /// <exception cref="InvalidOperationException">The table has no code for the symbol.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSymbolAndBits(Span<byte> destination, ref int position, JpegHuffmanEncoder table, int symbol, uint bits, int length)
    {
        var codeLength = table.GetLength(symbol);
        if (codeLength == 0)
            ThrowMissingCode(symbol);

        // At most 16 + 16 bits
        Write(destination, ref position, (table.GetCode(symbol) << length) | bits, codeLength + length);
    }

    private static void ThrowMissingCode(int symbol) => throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The Huffman table has no code for the symbol 0x{symbol:X2}."));

    /// <summary>Writes the code of a symbol.</summary>
    /// <exception cref="InvalidOperationException">The table has no code for the symbol.</exception>
    public void WriteSymbol(Span<byte> destination, ref int position, JpegHuffmanEncoder table, int symbol)
    {
        var length = table.GetLength(symbol);
        if (length == 0)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The Huffman table has no code for the symbol 0x{symbol:X2}."));

        Write(destination, ref position, table.GetCode(symbol), length);
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
            Write(destination, ref position, bits, size);
        }
    }

    /// <summary>Forgets the bits of the incomplete byte: the start of a scan whose previous one was not completed here.</summary>
    public void Reset()
    {
        _buffer = 0;
        _count = 0;
    }

    /// <summary>Completes the last byte with 1-bits at the end of an entropy-coded segment.</summary>
    /// <param name="destination">The destination; two bytes must be available.</param>
    /// <param name="position">The write position in <paramref name="destination"/>, advanced.</param>
    public void Pad(Span<byte> destination, ref int position)
    {
        if (_count > 0)
        {
            var padding = 8 - _count;
            Write(destination, ref position, (1u << padding) - 1, padding);
        }
    }
}
