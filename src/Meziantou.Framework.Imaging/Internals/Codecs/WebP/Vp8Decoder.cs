using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The VP8 key-frame decoder of WebP lossy images (RFC 6386, "VP8 Data Format and Decoding Guide"; WebP only uses key
/// frames): frame header, segmentation, loop-filter and quantizer settings, token probability updates, per-macroblock intra
/// modes, DCT/WHT token decoding with dequantization, intra prediction, the inverse transforms, and the normal and simple loop
/// filters. The output is the reconstructed Y'CbCr 4:2:0 planes (<see cref="Vp8Planes"/>), bit-exact by definition of the
/// format; the RGB conversion is a separate step (<see cref="WebPYuvConverter"/>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Rejected as <see cref="UnsupportedImageFeatureException"/>: interframes and frames that are not shown (a WebP image is a
/// shown key frame). Rejected as <see cref="InvalidImageContentException"/>: a wrong start code, a version above 3, empty
/// dimensions, a first partition or partition table extending past the chunk, and a partition read more than
/// <see cref="MaxOverrunBytes"/> bytes past its end (truncated data).
/// </description></item>
/// <item><description>
/// The color space and clamping bits and the upscaling bits of the dimensions are informative and ignored: reconstruction
/// always clamps (identical for streams that declare clamping unnecessary), and no upscaling is applied.
/// </description></item>
/// <item><description>
/// Intra prediction uses the unfiltered reconstruction (the loop filter runs once the whole frame is reconstructed, in
/// macroblock order); edges outside the frame are 127 above (and above-left on the first row) and 129 on the left;
/// sub-blocks on the right column of a macroblock take their above-right pixels from the macroblock row above, the last
/// macroblock of a row replicating its above neighbor's last pixel.
/// </description></item>
/// <item><description>
/// Dequantized coefficients are stored as 16-bit values (as in the reference decoders), and the inner edges of a
/// macroblock are filtered when it is predicted per sub-block or has at least one decoded non-zero coefficient.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class Vp8Decoder : IDisposable
{
    /// <summary>The length of the key-frame header before the first partition: 3-byte frame tag, start code, dimensions.</summary>
    public const int FrameHeaderLength = 10;

    /// <summary>The number of zero bytes a partition may be read past its end: the decoder's two-byte window looks ahead of the arithmetic decoder.</summary>
    public const int MaxOverrunBytes = 2;

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

    private static ReadOnlySpan<sbyte> SegmentTree => [2, 4, -0, -1, -2, -3];

    private readonly AllocationScope _scope;
    private readonly CancellationToken _cancellationToken;
    private readonly byte[] _data;
    private readonly byte[] _tokenProbabilities = new byte[4 * 8 * 3 * Vp8Tables.TokenNodes];
    private readonly short[] _coefficients = new short[25 * 16];
    private readonly Quantizer[] _quantizers = new Quantizer[4];
    private readonly byte[] _segmentFilterLevels = new byte[4];
    private readonly int[] _segmentQuantizers = new int[4];
    private readonly int[] _segmentLoopFilters = new int[4];
    private readonly byte[] _segmentProbabilities = [255, 255, 255];
    private readonly int[] _referenceDeltas = new int[4];
    private readonly int[] _modeDeltas = new int[4];
    private readonly List<PooledBuffer> _buffers = [];
    private Vp8BoolDecoder _header = null!;
    private Vp8BoolDecoder[] _partitions = [];

    // Frame settings
    private int _width;
    private int _height;
    private int _mbWidth;
    private int _mbHeight;
    private bool _segmentationEnabled;
    private bool _updateSegmentMap;
    private bool _absoluteSegmentValues;
    private bool _simpleFilter;
    private int _filterLevel;
    private int _sharpness;
    private bool _filterDeltasEnabled;
    private bool _skipEnabled;
    private int _skipProbability;

    // Contexts
    private byte[] _aboveModes = [];
    private readonly byte[] _leftModes = new byte[4];
    private byte[] _aboveNonZero = [];
    private readonly byte[] _leftNonZero = new byte[9];

    // Per-macroblock loop-filter information: level (bits 0-5) and the inner-edge flag (bit 7)
    private byte[] _filterInfo = [];

    private Vp8Planes? _planes;

    private Vp8Decoder(AllocationScope scope, byte[] data, CancellationToken cancellationToken)
    {
        _scope = scope;
        _data = data;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Parses the 10-byte key-frame header (frame tag, start code, dimensions).</summary>
    /// <param name="data">At least the first 10 bytes of the payload.</param>
    /// <param name="payloadLength">The payload length (the first partition must fit in it).</param>
    /// <exception cref="InvalidImageContentException">The header is invalid.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The frame is an interframe or is not shown.</exception>
    public static Vp8FrameHeader ReadFrameHeader(ReadOnlySpan<byte> data, long payloadLength)
    {
        if (data.Length < FrameHeaderLength || payloadLength < FrameHeaderLength)
            throw Invalid("The WebP lossy (VP8) chunk is shorter than its 10-byte key-frame header.");

        var tag = data[0] | (data[1] << 8) | (data[2] << 16);
        var isKeyFrame = (tag & 1) == 0;
        var version = (tag >> 1) & 7;
        var showFrame = ((tag >> 4) & 1) != 0;
        var firstPartitionSize = tag >> 5;
        if (!isKeyFrame)
            throw new UnsupportedImageFeatureException("The WebP lossy (VP8) frame is an interframe; WebP images are key frames.", ImageFormat.WebP, "VP8 interframe");

        if (version > 3)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The VP8 version {version} is not defined (0 to 3)."));

        if (!showFrame)
            throw new UnsupportedImageFeatureException("The WebP lossy (VP8) frame is not shown.", ImageFormat.WebP, "VP8 hidden frame");

        if (data[3] != 0x9D || data[4] != 0x01 || data[5] != 0x2A)
            throw Invalid("The VP8 key-frame start code is invalid.");

        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) & 0x3FFF;
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) & 0x3FFF;
        if (width == 0 || height == 0)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The VP8 frame size {width}x{height} is empty."));

        if (firstPartitionSize > payloadLength - FrameHeaderLength)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The VP8 first partition ({firstPartitionSize} bytes) extends past the end of the chunk."));

        return new Vp8FrameHeader(width, height, firstPartitionSize);
    }

    /// <summary>Decodes a VP8 chunk payload into Y'CbCr planes.</summary>
    /// <returns>The planes; the caller disposes them.</returns>
    public static Vp8Planes Decode(AllocationScope scope, byte[] data, int offset, int length, CancellationToken cancellationToken)
    {
        using var decoder = new Vp8Decoder(scope, data, cancellationToken);
        return decoder.DecodeFrame(offset, length);
    }

    public void Dispose()
    {
        foreach (var buffer in _buffers)
        {
            buffer.Dispose();
        }

        _buffers.Clear();
        _planes?.Dispose();
        _planes = null;
    }

    private Vp8Planes DecodeFrame(int offset, int length)
    {
        var frame = ReadFrameHeader(_data.AsSpan(offset, Math.Min(length, FrameHeaderLength)), length);
        _width = frame.Width;
        _height = frame.Height;
        _mbWidth = (_width + 15) >> 4;
        _mbHeight = (_height + 15) >> 4;
        _header = new Vp8BoolDecoder(_data, offset + FrameHeaderLength, frame.FirstPartitionSize);
        ParseFrameHeader();
        SetupPartitions(offset + FrameHeaderLength + frame.FirstPartitionSize, length - FrameHeaderLength - frame.FirstPartitionSize);

        _planes = Vp8Planes.Create(_scope, _width, _height);
        _aboveModes = new byte[_mbWidth * 4];
        _aboveNonZero = new byte[_mbWidth * 9];
        var filterBuffer = _scope.Rent(_mbWidth * _mbHeight, AllocationKind.DecoderState, clear: true);
        _buffers.Add(filterBuffer);
        _filterInfo = filterBuffer.RawBuffer;
        for (var mbY = 0; mbY < _mbHeight; mbY++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var tokens = _partitions[mbY & (_partitions.Length - 1)];
            Array.Clear(_leftModes);
            Array.Clear(_leftNonZero);
            for (var mbX = 0; mbX < _mbWidth; mbX++)
            {
                DecodeMacroblock(mbX, mbY, tokens);
            }

            if (_header.OverrunBytes > MaxOverrunBytes || tokens.OverrunBytes > MaxOverrunBytes)
                throw Invalid("The VP8 data is truncated (a partition ends before the last macroblock that uses it).");
        }

        if (_filterLevel != 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Vp8LoopFilter.Apply(_planes, _filterInfo, _mbWidth, _mbHeight, _simpleFilter, _sharpness);
        }

        var planes = _planes;
        _planes = null;
        return planes;
    }

    private void ParseFrameHeader()
    {
        var d = _header;
        _ = d.ReadBit(); // color space: 0 is BT.601 (the only defined value)
        _ = d.ReadBit(); // clamping type: reconstruction always clamps
        _segmentationEnabled = d.ReadBit() != 0;
        if (_segmentationEnabled)
        {
            _updateSegmentMap = d.ReadBit() != 0;
            var updateData = d.ReadBit() != 0;
            if (updateData)
            {
                _absoluteSegmentValues = d.ReadBit() != 0;
                for (var i = 0; i < 4; i++)
                {
                    _segmentQuantizers[i] = d.ReadOptionalSigned(7);
                }

                for (var i = 0; i < 4; i++)
                {
                    _segmentLoopFilters[i] = d.ReadOptionalSigned(6);
                }
            }

            if (_updateSegmentMap)
            {
                for (var i = 0; i < 3; i++)
                {
                    _segmentProbabilities[i] = d.ReadBit() != 0 ? (byte)d.ReadLiteral(8) : (byte)255;
                }
            }
        }

        _simpleFilter = d.ReadBit() != 0;
        _filterLevel = d.ReadLiteral(6);
        _sharpness = d.ReadLiteral(3);
        _filterDeltasEnabled = d.ReadBit() != 0;
        if (_filterDeltasEnabled && d.ReadBit() != 0)
        {
            for (var i = 0; i < 4; i++)
            {
                _referenceDeltas[i] = d.ReadOptionalSigned(6);
            }

            for (var i = 0; i < 4; i++)
            {
                _modeDeltas[i] = d.ReadOptionalSigned(6);
            }
        }

        var partitionCount = 1 << d.ReadLiteral(2);
        _partitions = new Vp8BoolDecoder[partitionCount];
        ParseQuantizers();
        _ = d.ReadBit(); // refresh_entropy_probs: irrelevant for a single key frame
        Vp8Tables.DefaultTokenProbabilities.CopyTo(_tokenProbabilities);
        var update = Vp8Tables.TokenUpdateProbabilities;
        for (var i = 0; i < _tokenProbabilities.Length; i++)
        {
            if (d.ReadBool(update[i]))
            {
                _tokenProbabilities[i] = (byte)d.ReadLiteral(8);
            }
        }

        _skipEnabled = d.ReadBit() != 0;
        _skipProbability = _skipEnabled ? d.ReadLiteral(8) : 0;
        if (d.OverrunBytes > MaxOverrunBytes)
            throw Invalid("The VP8 frame header is truncated.");

        ComputeFilterLevels();
    }

    private void ParseQuantizers()
    {
        var d = _header;
        var yAc = d.ReadLiteral(7);
        var yDcDelta = d.ReadOptionalSigned(4);
        var y2DcDelta = d.ReadOptionalSigned(4);
        var y2AcDelta = d.ReadOptionalSigned(4);
        var uvDcDelta = d.ReadOptionalSigned(4);
        var uvAcDelta = d.ReadOptionalSigned(4);
        for (var segment = 0; segment < 4; segment++)
        {
            var q = yAc;
            if (_segmentationEnabled)
            {
                q = _absoluteSegmentValues ? _segmentQuantizers[segment] : q + _segmentQuantizers[segment];
            }

            _quantizers[segment] = new Quantizer(
                YDc: Vp8Tables.DcQuantizer[Clamp127(q + yDcDelta)],
                YAc: Vp8Tables.AcQuantizer[Clamp127(q)],
                Y2Dc: (short)(Vp8Tables.DcQuantizer[Clamp127(q + y2DcDelta)] * 2),
                Y2Ac: (short)Math.Max(8, Vp8Tables.AcQuantizer[Clamp127(q + y2AcDelta)] * 155 / 100),
                UVDc: (short)Math.Min(132, (int)Vp8Tables.DcQuantizer[Clamp127(q + uvDcDelta)]),
                UVAc: Vp8Tables.AcQuantizer[Clamp127(q + uvAcDelta)]);
        }

        static int Clamp127(int value) => Math.Clamp(value, 0, 127);
    }

    private void ComputeFilterLevels()
    {
        for (var segment = 0; segment < 4; segment++)
        {
            var level = _filterLevel;
            if (_segmentationEnabled)
            {
                level = _absoluteSegmentValues ? _segmentLoopFilters[segment] : level + _segmentLoopFilters[segment];
            }

            _segmentFilterLevels[segment] = (byte)Math.Clamp(level, 0, 63);
        }
    }

    private void SetupPartitions(int start, int length)
    {
        var count = _partitions.Length;
        var tableLength = 3 * (count - 1);
        if (length < tableLength)
            throw Invalid("The VP8 partition size table extends past the end of the chunk.");

        var position = start + tableLength;
        var end = start + length;
        for (var i = 0; i < count; i++)
        {
            int size;
            if (i < count - 1)
            {
                var entry = start + (3 * i);
                size = _data[entry] | (_data[entry + 1] << 8) | (_data[entry + 2] << 16);
                if (size > end - position)
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The VP8 token partition {i} ({size} bytes) extends past the end of the chunk."));
            }
            else
            {
                size = end - position;
                if (size <= 0)
                    throw Invalid("The last VP8 token partition is empty: the data is truncated.");
            }

            _partitions[i] = new Vp8BoolDecoder(_data, position, size);
            position += size;
        }
    }

    private void DecodeMacroblock(int mbX, int mbY, Vp8BoolDecoder tokens)
    {
        var d = _header;
        var segment = 0;
        if (_updateSegmentMap)
        {
            segment = d.ReadTree(SegmentTree, _segmentProbabilities);
        }

        var skip = _skipEnabled && d.ReadBool(_skipProbability);
        var yMode = d.ReadTree(KeyFrameYModeTree, Vp8Tables.KeyFrameYModeProbabilities);
        Span<byte> subModes = stackalloc byte[16];
        var aboveModes = _aboveModes.AsSpan(mbX * 4, 4);
        if (yMode == Vp8Tables.BPred)
        {
            var probabilities = Vp8Tables.KeyFrameBModeProbabilities;
            for (var y = 0; y < 4; y++)
            {
                var left = _leftModes[y];
                for (var x = 0; x < 4; x++)
                {
                    var above = aboveModes[x];
                    var mode = (byte)d.ReadTree(BModeTree, probabilities.Slice(((above * 10) + left) * 9, 9));
                    subModes[(y * 4) + x] = mode;
                    aboveModes[x] = mode;
                    left = mode;
                }

                _leftModes[y] = left;
            }
        }
        else
        {
            // The implied sub-block mode of a 16x16 mode, for the contexts of later B_PRED macroblocks
            var implied = yMode switch
            {
                Vp8Tables.VPred => Vp8Tables.BVePred,
                Vp8Tables.HPred => Vp8Tables.BHePred,
                Vp8Tables.TmPred => Vp8Tables.BTmPred,
                _ => Vp8Tables.BDcPred,
            };
            aboveModes.Fill((byte)implied);
            _leftModes.AsSpan().Fill((byte)implied);
        }

        var uvMode = d.ReadTree(UVModeTree, Vp8Tables.KeyFrameUVModeProbabilities);

        // Residuals
        var coefficients = _coefficients.AsSpan();
        coefficients.Clear();
        var hasY2 = yMode != Vp8Tables.BPred;
        var nonZero = false;
        var aboveNonZero = _aboveNonZero.AsSpan(mbX * 9, 9);
        var leftNonZero = _leftNonZero.AsSpan();
        if (!skip)
        {
            nonZero = DecodeResiduals(tokens, segment, hasY2, aboveNonZero, leftNonZero);
        }
        else
        {
            // Contexts of a skipped macroblock: no coefficient, except that a B_PRED macroblock leaves the Y2 contexts unchanged
            aboveNonZero[..8].Clear();
            leftNonZero[..8].Clear();
            if (hasY2)
            {
                aboveNonZero[8] = 0;
                leftNonZero[8] = 0;
            }
        }

        // Loop-filter level and inner-edge flag of the macroblock
        var level = (int)_segmentFilterLevels[segment];
        if (_filterDeltasEnabled)
        {
            level += _referenceDeltas[0]; // intra frame
            if (!hasY2)
            {
                level += _modeDeltas[0]; // B_PRED
            }

            level = Math.Clamp(level, 0, 63);
        }

        var inner = !hasY2 || nonZero;
        _filterInfo[(mbY * _mbWidth) + mbX] = (byte)(level | (inner ? 0x80 : 0));

        Vp8Reconstruction.ReconstructMacroblock(_planes!, mbX, mbY, _mbWidth, yMode, subModes, uvMode, coefficients);
    }

    /// <returns><see langword="true"/> when at least one block has a decoded non-zero coefficient.</returns>
    private bool DecodeResiduals(Vp8BoolDecoder tokens, int segment, bool hasY2, Span<byte> above, Span<byte> left)
    {
        var q = _quantizers[segment];
        var coefficients = _coefficients.AsSpan();
        var nonZero = false;
        var firstCoefficient = 0;
        int yType;
        if (hasY2)
        {
            // The Y2 block holds the DC of the 16 luma blocks: inverse WHT into coefficient 0 of each block
            var y2 = coefficients.Slice(24 * 16, 16);
            var eob = DecodeBlock(tokens, 1, above[8] + left[8], 0, y2, q.Y2Dc, q.Y2Ac);
            var hasCoefficients = eob > 0;
            above[8] = left[8] = hasCoefficients ? (byte)1 : (byte)0;
            nonZero |= hasCoefficients;
            Vp8Transforms.InverseWalshHadamard(y2, coefficients);
            firstCoefficient = 1;
            yType = 0;
        }
        else
        {
            yType = 3;
        }

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var block = coefficients.Slice(((y * 4) + x) * 16, 16);
                var eob = DecodeBlock(tokens, yType, above[x] + left[y], firstCoefficient, block, q.YDc, q.YAc);
                var hasCoefficients = eob > firstCoefficient;
                above[x] = left[y] = hasCoefficients ? (byte)1 : (byte)0;
                nonZero |= hasCoefficients;
            }
        }

        // U then V: 2x2 blocks each; contexts 4-5 (U) and 6-7 (V)
        for (var plane = 0; plane < 2; plane++)
        {
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 2; x++)
                {
                    var index = 16 + (plane * 4) + (y * 2) + x;
                    var context = 4 + (plane * 2);
                    var block = coefficients.Slice(index * 16, 16);
                    var eob = DecodeBlock(tokens, 2, above[context + x] + left[context + y], 0, block, q.UVDc, q.UVAc);
                    var hasCoefficients = eob > 0;
                    above[context + x] = left[context + y] = hasCoefficients ? (byte)1 : (byte)0;
                    nonZero |= hasCoefficients;
                }
            }
        }

        return nonZero;
    }

    /// <summary>Decodes the tokens of one block (RFC 6386 section 13) into dequantized coefficients in raster order.</summary>
    /// <returns>The position following the last decoded token (the end-of-block position, or 16).</returns>
    private int DecodeBlock(Vp8BoolDecoder d, int type, int context, int first, Span<short> block, short dcFactor, short acFactor)
    {
        var probabilities = _tokenProbabilities.AsSpan(type * 8 * 3 * Vp8Tables.TokenNodes, 8 * 3 * Vp8Tables.TokenNodes);
        var bands = Vp8Tables.Bands;
        var zigzag = Vp8Tables.Zigzag;
        var i = first;
        var p = probabilities.Slice(((bands[i] * 3) + context) * Vp8Tables.TokenNodes, Vp8Tables.TokenNodes);
        while (i < 16)
        {
            if (!d.ReadBool(p[0]))
                return i; // end of block

            while (!d.ReadBool(p[1]))
            {
                // DCT_0: the next token cannot be an end of block
                if (++i == 16)
                    return 16;

                p = probabilities.Slice(bands[i] * 3 * Vp8Tables.TokenNodes, Vp8Tables.TokenNodes);
            }

            int value;
            int nextContext;
            if (!d.ReadBool(p[2]))
            {
                value = 1;
                nextContext = 1;
            }
            else
            {
                value = ReadLargeValue(d, p);
                nextContext = 2;
            }

            if (d.ReadBit() != 0)
            {
                value = -value;
            }

            block[zigzag[i]] = (short)(value * (i > 0 ? acFactor : dcFactor));
            if (++i == 16)
                return 16;

            p = probabilities.Slice(((bands[i] * 3) + nextContext) * Vp8Tables.TokenNodes, Vp8Tables.TokenNodes);
        }

        return 16;
    }

    private static int ReadLargeValue(Vp8BoolDecoder d, ReadOnlySpan<byte> p)
    {
        if (!d.ReadBool(p[3]))
        {
            if (!d.ReadBool(p[4]))
                return 2;

            return 3 + (d.ReadBool(p[5]) ? 1 : 0);
        }

        if (!d.ReadBool(p[6]))
        {
            if (!d.ReadBool(p[7]))
                return 5 + (d.ReadBool(159) ? 1 : 0); // DCT_CAT1: 5-6

            var high = d.ReadBool(165) ? 2 : 0;
            return 7 + high + (d.ReadBool(145) ? 1 : 0); // DCT_CAT2: 7-10
        }

        var b1 = d.ReadBool(p[8]) ? 1 : 0;
        var b0 = d.ReadBool(p[9 + b1]) ? 1 : 0;
        var category = (2 * b1) + b0; // DCT_CAT3 to DCT_CAT6
        var extra = category switch
        {
            0 => Vp8Tables.Cat3Probabilities,
            1 => Vp8Tables.Cat4Probabilities,
            2 => Vp8Tables.Cat5Probabilities,
            _ => Vp8Tables.Cat6Probabilities,
        };
        var value = 0;
        foreach (var probability in extra)
        {
            value = (value << 1) | (d.ReadBool(probability) ? 1 : 0);
        }

        return value + 3 + (8 << category);
    }

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.WebP);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Quantizer(short YDc, short YAc, short Y2Dc, short Y2Ac, short UVDc, short UVAc);
}
