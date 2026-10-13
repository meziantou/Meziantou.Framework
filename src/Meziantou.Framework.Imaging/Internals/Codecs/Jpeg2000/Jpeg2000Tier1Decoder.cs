namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoder of the coding passes of a code-block (T.800, Annex D): the significance propagation, magnitude refinement
/// and cleanup passes over the bit-planes, with the arithmetic decoder of Annex C or, for the passes that bypass it
/// (D.6), with raw bits.
/// </summary>
/// <remarks>
/// <para>
/// A coefficient is returned as a 32-bit word: its sign in the most significant bit, and in the other bits twice its
/// magnitude, with the bit-planes that were not decoded replaced by half of the range they span (the reconstruction
/// parameter of E.1.1.2 is 1/2). A coefficient that becomes significant at the bit-plane <c>p</c> is <c>3 &lt;&lt; p</c>,
/// that is 1.5 x 2^p; a refinement at the bit-plane <c>p</c> adds or subtracts <c>1 &lt;&lt; p</c>. When every bit-plane is
/// decoded, the word shifted right by one is the magnitude.
/// </para>
/// <para>
/// Each coefficient has a word of flags, in an array with a border of one coefficient: the significance of its eight
/// neighbors, the sign of the four nearest ones, and its own state. A coefficient that becomes significant updates the
/// flags of its neighbors, so that a context is one table lookup.
/// </para>
/// </remarks>
internal sealed class Jpeg2000Tier1Decoder : IDisposable
{
    private const uint NeighborMask = Jpeg2000Tier1Tables.NeighborMask;
    private const uint Significant = Jpeg2000Tier1Tables.Significant;
    private const uint Visited = Jpeg2000Tier1Tables.Visited;
    private const uint Refined = Jpeg2000Tier1Tables.Refined;
    private const int ContextMagnitude = Jpeg2000Tier1Tables.ContextMagnitude;
    private const int ContextRunLength = Jpeg2000Tier1Tables.ContextRunLength;
    private const int ContextUniform = Jpeg2000Tier1Tables.ContextUniform;
    private const uint SignBit = 0x80000000;

    private readonly Av1PooledArray<uint> _flagBuffer;
    private readonly byte[] _contexts = new byte[Jpeg2000Tier1Tables.ContextCount];
    private Jpeg2000MqDecoder _mq;
    private byte[] _rawData = [];
    private int _rawPosition;
    private int _rawEnd;
    private int _rawBits;
    private int _rawByte;
    private int _width;
    private int _height;
    private int _stride;
    private bool _isVerticallyCausal;

    public Jpeg2000Tier1Decoder(AllocationScope scope)
    {
        _flagBuffer = Av1PooledArray<uint>.Rent(scope, Jpeg2000Tier1Tables.MaxFlags, clear: false);
    }

