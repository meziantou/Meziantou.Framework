using System.Diagnostics;
using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>
/// The palettes of the encoder: the write walk of the palette syntax (AV1 specification, sections 5.11.46 and 5.11.49),
/// the palette that predicts a block exactly when the block has few colors, and the palette of a block record.
/// </content>
/// <remarks>
/// The colors of the palettes of the block being coded and the color of each of its samples are where the decoder has
/// them: in the scratch of the tile (<see cref="Av1TileScratch.GetPaletteColors"/> and
/// <see cref="Av1TileScratch.GetColorMap"/>).
/// </remarks>
internal ref partial struct Av1TileCoder
{
    // The parts of the palette syntax of a block: the luma plane, and both chroma planes
    private const int LumaPalette = 1;
    private const int ChromaPalette = 2;

    // has_palette_y and the palette of the luma plane (section 5.11.46 "Palette mode info syntax")
    private void WritePaletteModeInfoY()
    {
        if (_yMode is not Av1Constants.DcPred)
        {
            Debug.Assert(_paletteSizeY is 0);
            return;
        }

        var bsizeCtx = Av1Tables.MiWidthLog2[_miSize] + Av1Tables.MiHeightLog2[_miSize] - 2;
        WriteSymbol(Av1CdfLayout.PaletteYMode + (((bsizeCtx * Av1Constants.PaletteYModeContexts) + GetPaletteYModeContext()) * Av1CdfLayout.PaletteYModeSize), Av1CdfLayout.PaletteYModeSize, _paletteSizeY > 0 ? 1 : 0);
        if (_paletteSizeY is 0)
            return;

        WriteSymbol(Av1CdfLayout.PaletteYSize + (bsizeCtx * Av1CdfLayout.PaletteYSizeSize), Av1CdfLayout.PaletteYSizeSize, _paletteSizeY - 2);
        WritePaletteColors(plane: 0, _scratch.GetPaletteColors(0)[.._paletteSizeY]);
    }

    // has_palette_uv and the palette of the chroma planes (section 5.11.46 "Palette mode info syntax")
    private void WritePaletteModeInfoUv()
    {
        if (!_hasChroma || _uvMode is not Av1Constants.DcPred)
        {
            Debug.Assert(_paletteSizeUv is 0);
            return;
        }

        var bsizeCtx = Av1Tables.MiWidthLog2[_miSize] + Av1Tables.MiHeightLog2[_miSize] - 2;
        var ctx = _paletteSizeY > 0 ? 1 : 0;
        WriteSymbol(Av1CdfLayout.PaletteUvMode + (ctx * Av1CdfLayout.PaletteUvModeSize), Av1CdfLayout.PaletteUvModeSize, _paletteSizeUv > 0 ? 1 : 0);
        if (_paletteSizeUv is 0)
            return;

        WriteSymbol(Av1CdfLayout.PaletteUvSize + (bsizeCtx * Av1CdfLayout.PaletteUvSizeSize), Av1CdfLayout.PaletteUvSizeSize, _paletteSizeUv - 2);
        WritePaletteColors(plane: 1, _scratch.GetPaletteColors(1)[.._paletteSizeUv]);
        WritePaletteColorsV(_scratch.GetPaletteColors(2)[.._paletteSizeUv]);
    }

    // The colors of the palette of the luma plane or of the U plane, in increasing order: a flag for each color of the
    // cache (the palettes of the blocks above and to the left) until the palette is full, then the other colors as
    // differences. A color of the luma plane differs from the previous one; two colors of the U plane may be the same
    private void WritePaletteColors(int plane, scoped ReadOnlySpan<ushort> colors)
    {
        var minDelta = plane is 0 ? 1 : 0;
        Span<ushort> cache = stackalloc ushort[2 * Av1Constants.PaletteColors];
        var cacheN = GetPaletteCache(plane, cache);
        Span<bool> cached = stackalloc bool[Av1Constants.PaletteColors];
        var idx = 0;
        for (var i = 0; i < cacheN && idx < colors.Length; i++)
        {
            var found = -1;
            for (var j = 0; j < colors.Length; j++)
            {
                if (!cached[j] && colors[j] == cache[i])
                {
                    found = j;
                    break;
                }
            }

            WriteLiteral(found >= 0 ? 1 : 0, 1);
            if (found >= 0)
            {
                cached[found] = true;
                idx++;
            }
        }

        if (idx == colors.Length)
            return;

        // The number of bits of the largest difference: palette_num_extra_bits codes it from BitDepth - 3
        var bits = 0;
        var previous = -1;
        for (var j = 0; j < colors.Length; j++)
        {
            if (cached[j])
                continue;

            if (previous >= 0)
                bits = Math.Max(bits, GetBitLength(colors[j] - previous - minDelta));

            previous = colors[j];
        }

        var paletteBits = Math.Max(_bitDepth - 3, bits);
        previous = -1;
        for (var j = 0; j < colors.Length; j++)
        {
            if (cached[j])
                continue;

            if (previous < 0)
            {
                WriteLiteral(colors[j], _bitDepth);
                idx++;
                if (idx < colors.Length)
                    WriteLiteral(paletteBits - (_bitDepth - 3), 2);
            }
            else
            {
                // The differences that follow have as many bits as the values that are left need
                WriteLiteral(colors[j] - previous - minDelta, paletteBits);
                var range = (1 << _bitDepth) - colors[j] - minDelta;
                paletteBits = Math.Min(paletteBits, CeilLog2(range));
            }

            previous = colors[j];
        }
    }

    // The colors of the V plane, in the order of the colors of the U plane: as signed differences modulo the number of
    // values of a sample (delta_encode_palette_colors_v), or as they are, whichever takes fewer bits
    private void WritePaletteColorsV(scoped ReadOnlySpan<ushort> colors)
    {
        var maxVal = 1 << _bitDepth;
        Span<int> deltas = stackalloc int[Av1Constants.PaletteColors];
        var bits = 0;
        for (var i = 1; i < colors.Length; i++)
        {
            var delta = colors[i] - colors[i - 1];
            if (delta > maxVal / 2)
            {
                delta -= maxVal;
            }
            else if (delta < -(maxVal / 2))
            {
                delta += maxVal;
            }

            deltas[i] = delta;
            bits = Math.Max(bits, GetBitLength(Math.Abs(delta)));
        }

        // palette_num_extra_bits_v codes the number of bits of a difference from BitDepth - 4
        var paletteBits = Math.Max(_bitDepth - 4, bits);
        var deltaLength = 2 + _bitDepth;
        for (var i = 1; i < colors.Length; i++)
        {
            deltaLength += paletteBits + (deltas[i] is not 0 ? 1 : 0);
        }

        var usesDeltas = paletteBits <= _bitDepth - 1 && deltaLength < colors.Length * _bitDepth;
        WriteLiteral(usesDeltas ? 1 : 0, 1);
        if (!usesDeltas)
        {
            foreach (var color in colors)
            {
                WriteLiteral(color, _bitDepth);
            }

            return;
        }

        WriteLiteral(paletteBits - (_bitDepth - 4), 2);
        WriteLiteral(colors[0], _bitDepth);
        for (var i = 1; i < colors.Length; i++)
        {
            WriteLiteral(Math.Abs(deltas[i]), paletteBits);
            if (deltas[i] is not 0)
                WriteLiteral(deltas[i] < 0 ? 1 : 0, 1);
        }
    }

    private static int GetBitLength(int value) => value is 0 ? 0 : BitOperations.Log2((uint)value) + 1;

    // Section 5.11.49 "Palette tokens syntax" for the palettes of parts (LumaPalette, ChromaPalette)
    private void WritePaletteTokens(int parts)
    {
        if ((parts & LumaPalette) is not 0 && _paletteSizeY is not 0)
        {
            GetColorMapSize(isLuma: true, out _, out _, out var onscreenWidth, out var onscreenHeight);
            WriteColorMap(_scratch.GetColorMap(0), _paletteSizeY, onscreenWidth, onscreenHeight, isLuma: true);
        }

        if ((parts & ChromaPalette) is not 0 && _paletteSizeUv is not 0)
        {
            GetColorMapSize(isLuma: false, out _, out _, out var onscreenWidth, out var onscreenHeight);
            WriteColorMap(_scratch.GetColorMap(1), _paletteSizeUv, onscreenWidth, onscreenHeight, isLuma: false);
        }
    }

    // The colors of the samples of a color map that are in the frame: the first one, then along the diagonals, each one as
    // its rank among the colors ordered by the colors of the samples around it
    private void WriteColorMap(scoped ReadOnlySpan<byte> colorMap, int paletteSize, int onscreenWidth, int onscreenHeight, bool isLuma)
    {
        var cdfOffset = GetPaletteColorCdf(paletteSize, isLuma);
        var cdfSize = paletteSize + 1;
        Span<byte> colorOrder = stackalloc byte[Av1Constants.PaletteColors];
        WriteNonSymmetric(colorMap[0], paletteSize);
        for (var i = 1; i < onscreenHeight + onscreenWidth - 1; i++)
        {
            for (var j = Math.Min(i, onscreenWidth - 1); j >= Math.Max(0, i - onscreenHeight + 1); j--)
            {
                var hash = GetPaletteColorContext(colorMap, i - j, j, paletteSize, colorOrder);
                var ctx = Av1Tables.PaletteColorContext[hash];
                var index = colorOrder[..paletteSize].IndexOf(colorMap[((i - j) * ColorMapStride) + j]);
                Debug.Assert(index >= 0);
                WriteSymbol(cdfOffset + (ctx * cdfSize), cdfSize, index);
            }
        }
    }

    // A number in the range 0 to valueCount - 1 with the non-symmetric encoding (NS(n), section 4.10.10)
    private void WriteNonSymmetric(int value, int valueCount)
    {
        switch (_sink)
        {
            case SymbolSink.Cost:
                var w = BitOperations.Log2((uint)valueCount) + 1;
                var m = (1 << w) - valueCount;
                _rate += Av1Cost.GetLiteralCost(value < m ? w - 1 : w);
                break;
            case SymbolSink.Range:
                _encoder!.WriteNonSymmetric(value, valueCount);
                break;
        }
    }

    // The largest number of values of the luma samples of a block that a palette is searched for
    private const int MaxPaletteValues = 64;

    // The palette of the luma samples of the block: their values when there are 2 to 8 of them, which the palette then
    // predicts exactly; otherwise 8 groups of close values, each one predicted by its most frequent value. Returns the
    // number of colors, which are left with the color of each sample in the scratch of the tile; 0 when the samples have
    // one value, or too many
    private readonly int FindLumaPalette()
    {
        GetColorMapSize(isLuma: true, out var blockWidth, out var blockHeight, out var onscreenWidth, out var onscreenHeight);
        var source = _workspace!.GetSource(0);
        var samples = source.Samples;
        var stride = source.Stride;
        var x0 = _miCol * Av1Constants.MiSize;
        var y0 = _miRow * Av1Constants.MiSize;
        Span<ushort> values = stackalloc ushort[MaxPaletteValues];
        Span<int> counts = stackalloc int[MaxPaletteValues];
        var count = 0;
        for (var y = 0; y < onscreenHeight; y++)
        {
            foreach (var sample in samples.Slice(((y0 + y) * stride) + x0, onscreenWidth))
            {
                var index = values[..count].IndexOf(sample);
                if (index < 0)
                {
                    if (count is MaxPaletteValues)
                        return 0;

                    index = count++;
                    values[index] = sample;
                    counts[index] = 0;
                }

                counts[index]++;
            }
        }

        if (count < 2)
            return 0;

        // The values in increasing order, with the group of each one
        values = values[..count];
        counts = counts[..count];
        values.Sort(counts);
        Span<byte> groups = stackalloc byte[MaxPaletteValues];
        var size = GroupPaletteValues(values, counts, groups);
        var colors = _scratch.GetPaletteColors(0);
        for (var group = 0; group < size; group++)
        {
            // The most frequent value of the group: the smallest one of several
            var best = -1;
            for (var i = 0; i < count; i++)
            {
                if (groups[i] == group && (best < 0 || counts[i] > counts[best]))
                    best = i;
            }

            colors[group] = values[best];
        }

        var colorMap = _scratch.GetColorMap(0);
        for (var y = 0; y < onscreenHeight; y++)
        {
            var row = samples.Slice(((y0 + y) * stride) + x0, onscreenWidth);
            var mapRow = colorMap.Slice(y * ColorMapStride, onscreenWidth);
            for (var x = 0; x < row.Length; x++)
            {
                mapRow[x] = groups[values.BinarySearch(row[x])];
            }
        }

        ExtendColorMap(colorMap, blockWidth, blockHeight, onscreenWidth, onscreenHeight);
        return size;
    }

    // Divides values in increasing order into at most 8 groups of consecutive values: every value is a group, then the two
    // neighbors whose union adds the least squared error are joined, until 8 groups are left. Returns the number of groups
    private static int GroupPaletteValues(ReadOnlySpan<ushort> values, ReadOnlySpan<int> counts, Span<byte> groups)
    {
        var size = values.Length;
        Span<long> sums = stackalloc long[MaxPaletteValues];
        Span<long> weights = stackalloc long[MaxPaletteValues];
        Span<int> ends = stackalloc int[MaxPaletteValues];
        for (var i = 0; i < size; i++)
        {
            sums[i] = (long)values[i] * counts[i];
            weights[i] = counts[i];
            ends[i] = i + 1;
        }

        while (size > Av1Constants.PaletteColors)
        {
            // The error that joining two groups adds: the product of their weights over their sum, times the square of
            // the distance of their means. Compared as fractions with a common denominator, in integers
            var best = 0;
            Int128 bestNumerator = 0;
            Int128 bestDenominator = 0;
            for (var i = 0; i < size - 1; i++)
            {
                var difference = (Int128)((sums[i] * weights[i + 1]) - (sums[i + 1] * weights[i]));
                var numerator = difference * difference;
                var denominator = (Int128)weights[i] * weights[i + 1] * (weights[i] + weights[i + 1]);
                if (i is 0 || numerator * bestDenominator < bestNumerator * denominator)
                {
                    best = i;
                    bestNumerator = numerator;
                    bestDenominator = denominator;
                }
            }

            sums[best] += sums[best + 1];
            weights[best] += weights[best + 1];
            ends[best] = ends[best + 1];
            for (var i = best + 1; i < size - 1; i++)
            {
                sums[i] = sums[i + 1];
                weights[i] = weights[i + 1];
                ends[i] = ends[i + 1];
            }

            size--;
        }

        var start = 0;
        for (var group = 0; group < size; group++)
        {
            groups[start..ends[group]].Fill((byte)group);
            start = ends[group];
        }

        return size;
    }

    // The palette of the chroma samples of the block, when they have 2 to 8 pairs of values: see FindLumaPalette. The
    // colors are ordered by their U value, like the decoder orders them
    private readonly int FindChromaPalette()
    {
        GetColorMapSize(isLuma: false, out var blockWidth, out var blockHeight, out var onscreenWidth, out var onscreenHeight);
        var workspace = _workspace!;
        var sourceU = workspace.GetSource(1);
        var sourceV = workspace.GetSource(2);
        var stride = sourceU.Stride;
        var x0 = (_miCol >> _subX) * Av1Constants.MiSize;
        var y0 = (_miRow >> _subY) * Av1Constants.MiSize;
        Span<uint> colors = stackalloc uint[Av1Constants.PaletteColors];
        var count = 0;
        for (var y = 0; y < onscreenHeight; y++)
        {
            var rowU = sourceU.Samples.Slice(((y0 + y) * stride) + x0, onscreenWidth);
            var rowV = sourceV.Samples.Slice(((y0 + y) * stride) + x0, onscreenWidth);
            for (var x = 0; x < rowU.Length; x++)
            {
                var color = ((uint)rowU[x] << 16) | rowV[x];
                if (colors[..count].Contains(color))
                    continue;

                if (count is Av1Constants.PaletteColors)
                    return 0;

                colors[count++] = color;
            }
        }

        if (count < 2)
            return 0;

        colors = colors[..count];
        colors.Sort();
        var colorsU = _scratch.GetPaletteColors(1);
        var colorsV = _scratch.GetPaletteColors(2);
        for (var i = 0; i < count; i++)
        {
            colorsU[i] = (ushort)(colors[i] >> 16);
            colorsV[i] = (ushort)colors[i];
        }

        var colorMap = _scratch.GetColorMap(1);
        for (var y = 0; y < onscreenHeight; y++)
        {
            var rowU = sourceU.Samples.Slice(((y0 + y) * stride) + x0, onscreenWidth);
            var rowV = sourceV.Samples.Slice(((y0 + y) * stride) + x0, onscreenWidth);
            var mapRow = colorMap.Slice(y * ColorMapStride, onscreenWidth);
            for (var x = 0; x < rowU.Length; x++)
            {
                mapRow[x] = (byte)colors.IndexOf(((uint)rowU[x] << 16) | rowV[x]);
            }
        }

        ExtendColorMap(colorMap, blockWidth, blockHeight, onscreenWidth, onscreenHeight);
        return count;
    }

    // Appends the palettes of the block to its record: the colors of the luma palette and the color of each luma sample of
    // the frame, then the same for the chroma planes. Returns the number of values
    private readonly int RecordPalettes(int position)
    {
        var workspace = _workspace!;
        var start = position;
        for (var part = 0; part < 2; part++)
        {
            var size = part is 0 ? _paletteSizeY : _paletteSizeUv;
            if (size is 0)
                continue;

            GetColorMapSize(isLuma: part is 0, out _, out _, out var onscreenWidth, out var onscreenHeight);
            workspace.EnsureRecords(position, (2 * size) + (onscreenWidth * onscreenHeight));
            var records = workspace.Records;
            for (var plane = part; plane <= 2 * part; plane++)
            {
                foreach (var color in _scratch.GetPaletteColors(plane)[..size])
                {
                    records[position++] = color;
                }
            }

            var colorMap = _scratch.GetColorMap(part);
            for (var y = 0; y < onscreenHeight; y++)
            {
                foreach (var index in colorMap.Slice(y * ColorMapStride, onscreenWidth))
                {
                    records[position++] = index;
                }
            }
        }

        return position - start;
    }

    // Puts the palettes of a block record back where the palette syntax and the prediction read them
    private readonly void LoadPalettes(int position)
    {
        var records = _workspace!.Records;
        for (var part = 0; part < 2; part++)
        {
            var size = part is 0 ? _paletteSizeY : _paletteSizeUv;
            if (size is 0)
                continue;

            GetColorMapSize(isLuma: part is 0, out var blockWidth, out var blockHeight, out var onscreenWidth, out var onscreenHeight);
            for (var plane = part; plane <= 2 * part; plane++)
            {
                var colors = _scratch.GetPaletteColors(plane);
                for (var i = 0; i < size; i++)
                {
                    colors[i] = (ushort)records[position++];
                }
            }

            var colorMap = _scratch.GetColorMap(part);
            for (var y = 0; y < onscreenHeight; y++)
            {
                var row = colorMap.Slice(y * ColorMapStride, onscreenWidth);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = (byte)records[position++];
                }
            }

            ExtendColorMap(colorMap, blockWidth, blockHeight, onscreenWidth, onscreenHeight);
        }
    }
}
