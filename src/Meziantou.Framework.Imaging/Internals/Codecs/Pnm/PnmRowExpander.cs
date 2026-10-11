using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Expands one stored Netpbm row to the decoded representation: <c>MAXVAL</c> normalization, PBM bit inversion and the
/// gray-to-RGBA expansion of alpha tuples.
/// </summary>
/// <remarks>
/// Values up to <c>MAXVAL</c> 4,096 are normalized through a precomputed table; larger maximums are computed per sample.
/// A sample greater than <c>MAXVAL</c> is malformed and rejected instead of being clamped.
/// </remarks>
internal sealed class PnmRowExpander
{
    private const int MaxTableSize = 4096;

    private readonly PnmImageHeader _header;
    private readonly ushort[]? _scale;

    public PnmRowExpander(PnmImageHeader header)
    {
        _header = header;
        var max = header.MaxValue;
        var target = header.TargetMaxValue;
        if (max != target && max <= MaxTableSize)
        {
            _scale = new ushort[max + 1];
            for (var value = 0; value <= max; value++)
            {
                _scale[value] = (ushort)((((long)value * target) + (max / 2)) / max);
            }
        }
    }

    /// <summary>Expands one stored row.</summary>
    /// <param name="stored">Exactly <see cref="PnmImageHeader.StoredRowLength"/> bytes.</param>
    /// <param name="destination">Exactly one row of <see cref="PnmImageHeader.PixelFormat"/> pixels.</param>
    /// <exception cref="InvalidImageContentException">A sample is greater than <c>MAXVAL</c>.</exception>
    public void ExpandRow(ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        var header = _header;
        var width = header.Width;
        if (header.IsBitmap)
        {
            // A set bit is black; the bits past the width are padding
            for (var x = 0; x < width; x++)
            {
                destination[x] = (stored[x >> 3] & (0x80 >> (x & 7))) != 0 ? (byte)0 : byte.MaxValue;
            }

            return;
        }

        if (header.Is16Bit)
        {
            Expand16(stored, unsafe(MemoryMarshal.Cast<byte, ushort>(destination)));
            return;
        }

        Expand8(stored, destination);
    }

    private void Expand8(ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        var header = _header;
        var width = header.Width;
        var max = header.MaxValue;
        if (max == 255 && header.TupleType is not (PnmTupleType.BlackAndWhiteAlpha or PnmTupleType.GrayscaleAlpha))
        {
            // Gray, RGB and RGBA tuples with MAXVAL 255 are already the decoded layout
            stored[..destination.Length].CopyTo(destination);
            return;
        }

        switch (header.TupleType)
        {
            case PnmTupleType.BlackAndWhite:
            case PnmTupleType.Grayscale:
                for (var x = 0; x < width; x++)
                {
                    destination[x] = (byte)Scale(stored[x], max, identity: false);
                }

                break;

            case PnmTupleType.BlackAndWhiteAlpha:
            case PnmTupleType.GrayscaleAlpha:
            {
                var identity = max == 255;
                for (var x = 0; x < width; x++)
                {
                    var gray = (byte)Scale(stored[x * 2], max, identity);
                    var offset = x * 4;
                    destination[offset] = gray;
                    destination[offset + 1] = gray;
                    destination[offset + 2] = gray;
                    destination[offset + 3] = (byte)Scale(stored[(x * 2) + 1], max, identity);
                }

                break;
            }

            default:
                for (var i = 0; i < destination.Length; i++)
                {
                    destination[i] = (byte)Scale(stored[i], max, identity: false);
                }

                break;
        }
    }

    private void Expand16(ReadOnlySpan<byte> stored, Span<ushort> destination)
    {
        var header = _header;
        var width = header.Width;
        var max = header.MaxValue;
        var identity = max == 65535;
        switch (header.TupleType)
        {
            case PnmTupleType.BlackAndWhite:
            case PnmTupleType.Grayscale:
                for (var x = 0; x < width; x++)
                {
                    destination[x] = Scale(ReadSample(stored, x), max, identity);
                }

                break;

            case PnmTupleType.BlackAndWhiteAlpha:
            case PnmTupleType.GrayscaleAlpha:
                for (var x = 0; x < width; x++)
                {
                    var gray = Scale(ReadSample(stored, x * 2), max, identity);
                    var offset = x * 4;
                    destination[offset] = gray;
                    destination[offset + 1] = gray;
                    destination[offset + 2] = gray;
                    destination[offset + 3] = Scale(ReadSample(stored, (x * 2) + 1), max, identity);
                }

                break;

            case PnmTupleType.Rgb:
                for (var x = 0; x < width; x++)
                {
                    var offset = x * 4;
                    destination[offset] = Scale(ReadSample(stored, x * 3), max, identity);
                    destination[offset + 1] = Scale(ReadSample(stored, (x * 3) + 1), max, identity);
                    destination[offset + 2] = Scale(ReadSample(stored, (x * 3) + 2), max, identity);
                    destination[offset + 3] = ushort.MaxValue;
                }

                break;

            default:
                for (var i = 0; i < destination.Length; i++)
                {
                    destination[i] = Scale(ReadSample(stored, i), max, identity);
                }

                break;
        }
    }

    private static ushort ReadSample(ReadOnlySpan<byte> stored, int index) => BinaryPrimitives.ReadUInt16BigEndian(stored[(index * 2)..]);

    private ushort Scale(int value, int max, bool identity)
    {
        if (value > max)
            throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The PNM sample {value} is greater than MAXVAL ({max})."));

        if (identity)
            return (ushort)value;

        if (_scale is { } table)
            return table[value];

        return (ushort)((((long)value * _header.TargetMaxValue) + (max / 2)) / max);
    }
}
