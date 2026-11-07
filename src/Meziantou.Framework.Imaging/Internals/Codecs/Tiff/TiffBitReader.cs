// Compiled into Meziantou.Framework.Imaging and Meziantou.Framework.Pdf (CCITTFaxDecode, JBIG2), internal to each. The copy
// of Pdf is in its namespace, so that Pdf does not depend on the internals of Imaging.
#if MEZIANTOU_FRAMEWORK_PDF
namespace Meziantou.Framework.Pdf.Filters;
#else
namespace Meziantou.Framework.Imaging.Internals;
#endif

/// <summary>
/// Reads the bits of a TIFF strip or tile most significant bit first, the order of LZW codes and of CCITT code words.
/// </summary>
/// <remarks>
/// Reading past the end of the data never fails: the missing bits are zeros, and <see cref="IsPastEnd"/> tells the caller
/// that a code was completed with bits the data does not hold, so that a truncated block is reported instead of decoded.
/// </remarks>
internal ref struct TiffBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private long _position;

    public TiffBitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>Gets or sets the position of the next bit, from the first bit of the data.</summary>
    public long Position
    {
        readonly get => _position;
        set => _position = value;
    }

    /// <summary>Gets a value indicating whether bits were consumed beyond the last bit of the data.</summary>
    public readonly bool IsPastEnd => _position > (long)_data.Length * 8;

    /// <summary>Gets a value indicating whether every bit of the data was consumed.</summary>
    public readonly bool IsAtEnd => _position >= (long)_data.Length * 8;

    /// <summary>Returns the next bits without consuming them.</summary>
    /// <param name="count">The number of bits, from 1 to 24.</param>
    /// <returns>The bits, the first one in the most significant position; bits past the end of the data are zeros.</returns>
    public readonly uint Peek(int count)
    {
        var index = _position >> 3;
        var shift = (int)(_position & 7);
        uint window = 0;
        for (var i = 0; i < 4; i++)
        {
            window <<= 8;
            if (index + i < _data.Length)
            {
                window |= _data[(int)(index + i)];
            }
        }

        return (window << shift) >> (32 - count);
    }

    /// <summary>Consumes bits.</summary>
    /// <param name="count">The number of bits.</param>
    public void Skip(int count) => _position += count;

    /// <summary>Reads the next bits.</summary>
    /// <param name="count">The number of bits, from 1 to 24.</param>
    /// <returns>The bits, the first one in the most significant position.</returns>
    public uint Read(int count)
    {
        var value = Peek(count);
        _position += count;
        return value;
    }

    /// <summary>Moves to the first bit of the next byte, unless the position is already the first bit of a byte.</summary>
    public void AlignToByte() => _position = (_position + 7) & ~7L;
}
