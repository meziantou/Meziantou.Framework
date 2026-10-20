using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoder of a code-block of the high-throughput block coder (Rec. ITU-T T.814 | ISO/IEC 15444-15, clause 7): the
/// HT cleanup pass, which gives every magnitude bit of the code-block down to one bit-plane at once, then the optional
/// HT SigProp and HT MagRef passes, which refine the next bit-plane.
/// </summary>
/// <remarks>
/// <para>
/// The procedures are those of the Recommendation, with its names: the five bit-streams of 7.1 (MagSgn, MEL and VLC in
/// the HT cleanup segment, SigProp and MagRef in the HT refinement segment), the MEL symbols of 7.3.3, the quad-pair
/// interleaving of 7.3.4, the contexts and the CxtVLC codewords of 7.3.5, the unsigned residuals of 7.3.6, the exponent
/// predictors of 7.3.7, the MagSgn values of 7.3.8, and the two refinement passes of 7.4 and 7.5.
/// </para>
/// <para>
/// The MagSgn and VLC bit-streams are read through a window of up to 64 bits, filled with the bits importMagSgnBit and
/// importVLCBit would give: a codeword is found from the next seven bits at once, and the bits of a MagSgn value are
/// taken at once. A malformed byte or the end of a bit-stream stops the filling, and is reported when a bit after the
/// last one of the window is needed, where the procedures of the Recommendation meet it.
/// </para>
/// <para>
/// A coefficient is returned as <see cref="Jpeg2000Tier1Decoder"/> returns it: its sign in the most significant bit, and
/// twice its magnitude with the bit-planes that are not decoded replaced by half of their range (7.6: the decoded
/// bit-planes are those above the skipped ones, with one more for the samples a refinement pass decoded).
/// </para>
/// </remarks>
internal sealed class Jpeg2000HtBlockDecoder : IDisposable
{
    private const int MaxCleanupLength = 65535;
    private const int MaxRefinementLength = 2047;
    private const int MaxSuffixLength = 4079;
    private const uint SignBit = 0x80000000;

    // The most bits of the VLC bit-stream a quad-pair reads: two codewords of 7 bits, two unsigned residual prefixes of
    // 3 bits, two suffixes of 5 bits and two extensions of 4 bits
    private const int MaxPairVlcBits = 38;

    // Two exponents per quad of the widest code-block, and those of a quad that does not exist on each side
    private const int ExponentRowLength = 1024 + 4;

    // The quads of a row of the widest code-block
    private const int QuadRowLength = 1024 / 2;

    // The flags of the HT SigProp pass: the samples of a code-block with a margin of one column on the left, two on the
    // right, and one row above and below; this many for the code-blocks of the codestreams (sides of at most 1024
    // samples)
    private const int NeighborsLength = 4096 + (2 * 1024) + (3 * 1024) + 6;

    // Table 2
    private static ReadOnlySpan<byte> MelExponents => [0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 4, 5];

    private readonly Av1PooledArray<uint> _magnitudes;
    private readonly Av1PooledArray<byte> _states;
    private readonly Av1PooledArray<byte> _exponents;
    private readonly Av1PooledArray<uint> _quads;
    private readonly AllocationScope _scope;
    private Av1PooledArray<byte>? _neighbors;

    // The HT cleanup segment and its three bit-streams (7.1.1 to 7.1.4)
    private byte[] _data = [];
    private int _cleanupOffset;
    private int _cleanupLength;
    private int _prefixLength;
    private int _magSgnPosition;
    private int _magSgnLast;
    private ulong _magSgnWindow;
    private int _magSgnCount;
    private MagSgnError _magSgnError;
    private int _melPosition;
    private int _melBits;
    private int _melValue;
    private int _melExponent;
    private int _melRun;
    private bool _melOne;
    private int _vlcPosition;
    private int _vlcLast;
    private ulong _vlcWindow;
    private int _vlcCount;
    private bool _vlcEnded;

    // The geometry of the code-block, for the loops of the HT cleanup pass
    private int _width;
    private int _height;
    private int _lowestPlane;
    private int _maxExponent;
    private uint _maxMagnitude;

    // The HT refinement segment and its two bit-streams (7.1.5 and 7.1.6)
    private int _refinementOffset;
    private int _refinementLength;
    private int _sigPropPosition;
    private int _sigPropBits;
    private int _sigPropValue;
    private int _sigPropLast;
    private int _magRefPosition;
    private int _magRefBits;
    private int _magRefValue;
    private int _magRefLast;

    public Jpeg2000HtBlockDecoder(AllocationScope scope)
    {
        // A sample has a magnitude and a state; a row of quads needs the exponents of the bottom samples of the row above
        _scope = scope;
        Av1PooledArray<uint>? magnitudes = null;
        Av1PooledArray<byte>? states = null;
        Av1PooledArray<byte>? exponents = null;
        try
        {
            magnitudes = Av1PooledArray<uint>.Rent(scope, Jpeg2000Tier1Tables.MaxSamples, clear: false);
            states = Av1PooledArray<byte>.Rent(scope, Jpeg2000Tier1Tables.MaxSamples, clear: false);
            exponents = Av1PooledArray<byte>.Rent(scope, 2 * ExponentRowLength, clear: false);
            _quads = Av1PooledArray<uint>.Rent(scope, QuadRowLength, clear: false);
            _magnitudes = magnitudes;
            _states = states;
            _exponents = exponents;
        }
        catch
        {
            magnitudes?.Dispose();
            states?.Dispose();
            exponents?.Dispose();
            throw;
        }
    }

