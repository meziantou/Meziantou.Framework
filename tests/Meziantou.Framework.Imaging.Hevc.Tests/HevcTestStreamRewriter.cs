using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Rewrites the headers of a stream of an encoder so that it uses syntax that no pinned encoder writes: long-term reference
/// pictures (from the sequence parameter set or from the slice header, with or without the most significant bits of their
/// picture order count) and reference picture list modification. The slice data are kept as they are, so the pictures
/// that the rewritten stream describes are not those of the encoder: the reference decoders tell what they are.
/// </summary>
/// <remarks>
/// The first picture of the stream becomes a long-term reference picture of every picture that follows it, in place of
/// its oldest short-term reference picture; a stream whose pictures refer to pictures that are no longer kept is not
/// suitable. The parameter sets are parsed by the library, and the positions of the syntax elements that are changed are
/// found with its bit reader.
/// </remarks>
internal static class HevcTestStreamRewriter
{
    /// <summary>Where the long-term reference picture of a slice is signalled, and whether its lists are modified.</summary>
    [Flags]
    internal enum Options
    {
        None = 0,

        /// <summary>The long-term reference picture is a candidate of the sequence parameter set (<c>lt_idx_sps</c>).</summary>
        SpsCandidate = 1,

        /// <summary><c>delta_poc_msb_present_flag</c> is 1 for the pictures whose picture order count is odd.</summary>
        MsbPresent = 2,

        /// <summary>The reference picture lists of the pictures whose picture order count is odd are modified.</summary>
        ModifyLists = 4,
    }

    /// <summary>Rewrites a stream; <see langword="null"/> when the stream is not suitable.</summary>
    public static byte[]? Rewrite(ReadOnlyMemory<byte> annexB, Options options)
    {
        var units = ReferenceHevcStream.SplitAnnexB(annexB);
        HevcSequenceParameterSet? sps = null;
        HevcPictureParameterSet? pps = null;
        byte[]? spsRbsp = null;
        byte[]? ppsRbsp = null;
        foreach (var unit in units)
        {
            var type = ReferenceHevcStream.GetNalUnitType(unit.Span);
            if (type is ReferenceHevcStream.NalSps)
            {
                var rbsp = ReferenceHevcStream.GetRbsp(unit.Span);
                if (spsRbsp is not null && !spsRbsp.AsSpan().SequenceEqual(rbsp))
                    return null;

                spsRbsp = rbsp;
                sps = HevcSequenceParameterSet.ParseRbsp(rbsp);
            }
            else if (type is ReferenceHevcStream.NalPps)
            {
                var rbsp = ReferenceHevcStream.GetRbsp(unit.Span);
                if (ppsRbsp is not null && !ppsRbsp.AsSpan().SequenceEqual(rbsp))
                    return null;

                ppsRbsp = rbsp;
                pps = HevcPictureParameterSet.ParseRbsp(rbsp);
            }
        }

        if (sps is null || pps is null || sps.LongTermRefPicsPresentFlag || pps.ListsModificationPresentFlag || sps.ShortTermRefPicSets.Length is 0)
            return null;

        var newSps = RewriteSps(spsRbsp!, sps, options.HasFlag(Options.SpsCandidate));
        var newPps = options.HasFlag(Options.ModifyLists) ? RewritePps(ppsRbsp!) : ppsRbsp;
        if (newSps is null || newPps is null)
            return null;

        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(HevcTestStreamRewriter));
        using var layout = HevcPictureLayout.Create(scope, sps, pps);
        var result = new List<ReadOnlyMemory<byte>>();
        var kept = new HashSet<int>();
        HevcSliceSegmentHeader? previous = null;
        var maxLsb = 1 << sps.Log2MaxPicOrderCntLsb;
        var prevLsb = 0;
        var prevMsb = 0;
        var poc = 0;
        var isFirstPicture = true;
        foreach (var unit in units)
        {
            var type = ReferenceHevcStream.GetNalUnitType(unit.Span);
            if (type is ReferenceHevcStream.NalSps)
            {
                result.Add(ToNalUnit(unit.Span, newSps));
                continue;
            }

            if (type is ReferenceHevcStream.NalPps)
            {
                result.Add(ToNalUnit(unit.Span, newPps));
                continue;
            }

            if (type >= 32)
            {
                // The hash of a picture is the one of the picture of the encoder
                if (type is not ReferenceHevcStream.NalSuffixSei)
                {
                    result.Add(unit);
                }

                continue;
            }

            var rbsp = ReferenceHevcStream.GetRbsp(unit.Span);
            var nal = HevcNalUnitHeader.Read(unit.Span);
            var header = HevcSliceSegmentHeader.Parse(rbsp, nal, layout, previous);
            previous = header;
            if (header.DependentSliceSegmentFlag || nal.IsIdr)
            {
                if (nal.IsIdr)
                {
                    if (!isFirstPicture)
                        return null;

                    kept.Clear();
                    kept.Add(0);
                    poc = 0;
                    prevLsb = 0;
                    prevMsb = 0;
                }

                isFirstPicture = false;
                result.Add(unit);
                continue;
            }

            if (isFirstPicture)
                return null;

            if (header.FirstSliceSegmentInPicFlag)
            {
                // Clause 8.3.1
                var lsb = header.SlicePicOrderCntLsb;
                var msb = lsb < prevLsb && prevLsb - lsb >= maxLsb / 2 ? prevMsb + maxLsb : lsb > prevLsb && lsb - prevLsb > maxLsb / 2 ? prevMsb - maxLsb : prevMsb;
                poc = msb + lsb;
                if (nal.TemporalId is 0 && !nal.IsRasl && !nal.IsRadl && !nal.IsSubLayerNonReference)
                {
                    prevLsb = lsb;
                    prevMsb = msb;
                }
            }

            var rewritten = RewriteSlice(rbsp, nal, header, sps, pps, poc, kept, options);
            if (rewritten is null)
                return null;

            result.Add(ToNalUnit(unit.Span, rewritten));
        }

