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
/// The quantization field first, over the whole frame (<see cref="ComputeQuantField"/>): from the effort 3 and below the
/// distance 1.75, each block has its own quantization factor, finer where an error shows
/// (<see cref="JpegXlAdaptiveQuantization"/>), blended with the factor of the frame (<see cref="GetFieldStrength"/>), and
/// the scale of the quantization of the frame is the one that suits the field. The samples are converted to XYB a first
/// time for it. Otherwise, every block has the same factor.
/// </description></item>
/// <item><description>
/// Analysis, group by group: the samples of the group are converted to XYB; the transform of each area is chosen, from
/// the 8x8 DCT to the 64x64 one
/// by the effort, by what it costs for the luma (<see cref="GetTransformCost"/>), and has one factor, from the field of
/// its blocks; then each transform is applied and quantized, and only the quantized values are kept: one value per
/// coefficient, and the LF value of each block. The blocks of a frame do not predict each other, so the encoder does
/// not reconstruct what it writes.
/// </description></item>
/// <item><description>
/// The channels are X, Y and B in that order, as in the decoder; Y is quantized first, because the decoder predicts the
/// two others from its dequantized values (chroma from luma): B is predicted from Y even with the default parameters.
/// </description></item>
/// <item><description>
/// The sections are written in the order of the table of contents: <c>LfGlobal</c>, the LF groups (the LF image and the
/// transform, the quantization factor and the filter sharpness of each block, each with a tree learned from its samples
/// from the effort 3), <c>HfGlobal</c> (the entropy code of each pass), then the groups of each pass. The coefficients of a group are given to the entropy coder twice: to build the
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


    /// <summary>The effort from which each tile has its own factors of the chroma from luma prediction.</summary>
    private const int ChromaFromLumaEffort = 3;

    /// <summary>The unit of the factors of the chroma from luma prediction: the default value of the frame.</summary>
    private const float ColorScale = 1f / 84;

    /// <summary>
    /// The effort from which each block has its own quantization factor: the field costs a quarter of the time of an
    /// encoding at the default effort, and more of a faster one.
    /// </summary>
    private const int AdaptiveQuantizationEffort = 3;

    /// <summary>The effort from which the LF image and the block metadata are coded with learned trees.</summary>
    private const int LearnedTreeEffort = 3;

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
    /// The smallest magnitude, in quantization steps, below which a coefficient is quantized to zero: a little above one
    /// half. <see cref="ApplyZeroThresholds"/> raises it for the higher frequencies.
    /// </summary>
    private const float ZeroThreshold = 0.58f;

    /// <summary>
    /// The factor of the step of the LF values of the X channel: the default step is far finer than the one of the
    /// coefficients of X around it.
    /// </summary>
    private const float LfStepFactorX = 2;

    private static readonly JpegXlHybridUintConfig CoefficientConfig = new(4, 2, 0);

    private readonly JpegXlImageHeader _image;
    private readonly JpegXlFrameHeader _frame;
    private readonly PixelFormat _format;
    private readonly int _effort;
    private readonly AllocationScope _scope;
    private readonly CancellationToken _cancellationToken;
    private readonly int _maxDegreeOfParallelism;

    // A copy of the encoder that analyzes or writes groups next to others: it shares the state of the frame and has its own
    // working memory (CreateWorker)
    private bool _isWorker;
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
    private readonly float _distance;
    private int _globalScale;
    private int _quantLf;
    private readonly float[] _lfStep = new float[3];
    private readonly float[] _hfScale = new float[3];
    private readonly int[] _horizontalShift = new int[3];
    private readonly int[] _verticalShift = new int[3];
    private TileBlock[] _tileBlocks = new TileBlock[ColorTileBlocks * ColorTileBlocks];
    private readonly int[]?[] _ranks = new int[]?[JpegXlVarDctTables.OrderCount];
    private readonly int _maxTransformBlocks = 1;
    private readonly bool _usesRectangles;
    private float _referenceStep;

    // The group that is analyzed: its first column and its first row of blocks in the frame
    private int _groupBlockX;
    private int _groupBlockY;

    // The quantization table of each channel of a recompressed JPEG file, in the layout of the coefficients of the frame
    private ushort[][]? _jpegQuantization;

    private readonly Av1PooledArray<int>?[] _coefficients = new Av1PooledArray<int>?[3];
    private readonly Av1PooledArray<int>?[] _lf = new Av1PooledArray<int>?[3];
    private Av1PooledArray<float>?[] _samples = new Av1PooledArray<float>?[3];

    // The samples of a group and of its margin that the quantization field is computed from, and its working storage
    private Av1PooledArray<float>?[] _fieldPlanes = new Av1PooledArray<float>?[3];
    private Av1PooledArray<float>? _fieldScratch;
    private Av1PooledArray<byte>? _blockInfo;
    private Av1PooledArray<ushort>? _quantField;
    private Av1PooledArray<float>? _field;
    private Av1PooledArray<sbyte>? _xFromY;
    private Av1PooledArray<sbyte>? _bFromY;
    private Av1PooledArray<int>? _groupOffsets;
    private Av1PooledArray<int>? _blockOffsets;
    private Av1PooledArray<byte>? _nonZeros;
    private Av1PooledArray<float>? _work;
    private Av1PooledArray<float>? _tileCoefficients;
    private Av1PooledArray<float>? _tileLuma;
    private Av1PooledArray<float>? _candidate;
    private Av1PooledArray<int>? _tileQuantized;
    private JpegXlDequantMatrices? _matrices;
    private JpegXlCoefficientOrders? _orders;
    private JpegXlForwardColor? _color;

    /// <param name="image">The image header that was written, as the decoder reads it: an image stored in XYB.</param>
    /// <param name="frame">The frame header that was written, as the decoder reads it: a VarDCT frame that covers the image.</param>
    /// <param name="format">The pixel format of the frame.</param>
    /// <param name="quality">The quality, from 0 to 100; -1 for a frame that holds the coefficients of a JPEG file (<see cref="EncodeJpegCoefficients"/>).</param>
    /// <param name="effort">The effort, from 0 to 9.</param>
    /// <param name="maxDegreeOfParallelism">The largest number of groups analyzed or written at the same time (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>): the output does not depend on it.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cancellationToken">The token checked between groups.</param>
    public JpegXlVarDctEncoder(JpegXlImageHeader image, JpegXlFrameHeader frame, PixelFormat format, int quality, int effort, int maxDegreeOfParallelism, AllocationScope scope, CancellationToken cancellationToken)
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
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
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

        // The scale of the quantization is the one of the quantization field of the frame: see ComputeQuantField
        _distance = GetDistance(quality);

        // The X and B channels have their own scale of the quantization: a power of 0.8
        _hfScale[0] = MathF.Pow(0.8f, frame.XqmScale - 2);
        _hfScale[1] = 1;
        _hfScale[2] = MathF.Pow(0.8f, frame.BqmScale - 2);

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
    /// <remarks>Groups are written by several workers at the same time when the encoder has several: the writer must not change a shared state.</remarks>
    public delegate void ExtraChannelWriter(LsbBitWriter writer, PixelStorage pixels, int x, int y, int width, int height);

    /// <summary>The scale of the quantization of the coefficients at distance 1, where a difference is barely visible, when every block has the same factor.</summary>
    private static float HfScaleAtDistance1 => 0.8f;

    /// <summary>The scale of the quantization of the LF image at distance 1.</summary>
    private static float LfScaleAtDistance1 => 2.0f;

    /// <summary>The smallest factor of the LF image that the scale of the frame leaves: what keeps its steps precise.</summary>
    private static float MinQuantLf => 10f;

    /// <summary>
    /// The factor of the quantization field: what gives a quality, over photographs, the file size it had with one
    /// quantization factor for the frame (within a tenth, from the lowest qualities to the highest).
    /// </summary>
    private static float FieldScale => 1.75f;

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
            < 0.7f => 0,
            < 1.5f => 1,
            < EpfFirstPassDistance => 2,
            _ => 3,
        };
    }

    /// <summary>
    /// Gets the scale of the strength of the first iteration of the edge-preserving filter for a quality, or
    /// <see langword="null"/> for the default one (0.9): from the distance 2.5, where the decoder applies three
    /// iterations, a first one four times as strong removes more of the ringing of coarse steps.
    /// </summary>
    public static float? GetEpfFirstPassSigmaScale(int quality) => GetDistance(quality) >= EpfFirstPassDistance ? 3.6f : null;

    /// <summary>The distance from which the decoder applies three iterations of the edge-preserving filter, the first one stronger.</summary>
    private const float EpfFirstPassDistance = 2.5f;

    /// <summary>
    /// Gets the sharpness of the edge-preserving filter of every block of the frame, from 0 (no filter) to 7: the finer the
    /// steps, the less the filter blurs an edge it would otherwise smooth, so the higher the sharpness.
    /// </summary>
    private int GetEpfSharpness() => _distance switch
    {
        < 1.4f => 7,
        < 1.75f => 6,
        < 3.5f => 5,
        _ => 4,
    };

    /// <summary>
    /// Gets the weight of the adaptive quantization field of a distance against the factor of the frame: 0 at the
    /// distance 1.75 and above, where every block has the factor of the frame. Measured with SSIMULACRA 2 and Butteraugli,
    /// a field that is not blended makes the files 2.5 % larger at equal quality, and 4 % once the Gabor-like filter is off.
    /// </summary>
    internal static float GetFieldStrength(float distance)
    {
        ReadOnlySpan<float> distances = [0.5f, 1.0f, 1.4f, 1.75f];
        ReadOnlySpan<float> strengths = [0.26565f, 0.275459f, 0.058287f, 0f];
        if (distance <= distances[0])
            return strengths[0];

        for (var i = 1; i < distances.Length; i++)
        {
            if (distance <= distances[i])
            {
                var position = (distance - distances[i - 1]) / (distances[i] - distances[i - 1]);
                return strengths[i - 1] + ((strengths[i] - strengths[i - 1]) * position);
            }
        }

        return 0;
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
        var pixels = lease.Storage;
        AllocateFrame(clear: false);
        _color = new JpegXlForwardColor(_image, PixelFormats.GetBitsPerComponent(_format), _scope);
        _matrices = JpegXlDequantMatrices.CreateDefault(_scope);

        // The reference of the errors: the step of a coefficient in the middle of the 8x8 block of the luma
        _referenceStep = _matrices.GetMatrix(JpegXlVarDctTables.QuantTable[JpegXlVarDctTables.TransformDct8])[64 + (3 * 8) + 3];
        PrepareTables();
        ComputeQuantField(pixels);
        RunGroups((encoder, group) => encoder.AnalyzeGroup(pixels, group));
        DisposeScratch();

        _field?.Dispose();
        _field = null;

        _color?.Dispose();
        _color = null;
        WriteSections(pixels, writer, sizes, writeExtraChannels);
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

        PrepareTables();
        WriteSections(pixels: null, writer, sizes, writeExtraChannels: null);
    }

    public void Dispose()
    {
        DisposeScratch();
        if (_isWorker)
            return;

        for (var c = 0; c < 3; c++)
        {
            _coefficients[c]?.Dispose();
            _coefficients[c] = null;
            _lf[c]?.Dispose();
            _lf[c] = null;
        }

        _blockInfo?.Dispose();
        _blockInfo = null;
        _quantField?.Dispose();
        _quantField = null;
        _field?.Dispose();
        _field = null;
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
        _matrices?.Dispose();
        _matrices = null;
        _orders?.Dispose();
        _orders = null;
        _color?.Dispose();
        _color = null;
    }

    /// <summary>The distance of the LF image: it grows more slowly than the one of the coefficients, since an error of the LF image is a flat error of a whole block.</summary>
    private static float GetLfDistance(float distance) => distance <= 1 ? distance : 1 + ((distance - 1) * 0.5f);

    /// <summary>Rents what the analysis of a group works with, the first time a group is analyzed.</summary>
    private void EnsureScratch()
    {
        if (_work is not null)
            return;

        for (var c = 0; c < 3; c++)
        {
            _samples[c] = Av1PooledArray<float>.Rent(_scope, GroupSize * GroupSize, clear: false);
        }

        _work = Av1PooledArray<float>.Rent(_scope, (long)JpegXlForwardTransform.MaxSize * JpegXlForwardTransform.MaxSize, clear: false);
        _tileCoefficients = Av1PooledArray<float>.Rent(_scope, 3L * ColorTileBlocks * ColorTileBlocks * 64, clear: false);
        _tileLuma = Av1PooledArray<float>.Rent(_scope, (long)ColorTileBlocks * ColorTileBlocks * 64, clear: false);
        _candidate = Av1PooledArray<float>.Rent(_scope, (long)JpegXlForwardTransform.MaxSize * JpegXlForwardTransform.MaxSize, clear: false);
        _tileQuantized = Av1PooledArray<int>.Rent(_scope, (long)JpegXlForwardTransform.MaxSize * JpegXlForwardTransform.MaxSize, clear: false);
    }

    /// <summary>Returns the working memory of the analysis of a group and of the computation of the field.</summary>
    private void DisposeScratch()
    {
        for (var c = 0; c < 3; c++)
        {
            _samples[c]?.Dispose();
            _samples[c] = null;
            _fieldPlanes[c]?.Dispose();
            _fieldPlanes[c] = null;
        }

        _fieldScratch?.Dispose();
        _fieldScratch = null;
        _work?.Dispose();
        _work = null;
        _tileCoefficients?.Dispose();
        _tileCoefficients = null;
        _tileLuma?.Dispose();
        _tileLuma = null;
        _candidate?.Dispose();
        _candidate = null;
        _tileQuantized?.Dispose();
        _tileQuantized = null;
        if (_isWorker)
        {
            // The main encoder keeps these until the frame is written
            _blockOffsets?.Dispose();
            _blockOffsets = null;
            _nonZeros?.Dispose();
            _nonZeros = null;
        }
    }

    /// <summary>
    /// Computes the tables that are computed on first use (the matrices, the orders and the ranks of the transforms the
    /// encoder may choose), so that the workers only read them.
    /// </summary>
    private void PrepareTables()
    {
        for (var transform = 0; transform < JpegXlVarDctTables.TransformCount; transform++)
        {
            if (JpegXlVarDctTables.BlocksWide[transform] > _maxTransformBlocks || JpegXlVarDctTables.BlocksHigh[transform] > _maxTransformBlocks)
                continue;

            _matrices?.GetMatrix(JpegXlVarDctTables.QuantTable[transform]);
            GetRanks(JpegXlVarDctTables.Order[transform]);
        }
    }

    /// <summary>
    /// Runs an operation on every group, by up to <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers: the
    /// encoder itself when there is one, else copies of it (<see cref="CreateWorker"/>).
    /// </summary>
    private void RunGroups(Action<JpegXlVarDctEncoder, int> body)
    {
        var workers = JpegXlGroupWorkers.GetWorkerCount(_maxDegreeOfParallelism, _groupCount, (long)_width * _height);
        if (workers == 1)
        {
            for (var group = 0; group < _groupCount; group++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                body(this, group);
            }

            return;
        }

        JpegXlGroupWorkers.Run(workers, _groupCount, CreateWorker, (worker, group) =>
        {
            _cancellationToken.ThrowIfCancellationRequested();
            body(worker, group);
        });
    }

    /// <summary>
    /// Creates a worker: a copy of the encoder that shares what describes the frame (the field, the blocks, the
    /// coefficients, the tables), whose groups it writes in place, and has its own working memory. Disposing it returns
    /// its working memory only.
    /// </summary>
    private JpegXlVarDctEncoder CreateWorker()
    {
        var worker = (JpegXlVarDctEncoder)MemberwiseClone();
        worker._isWorker = true;
        worker._samples = new Av1PooledArray<float>?[3];
        worker._fieldPlanes = new Av1PooledArray<float>?[3];
        worker._fieldScratch = null;
        worker._tileBlocks = new TileBlock[ColorTileBlocks * ColorTileBlocks];
        worker._work = null;
        worker._tileCoefficients = null;
        worker._tileLuma = null;
        worker._candidate = null;
        worker._tileQuantized = null;
        worker._blockOffsets = null;
        worker._nonZeros = null;
        try
        {
            worker._blockOffsets = Av1PooledArray<int>.Rent(_scope, GroupBlocks * GroupBlocks, clear: false);
            worker._nonZeros = Av1PooledArray<byte>.Rent(_scope, 3L * GroupBlocks * GroupBlocks, clear: false);
            return worker;
        }
        catch
        {
            worker.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Computes the quantization field of the frame (<see cref="JpegXlAdaptiveQuantization"/>) from the samples as they
    /// are given (the decoder smooths what the quantization leaves, not the field), or the same value for every block
    /// at the lowest efforts, then the scale of the quantization of the frame, which the field decides.
    /// </summary>
    private void ComputeQuantField(PixelStorage pixels)
    {
        var blockCount = (long)_blocksWide * _blocksHigh;
        _field = Av1PooledArray<float>.Rent(_scope, blockCount, clear: false);
        var field = _field.Span;
        var flat = HfScaleAtDistance1 / _distance;
        var strength = GetFieldStrength(_distance);
        if (_effort < AdaptiveQuantizationEffort || strength == 0)
        {
            // One factor for the frame
            field[..(int)blockCount].Fill(flat);
        }
        else
        {
            // The adaptive field, blended with the factor of the frame
            ComputeAdaptiveQuantField(pixels);
            foreach (ref var value in field[..(int)blockCount])
            {
                value = (strength * value) + ((1 - strength) * flat);
            }
        }

        // The scale of the frame; the LF image keeps at least ten steps per unit of its own scale
        var lfDistance = GetLfDistance(_distance);
        using (var sorted = Av1PooledArray<float>.Rent(_scope, blockCount, clear: false))
        {
            _globalScale = JpegXlAdaptiveQuantization.GetGlobalScale(field[..(int)blockCount], sorted.Span, (int)(65536f * LfScaleAtDistance1 / (lfDistance * MinQuantLf)));
        }

        _quantLf = Math.Clamp((int)MathF.Round(65536f * LfScaleAtDistance1 / (lfDistance * _globalScale), MidpointRounding.AwayFromZero), 1, 65536);
        ReadOnlySpan<float> lfQuant = [LfStepFactorX / 4096, 1f / 512, 1f / 256];
        for (var c = 0; c < 3; c++)
        {
            _lfStep[c] = lfQuant[c] * (65536f / _globalScale / _quantLf);
        }
    }

    /// <summary>Computes the quantization field of the frame from its samples, group by group.</summary>
    private void ComputeAdaptiveQuantField(PixelStorage pixels)
    {
        RunGroups((encoder, group) => encoder.ComputeGroupField(pixels, group));
        DisposeScratch();
    }

    /// <summary>Computes the quantization field of the blocks of a group.</summary>
    private void ComputeGroupField(PixelStorage pixels, int group)
    {
        const int Margin = JpegXlAdaptiveQuantization.Margin;
        const int Stride = GroupSize + (2 * Margin);
        if (_fieldScratch is null)
        {
            for (var c = 0; c < 3; c++)
            {
                _fieldPlanes[c] = Av1PooledArray<float>.Rent(_scope, Stride * Stride, clear: false);
            }

            _fieldScratch = Av1PooledArray<float>.Rent(_scope, JpegXlAdaptiveQuantization.GetScratchLength(GroupBlocks, GroupBlocks), clear: false);
        }

        var planeX = _fieldPlanes[0]!;
        var planeY = _fieldPlanes[1]!;
        var planeB = _fieldPlanes[2]!;
        var field = _field!.Span;
        var scale = JpegXlAdaptiveQuantization.ScaleAtDistance1 * FieldScale / _distance;
        GetGroupRectangle(group, out var groupX, out var groupY, out var groupWidth, out var groupHeight);

        // The samples of the group and of what the field reads around it; outside the frame, its nearest sample
        var x0 = (groupX * 8) - Margin;
        var first = Math.Max(0, x0);
        var last = Math.Min(_width, x0 + (groupWidth * 8) + (2 * Margin));
        var width = (groupWidth * 8) + (2 * Margin);
        var previous = -1;
        for (var y = 0; y < (groupHeight * 8) + (2 * Margin); y++)
        {
            var rowX = planeX.Span.Slice(y * Stride, width);
            var rowY = planeY.Span.Slice(y * Stride, width);
            var rowB = planeB.Span.Slice(y * Stride, width);
            var sourceY = Math.Clamp((groupY * 8) - Margin + y, 0, _height - 1);
            if (sourceY == previous)
            {
                planeX.Span.Slice((y - 1) * Stride, width).CopyTo(rowX);
                planeY.Span.Slice((y - 1) * Stride, width).CopyTo(rowY);
                planeB.Span.Slice((y - 1) * Stride, width).CopyTo(rowB);
                continue;
            }

            previous = sourceY;
            _color!.Convert(pixels.GetRowSpanCore(sourceY), _format, first, rowX[(first - x0)..(last - x0)], rowY[(first - x0)..(last - x0)], rowB[(first - x0)..(last - x0)]);
            rowX[..(first - x0)].Fill(rowX[first - x0]);
            rowY[..(first - x0)].Fill(rowY[first - x0]);
            rowB[..(first - x0)].Fill(rowB[first - x0]);
            rowX[(last - x0)..].Fill(rowX[last - x0 - 1]);
            rowY[(last - x0)..].Fill(rowY[last - x0 - 1]);
            rowB[(last - x0)..].Fill(rowB[last - x0 - 1]);
        }

        JpegXlAdaptiveQuantization.ComputeField(planeX.Span, planeY.Span, planeB.Span, Stride, groupX, groupY, groupWidth, groupHeight, _blocksWide, _blocksHigh, _distance, scale, _fieldScratch.Span, field[((groupY * _blocksWide) + groupX)..], _blocksWide);
    }

    /// <summary>Gets the quantization factor of a transform at a block of the frame: the one of the field for its blocks.</summary>
    private int GetQuantFactor(int transform, int blockX, int blockY)
    {
        var value = JpegXlAdaptiveQuantization.GetTransformValue(_field!.Span[((blockY * _blocksWide) + blockX)..], _blocksWide, JpegXlVarDctTables.BlocksWide[transform], JpegXlVarDctTables.BlocksHigh[transform], _distance);
        return JpegXlAdaptiveQuantization.GetQuantFactor(value, 65536f / _globalScale);
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
    private void AnalyzeGroup(PixelStorage pixels, int group)
    {
        EnsureScratch();
        GetGroupRectangle(group, out var groupX, out var groupY, out var groupWidth, out var groupHeight);
        _groupBlockX = groupX;
        _groupBlockY = groupY;
        LoadSamples(pixels, groupX * 8, groupY * 8, groupWidth * 8, groupHeight * 8);

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
                var factor = (ushort)GetQuantFactor(transform, groupX + tileX + x, groupY + tileY + y);
                for (var by = 0; by < blocksHigh; by++)
                {
                    blockInfo.Slice(position + (by * _blocksWide), blocksWide).Fill(BlockCovered);
                    quantField.Slice(position + (by * _blocksWide), blocksWide).Fill(factor);
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
        var scale = (65536f / _globalScale) / GetQuantFactor(transform, _groupBlockX + x, _groupBlockY + y);
        var columns = Math.Max(blocksWide, blocksHigh) * 8;
        var rows = Math.Min(blocksWide, blocksHigh) * 8;
        var lowestRows = Math.Min(blocksWide, blocksHigh);
        var lowestColumns = Math.Max(blocksWide, blocksHigh);
        var ranks = GetRanks(JpegXlVarDctTables.Order[transform]);
        Span<float> thresholds = stackalloc float[4];
        GetZeroThresholds(1, blocksWide, blocksHigh, thresholds);
        var (error, bits, nonZeroCount, lastRank) = GetQuantizationCost(coefficients, matrix, scale, ranks, rows, columns, lowestRows, lowestColumns, thresholds);

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

        // The error is counted in the steps the field gives the blocks, whatever the transform: two choices for the same
        // blocks are compared on the same error
        var fieldSum = 0f;
        var fieldRows = _field!.Span[(((_groupBlockY + y) * _blocksWide) + _groupBlockX + x)..];
        for (var by = 0; by < blocksHigh; by++)
        {
            for (var bx = 0; bx < blocksWide; bx++)
            {
                fieldSum += fieldRows[(by * _blocksWide) + bx];
            }
        }

        var reference = _referenceStep * (blocksWide * blocksHigh) / fieldSum;
        return (error * size * (DistortionWeight / (reference * reference))) + bits;
    }

    /// <summary>
    /// Quantizes the coefficients of a transform for <see cref="GetTransformCost"/>, the lowest frequencies excepted: the sum
    /// of the squared errors, in the order of the coefficients, and the bits of the values that are not zero.
    /// </summary>
    /// <param name="coefficients">The coefficients, in rows of <paramref name="columns"/> values.</param>
    /// <param name="matrix">The dequantization matrix, in the same layout.</param>
    /// <param name="scale">The factor of the matrix that gives the steps.</param>
    /// <param name="ranks">The rank of each coefficient in the order of the transform.</param>
    /// <param name="rows">The number of rows.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="lowestRows">The number of rows that start with lowest frequencies.</param>
    /// <param name="lowestColumns">The number of lowest frequencies at the start of these rows.</param>
    /// <param name="thresholds">The zero thresholds of the four quarters of the frequencies (<see cref="GetZeroThresholds"/>).</param>
    /// <remarks>
    /// The vector path computes the terms of the error of a row one lane per coefficient, then adds them one after the other.
    /// The bits of a value are 3.5 plus an integer, and their sum stays far below 2^22: every partial sum is exact, so it is
    /// the same in any order.
    /// </remarks>
    internal static QuantizationCost GetQuantizationCost(ReadOnlySpan<float> coefficients, ReadOnlySpan<float> matrix, float scale, ReadOnlySpan<int> ranks, int rows, int columns, int lowestRows, int lowestColumns, ReadOnlySpan<float> thresholds)
    {
        if (!Vector.IsHardwareAccelerated || columns % Vector<float>.Count != 0 || columns > JpegXlForwardTransform.MaxSize)
            return GetQuantizationCostScalar(coefficients, matrix, scale, ranks, rows, columns, lowestRows, lowestColumns, thresholds);

        Span<float> errors = stackalloc float[JpegXlForwardTransform.MaxSize];
        var error = 0f;
        var nonZero = Vector<int>.Zero;
        var log2Sum = Vector<int>.Zero;
        var maxRank = Vector<int>.Zero;
        var scaleVector = new Vector<float>(scale);
        var half = new Vector<int>(columns / 2);
        for (var row = 0; row < rows; row++)
        {
            var quarter = row >= rows / 2 ? 2 : 0;
            var low = new Vector<float>(thresholds[quarter]);
            var high = new Vector<float>(thresholds[quarter + 1]);
            var start = row < lowestRows ? lowestColumns : 0;
            var startVector = new Vector<int>(start);
            var values = coefficients.Slice(row * columns, columns);
            var steps = matrix.Slice(row * columns, columns);
            var rowRanks = ranks.Slice(row * columns, columns);
            for (var column = 0; column < columns; column += Vector<float>.Count)
            {
                var columnIndex = Vector<int>.Indices + new Vector<int>(column);
                var value = new Vector<float>(values[column..]);
                var step = new Vector<float>(steps[column..]) * scaleVector;
                var quotient = value / step;
                var magnitude = Vector.Abs(quotient);

                // Quantize, then zero the magnitudes of one below the threshold of their quarter; the floor of the binary
                // logarithm of a magnitude is the exponent of the rounded value (the conversion saturates at 2^31 - 1)
                var rounding = magnitude + new Vector<float>(0.5f);
                var rounded = Vector.ConvertToInt32(rounding);
                var threshold = Vector.ConditionalSelect(Vector.GreaterThanOrEqual(columnIndex, half), high, low);
                var isZeroed = Vector.Equals(rounded, Vector<int>.One) & Vector.LessThan(magnitude, threshold);
                var isNonZero = Vector.AndNot(Vector.GreaterThanOrEqual(magnitude, new Vector<float>(ZeroThreshold)), isZeroed);
                var quantized = Vector.ConvertToSingle(rounded);
                quantized = Vector.ConditionalSelect(Vector.LessThan(quotient, Vector<float>.Zero), -quantized, quantized);
                var difference = value - (quantized * step);
                Vector.ConditionalSelect(isNonZero, difference * difference, value * value).CopyTo(errors[column..]);

                var counted = isNonZero & Vector.GreaterThanOrEqual(columnIndex, startVector);
                nonZero -= counted;
                var log2 = Vector.Min(Vector.ShiftRightLogical(Vector.AsVectorInt32(rounding), 23) - new Vector<int>(127), new Vector<int>(30));
                log2Sum += log2 & counted;
                maxRank = Vector.Max(maxRank, new Vector<int>(rowRanks[column..]) & counted);
            }

            foreach (var term in errors[start..columns])
            {
                error += term;
            }
        }

        var lastRank = 0;
        for (var i = 0; i < Vector<int>.Count; i++)
        {
            lastRank = Math.Max(lastRank, maxRank[i]);
        }

        var nonZeroCount = Vector.Sum(nonZero);
        return new QuantizationCost(error, (nonZeroCount * NonZeroBits) + (Vector.Sum(log2Sum) * MagnitudeBits), nonZeroCount, lastRank);
    }

    /// <summary>The scalar reference of <see cref="GetQuantizationCost"/>.</summary>
    internal static QuantizationCost GetQuantizationCostScalar(ReadOnlySpan<float> coefficients, ReadOnlySpan<float> matrix, float scale, ReadOnlySpan<int> ranks, int rows, int columns, int lowestRows, int lowestColumns, ReadOnlySpan<float> thresholds)
    {
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
                if (quantized is 1 or -1 && MathF.Abs(value / step) < thresholds[GetFrequencyQuarter(row, column, rows, columns)])
                {
                    quantized = 0;
                }

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

        return new QuantizationCost(error, bits, nonZeroCount, lastRank);
    }

    /// <summary>The squared error, the bits, the number and the last rank of the quantized values of <see cref="GetQuantizationCost"/>.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct QuantizationCost(float Error, float Bits, int NonZeroCount, int LastRank);

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
            QuantizeLuma(source.Slice(TileArea, size), matrix.Slice(size, size), scale, biasY, biasNumerator, quantizedY, luma);
            ApplyZeroThresholds(1, blocksWide, blocksHigh, source.Slice(TileArea, size), luma, 0, matrix.Slice(size, size), scale, quantizedY, luma);

            // The lowest frequencies are in the LF image
            ClearLowestFrequencies(quantizedY, blocksWide, blocksHigh);
            ClearLowestFrequencies(luma, blocksWide, blocksHigh);
            if (_effort < ChromaFromLumaEffort)
                continue;

            for (var k = 0; k < size; k++)
            {
                var dequantizedY = luma[k];
                if (quantizedY[k] != 0)
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
            QuantizeChroma(source[..size], luma, matrix[..size], kx, scale * _hfScale[0], quantizedX);
            QuantizeChroma(source.Slice(2 * TileArea, size), luma, matrix.Slice(2 * size, size), kb, scale * _hfScale[2], quantizedB);
            ApplyZeroThresholds(0, blocksWide, blocksHigh, source[..size], luma, kx, matrix[..size], scale * _hfScale[0], quantizedX, default);
            ApplyZeroThresholds(2, blocksWide, blocksHigh, source.Slice(2 * TileArea, size), luma, kb, matrix.Slice(2 * size, size), scale * _hfScale[2], quantizedB, default);
            ClearLowestFrequencies(quantizedX, blocksWide, blocksHigh);
            ClearLowestFrequencies(quantizedB, blocksWide, blocksHigh);
        }
    }

    /// <summary>
    /// Gets the magnitudes, in quantization steps, below which a coefficient of a channel is quantized to zero, for each
    /// quarter of the frequencies of a transform: low in both directions, high horizontally, high vertically, high in both.
    /// </summary>
    /// <remarks>
    /// The higher frequencies, the X channel outside the distances 0.75 to 1.5, and the high frequencies of B are zeroed
    /// more readily: a coefficient of magnitude one costs more bits there than its error is worth. The larger transforms
    /// lower the thresholds a little, and the finest steps (below the distance 1) bring the luma down to one half.
    /// Measured with SSIMULACRA 2 and Butteraugli: about 0.5 % smaller at equal quality than one threshold.
    /// </remarks>
    private void GetZeroThresholds(int channel, int blocksWide, int blocksHigh, Span<float> thresholds)
    {
        Span<float> normal = [0.58f, 0.635f, 0.66f, 0.7f];
        if (channel == 0)
        {
            var window = Math.Min(Math.Clamp((_distance - 0.5f) / 0.25f, 0f, 1f), Math.Clamp((2.0f - _distance) / 0.5f, 0f, 1f));
            normal[0] += window * (0.5f - normal[0]);
            for (var i = 1; i < 4; i++)
            {
                normal[i] += (1 - window) * 0.08f;
            }
        }
        else if (channel == 2)
        {
            normal[1..].Fill(0.75f);
        }

        if (blocksWide > 1 || blocksHigh > 1)
        {
            var delta = Math.Clamp(0.003f * blocksWide * blocksHigh, 0f, channel == 1 ? 0.12f : 0.08f);
            for (var i = 0; i < 4; i++)
            {
                normal[i] -= delta;
            }
        }

        ReadOnlySpan<float> fine = channel == 1 ? [0.50f, 0.51f, 0.52f, 0.54f] : normal;
        var position = Math.Clamp((_distance - 0.1f) / 0.9f, 0f, 1f);
        for (var i = 0; i < 4; i++)
        {
            thresholds[i] = fine[i] + (position * (normal[i] - fine[i]));
        }
    }

    /// <summary>Gets the quarter of the frequencies of a coefficient of a transform: the index of its threshold in <see cref="GetZeroThresholds"/>.</summary>
    private static int GetFrequencyQuarter(int row, int column, int rows, int columns) => (row >= rows / 2 ? 2 : 0) + (column >= columns / 2 ? 1 : 0);

    /// <summary>
    /// Zeroes the coefficients of magnitude one of a channel of a transform that are below the threshold of their quarter
    /// of the frequencies (<see cref="GetZeroThresholds"/>), once they are quantized with <see cref="ZeroThreshold"/>.
    /// </summary>
    /// <param name="channel">The channel: 0 for X, 1 for Y, 2 for B.</param>
    /// <param name="blocksWide">The number of columns of blocks of the transform.</param>
    /// <param name="blocksHigh">The number of rows of blocks of the transform.</param>
    /// <param name="coefficients">The coefficients of the channel.</param>
    /// <param name="luma">For X and B, the dequantized luma, which <paramref name="factor"/> times predicts them.</param>
    /// <param name="factor">For X and B, the factor of the chroma from luma prediction.</param>
    /// <param name="matrix">The dequantization matrix of the channel.</param>
    /// <param name="scale">The factor of the matrix that gives the steps.</param>
    /// <param name="quantized">The quantized values, updated.</param>
    /// <param name="dequantized">For Y, the dequantized values, updated with the values that are zeroed; empty otherwise.</param>
    private void ApplyZeroThresholds(int channel, int blocksWide, int blocksHigh, ReadOnlySpan<float> coefficients, ReadOnlySpan<float> luma, float factor, ReadOnlySpan<float> matrix, float scale, Span<int> quantized, Span<float> dequantized)
    {
        Span<float> thresholds = stackalloc float[4];
        GetZeroThresholds(channel, blocksWide, blocksHigh, thresholds);
        var columns = Math.Max(blocksWide, blocksHigh) * 8;
        var rows = Math.Min(blocksWide, blocksHigh) * 8;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var k = (row * columns) + column;
                if (quantized[k] is not (1 or -1))
                    continue;

                var value = channel == 1 ? coefficients[k] : coefficients[k] - (factor * luma[k]);
                if (MathF.Abs(value / (matrix[k] * scale)) < thresholds[GetFrequencyQuarter(row, column, rows, columns)])
                {
                    quantized[k] = 0;
                    if (!dequantized.IsEmpty)
                    {
                        dequantized[k] = 0;
                    }
                }
            }
        }
    }

    /// <summary>Zeroes the values of a transform that are in the LF image: the lowest frequencies, one per block.</summary>
    private static void ClearLowestFrequencies<T>(Span<T> values, int blocksWide, int blocksHigh)
    {
        var columns = Math.Max(blocksWide, blocksHigh) * 8;
        var lowestColumns = Math.Max(blocksWide, blocksHigh);
        for (var row = 0; row < Math.Min(blocksWide, blocksHigh); row++)
        {
            values.Slice(row * columns, lowestColumns).Clear();
        }
    }

    /// <summary>
    /// Quantizes the luma coefficients of a transform and dequantizes them as the decoder does (<see cref="Quantize"/>,
    /// <see cref="JpegXlVarDctDecoder.AdjustBias(int, float, float)"/>); the lowest frequencies are computed too, and cleared by the caller.
    /// </summary>
    private static void QuantizeLuma(ReadOnlySpan<float> coefficients, ReadOnlySpan<float> matrix, float scale, float bias, float biasNumerator, Span<int> quantized, Span<float> dequantized)
    {
        var k = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var scaleVector = new Vector<float>(scale);
            var biasVector = new Vector<float>(bias);
            var biasNumeratorVector = new Vector<float>(biasNumerator);
            for (; k <= coefficients.Length - Vector<float>.Count; k += Vector<float>.Count)
            {
                var step = new Vector<float>(matrix[k..]) * scaleVector;
                var value = QuantizeVector(new Vector<float>(coefficients[k..]) / step);
                value.CopyTo(quantized[k..]);

                // The bias of a value of magnitude one, or the value minus the bias numerator divided by it; zero stays zero
                var asFloat = Vector.ConvertToSingle(value);
                var magnitude = Vector.Abs(value);
                var adjusted = Vector.ConditionalSelect(Vector.Equals(magnitude, Vector<int>.One), asFloat * biasVector, asFloat - (biasNumeratorVector / asFloat));
                adjusted = Vector.ConditionalSelect(Vector.Equals(value, Vector<int>.Zero), Vector<float>.Zero, adjusted);
                (adjusted * step).CopyTo(dequantized[k..]);
            }
        }

        for (; k < coefficients.Length; k++)
        {
            var step = matrix[k] * scale;
            var value = Quantize(coefficients[k] / step);
            quantized[k] = value;
            dequantized[k] = JpegXlVarDctDecoder.AdjustBias(value, bias, biasNumerator) * step;
        }
    }

    /// <summary>
    /// Quantizes the coefficients of a chroma channel of a transform once the luma is predicted:
    /// <c>(coefficient - factor * luma) / (matrix * scale)</c>; the lowest frequencies are computed too.
    /// </summary>
    private static void QuantizeChroma(ReadOnlySpan<float> coefficients, ReadOnlySpan<float> luma, ReadOnlySpan<float> matrix, float factor, float scale, Span<int> quantized)
    {
        var k = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var factorVector = new Vector<float>(factor);
            var scaleVector = new Vector<float>(scale);
            for (; k <= coefficients.Length - Vector<float>.Count; k += Vector<float>.Count)
            {
                var value = (new Vector<float>(coefficients[k..]) - (factorVector * new Vector<float>(luma[k..]))) / (new Vector<float>(matrix[k..]) * scaleVector);
                QuantizeVector(value).CopyTo(quantized[k..]);
            }
        }

        for (; k < coefficients.Length; k++)
        {
            quantized[k] = Quantize((coefficients[k] - (factor * luma[k])) / (matrix[k] * scale));
        }
    }

    /// <summary><see cref="Quantize"/> on each element of a vector.</summary>
    private static Vector<int> QuantizeVector(Vector<float> value)
    {
        var magnitude = Vector.Abs(value);
        var rounded = Vector.ConvertToInt32(magnitude + new Vector<float>(0.5f));
        var signed = Vector.ConditionalSelect(Vector.LessThan(value, Vector<float>.Zero), -rounded, rounded);
        return Vector.ConditionalSelect(Vector.LessThan(magnitude, new Vector<float>(ZeroThreshold)), Vector<int>.Zero, signed);
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
            var quantized = _tileQuantized!.Span[..size];
            QuantizeChroma(coefficients, luma, matrix, k0, scale, quantized);
            ClearLowestFrequencies(quantized, blocksWide, blocksHigh);
            foreach (var value in quantized)
            {
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
        var magnitude = MathF.Abs(value);
        if (magnitude < ZeroThreshold)
            return 0;

        var rounded = (int)(magnitude + 0.5f);
        return value < 0 ? -rounded : rounded;
    }

    /// <summary>Converts a rectangle of the frame to XYB; the samples outside the frame repeat its last column and its last row.</summary>
    private void LoadSamples(PixelStorage pixels, int x0, int y0, int width, int height)
    {
        var available = Math.Min(width, _width - x0);
        var rows = Math.Min(height, _height - y0);
        var planeX = _samples[0]!.Span;
        var planeY = _samples[1]!.Span;
        var planeB = _samples[2]!.Span;
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

            _color!.Convert(pixels.GetRowSpanCore(y0 + y), _format, x0, rowX[..available], rowY[..available], rowB[..available]);
            rowX[available..].Fill(rowX[available - 1]);
            rowY[available..].Fill(rowY[available - 1]);
            rowB[available..].Fill(rowB[available - 1]);
        }
    }

    private void WriteSections(PixelStorage? pixels, LsbBitWriter writer, List<int> sizes, ExtraChannelWriter? writeExtraChannels)
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
            // The default steps, but for X (LfStepFactorX), in units of 1/128
            writer.WriteBool(false);
            writer.WriteF16(128f * LfStepFactorX / 4096);
            writer.WriteF16(128f / 512);
            writer.WriteF16(128f / 256);
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
            writeExtraChannels?.Invoke(writer, pixels!, 0, 0, _width, _height);
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
                var builder = builders[pass] = CreateCoefficientCode(maxClusters);
                CountCoefficients(builder, pass, maxClusters);
                builder.Build();
                writer.WriteU32(0, 0x5F, 0, 0x13, 0, 0, 0, 0, 13);
                builder.WriteDescription(writer);
            }

            EndSection();
            var workers = JpegXlGroupWorkers.GetWorkerCount(_maxDegreeOfParallelism, _groupCount, (long)_width * _height);
            for (var pass = 0; pass < _passCount; pass++)
            {
                var builder = builders[pass]!;
                var writesExtraChannels = writeExtraChannels is not null && !extraChannelsAreGlobal && pass == _passCount - 1;
                if (workers == 1)
                {
                    for (var group = 0; group < _groupCount; group++)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        builder.BeginStream();
                        AddGroupCoefficients(builder, group, pass, count: false);
                        builder.WriteStream(writer);
                        if (writesExtraChannels)
                        {
                            WriteGroupExtraChannels(writeExtraChannels!, writer, pixels!, group);
                        }

                        EndSection();
                    }

                    continue;
                }

                // Each group is a section of its own, written by a worker in a writer of its own, then appended in order: the
                // sections start at a byte boundary, so the bytes are those of one writer
                var sections = new LsbBitWriter?[_groupCount];
                try
                {
                    JpegXlGroupWorkers.Run(workers, _groupCount, () => new GroupWriter(this), (state, group) =>
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        var section = sections[group] = new LsbBitWriter(_scope);
                        state.Worker.AddGroupCoefficients(builder, group, pass, count: false, state.Stream);
                        builder.WriteStream(state.Stream, section);
                        if (writesExtraChannels)
                        {
                            WriteGroupExtraChannels(writeExtraChannels!, section, pixels!, group);
                        }

                        section.ZeroPadToByte();
                    });

                    for (var group = 0; group < _groupCount; group++)
                    {
                        writer.WriteAlignedBytes(sections[group]!.WrittenSpan);
                        EndSection();
                    }
                }
                finally
                {
                    foreach (var section in sections)
                    {
                        section?.Dispose();
                    }
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

    private JpegXlEntropyCodeBuilder CreateCoefficientCode(int maxClusters) => new(JpegXlVarDctTables.ContextsPerBlockContext * BlockContextCount, CoefficientConfig, maxClusters, useAns: true, _scope);

    /// <summary>
    /// Counts the coefficients of a pass for its entropy code: each worker counts its groups in a code of its own, and the
    /// counts are added up, which gives the counts of the groups counted one after the other.
    /// </summary>
    private void CountCoefficients(JpegXlEntropyCodeBuilder builder, int pass, int maxClusters)
    {
        var workers = JpegXlGroupWorkers.GetWorkerCount(_maxDegreeOfParallelism, _groupCount, (long)_width * _height);
        if (workers == 1)
        {
            for (var group = 0; group < _groupCount; group++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                AddGroupCoefficients(builder, group, pass, count: true);
            }

            return;
        }

        var builders = new List<JpegXlEntropyCodeBuilder>();
        try
        {
            JpegXlGroupWorkers.Run(
                workers,
                _groupCount,
                () =>
                {
                    var counter = CreateCoefficientCode(maxClusters);
                    lock (builders)
                    {
                        builders.Add(counter);
                    }

                    return new GroupCounter(this, counter);
                },
                (state, group) =>
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    state.Worker.AddGroupCoefficients(state.Builder, group, pass, count: true);
                });

            foreach (var counter in builders)
            {
                builder.AddCounts(counter);
            }
        }
        finally
        {
            foreach (var counter in builders)
            {
                counter.Dispose();
            }
        }
    }

    private void WriteGroupExtraChannels(ExtraChannelWriter writeExtraChannels, LsbBitWriter writer, PixelStorage pixels, int group)
    {
        var x = group % _groupColumns * GroupSize;
        var y = group / _groupColumns * GroupSize;
        writeExtraChannels(writer, pixels, x, y, Math.Min(GroupSize, _width - x), Math.Min(GroupSize, _height - y));
    }

    /// <summary>A worker that counts the coefficients of groups in a code of its own, which the caller owns.</summary>
    private sealed class GroupCounter(JpegXlVarDctEncoder encoder, JpegXlEntropyCodeBuilder builder) : IDisposable
    {
        public JpegXlVarDctEncoder Worker { get; } = encoder.CreateWorker();

        public JpegXlEntropyCodeBuilder Builder { get; } = builder;

        public void Dispose() => Worker.Dispose();
    }

    /// <summary>A worker that writes the coefficients of groups, with a stream of its own.</summary>
    private sealed class GroupWriter : IDisposable
    {
        public GroupWriter(JpegXlVarDctEncoder encoder)
        {
            Worker = encoder.CreateWorker();
            Stream = new JpegXlEntropyCodeBuilder.StreamBuffer(encoder._scope);
        }

        public JpegXlVarDctEncoder Worker { get; }

        public JpegXlEntropyCodeBuilder.StreamBuffer Stream { get; }

        public void Dispose()
        {
            Worker.Dispose();
            Stream.Dispose();
        }
    }

    /// <summary>
    /// Writes an LF group: the LF values of its blocks, as a Modular image of three channels (Y, X, B); then the factors of
    /// the chroma from luma prediction of its tiles, the transform and the quantization factor of each block that starts a
    /// transform, and the sharpness of each block, as a Modular image of four channels. From <see cref="LearnedTreeEffort"/>,
    /// each image has a tree learned from its samples (<see cref="JpegXlModularEncoder.EncodeWithLearnedTree"/>): the LF
    /// values of X and B are coded in contexts of the values of the channels before them.
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

            if (_effort >= LearnedTreeEffort)
            {
                // From the effort 7, as in the other Modular images, the weighted predictor is one of the predictors
                var settings = _effort >= JpegXlModularEncoder.WeightedPredictorEffort ? JpegXlModularEncoder.LfImageTreeWithWeightedPredictor : JpegXlModularEncoder.LfImageTree;
                JpegXlModularEncoder.EncodeWithLearnedTree(writer, image, 1 + lfGroup, settings, _scope, _cancellationToken);
            }
            else
            {
                JpegXlModularEncoder.Encode(writer, image, colorChannelCount: 0, _effort, _scope, _cancellationToken);
            }
        }

        var blockInfo = _blockInfo!.Span;
        var quantField = _quantField!.Span;
        var sharpness = _jpegQuantization is null ? GetEpfSharpness() : 0;
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
            metadata.Channels[3].GetRow(y).Fill(sharpness);
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
        if (_effort >= LearnedTreeEffort)
        {
            JpegXlModularEncoder.EncodeWithLearnedTree(writer, metadata, 1 + (2 * _lfGroupCount) + lfGroup, JpegXlModularEncoder.BlockMetadataTree, _scope, _cancellationToken);
        }
        else
        {
            JpegXlModularEncoder.Encode(writer, metadata, colorChannelCount: 0, _effort, _scope, _cancellationToken, firstRowChannel: 2);
        }
    }

    /// <summary>
    /// Gives the values that a pass codes for a group to the entropy coder, with their contexts: for each block and each
    /// channel (Y, X, B), the number of non-zero coefficients, then the coefficients up to the last one that is not zero.
    /// This is what <see cref="JpegXlVarDctDecoder.ReadGroupPass"/> reads.
    /// </summary>
    private void AddGroupCoefficients(JpegXlEntropyCodeBuilder builder, int group, int pass, bool count, JpegXlEntropyCodeBuilder.StreamBuffer? stream = null)
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
                    if (_passCount == 1)
                    {
                        // Every coefficient is in the pass, and the lowest frequencies, which the order puts first, are zero
                        nonZeroCount = CountNonZeros(block);
                    }
                    else
                    {
                        for (var k = blockCount; k < size; k++)
                        {
                            var index = coefficientOrder[k];
                            nonZeroCount += block[index] != 0 && GetPass(index, rows, columns) == pass ? 1 : 0;
                        }
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
            else if (stream is null)
            {
                builder.Add(context, value);
            }
            else
            {
                builder.Add(stream, context, value);
            }
        }
    }

    private static int CountNonZeros(ReadOnlySpan<int> values)
    {
        var count = 0;
        var k = 0;
        if (Vector.IsHardwareAccelerated)
        {
            // Each lane counts down by one for a value that is zero
            var zeros = Vector<int>.Zero;
            for (; k <= values.Length - Vector<int>.Count; k += Vector<int>.Count)
            {
                zeros += Vector.Equals(new Vector<int>(values[k..]), Vector<int>.Zero);
            }

            count = (k + Vector.Sum(zeros));
        }

        for (; k < values.Length; k++)
        {
            count += values[k] != 0 ? 1 : 0;
        }

        return count;
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