    /// <summary>The sample is significant after the HT cleanup pass.</summary>
    private const byte Significant = 1;

    /// <summary>The sample is negative.</summary>
    private const byte Negative = 2;

    /// <summary>A refinement pass decoded a bit for the sample (the refinement indicator z).</summary>
    private const byte Refined = 4;

    /// <summary>The bit a refinement pass decoded for the sample (the refinement value r).</summary>
    private const byte RefinementBit = 8;

    /// <summary>Decodes the coding passes of one HT set.</summary>
    /// <param name="samples">Receives the coefficients, row by row.</param>
    /// <param name="width">The width of the code-block.</param>
    /// <param name="height">The height of the code-block.</param>
    /// <param name="isVerticallyCausal">Whether the contexts of the HT SigProp pass ignore the next stripe (bit 3 of the code-block style).</param>
    /// <param name="bitPlanes">The number of magnitude bit-planes of the sub-band.</param>
    /// <param name="skippedBitPlanes">The number of skipped magnitude bit-planes (S_blk).</param>
    /// <param name="passCount">The number of coding passes to decode (Z_blk): 1 to 3.</param>
    /// <param name="data">The array that holds the two HT segments.</param>
    /// <param name="cleanupOffset">The offset of the HT cleanup segment.</param>
    /// <param name="cleanupLength">The length of the HT cleanup segment (Lcup).</param>
    /// <param name="refinementOffset">The offset of the HT refinement segment.</param>
    /// <param name="refinementLength">The length of the HT refinement segment (Lref).</param>
    /// <returns>The features the code-block used.</returns>
    /// <exception cref="InvalidImageContentException">The segments do not conform to the Recommendation.</exception>
    public Jpeg2000Tools Decode(Span<int> samples, int width, int height, bool isVerticallyCausal, int bitPlanes, int skippedBitPlanes, int passCount, byte[] data, int cleanupOffset, int cleanupLength, int refinementOffset, int refinementLength)
    {
        // 7.6: the HT cleanup pass gives the bit-planes above the lowest one it codes, which is this one from the least
        // significant bit-plane; a refinement pass gives the bit-plane below it
        var lowestPlane = bitPlanes - 1 - skippedBitPlanes;
        if (lowestPlane < 0 || (passCount > 1 && lowestPlane == 0))
            throw Invalid("it has more coding passes than its bit-planes allow.");

        if (cleanupLength is < 2 or >= MaxCleanupLength)
            throw Invalid("the length of its HT cleanup segment is not between 2 and 65534.");

        if (passCount > 1 && refinementLength >= MaxRefinementLength)
            throw Invalid("the length of its HT refinement segment exceeds 2046.");

        // 7.1.1: the length of the MEL and VLC byte-streams is in the last two bytes of the segment
        _data = data;
        _cleanupOffset = cleanupOffset;
        _cleanupLength = cleanupLength;
        var suffixLength = (16 * data[cleanupOffset + cleanupLength - 1]) + (data[cleanupOffset + cleanupLength - 2] & 0x0F);
        if (suffixLength < 2 || suffixLength > Math.Min(cleanupLength, MaxSuffixLength))
            throw Invalid("the suffix length of its HT cleanup segment is out of range.");

        _prefixLength = cleanupLength - suffixLength;
        var count = width * height;
        var magnitudes = _magnitudes.Span[..count];
        var states = _states.Span[..count];
        var tools = Jpeg2000Tools.HighThroughput;
        if (passCount == 1)
        {
            // Without refinement pass, the HT cleanup pass writes the coefficients of the significant samples itself
            samples = samples[..count];
            samples.Clear();
            DecodeCleanup(samples, lowestPlane, width, height, skippedBitPlanes);
            return tools;
        }

        states.Clear();
        DecodeCleanup(samples: default, lowestPlane, width, height, skippedBitPlanes);
        _refinementOffset = refinementOffset;
        _refinementLength = refinementLength;
        DecodeSignificancePropagation(states, width, height, isVerticallyCausal);
        tools |= Jpeg2000Tools.HighThroughputSignificancePropagation;
        if (passCount > 2)
        {
            DecodeMagnitudeRefinement(states, width, height);
            tools |= Jpeg2000Tools.HighThroughputMagnitudeRefinement;
        }

        // 7.6, with the half of the range of the bit-planes that are not decoded
        for (var n = 0; n < count; n++)
        {
            var state = states[n];
            uint value;
            if ((state & Significant) != 0)
            {
                value = (state & Refined) != 0
                    ? ((4 * magnitudes[n]) + ((state & RefinementBit) != 0 ? 2u : 0u) + 1) << (lowestPlane - 1)
                    : ((2 * magnitudes[n]) + 1) << lowestPlane;
            }
            else if ((state & RefinementBit) != 0)
            {
                // The sample became significant in the bit-plane the HT SigProp pass refines
                value = 3u << (lowestPlane - 1);
            }
            else
            {
                samples[n] = 0;
                continue;
            }

            samples[n] = (int)((state & Negative) != 0 ? value | SignBit : value);
        }

        return tools;
    }

    public void Dispose()
    {
        _magnitudes.Dispose();
        _states.Dispose();
        _exponents.Dispose();
        _quads.Dispose();
        _neighbors?.Dispose();
        _neighbors = null;
    }

    private enum MagSgnError
    {
        None,
        NotStuffed,
        TooShort,
    }

