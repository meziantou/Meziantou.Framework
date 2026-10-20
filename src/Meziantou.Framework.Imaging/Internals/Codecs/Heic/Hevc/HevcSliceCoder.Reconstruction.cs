using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

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
            var transformSkipFlag = ResidualCoding(log2TrafoSize, cIdx, predModeIntra, residual, out var rows, out var columns);
            ScaleAndTransform(residual, log2TrafoSize, cIdx, transformSkipFlag, scaled: true, rows, columns);

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
        if (nTbS is 4 && Vector128.IsHardwareAccelerated)
        {
            AddResidual4x4(samples[((yTb * plane.Stride) + xTb)..], plane.Stride, residual, maxValue);
            return;
        }

        for (var y = 0; y < nTbS; y++)
        {
            AddResidualRow(samples.Slice(((yTb + y) * plane.Stride) + xTb, nTbS), residual.Slice(y * nTbS, nTbS), maxValue);
        }
    }

    /// <summary>
    /// Adds the residual samples of a row to the prediction samples, clipped to the range of the samples (Equation 8-300):
    /// 8 samples at a time.
    /// </summary>
    internal static void AddResidualRow(Span<ushort> row, ReadOnlySpan<int> residual, int maxValue)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var max = Vector128.Create(maxValue);
            for (; x + 8 <= row.Length; x += 8)
            {
                var (low, high) = Vector128.Widen(Vector128.Create(row[x..]));
                var sumLow = Vector128.Min(Vector128.Max(low.AsInt32() + Vector128.Create(residual[x..]), Vector128<int>.Zero), max);
                var sumHigh = Vector128.Min(Vector128.Max(high.AsInt32() + Vector128.Create(residual[(x + 4)..]), Vector128<int>.Zero), max);
                Vector128.Narrow(sumLow.AsUInt32(), sumHigh.AsUInt32()).CopyTo(row[x..]);
            }
        }

        AddResidualRowScalar(row[x..], residual[x..], maxValue);
    }

    /// <summary>
    /// Adds the residual samples of a 4x4 block to its prediction samples, clipped to the range of the samples (Equation
    /// 8-300): two rows per vector.
    /// </summary>
    internal static void AddResidual4x4(Span<ushort> samples, int stride, ReadOnlySpan<int> residual, int maxValue)
    {
        var max = Vector128.Create(maxValue);
        for (var y = 0; y < 4; y += 2)
        {
            var first = samples.Slice(y * stride, 4);
            var second = samples.Slice((y + 1) * stride, 4);
            var (low, high) = Vector128.Widen(Vector128.Create(Vector64.Create<ushort>(first), Vector64.Create<ushort>(second)));
            var sumLow = Vector128.Min(Vector128.Max(low.AsInt32() + Vector128.Create(residual[(4 * y)..]), Vector128<int>.Zero), max);
            var sumHigh = Vector128.Min(Vector128.Max(high.AsInt32() + Vector128.Create(residual[((4 * y) + 4)..]), Vector128<int>.Zero), max);
            var rows = Vector128.Narrow(sumLow.AsUInt32(), sumHigh.AsUInt32());
            rows.GetLower().CopyTo(first);
            rows.GetUpper().CopyTo(second);
        }
    }

    /// <summary>The addition of <see cref="AddResidualRow"/>, one sample at a time: the reference of the vector path.</summary>
    internal static void AddResidualRowScalar(Span<ushort> row, ReadOnlySpan<int> residual, int maxValue)
    {
        for (var x = 0; x < row.Length; x++)
        {
            row[x] = (ushort)Math.Clamp(row[x] + residual[x], 0, maxValue);
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
    /// The scaling and transformation process (clause 8.6.2) of a block: the transform coefficient levels, or the scaled
    /// transform coefficients when <paramref name="scaled"/> is set, are replaced by the residual samples.
    /// </summary>
    /// <param name="block">The rows of the block.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of the size of the block.</param>
    /// <param name="cIdx">The colour component of the block.</param>
    /// <param name="transformSkipFlag"><c>transform_skip_flag</c>.</param>
    /// <param name="scaled">Whether the block has the scaled transform coefficients already (<see cref="ResidualCoding"/>), rather than the levels.</param>
    /// <param name="rows">The number of rows that can have values that are not zero: the others are not read when the block is transformed.</param>
    /// <param name="columns">The number of columns that can have values that are not zero: a multiple of 4.</param>
    private void ScaleAndTransform(Span<int> block, int log2TrafoSize, int cIdx, bool transformSkipFlag, bool scaled = false, int rows = 32, int columns = 32)
    {
        var nTbS = 1 << log2TrafoSize;
        rows = Math.Min(rows, nTbS);
        columns = Math.Min(columns, nTbS);
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
        var scalingList = !_layout.ScalingFactors.IsEmpty && !(transformSkipFlag && nTbS > 4);
        if (!scaled)
        {
            ScaleLevels(block, log2TrafoSize, cIdx, scalingList);
        }

        if (scalingList)
        {
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
        HevcInverseTransform.Transform(block, log2TrafoSize, trType, _scratch.Transform, rows, columns, residualShift);
        _tools |= trType is 1
            ? HevcTools.Dst4x4
            : log2TrafoSize switch
            {
                2 => HevcTools.Transform4x4,
                3 => HevcTools.Transform8x8,
                4 => HevcTools.Transform16x16,
                _ => HevcTools.Transform32x32,
            };
    }

    // Clause 8.6.3 "Scaling process for transform coefficients": the transform coefficient levels of a block are replaced by
    // the scaled transform coefficients
    private readonly void ScaleLevels(Span<int> block, int log2TrafoSize, int cIdx, bool scalingList)
    {
        var qP = cIdx is 0 ? _qpY + _sps.QpBdOffset : GetChromaQp(cIdx);
        var bdShift = _bitDepth + log2TrafoSize + 10 - 15;
        long levelScale = HevcQuantizationTables.LevelScale[qP % 6] << (qP / 6);
        var rounding = 1L << (bdShift - 1);
        if (!scalingList)
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

            return;
        }

        // Equation 8-308: matrixId is cIdx for a coding unit coded in intra prediction mode, 3 + cIdx otherwise (Table 7-4)
        var m = _layout.ScalingFactors.Slice(HevcScalingList.GetScalingFactorOffset(log2TrafoSize - 2, _isIntra ? cIdx : 3 + cIdx), block.Length);
        for (var i = 0; i < block.Length; i++)
        {
            if (block[i] is not 0)
            {
                block[i] = (int)Math.Clamp(((block[i] * m[i] * levelScale) + rounding) >> bdShift, HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax);
            }
        }
    }

    /// <summary>
    /// Predicts a block from its neighbouring samples (clause 8.4.4.2.1 "General intra sample prediction"), into the plane
    /// of its colour component.
    /// </summary>
    [SkipLocalsInit]
    private void PredictIntra(Av1Plane plane, int cIdx, int xTb, int yTb, int xTbY, int yTbY, int nTbS, int predModeIntra)
    {
        // The filtered samples are all written
        var count = (nTbS * 4) + 1;
        Span<ushort> buffer = unsafe(stackalloc ushort[2 * HevcIntraPredictor.MaxNeighbourCount]);
        var p = buffer[..count];
        GetIntraReferences(plane, cIdx, xTb, yTb, xTbY, yTbY, nTbS, p);

        // Step 1: the filtering process of neighbouring samples
        if (IsIntraReferenceFiltered(cIdx, nTbS, predModeIntra))
        {
            var filtered = buffer.Slice(HevcIntraPredictor.MaxNeighbourCount, count);
            FilterIntraReferences(cIdx, nTbS, p, filtered);
            p = filtered;
        }

        // Step 2: the intra sample prediction process of the mode
        var stride = plane.Stride;
        PredictIntraSamples(cIdx, nTbS, predModeIntra, p, plane.Samples[((yTb * stride) + xTb)..], stride);
    }

    // The neighbouring samples of a block (clause 8.4.4.2.1), after the reference sample substitution process
    [SkipLocalsInit]
    private void GetIntraReferences(Av1Plane plane, int cIdx, int xTb, int yTb, int xTbY, int yTbY, int nTbS, scoped Span<ushort> p)
    {
        var subWidth = cIdx is 0 ? 1 : _sps.SubWidthC;
        var subHeight = cIdx is 0 ? 1 : _sps.SubHeightC;

        // The availability is the one of the minimum transform block of a neighbouring sample: at least 4 luma samples
        var unitWidth = 4 / subWidth;
        var unitHeight = 4 / subHeight;
        var samples = plane.Samples;
        var stride = plane.Stride;
        var corner = nTbS * 2;

        // The samples that are not available are substituted before they are read: only the availability is cleared
        Span<bool> availableBuffer = unsafe(stackalloc bool[HevcIntraPredictor.MaxNeighbourCount]);
        var available = availableBuffer[..p.Length];
        available.Clear();
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
    }

    // Whether the neighbouring samples of a block are filtered for a prediction mode (clause 8.4.4.2.3)
    private bool IsIntraReferenceFiltered(int cIdx, int nTbS, int predModeIntra)
    {
        if (_sps.IntraSmoothingDisabledFlag)
        {
            _tools |= HevcTools.IntraSmoothingDisabled;
            return false;
        }

        return (cIdx is 0 || _chromaArrayType is 3) && HevcIntraPredictor.GetFilterFlag(predModeIntra, nTbS);
    }

    // Clause 8.4.4.2.3 "Filtering process of neighbouring samples", for a mode whose filterFlag is 1
    private void FilterIntraReferences(int cIdx, int nTbS, scoped ReadOnlySpan<ushort> p, scoped Span<ushort> filtered)
    {
        var biIntFlag = HevcIntraPredictor.Filter(p, filtered, nTbS, _sps.StrongIntraSmoothingEnabledFlag && cIdx is 0, _bitDepth);
        _tools |= HevcTools.IntraReferenceFiltering | (biIntFlag ? HevcTools.StrongIntraSmoothing : HevcTools.None);
    }

    // Step 2 of clause 8.4.4.2.1: the intra sample prediction process of the mode, from the neighbouring samples that apply
    // to it
    private void PredictIntraSamples(int cIdx, int nTbS, int predModeIntra, scoped ReadOnlySpan<ushort> p, scoped Span<ushort> destination, int stride)
    {
        switch (predModeIntra)
        {
            case HevcConstants.IntraPlanar:
                HevcIntraPredictor.PredictPlanar(p, destination, stride, nTbS, _bitDepth);
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
