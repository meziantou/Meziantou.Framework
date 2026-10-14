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

    // Two exponents per quad of the widest code-block, and those of a quad that does not exist on each side
    private const int ExponentRowLength = 1024 + 4;

    // Table 2
    private static ReadOnlySpan<byte> MelExponents => [0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 4, 5];

    private readonly Av1PooledArray<uint> _magnitudes;
    private readonly Av1PooledArray<byte> _states;
    private readonly Av1PooledArray<byte> _exponents;

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
        Av1PooledArray<uint>? magnitudes = null;
        Av1PooledArray<byte>? states = null;
        try
        {
            magnitudes = Av1PooledArray<uint>.Rent(scope, Jpeg2000Tier1Tables.MaxSamples, clear: false);
            states = Av1PooledArray<byte>.Rent(scope, Jpeg2000Tier1Tables.MaxSamples, clear: false);
            _exponents = Av1PooledArray<byte>.Rent(scope, 2 * ExponentRowLength, clear: false);
            _magnitudes = magnitudes;
            _states = states;
        }
        catch
        {
            magnitudes?.Dispose();
            states?.Dispose();
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
        states.Clear();
        DecodeCleanup(magnitudes, states, width, height, skippedBitPlanes);
        var tools = Jpeg2000Tools.HighThroughput;
        if (passCount > 1)
        {
            _refinementOffset = refinementOffset;
            _refinementLength = refinementLength;
            DecodeSignificancePropagation(states, width, height, isVerticallyCausal);
            tools |= Jpeg2000Tools.HighThroughputSignificancePropagation;
            if (passCount > 2)
            {
                DecodeMagnitudeRefinement(states, width, height);
                tools |= Jpeg2000Tools.HighThroughputMagnitudeRefinement;
            }
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
    }

    private enum MagSgnError
    {
        None,
        NotStuffed,
        TooShort,
    }

    private static InvalidImageContentException Invalid(string message) => Jpeg2000Format.Invalid("a code-block of the high-throughput block coder is malformed: " + message);

    /// <summary>The HT cleanup decoding algorithm (7.3).</summary>
    private void DecodeCleanup(Span<uint> magnitudes, Span<byte> states, int width, int height, int skippedBitPlanes)
    {
        var quadsWide = (width + 1) / 2;
        var quadsHigh = (height + 1) / 2;

        // initMS, initMEL, initMELDecoder and initVLC
        _magSgnPosition = 0;
        _magSgnLast = 0;
        _magSgnWindow = 0;
        _magSgnCount = 0;
        _magSgnError = MagSgnError.None;
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

        // The exponents of the bottom samples of a row of quads: the bottom-left then the bottom-right sample of each
        // quad, after the two entries of the quad that does not exist on the left
        var exponents = _exponents.Span[..ExponentRowLength];
        var nextExponents = _exponents.Span.Slice(ExponentRowLength, ExponentRowLength);
        exponents.Clear();
        nextExponents.Clear();

        // 7.6: a magnitude has the S_blk + 1 bit-planes the pass codes, so that its exponent is at most S_blk + 2
        var maxExponent = skippedBitPlanes + 2;
        var maxMagnitude = (1u << (skippedBitPlanes + 1)) - 1;
        Span<int> patterns = stackalloc int[2];
        Span<int> offsets = stackalloc int[2];
        Span<int> known = stackalloc int[2];
        Span<int> ones = stackalloc int[2];
        Span<int> residuals = stackalloc int[2];
        for (var quadY = 0; quadY < quadsHigh; quadY++)
        {
            var isInitialRow = quadY == 0;
            var previousPattern = 0;
            for (var quadX = 0; quadX < quadsWide; quadX += 2)
            {
                var pairLength = Math.Min(2, quadsWide - quadX);

                // 7.3.5: the significance pattern, the unsigned residual offset and the EMB patterns of each quad of the pair
                for (var i = 0; i < pairLength; i++)
                {
                    var x = quadX + i;
                    int context;
                    if (isInitialRow)
                    {
                        // Formula (1): the samples of the quad on the left
                        context = ((previousPattern | (previousPattern >> 1)) & 1) + (2 * ((previousPattern >> 2) & 1)) + (4 * ((previousPattern >> 3) & 1));
                    }
                    else
                    {
                        // Formula (2): the bottom samples of the three quads above, and the right samples of the quad on the left
                        var north = exponents[(2 * x) + 2] != 0 ? 1 : 0;
                        var northEast = exponents[(2 * x) + 3] != 0 ? 1 : 0;
                        var northWest = exponents[(2 * x) + 1] != 0 ? 1 : 0;
                        var northFar = exponents[(2 * x) + 4] != 0 ? 1 : 0;
                        context = (northWest | north) + (2 * (((previousPattern >> 2) | (previousPattern >> 3)) & 1)) + (4 * (northEast | northFar));
                    }

                    // decodeSigEMB: a quad whose neighbors are all insignificant is first described by a MEL symbol
                    if (context == 0 && DecodeMelSymbol() == 0)
                    {
                        patterns[i] = 0;
                        offsets[i] = 0;
                        known[i] = 0;
                        ones[i] = 0;
                    }
                    else
                    {
                        var entry = DecodeCodeword(isInitialRow, context);
                        patterns[i] = (int)((entry >> 3) & 0x0F);
                        offsets[i] = (int)((entry >> 7) & 1);
                        known[i] = (int)((entry >> 8) & 0x0F);
                        ones[i] = (int)((entry >> 12) & 0x0F);
                    }

                    previousPattern = patterns[i];
                }

                DecodeResiduals(isInitialRow, pairLength, offsets, residuals);

                // 7.3.7 and 7.3.8: the exponent bound of each quad, then the magnitude and the sign of its samples
                for (var i = 0; i < pairLength; i++)
                {
                    var x = quadX + i;
                    var pattern = patterns[i];
                    var predictor = 1;
                    if (!isInitialRow && (pattern & (pattern - 1)) != 0)
                    {
                        // Formula (5): the quad has more than one significant sample
                        var largest = Math.Max(Math.Max(exponents[(2 * x) + 1], exponents[(2 * x) + 2]), Math.Max(exponents[(2 * x) + 3], exponents[(2 * x) + 4]));
                        predictor = Math.Max(1, largest - 1);
                    }

                    var bound = predictor + residuals[i];
                    if (bound > maxExponent)
                        throw Invalid("a magnitude exceeds the bit-planes of the code-block.");

                    var bottomLeft = 0;
                    var bottomRight = 0;
                    for (var j = 0; j < 4; j++)
                    {
                        if ((pattern & (1 << j)) == 0)
                            continue;

                        var isKnown = (known[i] >> j) & 1;
                        var isOne = (ones[i] >> j) & 1;
                        var bitCount = bound - isKnown;
                        var value = DecodeMagSgnValue(bitCount, isOne);
                        var magnitude = (value >> 1) + 1;
                        if (magnitude > maxMagnitude)
                            throw Invalid("a magnitude exceeds the bit-planes of the code-block.");

                        var exponent = 32 - System.Numerics.BitOperations.LeadingZeroCount((2 * magnitude) - 1);
                        if (j == 1)
                        {
                            bottomLeft = exponent;
                        }
                        else if (j == 3)
                        {
                            bottomRight = exponent;
                        }

                        // j: the top-left, bottom-left, top-right and bottom-right samples of the quad (7.2). A sample of a
                        // padded column or row is not in the code-block
                        var sampleX = (2 * x) + (j >> 1);
                        var sampleY = (2 * quadY) + (j & 1);
                        if (sampleX >= width || sampleY >= height)
                            throw Invalid("a sample outside of the code-block is significant.");

                        var index = (sampleY * width) + sampleX;
                        magnitudes[index] = magnitude;
                        states[index] = (byte)(Significant | ((value & 1) != 0 ? Negative : 0));
                    }

                    nextExponents[(2 * x) + 2] = (byte)bottomLeft;
                    nextExponents[(2 * x) + 3] = (byte)bottomRight;
                }
            }

            var swap = exponents;
            exponents = nextExponents;
            nextExponents = swap;
        }
    }

    /// <summary>The unsigned residuals of a quad-pair, with the interleaving of 7.3.4 (decodeUPrefix, decodeUSuffix and decodeUExtension of 7.3.6).</summary>
    /// <param name="isInitialRow">Whether the quads are in the first row of quads.</param>
    /// <param name="pairLength">The number of quads of the pair, 1 or 2.</param>
    /// <param name="offsets">The unsigned residual offsets of the quads.</param>
    /// <param name="residuals">Receives the unsigned residuals of the quads; 0 for a quad that is not in the pair.</param>
    private void DecodeResiduals(bool isInitialRow, int pairLength, ReadOnlySpan<int> offsets, Span<int> residuals)
    {
        var prefix0 = 0;
        var prefix1 = 0;
        var bias = 0;
        if (isInitialRow && pairLength == 2 && offsets[0] != 0 && offsets[1] != 0)
        {
            // In the first row of quads, a MEL symbol tells whether both residuals of a pair exceed 2
            if (DecodeMelSymbol() != 0)
            {
                bias = 2;
                prefix0 = DecodePrefix();
                prefix1 = DecodePrefix();
            }
            else
            {
                // The second residual is one bit when the first prefix has three bits
                prefix0 = DecodePrefix();
                prefix1 = prefix0 > 2 ? ReadVlcBit() + 1 : DecodePrefix();
            }
        }
        else
        {
            if (offsets[0] != 0)
            {
                prefix0 = DecodePrefix();
            }

            if (pairLength == 2 && offsets[1] != 0)
            {
                prefix1 = DecodePrefix();
            }
        }

        // The suffixes of both quads, then their extensions
        var suffix0 = prefix0 >= 3 ? ReadVlcBits(prefix0 == 3 ? 1 : 5) : 0;
        var suffix1 = prefix1 >= 3 ? ReadVlcBits(prefix1 == 3 ? 1 : 5) : 0;

        // Formulae (3) and (4)
        residuals[0] = prefix0 == 0 ? 0 : bias + prefix0 + suffix0 + (4 * (suffix0 >= 28 ? ReadVlcBits(4) : 0));
        residuals[1] = prefix1 == 0 ? 0 : bias + prefix1 + suffix1 + (4 * (suffix1 >= 28 ? ReadVlcBits(4) : 0));
    }

    /// <summary>decodeUPrefix (7.3.6): the codewords 1, 01, 001 and 000 stand for the prefix values 1, 2, 3 and 5.</summary>
    private int DecodePrefix()
    {
        // The bits that are not in the bit-stream are 0: they can only make the codeword longer than the bits left
        var bits = PeekVlcBits(3);
        var (value, length) = (bits & 1) != 0 ? (1, 1) : (bits & 2) != 0 ? (2, 2) : (bits & 4) != 0 ? (3, 3) : (5, 3);
        SkipVlcBits(length);
        return value;
    }

    /// <summary>decodeCxtVLC (7.3.5): the codeword the next bits start with.</summary>
    /// <remarks>
    /// The tables give the codeword of a context for any seven bits; the bits that are not in the bit-stream are 0, and
    /// the codeword is consumed only when all its bits are there, as reading it bit by bit would require.
    /// </remarks>
    private uint DecodeCodeword(bool isInitialRow, int context)
    {
        var entry = Jpeg2000HtVlcTables.Find(isInitialRow, context, PeekVlcBits(Jpeg2000HtVlcTables.MaxCodewordLength));
        SkipVlcBits((int)(entry & 7));
        return entry;
    }

    /// <summary>decodeMagSgnValue (7.3.8).</summary>
    private uint DecodeMagSgnValue(int bitCount, int isOne)
    {
        uint value;
        if (bitCount <= 32)
        {
            value = (uint)ReadMagSgnBits(bitCount);
        }
        else
        {
            // A shift of 32 bits or more wraps around, as it does bit by bit
            value = 0u;
            for (var i = 0; i < bitCount; i++)
            {
                value |= (uint)ReadMagSgnBits(1) << i;
            }
        }

        return value + ((uint)isOne << bitCount);
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
    private ulong ReadMagSgnBits(int count)
    {
        if (count > _magSgnCount)
        {
            FillMagSgnWindow();
            if (count > _magSgnCount)
            {
                throw _magSgnError == MagSgnError.NotStuffed
                    ? Invalid("a bit is not stuffed after an FF byte of its MagSgn byte-stream.")
                    : Invalid("its MagSgn byte-stream is too short.");
            }
        }

        var value = _magSgnWindow & ((1UL << count) - 1);
        _magSgnWindow >>= count;
        _magSgnCount -= count;
        return value;
    }

    /// <summary>Appends the bits of the next bytes of the MagSgn byte-stream to its window, until it holds more than 56 bits or a byte cannot be read.</summary>
    private void FillMagSgnWindow()
    {
        while (_magSgnCount <= 56 && _magSgnError == MagSgnError.None)
        {
            var bits = _magSgnLast == 0xFF ? 7 : 8;
            int value;
            if (_magSgnPosition < _prefixLength)
            {
                value = ReadCleanupByte(_magSgnPosition);
                if ((value & (1 << bits)) != 0)
                {
                    _magSgnError = MagSgnError.NotStuffed;
                    return;
                }
            }
            else if (_magSgnPosition == _prefixLength)
            {
                value = 0xFF;
            }
            else
            {
                _magSgnError = MagSgnError.TooShort;
                return;
            }

            _magSgnLast = value;
            _magSgnPosition++;
            _magSgnWindow |= (ulong)(value & ((1 << bits) - 1)) << _magSgnCount;
            _magSgnCount += bits;
        }
    }

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

    /// <summary>importVLCBit (7.1.4): the next bit.</summary>
    private int ReadVlcBit() => ReadVlcBits(1);

    /// <summary>importVLCBit (7.1.4), for <paramref name="count"/> bits at once, the first one in the least significant bit.</summary>
    private int ReadVlcBits(int count)
    {
        var value = PeekVlcBits(count);
        SkipVlcBits(count);
        return value;
    }

    /// <summary>Gets the next <paramref name="count"/> bits of the VLC bit-stream (at most 32) without consuming them; the bits after the end of the bit-stream are 0.</summary>
    private int PeekVlcBits(int count)
    {
        if (count > _vlcCount)
        {
            FillVlcWindow();
        }

        return (int)(_vlcWindow & ((1UL << count) - 1));
    }

    /// <summary>Consumes the next <paramref name="count"/> bits of the VLC bit-stream.</summary>
    /// <exception cref="InvalidImageContentException">The bit-stream ends before.</exception>
    private void SkipVlcBits(int count)
    {
        if (count > _vlcCount)
        {
            FillVlcWindow();
            if (count > _vlcCount)
                throw Invalid("its VLC byte-stream is too short.");
        }

        _vlcWindow >>= count;
        _vlcCount -= count;
    }

    /// <summary>Appends the bits of the next bytes of the VLC byte-stream, read backward, to its window, until it holds more than 56 bits or the byte-stream ends.</summary>
    private void FillVlcWindow()
    {
        while (_vlcCount <= 56 && !_vlcEnded)
        {
            if (_vlcPosition < _prefixLength)
            {
                _vlcEnded = true;
                return;
            }

            var value = ReadCleanupByte(_vlcPosition);
            var bits = _vlcLast > 0x8F && (value & 0x7F) == 0x7F ? 7 : 8;
            _vlcLast = value;
            _vlcPosition--;
            _vlcWindow |= (ulong)(value & ((1 << bits) - 1)) << _vlcCount;
            _vlcCount += bits;
        }
    }

    /// <summary>The HT SigProp decoding procedure (7.4).</summary>
    private void DecodeSignificancePropagation(Span<byte> states, int width, int height, bool isVerticallyCausal)
    {
        // initSP
        _sigPropPosition = 0;
        _sigPropBits = 0;
        _sigPropValue = 0;
        _sigPropLast = 0;
        for (var stripe = 0; stripe < height; stripe += 4)
        {
            var stripeEnd = Math.Min(stripe + 4, height);
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

                        // The propagation neighborhood: the eight neighbors that are in the code-block, without the next
                        // stripe when the contexts are vertically causal. A neighbor counts when it is significant, or,
                        // if it comes earlier in the scan, when this pass made it significant
                        var lastRow = isVerticallyCausal ? stripeEnd - 1 : height - 1;
                        var hasSignificantNeighbor = false;
                        for (var neighborY = Math.Max(0, y - 1); neighborY <= Math.Min(lastRow, y + 1) && !hasSignificantNeighbor; neighborY++)
                        {
                            for (var neighborX = Math.Max(0, x - 1); neighborX <= Math.Min(width - 1, x + 1); neighborX++)
                            {
                                if (neighborX == x && neighborY == y)
                                    continue;

                                var neighbor = states[(neighborY * width) + neighborX];
                                if ((neighbor & Significant) != 0)
                                {
                                    hasSignificantNeighbor = true;
                                    break;
                                }

                                // Earlier in the stripe-oriented scan: a previous stripe, a previous column of this stripe,
                                // or the samples above in this column
                                var isEarlier = neighborY < stripe || (neighborY < stripeEnd && (neighborX < x || (neighborX == x && neighborY < y)));
                                if (isEarlier && (neighbor & RefinementBit) != 0)
                                {
                                    hasSignificantNeighbor = true;
                                    break;
                                }
                            }
                        }

                        if (hasSignificantNeighbor)
                        {
                            states[index] |= (byte)(Refined | (ReadSigPropBit() != 0 ? RefinementBit : 0));
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