    /// <summary>A choice that is made when a generic method is compiled, so that its loops do not test it.</summary>
    private interface IFlag
    {
        static abstract bool IsSet { get; }
    }

    private readonly struct Set : IFlag
    {
        public static bool IsSet => true;
    }

    private readonly struct NotSet : IFlag
    {
        public static bool IsSet => false;
    }

    private static InvalidImageContentException Invalid(string message) => Jpeg2000Format.Invalid("a code-block of the high-throughput block coder is malformed: " + message);

    /// <summary>The HT cleanup decoding algorithm (7.3).</summary>
    /// <param name="samples">
    /// Empty, or receives the coefficients of the significant samples when no refinement pass follows (7.6): the sign in
    /// the most significant bit, and <c>(2 * magnitude + 1) &lt;&lt; lowestPlane</c>; cleared by the caller. When it is
    /// empty, the magnitudes and the states of the significant samples are stored in <see cref="_magnitudes"/> and
    /// <see cref="_states"/>, which the caller clears.
    /// </param>
    /// <param name="lowestPlane">The lowest bit-plane the pass codes, from the least significant bit-plane.</param>
    /// <param name="width">The width of the code-block.</param>
    /// <param name="height">The height of the code-block.</param>
    /// <param name="skippedBitPlanes">The number of skipped magnitude bit-planes (S_blk).</param>
    /// <remarks>
    /// <para>
    /// A row of quads is decoded in two loops: the first one reads the MEL and VLC bit-streams of every quad-pair of the
    /// row (the CxtVLC codewords and the unsigned residuals), the second one the MagSgn bit-stream of every quad. The
    /// first loop only depends on the rows above, and each bit-stream is read in its order, so that the bits are those
    /// of the procedure of the Recommendation; an error of the first loop is reported by the second one when it reaches
    /// the quad-pair, where the procedure meets it.
    /// </para>
    /// <para>
    /// The window of a bit-stream is a local of the loop that reads it, passed by reference to helpers that are always
    /// inlined, so that it stays in a register; the rest of the state of the bit-streams is in fields, which only the
    /// filling of a window reads. Filling a window earlier than the procedure would does not change its bits: an error
    /// found while filling it is reported when a bit after the last one of the window is needed.
    /// </para>
    /// </remarks>
    private void DecodeCleanup(Span<int> samples, int lowestPlane, int width, int height, int skippedBitPlanes)
    {
        var quadsWide = (width + 1) / 2;
        var quadsHigh = (height + 1) / 2;

        // initMS, initMEL, initMELDecoder and initVLC
        _magSgnPosition = 0;
        _magSgnLast = 0;
        _magSgnError = MagSgnError.None;
        _magSgnWindow = 0;
        _magSgnCount = 0;
        _melPosition = _prefixLength;
        _melBits = 0;
        _melValue = 0;
        _melExponent = 0;
        _melRun = 0;
        _melOne = false;
        _vlcPosition = _cleanupLength - 3;
        _vlcLast = ReadCleanupByte(_cleanupLength - 2);
        _vlcCount = ((_vlcLast >> 4) & 7) < 7 ? 4 : 3;
        _vlcWindow = (ulong)(_vlcLast >> 4) & ((1UL << _vlcCount) - 1);
        _vlcEnded = false;

        // Two rows of exponents of the bottom samples of a row of quads, the row above and the row being decoded, which
        // alternate: the bottom-left then the bottom-right sample of each quad, after the two entries of the quad that
        // does not exist on the left
        _exponents.Span[..(2 * ExponentRowLength)].Clear();
        _width = width;
        _height = height;
        _lowestPlane = lowestPlane;

        // 7.6: a magnitude has the S_blk + 1 bit-planes the pass codes, so that its exponent is at most S_blk + 2
        _maxExponent = skippedBitPlanes + 2;
        _maxMagnitude = (1u << (skippedBitPlanes + 1)) - 1;
        for (var quadY = 0; quadY < quadsHigh; quadY++)
        {
            var aboveRow = (quadY & 1) * ExponentRowLength;
            var failedPair = DecodeQuadRowCodewords(quadsWide, aboveRow, quadY == 0);
            if (samples.IsEmpty)
            {
                DecodeQuadRowSamples<NotSet>(samples, quadsWide, aboveRow, quadY, failedPair);
            }
            else
            {
                DecodeQuadRowSamples<Set>(samples, quadsWide, aboveRow, quadY, failedPair);
            }
        }
    }

