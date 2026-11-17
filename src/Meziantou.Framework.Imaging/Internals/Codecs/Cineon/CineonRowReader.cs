using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the samples of a stored line of a pixel-interleaved Cineon image (field 1.29, note 1) and writes the pixels of the
/// line in the source layout of the image.
/// </summary>
/// <remarks>
/// <para>
/// The fields (samples) of a line are packed into cells of 8, 16 or 32 bits stored in the byte order of the file, the first
/// field of a cell in its most significant bits when the cell is left-justified, the last one in its least significant bits
/// when it is right-justified. Without the high bit of the packing field, a pixel of several channels starts a new cell and
/// a cell holds the fields of one pixel; with it (or for one channel), the fields of consecutive pixels share the cells.
/// Every line starts on a new cell. 8 and 16-bit samples are a stream of bytes and of 16-bit words, whatever the cells, as
/// ImageMagick and OpenImageIO read them (not one pixel per cell); the line still ends on a cell boundary, as ImageMagick
/// writes it.
/// </para>
/// <para>
/// Samples of up to 8 bits are scaled to 8-bit samples, deeper ones to 16-bit samples:
/// <c>(v × max' + max / 2) / max</c>. The channels of an RGB image are placed by their designators when they are the red,
/// green and blue of one metric, and in the order of the file otherwise.
/// </para>
/// </remarks>
internal sealed class CineonRowReader
{
    private readonly bool _bigEndian;
    private readonly int _channels;
    private readonly int _bits;
    private readonly int _width;
    private readonly int _cellBits;
    private readonly bool _leftJustified;
    private readonly bool _streamCells;
    private readonly int _fieldsPerCell;
    private readonly int[] _channelOrder;

    public CineonRowReader(bool bigEndian, int channels, int bits, int width, byte packing, int[] channelOrder)
    {
        _bigEndian = bigEndian;
        _channels = channels;
        _bits = bits;
        _width = width;
        _channelOrder = channelOrder;
        var code = packing & 0x7F;
        if (bits is 8 or 16)
        {
            _cellBits = bits;
            _leftJustified = true;
            _streamCells = true;
            _fieldsPerCell = 1;
        }
        else
        {
            _cellBits = code switch
            {
                1 or 2 => 8,
                3 or 4 => 16,
                5 or 6 => 32,
                _ => throw new ArgumentOutOfRangeException(nameof(packing), packing, "The Cineon packing has no cells."),
            };
            _leftJustified = (code & 1) == 1;
            _streamCells = channels == 1 || (packing & 0x80) != 0;
            _fieldsPerCell = _cellBits / bits;
            if (!_streamCells)
            {
                _fieldsPerCell = Math.Min(_fieldsPerCell, channels);
            }
        }

        var cellBytes = _cellBits / 8;
        long cells = _streamCells
            ? (((long)width * channels) + _fieldsPerCell - 1) / _fieldsPerCell
            : (long)width * ((channels + _fieldsPerCell - 1) / _fieldsPerCell);
        StoredLineLength = cells * cellBytes;
        if (bits is 8 or 16)
        {
            // A stream of samples still ends its line on a cell boundary, as ImageMagick writes it
            var lineCell = (code switch
            {
                3 or 4 => 16,
                5 or 6 => 32,
                _ => 8,
            }) / 8;
            StoredLineLength = (StoredLineLength + lineCell - 1) / lineCell * lineCell;
        }
        SourcePixelFormat = PixelFormatWidening.GetWidenedFormat(DefaultPixelFormats.ForCineon(channels == 1, bits));
    }

    /// <summary>Gets the number of bytes of a stored line, without its end-of-line padding.</summary>
    public long StoredLineLength { get; }

    /// <summary>Gets the number of samples of a line.</summary>
    public int SamplesPerLine => _width * _channels;

    /// <summary>Gets the layout of the rows the reader writes.</summary>
    public PixelFormat SourcePixelFormat { get; }

