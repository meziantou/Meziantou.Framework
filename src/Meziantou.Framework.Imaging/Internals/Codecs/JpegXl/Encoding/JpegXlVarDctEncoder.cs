using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes the three color channels of a frame with VarDCT, in the XYB color space (ISO/IEC 18181-1, annex I and sections
/// C.4 to C.9, in the direction the standard does not describe): the mirror of <see cref="JpegXlVarDctDecoder"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Analysis, group by group: the samples of the group are converted to XYB, and sharpened when the decoder smooths the
/// frame (<see cref="JpegXlInverseGaborish"/>); the transform of each area is chosen, from the 8x8 DCT to the 64x64 one
/// by the effort, by what it costs for the luma (<see cref="GetTransformCost"/>); then each transform is applied and
/// quantized, and only the quantized values are kept: one value per coefficient, and the LF value of each block. The
/// blocks of a frame do not predict each other, so the encoder does not reconstruct what it writes.
/// </description></item>
/// <item><description>
/// Every block has the quantization factor of the frame: a factor that follows the content of the block was measured
/// and is larger at equal quality (see the notes of the library).
/// </description></item>
/// <item><description>
/// The channels are X, Y and B in that order, as in the decoder; Y is quantized first, because the decoder predicts the
/// two others from its dequantized values (chroma from luma): B is predicted from Y even with the default parameters.
/// </description></item>
/// <item><description>
/// The sections are written in the order of the table of contents: <c>LfGlobal</c>, the LF groups (the LF image and the
/// transform, the quantization factor and the filter sharpness of each block), <c>HfGlobal</c> (the entropy code of each
/// pass), then the groups of each pass. The coefficients of a group are given to the entropy coder twice: to build the
/// code of the pass, then to write the group.
/// </description></item>
/// <item><description>
/// Passes: with several passes, each coefficient belongs to one pass and is zero in the others, by its frequency (the
/// lowest quarter of the frequencies of each direction, then the lowest half, then the rest), so the passes add up to the
/// coefficients of a frame of one pass.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class JpegXlVarDctEncoder : IDisposable
{
    private const int GroupBlocks = JpegXlCoefficientContexts.GroupBlocks;
    private const int GroupSize = GroupBlocks * 8;
    private const int ColorTileBlocks = 8;
    private const int BlockContextCount = 15;

    private const byte BlockCovered = 1;
    private const byte BlockFirst = 2;

    /// <summary>The quantization factor of a block that is neither coarser nor finer than the frame.</summary>
    private const int NominalQuantField = 32;

    /// <summary>The sharpness of the edge-preserving filter of every block: the middle of its range.</summary>
    private const int Sharpness = 4;

    /// <summary>The effort from which each tile has its own factors of the chroma from luma prediction.</summary>
    private const int ChromaFromLumaEffort = 3;

    /// <summary>The unit of the factors of the chroma from luma prediction: the default value of the frame.</summary>
    private const float ColorScale = 1f / 84;

    /// <summary>The effort from which the transform of each block is chosen.</summary>
    private const int TransformSelectionEffort = 3;

    /// <summary>The effort from which the transforms that are twice as wide as high, or as high as wide, are tried.</summary>
    private const int RectangularTransformEffort = 7;

    /// <summary>The effort from which transforms of 32x32 and 64x64 samples, and their halves, are tried.</summary>
    private const int LargeTransformEffort = 8;

    /// <summary>
    /// What an error is worth in bits, when a transform is chosen: the weight of the squared error of the samples, in units
    /// of the quantization step of a coefficient in the middle of the 8x8 block. Measured: see the remarks of
    /// <see cref="GetTransformCost"/>.
    /// </summary>
    private const float DistortionWeight = 0.8f;

    // The estimated size of the coefficients of a transform, fitted on the sizes of frames coded with one transform: a
    // coefficient that is not zero, each bit of its magnitude, a zero before the last coefficient that is not zero (by
    // the number of coefficients of the transform: 64, 256, 1024 and 4096), a transform without coefficient, and what
    // names the transform in the block list, the 8x8 DCT being the common one
    private const float NonZeroBits = 3.5f;
    private const float MagnitudeBits = 1f;
    private const float ZeroBits8 = 0.76f;
    private const float ZeroBits16 = 0.6f;
    private const float ZeroBits32 = 0.44f;
    private const float ZeroBits64 = 0.28f;
    private const float EmptyTransformBits = 0.3f;
    private const float TypeBits8 = 0.5f;
    private const float TypeBits = 4f;

    /// <summary>
    /// The factor of the distance of a frame that the decoder smooths: the sharpening of the encoder gives the same
    /// quantization steps more to code, so the steps are larger for the same file size, and the frame is closer to the
    /// original than without the filter.
    /// </summary>
    private const float GaborishDistanceFactor = 1.25f;

    private static readonly JpegXlHybridUintConfig CoefficientConfig = new(4, 2, 0);

    private readonly JpegXlImageHeader _image;
    private readonly JpegXlFrameHeader _frame;
    private readonly PixelFormat _format;
    private readonly int _effort;
    private readonly AllocationScope _scope;
    private readonly CancellationToken _cancellationToken;
    private readonly int _width;
    private readonly int _height;
    private readonly int _blocksWide;
    private readonly int _blocksHigh;
    private readonly int _tilesWide;
    private readonly int _tilesHigh;
    private readonly int _groupColumns;
    private readonly int _groupCount;
    private readonly int _lfGroupCount;
    private readonly int _passCount;
    private readonly int _globalScale;
    private readonly int _quantLf;
    private readonly float[] _lfStep = new float[3];
    private readonly float[] _hfScale = new float[3];
    private readonly int[] _horizontalShift = new int[3];
    private readonly int[] _verticalShift = new int[3];
    private readonly TileBlock[] _tileBlocks = new TileBlock[ColorTileBlocks * ColorTileBlocks];
    private readonly int[]?[] _ranks = new int[]?[JpegXlVarDctTables.OrderCount];
    private readonly bool _gaborish;
    private readonly float[] _gaborishWeights = new float[3 * JpegXlInverseGaborish.WeightCount];
    private readonly Av1PooledArray<float>?[] _padded = new Av1PooledArray<float>?[3];
    private readonly int _maxTransformBlocks = 1;
    private readonly bool _usesRectangles;
    private float _referenceStep;

    // The quantization table of each channel of a recompressed JPEG file, in the layout of the coefficients of the frame
    private ushort[][]? _jpegQuantization;

    private readonly Av1PooledArray<int>?[] _coefficients = new Av1PooledArray<int>?[3];
    private readonly Av1PooledArray<int>?[] _lf = new Av1PooledArray<int>?[3];
    private readonly Av1PooledArray<float>?[] _samples = new Av1PooledArray<float>?[3];
    private Av1PooledArray<byte>? _blockInfo;
    private Av1PooledArray<ushort>? _quantField;
    private Av1PooledArray<sbyte>? _xFromY;
    private Av1PooledArray<sbyte>? _bFromY;
    private Av1PooledArray<int>? _groupOffsets;
    private Av1PooledArray<int>? _blockOffsets;
    private Av1PooledArray<byte>? _nonZeros;
    private Av1PooledArray<float>? _work;
    private Av1PooledArray<float>? _tileCoefficients;
    private Av1PooledArray<float>? _tileLuma;
    private Av1PooledArray<float>? _candidate;
    private JpegXlDequantMatrices? _matrices;
    private JpegXlCoefficientOrders? _orders;
    private JpegXlForwardColor? _color;

    /// <param name="image">The image header that was written, as the decoder reads it: an image stored in XYB.</param>
    /// <param name="frame">The frame header that was written, as the decoder reads it: a VarDCT frame that covers the image.</param>
    /// <param name="format">The pixel format of the frame.</param>
    /// <param name="quality">The quality, from 0 to 100; -1 for a frame that holds the coefficients of a JPEG file (<see cref="EncodeJpegCoefficients"/>).</param>
    /// <param name="effort">The effort, from 0 to 9.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cancellationToken">The token checked between groups.</param>
    public JpegXlVarDctEncoder(JpegXlImageHeader image, JpegXlFrameHeader frame, PixelFormat format, int quality, int effort, AllocationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(scope);
        _image = image;
        _frame = frame;
        _format = format;
        _effort = effort;
        _scope = scope;
        _cancellationToken = cancellationToken;
        _width = (int)frame.CodedWidth;
        _height = (int)frame.CodedHeight;

        // The channels of a YCbCr frame may be subsampled, as the ones of the JPEG file it holds: the frame is then made of
        // whole minimum coded units
        ReadOnlySpan<int> horizontalFactors = [0, 1, 1, 0];
        ReadOnlySpan<int> verticalFactors = [0, 1, 0, 1];
        ReadOnlySpan<int> modes = [frame.ChromaSubsampling.First, frame.ChromaSubsampling.Second, frame.ChromaSubsampling.Third];
        var maxHorizontal = 0;
        var maxVertical = 0;
        foreach (var mode in modes)
        {
            maxHorizontal = Math.Max(maxHorizontal, horizontalFactors[mode]);
            maxVertical = Math.Max(maxVertical, verticalFactors[mode]);
        }

        for (var c = 0; c < 3; c++)
        {
            _horizontalShift[c] = maxHorizontal - horizontalFactors[modes[c]];
            _verticalShift[c] = maxVertical - verticalFactors[modes[c]];
        }

        _blocksWide = ((_width + (8 << maxHorizontal) - 1) / (8 << maxHorizontal)) << maxHorizontal;
        _blocksHigh = ((_height + (8 << maxVertical) - 1) / (8 << maxVertical)) << maxVertical;
        _tilesWide = (_blocksWide + ColorTileBlocks - 1) / ColorTileBlocks;
        _tilesHigh = (_blocksHigh + ColorTileBlocks - 1) / ColorTileBlocks;
        _groupColumns = (int)frame.GroupColumns;
        _groupCount = checked((int)frame.GroupCount);
        _lfGroupCount = checked((int)frame.LfGroupCount);
        _passCount = frame.PassCount;

        // The quantization: a step is a value of the dequantization matrix divided by "scale * factor / 65536"
        if (quality < 0)
        {
            // The coefficients of a JPEG file are stored as they are: the quantization of the frame is neutral, and the steps
            // are the quantization tables of the file
            _globalScale = 65536;
            _quantLf = 1;
            return;
        }

        // The decoder smooths the frame: the encoder sharpens it first, with what undoes the weights of each channel
        _gaborish = frame.RestorationFilter.IsGaborEnabled;
        var distance = GetDistance(quality) * (_gaborish ? GaborishDistanceFactor : 1f);
        _globalScale = Math.Clamp((int)MathF.Round(65536f * HfScaleAtDistance1 / (distance * NominalQuantField), MidpointRounding.AwayFromZero), 1, 8193 + 65535);
        _quantLf = Math.Clamp((int)MathF.Round(65536f * LfScaleAtDistance1 / (GetLfDistance(distance) * _globalScale), MidpointRounding.AwayFromZero), 1, 65536);
        ReadOnlySpan<float> lfQuant = [1f / 4096, 1f / 512, 1f / 256];
        for (var c = 0; c < 3; c++)
        {
            _lfStep[c] = lfQuant[c] * (65536f / _globalScale / _quantLf);
        }

        // The X and B channels have their own scale of the quantization: a power of 0.8
        _hfScale[0] = MathF.Pow(0.8f, frame.XqmScale - 2);
        _hfScale[1] = 1;
        _hfScale[2] = MathF.Pow(0.8f, frame.BqmScale - 2);

        if (_gaborish)
        {
            for (var c = 0; c < 3; c++)
            {
                JpegXlInverseGaborish.GetWeights(frame.RestorationFilter.GaborWeights[c * 2], frame.RestorationFilter.GaborWeights[(c * 2) + 1], _gaborishWeights.AsSpan(c * JpegXlInverseGaborish.WeightCount, JpegXlInverseGaborish.WeightCount));
            }
        }

        // What the effort searches: the 16x16 transform from 3, the transforms that are not square from 7, the transforms
        // of up to 64 samples per side from 8
        _maxTransformBlocks = effort switch
        {
            < TransformSelectionEffort => 1,
            < LargeTransformEffort => 2,
            _ => 8,
        };
        _usesRectangles = effort >= RectangularTransformEffort;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct TileBlock(byte X, byte Y, byte Transform, int Offset);

    /// <summary>Writes the Modular channels of a rectangle of the image that are not color channels (the alpha channel), as one sub-bitstream.</summary>
    public delegate void ExtraChannelWriter(LsbBitWriter writer, scoped in PixelLease lease, int x, int y, int width, int height);

    /// <summary>The scale of the quantization of the coefficients at distance 1, where a difference is barely visible.</summary>
    private static float HfScaleAtDistance1 => 0.8f;

    /// <summary>The scale of the quantization of the LF image at distance 1.</summary>
    private static float LfScaleAtDistance1 => 2.0f;

    /// <summary>
    /// Gets the distance of a quality: the factor by which the quantization steps are multiplied, 1 being a quality at which
    /// the difference with the original is barely visible.
    /// </summary>
    public static float GetDistance(int quality)
    {
        // Straight lines between a few qualities
        ReadOnlySpan<int> qualities = [0, 25, 50, 75, 90, 100];
        ReadOnlySpan<float> distances = [15f, 8f, 4.5f, 2.2f, 1f, 0.1f];
        for (var i = 1; i < qualities.Length; i++)
        {
            if (quality <= qualities[i])
            {
                var position = (quality - qualities[i - 1]) / (float)(qualities[i] - qualities[i - 1]);
                return distances[i - 1] + ((distances[i] - distances[i - 1]) * position);
            }
        }

        return distances[^1];
    }

    /// <summary>
    /// Gets the number of iterations of the edge-preserving filter that the decoder applies for a quality: the filter removes
    /// the ringing of coarse quantization steps, and costs decoding time, so fine steps do not use it.
    /// </summary>
    public static int GetEpfIterations(int quality)
    {
        return GetDistance(quality) switch
        {
            < 0.6f => 0,
            < 1.5f => 1,
            < 3.5f => 2,
            _ => 3,
        };
    }

    /// <summary>
    /// Gets a value indicating whether the decoder smooths the frames of a quality with the Gabor-like filter, which the
    /// encoder prepares by sharpening them (<see cref="JpegXlInverseGaborish"/>). The pair removes the edges that the
    /// quantization leaves between blocks; the sharpening is not the exact inverse of the smoothing, which shows on fine
    /// textures once the quantization is fine, so the highest qualities do not use it.
    /// </summary>
    public static bool UsesGaborish(int quality) => GetDistance(quality) >= 1f;

    /// <summary>Encodes a frame: its sections, one after the other, and their sizes.</summary>
    /// <param name="lease">The pixels of the frame.</param>
    /// <param name="writer">Receives the sections.</param>
    /// <param name="sizes">Receives the size of each section, in bytes.</param>
    /// <param name="writeExtraChannels">Writes the alpha channel of a rectangle, when the image has one.</param>
    public void Encode(scoped in PixelLease lease, LsbBitWriter writer, List<int> sizes, ExtraChannelWriter? writeExtraChannels)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sizes);
        Allocate();
        for (var group = 0; group < _groupCount; group++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            AnalyzeGroup(lease, group);
        }

        // The samples are not needed anymore
        for (var c = 0; c < 3; c++)
        {
            _samples[c]?.Dispose();
            _samples[c] = null;
        }

        _color?.Dispose();
        _color = null;
        WriteSections(lease, writer, sizes, writeExtraChannels);
    }

    /// <summary>
    /// Encodes a frame that holds the quantized coefficients of a JPEG file (ISO/IEC 18181-2, annex A): every block is an
    /// 8x8 DCT, the quantization tables of the file are stored by their values, the LF image holds the DC coefficients,
    /// and nothing predicts the chroma from the luma, so that a decoder reads the coefficients back as they are.
    /// </summary>
    /// <param name="coefficients">The coefficients of the components of the file.</param>
    /// <param name="channelComponents">For each channel of the frame (Cb, Y, Cr; or red, green, blue), its component in <paramref name="coefficients"/>, or -1 for a channel a grayscale file does not have.</param>
    /// <param name="quantization">The quantization table of each channel, in the order of the rows of a JPEG block.</param>
    /// <param name="dcOffset">What is added to the DC coefficients of each channel.</param>
    /// <param name="writer">Receives the sections.</param>
    /// <param name="sizes">Receives the size of each section, in bytes.</param>
    public void EncodeJpegCoefficients(JpegCoefficientStore coefficients, ReadOnlySpan<int> channelComponents, ReadOnlySpan<ushort[]> quantization, ReadOnlySpan<int> dcOffset, LsbBitWriter writer, List<int> sizes)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sizes);
        AllocateFrame(clear: true);

        // The tables of the frame are the transposition of the ones of JPEG, like the blocks
        _jpegQuantization = [new ushort[64], new ushort[64], new ushort[64]];
        for (var c = 0; c < 3; c++)
        {
            for (var k = 0; k < 64; k++)
            {
                _jpegQuantization[c][((k & 7) * 8) + (k >> 3)] = quantization[c][k];
            }
        }

        var blockInfo = _blockInfo!.Span;
        var quantField = _quantField!.Span;
        blockInfo.Fill(BlockFirst + JpegXlVarDctTables.TransformDct8);
        quantField.Fill(1);
        for (var group = 0; group < _groupCount; group++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            GetGroupRectangle(group, out var groupX, out var groupY, out var groupWidth, out var groupHeight);
            var groupOffset = _groupOffsets!.Span[group];
            for (var c = 0; c < 3; c++)
            {
                var component = channelComponents[c];
                if (component < 0)
                    continue;

                var hs = _horizontalShift[c];
                var vs = _verticalShift[c];
                var lf = _lf[c]!.Span;
                var target = _coefficients[c]!.Span;
                for (var y = 0; y < groupHeight; y += 1 << vs)
                {
                    for (var x = 0; x < groupWidth; x += 1 << hs)
                    {
                        var channelX = (groupX + x) >> hs;
                        var channelY = (groupY + y) >> vs;
                        var block = coefficients.GetBlock(component, channelX, channelY);
                        lf[(channelY * (_blocksWide >> hs)) + channelX] = block[0] + dcOffset[c];
                        var offset = groupOffset + (((y * groupWidth) + x) * 64);
                        for (var k = 1; k < 64; k++)
                        {
                            target[offset + ((k & 7) * 8) + (k >> 3)] = block[k];
                        }
                    }
                }
            }
        }

        WriteSections(default, writer, sizes, writeExtraChannels: null);
    }

    public void Dispose()
    {
        for (var c = 0; c < 3; c++)
        {
            _coefficients[c]?.Dispose();
            _coefficients[c] = null;
            _lf[c]?.Dispose();
            _lf[c] = null;
            _samples[c]?.Dispose();
            _samples[c] = null;
            _padded[c]?.Dispose();
            _padded[c] = null;
        }

        _blockInfo?.Dispose();
        _blockInfo = null;
        _quantField?.Dispose();
        _quantField = null;
        _xFromY?.Dispose();
        _xFromY = null;
        _bFromY?.Dispose();
        _bFromY = null;
        _groupOffsets?.Dispose();
        _groupOffsets = null;
        _blockOffsets?.Dispose();
        _blockOffsets = null;
        _nonZeros?.Dispose();
        _nonZeros = null;
        _work?.Dispose();
        _work = null;
        _tileCoefficients?.Dispose();
        _tileCoefficients = null;
        _tileLuma?.Dispose();
        _tileLuma = null;
        _candidate?.Dispose();
        _candidate = null;
        _matrices?.Dispose();
        _matrices = null;
        _orders?.Dispose();
        _orders = null;
        _color?.Dispose();
        _color = null;
    }

    /// <summary>The distance of the LF image: it grows more slowly than the one of the coefficients, since an error of the LF image is a flat error of a whole block.</summary>
    private static float GetLfDistance(float distance) => distance <= 1 ? distance : 1 + ((distance - 1) * 0.5f);

    private void Allocate()
    {
        AllocateFrame(clear: false);
        for (var c = 0; c < 3; c++)
        {
            _samples[c] = Av1PooledArray<float>.Rent(_scope, GroupSize * GroupSize, clear: false);
            if (_gaborish)
            {
                const int PaddedSize = GroupSize + (2 * JpegXlInverseGaborish.Radius);
                _padded[c] = Av1PooledArray<float>.Rent(_scope, PaddedSize * PaddedSize, clear: false);
            }
        }

        _work = Av1PooledArray<float>.Rent(_scope, (long)JpegXlForwardTransform.MaxSize * JpegXlForwardTransform.MaxSize, clear: false);
        _tileCoefficients = Av1PooledArray<float>.Rent(_scope, 3L * ColorTileBlocks * ColorTileBlocks * 64, clear: false);
        _tileLuma = Av1PooledArray<float>.Rent(_scope, (long)ColorTileBlocks * ColorTileBlocks * 64, clear: false);
        _candidate = Av1PooledArray<float>.Rent(_scope, (long)JpegXlForwardTransform.MaxSize * JpegXlForwardTransform.MaxSize, clear: false);
        _matrices = JpegXlDequantMatrices.CreateDefault(_scope);

        // The reference of the errors: the step of a coefficient in the middle of the 8x8 block of the luma
        _referenceStep = _matrices.GetMatrix(JpegXlVarDctTables.QuantTable[JpegXlVarDctTables.TransformDct8])[64 + (3 * 8) + 3];
        _color = new JpegXlForwardColor(_image, PixelFormats.GetBitsPerComponent(_format), _scope);
    }

    /// <summary>Rents what holds a frame once it is analyzed: the quantized values and what describes each block.</summary>
    private void AllocateFrame(bool clear)
    {
        var blockCount = (long)_blocksWide * _blocksHigh;
        for (var c = 0; c < 3; c++)
        {
            _coefficients[c] = Av1PooledArray<int>.Rent(_scope, blockCount * 64, clear);
            _lf[c] = Av1PooledArray<int>.Rent(_scope, (long)(_blocksWide >> _horizontalShift[c]) * (_blocksHigh >> _verticalShift[c]), clear);
        }

        _blockInfo = Av1PooledArray<byte>.Rent(_scope, blockCount, clear: true);
        _quantField = Av1PooledArray<ushort>.Rent(_scope, blockCount, clear: true);
        _xFromY = Av1PooledArray<sbyte>.Rent(_scope, (long)_tilesWide * _tilesHigh, clear: true);
        _bFromY = Av1PooledArray<sbyte>.Rent(_scope, (long)_tilesWide * _tilesHigh, clear: true);
        _groupOffsets = Av1PooledArray<int>.Rent(_scope, _groupCount, clear: false);
        _blockOffsets = Av1PooledArray<int>.Rent(_scope, GroupBlocks * GroupBlocks, clear: false);
        _nonZeros = Av1PooledArray<byte>.Rent(_scope, 3L * GroupBlocks * GroupBlocks, clear: false);
        _orders = new JpegXlCoefficientOrders(1, _scope);

        // The coefficients of the groups follow each other, each group holding 64 values per block
        var offsets = _groupOffsets.Span;
        var offset = 0;
        for (var group = 0; group < _groupCount; group++)
        {
            offsets[group] = offset;
            GetGroupRectangle(group, out _, out _, out var width, out var height);
            offset += width * height * 64;
        }
    }

    private void GetGroupRectangle(int group, out int x, out int y, out int width, out int height)
    {
        x = group % _groupColumns * GroupBlocks;
        y = group / _groupColumns * GroupBlocks;
        width = Math.Min(GroupBlocks, _blocksWide - x);
        height = Math.Min(GroupBlocks, _blocksHigh - y);
    }

    /// <summary>
    /// Converts the samples of a group to XYB, chooses the transform of its blocks, then transforms and quantizes them,
    /// tile by tile.
    /// </summary>
    private void AnalyzeGroup(scoped in PixelLease lease, int group)
    {
        GetGroupRectangle(group, out var groupX, out var groupY, out var groupWidth, out var groupHeight);
        LoadSamples(lease, groupX * 8, groupY * 8, groupWidth * 8, groupHeight * 8);

        // The transforms of every tile first: where the coefficients of a transform are stored depends on the ones before it
        for (var tileY = 0; tileY < groupHeight; tileY += ColorTileBlocks)
        {
            for (var tileX = 0; tileX < groupWidth; tileX += ColorTileBlocks)
            {
                ChooseTransforms(groupX, groupY, tileX, tileY, Math.Min(ColorTileBlocks, groupWidth - tileX), Math.Min(ColorTileBlocks, groupHeight - tileY));
            }
        }

        var blockInfo = _blockInfo!.Span;
        var offsets = _blockOffsets!.Span;
        var next = 0;
        for (var y = 0; y < groupHeight; y++)
        {
            var row = blockInfo.Slice(((groupY + y) * _blocksWide) + groupX, groupWidth);
            for (var x = 0; x < groupWidth; x++)
            {
                if (row[x] < BlockFirst)
                    continue;

                var transform = row[x] - BlockFirst;
                offsets[(y * GroupBlocks) + x] = next;
                next += JpegXlVarDctTables.BlocksWide[transform] * JpegXlVarDctTables.BlocksHigh[transform] * 64;
            }
        }

        var groupOffset = _groupOffsets!.Span[group];
        for (var tileY = 0; tileY < groupHeight; tileY += ColorTileBlocks)
        {
            for (var tileX = 0; tileX < groupWidth; tileX += ColorTileBlocks)
            {
                EncodeTile(groupOffset, groupX, groupY, tileX, tileY, Math.Min(ColorTileBlocks, groupWidth - tileX), Math.Min(ColorTileBlocks, groupHeight - tileY));
            }
        }
    }

    /// <summary>
    /// Chooses the transforms of a tile: each square of blocks is one transform, two transforms of half its size side by
    /// side or one above the other, or its four quarters, whichever has the smallest cost; the quarters are chosen first.
    /// </summary>
    private void ChooseTransforms(int groupX, int groupY, int tileX, int tileY, int tileWidth, int tileHeight)
    {
        // For each block of the tile: 0 when a transform that starts elsewhere covers it, or its transform plus one
        Span<byte> plan = stackalloc byte[ColorTileBlocks * ColorTileBlocks];
        plan.Clear();
        if (_maxTransformBlocks == 1)
        {
            plan.Fill(JpegXlVarDctTables.TransformDct8 + 1);
        }
        else
        {
            ChooseSquare(plan, tileX, tileY, 0, 0, ColorTileBlocks, tileWidth, tileHeight);
        }

        var blockInfo = _blockInfo!.Span;
        var quantField = _quantField!.Span;
        for (var y = 0; y < tileHeight; y++)
        {
            for (var x = 0; x < tileWidth; x++)
            {
                if (plan[(y * ColorTileBlocks) + x] == 0)
                    continue;

                var transform = plan[(y * ColorTileBlocks) + x] - 1;
                var blocksWide = JpegXlVarDctTables.BlocksWide[transform];
                var blocksHigh = JpegXlVarDctTables.BlocksHigh[transform];
                var position = ((groupY + tileY + y) * _blocksWide) + groupX + tileX + x;
                for (var by = 0; by < blocksHigh; by++)
                {
                    blockInfo.Slice(position + (by * _blocksWide), blocksWide).Fill(BlockCovered);
                    quantField.Slice(position + (by * _blocksWide), blocksWide).Fill(NominalQuantField);
                }

                blockInfo[position] = (byte)(BlockFirst + transform);
            }
        }
    }

    /// <summary>Chooses the transforms of a square of blocks of a tile, and returns their cost.</summary>
    /// <param name="plan">The transforms of the tile, updated.</param>
    /// <param name="tileX">The first column of blocks of the tile in its group.</param>
    /// <param name="tileY">The first row of blocks of the tile in its group.</param>
    /// <param name="x">The first column of blocks of the square in the tile.</param>
    /// <param name="y">The first row of blocks of the square in the tile.</param>
    /// <param name="blocks">The side of the square, in blocks.</param>
    /// <param name="tileWidth">The number of columns of blocks of the tile that are in the frame.</param>
    /// <param name="tileHeight">The number of rows of blocks of the tile that are in the frame.</param>
    private float ChooseSquare(Span<byte> plan, int tileX, int tileY, int x, int y, int blocks, int tileWidth, int tileHeight)
    {
        if (blocks == 1)
        {
            plan[(y * ColorTileBlocks) + x] = JpegXlVarDctTables.TransformDct8 + 1;
            return GetTransformCost(JpegXlVarDctTables.TransformDct8, tileX + x, tileY + y);
        }

        // The quarters that are in the frame
        var half = blocks / 2;
        Span<float> costs = [0, 0, 0, 0];
        Span<bool> whole = [false, false, false, false];
        for (var i = 0; i < 4; i++)
        {
            var quarterX = x + ((i & 1) * half);
            var quarterY = y + ((i >> 1) * half);
            if (quarterX >= tileWidth || quarterY >= tileHeight)
                continue;

            costs[i] = ChooseSquare(plan, tileX, tileY, quarterX, quarterY, half, tileWidth, tileHeight);

            // A larger transform is only tried over quarters that are each one transform: where the smaller ones did not
            // win, a larger one rarely does, and its evaluation is what costs the most
            var transform = plan[(quarterY * ColorTileBlocks) + quarterX] - 1;
            whole[i] = half == 1 || (transform >= 0 && JpegXlVarDctTables.BlocksWide[transform] == half && JpegXlVarDctTables.BlocksHigh[transform] == half);
        }

        var split = costs[0] + costs[1] + costs[2] + costs[3];
        if (blocks > _maxTransformBlocks)
            return split;

        var fitsWidth = x + blocks <= tileWidth;
        var fitsHeight = y + blocks <= tileHeight;
        var (full, wide, tall) = blocks switch
        {
            2 => (4, 7, 6),
            4 => (5, 11, 10),
            _ => (18, 20, 19),
        };

        // Two transforms, each as wide as the square, one above the other; or the quarters of the half that is not worth it
        var top = costs[0] + costs[1];
        var bottom = costs[2] + costs[3];
        var isTopMerged = false;
        var isBottomMerged = false;
        if (_usesRectangles && fitsWidth && whole[0] && whole[1])
        {
            var cost = GetTransformCost(wide, tileX + x, tileY + y);
            if (cost < top)
            {
                top = cost;
                isTopMerged = true;
            }
        }

        if (_usesRectangles && fitsWidth && fitsHeight && whole[2] && whole[3])
        {
            var cost = GetTransformCost(wide, tileX + x, tileY + y + half);
            if (cost < bottom)
            {
                bottom = cost;
                isBottomMerged = true;
            }
        }

        // Or side by side
        var left = costs[0] + costs[2];
        var right = costs[1] + costs[3];
        var isLeftMerged = false;
        var isRightMerged = false;
        if (_usesRectangles && fitsHeight && whole[0] && whole[2])
        {
            var cost = GetTransformCost(tall, tileX + x, tileY + y);
            if (cost < left)
            {
                left = cost;
                isLeftMerged = true;
            }
        }

        if (_usesRectangles && fitsWidth && fitsHeight && whole[1] && whole[3])
        {
            var cost = GetTransformCost(tall, tileX + x + half, tileY + y);
            if (cost < right)
            {
                right = cost;
                isRightMerged = true;
            }
        }

        var best = split;
        var choice = 0;
        if (top + bottom < best)
        {
            best = top + bottom;
            choice = 1;
        }

        if (left + right < best)
        {
            best = left + right;
            choice = 2;
        }

        if (fitsWidth && fitsHeight && whole[0] && whole[1] && whole[2] && whole[3])
        {
            var cost = GetTransformCost(full, tileX + x, tileY + y);
            if (cost < best)
            {
                best = cost;
                choice = 3;
            }
        }

        switch (choice)
        {
            case 1:
                if (isTopMerged)
                {
                    SetTransform(plan, wide, x, y);
                }

                if (isBottomMerged)
                {
                    SetTransform(plan, wide, x, y + half);
                }

                break;

            case 2:
                if (isLeftMerged)
                {
                    SetTransform(plan, tall, x, y);
                }

                if (isRightMerged)
                {
                    SetTransform(plan, tall, x + half, y);
                }

                break;

            case 3:
                SetTransform(plan, full, x, y);
                break;
        }

        return best;

        static void SetTransform(Span<byte> plan, int transform, int x, int y)
        {
            for (var by = 0; by < JpegXlVarDctTables.BlocksHigh[transform]; by++)
            {
                plan.Slice(((y + by) * ColorTileBlocks) + x, JpegXlVarDctTables.BlocksWide[transform]).Clear();
            }

            plan[(y * ColorTileBlocks) + x] = (byte)(transform + 1);
        }
    }

    /// <summary>
    /// Estimates what a transform costs for the luma of its blocks: the bits of its quantized coefficients, plus the
    /// squared error of the samples that the quantization leaves, in bits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A coefficient of every DCT of the format is the amplitude of a pattern over all the samples of the transform, so
    /// the squared error of the samples is the number of samples times the sum of the squared errors of the
    /// coefficients, whatever the size: transforms of different sizes are compared on the same error. The lowest
    /// frequencies of a transform of several blocks are in the LF image, which every choice codes.
    /// </para>
    /// <para>
    /// The size is a model fitted on frames coded with a single transform; it is within a few percent of their size.
    /// What names the transform matters as much: a block list that only holds the 8x8 DCT costs nothing, and each other
    /// transform about four bits. The weight of the error was chosen on photographs, for the size of the file at equal
    /// PSNR, SSIM and VMAF; only the luma is measured, which decides for the three channels.
    /// </para>
    /// </remarks>
    private float GetTransformCost(int transform, int x, int y)
    {
        var blocksWide = JpegXlVarDctTables.BlocksWide[transform];
        var blocksHigh = JpegXlVarDctTables.BlocksHigh[transform];
        var size = blocksWide * blocksHigh * 64;
        var coefficients = _candidate!.Span[..size];
        JpegXlForwardTransform.ForwardDct(_samples[1]!.Span[((y * 8 * GroupSize) + (x * 8))..], GroupSize, blocksHigh * 8, blocksWide * 8, coefficients, _work!.Span);

        var matrix = _matrices!.GetMatrix(JpegXlVarDctTables.QuantTable[transform]).Slice(size, size);
        var scale = (65536f / _globalScale) / NominalQuantField;
        var columns = Math.Max(blocksWide, blocksHigh) * 8;
        var rows = Math.Min(blocksWide, blocksHigh) * 8;
        var lowestRows = Math.Min(blocksWide, blocksHigh);
        var lowestColumns = Math.Max(blocksWide, blocksHigh);
        var ranks = GetRanks(JpegXlVarDctTables.Order[transform]);
        var error = 0f;
        var bits = 0f;
        var nonZeroCount = 0;
        var lastRank = 0;
        for (var row = 0; row < rows; row++)
        {
            var values = coefficients.Slice(row * columns, columns);
            var steps = matrix.Slice(row * columns, columns);
            var rowRanks = ranks.Slice(row * columns, columns);
            for (var column = row < lowestRows ? lowestColumns : 0; column < columns; column++)
            {
                var step = steps[column] * scale;
                var value = values[column];
                var quantized = Quantize(value / step);
                if (quantized == 0)
                {
                    error += value * value;
                }
                else
                {
                    var difference = value - (quantized * step);
                    error += difference * difference;
                    bits += NonZeroBits + (MagnitudeBits * BitOperations.Log2((uint)Math.Abs(quantized)));
                    nonZeroCount++;
                    lastRank = Math.Max(lastRank, rowRanks[column]);
                }
            }
        }

        if (nonZeroCount != 0)
        {
            // The zeros before the last coefficient that is not zero are coded too: the larger the transform, the less each costs
            var zeroBits = BitOperations.Log2((uint)size) switch
            {
                6 or 7 => ZeroBits8,
                8 => ZeroBits16,
                9 => (ZeroBits16 + ZeroBits32) / 2,
                10 => ZeroBits32,
                11 => (ZeroBits32 + ZeroBits64) / 2,
                _ => ZeroBits64,
            };
            bits += zeroBits * (lastRank + 1 - (blocksWide * blocksHigh) - nonZeroCount);
        }
        else
        {
            bits = EmptyTransformBits;
        }

        bits += transform == JpegXlVarDctTables.TransformDct8 ? TypeBits8 : TypeBits;
        var reference = _referenceStep * scale;
        return (error * size * (DistortionWeight / (reference * reference))) + bits;
    }

    /// <summary>Gets, for an order of coefficients, the rank of each position of a block.</summary>
    private ReadOnlySpan<int> GetRanks(int order)
    {
        var ranks = _ranks[order];
        if (ranks is null)
        {
            var positions = _orders!.GetOrder(0, order, 1);
            ranks = new int[positions.Length];
            for (var rank = 0; rank < positions.Length; rank++)
            {
                ranks[positions[rank]] = rank;
            }

            _ranks[order] = ranks;
        }

        return ranks;
    }

    /// <summary>
    /// Transforms and quantizes the blocks of a tile: the three channels of each transform, the LF value of each of its
    /// blocks, then the luma, the factors of the chroma from luma prediction of the tile, and the two other channels.
    /// </summary>
    private void EncodeTile(int groupOffset, int groupX, int groupY, int tileX, int tileY, int tileWidth, int tileHeight)
    {
        const int TileArea = ColorTileBlocks * ColorTileBlocks * 64;
        var blockInfo = _blockInfo!.Span;
        var quantField = _quantField!.Span;
        var offsets = _blockOffsets!.Span;
        var scratch = _work!.Span;
        var tileCoefficients = _tileCoefficients!.Span;
        var tileLuma = _tileLuma!.Span;
        var transformData = _image.TransformData;
        var biasY = transformData.QuantBias[1];
        var biasNumerator = transformData.QuantBiasNumerator;
        var inverseGlobalScale = 65536f / _globalScale;

        // The transforms of the tile, their coefficients one after the other
        var blocks = _tileBlocks.AsSpan();
        var count = 0;
        var next = 0;
        for (var y = 0; y < tileHeight; y++)
        {
            for (var x = 0; x < tileWidth; x++)
            {
                var info = blockInfo[((groupY + tileY + y) * _blocksWide) + groupX + tileX + x];
                if (info < BlockFirst)
                    continue;

                blocks[count++] = new TileBlock((byte)x, (byte)y, (byte)(info - BlockFirst), next);
                next += JpegXlVarDctTables.BlocksWide[info - BlockFirst] * JpegXlVarDctTables.BlocksHigh[info - BlockFirst] * 64;
            }
        }

        blocks = blocks[..count];

        // Y first: the two other channels are coded as their difference with a multiple of its dequantized values
        double sumXY = 0;
        double sumXYY = 0;
        double sumBY = 0;
        double sumBYY = 0;
        Span<float> lowFrequency = stackalloc float[3 * ColorTileBlocks * ColorTileBlocks];
        foreach (var block in blocks)
        {
            var blocksWide = JpegXlVarDctTables.BlocksWide[block.Transform];
            var blocksHigh = JpegXlVarDctTables.BlocksHigh[block.Transform];
            var blockCount = blocksWide * blocksHigh;
            var size = blockCount * 64;
            var x = tileX + block.X;
            var y = tileY + block.Y;
            var position = ((groupY + y) * _blocksWide) + groupX + x;
            for (var c = 0; c < 3; c++)
            {
                var coefficients = tileCoefficients.Slice((c * TileArea) + block.Offset, size);
                JpegXlForwardTransform.ForwardDct(_samples[c]!.Span[((y * 8 * GroupSize) + (x * 8))..], GroupSize, blocksHigh * 8, blocksWide * 8, coefficients, scratch);
                JpegXlForwardTransform.GetLowFrequencies(coefficients, blocksHigh, blocksWide, lowFrequency.Slice(c * blockCount, blockCount));
            }

            // The LF values: X is not predicted, and B is predicted from the dequantized Y with the factor 1
            for (var by = 0; by < blocksHigh; by++)
            {
                for (var bx = 0; bx < blocksWide; bx++)
                {
                    var index = (by * blocksWide) + bx;
                    var lfPosition = position + (by * _blocksWide) + bx;
                    var lfY = (int)MathF.Round(lowFrequency[blockCount + index] / _lfStep[1], MidpointRounding.AwayFromZero);
                    var dequantizedLfY = lfY * _lfStep[1];
                    _lf[1]!.Span[lfPosition] = lfY;
                    _lf[0]!.Span[lfPosition] = (int)MathF.Round(lowFrequency[index] / _lfStep[0], MidpointRounding.AwayFromZero);
                    _lf[2]!.Span[lfPosition] = (int)MathF.Round((lowFrequency[(2 * blockCount) + index] - dequantizedLfY) / _lfStep[2], MidpointRounding.AwayFromZero);
                }
            }

            var matrix = _matrices!.GetMatrix(JpegXlVarDctTables.QuantTable[block.Transform]);
            var scale = inverseGlobalScale / quantField[position];
            var source = tileCoefficients[block.Offset..];
            var quantizedY = _coefficients[1]!.Span.Slice(groupOffset + offsets[(y * GroupBlocks) + x], size);
            var luma = tileLuma.Slice(block.Offset, size);
            var columns = Math.Max(blocksWide, blocksHigh) * 8;
            var lowestLimit = Math.Min(blocksWide, blocksHigh) * columns;
            var lowestColumns = Math.Max(blocksWide, blocksHigh);
            for (var k = 0; k < size; k++)
            {
                // The lowest frequencies are in the LF image
                if (k < lowestLimit && k % columns < lowestColumns)
                {
                    quantizedY[k] = 0;
                    luma[k] = 0;
                    continue;
                }

                var stepY = matrix[size + k] * scale;
                var valueY = Quantize(source[TileArea + k] / stepY);
                quantizedY[k] = valueY;
                var dequantizedY = JpegXlVarDctDecoder.AdjustBias(valueY, biasY, biasNumerator) * stepY;
                luma[k] = dequantizedY;
                if (valueY != 0 && _effort >= ChromaFromLumaEffort)
                {
                    // Least squares, in quantization steps
                    var inverseStepX = 1.0 / (matrix[k] * (scale * _hfScale[0]));
                    var inverseStepB = 1.0 / (matrix[(2 * size) + k] * (scale * _hfScale[2]));
                    var weightedX = dequantizedY * inverseStepX;
                    var weightedB = dequantizedY * inverseStepB;
                    sumXY += weightedX * (source[k] * inverseStepX);
                    sumXYY += weightedX * weightedX;
                    sumBY += weightedB * ((source[(2 * TileArea) + k] - dequantizedY) * inverseStepB);
                    sumBYY += weightedB * weightedB;
                }
            }
        }

        // The factors of the tile, in units of 1/84: the decoder adds them to 0 for X and to 1 for B
        var factorX = GetChromaFactor(sumXY, sumXYY);
        var factorB = GetChromaFactor(sumBY, sumBYY);
        if (factorX != 0 && GetTileCost(blocks, 0, factorX, groupX + tileX, groupY + tileY, inverseGlobalScale) >= GetTileCost(blocks, 0, 0, groupX + tileX, groupY + tileY, inverseGlobalScale))
        {
            factorX = 0;
        }

        if (factorB != 0 && GetTileCost(blocks, 2, factorB, groupX + tileX, groupY + tileY, inverseGlobalScale) >= GetTileCost(blocks, 2, 0, groupX + tileX, groupY + tileY, inverseGlobalScale))
        {
            factorB = 0;
        }

        var tile = (((groupY + tileY) / ColorTileBlocks) * _tilesWide) + ((groupX + tileX) / ColorTileBlocks);
        _xFromY!.Span[tile] = factorX;
        _bFromY!.Span[tile] = factorB;
        var kx = factorX * ColorScale;
        var kb = 1f + (factorB * ColorScale);
        foreach (var block in blocks)
        {
            var blocksWide = JpegXlVarDctTables.BlocksWide[block.Transform];
            var blocksHigh = JpegXlVarDctTables.BlocksHigh[block.Transform];
            var size = blocksWide * blocksHigh * 64;
            var x = tileX + block.X;
            var y = tileY + block.Y;
            var matrix = _matrices!.GetMatrix(JpegXlVarDctTables.QuantTable[block.Transform]);
            var scale = inverseGlobalScale / quantField[((groupY + y) * _blocksWide) + groupX + x];
            var offset = groupOffset + offsets[(y * GroupBlocks) + x];
            var source = tileCoefficients[block.Offset..];
            var luma = tileLuma.Slice(block.Offset, size);
            var quantizedX = _coefficients[0]!.Span.Slice(offset, size);
            var quantizedB = _coefficients[2]!.Span.Slice(offset, size);
            var columns = Math.Max(blocksWide, blocksHigh) * 8;
            var lowestLimit = Math.Min(blocksWide, blocksHigh) * columns;
            var lowestColumns = Math.Max(blocksWide, blocksHigh);
            for (var k = 0; k < size; k++)
            {
                if (k < lowestLimit && k % columns < lowestColumns)
                {
                    quantizedX[k] = 0;
                    quantizedB[k] = 0;
                    continue;
                }

                quantizedX[k] = Quantize((source[k] - (kx * luma[k])) / (matrix[k] * (scale * _hfScale[0])));
                quantizedB[k] = Quantize((source[(2 * TileArea) + k] - (kb * luma[k])) / (matrix[(2 * size) + k] * (scale * _hfScale[2])));
            }
        }
    }

    /// <summary>
    /// Estimates the size of the coefficients of a channel (X or B) of a tile for a factor of the chroma from luma
    /// prediction: the sum, over the coefficients that are not zero, of two plus the number of bits of their magnitude.
    /// </summary>
    private long GetTileCost(ReadOnlySpan<TileBlock> blocks, int channel, int factor, int tileBlockX, int tileBlockY, float inverseGlobalScale)
    {
        const int TileArea = ColorTileBlocks * ColorTileBlocks * 64;
        var tileCoefficients = _tileCoefficients!.Span;
        var tileLuma = _tileLuma!.Span;
        var quantField = _quantField!.Span;
        var k0 = (channel == 0 ? 0f : 1f) + (factor * ColorScale);
        long cost = 0;
        foreach (var block in blocks)
        {
            var blocksWide = JpegXlVarDctTables.BlocksWide[block.Transform];
            var blocksHigh = JpegXlVarDctTables.BlocksHigh[block.Transform];
            var size = blocksWide * blocksHigh * 64;
            var scale = inverseGlobalScale / quantField[((tileBlockY + block.Y) * _blocksWide) + tileBlockX + block.X] * _hfScale[channel];
            var coefficients = tileCoefficients.Slice((channel * TileArea) + block.Offset, size);
            var luma = tileLuma.Slice(block.Offset, size);
            var matrix = _matrices!.GetMatrix(JpegXlVarDctTables.QuantTable[block.Transform]).Slice(channel * size, size);
            var columns = Math.Max(blocksWide, blocksHigh) * 8;
            var lowestLimit = Math.Min(blocksWide, blocksHigh) * columns;
            var lowestColumns = Math.Max(blocksWide, blocksHigh);
            for (var k = 0; k < size; k++)
            {
                if (k < lowestLimit && k % columns < lowestColumns)
                    continue;

                var value = Quantize((coefficients[k] - (k0 * luma[k])) / (matrix[k] * scale));
                if (value != 0)
                {
                    cost += 2 + BitOperations.Log2((uint)Math.Abs(value));
                }
            }
        }

        return cost;
    }

    private static sbyte GetChromaFactor(double sumXY, double sumYY)
    {
        if (sumYY <= 0)
            return 0;

        return (sbyte)Math.Clamp(Math.Round(sumXY / sumYY / ColorScale, MidpointRounding.AwayFromZero), sbyte.MinValue, sbyte.MaxValue);
    }

    /// <summary>Rounds a coefficient expressed in quantization steps: to the nearest integer, and to zero below a threshold above one half.</summary>
    private static int Quantize(float value)
    {
        const float ZeroThreshold = 0.58f;
        var magnitude = MathF.Abs(value);
        if (magnitude < ZeroThreshold)
            return 0;

        var rounded = (int)(magnitude + 0.5f);
        return value < 0 ? -rounded : rounded;
    }

    /// <summary>
    /// Converts a rectangle of the frame to XYB; the samples outside the frame repeat its last column and its last row.
    /// When the decoder smooths the frame (the Gabor-like filter), the samples are sharpened so that it gives them back.
    /// </summary>
    private void LoadSamples(scoped in PixelLease lease, int x0, int y0, int width, int height)
    {
        var available = Math.Min(width, _width - x0);
        var rows = Math.Min(height, _height - y0);
        var planeX = _samples[0]!.Span;
        var planeY = _samples[1]!.Span;
        var planeB = _samples[2]!.Span;
        if (_gaborish)
        {
            // The rectangle and what the filter reads around it: the samples of the frame, which is mirrored at its edges
            // as the decoder mirrors it
            const int Radius = JpegXlInverseGaborish.Radius;
            const int Stride = GroupSize + (2 * Radius);
            var paddedX = _padded[0]!.Span;
            var paddedY = _padded[1]!.Span;
            var paddedB = _padded[2]!.Span;
            var first = Math.Max(0, x0 - Radius);
            var last = Math.Min(_width, x0 + available + Radius);
            var origin = x0 - Radius;
            for (var y = -Radius; y < rows + Radius; y++)
            {
                var rowX = paddedX.Slice((y + Radius) * Stride, available + (2 * Radius));
                var rowY = paddedY.Slice((y + Radius) * Stride, available + (2 * Radius));
                var rowB = paddedB.Slice((y + Radius) * Stride, available + (2 * Radius));
                var source = lease.GetRowBytes(Mirror(y0 + y, _height));
                _color!.Convert(source, _format, first, rowX.Slice(first - origin, last - first), rowY.Slice(first - origin, last - first), rowB.Slice(first - origin, last - first));
                for (var x = origin; x < first; x++)
                {
                    var mirrored = Mirror(x, _width) - origin;
                    rowX[x - origin] = rowX[mirrored];
                    rowY[x - origin] = rowY[mirrored];
                    rowB[x - origin] = rowB[mirrored];
                }

                for (var x = last; x < x0 + available + Radius; x++)
                {
                    var mirrored = Mirror(x, _width) - origin;
                    rowX[x - origin] = rowX[mirrored];
                    rowY[x - origin] = rowY[mirrored];
                    rowB[x - origin] = rowB[mirrored];
                }
            }

            var weights = _gaborishWeights.AsSpan();
            JpegXlInverseGaborish.Apply(paddedX, Stride, planeX, GroupSize, available, rows, weights[..JpegXlInverseGaborish.WeightCount]);
            JpegXlInverseGaborish.Apply(paddedY, Stride, planeY, GroupSize, available, rows, weights.Slice(JpegXlInverseGaborish.WeightCount, JpegXlInverseGaborish.WeightCount));
            JpegXlInverseGaborish.Apply(paddedB, Stride, planeB, GroupSize, available, rows, weights.Slice(2 * JpegXlInverseGaborish.WeightCount, JpegXlInverseGaborish.WeightCount));
        }

        for (var y = 0; y < height; y++)
        {
            var rowX = planeX.Slice(y * GroupSize, width);
            var rowY = planeY.Slice(y * GroupSize, width);
            var rowB = planeB.Slice(y * GroupSize, width);
            if (y >= rows)
            {
                planeX.Slice((y - 1) * GroupSize, width).CopyTo(rowX);
                planeY.Slice((y - 1) * GroupSize, width).CopyTo(rowY);
                planeB.Slice((y - 1) * GroupSize, width).CopyTo(rowB);
                continue;
            }

            if (!_gaborish)
            {
                _color!.Convert(lease.GetRowBytes(y0 + y), _format, x0, rowX[..available], rowY[..available], rowB[..available]);
            }

            rowX[available..].Fill(rowX[available - 1]);
            rowY[available..].Fill(rowY[available - 1]);
            rowB[available..].Fill(rowB[available - 1]);
        }
    }

    /// <summary>The position inside a side of a position that may be outside: the side is mirrored at its two ends, which are repeated.</summary>
    private static int Mirror(int position, int size)
    {
        while (position < 0 || position >= size)
        {
            position = position < 0 ? -position - 1 : (2 * size) - 1 - position;
        }

        return position;
    }

    private void WriteSections(scoped in PixelLease lease, LsbBitWriter writer, List<int> sizes, ExtraChannelWriter? writeExtraChannels)
    {
        var isSingleSection = _groupCount == 1 && _passCount == 1;
        var total = writer.Length;
        var extraChannelsAreGlobal = _width <= GroupSize && _height <= GroupSize;

        // LfGlobal: the LF dequantization, the quantizer, the default block contexts and the chroma from luma parameters
        if (_jpegQuantization is { } tables)
        {
            // The LF value of a block is its DC coefficient: its step is the one of the file, in units of the samples (a DC
            // coefficient of JPEG is 8 times the mean of its block, whose samples go up to 255)
            writer.WriteBool(false);
            for (var c = 0; c < 3; c++)
            {
                writer.WriteF16(tables[c][0] * (128f / (8 * 255)));
            }
        }
        else
        {
            writer.WriteBool(true);
        }

        writer.WriteU32((uint)_globalScale, 1, 11, 2049, 11, 4097, 12, 8193, 16);
        writer.WriteU32((uint)_quantLf, 16, 0, 1, 5, 1, 8, 1, 16);
        writer.WriteBool(true);
        if (_jpegQuantization is not null)
        {
            // Nothing predicts the chroma from the luma: the default unit of the factors, base factors of 0 (the default
            // predicts B from Y), and LF factors of 0
            writer.WriteBool(false);
            writer.WriteU32(84, 84, 0, 256, 0, 2, 8, 258, 16);
            writer.WriteF16(0);
            writer.WriteF16(0);
            writer.WriteBits(128, 8);
            writer.WriteBits(128, 8);
        }
        else
        {
            writer.WriteBool(true);
        }

        if (writeExtraChannels is null || extraChannelsAreGlobal)
        {
            // No global tree: the global Modular data, if any, hold their own
            writer.WriteBool(false);
            writeExtraChannels?.Invoke(writer, lease, 0, 0, _width, _height);
        }
        else
        {
            // Every Modular channel is larger than a group and coded in the groups, each with its own tree; the global Modular
            // data use a global tree of one leaf, so that every decoder agrees on what they hold
            writer.WriteBool(true);
            JpegXlModularEncoder.WriteUnusedGlobalTree(writer, _scope);
            writer.WriteBool(true);
            writer.WriteBool(true);
            writer.WriteU32(0, 0, 0, 1, 0, 2, 4, 18, 8);
        }

        EndSection();
        for (var lfGroup = 0; lfGroup < _lfGroupCount; lfGroup++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            WriteLfGroup(writer, lfGroup);
            EndSection();
        }

        // HfGlobal: the dequantization matrices, one preset, then for each pass the natural orders and the entropy code
        if (_jpegQuantization is { } quantization)
        {
            // The table of the 8x8 DCT is stored by its values, as an image of three channels; the inverse of a value is
            // divided by the number that brings a coefficient of JPEG to the unit of the samples. The other tables are
            // the ones of the standard: no block uses them
            writer.WriteBool(false);
            writer.WriteBits(7, 3);
            writer.WriteF16(1f / (8 * 255));
            using (var image = new JpegXlModularImage(8))
            {
                for (var c = 0; c < 3; c++)
                {
                    image.AddChannel(8, 8, 0, 0);
                    image.Channels[c].Allocate(_scope);
                    for (var k = 0; k < 64; k++)
                    {
                        image.Channels[c].Samples[k] = quantization[c][k];
                    }
                }

                JpegXlModularEncoder.Encode(writer, image, colorChannelCount: 0, _effort, _scope, _cancellationToken);
            }

            for (var i = 1; i < JpegXlVarDctTables.QuantTableCount; i++)
            {
                writer.WriteBits(0, 3);
            }
        }
        else
        {
            writer.WriteBool(true);
        }

        writer.WriteBits(0, JpegXlHybridUintConfig.CeilingLog2(_groupCount));
        var builders = new JpegXlEntropyCodeBuilder?[_passCount];
        try
        {
            // The contexts are clustered in one pass, in their order: a cluster that is not available when a context needs it
            // makes it share a distribution that does not fit. Fewer than 64 clusters cost several times the size of the file
            // for a few percent of the time
            var maxClusters = _effort < 6 ? 64 : JpegXlEntropyCode.MaxClusters;
            for (var pass = 0; pass < _passCount; pass++)
            {
                var builder = builders[pass] = new JpegXlEntropyCodeBuilder(JpegXlVarDctTables.ContextsPerBlockContext * BlockContextCount, CoefficientConfig, maxClusters, useAns: true, _scope);
                for (var group = 0; group < _groupCount; group++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    AddGroupCoefficients(builder, group, pass, count: true);
                }

                builder.Build();
                writer.WriteU32(0, 0x5F, 0, 0x13, 0, 0, 0, 0, 13);
                builder.WriteDescription(writer);
            }

            EndSection();
            for (var pass = 0; pass < _passCount; pass++)
            {
                var builder = builders[pass]!;
                for (var group = 0; group < _groupCount; group++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    builder.BeginStream();
                    AddGroupCoefficients(builder, group, pass, count: false);
                    builder.WriteStream(writer);
                    if (writeExtraChannels is not null && !extraChannelsAreGlobal && pass == _passCount - 1)
                    {
                        var x = group % _groupColumns * GroupSize;
                        var y = group / _groupColumns * GroupSize;
                        writeExtraChannels(writer, lease, x, y, Math.Min(GroupSize, _width - x), Math.Min(GroupSize, _height - y));
                    }

                    EndSection();
                }
            }
        }
        finally
        {
            foreach (var builder in builders)
            {
                builder?.Dispose();
            }
        }

        if (isSingleSection)
        {
            writer.ZeroPadToByte();
            sizes.Add(writer.Length - total);
        }

        void EndSection()
        {
            // A frame of a single section is one bit stream: its parts follow each other without alignment
            if (isSingleSection)
                return;

            writer.ZeroPadToByte();
            sizes.Add(writer.Length - total);
            total = writer.Length;
        }
    }

    /// <summary>
    /// Writes an LF group: the LF values of its blocks, as a Modular image of three channels (Y, X, B); then the factors of
    /// the chroma from luma prediction of its tiles, the transform and the quantization factor of each block that starts a
    /// transform, and the sharpness of each block, as a Modular image of four channels.
    /// </summary>
    private void WriteLfGroup(LsbBitWriter writer, int lfGroup)
    {
        var dimension = (int)_frame.GroupDimension;
        var x0 = (int)(lfGroup % _frame.LfGroupColumns) * dimension;
        var y0 = (int)(lfGroup / _frame.LfGroupColumns) * dimension;
        var width = Math.Min(dimension, _blocksWide - x0);
        var height = Math.Min(dimension, _blocksHigh - y0);

        // No extra precision
        writer.WriteBits(0, 2);
        using (var image = new JpegXlModularImage(_image.BitDepth.BitsPerSample))
        {
            ReadOnlySpan<int> order = [1, 0, 2];
            for (var i = 0; i < 3; i++)
            {
                var hs = _horizontalShift[order[i]];
                var vs = _verticalShift[order[i]];
                image.AddChannel(width >> hs, height >> vs, 0, 0);
                image.Channels[i].Allocate(_scope);
                var source = _lf[order[i]]!.Span;
                for (var y = 0; y < height >> vs; y++)
                {
                    source.Slice((((y0 >> vs) + y) * (_blocksWide >> hs)) + (x0 >> hs), width >> hs).CopyTo(image.Channels[i].GetRow(y));
                }
            }

            JpegXlModularEncoder.Encode(writer, image, colorChannelCount: 0, _effort, _scope, _cancellationToken);
        }

        var blockInfo = _blockInfo!.Span;
        var quantField = _quantField!.Span;
        var transformCount = 0;
        for (var y = 0; y < height; y++)
        {
            foreach (var info in blockInfo.Slice(((y0 + y) * _blocksWide) + x0, width))
            {
                transformCount += info >= BlockFirst ? 1 : 0;
            }
        }

        writer.WriteBits((uint)(transformCount - 1), JpegXlHybridUintConfig.CeilingLog2(width * height));
        var tileX = x0 / ColorTileBlocks;
        var tileY = y0 / ColorTileBlocks;
        var tileWidth = (width + ColorTileBlocks - 1) / ColorTileBlocks;
        var tileHeight = (height + ColorTileBlocks - 1) / ColorTileBlocks;
        using var metadata = new JpegXlModularImage(_image.BitDepth.BitsPerSample);
        metadata.AddChannel(tileWidth, tileHeight, 3, 3);
        metadata.AddChannel(tileWidth, tileHeight, 3, 3);
        metadata.AddChannel(transformCount, 2, 0, 0);
        metadata.AddChannel(width, height, 0, 0);
        foreach (var channel in metadata.Channels)
        {
            channel.Allocate(_scope);
        }

        for (var y = 0; y < tileHeight; y++)
        {
            var targetX = metadata.Channels[0].GetRow(y);
            var targetB = metadata.Channels[1].GetRow(y);
            var sourceX = _xFromY!.Span.Slice(((tileY + y) * _tilesWide) + tileX, tileWidth);
            var sourceB = _bFromY!.Span.Slice(((tileY + y) * _tilesWide) + tileX, tileWidth);
            for (var x = 0; x < tileWidth; x++)
            {
                targetX[x] = sourceX[x];
                targetB[x] = sourceB[x];
            }
        }

        // The transforms, in the order of their first block
        var transforms = metadata.Channels[2].GetRow(0);
        var factors = metadata.Channels[2].GetRow(1);
        var next = 0;
        for (var y = 0; y < height; y++)
        {
            metadata.Channels[3].GetRow(y).Fill(_jpegQuantization is null ? Sharpness : 0);
            for (var x = 0; x < width; x++)
            {
                var position = ((y0 + y) * _blocksWide) + x0 + x;
                if (blockInfo[position] < BlockFirst)
                    continue;

                transforms[next] = blockInfo[position] - BlockFirst;
                factors[next] = quantField[position] - 1;
                next++;
            }
        }

        // The block list holds the transforms on its first row and the quantization factors on its second one
        JpegXlModularEncoder.Encode(writer, metadata, colorChannelCount: 0, _effort, _scope, _cancellationToken, firstRowChannel: 2);
    }

    /// <summary>
    /// Gives the values that a pass codes for a group to the entropy coder, with their contexts: for each block and each
    /// channel (Y, X, B), the number of non-zero coefficients, then the coefficients up to the last one that is not zero.
    /// This is what <see cref="JpegXlVarDctDecoder.ReadGroupPass"/> reads.
    /// </summary>
    private void AddGroupCoefficients(JpegXlEntropyCodeBuilder builder, int group, int pass, bool count)
    {
        GetGroupRectangle(group, out var groupX, out var groupY, out var groupWidth, out var groupHeight);
        var blockInfo = _blockInfo!.Span;
        var offsets = _blockOffsets!.Span;
        var nonZeros = _nonZeros!.Span;
        var groupOffset = _groupOffsets!.Span[group];
        var orders = _orders!;
        var blockContextMap = JpegXlVarDctTables.DefaultBlockContextMap;

        // The coefficients of the transforms follow each other in the order of their first block
        var next = 0;
        for (var y = 0; y < groupHeight; y++)
        {
            var row = blockInfo.Slice(((groupY + y) * _blocksWide) + groupX, groupWidth);
            for (var x = 0; x < groupWidth; x++)
            {
                if (row[x] < BlockFirst)
                    continue;

                var transform = row[x] - BlockFirst;
                offsets[(y * GroupBlocks) + x] = next;
                next += JpegXlVarDctTables.BlocksWide[transform] * JpegXlVarDctTables.BlocksHigh[transform] * 64;
            }
        }

        ReadOnlySpan<int> channelOrder = [1, 0, 2];
        for (var y = 0; y < groupHeight; y++)
        {
            for (var x = 0; x < groupWidth; x++)
            {
                var position = ((groupY + y) * _blocksWide) + groupX + x;
                if (blockInfo[position] < BlockFirst)
                    continue;

                var transform = blockInfo[position] - BlockFirst;
                var blocksWide = JpegXlVarDctTables.BlocksWide[transform];
                var blocksHigh = JpegXlVarDctTables.BlocksHigh[transform];
                var blockCount = blocksWide * blocksHigh;
                var logBlockCount = BitOperations.Log2((uint)blockCount);
                var size = blockCount * 64;
                var order = JpegXlVarDctTables.Order[transform];
                var columns = Math.Max(blocksWide, blocksHigh) * 8;
                var rows = Math.Min(blocksWide, blocksHigh) * 8;
                foreach (var c in channelOrder)
                {
                    // A subsampled channel has a block where its samples start
                    var channelX = x >> _horizontalShift[c];
                    var channelY = y >> _verticalShift[c];
                    if ((channelX << _horizontalShift[c]) != x || (channelY << _verticalShift[c]) != y)
                        continue;

                    var blockContext = blockContextMap[JpegXlCoefficientContexts.GetBlockContextIndex(c, order, 1, 0, 1, 0)];
                    var channelNonZeros = nonZeros.Slice(c * GroupBlocks * GroupBlocks, GroupBlocks * GroupBlocks);
                    var predicted = JpegXlCoefficientContexts.PredictNonZeros(channelNonZeros, channelX, channelY);
                    var block = _coefficients[c]!.Span.Slice(groupOffset + offsets[(y * GroupBlocks) + x], size);
                    var coefficientOrder = orders.GetOrder(0, order, c);
                    var nonZeroCount = 0;
                    for (var k = blockCount; k < size; k++)
                    {
                        var index = coefficientOrder[k];
                        nonZeroCount += block[index] != 0 && GetPass(index, rows, columns) == pass ? 1 : 0;
                    }

                    Add(JpegXlCoefficientContexts.GetNonZeroContext(predicted, BlockContextCount, blockContext), (uint)nonZeroCount);
                    var stored = JpegXlCoefficientContexts.GetStoredNonZeros(nonZeroCount, blockCount, logBlockCount);
                    for (var by = 0; by < blocksHigh; by++)
                    {
                        channelNonZeros.Slice((channelY * GroupBlocks) + channelX + (by * GroupBlocks), blocksWide).Fill(stored);
                    }

                    if (nonZeroCount == 0)
                        continue;

                    var coefficientContext = JpegXlCoefficientContexts.GetCoefficientContextOffset(BlockContextCount, blockContext);
                    var previous = JpegXlCoefficientContexts.GetInitialPrevious(nonZeroCount, size);
                    for (var k = blockCount; k < size && nonZeroCount != 0; k++)
                    {
                        var index = coefficientOrder[k];
                        var value = GetPass(index, rows, columns) == pass ? block[index] : 0;
                        var context = coefficientContext + JpegXlCoefficientContexts.GetCoefficientContext(nonZeroCount, k, blockCount, logBlockCount, previous);
                        Add(context, (uint)((value << 1) ^ (value >> 31)));
                        previous = value != 0 ? 1 : 0;
                        nonZeroCount -= previous;
                    }
                }
            }
        }

        void Add(int context, uint value)
        {
            if (count)
            {
                builder.Count(context, value);
            }
            else
            {
                builder.Add(context, value);
            }
        }
    }

    /// <summary>
    /// Gets the pass that codes a coefficient: with three passes, the first one holds the lowest quarter of the frequencies
    /// of each direction, the second one the rest of the lowest half, the last one the others.
    /// </summary>
    /// <param name="index">The position of the coefficient in its block, whose rows are the smaller side of the transform.</param>
    /// <param name="rows">The number of rows of the block.</param>
    /// <param name="columns">The number of columns of the block.</param>
    private int GetPass(int index, int rows, int columns)
    {
        if (_passCount == 1)
            return 0;

        var row = index / columns;
        var column = index - (row * columns);
        if (row < rows / 4 && column < columns / 4)
            return 0;

        return row < rows / 2 && column < columns / 2 ? 1 : 2;
    }
}
