using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Maps rows of color keys to the palette indices of a <see cref="PaletteQuantizer"/>, with the dithering of a
/// <see cref="DitheringMode"/>, for <see cref="ImageProcessingExtensions.Quantize(Image, QuantizeOptions?, CancellationToken)"/>
/// and the encoders that reduce colors to a palette (GIF, indexed BMP, indexed PNG).
/// </summary>
/// <remarks>
/// <para>
/// Error diffusion carries the error of each row to the next ones, so the rows of a frame must be mapped in display order, from
/// the top, after a <see cref="Reset"/>. The error terms (one row per row of the kernel, two pixels of margin on each side,
/// three channels) are rented once from the scope as <see cref="AllocationKind.Temporary"/>. Ordered dithering only depends on
/// the position of a pixel: its rows can be mapped in any order.
/// </para>
/// <para>
/// The arithmetic is integer, so the mapping is the same on every platform. The strength is a number of 256ths
/// (<c>s = round(256 × strength)</c>). An error-diffusion kernel of divisor <c>D</c> adds <c>e × s × w</c> to each neighbor of
/// weight <c>w</c>, and a pixel is adjusted by <c>floor((t + 128·D) / (256·D))</c> of the sum <c>t</c> it received (for
/// Floyd–Steinberg at full strength, <c>(t + 8) &gt;&gt; 4</c> of the error in sixteenths). Ordered dithering adds
/// <c>floor((2·M + 1 − n²) × S / (2·n²))</c> to the red, green and blue samples, where <c>M</c> is the entry of the
/// <c>n×n</c> Bayer matrix at <c>(x mod n, y mod n)</c> and <c>S = round(s × d / 256)</c> with <c>d</c> the mean distance
/// between each palette color and its nearest other color (<see cref="PaletteQuantizer.GetMeanNeighborDistance"/>).
/// </para>
/// </remarks>
internal sealed class PaletteRowMapper : IDisposable
{
    private const int Margin = 2;

    private static readonly Kernel FloydSteinbergKernel = new(16, [(1, 0, 7), (-1, 1, 3), (0, 1, 5), (1, 1, 1)]);
    private static readonly Kernel AtkinsonKernel = new(8, [(1, 0, 1), (2, 0, 1), (-1, 1, 1), (0, 1, 1), (1, 1, 1), (0, 2, 1)]);
    private static readonly Kernel BurkesKernel = new(32, [(1, 0, 8), (2, 0, 4), (-2, 1, 2), (-1, 1, 4), (0, 1, 8), (1, 1, 4), (2, 1, 2)]);
    private static readonly Kernel JarvisJudiceNinkeKernel = new(48, [(1, 0, 7), (2, 0, 5), (-2, 1, 3), (-1, 1, 5), (0, 1, 7), (1, 1, 5), (2, 1, 3), (-2, 2, 1), (-1, 2, 3), (0, 2, 5), (1, 2, 3), (2, 2, 1)]);
    private static readonly Kernel SierraKernel = new(32, [(1, 0, 5), (2, 0, 3), (-2, 1, 2), (-1, 1, 4), (0, 1, 5), (1, 1, 4), (2, 1, 2), (-1, 2, 2), (0, 2, 3), (1, 2, 2)]);
    private static readonly Kernel SierraTwoRowKernel = new(16, [(1, 0, 4), (2, 0, 3), (-2, 1, 1), (-1, 1, 2), (0, 1, 3), (1, 1, 2), (2, 1, 1)]);
    private static readonly Kernel SierraLiteKernel = new(4, [(1, 0, 2), (-1, 1, 1), (0, 1, 1)]);
    private static readonly Kernel StuckiKernel = new(42, [(1, 0, 8), (2, 0, 4), (-2, 1, 2), (-1, 1, 4), (0, 1, 8), (1, 1, 4), (2, 1, 2), (-2, 2, 1), (-1, 2, 2), (0, 2, 4), (1, 2, 2), (2, 2, 1)]);

