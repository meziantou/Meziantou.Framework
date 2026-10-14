using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The WebP lossless (VP8L) encoder, written from the WebP Lossless Bitstream specification (the inverse of
/// <see cref="Vp8LDecoder"/>). Every sample is preserved exactly.
/// </summary>
/// <remarks>
/// <para>Tools, by effort (0 fastest to 9 smallest):</para>
/// <list type="bullet">
/// <item><description>Images with at most 256 distinct colors use the color indexing transform (sorted color table, pixel bundling for 16 colors or fewer).</description></item>
/// <item><description>Other images use subtract green, then the predictor transform (64x64 tiles): the select predictor everywhere at effort 0, otherwise the best of 4 modes (effort 1 and 2) or of the 14 modes per tile, chosen by the smallest sum of absolute residuals (entropy of the residuals from effort 6; at effort 9 the encoder tries both criteria and keeps the smaller stream), then (effort 3 and above) the color transform per tile, each multiplier chosen by the entropy of the transformed channel.</description></item>
/// <item><description>
/// The image data is parsed into literals and LZ77 backward references with hash chains (deeper chains and lazy matching
/// with the effort), within the distance range of the format; the color cache size is chosen among candidates by the
/// estimated entropy of the result. One prefix code group is used (no entropy image). Codes are length-limited Huffman codes.
/// </description></item>
/// </list>
/// <para>
/// Memory (all <see cref="AllocationKind.Temporary"/>, from the writer scope): the ARGB image (transformed in place), the hash
/// chains (4 bytes per pixel), the subresolution images and the output bit stream. The parse runs twice (statistics, then
/// emission) instead of storing the tokens.
/// </para>
/// </remarks>
internal sealed class Vp8LEncoder : IDisposable
{
    private const int MaxLength = 4096;
    private const int MinLength = 3;
    private const int NumLiteralCodes = 256;
    private const int NumLengthCodes = 24;
    private const int NumDistanceCodes = 40;
    private const int MaxDistance = 1048576 - 120;

    private static ReadOnlySpan<byte> CodeLengthCodeOrder => [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    private readonly AllocationScope _scope;
    private readonly LsbBitWriter _writer;
    private readonly int _effort;
    private readonly bool _entropyPredictors;
    private readonly CancellationToken _cancellationToken;
    private readonly List<PooledBuffer> _buffers = [];

    private Vp8LEncoder(AllocationScope scope, LsbBitWriter writer, int effort, bool entropyPredictors, CancellationToken cancellationToken)
    {
        _scope = scope;
        _writer = writer;
        _effort = effort;
        _entropyPredictors = entropyPredictors;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Encodes a VP8L bitstream (header included).</summary>
    /// <param name="scope">The scope charged for the working memory.</param>
    /// <param name="writer">The destination.</param>
    /// <param name="argb">The pixels (<c>0xAARRGGBB</c>), modified in place.</param>
    /// <param name="width">The width (1 to 16,384).</param>
    /// <param name="height">The height (1 to 16,384).</param>
    /// <param name="effort">The effort, 0 to 9.</param>
    /// <param name="cancellationToken">Checked between rows and passes.</param>
    /// <param name="entropyPredictors">
    /// Whether the predictor modes are chosen by the entropy of the residuals instead of their sum of absolute values;
    /// <see langword="null"/> selects it from the effort (6 and above).
    /// </param>
    public static void Encode(AllocationScope scope, LsbBitWriter writer, Span<uint> argb, int width, int height, int effort, CancellationToken cancellationToken, bool? entropyPredictors = null)
    {
        var alphaIsUsed = false;
        foreach (var pixel in argb)
        {
            if (pixel < 0xFF000000u)
            {
                alphaIsUsed = true;
                break;
            }
        }

        writer.WriteBits(Vp8LDecoder.Signature, 8);
        writer.WriteBits((uint)(width - 1), 14);
        writer.WriteBits((uint)(height - 1), 14);
        writer.WriteBits(alphaIsUsed ? 1u : 0u, 1);
        writer.WriteBits(0, 3);
        using var encoder = new Vp8LEncoder(scope, writer, effort, entropyPredictors ?? effort >= 6, cancellationToken);
        encoder.EncodeMainImage(argb, width, height);
    }

    /// <summary>Encodes a headerless image stream of known dimensions (the lossless compression of an <c>ALPH</c> chunk).</summary>
    public static void EncodeImageStream(AllocationScope scope, LsbBitWriter writer, Span<uint> argb, int width, int height, int effort, CancellationToken cancellationToken)
    {
        using var encoder = new Vp8LEncoder(scope, writer, effort, effort >= 6, cancellationToken);
        encoder.EncodeMainImage(argb, width, height);
    }

    public void Dispose()
    {
        foreach (var buffer in _buffers)
        {
            buffer.Dispose();
        }

        _buffers.Clear();
    }

    private Span<uint> RentPixels(long count)
    {
        var bytes = count * sizeof(uint);
        if (bytes > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_scope.Limits);

        var buffer = _scope.Rent((int)Math.Max(bytes, sizeof(uint)), AllocationKind.Temporary, clear: true);
        _buffers.Add(buffer);
        return unsafe(MemoryMarshal.Cast<byte, uint>(buffer.RawBuffer.AsSpan()))[..(int)count];
    }

    private void EncodeMainImage(Span<uint> argb, int width, int height)
    {
        if (TryBuildPalette(argb, out var palette))
        {
            // Color indexing: the color table (subtraction coded), then the bundled indexes
            var size = palette.Length;
            var widthBits = size <= 2 ? 3 : size <= 4 ? 2 : size <= 16 ? 1 : 0;
            _writer.WriteBits(1, 1);
            _writer.WriteBits(3, 2);
            _writer.WriteBits((uint)(size - 1), 8);
            var deltas = RentPixels(size);
            deltas[0] = palette[0];
            for (var i = 1; i < size; i++)
            {
                deltas[i] = SubtractPixels(palette[i], palette[i - 1]);
            }

            EncodeSpatialImage(deltas, size, 1, isMainImage: false);
            var packedWidth = Vp8LDecoder.DivRoundUp(width, widthBits);
            var packed = RentPixels((long)packedWidth * height);
            PackIndexes(argb, width, height, palette, widthBits, packed, packedWidth);
            _writer.WriteBits(0, 1);
            EncodeSpatialImage(packed, packedWidth, height, isMainImage: true);
            return;
        }

        // Subtract green, then spatial prediction, then the color transform (applied in this order, inverted in reverse)
        _writer.WriteBits(1, 1);
        _writer.WriteBits(2, 2);
        Vp8LTransforms.SubtractGreen(argb);
        ApplyPredictor(argb, width, height);

        if (_effort >= 3)
        {
            ApplyColorTransform(argb, width, height);
        }

        _writer.WriteBits(0, 1);
        EncodeSpatialImage(argb, width, height, isMainImage: true);
    }

    private static bool TryBuildPalette(ReadOnlySpan<uint> argb, out uint[] palette)
    {
        // The distinct colors, up to 257: a pixel equal to the previous one is skipped (runs are common)
        var colors = new ColorTable();
        var count = 0;
        var previous = 0u;
        for (var i = 0; i < argb.Length; i++)
        {
            var pixel = argb[i];
            if (i > 0 && pixel == previous)
                continue;

            previous = pixel;
            if (colors.Add(pixel, count) && ++count > 256)
            {
                palette = [];
                return false;
            }
        }

        palette = colors.GetColors(count);
        Array.Sort(palette);
        return true;
    }

    /// <summary>An open-addressing table of at most 257 colors and their values, with 1,024 slots.</summary>
    private sealed class ColorTable
    {
        private const int SlotBits = 10;
        private readonly uint[] _colors = new uint[1 << SlotBits];
        private readonly int[] _values = new int[1 << SlotBits];
        private readonly bool[] _used = new bool[1 << SlotBits];

        /// <summary>Adds a color with its value.</summary>
        /// <returns><see langword="true"/> when the color was not in the table.</returns>
        public bool Add(uint color, int value)
        {
            var slot = Find(color);
            if (_used[slot])
                return false;

            _used[slot] = true;
            _colors[slot] = color;
            _values[slot] = value;
            return true;
        }

        /// <summary>Gets the value of a color of the table.</summary>
        public int GetValue(uint color) => _values[Find(color)];

        public uint[] GetColors(int count)
        {
            var colors = new uint[count];
            var index = 0;
            for (var slot = 0; slot < _used.Length; slot++)
            {
                if (_used[slot])
                {
                    colors[index++] = _colors[slot];
                }
            }

            return colors;
        }

        // The slot of the color, or the free slot where it would be inserted
        private int Find(uint color)
        {
            var mask = _used.Length - 1;
            var slot = (int)((color * 0x9E3779B1u) >> (32 - SlotBits));
            while (_used[slot] && _colors[slot] != color)
            {
                slot = (slot + 1) & mask;
            }

            return slot;
        }
    }

    private void PackIndexes(ReadOnlySpan<uint> argb, int width, int height, uint[] palette, int widthBits, Span<uint> packed, int packedWidth)
    {
        var lookup = new ColorTable();
        for (var i = 0; i < palette.Length; i++)
        {
            lookup.Add(palette[i], i);
        }

        var previousColor = palette[0];
        var previousIndex = 0;

        var bitsPerIndex = 8 >> widthBits;
        var perPixel = 1 << widthBits;
        for (var y = 0; y < height; y++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var row = argb.Slice(y * width, width);
            var output = packed.Slice(y * packedWidth, packedWidth);
            for (var x = 0; x < packedWidth; x++)
            {
                uint value = 0;
                for (var k = 0; k < perPixel; k++)
                {
                    var source = (x << widthBits) + k;
                    if (source < width)
                    {
                        var color = row[source];
                        if (color != previousColor)
                        {
                            previousColor = color;
                            previousIndex = lookup.GetValue(color);
                        }

                        value |= (uint)previousIndex << (k * bitsPerIndex);
                    }
                }

                output[x] = 0xFF000000u | (value << 8);
            }
        }
    }

    // 64x64 tiles: with a single prefix code group, larger tiles measured as small as or smaller than 16x16 and 32x32 ones
    private const int TransformTileBits = 6;

    private void ApplyPredictor(Span<uint> argb, int width, int height)
    {
        var bits = TransformTileBits;
        var tilesX = Vp8LDecoder.DivRoundUp(width, bits);
        var tilesY = Vp8LDecoder.DivRoundUp(height, bits);
        var modes = RentPixels((long)tilesX * tilesY);
        var candidates = _effort >= 3 ? 14 : _effort >= 1 ? 4 : 1;
        var useEntropy = _entropyPredictors;
        Span<int> histogram = useEntropy ? new int[4 * 256] : default;
        for (var ty = 0; ty < tilesY; ty++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            for (var tx = 0; tx < tilesX; tx++)
            {
                var bestMode = 0;
                var bestCost = double.MaxValue;
                for (var candidate = 0; candidate < candidates; candidate++)
                {
                    var mode = candidates == 14 ? candidate : candidate switch { 0 => 11, 1 => 1, 2 => 2, _ => 12 };
                    if (candidates == 1)
                    {
                        bestMode = mode;
                        break;
                    }

                    var cost = TileCost(argb, width, height, tx, ty, bits, mode, histogram);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestMode = mode;
                    }
                }

                modes[(ty * tilesX) + tx] = 0xFF000000u | ((uint)bestMode << 8);
            }
        }

        _writer.WriteBits(1, 1);
        _writer.WriteBits(0, 2);
        _writer.WriteBits((uint)(bits - 2), 3);
        EncodeSpatialImage(modes, tilesX, tilesY, isMainImage: false);

        // Residuals from the last row to the first, so that every prediction reads original pixels: a row is computed whole
        // (from its original pixels and the original row above) before it is replaced
        Span<uint> residuals = stackalloc uint[1 << TransformTileBits];
        for (var y = height - 1; y >= 0; y--)
        {
            var row = y * width;
            for (var tx = tilesX - 1; tx >= 0; tx--)
            {
                var x0 = tx << bits;
                var x1 = Math.Min(width, x0 + (1 << bits));
                var mode = (int)((modes[((y >> bits) * tilesX) + tx] >> 8) & 0xF);
                var segment = residuals[..(x1 - x0)];
                PredictionResiduals(argb, width, y, x0, x1, mode, segment);
                segment.CopyTo(argb.Slice(row + x0, x1 - x0));
            }
        }
    }

    private static uint Predict(ReadOnlySpan<uint> argb, int width, int x, int y, int mode)
    {
        var index = (y * width) + x;
        if (y == 0)
            return x == 0 ? Vp8LTransforms.Black : argb[index - 1];

        if (x == 0)
            return argb[index - width];

        return Vp8LTransforms.Predict(mode, argb[index - 1], argb[index - width], argb[index - width + 1], argb[index - width - 1]);
    }

    /// <summary>
    /// The cost of predicting a tile with a mode: the entropy of the residual components when <paramref name="histogram"/> is
    /// given (4 x 256 entries), otherwise the sum of their magnitudes. The residuals of each row are computed 4 at a time.
    /// </summary>
    internal static double TileCost(ReadOnlySpan<uint> argb, int width, int height, int tx, int ty, int bits, int mode, Span<int> histogram)
    {
        var x0 = tx << bits;
        var y0 = ty << bits;
        var x1 = Math.Min(width, x0 + (1 << bits));
        var y1 = Math.Min(height, y0 + (1 << bits));
        if (!histogram.IsEmpty)
        {
            histogram.Clear();
        }

        Span<uint> residuals = stackalloc uint[1 << TransformTileBits];
        residuals = residuals[..(x1 - x0)];
        long sum = 0;
        for (var y = y0; y < y1; y++)
        {
            PredictionResiduals(argb, width, y, x0, x1, mode, residuals);
            if (histogram.IsEmpty)
            {
                sum += MagnitudeSum(residuals);
            }
            else
            {
                foreach (var residual in residuals)
                {
                    histogram[(int)(residual & 0xFF)]++;
                    histogram[256 + (int)((residual >> 8) & 0xFF)]++;
                    histogram[512 + (int)((residual >> 16) & 0xFF)]++;
                    histogram[768 + (int)(residual >> 24)]++;
                }
            }
        }

        if (histogram.IsEmpty)
            return sum;

        var count = (x1 - x0) * (y1 - y0);
        return Entropy(histogram[..256], count) + Entropy(histogram.Slice(256, 256), count) + Entropy(histogram.Slice(512, 256), count) + Entropy(histogram.Slice(768, 256), count);
    }

    /// <summary>The sum of the magnitudes of the signed components of residuals.</summary>
    private static long MagnitudeSum(ReadOnlySpan<uint> residuals)
    {
        long sum = 0;
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            // Byte magnitudes (|-128| is 128 as a byte), then the 4 bytes of each lane added: at most 1020 per lane
            var total = Vector128<uint>.Zero;
            var lowBytes = Vector128.Create(0x00FF00FFu);
            for (; i <= residuals.Length - Vector128<uint>.Count; i += Vector128<uint>.Count)
            {
                var magnitudes = Vector128.Abs(Vector128.Create(residuals.Slice(i, Vector128<uint>.Count)).AsSByte()).AsUInt32();
                var pairs = (magnitudes & lowBytes) + (Vector128.ShiftRightLogical(magnitudes, 8) & lowBytes);
                total += (pairs & Vector128.Create(0xFFFFu)) + Vector128.ShiftRightLogical(pairs, 16);
            }

            sum = Vector128.Sum(total);
        }

        for (; i < residuals.Length; i++)
        {
            var residual = residuals[i];
            sum += Math.Abs((int)(sbyte)residual) + Math.Abs((int)(sbyte)(residual >> 8)) + Math.Abs((int)(sbyte)(residual >> 16)) + Math.Abs((int)(sbyte)(residual >> 24));
        }

        return sum;
    }

    /// <summary>
    /// Computes the residuals of the pixels <paramref name="x0"/> to <paramref name="x1"/> of row <paramref name="y"/> predicted
    /// with a mode from the (original) pixels: the first row predicts from the left (black for the first pixel), the first
    /// column from the top. Pixels that are not on the first row or column are computed 4 at a time.
    /// </summary>
    internal static void PredictionResiduals(ReadOnlySpan<uint> argb, int width, int y, int x0, int x1, int mode, Span<uint> residuals)
    {
        var row = y * width;
        var x = x0;
        if (y > 0 && Vector128.IsHardwareAccelerated)
        {
            if (x == 0)
            {
                residuals[0] = SubtractPixels(argb[row], argb[row - width]);
                x = 1;
            }

            for (; x <= x1 - Vector128<uint>.Count; x += Vector128<uint>.Count)
            {
                var index = row + x;
                var current = Vector128.Create(argb.Slice(index, Vector128<uint>.Count));
                var left = Vector128.Create(argb.Slice(index - 1, Vector128<uint>.Count));
                var top = Vector128.Create(argb.Slice(index - width, Vector128<uint>.Count));
                var topRight = Vector128.Create(argb.Slice(index - width + 1, Vector128<uint>.Count));
                var topLeft = Vector128.Create(argb.Slice(index - width - 1, Vector128<uint>.Count));
                var prediction = PredictVector(mode, left, top, topRight, topLeft);
                (current.AsByte() - prediction.AsByte()).AsUInt32().CopyTo(residuals.Slice(x - x0, Vector128<uint>.Count));
            }
        }

        for (; x < x1; x++)
        {
            residuals[x - x0] = SubtractPixels(argb[row + x], Predict(argb, width, x, y, mode));
        }
    }

    /// <summary>The scalar reference of <see cref="PredictionResiduals"/>.</summary>
    internal static void PredictionResidualsScalar(ReadOnlySpan<uint> argb, int width, int y, int x0, int x1, int mode, Span<uint> residuals)
    {
        for (var x = x0; x < x1; x++)
        {
            residuals[x - x0] = SubtractPixels(argb[(y * width) + x], Predict(argb, width, x, y, mode));
        }
    }

    // Vp8LTransforms.Predict for 4 pixels: the same formulas in 32-bit lanes (averages), bytes (select distances) and 16-bit
    // lanes (clamped sums)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> PredictVector(int mode, Vector128<uint> left, Vector128<uint> top, Vector128<uint> topRight, Vector128<uint> topLeft)
    {
        switch (mode)
        {
            case 1:
                return left;
            case 2:
                return top;
            case 3:
                return topRight;
            case 4:
                return topLeft;
            case 5:
                return Average2(Average2(left, topRight), top);
            case 6:
                return Average2(left, topLeft);
            case 7:
                return Average2(left, top);
            case 8:
                return Average2(topLeft, top);
            case 9:
                return Average2(top, topRight);
            case 10:
                return Average2(Average2(left, topLeft), Average2(top, topRight));
            case 11:
            {
                // Select: left when the distance sum |T - TL| is below |L - TL|, otherwise top
                var distanceToLeft = ComponentDistance(top, topLeft);
                var distanceToTop = ComponentDistance(left, topLeft);
                return Vector128.ConditionalSelect(Vector128.LessThan(distanceToLeft, distanceToTop), left, top);
            }

            case 12:
                return ClampedSum(left, top, topLeft);
            case 13:
                return ClampedHalfSum(Average2(left, top), topLeft);
            default:
                return Vector128.Create(Vp8LTransforms.Black);
        }

        static Vector128<uint> Average2(Vector128<uint> a, Vector128<uint> b) => Vector128.ShiftRightLogical((a ^ b) & Vector128.Create(0xFEFEFEFEu), 1) + (a & b);

        static Vector128<uint> ComponentDistance(Vector128<uint> a, Vector128<uint> b)
        {
            var differences = Vector128.Max(a.AsByte(), b.AsByte()) - Vector128.Min(a.AsByte(), b.AsByte());
            var lowBytes = Vector128.Create(0x00FF00FFu);
            var pairs = (differences.AsUInt32() & lowBytes) + (Vector128.ShiftRightLogical(differences.AsUInt32(), 8) & lowBytes);
            return (pairs & Vector128.Create(0xFFFFu)) + Vector128.ShiftRightLogical(pairs, 16);
        }

        // a + b - c per component, clamped to 0-255
        static Vector128<uint> ClampedSum(Vector128<uint> a, Vector128<uint> b, Vector128<uint> c)
        {
            var low = (Widen(a, upper: false) + Widen(b, upper: false)) - Widen(c, upper: false);
            var high = (Widen(a, upper: true) + Widen(b, upper: true)) - Widen(c, upper: true);
            return Narrow(low, high);
        }

        // a + (a - b) / 2 per component (the division truncates toward zero), clamped to 0-255
        static Vector128<uint> ClampedHalfSum(Vector128<uint> a, Vector128<uint> b)
        {
            return Narrow(Half(Widen(a, upper: false), Widen(b, upper: false)), Half(Widen(a, upper: true), Widen(b, upper: true)));

            static Vector128<short> Half(Vector128<short> a, Vector128<short> b)
            {
                var difference = a - b;
                var quotient = Vector128.ShiftRightArithmetic(difference + (Vector128.ShiftRightLogical(difference.AsUInt16(), 15).AsInt16()), 1);
                return a + quotient;
            }
        }

        static Vector128<short> Widen(Vector128<uint> value, bool upper) => (upper ? Vector128.WidenUpper(value.AsByte()) : Vector128.WidenLower(value.AsByte())).AsInt16();

        static Vector128<uint> Narrow(Vector128<short> low, Vector128<short> high)
        {
            var zero = Vector128<short>.Zero;
            var max = Vector128.Create((short)255);
            return Vector128.Narrow(Vector128.Min(Vector128.Max(low, zero), max).AsUInt16(), Vector128.Min(Vector128.Max(high, zero), max).AsUInt16()).AsUInt32();
        }
    }

    /// <summary>The scalar reference of <see cref="TileCost"/>.</summary>
    internal static double TileCostScalar(ReadOnlySpan<uint> argb, int width, int height, int tx, int ty, int bits, int mode, Span<int> histogram)
    {
        var x0 = tx << bits;
        var y0 = ty << bits;
        var x1 = Math.Min(width, x0 + (1 << bits));
        var y1 = Math.Min(height, y0 + (1 << bits));
        if (!histogram.IsEmpty)
        {
            histogram.Clear();
        }

        long sum = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var residual = SubtractPixels(argb[(y * width) + x], Predict(argb, width, x, y, mode));
                if (histogram.IsEmpty)
                {
                    sum += Math.Abs((int)(sbyte)residual) + Math.Abs((int)(sbyte)(residual >> 8)) + Math.Abs((int)(sbyte)(residual >> 16)) + Math.Abs((int)(sbyte)(residual >> 24));
                }
                else
                {
                    histogram[(int)(residual & 0xFF)]++;
                    histogram[256 + (int)((residual >> 8) & 0xFF)]++;
                    histogram[512 + (int)((residual >> 16) & 0xFF)]++;
                    histogram[768 + (int)(residual >> 24)]++;
                }
            }
        }

        if (histogram.IsEmpty)
            return sum;

        var count = (x1 - x0) * (y1 - y0);
        return Entropy(histogram[..256], count) + Entropy(histogram.Slice(256, 256), count) + Entropy(histogram.Slice(512, 256), count) + Entropy(histogram.Slice(768, 256), count);
    }

    private void ApplyColorTransform(Span<uint> argb, int width, int height)
    {
        var bits = TransformTileBits;
        var tilesX = Vp8LDecoder.DivRoundUp(width, bits);
        var tilesY = Vp8LDecoder.DivRoundUp(height, bits);
        var elements = RentPixels((long)tilesX * tilesY);
        var histogram = new int[256];
        var fixedHistogram = new int[256];
        var tileSize = 1 << (2 * bits);
        var green = new int[tileSize];
        var red = new int[tileSize];
        var blue = new int[tileSize];
        var movingValues = new int[tileSize];
        var movingMultipliers = new int[tileSize];
        for (var ty = 0; ty < tilesY; ty++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            for (var tx = 0; tx < tilesX; tx++)
            {
                var x0 = tx << bits;
                var y0 = ty << bits;
                var x1 = Math.Min(width, x0 + (1 << bits));
                var y1 = Math.Min(height, y0 + (1 << bits));
                var count = 0;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var pixel = argb[(y * width) + x];
                        green[count] = (sbyte)(pixel >> 8);
                        red[count] = (int)((pixel >> 16) & 0xFF);
                        blue[count] = (int)(pixel & 0xFF);
                        count++;
                    }
                }

                // Green to red first (red only depends on it), then green to blue, then red to blue on the result
                var greenToRed = SearchColorElement(red.AsSpan(0, count), green.AsSpan(0, count), histogram, fixedHistogram, movingValues, movingMultipliers);
                var greenToBlue = SearchColorElement(blue.AsSpan(0, count), green.AsSpan(0, count), histogram, fixedHistogram, movingValues, movingMultipliers);
                for (var i = 0; i < count; i++)
                {
                    blue[i] = (blue[i] - Vp8LTransforms.ColorTransformDelta(greenToBlue, (sbyte)green[i])) & 0xFF;
                    red[i] = (sbyte)red[i];
                }

                var redToBlue = SearchColorElement(blue.AsSpan(0, count), red.AsSpan(0, count), histogram, fixedHistogram, movingValues, movingMultipliers);
                elements[(ty * tilesX) + tx] = 0xFF000000u | ((uint)(byte)redToBlue << 16) | ((uint)(byte)greenToBlue << 8) | (byte)greenToRed;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        argb[(y * width) + x] = Vp8LTransforms.ForwardColorTransform(argb[(y * width) + x], greenToRed, greenToBlue, redToBlue);
                    }
                }
            }
        }

        _writer.WriteBits(1, 1);
        _writer.WriteBits(1, 2);
        _writer.WriteBits((uint)(bits - 2), 3);
        EncodeSpatialImage(elements, tilesX, tilesY, isMainImage: false);
    }

    /// <summary>
    /// Chooses the multiplier <c>t</c> minimizing the entropy of <c>(value - ((t * multiplier) &gt;&gt; 5)) &amp; 0xFF</c>
    /// (the color transform of one channel by one signed channel): a coarse search, then a refinement around the best value;
    /// ties keep the value searched first (zero first).
    /// </summary>
    private static sbyte SearchColorElement(ReadOnlySpan<int> values, ReadOnlySpan<int> multipliers, int[] histogram, int[] fixedHistogram, int[] movingValues, int[] movingMultipliers)
    {
        // The values whose multiplier is zero fall in the same bin whatever t: they are counted once, and every candidate
        // starts from their histogram (the counts, hence the costs, are the ones of counting every value)
        Array.Clear(fixedHistogram);
        multipliers = multipliers[..values.Length];
        var moving = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (multipliers[i] == 0)
            {
                fixedHistogram[values[i] & 0xFF]++;
            }
            else
            {
                movingValues[moving] = values[i];
                movingMultipliers[moving] = multipliers[i];
                moving++;
            }
        }

        var search = new ElementSearch(movingValues.AsSpan(0, moving), movingMultipliers.AsSpan(0, moving), fixedHistogram, histogram, values.Length);
        var best = 0;
        var bestCost = search.Cost(0);
        for (var t = -96; t <= 96; t += 8)
        {
            if (t == 0)
                continue;

            var cost = search.Cost(t);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = t;
            }
        }

        var center = best;
        for (var t = center - 7; t <= center + 7; t++)
        {
            if (t == center || t is < -128 or > 127)
                continue;

            var cost = search.Cost(t);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = t;
            }
        }

        return (sbyte)best;
    }

    /// <summary>The cost of the values of a channel transformed by <c>t</c>: the zero-multiplier histogram plus the other values.</summary>
    private readonly ref struct ElementSearch(ReadOnlySpan<int> values, ReadOnlySpan<int> multipliers, int[] fixedHistogram, int[] histogram, int count)
    {
        private readonly ReadOnlySpan<int> _values = values;
        private readonly ReadOnlySpan<int> _multipliers = multipliers;

        public double Cost(int t)
        {
            fixedHistogram.CopyTo(histogram);
            var factors = _multipliers[.._values.Length];
            for (var i = 0; i < _values.Length; i++)
            {
                histogram[(_values[i] - ((t * factors[i]) >> 5)) & 0xFF]++;
            }

            return Entropy(histogram, count);
        }
    }

    /// <summary>The Shannon cost in bits of the symbols of <paramref name="histogram"/> (<paramref name="count"/> in total).</summary>
    private static double Entropy(ReadOnlySpan<int> histogram, int count)
    {
        // sum(n log2(count / n)) = count log2(count) - sum(n log2(n))
        var cost = count * Log2Count(count);
        foreach (var value in histogram)
        {
            if (value > 1)
            {
                cost -= value * Log2Count(value);
            }
        }

        return cost;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Log2Count(int value) => (uint)value < (uint)Log2Table.Length ? Log2Table[value] : Math.Log2(value);

    private static readonly double[] Log2Table = CreateLog2Table();

    private static double[] CreateLog2Table()
    {
        var table = new double[4096];
        for (var i = 1; i < table.Length; i++)
        {
            table[i] = Math.Log2(i);
        }

        return table;
    }

    /// <summary>Writes a spatially coded image: color cache information, the meta prefix bit (main image only), one prefix code group and the LZ77-coded pixels.</summary>
    private void EncodeSpatialImage(ReadOnlySpan<uint> pixels, int width, int height, bool isMainImage)
    {
        _ = height;
        EncodeSpatialImageCore(pixels, width, isMainImage);
    }

    private void EncodeSpatialImageCore(ReadOnlySpan<uint> pixels, int width, bool isMainImage)
    {
        var matcher = new Matcher(this, pixels, width);
        var candidates = !isMainImage || _effort < 3 ? [0] : _effort < 6 ? new[] { 0, 10 } : [0, 4, 6, 8, 10];
        var statistics = new Statistics[candidates.Length];
        for (var i = 0; i < candidates.Length; i++)
        {
            statistics[i] = new Statistics(candidates[i]);
        }

        // The parse is recorded once, then replayed for the statistics and for the output
        var tokens = RentPixels(2L * Math.Max(pixels.Length, 1));
        var tokenCount = matcher.Parse(tokens);
        var collector = new StatisticsSink(statistics);
        Replay(pixels, tokens, tokenCount, ref collector);
        var best = 0;
        for (var i = 1; i < statistics.Length; i++)
        {
            if (statistics[i].EstimateBits() < statistics[best].EstimateBits())
            {
                best = i;
            }
        }

        var chosen = statistics[best];
        if (chosen.CacheBits > 0)
        {
            _writer.WriteBits(1, 1);
            _writer.WriteBits((uint)chosen.CacheBits, 4);
        }
        else
        {
            _writer.WriteBits(0, 1);
        }

        if (isMainImage)
        {
            _writer.WriteBits(0, 1); // a single prefix code group
        }

        var codes = new PrefixCode[5];
        codes[0] = WritePrefixCode(chosen.Green);
        codes[1] = WritePrefixCode(chosen.Red);
        codes[2] = WritePrefixCode(chosen.Blue);
        codes[3] = WritePrefixCode(chosen.Alpha);
        codes[4] = WritePrefixCode(chosen.Distance);
        var emitter = new EmitSink(_writer, codes, chosen.CacheBits);
        Replay(pixels, tokens, tokenCount, ref emitter);
    }

    /// <summary>Replays a recorded parse: pairs of (length, distance code), a zero length for a literal.</summary>
    private static void Replay<TSink>(ReadOnlySpan<uint> pixels, ReadOnlySpan<uint> tokens, int count, ref TSink sink)
        where TSink : struct, ITokenSink
    {
        var position = 0;
        for (var i = 0; i < count; i++)
        {
            var length = (int)tokens[2 * i];
            if (length == 0)
            {
                sink.Literal(pixels[position]);
                position++;
            }
            else
            {
                sink.Copy(length, (int)tokens[(2 * i) + 1], pixels.Slice(position, length));
                position += length;
            }
        }
    }

    private PrefixCode WritePrefixCode(uint[] histogram)
    {
        var used = 0;
        int first = -1, second = -1;
        for (var i = 0; i < histogram.Length; i++)
        {
            if (histogram[i] == 0)
                continue;

            used++;
            if (first < 0)
            {
                first = i;
            }
            else if (second < 0)
            {
                second = i;
            }
        }

        var lengths = new byte[histogram.Length];
        var codes = new ushort[histogram.Length];
        if (used == 0 || (used <= 2 && Math.Max(first, second) < 256))
        {
            // Simple code: one or two 8-bit symbols (an unused code is the single symbol 0)
            first = Math.Max(first, 0);
            _writer.WriteBits(1, 1);
            _writer.WriteBits(used == 2 ? 1u : 0u, 1);
            if (first < 2)
            {
                _writer.WriteBits(0, 1);
                _writer.WriteBits((uint)first, 1);
            }
            else
            {
                _writer.WriteBits(1, 1);
                _writer.WriteBits((uint)first, 8);
            }

            if (used == 2)
            {
                _writer.WriteBits((uint)second, 8);
                lengths[first] = 1;
                lengths[second] = 1;
                codes[second] = 1;
                return new PrefixCode(lengths, codes);
            }

            return new PrefixCode(new byte[histogram.Length], codes);
        }

        CanonicalHuffmanBuilder.BuildLengths(histogram, Vp8LPrefixCodes.MaxCodeLength, lengths);
        CanonicalHuffmanBuilder.BuildCodes(lengths, codes);
        WriteNormalCodeLengths(lengths);
        if (used == 1)
            return new PrefixCode(new byte[histogram.Length], codes); // a single leaf: no bit is written

        return new PrefixCode(lengths, codes);
    }

    private void WriteNormalCodeLengths(ReadOnlySpan<byte> lengths)
    {
        // Run-length tokens: 0-15 literal lengths, 16 repeats the previous non-zero length 3-6 times, 17/18 runs of zeros
        var tokens = new List<(int Symbol, int Extra)>();
        var previous = 8;
        var i = 0;
        while (i < lengths.Length)
        {
            var value = lengths[i];
            var run = 1;
            while (i + run < lengths.Length && lengths[i + run] == value)
            {
                run++;
            }

            i += run;
            if (value == 0)
            {
                while (run >= 11)
                {
                    var count = Math.Min(run, 138);
                    tokens.Add((18, count - 11));
                    run -= count;
                }

                if (run >= 3)
                {
                    tokens.Add((17, run - 3));
                    run = 0;
                }

                for (; run > 0; run--)
                {
                    tokens.Add((0, 0));
                }

                continue;
            }

            if (value != previous)
            {
                tokens.Add((value, 0));
                previous = value;
                run--;
            }

            while (run >= 3)
            {
                var count = Math.Min(run, 6);
                tokens.Add((16, count - 3));
                run -= count;
            }

            for (; run > 0; run--)
            {
                tokens.Add((value, 0));
            }
        }

        var histogram = new uint[19];
        foreach (var (symbol, _) in tokens)
        {
            histogram[symbol]++;
        }

        var codeLengths = new byte[19];
        var codes = new ushort[19];
        CanonicalHuffmanBuilder.BuildLengths(histogram, 7, codeLengths);
        CanonicalHuffmanBuilder.BuildCodes(codeLengths, codes);
        var count19 = 19;
        while (count19 > 4 && codeLengths[CodeLengthCodeOrder[count19 - 1]] == 0)
        {
            count19--;
        }

        _writer.WriteBits(0, 1); // normal code length code
        _writer.WriteBits((uint)(count19 - 4), 4);
        for (var k = 0; k < count19; k++)
        {
            _writer.WriteBits(codeLengths[CodeLengthCodeOrder[k]], 3);
        }

        _writer.WriteBits(0, 1); // the code lengths cover the whole alphabet
        var single = 0;
        foreach (var length in codeLengths)
        {
            if (length != 0)
            {
                single++;
            }
        }

        foreach (var (symbol, extra) in tokens)
        {
            if (single > 1)
            {
                _writer.WriteBits(codes[symbol], codeLengths[symbol]);
            }

            switch (symbol)
            {
                case 16:
                    _writer.WriteBits((uint)extra, 2);
                    break;
                case 17:
                    _writer.WriteBits((uint)extra, 3);
                    break;
                case 18:
                    _writer.WriteBits((uint)extra, 7);
                    break;
            }
        }
    }

    internal static uint SubtractPixels(uint a, uint b)
    {
        var alphaGreen = 0x00FF00FFu + (a & 0xFF00FF00u) - (b & 0xFF00FF00u);
        var redBlue = 0xFF00FF00u + (a & 0x00FF00FFu) - (b & 0x00FF00FFu);
        return (alphaGreen & 0xFF00FF00u) | (redBlue & 0x00FF00FFu);
    }

    /// <summary>Splits a length or distance code (at least 1) into its prefix symbol, extra bit count and extra bits.</summary>
    internal static (int Prefix, int ExtraBits, int ExtraValue) GetPrefix(int value)
    {
        if (value <= 4)
            return (value - 1, 0, 0);

        var v = value - 1;
        var highest = BitOperations.Log2((uint)v);
        var second = (v >> (highest - 1)) & 1;
        var extraBits = highest - 1;
        return ((2 * highest) + second, extraBits, v & ((1 << extraBits) - 1));
    }

    /// <summary>The distance code of a scan-order distance: the short code of a close neighbor when one exists, otherwise <c>distance + 120</c>.</summary>
    internal static int GetDistanceCode(int distance, int width, ReadOnlySpan<byte> neighborCodes)
    {
        var best = distance + 120;
        for (var yi = 0; yi < 8; yi++)
        {
            var xi = distance - (yi * width);
            if (xi is < -8 or > 8)
            {
                if (xi < -8)
                    break;

                continue;
            }

            var code = neighborCodes[(yi * 17) + xi + 8];
            if (code != 0 && code < best)
            {
                best = code;
            }
        }

        return best;
    }

    private static byte[] BuildNeighborCodes()
    {
        // Inverse of the decoder's distance map: code (1-120) of each (xi, yi), xi in -8..8, yi in 0..7
        var map = new byte[8 * 17];
        for (var code = 1; code <= 120; code++)
        {
            var (xi, yi) = Vp8LDecoder.GetDistanceMapEntry(code);
            map[(yi * 17) + xi + 8] = (byte)code;
        }

        return map;
    }

    private static readonly byte[] NeighborCodes = BuildNeighborCodes();

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct PrefixCode(byte[] Lengths, ushort[] Codes);

    /// <summary>Receives the parse: literals and backward references.</summary>
    private interface ITokenSink
    {
        void Literal(uint argb);

        void Copy(int length, int distanceCode, ReadOnlySpan<uint> pixels);
    }

    /// <summary>The symbol statistics of one color cache size.</summary>
    private sealed class Statistics
    {
        private readonly uint[] _cache;

        public Statistics(int cacheBits)
        {
            CacheBits = cacheBits;
            _cache = new uint[cacheBits == 0 ? 0 : 1 << cacheBits];
            Green = new uint[NumLiteralCodes + NumLengthCodes + _cache.Length];
        }

        public int CacheBits { get; }

        public uint[] Green { get; }

        public uint[] Red { get; } = new uint[NumLiteralCodes];

        public uint[] Blue { get; } = new uint[NumLiteralCodes];

        public uint[] Alpha { get; } = new uint[NumLiteralCodes];

        public uint[] Distance { get; } = new uint[NumDistanceCodes];

        public long ExtraBits { get; set; }

        public void Literal(uint argb)
        {
            if (_cache.Length != 0)
            {
                var key = (int)Vp8LDecoder.ColorCacheHash(argb, CacheBits);
                if (_cache[key] == argb)
                {
                    Green[NumLiteralCodes + NumLengthCodes + key]++;
                    return;
                }

                _cache[key] = argb;
            }

            Green[(argb >> 8) & 0xFF]++;
            Red[(argb >> 16) & 0xFF]++;
            Blue[argb & 0xFF]++;
            Alpha[argb >> 24]++;
        }

        public void Copy(int length, int distanceCode, ReadOnlySpan<uint> pixels)
        {
            var (lengthPrefix, lengthBits, _) = GetPrefix(length);
            var (distancePrefix, distanceBits, _) = GetPrefix(distanceCode);
            Green[NumLiteralCodes + lengthPrefix]++;
            Distance[distancePrefix]++;
            ExtraBits += lengthBits + distanceBits;
            if (_cache.Length != 0)
            {
                foreach (var argb in pixels)
                {
                    _cache[(int)Vp8LDecoder.ColorCacheHash(argb, CacheBits)] = argb;
                }
            }
        }

        public double EstimateBits() => Entropy(Green) + Entropy(Red) + Entropy(Blue) + Entropy(Alpha) + Entropy(Distance) + ExtraBits;

        private static double Entropy(uint[] histogram)
        {
            long total = 0;
            var used = 0;
            foreach (var value in histogram)
            {
                total += value;
                if (value != 0)
                {
                    used++;
                }
            }

            var bits = used * 4.0; // approximate cost of describing the code
            foreach (var value in histogram)
            {
                if (value != 0)
                {
                    bits += value * Math.Log2((double)total / value);
                }
            }

            return bits;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct StatisticsSink(Statistics[] statistics) : ITokenSink
    {
        public void Literal(uint argb)
        {
            foreach (var item in statistics)
            {
                item.Literal(argb);
            }
        }

        public void Copy(int length, int distanceCode, ReadOnlySpan<uint> pixels)
        {
            foreach (var item in statistics)
            {
                item.Copy(length, distanceCode, pixels);
            }
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct EmitSink(LsbBitWriter writer, PrefixCode[] codes, int cacheBits) : ITokenSink
    {
        private readonly uint[] _cache = new uint[cacheBits == 0 ? 0 : 1 << cacheBits];

        public void Literal(uint argb)
        {
            if (_cache.Length != 0)
            {
                var key = (int)Vp8LDecoder.ColorCacheHash(argb, cacheBits);
                if (_cache[key] == argb)
                {
                    Write(0, NumLiteralCodes + NumLengthCodes + key);
                    return;
                }

                _cache[key] = argb;
            }

            Write(0, (int)((argb >> 8) & 0xFF));
            Write(1, (int)((argb >> 16) & 0xFF));
            Write(2, (int)(argb & 0xFF));
            Write(3, (int)(argb >> 24));
        }

        public void Copy(int length, int distanceCode, ReadOnlySpan<uint> pixels)
        {
            var (lengthPrefix, lengthBits, lengthExtra) = GetPrefix(length);
            Write(0, NumLiteralCodes + lengthPrefix);
            writer.WriteBits((uint)lengthExtra, lengthBits);
            var (distancePrefix, distanceBits, distanceExtra) = GetPrefix(distanceCode);
            Write(4, distancePrefix);
            writer.WriteBits((uint)distanceExtra, distanceBits);
            if (_cache.Length != 0)
            {
                foreach (var argb in pixels)
                {
                    _cache[(int)Vp8LDecoder.ColorCacheHash(argb, cacheBits)] = argb;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Write(int code, int symbol)
        {
            var prefixCode = codes[code];
            writer.WriteBits(prefixCode.Codes[symbol], prefixCode.Lengths[symbol]);
        }
    }

    /// <summary>The LZ77 parser: hash chains over pairs of pixels, greedy with one step of lazy evaluation.</summary>
    private readonly ref struct Matcher
    {
        private readonly Vp8LEncoder _encoder;
        private readonly ReadOnlySpan<uint> _pixels;
        private readonly int _width;
        private readonly int _hashBits;
        private readonly Span<uint> _head;
        private readonly Span<uint> _previous;

        public Matcher(Vp8LEncoder encoder, ReadOnlySpan<uint> pixels, int width)
        {
            _encoder = encoder;
            _pixels = pixels;
            _width = width;
            _hashBits = Math.Clamp(BitOperations.Log2((uint)Math.Max(pixels.Length, 1)) + 1, 8, 18);
            _head = encoder.RentPixels(1 << _hashBits);
            _previous = encoder.RentPixels(Math.Max(pixels.Length, 1));
        }

        /// <summary>Parses the pixels into <paramref name="tokens"/> (pairs of length and distance code, a zero length for a literal).</summary>
        /// <returns>The number of tokens.</returns>
        public int Parse(Span<uint> tokens)
        {
            var pixels = _pixels;
            var count = pixels.Length;
            var effort = _encoder._effort;
            var chainDepth = effort switch
            {
                0 => 4,
                <= 2 => 8,
                <= 4 => 16,
                <= 6 => 32,
                <= 8 => 128,
                _ => 512,
            };
            var lazy = effort >= 3;
            _head.Fill(uint.MaxValue);
            var neighborCodes = NeighborCodes;
            var position = 0;
            var tokenCount = 0;
            var nextCheck = 0;
            var hasLookahead = false;
            (int Length, int Distance) lookahead = default;
            while (position < count)
            {
                if (position >= nextCheck)
                {
                    _encoder._cancellationToken.ThrowIfCancellationRequested();
                    nextCheck = position + 65536;
                }

                // The lookahead of the previous position was searched with the same chains (after inserting that position)
                var (length, distance) = hasLookahead ? lookahead : FindMatch(position, chainDepth);
                hasLookahead = false;
                Insert(position);
                if (length >= MinLength && lazy && position + 1 < count)
                {
                    lookahead = FindMatch(position + 1, chainDepth);
                    if (lookahead.Length > length + 1)
                    {
                        hasLookahead = true;
                        tokens[2 * tokenCount] = 0;
                        tokenCount++;
                        position++;
                        continue;
                    }
                }

                if (length >= MinLength)
                {
                    tokens[2 * tokenCount] = (uint)length;
                    tokens[(2 * tokenCount) + 1] = (uint)GetDistanceCode(distance, _width, neighborCodes);
                    tokenCount++;
                    for (var i = 1; i < length; i++)
                    {
                        Insert(position + i);
                    }

                    position += length;
                }
                else
                {
                    tokens[2 * tokenCount] = 0;
                    tokenCount++;
                    position++;
                }
            }

            return tokenCount;
        }

        private int Hash(int position)
        {
            var value = (_pixels[position] * 0x9E3779B1u) ^ (_pixels[position + 1] * 0x85EBCA77u);
            return (int)(value >> (32 - _hashBits));
        }

        private void Insert(int position)
        {
            if (position + 1 >= _pixels.Length)
            {
                _previous[position] = uint.MaxValue;
                return;
            }

            var hash = Hash(position);
            _previous[position] = _head[hash];
            _head[hash] = (uint)position;
        }

        private (int Length, int Distance) FindMatch(int position, int chainDepth)
        {
            var pixels = _pixels;
            var maxLength = Math.Min(MaxLength, pixels.Length - position);
            if (maxLength < MinLength)
                return (0, 0);

            var bestLength = 0;
            var bestDistance = 0;

            // The cheapest distances first: the left and the top neighbors
            Try(pixels, position, 1, maxLength, ref bestLength, ref bestDistance);
            if (_width > 1)
            {
                Try(pixels, position, _width, maxLength, ref bestLength, ref bestDistance);
            }

            var candidate = _head[Hash(position)];
            while (candidate != uint.MaxValue && chainDepth-- > 0 && bestLength < maxLength)
            {
                var distance = position - (int)candidate;
                if (distance > MaxDistance)
                    break;

                Try(pixels, position, distance, maxLength, ref bestLength, ref bestDistance);
                candidate = _previous[(int)candidate];
            }

            return (bestLength, bestDistance);
        }

        private static void Try(ReadOnlySpan<uint> pixels, int position, int distance, int maxLength, ref int bestLength, ref int bestDistance)
        {
            if (distance <= 0 || distance > position || distance > MaxDistance || bestLength >= maxLength)
                return;

            var source = position - distance;
            if (pixels[source + bestLength] != pixels[position + bestLength])
                return;

            // Overlapping references compare the original pixels: the decoder's forward copy reproduces them
            var length = pixels.Slice(source, maxLength).CommonPrefixLength(pixels.Slice(position, maxLength));
            if (length > bestLength)
            {
                bestLength = length;
                bestDistance = distance;
            }
        }
    }
}
