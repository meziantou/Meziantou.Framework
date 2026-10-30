using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>
/// The rate-distortion optimization of the quantized levels of a transform block: the quantizer rounds each coefficient
/// on its own, and this pass lowers the levels whose bits cost more than the error they save.
/// </content>
internal ref partial struct Av1TileCoder
{
    // The candidates whose added error is worth more bits than this are not costed: lowering a level by one saves fewer
    // bits than that
    private const int MaxLevelSavingBits = 12;

    // The positions whose rate changes with the level of a position: the position, and its dependents
    private const int MaxAffectedPositions = 6;

    // The positions whose contexts read the level of a position (get_coeff_base_ctx and the context of coeff_br): the
    // neighbors that the contexts read, with the opposite sign. Five per transform class, as row and column offsets
    private static ReadOnlySpan<sbyte> LevelDependents2D => [0, -1, -1, 0, -1, -1, 0, -2, -2, 0];

    private static ReadOnlySpan<sbyte> LevelDependentsHorizontal => [0, -1, -1, 0, 0, -2, 0, -3, 0, -4];

    private static ReadOnlySpan<sbyte> LevelDependentsVertical => [0, -1, -1, 0, -2, 0, -3, 0, -4, 0];

    // Lowers the levels of a transform block, by one at a time, from the last coefficient to the first one: a level is
    // lowered when the squared error that it adds is worth fewer bits than it saves, with the rates of the coefficient
    // syntax and the contexts of the block. The levels and the coefficients are in the layout of Quant; the levels are
    // changed in place
    private void OptimizeLevels(int plane, int startX, int startY, int txSz, int lumaTxType, int planeTxType, ReadOnlySpan<int> coefficients, Span<int> levels, int dcQuantizer, int acQuantizer, int denominator)
    {
        Debug.Assert(_sink is SymbolSink.Cost);
        var count = levels.Length;
        if (!levels.ContainsAnyExcept(0))
            return;

        var scan = GetScan(txSz, planeTxType);
        var eob = count;
        while (levels[scan[eob - 1]] is 0)
        {
            eob--;
        }

        var adjTxSz = Av1Tables.AdjustedTxSize[txSz];
        var bwl = Av1Tables.TxWidthLog2[adjTxSz];
        var width = 1 << bwl;
        var height = Av1Tables.TxHeight[adjTxSz];
        Debug.Assert(width * height == count);
        var txSzCtx = (Av1Tables.TxSizeSqr[txSz] + Av1Tables.TxSizeSqrUp[txSz] + 1) >> 1;
        var ptype = plane > 0 ? 1 : 0;
        var txClass = GetTxClass(planeTxType);
        var x4 = startX >> 2;
        var y4 = startY >> 2;
        var w4 = Av1Tables.TxWidth[txSz] >> 2;
        var h4 = Av1Tables.TxHeight[txSz] >> 2;
        var maxX4 = plane > 0 ? _miCols >> _subX : _miCols;
        var maxY4 = plane > 0 ? _miRows >> _subY : _miRows;
        var dcSignContext = GetDcSignContext(_state.GetAboveDcContext(plane), _scratch.GetLeftDcContext(plane), x4, y4, w4, h4, maxX4, maxY4);

        const int MaxCodedLevel = Av1Constants.NumBaseLevels + Av1Constants.CoeffBaseRange + 1;
        var model = new LevelModel
        {
            Width = width,
            Height = height,
            Bwl = bwl,
            Stride = width + Av1TileScratch.LevelMapPadding,
            TxSz = txSz,
            TxSzCtx = txSzCtx,
            Ptype = ptype,
            TxClass = txClass,
            BaseOffset = Av1CdfLayout.CoeffBase + ((((txSzCtx * Av1Constants.PlaneTypes) + ptype) * Av1Constants.SigCoefContexts) * Av1CdfLayout.CoeffBaseSize),
            BaseEobOffset = Av1CdfLayout.CoeffBaseEob + ((((txSzCtx * Av1Constants.PlaneTypes) + ptype) * Av1Constants.SigCoefContextsEob) * Av1CdfLayout.CoeffBaseEobSize),
            BrOffset = Av1CdfLayout.CoeffBr + ((((Math.Min(txSzCtx, Av1Constants.Tx32x32) * Av1Constants.PlaneTypes) + ptype) * Av1Constants.LevelContexts) * Av1CdfLayout.CoeffBrSize),
            DcSignOffset = Av1CdfLayout.DcSign + (((ptype * Av1Constants.DcSignContexts) + dcSignContext) * Av1CdfLayout.DcSignSize),
        };

        // The levels of the coded positions, as the contexts read them, and their scan indexes: the positions after the
        // end of block are zero, at the index of the end of block or after it
        Span<byte> map = stackalloc byte[(32 + Av1TileScratch.LevelMapPadding) * (32 + Av1TileScratch.LevelMapPadding)];
        map = map[..((height + Av1TileScratch.LevelMapPadding) * model.Stride)];
        map.Clear();
        Span<short> indexes = stackalloc short[32 * 32];
        indexes = indexes[..count];
        indexes.Fill(short.MaxValue);
        for (var i = 0; i < eob; i++)
        {
            var pos = scan[i];
            indexes[pos] = (short)i;
            map[((pos >> bwl) * model.Stride) + (pos & (width - 1))] = (byte)Math.Min(Math.Abs(levels[pos]), MaxCodedLevel);
        }

        var dependents = txClass switch
        {
            TxClass2D => LevelDependents2D,
            Av1Constants.TxClassHoriz => LevelDependentsHorizontal,
            _ => LevelDependentsVertical,
        };

        var gain = Av1ForwardTransform.GetSquaredGain(txSz);

        // An error of a chroma sample weighs more in the RGB pixels than in the squared error of the planes: the chroma
        // levels are lowered with half the multiplier of the rate
        var lambda = plane is 0 ? _decisions.Lambda : _decisions.Lambda / 2;
        var maxSaving = (MaxLevelSavingBits * Av1Cost.Scale * lambda) >> 10;
#if DEBUG
        var initialModelRate = GetLevelsRate(ref model, map, levels, scan, eob);
        var initialRate = GetCoefficientsRate(plane, startX, startY, txSz, lumaTxType, levels, scan, eob);
#endif

        // The rate of the end of block and of every coded position, as the levels stand: a candidate only computes the
        // rates that it changes
        Span<int> rates = stackalloc int[32 * 32];
        for (var i = 0; i < eob; i++)
        {
            rates[scan[i]] = GetPositionRate(ref model, map, levels, scan[i], i, eob);
        }

        var eobRate = GetEobRate(ref model, eob);
        Span<int> affected = stackalloc int[MaxAffectedPositions];
        Span<int> affectedRates = stackalloc int[MaxAffectedPositions];
        for (var c = eob - 1; c >= 0; c--)
        {
            var pos = scan[c];
            var level = levels[pos];
            if (level is 0)
                continue;

            var lowered = level > 0 ? level - 1 : level + 1;
            var quantizer = pos is 0 ? dcQuantizer : acQuantizer;
            long before = coefficients[pos] - Av1Quantizer.Dequantize(level, quantizer, denominator, _bitDepth);
            long after = coefficients[pos] - Av1Quantizer.Dequantize(lowered, quantizer, denominator, _bitDepth);
            var addedError = ((((after * after) - (before * before)) << 8) / gain) << 8;
            if (addedError >= maxSaving)
                continue;

            var row = pos >> bwl;
            var col = pos & (width - 1);
            var mapIndex = (row * model.Stride) + col;
            var mapLevel = map[mapIndex];
            long rateChange;
            int affectedCount;
            var newEob = eob;
            var newEobRate = eobRate;
            if (lowered is 0 && c == eob - 1)
            {
                // The last coefficient goes: the block ends at the previous coefficient that is not zero
                newEob = c;
                while (newEob > 0 && levels[scan[newEob - 1]] is 0)
                {
                    newEob--;
                }

                // The model costs the coefficients of a block that has some: the only coefficient of a block stays
                if (newEob is 0)
                    continue;

                // The positions from the new last one to the old one go or change, and so do the positions before them
                // whose contexts read the level that goes
                affectedCount = CollectDependents(indexes, dependents, model.Bwl, row, col, newEob - 1, affected);
                affected[affectedCount++] = scan[newEob - 1];
                rateChange = -eobRate;
                for (var i = newEob; i < eob; i++)
                {
                    rateChange -= rates[scan[i]];
                }

                levels[pos] = 0;
                map[mapIndex] = 0;
                newEobRate = GetEobRate(ref model, newEob);
                rateChange += newEobRate;
            }
            else
            {
                // The position and the positions whose contexts read its level
                affectedCount = CollectDependents(indexes, dependents, model.Bwl, row, col, eob, affected);
                affected[affectedCount++] = pos;
                rateChange = 0;
                levels[pos] = lowered;
                map[mapIndex] = (byte)Math.Min(Math.Abs(lowered), MaxCodedLevel);
            }

            for (var k = 0; k < affectedCount; k++)
            {
                var position = affected[k];
                affectedRates[k] = GetPositionRate(ref model, map, levels, position, indexes[position], newEob);
                rateChange += affectedRates[k] - rates[position];
            }

            if (addedError + ((rateChange * lambda) >> 10) < 0)
            {
                eob = newEob;
                eobRate = newEobRate;
                for (var k = 0; k < affectedCount; k++)
                {
                    rates[affected[k]] = affectedRates[k];
                }
            }
            else
            {
                levels[pos] = level;
                map[mapIndex] = mapLevel;
            }
        }

#if DEBUG
        // The model is the coefficient syntax: its rate changes as much as the rate of the syntax
        if (eob > 0)
            Debug.Assert(GetCoefficientsRate(plane, startX, startY, txSz, lumaTxType, levels, scan, eob) - initialRate == GetLevelsRate(ref model, map, levels, scan, eob) - initialModelRate);
#endif
    }

#if DEBUG
    // The rate of the coefficient syntax of a transform block, with the contexts left as they are found
    private long GetCoefficientsRate(int plane, int startX, int startY, int txSz, int lumaTxType, scoped ReadOnlySpan<int> levels, Av1Scan scan, int eob)
    {
        var x4 = startX >> 2;
        var y4 = startY >> 2;
        var w4 = Av1Tables.TxWidth[txSz] >> 2;
        var h4 = Av1Tables.TxHeight[txSz] >> 2;
        Span<byte> contexts = stackalloc byte[4 * 16];
        CopyTransformBlockContexts(contexts, plane, x4, y4, w4, h4, save: true);
        Span<int> ordered = stackalloc int[32 * 32];
        for (var i = 0; i < eob; i++)
        {
            ordered[i] = levels[scan[i]];
        }

        var rate = _rate;
        _rate = 0;
        WriteCoeffs(plane, startX, startY, txSz, lumaTxType, ordered[..eob], eob);
        var result = _rate;
        _rate = rate;
        CopyTransformBlockContexts(contexts, plane, x4, y4, w4, h4, save: false);
        return result;
    }

    // The rate of the end of block and of every coded position: what changes with the levels in the coefficient syntax
    private long GetLevelsRate(ref LevelModel model, scoped ReadOnlySpan<byte> map, scoped ReadOnlySpan<int> levels, Av1Scan scan, int eob)
    {
        var rate = GetEobRate(ref model, eob);
        for (var i = 0; i < eob; i++)
        {
            rate += GetPositionRate(ref model, map, levels, scan[i], i, eob);
        }

        return rate;
    }
#endif

    // Writes the coded positions before limit in the scan order whose contexts read the level of a position
    private static int CollectDependents(scoped ReadOnlySpan<short> indexes, ReadOnlySpan<sbyte> dependents, int bwl, int row, int col, int limit, scoped Span<int> positions)
    {
        var count = 0;
        for (var k = 0; k < dependents.Length; k += 2)
        {
            var r = row + dependents[k];
            var c = col + dependents[k + 1];
            if (r < 0 || c < 0)
                continue;

            var dependent = (r << bwl) + c;
            if (indexes[dependent] < limit)
                positions[count++] = dependent;
        }

        return count;
    }

    // The rate of coeff_base or coeff_base_eob, coeff_br, the sign and the Golomb part of a coded position
    private readonly int GetPositionRate(ref LevelModel model, scoped ReadOnlySpan<byte> map, scoped ReadOnlySpan<int> levels, int pos, int index, int eob)
    {
        const int MaxCodedLevel = Av1Constants.NumBaseLevels + Av1Constants.CoeffBaseRange + 1;
        var row = pos >> model.Bwl;
        var col = pos & (model.Width - 1);
        var value = levels[pos];
        var magnitude = Math.Abs(value);
        var level = Math.Min(magnitude, Av1Constants.NumBaseLevels + 1);
        int rate;
        if (index == eob - 1)
        {
            var ctx = GetCoeffBaseEobContext(index, model.Width, model.Height);
            rate = Av1Cost.GetSymbolCost(_cdf.Slice(model.BaseEobOffset + (ctx * Av1CdfLayout.CoeffBaseEobSize), Av1CdfLayout.CoeffBaseEobSize), level - 1);
        }
        else
        {
            var ctx = GetCoeffBaseContext(map, model.TxSz, model.TxClass, model.Stride, row, col);
            rate = Av1Cost.GetSymbolCost(_cdf.Slice(model.BaseOffset + (ctx * Av1CdfLayout.CoeffBaseSize), Av1CdfLayout.CoeffBaseSize), level);
        }

        if (level > Av1Constants.NumBaseLevels)
        {
            var cdf = _cdf.Slice(model.BrOffset + (GetCoeffBrContext(map, model.TxClass, model.Stride, row, col) * Av1CdfLayout.CoeffBrSize), Av1CdfLayout.CoeffBrSize);
            for (var idx = 0; idx < Av1Constants.CoeffBaseRange / (Av1Constants.BrCdfSize - 1); idx++)
            {
                var coeffBr = Math.Min(magnitude - level, Av1Constants.BrCdfSize - 1);
                rate += Av1Cost.GetSymbolCost(cdf, coeffBr);
                level += coeffBr;
                if (coeffBr < Av1Constants.BrCdfSize - 1)
                    break;
            }
        }

        if (value is not 0)
        {
            rate += index is 0 ? Av1Cost.GetSymbolCost(_cdf.Slice(model.DcSignOffset, Av1CdfLayout.DcSignSize), value < 0 ? 1 : 0) : Av1Cost.GetLiteralCost(1);
            if (magnitude >= MaxCodedLevel)
            {
                var length = BitOperations.Log2((uint)(magnitude - (MaxCodedLevel - 1))) + 1;
                rate += Av1Cost.GetLiteralCost((2 * length) - 1);
            }
        }

        return rate;
    }

    private long GetEobRate(ref LevelModel model, int eob)
    {
        var rate = _rate;
        _rate = 0;
        WriteEob(model.TxSz, model.TxSzCtx, model.Ptype, model.TxClass, eob);
        var result = _rate;
        _rate = rate;
        return result;
    }

    // The distributions of the coefficient syntax of a transform block, and the geometry of its level map
    [StructLayout(LayoutKind.Auto)]
    private struct LevelModel
    {
        public int Width;
        public int Height;
        public int Bwl;
        public int Stride;
        public int TxSz;
        public int TxSzCtx;
        public int Ptype;
        public int TxClass;
        public int BaseOffset;
        public int BaseEobOffset;
        public int BrOffset;
        public int DcSignOffset;
    }
}
