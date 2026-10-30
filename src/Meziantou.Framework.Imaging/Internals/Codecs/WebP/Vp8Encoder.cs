using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The VP8 key-frame encoder of WebP lossy output, written from RFC 6386 (the inverse of <see cref="Vp8Decoder"/>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Color: RGB is converted to BT.601 limited-range Y'CbCr (the inverse of the decoder matrix), chroma averaged over each 2x2
/// block (weighted by alpha when the block is partly transparent, so hidden colors do not bleed into visible pixels); the
/// luma of fully transparent pixels is replaced by the mean luma of the visible pixels of their 4x4 block, and each run of
/// fully transparent 8x8 blocks takes one value (lossy output does not preserve hidden colors: hidden areas then cost almost
/// no bits, see <see cref="FlattenTransparentRuns"/>). The planes are extended to whole macroblocks by repeating the last column
/// and row.
/// </description></item>
/// <item><description>
/// Quantization: one quantizer index for the frame, <c>round((100 - quality) * 127 / 100)</c>, with the standard VP8 steps
/// (luma, Y2 and chroma tables of RFC 6386 section 14.1); coefficients are quantized with a rounding offset (a dead zone for
/// AC coefficients).
/// </description></item>
/// <item><description>
/// Mode decision by rate-distortion cost (sum of squared errors plus lambda times the estimated bits of the tokens and modes,
/// from the token probabilities): the four 16x16 luma modes, then (effort 2 and above) the ten 4x4 modes of each sub-block
/// (B_PRED), and the four chroma modes. Reconstruction mirrors the decoder exactly (same prediction edges, inverse transforms
/// and 16-bit coefficients), so the encoder's reference pixels are the decoded pixels.
/// </description></item>
/// <item><description>
/// Loop filter: the normal filter; its level is derived from the quantizer, or (effort 4 and above) chosen among candidates by
/// the distortion of the filtered reconstruction. Token probabilities are updated in the frame header when the counted
/// statistics save bits. One token partition; the skip flag is used for macroblocks without coefficients.
/// </description></item>
/// <item><description>
/// First partition (19-bit size, 512 KiB): when the modes chosen freely do not fit, the frame is analyzed once more under a
/// budget for the mode bits, which always fits (see <see cref="Encode(AllocationScope, ReadOnlySpan{byte}, int, int, int, int, int, CancellationToken)"/>).
/// Only very large detailed frames reach it (photographs of about 40 megapixels and more at high quality).
/// </description></item>
/// </list>
/// Memory (<see cref="AllocationKind.Temporary"/>): the source and reconstructed planes, the quantized levels of every
/// macroblock (800 bytes per macroblock) and the two partitions. Encoding is deterministic.
/// </remarks>
internal sealed class Vp8Encoder : IDisposable
{
    private const int MaxLevel = 2048;

    /// <summary>The largest first partition: its size is a 19-bit field of the frame tag.</summary>
    internal const int MaxFirstPartitionSize = (1 << 19) - 1;

    /// <summary>
    /// The bits reserved for the frame header in the first partition: its fixed fields, and every token probability updated
    /// (an update flag of at most 8 bits and an 8-bit value each).
    /// </summary>
    private const int FrameHeaderReserveBits = 256 + (4 * 8 * 3 * Vp8Tables.TokenNodes * 16);

    /// <summary>The share of the remaining first-partition bits given to the modes, for the overhead of the boolean coder.</summary>
    private const double ModeBitMargin = 0.98;

    /// <summary>An upper bound of the bits per macroblock of the skip flag, whose probability is the frequency of skipped macroblocks.</summary>
    private const double SkipFlagBits = 1.05;

    /// <summary>The largest weight of the mode bits in the cost of a mode under a mode-bit budget.</summary>
    private const double MaxModeBitWeight = 1 << 16;

    private static ReadOnlySpan<sbyte> KeyFrameYModeTree => [-Vp8Tables.BPred, 2, 4, 6, -Vp8Tables.DcPred, -Vp8Tables.VPred, -Vp8Tables.HPred, -Vp8Tables.TmPred];

    private static ReadOnlySpan<sbyte> UVModeTree => [-Vp8Tables.DcPred, 2, -Vp8Tables.VPred, 4, -Vp8Tables.HPred, -Vp8Tables.TmPred];

    private static ReadOnlySpan<sbyte> BModeTree =>
    [
        -Vp8Tables.BDcPred, 2,
        -Vp8Tables.BTmPred, 4,
        -Vp8Tables.BVePred, 6,
        8, 12,
        -Vp8Tables.BHePred, 10,
        -Vp8Tables.BRdPred, -Vp8Tables.BVrPred,
        -Vp8Tables.BLdPred, 14,
        -Vp8Tables.BVlPred, 16,
        -Vp8Tables.BHdPred, -Vp8Tables.BHuPred,
    ];

    private static readonly double[] BitCosts = CreateBitCosts();
    private static readonly double[] YModeCosts = CreateModeCosts(KeyFrameYModeTree, Vp8Tables.KeyFrameYModeProbabilities, 5, contexts: 1);
    private static readonly double[] UVModeCosts = CreateModeCosts(UVModeTree, Vp8Tables.KeyFrameUVModeProbabilities, 4, contexts: 1);
    private static readonly double[] BModeCosts = CreateModeCosts(BModeTree, Vp8Tables.KeyFrameBModeProbabilities, 10, contexts: 100);

    /// <summary>The bits of the cheapest 16x16 luma mode: modes cost the same in every context.</summary>
    private static readonly double MinLumaModeBits = Math.Min(Math.Min(YModeCosts[Vp8Tables.DcPred], YModeCosts[Vp8Tables.VPred]), Math.Min(YModeCosts[Vp8Tables.HPred], YModeCosts[Vp8Tables.TmPred]));

    /// <summary>The bits of the cheapest chroma mode.</summary>
    private static readonly double MinChromaModeBits = Math.Min(Math.Min(UVModeCosts[Vp8Tables.DcPred], UVModeCosts[Vp8Tables.VPred]), Math.Min(UVModeCosts[Vp8Tables.HPred], UVModeCosts[Vp8Tables.TmPred]));

    private readonly AllocationScope _scope;
    private readonly CancellationToken _cancellationToken;
    private readonly int _width;
    private readonly int _height;
    private readonly int _mbWidth;
    private readonly int _mbHeight;
    private readonly int _effort;
    private readonly int _maxFirstPartitionSize;
    private readonly int _quantizerIndex;
    private readonly Quantizer _q;
    private readonly double _lambda;
    private readonly byte[] _probabilities = new byte[4 * 8 * 3 * Vp8Tables.TokenNodes];
    private readonly List<PooledBuffer> _buffers = [];
    private readonly Vp8Planes _source;
    private readonly Vp8Planes _reconstruction;
    private readonly Macroblock[] _macroblocks;
    private readonly PooledBuffer _levelBuffer = null!;
    private int _filterLevel;
    private bool _skipFlag = true;

    /// <summary>The bits the modes of the frame may use (infinite: no budget).</summary>
    private double _modeBitBudget = double.PositiveInfinity;
    private double _modeBitsUsed;

    /// <summary>The weight of the mode bits in the cost of a mode: 1, or more while the modes use their budget too fast.</summary>
    private double _modeBitWeight = 1;

    /// <summary>Gets the quantized levels: 25 blocks of 16 raster-order values per macroblock (luma 0-15, U 16-19, V 20-23, Y2 24).</summary>
    private Span<short> Levels => unsafe(MemoryMarshal.Cast<byte, short>(_levelBuffer.RawBuffer.AsSpan()))[..(_macroblocks.Length * 25 * 16)];

    private Vp8Encoder(AllocationScope scope, int width, int height, int quality, int effort, int maxFirstPartitionSize, CancellationToken cancellationToken)
    {
        _scope = scope;
        _width = width;
        _height = height;
        _mbWidth = (width + 15) >> 4;
        _mbHeight = (height + 15) >> 4;
        _effort = effort;
        _maxFirstPartitionSize = maxFirstPartitionSize;
        _cancellationToken = cancellationToken;
        _quantizerIndex = (int)Math.Round((100 - quality) * 127 / 100.0, MidpointRounding.AwayFromZero);
        _q = Quantizer.Create(_quantizerIndex);
        _lambda = 0.12 * _q.YAc * _q.YAc;
        Vp8Tables.DefaultTokenProbabilities.CopyTo(_probabilities);
        _source = Vp8Planes.Create(scope, width, height);
        try
        {
            _reconstruction = Vp8Planes.Create(scope, width, height);
            var macroblockCount = _mbWidth * _mbHeight;
            _macroblocks = new Macroblock[macroblockCount];
            var levelBytes = (long)macroblockCount * 25 * 16 * sizeof(short);
            if (levelBytes > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(scope.Limits);

            _levelBuffer = scope.Rent((int)levelBytes, AllocationKind.Temporary, clear: true);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Encodes 8-bit RGBA pixels as a VP8 key frame.</summary>
    /// <param name="scope">The scope charged for the working memory.</param>
    /// <param name="rgba">The pixels (<c>width * height * 4</c> bytes).</param>
    /// <param name="width">The width (1 to 16,383).</param>
    /// <param name="height">The height (1 to 16,383).</param>
    /// <param name="quality">The quality, 0 to 100.</param>
    /// <param name="effort">The effort, 0 to 9.</param>
    /// <param name="cancellationToken">Checked between macroblock rows.</param>
    /// <returns>The VP8 chunk payload; the caller disposes it.</returns>
    public static PooledPayloadWriter Encode(AllocationScope scope, ReadOnlySpan<byte> rgba, int width, int height, int quality, int effort, CancellationToken cancellationToken)
        => Encode(scope, rgba, width, height, quality, effort, MaxFirstPartitionSize, cancellationToken);

    /// <summary>Encodes 8-bit RGBA pixels as a VP8 key frame whose first partition is at most <paramref name="maxFirstPartitionSize"/> bytes.</summary>
    /// <remarks>
    /// The modes are chosen without constraint first. When they do not fit the first partition, the frame is analyzed once
    /// more with a budget for the mode bits (<see cref="TryGetModeBitBudget"/>): the bits of every mode are known exactly
    /// (key frames code modes with fixed probabilities), the weight of the mode bits in the cost of a mode rises while the
    /// macroblocks use the budget faster than their share, and a macroblock never uses bits that would leave the next ones
    /// unable to afford the cheapest modes. 4x4 prediction stays available where it pays most, rather than being disabled
    /// for the whole frame.
    /// </remarks>
    internal static PooledPayloadWriter Encode(AllocationScope scope, ReadOnlySpan<byte> rgba, int width, int height, int quality, int effort, int maxFirstPartitionSize, CancellationToken cancellationToken)
    {
        using var encoder = new Vp8Encoder(scope, width, height, quality, effort, maxFirstPartitionSize, cancellationToken);
        encoder.LoadSource(rgba);
        encoder.EncodeFrame();
        if (encoder.WriteFrame())
            return encoder.TakeOutput();

        if (!TryGetModeBitBudget(encoder._macroblocks.Length, maxFirstPartitionSize, out encoder._modeBitBudget, out encoder._skipFlag))
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The {width}x{height} image needs a VP8 first partition larger than {maxFirstPartitionSize} bytes even with the cheapest modes; encode it losslessly or reduce its size."), ImageFormat.WebP, "VP8 first partition size");

        encoder.EncodeFrame();
        if (encoder.WriteFrame())
            return encoder.TakeOutput();

        throw new InvalidOperationException("The VP8 modes chosen under a mode-bit budget do not fit the first partition.");
    }

    /// <summary>
    /// Computes the bits the modes of a frame may use in a first partition of <paramref name="maxFirstPartitionSize"/> bytes,
    /// after the frame header and the skip flags; the skip flag is left out when the cheapest modes would not fit with it.
    /// </summary>
    /// <returns><see langword="false"/> when the cheapest modes of every macroblock do not fit.</returns>
    internal static bool TryGetModeBitBudget(int macroblockCount, int maxFirstPartitionSize, out double budget, out bool skipFlag)
    {
        var available = ((maxFirstPartitionSize * 8.0) - FrameHeaderReserveBits) * ModeBitMargin;
        var minimum = macroblockCount * (MinLumaModeBits + MinChromaModeBits);
        skipFlag = available - (macroblockCount * SkipFlagBits) >= minimum;
        budget = skipFlag ? available - (macroblockCount * SkipFlagBits) : available;
        return budget >= minimum;
    }

    public void Dispose()
    {
        foreach (var buffer in _buffers)
        {
            buffer.Dispose();
        }

        _buffers.Clear();
        _levelBuffer?.Dispose();
        _output?.Dispose();
        _output = null;
        _source?.Dispose();
        _reconstruction?.Dispose();
    }

    /// <summary>Converts RGBA to the source planes, extended to whole macroblocks by repeating the last column and row.</summary>
    private void LoadSource(ReadOnlySpan<byte> rgba)
    {
        var y = _source.Y;
        var u = _source.U;
        var v = _source.V;
        var yStride = _source.YStride;
        var uvStride = _source.UVStride;
        var paddedWidth = _mbWidth * 16;
        var paddedHeight = _mbHeight * 16;
        for (var row = 0; row < _height; row++)
        {
            var pixels = rgba.Slice(row * _width * 4, _width * 4);
            var luma = y.Slice(row * yStride, yStride);
            for (var x = 0; x < _width; x++)
            {
                int r = pixels[x * 4], g = pixels[(x * 4) + 1], b = pixels[(x * 4) + 2];
                luma[x] = (byte)(((16829 * r) + (33039 * g) + (6416 * b) + (16 << 16) + 32768) >> 16);
            }
        }

        FlattenTransparentLuma(rgba, y, yStride);
        var chromaWidth = (_width + 1) >> 1;
        var chromaHeight = (_height + 1) >> 1;
        for (var cy = 0; cy < chromaHeight; cy++)
        {
            for (var cx = 0; cx < chromaWidth; cx++)
            {
                // Alpha-weighted average of the 2x2 block (plain average when every pixel is transparent)
                long sumU = 0, sumV = 0, weight = 0, plainU = 0, plainV = 0, count = 0;
                for (var dy = 0; dy < 2; dy++)
                {
                    var py = (2 * cy) + dy;
                    if (py >= _height)
                        continue;

                    for (var dx = 0; dx < 2; dx++)
                    {
                        var px = (2 * cx) + dx;
                        if (px >= _width)
                            continue;

                        var pixel = rgba.Slice(((py * _width) + px) * 4, 4);
                        int r = pixel[0], g = pixel[1], b = pixel[2], a = pixel[3];
                        var pu = (-9714 * r) - (19070 * g) + (28784 * b);
                        var pv = (28784 * r) - (24103 * g) - (4681 * b);
                        sumU += (long)pu * a;
                        sumV += (long)pv * a;
                        weight += a;
                        plainU += pu;
                        plainV += pv;
                        count++;
                    }
                }

                double cu, cv;
                if (weight > 0)
                {
                    cu = (double)sumU / weight;
                    cv = (double)sumV / weight;
                }
                else
                {
                    cu = (double)plainU / count;
                    cv = (double)plainV / count;
                }

                u[(cy * uvStride) + cx] = ClampByte(128 + (int)Math.Floor((cu / 65536.0) + 0.5));
                v[(cy * uvStride) + cx] = ClampByte(128 + (int)Math.Floor((cv / 65536.0) + 0.5));
            }
        }

        FlattenTransparentRuns(rgba);
        for (var row = 0; row < _height; row++)
        {
            var luma = y.Slice(row * yStride, yStride);
            luma[_width..paddedWidth].Fill(luma[_width - 1]);
        }

        for (var row = _height; row < paddedHeight; row++)
        {
            y.Slice((_height - 1) * yStride, yStride).CopyTo(y.Slice(row * yStride, yStride));
        }

        for (var cy = 0; cy < chromaHeight; cy++)
        {
            u.Slice(cy * uvStride, uvStride)[chromaWidth..].Fill(u[(cy * uvStride) + chromaWidth - 1]);
            v.Slice(cy * uvStride, uvStride)[chromaWidth..].Fill(v[(cy * uvStride) + chromaWidth - 1]);
        }

        for (var cy = chromaHeight; cy < paddedHeight / 2; cy++)
        {
            u.Slice((chromaHeight - 1) * uvStride, uvStride).CopyTo(u.Slice(cy * uvStride, uvStride));
            v.Slice((chromaHeight - 1) * uvStride, uvStride).CopyTo(v.Slice(cy * uvStride, uvStride));
        }
    }

    /// <summary>
    /// Replaces the luma of fully transparent pixels (whose colors lossy output never preserves) by the mean luma of the
    /// visible pixels of their 4x4 block, or by the block mean when the whole block is transparent: hidden colors then cost
    /// no bits and do not disturb the transform of the visible pixels next to them.
    /// </summary>
    private void FlattenTransparentLuma(ReadOnlySpan<byte> rgba, Span<byte> luma, int stride)
    {
        for (var by = 0; by < _height; by += 4)
        {
            for (var bx = 0; bx < _width; bx += 4)
            {
                int visibleSum = 0, visibleCount = 0, sum = 0, count = 0, transparent = 0;
                for (var yy = by; yy < Math.Min(by + 4, _height); yy++)
                {
                    for (var xx = bx; xx < Math.Min(bx + 4, _width); xx++)
                    {
                        int value = luma[(yy * stride) + xx];
                        sum += value;
                        count++;
                        if (rgba[(((yy * _width) + xx) * 4) + 3] == 0)
                        {
                            transparent++;
                        }
                        else
                        {
                            visibleSum += value;
                            visibleCount++;
                        }
                    }
                }

                if (transparent == 0)
                    continue;

                var fill = (byte)(visibleCount > 0 ? (visibleSum + (visibleCount / 2)) / visibleCount : (sum + (count / 2)) / count);
                for (var yy = by; yy < Math.Min(by + 4, _height); yy++)
                {
                    for (var xx = bx; xx < Math.Min(bx + 4, _width); xx++)
                    {
                        if (visibleCount == 0 || rgba[(((yy * _width) + xx) * 4) + 3] == 0)
                        {
                            luma[(yy * stride) + xx] = fill;
                        }
                    }
                }
            }
        }
    }

    private void EncodeFrame()
    {
        AnalyzeFrame();
        ChooseFilterLevel();
        UpdateProbabilities();
    }

    /// <summary>
    /// Gives each run of fully transparent 8x8 blocks of a block row (4x4 in chroma) one value: the value of the transparent
    /// block above the first block of the run, or the value of that first block (as libwebp's <c>WebPCleanupTransparentArea</c>).
    /// A transparent area then has a single value that the blocks predict exactly, so hidden areas cost almost no bits.
    /// </summary>
    private void FlattenTransparentRuns(ReadOnlySpan<byte> rgba)
    {
        var y = _source.Y;
        var u = _source.U;
        var v = _source.V;
        var yStride = _source.YStride;
        var uvStride = _source.UVStride;
        var transparentAbove = new bool[(_width + 7) / 8];
        for (var by = 0; by < _height; by += 8)
        {
            var blockHeight = Math.Min(8, _height - by);
            var startRun = true;
            byte fillY = 0, fillU = 0, fillV = 0;
            for (var bx = 0; bx < _width; bx += 8)
            {
                var blockWidth = Math.Min(8, _width - bx);
                if (!IsTransparentBlock(rgba, bx, by, blockWidth, blockHeight))
                {
                    startRun = true;
                    transparentAbove[bx / 8] = false;
                    continue;
                }

                if (startRun)
                {
                    // The transparent block above already holds the value of its run
                    var source = transparentAbove[bx / 8] ? by - 8 : by;
                    fillY = y[(source * yStride) + bx];
                    fillU = u[(source / 2 * uvStride) + (bx / 2)];
                    fillV = v[(source / 2 * uvStride) + (bx / 2)];
                    startRun = false;
                }

                transparentAbove[bx / 8] = true;

                for (var row = by; row < by + blockHeight; row++)
                {
                    y.Slice((row * yStride) + bx, blockWidth).Fill(fillY);
                }

                var chromaWidth = (blockWidth + 1) / 2;
                for (var row = by / 2; row < (by + blockHeight + 1) / 2; row++)
                {
                    u.Slice((row * uvStride) + (bx / 2), chromaWidth).Fill(fillU);
                    v.Slice((row * uvStride) + (bx / 2), chromaWidth).Fill(fillV);
                }
            }
        }
    }

    private bool IsTransparentBlock(ReadOnlySpan<byte> rgba, int x, int y, int width, int height)
    {
        for (var row = y; row < y + height; row++)
        {
            var pixels = rgba.Slice(((row * _width) + x) * 4, width * 4);
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                    return false;
            }
        }

        return true;
    }

    /// <summary>Chooses the modes of every macroblock, quantizes its residuals and reconstructs it.</summary>
    private void AnalyzeFrame()
    {
        _modeBitsUsed = 0;
        _modeBitWeight = 1;
        var aboveModes = new byte[_mbWidth * 4];
        Span<byte> leftModes = stackalloc byte[4];
        var aboveNonZero = new byte[_mbWidth * 9];
        Span<byte> leftNonZero = stackalloc byte[9];
        _reconstruction.Y.Clear();
        _reconstruction.U.Clear();
        _reconstruction.V.Clear();
        for (var mbY = 0; mbY < _mbHeight; mbY++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            leftModes.Clear();
            leftNonZero.Clear();
            for (var mbX = 0; mbX < _mbWidth; mbX++)
            {
                AnalyzeMacroblock(mbX, mbY, aboveModes.AsSpan(mbX * 4, 4), leftModes, aboveNonZero.AsSpan(mbX * 9, 9), leftNonZero);
            }
        }
    }

    private void AnalyzeMacroblock(int mbX, int mbY, Span<byte> aboveModes, Span<byte> leftModes, Span<byte> aboveNonZero, Span<byte> leftNonZero)
    {
        var index = (mbY * _mbWidth) + mbX;
        var levels = Levels.Slice(index * 25 * 16, 25 * 16);
        levels.Clear();
        var info = new Macroblock();
        var recon = _reconstruction.Y;
        var stride = _reconstruction.YStride;
        var offset = (mbY * 16 * stride) + (mbX * 16);

        // The mode bits this macroblock may use while every later one can still afford the cheapest modes (infinite without
        // budget); the cheapest modes are always allowed
        var modeBitLimit = _modeBitBudget - _modeBitsUsed - ((_macroblocks.Length - index - 1) * (MinLumaModeBits + MinChromaModeBits));
        var modeLambda = _lambda * _modeBitWeight;

        // 16x16 luma modes
        Span<short> bestLevels = stackalloc short[17 * 16];
        Span<short> trialLevels = stackalloc short[17 * 16];
        Span<byte> bestPixels = stackalloc byte[256];
        var bestMode = Vp8Tables.DcPred;
        var bestCost = double.MaxValue;
        for (var mode = Vp8Tables.DcPred; mode <= Vp8Tables.TmPred; mode++)
        {
            if (YModeCosts[mode] > MinLumaModeBits && YModeCosts[mode] + MinChromaModeBits > modeBitLimit)
                continue;

            Vp8Reconstruction.PredictBlock(recon, stride, offset, 16, mbX, mbY, mode);
            var cost = EncodeLuma16(offset, trialLevels, aboveNonZero, leftNonZero) + (modeLambda * YModeCosts[mode]);
            if (cost < bestCost)
            {
                bestCost = cost;
                bestMode = mode;
                trialLevels.CopyTo(bestLevels);
                CopyBlock(recon, stride, offset, bestPixels, 16);
            }
        }

        var useSubBlocks = false;
        var lumaModeBits = YModeCosts[bestMode];
        Span<byte> subModes = stackalloc byte[16];
        if (_effort >= 2 && YModeCosts[Vp8Tables.BPred] + MinChromaModeBits <= modeBitLimit)
        {
            Span<short> subLevels = stackalloc short[16 * 16];
            var subCost = EncodeSubBlocks(mbX, mbY, offset, subModes, subLevels, aboveModes, leftModes, aboveNonZero, leftNonZero, out var subModeBits) + (modeLambda * YModeCosts[Vp8Tables.BPred]);
            subModeBits += YModeCosts[Vp8Tables.BPred];
            if (subCost < bestCost && subModeBits + MinChromaModeBits <= modeBitLimit)
            {
                lumaModeBits = subModeBits;
                useSubBlocks = true;
                subLevels.CopyTo(levels[..(16 * 16)]);
            }
        }

        if (useSubBlocks)
        {
            info.YMode = Vp8Tables.BPred;
            info.SetSubModes(subModes);
        }
        else
        {
            info.YMode = (byte)bestMode;
            RestoreBlock(recon, stride, offset, bestPixels, 16);
            bestLevels[..(16 * 16)].CopyTo(levels[..(16 * 16)]);
            bestLevels.Slice(16 * 16, 16).CopyTo(levels.Slice(24 * 16, 16));
            var implied = bestMode switch
            {
                Vp8Tables.VPred => Vp8Tables.BVePred,
                Vp8Tables.HPred => Vp8Tables.BHePred,
                Vp8Tables.TmPred => Vp8Tables.BTmPred,
                _ => Vp8Tables.BDcPred,
            };
            aboveModes.Fill((byte)implied);
            leftModes.Fill((byte)implied);
        }

        // Chroma
        var uvStride = _reconstruction.UVStride;
        var uvOffset = (mbY * 8 * uvStride) + (mbX * 8);
        Span<short> uvLevels = stackalloc short[8 * 16];
        Span<short> bestUv = stackalloc short[8 * 16];
        Span<byte> bestU = stackalloc byte[64];
        Span<byte> bestV = stackalloc byte[64];
        var bestUvMode = Vp8Tables.DcPred;
        bestCost = double.MaxValue;
        for (var mode = Vp8Tables.DcPred; mode <= Vp8Tables.TmPred; mode++)
        {
            if (UVModeCosts[mode] > MinChromaModeBits && lumaModeBits + UVModeCosts[mode] > modeBitLimit)
                continue;

            Vp8Reconstruction.PredictBlock(_reconstruction.U, uvStride, uvOffset, 8, mbX, mbY, mode);
            Vp8Reconstruction.PredictBlock(_reconstruction.V, uvStride, uvOffset, 8, mbX, mbY, mode);
            var cost = EncodeChroma(uvOffset, uvLevels, aboveNonZero, leftNonZero) + (modeLambda * UVModeCosts[mode]);
            if (cost < bestCost)
            {
                bestCost = cost;
                bestUvMode = mode;
                uvLevels.CopyTo(bestUv);
                CopyBlock(_reconstruction.U, uvStride, uvOffset, bestU, 8);
                CopyBlock(_reconstruction.V, uvStride, uvOffset, bestV, 8);
            }
        }

        info.UVMode = (byte)bestUvMode;
        RestoreBlock(_reconstruction.U, uvStride, uvOffset, bestU, 8);
        RestoreBlock(_reconstruction.V, uvStride, uvOffset, bestV, 8);
        bestUv.CopyTo(levels.Slice(16 * 16, 8 * 16));

        // Contexts for the rate estimates of the following macroblocks (exact: the same rules as the decoder)
        UpdateContexts(levels, info.YMode != Vp8Tables.BPred, aboveNonZero, leftNonZero, out var hasCoefficients);
        info.HasCoefficients = hasCoefficients;
        _macroblocks[index] = info;

        _modeBitsUsed += lumaModeBits + UVModeCosts[bestUvMode];
        if (!double.IsPositiveInfinity(_modeBitBudget))
        {
            // Pacing: the weight rises while the modes use more than the share of the budget of the macroblocks done so far
            var share = _modeBitBudget * (index + 1) / _macroblocks.Length;
            _modeBitWeight = _modeBitsUsed > share ? Math.Min(_modeBitWeight * 1.1, MaxModeBitWeight) : Math.Max(1, _modeBitWeight / 1.1);
        }
    }

    /// <summary>Quantizes and reconstructs a 16x16 luma prediction already written at <paramref name="offset"/>.</summary>
    /// <returns>The rate-distortion cost of the residuals.</returns>
    private double EncodeLuma16(int offset, Span<short> levels, ReadOnlySpan<byte> aboveNonZero, ReadOnlySpan<byte> leftNonZero)
    {
        var recon = _reconstruction.Y;
        var source = _source.Y;
        var stride = _reconstruction.YStride;
        Span<int> residual = stackalloc int[16];
        Span<int> coefficients = stackalloc int[16 * 16];
        Span<int> dc = stackalloc int[16];
        for (var block = 0; block < 16; block++)
        {
            var blockOffset = offset + ((block >> 2) * 4 * stride) + ((block & 3) * 4);
            GetResidual(source, recon, stride, blockOffset, residual);
            Vp8ForwardTransforms.ForwardDctColumns(residual, coefficients.Slice(block * 16, 16));
            dc[block] = coefficients[block * 16];
        }

        // Y2: the Walsh-Hadamard transform of the DC coefficients
        Span<int> y2 = stackalloc int[16];
        Vp8ForwardTransforms.ForwardWalshHadamard(dc, y2);
        var y2Levels = levels.Slice(16 * 16, 16);
        Quantize(y2, y2Levels, _q.Y2Dc, _q.Y2Ac, first: 0);
        Span<short> dequantizedY2 = stackalloc short[16];
        Dequantize(y2Levels, dequantizedY2, _q.Y2Dc, _q.Y2Ac);
        Span<short> blocks = stackalloc short[16 * 16];
        blocks.Clear();
        Vp8Transforms.InverseWalshHadamard(dequantizedY2, blocks);

        var bits = TokenBits(y2Levels, 0, 1, aboveNonZero[8] + leftNonZero[8]);
        Span<byte> above = stackalloc byte[4];
        Span<byte> left = stackalloc byte[4];
        aboveNonZero[..4].CopyTo(above);
        leftNonZero[..4].CopyTo(left);
        for (var block = 0; block < 16; block++)
        {
            var blockLevels = levels.Slice(block * 16, 16);
            Quantize(coefficients.Slice(block * 16, 16), blockLevels, _q.YDc, _q.YAc, first: 1);
            var context = above[block & 3] + left[block >> 2];
            bits += TokenBits(blockLevels, 1, 0, context);
            var nonZero = HasLevels(blockLevels, 1) ? (byte)1 : (byte)0;
            above[block & 3] = left[block >> 2] = nonZero;

            var dequantized = blocks.Slice(block * 16, 16);
            for (var i = 1; i < 16; i++)
            {
                dequantized[i] = (short)(blockLevels[i] * _q.YAc);
            }

            var blockOffset = offset + ((block >> 2) * 4 * stride) + ((block & 3) * 4);
            Vp8Transforms.InverseDctAdd(dequantized, recon[blockOffset..], stride);
        }

        return Distortion(source, recon, stride, offset, 16) + (_lambda * bits);
    }

    /// <summary>Chooses, quantizes and reconstructs the 16 sub-blocks of a B_PRED macroblock in raster order.</summary>
    /// <returns>The rate-distortion cost of the sub-blocks; <paramref name="modeBits"/> receives the bits of their modes.</returns>
    private double EncodeSubBlocks(int mbX, int mbY, int offset, Span<byte> subModes, Span<short> levels, Span<byte> aboveModes, Span<byte> leftModes, ReadOnlySpan<byte> aboveNonZero, ReadOnlySpan<byte> leftNonZero, out double modeBits)
    {
        modeBits = 0;
        var modeWeight = _modeBitWeight;
        var recon = _reconstruction.Y;
        var source = _source.Y;
        var stride = _reconstruction.YStride;
        Span<byte> aboveRight = stackalloc byte[4];
        Vp8Reconstruction.GetMacroblockAboveRight(recon, stride, mbX, mbY, _mbWidth, aboveRight);
        Span<byte> modesAbove = stackalloc byte[4];
        Span<byte> modesLeft = stackalloc byte[4];
        aboveModes.CopyTo(modesAbove);
        leftModes.CopyTo(modesLeft);
        Span<byte> nzAbove = stackalloc byte[4];
        Span<byte> nzLeft = stackalloc byte[4];
        aboveNonZero[..4].CopyTo(nzAbove);
        leftNonZero[..4].CopyTo(nzLeft);
        Span<int> residual = stackalloc int[16];
        Span<int> coefficients = stackalloc int[16];
        Span<short> trial = stackalloc short[16];
        Span<short> best = stackalloc short[16];
        Span<short> dequantized = stackalloc short[16];
        Span<byte> bestPixels = stackalloc byte[16];
        Span<byte> edge = stackalloc byte[Vp8Reconstruction.SubBlockEdgeLength];
        Span<byte> sourcePixels = stackalloc byte[16];
        Span<byte> pixels = stackalloc byte[16];
        var modeCount = _effort >= 4 ? 10 : 6;
        var total = 0.0;
        for (var block = 0; block < 16; block++)
        {
            var bx = block & 3;
            var by = block >> 2;
            var blockOffset = offset + (by * 4 * stride) + (bx * 4);
            var px = (mbX * 16) + (bx * 4);
            var py = (mbY * 16) + (by * 4);
            var context = nzAbove[bx] + nzLeft[by];
            var modeCosts = BModeCosts.AsSpan(((modesAbove[bx] * 10) + modesLeft[by]) * 10, 10);
            var bestCost = double.MaxValue;
            var bestMode = 0;

            // The edge and the source of the block do not change while its modes are tried: every mode is predicted,
            // reconstructed and measured in local 4x4 buffers, and only the best reconstruction is written to the plane
            Vp8Reconstruction.GetSubBlockEdge(recon, stride, blockOffset, px, py, bx == 3 ? aboveRight : default, edge);
            CopyBlock(source, stride, blockOffset, sourcePixels, 4);
            for (var mode = 0; mode < modeCount; mode++)
            {
                Vp8Reconstruction.PredictSubBlock(edge, mode, pixels);
                // Column-major residuals (ForwardDctColumns)
                for (var i = 0; i < 16; i++)
                {
                    residual[((i & 3) * 4) + (i >> 2)] = sourcePixels[i] - pixels[i];
                }

                Vp8ForwardTransforms.ForwardDctColumns(residual, coefficients);
                Quantize(coefficients, trial, _q.YDc, _q.YAc, first: 0);
                Dequantize(trial, dequantized, _q.YDc, _q.YAc);
                Vp8Transforms.InverseDctAdd(dequantized, pixels, 4);
                var distortion = Distortion(sourcePixels, pixels, 4, 0, 4);

                // The token bits are not negative and the operations are monotonic: when the cost without them is not below
                // the best cost, the full cost is not either, and the mode cannot be chosen
                if (distortion + (_lambda * (modeWeight * modeCosts[mode])) >= bestCost)
                    continue;

                var cost = distortion + (_lambda * (TokenBits(trial, 0, 3, context) + (modeWeight * modeCosts[mode])));
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestMode = mode;
                    trial.CopyTo(best);
                    pixels.CopyTo(bestPixels);
                }
            }

            RestoreBlock(recon, stride, blockOffset, bestPixels, 4);
            best.CopyTo(levels.Slice(block * 16, 16));
            subModes[block] = (byte)bestMode;
            modeBits += modeCosts[bestMode];
            modesAbove[bx] = modesLeft[by] = (byte)bestMode;
            nzAbove[bx] = nzLeft[by] = HasLevels(best, 0) ? (byte)1 : (byte)0;
            total += bestCost;
        }

        modesAbove.CopyTo(aboveModes);
        modesLeft.CopyTo(leftModes);
        return total;
    }

    private double EncodeChroma(int offset, Span<short> levels, ReadOnlySpan<byte> aboveNonZero, ReadOnlySpan<byte> leftNonZero)
    {
        var stride = _reconstruction.UVStride;
        Span<int> residual = stackalloc int[16];
        Span<int> coefficients = stackalloc int[16];
        Span<short> dequantized = stackalloc short[16];
        var bits = 0.0;
        var distortion = 0.0;
        Span<byte> above = stackalloc byte[2];
        Span<byte> left = stackalloc byte[2];
        for (var plane = 0; plane < 2; plane++)
        {
            var source = plane == 0 ? _source.U : _source.V;
            var recon = plane == 0 ? _reconstruction.U : _reconstruction.V;
            var context = 4 + (plane * 2);
            aboveNonZero.Slice(context, 2).CopyTo(above);
            leftNonZero.Slice(context, 2).CopyTo(left);
            for (var block = 0; block < 4; block++)
            {
                var blockOffset = offset + ((block >> 1) * 4 * stride) + ((block & 1) * 4);
                var blockLevels = levels.Slice(((plane * 4) + block) * 16, 16);
                GetResidual(source, recon, stride, blockOffset, residual);
                Vp8ForwardTransforms.ForwardDctColumns(residual, coefficients);
                Quantize(coefficients, blockLevels, _q.UVDc, _q.UVAc, first: 0);
                bits += TokenBits(blockLevels, 0, 2, above[block & 1] + left[block >> 1]);
                above[block & 1] = left[block >> 1] = HasLevels(blockLevels, 0) ? (byte)1 : (byte)0;
                Dequantize(blockLevels, dequantized, _q.UVDc, _q.UVAc);
                Vp8Transforms.InverseDctAdd(dequantized, recon[blockOffset..], stride);
            }

            distortion += Distortion(source, recon, stride, offset, 8);
        }

        return distortion + (_lambda * bits);
    }

    private static void GetResidual(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction, int stride, int offset, Span<int> residual)
    {
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var position = offset + (y * stride) + x;
                residual[(x * 4) + y] = source[position] - prediction[position];
            }
        }
    }

    /// <summary>Quantizes raster-order coefficients into raster-order levels (positions before <paramref name="first"/> are zero).</summary>
    /// <remarks>
    /// The divisions by the steps, which do not change in a block, are multiplications by their reciprocals followed by a
    /// correction (<see cref="Divide"/>): the levels are the ones of <see cref="QuantizeReference"/>.
    /// </remarks>
    internal static void Quantize(ReadOnlySpan<int> coefficients, Span<short> levels, int dcStep, int acStep, int first)
    {
        if (!Vector128.IsHardwareAccelerated || !TryQuantizeVector(coefficients, levels, dcStep, acStep, first))
        {
            QuantizeScalar(coefficients, levels, dcStep, acStep, first);
        }
    }

    /// <summary>
    /// <see cref="Quantize"/> 4 coefficients at a time: the quotients are single-precision divisions rounded down, exact while
    /// the numerators are below 2^24 (otherwise nothing is written and <see langword="false"/> is returned).
    /// </summary>
    /// <remarks>
    /// For integers <c>0 &lt;= n &lt; 2^24</c> and <c>d &gt; 0</c>, the correctly rounded quotient differs from <c>n / d</c> by at
    /// most <c>n / d * 2^-24 &lt; 1 / d</c>, which is less than the distance from a non-integer <c>n / d</c> to the next integer:
    /// its floor is the integer quotient. Integer quotients are exact.
    /// </remarks>
    private static bool TryQuantizeVector(ReadOnlySpan<int> coefficients, Span<short> levels, int dcStep, int acStep, int first)
    {
        coefficients = coefficients[..16];
        levels = levels[..16];

        // Lane 0 of the first vector is the DC (rounded to nearest), the other lanes AC coefficients (2/3 of a step dead zone)
        var multiplierFirst = Vector128.Create(1, 3, 3, 3);
        var offsetFirst = Vector128.Create(dcStep >> 1, acStep, acStep, acStep);
        var divisorFirst = Vector128.Create((float)dcStep, 3 * acStep, 3 * acStep, 3 * acStep);
        var multiplier = Vector128.Create(3);
        var offset = Vector128.Create(acStep);
        var divisor = Vector128.Create((float)(3 * acStep));
        var limit = Vector128.Create(1u << 24);
        var c0 = Vector128.Create(coefficients[..4]);
        var c1 = Vector128.Create(coefficients.Slice(4, 4));
        var c2 = Vector128.Create(coefficients.Slice(8, 4));
        var c3 = Vector128.Create(coefficients.Slice(12, 4));
        var n0 = (Vector128.Abs(c0) * multiplierFirst) + offsetFirst;
        var n1 = (Vector128.Abs(c1) * multiplier) + offset;
        var n2 = (Vector128.Abs(c2) * multiplier) + offset;
        var n3 = (Vector128.Abs(c3) * multiplier) + offset;

        // Numerators from 2^24 (or wrapped to negative values, compared as unsigned) take the scalar path
        var tooLarge = Vector128.GreaterThanOrEqual(n0.AsUInt32(), limit) | Vector128.GreaterThanOrEqual(n1.AsUInt32(), limit)
            | Vector128.GreaterThanOrEqual(n2.AsUInt32(), limit) | Vector128.GreaterThanOrEqual(n3.AsUInt32(), limit);
        if (tooLarge != Vector128<uint>.Zero)
            return false;

        var l0 = Level(c0, n0, divisorFirst);
        if (first > 0)
        {
            l0 = l0.WithElement(0, 0);
        }

        Vector128.Narrow(l0, Level(c1, n1, divisor)).CopyTo(levels[..8]);
        Vector128.Narrow(Level(c2, n2, divisor), Level(c3, n3, divisor)).CopyTo(levels.Slice(8, 8));
        return true;

        static Vector128<int> Level(Vector128<int> coefficient, Vector128<int> numerator, Vector128<float> divisor)
        {
            var quotient = Vector128.ConvertToInt32(Vector128.Floor(Vector128.ConvertToSingle(numerator) / divisor));
            var level = Vector128.Min(quotient, Vector128.Create(MaxLevel));
            return Vector128.ConditionalSelect(Vector128.LessThan(coefficient, Vector128<int>.Zero), -level, level);
        }
    }

    /// <summary>The scalar version of <see cref="Quantize"/>, for any coefficient.</summary>
    internal static void QuantizeScalar(ReadOnlySpan<int> coefficients, Span<short> levels, int dcStep, int acStep, int first)
    {
        coefficients = coefficients[..16];
        levels = levels[..16];
        if (first > 0)
        {
            levels[0] = 0;
        }
        else
        {
            // Rounding to nearest for DC
            var magnitude = Math.Abs(coefficients[0]);
            var level = Math.Min((magnitude + (dcStep >> 1)) / dcStep, MaxLevel);
            levels[0] = (short)(coefficients[0] < 0 ? -level : level);
        }

        // A dead zone (2/3 of a step) for AC coefficients
        var divisor = 3 * acStep;
        var reciprocal = uint.MaxValue / (uint)divisor;
        for (var i = 1; i < 16; i++)
        {
            if (i < first)
            {
                levels[i] = 0;
                continue;
            }

            var magnitude = Math.Abs(coefficients[i]);
            var level = Math.Min(Divide((3 * magnitude) + acStep, divisor, reciprocal), MaxLevel);
            levels[i] = (short)(coefficients[i] < 0 ? -level : level);
        }
    }

    /// <summary>The reference of <see cref="Quantize"/>: one division per coefficient.</summary>
    internal static void QuantizeReference(ReadOnlySpan<int> coefficients, Span<short> levels, int dcStep, int acStep, int first)
    {
        for (var i = 0; i < 16; i++)
        {
            if (i < first)
            {
                levels[i] = 0;
                continue;
            }

            var step = i == 0 ? dcStep : acStep;
            var magnitude = Math.Abs(coefficients[i]);

            // Rounding offset: to nearest for DC, a dead zone (2/3 of a step) for AC coefficients
            var level = i == 0 ? (magnitude + (step >> 1)) / step : ((3 * magnitude) + step) / (3 * step);
            level = Math.Min(level, MaxLevel);
            levels[i] = (short)(coefficients[i] < 0 ? -level : level);
        }
    }

    /// <summary>
    /// Divides a non-negative number by a positive divisor whose reciprocal <c>floor((2^32 - 1) / divisor)</c> is known.
    /// </summary>
    /// <remarks>
    /// The reciprocal is between <c>2^32 / divisor - 1</c> and <c>2^32 / divisor</c>, so the high half of the product is the
    /// quotient or one less (the error is below <c>value / 2^32</c>, which is less than 1); one comparison corrects it.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Divide(int value, int divisor, uint reciprocal)
    {
        var quotient = (int)(((ulong)(uint)value * reciprocal) >> 32);
        return (long)(quotient + 1) * divisor <= value ? quotient + 1 : quotient;
    }

    private static void Dequantize(ReadOnlySpan<short> levels, Span<short> dequantized, int dcStep, int acStep)
    {
        for (var i = 0; i < 16; i++)
        {
            dequantized[i] = (short)(levels[i] * (i == 0 ? dcStep : acStep));
        }
    }

    private static bool HasLevels(ReadOnlySpan<short> levels, int first)
    {
        for (var i = first; i < 16; i++)
        {
            if (levels[i] != 0)
                return true;
        }

        return false;
    }

    private static double Distortion(ReadOnlySpan<byte> source, ReadOnlySpan<byte> reconstruction, int stride, int offset, int size)
    {
        long sum = 0;
        for (var y = 0; y < size; y++)
        {
            var row = offset + (y * stride);
            for (var x = 0; x < size; x++)
            {
                var difference = source[row + x] - reconstruction[row + x];
                sum += difference * difference;
            }
        }

        return sum;
    }

    private static void CopyBlock(ReadOnlySpan<byte> plane, int stride, int offset, Span<byte> destination, int size)
    {
        for (var y = 0; y < size; y++)
        {
            plane.Slice(offset + (y * stride), size).CopyTo(destination.Slice(y * size, size));
        }
    }

    private static void RestoreBlock(Span<byte> plane, int stride, int offset, ReadOnlySpan<byte> source, int size)
    {
        for (var y = 0; y < size; y++)
        {
            source.Slice(y * size, size).CopyTo(plane.Slice(offset + (y * stride), size));
        }
    }

    /// <summary>Tabulates <see cref="ModeBits"/> for <paramref name="contexts"/> consecutive probability sets of a tree.</summary>
    private static double[] CreateModeCosts(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities, int modes, int contexts)
    {
        var costs = new double[modes * contexts];
        var size = probabilities.Length / contexts;
        for (var context = 0; context < contexts; context++)
        {
            for (var mode = 0; mode < modes; mode++)
            {
                costs[(context * modes) + mode] = ModeBits(tree, probabilities.Slice(context * size, size), mode);
            }
        }

        return costs;
    }

    /// <summary>Estimates the bits of a tree-coded mode with its probabilities.</summary>
    private static double ModeBits(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities, int mode)
    {
        var bits = 0.0;
        var node = 0;
        while (true)
        {
            // Find which branch leads to the mode
            var bit = Contains(tree, tree[node + 1], mode) ? 1 : 0;
            bits += BoolBits(probabilities[node >> 1], bit != 0);
            var next = tree[node + bit];
            if (next <= 0)
                return bits;

            node = next;
        }

        static bool Contains(ReadOnlySpan<sbyte> tree, int entry, int mode) => entry <= 0 ? -entry == mode : Contains(tree, tree[entry], mode) || Contains(tree, tree[entry + 1], mode);
    }

    /// <summary>The cost in bits of a boolean with its probability of being false.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double BoolBits(int probability, bool value) => BitCosts[value ? 256 - probability : probability];

    /// <summary>Estimates the bits of the tokens of one block (raster-order levels) with the current probabilities.</summary>
    /// <remarks>
    /// The walk of <see cref="WalkTokens"/> with the bit costs added, in the same order, to a local sum (a cost sink adds them
    /// through a reference); probabilities and costs are addressed by reference
    /// (the type, band and context offsets stay inside the probability table, the costs are indexed by p or 256 - p).
    /// </remarks>
    private double TokenBits(ReadOnlySpan<short> levels, int first, int type, int context)
    {
        levels = levels[..16];
        ref var probabilities = ref unsafe(Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_probabilities), type * 8 * 3 * Vp8Tables.TokenNodes));
        ref var costs = ref unsafe(MemoryMarshal.GetArrayDataReference(BitCosts));
        var bands = Vp8Tables.Bands;
        var zigzag = Vp8Tables.Zigzag;
        var last = -1;
        for (var i = 15; i >= first; i--)
        {
            if (levels[zigzag[i]] != 0)
            {
                last = i;
                break;
            }
        }

        var bits = 0.0;
        var previousWasZero = false;
        for (var i = first; i < 16; i++)
        {
            ref var p = ref unsafe(Unsafe.Add(ref probabilities, ((bands[i] * 3) + context) * Vp8Tables.TokenNodes));
            if (!previousWasZero)
            {
                if (i > last)
                    return bits + Cost(ref costs, p, false); // end of block

                bits += Cost(ref costs, p, true);
            }

            var level = (int)levels[zigzag[i]];
            var value = Math.Abs(level);
            if (value == 0)
            {
                bits += Cost(ref costs, unsafe(Unsafe.Add(ref p, 1)), false);
                previousWasZero = true;
                context = 0;
                continue;
            }

            bits += Cost(ref costs, unsafe(Unsafe.Add(ref p, 1)), true);
            previousWasZero = false;
            if (value == 1)
            {
                bits += Cost(ref costs, unsafe(Unsafe.Add(ref p, 2)), false);
                context = 1;
            }
            else
            {
                bits += Cost(ref costs, unsafe(Unsafe.Add(ref p, 2)), true);
                var sink = new CostSink(bits);
                WriteLargeValue(unsafe(MemoryMarshal.CreateReadOnlySpan(ref p, Vp8Tables.TokenNodes)), value, ref sink);
                bits = sink.Bits;
                context = 2;
            }

            bits += Cost(ref costs, 128, level < 0);
        }

        return bits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double Cost(ref double costs, int probability, bool value) => unsafe(Unsafe.Add(ref costs, value ? 256 - probability : probability));
    }

    private static double[] CreateBitCosts()
    {
        // BitCosts[p] = -log2(p / 256), the cost of a boolean coded with probability p / 256 (p = 0 never occurs)
        var costs = new double[257];
        costs[0] = 16;
        for (var p = 1; p < costs.Length; p++)
        {
            costs[p] = -Math.Log2(p / 256.0);
        }

        return costs;
    }

    /// <summary>
    /// Walks the token tree of a block exactly as it is written (RFC 6386 section 13), reporting every boolean with its
    /// probability: end of block, zero runs, values with their extra bits, signs.
    /// </summary>
    private void WalkTokens<TSink>(ReadOnlySpan<short> levels, int first, int type, int context, ref TSink write)
        where TSink : struct, IBoolSink
    {
        var probabilities = _probabilities.AsSpan(type * 8 * 3 * Vp8Tables.TokenNodes, 8 * 3 * Vp8Tables.TokenNodes);
        var bands = Vp8Tables.Bands;
        var zigzag = Vp8Tables.Zigzag;
        var last = -1;
        for (var i = 15; i >= first; i--)
        {
            if (levels[zigzag[i]] != 0)
            {
                last = i;
                break;
            }
        }

        var previousWasZero = false;
        for (var i = first; i < 16; i++)
        {
            var p = probabilities.Slice(((bands[i] * 3) + context) * Vp8Tables.TokenNodes, Vp8Tables.TokenNodes);
            if (!previousWasZero)
            {
                if (i > last)
                {
                    write.Write(p[0], false); // end of block
                    return;
                }

                write.Write(p[0], true);
            }

            var level = (int)levels[zigzag[i]];
            var value = Math.Abs(level);
            if (value == 0)
            {
                write.Write(p[1], false);
                previousWasZero = true;
                context = 0;
                continue;
            }

            write.Write(p[1], true);
            previousWasZero = false;
            if (value == 1)
            {
                write.Write(p[2], false);
                context = 1;
            }
            else
            {
                write.Write(p[2], true);
                WriteLargeValue(p, value, ref write);
                context = 2;
            }

            write.Write(128, level < 0);
        }
    }

    private static void WriteLargeValue<TSink>(ReadOnlySpan<byte> p, int value, ref TSink write)
        where TSink : struct, IBoolSink
    {
        if (value <= 4)
        {
            write.Write(p[3], false);
            if (value == 2)
            {
                write.Write(p[4], false);
            }
            else
            {
                write.Write(p[4], true);
                write.Write(p[5], value == 4);
            }

            return;
        }

        write.Write(p[3], true);
        if (value <= 10)
        {
            write.Write(p[6], false);
            if (value <= 6)
            {
                write.Write(p[7], false);
                write.Write(159, value == 6);
            }
            else
            {
                write.Write(p[7], true);
                var extra = value - 7;
                write.Write(165, (extra & 2) != 0);
                write.Write(145, (extra & 1) != 0);
            }

            return;
        }

        write.Write(p[6], true);
        int category;
        ReadOnlySpan<byte> extraProbabilities;
        if (value <= 18)
        {
            category = 0;
            extraProbabilities = Vp8Tables.Cat3Probabilities;
        }
        else if (value <= 34)
        {
            category = 1;
            extraProbabilities = Vp8Tables.Cat4Probabilities;
        }
        else if (value <= 66)
        {
            category = 2;
            extraProbabilities = Vp8Tables.Cat5Probabilities;
        }
        else
        {
            category = 3;
            extraProbabilities = Vp8Tables.Cat6Probabilities;
        }

        var b1 = category >> 1;
        var b0 = category & 1;
        write.Write(p[8], b1 != 0);
        write.Write(p[9 + b1], b0 != 0);
        var extraValue = value - (3 + (8 << category));
        for (var i = 0; i < extraProbabilities.Length; i++)
        {
            write.Write(extraProbabilities[i], ((extraValue >> (extraProbabilities.Length - 1 - i)) & 1) != 0);
        }
    }

    /// <summary>Updates the non-zero contexts after a macroblock, exactly as the decoder does.</summary>
    private static void UpdateContexts(ReadOnlySpan<short> levels, bool hasY2, Span<byte> above, Span<byte> left, out bool hasCoefficients)
    {
        hasCoefficients = false;
        if (hasY2)
        {
            var y2 = HasLevels(levels.Slice(24 * 16, 16), 0);
            above[8] = left[8] = y2 ? (byte)1 : (byte)0;
            hasCoefficients |= y2;
        }

        var first = hasY2 ? 1 : 0;
        for (var block = 0; block < 16; block++)
        {
            var nonZero = HasLevels(levels.Slice(block * 16, 16), first);
            above[block & 3] = left[block >> 2] = nonZero ? (byte)1 : (byte)0;
            hasCoefficients |= nonZero;
        }

        for (var block = 0; block < 8; block++)
        {
            var nonZero = HasLevels(levels.Slice((16 + block) * 16, 16), 0);
            var context = 4 + ((block >> 2) * 2);
            above[context + (block & 1)] = left[context + ((block & 3) >> 1)] = nonZero ? (byte)1 : (byte)0;
            hasCoefficients |= nonZero;
        }
    }

    /// <summary>Chooses the loop-filter level: from the quantizer, or by the distortion of candidate levels.</summary>
    private void ChooseFilterLevel()
    {
        var baseLevel = Math.Clamp((int)Math.Round(_q.YAc * 0.35, MidpointRounding.AwayFromZero), 0, 63);
        if (_effort < 4 || baseLevel == 0)
        {
            _filterLevel = baseLevel;
            return;
        }

        var bestLevel = 0;
        var bestDistortion = FilteredDistortion(0);
        foreach (var candidate in new[] { baseLevel / 2, baseLevel, Math.Min(63, (baseLevel * 3) / 2) })
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (candidate == 0)
                continue;

            var distortion = FilteredDistortion(candidate);
            if (distortion < bestDistortion)
            {
                bestDistortion = distortion;
                bestLevel = candidate;
            }
        }

        _filterLevel = bestLevel;
    }

    private long FilteredDistortion(int level)
    {
        using var copy = Vp8Planes.Create(_scope, _width, _height);
        _reconstruction.Y.CopyTo(copy.Y);
        _reconstruction.U.CopyTo(copy.U);
        _reconstruction.V.CopyTo(copy.V);
        if (level > 0)
        {
            var info = new byte[_macroblocks.Length];
            for (var i = 0; i < info.Length; i++)
            {
                var mb = _macroblocks[i];
                var inner = mb.YMode == Vp8Tables.BPred || mb.HasCoefficients;
                info[i] = (byte)(level | (inner ? 0x80 : 0));
            }

            Vp8LoopFilter.Apply(copy, info, _mbWidth, _mbHeight, simple: false, sharpness: 0);
        }

        long sum = 0;
        sum += PlaneDistortion(_source.Y, copy.Y, _source.YStride, _width, _height);
        sum += PlaneDistortion(_source.U, copy.U, _source.UVStride, (_width + 1) >> 1, (_height + 1) >> 1);
        sum += PlaneDistortion(_source.V, copy.V, _source.UVStride, (_width + 1) >> 1, (_height + 1) >> 1);
        return sum;
    }

    private static long PlaneDistortion(ReadOnlySpan<byte> source, ReadOnlySpan<byte> plane, int stride, int width, int height)
    {
        long sum = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var difference = source[(y * stride) + x] - plane[(y * stride) + x];
                sum += difference * difference;
            }
        }

        return sum;
    }

    /// <summary>Counts the token statistics with the decoder's contexts and updates the probabilities that save bits.</summary>
    private void UpdateProbabilities()
    {
        Vp8Tables.DefaultTokenProbabilities.CopyTo(_probabilities);
        if (_effort < 1)
            return;

        var counts = new int[_probabilities.Length * 2];
        WalkFrameTokens((type, band, context, node, value) => counts[((((((type * 8) + band) * 3) + context) * Vp8Tables.TokenNodes) + node) * 2 + (value ? 1 : 0)]++);
        var update = Vp8Tables.TokenUpdateProbabilities;
        for (var i = 0; i < _probabilities.Length; i++)
        {
            var zeros = counts[2 * i];
            var ones = counts[(2 * i) + 1];
            var total = zeros + ones;
            if (total == 0)
                continue;

            var current = _probabilities[i];
            var proposed = (byte)Math.Clamp((int)Math.Round(256.0 * zeros / total, MidpointRounding.AwayFromZero), 1, 255);
            if (proposed == current)
                continue;

            var oldBits = (zeros * BoolBits(current, false)) + (ones * BoolBits(current, true)) + BoolBits(update[i], false);
            var newBits = (zeros * BoolBits(proposed, false)) + (ones * BoolBits(proposed, true)) + BoolBits(update[i], true) + 8;
            if (newBits < oldBits)
            {
                _probabilities[i] = proposed;
            }
        }
    }

    /// <summary>Walks the tokens of every macroblock with the decoder's contexts, reporting (type, band, context, node, value) for the tree nodes.</summary>
    private void WalkFrameTokens(Action<int, int, int, int, bool> visit)
    {
        var aboveNonZero = new byte[_mbWidth * 9];
        var leftNonZero = new byte[9];
        for (var mbY = 0; mbY < _mbHeight; mbY++)
        {
            Array.Clear(leftNonZero);
            for (var mbX = 0; mbX < _mbWidth; mbX++)
            {
                var index = (mbY * _mbWidth) + mbX;
                var mb = _macroblocks[index];
                var levels = Levels.Slice(index * 25 * 16, 25 * 16);
                var above = aboveNonZero.AsSpan(mbX * 9, 9);
                var hasY2 = mb.YMode != Vp8Tables.BPred;
                if (!mb.HasCoefficients && _skipFlag)
                {
                    // Skipped: the decoder clears the contexts (the Y2 contexts only for 16x16 macroblocks)
                    above[..8].Clear();
                    leftNonZero.AsSpan(0, 8).Clear();
                    if (hasY2)
                    {
                        above[8] = leftNonZero[8] = 0;
                    }

                    continue;
                }

                ForEachBlock(levels, hasY2, above, leftNonZero, (blockLevels, first, type, context) => WalkTokensWithNodes(blockLevels, first, type, context, visit));
            }
        }
    }

    /// <summary>Calls <paramref name="action"/> for every coded block of a macroblock in bitstream order, with its context, and updates the contexts.</summary>
    private static void ForEachBlock(ReadOnlySpan<short> levels, bool hasY2, Span<byte> above, Span<byte> left, BlockAction action)
    {
        var first = 0;
        var yType = 3;
        if (hasY2)
        {
            var y2 = levels.Slice(24 * 16, 16);
            action(y2, 0, 1, above[8] + left[8]);
            above[8] = left[8] = HasLevels(y2, 0) ? (byte)1 : (byte)0;
            first = 1;
            yType = 0;
        }

        for (var block = 0; block < 16; block++)
        {
            var blockLevels = levels.Slice(block * 16, 16);
            action(blockLevels, first, yType, above[block & 3] + left[block >> 2]);
            above[block & 3] = left[block >> 2] = HasLevels(blockLevels, first) ? (byte)1 : (byte)0;
        }

        for (var plane = 0; plane < 2; plane++)
        {
            var context = 4 + (plane * 2);
            for (var block = 0; block < 4; block++)
            {
                var blockLevels = levels.Slice((16 + (plane * 4) + block) * 16, 16);
                action(blockLevels, 0, 2, above[context + (block & 1)] + left[context + (block >> 1)]);
                above[context + (block & 1)] = left[context + (block >> 1)] = HasLevels(blockLevels, 0) ? (byte)1 : (byte)0;
            }
        }
    }

    private delegate void BlockAction(ReadOnlySpan<short> levels, int first, int type, int context);

    private static void WalkTokensWithNodes(ReadOnlySpan<short> levels, int first, int type, int context, Action<int, int, int, int, bool> visit)
    {
        // Same walk as WalkTokens, reporting the probability index instead of the probability
        var bands = Vp8Tables.Bands;
        var zigzag = Vp8Tables.Zigzag;
        var last = -1;
        for (var i = 15; i >= first; i--)
        {
            if (levels[zigzag[i]] != 0)
            {
                last = i;
                break;
            }
        }

        var previousWasZero = false;
        for (var i = first; i < 16; i++)
        {
            var band = bands[i];
            if (!previousWasZero)
            {
                if (i > last)
                {
                    visit(type, band, context, 0, false);
                    return;
                }

                visit(type, band, context, 0, true);
            }

            var value = Math.Abs((int)levels[zigzag[i]]);
            if (value == 0)
            {
                visit(type, band, context, 1, false);
                previousWasZero = true;
                context = 0;
                continue;
            }

            visit(type, band, context, 1, true);
            previousWasZero = false;
            if (value == 1)
            {
                visit(type, band, context, 2, false);
                context = 1;
                continue;
            }

            visit(type, band, context, 2, true);
            if (value <= 4)
            {
                visit(type, band, context, 3, false);
                visit(type, band, context, 4, value != 2);
                if (value != 2)
                {
                    visit(type, band, context, 5, value == 4);
                }
            }
            else
            {
                visit(type, band, context, 3, true);
                visit(type, band, context, 6, value > 10);
                if (value <= 10)
                {
                    visit(type, band, context, 7, value > 6);
                }
                else
                {
                    var category = value <= 18 ? 0 : value <= 34 ? 1 : value <= 66 ? 2 : 3;
                    visit(type, band, context, 8, category >= 2);
                    visit(type, band, context, 9 + (category >> 1), (category & 1) != 0);
                }
            }

            context = 2;
        }
    }

    /// <summary>Transfers the payload written by <see cref="WriteFrame"/> to the caller.</summary>
    private PooledPayloadWriter TakeOutput()
    {
        var output = _output ?? throw new InvalidOperationException("No frame was written.");
        _output = null;
        return output;
    }

    /// <summary>Writes the frame: frame tag, key-frame header, first partition (header and modes) and the token partition.</summary>
    /// <returns><see langword="false"/> when the first partition does not fit its 19-bit size field.</returns>
    private bool WriteFrame()
    {
        using var header = new Vp8BoolEncoder(_scope);
        using var tokens = new Vp8BoolEncoder(_scope);
        WriteFrameHeader(header);
        var aboveModes = new byte[_mbWidth * 4];
        var leftModes = new byte[4];
        var aboveNonZero = new byte[_mbWidth * 9];
        var leftNonZero = new byte[9];
        for (var mbY = 0; mbY < _mbHeight; mbY++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Array.Clear(leftModes);
            Array.Clear(leftNonZero);
            for (var mbX = 0; mbX < _mbWidth; mbX++)
            {
                var index = (mbY * _mbWidth) + mbX;
                var mb = _macroblocks[index];
                var hasY2 = mb.YMode != Vp8Tables.BPred;

                // Modes (first partition)
                if (_skipFlag)
                {
                    header.WriteBool(_skipProbability, !mb.HasCoefficients);
                }

                header.WriteTree(KeyFrameYModeTree, Vp8Tables.KeyFrameYModeProbabilities, mb.YMode);
                var above = aboveModes.AsSpan(mbX * 4, 4);
                if (!hasY2)
                {
                    for (var by = 0; by < 4; by++)
                    {
                        for (var bx = 0; bx < 4; bx++)
                        {
                            var mode = mb.GetSubMode((by * 4) + bx);
                            var probabilities = Vp8Tables.KeyFrameBModeProbabilities.Slice(((above[bx] * 10) + leftModes[by]) * 9, 9);
                            header.WriteTree(BModeTree, probabilities, mode);
                            above[bx] = leftModes[by] = mode;
                        }
                    }
                }
                else
                {
                    var implied = mb.YMode switch
                    {
                        Vp8Tables.VPred => Vp8Tables.BVePred,
                        Vp8Tables.HPred => Vp8Tables.BHePred,
                        Vp8Tables.TmPred => Vp8Tables.BTmPred,
                        _ => Vp8Tables.BDcPred,
                    };
                    above.Fill((byte)implied);
                    leftModes.AsSpan().Fill((byte)implied);
                }

                header.WriteTree(UVModeTree, Vp8Tables.KeyFrameUVModeProbabilities, mb.UVMode);

                // Tokens
                var nonZeroAbove = aboveNonZero.AsSpan(mbX * 9, 9);
                if (!mb.HasCoefficients && _skipFlag)
                {
                    nonZeroAbove[..8].Clear();
                    leftNonZero.AsSpan(0, 8).Clear();
                    if (hasY2)
                    {
                        nonZeroAbove[8] = leftNonZero[8] = 0;
                    }

                    continue;
                }

                var levels = Levels.Slice(index * 25 * 16, 25 * 16);
                ForEachBlock(levels, hasY2, nonZeroAbove, leftNonZero, (blockLevels, first, type, context) =>
                {
                    var sink = new EncoderSink(tokens);
                    WalkTokens(blockLevels, first, type, context, ref sink);
                });
            }
        }

        header.Flush();
        tokens.Flush();
        var firstPartition = header.WrittenMemory.Span;
        if (firstPartition.Length > _maxFirstPartitionSize)
            return false;

        _output?.Dispose();
        _output = new PooledPayloadWriter(_scope);
        var payload = _output;
        {
            // Frame tag: key frame, version 0, shown, first partition size; then the start code and the dimensions
            var tag = (firstPartition.Length << 5) | (1 << 4);
            Span<byte> frameHeader = stackalloc byte[Vp8Decoder.FrameHeaderLength];
            frameHeader[0] = (byte)tag;
            frameHeader[1] = (byte)(tag >> 8);
            frameHeader[2] = (byte)(tag >> 16);
            frameHeader[3] = 0x9D;
            frameHeader[4] = 0x01;
            frameHeader[5] = 0x2A;
            frameHeader[6] = (byte)_width;
            frameHeader[7] = (byte)(_width >> 8);
            frameHeader[8] = (byte)_height;
            frameHeader[9] = (byte)(_height >> 8);
            payload.Write(frameHeader);
            payload.Write(firstPartition);
            payload.Write(tokens.WrittenMemory.Span);
            return true;
        }
    }

    private int _skipProbability = 255;
    private PooledPayloadWriter? _output;

    private void WriteFrameHeader(Vp8BoolEncoder d)
    {
        d.WriteBit(false); // color space: BT.601
        d.WriteBit(false); // clamping required
        d.WriteBit(false); // no segmentation
        d.WriteBit(false); // normal loop filter
        d.WriteLiteral(_filterLevel, 6);
        d.WriteLiteral(0, 3); // sharpness
        d.WriteBit(false); // no loop-filter adjustments
        d.WriteLiteral(0, 2); // one token partition
        d.WriteLiteral(_quantizerIndex, 7);
        for (var i = 0; i < 5; i++)
        {
            d.WriteBit(false); // no quantizer delta
        }

        d.WriteBit(false); // refresh_entropy_probs
        var defaults = Vp8Tables.DefaultTokenProbabilities;
        var update = Vp8Tables.TokenUpdateProbabilities;
        for (var i = 0; i < _probabilities.Length; i++)
        {
            var changed = _probabilities[i] != defaults[i];
            d.WriteBool(update[i], changed);
            if (changed)
            {
                d.WriteLiteral(_probabilities[i], 8);
            }
        }

        // The skip flag (left out when the modes need its bits: every macroblock then codes its tokens): its probability from
        // the macroblock statistics
        if (!_skipFlag)
        {
            d.WriteBit(false);
            return;
        }

        var skipped = 0;
        foreach (var mb in _macroblocks)
        {
            if (!mb.HasCoefficients)
            {
                skipped++;
            }
        }

        _skipProbability = Math.Clamp((int)Math.Round(256.0 * (_macroblocks.Length - skipped) / _macroblocks.Length, MidpointRounding.AwayFromZero), 1, 255);
        d.WriteBit(true);
        d.WriteLiteral(_skipProbability, 8);
    }

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);

    /// <summary>Receives the booleans of a token walk.</summary>
    private interface IBoolSink
    {
        void Write(int probability, bool value);
    }

    /// <summary>Sums the estimated cost in bits of the booleans.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct CostSink(double bits) : IBoolSink
    {
        public double Bits { get; private set; } = bits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(int probability, bool value) => Bits += BoolBits(probability, value);
    }

    /// <summary>Writes the booleans to a boolean encoder.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct EncoderSink(Vp8BoolEncoder encoder) : IBoolSink
    {
        public void Write(int probability, bool value) => encoder.WriteBool(probability, value);
    }

    [StructLayout(LayoutKind.Auto)]
    private struct Macroblock
    {
        private ulong _subModes;

        public byte YMode { get; set; }

        public byte UVMode { get; set; }

        public bool HasCoefficients { get; set; }

        public readonly byte GetSubMode(int index) => (byte)((_subModes >> (index * 4)) & 0xF);

        public void SetSubModes(ReadOnlySpan<byte> modes)
        {
            _subModes = 0;
            for (var i = 0; i < 16; i++)
            {
                _subModes |= (ulong)modes[i] << (i * 4);
            }
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Quantizer(short YDc, short YAc, short Y2Dc, short Y2Ac, short UVDc, short UVAc)
    {
        public static Quantizer Create(int q) => new(
            YDc: Vp8Tables.DcQuantizer[q],
            YAc: Vp8Tables.AcQuantizer[q],
            Y2Dc: (short)(Vp8Tables.DcQuantizer[q] * 2),
            Y2Ac: (short)Math.Max(8, Vp8Tables.AcQuantizer[q] * 155 / 100),
            UVDc: (short)Math.Min(132, (int)Vp8Tables.DcQuantizer[q]),
            UVAc: Vp8Tables.AcQuantizer[q]);
    }
}
