using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoder of a code-block of the high-throughput block coder (Rec. ITU-T T.814 | ISO/IEC 15444-15), the reverse of
/// <see cref="Jpeg2000HtBlockDecoder"/>: one HT cleanup pass, which codes every magnitude bit of the code-block down to
/// one bit-plane at once, without HT refinement passes.
/// </summary>
/// <remarks>
/// <para>
/// The bit-streams are those the decoding procedures of clause 7 read: the MagSgn bit-stream from the start of the HT
/// cleanup segment (7.1.2), the MEL bit-stream after it (7.1.3), and the VLC bit-stream read backward from the end
/// (7.1.4), followed by the suffix length (7.1.1). Each one is written with the bit-stuffing its reading procedure
/// expects, so that the segment never holds a byte FF followed by a byte above 8F.
/// </para>
/// <para>
/// The quads are visited in the order of 7.3: the CxtVLC codeword of each quad (7.3.5), with the MEL symbol of a quad
/// whose context is 0 (7.3.3), then the unsigned residuals of the quad-pair (7.3.4 and 7.3.6), and the MagSgn values of
/// its significant samples (7.3.8). Among the exponent max bound patterns that a context, a significance pattern and an
/// unsigned residual offset allow, the encoder chooses the one whose codeword and MagSgn bits are the shortest.
/// </para>
/// </remarks>
internal sealed class Jpeg2000HtBlockEncoder : IDisposable
{
    private const int MaxCleanupLength = 65534;
    private const int MaxSuffixLength = 4079;

    // Two exponents per quad of the widest code-block, and those of a quad that does not exist on each side
    private const int ExponentRowLength = 1024 + 4;

    // The most bytes each bit-stream needs for a code-block of 4096 samples, with a margin
    private const int MagSgnCapacity = MaxCleanupLength + 16;
    private const int SuffixCapacity = MaxSuffixLength + 16;

    // Table 2
    private static ReadOnlySpan<byte> MelExponents => [0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 4, 5];

    // For each table, context, significance pattern, unsigned residual offset and pattern of the bits of the MagSgn
    // values at the exponent bound (the bit U - 1 of each sample): the best row of Annex C, or 0 when none can code them
    private static readonly uint[] Codewords = CreateCodewords();

    private readonly Av1PooledArray<byte> _magSgn;
    private readonly Av1PooledArray<byte> _mel;
    private readonly Av1PooledArray<byte> _vlc;
    private readonly Av1PooledArray<byte> _exponents;

    // The MagSgn bit-stream: the bits that are not written yet, the first one in the least significant bit, and the
    // number of bits the next byte holds (7 after an FF byte)
    private int _magSgnLength;
    private ulong _magSgnBits;
    private int _magSgnCount;
    private int _magSgnCapacity;

    // The MEL bit-stream, the most significant bit of a byte first, and the state of the MEL coder (7.3.3)
    private int _melLength;
    private int _melByte;
    private int _melFree;
    private int _melState;
    private int _melRun;

    // The VLC bit-stream, in the order it is read: the bits that are not written yet, the first one in the least
    // significant bit, and the last byte written
    private int _vlcLength;
    private ulong _vlcBits;
    private int _vlcCount;
    private int _vlcLast;

    public Jpeg2000HtBlockEncoder(AllocationScope scope)
    {
        Av1PooledArray<byte>? magSgn = null;
        Av1PooledArray<byte>? mel = null;
        Av1PooledArray<byte>? vlc = null;
        try
        {
            magSgn = Av1PooledArray<byte>.Rent(scope, MagSgnCapacity, clear: false, AllocationKind.Temporary);
            mel = Av1PooledArray<byte>.Rent(scope, SuffixCapacity, clear: false, AllocationKind.Temporary);
            vlc = Av1PooledArray<byte>.Rent(scope, SuffixCapacity, clear: false, AllocationKind.Temporary);
            _exponents = Av1PooledArray<byte>.Rent(scope, 2 * ExponentRowLength, clear: false, AllocationKind.Temporary);
        }
        catch
        {
            magSgn?.Dispose();
            mel?.Dispose();
            vlc?.Dispose();
            throw;
        }

        _magSgn = magSgn;
        _mel = mel;
        _vlc = vlc;
    }