        return ReferenceHevcStream.ToAnnexB(result);
    }

    private static byte[]? RewriteSlice(byte[] rbsp, HevcNalUnitHeader nal, HevcSliceSegmentHeader header, HevcSequenceParameterSet sps, HevcPictureParameterSet pps, int poc, HashSet<int> kept, Options options)
    {
        // The positions of the reference picture set and of the reference picture list modification in the header
        var reader = new HevcBitReader(rbsp);
        var first = reader.ReadFlag();
        if (nal.IsIrap)
        {
            reader.Skip(1);
        }

        reader.ReadUE();
        if (!first)
        {
            if (pps.DependentSliceSegmentsEnabledFlag)
            {
                reader.Skip(1);
            }

            reader.Skip(HevcSliceSegmentHeader.CeilLog2(sps.PicSizeInCtbsY));
        }

        reader.Skip(pps.NumExtraSliceHeaderBits);
        reader.ReadUE();
        if (pps.OutputFlagPresentFlag)
        {
            reader.Skip(1);
        }

        reader.Skip(sps.Log2MaxPicOrderCntLsb);
        var setStart = (int)reader.BitPosition;
        var sets = sps.ShortTermRefPicSets;
        if (reader.ReadFlag())
        {
            reader.Skip(HevcSliceSegmentHeader.CeilLog2(sets.Length));
        }
        else
        {
            HevcShortTermRefPicSet.Parse(ref reader, sets.Length, sets.Length, sets, sps.MaxDecPicBufferingMinus1[sps.HighestTid]);
        }

        var setEnd = (int)reader.BitPosition;
        if (sps.SpsTemporalMvpEnabledFlag)
        {
            reader.Skip(1);
        }

        if (sps.SampleAdaptiveOffsetEnabledFlag)
        {
            reader.Skip(sps.ChromaArrayType is 0 ? 1 : 2);
        }

        var isInter = header.SliceType is not HevcConstants.SliceI;
        if (isInter && reader.ReadFlag())
        {
            reader.ReadUE();
            if (header.SliceType is HevcConstants.SliceB)
            {
                reader.ReadUE();
            }
        }

        var modificationPosition = (int)reader.BitPosition;

        // The end of the header: the bit equal to 1 of byte_alignment( )
        var headerEnd = (header.DataOffset * 8) - 1;
        while (headerEnd > modificationPosition && !GetBit(rbsp, headerEnd))
        {
            headerEnd--;
        }

        // The short-term reference picture set without the first picture of the stream, or without its oldest picture
        var set = header.ShortTermRefPicSet!;
        var negative = new List<(int Delta, bool Used)>();
        for (var i = 0; i < set.NumNegativePics; i++)
        {
            negative.Add((set.DeltaPocS0[i], set.UsedByCurrPicS0[i]));
        }

        var index = negative.FindIndex(entry => poc + entry.Delta is 0);
        if (index < 0 && negative.Count < 2)
            return null;

        negative.RemoveAt(index < 0 ? negative.Count - 1 : index);
        var references = new HashSet<int> { 0 };
        foreach (var (delta, _) in negative)
        {
            references.Add(poc + delta);
        }

        for (var i = 0; i < set.NumPositivePics; i++)
        {
            references.Add(poc + set.DeltaPocS1[i]);
        }

        // Every picture that the picture keeps is a picture that the pictures before it kept
        if (header.FirstSliceSegmentInPicFlag)
        {
            if (!references.IsSubsetOf(kept))
                return null;

            kept.IntersectWith(references);
            kept.Add(poc);
        }

        var bits = new List<bool>(rbsp.Length * 8);
        CopyBits(rbsp, 0, setStart, bits);

        // short_term_ref_pic_set_sps_flag, then st_ref_pic_set( num_short_term_ref_pic_sets ) without prediction
        bits.Add(false);
        bits.Add(false);
        AddUE(bits, (uint)negative.Count);
        AddUE(bits, (uint)set.NumPositivePics);
        var previousDelta = 0;
        foreach (var (delta, used) in negative)
        {
            AddUE(bits, (uint)(previousDelta - delta - 1));
            bits.Add(used);
            previousDelta = delta;
        }

        previousDelta = 0;
        for (var i = 0; i < set.NumPositivePics; i++)
        {
            AddUE(bits, (uint)(set.DeltaPocS1[i] - previousDelta - 1));
            bits.Add(set.UsedByCurrPicS1[i]);
            previousDelta = set.DeltaPocS1[i];
        }

        // The long-term reference picture: the picture whose picture order count is 0
        if (options.HasFlag(Options.SpsCandidate))
        {
            // num_long_term_sps, without lt_idx_sps since the sequence parameter set has one candidate; num_long_term_pics
            AddUE(bits, 1);
            AddUE(bits, 0);
        }
        else
        {
            // num_long_term_pics, poc_lsb_lt, used_by_curr_pic_lt_flag
            AddUE(bits, 1);
            AddBits(bits, 0, sps.Log2MaxPicOrderCntLsb);
            bits.Add(true);
        }

        var msbPresent = options.HasFlag(Options.MsbPresent) && (poc & 1) is 1;
        bits.Add(msbPresent);
        if (msbPresent)
        {
            // delta_poc_msb_cycle_lt
            AddUE(bits, (uint)(poc >> sps.Log2MaxPicOrderCntLsb));
        }

        CopyBits(rbsp, setEnd, modificationPosition, bits);
        var numPicTotalCurr = negative.Count(entry => entry.Used) + set.UsedByCurrPicS1.Count(true) + 1;
        if (isInter && options.HasFlag(Options.ModifyLists) && numPicTotalCurr > 1)
        {
            // ref_pic_lists_modification( ): the lists of the pictures whose picture order count is odd start with their
            // second picture, and the last list entry of a list of several entries is its first picture again
            var entryLength = HevcSliceSegmentHeader.CeilLog2(numPicTotalCurr);
            for (var list = 0; list < (header.SliceType is HevcConstants.SliceB ? 2 : 1); list++)
            {
                var modified = (poc & 1) is 1;
                bits.Add(modified);
                if (!modified)
                    continue;

                var count = (list is 0 ? header.NumRefIdxL0ActiveMinus1 : header.NumRefIdxL1ActiveMinus1) + 1;
                for (var i = 0; i < count; i++)
                {
                    AddBits(bits, (uint)((i + 1 + list) % numPicTotalCurr), entryLength);
                }
            }
        }

        CopyBits(rbsp, modificationPosition, headerEnd, bits);

        // byte_alignment( ), then the slice segment data
        bits.Add(true);
        while (bits.Count % 8 is not 0)
        {
            bits.Add(false);
        }

        var result = new byte[(bits.Count / 8) + rbsp.Length - header.DataOffset];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                result[i >> 3] |= (byte)(0x80 >> (i & 7));
            }
        }

        rbsp.AsSpan(header.DataOffset).CopyTo(result.AsSpan(bits.Count / 8));
        return result;
    }

    // long_term_ref_pics_present_flag becomes 1 and is followed by num_long_term_ref_pics_sps and by its candidates: the
    // flag is the bit whose change gives a sequence parameter set that only differs by its long-term reference pictures
    private static byte[]? RewriteSps(byte[] rbsp, HevcSequenceParameterSet sps, bool withCandidate)
    {
        var insertion = new List<bool>();
        if (withCandidate)
        {
            // num_long_term_ref_pics_sps equal to 1, lt_ref_pic_poc_lsb_sps[ 0 ] equal to 0, used_by_curr_pic_lt_sps_flag[ 0 ]
            AddUE(insertion, 1);
            AddBits(insertion, 0, sps.Log2MaxPicOrderCntLsb);
            insertion.Add(true);
        }
        else
        {
            AddUE(insertion, 0);
        }

        var end = FindStopBit(rbsp);
        byte[]? found = null;
        for (var position = 0; position < end; position++)
        {
            if (GetBit(rbsp, position))
                continue;

            var bits = new List<bool>(rbsp.Length * 8);
            CopyBits(rbsp, 0, position, bits);
            bits.Add(true);
            bits.AddRange(insertion);
            CopyBits(rbsp, position + 1, end, bits);
            var candidate = ToRbsp(bits);
            HevcSequenceParameterSet parsed;
            try
            {
                parsed = HevcSequenceParameterSet.ParseRbsp(candidate);
            }
            catch (ImageException)
            {
                continue;
            }

            if (!parsed.LongTermRefPicsPresentFlag || parsed.LtRefPicPocLsbSps.Length != (withCandidate ? 1 : 0) || !IsSame(sps, parsed))
                continue;

            if (found is not null)
                return null;

            found = candidate;
        }

        return found;
    }

    private static bool IsSame(HevcSequenceParameterSet a, HevcSequenceParameterSet b)
    {
        return a.PicWidthInLumaSamples == b.PicWidthInLumaSamples && a.PicHeightInLumaSamples == b.PicHeightInLumaSamples && a.ChromaFormatIdc == b.ChromaFormatIdc && a.BitDepth == b.BitDepth
            && a.OutputWidth == b.OutputWidth && a.OutputHeight == b.OutputHeight && a.Log2MaxPicOrderCntLsb == b.Log2MaxPicOrderCntLsb && a.CtbLog2SizeY == b.CtbLog2SizeY
            && a.MinCbLog2SizeY == b.MinCbLog2SizeY && a.MinTbLog2SizeY == b.MinTbLog2SizeY && a.MaxTbLog2SizeY == b.MaxTbLog2SizeY
            && a.MaxTransformHierarchyDepthInter == b.MaxTransformHierarchyDepthInter && a.MaxTransformHierarchyDepthIntra == b.MaxTransformHierarchyDepthIntra
            && a.ScalingListEnabledFlag == b.ScalingListEnabledFlag && a.AmpEnabledFlag == b.AmpEnabledFlag && a.SampleAdaptiveOffsetEnabledFlag == b.SampleAdaptiveOffsetEnabledFlag
            && a.PcmEnabledFlag == b.PcmEnabledFlag && a.ShortTermRefPicSets.Length == b.ShortTermRefPicSets.Length && a.SpsTemporalMvpEnabledFlag == b.SpsTemporalMvpEnabledFlag
            && a.StrongIntraSmoothingEnabledFlag == b.StrongIntraSmoothingEnabledFlag && (a.Vui is null) == (b.Vui is null)
            && a.TransformSkipRotationEnabledFlag == b.TransformSkipRotationEnabledFlag && a.TransformSkipContextEnabledFlag == b.TransformSkipContextEnabledFlag
            && a.ImplicitRdpcmEnabledFlag == b.ImplicitRdpcmEnabledFlag && a.ExplicitRdpcmEnabledFlag == b.ExplicitRdpcmEnabledFlag && a.IntraSmoothingDisabledFlag == b.IntraSmoothingDisabledFlag
            && a.HighPrecisionOffsetsEnabledFlag == b.HighPrecisionOffsetsEnabledFlag && a.PersistentRiceAdaptationEnabledFlag == b.PersistentRiceAdaptationEnabledFlag
            && a.MaxDecPicBufferingMinus1.SequenceEqual(b.MaxDecPicBufferingMinus1) && a.MaxNumReorderPics.SequenceEqual(b.MaxNumReorderPics);
    }

    // lists_modification_present_flag becomes 1: the bit whose change gives a picture parameter set with the flag
    private static byte[]? RewritePps(byte[] rbsp)
    {
        var original = HevcPictureParameterSet.ParseRbsp(rbsp);
        var end = FindStopBit(rbsp);
        byte[]? found = null;
        for (var position = 0; position < end; position++)
        {
            if (GetBit(rbsp, position))
                continue;

            var candidate = (byte[])rbsp.Clone();
            candidate[position >> 3] |= (byte)(0x80 >> (position & 7));
            HevcPictureParameterSet parsed;
            try
            {
                parsed = HevcPictureParameterSet.ParseRbsp(candidate);
            }
            catch (ImageException)
            {
                continue;
            }

            if (!parsed.ListsModificationPresentFlag || parsed.Log2ParMrgLevel != original.Log2ParMrgLevel || parsed.WeightedPredFlag != original.WeightedPredFlag
                || parsed.WeightedBipredFlag != original.WeightedBipredFlag || parsed.CabacInitPresentFlag != original.CabacInitPresentFlag
                || parsed.NumRefIdxL0DefaultActiveMinus1 != original.NumRefIdxL0DefaultActiveMinus1 || parsed.NumExtraSliceHeaderBits != original.NumExtraSliceHeaderBits
                || parsed.DependentSliceSegmentsEnabledFlag != original.DependentSliceSegmentsEnabledFlag || parsed.OutputFlagPresentFlag != original.OutputFlagPresentFlag)
            {
                continue;
            }

            if (found is not null)
                return null;

            found = candidate;
        }

        return found;
    }

    private static bool GetBit(ReadOnlySpan<byte> data, int position) => (data[position >> 3] & (0x80 >> (position & 7))) is not 0;

    // The position of rbsp_stop_one_bit
    private static int FindStopBit(ReadOnlySpan<byte> rbsp)
    {
        var position = (rbsp.Length * 8) - 1;
        while (!GetBit(rbsp, position))
        {
            position--;
        }

        return position;
    }

    private static void CopyBits(ReadOnlySpan<byte> data, int start, int end, List<bool> bits)
    {
        for (var i = start; i < end; i++)
        {
            bits.Add(GetBit(data, i));
        }
    }

    private static void AddBits(List<bool> bits, uint value, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            bits.Add(((value >> i) & 1) is not 0);
        }
    }

    private static void AddUE(List<bool> bits, uint codeNum)
    {
        var value = codeNum + 1;
        var length = 32 - (int)uint.LeadingZeroCount(value);
        AddBits(bits, 0, length - 1);
        AddBits(bits, value, length);
    }

    // The bits, then rbsp_trailing_bits( )
    private static byte[] ToRbsp(List<bool> bits)
    {
        var result = new byte[(bits.Count / 8) + 1];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                result[i >> 3] |= (byte)(0x80 >> (i & 7));
            }
        }

        result[bits.Count >> 3] |= (byte)(0x80 >> (bits.Count & 7));
        return result;
    }

    // The header of a NAL unit, then a payload with its emulation prevention bytes (clause 7.4.2)
    private static byte[] ToNalUnit(ReadOnlySpan<byte> original, ReadOnlySpan<byte> rbsp)
    {
        var result = new List<byte>(rbsp.Length + 8) { original[0], original[1] };
        var zeros = 0;
        foreach (var value in rbsp)
        {
            if (zeros >= 2 && value <= 3)
            {
                result.Add(3);
                zeros = 0;
            }

            result.Add(value);
            zeros = value is 0 ? zeros + 1 : 0;
        }

        return [.. result];
    }
}
