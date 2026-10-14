using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
/// Each coefficient has a word of flags (<see cref="Jpeg2000Tier1Tables"/>): the significance of its eight neighbors, the
/// sign of the four nearest ones, and its own state. A coefficient that becomes significant updates the flags of its
/// neighbors, so that a context is one table lookup. The flags are stored in the order of the passes: stripe by stripe,
/// with the four words of a column of a stripe next to each other, and a border of one column on each side and of one
/// stripe above and below. A pass tests the four words of a column at once and skips the columns that it does not code.
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
    private int _stripeStride;
    private bool _isVerticallyCausal;

    public Jpeg2000Tier1Decoder(AllocationScope scope)
    {
        _flagBuffer = Av1PooledArray<uint>.Rent(scope, Jpeg2000Tier1Tables.MaxStripeFlags, clear: false);
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
        _stripeStride = Jpeg2000Tier1Tables.GetStripeStride(width);
        _isVerticallyCausal = (style & Jpeg2000CodingStyle.StyleVerticallyCausal) != 0;
        samples = samples[..(width * height)];
        samples.Clear();
        var flags = _flagBuffer.Span[..Jpeg2000Tier1Tables.GetStripeFlagCount(width, height)];
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
                    case 0 when isRaw:
                        DecodeRawSignificancePropagation(samples, flags, plane);
                        break;

                    case 0:
                        DecodeSignificancePropagation(samples, flags, zeroCoding, plane);
                        break;

                    case 1 when isRaw:
                        DecodeRawMagnitudeRefinement(samples, flags, plane);
                        break;

                    case 1:
                        DecodeMagnitudeRefinement(samples, flags, plane);
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

    // The passes that use the arithmetic decoder work on a copy of it, so that its registers stay in processor registers;
    // the copy is stored back at the end of the pass. The flags, the coefficients and the contexts are addressed through
    // references: every index is within the code-block and its border, and every context comes from the tables
    private void DecodeSignificancePropagation(Span<int> samples, Span<uint> flags, ReadOnlySpan<byte> zeroCoding, int plane)
    {
        var width = _width;
        var height = _height;
        var stripeStride = _stripeStride;
        ref var flagStart = ref unsafe(MemoryMarshal.GetReference(flags));
        ref var sampleStart = ref unsafe(MemoryMarshal.GetReference(samples));
        ref var contexts = ref unsafe(MemoryMarshal.GetArrayDataReference(_contexts));
        ref var zeroCodingContexts = ref unsafe(MemoryMarshal.GetReference(zeroCoding));
        var mq = _mq;
        for (int stripe = 0, first = stripeStride + 4; stripe < height; stripe += 4, first += stripeStride)
        {
            var rows = Math.Min(4, height - stripe);
            for (var x = 0; x < width; x++)
            {
                // A column without an insignificant coefficient that has a significant neighbor is not coded: coding a
                // coefficient of the column only changes the neighbors of the others
                ref var column = ref unsafe(Unsafe.Add(ref flagStart, first + (x * 4)));
                if (!Jpeg2000Tier1Tables.HasSignificanceCandidate(ref column))
                    continue;

                for (var row = 0; row < rows; row++)
                {
                    ref var flag = ref unsafe(Unsafe.Add(ref column, row));
                    var value = flag;

                    // An insignificant coefficient with a significant neighbor
                    if ((value & Significant) != 0 || (value & NeighborMask) == 0)
                        continue;

                    if (mq.Decode(ref unsafe(Unsafe.Add(ref contexts, Unsafe.Add(ref zeroCodingContexts, (int)(value & NeighborMask))))) != 0)
                    {
                        var negative = DecodeSign(ref mq, ref contexts, value);
                        SetSignificant(ref unsafe(Unsafe.Add(ref sampleStart, ((stripe + row) * width) + x)), ref flag, row, negative, plane);
                    }

                    flag |= Visited;
                }
            }
        }

        _mq = mq;
    }

    private void DecodeRawSignificancePropagation(Span<int> samples, Span<uint> flags, int plane)
    {
        var width = _width;
        var height = _height;
        var stripeStride = _stripeStride;
        ref var flagStart = ref unsafe(MemoryMarshal.GetReference(flags));
        ref var sampleStart = ref unsafe(MemoryMarshal.GetReference(samples));
        for (int stripe = 0, first = stripeStride + 4; stripe < height; stripe += 4, first += stripeStride)
        {
            var rows = Math.Min(4, height - stripe);
            for (var x = 0; x < width; x++)
            {
                ref var column = ref unsafe(Unsafe.Add(ref flagStart, first + (x * 4)));
                if (!Jpeg2000Tier1Tables.HasSignificanceCandidate(ref column))
                    continue;

                for (var row = 0; row < rows; row++)
                {
                    ref var flag = ref unsafe(Unsafe.Add(ref column, row));
                    if ((flag & Significant) != 0 || (flag & NeighborMask) == 0)
                        continue;

                    if (ReadRawBit() != 0)
                    {
                        SetSignificant(ref unsafe(Unsafe.Add(ref sampleStart, ((stripe + row) * width) + x)), ref flag, row, ReadRawBit() != 0, plane);
                    }

                    flag |= Visited;
                }
            }
        }
    }

    private void DecodeMagnitudeRefinement(Span<int> samples, Span<uint> flags, int plane)
    {
        var width = _width;
        var height = _height;
        var stripeStride = _stripeStride;
        var step = 1 << plane;
        ref var flagStart = ref unsafe(MemoryMarshal.GetReference(flags));
        ref var sampleStart = ref unsafe(MemoryMarshal.GetReference(samples));
        ref var contexts = ref unsafe(MemoryMarshal.GetArrayDataReference(_contexts));
        var mq = _mq;
        for (int stripe = 0, first = stripeStride + 4; stripe < height; stripe += 4, first += stripeStride)
        {
            var rows = Math.Min(4, height - stripe);
            for (var x = 0; x < width; x++)
            {
                ref var column = ref unsafe(Unsafe.Add(ref flagStart, first + (x * 4)));
                if (!Jpeg2000Tier1Tables.HasRefinementCandidate(ref column))
                    continue;

                for (var row = 0; row < rows; row++)
                {
                    ref var flag = ref unsafe(Unsafe.Add(ref column, row));
                    var value = flag;

                    // A coefficient that was significant before this bit-plane
                    if ((value & (Significant | Visited)) != Significant)
                        continue;

                    // Table D.4
                    var context = (value & Refined) != 0 ? ContextMagnitude + 2 : (value & NeighborMask) != 0 ? ContextMagnitude + 1 : ContextMagnitude;
                    ref var sample = ref unsafe(Unsafe.Add(ref sampleStart, ((stripe + row) * width) + x));
                    sample += mq.Decode(ref unsafe(Unsafe.Add(ref contexts, context))) != 0 ? step : -step;
                    flag = value | Refined;
                }
            }
        }

        _mq = mq;
    }

    private void DecodeRawMagnitudeRefinement(Span<int> samples, Span<uint> flags, int plane)
    {
        var width = _width;
        var height = _height;
        var stripeStride = _stripeStride;
        var step = 1 << plane;
        ref var flagStart = ref unsafe(MemoryMarshal.GetReference(flags));
        ref var sampleStart = ref unsafe(MemoryMarshal.GetReference(samples));
        for (int stripe = 0, first = stripeStride + 4; stripe < height; stripe += 4, first += stripeStride)
        {
            var rows = Math.Min(4, height - stripe);
            for (var x = 0; x < width; x++)
            {
                ref var column = ref unsafe(Unsafe.Add(ref flagStart, first + (x * 4)));
                if (!Jpeg2000Tier1Tables.HasRefinementCandidate(ref column))
                    continue;

                for (var row = 0; row < rows; row++)
                {
                    ref var flag = ref unsafe(Unsafe.Add(ref column, row));
                    if ((flag & (Significant | Visited)) != Significant)
                        continue;

                    ref var sample = ref unsafe(Unsafe.Add(ref sampleStart, ((stripe + row) * width) + x));
                    sample += ReadRawBit() != 0 ? step : -step;
                    flag |= Refined;
                }
            }
        }
    }

    private void DecodeCleanup(Span<int> samples, Span<uint> flags, ReadOnlySpan<byte> zeroCoding, int plane)
    {
        var width = _width;
        var height = _height;
        var stripeStride = _stripeStride;
        ref var flagStart = ref unsafe(MemoryMarshal.GetReference(flags));
        ref var sampleStart = ref unsafe(MemoryMarshal.GetReference(samples));
        ref var contexts = ref unsafe(MemoryMarshal.GetArrayDataReference(_contexts));
        ref var zeroCodingContexts = ref unsafe(MemoryMarshal.GetReference(zeroCoding));
        var mq = _mq;
        for (int stripe = 0, first = stripeStride + 4; stripe < height; stripe += 4, first += stripeStride)
        {
            var rows = Math.Min(4, height - stripe);
            for (var x = 0; x < width; x++)
            {
                ref var column = ref unsafe(Unsafe.Add(ref flagStart, first + (x * 4)));
                var row = 0;
                if (rows == 4 && Jpeg2000Tier1Tables.IsRunLengthColumn(ref column))
                {
                    // D.3.4: the four coefficients of the column are insignificant and have no significant neighbor
                    if (mq.Decode(ref unsafe(Unsafe.Add(ref contexts, ContextRunLength))) == 0)
                        continue;

                    ref var uniform = ref unsafe(Unsafe.Add(ref contexts, ContextUniform));
                    row = mq.Decode(ref uniform) << 1;
                    row |= mq.Decode(ref uniform);
                    ref var flag = ref unsafe(Unsafe.Add(ref column, row));
                    var negative = DecodeSign(ref mq, ref contexts, flag);
                    SetSignificant(ref unsafe(Unsafe.Add(ref sampleStart, ((stripe + row) * width) + x)), ref flag, row, negative, plane);
                    row++;
                }
                else if (!Jpeg2000Tier1Tables.HasCleanupCandidate(ref column))
                {
                    // Every coefficient of the column is significant or was coded by the significance propagation pass
                    Jpeg2000Tier1Tables.ClearVisited(ref column);
                    continue;
                }

                for (; row < rows; row++)
                {
                    ref var flag = ref unsafe(Unsafe.Add(ref column, row));
                    var value = flag;
                    if ((value & (Significant | Visited)) == 0 && mq.Decode(ref unsafe(Unsafe.Add(ref contexts, Unsafe.Add(ref zeroCodingContexts, (int)(value & NeighborMask))))) != 0)
                    {
                        var negative = DecodeSign(ref mq, ref contexts, value);
                        SetSignificant(ref unsafe(Unsafe.Add(ref sampleStart, ((stripe + row) * width) + x)), ref flag, row, negative, plane);
                    }

                    flag &= ~Visited;
                }
            }
        }

        _mq = mq;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool DecodeSign(ref Jpeg2000MqDecoder mq, ref byte contexts, uint flag)
    {
        var entry = Jpeg2000Tier1Tables.GetSignContext(flag);
        var bit = mq.Decode(ref unsafe(Unsafe.Add(ref contexts, entry & 0x7F)));
        return (bit ^ (entry >> 7)) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetSignificant(ref int sample, ref uint flag, int row, bool negative, int plane)
    {
        var value = 3u << plane;
        sample = (int)(negative ? value | SignBit : value);
        Jpeg2000Tier1Tables.SetSignificantInStripes(ref flag, row, _stripeStride, negative, updateNorth: !(_isVerticallyCausal && row == 0));
    }
}
