using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The color reduction of whole frames, shared by <see cref="ImageProcessingExtensions.Quantize(Image, QuantizeOptions?, CancellationToken)"/>,
/// <see cref="PaletteBuilder"/> and the indexed PNG encoder: frames are read as 8-bit RGBA keys (with the conversion of
/// <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/>), histogrammed by a <see cref="PaletteQuantizer"/>, and their pixels replaced by the
/// palette colors that a <see cref="PaletteRowMapper"/> gives them.
/// </summary>
/// <remarks>
/// The working rows (8-bit RGBA, keys and indexes) are rented once from the scope as <see cref="AllocationKind.Temporary"/>,
/// for frames of at most the width given at creation.
/// </remarks>
internal sealed class FrameQuantization : IDisposable
{
    private readonly int _width;
    private PooledBuffer? _rgba;
    private PooledBuffer? _keys;
    private PooledBuffer? _indices;

    /// <summary>Rents the working rows.</summary>
    /// <param name="scope">The scope charged for them.</param>
    /// <param name="width">The largest width of the frames.</param>
    /// <exception cref="ImageResourceLimitException">The rows exceed the live-allocation limit.</exception>
    public FrameQuantization(AllocationScope scope, int width)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        _width = width;
        if ((long)width * sizeof(int) > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        try
        {
            _rgba = scope.Rent(width * 4, AllocationKind.Temporary, clear: false);
            _keys = scope.Rent(width * sizeof(int), AllocationKind.Temporary, clear: false);
            _indices = scope.Rent(width, AllocationKind.Temporary, clear: false);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the keys of the entries of a palette that frames of a pixel format can store: the opaque gray entries for a gray
    /// format, the opaque entries for a format without alpha, every entry otherwise.
    /// </summary>
    /// <param name="palette">The palette.</param>
    /// <param name="pixelFormat">The pixel format of the frames.</param>
    /// <returns>The keys, in palette order.</returns>
    /// <exception cref="ArgumentException">No entry of the palette can be stored.</exception>
    public static int[] GetUsableKeys(ImagePalette palette, PixelFormat pixelFormat)
    {
        var gray = PixelFormats.IsGrayscale(pixelFormat);
        var alpha = PixelFormats.HasAlpha(pixelFormat);
        var keys = new List<int>(palette.Count);
        foreach (var key in palette.Keys)
        {
            var opaque = PaletteQuantizer.GetAlpha(key) == byte.MaxValue;
            if (!alpha && !opaque)
                continue;

            if (gray && (((key >> 16) & 0xFF) != ((key >> 8) & 0xFF) || ((key >> 8) & 0xFF) != (key & 0xFF)))
                continue;

            keys.Add(key);
        }

        if (keys.Count == 0)
        {
            throw new ArgumentException(
                gray ? $"The palette has no opaque gray color, which {pixelFormat} pixels need: their colors cannot be stored otherwise." : $"The palette has no opaque color, which {pixelFormat} pixels need: they have no alpha.",
                nameof(palette));
        }

        return [.. keys];
    }

    /// <summary>Adds every row of a leased frame to the histogram of a quantizer.</summary>
    /// <param name="quantizer">The quantizer, whose histogram is started.</param>
    /// <param name="lease">The lease of the frame.</param>
    /// <param name="pixelFormat">The pixel format of the frame.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    public void AddRows(PaletteQuantizer quantizer, scoped in PixelLease lease, PixelFormat pixelFormat, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(quantizer);
        for (var y = 0; y < lease.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            quantizer.Add(ReadKeys(lease.GetRowBytes(y), pixelFormat, lease.Width));
        }
    }

    /// <summary>
    /// Replaces every pixel of a leased frame by the palette color the mapper gives it, from the top row down: transparent
    /// pixels become transparent black, and 16-bit and floating-point samples get the exact values of 8-bit samples.
    /// </summary>
    /// <param name="quantizer">The quantizer holding the palette of the frame.</param>
    /// <param name="mapper">The mapper, reset for the frame.</param>
    /// <param name="lease">The lease of the frame.</param>
    /// <param name="pixelFormat">The pixel format of the frame.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <param name="transparentColor">The color of the transparent entry of the palette (a fully transparent color).</param>
    public void MapRows(PaletteQuantizer quantizer, PaletteRowMapper mapper, scoped in PixelLease lease, PixelFormat pixelFormat, CancellationToken cancellationToken, Rgba32 transparentColor = default)
    {
        ArgumentNullException.ThrowIfNull(quantizer);
        ArgumentNullException.ThrowIfNull(mapper);
        var width = lease.Width;
        Span<Rgba32> colors = stackalloc Rgba32[ImagePalette.MaxCount];
        var palette = quantizer.Palette;
        for (var i = 0; i < palette.Length; i++)
        {
            colors[i] = palette[i] == PaletteQuantizer.TransparentKey ? transparentColor : ImagePalette.ToColor(palette[i]);
        }

        var indices = Indices[..width];
        var rgba = unsafe(MemoryMarshal.Cast<byte, Rgba32>(Rgba))[..width];
        for (var y = 0; y < lease.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = lease.GetRowBytes(y);
            mapper.MapRow(quantizer, ReadKeys(row, pixelFormat, width), indices, transparentIndex: -1, y);
            for (var x = 0; x < width; x++)
            {
                rgba[x] = colors[indices[x]];
            }

            _ = PixelConverter.ConvertRow(PixelFormat.Rgba32, unsafe(MemoryMarshal.AsBytes(rgba)), pixelFormat, row);
        }
    }

    public void Dispose()
    {
        _rgba?.Dispose();
        _rgba = null;
        _keys?.Dispose();
        _keys = null;
        _indices?.Dispose();
        _indices = null;
    }

    private Span<byte> Rgba => (_rgba ?? throw new ObjectDisposedException(nameof(FrameQuantization))).Span;

    private Span<int> Keys => unsafe(MemoryMarshal.Cast<byte, int>((_keys ?? throw new ObjectDisposedException(nameof(FrameQuantization))).Span));

    private Span<byte> Indices => (_indices ?? throw new ObjectDisposedException(nameof(FrameQuantization))).Span;

    /// <summary>Reads the keys of a row of stored pixels, converted to 8-bit RGBA.</summary>
    private Span<int> ReadKeys(ReadOnlySpan<byte> row, PixelFormat pixelFormat, int width)
    {
        if (width > _width)
            throw new ArgumentOutOfRangeException(nameof(width), width, "The frame is wider than the working rows.");

        ReadOnlySpan<byte> samples = row;
        if (pixelFormat != PixelFormat.Rgba32)
        {
            var rgba = Rgba[..(width * 4)];
            _ = PixelConverter.ConvertRow(pixelFormat, row, PixelFormat.Rgba32, rgba);
            samples = rgba;
        }

        var keys = Keys[..width];
        GetKeys(samples[..(4 * width)], keys);
        return keys;
    }

    /// <summary>
    /// Gets the keys of 8-bit RGBA pixels (<see cref="PaletteQuantizer.GetKey"/>), a vector of pixels at a time: the bytes of
    /// a pixel are reordered into <c>(A &lt;&lt; 24) | (R &lt;&lt; 16) | (G &lt;&lt; 8) | B</c>, then the alpha of an opaque
    /// pixel is cleared and a transparent pixel becomes <see cref="PaletteQuantizer.TransparentKey"/>.
    /// </summary>
    /// <param name="rgba">The pixels, 4 bytes each.</param>
    /// <param name="keys">Receives one key per pixel.</param>
    internal static void GetKeys(ReadOnlySpan<byte> rgba, Span<int> keys)
    {
        var count = rgba.Length / 4;
        keys = keys[..count];
        if (!Vector128.IsHardwareAccelerated || count < Vector128<int>.Count)
        {
            GetKeysScalar(rgba, keys);
            return;
        }

        // The last vector ends at the last pixel, overlapping the previous one (the keys are written again, unchanged)
        ref var source = ref unsafe(MemoryMarshal.GetReference(rgba));
        ref var destination = ref unsafe(MemoryMarshal.GetReference(keys));
        if (Vector256.IsHardwareAccelerated && count >= Vector256<int>.Count)
        {
            var order = Vector256.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15, 18, 17, 16, 19, 22, 21, 20, 23, 26, 25, 24, 27, 30, 29, 28, 31);
            for (var x = 0; ; x += Vector256<int>.Count)
            {
                x = Math.Min(x, count - Vector256<int>.Count);
                var key = Vector256.Shuffle(unsafe(Vector256.LoadUnsafe(ref source, (nuint)x * 4)), order).AsInt32();
                var alpha = Vector256.ShiftRightLogical(key, 24);
                key = Vector256.AndNot(key, Vector256.Equals(alpha, Vector256.Create(255)) & Vector256.Create(unchecked((int)0xFF00_0000))) | Vector256.Equals(alpha, Vector256<int>.Zero);
                unsafe { key.StoreUnsafe(ref destination, (nuint)x); }
                if (x == count - Vector256<int>.Count)
                    return;
            }
        }

        var order128 = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
        for (var x = 0; ; x += Vector128<int>.Count)
        {
            x = Math.Min(x, count - Vector128<int>.Count);
            var key = Vector128.Shuffle(unsafe(Vector128.LoadUnsafe(ref source, (nuint)x * 4)), order128).AsInt32();
            var alpha = Vector128.ShiftRightLogical(key, 24);
            key = Vector128.AndNot(key, Vector128.Equals(alpha, Vector128.Create(255)) & Vector128.Create(unchecked((int)0xFF00_0000))) | Vector128.Equals(alpha, Vector128<int>.Zero);
            unsafe { key.StoreUnsafe(ref destination, (nuint)x); }
            if (x == count - Vector128<int>.Count)
                return;
        }
    }

    /// <summary>The scalar reference of <see cref="GetKeys"/>.</summary>
    /// <param name="rgba">The pixels, 4 bytes each.</param>
    /// <param name="keys">Receives one key per pixel.</param>
    internal static void GetKeysScalar(ReadOnlySpan<byte> rgba, Span<int> keys)
    {
        for (var x = 0; x < rgba.Length / 4; x++)
        {
            var pixel = rgba.Slice(4 * x, 4);
            keys[x] = PaletteQuantizer.GetKey(pixel[0], pixel[1], pixel[2], pixel[3]);
        }
    }
}
