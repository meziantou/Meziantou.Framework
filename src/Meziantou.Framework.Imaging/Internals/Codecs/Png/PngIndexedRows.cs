using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The palette of an indexed PNG output (color type 3) and the conversion of stored rows to palette indexes: the exact
/// counterpart of the palette expansion of <see cref="PngSampleFormat"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// The color of a pixel is the key <c>(A &lt;&lt; 24) | (R &lt;&lt; 16) | (G &lt;&lt; 8) | B</c> of its 8-bit samples: gray is
/// replicated, a pixel format without alpha is opaque, <c>Bgra32</c> is reordered. A 16-bit sample is only accepted when it
/// is a multiple of 257 (its two bytes are equal), the values an 8-bit sample expands to: nothing is ever rounded.
/// </description></item>
/// <item><description>
/// The palette is the set of the keys of every row collected before <see cref="Freeze"/> (<see cref="Collect"/>), in
/// ascending key order: the entries that are not opaque come first, so that <c>tRNS</c> only stores their alpha values.
/// Afterward rows can only be checked against it (<see cref="Check"/>) and mapped (<see cref="Map"/>).
/// </description></item>
/// </list>
/// Private state is the collector and one row of keys, charged to the writer scope.
/// </remarks>
internal sealed class PngIndexedRows : IDisposable
{
    private readonly PixelFormat _pixelFormat;
    private readonly int _width;
    private ExactPaletteCollector? _palette;
    private PooledBuffer? _keys;

    /// <summary>Creates the palette of images of one width and pixel format, and rents its working memory.</summary>
    /// <param name="scope">The writer scope, charged for every buffer.</param>
    /// <param name="pixelFormat">The stored pixel format.</param>
    /// <param name="width">The image width.</param>
    /// <exception cref="ImageResourceLimitException">The working buffers exceed the allocation limit.</exception>
    public PngIndexedRows(AllocationScope scope, PixelFormat pixelFormat, int width)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        _pixelFormat = pixelFormat;
        _width = width;
        var keysLength = (long)width * sizeof(uint);
        if (keysLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        try
        {
            _palette = new ExactPaletteCollector(scope);
            _keys = scope.Rent((int)keysLength, AllocationKind.Temporary, clear: false);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets the number of palette entries.</summary>
    public int Count => Palette.Count;

    /// <summary>Gets a value indicating whether the palette is final.</summary>
    public bool IsFrozen => Palette.IsFrozen;

    /// <summary>Gets the palette entries as <c>(A &lt;&lt; 24) | (R &lt;&lt; 16) | (G &lt;&lt; 8) | B</c> keys, in palette order once the palette is final.</summary>
    public ReadOnlySpan<uint> Colors => Palette.Colors;

    private ExactPaletteCollector Palette
    {
        get
        {
            ObjectDisposedException.ThrowIf(_palette is null, this);
            return _palette;
        }
    }

    /// <summary>Gets the smallest PNG bit depth whose indexes address a palette.</summary>
    /// <param name="count">The number of palette entries.</param>
    /// <returns>1, 2, 4 or 8.</returns>
    public static byte GetBitDepth(int count) => count switch
    {
        <= 2 => 1,
        <= 4 => 2,
        <= 16 => 4,
        _ => 8,
    };

    /// <summary>Adds the colors of a stored row to the palette.</summary>
    /// <param name="row">The stored pixel bytes of a whole row.</param>
    /// <param name="x">The column of the pixel that cannot be stored, when the result is not <see cref="PngIndexedRowStatus.Ok"/>.</param>
    /// <returns>The outcome. Colors added before a failure stay in the palette: see <see cref="Rollback"/>.</returns>
    public PngIndexedRowStatus Collect(ReadOnlySpan<byte> row, out int x)
    {
        var palette = Palette;
        var keys = GetKeys(_width);
        x = ReadKeys(row, start: 0, step: 1, keys);
        if (x >= 0)
            return PngIndexedRowStatus.NotEightBit;

        // Consecutive pixels often share their color: only a change is looked up
        var last = ~keys[0];
        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            if (key == last)
                continue;

            if (!palette.TryAdd(key))
            {
                x = i;
                return PngIndexedRowStatus.TooManyColors;
            }

            last = key;
        }

        return PngIndexedRowStatus.Ok;
    }

    /// <summary>Checks that every color of a stored row is in the palette, without changing it.</summary>
    /// <param name="row">The stored pixel bytes of a whole row.</param>
    /// <param name="x">The column of the pixel that cannot be stored, when the result is not <see cref="PngIndexedRowStatus.Ok"/>.</param>
    /// <returns>The outcome.</returns>
    public PngIndexedRowStatus Check(ReadOnlySpan<byte> row, out int x)
    {
        var palette = Palette;
        var keys = GetKeys(_width);
        x = ReadKeys(row, start: 0, step: 1, keys);
        if (x >= 0)
            return PngIndexedRowStatus.NotEightBit;

        var last = ~keys[0];
        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            if (key == last)
                continue;

            if (!palette.TryGetIndex(key, out _))
            {
                x = i;
                return PngIndexedRowStatus.MissingColor;
            }

            last = key;
        }

        return PngIndexedRowStatus.Ok;
    }

    /// <summary>Adds a color to the palette, if it is not in it yet.</summary>
    /// <param name="key">The color, <c>(A &lt;&lt; 24) | (R &lt;&lt; 16) | (G &lt;&lt; 8) | B</c>.</param>
    /// <returns><see langword="false"/> when the color is new and the palette is full.</returns>
    public bool TryAdd(uint key) => Palette.TryAdd(key);

