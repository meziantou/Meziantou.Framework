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
    private readonly Jpeg2000MqState[] _states = new Jpeg2000MqState[MaxPasses];
    private Jpeg2000MqEncoder _mq;
    private bool _tracksDistortion;
    private double _offset;
    private double _distortion;
    private int _width;
    private int _height;
    private int _stripeStride;

    /// <summary>The largest number of coding passes of a code-block: three for each of 31 bit-planes, but the first.</summary>
    public const int MaxPasses = (3 * 31) - 2;

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
        => Encode(samples, width, height, orientation, output, out length, passes: default, isIrreversible: false);

    /// <summary>Encodes the coefficients of a code-block and records what each coding pass contributes.</summary>
    /// <param name="samples">The coefficients, row by row: the magnitude in the 31 low bits and the sign in the high one.</param>
    /// <param name="width">The width of the code-block.</param>
    /// <param name="height">The height of the code-block.</param>
    /// <param name="orientation">The orientation of the sub-band: one of the <c>Orientation</c> constants of <see cref="Jpeg2000Tier1Tables"/>.</param>
    /// <param name="output">The writer the codeword segment is appended to.</param>
    /// <param name="length">Receives the number of bytes of the codeword segment.</param>
    /// <param name="passes">
    /// Receives the coding passes, in order (<c>3 * bitPlanes - 2</c> of them): the length of the segment that decodes
    /// each one, and the reduction of the squared error. Empty to record nothing.
    /// </param>
    /// <param name="isIrreversible">
    /// Whether the coefficients are quantization indices of the irreversible path, which a decoder reconstructs in the
    /// middle of their interval: the squared errors are measured from that point. Otherwise a coefficient is an exact value.
    /// </param>
    /// <returns>The number of bit-planes that are coded: 0 when every coefficient is zero, and nothing is written.</returns>
    /// <remarks>
    /// A decoder reconstructs a coefficient whose bit-planes are known down to <c>p</c> in the middle of what is left:
    /// <c>floor(m / 2^p) * 2^p + 2^(p - 1)</c>, or the coefficient itself (plus a half for a quantization index) once every
    /// bit-plane is known.
    /// </remarks>
    public int Encode(ReadOnlySpan<int> samples, int width, int height, int orientation, PooledPayloadWriter output, out int length, Span<Jpeg2000CodingPass> passes, bool isIrreversible)
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
        var start = output.Length;
        _mq.Initialize(output);
        _tracksDistortion = !passes.IsEmpty;
        _offset = isIrreversible ? 0.5 : 0;
        _distortion = 0;
        var states = _states.AsSpan();
        var pass = 0;

        // The first bit-plane has a cleanup pass only (D.3)
        for (var plane = bitPlanes - 1; plane >= 0; plane--)
        {
            if (plane != bitPlanes - 1)
            {
                EncodeSignificancePropagation(samples, flags, zeroCoding, plane);
                EndPass(passes, states, ref pass);
                EncodeMagnitudeRefinement(samples, flags, plane);
                EndPass(passes, states, ref pass);
            }

            EncodeCleanup(samples, flags, zeroCoding, plane);
            EndPass(passes, states, ref pass);
        }

        length = _mq.Flush();
        if (_tracksDistortion)
        {
            // The segment of a pass is at least as long as the one of the previous pass, and the last one is the whole segment
            var segment = output.WrittenSpan.Slice(start, length);
            var previous = 0;
            for (var i = 0; i < pass; i++)
            {
                var truncated = i == pass - 1 ? length : Math.Min(length, Jpeg2000MqEncoder.GetTruncationLength(segment, in states[i]));
                previous = Math.Max(previous, truncated);
                passes[i].Length = previous;
            }
        }

        return bitPlanes;
    }

    public void Dispose() => _flagBuffer.Dispose();

    private void EndPass(Span<Jpeg2000CodingPass> passes, Span<Jpeg2000MqState> states, ref int pass)
    {
        if (_tracksDistortion)
        {
            states[pass] = _mq.GetState();
            passes[pass].Distortion = _distortion;
            _distortion = 0;
        }

        pass++;
    }

    /// <summary>Gets the reconstruction of a magnitude whose bit-planes are known down to <paramref name="plane"/>.</summary>
    private double Reconstruct(int magnitude, int plane)
        => plane == 0 ? magnitude + _offset : ((magnitude >> plane) << plane) + (double)(1 << (plane - 1));

    /// <summary>Gets how much the squared error of a coefficient decreases when it becomes significant in a bit-plane.</summary>
    private double GetSignificanceGain(int sample, int plane)
    {
        var magnitude = sample & int.MaxValue;
        var value = magnitude + _offset;
        var reconstruction = Reconstruct(magnitude, plane);
        return reconstruction * ((2 * value) - reconstruction);
    }

    /// <summary>Gets how much the squared error of a significant coefficient decreases when a bit-plane refines it.</summary>
    private double GetRefinementGain(int sample, int plane)
    {
        var magnitude = sample & int.MaxValue;
        var value = magnitude + _offset;
        var before = value - Reconstruct(magnitude, plane + 1);
        var after = value - Reconstruct(magnitude, plane);
        return (before * before) - (after * after);
    }

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
        var tracksDistortion = _tracksDistortion;
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
                        if (tracksDistortion)
                        {
                            _distortion += GetSignificanceGain(sample, plane);
                        }
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
        var tracksDistortion = _tracksDistortion;
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
                    if (tracksDistortion)
                    {
                        _distortion += GetRefinementGain(sample, plane);
                    }
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
        var tracksDistortion = _tracksDistortion;
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
                    var runSample = unsafe(Unsafe.Add(ref columnSample, row * width));
                    var negative = runSample < 0;
                    EncodeSign(ref mq, ref contexts, flag, negative);
                    Jpeg2000Tier1Tables.SetSignificant(ref flag, row, stripeStride, negative, updateNorth: true);
                    if (tracksDistortion)
                    {
                        _distortion += GetSignificanceGain(runSample, plane);
                    }

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
                            if (tracksDistortion)
                            {
                                _distortion += GetSignificanceGain(sample, plane);
                            }
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