    /// <summary>
    /// The MEL and VLC bit-streams of a row of quads: the CxtVLC table entry of each quad (7.3.5, 0 for a quad without
    /// significant sample) is stored in <see cref="_quads"/>, with its unsigned residual (7.3.6) in the high 16 bits.
    /// </summary>
    /// <param name="quadsWide">The number of quads of the row.</param>
    /// <param name="aboveRow">The offset of the exponents of the row of quads above in <see cref="_exponents"/>.</param>
    /// <param name="isInitialRow">Whether the row is the first row of quads.</param>
    /// <returns>The index of the first quad of the quad-pair whose VLC bits are not in the bit-stream; <paramref name="quadsWide"/> when there is none.</returns>
    private int DecodeQuadRowCodewords(int quadsWide, int aboveRow, bool isInitialRow)
    {
        var quads = _quads.Span[..quadsWide];
        var exponents = _exponents.Span.Slice(aboveRow, ExponentRowLength);
        var vlcWindow = _vlcWindow;
        var vlcCount = _vlcCount;
        var previousPattern = 0;
        var quadX = 0;
        for (; quadX < quadsWide; quadX += 2)
        {
            // The bits of a quad-pair are in the window, unless the bit-stream ends
            if (vlcCount < MaxPairVlcBits)
            {
                vlcWindow = FillVlcWindow(vlcWindow, vlcCount);
                vlcCount = _vlcCount;
            }

            // 7.3.5: the significance pattern, the unsigned residual offset and the EMB patterns of each quad of the pair
            var hasSecond = quadX + 1 < quadsWide;
            var entry0 = DecodeQuadEntry(ref vlcWindow, ref vlcCount, isInitialRow, GetContext(exponents, quadX, isInitialRow, previousPattern));
            if (entry0 == uint.MaxValue)
                break;

            previousPattern = (int)((entry0 >> 3) & 0x0F);
            var entry1 = 0u;
            if (hasSecond)
            {
                entry1 = DecodeQuadEntry(ref vlcWindow, ref vlcCount, isInitialRow, GetContext(exponents, quadX + 1, isInitialRow, previousPattern));
                if (entry1 == uint.MaxValue)
                    break;

                previousPattern = (int)((entry1 >> 3) & 0x0F);
            }

            var residuals = DecodeResiduals(ref vlcWindow, ref vlcCount, isInitialRow, hasSecond, (int)((entry0 >> 7) & 1), (int)((entry1 >> 7) & 1));
            if (residuals < 0)
                break;

            quads[quadX] = (entry0 & 0xFFFF) | ((uint)residuals << 16);
            if (hasSecond)
            {
                quads[quadX + 1] = (entry1 & 0xFFFF) | (uint)(residuals & ~0xFFFF);
            }
        }

        _vlcWindow = vlcWindow;
        _vlcCount = vlcCount;
        return quadX;
    }

    /// <summary>
    /// The MagSgn bit-stream of a row of quads: the exponent bound of each quad (7.3.7), then the magnitude and the sign
    /// of its samples (7.3.8).
    /// </summary>
    /// <typeparam name="TWritesCoefficients">Whether the coefficients are written to <paramref name="samples"/> rather than the magnitudes and the states to their arrays.</typeparam>
    private void DecodeQuadRowSamples<TWritesCoefficients>(Span<int> samples, int quadsWide, int aboveRow, int quadY, int failedPair)
        where TWritesCoefficients : struct, IFlag
    {
        var quads = _quads.Span[..quadsWide];
        var exponents = _exponents.Span.Slice(aboveRow, ExponentRowLength);
        var nextExponents = _exponents.Span.Slice(ExponentRowLength - aboveRow, ExponentRowLength);
        var magnitudes = _magnitudes.Span;
        var states = _states.Span;
        var width = _width;
        var height = _height;
        var lowestPlane = _lowestPlane;
        var maxExponent = _maxExponent;
        var maxMagnitude = _maxMagnitude;
        var isInitialRow = quadY == 0;
        var magSgnWindow = _magSgnWindow;
        var magSgnCount = _magSgnCount;
        for (var x = 0; x < quads.Length; x++)
        {
            if (x == failedPair)
                ThrowVlcTooShort();

            var quad = quads[x];
            var pattern = (int)((quad >> 3) & 0x0F);
            var predictor = 1;
            if (!isInitialRow && (pattern & (pattern - 1)) != 0)
            {
                // Formula (5): the quad has more than one significant sample; the largest of the four exponents above
                var above = BinaryPrimitives.ReadUInt32LittleEndian(exponents.Slice((2 * x) + 1, 4));
                var largest = Math.Max(Math.Max(above & 0xFF, (above >> 8) & 0xFF), Math.Max((above >> 16) & 0xFF, above >> 24));
                predictor = Math.Max(1, (int)largest - 1);
            }

            var bound = predictor + (int)(quad >> 16);
            if (bound > maxExponent)
                throw Invalid("a magnitude exceeds the bit-planes of the code-block.");

            // The exponents of the bottom-left and bottom-right samples, in the low two bytes
            var bottom = 0;
            var remaining = pattern;
            while (remaining != 0)
            {
                // j: the top-left, bottom-left, top-right and bottom-right samples of the quad (7.2)
                var j = BitOperations.TrailingZeroCount(remaining);
                remaining &= remaining - 1;

                // decodeMagSgnValue (7.3.8): a sample with a known bit has one bit less in the bit-stream
                var bitCount = bound - (int)((quad >> (8 + j)) & 1);
                uint value;
                if (bitCount <= 32)
                {
                    value = ReadMagSgnBits(ref magSgnWindow, ref magSgnCount, bitCount);
                }
                else
                {
                    _magSgnWindow = magSgnWindow;
                    _magSgnCount = magSgnCount;
                    value = ReadLongMagSgnValue(bitCount);
                    magSgnWindow = _magSgnWindow;
                    magSgnCount = _magSgnCount;
                }

                value += ((quad >> (12 + j)) & 1) << bitCount;
                var magnitude = (value >> 1) + 1;
                if (magnitude > maxMagnitude)
                    throw Invalid("a magnitude exceeds the bit-planes of the code-block.");

                // The samples 1 and 3 are at the bottom of the quad
                var exponent = 32 - BitOperations.LeadingZeroCount((2 * magnitude) - 1);
                bottom |= (exponent << (4 * (j & 2))) & -(j & 1);

                // A sample of a padded column or row is not in the code-block
                var sampleX = (2 * x) + (j >> 1);
                var sampleY = (2 * quadY) + (j & 1);
                if (sampleX >= width || sampleY >= height)
                    throw Invalid("a sample outside of the code-block is significant.");

                var index = (sampleY * width) + sampleX;
                if (TWritesCoefficients.IsSet)
                {
                    samples[index] = (int)((((2 * magnitude) + 1) << lowestPlane) | (value << 31));
                }
                else
                {
                    magnitudes[index] = magnitude;
                    states[index] = (byte)(Significant | (int)((value & 1) << 1));
                }
            }

            BinaryPrimitives.WriteUInt16LittleEndian(nextExponents.Slice((2 * x) + 2, 2), (ushort)bottom);
        }

        _magSgnWindow = magSgnWindow;
        _magSgnCount = magSgnCount;
    }

