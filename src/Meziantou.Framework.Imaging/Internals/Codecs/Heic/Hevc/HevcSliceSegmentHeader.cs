namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The header of a slice segment (Rec. ITU-T H.265, clauses 7.3.6 "Slice segment header syntax" and 7.4.7 "Slice segment
/// header semantics"). The names are those of the specification. The header of a dependent slice segment has the values
/// of the slice header of the slice it belongs to for the syntax elements it does not carry.
/// </summary>
internal sealed class HevcSliceSegmentHeader
{
    private int[] _pocLsbLt = [];
    private bool[] _usedByCurrPicLt = [];
    private bool[] _deltaPocMsbPresentFlag = [];
    private int[] _deltaPocMsbCycleLt = [];
    private int[] _listEntryL0 = [];
    private int[] _listEntryL1 = [];
    private int[] _entryPointOffsetMinus1 = [];

    private HevcSliceSegmentHeader()
    {
    }

    public bool FirstSliceSegmentInPicFlag { get; private set; }

    public bool NoOutputOfPriorPicsFlag { get; private set; }

    public int SlicePicParameterSetId { get; private set; }

    public bool DependentSliceSegmentFlag { get; private set; }

    public int SliceSegmentAddress { get; private set; }

    /// <summary>Gets <c>SliceAddrRs</c>: the address of the first coding tree block of the slice, in raster scan.</summary>
    public int SliceAddrRs { get; private set; }

    /// <summary>Gets <c>slice_type</c> (Table 7-7): 0 for B, 1 for P, 2 for I.</summary>
    public int SliceType { get; private set; }

    public bool PicOutputFlag { get; private set; } = true;

    public int SlicePicOrderCntLsb { get; private set; }

    public bool ShortTermRefPicSetSpsFlag { get; private set; }

    public int ShortTermRefPicSetIdx { get; private set; }

    /// <summary>Gets the short-term reference picture set of the picture, or <see langword="null"/> for an IDR picture, which has none.</summary>
    public HevcShortTermRefPicSet? ShortTermRefPicSet { get; private set; }

    /// <summary>Gets <c>num_long_term_sps + num_long_term_pics</c>.</summary>
    public int NumLongTermRefPics => _pocLsbLt.Length;

    /// <summary>Gets <c>PocLsbLt[ i ]</c>.</summary>
    public ReadOnlySpan<int> PocLsbLt => _pocLsbLt;

    /// <summary>Gets <c>UsedByCurrPicLt[ i ]</c>.</summary>
    public ReadOnlySpan<bool> UsedByCurrPicLt => _usedByCurrPicLt;

    /// <summary>Gets <c>delta_poc_msb_present_flag[ i ]</c>.</summary>
    public ReadOnlySpan<bool> DeltaPocMsbPresentFlag => _deltaPocMsbPresentFlag;

    /// <summary>Gets <c>DeltaPocMsbCycleLt[ i ]</c> (Equation 7-52).</summary>
    public ReadOnlySpan<int> DeltaPocMsbCycleLt => _deltaPocMsbCycleLt;

    public bool SliceTemporalMvpEnabledFlag { get; private set; }

    public bool SliceSaoLumaFlag { get; private set; }

    public bool SliceSaoChromaFlag { get; private set; }

    public int NumRefIdxL0ActiveMinus1 { get; private set; }

    public int NumRefIdxL1ActiveMinus1 { get; private set; }

    /// <summary>Gets <c>NumPicTotalCurr</c> (Equation 7-55): the number of pictures that the picture may refer to.</summary>
    public int NumPicTotalCurr { get; private set; }

    public bool RefPicListModificationFlagL0 { get; private set; }

    public bool RefPicListModificationFlagL1 { get; private set; }

    /// <summary>Gets <c>list_entry_l0[ i ]</c>.</summary>
    public ReadOnlySpan<int> ListEntryL0 => _listEntryL0;

    /// <summary>Gets <c>list_entry_l1[ i ]</c>.</summary>
    public ReadOnlySpan<int> ListEntryL1 => _listEntryL1;

    public bool MvdL1ZeroFlag { get; private set; }

    public bool CabacInitFlag { get; private set; }

    public bool CollocatedFromL0Flag { get; private set; } = true;

    public int CollocatedRefIdx { get; private set; }

    /// <summary>Gets the weighted prediction parameters, or <see langword="null"/> when the slice has none.</summary>
    public HevcPredWeightTable? PredWeightTable { get; private set; }

    /// <summary>Gets <c>MaxNumMergeCand</c>: <c>5 - five_minus_max_num_merge_cand</c>.</summary>
    public int MaxNumMergeCand { get; private set; }

    public int SliceQpDelta { get; private set; }

    /// <summary>Gets <c>SliceQpY</c> (Equation 7-54).</summary>
    public int SliceQpY { get; private set; }

    public int SliceCbQpOffset { get; private set; }

    public int SliceCrQpOffset { get; private set; }

    public bool CuChromaQpOffsetEnabledFlag { get; private set; }

    public bool DeblockingFilterOverrideFlag { get; private set; }

    public bool SliceDeblockingFilterDisabledFlag { get; private set; }

    public int SliceBetaOffsetDiv2 { get; private set; }

    public int SliceTcOffsetDiv2 { get; private set; }

    public bool SliceLoopFilterAcrossSlicesEnabledFlag { get; private set; }

    /// <summary>Gets <c>entry_point_offset_minus1[ i ]</c>: <c>num_entry_point_offsets</c> values.</summary>
    public ReadOnlySpan<int> EntryPointOffsetMinus1 => _entryPointOffsetMinus1;

    /// <summary>Gets the position of the slice segment data in the payload of the NAL unit, in bytes.</summary>
    public int DataOffset { get; private set; }

    /// <summary>Gets <c>initType</c> of the context variables of the slice (Equation 9-7).</summary>
    public int InitType => SliceType switch
    {
        HevcConstants.SliceI => 0,
        HevcConstants.SliceP => CabacInitFlag ? 2 : 1,
        _ => CabacInitFlag ? 1 : 2,
    };

    /// <summary>
    /// Reads the first syntax elements of a slice segment header, up to <c>slice_pic_parameter_set_id</c>, which tells the
    /// parameter sets that the rest of the header is read with.
    /// </summary>
    public static int ReadPicParameterSetId(ReadOnlySpan<byte> rbsp, in HevcNalUnitHeader nal, out bool firstSliceSegmentInPicFlag)
    {
        var reader = new HevcBitReader(rbsp);
        firstSliceSegmentInPicFlag = reader.ReadFlag();
        if (nal.IsIrap)
        {
            reader.Skip(1);
        }

        return reader.ReadUE(HevcConstants.MaxPpsCount - 1, "slice_pic_parameter_set_id");
    }

    /// <summary>Reads the syntax structure <c>slice_segment_header( )</c> from the payload of a NAL unit.</summary>
    /// <param name="rbsp">The payload of the NAL unit, without emulation prevention bytes.</param>
    /// <param name="nal">The header of the NAL unit.</param>
    /// <param name="layout">The parameter sets that <c>slice_pic_parameter_set_id</c> refers to.</param>
    /// <param name="sliceHeader">The slice header of the slice that a dependent slice segment belongs to: the header of the previous slice segment of the picture; or <see langword="null"/> when the picture has none yet.</param>
    /// <exception cref="InvalidImageContentException">The header is malformed or truncated.</exception>
    public static HevcSliceSegmentHeader Parse(ReadOnlySpan<byte> rbsp, in HevcNalUnitHeader nal, HevcPictureLayout layout, HevcSliceSegmentHeader? sliceHeader)
    {
        var sps = layout.Sps;
        var pps = layout.Pps;
        var reader = new HevcBitReader(rbsp);
        var firstSliceSegmentInPicFlag = reader.ReadFlag();
        var noOutputOfPriorPicsFlag = nal.IsIrap && reader.ReadFlag();
        var slicePicParameterSetId = reader.ReadUE(HevcConstants.MaxPpsCount - 1, "slice_pic_parameter_set_id");
        var dependentSliceSegmentFlag = false;
        var sliceSegmentAddress = 0;
        if (!firstSliceSegmentInPicFlag)
        {
            dependentSliceSegmentFlag = pps.DependentSliceSegmentsEnabledFlag && reader.ReadFlag();
            sliceSegmentAddress = (int)reader.ReadBits(CeilLog2(sps.PicSizeInCtbsY));
            if (sliceSegmentAddress >= sps.PicSizeInCtbsY)
                throw HevcErrors.Invalid("slice_segment_address is out of range.");
        }

        HevcSliceSegmentHeader header;
        if (dependentSliceSegmentFlag)
        {
            if (sliceHeader is null)
                throw HevcErrors.Invalid("a dependent slice segment has no slice.");

            header = (HevcSliceSegmentHeader)sliceHeader.MemberwiseClone();
            header._entryPointOffsetMinus1 = [];
        }
        else
        {
            header = new HevcSliceSegmentHeader { SliceAddrRs = sliceSegmentAddress };
        }

        header.FirstSliceSegmentInPicFlag = firstSliceSegmentInPicFlag;
        header.NoOutputOfPriorPicsFlag = noOutputOfPriorPicsFlag;
        header.SlicePicParameterSetId = slicePicParameterSetId;
        header.DependentSliceSegmentFlag = dependentSliceSegmentFlag;
        header.SliceSegmentAddress = sliceSegmentAddress;
        if (!dependentSliceSegmentFlag)
        {
            header.ParseSliceHeader(ref reader, nal, sps, pps);
        }

        if (pps.TilesEnabledFlag || pps.EntropyCodingSyncEnabledFlag)
        {
            // Clause 7.4.7.1: the largest number of entry points is the number of substreams of a picture, minus one
            var maximum = !pps.TilesEnabledFlag
                ? sps.PicHeightInCtbsY - 1
                : !pps.EntropyCodingSyncEnabledFlag
                    ? (pps.NumTileColumns * pps.NumTileRows) - 1
                    : (pps.NumTileColumns * sps.PicHeightInCtbsY) - 1;
            var numEntryPointOffsets = reader.ReadUE(maximum, "num_entry_point_offsets");
            if (numEntryPointOffsets > 0)
            {
                var offsetLenMinus1 = reader.ReadUE(31, "offset_len_minus1");

                // Each entry point is at least one byte of the slice segment data
                if (numEntryPointOffsets > rbsp.Length)
                    throw HevcErrors.Truncated();

                header._entryPointOffsetMinus1 = new int[numEntryPointOffsets];
                for (var i = 0; i < numEntryPointOffsets; i++)
                {
                    var entryPointOffsetMinus1 = reader.ReadBits(offsetLenMinus1 + 1);

                    // An offset is a number of bytes of the NAL unit
                    if (entryPointOffsetMinus1 >= int.MaxValue / 2)
                        throw HevcErrors.Truncated();

                    header._entryPointOffsetMinus1[i] = (int)entryPointOffsetMinus1;
                }
            }
        }

        if (pps.SliceSegmentHeaderExtensionPresentFlag)
        {
            var sliceSegmentHeaderExtensionLength = reader.ReadUE(256, "slice_segment_header_extension_length");

            // slice_segment_header_extension_data_byte
            reader.Skip(8L * sliceSegmentHeaderExtensionLength);
        }

        reader.ReadByteAlignment();
        header.DataOffset = reader.BytePosition;
        return header;
    }

    private void ParseSliceHeader(ref HevcBitReader reader, in HevcNalUnitHeader nal, HevcSequenceParameterSet sps, HevcPictureParameterSet pps)
    {
        // slice_reserved_flag[ i ]
        reader.Skip(pps.NumExtraSliceHeaderBits);
        SliceType = reader.ReadUE(2, "slice_type");
        if (nal.IsIrap && SliceType is not HevcConstants.SliceI)
            throw HevcErrors.Invalid("a slice of an intra random access point picture is not an I slice.");

        if (pps.OutputFlagPresentFlag)
        {
            PicOutputFlag = reader.ReadFlag();
        }

        if (!nal.IsIdr)
        {
            SlicePicOrderCntLsb = (int)reader.ReadBits(sps.Log2MaxPicOrderCntLsb);
            ShortTermRefPicSetSpsFlag = reader.ReadFlag();
            var numShortTermRefPicSets = sps.ShortTermRefPicSets.Length;
            var maxDecPicBufferingMinus1 = sps.MaxDecPicBufferingMinus1[sps.HighestTid];
            if (!ShortTermRefPicSetSpsFlag)
            {
                ShortTermRefPicSet = HevcShortTermRefPicSet.Parse(ref reader, numShortTermRefPicSets, numShortTermRefPicSets, sps.ShortTermRefPicSets, maxDecPicBufferingMinus1);
            }
            else
            {
                if (numShortTermRefPicSets is 0)
                    throw HevcErrors.Invalid("a slice refers to a short-term reference picture set of a sequence that has none.");

                ShortTermRefPicSetIdx = (int)reader.ReadBits(CeilLog2(numShortTermRefPicSets));
                if (ShortTermRefPicSetIdx >= numShortTermRefPicSets)
                    throw HevcErrors.Invalid("short_term_ref_pic_set_idx is out of range.");

                ShortTermRefPicSet = sps.ShortTermRefPicSets[ShortTermRefPicSetIdx];
            }

            NumPicTotalCurr = ShortTermRefPicSet.NumUsedByCurrPic;
            if (sps.LongTermRefPicsPresentFlag)
            {
                var numLongTermRefPicsSps = sps.LtRefPicPocLsbSps.Length;
                var numLongTermSps = numLongTermRefPicsSps > 0 ? reader.ReadUE(numLongTermRefPicsSps, "num_long_term_sps") : 0;
                var maximum = maxDecPicBufferingMinus1 - ShortTermRefPicSet.NumDeltaPocs - numLongTermSps;
                if (maximum < 0)
                    throw HevcErrors.Invalid("a reference picture set has more pictures than the decoded picture buffer.");

                var numLongTermPics = reader.ReadUE(maximum, "num_long_term_pics");
                var count = numLongTermSps + numLongTermPics;
                _pocLsbLt = new int[count];
                _usedByCurrPicLt = new bool[count];
                _deltaPocMsbPresentFlag = new bool[count];
                _deltaPocMsbCycleLt = new int[count];
                for (var i = 0; i < count; i++)
                {
                    if (i < numLongTermSps)
                    {
                        var ltIdxSps = numLongTermRefPicsSps > 1 ? (int)reader.ReadBits(CeilLog2(numLongTermRefPicsSps)) : 0;
                        if (ltIdxSps >= numLongTermRefPicsSps)
                            throw HevcErrors.Invalid("lt_idx_sps is out of range.");

                        _pocLsbLt[i] = sps.LtRefPicPocLsbSps[ltIdxSps];
                        _usedByCurrPicLt[i] = sps.UsedByCurrPicLtSpsFlag[ltIdxSps];
                    }
                    else
                    {
                        _pocLsbLt[i] = (int)reader.ReadBits(sps.Log2MaxPicOrderCntLsb);
                        _usedByCurrPicLt[i] = reader.ReadFlag();
                    }

                    _deltaPocMsbPresentFlag[i] = reader.ReadFlag();
                    var deltaPocMsbCycleLt = _deltaPocMsbPresentFlag[i] ? reader.ReadUE() : 0;

                    // Equation 7-52: the cycles add up within the pictures of the sequence parameter set, and within the others
                    var total = i is 0 || i == numLongTermSps ? deltaPocMsbCycleLt : deltaPocMsbCycleLt + (long)_deltaPocMsbCycleLt[i - 1];
                    if (total > int.MaxValue >> sps.Log2MaxPicOrderCntLsb)
                        throw HevcErrors.Invalid("delta_poc_msb_cycle_lt is out of range.");

                    _deltaPocMsbCycleLt[i] = (int)total;
                    if (_usedByCurrPicLt[i])
                    {
                        NumPicTotalCurr++;
                    }
                }
            }

            if (sps.SpsTemporalMvpEnabledFlag)
            {
                SliceTemporalMvpEnabledFlag = reader.ReadFlag();
            }
        }

        if (sps.SampleAdaptiveOffsetEnabledFlag)
        {
            SliceSaoLumaFlag = reader.ReadFlag();
            if (sps.ChromaArrayType is not 0)
            {
                SliceSaoChromaFlag = reader.ReadFlag();
            }
        }

        if (SliceType is HevcConstants.SliceP or HevcConstants.SliceB)
        {
            NumRefIdxL0ActiveMinus1 = pps.NumRefIdxL0DefaultActiveMinus1;
            NumRefIdxL1ActiveMinus1 = SliceType is HevcConstants.SliceB ? pps.NumRefIdxL1DefaultActiveMinus1 : 0;
            if (reader.ReadFlag())
            {
                NumRefIdxL0ActiveMinus1 = reader.ReadUE(14, "num_ref_idx_l0_active_minus1");
                if (SliceType is HevcConstants.SliceB)
                {
                    NumRefIdxL1ActiveMinus1 = reader.ReadUE(14, "num_ref_idx_l1_active_minus1");
                }
            }

            // Clause 7.4.7.1: a P or B slice has at least one picture to refer to
            if (NumPicTotalCurr is 0)
                throw HevcErrors.Invalid("a P or B slice has no reference picture.");

            if (pps.ListsModificationPresentFlag && NumPicTotalCurr > 1)
            {
                // Clause 7.3.6.2 "Reference picture list modification syntax"
                var entryLength = CeilLog2(NumPicTotalCurr);
                RefPicListModificationFlagL0 = reader.ReadFlag();
                if (RefPicListModificationFlagL0)
                {
                    _listEntryL0 = ReadListEntries(ref reader, NumRefIdxL0ActiveMinus1 + 1, entryLength, NumPicTotalCurr);
                }

                if (SliceType is HevcConstants.SliceB)
                {
                    RefPicListModificationFlagL1 = reader.ReadFlag();
                    if (RefPicListModificationFlagL1)
                    {
                        _listEntryL1 = ReadListEntries(ref reader, NumRefIdxL1ActiveMinus1 + 1, entryLength, NumPicTotalCurr);
                    }
                }
            }

            if (SliceType is HevcConstants.SliceB)
            {
                MvdL1ZeroFlag = reader.ReadFlag();
            }

            if (pps.CabacInitPresentFlag)
            {
                CabacInitFlag = reader.ReadFlag();
            }

            if (SliceTemporalMvpEnabledFlag)
            {
                if (SliceType is HevcConstants.SliceB)
                {
                    CollocatedFromL0Flag = reader.ReadFlag();
                }

                var numRefIdxActiveMinus1 = CollocatedFromL0Flag ? NumRefIdxL0ActiveMinus1 : NumRefIdxL1ActiveMinus1;
                if (numRefIdxActiveMinus1 > 0)
                {
                    CollocatedRefIdx = reader.ReadUE(numRefIdxActiveMinus1, "collocated_ref_idx");
                }
            }

            if ((pps.WeightedPredFlag && SliceType is HevcConstants.SliceP) || (pps.WeightedBipredFlag && SliceType is HevcConstants.SliceB))
            {
                PredWeightTable = HevcPredWeightTable.Parse(ref reader, sps, SliceType, NumRefIdxL0ActiveMinus1, NumRefIdxL1ActiveMinus1);
            }

            MaxNumMergeCand = 5 - reader.ReadUE(4, "five_minus_max_num_merge_cand");
        }

        SliceQpDelta = reader.ReadSE();
        var sliceQpY = 26L + pps.InitQpMinus26 + SliceQpDelta;
        if (sliceQpY < -sps.QpBdOffset || sliceQpY > 51)
            throw HevcErrors.Invalid("the quantization parameter of a slice is out of range.");

        SliceQpY = (int)sliceQpY;
        if (pps.SliceChromaQpOffsetsPresentFlag)
        {
            SliceCbQpOffset = reader.ReadSE(-12, 12, "slice_cb_qp_offset");
            SliceCrQpOffset = reader.ReadSE(-12, 12, "slice_cr_qp_offset");
            if (Math.Abs(pps.CbQpOffset + SliceCbQpOffset) > 12 || Math.Abs(pps.CrQpOffset + SliceCrQpOffset) > 12)
                throw HevcErrors.Invalid("the chroma quantization parameter offset of a slice is out of range.");
        }

        if (pps.ChromaQpOffsetListEnabledFlag)
        {
            CuChromaQpOffsetEnabledFlag = reader.ReadFlag();
        }

        if (pps.DeblockingFilterOverrideEnabledFlag)
        {
            DeblockingFilterOverrideFlag = reader.ReadFlag();
        }

        SliceDeblockingFilterDisabledFlag = pps.DeblockingFilterDisabledFlag;
        SliceBetaOffsetDiv2 = pps.BetaOffsetDiv2;
        SliceTcOffsetDiv2 = pps.TcOffsetDiv2;
        if (DeblockingFilterOverrideFlag)
        {
            SliceDeblockingFilterDisabledFlag = reader.ReadFlag();
            if (!SliceDeblockingFilterDisabledFlag)
            {
                SliceBetaOffsetDiv2 = reader.ReadSE(-6, 6, "slice_beta_offset_div2");
                SliceTcOffsetDiv2 = reader.ReadSE(-6, 6, "slice_tc_offset_div2");
            }
        }

        SliceLoopFilterAcrossSlicesEnabledFlag = pps.LoopFilterAcrossSlicesEnabledFlag;
        if (pps.LoopFilterAcrossSlicesEnabledFlag && (SliceSaoLumaFlag || SliceSaoChromaFlag || !SliceDeblockingFilterDisabledFlag))
        {
            SliceLoopFilterAcrossSlicesEnabledFlag = reader.ReadFlag();
        }
    }

    private static int[] ReadListEntries(ref HevcBitReader reader, int count, int entryLength, int numPicTotalCurr)
    {
        var entries = new int[count];
        for (var i = 0; i < count; i++)
        {
            entries[i] = (int)reader.ReadBits(entryLength);
            if (entries[i] >= numPicTotalCurr)
                throw HevcErrors.Invalid("an entry of a reference picture list modification is out of range.");
        }

        return entries;
    }

    /// <summary>Gets <c>Ceil( Log2( value ) )</c> for a positive value.</summary>
    public static int CeilLog2(int value) => value <= 1 ? 0 : 32 - int.LeadingZeroCount(value - 1);
}
