using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the three color channels of a frame coded with VarDCT (ISO/IEC 18181-1, annex I and sections C.4 to C.9): the
/// LF image (one value per block of 8x8 samples), the transform and the quantization of each block, the coefficients of
/// each pass, then the samples.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Channels: X, Y and B, in that order everywhere in this class (for a frame stored as YCbCr: Cb, Y, Cr). The bit stream
/// codes Y first, because the two others are predicted from it (chroma from luma).
/// </description></item>
/// <item><description>
/// Order of the calls: <see cref="ReadLfGlobal"/>; for each LF group <see cref="ReadLfCoefficients"/> then
/// <see cref="ReadHfMetadata"/>; <see cref="FinishLf"/>; <see cref="ReadHfGlobal"/>; then for each group
/// <see cref="BeginGroup"/>, <see cref="ReadGroupPass"/> for each pass, and <see cref="FinishGroup"/>. The coefficients
/// are only kept for the group that is being decoded.
/// </description></item>
/// <item><description>
/// The samples are float32 values computed in a fixed order of operations: the format defines them in real numbers, and
/// the reference decoders agree within rounding.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class JpegXlVarDctDecoder : IDisposable
{
    /// <summary>The side of a group of a VarDCT frame, in blocks.</summary>
    private const int GroupBlocks = JpegXlCoefficientContexts.GroupBlocks;

    /// <summary>The side of the rectangles that share the factors of the chroma from luma prediction, in blocks.</summary>
    private const int ColorTileBlocks = 8;

    private const int MaxQuantField = 256;
    private const int MaxBlockContexts = 16;
    private const int MaxLfAndQuantContexts = 64;
    private const int SharpnessCount = 8;

    private const byte BlockEmpty = 0;
    private const byte BlockCovered = 1;
    private const byte BlockFirst = 2;

    private readonly JpegXlImageHeader _image;
    private readonly JpegXlFrameHeader _frame;
    private readonly AllocationScope _scope;
    private readonly CancellationToken _cancellationToken;
    private readonly int _blocksWide;
    private readonly int _blocksHigh;
    private readonly int _tilesWide;
    private readonly int _tilesHigh;
    private readonly int[] _horizontalShift = new int[3];
    private readonly int[] _verticalShift = new int[3];
    private readonly bool _is444;
    private readonly bool _usesLfFrame;
    private readonly JpegXlImageBuffer? _lfFrame;

    // LfGlobal
    private readonly float[] _lfQuant = [1f / 4096, 1f / 512, 1f / 256];
    private readonly float[] _lfFactor = new float[3];
    private readonly int[][] _lfThresholds = [[], [], []];
    private int _globalScale;
    private uint[] _quantThresholds = [];
    private byte[] _blockContextMap = [.. JpegXlVarDctTables.DefaultBlockContextMap];
    private int _blockContextCount = 15;
    private int _lfContextCount = 1;
    private float _colorScale = 1f / 84;
    private float _baseCorrelationX;
    private float _baseCorrelationB = 1;
    private int _xFactorLf;
    private int _bFactorLf;

    // One value per block of the frame
    private readonly Av1PooledArray<float>?[] _lf = new Av1PooledArray<float>?[3];
    private Av1PooledArray<byte>? _lfIndex;
    private Av1PooledArray<byte>? _blockInfo;
    private Av1PooledArray<byte>? _sharpness;
    private Av1PooledArray<ushort>? _quantField;
    private Av1PooledArray<sbyte>? _xFromY;
    private Av1PooledArray<sbyte>? _bFromY;
    private int _largestTransformSize = 64;

    // HfGlobal
    private JpegXlDequantMatrices? _matrices;
    private JpegXlCoefficientOrders? _orders;
    private JpegXlEntropyCode?[] _codes = [];
    private int _presetCount = 1;

    // The group that is being decoded
    private readonly Av1PooledArray<float>?[] _planes = new Av1PooledArray<float>?[3];
    private Av1PooledArray<int>? _coefficients;
    private Av1PooledArray<int>? _blockOffsets;
    private Av1PooledArray<byte>? _nonZeros;
    private Av1PooledArray<float>? _work;
    private int _groupX;
    private int _groupY;
    private int _groupWidth;
    private int _groupHeight;

    /// <param name="image">The image header.</param>
    /// <param name="frame">The frame header.</param>
    /// <param name="lfFrame">The LF frame that holds the LF image of the frame, when the frame does not store it itself.</param>
    /// <param name="scope">The scope of the samples and of the working memory.</param>
    /// <param name="cancellationToken">The token checked between rows and groups.</param>
    public JpegXlVarDctDecoder(JpegXlImageHeader image, JpegXlFrameHeader frame, JpegXlImageBuffer? lfFrame, AllocationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(scope);
        _image = image;
        _frame = frame;
        _lfFrame = lfFrame;
        _usesLfFrame = (frame.Flags & JpegXlFrameHeader.FlagUseLfFrame) != 0;
        if (_usesLfFrame && lfFrame is null)
            throw JpegXlFormat.Invalid("a frame takes its LF image from an LF frame that does not exist.");

        _scope = scope;
        _cancellationToken = cancellationToken;

        // A channel that is not subsampled has the largest sampling factor of the frame
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

        _is444 = maxHorizontal == 0 && maxVertical == 0;
        _blocksWide = (int)(((frame.CodedWidth + (8u << maxHorizontal) - 1) / (8u << maxHorizontal)) << maxHorizontal);
        _blocksHigh = (int)(((frame.CodedHeight + (8u << maxVertical) - 1) / (8u << maxVertical)) << maxVertical);
        _tilesWide = (_blocksWide + ColorTileBlocks - 1) / ColorTileBlocks;
        _tilesHigh = (_blocksHigh + ColorTileBlocks - 1) / ColorTileBlocks;
    }

    /// <summary>Decodes a Modular image that is stored in the frame with the given sub-bitstream index, and undoes its transforms.</summary>
    public delegate void ModularReader(JpegXlModularImage image, int streamIndex);

    /// <summary>Gets the width of the frame, in blocks of 8 samples.</summary>
    public int BlocksWide => _blocksWide;

    /// <summary>Gets the height of the frame, in blocks of 8 samples.</summary>
    public int BlocksHigh => _blocksHigh;

    /// <summary>Gets a value indicating whether no channel is subsampled.</summary>
    public bool Is444 => _is444;

    /// <summary>Gets the sharpness of the edge-preserving filter of each block of the frame, in rows of <see cref="BlocksWide"/> values.</summary>
    private ReadOnlySpan<byte> Sharpness => _sharpness is null ? default : _sharpness.Span[..(_blocksWide * _blocksHigh)];

    /// <summary>Gets the quantization factor of each block of the frame (1 to 256), in rows of <see cref="BlocksWide"/> values.</summary>
    private ReadOnlySpan<ushort> QuantField => _quantField is null ? default : _quantField.Span[..(_blocksWide * _blocksHigh)];

    /// <summary>Gets the quantization factor of the LF image.</summary>
    public int QuantLf { get; private set; }

    /// <summary>Gets the base factor of the prediction of X from Y.</summary>
    public float BaseCorrelationX => _baseCorrelationX;

    /// <summary>Gets the base factor of the prediction of B from Y.</summary>
    public float BaseCorrelationB => _baseCorrelationB;

    /// <summary>Gets the factors of the LF dequantization of the channels X, Y and B, which also scale the samples of a Modular frame stored in XYB.</summary>
    public ReadOnlySpan<float> LfQuant => _lfQuant;

    /// <summary>Gets the number of bits by which the samples of a channel (X, Y, B) are subsampled horizontally.</summary>
    public int GetHorizontalShift(int channel) => _horizontalShift[channel];

    /// <summary>Gets the number of bits by which the samples of a channel (X, Y, B) are subsampled vertically.</summary>
    public int GetVerticalShift(int channel) => _verticalShift[channel];

    /// <summary>Gets the width of the samples of a channel, a multiple of 8 that covers the frame.</summary>
    public int GetPlaneWidth(int channel) => (_blocksWide >> _horizontalShift[channel]) * 8;

    /// <summary>Gets the height of the samples of a channel, a multiple of 8 that covers the frame.</summary>
    public int GetPlaneHeight(int channel) => (_blocksHigh >> _verticalShift[channel]) * 8;

    /// <summary>Gets the samples of a channel (X, Y, B), in rows of <see cref="GetPlaneWidth"/> values.</summary>
    public Span<float> GetPlane(int channel) => _planes[channel]!.Span[..(GetPlaneWidth(channel) * GetPlaneHeight(channel))];

    /// <summary>Gives the samples of a channel to the caller, which disposes them.</summary>
    public Av1PooledArray<float> DetachPlane(int channel)
    {
        var plane = _planes[channel] ?? throw new InvalidOperationException("The channel was not decoded.");
        _planes[channel] = null;
        return plane;
    }

    /// <summary>Reads the parameters of the LF dequantization, which every frame stores at the start of <c>LfGlobal</c>.</summary>
    public void ReadLfDequantization(JpegXlBitReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.ReadBool())
            return;

        for (var c = 0; c < 3; c++)
        {
            var value = reader.ReadF16() * (1f / 128);
            if (!(value >= 1e-8f) || !float.IsFinite(value))
                throw JpegXlFormat.Invalid("a factor of the LF dequantization is not valid.");

            _lfQuant[c] = value;
        }
    }

    /// <summary>Reads the part of <c>LfGlobal</c> that only a VarDCT frame has: the quantizer, the block contexts and the chroma from luma parameters.</summary>
    public void ReadLfGlobal(JpegXlBitReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _globalScale = (int)reader.ReadU32(1, 11, 2049, 11, 4097, 12, 8193, 16);
        QuantLf = (int)reader.ReadU32(16, 0, 1, 5, 1, 8, 1, 16);
        for (var c = 0; c < 3; c++)
        {
            _lfFactor[c] = _lfQuant[c] * (65536f / _globalScale / QuantLf);
        }

        if (!reader.ReadBool())
        {
            _lfContextCount = 1;
            for (var c = 0; c < 3; c++)
            {
                _lfThresholds[c] = new int[reader.ReadBits(4)];
                _lfContextCount *= _lfThresholds[c].Length + 1;
                for (var i = 0; i < _lfThresholds[c].Length; i++)
                {
                    _lfThresholds[c][i] = JpegXlBitReader.UnpackSigned(reader.ReadU32(0, 4, 16, 8, 272, 16, 65808, 32));
                }
            }

            _quantThresholds = new uint[reader.ReadBits(4)];
            for (var i = 0; i < _quantThresholds.Length; i++)
            {
                _quantThresholds[i] = reader.ReadU32(0, 2, 4, 3, 12, 5, 44, 8) + 1;
            }

            if (_lfContextCount * (_quantThresholds.Length + 1) > MaxLfAndQuantContexts)
                throw JpegXlFormat.Invalid("a frame has too many block contexts.");

            _blockContextMap = new byte[3 * JpegXlVarDctTables.OrderCount * _lfContextCount * (_quantThresholds.Length + 1)];
            _blockContextCount = JpegXlEntropyCode.ReadContextMap(reader, _blockContextMap, _scope);
            if (_blockContextCount > MaxBlockContexts)
                throw JpegXlFormat.Invalid("a frame has too many clusters of block contexts.");
        }

        if (!reader.ReadBool())
        {
            _colorScale = 1f / reader.ReadU32(84, 0, 256, 0, 2, 8, 258, 16);
            _baseCorrelationX = reader.ReadF16();
            _baseCorrelationB = reader.ReadF16();
            if (!(MathF.Abs(_baseCorrelationX) <= 4) || !(MathF.Abs(_baseCorrelationB) <= 4))
                throw JpegXlFormat.Invalid("a base correlation of the chroma from luma prediction is out of range.");

            _xFactorLf = (int)reader.ReadBits(8) - 128;
            _bFactorLf = (int)reader.ReadBits(8) - 128;
        }

        reader.EnsureNotOverrun();
        AllocateFrameArrays();
        if (_usesLfFrame)
        {
            // The LF image is the samples of the LF frame, which has one sample per block
            var lfFrame = _lfFrame!;
            if (!_is444 || lfFrame.Width != _blocksWide || lfFrame.Height != _blocksHigh || lfFrame.ColorChannelCount != 3)
                throw JpegXlFormat.Invalid("a frame takes its LF image from an LF frame of another size.");

            for (var c = 0; c < 3; c++)
            {
                for (var y = 0; y < _blocksHigh; y++)
                {
                    lfFrame.GetRow(c, y).CopyTo(_lf[c]!.Span.Slice(y * _blocksWide, _blocksWide));
                }
            }
        }
    }

    /// <summary>Reads the LF coefficients of an LF group: a Modular image of three channels, Y first.</summary>
    /// <param name="reader">The reader, in the section of the LF group.</param>
    /// <param name="lfGroup">The index of the LF group.</param>
    /// <param name="readModular">Decodes the Modular image.</param>
    public void ReadLfCoefficients(JpegXlBitReader reader, int lfGroup, ModularReader readModular)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(readModular);
        if (_usesLfFrame)
            return;

        GetLfGroupRectangle(lfGroup, out var x0, out var y0, out var width, out var height);
        var extraPrecision = (int)reader.ReadBits(2);
        using var image = new JpegXlModularImage(_image.BitDepth.BitsPerSample);

        // The channels of the image: Y, X, B
        ReadOnlySpan<int> order = [1, 0, 2];
        foreach (var c in order)
        {
            image.AddChannel(width >> _horizontalShift[c], height >> _verticalShift[c], 0, 0);
        }

        readModular(image, 1 + lfGroup);
        if (image.Channels.Count != 3)
            throw JpegXlFormat.Invalid("the LF coefficients do not have three channels.");

        var scale = 1f / (1 << extraPrecision);
        for (var i = 0; i < 3; i++)
        {
            var c = order[i];
            var channel = image.Channels[i];
            var channelWidth = width >> _horizontalShift[c];
            var channelHeight = height >> _verticalShift[c];
            if (channel.Width != channelWidth || channel.Height != channelHeight)
                throw JpegXlFormat.Invalid("the LF coefficients do not have the size of their LF group.");

            var factor = _lfFactor[c] * scale;
            var stride = _blocksWide >> _horizontalShift[c];
            var target = _lf[c]!.Span;
            for (var y = 0; y < channelHeight; y++)
            {
                var source = channel.GetRow(y);
                var row = target.Slice((((y0 >> _verticalShift[c]) + y) * stride) + (x0 >> _horizontalShift[c]), channelWidth);
                for (var x = 0; x < channelWidth; x++)
                {
                    row[x] = source[x] * factor;
                }
            }
        }

        if (_is444)
        {
            // The LF of X and B are predicted from the one of Y
            var kx = _baseCorrelationX + (_xFactorLf * _colorScale);
            var kb = _baseCorrelationB + (_bFactorLf * _colorScale);
            for (var y = 0; y < height; y++)
            {
                var offset = ((y0 + y) * _blocksWide) + x0;
                var rowX = _lf[0]!.Span.Slice(offset, width);
                var rowY = _lf[1]!.Span.Slice(offset, width);
                var rowB = _lf[2]!.Span.Slice(offset, width);
                for (var x = 0; x < width; x++)
                {
                    rowX[x] += kx * rowY[x];
                    rowB[x] += kb * rowY[x];
                }
            }
        }

        if (_lfContextCount > 1)
        {
            // The context of the coefficients of a block depends on the range of its quantized LF values
            var lfIndex = _lfIndex!.Span;
            var channelX = image.Channels[1];
            var channelY = image.Channels[0];
            var channelB = image.Channels[2];
            for (var y = 0; y < height; y++)
            {
                var rowX = channelX.GetRow(y >> _verticalShift[0]);
                var rowY = channelY.GetRow(y >> _verticalShift[1]);
                var rowB = channelB.GetRow(y >> _verticalShift[2]);
                var row = lfIndex.Slice(((y0 + y) * _blocksWide) + x0, width);
                for (var x = 0; x < width; x++)
                {
                    var bucket = CountAbove(_lfThresholds[0], rowX[x >> _horizontalShift[0]]);
                    bucket = (bucket * (_lfThresholds[2].Length + 1)) + CountAbove(_lfThresholds[2], rowB[x >> _horizontalShift[2]]);
                    bucket = (bucket * (_lfThresholds[1].Length + 1)) + CountAbove(_lfThresholds[1], rowY[x >> _horizontalShift[1]]);
                    row[x] = (byte)bucket;
                }
            }
        }
    }

    /// <summary>
    /// Reads the HF metadata of an LF group: a Modular image that holds the factors of the chroma from luma prediction, the
    /// transform and the quantization factor of each block that starts a transform, and the sharpness of the
    /// edge-preserving filter.
    /// </summary>
    public void ReadHfMetadata(JpegXlBitReader reader, int lfGroup, ModularReader readModular)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(readModular);
        GetLfGroupRectangle(lfGroup, out var x0, out var y0, out var width, out var height);
        var blockCount = (int)reader.ReadBits(JpegXlHybridUintConfig.CeilingLog2(width * height)) + 1;
        var tileX = x0 / ColorTileBlocks;
        var tileY = y0 / ColorTileBlocks;
        var tileWidth = (width + ColorTileBlocks - 1) / ColorTileBlocks;
        var tileHeight = (height + ColorTileBlocks - 1) / ColorTileBlocks;
        using var image = new JpegXlModularImage(_image.BitDepth.BitsPerSample);
        image.AddChannel(tileWidth, tileHeight, 3, 3);
        image.AddChannel(tileWidth, tileHeight, 3, 3);
        image.AddChannel(blockCount, 2, 0, 0);
        image.AddChannel(width, height, 0, 0);
        readModular(image, 1 + (2 * (int)_frame.LfGroupCount) + lfGroup);
        if (image.Channels.Count != 4
            || image.Channels[0].Width != tileWidth || image.Channels[0].Height != tileHeight
            || image.Channels[1].Width != tileWidth || image.Channels[1].Height != tileHeight
            || image.Channels[2].Width != blockCount || image.Channels[2].Height != 2
            || image.Channels[3].Width != width || image.Channels[3].Height != height)
        {
            throw JpegXlFormat.Invalid("the HF metadata do not have the size of their LF group.");
        }

        for (var y = 0; y < tileHeight; y++)
        {
            var sourceX = image.Channels[0].GetRow(y);
            var sourceB = image.Channels[1].GetRow(y);
            var targetX = _xFromY!.Span.Slice(((tileY + y) * _tilesWide) + tileX, tileWidth);
            var targetB = _bFromY!.Span.Slice(((tileY + y) * _tilesWide) + tileX, tileWidth);
            for (var x = 0; x < tileWidth; x++)
            {
                targetX[x] = (sbyte)Math.Clamp(sourceX[x], sbyte.MinValue, sbyte.MaxValue);
                targetB[x] = (sbyte)Math.Clamp(sourceB[x], sbyte.MinValue, sbyte.MaxValue);
            }
        }

        // The blocks are given in the order of the first block, in raster order, that no transform covers yet
        var transforms = image.Channels[2].GetRow(0);
        var factors = image.Channels[2].GetRow(1);
        var blockInfo = _blockInfo!.Span;
        var quantField = _quantField!.Span;
        var sharpness = _sharpness!.Span;
        var next = 0;
        for (var y = 0; y < height; y++)
        {
            var sourceSharpness = image.Channels[3].GetRow(y);
            for (var x = 0; x < width; x++)
            {
                if ((uint)sourceSharpness[x] >= SharpnessCount)
                    throw JpegXlFormat.Invalid("the sharpness of a block is out of range.");

                var position = ((y0 + y) * _blocksWide) + x0 + x;
                sharpness[position] = (byte)sourceSharpness[x];
                if (blockInfo[position] != BlockEmpty)
                    continue;

                if (next >= blockCount)
                    throw JpegXlFormat.Invalid("the HF metadata describe fewer transforms than the blocks need.");

                var transform = transforms[next];
                if ((uint)transform >= JpegXlVarDctTables.TransformCount)
                    throw JpegXlFormat.Invalid("a block has a transform that does not exist.");

                var blocksWide = JpegXlVarDctTables.BlocksWide[transform];
                var blocksHigh = JpegXlVarDctTables.BlocksHigh[transform];
                if (!_is444 && (blocksWide > 1 || blocksHigh > 1))
                    throw JpegXlFormat.Invalid("a transform of several blocks is used with subsampled channels.");

                // A transform stays in its group, and in the frame
                var blockX = x0 + x;
                var blockY = y0 + y;
                if (blockX + blocksWide > Math.Min(_blocksWide, ((blockX / GroupBlocks) + 1) * GroupBlocks)
                    || blockY + blocksHigh > Math.Min(_blocksHigh, ((blockY / GroupBlocks) + 1) * GroupBlocks))
                {
                    throw JpegXlFormat.Invalid("a transform crosses the boundary of its group.");
                }

                for (var by = 0; by < blocksHigh; by++)
                {
                    var row = blockInfo.Slice(((blockY + by) * _blocksWide) + blockX, blocksWide);
                    if (row.ContainsAnyExcept(BlockEmpty))
                        throw JpegXlFormat.Invalid("two transforms overlap.");

                    row.Fill(BlockCovered);
                }

                // Every block of the transform has its quantization factor, which the edge-preserving filter reads
                var factor = (ushort)(1 + Math.Clamp(factors[next], 0, MaxQuantField - 1));
                for (var by = 0; by < blocksHigh; by++)
                {
                    quantField.Slice(((blockY + by) * _blocksWide) + blockX, blocksWide).Fill(factor);
                }

                blockInfo[position] = (byte)(BlockFirst + transform);
                _largestTransformSize = Math.Max(_largestTransformSize, blocksWide * blocksHigh * 64);
                next++;
            }
        }
    }

    /// <summary>
    /// Smooths the LF image once every LF group is read (section I.2.7): where a sample is close to the weighted average of
    /// its neighbors, compared with the quantization step, it is moved towards it. The border is kept.
    /// </summary>
    public void FinishLf()
    {
        if ((_frame.Flags & JpegXlFrameHeader.FlagSkipAdaptiveLfSmoothing) != 0 || _usesLfFrame)
            return;

        if (!_is444)
            throw JpegXlFormat.Invalid("a frame with subsampled channels smooths its LF image.");

        var width = _blocksWide;
        var height = _blocksHigh;
        if (width <= 2 || height <= 2)
            return;

        const float SideWeight = 0.20345139757231578f;
        const float CornerWeight = 0.0334829185968739f;
        const float CenterWeight = 1f - (4f * (SideWeight + CornerWeight));

        // The filter reads the samples before they are smoothed: two rows of each channel are kept
        using var saved = Av1PooledArray<float>.Rent(_scope, 6L * width, clear: false);
        Span<float> smoothed = stackalloc float[3];
        for (var c = 0; c < 3; c++)
        {
            _lf[c]!.Span[..width].CopyTo(saved.Span.Slice(((c * 2) + 1) * width, width));
        }

        for (var y = 1; y < height - 1; y++)
        {
            if ((y & 63) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            // The saved rows alternate: the one of the row above, then the one of this row
            var above = (y & 1) != 0 ? 1 : 0;
            for (var c = 0; c < 3; c++)
            {
                _lf[c]!.Span.Slice(y * width, width).CopyTo(saved.Span.Slice(((c * 2) + (above ^ 1)) * width, width));
            }

            for (var x = 1; x < width - 1; x++)
            {
                var gap = 0.5f;
                for (var c = 0; c < 3; c++)
                {
                    var top = saved.Span.Slice(((c * 2) + above) * width, width);
                    var middle = saved.Span.Slice(((c * 2) + (above ^ 1)) * width, width);
                    var bottom = _lf[c]!.Span.Slice((y + 1) * width, width);
                    var corners = top[x - 1] + top[x + 1] + bottom[x - 1] + bottom[x + 1];
                    var sides = middle[x - 1] + middle[x + 1] + top[x] + bottom[x];
                    smoothed[c] = (corners * CornerWeight) + ((sides * SideWeight) + (middle[x] * CenterWeight));
                    gap = MathF.Max(gap, MathF.Abs((middle[x] - smoothed[c]) / _lfFactor[c]));
                }

                var factor = MathF.Max(0, 3f - (4f * gap));
                for (var c = 0; c < 3; c++)
                {
                    var sample = saved.Span[(((c * 2) + (above ^ 1)) * width) + x];
                    _lf[c]!.Span[(y * width) + x] = ((smoothed[c] - sample) * factor) + sample;
                }
            }
        }
    }

    /// <summary>Reads <c>HfGlobal</c>: the dequantization matrices, the number of presets, then for each pass the coefficient orders and the entropy code of the coefficients.</summary>
    /// <param name="reader">The reader, in the section of <c>HfGlobal</c>.</param>
    /// <param name="readRawTable">Reads the Modular image of a quantization table stored by its values.</param>
    public void ReadHfGlobal(JpegXlBitReader reader, JpegXlDequantMatrices.RawTableReader readRawTable)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _matrices = JpegXlDequantMatrices.Read(reader, readRawTable, _scope);
        var groupCount = checked((int)_frame.GroupCount);
        _presetCount = (int)reader.ReadBits(JpegXlHybridUintConfig.CeilingLog2(groupCount)) + 1;
        reader.EnsureNotOverrun();

        _orders = new JpegXlCoefficientOrders(_frame.PassCount, _scope);
        _codes = new JpegXlEntropyCode?[_frame.PassCount];
        var contextCount = checked(JpegXlVarDctTables.ContextsPerBlockContext * _blockContextCount * _presetCount);
        for (var pass = 0; pass < _frame.PassCount; pass++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _orders.ReadPass(reader, pass);
            _codes[pass] = JpegXlEntropyCode.Read(reader, contextCount, _scope);
            reader.EnsureNotOverrun();
        }

        for (var c = 0; c < 3; c++)
        {
            _planes[c] = Av1PooledArray<float>.Rent(_scope, (long)GetPlaneWidth(c) * GetPlaneHeight(c), clear: false);
        }

        _coefficients = Av1PooledArray<int>.Rent(_scope, 3L * GroupBlocks * GroupBlocks * 64, clear: false);
        _blockOffsets = Av1PooledArray<int>.Rent(_scope, GroupBlocks * GroupBlocks, clear: false);
        _nonZeros = Av1PooledArray<byte>.Rent(_scope, 3L * GroupBlocks * GroupBlocks, clear: false);
        _work = Av1PooledArray<float>.Rent(_scope, (3L * _largestTransformSize) + JpegXlInverseTransform.ScratchLength, clear: false);
    }

    /// <summary>Starts a group: its coefficients are zero until its passes are read.</summary>
    public void BeginGroup(int group)
    {
        _groupX = (int)(group % _frame.GroupColumns) * GroupBlocks;
        _groupY = (int)(group / _frame.GroupColumns) * GroupBlocks;
        _groupWidth = Math.Min(GroupBlocks, _blocksWide - _groupX);
        _groupHeight = Math.Min(GroupBlocks, _blocksHigh - _groupY);
        _coefficients!.Span[..(3 * _groupWidth * _groupHeight * 64)].Clear();

        // The coefficients of the transforms follow each other in the order of their first block
        var offsets = _blockOffsets!.Span;
        var blockInfo = _blockInfo!.Span;
        var offset = 0;
        for (var y = 0; y < _groupHeight; y++)
        {
            var row = blockInfo.Slice(((_groupY + y) * _blocksWide) + _groupX, _groupWidth);
            for (var x = 0; x < _groupWidth; x++)
            {
                if (row[x] < BlockFirst)
                {
                    if (row[x] == BlockEmpty)
                        throw JpegXlFormat.Invalid("a block has no transform.");

                    continue;
                }

                var transform = row[x] - BlockFirst;
                offsets[(y * GroupBlocks) + x] = offset;
                offset += JpegXlVarDctTables.BlocksWide[transform] * JpegXlVarDctTables.BlocksHigh[transform] * 64;
            }
        }
    }

    /// <summary>Reads the coefficients that a pass adds to the group that was begun (the start of a <c>PassGroup</c> section).</summary>
    public void ReadGroupPass(JpegXlBitReader reader, int pass)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var preset = (int)reader.ReadBits(JpegXlHybridUintConfig.CeilingLog2(_presetCount));
        if (preset >= _presetCount)
            throw JpegXlFormat.Invalid("a group uses a preset that does not exist.");

        var code = _codes[pass]!;
        var contextOffset = JpegXlVarDctTables.ContextsPerBlockContext * _blockContextCount * preset;
        var shift = _frame.PassShifts[pass];
        var groupArea = _groupWidth * _groupHeight * 64;
        var blockInfo = _blockInfo!.Span;
        var quantField = _quantField!.Span;
        var lfIndex = _lfIndex!.Span;
        var offsets = _blockOffsets!.Span;
        var coefficients = _coefficients!.Span;
        var nonZeros = _nonZeros!.Span;
        var orders = _orders!;
        var quantContextCount = _quantThresholds.Length + 1;
        using var decoder = new JpegXlEntropyDecoder(code, _scope);
        decoder.Begin(reader);

        // Y is coded first, then X and B
        ReadOnlySpan<int> channelOrder = [1, 0, 2];
        for (var y = 0; y < _groupHeight; y++)
        {
            if ((y & 7) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                reader.EnsureNotOverrun();
            }

            for (var x = 0; x < _groupWidth; x++)
            {
                var position = ((_groupY + y) * _blocksWide) + _groupX + x;
                if (blockInfo[position] < BlockFirst)
                    continue;

                var transform = blockInfo[position] - BlockFirst;
                var blocksWide = JpegXlVarDctTables.BlocksWide[transform];
                var blocksHigh = JpegXlVarDctTables.BlocksHigh[transform];
                var blockCount = blocksWide * blocksHigh;
                var logBlockCount = BitOperations.Log2((uint)blockCount);
                var size = blockCount * 64;
                var order = JpegXlVarDctTables.Order[transform];
                var quantIndex = CountAbove(_quantThresholds, quantField[position]);
                foreach (var c in channelOrder)
                {
                    var hs = _horizontalShift[c];
                    var vs = _verticalShift[c];
                    var cx = x >> hs;
                    var cy = y >> vs;
                    if ((cx << hs) != x || (cy << vs) != y)
                        continue;

                    // The block context: the channel (Y, X, B), the order, the range of the quantization factor and of the LF
                    var blockContext = _blockContextMap[JpegXlCoefficientContexts.GetBlockContextIndex(c, order, quantContextCount, quantIndex, _lfContextCount, lfIndex[position])];

                    // The number of non-zero coefficients is predicted from the blocks above and on the left, in the group
                    var channelNonZeros = nonZeros.Slice(c * GroupBlocks * GroupBlocks, GroupBlocks * GroupBlocks);
                    var index = (cy * GroupBlocks) + cx;
                    var predicted = JpegXlCoefficientContexts.PredictNonZeros(channelNonZeros, cx, cy);
                    var nonZeroContext = JpegXlCoefficientContexts.GetNonZeroContext(predicted, _blockContextCount, blockContext);
                    var nonZeroCount = (int)decoder.Read(reader, contextOffset + nonZeroContext);
                    if ((uint)nonZeroCount > (uint)(size - blockCount))
                        throw JpegXlFormat.Invalid("a block has more non-zero coefficients than coefficients.");

                    var stored = JpegXlCoefficientContexts.GetStoredNonZeros(nonZeroCount, blockCount, logBlockCount);
                    for (var by = 0; by < blocksHigh; by++)
                    {
                        channelNonZeros.Slice(index + (by * GroupBlocks), blocksWide).Fill(stored);
                    }

                    if (nonZeroCount == 0)
                        continue;

                    var block = coefficients.Slice((c * groupArea) + offsets[(y * GroupBlocks) + x], size);
                    var coefficientOrder = orders.GetOrder(pass, order, c);
                    var coefficientContext = contextOffset + JpegXlCoefficientContexts.GetCoefficientContextOffset(_blockContextCount, blockContext);
                    var previous = JpegXlCoefficientContexts.GetInitialPrevious(nonZeroCount, size);
                    for (var k = blockCount; k < size && nonZeroCount != 0; k++)
                    {
                        // The contexts are defined for the counts a block can still hold
                        if (nonZeroCount > size - k)
                            throw JpegXlFormat.Invalid("a block has more non-zero coefficients left than coefficients.");

                        var context = coefficientContext + JpegXlCoefficientContexts.GetCoefficientContext(nonZeroCount, k, blockCount, logBlockCount, previous);
                        var value = decoder.Read(reader, context);
                        block[coefficientOrder[k]] += JpegXlBitReader.UnpackSigned(value) << shift;
                        previous = value != 0 ? 1 : 0;
                        nonZeroCount -= previous;
                    }

                    if (nonZeroCount != 0)
                        throw JpegXlFormat.Invalid("a block has fewer coefficients than its number of non-zero coefficients.");
                }
            }
        }

        reader.EnsureNotOverrun();
        decoder.EnsureFinalState();
    }

    /// <summary>Computes the samples of the group that was begun, from the coefficients of its passes.</summary>
    public void FinishGroup()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var groupArea = _groupWidth * _groupHeight * 64;
        var blockInfo = _blockInfo!.Span;
        var quantField = _quantField!.Span;
        var offsets = _blockOffsets!.Span;
        var coefficients = _coefficients!.Span;
        var work = _work!.Span;
        var scratch = work.Slice(3 * _largestTransformSize, JpegXlInverseTransform.ScratchLength);
        var matrices = _matrices!;
        var transformData = _image.TransformData;
        var biasX = transformData.QuantBias[0];
        var biasY = transformData.QuantBias[1];
        var biasB = transformData.QuantBias[2];
        var biasNumerator = transformData.QuantBiasNumerator;

        // The X and B channels have their own scale of the quantization: a power of 0.8
        var scaleX = MathF.Pow(0.8f, _frame.XqmScale - 2);
        var scaleB = MathF.Pow(0.8f, _frame.BqmScale - 2);
        var inverseGlobalScale = 65536f / _globalScale;
        for (var y = 0; y < _groupHeight; y++)
        {
            for (var x = 0; x < _groupWidth; x++)
            {
                var blockX = _groupX + x;
                var blockY = _groupY + y;
                var position = (blockY * _blocksWide) + blockX;
                if (blockInfo[position] < BlockFirst)
                    continue;

                var transform = blockInfo[position] - BlockFirst;
                var blocksWide = JpegXlVarDctTables.BlocksWide[transform];
                var blocksHigh = JpegXlVarDctTables.BlocksHigh[transform];
                var size = blocksWide * blocksHigh * 64;
                var matrix = matrices.GetMatrix(JpegXlVarDctTables.QuantTable[transform]);
                var offset = offsets[(y * GroupBlocks) + x];
                var quantized = coefficients.Slice(offset, groupArea - offset);
                var scale = inverseGlobalScale / quantField[position];
                var dequantizedX = work[..size];
                var dequantizedY = work.Slice(_largestTransformSize, size);
                var dequantizedB = work.Slice(2 * _largestTransformSize, size);
                if (_is444)
                {
                    var tile = ((blockY / ColorTileBlocks) * _tilesWide) + (blockX / ColorTileBlocks);
                    var kx = _baseCorrelationX + (_xFromY!.Span[tile] * _colorScale);
                    var kb = _baseCorrelationB + (_bFromY!.Span[tile] * _colorScale);
                    var quantizedX = quantized[..size];
                    var quantizedY = coefficients.Slice(groupArea + offset, size);
                    var quantizedB = coefficients.Slice((2 * groupArea) + offset, size);
                    var matrixX = matrix[..size];
                    var matrixY = matrix.Slice(size, size);
                    var matrixB = matrix.Slice(2 * size, size);
                    var stepX = scale * scaleX;
                    var stepB = scale * scaleB;
                    for (var k = 0; k < size; k++)
                    {
                        var valueY = AdjustBias(quantizedY[k], biasY, biasNumerator) * (matrixY[k] * scale);
                        dequantizedY[k] = valueY;
                        dequantizedX[k] = (kx * valueY) + (AdjustBias(quantizedX[k], biasX, biasNumerator) * (matrixX[k] * stepX));
                        dequantizedB[k] = (kb * valueY) + (AdjustBias(quantizedB[k], biasB, biasNumerator) * (matrixB[k] * stepB));
                    }

                    for (var c = 0; c < 3; c++)
                    {
                        var block = work.Slice(c * _largestTransformSize, size);
                        JpegXlInverseTransform.SetLowestFrequencies(_lf[c]!.Span[position..], _blocksWide, blocksHigh, blocksWide, block, scratch);
                        var planeWidth = _blocksWide * 8;
                        JpegXlInverseTransform.Apply(transform, block, _planes[c]!.Span[((blockY * 8 * planeWidth) + (blockX * 8))..], planeWidth, scratch);
                    }
                }
                else
                {
                    // Subsampled channels: every transform is one block, and a channel has a block where its samples start
                    for (var c = 0; c < 3; c++)
                    {
                        var hs = _horizontalShift[c];
                        var vs = _verticalShift[c];
                        if (((blockX >> hs) << hs) != blockX || ((blockY >> vs) << vs) != blockY)
                            continue;

                        var channelStep = c switch
                        {
                            0 => scale * scaleX,
                            1 => scale,
                            _ => scale * scaleB,
                        };
                        var bias = transformData.QuantBias[c];
                        var source = coefficients.Slice((c * groupArea) + offset, size);
                        var channelMatrix = matrix.Slice(c * size, size);
                        var block = work[..size];
                        for (var k = 0; k < size; k++)
                        {
                            block[k] = AdjustBias(source[k], bias, biasNumerator) * (channelMatrix[k] * channelStep);
                        }

                        var channelBlocksWide = _blocksWide >> hs;
                        var channelX = blockX >> hs;
                        var channelY = blockY >> vs;
                        block[0] = _lf[c]!.Span[(channelY * channelBlocksWide) + channelX];
                        var planeWidth = channelBlocksWide * 8;
                        JpegXlInverseTransform.Apply(transform, block, _planes[c]!.Span[((channelY * 8 * planeWidth) + (channelX * 8))..], planeWidth, scratch);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Brings the subsampled channels to the size of the frame, once every group is decoded (section I.2.8): each sample
    /// becomes two, three quarters of itself and one quarter of its neighbor on that side, horizontally then vertically.
    /// </summary>
    public void UpsampleChroma()
    {
        if (_is444)
            return;

        var frameWidth = (int)_frame.CodedWidth;
        var frameHeight = (int)_frame.CodedHeight;
        var fullWidth = _blocksWide * 8;
        var fullHeight = _blocksHigh * 8;
        for (var c = 0; c < 3; c++)
        {
            var hs = _horizontalShift[c];
            var vs = _verticalShift[c];
            if (hs == 0 && vs == 0)
                continue;

            _cancellationToken.ThrowIfCancellationRequested();
            var sourceStride = GetPlaneWidth(c);
            var width = (frameWidth + (1 << hs) - 1) >> hs;
            var height = (frameHeight + (1 << vs) - 1) >> vs;
            var outputWidth = Math.Min(width << hs, fullWidth);
            Av1PooledArray<float>? owned = null;
            try
            {
                owned = Av1PooledArray<float>.Rent(_scope, (long)fullWidth * fullHeight, clear: false);
                using var rows = Av1PooledArray<float>.Rent(_scope, 3L * outputWidth, clear: false);
                var source = _planes[c]!.Span;
                var target = owned.Span;
                var above = rows.Span[..outputWidth];
                var current = rows.Span.Slice(outputWidth, outputWidth);
                var below = rows.Span.Slice(2 * outputWidth, outputWidth);

                // The rows of the channel, at the width of the frame: the one above and the one below are needed
                Widen(source, sourceStride, width, height, hs, 0, current);
                current.CopyTo(above);
                for (var y = 0; y < height; y++)
                {
                    Widen(source, sourceStride, width, height, hs, y + 1, below);
                    if (vs == 0)
                    {
                        current.CopyTo(target.Slice(y * fullWidth, outputWidth));
                    }
                    else
                    {
                        var first = target.Slice(2 * y * fullWidth, outputWidth);
                        for (var x = 0; x < outputWidth; x++)
                        {
                            first[x] = (above[x] * 0.25f) + (current[x] * 0.75f);
                        }

                        if ((2 * y) + 1 < fullHeight)
                        {
                            var second = target.Slice(((2 * y) + 1) * fullWidth, outputWidth);
                            for (var x = 0; x < outputWidth; x++)
                            {
                                second[x] = (below[x] * 0.25f) + (current[x] * 0.75f);
                            }
                        }
                    }

                    // The rows rotate: the current one becomes the one above
                    var recycled = above;
                    above = current;
                    current = below;
                    below = recycled;
                }

                _planes[c]!.Dispose();
                _planes[c] = owned;
                owned = null;
                _horizontalShift[c] = 0;
                _verticalShift[c] = 0;
            }
            finally
            {
                owned?.Dispose();
            }
        }
    }

    /// <summary>Copies a row of a channel, doubling its width when the channel is subsampled horizontally; a row outside the channel is the nearest one.</summary>
    private static void Widen(ReadOnlySpan<float> source, int sourceStride, int width, int height, int horizontalShift, int y, Span<float> destination)
    {
        var row = source.Slice(Math.Clamp(y, 0, height - 1) * sourceStride, width);
        if (horizontalShift == 0)
        {
            row.CopyTo(destination);
            return;
        }

        for (var x = 0; x < width; x++)
        {
            var scaled = row[x] * 0.75f;
            var left = (0.25f * row[Math.Max(x - 1, 0)]) + scaled;
            var right = (0.25f * row[Math.Min(x + 1, width - 1)]) + scaled;
            if (2 * x < destination.Length)
            {
                destination[2 * x] = left;
            }

            if ((2 * x) + 1 < destination.Length)
            {
                destination[(2 * x) + 1] = right;
            }
        }
    }

    /// <summary>Applies the restoration filters of the frame to the samples, once every group is decoded and the channels have the size of the frame.</summary>
    public void ApplyRestorationFilters()
    {
        var filter = _frame.RestorationFilter;
        if (!filter.IsGaborEnabled && filter.EpfIterations == 0)
            return;

        var width = (int)_frame.CodedWidth;
        var height = (int)_frame.CodedHeight;
        var stride = _blocksWide * 8;
        ReadOnlySpan<Av1PooledArray<float>> planes = [_planes[0]!, _planes[1]!, _planes[2]!];
        if (filter.IsGaborEnabled)
        {
            JpegXlRestorationFilters.ApplyGaborish(planes, stride, width, height, filter, _scope, _cancellationToken);
        }

        if (filter.EpfIterations > 0)
        {
            using var inverseSigma = Av1PooledArray<float>.Rent(_scope, (long)_blocksWide * _blocksHigh, clear: false);
            var blocks = inverseSigma.Span[..(_blocksWide * _blocksHigh)];
            JpegXlRestorationFilters.ComputeInverseSigma(filter, _globalScale, QuantField, Sharpness, blocks);
            JpegXlRestorationFilters.ApplyEdgePreservingFilter(planes, stride, width, height, filter, blocks, _blocksWide, _scope, _cancellationToken);
        }
    }

    public void Dispose()
    {
        for (var c = 0; c < 3; c++)
        {
            _lf[c]?.Dispose();
            _lf[c] = null;
            _planes[c]?.Dispose();
            _planes[c] = null;
        }

        _lfIndex?.Dispose();
        _blockInfo?.Dispose();
        _sharpness?.Dispose();
        _quantField?.Dispose();
        _xFromY?.Dispose();
        _bFromY?.Dispose();
        _matrices?.Dispose();
        _orders?.Dispose();
        foreach (var code in _codes)
        {
            code?.Dispose();
        }

        _codes = [];
        _coefficients?.Dispose();
        _blockOffsets?.Dispose();
        _nonZeros?.Dispose();
        _work?.Dispose();
        _lfIndex = null;
        _blockInfo = null;
        _sharpness = null;
        _quantField = null;
        _xFromY = null;
        _bFromY = null;
        _matrices = null;
        _orders = null;
        _coefficients = null;
        _blockOffsets = null;
        _nonZeros = null;
        _work = null;
    }

    /// <summary>
    /// The value of a quantized coefficient before it is multiplied by its quantization step: a coefficient of magnitude 1
    /// is brought towards zero by the bias of its channel, and a larger one by a bias inversely proportional to it.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static float AdjustBias(int quantized, float bias, float biasNumerator)
    {
        if (quantized == 0)
            return 0;

        if (quantized == 1)
            return bias;

        if (quantized == -1)
            return -bias;

        float value = quantized;
        return value - (biasNumerator / value);
    }

    private static int CountAbove(ReadOnlySpan<int> thresholds, int value)
    {
        var count = 0;
        foreach (var threshold in thresholds)
        {
            count += value > threshold ? 1 : 0;
        }

        return count;
    }

    private static int CountAbove(ReadOnlySpan<uint> thresholds, uint value)
    {
        var count = 0;
        foreach (var threshold in thresholds)
        {
            count += value > threshold ? 1 : 0;
        }

        return count;
    }

    private void AllocateFrameArrays()
    {
        var blockCount = (long)_blocksWide * _blocksHigh;
        for (var c = 0; c < 3; c++)
        {
            _lf[c] = Av1PooledArray<float>.Rent(_scope, (long)(_blocksWide >> _horizontalShift[c]) * (_blocksHigh >> _verticalShift[c]), clear: true);
        }

        _lfIndex = Av1PooledArray<byte>.Rent(_scope, blockCount, clear: true);
        _blockInfo = Av1PooledArray<byte>.Rent(_scope, blockCount, clear: true);
        _sharpness = Av1PooledArray<byte>.Rent(_scope, blockCount, clear: true);
        _quantField = Av1PooledArray<ushort>.Rent(_scope, blockCount, clear: true);
        _xFromY = Av1PooledArray<sbyte>.Rent(_scope, (long)_tilesWide * _tilesHigh, clear: true);
        _bFromY = Av1PooledArray<sbyte>.Rent(_scope, (long)_tilesWide * _tilesHigh, clear: true);
    }

    private void GetLfGroupRectangle(int lfGroup, out int x0, out int y0, out int width, out int height)
    {
        // An LF group has the side of a group, in blocks instead of samples
        var dimension = (int)_frame.GroupDimension;
        x0 = (int)(lfGroup % _frame.LfGroupColumns) * dimension;
        y0 = (int)(lfGroup / _frame.LfGroupColumns) * dimension;
        width = Math.Min(dimension, _blocksWide - x0);
        height = Math.Min(dimension, _blocksHigh - y0);
        if (width <= 0 || height <= 0)
            throw JpegXlFormat.Invalid("an LF group is outside the frame.");
    }
}