    /// <summary>decodeMagSgnValue (7.3.8) for more than 32 bits, from the window in <see cref="_magSgnWindow"/>: a shift of 32 bits or more wraps around, as it does bit by bit.</summary>
    private uint ReadLongMagSgnValue(int bitCount)
    {
        var window = _magSgnWindow;
        var count = _magSgnCount;
        var value = 0u;
        for (var i = 0; i < bitCount; i++)
        {
            value |= ReadMagSgnBits(ref window, ref count, 1) << i;
        }

        _magSgnWindow = window;
        _magSgnCount = count;
        return value;
    }

    /// <summary>The context of a quad (7.3.5): formula (1) in the first row of quads, formula (2) in the others.</summary>
    /// <param name="exponents">The exponents of the bottom samples of the row of quads above.</param>
    /// <param name="x">The index of the quad in its row.</param>
    /// <param name="isInitialRow">Whether the quad is in the first row of quads.</param>
    /// <param name="previousPattern">The significance pattern of the quad on the left; 0 for the first one.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetContext(ReadOnlySpan<byte> exponents, int x, bool isInitialRow, int previousPattern)
    {
        if (isInitialRow)
        {
            // Formula (1): the samples of the quad on the left
            return ((previousPattern | (previousPattern >> 1)) & 1) + (2 * ((previousPattern >> 2) & 1)) + (4 * ((previousPattern >> 3) & 1));
        }

        // Formula (2): the bottom samples of the three quads above (the bottom-right sample of the one on the left, both
        // samples of the one above, the bottom-left sample of the one on the right: four bytes), and the right samples of
        // the quad on the left
        var above = BinaryPrimitives.ReadUInt32LittleEndian(exponents.Slice((2 * x) + 1, 4));
        var west = (above & 0xFFFF) != 0 ? 1 : 0;
        var east = (above >> 16) != 0 ? 1 : 0;
        return west + (2 * (((previousPattern >> 2) | (previousPattern >> 3)) & 1)) + (4 * east);
    }

    /// <summary>
    /// decodeSigEMB: the CxtVLC table entry of a quad; 0 when a MEL symbol says that the quad has no significant sample,
    /// and <see cref="uint.MaxValue"/> when the bits of the codeword are not in the bit-stream.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint DecodeQuadEntry(ref ulong vlcWindow, ref int vlcCount, bool isInitialRow, int context)
    {
        // A quad whose neighbors are all insignificant is first described by a MEL symbol
        if (context == 0 && DecodeMelSymbol() == 0)
            return 0;

        // decodeCxtVLC (7.3.5): the tables give the codeword of a context for any seven bits; the bits that are not in
        // the bit-stream are 0, and the codeword is consumed only when all its bits are there, as reading it bit by bit
        // would require
        var entry = Jpeg2000HtVlcTables.Find(isInitialRow, context, PeekVlcBits(vlcWindow, Jpeg2000HtVlcTables.MaxCodewordLength));
        return TrySkipVlcBits(ref vlcWindow, ref vlcCount, (int)(entry & 7)) ? entry : uint.MaxValue;
    }

    /// <summary>The unsigned residuals of a quad-pair, with the interleaving of 7.3.4 (decodeUPrefix, decodeUSuffix and decodeUExtension of 7.3.6).</summary>
    /// <param name="vlcWindow">The window of the VLC bit-stream.</param>
    /// <param name="vlcCount">The number of bits in <paramref name="vlcWindow"/>.</param>
    /// <param name="isInitialRow">Whether the quads are in the first row of quads.</param>
    /// <param name="hasSecond">Whether the pair has a second quad.</param>
    /// <param name="offset0">The unsigned residual offset of the first quad.</param>
    /// <param name="offset1">The unsigned residual offset of the second quad; 0 when there is none.</param>
    /// <returns>
    /// The unsigned residual of the first quad in the low 16 bits, and that of the second quad (0 when there is none)
    /// above them; -1 when their bits are not in the bit-stream.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int DecodeResiduals(ref ulong vlcWindow, ref int vlcCount, bool isInitialRow, bool hasSecond, int offset0, int offset1)
    {
        if ((offset0 | offset1) == 0)
            return 0;

        var prefix0 = 0;
        var prefix1 = 0;
        var bias = 0;
        if (isInitialRow && hasSecond && offset0 != 0 && offset1 != 0)
        {
            // In the first row of quads, a MEL symbol tells whether both residuals of a pair exceed 2
            if (DecodeMelSymbol() != 0)
            {
                bias = 2;
                prefix0 = DecodePrefix(ref vlcWindow, ref vlcCount);
                prefix1 = prefix0 < 0 ? prefix0 : DecodePrefix(ref vlcWindow, ref vlcCount);
            }
            else
            {
                // The second residual is one bit when the first prefix has three bits
                prefix0 = DecodePrefix(ref vlcWindow, ref vlcCount);
                prefix1 = prefix0 < 0 ? prefix0 : prefix0 > 2 ? ReadVlcBits(ref vlcWindow, ref vlcCount, 1) + 1 : DecodePrefix(ref vlcWindow, ref vlcCount);
            }
        }
        else
        {
            if (offset0 != 0)
            {
                prefix0 = DecodePrefix(ref vlcWindow, ref vlcCount);
            }

            if (offset1 != 0 && prefix0 >= 0)
            {
                prefix1 = DecodePrefix(ref vlcWindow, ref vlcCount);
            }
        }

        if ((prefix0 | prefix1) < 0)
            return -1;

        // The suffixes of both quads, then their extensions
        var suffix0 = prefix0 >= 3 ? ReadVlcBits(ref vlcWindow, ref vlcCount, prefix0 == 3 ? 1 : 5) : 0;
        var suffix1 = prefix1 >= 3 && suffix0 >= 0 ? ReadVlcBits(ref vlcWindow, ref vlcCount, prefix1 == 3 ? 1 : 5) : 0;
        var extension0 = suffix0 >= 28 && suffix1 >= 0 ? ReadVlcBits(ref vlcWindow, ref vlcCount, 4) : 0;
        var extension1 = suffix1 >= 28 && extension0 >= 0 ? ReadVlcBits(ref vlcWindow, ref vlcCount, 4) : 0;
        if ((suffix0 | suffix1 | extension0 | extension1) < 0)
            return -1;

        // Formulae (3) and (4)
        var residual0 = prefix0 == 0 ? 0 : bias + prefix0 + suffix0 + (4 * extension0);
        var residual1 = prefix1 == 0 ? 0 : bias + prefix1 + suffix1 + (4 * extension1);
        return residual0 | (residual1 << 16);
    }

