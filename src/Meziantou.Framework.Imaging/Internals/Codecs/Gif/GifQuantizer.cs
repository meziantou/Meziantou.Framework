using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The deterministic color reduction of one GIF frame: a histogram pass over the frame's opaque
/// colors, a palette of at most the requested number of colors, and the mapping of colors to palette indices.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Colors are 24-bit keys <c>(R &lt;&lt; 16) | (G &lt;&lt; 8) | B</c>; <see cref="TransparentKey"/> marks a transparent pixel (it
/// takes no palette color: the encoder reserves the transparent entry separately).
/// </description></item>
/// <item><description>
/// <b>Exact palette.</b> While the frame has at most the requested number of distinct opaque colors (tracked exactly), the
/// palette is exactly these colors, sorted by key: every pixel is reproduced without error and dithering has no effect.
/// </description></item>
/// <item><description>
/// <b>Variance-based median cut</b> otherwise: on the exact colors when they are all known (at most the tracked count, for
/// example 256 colors and a transparent slot), else on a 5-bit-per-channel histogram (32,768 buckets holding a pixel count
/// and the exact 8-bit sums, so memory is fixed whatever the frame size). Each exact color or non-empty bucket is an item at
/// its mean color, weighted by its pixel count. Starting from one box holding every item, the box with the largest weighted sum of squared errors (ties: the
/// lowest box number) is split along the axis of largest weighted variance (ties: R, then G, then B), between the two
/// consecutive distinct coordinates that maximize the between-part variance <c>SL²/NL + SR²/NR</c> (ties: the first split),
/// until the requested number of boxes exists or no box has two items. Each palette color is the weighted mean of its box
/// (exact 8-bit sums divided by the count, nearest, ties up), and the palette is sorted by key (duplicates removed).
/// </description></item>
/// <item><description>
/// <b>Mapping.</b> A color maps to the palette entry at the smallest squared RGB distance, ties to the lowest index (a cached
/// pure function of the color). Arithmetic is integer except the split criteria (IEEE double, deterministic in .NET), so the
/// output is identical for the same input and settings on every platform.
/// </description></item>
/// </list>
/// Working memory (about 1.2 MiB, fixed) is rented once from the writer scope as <see cref="AllocationKind.Temporary"/>.
/// </remarks>
internal sealed class GifQuantizer : IDisposable
{
    /// <summary>The key of a transparent pixel.</summary>
    public const int TransparentKey = -1;

    private const int Buckets = 1 << 15;
    private const int ExactSlots = 1024;
    private const int CacheSlots = 4096;
    private const int CountsOffset = 0;
    private const int SumsOffset = CountsOffset + (Buckets * sizeof(int));
    private const int ItemsOffset = SumsOffset + (Buckets * 3 * sizeof(long));
    private const int ItemColorsOffset = ItemsOffset + (Buckets * sizeof(int));
    private const int SortKeysOffset = ItemColorsOffset + (Buckets * sizeof(int));
    private const int ExactOffset = SortKeysOffset + (Buckets * sizeof(int));
    private const int ExactCountsOffset = ExactOffset + (ExactSlots * sizeof(int));
    private const int CacheKeysOffset = ExactCountsOffset + (ExactSlots * sizeof(int));
    private const int CacheValuesOffset = CacheKeysOffset + (CacheSlots * sizeof(int));

    // Vectorized nearest-color search: the palette as 32-bit red, green and blue planes, each padded to whole vectors (at most
    // 16 lanes) with an entry farther than any color (never selected, and 3 * (1000 - 0)^2 cannot overflow)
    private const int VectorPlaneLength = 256 + 16;
    private const int VectorPaletteOffset = CacheValuesOffset + CacheSlots;
    private const int StateLength = VectorPaletteOffset + (3 * VectorPlaneLength * sizeof(int));

    private readonly int[] _palette = new int[256];
    private readonly byte[] _paletteRed = new byte[256];
    private readonly byte[] _paletteGreen = new byte[256];
    private readonly byte[] _paletteBlue = new byte[256];
    private readonly int[] _boxStart = new int[256];
    private readonly int[] _boxEnd = new int[256];
    private readonly double[] _boxError = new double[256];
    private PooledBuffer? _state;
    private int _limit;
    private int _exactCount;
    private bool _exactOverflow;
    private bool _hasTransparency;
    private bool _isExact;
    private int _paletteCount;

