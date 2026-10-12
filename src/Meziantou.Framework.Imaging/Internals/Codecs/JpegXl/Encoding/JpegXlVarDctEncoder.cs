using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes the three color channels of a frame with VarDCT, in the XYB color space (ISO/IEC 18181-1, annex I and sections
/// C.4 to C.9, in the direction the standard does not describe): the mirror of <see cref="JpegXlVarDctDecoder"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Analysis, group by group: the samples of the group are converted to XYB, each block is transformed and quantized, and
/// only the quantized values are kept: one value per coefficient, and the LF value of each block. The blocks of a frame do
/// not predict each other, so the encoder does not reconstruct what it writes.
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

    private const byte BlockFirst = 2;

    /// <summary>The quantization factor of a block that is neither coarser nor finer than the frame.</summary>
    private const int NominalQuantField = 32;

    /// <summary>The sharpness of the edge-preserving filter of every block: the middle of its range.</summary>
    private const int Sharpness = 4;

    /// <summary>The effort from which each tile has its own factors of the chroma from luma prediction.</summary>
    private const int ChromaFromLumaEffort = 3;

    /// <summary>The unit of the factors of the chroma from luma prediction: the default value of the frame.</summary>
    private const float ColorScale = 1f / 84;

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

        var distance = GetDistance(quality);
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
    }

    /// <summary>Writes the Modular channels of a rectangle of the image that are not color channels (the alpha channel), as one sub-bitstream.</summary>
    public delegate void ExtraChannelWriter(LsbBitWriter writer, scoped in PixelLease lease, int x, int y, int width, int height);

    /// <summary>The scale of the quantization of the coefficients at distance 1, where a difference is barely visible.</summary>
    private static float HfScaleAtDistance1 => 0.8f;

    /// <summary>The scale of the quantization of the LF image at distance 1.</summary>
    private static float LfScaleAtDistance1 => 1.0f;

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
        }

        _work = Av1PooledArray<float>.Rent(_scope, (long)JpegXlForwardTransform.MaxSize * JpegXlForwardTransform.MaxSize, clear: false);
        _tileCoefficients = Av1PooledArray<float>.Rent(_scope, 3L * ColorTileBlocks * ColorTileBlocks * 64, clear: false);
        _tileLuma = Av1PooledArray<float>.Rent(_scope, (long)ColorTileBlocks * ColorTileBlocks * 64, clear: false);
        _matrices = JpegXlDequantMatrices.CreateDefault(_scope);
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

    /// <summary>Converts the samples of a group to XYB, then transforms and quantizes each of its blocks, tile by tile.</summary>
    private void AnalyzeGroup(scoped in PixelLease lease, int group)
    {
        GetGroupRectangle(group, out var groupX, out var groupY, out var groupWidth, out var groupHeight);
        LoadSamples(lease, groupX * 8, groupY * 8, groupWidth * 8, groupHeight * 8);

        var blockInfo = _blockInfo!.Span;
        var quantField = _quantField!.Span;
        var scratch = _work!.Span;
        var tileCoefficients = _tileCoefficients!.Span;
        var tileLuma = _tileLuma!.Span;
        var matrix = _matrices!.GetMatrix(JpegXlVarDctTables.QuantTable[JpegXlVarDctTables.TransformDct8]);
        var transformData = _image.TransformData;
        var biasY = transformData.QuantBias[1];
        var biasNumerator = transformData.QuantBiasNumerator;
        var groupOffset = _groupOffsets!.Span[group];
        var inverseGlobalScale = 65536f / _globalScale;
        for (var tileY = 0; tileY < groupHeight; tileY += ColorTileBlocks)
        {
            for (var tileX = 0; tileX < groupWidth; tileX += ColorTileBlocks)
            {
                var tileWidth = Math.Min(ColorTileBlocks, groupWidth - tileX);
                var tileHeight = Math.Min(ColorTileBlocks, groupHeight - tileY);

                // Y first: the two other channels are coded as their difference with a multiple of its dequantized values
                double sumXY = 0;
                double sumXYY = 0;
                double sumBY = 0;
                double sumBYY = 0;
                for (var y = 0; y < tileHeight; y++)
                {
                    for (var x = 0; x < tileWidth; x++)
                    {
                        var blockX = groupX + tileX + x;
                        var blockY = groupY + tileY + y;
                        var position = (blockY * _blocksWide) + blockX;
                        blockInfo[position] = BlockFirst + JpegXlVarDctTables.TransformDct8;
                        quantField[position] = NominalQuantField;
                        var coefficients = tileCoefficients.Slice(((y * ColorTileBlocks) + x) * 3 * 64, 3 * 64);
                        for (var c = 0; c < 3; c++)
                        {
                            JpegXlForwardTransform.ForwardDct(_samples[c]!.Span[((((tileY + y) * 8) * GroupSize) + ((tileX + x) * 8))..], GroupSize, 8, 8, coefficients.Slice(c * 64, 64), scratch);
                        }

                        // The LF values: X is not predicted, and B is predicted from the dequantized Y with the factor 1
                        var lfY = (int)MathF.Round(coefficients[64] / _lfStep[1], MidpointRounding.AwayFromZero);
                        var dequantizedLfY = lfY * _lfStep[1];
                        _lf[1]!.Span[position] = lfY;
                        _lf[0]!.Span[position] = (int)MathF.Round(coefficients[0] / _lfStep[0], MidpointRounding.AwayFromZero);
                        _lf[2]!.Span[position] = (int)MathF.Round((coefficients[128] - dequantizedLfY) / _lfStep[2], MidpointRounding.AwayFromZero);

                        var scale = inverseGlobalScale / quantField[position];
                        var offset = groupOffset + ((((tileY + y) * groupWidth) + tileX + x) * 64);
                        var quantizedY = _coefficients[1]!.Span.Slice(offset, 64);
                        var luma = tileLuma.Slice(((y * ColorTileBlocks) + x) * 64, 64);
                        quantizedY[0] = 0;
                        luma[0] = 0;
                        for (var k = 1; k < 64; k++)
                        {
                            var stepY = matrix[64 + k] * scale;
                            var valueY = Quantize(coefficients[64 + k] / stepY);
                            quantizedY[k] = valueY;
                            var dequantizedY = JpegXlVarDctDecoder.AdjustBias(valueY, biasY, biasNumerator) * stepY;
                            luma[k] = dequantizedY;
                            if (valueY != 0 && _effort >= ChromaFromLumaEffort)
                            {
                                // Least squares, in quantization steps
                                var inverseStepX = 1.0 / (matrix[k] * (scale * _hfScale[0]));
                                var inverseStepB = 1.0 / (matrix[128 + k] * (scale * _hfScale[2]));
                                var weightedX = dequantizedY * inverseStepX;
                                var weightedB = dequantizedY * inverseStepB;
                                sumXY += weightedX * (coefficients[k] * inverseStepX);
                                sumXYY += weightedX * weightedX;
                                sumBY += weightedB * ((coefficients[128 + k] - dequantizedY) * inverseStepB);
                                sumBYY += weightedB * weightedB;
                            }
                        }
                    }
                }

                // The factors of the tile, in units of 1/84: the decoder adds them to 0 for X and to 1 for B
                var factorX = GetChromaFactor(sumXY, sumXYY);
                var factorB = GetChromaFactor(sumBY, sumBYY);
                if (factorX != 0 && GetTileCost(0, factorX, tileWidth, tileHeight, groupX + tileX, groupY + tileY, matrix, inverseGlobalScale) >= GetTileCost(0, 0, tileWidth, tileHeight, groupX + tileX, groupY + tileY, matrix, inverseGlobalScale))
                {
                    factorX = 0;
                }

                if (factorB != 0 && GetTileCost(2, factorB, tileWidth, tileHeight, groupX + tileX, groupY + tileY, matrix, inverseGlobalScale) >= GetTileCost(2, 0, tileWidth, tileHeight, groupX + tileX, groupY + tileY, matrix, inverseGlobalScale))
                {
                    factorB = 0;
                }

                var tile = (((groupY + tileY) / ColorTileBlocks) * _tilesWide) + ((groupX + tileX) / ColorTileBlocks);
                _xFromY!.Span[tile] = factorX;
                _bFromY!.Span[tile] = factorB;
                var kx = factorX * ColorScale;
                var kb = 1f + (factorB * ColorScale);
                for (var y = 0; y < tileHeight; y++)
                {
                    for (var x = 0; x < tileWidth; x++)
                    {
                        var position = ((groupY + tileY + y) * _blocksWide) + groupX + tileX + x;
                        var scale = inverseGlobalScale / quantField[position];
                        var offset = groupOffset + ((((tileY + y) * groupWidth) + tileX + x) * 64);
                        var coefficients = tileCoefficients.Slice(((y * ColorTileBlocks) + x) * 3 * 64, 3 * 64);
                        var luma = tileLuma.Slice(((y * ColorTileBlocks) + x) * 64, 64);
                        var quantizedX = _coefficients[0]!.Span.Slice(offset, 64);
                        var quantizedB = _coefficients[2]!.Span.Slice(offset, 64);
                        quantizedX[0] = 0;
                        quantizedB[0] = 0;
                        for (var k = 1; k < 64; k++)
                        {
                            quantizedX[k] = Quantize((coefficients[k] - (kx * luma[k])) / (matrix[k] * (scale * _hfScale[0])));
                            quantizedB[k] = Quantize((coefficients[128 + k] - (kb * luma[k])) / (matrix[128 + k] * (scale * _hfScale[2])));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Estimates the size of the coefficients of a channel (X or B) of a tile for a factor of the chroma from luma
    /// prediction: the sum, over the coefficients that are not zero, of one plus the number of bits of their magnitude.
    /// </summary>
    private long GetTileCost(int channel, int factor, int tileWidth, int tileHeight, int blockX, int blockY, ReadOnlySpan<float> matrix, float inverseGlobalScale)
    {
        var tileCoefficients = _tileCoefficients!.Span;
        var tileLuma = _tileLuma!.Span;
        var quantField = _quantField!.Span;
        var k0 = (channel == 0 ? 0f : 1f) + (factor * ColorScale);
        long cost = 0;
        for (var y = 0; y < tileHeight; y++)
        {
            for (var x = 0; x < tileWidth; x++)
            {
                var scale = inverseGlobalScale / quantField[((blockY + y) * _blocksWide) + blockX + x] * _hfScale[channel];
                var coefficients = tileCoefficients.Slice(((((y * ColorTileBlocks) + x) * 3) + channel) * 64, 64);
                var luma = tileLuma.Slice(((y * ColorTileBlocks) + x) * 64, 64);
                var channelMatrix = matrix.Slice(channel * 64, 64);
                for (var k = 1; k < 64; k++)
                {
                    var value = Quantize((coefficients[k] - (k0 * luma[k])) / (channelMatrix[k] * scale));
                    if (value != 0)
                    {
                        cost += 2 + BitOperations.Log2((uint)Math.Abs(value));
                    }
                }
            }
        }

        return cost;
    }

    /// <summary>Gets the factor that minimizes a sum of squares, in units of 1/84, as the signed byte the frame stores.</summary>
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

    /// <summary>Converts a rectangle of the frame to XYB; the samples outside the frame repeat its last column and its last row.</summary>
    private void LoadSamples(scoped in PixelLease lease, int x0, int y0, int width, int height)
    {
        var available = Math.Min(width, _width - x0);
        var planeX = _samples[0]!.Span;
        var planeY = _samples[1]!.Span;
        var planeB = _samples[2]!.Span;
        for (var y = 0; y < height; y++)
        {
            var rowX = planeX.Slice(y * GroupSize, width);
            var rowY = planeY.Slice(y * GroupSize, width);
            var rowB = planeB.Slice(y * GroupSize, width);
            if (y0 + y >= _height)
            {
                planeX.Slice((y - 1) * GroupSize, width).CopyTo(rowX);
                planeY.Slice((y - 1) * GroupSize, width).CopyTo(rowY);
                planeB.Slice((y - 1) * GroupSize, width).CopyTo(rowB);
                continue;
            }

            _color!.Convert(lease.GetRowBytes(y0 + y), _format, x0, rowX[..available], rowY[..available], rowB[..available]);
            rowX[available..].Fill(rowX[available - 1]);
            rowY[available..].Fill(rowY[available - 1]);
            rowB[available..].Fill(rowB[available - 1]);
        }
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
            var maxClusters = _effort switch
            {
                < 3 => 16,
                < 6 => 64,
                _ => JpegXlEntropyCode.MaxClusters,
            };
            for (var pass = 0; pass < _passCount; pass++)
            {
                var builder = builders[pass] = new JpegXlEntropyCodeBuilder(JpegXlVarDctTables.ContextsPerBlockContext * BlockContextCount, CoefficientConfig, maxClusters, useAns: _effort >= 1, _scope);
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

        JpegXlModularEncoder.Encode(writer, metadata, colorChannelCount: 0, _effort, _scope, _cancellationToken);
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