    /// <summary>Creates a palette of the same colors plus one, not final yet.</summary>
    /// <param name="scope">The writer scope, charged for the working memory of the copy.</param>
    /// <param name="key">The added color.</param>
    /// <returns>The copy, which the caller disposes.</returns>
    /// <exception cref="InvalidOperationException">The palette is full and the color is new.</exception>
    public PngIndexedRows CreateCopy(AllocationScope scope, uint key)
    {
        var copy = new PngIndexedRows(scope, _pixelFormat, _width);
        try
        {
            foreach (var color in Colors)
            {
                _ = copy.TryAdd(color);
            }

            if (!copy.TryAdd(key))
                throw new InvalidOperationException("The palette has no room for another color.");

            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    /// <summary>Removes the colors added since the palette held <paramref name="count"/> entries.</summary>
    /// <param name="count">A previous value of <see cref="Count"/>.</param>
    public void Rollback(int count) => Palette.Rollback(count);

    /// <summary>Makes the palette final: the entries get their order and no color can be added.</summary>
    public void Freeze() => Palette.Freeze();

    /// <summary>Maps pixels of a stored row to their palette indexes.</summary>
    /// <param name="row">The stored pixel bytes of a whole row.</param>
    /// <param name="start">The column of the first pixel.</param>
    /// <param name="step">The distance between two pixels (1, or the column step of an Adam7 pass).</param>
    /// <param name="indexes">The palette indexes, one byte per pixel; its length is the number of pixels.</param>
    /// <exception cref="InvalidOperationException">A pixel has a color that is not in the palette: the frame changed since it was validated.</exception>
    public void Map(ReadOnlySpan<byte> row, int start, int step, Span<byte> indexes)
    {
        if (indexes.IsEmpty)
            return;

        var palette = Palette;
        var keys = GetKeys(indexes.Length);
        var invalid = ReadKeys(row, start, step, keys);
        if (invalid < 0)
        {
            var last = ~keys[0];
            byte lastIndex = 0;
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (key != last)
                {
                    if (!palette.TryGetIndex(key, out lastIndex))
                    {
                        invalid = start + (i * step);
                        break;
                    }

                    last = key;
                }

                indexes[i] = lastIndex;
            }
        }

        if (invalid >= 0)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The pixel at column {invalid} has a color that is not in the PNG palette: the pixels of the frame changed while it was written."));
    }

    public void Dispose()
    {
        _keys?.Dispose();
        _keys = null;
        _palette?.Dispose();
        _palette = null;
    }

    private Span<uint> GetKeys(int count)
    {
        ObjectDisposedException.ThrowIf(_keys is null, this);
        return unsafe(MemoryMarshal.Cast<byte, uint>(_keys.Span))[..count];
    }

    /// <summary>Reads the keys of <c>keys.Length</c> pixels of a stored row.</summary>
    /// <returns>The column of the first pixel with a sample that 8 bits cannot represent exactly, or -1.</returns>
    private int ReadKeys(ReadOnlySpan<byte> row, int start, int step, Span<uint> keys)
    {
        const uint Opaque = 0xFF00_0000;
        switch (_pixelFormat)
        {
            case PixelFormat.Gray8:
                for (int i = 0, x = start; i < keys.Length; i++, x += step)
                {
                    keys[i] = Opaque | (row[x] * 0x01_0101u);
                }

                return -1;

            case PixelFormat.Rgb24:
                for (int i = 0, x = start; i < keys.Length; i++, x += step)
                {
                    var pixel = row.Slice(x * 3, 3);
                    keys[i] = Opaque | ((uint)pixel[0] << 16) | ((uint)pixel[1] << 8) | pixel[2];
                }

                return -1;

            case PixelFormat.Rgba32:
                for (int i = 0, x = start; i < keys.Length; i++, x += step)
                {
                    var pixel = row.Slice(x * 4, 4);
                    keys[i] = ((uint)pixel[3] << 24) | ((uint)pixel[0] << 16) | ((uint)pixel[1] << 8) | pixel[2];
                }

                return -1;

            case PixelFormat.Bgra32:
                for (int i = 0, x = start; i < keys.Length; i++, x += step)
                {
                    var pixel = row.Slice(x * 4, 4);
                    keys[i] = ((uint)pixel[3] << 24) | ((uint)pixel[2] << 16) | ((uint)pixel[1] << 8) | pixel[0];
                }

                return -1;

            case PixelFormat.Gray16:
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
                    for (int i = 0, x = start; i < keys.Length; i++, x += step)
                    {
                        var gray = samples[x];
                        if (!IsEightBit(gray))
                            return x;

                        keys[i] = Opaque | ((gray & 0xFFu) * 0x01_0101u);
                    }

                    return -1;
                }

            case PixelFormat.Rgba64:
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
                    for (int i = 0, x = start; i < keys.Length; i++, x += step)
                    {
                        var pixel = samples.Slice(x * 4, 4);
                        if (!IsEightBit(pixel[0]) || !IsEightBit(pixel[1]) || !IsEightBit(pixel[2]) || !IsEightBit(pixel[3]))
                            return x;

                        keys[i] = ((pixel[3] & 0xFFu) << 24) | ((pixel[0] & 0xFFu) << 16) | ((pixel[1] & 0xFFu) << 8) | (pixel[2] & 0xFFu);
                    }

                    return -1;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(row), _pixelFormat, "The pixel format is not supported.");
        }

        // The 16-bit samples an 8-bit sample expands to are the multiples of 257: both bytes are equal
        static bool IsEightBit(ushort sample) => (sample >> 8) == (sample & 0xFF);
    }
}
