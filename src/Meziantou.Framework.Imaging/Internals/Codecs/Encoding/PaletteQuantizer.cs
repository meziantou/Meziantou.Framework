using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The deterministic color reduction shared by <see cref="ImageProcessingExtensions.Quantize(Image, QuantizeOptions?, CancellationToken)"/>,
/// <see cref="PaletteBuilder"/> and the encoders that reduce colors to a palette (GIF, indexed BMP, indexed PNG): a histogram
/// pass over the colors of one or more frames, a palette of at most the requested number of colors (or a fixed palette), and
/// the mapping of colors to palette indices.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Colors are keys: <c>(R &lt;&lt; 16) | (G &lt;&lt; 8) | B</c> for an opaque color, <see cref="TransparentKey"/> for a fully
/// transparent pixel, and <c>(A &lt;&lt; 24) | (R &lt;&lt; 16) | (G &lt;&lt; 8) | B</c> for a translucent color (alpha from 1 to
/// 254), which only a quantizer created with translucent colors accepts (<see cref="GetKey"/>). A transparent pixel takes no
/// computed palette color: the caller reserves the transparent entry (<see cref="BuildPalette(int, bool)"/>).
/// </description></item>
/// <item><description>
/// <b>Translucent colors</b> are histogrammed apart from the opaque ones (4 bits per color channel and 3 bits of alpha), and the
/// median cut never puts an opaque color and a translucent one in the same box (unless a single color is requested): an
/// opaque pixel always maps to an opaque entry, so opaque areas never become translucent. The coordinates of a translucent
/// color are premultiplied by its alpha, plus the alpha itself, so colors that are almost transparent are close to each
/// other whatever their hue. Its palette color is the alpha-weighted mean color of its box, with the mean alpha.
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
/// (exact 8-bit sums divided by the count, nearest, ties up). The palette is then refined by k-means (Lloyd iterations): each
/// item moves to the nearest entry of its class (ties: its current entry, then the lowest index) and each entry becomes the
/// weighted mean of its items, until no item moves or <see cref="RefinementPasses"/> passes are done. The palette is sorted
/// by key (duplicates removed).
/// </description></item>
/// <item><description>
/// <b>Mapping.</b> An opaque color maps to the opaque palette entry at the smallest squared RGB distance, ties to the lowest
/// index (a pure function of the color). A translucent color, or an opaque color when the palette has no opaque entry, maps
/// to the entry at the smallest squared distance of the premultiplied samples plus the alpha (<see cref="FindNearestRgba"/>),
/// the transparent entry included. Arithmetic is integer except the split criteria (IEEE double, deterministic in .NET), so
/// the output is identical for the same input and settings on every platform.
/// </description></item>
/// <item><description>
/// <b>Palette order.</b> The opaque entries come first, sorted by key, then the translucent ones sorted by key, then the
/// transparent entry, if any.
/// </description></item>
/// <item><description>
/// <b>Mapping speed.</b> The entries that can be the nearest one of some color of a 5-bit cell of the color cube are found
/// once per cell and palette, when the first color of the cell is mapped (<see cref="BuildCell"/>); the colors of the cell
/// are then only compared with these few candidates. The candidates are a superset of the possible results, so the mapping
/// is exactly the one of the linear search (<see cref="FindNearestScalar"/>).
/// </description></item>
/// </list>
/// Working memory (about 1.3 MiB, 2.6 MiB with translucent colors, fixed) is rented once from the scope as <see cref="AllocationKind.Temporary"/>.
/// The mapping reuses the memory of the histogram: a frame is analyzed (<see cref="Begin"/>, <see cref="Add"/>), its palette
/// is built once (<see cref="BuildPalette(int, bool)"/>), then its colors are mapped (<see cref="Map"/>).
/// </remarks>
internal sealed class PaletteQuantizer : IDisposable
{
    /// <summary>The key of a transparent pixel.</summary>
    public const int TransparentKey = -1;

    private const int Buckets = 1 << 15;
    private const int ExactSlots = 1024;

    // Mapping: for each 5-bit cell of the color cube, a count and the palette entries that can be the nearest one of a color
    // of the cell, in whole groups
    private const int CellStride = 16;
    private const int CellGroup = 4;
    private const int MaxCellCandidates = (CellStride - 1) / CellGroup * CellGroup;
    private const byte CellOverflow = byte.MaxValue;
    private const int CountsOffset = 0;
    private const int CellsOffset = CountsOffset;

    // Vectorized nearest-color search: the palette as 32-bit red, green and blue planes, each padded to whole vectors (at most
    // 16 lanes) with an entry farther than any color (never selected, and 3 * (1000 - 0)^2 cannot overflow)
    private const int VectorPlaneLength = 256 + 16;

    // The translucent search remembers the entry of the last colors it was given, by key; an empty slot holds a value that
    // is not a key (alpha 255 is never stored in the high byte)
    private const int RgbaCacheLength = 4096;
    private const int NoKey = unchecked((int)0xFF00_0000);

    // The largest number of k-means passes that refine the palette of the median cut
    private const int RefinementPasses = 8;

    // The histogram counts and the median cut arrays are only used until the palette is built, and the cells only afterward:
    // they share their memory. A quantizer of translucent colors has twice as many buckets (the opaque ones, then the
    // translucent ones) and the sums of the translucent buckets (alpha-weighted colors and alpha)
    private readonly int _bucketCount;
    private readonly int _itemsOffset;
    private readonly int _colorsOffset;
    private readonly int _sortKeysOffset;
    private readonly int _sumsOffset;
    private readonly int _translucentSumsOffset;
    private readonly int _exactOffset;
    private readonly int _exactCountsOffset;
    private readonly int _vectorPaletteOffset;
    private readonly int _stateLength;

    private readonly int[] _palette = new int[256];
    private readonly byte[] _paletteRed = new byte[256];
    private readonly byte[] _paletteGreen = new byte[256];
    private readonly byte[] _paletteBlue = new byte[256];
    private readonly int[] _boxStart = new int[256];
    private readonly int[] _boxEnd = new int[256];
    private readonly double[] _boxError = new double[256];
    private readonly int[] _boxAxis = new int[256];
    private readonly bool[] _boxTranslucent = new bool[256];

    // The samples of every entry premultiplied by its alpha (R * A, G * A, B * A, then A * 255), for the translucent search
    private readonly int[] _premultiplied = new int[256 * 4];
    private readonly bool _translucent;
    private int[]? _rgbaCacheKeys;
    private byte[]? _rgbaCacheIndexes;

    // For each red sample, the index of the first palette entry whose red sample is at least that value (the palette is sorted)
    private readonly ushort[] _redStart = new ushort[256];
    private bool _useSortedSearch;
    private int _lastIndex;
    private bool _hasHistogram;
    private PooledBuffer? _state;
    private int _limit;
    private int _exactCount;
    private bool _exactOverflow;
    private bool _hasTransparency;
    private bool _isExact;
    private int _paletteCount;
    private int _opaqueCount;
    private int _transparentIndex = -1;
    private bool _exactMode;
    private long _pixelCount;

