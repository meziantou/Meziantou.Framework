using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>
/// The prediction of the motion vectors of a block from its neighbors and from the motion field of the frame
/// (AV1 specification, sections 7.10.2 "Find MV stack process", 7.10.3 "Has overlappable candidates process" and 7.10.4 "Find
/// warp samples process").
/// </content>
internal ref partial struct Av1TileCoder
{
    // Section 7.10.2 "Find MV stack process"
    private void FindMvStack(bool isCompound)
    {
        var bw4 = Av1Tables.Num4x4BlocksWide[_miSize];
        var bh4 = Av1Tables.Num4x4BlocksHigh[_miSize];
        _numMvFound = 0;
        _newMvCount = 0;
        SetupGlobalMv(0);
        if (isCompound)
            SetupGlobalMv(1);

        _foundMatch = false;
        ScanRow(-1, isCompound);
        var foundAboveMatch = _foundMatch;
        _foundMatch = false;
        ScanCol(-1, isCompound);
        var foundLeftMatch = _foundMatch;
        _foundMatch = false;
        if (Math.Max(bw4, bh4) <= 16)
            ScanPoint(-1, bw4, isCompound);

        foundAboveMatch |= _foundMatch;
        _closeMatches = (foundAboveMatch ? 1 : 0) + (foundLeftMatch ? 1 : 0);
        var numNearest = _numMvFound;
        var numNew = _newMvCount;
        for (var idx = 0; idx < numNearest; idx++)
        {
            _weightStack[idx] += Av1Constants.RefCatLevel;
        }

        _zeroMvContext = 0;
        if (_header.UseRefFrameMvs)
            TemporalScan(isCompound);

        ScanPoint(-1, -1, isCompound);
        foundAboveMatch |= _foundMatch;
        _foundMatch = false;
        ScanRow(-3, isCompound);
        foundAboveMatch |= _foundMatch;
        _foundMatch = false;
        ScanCol(-3, isCompound);
        foundLeftMatch |= _foundMatch;
        _foundMatch = false;
        if (bh4 > 1)
            ScanRow(-5, isCompound);

        foundAboveMatch |= _foundMatch;
        _foundMatch = false;
        if (bw4 > 1)
            ScanCol(-5, isCompound);

        foundLeftMatch |= _foundMatch;
        _totalMatches = (foundAboveMatch ? 1 : 0) + (foundLeftMatch ? 1 : 0);
        SortStack(0, numNearest);
        SortStack(numNearest, _numMvFound);
        if (_numMvFound < 2)
            ExtraSearch(isCompound);

        ContextAndClamping(isCompound, numNew);
    }

    // Section 7.10.2.1 "Setup global MV process"
    private void SetupGlobalMv(int refList)
    {
        var reference = refList is 0 ? _refFrame0 : _refFrame1;
        var typ = reference is not Av1Constants.IntraFrame ? _header.GlobalMotion.Types[reference] : Av1Constants.Identity;
        var mvRow = 0;
        var mvCol = 0;
        if (reference is not Av1Constants.IntraFrame && typ is Av1Constants.Translation)
        {
            mvRow = _header.GlobalMotion.Parameters[reference][0] >> (Av1Constants.WarpedmodelPrecBits - 3);
            mvCol = _header.GlobalMotion.Parameters[reference][1] >> (Av1Constants.WarpedmodelPrecBits - 3);
        }
        else if (reference is not Av1Constants.IntraFrame && typ is not Av1Constants.Identity)
        {
            ref readonly var gm = ref _header.GlobalMotion.Parameters[reference];
            long x = (_miCol * Av1Constants.MiSize) + (BlockWidth(_miSize) / 2) - 1;
            long y = (_miRow * Av1Constants.MiSize) + (BlockHeight(_miSize) / 2) - 1;
            var xc = ((gm[2] - (1 << Av1Constants.WarpedmodelPrecBits)) * x) + (gm[3] * y) + gm[0];
            var yc = (gm[4] * x) + ((gm[5] - (1 << Av1Constants.WarpedmodelPrecBits)) * y) + gm[1];
            if (_header.AllowHighPrecisionMv)
            {
                mvRow = (int)Round2Signed(yc, Av1Constants.WarpedmodelPrecBits - 3);
                mvCol = (int)Round2Signed(xc, Av1Constants.WarpedmodelPrecBits - 3);
            }
            else
            {
                mvRow = (int)Round2Signed(yc, Av1Constants.WarpedmodelPrecBits - 2) * 2;
                mvCol = (int)Round2Signed(xc, Av1Constants.WarpedmodelPrecBits - 2) * 2;
            }
        }

        LowerMvPrecision(ref mvRow, ref mvCol);
        _globalMvs[refList * 2] = mvRow;
        _globalMvs[(refList * 2) + 1] = mvCol;
    }

    private static long Round2Signed(long value, int bits) => value >= 0 ? (value + (1L << (bits - 1))) >> bits : -((-value + (1L << (bits - 1))) >> bits);

    // Section 7.10.2.2 "Scan row process"
    private void ScanRow(int deltaRow, bool isCompound)
    {
        int bw4 = Av1Tables.Num4x4BlocksWide[_miSize];
        var end4 = Math.Min(Math.Min(bw4, _miCols - _miCol), 16);
        var deltaCol = 0;
        var useStep16 = bw4 >= 16;
        var far = Math.Abs(deltaRow) > 1;
        if (far)
        {
            deltaRow += _miRow & 1;
            deltaCol = 1 - (_miCol & 1);
        }

        var i = 0;
        while (i < end4)
        {
            var mvRow = _miRow + deltaRow;
            var mvCol = _miCol + deltaCol + i;
            if (!IsInside(mvRow, mvCol))
                break;

            var len = Math.Min(bw4, Av1Tables.Num4x4BlocksWide[_miSizes[MiIndex(mvRow, mvCol)]]);
            if (far)
                len = Math.Max(2, len);

            if (useStep16)
                len = Math.Max(4, len);

            AddRefMvCandidate(mvRow, mvCol, isCompound, len * 2);
            i += len;
        }
    }

    // Section 7.10.2.3 "Scan col process"
    private void ScanCol(int deltaCol, bool isCompound)
    {
        int bh4 = Av1Tables.Num4x4BlocksHigh[_miSize];
        var end4 = Math.Min(Math.Min(bh4, _miRows - _miRow), 16);
        var deltaRow = 0;
        var useStep16 = bh4 >= 16;
        var far = Math.Abs(deltaCol) > 1;
        if (far)
        {
            deltaRow = 1 - (_miRow & 1);
            deltaCol += _miCol & 1;
        }

        var i = 0;
        while (i < end4)
        {
            var mvRow = _miRow + deltaRow + i;
            var mvCol = _miCol + deltaCol;
            if (!IsInside(mvRow, mvCol))
                break;

            var len = Math.Min(bh4, Av1Tables.Num4x4BlocksHigh[_miSizes[MiIndex(mvRow, mvCol)]]);
            if (far)
                len = Math.Max(2, len);

            if (useStep16)
                len = Math.Max(4, len);

            AddRefMvCandidate(mvRow, mvCol, isCompound, len * 2);
            i += len;
        }
    }

    // Section 7.10.2.4 "Scan point process"
    private void ScanPoint(int deltaRow, int deltaCol, bool isCompound)
    {
        var mvRow = _miRow + deltaRow;
        var mvCol = _miCol + deltaCol;
        if (IsInside(mvRow, mvCol) && _refFrames[MiIndex(mvRow, mvCol) * 2] is not Av1FrameState.RefFrameNotWritten)
            AddRefMvCandidate(mvRow, mvCol, isCompound, weight: 4);
    }

    // Section 7.10.2.5 "Temporal scan process"
    private void TemporalScan(bool isCompound)
    {
        int bw4 = Av1Tables.Num4x4BlocksWide[_miSize];
        int bh4 = Av1Tables.Num4x4BlocksHigh[_miSize];
        var stepW4 = bw4 >= 16 ? 4 : 2;
        var stepH4 = bh4 >= 16 ? 4 : 2;
        for (var deltaRow = 0; deltaRow < Math.Min(bh4, 16); deltaRow += stepH4)
        {
            for (var deltaCol = 0; deltaCol < Math.Min(bw4, 16); deltaCol += stepW4)
            {
                AddTplRefMv(deltaRow, deltaCol, isCompound);
            }
        }

        var allowExtension = bh4 is >= 2 and < 16 && bw4 is >= 2 and < 16;
        if (!allowExtension)
            return;

        if (CheckSbBorder(bh4, -2))
            AddTplRefMv(bh4, -2, isCompound);

        if (CheckSbBorder(bh4, bw4))
            AddTplRefMv(bh4, bw4, isCompound);

        if (CheckSbBorder(bh4 - 2, bw4))
            AddTplRefMv(bh4 - 2, bw4, isCompound);
    }

    private readonly bool CheckSbBorder(int deltaRow, int deltaCol)
    {
        var row = (_miRow & 15) + deltaRow;
        var col = (_miCol & 15) + deltaCol;
        return row is >= 0 and < 16 && col is >= 0 and < 16;
    }

    // Section 7.10.2.6 "Temporal sample process"
    private void AddTplRefMv(int deltaRow, int deltaCol, bool isCompound)
    {
        var mvRow = (_miRow + deltaRow) | 1;
        var mvCol = (_miCol + deltaCol) | 1;
        if (!IsInside(mvRow, mvCol))
            return;

        var x8 = mvCol >> 1;
        var y8 = mvRow >> 1;
        _scratch.Tools |= Av1Tools.TemporalMotionVector;
        var isOrigin = deltaRow is 0 && deltaCol is 0;
        if (isOrigin)
            _zeroMvContext = 1;

        var fieldStride = _miCols >> 1;
        var fieldLength = fieldStride * (_miRows >> 1);
        var position = ((y8 * fieldStride) + x8) * 2;
        var field0 = _motionFieldMvs.Slice((_refFrame0 - Av1Constants.LastFrame) * fieldLength * 2, fieldLength * 2);
        int cand0Row = field0[position];
        int cand0Col = field0[position + 1];
        if (cand0Row is short.MinValue)
            return;

        if (!isCompound)
        {
            LowerMvPrecision(ref cand0Row, ref cand0Col);
            if (isOrigin)
                _zeroMvContext = Math.Abs(cand0Row - _globalMvs[0]) >= 16 || Math.Abs(cand0Col - _globalMvs[1]) >= 16 ? 1 : 0;

            for (var idx = 0; idx < _numMvFound; idx++)
            {
                if (cand0Row == _refStackMv[idx][0] && cand0Col == _refStackMv[idx][1])
                {
                    _weightStack[idx] += 2;
                    return;
                }
            }

            if (_numMvFound < Av1Constants.MaxRefMvStackSize)
            {
                _refStackMv[_numMvFound][0] = cand0Row;
                _refStackMv[_numMvFound][1] = cand0Col;
                _weightStack[_numMvFound] = 2;
                _numMvFound++;
            }

            return;
        }

        var field1 = _motionFieldMvs.Slice((_refFrame1 - Av1Constants.LastFrame) * fieldLength * 2, fieldLength * 2);
        int cand1Row = field1[position];
        int cand1Col = field1[position + 1];
        if (cand1Row is short.MinValue)
            return;

        LowerMvPrecision(ref cand0Row, ref cand0Col);
        LowerMvPrecision(ref cand1Row, ref cand1Col);
        if (isOrigin)
        {
            var far = Math.Abs(cand0Row - _globalMvs[0]) >= 16
                || Math.Abs(cand0Col - _globalMvs[1]) >= 16
                || Math.Abs(cand1Row - _globalMvs[2]) >= 16
                || Math.Abs(cand1Col - _globalMvs[3]) >= 16;
            _zeroMvContext = far ? 1 : 0;
        }

        AddToStack(cand0Row, cand0Col, cand1Row, cand1Col, isCompound: true, weight: 2);
    }

    // Adds a candidate to RefStackMv, or adds its weight to the one of the entry that holds the same motion vectors
    private void AddToStack(int row0, int col0, int row1, int col1, bool isCompound, int weight)
    {
        for (var idx = 0; idx < _numMvFound; idx++)
        {
            ref var entry = ref _refStackMv[idx];
            if (row0 == entry[0] && col0 == entry[1] && (!isCompound || (row1 == entry[2] && col1 == entry[3])))
            {
                _weightStack[idx] += weight;
                return;
            }
        }

        if (_numMvFound < Av1Constants.MaxRefMvStackSize)
        {
            ref var entry = ref _refStackMv[_numMvFound];
            entry[0] = row0;
            entry[1] = col0;
            entry[2] = row1;
            entry[3] = col1;
            _weightStack[_numMvFound] = weight;
            _numMvFound++;
        }
    }

    // Section 7.10.2.7 "Add reference motion vector process"
    private void AddRefMvCandidate(int mvRow, int mvCol, bool isCompound, int weight)
    {
        var index = MiIndex(mvRow, mvCol);
        if ((_flags[index] & Av1FrameState.IsInterFlag) is 0)
            return;

        if (!isCompound)
        {
            for (var candList = 0; candList < 2; candList++)
            {
                if (_refFrames[(index * 2) + candList] == _refFrame0)
                    SearchStack(index, candList, weight);
            }
        }
        else if (_refFrames[index * 2] == _refFrame0 && _refFrames[(index * 2) + 1] == _refFrame1)
        {
            CompoundSearchStack(index, weight);
        }
    }

    // Section 7.10.2.8 "Search stack process"
    private void SearchStack(int index, int candList, int weight)
    {
        int candMode = _yModes[index];
        int candSize = _miSizes[index];
        var large = Math.Min(BlockWidth(candSize), BlockHeight(candSize)) >= 8;
        int candRow;
        int candCol;
        if (candMode is Av1Constants.Globalmv or Av1Constants.GlobalGlobalmv && _header.GlobalMotion.Types[_refFrame0] > Av1Constants.Translation && large)
        {
            candRow = _globalMvs[0];
            candCol = _globalMvs[1];
        }
        else
        {
            candRow = _mvs[(index * 4) + (candList * 2)];
            candCol = _mvs[(index * 4) + (candList * 2) + 1];
        }

        LowerMvPrecision(ref candRow, ref candCol);
        if (HasNewmv(candMode))
            _newMvCount++;

        _foundMatch = true;
        AddToStack(candRow, candCol, 0, 0, isCompound: false, weight);
    }

    // Section 7.10.2.9 "Compound search stack process"
    private void CompoundSearchStack(int index, int weight)
    {
        int cand0Row = _mvs[index * 4];
        int cand0Col = _mvs[(index * 4) + 1];
        int cand1Row = _mvs[(index * 4) + 2];
        int cand1Col = _mvs[(index * 4) + 3];
        int candMode = _yModes[index];
        if (candMode is Av1Constants.GlobalGlobalmv)
        {
            if (_header.GlobalMotion.Types[_refFrame0] > Av1Constants.Translation)
            {
                cand0Row = _globalMvs[0];
                cand0Col = _globalMvs[1];
            }

            if (_header.GlobalMotion.Types[_refFrame1] > Av1Constants.Translation)
            {
                cand1Row = _globalMvs[2];
                cand1Col = _globalMvs[3];
            }
        }

        LowerMvPrecision(ref cand0Row, ref cand0Col);
        LowerMvPrecision(ref cand1Row, ref cand1Col);
        _foundMatch = true;
        AddToStack(cand0Row, cand0Col, cand1Row, cand1Col, isCompound: true, weight);
        if (HasNewmv(candMode))
            _newMvCount++;
    }

    private static bool HasNewmv(int mode)
    {
        return mode is Av1Constants.Newmv
            or Av1Constants.NewNewmv
            or Av1Constants.NearNewmv
            or Av1Constants.NewNearmv
            or Av1Constants.NearestNewmv
            or Av1Constants.NewNearestmv;
    }

    // Section 7.10.2.10 "Lower precision process"
    private readonly void LowerMvPrecision(ref int row, ref int col)
    {
        if (_header.AllowHighPrecisionMv)
            return;

        row = Lower(row, _header.ForceIntegerMv);
        col = Lower(col, _header.ForceIntegerMv);

        static int Lower(int value, bool forceIntegerMv)
        {
            if (forceIntegerMv)
            {
                var aInt = (Math.Abs(value) + 3) >> 3;
                return value > 0 ? aInt << 3 : -(aInt << 3);
            }

            if ((value & 1) is not 0)
                return value > 0 ? value - 1 : value + 1;

            return value;
        }
    }

    // Section 7.10.2.11 "Sorting process"
    private void SortStack(int start, int end)
    {
        while (end > start)
        {
            var newEnd = start;
            for (var idx = start + 1; idx < end; idx++)
            {
                if (_weightStack[idx - 1] < _weightStack[idx])
                {
                    (_weightStack[idx - 1], _weightStack[idx]) = (_weightStack[idx], _weightStack[idx - 1]);
                    (_refStackMv[idx - 1], _refStackMv[idx]) = (_refStackMv[idx], _refStackMv[idx - 1]);
                    newEnd = idx;
                }
            }

            end = newEnd;
        }
    }

    // Section 7.10.2.12 "Extra search process"
    private void ExtraSearch(bool isCompound)
    {
        // RefIdMvs and RefDiffMvs: for each list, 2 motion vectors
        Span<int> refIdMvs = stackalloc int[8];
        Span<int> refDiffMvs = stackalloc int[8];
        Span<int> refIdCount = stackalloc int[2];
        Span<int> refDiffCount = stackalloc int[2];
        var w4 = Math.Min(Math.Min(16, (int)Av1Tables.Num4x4BlocksWide[_miSize]), _miCols - _miCol);
        var h4 = Math.Min(Math.Min(16, (int)Av1Tables.Num4x4BlocksHigh[_miSize]), _miRows - _miRow);
        var num4x4 = Math.Min(w4, h4);
        for (var pass = 0; pass < 2; pass++)
        {
            var idx = 0;
            while (idx < num4x4 && _numMvFound < 2)
            {
                var mvRow = pass is 0 ? _miRow - 1 : _miRow + idx;
                var mvCol = pass is 0 ? _miCol + idx : _miCol - 1;
                if (!IsInside(mvRow, mvCol))
                    break;

                var index = MiIndex(mvRow, mvCol);
                AddExtraMvCandidate(index, isCompound, refIdMvs, refDiffMvs, refIdCount, refDiffCount);
                idx += pass is 0 ? Av1Tables.Num4x4BlocksWide[_miSizes[index]] : Av1Tables.Num4x4BlocksHigh[_miSizes[index]];
            }
        }

        if (!isCompound)
        {
            for (var idx = _numMvFound; idx < 2; idx++)
            {
                _refStackMv[idx][0] = _globalMvs[0];
                _refStackMv[idx][1] = _globalMvs[1];
            }

            return;
        }

        // combinedMvs[ idx ][ list ]
        Span<int> combinedMvs = stackalloc int[8];
        for (var list = 0; list < 2; list++)
        {
            var compCount = 0;
            for (var idx = 0; idx < refIdCount[list]; idx++)
            {
                combinedMvs[(((compCount * 2) + list) * 2) + 0] = refIdMvs[(((list * 2) + idx) * 2) + 0];
                combinedMvs[(((compCount * 2) + list) * 2) + 1] = refIdMvs[(((list * 2) + idx) * 2) + 1];
                compCount++;
            }

            for (var idx = 0; idx < refDiffCount[list] && compCount < 2; idx++)
            {
                combinedMvs[(((compCount * 2) + list) * 2) + 0] = refDiffMvs[(((list * 2) + idx) * 2) + 0];
                combinedMvs[(((compCount * 2) + list) * 2) + 1] = refDiffMvs[(((list * 2) + idx) * 2) + 1];
                compCount++;
            }

            while (compCount < 2)
            {
                combinedMvs[(((compCount * 2) + list) * 2) + 0] = _globalMvs[list * 2];
                combinedMvs[(((compCount * 2) + list) * 2) + 1] = _globalMvs[(list * 2) + 1];
                compCount++;
            }
        }

        if (_numMvFound is 1)
        {
            var isSame = combinedMvs[0] == _refStackMv[0][0]
                && combinedMvs[1] == _refStackMv[0][1]
                && combinedMvs[2] == _refStackMv[0][2]
                && combinedMvs[3] == _refStackMv[0][3];
            var source = isSame ? 4 : 0;
            for (var i = 0; i < 4; i++)
            {
                _refStackMv[1][i] = combinedMvs[source + i];
            }

            _weightStack[1] = 2;
            _numMvFound = 2;
        }
        else
        {
            for (var idx = 0; idx < 2; idx++)
            {
                for (var i = 0; i < 4; i++)
                {
                    _refStackMv[_numMvFound][i] = combinedMvs[(idx * 4) + i];
                }

                _weightStack[_numMvFound] = 2;
                _numMvFound++;
            }
        }
    }

    // Section 7.10.2.13 "Add extra MV candidate process"
    private void AddExtraMvCandidate(int index, bool isCompound, scoped Span<int> refIdMvs, scoped Span<int> refDiffMvs, scoped Span<int> refIdCount, scoped Span<int> refDiffCount)
    {
        for (var candList = 0; candList < 2; candList++)
        {
            int candRef = _refFrames[(index * 2) + candList];
            if (candRef <= Av1Constants.IntraFrame)
                continue;

            if (isCompound)
            {
                for (var list = 0; list < 2; list++)
                {
                    int candRow = _mvs[(index * 4) + (candList * 2)];
                    int candCol = _mvs[(index * 4) + (candList * 2) + 1];
                    var reference = list is 0 ? _refFrame0 : _refFrame1;
                    if (candRef == reference && refIdCount[list] < 2)
                    {
                        refIdMvs[(((list * 2) + refIdCount[list]) * 2) + 0] = candRow;
                        refIdMvs[(((list * 2) + refIdCount[list]) * 2) + 1] = candCol;
                        refIdCount[list]++;
                    }
                    else if (refDiffCount[list] < 2)
                    {
                        if (_header.RefFrameSignBias[candRef] != _header.RefFrameSignBias[reference])
                        {
                            candRow = -candRow;
                            candCol = -candCol;
                        }

                        refDiffMvs[(((list * 2) + refDiffCount[list]) * 2) + 0] = candRow;
                        refDiffMvs[(((list * 2) + refDiffCount[list]) * 2) + 1] = candCol;
                        refDiffCount[list]++;
                    }
                }
            }
            else
            {
                int candRow = _mvs[(index * 4) + (candList * 2)];
                int candCol = _mvs[(index * 4) + (candList * 2) + 1];
                if (_header.RefFrameSignBias[candRef] != _header.RefFrameSignBias[_refFrame0])
                {
                    candRow = -candRow;
                    candCol = -candCol;
                }

                var idx = 0;
                for (; idx < _numMvFound; idx++)
                {
                    if (candRow == _refStackMv[idx][0] && candCol == _refStackMv[idx][1])
                        break;
                }

                if (idx == _numMvFound)
                {
                    _refStackMv[idx][0] = candRow;
                    _refStackMv[idx][1] = candCol;
                    _weightStack[idx] = 2;
                    _numMvFound++;
                }
            }
        }
    }

    // Section 7.10.2.14 "Context and clamping process"
    private void ContextAndClamping(bool isCompound, int numNew)
    {
        var bw = BlockWidth(_miSize);
        var bh = BlockHeight(_miSize);
        for (var idx = 0; idx < _numMvFound; idx++)
        {
            var z = 0;
            if (idx + 1 < _numMvFound)
            {
                var w0 = _weightStack[idx];
                var w1 = _weightStack[idx + 1];
                if (w0 >= Av1Constants.RefCatLevel)
                {
                    if (w1 < Av1Constants.RefCatLevel)
                        z = 1;
                }
                else
                {
                    z = 2;
                }
            }

            _drlCtxStack[idx] = z;
        }

        for (var list = 0; list < (isCompound ? 2 : 1); list++)
        {
            for (var idx = 0; idx < _numMvFound; idx++)
            {
                ref var entry = ref _refStackMv[idx];
                entry[list * 2] = ClampMvRow(entry[list * 2], Av1Constants.MvBorder + (bh * 8));
                entry[(list * 2) + 1] = ClampMvCol(entry[(list * 2) + 1], Av1Constants.MvBorder + (bw * 8));
            }
        }

        if (_closeMatches is 0)
        {
            _newMvContext = Math.Min(_totalMatches, 1);
            _refMvContext = _totalMatches;
        }
        else if (_closeMatches is 1)
        {
            _newMvContext = 3 - Math.Min(numNew, 1);
            _refMvContext = 2 + _totalMatches;
        }
        else
        {
            _newMvContext = 5 - Math.Min(numNew, 1);
            _refMvContext = 5;
        }
    }

    // Section 5.11.53 "Clamp MV row function"
    private readonly int ClampMvRow(int mvec, int border)
    {
        var bh4 = Av1Tables.Num4x4BlocksHigh[_miSize];
        var mbToTopEdge = -(_miRow * Av1Constants.MiSize * 8);
        var mbToBottomEdge = (_miRows - bh4 - _miRow) * Av1Constants.MiSize * 8;
        return Math.Clamp(mvec, mbToTopEdge - border, mbToBottomEdge + border);
    }

    // Section 5.11.54 "Clamp MV col function"
    private readonly int ClampMvCol(int mvec, int border)
    {
        var bw4 = Av1Tables.Num4x4BlocksWide[_miSize];
        var mbToLeftEdge = -(_miCol * Av1Constants.MiSize * 8);
        var mbToRightEdge = (_miCols - bw4 - _miCol) * Av1Constants.MiSize * 8;
        return Math.Clamp(mvec, mbToLeftEdge - border, mbToRightEdge + border);
    }

    // Section 7.10.3 "Has overlappable candidates process"
    private readonly bool HasOverlappableCandidates()
    {
        if (_availU)
        {
            var w4 = Av1Tables.Num4x4BlocksWide[_miSize];
            for (var x4 = _miCol; x4 < Math.Min(_miCols, _miCol + w4); x4 += 2)
            {
                if (_refFrames[MiIndex(_miRow - 1, x4 | 1) * 2] > Av1Constants.IntraFrame)
                    return true;
            }
        }

        if (_availL)
        {
            var h4 = Av1Tables.Num4x4BlocksHigh[_miSize];
            for (var y4 = _miRow; y4 < Math.Min(_miRows, _miRow + h4); y4 += 2)
            {
                if (_refFrames[MiIndex(y4 | 1, _miCol - 1) * 2] > Av1Constants.IntraFrame)
                    return true;
            }
        }

        return false;
    }

    // Section 7.10.4.1 "Find warp samples process"
    private void FindWarpSamples()
    {
        _numSamples = 0;
        _numSamplesScanned = 0;
        var w4 = Av1Tables.Num4x4BlocksWide[_miSize];
        var h4 = Av1Tables.Num4x4BlocksHigh[_miSize];
        var doTopLeft = true;
        var doTopRight = true;
        if (_availU)
        {
            int srcW = Av1Tables.Num4x4BlocksWide[_miSizes[MiIndex(_miRow - 1, _miCol)]];
            if (w4 <= srcW)
            {
                var colOffset = -(_miCol & (srcW - 1));
                if (colOffset < 0)
                    doTopLeft = false;

                if (colOffset + srcW > w4)
                    doTopRight = false;

                AddSample(-1, 0);
            }
            else
            {
                int miStep;
                for (var i = 0; i < Math.Min(w4, _miCols - _miCol); i += miStep)
                {
                    srcW = Av1Tables.Num4x4BlocksWide[_miSizes[MiIndex(_miRow - 1, _miCol + i)]];
                    miStep = Math.Min(w4, srcW);
                    AddSample(-1, i);
                }
            }
        }

        if (_availL)
        {
            int srcH = Av1Tables.Num4x4BlocksHigh[_miSizes[MiIndex(_miRow, _miCol - 1)]];
            if (h4 <= srcH)
            {
                var rowOffset = -(_miRow & (srcH - 1));
                if (rowOffset < 0)
                    doTopLeft = false;

                AddSample(0, -1);
            }
            else
            {
                int miStep;
                for (var i = 0; i < Math.Min(h4, _miRows - _miRow); i += miStep)
                {
                    srcH = Av1Tables.Num4x4BlocksHigh[_miSizes[MiIndex(_miRow + i, _miCol - 1)]];
                    miStep = Math.Min(h4, srcH);
                    AddSample(i, -1);
                }
            }
        }

        if (doTopLeft)
            AddSample(-1, -1);

        if (doTopRight && Math.Max(w4, h4) <= 16)
            AddSample(-1, w4);

        if (_numSamples is 0 && _numSamplesScanned > 0)
            _numSamples = 1;
    }

    // Section 7.10.4.2 "Add sample process"
    private void AddSample(int deltaRow, int deltaCol)
    {
        if (_numSamplesScanned >= Av1Constants.LeastSquaresSamplesMax)
            return;

        var mvRow = _miRow + deltaRow;
        var mvCol = _miCol + deltaCol;
        if (!IsInside(mvRow, mvCol))
            return;

        // A reference frame that is not written is not a reference frame of the block either
        var index = MiIndex(mvRow, mvCol);
        if (_refFrames[index * 2] != _refFrame0 || _refFrames[(index * 2) + 1] is not NoReference)
            return;

        int candSz = _miSizes[index];
        int candW4 = Av1Tables.Num4x4BlocksWide[candSz];
        int candH4 = Av1Tables.Num4x4BlocksHigh[candSz];
        var candRow = mvRow & ~(candH4 - 1);
        var candCol = mvCol & ~(candW4 - 1);
        var midY = (candRow * 4) + (candH4 * 2) - 1;
        var midX = (candCol * 4) + (candW4 * 2) - 1;
        var threshold = Math.Clamp(Math.Max(BlockWidth(_miSize), BlockHeight(_miSize)), 16, 112);
        var candIndex = MiIndex(candRow, candCol) * 4;
        int candMvRow = _mvs[candIndex];
        int candMvCol = _mvs[candIndex + 1];
        var mvDiffRow = Math.Abs(candMvRow - _mv[0]);
        var mvDiffCol = Math.Abs(candMvCol - _mv[1]);
        var valid = mvDiffRow + mvDiffCol <= threshold;
        _numSamplesScanned++;
        if (!valid && _numSamplesScanned > 1)
            return;

        ref var cand = ref _candList[_numSamples];
        cand[0] = midY * 8;
        cand[1] = midX * 8;
        cand[2] = (midY * 8) + candMvRow;
        cand[3] = (midX * 8) + candMvCol;
        if (valid)
            _numSamples++;
    }
}