    private static readonly byte[] Bayer2 = CreateBayer(2);
    private static readonly byte[] Bayer4 = CreateBayer(4);
    private static readonly byte[] Bayer8 = CreateBayer(8);

    private readonly int _width;
    private readonly int _strength;
    private readonly Kernel? _kernel;
    private readonly byte[]? _matrix;
    private readonly int _matrixSize;
    private readonly int _stride;
    private PooledBuffer? _errors;
    private int _current;
    private PaletteQuantizer? _spreadQuantizer;
    private int _spreadVersion;
    private int _spread;

    /// <summary>Allocates the error terms of a dithering mapper.</summary>
    /// <param name="scope">The scope charged for them.</param>
    /// <param name="width">The number of pixels of a row.</param>
    /// <param name="mode">The dithering.</param>
    /// <param name="strength">The strength of the dithering, from 0 (none) to 1.</param>
    /// <exception cref="ImageResourceLimitException">The memory exceeds the live-allocation limit.</exception>
    public PaletteRowMapper(AllocationScope scope, int width, DitheringMode mode, double strength)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _width = width;
        _strength = (int)Math.Round(strength * 256, MidpointRounding.AwayFromZero);
        if (_strength == 0)
            return;

        (_matrix, _matrixSize) = mode switch
        {
            DitheringMode.Bayer2x2 => (Bayer2, 2),
            DitheringMode.Bayer4x4 => (Bayer4, 4),
            DitheringMode.Bayer8x8 => (Bayer8, 8),
            _ => (null, 0),
        };

        _kernel = GetKernel(mode);
        if (_kernel is not null)
        {
            _stride = (width + (2 * Margin)) * 3;
            var length = (long)_kernel.Rows * _stride * sizeof(int);
            if (length > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(scope.Limits);

            _errors = scope.Rent((int)length, AllocationKind.Temporary, clear: true);
        }
    }

    /// <summary>Gets a value indicating whether the rows must be mapped in display order, from the top (error diffusion).</summary>
    public bool RequiresDisplayOrder => _kernel is not null;

    /// <summary>Gets a value indicating whether a dithering mode diffuses the error (as opposed to no dithering or ordered dithering).</summary>
    /// <param name="mode">The mode.</param>
    /// <returns><see langword="true"/> for the error-diffusion kernels.</returns>
    public static bool IsErrorDiffusion(DitheringMode mode) => GetKernel(mode) is not null;

    /// <summary>Forgets the diffused error: the next row mapped is the first row of a frame.</summary>
    public void Reset()
    {
        _errors?.Span.Clear();
        _current = 0;
    }

    /// <summary>
    /// Maps a row of keys (rows in display order for error diffusion) to palette indices: transparent pixels to the transparent
    /// index, colors to their palette entry, or the nearest entry of the dithered color when the palette is not exact.
    /// </summary>
    /// <param name="quantizer">The quantizer holding the palette of the frame.</param>
    /// <param name="keys">The color keys of the row (<see cref="PaletteQuantizer.TransparentKey"/> for transparent pixels).</param>
    /// <param name="indices">Receives one palette index per pixel.</param>
    /// <param name="transparentIndex">
    /// The index written for transparent pixels (unused when the row has none), or -1 to map them with
    /// <see cref="PaletteQuantizer.MapAny"/> (to the transparent entry of the palette, or the nearest one).
    /// </param>
    /// <param name="y">The row of the frame (ordered dithering).</param>
    public void MapRow(PaletteQuantizer quantizer, ReadOnlySpan<int> keys, Span<byte> indices, int transparentIndex, int y)
    {
        ArgumentNullException.ThrowIfNull(quantizer);
        if (_strength == 0 || quantizer.IsExact || (_kernel is null && _matrix is null))
        {
            MapNearest(quantizer, keys, indices, transparentIndex);
        }
        else if (_kernel is not null)
        {
            MapDiffused(quantizer, _kernel, keys, indices, transparentIndex);
        }
        else
        {
            MapOrdered(quantizer, keys, indices, transparentIndex, y);
        }
    }

