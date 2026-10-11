using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>
/// The mode info of a block that is predicted from another frame, or from the frame itself with intra block copy
/// (AV1 specification, sections 5.11.10, 5.11.17 to 5.11.23 and 5.11.25 to 5.11.33).
/// </content>
internal ref partial struct Av1TileCoder
{
    /// <summary>The value of <c>RefFrame[ 1 ]</c> for a block with one reference (<c>NONE</c>).</summary>
    private const int NoReference = -1;

    // Section 5.11.18 "Inter frame mode info syntax"
    private void InterFrameModeInfo()
    {
        _useIntrabc = false;
        _leftRefFrame0 = _availL ? _refFrames[MiIndex(_miRow, _miCol - 1) * 2] : Av1Constants.IntraFrame;
        _aboveRefFrame0 = _availU ? _refFrames[MiIndex(_miRow - 1, _miCol) * 2] : Av1Constants.IntraFrame;
        _leftRefFrame1 = _availL ? _refFrames[(MiIndex(_miRow, _miCol - 1) * 2) + 1] : NoReference;
        _aboveRefFrame1 = _availU ? _refFrames[(MiIndex(_miRow - 1, _miCol) * 2) + 1] : NoReference;
        _leftIntra = _leftRefFrame0 <= Av1Constants.IntraFrame;
        _aboveIntra = _aboveRefFrame0 <= Av1Constants.IntraFrame;
        _leftSingle = _leftRefFrame1 <= Av1Constants.IntraFrame;
        _aboveSingle = _aboveRefFrame1 <= Av1Constants.IntraFrame;
        _skip = false;
        InterSegmentId(preSkip: true);
        ReadSkipMode();
        if (_skipMode)
        {
            _skip = true;
        }
        else
        {
            ReadSkip();
        }

        if (!_header.SegIdPreSkip)
            InterSegmentId(preSkip: false);

        _lossless = _header.LosslessArray[_segmentId];
        ReadCdef();
        ReadDeltaQIndex();
        ReadDeltaLf();
        _readDeltas = false;
        ReadIsInter();
        if (_isInter)
        {
            InterBlockModeInfo();
        }
        else
        {
            IntraBlockModeInfo();
        }
    }

    // Section 5.11.19 "Inter segment ID syntax"
    private void InterSegmentId(bool preSkip)
    {
        if (!_header.SegmentationEnabled)
        {
            _segmentId = 0;
            return;
        }

        var predictedSegmentId = GetSegmentId();
        if (!_header.SegmentationUpdateMap)
        {
            _segmentId = predictedSegmentId;
            return;
        }

        if (preSkip && !_header.SegIdPreSkip)
        {
            _segmentId = 0;
            return;
        }

        if (!preSkip && _skip)
        {
            SetSegPredContext(0);
            ReadSegmentId();
            return;
        }

        if (_header.SegmentationTemporalUpdate)
        {
            var ctx = _scratch.LeftSegPredContext[_miRow & 31] + _state.AboveSegPredContext[_miCol];
            var segIdPredicted = ReadSymbol(Av1CdfLayout.SegmentIdPredicted + (ctx * Av1CdfLayout.SegmentIdPredictedSize), Av1CdfLayout.SegmentIdPredictedSize);
            if (segIdPredicted is not 0)
            {
                _state.Tools |= Av1Tools.PredictedSegmentId;
                _segmentId = predictedSegmentId;
            }
            else
            {
                ReadSegmentId();
            }

            SetSegPredContext((byte)segIdPredicted);
        }
        else
        {
            ReadSegmentId();
        }
    }

    private readonly void SetSegPredContext(byte value)
    {
        _state.AboveSegPredContext.Slice(_miCol, Av1Tables.Num4x4BlocksWide[_miSize]).Fill(value);
        var left = _scratch.LeftSegPredContext;
        for (var i = 0; i < Av1Tables.Num4x4BlocksHigh[_miSize]; i++)
        {
            left[(_miRow + i) & 31] = value;
        }
    }

    // Section 5.11.21 "Get segment ID function"
    private readonly int GetSegmentId()
    {
        if (_prevSegmentIds.IsEmpty)
            return 0;

        var xMis = Math.Min(_miCols - _miCol, Av1Tables.Num4x4BlocksWide[_miSize]);
        var yMis = Math.Min(_miRows - _miRow, Av1Tables.Num4x4BlocksHigh[_miSize]);
        var seg = 7;
        for (var y = 0; y < yMis; y++)
        {
            foreach (var value in _prevSegmentIds.Slice(MiIndex(_miRow + y, _miCol), xMis))
            {
                seg = Math.Min(seg, value);
            }
        }

        return seg;
    }

    // Section 5.11.10 "Skip mode syntax"
    private void ReadSkipMode()
    {
        if (IsSegmentationFeatureActive(Av1Constants.SegLvlSkip)
            || IsSegmentationFeatureActive(Av1Constants.SegLvlRefFrame)
            || IsSegmentationFeatureActive(Av1Constants.SegLvlGlobalmv)
            || !_header.SkipModePresent
            || BlockWidth(_miSize) < 8
            || BlockHeight(_miSize) < 8)
        {
            _skipMode = false;
            return;
        }

        var ctx = 0;
        if (_availU && (_flags[MiIndex(_miRow - 1, _miCol)] & Av1FrameState.SkipModeFlag) is not 0)
            ctx++;

        if (_availL && (_flags[MiIndex(_miRow, _miCol - 1)] & Av1FrameState.SkipModeFlag) is not 0)
            ctx++;

        _skipMode = ReadSymbol(Av1CdfLayout.SkipMode + (ctx * Av1CdfLayout.SkipModeSize), Av1CdfLayout.SkipModeSize) is not 0;
    }

    // Section 5.11.20 "Is inter syntax"
    private void ReadIsInter()
    {
        if (_skipMode)
        {
            _isInter = true;
        }
        else if (IsSegmentationFeatureActive(Av1Constants.SegLvlRefFrame))
        {
            _isInter = _header.Segmentation.GetData(_segmentId, Av1Constants.SegLvlRefFrame) is not Av1Constants.IntraFrame;
        }
        else if (IsSegmentationFeatureActive(Av1Constants.SegLvlGlobalmv))
        {
            _isInter = true;
        }
        else
        {
            int ctx;
            if (_availU && _availL)
            {
                if (_leftIntra && _aboveIntra)
                {
                    ctx = 3;
                }
                else
                {
                    ctx = _leftIntra || _aboveIntra ? 1 : 0;
                }
            }
            else if (_availU || _availL)
            {
                ctx = 2 * ((_availU ? _aboveIntra : _leftIntra) ? 1 : 0);
            }
            else
            {
                ctx = 0;
            }

            _isInter = ReadSymbol(Av1CdfLayout.IsInter + (ctx * Av1CdfLayout.IsInterSize), Av1CdfLayout.IsInterSize) is not 0;
        }
    }

    // Section 5.11.22 "Intra block mode info syntax"
    private void IntraBlockModeInfo()
    {
        _refFrame0 = Av1Constants.IntraFrame;
        _refFrame1 = NoReference;
        _interintra = false;
        _motionMode = Av1Constants.Simple;
        _yMode = ReadSymbol(Av1CdfLayout.YMode + (Av1Tables.SizeGroup[_miSize] * Av1CdfLayout.YModeSize), Av1CdfLayout.YModeSize);
        ReadIntraModeDetails();
    }

    // Section 5.11.23 "Inter block mode info syntax"
    private void InterBlockModeInfo()
    {
        _paletteSizeY = 0;
        _paletteSizeUv = 0;
        _useFilterIntra = false;
        _angleDeltaY = 0;
        _angleDeltaUv = 0;
        ReadRefFrames();
        var isCompound = _refFrame1 > Av1Constants.IntraFrame;
        FindMvStack(isCompound);
        if (_skipMode)
        {
            _yMode = Av1Constants.NearestNearestmv;
        }
        else if (IsSegmentationFeatureActive(Av1Constants.SegLvlSkip) || IsSegmentationFeatureActive(Av1Constants.SegLvlGlobalmv))
        {
            _yMode = Av1Constants.Globalmv;
        }
        else if (isCompound)
        {
            var ctx = Av1Tables.CompoundModeCtxMap[((_refMvContext >> 1) * Av1Constants.CompNewmvCtxs) + Math.Min(_newMvContext, Av1Constants.CompNewmvCtxs - 1)];
            _yMode = Av1Constants.NearestNearestmv + ReadSymbol(Av1CdfLayout.CompoundMode + (ctx * Av1CdfLayout.CompoundModeSize), Av1CdfLayout.CompoundModeSize);
        }
        else if (ReadSymbol(Av1CdfLayout.NewMv + (_newMvContext * Av1CdfLayout.NewMvSize), Av1CdfLayout.NewMvSize) is 0)
        {
            _yMode = Av1Constants.Newmv;
        }
        else if (ReadSymbol(Av1CdfLayout.ZeroMv + (_zeroMvContext * Av1CdfLayout.ZeroMvSize), Av1CdfLayout.ZeroMvSize) is 0)
        {
            _yMode = Av1Constants.Globalmv;
        }
        else
        {
            _yMode = ReadSymbol(Av1CdfLayout.RefMv + (_refMvContext * Av1CdfLayout.RefMvSize), Av1CdfLayout.RefMvSize) is 0 ? Av1Constants.Nearestmv : Av1Constants.Nearmv;
        }

        _refMvIdx = 0;
        if (_yMode is Av1Constants.Newmv or Av1Constants.NewNewmv)
        {
            ReadDrlMode(firstIndex: 0);
        }
        else if (_yMode is Av1Constants.Nearmv or Av1Constants.NearNearmv or Av1Constants.NearNewmv or Av1Constants.NewNearmv)
        {
            _refMvIdx = 1;
            ReadDrlMode(firstIndex: 1);
        }

        AssignMv(isCompound);
        ReadInterIntraMode(isCompound);
        ReadMotionMode(isCompound);
        ReadCompoundType(isCompound);
        if (_header.InterpolationFilter is Av1Constants.Switchable)
        {
            var needsInterpFilter = NeedsInterpFilter();
            _interpFilter0 = needsInterpFilter ? ReadInterpFilter(dir: 0) : Av1Constants.Eighttap;
            if (_sequence.EnableDualFilter)
            {
                _interpFilter1 = needsInterpFilter ? ReadInterpFilter(dir: 1) : Av1Constants.Eighttap;
            }
            else
            {
                _interpFilter1 = _interpFilter0;
            }
        }
        else
        {
            _interpFilter0 = _header.InterpolationFilter;
            _interpFilter1 = _header.InterpolationFilter;
        }
    }

    // The drl_mode syntax elements, which select RefMvIdx among two candidates from firstIndex
    private void ReadDrlMode(int firstIndex)
    {
        for (var idx = firstIndex; idx < firstIndex + 2; idx++)
        {
            if (_numMvFound <= idx + 1)
                continue;

            var drlMode = ReadSymbol(Av1CdfLayout.DrlMode + (_drlCtxStack[idx] * Av1CdfLayout.DrlModeSize), Av1CdfLayout.DrlModeSize);
            if (drlMode is 0)
            {
                _refMvIdx = idx;
                return;
            }

            _refMvIdx = idx + 1;
        }
    }

    private readonly bool NeedsInterpFilter()
    {
        var large = Math.Min(BlockWidth(_miSize), BlockHeight(_miSize)) >= 8;
        if (_skipMode || _motionMode is Av1Constants.Localwarp)
            return false;

        if (large && _yMode is Av1Constants.Globalmv)
            return _header.GlobalMotion.Types[_refFrame0] is Av1Constants.Translation;

        if (large && _yMode is Av1Constants.GlobalGlobalmv)
            return _header.GlobalMotion.Types[_refFrame0] is Av1Constants.Translation || _header.GlobalMotion.Types[_refFrame1] is Av1Constants.Translation;

        return true;
    }

    private readonly int ReadInterpFilter(int dir)
    {
        var ctx = (((dir & 1) * 2) + (_refFrame1 > Av1Constants.IntraFrame ? 1 : 0)) * 4;
        var leftType = 3;
        var aboveType = 3;
        if (_availL)
        {
            var index = MiIndex(_miRow, _miCol - 1);
            if (_refFrames[index * 2] == _refFrame0 || _refFrames[(index * 2) + 1] == _refFrame0)
                leftType = (_interInfo[index] >> (dir * 2)) & 3;
        }

        if (_availU)
        {
            var index = MiIndex(_miRow - 1, _miCol);
            if (_refFrames[index * 2] == _refFrame0 || _refFrames[(index * 2) + 1] == _refFrame0)
                aboveType = (_interInfo[index] >> (dir * 2)) & 3;
        }

        if (leftType == aboveType)
        {
            ctx += leftType;
        }
        else if (leftType is 3)
        {
            ctx += aboveType;
        }
        else if (aboveType is 3)
        {
            ctx += leftType;
        }
        else
        {
            ctx += 3;
        }

        return ReadSymbol(Av1CdfLayout.InterpFilter + (ctx * Av1CdfLayout.InterpFilterSize), Av1CdfLayout.InterpFilterSize);
    }

    // Section 5.11.25 "Ref frames syntax"
    private void ReadRefFrames()
    {
        if (_skipMode)
        {
            _refFrame0 = _header.SkipModeFrame[0];
            _refFrame1 = _header.SkipModeFrame[1];
            return;
        }

        if (IsSegmentationFeatureActive(Av1Constants.SegLvlRefFrame))
        {
            _refFrame0 = _header.Segmentation.GetData(_segmentId, Av1Constants.SegLvlRefFrame);
            _refFrame1 = NoReference;
            return;
        }

        if (IsSegmentationFeatureActive(Av1Constants.SegLvlSkip) || IsSegmentationFeatureActive(Av1Constants.SegLvlGlobalmv))
        {
            _refFrame0 = Av1Constants.LastFrame;
            _refFrame1 = NoReference;
            return;
        }

        var bw4 = Av1Tables.Num4x4BlocksWide[_miSize];
        var bh4 = Av1Tables.Num4x4BlocksHigh[_miSize];
        var compMode = Av1Constants.SingleReference;
        if (_header.ReferenceSelect && Math.Min(bw4, bh4) >= 2)
            compMode = ReadSymbol(Av1CdfLayout.CompMode + (GetCompModeContext() * Av1CdfLayout.CompModeSize), Av1CdfLayout.CompModeSize);

        if (compMode is Av1Constants.SingleReference)
        {
            if (ReadSingleRef(0, GetSingleRefP1Context()))
            {
                if (!ReadSingleRef(1, GetCompBwdRefContext()))
                {
                    _refFrame0 = ReadSingleRef(5, GetCompBwdRefP1Context()) ? Av1Constants.Altref2Frame : Av1Constants.BwdrefFrame;
                }
                else
                {
                    _refFrame0 = Av1Constants.AltrefFrame;
                }
            }
            else if (ReadSingleRef(2, GetCompRefContext()))
            {
                _refFrame0 = ReadSingleRef(4, GetCompRefP2Context()) ? Av1Constants.GoldenFrame : Av1Constants.Last3Frame;
            }
            else
            {
                _refFrame0 = ReadSingleRef(3, GetCompRefP1Context()) ? Av1Constants.Last2Frame : Av1Constants.LastFrame;
            }

            _refFrame1 = NoReference;
            return;
        }

        var compRefType = ReadSymbol(Av1CdfLayout.CompRefType + (GetCompRefTypeContext() * Av1CdfLayout.CompRefTypeSize), Av1CdfLayout.CompRefTypeSize);
        if (compRefType is Av1Constants.UnidirCompReference)
        {
            if (ReadUniCompRef(0, GetSingleRefP1Context()))
            {
                _refFrame0 = Av1Constants.BwdrefFrame;
                _refFrame1 = Av1Constants.AltrefFrame;
            }
            else if (ReadUniCompRef(1, GetUniCompRefP1Context()))
            {
                _refFrame0 = Av1Constants.LastFrame;
                _refFrame1 = ReadUniCompRef(2, GetCompRefP2Context()) ? Av1Constants.GoldenFrame : Av1Constants.Last3Frame;
            }
            else
            {
                _refFrame0 = Av1Constants.LastFrame;
                _refFrame1 = Av1Constants.Last2Frame;
            }

            return;
        }

        if (!ReadCompRef(0, GetCompRefContext()))
        {
            _refFrame0 = ReadCompRef(1, GetCompRefP1Context()) ? Av1Constants.Last2Frame : Av1Constants.LastFrame;
        }
        else
        {
            _refFrame0 = ReadCompRef(2, GetCompRefP2Context()) ? Av1Constants.GoldenFrame : Av1Constants.Last3Frame;
        }

        if (!ReadCompBwdRef(0, GetCompBwdRefContext()))
        {
            _refFrame1 = ReadCompBwdRef(1, GetCompBwdRefP1Context()) ? Av1Constants.Altref2Frame : Av1Constants.BwdrefFrame;
        }
        else
        {
            _refFrame1 = Av1Constants.AltrefFrame;
        }
    }

    private readonly bool ReadSingleRef(int index, int ctx) => ReadSymbol(Av1CdfLayout.SingleRef + (((ctx * (Av1Constants.SingleRefs - 1)) + index) * Av1CdfLayout.SingleRefSize), Av1CdfLayout.SingleRefSize) is not 0;

    private readonly bool ReadCompRef(int index, int ctx) => ReadSymbol(Av1CdfLayout.CompRef + (((ctx * (Av1Constants.FwdRefs - 1)) + index) * Av1CdfLayout.CompRefSize), Av1CdfLayout.CompRefSize) is not 0;

    private readonly bool ReadCompBwdRef(int index, int ctx) => ReadSymbol(Av1CdfLayout.CompBwdRef + (((ctx * (Av1Constants.BwdRefs - 1)) + index) * Av1CdfLayout.CompBwdRefSize), Av1CdfLayout.CompBwdRefSize) is not 0;

    private readonly bool ReadUniCompRef(int index, int ctx) => ReadSymbol(Av1CdfLayout.UniCompRef + (((ctx * (Av1Constants.UnidirCompRefs - 1)) + index) * Av1CdfLayout.UniCompRefSize), Av1CdfLayout.UniCompRefSize) is not 0;

    private static bool CheckBackward(int refFrame) => refFrame is >= Av1Constants.BwdrefFrame and <= Av1Constants.AltrefFrame;

    private readonly int GetCompModeContext()
    {
        if (_availU && _availL)
        {
            if (_aboveSingle && _leftSingle)
                return (CheckBackward(_aboveRefFrame0) ? 1 : 0) ^ (CheckBackward(_leftRefFrame0) ? 1 : 0);

            if (_aboveSingle)
                return 2 + (CheckBackward(_aboveRefFrame0) || _aboveIntra ? 1 : 0);

            if (_leftSingle)
                return 2 + (CheckBackward(_leftRefFrame0) || _leftIntra ? 1 : 0);

            return 4;
        }

        if (_availU)
        {
            if (_aboveSingle)
                return CheckBackward(_aboveRefFrame0) ? 1 : 0;

            return 3;
        }

        if (_availL)
        {
            if (_leftSingle)
                return CheckBackward(_leftRefFrame0) ? 1 : 0;

            return 3;
        }

        return 1;
    }

    private readonly int GetCompRefTypeContext()
    {
        var above0 = _aboveRefFrame0;
        var above1 = _aboveRefFrame1;
        var left0 = _leftRefFrame0;
        var left1 = _leftRefFrame1;
        var aboveCompInter = _availU && !_aboveIntra && !_aboveSingle;
        var leftCompInter = _availL && !_leftIntra && !_leftSingle;
        var aboveUniComp = aboveCompInter && IsSameDirectionRefPair(above0, above1);
        var leftUniComp = leftCompInter && IsSameDirectionRefPair(left0, left1);
        if (_availU && !_aboveIntra && _availL && !_leftIntra)
        {
            var samedir = IsSameDirectionRefPair(above0, left0) ? 1 : 0;
            if (!aboveCompInter && !leftCompInter)
                return 1 + (2 * samedir);

            if (!aboveCompInter)
                return !leftUniComp ? 1 : 3 + samedir;

            if (!leftCompInter)
                return !aboveUniComp ? 1 : 3 + samedir;

            if (!aboveUniComp && !leftUniComp)
                return 0;

            if (!aboveUniComp || !leftUniComp)
                return 2;

            return 3 + ((above0 is Av1Constants.BwdrefFrame) == (left0 is Av1Constants.BwdrefFrame) ? 1 : 0);
        }

        if (_availU && _availL)
        {
            if (aboveCompInter)
                return 1 + (aboveUniComp ? 2 : 0);

            if (leftCompInter)
                return 1 + (leftUniComp ? 2 : 0);

            return 2;
        }

        if (aboveCompInter)
            return aboveUniComp ? 4 : 0;

        if (leftCompInter)
            return leftUniComp ? 4 : 0;

        return 2;

        static bool IsSameDirectionRefPair(int ref0, int ref1) => (ref0 >= Av1Constants.BwdrefFrame) == (ref1 >= Av1Constants.BwdrefFrame);
    }

    private readonly int CountRefs(int frameType)
    {
        var c = 0;
        if (_availU)
        {
            if (_aboveRefFrame0 == frameType)
                c++;

            if (_aboveRefFrame1 == frameType)
                c++;
        }

        if (_availL)
        {
            if (_leftRefFrame0 == frameType)
                c++;

            if (_leftRefFrame1 == frameType)
                c++;
        }

        return c;
    }

    private static int GetRefCountContext(int counts0, int counts1)
    {
        if (counts0 < counts1)
            return 0;

        return counts0 == counts1 ? 1 : 2;
    }

    private readonly int GetCompRefContext() => GetRefCountContext(
        CountRefs(Av1Constants.LastFrame) + CountRefs(Av1Constants.Last2Frame),
        CountRefs(Av1Constants.Last3Frame) + CountRefs(Av1Constants.GoldenFrame));

    private readonly int GetCompRefP1Context() => GetRefCountContext(CountRefs(Av1Constants.LastFrame), CountRefs(Av1Constants.Last2Frame));

    private readonly int GetCompRefP2Context() => GetRefCountContext(CountRefs(Av1Constants.Last3Frame), CountRefs(Av1Constants.GoldenFrame));

    private readonly int GetCompBwdRefContext() => GetRefCountContext(
        CountRefs(Av1Constants.BwdrefFrame) + CountRefs(Av1Constants.Altref2Frame),
        CountRefs(Av1Constants.AltrefFrame));

    private readonly int GetCompBwdRefP1Context() => GetRefCountContext(CountRefs(Av1Constants.BwdrefFrame), CountRefs(Av1Constants.Altref2Frame));

    private readonly int GetSingleRefP1Context() => GetRefCountContext(
        CountRefs(Av1Constants.LastFrame) + CountRefs(Av1Constants.Last2Frame) + CountRefs(Av1Constants.Last3Frame) + CountRefs(Av1Constants.GoldenFrame),
        CountRefs(Av1Constants.BwdrefFrame) + CountRefs(Av1Constants.Altref2Frame) + CountRefs(Av1Constants.AltrefFrame));

    private readonly int GetUniCompRefP1Context() => GetRefCountContext(
        CountRefs(Av1Constants.Last2Frame),
        CountRefs(Av1Constants.Last3Frame) + CountRefs(Av1Constants.GoldenFrame));

    // Section 5.11.26 "Assign MV syntax"
    private void AssignMv(bool isCompound)
    {
        for (var i = 0; i < (isCompound ? 2 : 1); i++)
        {
            var compMode = _useIntrabc ? Av1Constants.Newmv : GetMode(i);
            if (_useIntrabc)
            {
                _predMv[0] = _refStackMv[0][0];
                _predMv[1] = _refStackMv[0][1];
                if (_predMv[0] is 0 && _predMv[1] is 0)
                {
                    _predMv[0] = _refStackMv[1][0];
                    _predMv[1] = _refStackMv[1][1];
                }

                if (_predMv[0] is 0 && _predMv[1] is 0)
                {
                    var sbSize4 = _sequence.Use128x128Superblock ? 32 : 16;
                    if (_miRow - sbSize4 < _miRowStart)
                    {
                        _predMv[0] = 0;
                        _predMv[1] = -((sbSize4 * Av1Constants.MiSize) + Av1Constants.IntrabcDelayPixels) * 8;
                    }
                    else
                    {
                        _predMv[0] = -(sbSize4 * Av1Constants.MiSize * 8);
                        _predMv[1] = 0;
                    }
                }
            }
            else if (compMode is Av1Constants.Globalmv)
            {
                _predMv[i * 2] = _globalMvs[i * 2];
                _predMv[(i * 2) + 1] = _globalMvs[(i * 2) + 1];
            }
            else
            {
                var pos = compMode is Av1Constants.Nearestmv ? 0 : _refMvIdx;
                if (compMode is Av1Constants.Newmv && _numMvFound <= 1)
                    pos = 0;

                _predMv[i * 2] = _refStackMv[pos][i * 2];
                _predMv[(i * 2) + 1] = _refStackMv[pos][(i * 2) + 1];
            }

            if (compMode is Av1Constants.Newmv)
            {
                ReadMv(i);
            }
            else
            {
                _mv[i * 2] = _predMv[i * 2];
                _mv[(i * 2) + 1] = _predMv[(i * 2) + 1];
            }
        }

        if (!IsMvValid(isCompound))
            throw Av1Errors.Invalid("a motion vector is out of range.");
    }

    // The is_mv_valid function of section 6.10.25 "Assign mv semantics"
    private readonly bool IsMvValid(bool isCompound)
    {
        for (var i = 0; i < (isCompound ? 4 : 2); i++)
        {
            if (Math.Abs(_mv[i]) >= 1 << 14)
                return false;
        }

        if (!_useIntrabc)
            return true;

        var bw = BlockWidth(_miSize);
        var bh = BlockHeight(_miSize);
        if ((_mv[0] & 7) is not 0 || (_mv[1] & 7) is not 0)
            return false;

        var deltaRow = _mv[0] >> 3;
        var deltaCol = _mv[1] >> 3;
        var srcTopEdge = (_miRow * Av1Constants.MiSize) + deltaRow;
        var srcLeftEdge = (_miCol * Av1Constants.MiSize) + deltaCol;
        var srcBottomEdge = srcTopEdge + bh;
        var srcRightEdge = srcLeftEdge + bw;
        if (_hasChroma)
        {
            if (bw < 8 && _subX is 1)
                srcLeftEdge -= 4;

            if (bh < 8 && _subY is 1)
                srcTopEdge -= 4;
        }

        if (srcTopEdge < _miRowStart * Av1Constants.MiSize
            || srcLeftEdge < _miColStart * Av1Constants.MiSize
            || srcBottomEdge > _miRowEnd * Av1Constants.MiSize
            || srcRightEdge > _miColEnd * Av1Constants.MiSize)
        {
            return false;
        }

        var use128 = _sequence.Use128x128Superblock ? 1 : 0;
        var sbH = _sequence.Use128x128Superblock ? 128 : 64;
        var activeSbRow = _miRow * Av1Constants.MiSize / sbH;
        var activeSb64Col = (_miCol * Av1Constants.MiSize) >> 6;
        var srcSbRow = (srcBottomEdge - 1) / sbH;
        var srcSb64Col = (srcRightEdge - 1) >> 6;
        var totalSb64PerRow = ((_miColEnd - _miColStart - 1) >> 4) + 1;
        var activeSb64 = (activeSbRow * totalSb64PerRow) + activeSb64Col;
        var srcSb64 = (srcSbRow * totalSb64PerRow) + srcSb64Col;
        if (srcSb64 >= activeSb64 - Av1Constants.IntrabcDelaySb64)
            return false;

        var gradient = 1 + Av1Constants.IntrabcDelaySb64 + use128;
        var wfOffset = gradient * (activeSbRow - srcSbRow);
        return srcSbRow <= activeSbRow && srcSb64Col < activeSb64Col - Av1Constants.IntrabcDelaySb64 + wfOffset;
    }

    // Section 5.11.29 "Get mode function"
    private readonly int GetMode(int refList)
    {
        if (refList is 0)
        {
            return _yMode switch
            {
                < Av1Constants.NearestNearestmv => _yMode,
                Av1Constants.NewNewmv or Av1Constants.NewNearestmv or Av1Constants.NewNearmv => Av1Constants.Newmv,
                Av1Constants.NearestNearestmv or Av1Constants.NearestNewmv => Av1Constants.Nearestmv,
                Av1Constants.NearNearmv or Av1Constants.NearNewmv => Av1Constants.Nearmv,
                _ => Av1Constants.Globalmv,
            };
        }

        return _yMode switch
        {
            Av1Constants.NewNewmv or Av1Constants.NearestNewmv or Av1Constants.NearNewmv => Av1Constants.Newmv,
            Av1Constants.NearestNearestmv or Av1Constants.NewNearestmv => Av1Constants.Nearestmv,
            Av1Constants.NearNearmv or Av1Constants.NewNearmv => Av1Constants.Nearmv,
            _ => Av1Constants.Globalmv,
        };
    }

    // Section 5.11.31 "MV syntax"
    private void ReadMv(int refList)
    {
        var mvCtx = _useIntrabc ? Av1Constants.MvIntrabcContext : 0;
        var diffRow = 0;
        var diffCol = 0;
        var mvJoint = ReadSymbol(Av1CdfLayout.MvJoint + (mvCtx * Av1CdfLayout.MvJointSize), Av1CdfLayout.MvJointSize);
        if (mvJoint is Av1Constants.MvJointHzvnz or Av1Constants.MvJointHnzvnz)
            diffRow = ReadMvComponent(mvCtx, comp: 0);

        if (mvJoint is Av1Constants.MvJointHnzvz or Av1Constants.MvJointHnzvnz)
            diffCol = ReadMvComponent(mvCtx, comp: 1);

        _mv[refList * 2] = _predMv[refList * 2] + diffRow;
        _mv[(refList * 2) + 1] = _predMv[(refList * 2) + 1] + diffCol;
    }

    // Section 5.11.32 "MV component syntax"
    private readonly int ReadMvComponent(int mvCtx, int comp)
    {
        var context = (mvCtx * 2) + comp;
        var mvSign = ReadSymbol(Av1CdfLayout.MvSign + (context * Av1CdfLayout.MvSignSize), Av1CdfLayout.MvSignSize);
        var mvClass = ReadSymbol(Av1CdfLayout.MvClass + (context * Av1CdfLayout.MvClassSize), Av1CdfLayout.MvClassSize);
        int mag;
        if (mvClass is Av1Constants.MvClass0)
        {
            var mvClass0Bit = ReadSymbol(Av1CdfLayout.MvClass0Bit + (context * Av1CdfLayout.MvClass0BitSize), Av1CdfLayout.MvClass0BitSize);
            var mvClass0Fr = 3;
            if (!_header.ForceIntegerMv)
                mvClass0Fr = ReadSymbol(Av1CdfLayout.MvClass0Fr + (((context * Av1Constants.Class0Size) + mvClass0Bit) * Av1CdfLayout.MvClass0FrSize), Av1CdfLayout.MvClass0FrSize);

            var mvClass0Hp = 1;
            if (_header.AllowHighPrecisionMv)
                mvClass0Hp = ReadSymbol(Av1CdfLayout.MvClass0Hp + (context * Av1CdfLayout.MvClass0HpSize), Av1CdfLayout.MvClass0HpSize);

            mag = ((mvClass0Bit << 3) | (mvClass0Fr << 1) | mvClass0Hp) + 1;
        }
        else
        {
            var d = 0;
            for (var i = 0; i < mvClass; i++)
            {
                var mvBit = ReadSymbol(Av1CdfLayout.MvBit + (((context * Av1Constants.MvOffsetBits) + i) * Av1CdfLayout.MvBitSize), Av1CdfLayout.MvBitSize);
                d |= mvBit << i;
            }

            mag = Av1Constants.Class0Size << (mvClass + 2);
            var mvFr = 3;
            if (!_header.ForceIntegerMv)
                mvFr = ReadSymbol(Av1CdfLayout.MvFr + (context * Av1CdfLayout.MvFrSize), Av1CdfLayout.MvFrSize);

            var mvHp = 1;
            if (_header.AllowHighPrecisionMv)
                mvHp = ReadSymbol(Av1CdfLayout.MvHp + (context * Av1CdfLayout.MvHpSize), Av1CdfLayout.MvHpSize);

            mag += ((d << 3) | (mvFr << 1) | mvHp) + 1;
        }

        return mvSign is not 0 ? -mag : mag;
    }

    // Section 5.11.27 "Read motion mode syntax"
    private void ReadMotionMode(bool isCompound)
    {
        _motionMode = Av1Constants.Simple;
        if (_skipMode || !_header.IsMotionModeSwitchable || Math.Min(BlockWidth(_miSize), BlockHeight(_miSize)) < 8)
            return;

        if (!_header.ForceIntegerMv && _yMode is Av1Constants.Globalmv or Av1Constants.GlobalGlobalmv && _header.GlobalMotion.Types[_refFrame0] > Av1Constants.Translation)
            return;

        if (isCompound || _refFrame1 is Av1Constants.IntraFrame || !HasOverlappableCandidates())
            return;

        FindWarpSamples();
        if (_header.ForceIntegerMv || _numSamples is 0 || !_header.AllowWarpedMotion || _references.IsScaled(_refFrame0))
        {
            var useObmc = ReadSymbol(Av1CdfLayout.UseObmc + (_miSize * Av1CdfLayout.UseObmcSize), Av1CdfLayout.UseObmcSize);
            _motionMode = useObmc is not 0 ? Av1Constants.Obmc : Av1Constants.Simple;
        }
        else
        {
            _motionMode = ReadSymbol(Av1CdfLayout.MotionMode + (_miSize * Av1CdfLayout.MotionModeSize), Av1CdfLayout.MotionModeSize);
        }
    }

    // Section 5.11.28 "Read inter intra syntax"
    private void ReadInterIntraMode(bool isCompound)
    {
        _interintra = false;
        if (_skipMode || !_sequence.EnableInterIntraCompound || isCompound || _miSize is < Av1Constants.Block8x8 or > Av1Constants.Block32x32)
            return;

        var ctx = Av1Tables.SizeGroup[_miSize] - 1;
        _interintra = ReadSymbol(Av1CdfLayout.InterIntra + (ctx * Av1CdfLayout.InterIntraSize), Av1CdfLayout.InterIntraSize) is not 0;
        if (!_interintra)
            return;

        _interintraMode = ReadSymbol(Av1CdfLayout.InterIntraMode + (ctx * Av1CdfLayout.InterIntraModeSize), Av1CdfLayout.InterIntraModeSize);
        _refFrame1 = Av1Constants.IntraFrame;
        _angleDeltaY = 0;
        _angleDeltaUv = 0;
        _useFilterIntra = false;
        _wedgeInterintra = ReadSymbol(Av1CdfLayout.WedgeInterIntra + (_miSize * Av1CdfLayout.WedgeInterIntraSize), Av1CdfLayout.WedgeInterIntraSize) is not 0;
        if (_wedgeInterintra)
        {
            _wedgeIndex = ReadSymbol(Av1CdfLayout.WedgeIndex + (_miSize * Av1CdfLayout.WedgeIndexSize), Av1CdfLayout.WedgeIndexSize);
            _wedgeSign = 0;
        }
    }

    // Section 5.11.30 "Read compound type syntax"
    private void ReadCompoundType(bool isCompound)
    {
        _compGroupIdx = 0;
        _compoundIdx = 1;
        if (_skipMode)
        {
            _compoundType = Av1Constants.CompoundAverage;
            return;
        }

        if (!isCompound)
        {
            if (_interintra)
            {
                _compoundType = _wedgeInterintra ? Av1Constants.CompoundWedge : Av1Constants.CompoundIntra;
            }
            else
            {
                _compoundType = Av1Constants.CompoundAverage;
            }

            return;
        }

        var n = Av1Tables.WedgeBits[_miSize];
        if (_sequence.EnableMaskedCompound)
            _compGroupIdx = ReadSymbol(Av1CdfLayout.CompGroupIdx + (GetCompGroupIdxContext() * Av1CdfLayout.CompGroupIdxSize), Av1CdfLayout.CompGroupIdxSize);

        if (_compGroupIdx is 0)
        {
            if (_sequence.EnableJntComp)
            {
                _compoundIdx = ReadSymbol(Av1CdfLayout.CompoundIdx + (GetCompoundIdxContext() * Av1CdfLayout.CompoundIdxSize), Av1CdfLayout.CompoundIdxSize);
                _compoundType = _compoundIdx is not 0 ? Av1Constants.CompoundAverage : Av1Constants.CompoundDistance;
            }
            else
            {
                _compoundType = Av1Constants.CompoundAverage;
            }
        }
        else if (n is 0)
        {
            _compoundType = Av1Constants.CompoundDiffwtd;
        }
        else
        {
            _compoundType = ReadSymbol(Av1CdfLayout.CompoundType + (_miSize * Av1CdfLayout.CompoundTypeSize), Av1CdfLayout.CompoundTypeSize);
        }

        if (_compoundType is Av1Constants.CompoundWedge)
        {
            _wedgeIndex = ReadSymbol(Av1CdfLayout.WedgeIndex + (_miSize * Av1CdfLayout.WedgeIndexSize), Av1CdfLayout.WedgeIndexSize);
            _wedgeSign = ReadLiteral(1);
        }
        else if (_compoundType is Av1Constants.CompoundDiffwtd)
        {
            _maskType = ReadLiteral(1);
        }
    }

    private readonly int GetCompGroupIdxContext()
    {
        var ctx = 0;
        if (_availU)
        {
            if (!_aboveSingle)
            {
                ctx += (_interInfo[MiIndex(_miRow - 1, _miCol)] >> Av1FrameState.CompGroupIdxShift) & 1;
            }
            else if (_aboveRefFrame0 is Av1Constants.AltrefFrame)
            {
                ctx += 3;
            }
        }

        if (_availL)
        {
            if (!_leftSingle)
            {
                ctx += (_interInfo[MiIndex(_miRow, _miCol - 1)] >> Av1FrameState.CompGroupIdxShift) & 1;
            }
            else if (_leftRefFrame0 is Av1Constants.AltrefFrame)
            {
                ctx += 3;
            }
        }

        return Math.Min(5, ctx);
    }

    private readonly int GetCompoundIdxContext()
    {
        var fwd = Math.Abs(GetRelativeDistance(_header.OrderHints[_refFrame0], _header.OrderHint));
        var bck = Math.Abs(GetRelativeDistance(_header.OrderHints[_refFrame1], _header.OrderHint));
        var ctx = fwd == bck ? 3 : 0;
        if (_availU)
        {
            if (!_aboveSingle)
            {
                ctx += (_interInfo[MiIndex(_miRow - 1, _miCol)] >> Av1FrameState.CompoundIdxShift) & 1;
            }
            else if (_aboveRefFrame0 is Av1Constants.AltrefFrame)
            {
                ctx++;
            }
        }

        if (_availL)
        {
            if (!_leftSingle)
            {
                ctx += (_interInfo[MiIndex(_miRow, _miCol - 1)] >> Av1FrameState.CompoundIdxShift) & 1;
            }
            else if (_leftRefFrame0 is Av1Constants.AltrefFrame)
            {
                ctx++;
            }
        }

        return ctx;
    }

    private readonly int GetRelativeDistance(int a, int b) => Av1FrameHeaderParser.GetRelativeDistance(_sequence, a, b);

    // Section 5.11.17 "Var TX size syntax"
    private void ReadVarTxSize(int row, int col, int txSz, int depth)
    {
        if (row >= _miRows || col >= _miCols)
            return;

        var txfmSplit = false;
        if (txSz is not Av1Constants.Tx4x4 && depth is not Av1Constants.MaxVartxDepth)
        {
            var above = GetAboveTxWidth(row, col) < Av1Tables.TxWidth[txSz] ? 1 : 0;
            var left = GetLeftTxHeight(row, col) < Av1Tables.TxHeight[txSz] ? 1 : 0;
            var size = Math.Min(64, Math.Max(BlockWidth(_miSize), BlockHeight(_miSize)));
            var maxTxSz = FindTxSize(size, size);
            var txSzSqrUp = Av1Tables.TxSizeSqrUp[txSz];
            var ctx = ((txSzSqrUp != maxTxSz ? 1 : 0) * 3) + ((Av1Constants.TxSizes - 1 - maxTxSz) * 6) + above + left;
            txfmSplit = ReadSymbol(Av1CdfLayout.TxfmSplit + (ctx * Av1CdfLayout.TxfmSplitSize), Av1CdfLayout.TxfmSplitSize) is not 0;
        }

        var w4 = Av1Tables.TxWidth[txSz] / Av1Constants.MiSize;
        var h4 = Av1Tables.TxHeight[txSz] / Av1Constants.MiSize;
        if (txfmSplit)
        {
            _state.Tools |= Av1Tools.VariableTransformSize;
            var subTxSz = Av1Tables.SplitTxSize[txSz];
            var stepW = Av1Tables.TxWidth[subTxSz] / Av1Constants.MiSize;
            var stepH = Av1Tables.TxHeight[subTxSz] / Av1Constants.MiSize;
            for (var i = 0; i < h4; i += stepH)
            {
                for (var j = 0; j < w4; j += stepW)
                {
                    ReadVarTxSize(row + i, col + j, subTxSz, depth + 1);
                }
            }
        }
        else
        {
            var width = Math.Min(w4, _miCols - col);
            var height = Math.Min(h4, _miRows - row);
            for (var i = 0; i < height; i++)
            {
                _interTxSizes.Slice(MiIndex(row + i, col), width).Fill((byte)txSz);
            }

            _txSize = txSz;
        }
    }

    // The find_tx_size function of section 5.11.36 "Transform tree syntax"
    private static int FindTxSize(int w, int h)
    {
        var txSz = 0;
        for (; txSz < Av1Constants.TxSizesAll; txSz++)
        {
            if (Av1Tables.TxWidth[txSz] == w && Av1Tables.TxHeight[txSz] == h)
                break;
        }

        return txSz;
    }

    // Section 5.11.36 "Transform tree syntax"
    private void TransformTree(int startX, int startY, int w, int h)
    {
        var maxX = _miCols * Av1Constants.MiSize;
        var maxY = _miRows * Av1Constants.MiSize;
        if (startX >= maxX || startY >= maxY)
            return;

        var row = startY >> Av1Constants.MiSizeLog2;
        var col = startX >> Av1Constants.MiSizeLog2;
        var lumaTxSz = _interTxSizes[MiIndex(row, col)];
        var lumaW = Av1Tables.TxWidth[lumaTxSz];
        var lumaH = Av1Tables.TxHeight[lumaTxSz];
        if (w <= lumaW && h <= lumaH)
        {
            TransformBlock(0, startX, startY, FindTxSize(w, h), 0, 0);
        }
        else if (w > h)
        {
            TransformTree(startX, startY, w / 2, h);
            TransformTree(startX + (w / 2), startY, w / 2, h);
        }
        else if (w < h)
        {
            TransformTree(startX, startY, w, h / 2);
            TransformTree(startX, startY + (h / 2), w, h / 2);
        }
        else
        {
            TransformTree(startX, startY, w / 2, h / 2);
            TransformTree(startX + (w / 2), startY, w / 2, h / 2);
            TransformTree(startX, startY + (h / 2), w / 2, h / 2);
            TransformTree(startX + (w / 2), startY + (h / 2), w / 2, h / 2);
        }
    }
}
