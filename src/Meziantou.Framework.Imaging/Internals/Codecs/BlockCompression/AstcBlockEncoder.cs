using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes a block of RGBA8 texels as one two-dimensional ASTC LDR block of 128 bits (Khronos Data Format Specification
/// 1.4, section 23), for every two-dimensional footprint from 4x4 to 12x12.
/// </summary>
/// <remarks>
/// <para>
/// A block of one color is a void-extent block (section 23.22), exact in both operation modes. Otherwise the encoder
/// emits blocks of the LDR color endpoint modes (section 23.11): luminance (0), luminance and alpha (4), RGB (8) and RGBA
/// (12) for gray, opaque or translucent content, and, in the thorough mode, RGB with a scale (6) and RGB with a scale and
/// alpha (10). A block has one partition, or two or three partitions sharing the endpoint mode (section 23.20), and one
/// weight plane, or two for one partition (section 23.19). The fast mode tries one partition with one and two planes, and
/// two partitions when no line fits the texels; the thorough mode tries more weight grids, the variants of the endpoint
/// mode, two channels for the second plane, and three partition patterns of two partitions and two of three, with more
/// refinements. It never emits the HDR endpoint modes, nor an illegal block (section 23.23).
/// </para>
/// <para>
/// The weight grid and the ranges of the weights and of the endpoints share the 128 bits (section 23.10): for each grid
/// and weight range, the endpoints get the largest range that fits, as the decoder computes it. An estimate of the error
/// of each such configuration (the error of fitting the weight grid by least squares, plus the quantization noise of the
/// weights and of the endpoints) selects the few that are encoded. Each one is encoded by the principal axis of the
/// texels, the projection of the texels on the quantized endpoints, a least-squares fit of the grid weights through the
/// bilinear infill of the decoder (section 23.17), then least-squares refinements of the endpoints from the decoded
/// weights. The exact error of the decoded block, in the space of the operation mode, picks the best block.
/// </para>
/// <para>
/// The error is measured in the space of the operation mode: the 8-bit results for an sRGB block (endpoints expanded with
/// 0x80, section 23.18), the UNORM16 results otherwise (endpoints expanded by bit replication), whose target for an 8-bit
/// value v is v × 257: decode_float16 and decode_unorm8 of the KTX formats are functions of it.
/// </para>
/// </remarks>
internal static class AstcBlockEncoder
{
    // The texels are stored as R, G, B, A and the luminance (R + G + B) / 3 that the luminance modes fit
    private const int Channels = 5;
    private const int Luminance = 4;
    private const int MaxTexels = 144;
    private const int MaxGridWeights = AstcBlockDecoder.MaxWeights;

    /// <summary>Encodes a block.</summary>
    /// <param name="rgba">The <paramref name="blockWidth"/> × <paramref name="blockHeight"/> RGBA8 texels of the block, row-major.</param>
    /// <param name="blockWidth">The width of the footprint.</param>
    /// <param name="blockHeight">The height of the footprint.</param>
    /// <param name="srgb">Whether the block is for an sRGB format (the sRGB operation mode) or a UNORM format (linear LDR).</param>
    /// <param name="block">The 16 bytes of the block.</param>
    /// <param name="thorough">Whether to search more configurations: partitions, two planes, more weight grids and refinements.</param>
    /// <exception cref="ArgumentOutOfRangeException">The footprint is not a two-dimensional footprint of ASTC.</exception>
    /// <exception cref="ArgumentException">A span is too short.</exception>
    public static void Encode(ReadOnlySpan<byte> rgba, int blockWidth, int blockHeight, bool srgb, Span<byte> block, bool thorough)
    {
        var footprint = AstcFootprintTables.GetFootprintIndex(blockWidth, blockHeight);
        if (footprint < 0)
            throw new ArgumentOutOfRangeException(nameof(blockWidth), "The footprint is not a two-dimensional ASTC footprint.");

        var count = blockWidth * blockHeight;
        if (rgba.Length < count * 4)
            throw new ArgumentException("The span is shorter than the texels of the block.", nameof(rgba));

        if (block.Length < 16)
            throw new ArgumentException("The span is shorter than a block.", nameof(block));

        rgba = rgba[..(count * 4)];
        var first = BinaryPrimitives.ReadUInt32LittleEndian(rgba);
        var solid = true;
        var opaque = true;
        var gray = true;
        var nearGray = true;
        for (var i = 0; i < count; i++)
        {
            var offset = i * 4;
            if (BinaryPrimitives.ReadUInt32LittleEndian(rgba[offset..]) != first)
            {
                solid = false;
            }

            int r = rgba[offset], g = rgba[offset + 1], b = rgba[offset + 2];
            opaque &= rgba[offset + 3] == 255;
            gray &= r == g && g == b;
            nearGray &= Math.Abs(r - g) <= 4 && Math.Abs(g - b) <= 4 && Math.Abs(r - b) <= 4;
        }

        if (solid)
        {
            WriteVoidExtent(rgba, block);
            return;
        }

        Span<float> texels = stackalloc float[MaxTexels * Channels];
        for (var i = 0; i < count; i++)
        {
            var offset = i * 4;
            for (var c = 0; c < 4; c++)
            {
                texels[(i * Channels) + c] = rgba[offset + c];
            }

            texels[(i * Channels) + Luminance] = (rgba[offset] + rgba[offset + 1] + rgba[offset + 2]) / 3f;
        }

        Span<byte> partition = stackalloc byte[MaxTexels];
        partition.Clear();
        var context = new Context
        {
            Source = rgba,
            Texels = texels[..(count * Channels)],
            Partition = partition[..count],
            BlockWidth = blockWidth,
            BlockHeight = blockHeight,
            Count = count,
            Srgb = srgb,
            Thorough = thorough,
            Tables = AstcFootprintTables.Get(footprint),
            BestError = long.MaxValue,
        };

        // The endpoint mode: luminance for gray blocks, RGB for opaque blocks
        var mode = gray ? (opaque ? 0 : 4) : (opaque ? 8 : 12);
        Span<float> endpoints = stackalloc float[3 * 2 * Channels];
        if (!thorough)
        {
            FitEndpoints(ref context, 1, ChannelMask(mode), -1, endpoints);
            SearchConfigs(ref context, 1, 0, mode, -1, endpoints, keep: 2, iterations: 2);
            if (mode != 0 && context.BestError != 0)
            {
                EncodeDualPlane(ref context, mode, endpoints, channelCount: 1, keep: 1, iterations: 2);
            }

            // Two partitions for the blocks that are still far off (a root mean square error above 4 per sample): blocks
            // of more than two colors, which no line fits
            if (context.BestError > (long)count * 4 * 16 * (srgb ? 1 : 257 * 257))
            {
                Span<int> seed = stackalloc int[1];
                if (SelectPartitionSeeds(ref context, footprint, 2, ChannelMask(mode), seed) > 0)
                {
                    SetPartitions(ref context, 2, seed[0]);
                    FitEndpoints(ref context, 2, ChannelMask(mode), -1, endpoints);
                    SearchConfigs(ref context, 2, seed[0], mode, -1, endpoints, keep: 1, iterations: 2);
                }
            }
        }
        else
        {
            EncodeThorough(ref context, footprint, mode, nearGray && !gray, endpoints);
        }

        BinaryPrimitives.WriteUInt128LittleEndian(block, context.Best);
    }

    private static void EncodeThorough(ref Context context, int footprint, int mode, bool nearGray, scoped Span<float> endpoints)
    {
        // One partition: the endpoint mode of the content, its variant with a scale (fewer integers, so more precise
        // ones), and the luminance modes for nearly gray content
        Span<int> modes = stackalloc int[3];
        var modeCount = 0;
        modes[modeCount++] = mode;
        if (mode is 8 or 12)
        {
            modes[modeCount++] = mode == 8 ? 6 : 10;
            if (nearGray)
            {
                modes[modeCount++] = mode == 8 ? 0 : 4;
            }
        }

        for (var i = 0; i < modeCount; i++)
        {
            FitEndpoints(ref context, 1, ChannelMask(modes[i]), -1, endpoints);
            SearchConfigs(ref context, 1, 0, modes[i], -1, endpoints, keep: 3, iterations: 3);
            if (context.BestError == 0)
                return;
        }

        if (mode != 0)
        {
            EncodeDualPlane(ref context, mode, endpoints, channelCount: 2, keep: 2, iterations: 3);
            if (context.BestError == 0)
                return;
        }

        // Two and three partitions with the endpoint mode of the content and its variant with a scale, which fits more
        // precise integers (all the partitions share the mode; there are at most 18 integers)
        Span<int> seeds = stackalloc int[3];
        var scaled = mode switch
        {
            8 => 6,
            12 => 10,
            _ => -1,
        };
        for (var partitions = 2; partitions <= 3; partitions++)
        {
            if (partitions * ValueCount(scaled >= 0 ? scaled : mode) > 18)
                break;

            var seedCount = SelectPartitionSeeds(ref context, footprint, partitions, ChannelMask(mode), seeds[..(partitions == 2 ? 3 : 2)]);
            for (var i = 0; i < seedCount; i++)
            {
                SetPartitions(ref context, partitions, seeds[i]);
                for (var variant = 0; variant < 2; variant++)
                {
                    var partitionMode = variant == 0 ? mode : scaled;
                    if (partitionMode < 0 || partitions * ValueCount(partitionMode) > 18)
                        continue;

                    FitEndpoints(ref context, partitions, ChannelMask(partitionMode), -1, endpoints);
                    SearchConfigs(ref context, partitions, seeds[i], partitionMode, -1, endpoints, keep: 1, iterations: 3);
                    if (context.BestError == 0)
                        return;
                }
            }
        }
    }

    /// <summary>
    /// Encodes blocks of two weight planes (section 23.19), the channel of the second plane being the ones whose removal
    /// leaves the other channels the nearest to a line (alpha for the luminance and alpha mode).
    /// </summary>
    private static void EncodeDualPlane(ref Context context, int mode, scoped Span<float> endpoints, int channelCount, int keep, int iterations)
    {
        var channels = ChannelMask(mode);
        Span<double> residuals = stackalloc double[4];
        residuals.Fill(double.MaxValue);
        for (var c = 0; c < 4; c++)
        {
            if ((channels & (1 << c)) == 0 || (mode == 4 && c != 3))
                continue;

            var plane0 = (mode == 4 ? 1 << Luminance : channels) & ~(1 << c);
            residuals[c] = FitLine(context.Texels, context.Partition, 0, plane0, endpoints);
        }

        for (var i = 0; i < channelCount; i++)
        {
            var channel = -1;
            for (var c = 0; c < 4; c++)
            {
                if (residuals[c] != double.MaxValue && (channel < 0 || residuals[c] < residuals[channel]))
                {
                    channel = c;
                }
            }

            if (channel < 0)
                return;

            residuals[channel] = double.MaxValue;
            FitEndpoints(ref context, 1, channels, channel, endpoints);
            SearchConfigs(ref context, 1, 0, mode, channel, endpoints, keep, iterations);
            if (context.BestError == 0)
                return;
        }
    }

    /// <summary>The data of the block being encoded and the best block found.</summary>
    private ref struct Context
    {
        public ReadOnlySpan<byte> Source;
        public Span<float> Texels;
        public Span<byte> Partition;
        public int BlockWidth;
        public int BlockHeight;
        public int Count;
        public bool Srgb;
        public bool Thorough;
        public AstcFootprintTables Tables;
        public UInt128 Best;
        public long BestError;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Endpoint modes (section 23.11)
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The channels an endpoint mode fits: luminance (and alpha), or R, G, B (and alpha).</summary>
    private static int ChannelMask(int mode) => mode switch
    {
        0 => 1 << Luminance,
        4 => (1 << Luminance) | 8,
        8 or 6 => 7,
        _ => 15,
    };

    /// <summary>The number of endpoint integers of a mode (section 23.11).</summary>
    private static int ValueCount(int mode) => ((mode >> 2) + 1) * 2;

    /// <summary>The number of output channels the fitted channels of a mode drive.</summary>
    private static int OutputChannelCount(int mode) => mode is 0 or 6 or 8 ? 3 : 4;

    /// <summary>
    /// Quantizes the continuous endpoints of each partition to the integers of an endpoint mode, and decodes them as the
    /// decoder does (section 23.14) into the 8-bit endpoints of each partition (RGBA0, RGBA1).
    /// </summary>
    private static void QuantizeEndpoints(int mode, int colorRange, int partitions, ReadOnlySpan<float> endpoints, Span<int> values, Span<int> decoded)
    {
        var count = ValueCount(mode);
        Span<int> unquantized = stackalloc int[8];
        for (var k = 0; k < partitions; k++)
        {
            var e0 = endpoints.Slice(k * 2 * Channels, Channels);
            var e1 = endpoints.Slice((k * 2 * Channels) + Channels, Channels);
            var v = values.Slice(k * count, count);
            switch (mode)
            {
                case 0:
                    v[0] = Quantize(colorRange, e0[Luminance]);
                    v[1] = Quantize(colorRange, e1[Luminance]);
                    break;
                case 4:
                    v[0] = Quantize(colorRange, e0[Luminance]);
                    v[1] = Quantize(colorRange, e1[Luminance]);
                    v[2] = Quantize(colorRange, e0[3]);
                    v[3] = Quantize(colorRange, e1[3]);
                    break;
                case 6:
                case 10:
                {
                    // The brighter endpoint is the base, the other one its scale (e0 = base × s / 256)
                    var swap = e0[0] + e0[1] + e0[2] > e1[0] + e1[1] + e1[2];
                    var scaled = swap ? e1 : e0;
                    var base0 = swap ? e0 : e1;
                    long dot = 0, length = 0;
                    for (var c = 0; c < 3; c++)
                    {
                        v[c] = Quantize(colorRange, base0[c]);
                        var b = AstcEncodingTables.UnquantizeColor(colorRange, v[c]);
                        dot += (long)MathF.Round(scaled[c], MidpointRounding.AwayFromZero) * b;
                        length += b * b;
                    }

                    // The decoder truncates base × s / 256: aim at the middle of the interval
                    var scale = length == 0 ? 0 : (int)Math.Clamp(((256 * dot) + (length / 2)) / length, 0, 255);
                    v[3] = AstcEncodingTables.QuantizeColor(colorRange, scale);
                    if (mode == 10)
                    {
                        v[4] = Quantize(colorRange, scaled[3]);
                        v[5] = Quantize(colorRange, base0[3]);
                    }

                    break;
                }

                default:
                {
                    // Modes 8 and 12: the decoder swaps the endpoints (and contracts blue) when the second one has the
                    // smaller sum, so the endpoint with the larger sum goes second
                    var channels = mode == 8 ? 3 : 4;
                    for (var c = 0; c < channels; c++)
                    {
                        v[2 * c] = Quantize(colorRange, e0[c]);
                        v[(2 * c) + 1] = Quantize(colorRange, e1[c]);
                    }

                    var sum0 = 0;
                    var sum1 = 0;
                    for (var c = 0; c < 3; c++)
                    {
                        sum0 += AstcEncodingTables.UnquantizeColor(colorRange, v[2 * c]);
                        sum1 += AstcEncodingTables.UnquantizeColor(colorRange, v[(2 * c) + 1]);
                    }

                    if (sum1 < sum0)
                    {
                        for (var c = 0; c < channels; c++)
                        {
                            (v[2 * c], v[(2 * c) + 1]) = (v[(2 * c) + 1], v[2 * c]);
                        }
                    }

                    break;
                }
            }

            for (var i = 0; i < count; i++)
            {
                unquantized[i] = AstcEncodingTables.UnquantizeColor(colorRange, v[i]);
            }

            AstcBlockDecoder.DecodeEndpoints(mode, unquantized[..count], decoded.Slice(k * 8, 8), out _, out _);
        }
    }

    private static int Quantize(int colorRange, float value) => AstcEncodingTables.QuantizeColor(colorRange, (int)Math.Clamp(MathF.Round(value, MidpointRounding.AwayFromZero), 0, 255));

    // -----------------------------------------------------------------------------------------------------------------
    // Endpoint fitting
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Fits the initial endpoints of each partition: the extremes of the projections of its texels on their principal axis,
    /// per weight plane (the channel of the second plane, <paramref name="planeChannel"/>, gets its own extremes). Returns the
    /// squared distance of the texels to the lines of the first plane.
    /// </summary>
    private static double FitEndpoints(ref Context context, int partitions, int channels, int planeChannel, scoped Span<float> endpoints)
    {
        Span<float> line = stackalloc float[2 * Channels];
        var residual = 0.0;
        for (var k = 0; k < partitions; k++)
        {
            var target = endpoints.Slice(k * 2 * Channels, 2 * Channels);
            target.Clear();
            var plane0 = planeChannel < 0 ? channels : channels & ~(1 << planeChannel);
            residual += FitLine(context.Texels, context.Partition, k, plane0, line);
            Copy(line, target, plane0);
            if (planeChannel >= 0)
            {
                FitLine(context.Texels, context.Partition, k, 1 << planeChannel, line);
                Copy(line, target, 1 << planeChannel);
            }
        }

        return residual;

        static void Copy(ReadOnlySpan<float> source, Span<float> target, int mask)
        {
            for (var c = 0; c < Channels; c++)
            {
                if ((mask & (1 << c)) != 0)
                {
                    target[c] = source[c];
                    target[Channels + c] = source[Channels + c];
                }
            }
        }
    }

    /// <summary>
    /// Fits a line through the texels of a partition over some channels: the mean and the principal axis (power
    /// iteration on the covariance). Writes the extremes of the projections as two endpoints (E0, then E1), and returns the
    /// squared distance of the texels to the line.
    /// </summary>
    private static double FitLine(ReadOnlySpan<float> texels, ReadOnlySpan<byte> partition, int part, int channels, Span<float> endpoints)
    {
        Span<int> list = stackalloc int[4];
        var n = 0;
        for (var c = 0; c < Channels; c++)
        {
            if ((channels & (1 << c)) != 0)
            {
                list[n++] = c;
            }
        }

        list = list[..n];
        Span<double> mean = stackalloc double[4];
        mean.Clear();
        var count = 0;
        for (var t = 0; t < partition.Length; t++)
        {
            if (partition[t] != part)
                continue;

            count++;
            for (var i = 0; i < n; i++)
            {
                mean[i] += texels[(t * Channels) + list[i]];
            }
        }

        if (count == 0)
            return 0;

        for (var i = 0; i < n; i++)
        {
            mean[i] /= count;
        }

        Span<double> covariance = stackalloc double[16];
        covariance.Clear();
        var total = 0.0;
        for (var t = 0; t < partition.Length; t++)
        {
            if (partition[t] != part)
                continue;

            for (var i = 0; i < n; i++)
            {
                var di = texels[(t * Channels) + list[i]] - mean[i];
                for (var j = i; j < n; j++)
                {
                    covariance[(i * 4) + j] += di * (texels[(t * Channels) + list[j]] - mean[j]);
                }
            }
        }

        for (var i = 0; i < n; i++)
        {
            total += covariance[(i * 4) + i];
            for (var j = 0; j < i; j++)
            {
                covariance[(i * 4) + j] = covariance[(j * 4) + i];
            }
        }

        // Power iteration from the column of the largest variance
        Span<double> axis = stackalloc double[4];
        Span<double> next = stackalloc double[4];
        var largest = 0;
        for (var i = 1; i < n; i++)
        {
            if (covariance[(i * 4) + i] > covariance[(largest * 4) + largest])
            {
                largest = i;
            }
        }

        for (var i = 0; i < n; i++)
        {
            axis[i] = covariance[(i * 4) + largest];
        }

        var norm = Normalize(axis[..n]);
        for (var iteration = 0; iteration < 8 && norm > 0; iteration++)
        {
            for (var i = 0; i < n; i++)
            {
                var sum = 0.0;
                for (var j = 0; j < n; j++)
                {
                    sum += covariance[(i * 4) + j] * axis[j];
                }

                next[i] = sum;
            }

            if (Normalize(next[..n]) <= 0)
                break;

            next[..n].CopyTo(axis);
        }

        var min = 0.0;
        var max = 0.0;
        var projected = 0.0;
        if (norm > 0)
        {
            min = double.MaxValue;
            max = double.MinValue;
            for (var t = 0; t < partition.Length; t++)
            {
                if (partition[t] != part)
                    continue;

                var p = 0.0;
                for (var i = 0; i < n; i++)
                {
                    p += (texels[(t * Channels) + list[i]] - mean[i]) * axis[i];
                }

                projected += p * p;
                min = Math.Min(min, p);
                max = Math.Max(max, p);
            }
        }

        for (var i = 0; i < n; i++)
        {
            endpoints[list[i]] = (float)Math.Clamp(mean[i] + (min * axis[i]), 0, 255);
            endpoints[Channels + list[i]] = (float)Math.Clamp(mean[i] + (max * axis[i]), 0, 255);
        }

        return Math.Max(0, total - projected);
    }

    private static double Normalize(Span<double> vector)
    {
        var length = 0.0;
        foreach (var value in vector)
        {
            length += value * value;
        }

        length = Math.Sqrt(length);
        if (length <= 1e-12)
            return 0;

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= length;
        }

        return length;
    }

    /// <summary>
    /// Refines the endpoints from the decoded weights: per partition and channel, the least-squares solution of
    /// (1 - u) × e0 + u × e1 = x over the texels, u the weight of the texel in the plane of the channel.
    /// </summary>
    private static void RefineEndpoints(ref Context context, int partitions, int channels, int planeChannel, scoped ReadOnlySpan<int> effective, scoped Span<float> endpoints)
    {
        for (var k = 0; k < partitions; k++)
        {
            for (var c = 0; c < Channels; c++)
            {
                if ((channels & (1 << c)) == 0)
                    continue;

                var plane = c == planeChannel ? 1 : 0;
                double a = 0, b = 0, d = 0, x0 = 0, x1 = 0;
                for (var t = 0; t < context.Count; t++)
                {
                    if (context.Partition[t] != k)
                        continue;

                    var u = effective[(t * 2) + plane] / 64.0;
                    var x = context.Texels[(t * Channels) + c];
                    a += (1 - u) * (1 - u);
                    b += (1 - u) * u;
                    d += u * u;
                    x0 += (1 - u) * x;
                    x1 += u * x;
                }

                var determinant = (a * d) - (b * b);
                if (determinant <= 1e-9 * Math.Max(1, a * d))
                    continue;

                endpoints[(k * 2 * Channels) + c] = (float)Math.Clamp(((d * x0) - (b * x1)) / determinant, 0, 255);
                endpoints[(k * 2 * Channels) + Channels + c] = (float)Math.Clamp(((a * x1) - (b * x0)) / determinant, 0, 255);
            }
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Weights (sections 23.16 and 23.17)
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Computes the ideal weight (0..1) of each texel in each plane, the projection of the texel on the endpoints of its
    /// partition, and its importance: the squared distance between the endpoints, by which an error of the weight scales.
    /// </summary>
    private static void ComputeIdealWeights(ref Context context, int channels, int planeChannel, scoped ReadOnlySpan<float> endpoints, scoped Span<float> ideal, scoped Span<float> importance)
    {
        var planes = planeChannel < 0 ? 1 : 2;
        for (var plane = 0; plane < planes; plane++)
        {
            var mask = planeChannel < 0 ? channels : plane == 0 ? channels & ~(1 << planeChannel) : 1 << planeChannel;
            for (var t = 0; t < context.Count; t++)
            {
                var e0 = endpoints.Slice(context.Partition[t] * 2 * Channels, Channels);
                var e1 = endpoints.Slice((context.Partition[t] * 2 * Channels) + Channels, Channels);
                float dot = 0, length = 0;
                for (var c = 0; c < Channels; c++)
                {
                    if ((mask & (1 << c)) == 0)
                        continue;

                    var direction = e1[c] - e0[c];
                    dot += (context.Texels[(t * Channels) + c] - e0[c]) * direction;
                    length += direction * direction;
                }

                ideal[(t * 2) + plane] = length > 0 ? Math.Clamp(dot / length, 0, 1) : 0;
                importance[(t * 2) + plane] = length;
            }
        }
    }

    /// <summary>
    /// Fits the grid weights (0..1) of each plane so that their infill matches the ideal weights of the texels, by weighted
    /// least squares (coordinate descent from the weighted averages).
    /// </summary>
    private static void Decimate(ref Context context, AstcWeightGrid grid, int planes, scoped ReadOnlySpan<float> ideal, scoped ReadOnlySpan<float> importance, scoped Span<float> gridWeights, int sweeps)
    {
        if (grid.Size == context.Count)
        {
            // A grid of the size of the footprint has the weight of each texel (the infill is the identity)
            for (var plane = 0; plane < planes; plane++)
            {
                for (var t = 0; t < context.Count; t++)
                {
                    gridWeights[(plane * grid.Size) + t] = ideal[(t * 2) + plane];
                }
            }

            return;
        }

        Span<float> reconstructed = stackalloc float[MaxTexels];
        for (var plane = 0; plane < planes; plane++)
        {
            var weights = gridWeights.Slice(plane * grid.Size, grid.Size);
            for (var g = 0; g < grid.Size; g++)
            {
                float sum = 0, total = 0, plainSum = 0, plainTotal = 0;
                for (var i = grid.Starts[g]; i < grid.Starts[g + 1]; i++)
                {
                    var entry = grid.Entries[i];
                    var t = entry >> 2;
                    var factor = grid.Factors[entry];
                    sum += importance[(t * 2) + plane] * factor * ideal[(t * 2) + plane];
                    total += importance[(t * 2) + plane] * factor;
                    plainSum += factor * ideal[(t * 2) + plane];
                    plainTotal += factor;
                }

                weights[g] = total > 1e-6f ? sum / total : plainTotal > 0 ? plainSum / plainTotal : 0.5f;
            }

            for (var t = 0; t < context.Count; t++)
            {
                float value = 0;
                for (var k = 0; k < 4; k++)
                {
                    value += grid.Factors[(t * 4) + k] * weights[grid.Indices[(t * 4) + k]];
                }

                reconstructed[t] = value / 16;
            }

            for (var sweep = 0; sweep < sweeps; sweep++)
            {
                for (var g = 0; g < grid.Size; g++)
                {
                    float numerator = 0, denominator = 0;
                    for (var i = grid.Starts[g]; i < grid.Starts[g + 1]; i++)
                    {
                        var entry = grid.Entries[i];
                        var t = entry >> 2;
                        var factor = grid.Factors[entry] / 16f;
                        var lambda = importance[(t * 2) + plane];
                        numerator += lambda * factor * (ideal[(t * 2) + plane] - reconstructed[t]);
                        denominator += lambda * factor * factor;
                    }

                    if (denominator <= 1e-6f)
                        continue;

                    var updated = Math.Clamp(weights[g] + (numerator / denominator), 0, 1);
                    var delta = updated - weights[g];
                    weights[g] = updated;
                    for (var i = grid.Starts[g]; i < grid.Starts[g + 1]; i++)
                    {
                        var entry = grid.Entries[i];
                        reconstructed[entry >> 2] += delta * grid.Factors[entry] / 16f;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Quantizes the grid weights to the nearest levels of the weight range, then (thorough mode) moves each one to a
    /// neighbor level when the infilled weights get nearer to the ideal ones. Writes the ranks of the levels and the
    /// infilled weights of the texels (0..64, as the decoder computes them).
    /// </summary>
    private static void QuantizeWeights(ref Context context, AstcWeightGrid grid, int planes, int weightRange, scoped ReadOnlySpan<float> gridWeights, scoped ReadOnlySpan<float> ideal, scoped ReadOnlySpan<float> importance, scoped Span<int> ranks, scoped Span<int> effective)
    {
        var levels = AstcEncodingTables.GetWeightLevelCount(weightRange);
        Span<int> sums = stackalloc int[MaxTexels];
        for (var plane = 0; plane < planes; plane++)
        {
            var planeRanks = ranks.Slice(plane * grid.Size, grid.Size);
            for (var g = 0; g < grid.Size; g++)
            {
                planeRanks[g] = AstcEncodingTables.GetNearestWeightRank(weightRange, (int)MathF.Round(gridWeights[(plane * grid.Size) + g] * 64, MidpointRounding.AwayFromZero));
            }

            for (var t = 0; t < context.Count; t++)
            {
                var sum = 0;
                for (var k = 0; k < 4; k++)
                {
                    sum += grid.Factors[(t * 4) + k] * AstcEncodingTables.GetRankWeight(weightRange, planeRanks[grid.Indices[(t * 4) + k]]);
                }

                sums[t] = sum;
            }

            if (context.Thorough && grid.Size < context.Count)
            {
                for (var pass = 0; pass < 2; pass++)
                {
                    var changed = false;
                    for (var g = 0; g < grid.Size; g++)
                    {
                        var current = AstcEncodingTables.GetRankWeight(weightRange, planeRanks[g]);
                        for (var direction = -1; direction <= 1; direction += 2)
                        {
                            var rank = planeRanks[g] + direction;
                            if (rank < 0 || rank >= levels)
                                continue;

                            var change = AstcEncodingTables.GetRankWeight(weightRange, rank) - current;
                            var gain = 0f;
                            for (var i = grid.Starts[g]; i < grid.Starts[g + 1]; i++)
                            {
                                var entry = grid.Entries[i];
                                var t = entry >> 2;
                                var target = ideal[(t * 2) + plane] * 64;
                                var before = ((sums[t] + 8) >> 4) - target;
                                var after = ((sums[t] + (grid.Factors[entry] * change) + 8) >> 4) - target;
                                gain += importance[(t * 2) + plane] * ((before * before) - (after * after));
                            }

                            if (gain > 0)
                            {
                                planeRanks[g] = rank;
                                for (var i = grid.Starts[g]; i < grid.Starts[g + 1]; i++)
                                {
                                    var entry = grid.Entries[i];
                                    sums[entry >> 2] += grid.Factors[entry] * change;
                                }

                                changed = true;
                                break;
                            }
                        }
                    }

                    if (!changed)
                        break;
                }
            }

            for (var t = 0; t < context.Count; t++)
            {
                effective[(t * 2) + plane] = (sums[t] + 8) >> 4;
            }
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Configuration search
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The weighted error of quantizing the ideal weights of the texels to the nearest levels of a weight range.</summary>
    private static double QuantizationError(ref Context context, int planes, scoped ReadOnlySpan<float> ideal, scoped ReadOnlySpan<float> importance, int weightRange)
    {
        var error = 0.0;
        for (var plane = 0; plane < planes; plane++)
        {
            for (var t = 0; t < context.Count; t++)
            {
                var target = ideal[(t * 2) + plane] * 64;
                var quantized = AstcEncodingTables.GetRankWeight(weightRange, AstcEncodingTables.GetNearestWeightRank(weightRange, (int)MathF.Round(target, MidpointRounding.AwayFromZero)));
                var difference = (quantized - target) / 64;
                error += importance[(t * 2) + plane] * difference * difference;
            }
        }

        return error;
    }

    /// <summary>
    /// The weighted residual of the least-squares fits of the ideal weights of the rows (or columns) of the footprint by
    /// the linear infill of a one-dimensional grid of <paramref name="gridSize"/> weights (section 23.17 in one dimension):
    /// tridiagonal normal equations, solved by the Thomas algorithm.
    /// </summary>
    private static double LineResidual(ref Context context, int planes, scoped ReadOnlySpan<float> ideal, scoped ReadOnlySpan<float> importance, int gridSize, bool horizontal)
    {
        var length = horizontal ? context.BlockWidth : context.BlockHeight;
        var lines = horizontal ? context.BlockHeight : context.BlockWidth;
        if (gridSize >= length)
            return 0;

        var scale = AstcBlockDecoder.GetInfillScale(length);
        Span<double> diagonal = stackalloc double[12];
        Span<double> upper = stackalloc double[12];
        Span<double> right = stackalloc double[12];
        Span<double> solution = stackalloc double[12];
        var total = 0.0;
        for (var plane = 0; plane < planes; plane++)
        {
            for (var line = 0; line < lines; line++)
            {
                diagonal.Clear();
                upper.Clear();
                right.Clear();
                for (var i = 0; i < length; i++)
                {
                    var t = horizontal ? i + (line * context.BlockWidth) : line + (i * context.BlockWidth);
                    var position = ((scale * i * (gridSize - 1)) + 32) >> 6;
                    var (j, f) = (position >> 4, position & 0xF);
                    double lambda = importance[(t * 2) + plane];
                    double w = ideal[(t * 2) + plane];
                    var a0 = (16 - f) / 16.0;
                    var a1 = f / 16.0;
                    diagonal[j] += lambda * a0 * a0;
                    right[j] += lambda * a0 * w;
                    if (f != 0)
                    {
                        diagonal[j + 1] += lambda * a1 * a1;
                        upper[j] += lambda * a0 * a1;
                        right[j + 1] += lambda * a1 * w;
                    }
                }

                // Forward elimination (a small regularization keeps a weight without texels at 0), then back substitution
                for (var j = 0; j < gridSize; j++)
                {
                    diagonal[j] += 1e-6;
                    if (j > 0)
                    {
                        var factor = upper[j - 1] / diagonal[j - 1];
                        diagonal[j] -= factor * upper[j - 1];
                        right[j] -= factor * right[j - 1];
                    }
                }

                var fitted = 0.0;
                for (var j = gridSize - 1; j >= 0; j--)
                {
                    solution[j] = (right[j] - (j + 1 < gridSize ? upper[j] * solution[j + 1] : 0)) / diagonal[j];
                }

                // The residual, from the texels reconstructed by the solution
                for (var i = 0; i < length; i++)
                {
                    var t = horizontal ? i + (line * context.BlockWidth) : line + (i * context.BlockWidth);
                    var position = ((scale * i * (gridSize - 1)) + 32) >> 6;
                    var (j, f) = (position >> 4, position & 0xF);
                    var reconstructed = (((16 - f) * solution[j]) + (f != 0 ? f * solution[j + 1] : 0)) / 16;
                    var difference = reconstructed - ideal[(t * 2) + plane];
                    fitted += importance[(t * 2) + plane] * difference * difference;
                }

                total += fitted;
            }
        }

        return total;
    }

    /// <summary>
    /// Estimates the error of each weight grid and range for the endpoint mode, partitions and planes, and encodes the
    /// <paramref name="keep"/> best ones (unless <paramref name="estimateOnly"/>). Returns the smallest estimate, in squared
    /// 8-bit units.
    /// </summary>
    private static double SearchConfigs(ref Context context, int partitions, int seed, int mode, int planeChannel, scoped ReadOnlySpan<float> endpoints, int keep, int iterations, bool estimateOnly = false)
    {
        var planes = planeChannel < 0 ? 1 : 2;
        var channels = ChannelMask(mode);
        var configs = context.Tables.GetConfigs(partitions > 1, planes == 2, ValueCount(mode) * partitions);
        if (configs.IsEmpty)
            return double.MaxValue;

        Span<float> ideal = stackalloc float[MaxTexels * 2];
        Span<float> importance = stackalloc float[MaxTexels * 2];
        ComputeIdealWeights(ref context, channels, planeChannel, endpoints, ideal, importance);
        // The error of quantizing the ideal weights to each weight range: weights that are all 0 or 1 lose nothing to
        // the smallest range
        Span<double> quantizationErrors = stackalloc double[AstcEncodingTables.WeightRangeCount];
        quantizationErrors.Fill(-1);

        // The error of fitting a grid, estimated separably (the infill is bilinear): the residuals of the least-squares
        // fits of the rows by the grid width and of the columns by the grid height
        Span<double> rowResiduals = stackalloc double[13];
        Span<double> columnResiduals = stackalloc double[13];
        rowResiduals.Fill(-1);
        columnResiduals.Fill(-1);

        // The best configurations by estimated error, the smallest first
        Span<int> best = stackalloc int[4];
        Span<double> bestEstimates = stackalloc double[4];
        keep = Math.Min(keep, best.Length);
        var found = 0;
        var outputs = OutputChannelCount(mode);
        for (var i = 0; i < configs.Length; i++)
        {
            var config = configs[i];
            if (rowResiduals[config.GridWidth] < 0)
            {
                rowResiduals[config.GridWidth] = LineResidual(ref context, planes, ideal, importance, config.GridWidth, horizontal: true);
            }

            if (columnResiduals[config.GridHeight] < 0)
            {
                columnResiduals[config.GridHeight] = LineResidual(ref context, planes, ideal, importance, config.GridHeight, horizontal: false);
            }

            if (quantizationErrors[config.WeightRange] < 0)
            {
                quantizationErrors[config.WeightRange] = QuantizationError(ref context, planes, ideal, importance, config.WeightRange);
            }

            // The endpoints are off by a uniform error of the color step, whose effect is 2/3 of it on average over the
            // weights (the mean of (1 - u)² + u²)
            var colorStep = 255.0 / (AstcEncodingTables.GetColorLevelCount(config.ColorRange) - 1);
            var estimate = rowResiduals[config.GridWidth] + columnResiduals[config.GridHeight] + quantizationErrors[config.WeightRange] + (context.Count * outputs * colorStep * colorStep / 18);

            // Insertion into the sorted list of the best ones (stable: the first configuration wins ties)
            var position = found;
            while (position > 0 && bestEstimates[position - 1] > estimate)
            {
                position--;
            }

            if (position >= keep)
                continue;

            for (var j = Math.Min(found, keep - 1); j > position; j--)
            {
                best[j] = best[j - 1];
                bestEstimates[j] = bestEstimates[j - 1];
            }

            best[position] = i;
            bestEstimates[position] = estimate;
            found = Math.Min(found + 1, keep);
        }

        if (estimateOnly || found == 0)
            return found == 0 ? double.MaxValue : bestEstimates[0];

        for (var i = 0; i < found; i++)
        {
            EncodeConfig(ref context, configs[best[i]], partitions, seed, mode, planeChannel, endpoints, iterations);
            if (context.BestError == 0)
                break;
        }

        return bestEstimates[0];
    }

    /// <summary>Encodes a configuration: endpoints, weights and refinements, keeping the block if its error is the smallest so far.</summary>
    private static void EncodeConfig(ref Context context, AstcGridConfig config, int partitions, int seed, int mode, int planeChannel, scoped ReadOnlySpan<float> initialEndpoints, int iterations)
    {
        var planes = planeChannel < 0 ? 1 : 2;
        var channels = ChannelMask(mode);
        var grid = context.Tables.GetGrid(config);
        Span<float> endpoints = stackalloc float[3 * 2 * Channels];
        initialEndpoints[..(partitions * 2 * Channels)].CopyTo(endpoints);
        Span<float> decodedEndpoints = stackalloc float[3 * 2 * Channels];
        Span<int> values = stackalloc int[18];
        Span<int> decoded = stackalloc int[3 * 8];
        Span<float> ideal = stackalloc float[MaxTexels * 2];
        Span<float> importance = stackalloc float[MaxTexels * 2];
        Span<float> gridWeights = stackalloc float[MaxGridWeights * 2];
        Span<int> ranks = stackalloc int[MaxGridWeights * 2];
        Span<int> effective = stackalloc int[MaxTexels * 2];
        var previous = long.MaxValue;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            QuantizeEndpoints(mode, config.ColorRange, partitions, endpoints, values, decoded);

            // The weights are fitted to the decoded endpoints
            for (var k = 0; k < partitions; k++)
            {
                for (var c = 0; c < Channels; c++)
                {
                    var source = c == Luminance ? 0 : c;
                    decodedEndpoints[(k * 2 * Channels) + c] = decoded[(k * 8) + source];
                    decodedEndpoints[(k * 2 * Channels) + Channels + c] = decoded[(k * 8) + 4 + source];
                }
            }

            ComputeIdealWeights(ref context, channels, planeChannel, decodedEndpoints, ideal, importance);
            Decimate(ref context, grid, planes, ideal, importance, gridWeights, sweeps: context.Thorough ? 4 : 2);
            QuantizeWeights(ref context, grid, planes, config.WeightRange, gridWeights, ideal, importance, ranks, effective);
            var error = ComputeError(ref context, decoded, effective, planeChannel, context.BestError);
            if (error < context.BestError)
            {
                context.BestError = error;
                context.Best = WriteBlock(config, partitions, seed, mode, planeChannel, values[..(ValueCount(mode) * partitions)], ranks, grid.Size);
                if (error == 0)
                    return;
            }

            if (error >= previous)
                return;

            previous = error;
            if (iteration + 1 < iterations)
            {
                RefineEndpoints(ref context, partitions, channels, planeChannel, effective, endpoints);
            }
        }
    }

    /// <summary>
    /// The squared error of the decoded block, in the space of the operation mode: 8-bit results (sRGB) or UNORM16 results
    /// against v × 257 (linear), as the decoder interpolates them (section 23.18). Stops early above <paramref name="limit"/>.
    /// </summary>
    private static long ComputeError(ref Context context, scoped ReadOnlySpan<int> decoded, scoped ReadOnlySpan<int> effective, int planeChannel, long limit)
    {
        long error = 0;
        for (var t = 0; t < context.Count; t++)
        {
            var endpoint = decoded.Slice(context.Partition[t] * 8, 8);
            for (var c = 0; c < 4; c++)
            {
                var weight = effective[(t * 2) + (c == planeChannel ? 1 : 0)];
                var x = context.Source[(t * 4) + c];
                long difference;
                if (context.Srgb)
                {
                    difference = (((((endpoint[c] << 8) | 0x80) * (64 - weight)) + (((endpoint[4 + c] << 8) | 0x80) * weight) + 32) >> 14) - x;
                }
                else
                {
                    difference = (((endpoint[c] * 257 * (64 - weight)) + (endpoint[4 + c] * 257 * weight) + 32) >> 6) - (x * 257);
                }

                error += difference * difference;
            }

            if (error >= limit)
                return error;
        }

        return error;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Partitions (section 23.20)
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Selects the partition patterns to encode: the texels are clustered (k-means), the patterns that differ from the
    /// clusters in the fewest texels are kept, and those whose partitions are the nearest to lines win.
    /// </summary>
    private static int SelectPartitionSeeds(ref Context context, int footprint, int partitions, int channels, scoped Span<int> seeds)
    {
        var count = context.Count;
        Span<float> centers = stackalloc float[3 * Channels];
        Span<byte> labels = stackalloc byte[MaxTexels];
        labels = labels[..count];

        // Initial centers: the extremes of the principal axis, then (three partitions) the texel the farthest from both
        Span<float> line = stackalloc float[2 * Channels];
        context.Partition.Clear();
        FitLine(context.Texels, context.Partition, 0, channels, line);
        line.CopyTo(centers);
        if (partitions == 3)
        {
            var farthest = 0;
            var farthestDistance = -1f;
            for (var t = 0; t < count; t++)
            {
                var distance = Math.Min(Distance(context.Texels, t, centers[..Channels], channels), Distance(context.Texels, t, centers.Slice(Channels, Channels), channels));
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthest = t;
                }
            }

            context.Texels.Slice(farthest * Channels, Channels).CopyTo(centers[(2 * Channels)..]);
        }

        Span<float> sums = stackalloc float[3 * Channels];
        Span<int> sizes = stackalloc int[3];
        for (var iteration = 0; iteration < 5; iteration++)
        {
            sums.Clear();
            sizes.Clear();
            for (var t = 0; t < count; t++)
            {
                var label = 0;
                var nearest = float.MaxValue;
                for (var k = 0; k < partitions; k++)
                {
                    var distance = Distance(context.Texels, t, centers.Slice(k * Channels, Channels), channels);
                    if (distance < nearest)
                    {
                        nearest = distance;
                        label = k;
                    }
                }

                labels[t] = (byte)label;
                sizes[label]++;
                for (var c = 0; c < Channels; c++)
                {
                    sums[(label * Channels) + c] += context.Texels[(t * Channels) + c];
                }
            }

            for (var k = 0; k < partitions; k++)
            {
                if (sizes[k] == 0)
                    continue;

                for (var c = 0; c < Channels; c++)
                {
                    centers[(k * Channels) + c] = sums[(k * Channels) + c] / sizes[k];
                }
            }
        }

        Span<ulong> ideal = stackalloc ulong[3 * 3];
        ideal.Clear();
        for (var t = 0; t < count; t++)
        {
            ideal[(labels[t] * 3) + (t >> 6)] |= 1UL << (t & 63);
        }

        // The patterns that differ the least from the clusters (any assignment of the clusters to the partitions)
        var patterns = AstcFootprintTables.GetPartitionPatterns(footprint, partitions);
        var masks = patterns.Masks;
        const int Candidates = 8;
        var words = (count + 63) >> 6;
        Span<int> overlaps = stackalloc int[9];
        Span<int> candidates = stackalloc int[Candidates];
        Span<int> mismatches = stackalloc int[Candidates];
        var found = 0;
        for (var index = 0; index < patterns.Seeds.Length; index++)
        {
            var seed = patterns.Seeds[index];
            var pattern = masks.AsSpan(index * partitions * 3, partitions * 3);
            int agreement;
            if (partitions == 2)
            {
                var same = Overlap(pattern, 0, ideal, 0, words) + Overlap(pattern, 1, ideal, 1, words);
                agreement = Math.Max(same, count - same);
            }
            else
            {
                // The overlaps of each partition with each cluster, then the best of the 6 assignments
                for (var k = 0; k < 3; k++)
                {
                    for (var label = 0; label < 3; label++)
                    {
                        overlaps[(k * 3) + label] = Overlap(pattern, k, ideal, label, words);
                    }
                }

                agreement = Math.Max(
                    Math.Max(Math.Max(overlaps[0] + overlaps[4] + overlaps[8], overlaps[0] + overlaps[5] + overlaps[7]), Math.Max(overlaps[1] + overlaps[3] + overlaps[8], overlaps[1] + overlaps[5] + overlaps[6])),
                    Math.Max(overlaps[2] + overlaps[3] + overlaps[7], overlaps[2] + overlaps[4] + overlaps[6]));
            }

            var mismatch = count - agreement;
            var position = found;
            while (position > 0 && mismatches[position - 1] > mismatch)
            {
                position--;
            }

            if (position >= Candidates)
                continue;

            for (var j = Math.Min(found, Candidates - 1); j > position; j--)
            {
                candidates[j] = candidates[j - 1];
                mismatches[j] = mismatches[j - 1];
            }

            candidates[position] = seed;
            mismatches[position] = mismatch;
            found = Math.Min(found + 1, Candidates);
        }

        // The candidates whose partitions are the nearest to lines (the first wins ties)
        Span<double> residuals = stackalloc double[Candidates];
        for (var i = 0; i < found; i++)
        {
            SetPartitions(ref context, partitions, candidates[i]);
            var residual = 0.0;
            for (var k = 0; k < partitions; k++)
            {
                residual += FitLine(context.Texels, context.Partition, k, channels, line);
            }

            residuals[i] = residual;
        }

        var selected = 0;
        for (; selected < seeds.Length && selected < found; selected++)
        {
            var bestIndex = -1;
            for (var i = 0; i < found; i++)
            {
                if (residuals[i] >= 0 && (bestIndex < 0 || residuals[i] < residuals[bestIndex]))
                {
                    bestIndex = i;
                }
            }

            seeds[selected] = candidates[bestIndex];
            residuals[bestIndex] = -1;
        }

        context.Partition.Clear();
        return selected;

        static int Overlap(ReadOnlySpan<ulong> patterns, int partition, ReadOnlySpan<ulong> ideal, int label, int words)
        {
            var result = 0;
            for (var i = 0; i < words; i++)
            {
                result += BitOperations.PopCount(patterns[(partition * 3) + i] & ideal[(label * 3) + i]);
            }

            return result;
        }
    }

    private static float Distance(ReadOnlySpan<float> texels, int texel, ReadOnlySpan<float> center, int channels)
    {
        var distance = 0f;
        for (var c = 0; c < Channels; c++)
        {
            if ((channels & (1 << c)) != 0)
            {
                var difference = texels[(texel * Channels) + c] - center[c];
                distance += difference * difference;
            }
        }

        return distance;
    }

    private static void SetPartitions(ref Context context, int partitions, int seed)
    {
        var smallBlock = context.Count < 31;
        for (var t = 0; t < context.Count; t++)
        {
            context.Partition[t] = (byte)AstcBlockDecoder.SelectPartition(seed, t % context.BlockWidth, t / context.BlockWidth, 0, partitions, smallBlock);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Block layout (sections 23.9 to 23.12, 23.22)
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes the bits of a block: the block mode, the partitions and the endpoint mode (one mode shared by the partitions,
    /// section 23.9.2), the endpoint integers from the bottom, the weights from the top in reverse bit order (section
    /// 23.15), and the channel of the second plane below the weights.
    /// </summary>
    private static UInt128 WriteBlock(AstcGridConfig config, int partitions, int seed, int mode, int planeChannel, ReadOnlySpan<int> values, ReadOnlySpan<int> ranks, int gridSize)
    {
        UInt128 block = (uint)config.BlockMode;
        block |= (UInt128)(uint)(partitions - 1) << 11;
        int colorStart;
        if (partitions == 1)
        {
            block |= (UInt128)(uint)mode << 13;
            colorStart = 17;
        }
        else
        {
            // Bits 23..24 are 0: every partition uses the endpoint mode in bits 25..28
            block |= (UInt128)(uint)seed << 13;
            block |= (UInt128)(uint)mode << 25;
            colorStart = 29;
        }

        var color = AstcBlockDecoder.ColorRanges[config.ColorRange];
        AstcEncodingTables.WriteSequence(ref block, colorStart, values, color.Trits, color.Quints, color.Bits);

        var planes = planeChannel < 0 ? 1 : 2;
        Span<int> weights = stackalloc int[MaxGridWeights];
        weights = weights[..(gridSize * planes)];
        for (var g = 0; g < gridSize; g++)
        {
            for (var plane = 0; plane < planes; plane++)
            {
                weights[(g * planes) + plane] = AstcEncodingTables.GetRankValue(config.WeightRange, ranks[(plane * gridSize) + g]);
            }
        }

        var range = AstcEncodingTables.WeightRanges[config.WeightRange];
        UInt128 weightBits = 0;
        AstcEncodingTables.WriteSequence(ref weightBits, 0, weights, range.Trits, range.Quints, range.Bits);
        block |= AstcBlockDecoder.ReverseBits(weightBits);
        if (planes == 2)
        {
            var weightBitCount = AstcBlockDecoder.GetSequenceBits(weights.Length, range.Trits, range.Quints, range.Bits);
            block |= (UInt128)(uint)planeChannel << (128 - weightBitCount - 2);
        }

        return block;
    }

    /// <summary>
    /// Writes a void-extent block (section 23.22): an LDR color of UNORM16 components v × 257, exact in both operation
    /// modes (the sRGB mode keeps their top 8 bits, the linear mode decodes them as UNORM16), and the extent coordinates all
    /// ones, which mean that the block has no extent.
    /// </summary>
    private static void WriteVoidExtent(ReadOnlySpan<byte> rgba, Span<byte> block)
    {
        const ulong Low = 0xFFFF_FFFF_FFFF_FDFC;
        ulong high = 0;
        for (var c = 0; c < 4; c++)
        {
            high |= (ulong)(rgba[c] * 257) << (16 * c);
        }

        BinaryPrimitives.WriteUInt64LittleEndian(block, Low);
        BinaryPrimitives.WriteUInt64LittleEndian(block[8..], high);
    }
}
