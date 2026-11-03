using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

        var subBlocksPerSide = 1 << (log2TrafoSize - 2);
        Span<byte> codedSubBlockFlag = stackalloc byte[64];
        Span<byte> significant = stackalloc byte[16];
        Span<int> remaining = stackalloc int[16];
        var transformSkipContext = _sps.TransformSkipContextEnabledFlag && (transformSkipFlag || _cuTransquantBypassFlag);
        var implicitRdpcm = _sps.ImplicitRdpcmEnabledFlag && transformSkipFlag && predModeIntra is HevcConstants.IntraAngular10 or HevcConstants.IntraAngular26;
        var levels = new HevcResidualLevels
        {
            Coefficients = coefficients,
            PositionScan = positionScan,
            ScalingFactors = m,
            Remaining = remaining,
            LevelScale = levelScale,
            Rounding = 1L << (bdShift - 1),
            BdShift = bdShift,
            Log2TrafoSize = log2TrafoSize,
            Scaled = scaled,
            SignHidingAllowed = _pps.SignDataHidingEnabledFlag && !_cuTransquantBypassFlag && !implicitRdpcm && !_explicitRdpcmFlag,
            PersistentRice = _sps.PersistentRiceAdaptationEnabledFlag,
            SbType = (cIdx is 0 ? 2 : 0) + (transformSkipFlag || _cuTransquantBypassFlag ? 1 : 0),
            Greater1Context = HevcContextLayout.CoeffAbsLevelGreater1Flag + (cIdx is 0 ? 0 : 16),
            Greater2Context = HevcContextLayout.CoeffAbsLevelGreater2Flag + (cIdx is 0 ? 0 : 4),
            StatCoeff = _statCoeff,
        };
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

            // ctxSet of coeff_abs_level_greater1_flag (clause 9.3.4.2.6): lastGreater1Ctx is greater1Ctx after the flags of the
            // previous sub-block that has some
            var ctxSet = (i is 0 || cIdx > 0 ? 0 : 2) + (greater1Ctx is 0 ? 1 : 0);
            greater1Ctx = ReadCoefficientLevels(_cabac, ref levels, significant[..numSig], ctxSet, (yS << (log2TrafoSize + 2)) + (xS << 2));
        }

        _statCoeff = levels.StatCoeff;
        _tools |= levels.Tools;
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
        var first = numSig;

        // The position is written in any case and kept when the flag is 1: the flags are hard to predict
        for (; n > 0; n--)
        {
            significant[numSig] = (byte)n;
            numSig += engine.DecodeDecision(contexts, contextBase + sigCtxs[n], cabac);
        }

        if (n is 0)
        {
            significant[numSig] = 0;
            if (inferSbDcSigCoeffFlag && numSig == first)
            {
                // The sub-block is coded and none of its other coefficients is significant
                numSig++;
            }
            else
            {
                numSig += engine.DecodeDecision(contexts, contextBase + sigCtxs[0], cabac);
            }
        }

        cabac.Engine = engine;
        return numSig;
    }

    // The levels of the significant coefficients of a sub-block (clause 7.3.8.11), from coeff_abs_level_greater1_flag to
    // coeff_abs_level_remaining, scaled and stored in the block: significant holds their scan positions in decoding order,
    // subBlockOffset is the index in the block of the first coefficient of the sub-block. Returns greater1Ctx after the last
    // coeff_abs_level_greater1_flag. Not inlined, like ReadSigCoeffFlags
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadCoefficientLevels(HevcCabacDecoder cabac, scoped ref HevcResidualLevels levels, ReadOnlySpan<byte> significant, int ctxSet, int subBlockOffset)
    {
        var contexts = cabac.Contexts;
        var engine = cabac.Engine;
        var numSig = significant.Length;

        // coeff_abs_level_greater1_flag (clause 9.3.4.2.6) of the first 8 significant coefficients: the bit k of flags is the
        // flag of the coefficient k. greater1Ctx is 0 after a flag equal to 1, otherwise 1 + the number of flags already read
        var greater1Count = Math.Min(numSig, 8);
        var greater1Context = levels.Greater1Context + (ctxSet * 4);
        var flags = 0;
        for (var k = 0; k < greater1Count; k++)
        {
            var greater1Ctx = flags is 0 ? Math.Min(3, k + 1) : 0;
            flags |= engine.DecodeDecision(contexts, greater1Context + greater1Ctx, cabac) << k;
        }

        // coeff_abs_level_greater2_flag (clause 9.3.4.2.7): the first coefficient that is greater than 1
        var firstGreater1 = flags & -flags;
        var greater2Flag = 0;
        if (flags is not 0)
        {
            greater2Flag = engine.DecodeDecision(contexts, levels.Greater2Context + ctxSet, cabac);
        }

        // coeff_sign_flag: the sign of the last coefficient of the sub-block in decoding order is hidden in the parity of the
        // sum of the levels when the first and the last one are more than 3 scan positions apart
        var signHidden = levels.SignHidingAllowed && significant[0] - significant[numSig - 1] > 3;
        var signCount = signHidden ? numSig - 1 : numSig;
        var signs = engine.DecodeBypassBits(signCount, cabac) << (16 - signCount);
        if (signHidden)
        {
            levels.Tools |= HevcTools.SignDataHiding;
        }

        // coeff_abs_level_remaining (clause 9.3.3.11), in the order of the coefficients, of those whose baseLevel is the
        // largest that the flags give: the first 8 coefficients greater than 1 but the first one, which has it when it is
        // greater than 2, and the coefficients after the first 8 ones. The bit k of escapes is set for the coefficient k
        var escapes = ((flags & ~firstGreater1) | (greater2Flag is not 0 ? firstGreater1 : 0) | ~0xFF) & ((1 << numSig) - 1);
        var remaining = levels.Remaining;
        if (escapes is not 0)
        {
            levels.Tools |= HevcTools.EscapeCoefficients;
            var cLastAbsLevel = 0;
            var cLastRiceParam = 0;
            if (levels.PersistentRice)
            {
                // Equation 9-22
                cLastRiceParam = (int)((levels.StatCoeff >> (levels.SbType * 8)) & 0xFF) / 4;
            }

            var pending = escapes;
            var firstRemaining = true;
            do
            {
                var k = BitOperations.TrailingZeroCount(pending);
                pending &= pending - 1;

                // Equations 9-24 and 9-25
                var cRiceParam = cLastRiceParam + (cLastAbsLevel > 3 * (1 << cLastRiceParam) ? 1 : 0);
                if (!levels.PersistentRice)
                {
                    cRiceParam = Math.Min(cRiceParam, 4);
                }

                var coeffAbsLevelRemaining = engine.DecodeCoeffAbsLevelRemaining(cRiceParam, cabac);
                if (firstRemaining && levels.PersistentRice)
                {
                    UpdateStatCoeff(ref levels, coeffAbsLevelRemaining);
                }

                firstRemaining = false;
                remaining[k] = coeffAbsLevelRemaining;
                cLastAbsLevel = GetBaseLevel(flags, firstGreater1, greater2Flag, k) + coeffAbsLevelRemaining;
                cLastRiceParam = cRiceParam;
            }
            while (pending is not 0);
        }

        cabac.Engine = engine;

        // The transform coefficient levels, scaled (clause 8.6.3) when the coding unit does not bypass the scaling
        var coefficients = levels.Coefficients;
        var positionScan = levels.PositionScan;
        var m = levels.ScalingFactors;
        var log2TrafoSize = levels.Log2TrafoSize;
        var sumAbsLevel = 0;
        for (var k = 0; k < numSig; k++)
        {
            // remaining is only read for the coefficients that have coeff_abs_level_remaining
            var absLevel = GetBaseLevel(flags, firstGreater1, greater2Flag, k) + (remaining[k] & -((escapes >> k) & 1));

            // The signs are hard to predict: 1 when the level is negative
            var negative = (signs >> (15 - k)) & 1;
            sumAbsLevel += absLevel;
            if (signHidden && k == numSig - 1)
            {
                negative |= sumAbsLevel & 1;
            }

            // Clause 7.4.9.11: a transform coefficient level is in the range of CoeffMinY to CoeffMaxY
            var level = (absLevel ^ -negative) + negative;
            if (level is < HevcInverseTransform.CoeffMin or > HevcInverseTransform.CoeffMax)
                throw HevcErrors.Invalid("a transform coefficient level is out of range.");

            var position = positionScan[significant[k]];
            var index = subBlockOffset + (HevcScanOrder.Y(position) << log2TrafoSize) + HevcScanOrder.X(position);
            coefficients[index] = levels.Scaled
                ? (int)Math.Clamp((((m.IsEmpty ? level : level * m[index]) * levels.LevelScale) + levels.Rounding) >> levels.BdShift, HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax)
                : level;
        }

        return flags is 0 ? greater1Count + 1 : 0;
    }

    // baseLevel of the coefficient k of a sub-block (Equation 7-80 without coeff_abs_level_remaining): 1, plus its
    // coeff_abs_level_greater1_flag, plus coeff_abs_level_greater2_flag for the first coefficient greater than 1
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetBaseLevel(int flags, int firstGreater1, int greater2Flag, int k)
        => 1 + ((flags >> k) & 1) + (((firstGreater1 >> k) & 1) * greater2Flag);

    // Equation 9-23
    private static void UpdateStatCoeff(scoped ref HevcResidualLevels levels, int coeffAbsLevelRemaining)
    {
        var shift = levels.SbType * 8;
        var statCoeff = (int)((levels.StatCoeff >> shift) & 0xFF);
        if (coeffAbsLevelRemaining >= 3 << (statCoeff / 4))
        {
            statCoeff++;
        }
        else if (2 * coeffAbsLevelRemaining < 1 << (statCoeff / 4) && statCoeff > 0)
        {
            statCoeff--;
        }

        levels.StatCoeff = (levels.StatCoeff & ~(0xFFu << shift)) | ((uint)statCoeff << shift);
        levels.Tools |= HevcTools.PersistentRiceAdaptation;
    }

    // The parameters of the levels of a transform block that are the same for its sub-blocks (ReadCoefficientLevels)
    [StructLayout(LayoutKind.Auto)]
    private ref struct HevcResidualLevels
    {
        // The rows of the block: TransCoeffLevel, or the scaled transform coefficients d (ResidualCoding)
        public Span<int> Coefficients;

        // The positions of a sub-block in scan order
        public ReadOnlySpan<byte> PositionScan;

        // m[ x ][ y ] of the scaling list, empty when it is 16 for every coefficient (LevelScale is then multiplied by 16)
        public ReadOnlySpan<byte> ScalingFactors;

        // coeff_abs_level_remaining of the 16 coefficients of a sub-block
        public Span<int> Remaining;

        public long LevelScale;
        public long Rounding;
        public int BdShift;
        public int Log2TrafoSize;

        // false for a coding unit that bypasses the scaling and the transform
        public bool Scaled;
        public bool SignHidingAllowed;
        public bool PersistentRice;
        public int SbType;

        // ctxIdx of the first coeff_abs_level_greater1_flag and coeff_abs_level_greater2_flag of the colour component
        public int Greater1Context;
        public int Greater2Context;

        // StatCoeff of the four sbType (Equation 9-23), and the coding tools that the levels use
        public uint StatCoeff;
        public HevcTools Tools;
    }
}
