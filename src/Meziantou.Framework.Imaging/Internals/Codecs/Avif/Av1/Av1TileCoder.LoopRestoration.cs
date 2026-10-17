using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>The loop restoration units read with the superblocks (AV1 specification, sections 5.11.57 and 5.11.58).</content>
internal ref partial struct Av1TileCoder
{
    // Section 5.11.57 "Read loop restoration syntax"
    private void ReadLr(int r, int c, int bSize) => VisitLr(r, c, bSize, write: false);

    // The same units, written: the encoder chose their parameters after the frame was filtered
    private void WriteLr(int r, int c, int bSize) => VisitLr(r, c, bSize, write: true);

    // RefLrWiener and RefSgrXqd at the start of a tile (clear_above_context in decode_tile, section 5.11.2)
    private void ResetLoopRestorationReferences()
    {
        for (var plane = 0; plane < _planeCount; plane++)
        {
            for (var pass = 0; pass < 2; pass++)
            {
                _refSgrXqd[plane][pass] = Av1Tables.SgrprojXqdMid[pass];
                for (var i = 0; i < Av1Constants.WienerCoeffs; i++)
                {
                    _refLrWiener[plane][pass][i] = Av1Tables.WienerTapsMid[i];
                }
            }
        }
    }

    // The loop restoration units whose first sample is in the superblock at (r, c), which are coded with it
    private void VisitLr(int r, int c, int bSize, bool write)
    {
        if (_header.AllowIntrabc)
            return;

        var w = Av1Tables.Num4x4BlocksWide[bSize];
        var h = Av1Tables.Num4x4BlocksHigh[bSize];
        for (var plane = 0; plane < _planeCount; plane++)
        {
            if (_header.FrameRestorationType[plane] is Av1Constants.RestoreNone)
                continue;

            var subX = plane is 0 ? 0 : _subX;
            var subY = plane is 0 ? 0 : _subY;
            var unitSize = _header.LoopRestorationSize[plane];
            var unitRows = _state.GetLoopRestorationUnitRows(plane);
            var unitCols = _state.GetLoopRestorationUnitCols(plane);
            var unitRowStart = ((r * (Av1Constants.MiSize >> subY)) + unitSize - 1) / unitSize;
            var unitRowEnd = Math.Min(unitRows, (((r + h) * (Av1Constants.MiSize >> subY)) + unitSize - 1) / unitSize);
            int numerator;
            int denominator;
            if (_header.UseSuperres)
            {
                numerator = (Av1Constants.MiSize >> subX) * _header.SuperresDenom;
                denominator = unitSize * Av1Constants.SuperresNum;
            }
            else
            {
                numerator = Av1Constants.MiSize >> subX;
                denominator = unitSize;
            }

            var unitColStart = ((c * numerator) + denominator - 1) / denominator;
            var unitColEnd = Math.Min(unitCols, (((c + w) * numerator) + denominator - 1) / denominator);
            for (var unitRow = unitRowStart; unitRow < unitRowEnd; unitRow++)
            {
                for (var unitCol = unitColStart; unitCol < unitColEnd; unitCol++)
                {
                    var unit = _state.GetLoopRestorationUnits(plane).Slice(((unitRow * unitCols) + unitCol) * Av1FrameState.LoopRestorationUnitLength, Av1FrameState.LoopRestorationUnitLength);
                    if (write)
                        WriteLrUnit(plane, unit);
                    else
                        ReadLrUnit(plane, unit);
                }
            }
        }
    }

    // Section 5.11.58 "Read loop restoration unit syntax": a unit stores LrType, LrWiener[ 2 ][ 3 ], LrSgrSet and LrSgrXqd[ 2 ]
    private void ReadLrUnit(int plane, Span<sbyte> unit)
    {
        int restorationType;
        if (_header.FrameRestorationType[plane] is Av1Constants.RestoreWiener)
        {
            restorationType = ReadSymbol(Av1CdfLayout.UseWiener, Av1CdfLayout.UseWienerSize) is not 0 ? Av1Constants.RestoreWiener : Av1Constants.RestoreNone;
        }
        else if (_header.FrameRestorationType[plane] is Av1Constants.RestoreSgrproj)
        {
            restorationType = ReadSymbol(Av1CdfLayout.UseSgrproj, Av1CdfLayout.UseSgrprojSize) is not 0 ? Av1Constants.RestoreSgrproj : Av1Constants.RestoreNone;
        }
        else
        {
            restorationType = ReadSymbol(Av1CdfLayout.RestorationType, Av1CdfLayout.RestorationTypeSize);
        }

        unit[0] = (sbyte)restorationType;
        if (restorationType is Av1Constants.RestoreWiener)
        {
            for (var pass = 0; pass < 2; pass++)
            {
                var firstCoeff = 0;
                if (plane is not 0)
                {
                    firstCoeff = 1;
                    unit[1 + (pass * 3)] = 0;
                }

                for (var j = firstCoeff; j < 3; j++)
                {
                    int min = Av1Tables.WienerTapsMin[j];
                    int max = Av1Tables.WienerTapsMax[j];
                    int k = Av1Tables.WienerTapsK[j];
                    var v = DecodeSignedSubexpWithRefBool(min, max + 1, k, _refLrWiener[plane][pass][j]);
                    unit[1 + (pass * 3) + j] = (sbyte)v;
                    _refLrWiener[plane][pass][j] = v;
                }
            }
        }
        else if (restorationType is Av1Constants.RestoreSgrproj)
        {
            var lrSgrSet = ReadLiteral(Av1Constants.SgrprojParamsBits);
            unit[7] = (sbyte)lrSgrSet;
            for (var i = 0; i < 2; i++)
            {
                var radius = Av1Tables.SgrParams[(lrSgrSet * 4) + (i * 2)];
                int min = Av1Tables.SgrprojXqdMin[i];
                int max = Av1Tables.SgrprojXqdMax[i];
                int v;
                if (radius is not 0)
                {
                    v = DecodeSignedSubexpWithRefBool(min, max + 1, Av1Constants.SgrprojPrjSubexpK, _refSgrXqd[plane][i]);
                }
                else
                {
                    v = 0;
                    if (i is 1)
                        v = Math.Clamp((1 << Av1Constants.SgrprojPrjBits) - _refSgrXqd[plane][0], min, max);
                }

                unit[8 + i] = (sbyte)v;
                _refSgrXqd[plane][i] = v;
            }
        }
    }

    // Section 5.11.58 "Read loop restoration unit syntax", written: the encoder only selects the Wiener filter
    private void WriteLrUnit(int plane, ReadOnlySpan<sbyte> unit)
    {
        int restorationType = unit[0];
        Debug.Assert(restorationType is Av1Constants.RestoreNone or Av1Constants.RestoreWiener);
        if (_header.FrameRestorationType[plane] is Av1Constants.RestoreWiener)
        {
            WriteSymbol(Av1CdfLayout.UseWiener, Av1CdfLayout.UseWienerSize, restorationType is Av1Constants.RestoreWiener ? 1 : 0);
        }
        else
        {
            Debug.Assert(_header.FrameRestorationType[plane] is Av1Constants.RestoreSwitchable);
            WriteSymbol(Av1CdfLayout.RestorationType, Av1CdfLayout.RestorationTypeSize, restorationType);
        }

        if (restorationType is not Av1Constants.RestoreWiener)
            return;

        for (var pass = 0; pass < 2; pass++)
        {
            for (var j = plane is 0 ? 0 : 1; j < 3; j++)
            {
                int min = Av1Tables.WienerTapsMin[j];
                int max = Av1Tables.WienerTapsMax[j];
                int k = Av1Tables.WienerTapsK[j];
                int v = unit[1 + (pass * 3) + j];
                EncodeUnsignedSubexpWithRefBool(max + 1 - min, k, _refLrWiener[plane][pass][j] - min, v - min);
                _refLrWiener[plane][pass][j] = v;
            }
        }
    }

    // The inverse of decode_unsigned_subexp_with_ref_bool
    private void EncodeUnsignedSubexpWithRefBool(int mx, int k, int r, int v)
    {
        if ((r << 1) <= mx)
            EncodeSubexpBool(mx, k, Recenter(r, v));
        else
            EncodeSubexpBool(mx, k, Recenter(mx - 1 - r, mx - 1 - v));
    }

    // The inverse of decode_subexp_bool
    private void EncodeSubexpBool(int numSyms, int k, int v)
    {
        var i = 0;
        var mk = 0;
        while (true)
        {
            var b2 = i is not 0 ? k + i - 1 : k;
            var a = 1 << b2;
            if (numSyms <= mk + (3 * a))
            {
                WriteNonSymmetric(v - mk, numSyms - mk);
                return;
            }

            if (v < mk + a)
            {
                WriteLiteral(0, 1);
                WriteLiteral(v - mk, b2);
                return;
            }

            WriteLiteral(1, 1);
            i++;
            mk += a;
        }
    }

    // The inverse of inverse_recenter
    private static int Recenter(int r, int v)
    {
        if (v > 2 * r)
            return v;

        return v >= r ? (v - r) << 1 : ((r - v) << 1) - 1;
    }

    private readonly int DecodeSignedSubexpWithRefBool(int low, int high, int k, int r)
    {
        var x = DecodeUnsignedSubexpWithRefBool(high - low, k, r - low);
        return x + low;
    }

    private readonly int DecodeUnsignedSubexpWithRefBool(int mx, int k, int r)
    {
        var v = DecodeSubexpBool(mx, k);
        if ((r << 1) <= mx)
            return InverseRecenter(r, v);

        return mx - 1 - InverseRecenter(mx - 1 - r, v);
    }

    private readonly int DecodeSubexpBool(int numSyms, int k)
    {
        var i = 0;
        var mk = 0;
        while (true)
        {
            var b2 = i is not 0 ? k + i - 1 : k;
            var a = 1 << b2;
            if (numSyms <= mk + (3 * a))
                return _symbols.ReadNonSymmetric(numSyms - mk) + mk;

            if (ReadLiteral(1) is 0)
                return ReadLiteral(b2) + mk;

            i++;
            mk += a;
        }
    }

    private static int InverseRecenter(int r, int v)
    {
        if (v > 2 * r)
            return v;

        if ((v & 1) is not 0)
            return r - ((v + 1) >> 1);

        return r + (v >> 1);
    }
}