    /// <summary>Allocates the working memory.</summary>
    /// <param name="scope">The scope charged for it.</param>
    /// <exception cref="ImageResourceLimitException">The memory exceeds the live-allocation limit.</exception>
    public GifQuantizer(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _state = scope.Rent(StateLength, AllocationKind.Temporary, clear: false);
    }

    /// <summary>Gets a value indicating whether the frame has transparent pixels.</summary>
    public bool HasTransparency => _hasTransparency;

    /// <summary>Gets a value indicating whether the palette holds every color of the frame exactly.</summary>
    public bool IsExact => _isExact;

    /// <summary>Gets the number of palette colors (0 for a frame without opaque pixels).</summary>
    public int PaletteCount => _paletteCount;

    /// <summary>Gets the palette color keys, sorted.</summary>
    public ReadOnlySpan<int> Palette => _palette.AsSpan(0, _paletteCount);

    /// <summary>Gets the 5-bit histogram bucket of a color key.</summary>
    /// <param name="key">The color key.</param>
    /// <returns>The bucket index <c>(R5 &lt;&lt; 10) | (G5 &lt;&lt; 5) | B5</c>.</returns>
    public static int GetBucket(int key) => ((key >> 9) & 0x7C00) | ((key >> 6) & 0x03E0) | ((key >> 3) & 0x001F);