    /// <summary>Decodes the coding passes of a code-block.</summary>
    /// <param name="samples">Receives the coefficients, row by row.</param>
    /// <param name="width">The width of the code-block.</param>
    /// <param name="height">The height of the code-block.</param>
    /// <param name="orientation">The orientation of the sub-band: one of the <c>Orientation</c> constants of <see cref="Jpeg2000Tier1Tables"/>.</param>
    /// <param name="style">The style of the coding passes (<see cref="Jpeg2000CodingStyle.CodeBlockStyle"/>).</param>
    /// <param name="bitPlanes">The number of bit-planes of the code-block: those of its sub-band, less the ones the packet headers declare empty.</param>
    /// <param name="data">The array that holds the codeword segments.</param>
    /// <param name="segments">The codeword segments, in order.</param>
    /// <exception cref="InvalidImageContentException">A segmentation symbol is wrong, or the code-block has more coding passes than its bit-planes allow.</exception>
    public void Decode(Span<int> samples, int width, int height, int orientation, int style, int bitPlanes, byte[] data, ReadOnlySpan<Jpeg2000CodeBlockSegment> segments)
    {
        _width = width;
        _height = height;
        _stride = width + 2;
        _isVerticallyCausal = (style & Jpeg2000CodingStyle.StyleVerticallyCausal) != 0;
        samples = samples[..(width * height)];
        samples.Clear();
        var flags = _flagBuffer.Span[..(_stride * (height + 2))];
        flags.Clear();
        ResetContexts();

        var bypass = (style & Jpeg2000CodingStyle.StyleBypass) != 0;
        var reset = (style & Jpeg2000CodingStyle.StyleReset) != 0;
        var segmentationSymbols = (style & Jpeg2000CodingStyle.StyleSegmentationSymbols) != 0;
        var zeroCoding = Jpeg2000Tier1Tables.GetZeroCodingContexts(orientation);
        var pass = 0;
        foreach (var segment in segments)
        {
            // D.6: from the fifth bit-plane, the significance propagation and magnitude refinement passes are raw
            var isRaw = bypass && pass >= Jpeg2000CodeBlockSegment.FirstBypassPass && (pass - 1) % 3 != 2;
            if (isRaw)
            {
                _rawData = data;
                _rawPosition = segment.Offset;
                _rawEnd = segment.Offset + segment.Length;
                _rawBits = 0;
                _rawByte = 0;
            }
            else
            {
                _mq.Initialize(data, segment.Offset, segment.Length);
            }

            for (var i = 0; i < segment.PassCount; i++, pass++)
            {
                // The first pass is the cleanup pass of the most significant bit-plane; three passes follow per bit-plane
                var plane = bitPlanes - 1 - ((pass + 2) / 3);
                if (plane < 0)
                    throw Jpeg2000Format.Invalid("a code-block has more coding passes than its bit-planes allow.");

                if (reset && pass > 0)
                {
                    ResetContexts();
                }

                var type = pass == 0 ? 2 : (pass - 1) % 3;
                switch (type)
                {
                    case 0:
                        DecodeSignificancePropagation(samples, flags, zeroCoding, plane, isRaw);
                        break;

                    case 1:
                        DecodeMagnitudeRefinement(samples, flags, plane, isRaw);
                        break;

                    default:
                        DecodeCleanup(samples, flags, zeroCoding, plane);
                        if (segmentationSymbols)
                        {
                            // D.5: the symbols 1, 0, 1, 0 in the uniform context end each cleanup pass
                            ref var uniform = ref _contexts[ContextUniform];
                            var symbol = (_mq.Decode(ref uniform) << 3) | (_mq.Decode(ref uniform) << 2) | (_mq.Decode(ref uniform) << 1) | _mq.Decode(ref uniform);
                            if (symbol != 0b1010)
                                throw Jpeg2000Format.Invalid("the segmentation symbol of a code-block is wrong.");
                        }

                        break;
                }
            }
        }
    }

    public void Dispose() => _flagBuffer.Dispose();

    private void ResetContexts() => Jpeg2000Tier1Tables.ResetContexts(_contexts);

    private int ReadRawBit()
    {
        if (_rawBits == 0)
        {
            // D.6: a bit is stuffed after an FF byte; past the end of the segment, the bits are 1
            var previous = _rawByte;
            _rawByte = _rawPosition < _rawEnd ? _rawData[_rawPosition] : 0xFF;
            _rawPosition++;
            _rawBits = previous == 0xFF ? 7 : 8;
        }

        _rawBits--;
        return (_rawByte >> _rawBits) & 1;
    }

    private void DecodeSignificancePropagation(Span<int> samples, Span<uint> flags, ReadOnlySpan<byte> zeroCoding, int plane, bool isRaw)
    {
        var width = _width;
        var height = _height;
        var stride = _stride;
        for (var stripe = 0; stripe < height; stripe += 4)
        {
            var stripeEnd = Math.Min(stripe + 4, height);
            for (var x = 0; x < width; x++)
            {
                for (var y = stripe; y < stripeEnd; y++)
                {
                    var index = ((y + 1) * stride) + x + 1;
                    var flag = flags[index];

                    // An insignificant coefficient with a significant neighbor
                    if ((flag & Significant) != 0 || (flag & NeighborMask) == 0)
                        continue;

                    var bit = isRaw ? ReadRawBit() : _mq.Decode(ref _contexts[zeroCoding[(int)(flag & NeighborMask)]]);
                    if (bit != 0)
                    {
                        var negative = isRaw ? ReadRawBit() != 0 : DecodeSign(flag);
                        SetSignificant(samples, flags, x, y, negative, plane);
                    }

                    flags[index] |= Visited;
                }
            }
        }
    }

