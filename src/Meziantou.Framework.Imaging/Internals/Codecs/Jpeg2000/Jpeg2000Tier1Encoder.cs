namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoder of the coding passes of a code-block (T.800, Annex D), the reverse of <see cref="Jpeg2000Tier1Decoder"/>:
/// every bit-plane is coded with the three passes, in one codeword segment of the arithmetic coder of Annex C, with none
/// of the optional code-block styles.
/// </summary>
internal sealed class Jpeg2000Tier1Encoder : IDisposable
{
    private const uint NeighborMask = Jpeg2000Tier1Tables.NeighborMask;
    private const uint Significant = Jpeg2000Tier1Tables.Significant;
    private const uint Visited = Jpeg2000Tier1Tables.Visited;
    private const uint Refined = Jpeg2000Tier1Tables.Refined;
    private const int ContextMagnitude = Jpeg2000Tier1Tables.ContextMagnitude;
    private const int ContextRunLength = Jpeg2000Tier1Tables.ContextRunLength;
    private const int ContextUniform = Jpeg2000Tier1Tables.ContextUniform;

    private readonly Av1PooledArray<uint> _flagBuffer;
    private readonly byte[] _contexts = new byte[Jpeg2000Tier1Tables.ContextCount];
    private Jpeg2000MqEncoder _mq;
    private int _width;
    private int _height;
    private int _stride;

    public Jpeg2000Tier1Encoder(AllocationScope scope)
    {
        _flagBuffer = Av1PooledArray<uint>.Rent(scope, Jpeg2000Tier1Tables.MaxFlags, clear: false, AllocationKind.Temporary);
    }

    /// <summary>Encodes the coefficients of a code-block.</summary>
    /// <param name="samples">The coefficients, row by row: the magnitude in the 31 low bits and the sign in the high one.</param>
    /// <param name="width">The width of the code-block.</param>
    /// <param name="height">The height of the code-block.</param>
    /// <param name="orientation">The orientation of the sub-band: one of the <c>Orientation</c> constants of <see cref="Jpeg2000Tier1Tables"/>.</param>
    /// <param name="output">The writer the codeword segment is appended to.</param>
    /// <param name="length">Receives the number of bytes of the codeword segment.</param>
    /// <returns>The number of bit-planes that are coded: 0 when every coefficient is zero, and nothing is written.</returns>
    public int Encode(ReadOnlySpan<int> samples, int width, int height, int orientation, PooledPayloadWriter output, out int length)
    {
        length = 0;
        samples = samples[..(width * height)];
        var maximum = 0;
        foreach (var sample in samples)
        {
            maximum |= sample & int.MaxValue;
        }

        if (maximum == 0)
            return 0;

        var bitPlanes = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)maximum);
        _width = width;
        _height = height;
        _stride = width + 2;
        var flags = _flagBuffer.Span[..(_stride * (height + 2))];
        flags.Clear();
        Jpeg2000Tier1Tables.ResetContexts(_contexts);
        var zeroCoding = Jpeg2000Tier1Tables.GetZeroCodingContexts(orientation);
        _mq.Initialize(output);

        // The first bit-plane has a cleanup pass only (D.3)
        for (var plane = bitPlanes - 1; plane >= 0; plane--)
        {
            if (plane != bitPlanes - 1)
            {
                EncodeSignificancePropagation(samples, flags, zeroCoding, plane);
                EncodeMagnitudeRefinement(samples, flags, plane);
            }

            EncodeCleanup(samples, flags, zeroCoding, plane);
        }

        length = _mq.Flush();
        return bitPlanes;
    }

    public void Dispose() => _flagBuffer.Dispose();

    private void EncodeSignificancePropagation(ReadOnlySpan<int> samples, Span<uint> flags, ReadOnlySpan<byte> zeroCoding, int plane)
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
                    if ((flag & Significant) != 0 || (flag & NeighborMask) == 0)
                        continue;

                    var sample = samples[(y * width) + x];
                    var bit = (sample >> plane) & 1;
                    _mq.Encode(bit, ref _contexts[zeroCoding[(int)(flag & NeighborMask)]]);
                    if (bit != 0)
                    {
                        EncodeSign(flag, sample < 0);
                        Jpeg2000Tier1Tables.SetSignificant(flags, index, stride, sample < 0, updateNorth: true);
                    }

                    flags[index] |= Visited;
                }
            }
        }
    }

    private void EncodeMagnitudeRefinement(ReadOnlySpan<int> samples, Span<uint> flags, int plane)
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
                    if ((flag & (Significant | Visited)) != Significant)
                        continue;

                    var context = (flag & Refined) != 0 ? ContextMagnitude + 2 : (flag & NeighborMask) != 0 ? ContextMagnitude + 1 : ContextMagnitude;
                    _mq.Encode((samples[(y * width) + x] >> plane) & 1, ref _contexts[context]);
                    flags[index] = flag | Refined;
                }
            }
        }
    }

    private void EncodeCleanup(ReadOnlySpan<int> samples, Span<uint> flags, ReadOnlySpan<byte> zeroCoding, int plane)
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
                    // D.3.4: the run-length mode, for a column of four insignificant coefficients without significant neighbor
                    var run = 0;
                    while (run < 4 && ((samples[((stripe + run) * width) + x] >> plane) & 1) == 0)
                    {
                        run++;
                    }

                    if (run == 4)
                    {
                        _mq.Encode(0, ref _contexts[ContextRunLength]);
                        continue;
                    }

                    _mq.Encode(1, ref _contexts[ContextRunLength]);
                    ref var uniform = ref _contexts[ContextUniform];
                    _mq.Encode(run >> 1, ref uniform);
                    _mq.Encode(run & 1, ref uniform);
                    y = stripe + run;
                    var index = ((y + 1) * stride) + x + 1;
                    var negative = samples[(y * width) + x] < 0;
                    EncodeSign(flags[index], negative);
                    Jpeg2000Tier1Tables.SetSignificant(flags, index, stride, negative, updateNorth: true);
                    y++;
                }

                for (; y < stripeEnd; y++)
                {
                    var index = ((y + 1) * stride) + x + 1;
                    var flag = flags[index];
                    if ((flag & (Significant | Visited)) == 0)
                    {
                        var sample = samples[(y * width) + x];
                        var bit = (sample >> plane) & 1;
                        _mq.Encode(bit, ref _contexts[zeroCoding[(int)(flag & NeighborMask)]]);
                        if (bit != 0)
                        {
                            EncodeSign(flag, sample < 0);
                            Jpeg2000Tier1Tables.SetSignificant(flags, index, stride, sample < 0, updateNorth: true);
                        }
                    }

                    flags[index] &= ~Visited;
                }
            }
        }
    }

    private void EncodeSign(uint flag, bool negative)
    {
        var entry = Jpeg2000Tier1Tables.GetSignContext(flag);
        _mq.Encode((negative ? 1 : 0) ^ (entry >> 7), ref _contexts[entry & 0x7F]);
    }
}