    public void Dispose()
    {
        _errors?.Dispose();
        _errors = null;
    }

    private static Kernel? GetKernel(DitheringMode mode) => mode switch
    {
        DitheringMode.FloydSteinberg => FloydSteinbergKernel,
        DitheringMode.Atkinson => AtkinsonKernel,
        DitheringMode.Burkes => BurkesKernel,
        DitheringMode.JarvisJudiceNinke => JarvisJudiceNinkeKernel,
        DitheringMode.Sierra => SierraKernel,
        DitheringMode.SierraTwoRow => SierraTwoRowKernel,
        DitheringMode.SierraLite => SierraLiteKernel,
        DitheringMode.Stucki => StuckiKernel,
        _ => null,
    };

    /// <summary>Builds the n×n Bayer matrix, row by row: <c>M(2n) = [4M, 4M + 2; 4M + 3, 4M + 1]</c> from <c>M(1) = [0]</c>.</summary>
    private static byte[] CreateBayer(int size)
    {
        var matrix = new byte[] { 0 };
        for (var n = 1; n < size; n *= 2)
        {
            var next = new byte[4 * n * n];
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++)
                {
                    var value = 4 * matrix[(y * n) + x];
                    next[(y * 2 * n) + x] = (byte)value;
                    next[(y * 2 * n) + x + n] = (byte)(value + 2);
                    next[((y + n) * 2 * n) + x] = (byte)(value + 3);
                    next[((y + n) * 2 * n) + x + n] = (byte)(value + 1);
                }
            }