    /// <summary>Allocates the working memory.</summary>
    /// <param name="scope">The scope charged for it.</param>
    /// <param name="translucent"><see langword="true"/> to accept translucent colors (alpha from 1 to 254) as well as opaque ones.</param>
    /// <exception cref="ImageResourceLimitException">The memory exceeds the live-allocation limit.</exception>
    public PaletteQuantizer(AllocationScope scope, bool translucent = false)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _translucent = translucent;
        _bucketCount = translucent ? 2 * Buckets : Buckets;
        _itemsOffset = CountsOffset + (_bucketCount * sizeof(int));
        _colorsOffset = _itemsOffset + (_bucketCount * sizeof(int));
        _sortKeysOffset = _colorsOffset + (_bucketCount * sizeof(int));
        _sumsOffset = _sortKeysOffset + (_bucketCount * sizeof(int));
        _translucentSumsOffset = _sumsOffset + (Buckets * 3 * sizeof(long));
        _exactOffset = _translucentSumsOffset + (translucent ? Buckets * 4 * sizeof(long) : 0);
        _exactCountsOffset = _exactOffset + (ExactSlots * sizeof(int));
        _vectorPaletteOffset = _exactCountsOffset + (ExactSlots * sizeof(int));
        _stateLength = _vectorPaletteOffset + (3 * VectorPlaneLength * sizeof(int));
        _state = scope.Rent(_stateLength, AllocationKind.Temporary, clear: false);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the cells of the mapping are built with vectors (the default with 128-bit
    /// hardware vectors). Tests turn it off to compare the mapping of both paths with the linear search.
    /// </summary>
    internal bool UseVectors { get; set; } = Vector128.IsHardwareAccelerated;

    /// <summary>Gets a value indicating whether the frame has transparent pixels.</summary>
    public bool HasTransparency => _hasTransparency;

    /// <summary>Gets a value indicating whether the palette holds every color of the frame exactly.</summary>
    public bool IsExact => _isExact;

    /// <summary>Gets a value indicating whether the quantizer accepts translucent colors.</summary>
    public bool IsTranslucent => _translucent;

    /// <summary>Gets the number of palette entries (0 for a frame without opaque pixels), the transparent entry included when the palette has one.</summary>
    public int PaletteCount => _paletteCount;

    /// <summary>Gets the number of opaque palette entries: they are the first ones.</summary>
    public int OpaqueCount => _opaqueCount;

    /// <summary>Gets a number that changes whenever the palette changes.</summary>
    public int PaletteVersion { get; private set; }

    /// <summary>
    /// Gets the mean distance between each palette entry that is not transparent and its nearest other entry (Euclidean, on the
    /// straight 8-bit samples), or 0 for a palette of one color: the spacing of the palette, which ordered dithering spreads
    /// its pattern over.
    /// </summary>
    /// <returns>The mean distance.</returns>
    public double GetMeanNeighborDistance()
    {
        var count = _paletteCount - (_transparentIndex >= 0 ? 1 : 0);
        if (count < 2)
            return 0;

        var total = 0d;
        for (var i = 0; i < count; i++)
        {
            var nearest = int.MaxValue;
            for (var j = 0; j < count; j++)
            {
                if (i == j)
                    continue;

                var dr = ((_palette[i] >> 16) & 0xFF) - ((_palette[j] >> 16) & 0xFF);
                var dg = ((_palette[i] >> 8) & 0xFF) - ((_palette[j] >> 8) & 0xFF);
                var db = (_palette[i] & 0xFF) - (_palette[j] & 0xFF);
                nearest = Math.Min(nearest, (dr * dr) + (dg * dg) + (db * db));
            }

            total += Math.Sqrt(nearest);
        }

        return total / count;
    }

    /// <summary>Gets the index of the transparent palette entry, or -1 when the palette has none.</summary>
    public int TransparentIndex => _transparentIndex;

    /// <summary>Gets the palette keys, in palette order (opaque entries, translucent entries, then the transparent entry).</summary>
    public ReadOnlySpan<int> Palette => _palette.AsSpan(0, _paletteCount);

    /// <summary>Gets the key of a color.</summary>
    /// <param name="red">The red sample.</param>
    /// <param name="green">The green sample.</param>
    /// <param name="blue">The blue sample.</param>
    /// <param name="alpha">The alpha sample.</param>
    /// <returns><see cref="TransparentKey"/> when <paramref name="alpha"/> is 0, the 24-bit key of an opaque color, or the key of a translucent color.</returns>
    public static int GetKey(int red, int green, int blue, int alpha) => alpha switch
    {
        0 => TransparentKey,
        byte.MaxValue => (red << 16) | (green << 8) | blue,
        _ => (alpha << 24) | (red << 16) | (green << 8) | blue,
    };

    /// <summary>Gets the alpha sample of a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>0 for <see cref="TransparentKey"/>, 255 for an opaque color, the alpha of a translucent color otherwise.</returns>
    public static int GetAlpha(int key) => key == TransparentKey ? 0 : ((key >>> 24) is var alpha && alpha != 0 ? alpha : byte.MaxValue);

    /// <summary>Gets a value indicating whether a key is the key of a translucent color.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> for an alpha from 1 to 254.</returns>
    public static bool IsTranslucentKey(int key) => key != TransparentKey && (key >>> 24) != 0;

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
        _opaqueCount = 0;
        _transparentIndex = -1;
        _pixelCount = 0;
        var state = GetState();
        state.Slice(CountsOffset, _bucketCount * sizeof(int)).Clear();
        state.Slice(_sumsOffset, Buckets * 3 * sizeof(long)).Clear();
        if (_translucent)
        {
            state.Slice(_translucentSumsOffset, Buckets * 4 * sizeof(long)).Clear();
        }

        _hasHistogram = true;
        unsafe { MemoryMarshal.Cast<byte, int>(state.Slice(_exactOffset, ExactSlots * sizeof(int))).Fill(TransparentKey); }
        state.Slice(_exactCountsOffset, ExactSlots * sizeof(int)).Clear();
    }

