using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoder of the blocks of 4×4 texels of BC7 (BPTC_UNORM: Khronos Data Format Specification 1.4, section 20.1, and
/// the Direct3D 11.3 functional specification): the 16 RGBA texels of a block in, the 16 bytes of the block out.
/// </summary>
/// <remarks>
/// <para>
/// Each candidate (a mode, a partition, a rotation, an index selection) is fitted subset by subset: the endpoints at the
/// extremes of the principal axis of the texels, rounded to the precision of the mode with the best P-bits, then refined by
/// least squares on the indices they get. The error of a candidate is the sum of the squared differences of the four
/// channels of the texels decoded with Equation 2, so the candidates of different modes compare exactly.
/// </para>
/// <para>
/// The fast level tries mode 6 (one subset, 4-bit indices, RGBA endpoints of 7 bits and a P-bit), the best single-subset
/// mode for most blocks, and, when alpha varies, modes 5 and 4 (separate indices for alpha). When the colors are not on a
/// line (the error is still large), it adds the partition of two subsets of the smallest estimated error (the residual of
/// a line fit of each subset) in modes 3 and 1, and 7 when alpha varies. The thorough level fits every rotation of modes 4
/// and 5, both index selections of mode 4, and the modes of two subsets (1, 3, 7) and three subsets (0, 2) on the
/// partitions of the smallest estimated errors at the fast level; then it fits the best few configurations again with more
/// least-squares iterations and every combination of P-bits, and polishes them by moving each stored endpoint value by one
/// step while the error decreases.
/// </para>
/// <para>
/// A block whose alpha is the same for every texel keeps it exactly: candidates that would change it are rejected (mode 6
/// can always keep it: its 7-bit alpha and P-bit form 8 bits). Opaque blocks therefore decode with an alpha of 255. A
/// block of one color uses mode 5, whose interpolated entry reproduces any 8-bit value from 7-bit endpoints.
/// </para>
/// <para>
/// The encoder is deterministic (integer decisions, scalar double arithmetic in a fixed order) and allocation-free.
/// </para>
/// </remarks>
internal static class Bc7BlockEncoder
{
    /// <summary>The error of a candidate that cannot be used (it would change a constant alpha).</summary>
    private const long Infeasible = long.MaxValue;

    /// <summary>The penalty of a P-bit that cannot keep a constant alpha, when choosing P-bits by the error of the endpoints.</summary>
    private const double ConstraintPenalty = 1e12;

    /// <summary>
    /// The error of the single-subset modes above which the fast level also tries the best partition of two subsets: an
    /// average squared error of 16 per texel (a deviation of about 2 per channel).
    /// </summary>
    private const long FastTwoSubsetThreshold = 16 * 16;

    /// <summary>The number of partitions of two subsets (those of the smallest estimated errors) the thorough level fits in modes 1, 3 and 7.</summary>
    private const int TwoSubsetCandidates = 4;

    /// <summary>The number of partitions of three subsets (those of the smallest estimated errors) the thorough level fits in modes 0 and 2.</summary>
    private const int ThreeSubsetCandidates = 3;

    /// <summary>The number of moments of a texel for the estimate of a partition: 4 channels and 10 products of channels.</summary>
    private const int MomentCount = 14;

    /// <summary>The number of power iterations of the principal axis of a subset when estimating the error of a partition.</summary>
    private const int EstimateIterations = 4;

    /// <summary>The number of configurations the thorough level fits again after fitting every configuration at the fast level.</summary>
    private const int ShortlistLength = 3;

    /// <summary>The maximum number of passes of the polish of the best candidate.</summary>
    private const int MaxPolishPasses = 8;

    /// <summary>
    /// For each 8-bit value, the 7-bit endpoints <c>(q0, q1)</c> of mode 5 whose entry of weight 21 (index 1 of 2 bits) is
    /// that value, or the nearest to it.
    /// </summary>
    private static readonly byte[] SolidColorTable = BuildSolidColorTable();

    /// <summary>
    /// For indices of 2, 3 and 4 bits (65 entries each), the level whose weight (Table 119) is the nearest to each weight
    /// from 0 to 64, the lower level on a tie.
    /// </summary>
    private static readonly byte[] NearestLevels = BuildNearestLevels();

    /// <summary>
    /// The texels of the subsets after the first of each partition, as 16-bit masks: subset 1 of the partitions of two
    /// subsets (64), then subsets 1 and 2 of the partitions of three subsets (64 each).
    /// </summary>
    private static readonly ushort[] PartitionMasks = BuildPartitionMasks();

    /// <summary>Encodes a BC7 block.</summary>
    /// <param name="rgba">The 64 bytes of the 16 RGBA texels.</param>
    /// <param name="block">The 16 bytes of the block.</param>
    /// <param name="thorough">Whether to search the modes of two and three subsets and every rotation (slower, better).</param>
    public static void Encode(ReadOnlySpan<byte> rgba, Span<byte> block, bool thorough)
    {
        var texels = rgba[..64];
        var constantAlpha = (int)texels[3];
        var solid = true;
        var first = BinaryPrimitives.ReadUInt32LittleEndian(texels);
        for (var i = 1; i < 16; i++)
        {
            var texel = BinaryPrimitives.ReadUInt32LittleEndian(texels[(i * 4)..]);
            solid &= texel == first;
            if (texels[(i * 4) + 3] != constantAlpha)
            {
                constantAlpha = -1;
            }
        }

        var best = default(Candidate);
        best.Error = long.MaxValue;
        if (solid)
        {
            EncodeSolid(texels, ref best);
            if (best.Error == 0)
            {
                WriteBlock(ref best, block);
                return;
            }
        }

        var current = default(Candidate);
        if (!thorough)
        {
            TryConfiguration(texels, constantAlpha, new Configuration(6, 0, 0, 0), FitLevel.Fast, ref current, ref best);
            if (constantAlpha < 0)
            {
                TryConfiguration(texels, constantAlpha, new Configuration(5, 0, 0, 0), FitLevel.Fast, ref current, ref best);
                TryConfiguration(texels, constantAlpha, new Configuration(4, 0, 0, 0), FitLevel.Fast, ref current, ref best);
            }

            // A block that is not a line of colors: the best partition of two subsets, in mode 3 (2-bit indices, 8-bit
            // endpoints), mode 1 (3-bit indices, 7-bit endpoints) and mode 7 (RGBA) when alpha varies
            if (best.Error > FastTwoSubsetThreshold)
            {
                Span<double> scores = stackalloc double[64];
                Span<int> selected = stackalloc int[1];
                EstimatePartitions(texels, 2, scores);
                SelectPartitions(scores, 1, selected);
                TryConfiguration(texels, constantAlpha, new Configuration(3, selected[0], 0, 0), FitLevel.Fast, ref current, ref best);
                TryConfiguration(texels, constantAlpha, new Configuration(1, selected[0], 0, 0), FitLevel.Fast, ref current, ref best);
                if (constantAlpha < 0)
                {
                    TryConfiguration(texels, constantAlpha, new Configuration(7, selected[0], 0, 0), FitLevel.Fast, ref current, ref best);
                }
            }
        }
        else
        {
            // Every configuration is fitted at the fast level, then the best few again with every P-bit combination,
            // more least-squares iterations and the polish
            Span<Configuration> shortlist = stackalloc Configuration[ShortlistLength];
            var listed = 0;
            Consider(texels, constantAlpha, new Configuration(6, 0, 0, 0), shortlist, ref listed, ref current, ref best);
            for (var rotation = 0; rotation < 4; rotation++)
            {
                Consider(texels, constantAlpha, new Configuration(5, 0, rotation, 0), shortlist, ref listed, ref current, ref best);
                Consider(texels, constantAlpha, new Configuration(4, 0, rotation, 0), shortlist, ref listed, ref current, ref best);
                Consider(texels, constantAlpha, new Configuration(4, 0, rotation, 1), shortlist, ref listed, ref current, ref best);
            }

            Span<double> scores = stackalloc double[64];
            Span<int> selected = stackalloc int[Math.Max(TwoSubsetCandidates, ThreeSubsetCandidates)];
            EstimatePartitions(texels, 2, scores);
            var count = SelectPartitions(scores, TwoSubsetCandidates, selected);
            for (var i = 0; i < count; i++)
            {
                Consider(texels, constantAlpha, new Configuration(1, selected[i], 0, 0), shortlist, ref listed, ref current, ref best);
                Consider(texels, constantAlpha, new Configuration(3, selected[i], 0, 0), shortlist, ref listed, ref current, ref best);
                Consider(texels, constantAlpha, new Configuration(7, selected[i], 0, 0), shortlist, ref listed, ref current, ref best);
            }

            EstimatePartitions(texels, 3, scores);
            count = SelectPartitions(scores, ThreeSubsetCandidates, selected);
            for (var i = 0; i < count; i++)
            {
                Consider(texels, constantAlpha, new Configuration(2, selected[i], 0, 0), shortlist, ref listed, ref current, ref best);
            }

            // Mode 0 has 4 partition bits: the first 16 partitions of three subsets only
            count = SelectPartitions(scores[..16], ThreeSubsetCandidates, selected);
            for (var i = 0; i < count; i++)
            {
                Consider(texels, constantAlpha, new Configuration(0, selected[i], 0, 0), shortlist, ref listed, ref current, ref best);
            }

            for (var i = 0; i < listed && best.Error > 0; i++)
            {
                TryConfiguration(texels, constantAlpha, shortlist[i], FitLevel.Thorough, ref current, ref best);
            }
        }

        WriteBlock(ref best, block);
    }

    /// <summary>Encodes a block of one color with mode 5: every channel exact when the table reaches its value.</summary>
    private static void EncodeSolid(ReadOnlySpan<byte> texels, ref Candidate best)
    {
        best.Mode = 5;
        best.Partition = 0;
        best.Rotation = 0;
        best.IndexSelection = 0;
        long error = 0;
        for (var c = 0; c < 3; c++)
        {
            int q0 = SolidColorTable[texels[c] * 2];
            int q1 = SolidColorTable[(texels[c] * 2) + 1];
            best.Endpoints[c] = q0;
            best.Endpoints[4 + c] = q1;
            var decoded = Interpolate(Expand(q0, 0, 7, hasPBit: false), Expand(q1, 0, 7, hasPBit: false), BptcTables.Weights2[1]);
            error += (decoded - texels[c]) * (decoded - texels[c]);
        }

        // Alpha has 8 bits: exact with both endpoints equal
        best.Endpoints[3] = texels[3];
        best.Endpoints[7] = texels[3];
        for (var i = 0; i < 16; i++)
        {
            best.ColorIndices[i] = 1;
            best.AlphaIndices[i] = 0;
        }

        best.Error = error * 16;
    }

    /// <summary>Encodes a configuration and keeps it when its error is smaller than the best one.</summary>
    private static void TryConfiguration(ReadOnlySpan<byte> texels, int constantAlpha, Configuration configuration, FitLevel level, ref Candidate current, ref Candidate best)
    {
        current.Mode = configuration.Mode;
        current.Partition = configuration.Partition;
        current.Rotation = configuration.Rotation;
        current.IndexSelection = configuration.IndexSelection;
        current.Error = EncodeConfiguration(texels, constantAlpha, ref current, level, best.Error);
        if (current.Error < best.Error)
        {
            best = current;
        }
    }

    /// <summary>
    /// Encodes a configuration at the fast level and lists it among the configurations of the smallest errors, which the
    /// thorough level fits again.
    /// </summary>
    private static void Consider(ReadOnlySpan<byte> texels, int constantAlpha, Configuration configuration, Span<Configuration> shortlist, ref int listed, ref Candidate current, ref Candidate best)
    {
        // Pruned against the last listed configuration, not the best: a configuration that is not the best can be listed
        var limit = listed == shortlist.Length ? shortlist[listed - 1].Error : long.MaxValue;
        current.Mode = configuration.Mode;
        current.Partition = configuration.Partition;
        current.Rotation = configuration.Rotation;
        current.IndexSelection = configuration.IndexSelection;
        var error = EncodeConfiguration(texels, constantAlpha, ref current, FitLevel.Fast, limit);
        current.Error = error;
        if (error < best.Error)
        {
            best = current;
        }

        if (error >= limit)
            return;

        var position = Math.Min(listed, shortlist.Length - 1);
        while (position > 0 && shortlist[position - 1].Error > error)
        {
            shortlist[position] = shortlist[position - 1];
            position--;
        }

        shortlist[position] = configuration with { Error = error };
        listed = Math.Min(listed + 1, shortlist.Length);
    }

    /// <summary>Fits the endpoints and indices of every subset of a configuration.</summary>
    /// <returns>
    /// The squared error of the four channels, or <see cref="Infeasible"/> (also when the error of the subsets fitted so far
    /// reaches <paramref name="limit"/>: the configuration cannot be the best).
    /// </returns>
    private static long EncodeConfiguration(ReadOnlySpan<byte> texels, int constantAlpha, ref Candidate candidate, FitLevel level, long limit)
    {
        var mode = candidate.Mode;
        var parameters = BlockDecoder.Bc7Modes[mode];
        Span<int> endpoints = candidate.Endpoints;
        Span<int> pBits = candidate.PBits;
        Span<byte> colorIndices = candidate.ColorIndices;
        if (mode is 4 or 5)
        {
            // Table 120: the rotation swaps alpha with a color channel after decoding, so the encoder swaps them before
            Span<byte> rotated = stackalloc byte[64];
            texels.CopyTo(rotated);
            var rotation = candidate.Rotation;
            if (rotation != 0)
            {
                for (var i = 0; i < 16; i++)
                {
                    (rotated[(i * 4) + 3], rotated[(i * 4) + rotation - 1]) = (rotated[(i * 4) + rotation - 1], rotated[(i * 4) + 3]);
                }
            }

            var alphaChannel = rotation == 0 ? 3 : rotation - 1;
            var wideColorIndices = mode == 4 && candidate.IndexSelection == 1;
            var colorSettings = new FitSettings(0b0111, parameters.ColorBits, parameters.AlphaBits, PBitKind.None, wideColorIndices ? 3 : 2, constantAlpha >= 0 && alphaChannel < 3 ? alphaChannel : -1, constantAlpha);
            var alphaSettings = new FitSettings(0b1000, parameters.ColorBits, parameters.AlphaBits, PBitKind.None, mode == 5 || wideColorIndices ? 2 : 3, constantAlpha >= 0 && alphaChannel == 3 ? 3 : -1, constantAlpha);
            var colorError = FitSubset(rotated, 0xFFFF, colorSettings, level, endpoints[..8], pBits[..2], colorIndices);
            if (colorError >= limit)
                return Infeasible;

            var alphaError = FitSubset(rotated, 0xFFFF, alphaSettings, level, endpoints[..8], pBits[..2], candidate.AlphaIndices);
            if (alphaError == Infeasible)
                return Infeasible;

            return colorError + alphaError;
        }

        long error = 0;
        var channelMask = 0b1111;
        if (parameters.AlphaBits == 0)
        {
            // Modes 0 to 3 decode an alpha of 255
            if (constantAlpha >= 0 && constantAlpha != 255)
                return Infeasible;

            channelMask = 0b0111;
            for (var i = 0; i < 16; i++)
            {
                var difference = 255 - texels[(i * 4) + 3];
                error += difference * difference;
            }

            if (error >= limit)
                return Infeasible;
        }

        var pBitKind = parameters.EndpointPBits ? PBitKind.Unique : parameters.SharedPBits ? PBitKind.Shared : PBitKind.None;
        var settings = new FitSettings(channelMask, parameters.ColorBits, parameters.AlphaBits, pBitKind, parameters.IndexBits, constantAlpha >= 0 && channelMask == 0b1111 ? 3 : -1, constantAlpha);
        for (var subset = 0; subset < parameters.Subsets; subset++)
        {
            var texelMask = 0;
            for (var i = 0; i < 16; i++)
            {
                if (BptcTables.GetSubset(parameters.Subsets, candidate.Partition, i) == subset)
                {
                    texelMask |= 1 << i;
                }
            }

            var subsetError = FitSubset(texels, texelMask, settings, level, endpoints.Slice(subset * 8, 8), pBits.Slice(subset * 2, 2), colorIndices);
            if (subsetError == Infeasible)
                return Infeasible;

            error += subsetError;
            if (error >= limit)
                return Infeasible;
        }

        return error;
    }

    /// <summary>
    /// Fits the two endpoints and the indices of the texels of one subset, in the channels of the settings.
    /// </summary>
    /// <param name="texels">The 16 RGBA texels.</param>
    /// <param name="texelMask">The texels of the subset.</param>
    /// <param name="settings">The channels, precision, P-bits and indices of the mode.</param>
    /// <param name="level">The effort.</param>
    /// <param name="endpoints">The stored values of the endpoints: endpoint 0 channels 0 to 3, then endpoint 1; only the channels of the settings are written.</param>
    /// <param name="pBits">The P-bits of the two endpoints.</param>
    /// <param name="indices">The indices of the texels: only those of the subset are written.</param>
    /// <returns>The squared error of the channels of the settings, or <see cref="Infeasible"/>.</returns>
    private static long FitSubset(ReadOnlySpan<byte> texels, int texelMask, in FitSettings settings, FitLevel level, Span<int> endpoints, Span<int> pBits, Span<byte> indices)
    {
        Span<int> points = stackalloc int[64];
        Span<int> texelIds = stackalloc int[16];
        var count = 0;
        for (var i = 0; i < 16; i++)
        {
            if ((texelMask & (1 << i)) == 0)
                continue;

            // The channels out of the fit are 0, as their palette entries: the distances can always sum four channels
            for (var c = 0; c < 4; c++)
            {
                points[(count * 4) + c] = (settings.ChannelMask & (1 << c)) != 0 ? texels[(i * 4) + c] : 0;
            }

            texelIds[count] = i;
            count++;
        }

        Span<int> channels = stackalloc int[4];
        var dimensions = 0;
        for (var c = 0; c < 4; c++)
        {
            if ((settings.ChannelMask & (1 << c)) != 0)
            {
                channels[dimensions++] = c;
            }
        }

        // The principal axis of the texels, and the endpoints at the extremes of their projections on it
        Span<double> mean = stackalloc double[4];
        Span<double> scatter = stackalloc double[16];
        Span<double> axis = stackalloc double[4];
        for (var k = 0; k < count; k++)
        {
            for (var d = 0; d < dimensions; d++)
            {
                mean[d] += points[(k * 4) + channels[d]];
            }
        }

        for (var d = 0; d < dimensions; d++)
        {
            mean[d] /= count;
        }

        for (var k = 0; k < count; k++)
        {
            for (var d = 0; d < dimensions; d++)
            {
                for (var e = 0; e < dimensions; e++)
                {
                    scatter[(d * dimensions) + e] += (points[(k * 4) + channels[d]] - mean[d]) * (points[(k * 4) + channels[e]] - mean[e]);
                }
            }
        }

        BcnEncoderMath.PrincipalAxis(scatter[..(dimensions * dimensions)], dimensions, axis);
        var minimum = 0.0;
        var maximum = 0.0;
        for (var k = 0; k < count; k++)
        {
            var t = 0.0;
            for (var d = 0; d < dimensions; d++)
            {
                t += (points[(k * 4) + channels[d]] - mean[d]) * axis[d];
            }

            minimum = Math.Min(minimum, t);
            maximum = Math.Max(maximum, t);
        }

        Span<double> endpoint0 = stackalloc double[4];
        Span<double> endpoint1 = stackalloc double[4];
        for (var d = 0; d < dimensions; d++)
        {
            endpoint0[channels[d]] = mean[d] + (minimum * axis[d]);
            endpoint1[channels[d]] = mean[d] + (maximum * axis[d]);
        }

        var state = new FitState(points[..(count * 4)], channels[..dimensions], settings);
        Span<int> bestValues = stackalloc int[8];
        Span<int> bestPBits = stackalloc int[2];
        Span<byte> bestIndices = stackalloc byte[16];
        var bestError = Infeasible;
        Span<double> bestEndpoint0 = stackalloc double[4];
        Span<double> bestEndpoint1 = stackalloc double[4];
        endpoint0.CopyTo(bestEndpoint0);
        endpoint1.CopyTo(bestEndpoint1);
        QuantizeAndEvaluate(in state, allPBitCombinations: false, endpoint0, endpoint1, bestValues, bestPBits, bestIndices, ref bestError);

        // Least squares on the indices, while the error decreases
        var iterations = level == FitLevel.Fast ? 1 : 3;
        for (var iteration = 0; iteration < iterations && bestError != Infeasible && bestError > 0; iteration++)
        {
            if (!LeastSquares(in state, bestIndices, endpoint0, endpoint1))
                break;

            var previous = bestError;
            QuantizeAndEvaluate(in state, allPBitCombinations: false, endpoint0, endpoint1, bestValues, bestPBits, bestIndices, ref bestError);
            if (bestError >= previous)
                break;

            endpoint0.CopyTo(bestEndpoint0);
            endpoint1.CopyTo(bestEndpoint1);
        }

        // The P-bits nearest to the real endpoints are not always the best once the indices are chosen: the thorough levels
        // try every combination on the best endpoints
        if (level != FitLevel.Fast && settings.PBitKind != PBitKind.None)
        {
            QuantizeAndEvaluate(in state, allPBitCombinations: true, bestEndpoint0, bestEndpoint1, bestValues, bestPBits, bestIndices, ref bestError);
        }

        if (level == FitLevel.Thorough && bestError != Infeasible)
        {
            Polish(in state, bestValues, bestPBits, bestIndices, ref bestError);
        }

        if (bestError == Infeasible)
            return Infeasible;

        for (var d = 0; d < dimensions; d++)
        {
            endpoints[channels[d]] = bestValues[channels[d]];
            endpoints[4 + channels[d]] = bestValues[4 + channels[d]];
        }

        if (settings.PBitKind != PBitKind.None)
        {
            pBits[0] = bestPBits[0];
            pBits[1] = bestPBits[1];
        }

        for (var k = 0; k < count; k++)
        {
            indices[texelIds[k]] = bestIndices[k];
        }

        return bestError;
    }

    /// <summary>
    /// Rounds real endpoints to the precision of the mode with the P-bits to try (those whose rounded endpoints are the
    /// nearest to the real ones, or every combination), evaluates them and keeps the best.
    /// </summary>
    private static void QuantizeAndEvaluate(in FitState state, bool allPBitCombinations, ReadOnlySpan<double> endpoint0, ReadOnlySpan<double> endpoint1, Span<int> bestValues, Span<int> bestPBits, Span<byte> bestIndices, ref long bestError)
    {
        Span<int> values = stackalloc int[8];
        Span<int> pBits = stackalloc int[2];
        Span<byte> indices = stackalloc byte[16];
        var kind = state.Settings.PBitKind;

        // Each endpoint rounded with each P-bit: [endpoint × 2 + P-bit][channel]
        Span<int> rounded = stackalloc int[16];
        Span<double> roundingErrors = stackalloc double[4];
        var pBitValues = kind == PBitKind.None ? 1 : 2;
        for (var pBit = 0; pBit < pBitValues; pBit++)
        {
            roundingErrors[pBit] = QuantizeEndpoint(in state, endpoint0, pBit, rounded.Slice(pBit * 4, 4));
            roundingErrors[2 + pBit] = QuantizeEndpoint(in state, endpoint1, pBit, rounded.Slice(8 + (pBit * 4), 4));
        }

        var combinations = kind switch
        {
            PBitKind.None => 1,
            PBitKind.Shared => 2,
            _ => 4,
        };

        if (!allPBitCombinations && kind != PBitKind.None)
        {
            // The P-bits whose rounded endpoints are the nearest to the real ones
            if (kind == PBitKind.Unique)
            {
                pBits[0] = roundingErrors[0] <= roundingErrors[1] ? 0 : 1;
                pBits[1] = roundingErrors[2] <= roundingErrors[3] ? 0 : 1;
            }
            else
            {
                pBits[0] = pBits[1] = roundingErrors[0] + roundingErrors[2] <= roundingErrors[1] + roundingErrors[3] ? 0 : 1;
            }

            combinations = 1;
        }

        for (var combination = 0; combination < combinations; combination++)
        {
            if (allPBitCombinations || kind == PBitKind.None)
            {
                pBits[0] = combination & 1;
                pBits[1] = kind == PBitKind.Unique ? combination >> 1 : combination & 1;
            }

            rounded.Slice(pBits[0] * 4, 4).CopyTo(values);
            rounded.Slice(8 + (pBits[1] * 4), 4).CopyTo(values[4..]);
            var error = Evaluate(in state, values, pBits, indices, bestError);
            if (error < bestError)
            {
                bestError = error;
                values.CopyTo(bestValues);
                pBits.CopyTo(bestPBits);
                indices.CopyTo(bestIndices);
            }
        }
    }

    /// <summary>Rounds a real endpoint to the stored values of the mode, given its P-bit.</summary>
    /// <returns>The squared rounding error, plus <see cref="ConstraintPenalty"/> when a constant alpha is not kept.</returns>
    private static double QuantizeEndpoint(in FitState state, ReadOnlySpan<double> endpoint, int pBit, Span<int> values)
    {
        var hasPBit = state.Settings.PBitKind != PBitKind.None;
        var error = 0.0;
        foreach (var c in state.Channels)
        {
            var bits = c == 3 ? state.Settings.AlphaBits : state.Settings.ColorBits;
            var target = Math.Clamp(endpoint[c], 0, 255);
            var maximum = (1 << bits) - 1;

            // The expansion replicates the top bits, so it is within one step of the linear scale: the value of the linear
            // estimate or a neighbor is the nearest
            var estimate = hasPBit ? (int)((((target * ((2 << bits) - 1) / 255) - pBit) / 2) + 0.5) : (int)((target * maximum / 255) + 0.5);
            var bestValue = 0;
            var bestDistance = double.MaxValue;
            for (var value = Math.Max(estimate - 1, 0); value <= Math.Min(estimate + 1, maximum); value++)
            {
                var distance = Math.Abs(Expand(value, pBit, bits, hasPBit) - target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestValue = value;
                }
            }

            values[c] = bestValue;
            error += bestDistance * bestDistance;
            if (c == state.Settings.ConstrainedChannel && Expand(bestValue, pBit, bits, hasPBit) != state.Settings.ConstrainedValue)
            {
                error += ConstraintPenalty;
            }
        }

        return error;
    }

    /// <summary>
    /// Computes the indices and the squared error of quantized endpoints: each texel takes the entry of the palette (Equation
    /// 2) nearest to it. Stops early once the error reaches <paramref name="limit"/>.
    /// </summary>
    private static long Evaluate(in FitState state, ReadOnlySpan<int> values, ReadOnlySpan<int> pBits, Span<byte> indices, long limit)
    {
        var settings = state.Settings;
        var hasPBit = settings.PBitKind != PBitKind.None;
        Span<int> endpoint0 = stackalloc int[4];
        Span<int> endpoint1 = stackalloc int[4];
        foreach (var c in state.Channels)
        {
            var bits = c == 3 ? settings.AlphaBits : settings.ColorBits;
            endpoint0[c] = Expand(values[c], pBits[0], bits, hasPBit);
            endpoint1[c] = Expand(values[4 + c], pBits[1], bits, hasPBit);
        }

        if (settings.ConstrainedChannel >= 0 && (endpoint0[settings.ConstrainedChannel] != settings.ConstrainedValue || endpoint1[settings.ConstrainedChannel] != settings.ConstrainedValue))
            return Infeasible;

        // The palette; the channels out of the fit have endpoints 0, so their entries are 0, as in the points
        var weights = BptcTables.GetWeights(settings.IndexBits);
        var levels = weights.Length;
        Span<int> palette = stackalloc int[64];
        for (var level = 0; level < levels; level++)
        {
            int weight = weights[level];
            palette[level * 4] = Interpolate(endpoint0[0], endpoint1[0], weight);
            palette[(level * 4) + 1] = Interpolate(endpoint0[1], endpoint1[1], weight);
            palette[(level * 4) + 2] = Interpolate(endpoint0[2], endpoint1[2], weight);
            palette[(level * 4) + 3] = Interpolate(endpoint0[3], endpoint1[3], weight);
        }

        int d0 = endpoint1[0] - endpoint0[0], d1 = endpoint1[1] - endpoint0[1], d2 = endpoint1[2] - endpoint0[2], d3 = endpoint1[3] - endpoint0[3];
        var length = (d0 * d0) + (d1 * d1) + (d2 * d2) + (d3 * d3);
        var nearestLevels = NearestLevels.AsSpan((settings.IndexBits - 2) * 65, 65);
        var points = state.Points;
        var count = points.Length / 4;
        long error = 0;
        for (var k = 0; k < count; k++)
        {
            var point = points.Slice(k * 4, 4);

            // The projection on the segment gives the nearest weight; the rounding of the entries can make a neighbor of
            // its level nearer, so the levels around it are compared exactly
            var center = 0;
            if (length > 0)
            {
                var t = ((point[0] - endpoint0[0]) * d0) + ((point[1] - endpoint0[1]) * d1) + ((point[2] - endpoint0[2]) * d2) + ((point[3] - endpoint0[3]) * d3);
                var weight = t <= 0 ? 0 : t >= length ? 64 : (int)((((long)t * 64) + (length / 2)) / length);
                center = nearestLevels[weight];
            }

            var bestLevel = 0;
            var bestDistance = int.MaxValue;
            for (var level = Math.Max(center - 1, 0); level <= Math.Min(center + 1, levels - 1); level++)
            {
                var entry = palette.Slice(level * 4, 4);
                int e0 = entry[0] - point[0], e1 = entry[1] - point[1], e2 = entry[2] - point[2], e3 = entry[3] - point[3];
                var distance = (e0 * e0) + (e1 * e1) + (e2 * e2) + (e3 * e3);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestLevel = level;
                }
            }

            indices[k] = (byte)bestLevel;
            error += bestDistance;
            if (error >= limit)
                return Infeasible;
        }

        return error;
    }

    /// <summary>Solves the real endpoints that minimize the squared error for the indices of the texels.</summary>
    /// <returns><see langword="false"/> when the indices do not determine the endpoints (every texel has the same weight).</returns>
    private static bool LeastSquares(in FitState state, ReadOnlySpan<byte> indices, Span<double> endpoint0, Span<double> endpoint1)
    {
        var weights = BptcTables.GetWeights(state.Settings.IndexBits);
        double a = 0, b = 0, c = 0;
        Span<double> sum0 = stackalloc double[4];
        Span<double> sum1 = stackalloc double[4];
        var points = state.Points;
        var count = points.Length / 4;
        for (var k = 0; k < count; k++)
        {
            var weight = weights[indices[k]] / 64.0;
            a += (1 - weight) * (1 - weight);
            b += (1 - weight) * weight;
            c += weight * weight;
            foreach (var channel in state.Channels)
            {
                sum0[channel] += (1 - weight) * points[(k * 4) + channel];
                sum1[channel] += weight * points[(k * 4) + channel];
            }
        }

        var determinant = (a * c) - (b * b);
        if (determinant < 1e-6)
            return false;

        foreach (var channel in state.Channels)
        {
            endpoint0[channel] = ((c * sum0[channel]) - (b * sum1[channel])) / determinant;
            endpoint1[channel] = ((a * sum1[channel]) - (b * sum0[channel])) / determinant;
        }

        return true;
    }

    /// <summary>
    /// Moves each stored value of the endpoints by one step, and flips each P-bit, while the error decreases: the rounding
    /// of the least-squares endpoints is not the best rounding, and the rounding of Equation 2 matters.
    /// </summary>
    private static void Polish(in FitState state, Span<int> bestValues, Span<int> bestPBits, Span<byte> bestIndices, ref long bestError)
    {
        Span<int> values = stackalloc int[8];
        Span<int> pBits = stackalloc int[2];
        Span<byte> indices = stackalloc byte[16];
        var kind = state.Settings.PBitKind;
        for (var pass = 0; pass < MaxPolishPasses && bestError > 0; pass++)
        {
            var improved = false;
            for (var endpoint = 0; endpoint < 2; endpoint++)
            {
                foreach (var c in state.Channels)
                {
                    var maximum = (1 << (c == 3 ? state.Settings.AlphaBits : state.Settings.ColorBits)) - 1;
                    for (var delta = -1; delta <= 1; delta += 2)
                    {
                        var value = bestValues[(endpoint * 4) + c] + delta;
                        if (value < 0 || value > maximum)
                            continue;

                        bestValues.CopyTo(values);
                        bestPBits.CopyTo(pBits);
                        values[(endpoint * 4) + c] = value;
                        improved |= TryImprove(in state, values, pBits, indices, bestValues, bestPBits, bestIndices, ref bestError);
                    }
                }

                if (kind == PBitKind.Unique || (kind == PBitKind.Shared && endpoint == 0))
                {
                    bestValues.CopyTo(values);
                    bestPBits.CopyTo(pBits);
                    pBits[endpoint] ^= 1;
                    if (kind == PBitKind.Shared)
                    {
                        pBits[1] = pBits[0];
                    }

                    improved |= TryImprove(in state, values, pBits, indices, bestValues, bestPBits, bestIndices, ref bestError);
                }
            }

            if (!improved)
                return;
        }
    }

    private static bool TryImprove(in FitState state, Span<int> values, Span<int> pBits, Span<byte> indices, Span<int> bestValues, Span<int> bestPBits, Span<byte> bestIndices, ref long bestError)
    {
        var error = Evaluate(in state, values, pBits, indices, bestError);
        if (error >= bestError)
            return false;

        bestError = error;
        values.CopyTo(bestValues);
        pBits.CopyTo(bestPBits);
        indices.CopyTo(bestIndices);
        return true;
    }

    /// <summary>
    /// Estimates the error of every partition of 2 or 3 subsets: the residual of the line that best fits each subset (the
    /// sum of the squared distances of its texels to their principal axis), which ignores the rounding of the endpoints and
    /// indices but ranks the partitions well and costs no fit.
    /// </summary>
    private static void EstimatePartitions(ReadOnlySpan<byte> texels, int subsets, Span<double> scores)
    {
        // The moments of each texel: its 4 channels, then the 10 distinct products of its channels (the scatter matrix is
        // symmetric); the moments of a subset are their sums, those of the first subset the total minus the others
        Span<int> moments = stackalloc int[16 * MomentCount];
        Span<int> total = stackalloc int[MomentCount];
        for (var i = 0; i < 16; i++)
        {
            var texel = moments.Slice(i * MomentCount, MomentCount);
            var product = 4;
            for (var c = 0; c < 4; c++)
            {
                texel[c] = texels[(i * 4) + c];
                for (var d = c; d < 4; d++)
                {
                    texel[product++] = texels[(i * 4) + c] * texels[(i * 4) + d];
                }
            }

            for (var k = 0; k < MomentCount; k++)
            {
                total[k] += texel[k];
            }
        }

        Span<int> subsetMoments = stackalloc int[3 * MomentCount];
        for (var partition = 0; partition < 64; partition++)
        {
            var mask1 = (int)PartitionMasks[((subsets - 2) * 128) + partition];
            var mask2 = subsets == 3 ? (int)PartitionMasks[192 + partition] : 0;
            SumMoments(moments, mask1, subsetMoments.Slice(MomentCount, MomentCount));
            SumMoments(moments, mask2, subsetMoments.Slice(2 * MomentCount, MomentCount));
            for (var k = 0; k < MomentCount; k++)
            {
                subsetMoments[k] = total[k] - subsetMoments[MomentCount + k] - subsetMoments[(2 * MomentCount) + k];
            }

            var score = LineResidual(subsetMoments[..MomentCount], 16 - BitOperations.PopCount((uint)(mask1 | mask2)));
            score += LineResidual(subsetMoments.Slice(MomentCount, MomentCount), BitOperations.PopCount((uint)mask1));
            if (subsets == 3)
            {
                score += LineResidual(subsetMoments.Slice(2 * MomentCount, MomentCount), BitOperations.PopCount((uint)mask2));
            }

            scores[partition] = score;
        }
    }

    /// <summary>
    /// The residual of the line that best fits the texels of a subset: the total variance minus the variance along the
    /// principal axis, from the moments of the subset.
    /// </summary>
    private static double LineResidual(ReadOnlySpan<int> sums, int count)
    {
        if (count == 0)
            return 0;

        var inverse = 1.0 / count;
        double s0 = sums[0], s1 = sums[1], s2 = sums[2], s3 = sums[3];
        var matrix = new BcnEncoderMath.SymmetricMatrix4(
            sums[4] - (s0 * s0 * inverse),
            sums[5] - (s0 * s1 * inverse),
            sums[6] - (s0 * s2 * inverse),
            sums[7] - (s0 * s3 * inverse),
            sums[8] - (s1 * s1 * inverse),
            sums[9] - (s1 * s2 * inverse),
            sums[10] - (s1 * s3 * inverse),
            sums[11] - (s2 * s2 * inverse),
            sums[12] - (s2 * s3 * inverse),
            sums[13] - (s3 * s3 * inverse));
        var trace = matrix.M00 + matrix.M11 + matrix.M22 + matrix.M33;

        // A few iterations rank the partitions well enough: the estimate is only compared with other estimates
        return trace - BcnEncoderMath.PrincipalAxis(matrix, EstimateIterations, out _, out _, out _, out _);
    }

    /// <summary>Sums the moments of the texels of a mask.</summary>
    private static void SumMoments(ReadOnlySpan<int> moments, int mask, Span<int> sums)
    {
        int s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, s5 = 0, s6 = 0, s7 = 0, s8 = 0, s9 = 0, s10 = 0, s11 = 0, s12 = 0, s13 = 0;
        while (mask != 0)
        {
            var texel = moments.Slice(BitOperations.TrailingZeroCount(mask) * MomentCount, MomentCount);
            mask &= mask - 1;
            s0 += texel[0];
            s1 += texel[1];
            s2 += texel[2];
            s3 += texel[3];
            s4 += texel[4];
            s5 += texel[5];
            s6 += texel[6];
            s7 += texel[7];
            s8 += texel[8];
            s9 += texel[9];
            s10 += texel[10];
            s11 += texel[11];
            s12 += texel[12];
            s13 += texel[13];
        }

        sums[0] = s0;
        sums[1] = s1;
        sums[2] = s2;
        sums[3] = s3;
        sums[4] = s4;
        sums[5] = s5;
        sums[6] = s6;
        sums[7] = s7;
        sums[8] = s8;
        sums[9] = s9;
        sums[10] = s10;
        sums[11] = s11;
        sums[12] = s12;
        sums[13] = s13;
    }

    /// <summary>Selects the partitions of the smallest scores, the lowest partition first on a tie.</summary>
    /// <returns>The number of partitions selected.</returns>
    private static int SelectPartitions(ReadOnlySpan<double> scores, int maximumCount, Span<int> selected)
    {
        var count = 0;
        for (var partition = 0; partition < scores.Length; partition++)
        {
            var position = count;
            while (position > 0 && scores[selected[position - 1]] > scores[partition])
            {
                position--;
            }

            if (position >= maximumCount)
                continue;

            for (var i = Math.Min(count, maximumCount - 1); i > position; i--)
            {
                selected[i] = selected[i - 1];
            }

            selected[position] = partition;
            count = Math.Min(count + 1, maximumCount);
        }

        return count;
    }

    /// <summary>
    /// Writes a candidate. The index of the anchor texel of each subset (and of each index set of modes 4 and 5) has one bit
    /// less, so its high bit must be 0: when it is not, the endpoints of the subset are swapped and its indices inverted
    /// (the weights are symmetric, <c>w(n − i) = 64 − w(i)</c>, so the decoded texels do not change).
    /// </summary>
    private static void WriteBlock(ref Candidate candidate, Span<byte> block)
    {
        var mode = candidate.Mode;
        var parameters = BlockDecoder.Bc7Modes[mode];
        var subsets = parameters.Subsets;
        var partition = candidate.Partition;
        Span<int> endpoints = candidate.Endpoints;
        Span<int> pBits = candidate.PBits;
        Span<byte> colorIndices = candidate.ColorIndices;
        Span<byte> alphaIndices = candidate.AlphaIndices;
        var wideColorIndices = mode == 4 && candidate.IndexSelection == 1;
        if (mode is 4 or 5)
        {
            var colorIndexBits = wideColorIndices ? 3 : 2;
            var alphaIndexBits = mode == 5 || wideColorIndices ? 2 : 3;
            if (colorIndices[0] >= 1 << (colorIndexBits - 1))
            {
                for (var c = 0; c < 3; c++)
                {
                    (endpoints[c], endpoints[4 + c]) = (endpoints[4 + c], endpoints[c]);
                }

                InvertIndices(colorIndices, colorIndexBits, subsets: 1, partition, subset: 0);
            }

            if (alphaIndices[0] >= 1 << (alphaIndexBits - 1))
            {
                (endpoints[3], endpoints[7]) = (endpoints[7], endpoints[3]);
                InvertIndices(alphaIndices, alphaIndexBits, subsets: 1, partition, subset: 0);
            }
        }
        else
        {
            for (var subset = 0; subset < subsets; subset++)
            {
                var anchor = subset switch
                {
                    0 => 0,
                    1 when subsets == 2 => BptcTables.TwoSubsetAnchors[partition],
                    1 => BptcTables.ThreeSubsetSecondAnchors[partition],
                    _ => BptcTables.ThreeSubsetThirdAnchors[partition],
                };

                if (colorIndices[anchor] < 1 << (parameters.IndexBits - 1))
                    continue;

                for (var c = 0; c < 4; c++)
                {
                    (endpoints[(subset * 8) + c], endpoints[(subset * 8) + 4 + c]) = (endpoints[(subset * 8) + 4 + c], endpoints[(subset * 8) + c]);
                }

                (pBits[subset * 2], pBits[(subset * 2) + 1]) = (pBits[(subset * 2) + 1], pBits[subset * 2]);
                InvertIndices(colorIndices, parameters.IndexBits, subsets, partition, subset);
            }
        }

        var writer = default(BitWriter);
        writer.Write(1 << mode, mode + 1);
        writer.Write(partition, parameters.PartitionBits);
        writer.Write(candidate.Rotation, parameters.RotationBits);
        writer.Write(candidate.IndexSelection, parameters.IndexSelectionBits);
        var endpointCount = subsets * 2;
        for (var c = 0; c < 3; c++)
        {
            for (var e = 0; e < endpointCount; e++)
            {
                writer.Write(endpoints[(e * 4) + c], parameters.ColorBits);
            }
        }

        for (var e = 0; e < endpointCount && parameters.AlphaBits > 0; e++)
        {
            writer.Write(endpoints[(e * 4) + 3], parameters.AlphaBits);
        }

        if (parameters.EndpointPBits)
        {
            for (var e = 0; e < endpointCount; e++)
            {
                writer.Write(pBits[e], 1);
            }
        }
        else if (parameters.SharedPBits)
        {
            for (var subset = 0; subset < subsets; subset++)
            {
                writer.Write(pBits[subset * 2], 1);
            }
        }

        // Mode 4 with index selection 1 stores the 2-bit alpha indices first and the 3-bit color indices second
        var primary = wideColorIndices ? alphaIndices : colorIndices;
        var secondary = wideColorIndices ? colorIndices : alphaIndices;
        for (var i = 0; i < 16; i++)
        {
            writer.Write(primary[i], BptcTables.IsAnchor(subsets, partition, i) ? parameters.IndexBits - 1 : parameters.IndexBits);
        }

        if (parameters.SecondaryIndexBits > 0)
        {
            for (var i = 0; i < 16; i++)
            {
                writer.Write(secondary[i], i == 0 ? parameters.SecondaryIndexBits - 1 : parameters.SecondaryIndexBits);
            }
        }

        writer.WriteTo(block);
    }

    private static void InvertIndices(Span<byte> indices, int indexBits, int subsets, int partition, int subset)
    {
        var maximum = (1 << indexBits) - 1;
        for (var i = 0; i < 16; i++)
        {
            if (BptcTables.GetSubset(subsets, partition, i) == subset)
            {
                indices[i] = (byte)(maximum - indices[i]);
            }
        }
    }

    /// <summary>
    /// The 8-bit value of a stored endpoint value: the P-bit below it, then the top bits replicated below (section 20.1).
    /// </summary>
    private static int Expand(int value, int pBit, int bits, bool hasPBit)
    {
        if (hasPBit)
        {
            value = (value << 1) | pBit;
            bits++;
        }

        value <<= 8 - bits;
        return value | (value >> bits);
    }

    /// <summary>Equation 2 of section 20.1.</summary>
    private static int Interpolate(int e0, int e1, int weight) => (((64 - weight) * e0) + (weight * e1) + 32) >> 6;

    private static ushort[] BuildPartitionMasks()
    {
        var masks = new ushort[256];
        for (var partition = 0; partition < 64; partition++)
        {
            for (var i = 0; i < 16; i++)
            {
                if (BptcTables.GetSubset(2, partition, i) == 1)
                {
                    masks[partition] |= (ushort)(1 << i);
                }

                switch (BptcTables.GetSubset(3, partition, i))
                {
                    case 1:
                        masks[128 + partition] |= (ushort)(1 << i);
                        break;
                    case 2:
                        masks[192 + partition] |= (ushort)(1 << i);
                        break;
                }
            }
        }

        return masks;
    }

    private static byte[] BuildNearestLevels()
    {
        var table = new byte[3 * 65];
        for (var bits = 2; bits <= 4; bits++)
        {
            var weights = BptcTables.GetWeights(bits);
            for (var weight = 0; weight <= 64; weight++)
            {
                var nearest = 0;
                for (var level = 1; level < weights.Length; level++)
                {
                    if (Math.Abs(weights[level] - weight) < Math.Abs(weights[nearest] - weight))
                    {
                        nearest = level;
                    }
                }

                table[((bits - 2) * 65) + weight] = (byte)nearest;
            }
        }

        return table;
    }

    private static byte[] BuildSolidColorTable()
    {
        var table = new byte[512];
        Span<int> pairs = stackalloc int[256];
        pairs.Fill(-1);
        var weight = BptcTables.Weights2[1];
        for (var q0 = 0; q0 < 128; q0++)
        {
            for (var q1 = 0; q1 < 128; q1++)
            {
                var decoded = Interpolate(Expand(q0, 0, 7, hasPBit: false), Expand(q1, 0, 7, hasPBit: false), weight);
                if (pairs[decoded] < 0)
                {
                    pairs[decoded] = (q0 << 8) | q1;
                }
            }
        }

        for (var value = 0; value < 256; value++)
        {
            var pair = -1;
            for (var distance = 0; pair < 0; distance++)
            {
                if (value - distance >= 0 && pairs[value - distance] >= 0)
                {
                    pair = pairs[value - distance];
                }
                else if (value + distance < 256 && pairs[value + distance] >= 0)
                {
                    pair = pairs[value + distance];
                }
            }

            table[value * 2] = (byte)(pair >> 8);
            table[(value * 2) + 1] = (byte)pair;
        }

        return table;
    }

    private enum FitLevel
    {
        /// <summary>The P-bits nearest to the real endpoints, one least-squares iteration.</summary>
        Fast,

        /// <summary>Up to three least-squares iterations, every P-bit combination, then a local search of the stored values.</summary>
        Thorough,
    }

    private enum PBitKind
    {
        None,
        Unique,
        Shared,
    }

    /// <summary>The parameters of a subset fit.</summary>
    /// <param name="ChannelMask">The channels fitted (bit 0 red to bit 3 alpha).</param>
    /// <param name="ColorBits">The bits of a stored color value.</param>
    /// <param name="AlphaBits">The bits of a stored alpha value.</param>
    /// <param name="PBitKind">The P-bits of the mode.</param>
    /// <param name="IndexBits">The bits of an index.</param>
    /// <param name="ConstrainedChannel">The channel that holds a constant alpha, which must decode exactly, or -1.</param>
    /// <param name="ConstrainedValue">The constant alpha.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct FitSettings(int ChannelMask, int ColorBits, int AlphaBits, PBitKind PBitKind, int IndexBits, int ConstrainedChannel, int ConstrainedValue);

    /// <summary>The texels of a subset fit (4 channels each) and its settings.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly ref struct FitState(ReadOnlySpan<int> points, ReadOnlySpan<int> channels, FitSettings settings)
    {
        public ReadOnlySpan<int> Points { get; } = points;

        public ReadOnlySpan<int> Channels { get; } = channels;

        public FitSettings Settings { get; } = settings;
    }

    /// <summary>A mode, a partition, a rotation and an index selection, and the error of their fit.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Configuration(int Mode, int Partition, int Rotation, int IndexSelection, long Error = Infeasible);

    /// <summary>A configuration and its endpoints, P-bits, indices and squared error.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct Candidate
    {
        public int Mode;
        public int Partition;
        public int Rotation;
        public int IndexSelection;
        public long Error;

        /// <summary>The stored values: endpoint <c>e</c> (subset × 2 + 0 or 1), channel <c>c</c> at <c>e × 4 + c</c>.</summary>
        public EndpointArray Endpoints;

        /// <summary>The P-bit of each endpoint (both endpoints of a subset hold its shared P-bit).</summary>
        public PBitArray PBits;

        /// <summary>The indices of the colors (of every channel, except in modes 4 and 5).</summary>
        public IndexArray ColorIndices;

        /// <summary>The indices of alpha in modes 4 and 5.</summary>
        public IndexArray AlphaIndices;
    }

    [InlineArray(24)]
    private struct EndpointArray
    {
        private int _element;
    }

    [InlineArray(6)]
    private struct PBitArray
    {
        private int _element;
    }

    [InlineArray(16)]
    private struct IndexArray
    {
        private byte _element;
    }

    /// <summary>Writes the bits of a 128-bit block from the least significant one.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct BitWriter
    {
        private ulong _low;
        private ulong _high;
        private int _position;

        public void Write(int value, int count)
        {
            if (count == 0)
                return;

            var bits = (ulong)value & ((1UL << count) - 1);
            if (_position >= 64)
            {
                _high |= bits << (_position - 64);
            }
            else
            {
                _low |= bits << _position;
                if (_position + count > 64)
                {
                    _high |= bits >> (64 - _position);
                }
            }

            _position += count;
        }

        public readonly void WriteTo(Span<byte> block)
        {
            Debug.Assert(_position == 128, "A BC7 block has 128 bits.");
            BinaryPrimitives.WriteUInt64LittleEndian(block, _low);
            BinaryPrimitives.WriteUInt64LittleEndian(block[8..], _high);
        }
    }
}
