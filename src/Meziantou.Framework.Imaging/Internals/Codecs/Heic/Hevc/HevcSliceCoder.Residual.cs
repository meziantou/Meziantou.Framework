using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

// The transform coefficient levels of a transform block
internal ref partial struct HevcSliceCoder
{
    // sigCtx of sig_coeff_flag (GetSigCtx) for each scan position n of a sub-block, 16 values per block size, colour
    // component, scan order, sub-block (the first one or another one) and prevCsbf: see GetSigCtxIndex
    private static readonly byte[] SigCtxTable = CreateSigCtxTable();

    // sigCtx of every scan position of the blocks that use the context of transform_skip_context_enabled_flag, which is
    // in the ctxIdx of the decoding
    private static ReadOnlySpan<byte> SingleSigCtx => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>
    /// Reads the transform coefficient levels of a transform block (clause 7.3.8.11 "Residual coding syntax" and clause
    /// 7.4.9.11 "Residual coding semantics") of a coding unit coded in intra prediction mode.
    /// </summary>
    /// <param name="log2TrafoSize">The base 2 logarithm of the size of the block.</param>
    /// <param name="cIdx">The colour component of the block.</param>
    /// <param name="predModeIntra">The intra prediction mode of the block.</param>
    /// <param name="coefficients">
    /// The rows of the block: <c>TransCoeffLevel</c> for a coding unit that bypasses the scaling and the transform, otherwise
    /// the scaled transform coefficients <c>d</c> (clause 8.6.3), which the scaling process gives for each level as it is
    /// read.
    /// </param>
    /// <param name="rows">The number of rows of the block that are written: the others are not cleared.</param>
    /// <param name="columns">The number of columns of the block that are written in those rows: a multiple of 4.</param>
    /// <returns><c>transform_skip_flag</c>.</returns>
    private bool ResidualCoding(int log2TrafoSize, int cIdx, int predModeIntra, Span<int> coefficients, out int rows, out int columns)
    {
        var nTbS = 1 << log2TrafoSize;
        var transformSkipFlag = false;
        if (_pps.TransformSkipEnabledFlag && !_cuTransquantBypassFlag && log2TrafoSize <= _pps.Log2MaxTransformSkipSize)
        {
            transformSkipFlag = _cabac.DecodeDecision(HevcContextLayout.TransformSkipFlag + (cIdx is 0 ? 0 : 1)) is not 0;
            if (transformSkipFlag)
            {
                _tools |= HevcTools.TransformSkip | (log2TrafoSize > 2 ? HevcTools.LargeTransformSkip : HevcTools.None);
            }
        }

        // explicit_rdpcm_flag and explicit_rdpcm_dir_flag are those of a coding unit coded in inter prediction mode
        _explicitRdpcmFlag = false;
        _explicitRdpcmDirFlag = false;
        if (!_isIntra && _sps.ExplicitRdpcmEnabledFlag && (transformSkipFlag || _cuTransquantBypassFlag))
        {
            _explicitRdpcmFlag = _cabac.DecodeDecision(HevcContextLayout.ExplicitRdpcmFlag + (cIdx is 0 ? 0 : 1)) is not 0;
            if (_explicitRdpcmFlag)
            {
                _explicitRdpcmDirFlag = _cabac.DecodeDecision(HevcContextLayout.ExplicitRdpcmDirFlag + (cIdx is 0 ? 0 : 1)) is not 0;
                _tools |= HevcTools.ExplicitRdpcm;
            }
        }

        // last_sig_coeff_x_prefix, last_sig_coeff_y_prefix (clause 9.3.4.2.3), then the suffixes
        GetLastSigCoeffPrefixContext(log2TrafoSize, cIdx, out var ctxOffset, out var ctxShift);
        var cMax = (log2TrafoSize << 1) - 1;
        var lastSigCoeffXPrefix = 0;
        while (lastSigCoeffXPrefix < cMax && _cabac.DecodeDecision(HevcContextLayout.LastSigCoeffXPrefix + (lastSigCoeffXPrefix >> ctxShift) + ctxOffset) is not 0)
        {
            lastSigCoeffXPrefix++;
        }

        var lastSigCoeffYPrefix = 0;
        while (lastSigCoeffYPrefix < cMax && _cabac.DecodeDecision(HevcContextLayout.LastSigCoeffYPrefix + (lastSigCoeffYPrefix >> ctxShift) + ctxOffset) is not 0)
        {
            lastSigCoeffYPrefix++;
        }

        // Equations 7-74 to 7-77
        var lastSignificantCoeffX = lastSigCoeffXPrefix;
        if (lastSigCoeffXPrefix > 3)
        {
            var length = (lastSigCoeffXPrefix >> 1) - 1;
            lastSignificantCoeffX = ((1 << length) * (2 + (lastSigCoeffXPrefix & 1))) + _cabac.DecodeBypassBits(length);
        }

        var lastSignificantCoeffY = lastSigCoeffYPrefix;
        if (lastSigCoeffYPrefix > 3)
        {
            var length = (lastSigCoeffYPrefix >> 1) - 1;
            lastSignificantCoeffY = ((1 << length) * (2 + (lastSigCoeffYPrefix & 1))) + _cabac.DecodeBypassBits(length);
        }

        // scanIdx: the scan order of a small block follows its intra prediction mode
        var scanIdx = GetScanIdx(log2TrafoSize, cIdx, predModeIntra);
        _tools |= scanIdx switch
        {
            HevcScanOrder.Vertical => HevcTools.VerticalScan,
            HevcScanOrder.Horizontal => HevcTools.HorizontalScan,
            _ => HevcTools.None,
        };

        if (scanIdx is HevcScanOrder.Vertical)
        {
            // Equation 7-78
            (lastSignificantCoeffX, lastSignificantCoeffY) = (lastSignificantCoeffY, lastSignificantCoeffX);
        }

        // The sub-block and the position in it of the last significant coefficient, which the syntax searches backwards
        var subBlockScan = HevcScanOrder.Get(log2TrafoSize - 2, scanIdx);
        var positionScan = HevcScanOrder.Get(2, scanIdx);
        var lastSubBlock = subBlockScan.IndexOf((byte)(((lastSignificantCoeffY >> 2) << 4) | (lastSignificantCoeffX >> 2)));
        var lastScanPos = positionScan.IndexOf((byte)(((lastSignificantCoeffY & 3) << 4) | (lastSignificantCoeffX & 3)));

        // The transform only reads the rows and the columns of the sub-blocks up to the last one: only they are cleared in
        // the blocks of 16x16 and 32x32 coefficients. The residual of a block without transform is the whole block
        if (transformSkipFlag || _cuTransquantBypassFlag || log2TrafoSize <= 3)
        {
            rows = nTbS;
            columns = nTbS;
            coefficients[..(nTbS * nTbS)].Clear();
        }
        else
        {
            var maxXS = 0;
            var maxYS = 0;
            foreach (var subBlock in subBlockScan[..(lastSubBlock + 1)])
            {
                maxXS = Math.Max(maxXS, HevcScanOrder.X(subBlock));
                maxYS = Math.Max(maxYS, HevcScanOrder.Y(subBlock));
            }

            rows = (maxYS + 1) << 2;
            columns = (maxXS + 1) << 2;
            for (var y = 0; y < rows; y++)
            {
                coefficients.Slice(y << log2TrafoSize, columns).Clear();
            }
        }

        // Clause 8.6.3 "Scaling process for transform coefficients", for each level as it is read: m[ x ][ y ] is 16 without
        // scaling list and for a block larger than 4x4 that skips the transform. Equation 8-308: matrixId is cIdx for a
        // coding unit coded in intra prediction mode, 3 + cIdx otherwise (Table 7-4)
        var scaled = !_cuTransquantBypassFlag;
        var bdShift = _bitDepth + log2TrafoSize + 10 - 15;
        var levelScale = 0L;
        ReadOnlySpan<byte> m = default;
        if (scaled)
        {
            var qP = cIdx is 0 ? _qpY + _sps.QpBdOffset : GetChromaQp(cIdx);
            levelScale = HevcQuantizationTables.LevelScale[qP % 6] << (qP / 6);
            var scalingFactors = _layout.ScalingFactors;
            if (scalingFactors.IsEmpty || (transformSkipFlag && nTbS > 4))
            {
                levelScale *= 16;
            }
            else
            {
                m = scalingFactors.Slice(HevcScalingList.GetScalingFactorOffset(log2TrafoSize - 2, _isIntra ? cIdx : 3 + cIdx), nTbS * nTbS);
            }
        }

        var rounding = 1L << (bdShift - 1);
        var subBlocksPerSide = 1 << (log2TrafoSize - 2);
        Span<byte> codedSubBlockFlag = stackalloc byte[64];
        Span<byte> significant = stackalloc byte[16];
        Span<byte> greater1Flag = stackalloc byte[16];
        var transformSkipContext = _sps.TransformSkipContextEnabledFlag && (transformSkipFlag || _cuTransquantBypassFlag);
        var implicitRdpcm = _sps.ImplicitRdpcmEnabledFlag && transformSkipFlag && predModeIntra is HevcConstants.IntraAngular10 or HevcConstants.IntraAngular26;
        var signHidingAllowed = _pps.SignDataHidingEnabledFlag && !_cuTransquantBypassFlag && !implicitRdpcm && !_explicitRdpcmFlag;
        var persistentRice = _sps.PersistentRiceAdaptationEnabledFlag;
        var sbType = (cIdx is 0 ? 2 : 0) + (transformSkipFlag || _cuTransquantBypassFlag ? 1 : 0);
        var greater1Ctx = 1;
        if (transformSkipContext)
        {
            _tools |= HevcTools.TransformSkipContext;
        }

        // ctxIdx of sig_coeff_flag: sigCtx, after the 27 contexts of the luma for the chroma. The blocks that use the context
        // of transform_skip_context_enabled_flag have a single sigCtx: 42 for the luma and 16 for the chroma
        var sigCoeffFlagContext = HevcContextLayout.SigCoeffFlag + (cIdx is 0 ? 0 : 27) + (transformSkipContext ? (cIdx is 0 ? 42 : 16) : 0);

        for (var i = lastSubBlock; i >= 0; i--)
        {
            var xS = HevcScanOrder.X(subBlockScan[i]);
            var yS = HevcScanOrder.Y(subBlockScan[i]);
            var rightCsbf = xS < subBlocksPerSide - 1 ? codedSubBlockFlag[(yS * 8) + xS + 1] : 0;
            var belowCsbf = yS < subBlocksPerSide - 1 ? codedSubBlockFlag[((yS + 1) * 8) + xS] : 0;
            var inferSbDcSigCoeffFlag = false;
            var csbf = true;
            if (i < lastSubBlock && i > 0)
            {
                // coded_sub_block_flag (clause 9.3.4.2.4)
                var ctxInc = Math.Min(rightCsbf + belowCsbf, 1) + (cIdx is 0 ? 0 : 2);
                csbf = _cabac.DecodeDecision(HevcContextLayout.CodedSubBlockFlag + ctxInc) is not 0;
                inferSbDcSigCoeffFlag = true;
            }

            if (!csbf)
                continue;

            codedSubBlockFlag[(yS * 8) + xS] = 1;

            // sig_coeff_flag (clause 9.3.4.2.5): the scan positions of the significant coefficients, the last one first
            var prevCsbf = rightCsbf + (belowCsbf << 1);
            var sigCtxs = transformSkipContext ? SingleSigCtx : SigCtxTable.AsSpan(GetSigCtxIndex(log2TrafoSize, cIdx, scanIdx, (xS | yS) is not 0, prevCsbf), 16);
            var numSig = 0;
            var n = 15;
            if (i == lastSubBlock)
            {
                significant[numSig++] = (byte)lastScanPos;
                n = lastScanPos - 1;
            }

            numSig = ReadSigCoeffFlags(_cabac, sigCoeffFlagContext, sigCtxs, n, inferSbDcSigCoeffFlag, significant, numSig);
            if (numSig is 0)
                continue;

            // coeff_abs_level_greater1_flag (clause 9.3.4.2.6): the first 8 significant coefficients of the sub-block
            var ctxSet = i is 0 || cIdx > 0 ? 0 : 2;
            if (greater1Ctx is 0)
            {
                ctxSet++;
            }

            var greater1Count = Math.Min(numSig, 8);
            greater1Ctx = ReadGreater1Flags(_cabac, HevcContextLayout.CoeffAbsLevelGreater1Flag + (ctxSet * 4) + (cIdx is 0 ? 0 : 16), greater1Flag[..greater1Count], out var lastGreater1Index);

            // coeff_abs_level_greater2_flag (clause 9.3.4.2.7): the first coefficient that is greater than 1
            var greater2Flag = 0;
            if (lastGreater1Index >= 0)
            {
                greater2Flag = _cabac.DecodeDecision(HevcContextLayout.CoeffAbsLevelGreater2Flag + ctxSet + (cIdx is 0 ? 0 : 4));
            }

            // coeff_sign_flag: the sign of the last coefficient of the sub-block in decoding order is hidden in the parity
            // of the sum of the levels when the first and the last one are more than 3 scan positions apart
            var signHidden = signHidingAllowed && significant[0] - significant[numSig - 1] > 3;
            var signCount = signHidden ? numSig - 1 : numSig;
            var signs = _cabac.DecodeBypassBits(signCount) << (16 - signCount);
            if (signHidden)
            {
                _tools |= HevcTools.SignDataHiding;
            }

            // coeff_abs_level_remaining (clause 9.3.3.11)
            var numSigCoeff = 0;
            var sumAbsLevel = 0;
            var firstRemaining = true;
            var cLastAbsLevel = 0;
            var cLastRiceParam = 0;
            for (var k = 0; k < numSig; k++)
            {
                var baseLevel = 1 + (k < greater1Count ? greater1Flag[k] : 0) + (k == lastGreater1Index ? greater2Flag : 0);
                var absLevel = baseLevel;
                if (baseLevel == (numSigCoeff < 8 ? (k == lastGreater1Index ? 3 : 2) : 1))
                {
                    if (firstRemaining && persistentRice)
                    {
                        // Equation 9-22
                        cLastRiceParam = (int)((_statCoeff >> (sbType * 8)) & 0xFF) / 4;
                    }

                    // Equations 9-24 and 9-25
                    var cRiceParam = cLastRiceParam + (cLastAbsLevel > 3 * (1 << cLastRiceParam) ? 1 : 0);
                    if (!persistentRice)
                    {
                        cRiceParam = Math.Min(cRiceParam, 4);
                    }

                    var coeffAbsLevelRemaining = ReadCoeffAbsLevelRemaining(_cabac, cRiceParam);
                    if (firstRemaining && persistentRice)
                    {
                        UpdateStatCoeff(sbType, coeffAbsLevelRemaining);
                    }

                    firstRemaining = false;
                    absLevel = baseLevel + coeffAbsLevelRemaining;
                    cLastAbsLevel = absLevel;
                    cLastRiceParam = cRiceParam;
                    _tools |= HevcTools.EscapeCoefficients;
                }

                var negative = (signs & (0x8000 >> k)) is not 0;
                if (signHidden)
                {
                    sumAbsLevel += absLevel;
                    if (k == numSig - 1 && (sumAbsLevel & 1) is 1)
                    {
                        negative = true;
                    }
                }

                // Clause 7.4.9.11: a transform coefficient level is in the range of CoeffMinY to CoeffMaxY
                var level = negative ? -absLevel : absLevel;
                if (level is < HevcInverseTransform.CoeffMin or > HevcInverseTransform.CoeffMax)
                    throw HevcErrors.Invalid("a transform coefficient level is out of range.");

                var position = positionScan[significant[k]];
                var index = ((((yS << 2) + HevcScanOrder.Y(position)) << log2TrafoSize) + (xS << 2)) + HevcScanOrder.X(position);
                coefficients[index] = scaled
                    ? (int)Math.Clamp((((m.IsEmpty ? level : level * m[index]) * levelScale) + rounding) >> bdShift, HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax)
                    : level;
                numSigCoeff++;
            }
        }

        return transformSkipFlag;
    }

    // last_sig_coeff_x_prefix and last_sig_coeff_y_prefix (clause 9.3.4.2.3): ctxOffset and ctxShift
    private static void GetLastSigCoeffPrefixContext(int log2TrafoSize, int cIdx, out int ctxOffset, out int ctxShift)
    {
        if (cIdx is 0)
        {
            ctxOffset = (3 * (log2TrafoSize - 2)) + ((log2TrafoSize - 1) >> 2);
            ctxShift = (log2TrafoSize + 1) >> 2;
        }
        else
        {
            ctxOffset = 15;
            ctxShift = log2TrafoSize - 2;
        }
    }

    // scanIdx (clause 7.4.9.11): the scan order of a small block follows its intra prediction mode
    private readonly int GetScanIdx(int log2TrafoSize, int cIdx, int predModeIntra)
    {
        if (log2TrafoSize is 2 || (log2TrafoSize is 3 && (cIdx is 0 || _chromaArrayType is 3)))
        {
            if (predModeIntra is >= 6 and <= 14)
                return HevcScanOrder.Vertical;

            if (predModeIntra is >= 22 and <= 30)
                return HevcScanOrder.Horizontal;
        }

        return HevcScanOrder.Diagonal;
    }

    // The index in SigCtxTable of the 16 values of sigCtx of a sub-block
    private static int GetSigCtxIndex(int log2TrafoSize, int cIdx, int scanIdx, bool isNotFirstSubBlock, int prevCsbf)
        => (((((((((log2TrafoSize - 2) * 2) + (cIdx is 0 ? 0 : 1)) * 3) + scanIdx) * 2) + (isNotFirstSubBlock ? 1 : 0)) * 4) + prevCsbf) * 16;

    private static byte[] CreateSigCtxTable()
    {
        var table = new byte[GetSigCtxIndex(6, 0, 0, isNotFirstSubBlock: false, 0)];
        for (var log2TrafoSize = 2; log2TrafoSize <= 5; log2TrafoSize++)
        {
            for (var cIdx = 0; cIdx < 2; cIdx++)
            {
                for (var scanIdx = 0; scanIdx < 3; scanIdx++)
                {
                    var positionScan = HevcScanOrder.Get(2, scanIdx);
                    for (var subBlock = 0; subBlock < 2; subBlock++)
                    {
                        for (var prevCsbf = 0; prevCsbf < 4; prevCsbf++)
                        {
                            var index = GetSigCtxIndex(log2TrafoSize, cIdx, scanIdx, subBlock is not 0, prevCsbf);
                            for (var n = 0; n < 16; n++)
                            {
                                // GetSigCtx only depends on whether xS and yS are both zero. The last position of a 4x4 block
                                // is never read: it is the last significant coefficient when it is significant
                                var xP = HevcScanOrder.X(positionScan[n]);
                                var yP = HevcScanOrder.Y(positionScan[n]);
                                if (log2TrafoSize is 2 && (yP << 2) + xP >= HevcCabacTables.CtxIdxMap.Length)
                                    continue;

                                table[index + n] = (byte)GetSigCtx(log2TrafoSize, cIdx, scanIdx, subBlock, 0, xP, yP, prevCsbf);
                            }
                        }
                    }
                }
            }
        }

        return table;
    }

    // sigCtx of sig_coeff_flag (clause 9.3.4.2.5) for a block without the context of the blocks that have no transform
    private static int GetSigCtx(int log2TrafoSize, int cIdx, int scanIdx, int xS, int yS, int xP, int yP, int prevCsbf)
    {
        if (log2TrafoSize is 2)
            return HevcCabacTables.CtxIdxMap[(yP << 2) + xP];

        if ((xS | yS | xP | yP) is 0)
            return 0;

        // Equations 9-45 to 9-53
        var sigCtx = prevCsbf switch
        {
            0 => xP + yP is 0 ? 2 : xP + yP < 3 ? 1 : 0,
            1 => yP is 0 ? 2 : yP is 1 ? 1 : 0,
            2 => xP is 0 ? 2 : xP is 1 ? 1 : 0,
            _ => 2,
        };
        if (cIdx is 0)
        {
            if ((xS | yS) is not 0)
            {
                sigCtx += 3;
            }

            sigCtx += log2TrafoSize is 3 ? (scanIdx is HevcScanOrder.Diagonal ? 9 : 15) : 21;
        }
        else
        {
            sigCtx += log2TrafoSize is 3 ? 9 : 12;
        }

        return sigCtx;
    }

    // sig_coeff_flag (clause 9.3.4.2.5) of the scan positions n to 0 of a sub-block, whose ctxIdx is contextBase + sigCtxs[ n ]:
    // the positions of the significant coefficients are appended to significant after its first numSig items. Returns the
    // number of significant coefficients. The loops of bins are not inlined in ResidualCoding, where the registers of the
    // arithmetic decoder would not stay in registers (HevcCabacEngine)
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadSigCoeffFlags(HevcCabacDecoder cabac, int contextBase, ReadOnlySpan<byte> sigCtxs, int n, bool inferSbDcSigCoeffFlag, Span<byte> significant, int numSig)
    {
        var contexts = cabac.Contexts;
        var engine = cabac.Engine;
        for (; n >= 0; n--)
        {
            if (n is 0 && inferSbDcSigCoeffFlag)
            {
                // The sub-block is coded and none of its other coefficients is significant
                significant[numSig++] = 0;
                break;
            }

            if (engine.DecodeDecision(contexts, contextBase + sigCtxs[n], cabac) is not 0)
            {
                significant[numSig++] = (byte)n;
                inferSbDcSigCoeffFlag = false;
            }
        }

        cabac.Engine = engine;
        return numSig;
    }

    // coeff_abs_level_greater1_flag (clause 9.3.4.2.6) of the first significant coefficients of a sub-block, whose ctxIdx is
    // contextBase + Min( 3, greater1Ctx ). Returns greater1Ctx after the last flag. Not inlined, like ReadSigCoeffFlags
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadGreater1Flags(HevcCabacDecoder cabac, int contextBase, Span<byte> greater1Flag, out int lastGreater1Index)
    {
        var contexts = cabac.Contexts;
        var engine = cabac.Engine;
        var greater1Ctx = 1;
        lastGreater1Index = -1;
        for (var k = 0; k < greater1Flag.Length; k++)
        {
            var flag = engine.DecodeDecision(contexts, contextBase + Math.Min(3, greater1Ctx), cabac);
            greater1Flag[k] = (byte)flag;
            if (flag is not 0)
            {
                greater1Ctx = 0;
                if (lastGreater1Index < 0)
                {
                    lastGreater1Index = k;
                }
            }
            else if (greater1Ctx > 0)
            {
                greater1Ctx++;
            }
        }

        cabac.Engine = engine;
        return greater1Ctx;
    }

    // The binarization of coeff_abs_level_remaining (clause 9.3.3.11): a prefix of at most 4 bins and a suffix of
    // cRiceParam bins, or a prefix of 4 bins and an Exp-Golomb code of order cRiceParam + 1. Not inlined, like
    // ReadSigCoeffFlags
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadCoeffAbsLevelRemaining(HevcCabacDecoder cabac, int cRiceParam)
    {
        var engine = cabac.Engine;
        var prefix = 0;
        while (engine.DecodeBypass(cabac) is not 0)
        {
            // The longest codes are those of the largest transform coefficient levels: a longer prefix is not a value
            if (++prefix > 32)
                throw HevcErrors.Invalid("a transform coefficient level is out of range.");
        }

        long value;
        if (prefix <= 3)
        {
            value = (prefix << cRiceParam) + engine.DecodeBypassBits(cRiceParam, cabac);
        }
        else
        {
            var length = prefix - 3 + cRiceParam;
            if (length > 30)
                throw HevcErrors.Invalid("a transform coefficient level is out of range.");

            value = (((1L << (prefix - 3)) + 3 - 1) << cRiceParam) + engine.DecodeBypassBits(length, cabac);
            if (value > 2 * -(long)HevcInverseTransform.CoeffMin)
                throw HevcErrors.Invalid("a transform coefficient level is out of range.");
        }

        cabac.Engine = engine;
        return (int)value;
    }

    // Equation 9-23
    private void UpdateStatCoeff(int sbType, int coeffAbsLevelRemaining)
    {
        var shift = sbType * 8;
        var statCoeff = (int)((_statCoeff >> shift) & 0xFF);
        if (coeffAbsLevelRemaining >= 3 << (statCoeff / 4))
        {
            statCoeff++;
        }
        else if (2 * coeffAbsLevelRemaining < 1 << (statCoeff / 4) && statCoeff > 0)
        {
            statCoeff--;
        }

        _statCoeff = (_statCoeff & ~(0xFFu << shift)) | ((uint)statCoeff << shift);
        _tools |= HevcTools.PersistentRiceAdaptation;
    }
}