    /// <summary>decodeUPrefix (7.3.6): the codewords 1, 01, 001 and 000 stand for the prefix values 1, 2, 3 and 5; -1 when the bits are not in the bit-stream.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodePrefix(ref ulong vlcWindow, ref int vlcCount)
    {
        // The bits that are not in the bit-stream are 0: they can only make the codeword longer than the bits left
        var bits = PeekVlcBits(vlcWindow, 3);
        var (value, length) = (bits & 1) != 0 ? (1, 1) : (bits & 2) != 0 ? (2, 2) : (bits & 4) != 0 ? (3, 3) : (5, 3);
        return TrySkipVlcBits(ref vlcWindow, ref vlcCount, length) ? value : -1;
    }

    /// <summary>modDcup (7.1.1): the bits of the last two bytes that give the suffix length read as 1.</summary>
    private int ReadCleanupByte(int position)
    {
        if (position == _cleanupLength - 1)
            return 0xFF;

        var value = _data[_cleanupOffset + position];
        return position == _cleanupLength - 2 ? value | 0x0F : value;
    }

    /// <summary>importMagSgnBit (7.1.2), for <paramref name="count"/> bits at once (at most 32), the first one in the least significant bit.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ReadMagSgnBits(ref ulong magSgnWindow, ref int magSgnCount, int count)
    {
        if (count > magSgnCount)
        {
            magSgnWindow = FillMagSgnWindow(magSgnWindow, magSgnCount);
            magSgnCount = _magSgnCount;
            if (count > magSgnCount)
                ThrowMagSgnError();
        }

        var value = magSgnWindow & ((1UL << count) - 1);
        magSgnWindow >>= count;
        magSgnCount -= count;
        return (uint)value;
    }

    [DoesNotReturn]
    private void ThrowMagSgnError()
    {
        throw _magSgnError == MagSgnError.NotStuffed
            ? Invalid("a bit is not stuffed after an FF byte of its MagSgn byte-stream.")
            : Invalid("its MagSgn byte-stream is too short.");
    }

    /// <summary>
    /// Appends the bits of the next bytes of the MagSgn byte-stream to its window, until it holds more than 56 bits or a
    /// byte cannot be read.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="count">The number of bits in <paramref name="window"/>.</param>
    /// <returns>The window; its number of bits is in <see cref="_magSgnCount"/>.</returns>
    private ulong FillMagSgnWindow(ulong window, int count)
    {
        // The bytes that fill the window at once, when none of them is FF and the byte before them is not FF: each one
        // gives its eight bits, without a stuffed bit to check
        if (count <= 56 && _magSgnError == MagSgnError.None && _magSgnLast != 0xFF && _magSgnPosition + sizeof(ulong) <= _prefixLength)
        {
            var bytes = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(_cleanupOffset + _magSgnPosition, sizeof(ulong)));
            var byteCount = (64 - count) >> 3;
            var used = byteCount == sizeof(ulong) ? ulong.MaxValue : (1UL << (8 * byteCount)) - 1;
            if (!HasZeroByte(~bytes, used))
            {
                _magSgnLast = (int)((bytes >> (8 * (byteCount - 1))) & 0xFF);
                _magSgnPosition += byteCount;
                _magSgnCount = count + (8 * byteCount);
                return window | ((bytes & used) << count);
            }
        }

        while (count <= 56 && _magSgnError == MagSgnError.None)
        {
            var bits = _magSgnLast == 0xFF ? 7 : 8;
            int value;
            if (_magSgnPosition < _prefixLength)
            {
                value = ReadCleanupByte(_magSgnPosition);
                if ((value & (1 << bits)) != 0)
                {
                    _magSgnError = MagSgnError.NotStuffed;
                    break;
                }
            }
            else if (_magSgnPosition == _prefixLength)
            {
                value = 0xFF;
            }
            else
            {
                _magSgnError = MagSgnError.TooShort;
                break;
            }

            _magSgnLast = value;
            _magSgnPosition++;
            window |= (ulong)(value & ((1 << bits) - 1)) << count;
            count += bits;
        }