    private void DecodeMagnitudeRefinement(Span<int> samples, Span<uint> flags, int plane, bool isRaw)
    {
        var width = _width;
        var height = _height;
        var stride = _stride;
        var step = 1 << plane;
        for (var stripe = 0; stripe < height; stripe += 4)
        {
            var stripeEnd = Math.Min(stripe + 4, height);
            for (var x = 0; x < width; x++)
            {
                for (var y = stripe; y < stripeEnd; y++)
                {
                    var index = ((y + 1) * stride) + x + 1;
                    var flag = flags[index];

                    // A coefficient that was significant before this bit-plane
                    if ((flag & (Significant | Visited)) != Significant)
                        continue;

                    int bit;
                    if (isRaw)
                    {
                        bit = ReadRawBit();
                    }
                    else
                    {
                        // Table D.4
                        var context = (flag & Refined) != 0 ? ContextMagnitude + 2 : (flag & NeighborMask) != 0 ? ContextMagnitude + 1 : ContextMagnitude;
                        bit = _mq.Decode(ref _contexts[context]);
                    }

                    ref var sample = ref samples[(y * width) + x];
                    sample += bit != 0 ? step : -step;
                    flags[index] = flag | Refined;
                }
            }
        }
    }

    private void DecodeCleanup(Span<int> samples, Span<uint> flags, ReadOnlySpan<byte> zeroCoding, int plane)
    {
        var width = _width;
        var height = _height;
        var stride = _stride;
        for (var stripe = 0; stripe < height; stripe += 4)
        {
            var stripeEnd = Math.Min(stripe + 4, height);
            for (var x = 0; x < width; x++)
            {
                var y = stripe;
                var first = ((stripe + 1) * stride) + x + 1;
                if (stripeEnd - stripe == 4 && ((flags[first] | flags[first + stride] | flags[first + (2 * stride)] | flags[first + (3 * stride)]) & (Significant | Visited | NeighborMask)) == 0)
                {
                    // D.3.4: the four coefficients of the column are insignificant and have no significant neighbor
                    if (_mq.Decode(ref _contexts[ContextRunLength]) == 0)
                        continue;

                    ref var uniform = ref _contexts[ContextUniform];
                    var run = _mq.Decode(ref uniform) << 1;
                    run |= _mq.Decode(ref uniform);
                    y = stripe + run;
                    SetSignificant(samples, flags, x, y, DecodeSign(flags[((y + 1) * stride) + x + 1]), plane);
                    y++;
                }

                for (; y < stripeEnd; y++)
                {
                    var index = ((y + 1) * stride) + x + 1;
                    var flag = flags[index];
                    if ((flag & (Significant | Visited)) == 0)
                    {
                        if (_mq.Decode(ref _contexts[zeroCoding[(int)(flag & NeighborMask)]]) != 0)
                        {
                            SetSignificant(samples, flags, x, y, DecodeSign(flag), plane);
                        }
                    }

                    flags[index] &= ~Visited;
                }
            }
        }
    }

    private bool DecodeSign(uint flag)
    {
        var entry = Jpeg2000Tier1Tables.GetSignContext(flag);
        var bit = _mq.Decode(ref _contexts[entry & 0x7F]);
        return (bit ^ (entry >> 7)) != 0;
    }

    private void SetSignificant(Span<int> samples, Span<uint> flags, int x, int y, bool negative, int plane)
    {
        var value = 3u << plane;
        samples[(y * _width) + x] = (int)(negative ? value | SignBit : value);
        Jpeg2000Tier1Tables.SetSignificant(flags, ((y + 1) * _stride) + x + 1, _stride, negative, updateNorth: !(_isVerticallyCausal && (y & 3) == 0));
    }
}