    /// <summary>Starts the histogram of a frame.</summary>
    /// <param name="maxOpaqueColors">The number of distinct colors (with their pixel counts) tracked exactly: the largest palette that can be requested.</param>
    public void Begin(int maxOpaqueColors)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxOpaqueColors, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxOpaqueColors, 256);
        _limit = maxOpaqueColors;
        _exactCount = 0;
        _exactOverflow = false;
        _hasTransparency = false;
        _isExact = false;
        _paletteCount = 0;
        var state = GetState();
        state.Slice(CountsOffset, ItemsOffset - CountsOffset).Clear();
        unsafe { MemoryMarshal.Cast<byte, int>(state.Slice(ExactOffset, ExactSlots * sizeof(int))).Fill(TransparentKey); }
        state.Slice(ExactCountsOffset, ExactSlots * sizeof(int)).Clear();
        unsafe { MemoryMarshal.Cast<byte, int>(state.Slice(CacheKeysOffset, CacheSlots * sizeof(int))).Fill(TransparentKey); }
    }

    /// <summary>Adds a row of color keys to the histogram.</summary>
    /// <param name="keys">The keys (<see cref="TransparentKey"/> for transparent pixels).</param>
    public void Add(ReadOnlySpan<int> keys)
    {
        var state = GetState();
        var counts = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(CountsOffset, Buckets * sizeof(int))));
        var sums = unsafe(MemoryMarshal.Cast<byte, long>(state.Slice(SumsOffset, Buckets * 3 * sizeof(long))));
        var exact = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(ExactOffset, ExactSlots * sizeof(int))));
        var exactCounts = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(ExactCountsOffset, ExactSlots * sizeof(int))));
        foreach (var key in keys)
        {
            if (key == TransparentKey)
            {
                _hasTransparency = true;
                continue;
            }

            var bucket = GetBucket(key);
            counts[bucket]++;
            sums[3 * bucket] += (key >> 16) & 0xFF;
            sums[(3 * bucket) + 1] += (key >> 8) & 0xFF;
            sums[(3 * bucket) + 2] += key & 0xFF;
            if (_exactOverflow)
                continue;

            var slot = HashExact(key);
            while (exact[slot] != TransparentKey && exact[slot] != key)
            {
                slot = (slot + 1) & (ExactSlots - 1);
            }

            if (exact[slot] != key)
            {
                if (_exactCount == _limit)
                {
                    _exactOverflow = true;
                    continue;
                }

                exact[slot] = key;
                _exactCount++;
            }

            exactCounts[slot]++;
        }
    }

    /// <summary>Builds the palette of the frame from its histogram.</summary>
    /// <param name="maxColors">The largest number of palette colors (at most the value given to <see cref="Begin"/>).</param>
    public void BuildPalette(int maxColors)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxColors, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxColors, _limit);
        var state = GetState();
        var exact = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(ExactOffset, ExactSlots * sizeof(int))));
        var counts = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(CountsOffset, Buckets * sizeof(int))));
        var sums = unsafe(MemoryMarshal.Cast<byte, long>(state.Slice(SumsOffset, Buckets * 3 * sizeof(long))));
        var bucketCount = Buckets;
        if (!_exactOverflow)
        {
            if (_exactCount <= maxColors)
            {
                var count = 0;
                foreach (var key in exact)
                {
                    if (key != TransparentKey)
                    {
                        _palette[count++] = key;
                    }
                }

                _isExact = true;
                SetPalette(count);
                return;
            }

            // Every color is known exactly but there are too many (the transparent entry took a slot): the median cut runs
            // on the exact colors, the histogram arrays being reused with one item per exact-set slot
            var exactCounts = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(ExactCountsOffset, ExactSlots * sizeof(int))));
            bucketCount = ExactSlots;
            for (var slot = 0; slot < ExactSlots; slot++)
            {
                var key = exact[slot];
                var n = key == TransparentKey ? 0 : exactCounts[slot];
                counts[slot] = n;
                sums[3 * slot] = (long)((key >> 16) & 0xFF) * n;
                sums[(3 * slot) + 1] = (long)((key >> 8) & 0xFF) * n;
                sums[(3 * slot) + 2] = (long)(key & 0xFF) * n;
            }
        }

        _isExact = false;
        var items = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(ItemsOffset, Buckets * sizeof(int))));
        var itemColors = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(ItemColorsOffset, Buckets * sizeof(int))));
        var sortKeys = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(SortKeysOffset, Buckets * sizeof(int))));

        // Items: the non-empty buckets, in bucket order, at their mean color
        var itemCount = 0;
        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var n = counts[bucket];
            if (n == 0)
                continue;

            items[itemCount] = bucket;
            itemColors[itemCount] = (Mean(sums[3 * bucket], n) << 16) | (Mean(sums[(3 * bucket) + 1], n) << 8) | Mean(sums[(3 * bucket) + 2], n);
            itemCount++;
        }

        var boxes = 1;
        _boxStart[0] = 0;
        _boxEnd[0] = itemCount;
        _boxError[0] = ComputeError(counts, items, itemColors, 0, itemCount, out _);
        while (boxes < maxColors)
        {
            // The splittable box with the largest weighted squared error (ties: the first)
            var selected = -1;
            for (var box = 0; box < boxes; box++)
            {
                if (_boxEnd[box] - _boxStart[box] >= 2 && (selected < 0 || _boxError[box] > _boxError[selected]))
                {
                    selected = box;
                }
            }

            if (selected < 0)
                break;

            var start = _boxStart[selected];
            var end = _boxEnd[selected];
            _ = ComputeError(counts, items, itemColors, start, end, out var axis);
            var shift = 16 - (8 * axis);

            // Sort the items of the box along the axis (keys are unique: coordinate, then position in bucket order)
            for (var i = start; i < end; i++)
            {
                sortKeys[i] = (((itemColors[i] >> shift) & 0xFF) << 15) | items[i];
            }

            sortKeys[start..end].Sort();
            for (var i = start; i < end; i++)
            {
                var bucket = sortKeys[i] & (Buckets - 1);
                items[i] = bucket;
                itemColors[i] = (Mean(sums[3 * bucket], counts[bucket]) << 16) | (Mean(sums[(3 * bucket) + 1], counts[bucket]) << 8) | Mean(sums[(3 * bucket) + 2], counts[bucket]);
            }

            var split = FindSplit(counts, items, itemColors, start, end, shift);
            _boxEnd[selected] = split;
            _boxError[selected] = ComputeError(counts, items, itemColors, start, split, out _);
            _boxStart[boxes] = split;
            _boxEnd[boxes] = end;
            _boxError[boxes] = ComputeError(counts, items, itemColors, split, end, out _);
            boxes++;
        }

        // Palette colors: the weighted means of the boxes (exact 8-bit sums)
        for (var box = 0; box < boxes; box++)
        {
            long n = 0, r = 0, g = 0, b = 0;
            for (var i = _boxStart[box]; i < _boxEnd[box]; i++)
            {
                var bucket = items[i];
                n += counts[bucket];
                r += sums[3 * bucket];
                g += sums[(3 * bucket) + 1];
                b += sums[(3 * bucket) + 2];
            }

            _palette[box] = (Mean(r, n) << 16) | (Mean(g, n) << 8) | Mean(b, n);
        }

        SetPalette(boxes);
    }

    /// <summary>Gets the palette index of a color: its exact entry, or the nearest entry (smallest squared distance, lowest index).</summary>
    /// <param name="key">An opaque color key.</param>
    /// <returns>The palette index.</returns>
    public int Map(int key)
    {
        var state = GetState();
        var cacheKeys = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(CacheKeysOffset, CacheSlots * sizeof(int))));
        var slot = (int)(((uint)key * 0x9E3779B1u) >> 20);
        if (cacheKeys[slot] == key)
            return state[CacheValuesOffset + slot];

        var index = FindNearest((key >> 16) & 0xFF, (key >> 8) & 0xFF, key & 0xFF);
        cacheKeys[slot] = key;
        state[CacheValuesOffset + slot] = (byte)index;
        return index;
    }

    /// <summary>Gets the palette index nearest to a color (smallest squared RGB distance, ties to the lowest index).</summary>
    /// <param name="red">The red sample.</param>
    /// <param name="green">The green sample.</param>
    /// <param name="blue">The blue sample.</param>
    /// <returns>The palette index.</returns>
    /// <remarks>
    /// With hardware vector support, the distances to <see cref="Vector{T}.Count"/> entries are computed at once (exact integer
    /// arithmetic) and each lane keeps its first minimum; the lowest index among the lanes holding the overall minimum is the
    /// result, which is exactly the result of the scalar reference search (<see cref="FindNearestScalar"/>).
    /// </remarks>
    public int FindNearest(int red, int green, int blue)
        => Vector.IsHardwareAccelerated && Vector<int>.Count <= 16 && _paletteCount >= Vector<int>.Count ? FindNearestVector(red, green, blue) : FindNearestScalar(red, green, blue);

    /// <summary>The scalar reference of <see cref="FindNearest"/>: a linear search keeping the first entry of smallest distance.</summary>
    internal int FindNearestScalar(int red, int green, int blue)
    {
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < _paletteCount; i++)
        {
            var dr = red - _paletteRed[i];
            var distance = dr * dr;
            if (distance >= bestDistance)
                continue;

            var dg = green - _paletteGreen[i];
            distance += dg * dg;
            if (distance >= bestDistance)
                continue;

            var db = blue - _paletteBlue[i];
            distance += db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
                if (distance == 0)
                    break;
            }
        }

        return best;
    }

    private int FindNearestVector(int red, int green, int blue)
    {
        var count = Vector<int>.Count;
        var r = new Vector<int>(red);
        var g = new Vector<int>(green);
        var b = new Vector<int>(blue);
        var index = Vector<int>.Indices;
        var step = new Vector<int>(count);
        var bestDistance = new Vector<int>(int.MaxValue);
        var bestIndex = Vector<int>.Zero;
        var planes = GetVectorPalette();
        var paletteRed = planes[..VectorPlaneLength];
        var paletteGreen = planes.Slice(VectorPlaneLength, VectorPlaneLength);
        var paletteBlue = planes.Slice(2 * VectorPlaneLength, VectorPlaneLength);
        for (var i = 0; i < _paletteCount; i += count)
        {
            var dr = r - new Vector<int>(paletteRed[i..]);
            var dg = g - new Vector<int>(paletteGreen[i..]);
            var db = b - new Vector<int>(paletteBlue[i..]);
            var distance = (dr * dr) + (dg * dg) + (db * db);

            // Strictly smaller only: each lane keeps the lowest index of its minimum
            var smaller = Vector.LessThan(distance, bestDistance);
            bestDistance = Vector.ConditionalSelect(smaller, distance, bestDistance);
            bestIndex = Vector.ConditionalSelect(smaller, index, bestIndex);
            index += step;
        }

        // The smallest distance, then the lowest index among the lanes holding it
        var best = bestIndex[0];
        var smallest = bestDistance[0];
        for (var lane = 1; lane < count; lane++)
        {
            var distance = bestDistance[lane];
            if (distance < smallest || (distance == smallest && bestIndex[lane] < best))
            {
                smallest = distance;
                best = bestIndex[lane];
            }
        }

        return best;
    }

    public void Dispose()
    {
        _state?.Dispose();
        _state = null;
    }

    private static int Mean(long sum, long count) => (int)((sum + (count / 2)) / count);

    private static int HashExact(int key) => (int)(((uint)key * 0x9E3779B1u) >> 22);

    /// <summary>Computes the weighted sum of squared errors of a box around its mean, and the axis (0 R, 1 G, 2 B) of largest error.</summary>
    private static double ComputeError(ReadOnlySpan<int> counts, ReadOnlySpan<int> items, ReadOnlySpan<int> itemColors, int start, int end, out int axis)
    {
        Span<long> sum = stackalloc long[3];
        Span<long> squares = stackalloc long[3];
        long n = 0;
        for (var i = start; i < end; i++)
        {
            long weight = counts[items[i]];
            var color = itemColors[i];
            n += weight;
            for (var c = 0; c < 3; c++)
            {
                long value = (color >> (16 - (8 * c))) & 0xFF;
                sum[c] += weight * value;
                squares[c] += weight * value * value;
            }
        }

        axis = 0;
        var total = 0d;
        var largest = -1d;
        for (var c = 0; c < 3; c++)
        {
            var error = squares[c] - ((double)sum[c] * sum[c] / n);
            total += error;
            if (error > largest)
            {
                largest = error;
                axis = c;
            }
        }

        return total;
    }

    /// <summary>Returns the first item of the upper part of the best split of a sorted box (between two distinct coordinates).</summary>
    private static int FindSplit(ReadOnlySpan<int> counts, ReadOnlySpan<int> items, ReadOnlySpan<int> itemColors, int start, int end, int shift)
    {
        long totalCount = 0, totalSum = 0;
        for (var i = start; i < end; i++)
        {
            long weight = counts[items[i]];
            totalCount += weight;
            totalSum += weight * ((itemColors[i] >> shift) & 0xFF);
        }

        long leftCount = 0, leftSum = 0;
        var best = -1;
        var bestScore = double.NegativeInfinity;
        for (var i = start; i < end - 1; i++)
        {
            long weight = counts[items[i]];
            var value = (itemColors[i] >> shift) & 0xFF;
            leftCount += weight;
            leftSum += weight * value;
            if (value == ((itemColors[i + 1] >> shift) & 0xFF))
                continue;

            var rightCount = totalCount - leftCount;
            var rightSum = totalSum - leftSum;
            var score = ((double)leftSum * leftSum / leftCount) + ((double)rightSum * rightSum / rightCount);
            if (score > bestScore)
            {
                bestScore = score;
                best = i + 1;
            }
        }

        // Every coordinate is equal along the axis (impossible for the axis of largest error of distinct items): split in half
        return best < 0 ? start + ((end - start) / 2) : best;
    }

    private void SetPalette(int count)
    {
        var palette = _palette.AsSpan(0, count);
        palette.Sort();
        var unique = 0;
        for (var i = 0; i < palette.Length; i++)
        {
            if (unique == 0 || palette[unique - 1] != palette[i])
            {
                palette[unique++] = palette[i];
            }
        }

        _paletteCount = unique;
        for (var i = 0; i < unique; i++)
        {
            _paletteRed[i] = (byte)(_palette[i] >> 16);
            _paletteGreen[i] = (byte)(_palette[i] >> 8);
            _paletteBlue[i] = (byte)_palette[i];
        }

        var planes = GetVectorPalette();
        for (var plane = 0; plane < 3; plane++)
        {
            var samples = planes.Slice(plane * VectorPlaneLength, VectorPlaneLength);
            var source = plane == 0 ? _paletteRed : plane == 1 ? _paletteGreen : _paletteBlue;
            for (var i = 0; i < unique; i++)
            {
                samples[i] = source[i];
            }

            samples[unique..].Fill(1000);
        }
    }

    private Span<int> GetVectorPalette() => unsafe(MemoryMarshal.Cast<byte, int>(GetState().Slice(VectorPaletteOffset, 3 * VectorPlaneLength * sizeof(int))));

    private Span<byte> GetState() => (_state ?? throw new ObjectDisposedException(nameof(GifQuantizer))).RawBuffer.AsSpan(0, StateLength);
}
