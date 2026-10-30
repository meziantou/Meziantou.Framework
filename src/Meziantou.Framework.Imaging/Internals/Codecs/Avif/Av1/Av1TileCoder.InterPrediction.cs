using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>
/// The prediction of a block from another frame, or from the frame itself with intra block copy (AV1 specification, sections
/// 5.11.33 "Compute prediction syntax" and 7.11.3 "Inter prediction process").
/// </content>
internal ref partial struct Av1TileCoder
{
    // Section 5.11.33 "Compute prediction syntax": the intra prediction of a block that is not inter is made for each
    // transform block
    private void ComputePrediction()
    {
        if (!_isInter)
            return;

        var subBlockMiRow = _miRow & _sbMask;
        var subBlockMiCol = _miCol & _sbMask;
        var isInterIntra = _refFrame1 is Av1Constants.IntraFrame;
        for (var plane = 0; plane < 1 + (_hasChroma ? 2 : 0); plane++)
        {
            var planeSz = GetPlaneResidualSize(_miSize, plane);
            int num4x4W = Av1Tables.Num4x4BlocksWide[planeSz];
            int num4x4H = Av1Tables.Num4x4BlocksHigh[planeSz];
            var subX = plane > 0 ? _subX : 0;
            var subY = plane > 0 ? _subY : 0;
            var baseX = (_miCol >> subX) * Av1Constants.MiSize;
            var baseY = (_miRow >> subY) * Av1Constants.MiSize;
            var candRow = (_miRow >> subY) << subY;
            var candCol = (_miCol >> subX) << subX;
            if (isInterIntra)
            {
                var mode = _interintraMode switch
                {
                    Av1Constants.IiDcPred => Av1Constants.DcPred,
                    Av1Constants.IiVPred => Av1Constants.VPred,
                    Av1Constants.IiHPred => Av1Constants.HPred,
                    _ => Av1Constants.SmoothPred,
                };

                var planeBuffer = _frame.GetPlane(plane);
                var blockDecoded = _scratch.GetBlockDecoded(plane);
                var haveAboveRight = blockDecoded[BlockDecodedIndex((subBlockMiRow >> subY) - 1, (subBlockMiCol >> subX) + num4x4W)] is not 0;
                var haveBelowLeft = blockDecoded[BlockDecodedIndex((subBlockMiRow >> subY) + num4x4H, (subBlockMiCol >> subX) - 1)] is not 0;
                var parameters = new Av1IntraPredictor.Parameters(_bitDepth, mode, AngleDelta: 0, FilterIntraMode: -1, _sequence.EnableIntraEdgeFilter, SmoothNeighbor: false);
                Av1IntraPredictor.Predict(
                    planeBuffer.Samples,
                    planeBuffer.Stride,
                    baseX,
                    baseY,
                    plane is 0 ? _availL : _availLChroma,
                    plane is 0 ? _availU : _availUChroma,
                    haveAboveRight,
                    haveBelowLeft,
                    Av1Constants.MiSizeLog2 + Av1Tables.MiWidthLog2[planeSz],
                    Av1Constants.MiSizeLog2 + Av1Tables.MiHeightLog2[planeSz],
                    ((_miCols * Av1Constants.MiSize) >> subX) - 1,
                    ((_miRows * Av1Constants.MiSize) >> subY) - 1,
                    parameters);
            }

            var predW = BlockWidth(_miSize) >> subX;
            var predH = BlockHeight(_miSize) >> subY;
            var someUseIntra = false;
            for (var r = 0; r < (num4x4H << subY) && candRow + r < _miRows; r++)
            {
                for (var c = 0; c < (num4x4W << subX) && candCol + c < _miCols; c++)
                {
                    if (_refFrames[MiIndex(candRow + r, candCol + c) * 2] is Av1Constants.IntraFrame)
                        someUseIntra = true;
                }
            }

            if (!someUseIntra && (predW < num4x4W * 4 || predH < num4x4H * 4))
                _scratch.Tools |= Av1Tools.SubBlockChromaPrediction;

            if (someUseIntra)
            {
                predW = num4x4W * 4;
                predH = num4x4H * 4;
                candRow = _miRow;
                candCol = _miCol;
            }

            var rowOffset = 0;
            for (var y = 0; y < num4x4H * 4; y += predH)
            {
                var colOffset = 0;
                for (var x = 0; x < num4x4W * 4; x += predW)
                {
                    PredictInter(plane, baseX + x, baseY + y, predW, predH, candRow + rowOffset, candCol + colOffset);
                    colOffset++;
                }

                rowOffset++;
            }
        }
    }

    // Section 7.11.3.1 "General" of the inter prediction process
    private void PredictInter(int plane, int x, int y, int w, int h, int candRow, int candCol)
    {
        const int Stride = Av1InterScratch.Stride;
        var scratch = _interScratch!;
        var candIndex = MiIndex(candRow, candCol);
        var isCompound = _refFrames[(candIndex * 2) + 1] > Av1Constants.IntraFrame;
        var isInterIntra = _refFrame1 is Av1Constants.IntraFrame;
        var subX = plane > 0 ? _subX : 0;
        var subY = plane > 0 ? _subY : 0;

        // Section 7.11.3.2 "Rounding variables derivation process"
        var interRound0 = 3;
        var interRound1 = isCompound ? 7 : 11;
        if (_bitDepth is 12)
        {
            interRound0 += 2;
            if (!isCompound)
                interRound1 -= 2;
        }

        var interPostRound = (2 * Av1Constants.FilterBits) - (interRound0 + interRound1);
        Span<int> warpParams = stackalloc int[6];
        Span<int> shear = stackalloc int[4];
        if (plane is 0 && _motionMode is Av1Constants.Localwarp)
        {
            var midY = (_miRow * 4) + (Av1Tables.Num4x4BlocksHigh[_miSize] * 2) - 1;
            var midX = (_miCol * 4) + (Av1Tables.Num4x4BlocksWide[_miSize] * 2) - 1;
            _localValid = Av1WarpModel.Estimate(in _candList, _numSamples, midY, midX, _mv[0], _mv[1], warpParams);
            if (_localValid)
                _localValid = Av1WarpModel.SetupShear(warpParams, shear);

            for (var i = 0; i < 6; i++)
            {
                _localWarpParams[i] = warpParams[i];
            }

            for (var i = 0; i < 4; i++)
            {
                _localShear[i] = shear[i];
            }
        }

        var isGlobalMode = _yMode is Av1Constants.Globalmv or Av1Constants.GlobalGlobalmv;
        var filters = _interInfo[candIndex];
        for (var refList = 0; refList < (isCompound ? 2 : 1); refList++)
        {
            int refFrame = _refFrames[(candIndex * 2) + refList];
            var pred = scratch.GetPrediction(refList);
            var isGlobalWarp = isGlobalMode && _header.GlobalMotion.Types[refFrame] > Av1Constants.Translation;
            var globalValid = false;
            if (isGlobalWarp)
            {
                for (var i = 0; i < 6; i++)
                {
                    warpParams[i] = _header.GlobalMotion.Parameters[refFrame][i];
                }

                globalValid = Av1WarpModel.SetupShear(warpParams, shear);
            }

            int useWarp;
            if (w < 8 || h < 8 || _header.ForceIntegerMv)
            {
                useWarp = 0;
            }
            else if (_motionMode is Av1Constants.Localwarp && _localValid)
            {
                useWarp = 1;
            }
            else
            {
                useWarp = isGlobalWarp && globalValid && !_references.IsScaled(refFrame) ? 2 : 0;
            }

            GetReferencePlane(plane, refFrame, out var reference, out var lastX, out var lastY, out var xScale, out var yScale);
            if (useWarp is not 0)
            {
                _scratch.Tools |= useWarp is 1 ? Av1Tools.LocalWarp : Av1Tools.GlobalWarp;
                if (useWarp is 1)
                {
                    for (var i = 0; i < 6; i++)
                    {
                        warpParams[i] = _localWarpParams[i];
                    }

                    for (var i = 0; i < 4; i++)
                    {
                        shear[i] = _localShear[i];
                    }
                }

                for (var i8 = 0; i8 <= (h - 1) >> 3; i8++)
                {
                    for (var j8 = 0; j8 <= (w - 1) >> 3; j8++)
                    {
                        Av1InterPredictor.WarpBlock(reference.Samples, reference.Stride, lastX, lastY, warpParams, shear, x, y, i8, j8, w, h, subX, subY, interRound0, interRound1, pred, Stride);
                    }
                }
            }
            else
            {
                ScaleMotionVector(x, y, _mvs[(candIndex * 4) + (refList * 2)], _mvs[(candIndex * 4) + (refList * 2) + 1], subX, subY, xScale, yScale, out var startX, out var startY, out var stepX, out var stepY);
                Av1InterPredictor.PredictBlock(reference.Samples, reference.Stride, lastX, lastY, startX, startY, stepX, stepY, w, h, (filters >> 2) & 3, filters & 3, interRound0, interRound1, scratch.Intermediate, pred, Stride);
            }
        }

        var mask = scratch.Mask;
        if (_compoundType is Av1Constants.CompoundWedge && plane is 0)
        {
            Av1InterMasks.FillWedgeMask(mask, Stride, _miSize, _wedgeSign, _wedgeIndex);
        }
        else if (_compoundType is Av1Constants.CompoundIntra)
        {
            Av1InterMasks.FillIntraVariantMask(mask, Stride, w, h, _interintraMode);
        }
        else if (_compoundType is Av1Constants.CompoundDiffwtd && plane is 0)
        {
            Av1InterMasks.FillDifferenceWeightMask(mask, Stride, scratch.GetPrediction(0), scratch.GetPrediction(1), Stride, w, h, _bitDepth, interPostRound, _maskType);
        }

        var planeBuffer = _frame.GetPlane(plane);
        var samples = planeBuffer.Samples;
        var stride = planeBuffer.Stride;
        var max = (1 << _bitDepth) - 1;
        ReadOnlySpan<int> pred0 = scratch.GetPrediction(0);
        ReadOnlySpan<int> pred1 = scratch.GetPrediction(1);
        if (!isCompound && !isInterIntra)
        {
            for (var i = 0; i < h; i++)
            {
                var destination = samples.Slice(((y + i) * stride) + x, w);
                var source = pred0.Slice(i * Stride, w);
                for (var j = 0; j < w; j++)
                {
                    destination[j] = (ushort)Math.Clamp(source[j], 0, max);
                }
            }
        }
        else if (_compoundType is Av1Constants.CompoundAverage or Av1Constants.CompoundDistance)
        {
            var fwdWeight = 1;
            var bckWeight = 1;
            var bits = 1 + interPostRound;
            if (_compoundType is Av1Constants.CompoundDistance)
            {
                GetDistanceWeights(candIndex, out fwdWeight, out bckWeight);
                bits = 4 + interPostRound;
            }

            var half = (1 << bits) >> 1;
            for (var i = 0; i < h; i++)
            {
                var destination = samples.Slice(((y + i) * stride) + x, w);
                var source0 = pred0.Slice(i * Stride, w);
                var source1 = pred1.Slice(i * Stride, w);
                for (var j = 0; j < w; j++)
                {
                    destination[j] = (ushort)Math.Clamp(((fwdWeight * source0[j]) + (bckWeight * source1[j]) + half) >> bits, 0, max);
                }
            }
        }
        else
        {
            MaskBlend(pred0, pred1, mask, samples, stride, x, y, w, h, subX, subY, interPostRound);
        }

        if (_motionMode is Av1Constants.Obmc)
            OverlappedMotionCompensation(plane, w, h, interRound0, interRound1);
    }

    // The reference plane of a prediction, and the values of the motion vector scaling and block inter prediction processes
    // that depend on the reference frame
    private readonly void GetReferencePlane(int plane, int refFrame, out Av1Plane reference, out int lastX, out int lastY, out int xScale, out int yScale)
    {
        var subX = plane > 0 ? _subX : 0;
        var subY = plane > 0 ? _subY : 0;
        if (_useIntrabc)
        {
            // The prediction of an intra block copy is not cropped to the frame
            reference = _frame.GetPlane(plane);
            lastX = (((_miCols * Av1Constants.MiSize) + subX) >> subX) - 1;
            lastY = (((_miRows * Av1Constants.MiSize) + subY) >> subY) - 1;
            xScale = 1 << Av1Constants.RefScaleShift;
            yScale = 1 << Av1Constants.RefScaleShift;
            return;
        }

        var frame = _references.Get(refFrame);
        reference = frame.GetPlane(plane);
        lastX = ((frame.Header.UpscaledWidth + subX) >> subX) - 1;
        lastY = ((frame.Header.FrameHeight + subY) >> subY) - 1;
        xScale = _references.GetXScale(refFrame);
        yScale = _references.GetYScale(refFrame);
    }

    // Section 7.11.3.3 "Motion vector scaling process"
    private static void ScaleMotionVector(int x, int y, int mvRow, int mvCol, int subX, int subY, int xScale, int yScale, out int startX, out int startY, out int stepX, out int stepY)
    {
        const int HalfSample = 1 << (Av1Constants.SubpelBits - 1);
        const int Shift = Av1Constants.RefScaleShift + Av1Constants.SubpelBits - Av1Constants.ScaleSubpelBits;
        const int Off = (1 << (Av1Constants.ScaleSubpelBits - Av1Constants.SubpelBits)) / 2;
        long origX = (x << Av1Constants.SubpelBits) + ((2 * mvCol) >> subX) + HalfSample;
        long origY = (y << Av1Constants.SubpelBits) + ((2 * mvRow) >> subY) + HalfSample;
        var baseX = (origX * xScale) - (HalfSample << Av1Constants.RefScaleShift);
        var baseY = (origY * yScale) - (HalfSample << Av1Constants.RefScaleShift);
        startX = (int)Round2Signed(baseX, Shift) + Off;
        startY = (int)Round2Signed(baseY, Shift) + Off;
        stepX = (int)Round2Signed(xScale, Av1Constants.RefScaleShift - Av1Constants.ScaleSubpelBits);
        stepY = (int)Round2Signed(yScale, Av1Constants.RefScaleShift - Av1Constants.ScaleSubpelBits);
    }

    // Section 7.11.3.14 "Mask blend process"
    private readonly void MaskBlend(ReadOnlySpan<int> pred0, ReadOnlySpan<int> pred1, ReadOnlySpan<byte> mask, Span<ushort> samples, int stride, int dstX, int dstY, int w, int h, int subX, int subY, int interPostRound)
    {
        const int Stride = Av1InterScratch.Stride;
        var max = (1 << _bitDepth) - 1;
        var isPlaneMask = (subX is 0 && subY is 0) || (_interintra && !_wedgeInterintra);
        var postRoundHalf = (1 << interPostRound) >> 1;
        var bits = 6 + interPostRound;
        var half = (1 << bits) >> 1;
        for (var y = 0; y < h; y++)
        {
            var destination = samples.Slice(((dstY + y) * stride) + dstX, w);
            for (var x = 0; x < w; x++)
            {
                int m;
                if (isPlaneMask)
                {
                    m = mask[(y * Stride) + x];
                }
                else if (subY is 0)
                {
                    m = (mask[(y * Stride) + (2 * x)] + mask[(y * Stride) + (2 * x) + 1] + 1) >> 1;
                }
                else
                {
                    m = (mask[(2 * y * Stride) + (2 * x)] + mask[(2 * y * Stride) + (2 * x) + 1] + mask[(((2 * y) + 1) * Stride) + (2 * x)] + mask[(((2 * y) + 1) * Stride) + (2 * x) + 1] + 2) >> 2;
                }

                if (_interintra)
                {
                    var inter = Math.Clamp((pred0[(y * Stride) + x] + postRoundHalf) >> interPostRound, 0, max);
                    destination[x] = (ushort)(((m * destination[x]) + ((64 - m) * inter) + 32) >> 6);
                }
                else
                {
                    destination[x] = (ushort)Math.Clamp(((m * pred0[(y * Stride) + x]) + ((64 - m) * pred1[(y * Stride) + x]) + half) >> bits, 0, max);
                }
            }
        }
    }

    // Section 7.11.3.15 "Distance weights process"
    private readonly void GetDistanceWeights(int candIndex, out int fwdWeight, out int bckWeight)
    {
        var dist0 = Math.Clamp(Math.Abs(GetRelativeDistance(_header.OrderHints[_refFrames[candIndex * 2]], _header.OrderHint)), 0, Av1Constants.MaxFrameDistance);
        var dist1 = Math.Clamp(Math.Abs(GetRelativeDistance(_header.OrderHints[_refFrames[(candIndex * 2) + 1]], _header.OrderHint)), 0, Av1Constants.MaxFrameDistance);
        var d0 = dist1;
        var d1 = dist0;
        var order = d0 <= d1 ? 1 : 0;
        var i = 3;
        if (d0 is not 0 && d1 is not 0)
        {
            for (i = 0; i < 3; i++)
            {
                int c0 = Av1Tables.QuantDistWeight[(i * 2) + order];
                int c1 = Av1Tables.QuantDistWeight[(i * 2) + 1 - order];
                if (order is 1 ? d0 * c0 > d1 * c1 : d0 * c0 < d1 * c1)
                    break;
            }
        }

        fwdWeight = Av1Tables.QuantDistLookup[(i * 2) + order];
        bckWeight = Av1Tables.QuantDistLookup[(i * 2) + 1 - order];
    }

    // Section 7.11.3.9 "Overlapped motion compensation process"
    private readonly void OverlappedMotionCompensation(int plane, int w, int h, int interRound0, int interRound1)
    {
        var subX = plane > 0 ? _subX : 0;
        var subY = plane > 0 ? _subY : 0;
        if (_availU && GetPlaneResidualSize(_miSize, plane) >= Av1Constants.Block8x8)
        {
            var w4 = Av1Tables.Num4x4BlocksWide[_miSize];
            var x4 = _miCol;
            var nCount = 0;
            var nLimit = Math.Min(4, (int)Av1Tables.MiWidthLog2[_miSize]);
            while (nCount < nLimit && x4 < Math.Min(_miCols, _miCol + w4))
            {
                var candIndex = MiIndex(_miRow - 1, x4 | 1);
                var step4 = Math.Clamp((int)Av1Tables.Num4x4BlocksWide[_miSizes[candIndex]], 2, 16);
                if (_refFrames[candIndex * 2] > Av1Constants.IntraFrame)
                {
                    nCount++;
                    var predW = Math.Min(w, (step4 * Av1Constants.MiSize) >> subX);
                    var predH = Math.Min(h >> 1, 32 >> subY);
                    PredictOverlap(plane, candIndex, x4, _miRow, predW, predH, isLeft: false, interRound0, interRound1);
                }

                x4 += step4;
            }
        }

        if (_availL)
        {
            var h4 = Av1Tables.Num4x4BlocksHigh[_miSize];
            var y4 = _miRow;
            var nCount = 0;
            var nLimit = Math.Min(4, (int)Av1Tables.MiHeightLog2[_miSize]);
            while (nCount < nLimit && y4 < Math.Min(_miRows, _miRow + h4))
            {
                var candIndex = MiIndex(y4 | 1, _miCol - 1);
                var step4 = Math.Clamp((int)Av1Tables.Num4x4BlocksHigh[_miSizes[candIndex]], 2, 16);
                if (_refFrames[candIndex * 2] > Av1Constants.IntraFrame)
                {
                    nCount++;
                    var predW = Math.Min(w >> 1, 32 >> subX);
                    var predH = Math.Min(h, (step4 * Av1Constants.MiSize) >> subY);
                    PredictOverlap(plane, candIndex, _miCol, y4, predW, predH, isLeft: true, interRound0, interRound1);
                }

                y4 += step4;
            }
        }
    }

    // The predict_overlap function of the overlapped motion compensation process, and section 7.11.3.10 "Overlap blending
    // process"
    private readonly void PredictOverlap(int plane, int candIndex, int x4, int y4, int predW, int predH, bool isLeft, int interRound0, int interRound1)
    {
        const int Stride = Av1InterScratch.Stride;
        var scratch = _interScratch!;
        var subX = plane > 0 ? _subX : 0;
        var subY = plane > 0 ? _subY : 0;
        int refFrame = _refFrames[candIndex * 2];
        var predX = (x4 * 4) >> subX;
        var predY = (y4 * 4) >> subY;
        var filters = _interInfo[candIndex];
        var obmcPred = scratch.GetPrediction(1);
        GetReferencePlane(plane, refFrame, out var reference, out var lastX, out var lastY, out var xScale, out var yScale);
        ScaleMotionVector(predX, predY, _mvs[candIndex * 4], _mvs[(candIndex * 4) + 1], subX, subY, xScale, yScale, out var startX, out var startY, out var stepX, out var stepY);
        Av1InterPredictor.PredictBlock(reference.Samples, reference.Stride, lastX, lastY, startX, startY, stepX, stepY, predW, predH, (filters >> 2) & 3, filters & 3, interRound0, interRound1, scratch.Intermediate, obmcPred, Stride);

        var obmcMask = (isLeft ? predW : predH) switch
        {
            2 => Av1Tables.ObmcMask2,
            4 => Av1Tables.ObmcMask4,
            8 => Av1Tables.ObmcMask8,
            16 => Av1Tables.ObmcMask16,
            _ => Av1Tables.ObmcMask32,
        };

        var planeBuffer = _frame.GetPlane(plane);
        var samples = planeBuffer.Samples;
        var stride = planeBuffer.Stride;
        var max = (1 << _bitDepth) - 1;
        for (var i = 0; i < predH; i++)
        {
            var destination = samples.Slice(((predY + i) * stride) + predX, predW);
            var source = obmcPred.Slice(i * Stride, predW);
            for (var j = 0; j < predW; j++)
            {
                int m = obmcMask[isLeft ? j : i];
                destination[j] = (ushort)(((m * destination[j]) + ((64 - m) * Math.Clamp(source[j], 0, max)) + 32) >> 6);
            }
        }
    }
}