    /// <summary>Adds a row of color keys to the histogram.</summary>
    /// <param name="keys">The keys (<see cref="TransparentKey"/> for transparent pixels; translucent keys only when <see cref="IsTranslucent"/>).</param>
    /// <exception cref="InvalidOperationException">The histogram would count more than <see cref="int.MaxValue"/> pixels.</exception>
    public void Add(ReadOnlySpan<int> keys)
    {
        EnsureHistogram();
        _pixelCount += keys.Length;
        if (_pixelCount > int.MaxValue)
            throw new InvalidOperationException("A palette is computed from at most 2^31 - 1 pixels.");

        var state = GetState();
        var counts = Counts;
        var sums = unsafe(MemoryMarshal.Cast<byte, long>(state.Slice(_sumsOffset, Buckets * 3 * sizeof(long))));
        var translucentSums = TranslucentSums;
        var exact = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(_exactOffset, ExactSlots * sizeof(int))));
        var exactCounts = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(_exactCountsOffset, ExactSlots * sizeof(int))));
        // A run of equal keys is counted at once: every count and sum it changes grows by the length of the run
        for (var i = 0; i < keys.Length;)
        {
            var key = keys[i];
            var run = 1;
            while (i + run < keys.Length && keys[i + run] == key)
            {
                run++;
            }

            i += run;
            if (key == TransparentKey)
            {
                _hasTransparency = true;
                continue;
            }

            if ((key >>> 24) != 0)
            {
                if (!_translucent)
                    throw new ArgumentException("The quantizer was not created for translucent colors.", nameof(keys));

                var alpha = key >>> 24;
                var translucent = GetTranslucentBucket(key);
                counts[Buckets + translucent] += run;
                translucentSums[4 * translucent] += (long)((key >> 16) & 0xFF) * alpha * run;
                translucentSums[(4 * translucent) + 1] += (long)((key >> 8) & 0xFF) * alpha * run;
                translucentSums[(4 * translucent) + 2] += (long)(key & 0xFF) * alpha * run;
                translucentSums[(4 * translucent) + 3] += (long)alpha * run;
            }
            else
            {
                var bucket = GetBucket(key);
                counts[bucket] += run;
                sums[3 * bucket] += ((key >> 16) & 0xFF) * run;
                sums[(3 * bucket) + 1] += ((key >> 8) & 0xFF) * run;
                sums[(3 * bucket) + 2] += (key & 0xFF) * run;
            }

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

            exactCounts[slot] += run;
        }
    }

    /// <summary>Builds the palette of the frame from its histogram; the transparent entry is reserved by the caller.</summary>
    /// <param name="maxColors">The largest number of palette colors (at most the value given to <see cref="Begin"/>).</param>
    public void BuildPalette(int maxColors) => BuildPalette(maxColors, includeTransparent: false);

    /// <summary>Builds the palette of the frame from its histogram.</summary>
    /// <param name="maxColors">The largest number of palette entries (at most the value given to <see cref="Begin"/>).</param>
    /// <param name="includeTransparent">
    /// <see langword="true"/> to make the transparent entry part of the palette when the histogram has transparent pixels
    /// (it then takes one of the <paramref name="maxColors"/> entries, and translucent colors may map to it).
    /// </param>
    public void BuildPalette(int maxColors, bool includeTransparent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxColors, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxColors, _limit);
        EnsureHistogram();
        var transparent = includeTransparent && _hasTransparency;
        if (transparent && maxColors < 2)
            throw new ArgumentOutOfRangeException(nameof(maxColors), maxColors, "A palette with a transparent entry has at least two entries.");

        var colorCount = transparent ? maxColors - 1 : maxColors;
        var state = GetState();
        var exact = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(_exactOffset, ExactSlots * sizeof(int))));
        var counts = Counts;
        var sums = unsafe(MemoryMarshal.Cast<byte, long>(state.Slice(_sumsOffset, Buckets * 3 * sizeof(long))));
        var translucentSums = TranslucentSums;
        var bucketCount = _bucketCount;
        _exactMode = false;
        if (!_exactOverflow)
        {
            if (_exactCount <= colorCount)
            {
                var count = 0;
                foreach (var key in exact)
                {
                    if (key != TransparentKey)
                    {
                        _palette[count++] = key;
                    }
                }

                if (transparent)
                {
                    _palette[count++] = TransparentKey;
                }

                _isExact = true;
                SetPalette(count);
                return;
            }

            // Every color is known exactly but there are too many (the transparent entry took a slot): the median cut runs
            // on the exact colors, the histogram arrays being reused with one item per exact-set slot
            var exactCounts = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(_exactCountsOffset, ExactSlots * sizeof(int))));
            bucketCount = ExactSlots;
            _exactMode = true;
            for (var slot = 0; slot < ExactSlots; slot++)
            {
                var key = exact[slot];
                var n = key == TransparentKey ? 0 : exactCounts[slot];
                counts[slot] = n;
                if (IsTranslucentKey(key))
                {
                    long alpha = key >>> 24;
                    translucentSums[4 * slot] = ((key >> 16) & 0xFF) * alpha * n;
                    translucentSums[(4 * slot) + 1] = ((key >> 8) & 0xFF) * alpha * n;
                    translucentSums[(4 * slot) + 2] = (key & 0xFF) * alpha * n;
                    translucentSums[(4 * slot) + 3] = alpha * n;
                }
                else
                {
                    sums[3 * slot] = (long)((key >> 16) & 0xFF) * n;
                    sums[(3 * slot) + 1] = (long)((key >> 8) & 0xFF) * n;
                    sums[(3 * slot) + 2] = (long)(key & 0xFF) * n;
                }
            }
        }

        _isExact = false;
        var items = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(_itemsOffset, _bucketCount * sizeof(int))));
        var colors = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(_colorsOffset, _bucketCount * sizeof(int))));
        var sortKeys = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(_sortKeysOffset, _bucketCount * sizeof(int))));

        // Items: the non-empty buckets, opaque ones first, in bucket order, at their mean color (kept by bucket: the items are
        // reordered). An opaque item has the alpha 255; a translucent one has its premultiplied mean color and its mean alpha
        var itemCount = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var bucket = 0; bucket < bucketCount; bucket++)
            {
                var n = counts[bucket];
                if (n == 0 || IsTranslucentItem(bucket, exact) != (pass == 1))
                    continue;

                items[itemCount] = bucket;
                if (pass == 0)
                {
                    colors[bucket] = unchecked((int)0xFF00_0000) | (Mean(sums[3 * bucket], n) << 16) | (Mean(sums[(3 * bucket) + 1], n) << 8) | Mean(sums[(3 * bucket) + 2], n);
                }
                else
                {
                    var t = 4 * (bucket - (_exactMode ? 0 : Buckets));
                    var weight = (long)n * byte.MaxValue;
                    colors[bucket] = (Mean(translucentSums[t + 3], n) << 24) | (Mean(translucentSums[t], weight) << 16) | (Mean(translucentSums[t + 1], weight) << 8) | Mean(translucentSums[t + 2], weight);
                }

                itemCount++;
            }

            if (!_translucent)
                break;
        }

        // The initial boxes: the opaque items, then the translucent ones (one box for both when a single color is requested)
        var opaqueItems = 0;
        while (opaqueItems < itemCount && !IsTranslucentItem(items[opaqueItems], exact))
        {
            opaqueItems++;
        }

        var boxes = 0;
        if (opaqueItems == itemCount || opaqueItems == 0 || colorCount == 1)
        {
            AddBox(ref boxes, 0, itemCount, translucent: opaqueItems != itemCount, counts, items, colors);
        }
        else
        {
            AddBox(ref boxes, 0, opaqueItems, translucent: false, counts, items, colors);
            AddBox(ref boxes, opaqueItems, itemCount, translucent: true, counts, items, colors);
        }

        while (boxes < colorCount)
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
            var shift = GetShift(_boxAxis[selected]);

            // Sort the items of the box along the axis (keys are unique: coordinate, then position in bucket order)
            for (var i = start; i < end; i++)
            {
                sortKeys[i] = (((colors[items[i]] >> shift) & 0xFF) << 16) | items[i];
            }

            SortKeys(sortKeys[start..end], items[start..end]);
            for (var i = start; i < end; i++)
            {
                items[i] = sortKeys[i] & 0xFFFF;
            }

            var split = FindSplit(counts, items, colors, start, end, shift);
            var translucent = _boxTranslucent[selected];
            _boxEnd[selected] = split;
            _boxError[selected] = ComputeError(counts, items, colors, start, split, translucent, out _boxAxis[selected]);
            _boxStart[boxes] = split;
            _boxEnd[boxes] = end;
            _boxTranslucent[boxes] = translucent;
            _boxError[boxes] = ComputeError(counts, items, colors, split, end, translucent, out _boxAxis[boxes]);
            boxes++;
        }

        // Each item starts in its box, then the palette is refined by k-means: the items move to their nearest entry of their
        // class and the entries to the weighted mean of their items, until no item moves (at most RefinementPasses times)
        var owners = sortKeys;
        for (var box = 0; box < boxes; box++)
        {
            for (var i = _boxStart[box]; i < _boxEnd[box]; i++)
            {
                owners[i] = box;
            }
        }

        Span<int> coordinates = stackalloc int[256 * 4];
        Span<bool> entryTranslucent = stackalloc bool[256];
        ComputeEntries(boxes, itemCount, counts, items, owners, sums, translucentSums, exact, coordinates, entryTranslucent);
        if (boxes > 1 && colorCount > 1)
        {
            for (var pass = 0; pass < RefinementPasses; pass++)
            {
                if (!AssignItems(boxes, itemCount, items, colors, exact, owners, coordinates, entryTranslucent))
                    break;

                ComputeEntries(boxes, itemCount, counts, items, owners, sums, translucentSums, exact, coordinates, entryTranslucent);
            }
        }

        if (transparent)
        {
            _palette[boxes++] = TransparentKey;
        }

        SetPalette(boxes);
    }

    /// <summary>
    /// Uses a fixed palette instead of building one. When the histogram of the frame is still available, the palette is exact
    /// (<see cref="IsExact"/>) when it holds every color of the frame (the transparent pixels need a transparent entry, unless
    /// <paramref name="reservedTransparent"/>).
    /// </summary>
    /// <param name="keys">The entries, 1 to 256 keys (duplicates are merged; translucent keys only when <see cref="IsTranslucent"/>).</param>
    /// <param name="reservedTransparent"><see langword="true"/> when the caller maps the transparent pixels to an entry of its own.</param>
    public void SetPalette(ReadOnlySpan<int> keys, bool reservedTransparent = false)
    {
        ArgumentOutOfRangeException.ThrowIfZero(keys.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keys.Length, _palette.Length);
        if (!_translucent)
        {
            foreach (var key in keys)
            {
                if (IsTranslucentKey(key))
                    throw new ArgumentException("The quantizer was not created for translucent colors.", nameof(keys));
            }
        }

        var exact = false;
        if (_hasHistogram && !_exactOverflow)
        {
            exact = !_hasTransparency || reservedTransparent || keys.Contains(TransparentKey);
            var slots = unsafe(MemoryMarshal.Cast<byte, int>(GetState().Slice(_exactOffset, ExactSlots * sizeof(int))));
            foreach (var key in slots)
            {
                if (key != TransparentKey && !keys.Contains(key))
                {
                    exact = false;
                    break;
                }
            }
        }

        keys.CopyTo(_palette);
        _isExact = exact;
        SetPalette(keys.Length);
    }

    /// <summary>
    /// Gets the palette index of any color key: the transparent entry (or the nearest entry when the palette has none) for
    /// <see cref="TransparentKey"/>, <see cref="Map"/> for an opaque color, <see cref="FindNearestRgba"/> for a translucent one.
    /// </summary>
    /// <param name="key">A color key.</param>
    /// <returns>The palette index.</returns>
    public int MapAny(int key)
    {
        if (key == TransparentKey)
            return _transparentIndex >= 0 ? _transparentIndex : FindNearestRgba(key);

        return IsTranslucentKey(key) ? FindNearestRgba(key) : Map(key);
    }

    /// <summary>Gets the palette index of a color: its exact entry, or the nearest opaque entry (smallest squared distance, lowest index).</summary>
    /// <param name="key">An opaque color key.</param>
    /// <returns>The palette index.</returns>
    public int Map(int key)
    {
        if (_opaqueCount == 0)
            return FindNearestRgba(key);

        var cells = GetState().Slice(CellsOffset, Buckets * CellStride);
        var offset = GetBucket(key) * CellStride;
        ref var cell = ref cells[offset];
        int count = cell;
        if (count == 0)
        {
            count = BuildCell(key, cells.Slice(offset, CellStride));
        }

        var red = (key >> 16) & 0xFF;
        var green = (key >> 8) & 0xFF;
        var blue = key & 0xFF;
        if (count == CellOverflow)
            return _lastIndex = FindNearest(red, green, blue, _lastIndex);

        // The smallest (distance, index) pair over the candidates, as one number: a distance is at most 3 * 255^2 (18 bits).
        // The candidates are evaluated four at a time without a branch (the list is padded with its last entry): which one
        // is the nearest is not predictable, and most cells have at most four of them
        ref var reds = ref unsafe(MemoryMarshal.GetArrayDataReference(_paletteRed));
        ref var greens = ref unsafe(MemoryMarshal.GetArrayDataReference(_paletteGreen));
        ref var blues = ref unsafe(MemoryMarshal.GetArrayDataReference(_paletteBlue));
        var best = int.MaxValue;
        var i = 1;
        do
        {
            for (var end = i + CellGroup; i < end; i++)
            {
                int index = unsafe(Unsafe.Add(ref cell, i));
                var dr = unsafe(Unsafe.Add(ref reds, index)) - red;
                var dg = unsafe(Unsafe.Add(ref greens, index)) - green;
                var db = unsafe(Unsafe.Add(ref blues, index)) - blue;
                var candidate = ((((dr * dr) + (dg * dg) + (db * db)) << 8) | index) - best;

                // best = min(best, candidate) with a mask instead of a branch
                best += candidate & (candidate >> 31);
            }
        }
        while (i <= count);

        // The entry of this pixel bounds the search of the next cell: neighboring pixels have close colors
        return _lastIndex = best & 0xFF;
    }

    /// <summary>
    /// Finds the palette entries that can be the nearest entry of a color of the 5-bit cell of <paramref name="key"/>, and
    /// stores them in <paramref name="cell"/> (the count, then the indexes, the last one repeated up to a multiple of
    /// <see cref="CellGroup"/>).
    /// </summary>
    /// <returns>The number of stored indexes, or <see cref="CellOverflow"/> when the cell cannot hold the candidates (its colors are searched one by one).</returns>
    /// <remarks>
    /// Every color of the cell is at most as far from an entry as the farthest corner of the cell is: the smallest of these
    /// distances over the entries bounds the distance of the nearest entry of any color of the cell. An entry whose
    /// distance to the cell (to its nearest point) exceeds that bound is never the nearest one, not even in a tie; the
    /// others are the candidates. Only the entries whose red sample is close enough to the cell are examined (the palette
    /// is sorted by red first), starting from the bound given by the entry of the previous pixel.
    /// </remarks>
    private int BuildCell(int key, Span<byte> cell)
    {
        var lowRed = (key >> 16) & 0xF8;
        var lowGreen = (key >> 8) & 0xF8;
        var lowBlue = key & 0xF8;

        // The bound: the smallest distance between an entry and its farthest corner of the cell
        var bound = GetFarthestCellDistance(_lastIndex, lowRed, lowGreen, lowBlue);
        var (first, last) = GetRedWindow(lowRed, bound);
        bound = UseVectors
            ? GetCellBoundVector(first, last, lowRed, lowGreen, lowBlue, bound)
            : GetCellBoundScalar(first, last, lowRed, lowGreen, lowBlue, bound);

        // The candidates: the entries at most as far from the cell as the bound
        (first, last) = GetRedWindow(lowRed, bound);
        var candidates = UseVectors
            ? AddCellCandidatesVector(first, last, lowRed, lowGreen, lowBlue, bound, cell)
            : AddCellCandidatesScalar(first, last, lowRed, lowGreen, lowBlue, bound, cell);
        if (candidates > MaxCellCandidates)
        {
            cell[0] = CellOverflow;
            return CellOverflow;
        }

        // Whole groups of entries are evaluated: the last candidate is repeated
        while (candidates % CellGroup != 0)
        {
            cell[candidates + 1] = cell[candidates];
            candidates++;
        }

        cell[0] = (byte)candidates;
        return candidates;
    }

    /// <summary>Gets the entries whose red sample is at most as far from the red interval of a cell as a squared distance allows: [first, last).</summary>
    private (int First, int Last) GetRedWindow(int lowRed, int bound)
    {
        var reach = (int)Math.Sqrt(bound);
        var first = (int)_redStart[Math.Max(lowRed - reach, 0)];
        var last = lowRed + 8 + reach > 255 ? _opaqueCount : _redStart[lowRed + 8 + reach];
        return (first, last);
    }

    /// <summary>Gets the squared distance between a palette entry and the farthest point of a cell.</summary>
    private int GetFarthestCellDistance(int index, int lowRed, int lowGreen, int lowBlue)
    {
        // On one axis the cell is [0, 7] relative to its low corner
        var red = _paletteRed[index] - lowRed;
        var green = _paletteGreen[index] - lowGreen;
        var blue = _paletteBlue[index] - lowBlue;
        var farRed = Math.Max(Math.Abs(red), Math.Abs(red - 7));
        var farGreen = Math.Max(Math.Abs(green), Math.Abs(green - 7));
        var farBlue = Math.Max(Math.Abs(blue), Math.Abs(blue - 7));
        return (farRed * farRed) + (farGreen * farGreen) + (farBlue * farBlue);
    }

    /// <summary>Gets the squared distance between a palette entry and the nearest point of a cell.</summary>
    private int GetNearestCellDistance(int index, int lowRed, int lowGreen, int lowBlue)
    {
        var red = _paletteRed[index] - lowRed;
        var green = _paletteGreen[index] - lowGreen;
        var blue = _paletteBlue[index] - lowBlue;
        var nearRed = Math.Max(Math.Max(-red, red - 7), 0);
        var nearGreen = Math.Max(Math.Max(-green, green - 7), 0);
        var nearBlue = Math.Max(Math.Max(-blue, blue - 7), 0);
        return (nearRed * nearRed) + (nearGreen * nearGreen) + (nearBlue * nearBlue);
    }

    /// <summary>The scalar version of <see cref="GetCellBoundVector"/>, limited to the entries of [first, last).</summary>
    private int GetCellBoundScalar(int first, int last, int lowRed, int lowGreen, int lowBlue, int bound)
    {
        for (var index = first; index < last; index++)
        {
            bound = Math.Min(bound, GetFarthestCellDistance(index, lowRed, lowGreen, lowBlue));
        }

        return bound;
    }

    /// <summary>
    /// Lowers <paramref name="bound"/> to the smallest distance between an entry of [first, last) and its farthest corner of
    /// the cell, 16 entries at a time.
    /// </summary>
    /// <remarks>
    /// The blocks are whole: they may hold entries outside [first, last), which bound the cell as well (the entries after
    /// the last one of the palette repeat it). The distances are summed in saturating 16-bit lanes; a saturated sum is not
    /// a distance and is ignored, which leaves a valid, larger bound.
    /// </remarks>
    private int GetCellBoundVector(int first, int last, int lowRed, int lowGreen, int lowBlue, int bound)
    {
        var lowReds = Vector128.Create((byte)lowRed);
        var lowGreens = Vector128.Create((byte)lowGreen);
        var lowBlues = Vector128.Create((byte)lowBlue);
        var seven = Vector128.Create((byte)7);
        ref var reds = ref unsafe(MemoryMarshal.GetArrayDataReference(_paletteRed));
        ref var greens = ref unsafe(MemoryMarshal.GetArrayDataReference(_paletteGreen));
        ref var blues = ref unsafe(MemoryMarshal.GetArrayDataReference(_paletteBlue));
        var smallest = Vector128.Create(ushort.MaxValue);
        for (var block = first & ~15; block < last; block += 16)
        {
            var red = Farthest(unsafe(Vector128.LoadUnsafe(ref reds, (nuint)block)), lowReds, seven);
            var green = Farthest(unsafe(Vector128.LoadUnsafe(ref greens, (nuint)block)), lowGreens, seven);
            var blue = Farthest(unsafe(Vector128.LoadUnsafe(ref blues, (nuint)block)), lowBlues, seven);
            smallest = Vector128.Min(smallest, SumOfSquares(Vector128.WidenLower(red), Vector128.WidenLower(green), Vector128.WidenLower(blue)));
            smallest = Vector128.Min(smallest, SumOfSquares(Vector128.WidenUpper(red), Vector128.WidenUpper(green), Vector128.WidenUpper(blue)));
        }

        // The smallest lane: the minimum of the two halves, three times
        smallest = Vector128.Min(smallest, Vector128.Shuffle(smallest, Vector128.Create((ushort)4, 5, 6, 7, 0, 1, 2, 3)));
        smallest = Vector128.Min(smallest, Vector128.Shuffle(smallest, Vector128.Create((ushort)2, 3, 0, 1, 6, 7, 4, 5)));
        smallest = Vector128.Min(smallest, Vector128.Shuffle(smallest, Vector128.Create((ushort)1, 0, 3, 2, 5, 4, 7, 6)));
        var distance = smallest.ToScalar();
        return distance == ushort.MaxValue ? bound : Math.Min(bound, distance);

        // The larger of the distances between samples and the two ends of the cell [low, low + 7]
        static Vector128<byte> Farthest(Vector128<byte> samples, Vector128<byte> low, Vector128<byte> seven)
        {
            var high = low + seven;
            var fromLow = Vector128.Max(samples, low) - Vector128.Min(samples, low);
            var fromHigh = Vector128.Max(samples, high) - Vector128.Min(samples, high);
            return Vector128.Max(fromLow, fromHigh);
        }

        // Each square fits 16 bits (at most 255^2); their sum saturates
        static Vector128<ushort> SumOfSquares(Vector128<ushort> red, Vector128<ushort> green, Vector128<ushort> blue)
            => Vector128.AddSaturate(Vector128.AddSaturate(red * red, green * green), blue * blue);
    }

    /// <summary>The scalar version of <see cref="AddCellCandidatesVector"/>.</summary>
    private int AddCellCandidatesScalar(int first, int last, int lowRed, int lowGreen, int lowBlue, int bound, Span<byte> cell)
    {
        var candidates = 0;
        for (var index = first; index < last; index++)
        {
            if (GetNearestCellDistance(index, lowRed, lowGreen, lowBlue) > bound)
                continue;

            candidates++;
            if (candidates <= MaxCellCandidates)
            {
                cell[candidates] = (byte)index;
            }
        }

        return candidates;
    }

    /// <summary>
    /// Stores the entries of [first, last) that are at most as far from the cell as <paramref name="bound"/>, in increasing
    /// order, and returns their number (which may exceed what the cell holds). 16 entries at a time, the entries that are
    /// farther than the bound allows on one axis are discarded; the distance of the others is computed.
    /// </summary>
    private int AddCellCandidatesVector(int first, int last, int lowRed, int lowGreen, int lowBlue, int bound, Span<byte> cell)
    {
        var lowReds = Vector128.Create((byte)lowRed);
        var lowGreens = Vector128.Create((byte)lowGreen);
        var lowBlues = Vector128.Create((byte)lowBlue);
        var seven = Vector128.Create((byte)7);
        var reaches = Vector128.Create((byte)Math.Min((int)Math.Sqrt(bound), 255));
        ref var reds = ref unsafe(MemoryMarshal.GetArrayDataReference(_paletteRed));
        ref var greens = ref unsafe(MemoryMarshal.GetArrayDataReference(_paletteGreen));
        ref var blues = ref unsafe(MemoryMarshal.GetArrayDataReference(_paletteBlue));
        var candidates = 0;
        for (var block = first & ~15; block < last; block += 16)
        {
            var red = Nearest(unsafe(Vector128.LoadUnsafe(ref reds, (nuint)block)), lowReds, seven);
            var green = Nearest(unsafe(Vector128.LoadUnsafe(ref greens, (nuint)block)), lowGreens, seven);
            var blue = Nearest(unsafe(Vector128.LoadUnsafe(ref blues, (nuint)block)), lowBlues, seven);
            var near = Vector128.LessThanOrEqual(Vector128.Max(red, Vector128.Max(green, blue)), reaches).ExtractMostSignificantBits();

            // The bits of the entries of the block that are in [first, last)
            if (block < first)
            {
                near &= ~0u << (first - block);
            }

            if (block + 16 > last)
            {
                near &= (1u << (last - block)) - 1;
            }

            while (near != 0)
            {
                var index = block + BitOperations.TrailingZeroCount(near);
                near &= near - 1;
                if (GetNearestCellDistance(index, lowRed, lowGreen, lowBlue) > bound)
                    continue;

                candidates++;
                if (candidates <= MaxCellCandidates)
                {
                    cell[candidates] = (byte)index;
                }
            }
        }

        return candidates;

        // The distance on one axis between samples and the cell [low, low + 7]: one of the two differences is zero
        static Vector128<byte> Nearest(Vector128<byte> samples, Vector128<byte> low, Vector128<byte> seven)
            => Vector128.SubtractSaturate(low, samples) | Vector128.SubtractSaturate(samples, low + seven);
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
    public int FindNearest(int red, int green, int blue) => FindNearest(red, green, blue, hint: 0);

    /// <summary>Gets the palette index nearest to a color, as <see cref="FindNearest(int, int, int)"/>.</summary>
    /// <param name="red">The red sample.</param>
    /// <param name="green">The green sample.</param>
    /// <param name="blue">The blue sample.</param>
    /// <param name="hint">Any palette index, ideally of an entry close to the color (the entry of a neighboring pixel): it shortens the search and never changes its result.</param>
    /// <returns>The palette index.</returns>
    public int FindNearest(int red, int green, int blue, int hint)
    {
        if (_useSortedSearch)
            return FindNearestSorted(red, green, blue, hint);

        return Vector.IsHardwareAccelerated && Vector<int>.Count <= 16 && _opaqueCount >= Vector<int>.Count ? FindNearestVector(red, green, blue) : FindNearestScalar(red, green, blue);
    }

    /// <summary>
    /// The search of <see cref="FindNearest(int, int, int)"/> that uses the order of the palette: its entries are sorted by key, so their
    /// red samples never decrease. The search walks away from the first entry whose red sample is at least
    /// <paramref name="red"/>, in both directions, and leaves a direction as soon as the squared red difference alone
    /// excludes every further entry of it. The distance of the <paramref name="hint"/> entry bounds the walk from the start.
    /// </summary>
    /// <remarks>
    /// Upward, the indexes increase: an entry replaces the best one only when it is strictly nearer, and the walk stops at a
    /// red difference that cannot be strictly nearer. Downward, the indexes decrease: an entry at the same distance has a
    /// lower index than every entry seen so far and replaces the best one, and the walk stops at a red difference that
    /// cannot even tie. The result is therefore the entry of smallest distance and lowest index, as <see cref="FindNearestScalar"/>.
    /// The initial bound is one more than the distance of the hint entry: the walk still reaches that entry and every entry
    /// at most as far, so the bound removes no candidate.
    /// </remarks>
    internal int FindNearestSorted(int red, int green, int blue, int hint)
    {
        var count = _opaqueCount;
        ref var reds = ref unsafe(MemoryMarshal.GetArrayDataReference(_paletteRed));
        ref var greens = ref unsafe(MemoryMarshal.GetArrayDataReference(_paletteGreen));
        ref var blues = ref unsafe(MemoryMarshal.GetArrayDataReference(_paletteBlue));
        var up = (int)_redStart[red];
        var down = up - 1;
        var best = 0;
        var bestDistance = int.MaxValue;
        if ((uint)hint < (uint)count)
        {
            var dr = unsafe(Unsafe.Add(ref reds, hint)) - red;
            var dg = unsafe(Unsafe.Add(ref greens, hint)) - green;
            var db = unsafe(Unsafe.Add(ref blues, hint)) - blue;
            bestDistance = (dr * dr) + (dg * dg) + (db * db) + 1;
        }

        while (true)
        {
            var advanced = false;
            if (up < count)
            {
                var dr = unsafe(Unsafe.Add(ref reds, up)) - red;
                var distance = dr * dr;
                if (distance < bestDistance)
                {
                    var dg = unsafe(Unsafe.Add(ref greens, up)) - green;
                    var db = unsafe(Unsafe.Add(ref blues, up)) - blue;
                    distance += (dg * dg) + (db * db);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = up;
                    }

                    up++;
                    advanced = true;
                }
                else
                {
                    up = count;
                }
            }

            if (down >= 0)
            {
                var dr = red - unsafe(Unsafe.Add(ref reds, down));
                var distance = dr * dr;
                if (distance <= bestDistance)
                {
                    var dg = unsafe(Unsafe.Add(ref greens, down)) - green;
                    var db = unsafe(Unsafe.Add(ref blues, down)) - blue;
                    distance += (dg * dg) + (db * db);
                    if (distance <= bestDistance)
                    {
                        bestDistance = distance;
                        best = down;
                    }

                    down--;
                    advanced = true;
                }
                else
                {
                    down = -1;
                }
            }

            if (!advanced)
                return best;
        }
    }

    /// <summary>The scalar reference of <see cref="FindNearest(int, int, int)"/>: a linear search keeping the first entry of smallest distance.</summary>
    internal int FindNearestScalar(int red, int green, int blue)
    {
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < _opaqueCount; i++)
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
        for (var i = 0; i < _opaqueCount; i += count)
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

    /// <summary>
    /// Gets the palette index nearest to a color of any alpha, over every entry (the transparent entry included): the smallest
    /// <c>(R·A − R'·A')² + (G·A − G'·A')² + (B·A − B'·A')² + (255·(A − A'))²</c>, ties to the lowest index. The colors of the
    /// last searches are remembered by key.
    /// </summary>
    /// <param name="key">A color key.</param>
    /// <returns>The palette index.</returns>
    public int FindNearestRgba(int key)
    {
        var cacheKeys = _rgbaCacheKeys ??= CreateRgbaCache();
        var cacheIndexes = _rgbaCacheIndexes!;
        var slot = (int)(((uint)key * 0x9E3779B1u) >> 20);
        if (cacheKeys[slot] == key)
            return cacheIndexes[slot];

        var alpha = GetAlpha(key);
        var red = ((key >> 16) & 0xFF) * alpha;
        var green = ((key >> 8) & 0xFF) * alpha;
        var blue = (key & 0xFF) * alpha;
        alpha *= byte.MaxValue;
        if (key == TransparentKey)
        {
            red = green = blue = 0;
        }

        var best = 0;
        var bestDistance = long.MaxValue;
        for (var i = 0; i < _paletteCount; i++)
        {
            long dr = red - _premultiplied[4 * i];
            long dg = green - _premultiplied[(4 * i) + 1];
            long db = blue - _premultiplied[(4 * i) + 2];
            long da = alpha - _premultiplied[(4 * i) + 3];
            var distance = (dr * dr) + (dg * dg) + (db * db) + (da * da);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        cacheKeys[slot] = key;
        cacheIndexes[slot] = (byte)best;
        return best;
    }

    public void Dispose()
    {
        _state?.Dispose();
        _state = null;
    }

    private int[] CreateRgbaCache()
    {
        var keys = new int[RgbaCacheLength];
        keys.AsSpan().Fill(NoKey);
        _rgbaCacheIndexes = new byte[RgbaCacheLength];
        return keys;
    }

    /// <summary>Gets the shift of the sample of an axis (0 R, 1 G, 2 B, 3 A) in a color.</summary>
    private static int GetShift(int axis) => axis == 3 ? 24 : 16 - (8 * axis);

    /// <summary>Gets the translucent histogram bucket of a translucent key: <c>(A3 &lt;&lt; 12) | (R4 &lt;&lt; 8) | (G4 &lt;&lt; 4) | B4</c>.</summary>
    private static int GetTranslucentBucket(int key) => (int)(((uint)key >> 29) << 12) | ((key >> 12) & 0x0F00) | ((key >> 8) & 0x00F0) | ((key >> 4) & 0x000F);

    private static int Mean(long sum, long count) => (int)((sum + (count / 2)) / count);

    private static int HashExact(int key) => (int)(((uint)key * 0x9E3779B1u) >> 22);

    /// <summary>
    /// Computes the palette entries from the items they own (exact 8-bit sums), alpha-weighted for the translucent colors, as
    /// palette keys in <c>_palette</c> and as item coordinates (straight samples for an opaque entry, samples premultiplied by
    /// alpha and the alpha for a translucent one). An entry that owns no item keeps its color.
    /// </summary>
    private void ComputeEntries(int entries, int itemCount, ReadOnlySpan<int> counts, ReadOnlySpan<int> items, ReadOnlySpan<int> owners, ReadOnlySpan<long> sums, ReadOnlySpan<long> translucentSums, ReadOnlySpan<int> exact, Span<int> coordinates, Span<bool> entryTranslucent)
    {
        Span<long> totals = stackalloc long[256 * 8];
        totals[..(entries * 8)].Clear();
        for (var i = 0; i < itemCount; i++)
        {
            var item = items[i];
            var total = totals.Slice(owners[i] * 8, 8);
            long count = counts[item];
            total[0] += count;
            if (IsTranslucentItem(item, exact))
            {
                var t = 4 * (item - (_exactMode ? 0 : Buckets));
                total[4] += translucentSums[t];
                total[5] += translucentSums[t + 1];
                total[6] += translucentSums[t + 2];
                total[7] += translucentSums[t + 3];
            }
            else
            {
                total[1] += sums[3 * item];
                total[2] += sums[(3 * item) + 1];
                total[3] += sums[(3 * item) + 2];
                total[4] += sums[3 * item] * byte.MaxValue;
                total[5] += sums[(3 * item) + 1] * byte.MaxValue;
                total[6] += sums[(3 * item) + 2] * byte.MaxValue;
                total[7] += count * byte.MaxValue;
            }
        }

        for (var entry = 0; entry < entries; entry++)
        {
            var total = totals.Slice(entry * 8, 8);
            var n = total[0];
            if (n == 0)
                continue;

            var coordinate = coordinates.Slice(entry * 4, 4);
            if (total[7] == n * byte.MaxValue)
            {
                // Opaque colors only: the exact means of the 8-bit samples
                _palette[entry] = (Mean(total[1], n) << 16) | (Mean(total[2], n) << 8) | Mean(total[3], n);
                coordinate[0] = Mean(total[1], n);
                coordinate[1] = Mean(total[2], n);
                coordinate[2] = Mean(total[3], n);
                coordinate[3] = byte.MaxValue;
                entryTranslucent[entry] = false;
            }
            else
            {
                var weight = n * byte.MaxValue;
                _palette[entry] = GetKey(Math.Min(Mean(total[4], total[7]), byte.MaxValue), Math.Min(Mean(total[5], total[7]), byte.MaxValue), Math.Min(Mean(total[6], total[7]), byte.MaxValue), Mean(total[7], n));
                coordinate[0] = Mean(total[4], weight);
                coordinate[1] = Mean(total[5], weight);
                coordinate[2] = Mean(total[6], weight);
                coordinate[3] = Mean(total[7], n);
                entryTranslucent[entry] = true;
            }
        }
    }

    /// <summary>
    /// Moves every item to the entry of its class (opaque or translucent) nearest to its color: the smallest squared distance
    /// of the coordinates, ties to the current entry, then to the lowest index.
    /// </summary>
    /// <returns><see langword="true"/> when an item moved.</returns>
    private bool AssignItems(int entries, int itemCount, ReadOnlySpan<int> items, ReadOnlySpan<int> colors, ReadOnlySpan<int> exact, Span<int> owners, ReadOnlySpan<int> coordinates, ReadOnlySpan<bool> entryTranslucent)
    {
        var moved = false;
        for (var i = 0; i < itemCount; i++)
        {
            var item = items[i];
            var color = colors[item];
            var translucent = IsTranslucentItem(item, exact);
            var r = (color >> 16) & 0xFF;
            var g = (color >> 8) & 0xFF;
            var b = color & 0xFF;
            var a = (color >>> 24) & 0xFF;
            var best = owners[i];
            var bestDistance = Distance(coordinates.Slice(best * 4, 4), r, g, b, a);
            for (var entry = 0; entry < entries && bestDistance > 0; entry++)
            {
                if (entryTranslucent[entry] != translucent || entry == best)
                    continue;

                var distance = Distance(coordinates.Slice(entry * 4, 4), r, g, b, a);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = entry;
                }
            }

            if (best != owners[i])
            {
                owners[i] = best;
                moved = true;
            }
        }

        return moved;

        static int Distance(ReadOnlySpan<int> entry, int r, int g, int b, int a)
        {
            var dr = entry[0] - r;
            var dg = entry[1] - g;
            var db = entry[2] - b;
            var da = entry[3] - a;
            return (dr * dr) + (dg * dg) + (db * db) + (da * da);
        }
    }

    /// <summary>
    /// Computes the weighted sum of squared errors of a box around its mean, and the axis (0 R, 1 G, 2 B, and 3 A for a box of
    /// translucent colors) of largest error.
    /// </summary>
    private static double ComputeError(ReadOnlySpan<int> counts, ReadOnlySpan<int> items, ReadOnlySpan<int> colors, int start, int end, bool translucent, out int axis)
    {
        var axes = translucent ? 4 : 3;
        Span<long> sum = stackalloc long[4];
        Span<long> squares = stackalloc long[4];
        long n = 0;
        for (var i = start; i < end; i++)
        {
            var item = items[i];
            long weight = counts[item];
            var color = colors[item];
            n += weight;
            for (var c = 0; c < axes; c++)
            {
                long value = (color >> GetShift(c)) & 0xFF;
                sum[c] += weight * value;
                squares[c] += weight * value * value;
            }
        }

        axis = 0;
        var total = 0d;
        var largest = -1d;
        for (var c = 0; c < axes; c++)
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
    private static int FindSplit(ReadOnlySpan<int> counts, ReadOnlySpan<int> items, ReadOnlySpan<int> colors, int start, int end, int shift)
    {
        long totalCount = 0, totalSum = 0;
        for (var i = start; i < end; i++)
        {
            long weight = counts[items[i]];
            totalCount += weight;
            totalSum += weight * ((colors[items[i]] >> shift) & 0xFF);
        }

        long leftCount = 0, leftSum = 0;
        var best = -1;
        var bestScore = double.NegativeInfinity;
        for (var i = start; i < end - 1; i++)
        {
            long weight = counts[items[i]];
            var value = (colors[items[i]] >> shift) & 0xFF;
            leftCount += weight;
            leftSum += weight * value;
            if (value == ((colors[items[i + 1]] >> shift) & 0xFF))
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

    /// <summary>
    /// Sorts unique 23-bit keys. Long runs are sorted by their three bytes, least significant first, each pass being a
    /// stable counting sort (the result is the one of any sort: the keys are unique).
    /// </summary>
    /// <param name="keys">The keys; sorted in place.</param>
    /// <param name="scratch">Scratch space of the same length.</param>
    internal static void SortKeys(Span<int> keys, Span<int> scratch)
    {
        const int RadixThreshold = 256;
        if (keys.Length < RadixThreshold)
        {
            keys.Sort();
            return;
        }

        Span<int> starts = stackalloc int[3 * 256];
        starts.Clear();
        foreach (var key in keys)
        {
            starts[key & 0xFF]++;
            starts[256 + ((key >> 8) & 0xFF)]++;
            starts[512 + ((key >> 16) & 0xFF)]++;
        }

        // Counts to the position of the first key of each byte value
        for (var pass = 0; pass < 3; pass++)
        {
            var position = 0;
            foreach (ref var start in starts.Slice(pass * 256, 256))
            {
                (start, position) = (position, position + start);
            }
        }

        Scatter(keys, scratch, starts[..256], 0);
        Scatter(scratch, keys, starts.Slice(256, 256), 8);
        Scatter(keys, scratch, starts.Slice(512, 256), 16);
        scratch.CopyTo(keys);

        static void Scatter(ReadOnlySpan<int> source, Span<int> destination, Span<int> starts, int shift)
        {
            foreach (var key in source)
            {
                destination[starts[(key >> shift) & 0xFF]++] = key;
            }
        }
    }

    private void SetPalette(int count)
    {
        // Sorted as unsigned keys: the opaque entries (high byte 0), the translucent ones, then the transparent entry (all ones)
        var palette = unsafe(MemoryMarshal.Cast<int, uint>(_palette.AsSpan(0, count)));
        palette.Sort();
        var total = 0;
        for (var i = 0; i < palette.Length; i++)
        {
            if (total == 0 || palette[total - 1] != palette[i])
            {
                palette[total++] = palette[i];
            }
        }

        _paletteCount = total;
        PaletteVersion++;
        var unique = 0;
        while (unique < total && (_palette[unique] >>> 24) == 0)
        {
            unique++;
        }

        _opaqueCount = unique;
        _transparentIndex = total > 0 && _palette[total - 1] == TransparentKey ? total - 1 : -1;
        for (var i = 0; i < total; i++)
        {
            var key = _palette[i];
            var alpha = GetAlpha(key);
            _premultiplied[4 * i] = key == TransparentKey ? 0 : ((key >> 16) & 0xFF) * alpha;
            _premultiplied[(4 * i) + 1] = key == TransparentKey ? 0 : ((key >> 8) & 0xFF) * alpha;
            _premultiplied[(4 * i) + 2] = key == TransparentKey ? 0 : (key & 0xFF) * alpha;
            _premultiplied[(4 * i) + 3] = alpha * byte.MaxValue;
        }

        _rgbaCacheKeys?.AsSpan().Fill(NoKey);
        for (var i = 0; i < unique; i++)
        {
            _paletteRed[i] = (byte)(_palette[i] >> 16);
            _paletteGreen[i] = (byte)(_palette[i] >> 8);
            _paletteBlue[i] = (byte)_palette[i];
        }

        // The entries after the last one repeat it: whole vectors of entries can be read (they never hold another color)
        if (unique > 0)
        {
            _paletteRed.AsSpan(unique).Fill(_paletteRed[unique - 1]);
            _paletteGreen.AsSpan(unique).Fill(_paletteGreen[unique - 1]);
            _paletteBlue.AsSpan(unique).Fill(_paletteBlue[unique - 1]);
        }

        var entry = 0;
        for (var value = 0; value < 256; value++)
        {
            while (entry < unique && _paletteRed[entry] < value)
            {
                entry++;
            }

            _redStart[value] = (ushort)entry;
        }

        // The sorted search needs red samples that separate the entries; otherwise every entry is compared at once
        var distinctReds = 0;
        for (var i = 0; i < unique; i++)
        {
            if (i == 0 || _paletteRed[i] != _paletteRed[i - 1])
            {
                distinctReds++;
            }
        }

        _useSortedSearch = unique <= 4 * distinctReds;
        _lastIndex = 0;

        // The cells of the new palette take the memory of the histogram, which is complete
        _hasHistogram = false;
        GetState().Slice(CellsOffset, Buckets * CellStride).Clear();
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

    private void EnsureHistogram()
    {
        if (!_hasHistogram)
            throw new InvalidOperationException("The histogram of the frame is released once its palette is built.");
    }

    private Span<int> Counts => unsafe(MemoryMarshal.Cast<byte, int>(GetState().Slice(CountsOffset, _bucketCount * sizeof(int))));

    private Span<long> TranslucentSums => _translucent ? unsafe(MemoryMarshal.Cast<byte, long>(GetState().Slice(_translucentSumsOffset, Buckets * 4 * sizeof(long)))) : default;

    /// <summary>Gets a value indicating whether an item of the median cut (a bucket, or an exact-set slot) holds translucent colors.</summary>
    private bool IsTranslucentItem(int item, ReadOnlySpan<int> exact) => _exactMode ? IsTranslucentKey(exact[item]) : item >= Buckets;

    private void AddBox(ref int boxes, int start, int end, bool translucent, ReadOnlySpan<int> counts, ReadOnlySpan<int> items, ReadOnlySpan<int> colors)
    {
        _boxStart[boxes] = start;
        _boxEnd[boxes] = end;
        _boxTranslucent[boxes] = translucent;
        _boxError[boxes] = ComputeError(counts, items, colors, start, end, translucent, out _boxAxis[boxes]);
        boxes++;
    }

    private Span<int> GetVectorPalette() => unsafe(MemoryMarshal.Cast<byte, int>(GetState().Slice(_vectorPaletteOffset, 3 * VectorPlaneLength * sizeof(int))));

    private Span<byte> GetState() => (_state ?? throw new ObjectDisposedException(nameof(PaletteQuantizer))).RawBuffer.AsSpan(0, _stateLength);
}
