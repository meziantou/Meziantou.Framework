using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Maps rows of color keys to the palette indices of a <see cref="PaletteQuantizer"/>, with optional Floyd–Steinberg
/// dithering, for the encoders that reduce colors to a palette (GIF, indexed BMP).
/// </summary>
/// <remarks>
/// Dithering carries the error of each row to the next one, so the rows of a frame must be mapped in display order, from
/// the top, after a <see cref="Reset"/>. The error terms (two rows, one pixel of margin on each side, three channels) are
/// rented once from the writer scope as <see cref="AllocationKind.Temporary"/>.
/// </remarks>
internal sealed class PaletteRowMapper : IDisposable
{
    private readonly int _width;
    private readonly bool _dither;
    private PooledBuffer? _errors;

    /// <summary>Allocates the error terms of a dithering mapper.</summary>
    /// <param name="scope">The scope charged for them.</param>
    /// <param name="width">The number of pixels of a row.</param>
    /// <param name="dither"><see langword="true"/> to diffuse the quantization error; otherwise each pixel maps to its nearest palette entry.</param>
    /// <exception cref="ImageResourceLimitException">The memory exceeds the live-allocation limit.</exception>
    public PaletteRowMapper(AllocationScope scope, int width, bool dither)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _width = width;
        _dither = dither;
        if (dither)
        {
            var length = 2L * (width + 2L) * 3 * sizeof(int);
            if (length > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(scope.Limits);

            _errors = scope.Rent((int)length, AllocationKind.Temporary, clear: true);
        }
    }

    /// <summary>Forgets the diffused error: the next row mapped is the first row of a frame.</summary>
    public void Reset() => _errors?.Span.Clear();

    /// <summary>
    /// Maps a row of keys (rows in display order when dithering) to palette indices: transparent pixels to the transparent
    /// index, colors to their palette entry, or with Floyd–Steinberg dithering (when the palette is not exact) the nearest
    /// entry of the color plus the diffused error, the error of each pixel being spread 7/16 right, 3/16 below left,
    /// 5/16 below and 1/16 below right (left to right on every row; integer sixteenths, the sum rounded with
    /// <c>(e + 8) &gt;&gt; 4</c>; transparent pixels neither receive nor spread error).
    /// </summary>
    /// <param name="quantizer">The quantizer holding the palette of the frame.</param>
    /// <param name="keys">The color keys of the row (<see cref="PaletteQuantizer.TransparentKey"/> for transparent pixels).</param>
    /// <param name="indices">Receives one palette index per pixel.</param>
    /// <param name="transparentIndex">The index written for transparent pixels (unused when the row has none).</param>
    public void MapRow(PaletteQuantizer quantizer, ReadOnlySpan<int> keys, Span<byte> indices, int transparentIndex)
    {
        // A key equal to the previous one maps to the same entry: Map is a function of the key, and the entry it remembers
        // to bound the next search is the one it returned
        var lastKey = PaletteQuantizer.TransparentKey;
        var lastIndex = 0;
        if (!_dither || quantizer.IsExact)
        {
            for (var x = 0; x < _width; x++)
            {
                var key = keys[x];
                if (key == PaletteQuantizer.TransparentKey)
                {
                    indices[x] = (byte)transparentIndex;
                    continue;
                }

                if (key != lastKey)
                {
                    lastKey = key;
                    lastIndex = quantizer.Map(key);
                }

                indices[x] = (byte)lastIndex;
            }

            return;
        }

        var errors = unsafe(MemoryMarshal.Cast<byte, int>((_errors ?? throw new ObjectDisposedException(nameof(PaletteRowMapper))).Span));
        var stride = (_width + 2) * 3;
        var current = errors[..stride];
        var next = errors.Slice(stride, stride);
        var palette = quantizer.Palette;
        for (var x = 0; x < _width; x++)
        {
            var key = keys[x];
            if (key == PaletteQuantizer.TransparentKey)
            {
                indices[x] = (byte)transparentIndex;
                continue;
            }

            var offset = (x + 1) * 3;
            var r = Math.Clamp(((key >> 16) & 0xFF) + ((current[offset] + 8) >> 4), 0, 255);
            var g = Math.Clamp(((key >> 8) & 0xFF) + ((current[offset + 1] + 8) >> 4), 0, 255);
            var b = Math.Clamp((key & 0xFF) + ((current[offset + 2] + 8) >> 4), 0, 255);
            var color24 = (r << 16) | (g << 8) | b;
            if (color24 != lastKey)
            {
                lastKey = color24;
                lastIndex = quantizer.Map(color24);
            }

            var index = lastIndex;
            indices[x] = (byte)index;
            var color = palette[index];
            Spread(current, next, offset, 0, r - ((color >> 16) & 0xFF));
            Spread(current, next, offset, 1, g - ((color >> 8) & 0xFF));
            Spread(current, next, offset, 2, b - (color & 0xFF));
        }

        // The next row's errors become the current ones; the following row starts without error
        next.CopyTo(current);
        next.Clear();

        static void Spread(Span<int> current, Span<int> next, int offset, int channel, int error)
        {
            current[offset + 3 + channel] += error * 7;
            next[offset - 3 + channel] += error * 3;
            next[offset + channel] += error * 5;
            next[offset + 3 + channel] += error;
        }
    }

    public void Dispose()
    {
        _errors?.Dispose();
        _errors = null;
    }
}
