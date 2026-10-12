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

    /// <summary>Appends bits, emitting the whole bytes.</summary>
    /// <param name="destination">The destination; two bytes per completed byte must be available.</param>
    /// <param name="position">The write position in <paramref name="destination"/>, advanced.</param>
    /// <param name="bits">The bits, right-aligned.</param>
    /// <param name="length">The number of bits, 0 to 32.</param>
    public void Write(Span<byte> destination, ref int position, uint bits, int length)
    {
        _buffer = (_buffer << length) | bits;
        _count += length;
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