    /// <summary>Gets whether a packing code has cells the reader knows for a depth: bit fields (0) are only read for 8 and 16-bit samples, which they store as streams.</summary>
    public static bool IsSupported(byte packing, int bits)
    {
        var code = packing & 0x7F;
        if (bits is 8 or 16)
            return code <= 6;

        var cellBits = code switch
        {
            1 or 2 => 8,
            3 or 4 => 16,
            5 or 6 => 32,
            _ => 0,
        };

        return cellBits >= bits;
    }

    /// <summary>Reads a stored line into a row of <see cref="SourcePixelFormat"/>.</summary>
    /// <param name="stored">The stored line: <see cref="StoredLineLength"/> bytes.</param>
    /// <param name="destination">The row.</param>
    /// <param name="samples">A buffer of <see cref="SamplesPerLine"/> samples.</param>
    public void ReadRow(ReadOnlySpan<byte> stored, Span<byte> destination, Span<uint> samples)
    {
        samples = samples[..SamplesPerLine];
        ReadSamples(stored, samples);
        var max = (1u << _bits) - 1;
        if (_bits <= 8)
        {
            if (_channels == 1)
            {
                for (var x = 0; x < _width; x++)
                {
                    destination[x] = (byte)Scale(samples[x], max, 255);
                }

                return;
            }

            for (var x = 0; x < _width; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    destination[(x * 3) + c] = (byte)Scale(samples[(x * 3) + _channelOrder[c]], max, 255);
                }
            }

            return;
        }

        var wide = unsafe(MemoryMarshal.Cast<byte, ushort>(destination));
        if (_channels == 1)
        {
            for (var x = 0; x < _width; x++)
            {
                wide[x] = (ushort)Scale(samples[x], max, 65535);
            }

            return;
        }

        for (var x = 0; x < _width; x++)
        {
            for (var c = 0; c < 3; c++)
            {
                wide[(x * 4) + c] = (ushort)Scale(samples[(x * 3) + _channelOrder[c]], max, 65535);
            }

            wide[(x * 4) + 3] = ushort.MaxValue;
        }
    }

    /// <summary>Gets the stored samples of a line, as stored (not scaled), in the order of the file.</summary>
    internal void ReadSamples(ReadOnlySpan<byte> stored, Span<uint> samples)
    {
        if (_bits == 8)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = stored[i];
            }

            return;
        }

        if (_bits == 16)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = _bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(stored[(i * 2)..]) : BinaryPrimitives.ReadUInt16LittleEndian(stored[(i * 2)..]);
            }

            return;
        }

        var mask = (1u << _bits) - 1;
        var cellBytes = _cellBits / 8;
        var cell = 0;
        var i2 = 0;
        if (_streamCells)
        {
            while (i2 < samples.Length)
            {
                var count = Math.Min(_fieldsPerCell, samples.Length - i2);
                ReadCell(stored.Slice(cell * cellBytes, cellBytes), count, samples.Slice(i2, count), mask);
                i2 += count;
                cell++;
            }

            return;
        }

        for (var x = 0; x < _width; x++)
        {
            var remaining = _channels;
            while (remaining > 0)
            {
                var count = Math.Min(_fieldsPerCell, remaining);
                ReadCell(stored.Slice(cell * cellBytes, cellBytes), count, samples.Slice(i2, count), mask);
                i2 += count;
                remaining -= count;
                cell++;
            }
        }
    }

    private static uint Scale(uint value, uint max, uint outputMax) => max == outputMax ? value : (uint)(((value * (ulong)outputMax) + (max / 2)) / max);

    private void ReadCell(ReadOnlySpan<byte> cell, int count, Span<uint> fields, uint mask)
    {
        uint value = _cellBits switch
        {
            8 => cell[0],
            16 => _bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(cell) : BinaryPrimitives.ReadUInt16LittleEndian(cell),
            _ => _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(cell) : BinaryPrimitives.ReadUInt32LittleEndian(cell),
        };

        for (var j = 0; j < count; j++)
        {
            var shift = _leftJustified ? _cellBits - (_bits * (j + 1)) : _bits * (count - 1 - j);
            fields[j] = (value >> shift) & mask;
        }
    }
}
