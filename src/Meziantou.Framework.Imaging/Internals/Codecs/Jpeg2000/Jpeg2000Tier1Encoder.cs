using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoder of the coding passes of a code-block (T.800, Annex D), the reverse of <see cref="Jpeg2000Tier1Decoder"/>:
/// every bit-plane is coded with the three passes, in one codeword segment of the arithmetic coder of Annex C, with none
/// of the optional code-block styles.
/// </summary>
/// <remarks>
/// The flags are stored stripe by stripe, as the decoder stores them, and a pass skips the columns that it does not code.
/// </remarks>
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
    private int _stripeStride;

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
        _stripeStride = Jpeg2000Tier1Tables.GetStripeStride(width);
        var flags = _flagBuffer.Span[..Jpeg2000Tier1Tables.GetStripeFlagCount(width, height)];
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

    // The passes work on a copy of the arithmetic encoder, so that its registers stay in processor registers; the copy is
    // stored back at the end of the pass. The flags, the coefficients and the contexts are addressed through references:
    // every index is within the code-block and its border, and every context comes from the tables
    private void EncodeSignificancePropagation(ReadOnlySpan<int> samples, Span<uint> flags, ReadOnlySpan<byte> zeroCoding, int plane)
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
                    if ((value & Significant) != 0 || (value & NeighborMask) == 0)
                        continue;

                    var sample = unsafe(Unsafe.Add(ref sampleStart, ((stripe + row) * width) + x));
                    var bit = (sample >> plane) & 1;
                    mq.Encode(bit, ref unsafe(Unsafe.Add(ref contexts, Unsafe.Add(ref zeroCodingContexts, (int)(value & NeighborMask)))));
                    if (bit != 0)
                    {
                        EncodeSign(ref mq, ref contexts, value, sample < 0);
                        Jpeg2000Tier1Tables.SetSignificant(ref flag, row, stripeStride, sample < 0, updateNorth: true);
                    }

                    flag |= Visited;
                }
            }
        }

        _mq = mq;
    }

    private void EncodeMagnitudeRefinement(ReadOnlySpan<int> samples, Span<uint> flags, int plane)
    {
        var width = _width;
        var height = _height;
        var stripeStride = _stripeStride;
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
                    if ((value & (Significant | Visited)) != Significant)
                        continue;

                    var context = (value & Refined) != 0 ? ContextMagnitude + 2 : (value & NeighborMask) != 0 ? ContextMagnitude + 1 : ContextMagnitude;
                    var sample = unsafe(Unsafe.Add(ref sampleStart, ((stripe + row) * width) + x));
                    mq.Encode((sample >> plane) & 1, ref unsafe(Unsafe.Add(ref contexts, context)));
                    flag = value | Refined;
                }
            }
        }

        _mq = mq;
    }

    private void EncodeCleanup(ReadOnlySpan<int> samples, Span<uint> flags, ReadOnlySpan<byte> zeroCoding, int plane)
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
                ref var columnSample = ref unsafe(Unsafe.Add(ref sampleStart, (stripe * width) + x));
                var row = 0;
                if (rows == 4 && Jpeg2000Tier1Tables.IsRunLengthColumn(ref column))
                {
                    // D.3.4: the run-length mode, for a column of four insignificant coefficients without significant neighbor
                    while (row < 4 && ((unsafe(Unsafe.Add(ref columnSample, row * width)) >> plane) & 1) == 0)
                    {
                        row++;
                    }

                    if (row == 4)
                    {
                        mq.Encode(0, ref unsafe(Unsafe.Add(ref contexts, ContextRunLength)));
                        continue;
                    }

                    mq.Encode(1, ref unsafe(Unsafe.Add(ref contexts, ContextRunLength)));
                    ref var uniform = ref unsafe(Unsafe.Add(ref contexts, ContextUniform));
                    mq.Encode(row >> 1, ref uniform);
                    mq.Encode(row & 1, ref uniform);
                    ref var flag = ref unsafe(Unsafe.Add(ref column, row));
                    var negative = unsafe(Unsafe.Add(ref columnSample, row * width)) < 0;
                    EncodeSign(ref mq, ref contexts, flag, negative);
                    Jpeg2000Tier1Tables.SetSignificant(ref flag, row, stripeStride, negative, updateNorth: true);
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
                    if ((value & (Significant | Visited)) == 0)
                    {
                        var sample = unsafe(Unsafe.Add(ref columnSample, row * width));
                        var bit = (sample >> plane) & 1;
                        mq.Encode(bit, ref unsafe(Unsafe.Add(ref contexts, Unsafe.Add(ref zeroCodingContexts, (int)(value & NeighborMask)))));
                        if (bit != 0)
                        {
                            EncodeSign(ref mq, ref contexts, value, sample < 0);
                            Jpeg2000Tier1Tables.SetSignificant(ref flag, row, stripeStride, sample < 0, updateNorth: true);
                        }
                    }

                    flag &= ~Visited;
                }
            }
        }

        _mq = mq;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EncodeSign(ref Jpeg2000MqEncoder mq, ref byte contexts, uint flag, bool negative)
    {
        var entry = Jpeg2000Tier1Tables.GetSignContext(flag);
        mq.Encode((negative ? 1 : 0) ^ (entry >> 7), ref unsafe(Unsafe.Add(ref contexts, entry & 0x7F)));
    }
}