    /// <summary>Encodes the coefficients of a code-block with one HT cleanup pass that codes every bit-plane.</summary>
    /// <param name="samples">The coefficients, row by row: the magnitude in the 31 low bits and the sign in the high one.</param>
    /// <param name="width">The width of the code-block.</param>
    /// <param name="height">The height of the code-block.</param>
    /// <param name="output">The writer the HT cleanup segment is appended to.</param>
    /// <param name="length">Receives the number of bytes of the segment.</param>
    /// <returns>The number of magnitude bit-planes of the coefficients: 0 when every coefficient is zero, and nothing is written.</returns>
    public int Encode(ReadOnlySpan<int> samples, int width, int height, PooledPayloadWriter output, out int length)
        => Encode(samples, width, height, output, out length, candidates: default, isIrreversible: false);

    /// <summary>
    /// Encodes the coefficients of a code-block with one HT cleanup pass that codes every bit-plane or, to choose the
    /// lowest coded bit-plane afterwards, with one HT cleanup segment for each possible lowest bit-plane.
    /// </summary>
    /// <param name="samples">The coefficients, row by row: the magnitude in the 31 low bits and the sign in the high one.</param>
    /// <param name="width">The width of the code-block.</param>
    /// <param name="height">The height of the code-block.</param>
    /// <param name="output">The writer the HT cleanup segments are appended to.</param>
    /// <param name="length">Receives the number of bytes of the segments.</param>
    /// <param name="candidates">
    /// Empty to write the segment that codes every bit-plane only. Otherwise receives, for each lowest bit-plane from the
    /// highest one (<c>bitPlanes - 1</c>) to 0, the length of its segment, appended in that order, and how much more it
    /// reduces the squared error of the coefficients than the previous one (as <see cref="Jpeg2000Tier1Encoder"/>
    /// measures it): <c>bitPlanes</c> of them.
    /// </param>
    /// <param name="isIrreversible">
    /// Whether the coefficients are quantization indices of the irreversible path, which a decoder reconstructs in the
    /// middle of their interval: the squared errors are measured from that point.
    /// </param>
    /// <returns>The number of magnitude bit-planes of the coefficients: 0 when every coefficient is zero, and nothing is written.</returns>
    public int Encode(ReadOnlySpan<int> samples, int width, int height, PooledPayloadWriter output, out int length, Span<Jpeg2000CodingPass> candidates, bool isIrreversible)
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

        var bitPlanes = 32 - BitOperations.LeadingZeroCount((uint)maximum);
        if (candidates.IsEmpty)
        {
            length = EncodeCleanup(samples, width, height, plane: 0, output);
            return bitPlanes;
        }

        var offset = isIrreversible ? 0.5 : 0;
        var previous = 0.0;
        for (var i = 0; i < bitPlanes; i++)
        {
            var plane = bitPlanes - 1 - i;
            var segment = EncodeCleanup(samples, width, height, plane, output);
            var reduction = GetErrorReduction(samples, plane, offset);
            candidates[i] = new Jpeg2000CodingPass { Length = segment, Distortion = reduction - previous };
            previous = reduction;
            length += segment;
        }