            matrix = next;
        }

        return matrix;
    }

    private static int FloorDivide(int value, int divisor)
    {
        var quotient = Math.DivRem(value, divisor, out var remainder);
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static int MapTransparent(PaletteQuantizer quantizer, int transparentIndex)
        => transparentIndex >= 0 ? transparentIndex : quantizer.MapAny(PaletteQuantizer.TransparentKey);

    private void MapNearest(PaletteQuantizer quantizer, ReadOnlySpan<int> keys, Span<byte> indices, int transparentIndex)
    {
        // A key equal to the previous one maps to the same entry: the mapping is a function of the key, and the entry it
        // remembers to bound the next search is the one it returned
        var lastKey = PaletteQuantizer.TransparentKey;
        var lastIndex = 0;
        for (var x = 0; x < _width; x++)
        {
            var key = keys[x];
            if (key == PaletteQuantizer.TransparentKey)
            {
                indices[x] = (byte)MapTransparent(quantizer, transparentIndex);
                continue;
            }

            if (key != lastKey)
            {
                lastKey = key;
                lastIndex = quantizer.MapAny(key);
            }

            indices[x] = (byte)lastIndex;
        }
    }

    private void MapOrdered(PaletteQuantizer quantizer, ReadOnlySpan<int> keys, Span<byte> indices, int transparentIndex, int y)
    {
        // The pattern spans the spacing of the palette colors, computed once per palette
        if (_spreadVersion != quantizer.PaletteVersion || _spreadQuantizer != quantizer)
        {
            _spread = (int)Math.Round(_strength * quantizer.GetMeanNeighborDistance() / 256, MidpointRounding.AwayFromZero);
            _spreadVersion = quantizer.PaletteVersion;
            _spreadQuantizer = quantizer;
        }

        var spread = _spread;
        var n = _matrixSize;
        var cells = n * n;
        var row = _matrix.AsSpan((y % n) * n, n);
        Span<int> offsets = stackalloc int[8];
        for (var i = 0; i < n; i++)
        {
            offsets[i] = FloorDivide(((2 * row[i]) + 1 - cells) * spread, 2 * cells);
        }

        var lastKey = PaletteQuantizer.TransparentKey;
        var lastIndex = 0;
        for (var x = 0; x < _width; x++)
        {
            var key = keys[x];
            if (key == PaletteQuantizer.TransparentKey)
            {
                indices[x] = (byte)MapTransparent(quantizer, transparentIndex);
                continue;
            }

            var offset = offsets[x % n];
            var r = Math.Clamp(((key >> 16) & 0xFF) + offset, 0, 255);
            var g = Math.Clamp(((key >> 8) & 0xFF) + offset, 0, 255);
            var b = Math.Clamp((key & 0xFF) + offset, 0, 255);
            var dithered = (key & unchecked((int)0xFF00_0000)) | (r << 16) | (g << 8) | b;
            if (dithered != lastKey)
            {
                lastKey = dithered;
                lastIndex = quantizer.MapAny(dithered);
            }

            indices[x] = (byte)lastIndex;
        }
    }

    private void MapDiffused(PaletteQuantizer quantizer, Kernel kernel, ReadOnlySpan<int> keys, Span<byte> indices, int transparentIndex)
    {
        var errors = unsafe(MemoryMarshal.Cast<byte, int>((_errors ?? throw new ObjectDisposedException(nameof(PaletteRowMapper))).Span));
        var rows = kernel.Rows;
        var stride = _stride;
        var current = errors.Slice(_current * stride, stride);
        var unit = kernel.Divisor * 256;
        var half = kernel.Divisor * 128;
        var palette = quantizer.Palette;
        var taps = kernel.Taps;
        var lastKey = PaletteQuantizer.TransparentKey;
        var lastIndex = 0;
        for (var x = 0; x < _width; x++)
        {
            var key = keys[x];
            if (key == PaletteQuantizer.TransparentKey)
            {
                indices[x] = (byte)MapTransparent(quantizer, transparentIndex);
                continue;
            }

            // Translucent pixels are mapped to their nearest entry: they neither receive nor spread error
            if (PaletteQuantizer.IsTranslucentKey(key))
            {
                indices[x] = (byte)quantizer.FindNearestRgba(key);
                continue;
            }

            var offset = (x + Margin) * 3;
            var r = Math.Clamp(((key >> 16) & 0xFF) + FloorDivide(current[offset] + half, unit), 0, 255);
            var g = Math.Clamp(((key >> 8) & 0xFF) + FloorDivide(current[offset + 1] + half, unit), 0, 255);
            var b = Math.Clamp((key & 0xFF) + FloorDivide(current[offset + 2] + half, unit), 0, 255);
            var color24 = (r << 16) | (g << 8) | b;
            if (color24 != lastKey)
            {
                lastKey = color24;
                lastIndex = quantizer.Map(color24);
            }

            indices[x] = (byte)lastIndex;
            var color = palette[lastIndex];
            var errorRed = (r - ((color >> 16) & 0xFF)) * _strength;
            var errorGreen = (g - ((color >> 8) & 0xFF)) * _strength;
            var errorBlue = (b - (color & 0xFF)) * _strength;
            foreach (var (dx, dy, weight) in taps)
            {
                var target = errors.Slice(((_current + dy) % rows) * stride, stride);
                var position = offset + (3 * dx);
                target[position] += errorRed * weight;
                target[position + 1] += errorGreen * weight;
                target[position + 2] += errorBlue * weight;
            }
        }

        // The row is done: its terms are cleared and become the last row of the kernel
        current.Clear();
        _current = (_current + 1) % rows;
    }

    /// <summary>An error-diffusion kernel: the weights of the neighbors (right of the pixel on its row, then on the next rows) over a divisor.</summary>
    private sealed class Kernel(int divisor, (int Dx, int Dy, int Weight)[] taps)
    {
        public (int Dx, int Dy, int Weight)[] Taps { get; } = taps;

        public int Divisor { get; } = divisor;

        public int Rows { get; } = taps.Max(tap => tap.Dy) + 1;
    }
}