        _magSgnCount = count;
        return window;
    }

    /// <summary>Whether one of the bytes of <paramref name="value"/> that <paramref name="used"/> selects is 0.</summary>
    /// <remarks>The borrow of a subtraction only goes up, so that the bytes that are not selected do not change the result.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasZeroByte(ulong value, ulong used)
        => ((value - 0x0101010101010101UL) & ~value & 0x8080808080808080UL & used) != 0;

    /// <summary>decodeMELSym (7.3.3), on the bits of importMELBit (7.1.3).</summary>
    private int DecodeMelSymbol()
    {
        if (_melRun == 0 && !_melOne)
        {
            int exponent = MelExponents[_melExponent];
            if (ReadMelBit() == 1)
            {
                _melRun = 1 << exponent;
                _melExponent = Math.Min(12, _melExponent + 1);
            }
            else
            {
                _melRun = 0;
                for (; exponent > 0; exponent--)
                {
                    _melRun = (2 * _melRun) + ReadMelBit();
                }

                _melExponent = Math.Max(0, _melExponent - 1);
                _melOne = true;
            }
        }

        if (_melRun > 0)
        {
            _melRun--;
            return 0;
        }

        _melOne = false;
        return 1;
    }

    private int ReadMelBit()
    {
        if (_melBits == 0)
        {
            _melBits = _melValue == 0xFF ? 7 : 8;
            if (_melPosition < _cleanupLength)
            {
                _melValue = ReadCleanupByte(_melPosition);
                _melPosition++;
            }
            else
            {
                _melValue = 0xFF;
            }
        }

        _melBits--;
        return (_melValue >> _melBits) & 1;
    }

    /// <summary>importVLCBit (7.1.4), for <paramref name="count"/> bits at once, the first one in the least significant bit; -1 when they are not in the window.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadVlcBits(ref ulong vlcWindow, ref int vlcCount, int count)
    {
        var value = PeekVlcBits(vlcWindow, count);
        return TrySkipVlcBits(ref vlcWindow, ref vlcCount, count) ? value : -1;
    }

    /// <summary>Gets the next <paramref name="count"/> bits of the window of the VLC bit-stream without consuming them; the bits after the end of the bit-stream are 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PeekVlcBits(ulong vlcWindow, int count) => (int)(vlcWindow & ((1UL << count) - 1));

    /// <summary>
    /// Consumes the next <paramref name="count"/> bits of the window of the VLC bit-stream; <see langword="false"/> when
    /// the window has fewer bits, which the window filled for the quad-pair only has when the bit-stream ends.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySkipVlcBits(ref ulong vlcWindow, ref int vlcCount, int count)
    {
        if (count > vlcCount)
            return false;

        vlcWindow >>= count;
        vlcCount -= count;
        return true;
    }

    [DoesNotReturn]
    private static void ThrowVlcTooShort() => throw Invalid("its VLC byte-stream is too short.");

    /// <summary>
    /// Appends the bits of the next bytes of the VLC byte-stream, read backward, to its window, until it holds more than 56
    /// bits or the byte-stream ends.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="count">The number of bits in <paramref name="window"/>.</param>
    /// <returns>The window; its number of bits is in <see cref="_vlcCount"/>.</returns>
    private ulong FillVlcWindow(ulong window, int count)
    {
        // The bytes that fill the window at once, when none of them ends with seven bits 1: each one gives its eight
        // bits, whatever the byte before it. The byte at the position is the first one: the least significant byte of
        // the big-endian value of the eight bytes that end with it
        if (count <= 56 && !_vlcEnded && _vlcPosition - (sizeof(ulong) - 1) >= _prefixLength)
        {
            var bytes = BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(_cleanupOffset + _vlcPosition - (sizeof(ulong) - 1), sizeof(ulong)));
            var byteCount = (64 - count) >> 3;
            var used = byteCount == sizeof(ulong) ? ulong.MaxValue : (1UL << (8 * byteCount)) - 1;
            if (!HasZeroByte(~(bytes | 0x8080808080808080UL), used))
            {
                _vlcLast = (int)((bytes >> (8 * (byteCount - 1))) & 0xFF);
                _vlcPosition -= byteCount;
                _vlcCount = count + (8 * byteCount);
                return window | ((bytes & used) << count);
            }
        }

        while (count <= 56 && !_vlcEnded)
        {
            if (_vlcPosition < _prefixLength)
            {
                _vlcEnded = true;
                break;
            }

            var value = ReadCleanupByte(_vlcPosition);
            var bits = _vlcLast > 0x8F && (value & 0x7F) == 0x7F ? 7 : 8;
            _vlcLast = value;
            _vlcPosition--;
            window |= (ulong)(value & ((1 << bits) - 1)) << count;
            count += bits;
        }

        _vlcCount = count;
        return window;
    }

    /// <summary>The HT SigProp decoding procedure (7.4).</summary>
    /// <remarks>
    /// A neighbor counts when it is significant, or, if it comes earlier in the stripe-oriented scan, when this pass made
    /// it significant. A sample that comes later has not been visited, so that this pass has not made it significant:
    /// a neighbor counts when it is significant or this pass made it significant, whatever its place in the scan. The
    /// neighborhood is read from a copy of these two flags with a margin of zeros around the code-block, three bytes of
    /// three rows.
    /// </remarks>
    private void DecodeSignificancePropagation(Span<byte> states, int width, int height, bool isVerticallyCausal)
    {
        // initSP
        _sigPropPosition = 0;
        _sigPropBits = 0;
        _sigPropValue = 0;
        _sigPropLast = 0;

        // One column of zeros on the left, two on the right (a neighborhood row is read as four bytes) and one row above
        // and below
        var stride = width + 3;
        var length = stride * (height + 2);
        if (_neighbors is null || _neighbors.Length < length)
        {
            _neighbors?.Dispose();
            _neighbors = null;
            _neighbors = Av1PooledArray<byte>.Rent(_scope, Math.Max(length, NeighborsLength), clear: false);
        }

        var neighbors = _neighbors.Span[..length];
        neighbors.Clear();
        for (var y = 0; y < height; y++)
        {
            var row = neighbors.Slice(((y + 1) * stride) + 1, width);
            var source = states.Slice(y * width, width);
            for (var x = 0; x < width; x++)
            {
                row[x] = (byte)(source[x] & Significant);
            }
        }

        for (var stripe = 0; stripe < height; stripe += 4)
        {
            var stripeEnd = Math.Min(stripe + 4, height);

            // The propagation neighborhood: the eight neighbors that are in the code-block, without the next stripe when
            // the contexts are vertically causal
            var lastRow = isVerticallyCausal ? stripeEnd - 1 : height - 1;
            for (var group = 0; group < width; group += 4)
            {
                var groupEnd = Math.Min(group + 4, width);

                // decodeSigPropMag for the four columns of the group, then decodeSigPropSign for the same samples
                for (var x = group; x < groupEnd; x++)
                {
                    for (var y = stripe; y < stripeEnd; y++)
                    {
                        var index = (y * width) + x;
                        if ((states[index] & Significant) != 0)
                            continue;

                        // The bytes of the columns x - 1 to x + 1 of the rows y - 1 to y + 1
                        var center = ((y + 1) * stride) + x + 1;
                        var neighborhood = BinaryPrimitives.ReadUInt32LittleEndian(neighbors.Slice(center - stride - 1, 4)) | BinaryPrimitives.ReadUInt32LittleEndian(neighbors.Slice(center - 1, 4));
                        if (y < lastRow)
                        {
                            neighborhood |= BinaryPrimitives.ReadUInt32LittleEndian(neighbors.Slice(center + stride - 1, 4));
                        }

                        if ((neighborhood & 0x00FFFFFF) != 0)
                        {
                            var bit = ReadSigPropBit();
                            states[index] |= (byte)(Refined | (bit != 0 ? RefinementBit : 0));
                            neighbors[center] = (byte)bit;
                        }
                    }
                }

                for (var x = group; x < groupEnd; x++)
                {
                    for (var y = stripe; y < stripeEnd; y++)
                    {
                        var index = (y * width) + x;
                        if ((states[index] & (Significant | RefinementBit)) == RefinementBit && ReadSigPropBit() != 0)
                        {
                            states[index] |= Negative;
                        }
                    }
                }
            }
        }
    }

    /// <summary>importSigPropBit (7.1.5).</summary>
    private int ReadSigPropBit()
    {
        if (_sigPropBits == 0)
        {
            _sigPropBits = _sigPropLast == 0xFF ? 7 : 8;
            if (_sigPropPosition < _refinementLength)
            {
                _sigPropValue = _data[_refinementOffset + _sigPropPosition];
                _sigPropPosition++;
                if ((_sigPropValue & (1 << _sigPropBits)) != 0)
                    throw Invalid("a bit is not stuffed after an FF byte of its SigProp byte-stream.");
            }
            else
            {
                _sigPropValue = 0;
            }

            _sigPropLast = _sigPropValue;
        }

        var bit = _sigPropValue & 1;
        _sigPropValue >>= 1;
        _sigPropBits--;
        return bit;
    }

    /// <summary>The HT MagRef decoding procedure (7.5).</summary>
    private void DecodeMagnitudeRefinement(Span<byte> states, int width, int height)
    {
        // initMR
        _magRefPosition = _refinementLength - 1;
        _magRefBits = 0;
        _magRefLast = 0xFF;
        _magRefValue = 0;
        for (var stripe = 0; stripe < height; stripe += 4)
        {
            var stripeEnd = Math.Min(stripe + 4, height);
            for (var x = 0; x < width; x++)
            {
                for (var y = stripe; y < stripeEnd; y++)
                {
                    var index = (y * width) + x;
                    if ((states[index] & Significant) != 0)
                    {
                        states[index] |= (byte)(Refined | (ReadMagRefBit() != 0 ? RefinementBit : 0));
                    }
                }
            }
        }
    }

    /// <summary>importMagRefBit (7.1.6): the bytes are read backward.</summary>
    private int ReadMagRefBit()
    {
        if (_magRefBits == 0)
        {
            if (_magRefPosition >= 0)
            {
                _magRefValue = _data[_refinementOffset + _magRefPosition];
                _magRefPosition--;
            }
            else
            {
                _magRefValue = 0;
            }

            _magRefBits = _magRefLast > 0x8F && (_magRefValue & 0x7F) == 0x7F ? 7 : 8;
            _magRefLast = _magRefValue;
        }

        var bit = _magRefValue & 1;
        _magRefValue >>= 1;
        _magRefBits--;
        return bit;
    }
}