        return bitPlanes;
    }

    /// <summary>Encodes the HT cleanup pass whose lowest bit-plane is <paramref name="plane"/>.</summary>
    /// <param name="samples">The coefficients, row by row: the magnitude in the 31 low bits and the sign in the high one.</param>
    /// <param name="width">The width of the code-block: at most 1024.</param>
    /// <param name="height">The height of the code-block.</param>
    /// <param name="plane">The lowest bit-plane the pass codes; a coefficient is coded as its magnitude shifted right by it.</param>
    /// <param name="output">The writer the HT cleanup segment is appended to.</param>
    /// <returns>The length of the segment (Lcup).</returns>
    /// <exception cref="InvalidOperationException">The segment would exceed the lengths of T.814.</exception>
    public int EncodeCleanup(ReadOnlySpan<int> samples, int width, int height, int plane, PooledPayloadWriter output)
    {
        // initMS, initMEL and initVLC of the encoder: the VLC bit-stream starts in the high half of the second last byte,
        // after a virtual FF byte, its low half being the four bits of the suffix length written at the end
        _magSgnLength = 0;
        _magSgnBits = 0;
        _magSgnCount = 0;
        _magSgnCapacity = 8;
        _melLength = 0;
        _melByte = 0;
        _melFree = 8;
        _melState = 0;
        _melRun = 0;
        _vlcLength = 0;
        _vlcBits = 0x0F;
        _vlcCount = 4;
        _vlcLast = 0xFF;

        var quadsWide = (width + 1) / 2;
        var quadsHigh = (height + 1) / 2;
        _exponents.Span[..(2 * ExponentRowLength)].Clear();
        Span<uint> values = stackalloc uint[8];
        Span<int> sampleExponents = stackalloc int[8];
        for (var quadY = 0; quadY < quadsHigh; quadY++)
        {
            var isInitialRow = quadY == 0;
            var aboveRow = (quadY & 1) * ExponentRowLength;
            var exponents = _exponents.Span.Slice(aboveRow, ExponentRowLength);
            var nextExponents = _exponents.Span.Slice(ExponentRowLength - aboveRow, ExponentRowLength);
            var previousPattern = 0;
            for (var quadX = 0; quadX < quadsWide; quadX += 2)
            {
                var hasSecond = quadX + 1 < quadsWide;
                var pattern0 = ReadQuad(samples, width, height, plane, quadX, quadY, values[..4], sampleExponents[..4]);
                var pattern1 = hasSecond ? ReadQuad(samples, width, height, plane, quadX + 1, quadY, values[4..], sampleExponents[4..]) : 0;

                // The first quad of the pair, then the second one, whose context depends on the first one
                var context0 = GetContext(exponents, quadX, isInitialRow, previousPattern);
                var bound0 = EncodeQuad(exponents, quadX, isInitialRow, context0, pattern0, values[..4], sampleExponents[..4], out var residual0, out var known0);
                var context1 = 0;
                var bound1 = 0;
                var residual1 = 0;
                var known1 = 0;
                if (hasSecond)
                {
                    context1 = GetContext(exponents, quadX + 1, isInitialRow, pattern0);
                    bound1 = EncodeQuad(exponents, quadX + 1, isInitialRow, context1, pattern1, values[4..], sampleExponents[4..], out residual1, out known1);
                    previousPattern = pattern1;
                }
                else
                {
                    previousPattern = pattern0;
                }

                EncodeResiduals(isInitialRow, hasSecond, residual0, residual1);

                // The MagSgn values of the significant samples of the two quads, in their order
                WriteMagSgnValues(pattern0, bound0, known0, values[..4]);
                WriteBottomExponents(nextExponents, quadX, sampleExponents[..4]);
                if (hasSecond)
                {
                    WriteMagSgnValues(pattern1, bound1, known1, values[4..]);
                    WriteBottomExponents(nextExponents, quadX + 1, sampleExponents[4..]);
                }
            }
        }

        return Terminate(output);
    }

    public void Dispose()
    {
        _magSgn.Dispose();
        _mel.Dispose();
        _vlc.Dispose();
        _exponents.Dispose();
    }

    /// <summary>Gets how much coding the bit-planes down to <paramref name="plane"/> reduces the squared error of the coefficients.</summary>
    /// <remarks>A coded coefficient is reconstructed as <see cref="Jpeg2000HtBlockDecoder"/> does (7.6): in the middle of the bit-planes that are not coded.</remarks>
    private static double GetErrorReduction(ReadOnlySpan<int> samples, int plane, double offset)
    {
        var half = plane == 0 ? offset : 1 << (plane - 1);
        var reduction = 0.0;
        foreach (var sample in samples)
        {
            var magnitude = sample & int.MaxValue;
            if (magnitude >> plane == 0)
                continue;

            var value = magnitude + offset;
            var reconstruction = ((magnitude >> plane) << plane) + half;
            reduction += reconstruction * ((2 * value) - reconstruction);
        }

        return reduction;
    }

    /// <summary>Reads the four samples of a quad (7.2: top-left, bottom-left, top-right, bottom-right).</summary>
    /// <param name="samples">The coefficients of the code-block.</param>
    /// <param name="width">The width of the code-block.</param>
    /// <param name="height">The height of the code-block.</param>
    /// <param name="plane">The lowest coded bit-plane.</param>
    /// <param name="quadX">The column of the quad.</param>
    /// <param name="quadY">The row of the quad.</param>
    /// <param name="values">Receives the MagSgn value of each significant sample: twice its magnitude less one, plus its sign (7.3.8).</param>
    /// <param name="exponents">Receives the exponent of each sample: the number of bits of twice its magnitude less one, 0 when it is not significant.</param>
    /// <returns>The significance pattern of the quad.</returns>
    private static int ReadQuad(ReadOnlySpan<int> samples, int width, int height, int plane, int quadX, int quadY, Span<uint> values, Span<int> exponents)
    {
        var pattern = 0;
        for (var j = 0; j < 4; j++)
        {
            var x = (2 * quadX) + (j >> 1);
            var y = (2 * quadY) + (j & 1);
            values[j] = 0;
            exponents[j] = 0;
            if (x >= width || y >= height)
                continue;

            var sample = (uint)samples[(y * width) + x];
            var magnitude = (sample & int.MaxValue) >> plane;
            if (magnitude == 0)
                continue;

            pattern |= 1 << j;
            values[j] = (2 * (magnitude - 1)) + (sample >> 31);
            exponents[j] = 32 - BitOperations.LeadingZeroCount((2 * magnitude) - 1);
        }

        return pattern;
    }

    /// <summary>Stores the exponents of the bottom-left and bottom-right samples of a quad for the contexts of the next row of quads.</summary>
    private static void WriteBottomExponents(Span<byte> nextExponents, int quadX, ReadOnlySpan<int> exponents)
        => BinaryPrimitives.WriteUInt16LittleEndian(nextExponents.Slice((2 * quadX) + 2, 2), (ushort)(exponents[1] | (exponents[3] << 8)));

    /// <summary>The context of a quad (7.3.5): formula (1) in the first row of quads, formula (2) in the others.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetContext(ReadOnlySpan<byte> exponents, int x, bool isInitialRow, int previousPattern)
    {
        if (isInitialRow)
            return ((previousPattern | (previousPattern >> 1)) & 1) + (2 * ((previousPattern >> 2) & 1)) + (4 * ((previousPattern >> 3) & 1));

        var above = BinaryPrimitives.ReadUInt32LittleEndian(exponents.Slice((2 * x) + 1, 4));
        var west = (above & 0xFFFF) != 0 ? 1 : 0;
        var east = (above >> 16) != 0 ? 1 : 0;
        return west + (2 * (((previousPattern >> 2) | (previousPattern >> 3)) & 1)) + (4 * east);
    }

    /// <summary>
    /// Writes the MEL symbol and the CxtVLC codeword of a quad (7.3.5): its significance pattern, whether it has an unsigned
    /// residual and its exponent max bound patterns.
    /// </summary>
    /// <param name="exponents">The exponents of the bottom samples of the row of quads above.</param>
    /// <param name="x">The index of the quad in its row.</param>
    /// <param name="isInitialRow">Whether the quad is in the first row of quads.</param>
    /// <param name="context">The context of the quad.</param>
    /// <param name="pattern">The significance pattern of the quad.</param>
    /// <param name="values">The MagSgn values of its samples.</param>
    /// <param name="sampleExponents">The exponents of its samples.</param>
    /// <param name="residual">Receives the unsigned residual of the quad: its exponent bound less its predictor (7.3.7); 0 when the quad has no unsigned residual.</param>
    /// <param name="known">Receives the known-bit pattern of the codeword.</param>
    /// <returns>The exponent bound of the quad (U_q).</returns>
    private int EncodeQuad(ReadOnlySpan<byte> exponents, int x, bool isInitialRow, int context, int pattern, ReadOnlySpan<uint> values, ReadOnlySpan<int> sampleExponents, out int residual, out int known)
    {
        residual = 0;
        known = 0;

        // A quad whose neighbors are all insignificant is first described by a MEL symbol
        if (context == 0)
        {
            EncodeMelSymbol(pattern != 0 ? 1 : 0);
            if (pattern == 0)
                return 0;
        }

        // Formula (5): the predictor of the exponent bound
        var predictor = 1;
        if (!isInitialRow && (pattern & (pattern - 1)) != 0)
        {
            var above = BinaryPrimitives.ReadUInt32LittleEndian(exponents.Slice((2 * x) + 1, 4));
            var largest = Math.Max(Math.Max(above & 0xFF, (above >> 8) & 0xFF), Math.Max((above >> 16) & 0xFF, above >> 24));
            predictor = Math.Max(1, (int)largest - 1);
        }

        var largestExponent = Math.Max(Math.Max(sampleExponents[0], sampleExponents[1]), Math.Max(sampleExponents[2], sampleExponents[3]));
        var bound = Math.Max(predictor, largestExponent);
        residual = bound - predictor;

        // The bit U_q - 1 of the MagSgn value of each significant sample: the codeword gives it for the samples of its
        // known-bit pattern
        var top = 0;
        if (pattern != 0)
        {
            for (var j = 0; j < 4; j++)
            {
                top |= (int)((values[j] >> (bound - 1)) & 1) << j;
            }

            top &= pattern;
        }

        var row = Codewords[GetCodewordIndex(isInitialRow, context, pattern, residual != 0 ? 1 : 0, top)];
        if (row == 0)
            throw new InvalidOperationException("No codeword of the HT code tables describes a quad.");

        WriteVlcBits(row & 0x7F, (int)((row >> 7) & 7));
        known = (int)((row >> 10) & 0x0F);
        return bound;
    }

    private static int GetCodewordIndex(bool isInitialRow, int context, int pattern, int offset, int top)
        => (((((((isInitialRow ? 0 : 8) + context) * 16) + pattern) * 2) + offset) * 16) + top;

    /// <summary>Writes the unsigned residuals of a quad-pair, with the interleaving of 7.3.4 (the reverse of decodeUPrefix, decodeUSuffix and decodeUExtension of 7.3.6).</summary>
    private void EncodeResiduals(bool isInitialRow, bool hasSecond, int residual0, int residual1)
    {
        if ((residual0 | residual1) == 0)
            return;

        var value0 = residual0;
        var value1 = residual1;
        var isShortSecond = false;
        if (isInitialRow && hasSecond && residual0 != 0 && residual1 != 0)
        {
            // In the first row of quads, a MEL symbol tells whether both residuals exceed 2: they are then coded less 2;
            // otherwise the second residual is one bit when the first one exceeds 2
            if (residual0 > 2 && residual1 > 2)
            {
                EncodeMelSymbol(1);
                value0 -= 2;
                value1 -= 2;
            }
            else
            {
                EncodeMelSymbol(0);
                isShortSecond = residual0 > 2;
            }
        }

        // The prefixes of both quads, then their suffixes, then their extensions
        var prefix0 = value0 == 0 ? 0 : GetPrefix(value0);
        var prefix1 = value1 == 0 ? 0 : GetPrefix(value1);
        if (prefix0 != 0)
        {
            WritePrefix(prefix0);
        }

        if (isShortSecond)
        {
            WriteVlcBits((uint)(value1 - 1), 1);
            prefix1 = 0;
        }
        else if (prefix1 != 0)
        {
            WritePrefix(prefix1);
        }

        var extension0 = WriteSuffix(prefix0, value0);
        var extension1 = WriteSuffix(prefix1, value1);
        if (extension0 >= 0)
        {
            WriteVlcBits((uint)extension0, 4);
        }

        if (extension1 >= 0)
        {
            WriteVlcBits((uint)extension1, 4);
        }

        // The prefix values 1, 2, 3 and 5 (formulae (3) and (4))
        static int GetPrefix(int value) => value switch
        {
            1 => 1,
            2 => 2,
            3 or 4 => 3,
            _ => 5,
        };
    }

    /// <summary>Writes the codeword of a prefix value: 1, 01, 001 and 000 for 1, 2, 3 and 5.</summary>
    private void WritePrefix(int prefix)
    {
        switch (prefix)
        {
            case 1:
                WriteVlcBits(1, 1);
                break;

            case 2:
                WriteVlcBits(2, 2);
                break;

            case 3:
                WriteVlcBits(4, 3);
                break;

            default:
                WriteVlcBits(0, 3);
                break;
        }
    }

    /// <summary>Writes the suffix of a residual value.</summary>
    /// <returns>The extension that follows the suffixes of the pair; -1 when there is none.</returns>
    private int WriteSuffix(int prefix, int value)
    {
        switch (prefix)
        {
            case 3:
                WriteVlcBits((uint)(value - 3), 1);
                return -1;

            case 5 when value - 5 < 28:
                WriteVlcBits((uint)(value - 5), 5);
                return -1;

            case 5:
            {
                var excess = value - 33;
                if (excess >> 2 > 15)
                    throw new InvalidOperationException("An unsigned residual of the high-throughput block coder exceeds 96.");

                WriteVlcBits((uint)(28 + (excess & 3)), 5);
                return excess >> 2;
            }

            default:
                return -1;
        }
    }

    /// <summary>Writes the MagSgn values of the significant samples of a quad (the reverse of decodeMagSgnValue, 7.3.8).</summary>
    private void WriteMagSgnValues(int pattern, int bound, int known, ReadOnlySpan<uint> values)
    {
        var remaining = pattern;
        while (remaining != 0)
        {
            var j = BitOperations.TrailingZeroCount(remaining);
            remaining &= remaining - 1;

            // A sample of the known-bit pattern has its bit U_q - 1 in the codeword
            var count = bound - ((known >> j) & 1);
            WriteMagSgnBits(values[j] & (uint)((1UL << count) - 1), count);
        }
    }

    /// <summary>The MEL coder (the reverse of decodeMELSym, 7.3.3): runs of 0 symbols, whose length adapts.</summary>
    private void EncodeMelSymbol(int symbol)
    {
        int exponent = MelExponents[_melState];
        if (symbol == 0)
        {
            _melRun++;
            if (_melRun == 1 << exponent)
            {
                WriteMelBit(1);
                _melRun = 0;
                _melState = Math.Min(12, _melState + 1);
            }

            return;
        }

        // An interrupted run: the bit 0, then the length of the run in the bits of its exponent
        WriteMelBit(0);
        for (var i = exponent - 1; i >= 0; i--)
        {
            WriteMelBit((_melRun >> i) & 1);
        }

        _melRun = 0;
        _melState = Math.Max(0, _melState - 1);
    }

    /// <summary>Appends a bit to the MEL bit-stream (7.1.3): a byte after an FF byte has seven bits, its most significant one being 0.</summary>
    private void WriteMelBit(int bit)
    {
        _melByte = (_melByte << 1) | bit;
        _melFree--;
        if (_melFree == 0)
        {
            _mel.Span[_melLength++] = (byte)_melByte;
            _melFree = _melByte == 0xFF ? 7 : 8;
            _melByte = 0;
        }
    }

    /// <summary>Appends bits to the MagSgn bit-stream (7.1.2), the first one in the least significant bit: a byte after an FF byte has seven bits, its most significant one being 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteMagSgnBits(uint value, int count)
    {
        _magSgnBits |= (ulong)value << _magSgnCount;
        _magSgnCount += count;
        while (_magSgnCount >= _magSgnCapacity)
        {
            var capacity = _magSgnCapacity;
            var value8 = (int)(_magSgnBits & ((1UL << capacity) - 1));
            _magSgn.Span[_magSgnLength++] = (byte)value8;
            _magSgnBits >>= capacity;
            _magSgnCount -= capacity;
            _magSgnCapacity = value8 == 0xFF ? 7 : 8;
        }
    }

    /// <summary>
    /// Appends bits to the VLC bit-stream (7.1.4), the first one in the least significant bit. The bytes are read backward:
    /// a byte that follows a byte above 8F in that order, and whose seven low bits are 1, has these seven bits only.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteVlcBits(uint value, int count)
    {
        _vlcBits |= (ulong)value << _vlcCount;
        _vlcCount += count;
        while (_vlcCount >= 7)
        {
            int value8;
            if (_vlcLast > 0x8F && (_vlcBits & 0x7F) == 0x7F)
            {
                value8 = 0x7F;
                _vlcBits >>= 7;
                _vlcCount -= 7;
            }
            else if (_vlcCount >= 8)
            {
                value8 = (int)(_vlcBits & 0xFF);
                _vlcBits >>= 8;
                _vlcCount -= 8;
            }
            else
            {
                break;
            }

            _vlc.Span[_vlcLength++] = (byte)value8;
            _vlcLast = value8;
        }
    }

    /// <summary>Ends the three bit-streams and appends the HT cleanup segment: MagSgn, MEL, the VLC bytes from the last one, and the suffix length (7.1.1).</summary>
    private int Terminate(PooledPayloadWriter output)
    {
        // MagSgn: the last byte is padded with 1 bits, which a decoder reads after the end of the bit-stream; a last FF
        // byte is then what the decoder reads after it, and is left out
        if (_magSgnCount > 0)
        {
            var mask = (1 << _magSgnCapacity) - 1;
            _magSgn.Span[_magSgnLength++] = (byte)((int)(_magSgnBits | ~((1UL << _magSgnCount) - 1)) & mask);
        }

        if (_magSgnLength > 0 && _magSgn.Span[_magSgnLength - 1] == 0xFF)
        {
            _magSgnLength--;
        }

        // MEL: a run that is not interrupted is ended by the bit 1, which stands for a longer run than the symbols left;
        // the last byte is padded with 0 bits, and a last FF byte is followed by a byte of 0 bits, so that the segment
        // has no FF byte followed by a byte above 8F
        if (_melRun > 0)
        {
            WriteMelBit(1);
        }

        if (_melFree != (_melLength > 0 && _mel.Span[_melLength - 1] == 0xFF ? 7 : 8))
        {
            _mel.Span[_melLength++] = (byte)(_melByte << _melFree);
        }

        if (_melLength > 0 && _mel.Span[_melLength - 1] == 0xFF)
        {
            _mel.Span[_melLength++] = 0;
        }

        // VLC: the last byte is padded with 0 bits; the first one holds the low half of the suffix length
        if (_vlcCount > 0)
        {
            _vlc.Span[_vlcLength++] = (byte)(_vlcBits & ((1UL << _vlcCount) - 1));
        }

        var suffixLength = _melLength + _vlcLength + 1;
        var length = _magSgnLength + suffixLength;
        if (suffixLength > MaxSuffixLength || length > MaxCleanupLength)
            throw new InvalidOperationException("An HT cleanup segment exceeds the lengths of Rec. ITU-T T.814.");

        var vlc = _vlc.Span[.._vlcLength];
        vlc[0] = (byte)((vlc[0] & 0xF0) | (suffixLength & 0x0F));
        vlc.Reverse();
        output.Write(_magSgn.Span[.._magSgnLength]);
        output.Write(_mel.Span[.._melLength]);
        output.Write(vlc);
        output.Write((byte)(suffixLength >> 4));
        return length;
    }

    /// <summary>
    /// Builds the codeword lookup: for each table, context, significance pattern, unsigned residual offset and pattern of
    /// the bits U_q - 1, the row of Annex C whose codeword and MagSgn bits are the shortest, among those whose known-bit
    /// pattern only covers significant samples whose bit is the one of the known-1 pattern.
    /// </summary>
    private static uint[] CreateCodewords()
    {
        var codewords = new uint[2 * 8 * 16 * 2 * 16];
        Span<int> costs = stackalloc int[codewords.Length];
        costs.Fill(int.MaxValue);
        Fill(codewords, costs, Jpeg2000HtVlcTables.InitialRows, isInitialRow: true);
        Fill(codewords, costs, Jpeg2000HtVlcTables.NonInitialRows, isInitialRow: false);
        return codewords;

        static void Fill(uint[] codewords, Span<int> costs, ReadOnlySpan<byte> rows, bool isInitialRow)
        {
            for (var row = 0; row < rows.Length / 4; row++)
            {
                var entry = rows.Slice(4 * row, 4);
                var context = entry[0] >> 4;
                var pattern = entry[0] & 0x0F;
                var known = entry[1] >> 4;
                var knownOnes = entry[1] & 0x0F;
                var codeword = entry[2] & 0x7F;
                var offset = entry[2] >> 7;
                int length = entry[3];
                if ((known & ~pattern) != 0 || (knownOnes & ~known) != 0)
                    continue;

                // Each known bit saves one bit of the MagSgn bit-stream
                var cost = length - BitOperations.PopCount((uint)known);
                for (var top = 0; top < 16; top++)
                {
                    if ((top & ~pattern) != 0 || ((top ^ knownOnes) & known) != 0)
                        continue;

                    var index = GetCodewordIndex(isInitialRow, context, pattern, offset, top);
                    if (cost < costs[index])
                    {
                        costs[index] = cost;
                        codewords[index] = (uint)codeword | ((uint)length << 7) | ((uint)known << 10) | ((uint)knownOnes << 14) | (1u << 31);
                    }
                }
            }
        }
    }
}
