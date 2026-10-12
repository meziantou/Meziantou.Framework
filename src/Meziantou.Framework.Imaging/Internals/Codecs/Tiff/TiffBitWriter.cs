namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the bits of a TIFF strip most significant bit first, the order of LZW codes and of CCITT code words, into an
/// <see cref="ImageOutputBuffer"/>.
/// </summary>
internal ref struct TiffBitWriter
{
    private const int ChunkLength = 4096;

    private readonly ImageOutputBuffer _output;
    private Span<byte> _span;
    private int _used;
    private ulong _bits;
    private int _count;

    public TiffBitWriter(ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        _span = output.GetSpan(ChunkLength);
        _used = 0;
        _bits = 0;
        _count = 0;
    }

    /// <summary>Appends bits.</summary>
    /// <param name="value">The bits, in the least significant positions; the most significant of them is written first.</param>
    /// <param name="length">The number of bits, from 1 to 32.</param>
    public void Write(uint value, int length)
    {
        _bits = (_bits << length) | value;
        _count += length;
        while (_count >= 8)
        {
            _count -= 8;
            if (_used == _span.Length)
            {
                _output.Advance(_used);
                _span = _output.GetSpan(ChunkLength);
                _used = 0;
            }

            _span[_used++] = (byte)(_bits >> _count);
        }
    }

    /// <summary>Completes the current byte with zero bits, unless it has no bit yet.</summary>
    public void AlignToByte()
    {
        if (_count > 0)
        {
            Write(0, 8 - _count);
        }
    }

    /// <summary>Completes the current byte with zero bits and hands every byte to the output.</summary>
    public void Flush()
    {
        AlignToByte();
        _output.Advance(_used);
        _span = default;
        _used = 0;
    }
}
