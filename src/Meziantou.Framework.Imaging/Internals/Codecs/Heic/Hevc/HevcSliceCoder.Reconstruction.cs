namespace Meziantou.Framework.Imaging.Internals;

// The decoding process of the blocks of a transform unit: intra prediction, scaling, transformation and construction of
// the picture
internal ref partial struct HevcSliceCoder
{
    /// <summary>
    /// Decodes a block of a colour component (clause 8.4.4.1 "General decoding process for intra blocks", steps 4 to 11):
    /// the block is predicted from the samples constructed before it, and its residual is added to it.
    /// </summary>
    /// <param name="cIdx">The colour component of the block.</param>
    /// <param name="xTb">The horizontal position of the block, in samples of the component.</param>
    /// <param name="yTb">The vertical position of the block, in samples of the component.</param>
    /// <param name="xTbY">The horizontal position of the block, in luma samples.</param>
    /// <param name="yTbY">The vertical position of the block, in luma samples.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of the size of the block.</param>
    /// <param name="predModeIntra">The intra prediction mode of the block; or -1 for a block of a coding unit coded in inter prediction mode, whose prediction samples are in the picture already.</param>
    /// <param name="coded">Whether the block has transform coefficient levels: its coded block flag.</param>
    /// <param name="crossComponent">Whether the cross-component prediction applies to the transform unit.</param>
    private void DecodeIntraBlock(int cIdx, int xTb, int yTb, int xTbY, int yTbY, int log2TrafoSize, int predModeIntra, bool coded, bool crossComponent)
    {
        var plane = _picture.GetPlane(cIdx);
        var nTbS = 1 << log2TrafoSize;
        if (predModeIntra >= 0)
        {
            PredictIntra(plane, cIdx, xTb, yTb, xTbY, yTbY, nTbS, predModeIntra);
        }

        var residual = _scratch.Coefficients[..(nTbS * nTbS)];
        var hasResidual = false;
        if (coded)
        {
            var transformSkipFlag = ResidualCoding(log2TrafoSize, cIdx, predModeIntra, residual);
            ScaleAndTransform(residual, log2TrafoSize, cIdx, transformSkipFlag);

            // Step 7: the residual of a block that has no transform is a difference with the residual before it, in the
            // direction of the prediction (clause 8.6.5)
            if (_sps.ImplicitRdpcmEnabledFlag && (transformSkipFlag || _cuTransquantBypassFlag) && predModeIntra is HevcConstants.IntraAngular10 or HevcConstants.IntraAngular26)
            {
                AccumulateResidual(residual, nTbS, vertical: predModeIntra is HevcConstants.IntraAngular26);
                _tools |= HevcTools.ImplicitRdpcm;
            }
            else if (_explicitRdpcmFlag)
            {
                // Clause 8.5.4.2, step 4: the direction is explicit_rdpcm_dir_flag
                AccumulateResidual(residual, nTbS, vertical: _explicitRdpcmDirFlag);
            }

            hasResidual = true;
            if (cIdx is 0 && (_edgeFlags[((yTb >> 2) * _widthIn4x4) + (xTb >> 2)] & HevcPictureState.CodedLuma) is 0)
            {
                MarkCodedLuma(xTb, yTb, log2TrafoSize);
            }
        }

        if (cIdx is 0)
        {
            if (_pps.CrossComponentPredictionEnabledFlag)
            {
                // The luma residual of the transform unit is an input of the residual of its chroma blocks
                var lumaResidual = _scratch.LumaResidual[..(nTbS * nTbS)];
                if (coded)
                {
                    residual.CopyTo(lumaResidual);
                }
                else
                {
                    lumaResidual.Clear();
                }
            }
        }
        else if (crossComponent && _resScaleVal is not 0)
        {
            // Step 8: clause 8.6.6 "Residual modification process for transform blocks using cross-component prediction".
            // Both bit depths are the same: ( rY << BitDepthC ) >> BitDepthY is rY
            if (!coded)
            {
                residual.Clear();
            }

            var lumaResidual = _scratch.LumaResidual[..(nTbS * nTbS)];
            for (var i = 0; i < residual.Length; i++)
            {
                residual[i] += (_resScaleVal * lumaResidual[i]) >> 3;
            }

            hasResidual = true;
        }

        if (!hasResidual)
            return;

        AddResidual(plane, xTb, yTb, nTbS, residual);
    }

    // Clause 8.6.7 "Picture construction process prior to in-loop filter process"
    private readonly void AddResidual(Av1Plane plane, int xTb, int yTb, int nTbS, ReadOnlySpan<int> residual)
    {
        var maxValue = (1 << _bitDepth) - 1;
        var samples = plane.Samples;
        for (var y = 0; y < nTbS; y++)
        {
            var row = samples.Slice(((yTb + y) * plane.Stride) + xTb, nTbS);
            var residualRow = residual.Slice(y * nTbS, nTbS);
            for (var x = 0; x < nTbS; x++)
            {
                row[x] = (ushort)Math.Clamp(row[x] + residualRow[x], 0, maxValue);
            }
        }
    }

    private readonly void MarkCodedLuma(int x0, int y0, int log2Size)
    {
        var count = 1 << (log2Size - 2);
        var index = ((y0 >> 2) * _widthIn4x4) + (x0 >> 2);
        for (var y = 0; y < count; y++)
        {
            var row = _edgeFlags.Slice(index + (y * _widthIn4x4), count);
            for (var x = 0; x < count; x++)
            {
                row[x] |= HevcPictureState.CodedLuma;
            }
        }
    }

    // Clause 8.6.5 "Residual modification process for blocks using a transform bypass"
    private static void AccumulateResidual(Span<int> residual, int nTbS, bool vertical)
    {
        if (vertical)
        {
            for (var i = nTbS; i < residual.Length; i++)
            {
                residual[i] += residual[i - nTbS];
            }
        }
        else
        {
            for (var y = 0; y < nTbS; y++)
            {
                var row = residual.Slice(y * nTbS, nTbS);
                for (var x = 1; x < nTbS; x++)
                {
                    row[x] += row[x - 1];
                }
            }
        }
    }

    /// <summary>
    /// The scaling and transformation process (clause 8.6.2) of a block of a coding unit coded in intra prediction mode:
    /// the transform coefficient levels are replaced by the residual samples.
    /// </summary>
    private void ScaleAndTransform(Span<int> block, int log2TrafoSize, int cIdx, bool transformSkipFlag)
    {
        var nTbS = 1 << log2TrafoSize;
        var rotateCoeffs = _sps.TransformSkipRotationEnabledFlag && nTbS is 4 && _isIntra;
        if (_cuTransquantBypassFlag)
        {
            // Equation 8-297
            if (rotateCoeffs)
            {
                block.Reverse();
                _tools |= HevcTools.TransformSkipRotation;
            }

            return;
        }

        // Clause 8.6.3 "Scaling process for transform coefficients"
        var qP = cIdx is 0 ? _qpY + _sps.QpBdOffset : GetChromaQp(cIdx);
        var bdShift = _bitDepth + log2TrafoSize + 10 - 15;
        long levelScale = HevcQuantizationTables.LevelScale[qP % 6] << (qP / 6);
        var rounding = 1L << (bdShift - 1);
        var scalingFactors = _layout.ScalingFactors;
        if (scalingFactors.IsEmpty || (transformSkipFlag && nTbS > 4))
        {
            // m[ x ][ y ] is 16
            levelScale *= 16;
            for (var i = 0; i < block.Length; i++)
            {
                if (block[i] is not 0)
                {
                    block[i] = (int)Math.Clamp(((block[i] * levelScale) + rounding) >> bdShift, HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax);
                }
            }
        }
        else
        {
            // Equation 8-308: matrixId is cIdx for a coding unit coded in intra prediction mode, 3 + cIdx otherwise
            // (Table 7-4)
            var m = scalingFactors.Slice(HevcScalingList.GetScalingFactorOffset(log2TrafoSize - 2, _isIntra ? cIdx : 3 + cIdx), block.Length);
            for (var i = 0; i < block.Length; i++)
            {
                if (block[i] is not 0)
                {
                    block[i] = (int)Math.Clamp(((block[i] * m[i] * levelScale) + rounding) >> bdShift, HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax);
                }
            }

            _tools |= HevcTools.ScalingList;
        }

        var residualShift = 20 - _bitDepth;
        if (transformSkipFlag)
        {
            // Equation 8-298
            if (rotateCoeffs)
            {
                block.Reverse();
                _tools |= HevcTools.TransformSkipRotation;
            }

            var tsShift = 5 + log2TrafoSize;
            for (var i = 0; i < block.Length; i++)
            {
                block[i] = ((block[i] << tsShift) + (1 << (residualShift - 1))) >> residualShift;
            }

            return;
        }

        // Clause 8.6.4.1: the transform type of the 4x4 luma blocks of an intra coding unit is the discrete sine transform
        var trType = nTbS is 4 && cIdx is 0 && _isIntra ? 1 : 0;
        HevcInverseTransform.Transform(block, log2TrafoSize, trType, _scratch.Transform);
        _tools |= trType is 1
            ? HevcTools.Dst4x4
            : log2TrafoSize switch
            {
                2 => HevcTools.Transform4x4,
                3 => HevcTools.Transform8x8,
                4 => HevcTools.Transform16x16,
                _ => HevcTools.Transform32x32,
            };

        // Equation 8-299
        for (var i = 0; i < block.Length; i++)
        {
            block[i] = (block[i] + (1 << (residualShift - 1))) >> residualShift;
        }
    }

    /// <summary>
    /// Predicts a block from its neighbouring samples (clause 8.4.4.2.1 "General intra sample prediction"), into the plane
    /// of its colour component.
    /// </summary>
    private void PredictIntra(Av1Plane plane, int cIdx, int xTb, int yTb, int xTbY, int yTbY, int nTbS, int predModeIntra)
    {
        var subWidth = cIdx is 0 ? 1 : _sps.SubWidthC;
        var subHeight = cIdx is 0 ? 1 : _sps.SubHeightC;

        // The availability is the one of the minimum transform block of a neighbouring sample: at least 4 luma samples
        var unitWidth = 4 / subWidth;
        var unitHeight = 4 / subHeight;
        var samples = plane.Samples;
        var stride = plane.Stride;
        var corner = nTbS * 2;
        var count = (nTbS * 4) + 1;
        Span<ushort> buffer = stackalloc ushort[2 * HevcIntraPredictor.MaxNeighbourCount];
        Span<bool> availableBuffer = stackalloc bool[HevcIntraPredictor.MaxNeighbourCount];
        var p = buffer[..count];
        var available = availableBuffer[..count];
        var constrainedIntraPred = _pps.ConstrainedIntraPredFlag;

        // The left column, p[ -1 ][ y ]
        for (var y = 0; y < nTbS * 2; y += unitHeight)
        {
            if (!IsAvailableForIntra(xTbY, yTbY, xTbY - 1, yTbY + (y * subHeight), constrainedIntraPred))
                continue;

            for (var k = 0; k < unitHeight; k++)
            {
                p[corner - 1 - y - k] = samples[((yTb + y + k) * stride) + xTb - 1];
                available[corner - 1 - y - k] = true;
            }
        }

        // The corner, p[ -1 ][ -1 ]
        if (IsAvailableForIntra(xTbY, yTbY, xTbY - 1, yTbY - 1, constrainedIntraPred))
        {
            p[corner] = samples[((yTb - 1) * stride) + xTb - 1];
            available[corner] = true;
        }

        // The row above, p[ x ][ -1 ]
        for (var x = 0; x < nTbS * 2; x += unitWidth)
        {
            if (!IsAvailableForIntra(xTbY, yTbY, xTbY + (x * subWidth), yTbY - 1, constrainedIntraPred))
                continue;

            samples.Slice(((yTb - 1) * stride) + xTb + x, unitWidth).CopyTo(p[(corner + 1 + x)..]);
            available.Slice(corner + 1 + x, unitWidth).Fill(true);
        }

        if (HevcIntraPredictor.Substitute(p, available, _bitDepth))
        {
            _tools |= HevcTools.IntraReferenceSubstitution | (constrainedIntraPred ? HevcTools.ConstrainedIntraPred : HevcTools.None);
        }

        // Step 1: the filtering process of neighbouring samples
        if (_sps.IntraSmoothingDisabledFlag)
        {
            _tools |= HevcTools.IntraSmoothingDisabled;
        }
        else if ((cIdx is 0 || _chromaArrayType is 3) && HevcIntraPredictor.GetFilterFlag(predModeIntra, nTbS))
        {
            var filtered = buffer.Slice(HevcIntraPredictor.MaxNeighbourCount, count);
            var biIntFlag = HevcIntraPredictor.Filter(p, filtered, nTbS, _sps.StrongIntraSmoothingEnabledFlag && cIdx is 0, _bitDepth);
            p = filtered;
            _tools |= HevcTools.IntraReferenceFiltering | (biIntFlag ? HevcTools.StrongIntraSmoothing : HevcTools.None);
        }

        // Step 2: the intra sample prediction process of the mode
        var destination = samples[((yTb * stride) + xTb)..];
        switch (predModeIntra)
        {
            case HevcConstants.IntraPlanar:
                HevcIntraPredictor.PredictPlanar(p, destination, stride, nTbS);
                _tools |= HevcTools.IntraPlanar;
                break;

            case HevcConstants.IntraDc:
                HevcIntraPredictor.PredictDc(p, destination, stride, nTbS, edgeFilter: cIdx is 0 && nTbS < 32);
                _tools |= HevcTools.IntraDc;
                break;

            default:
                // disableIntraBoundaryFilter
                var edgeFilter = cIdx is 0 && nTbS < 32 && !(_sps.ImplicitRdpcmEnabledFlag && _cuTransquantBypassFlag);
                HevcIntraPredictor.PredictAngular(p, destination, stride, nTbS, predModeIntra, edgeFilter, _bitDepth);
                _tools |= HevcTools.IntraAngular;
                break;
        }
    }

    // A neighbouring sample is marked as available for intra prediction when its block is available and, with constrained
    // intra prediction, when its coding unit is coded in intra prediction mode
    private readonly bool IsAvailableForIntra(int xCurr, int yCurr, int xNbY, int yNbY, bool constrainedIntraPred)
    {
        if (!IsAvailable(xCurr, yCurr, xNbY, yNbY))
            return false;

        return !constrainedIntraPred || (GetCuFlags(xNbY, yNbY) & HevcPictureState.CuIntra) is not 0;
    }
}
