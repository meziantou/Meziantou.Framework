namespace Meziantou.Framework.Imaging.Internals;

/// <content>The palette of a block (AV1 specification, sections 5.11.46, 5.11.49, 5.11.50 and 7.11.4).</content>
internal ref partial struct Av1TileCoder
{
    private const int ColorMapStride = 64;

    // Section 5.11.46 "Palette mode info syntax"
    private void PaletteModeInfo()
    {
        var bsizeCtx = Av1Tables.MiWidthLog2[_miSize] + Av1Tables.MiHeightLog2[_miSize] - 2;
        var maxSample = (1 << _bitDepth) - 1;
        Span<ushort> cache = stackalloc ushort[2 * Av1Constants.PaletteColors];
        if (_yMode is Av1Constants.DcPred)
        {
            var hasPaletteY = ReadSymbol(Av1CdfLayout.PaletteYMode + (((bsizeCtx * Av1Constants.PaletteYModeContexts) + GetPaletteYModeContext()) * Av1CdfLayout.PaletteYModeSize), Av1CdfLayout.PaletteYModeSize) is not 0;
            if (hasPaletteY)
            {
                _paletteSizeY = ReadSymbol(Av1CdfLayout.PaletteYSize + (bsizeCtx * Av1CdfLayout.PaletteYSizeSize), Av1CdfLayout.PaletteYSizeSize) + 2;
                var colors = _scratch.GetPaletteColors(0);
                var cacheN = GetPaletteCache(0, cache);
                var idx = 0;
                for (var i = 0; i < cacheN && idx < _paletteSizeY; i++)
                {
                    if (ReadLiteral(1) is not 0)
                        colors[idx++] = cache[i];
                }

                if (idx < _paletteSizeY)
                {
                    colors[idx++] = (ushort)ReadLiteral(_bitDepth);
                    if (idx < _paletteSizeY)
                    {
                        var paletteBits = _bitDepth - 3 + ReadLiteral(2);
                        while (idx < _paletteSizeY)
                        {
                            var delta = ReadLiteral(paletteBits) + 1;
                            colors[idx] = (ushort)Math.Clamp(colors[idx - 1] + delta, 0, maxSample);
                            var range = (1 << _bitDepth) - colors[idx] - 1;
                            paletteBits = Math.Min(paletteBits, CeilLog2(range));
                            idx++;
                        }
                    }
                }

                colors[.._paletteSizeY].Sort();
            }
        }

        if (_hasChroma && _uvMode is Av1Constants.DcPred)
        {
            var ctx = _paletteSizeY > 0 ? 1 : 0;
            var hasPaletteUv = ReadSymbol(Av1CdfLayout.PaletteUvMode + (ctx * Av1CdfLayout.PaletteUvModeSize), Av1CdfLayout.PaletteUvModeSize) is not 0;
            if (hasPaletteUv)
            {
                _paletteSizeUv = ReadSymbol(Av1CdfLayout.PaletteUvSize + (bsizeCtx * Av1CdfLayout.PaletteUvSizeSize), Av1CdfLayout.PaletteUvSizeSize) + 2;
                var colorsU = _scratch.GetPaletteColors(1);
                var cacheN = GetPaletteCache(1, cache);
                var idx = 0;
                for (var i = 0; i < cacheN && idx < _paletteSizeUv; i++)
                {
                    if (ReadLiteral(1) is not 0)
                        colorsU[idx++] = cache[i];
                }

                if (idx < _paletteSizeUv)
                {
                    colorsU[idx++] = (ushort)ReadLiteral(_bitDepth);
                    if (idx < _paletteSizeUv)
                    {
                        var paletteBits = _bitDepth - 3 + ReadLiteral(2);
                        while (idx < _paletteSizeUv)
                        {
                            var delta = ReadLiteral(paletteBits);
                            colorsU[idx] = (ushort)Math.Clamp(colorsU[idx - 1] + delta, 0, maxSample);
                            var range = (1 << _bitDepth) - colorsU[idx];
                            paletteBits = Math.Min(paletteBits, CeilLog2(range));
                            idx++;
                        }
                    }
                }

                colorsU[.._paletteSizeUv].Sort();

                var colorsV = _scratch.GetPaletteColors(2);
                if (ReadLiteral(1) is not 0)
                {
                    // delta_encode_palette_colors_v
                    var maxVal = 1 << _bitDepth;
                    var paletteBits = _bitDepth - 4 + ReadLiteral(2);
                    colorsV[0] = (ushort)ReadLiteral(_bitDepth);
                    for (idx = 1; idx < _paletteSizeUv; idx++)
                    {
                        var delta = ReadLiteral(paletteBits);
                        if (delta is not 0 && ReadLiteral(1) is not 0)
                            delta = -delta;

                        var val = colorsV[idx - 1] + delta;
                        if (val < 0)
                            val += maxVal;

                        if (val >= maxVal)
                            val -= maxVal;

                        colorsV[idx] = (ushort)Math.Clamp(val, 0, maxSample);
                    }
                }
                else
                {
                    for (idx = 0; idx < _paletteSizeUv; idx++)
                    {
                        colorsV[idx] = (ushort)ReadLiteral(_bitDepth);
                    }
                }
            }
        }
    }

    // The CDF selection of has_palette_y (section 8.3.2)
    private readonly int GetPaletteYModeContext()
    {
        var ctx = 0;
        if (_availU && (_paletteSizes[MiIndex(_miRow - 1, _miCol)] & 0xF) > 0)
            ctx++;

        if (_availL && (_paletteSizes[MiIndex(_miRow, _miCol - 1)] & 0xF) > 0)
            ctx++;

        return ctx;
    }

    private static int CeilLog2(int x)
    {
        if (x < 2)
            return 0;

        var i = 1;
        var p = 2;
        while (p < x)
        {
            i++;
            p <<= 1;
        }

        return i;
    }

    // get_palette_cache: the sorted union of the palettes of the block above and of the block to the left
    private readonly int GetPaletteCache(int plane, Span<ushort> cache)
    {
        var shift = plane * 4;
        var aboveN = 0;
        if (((_miRow * Av1Constants.MiSize) % 64) is not 0)
            aboveN = (_paletteSizes[MiIndex(_miRow - 1, _miCol)] >> shift) & 0xF;

        var leftN = 0;
        if (_availL)
            leftN = (_paletteSizes[MiIndex(_miRow, _miCol - 1)] >> shift) & 0xF;

        var above = _state.AbovePaletteColors.Slice((plane * Av1Constants.PaletteColors * (_miCols + Av1FrameState.AboveContextMargin)) + (_miCol * Av1Constants.PaletteColors), Av1Constants.PaletteColors);
        var left = _scratch.GetLeftPaletteColors(plane).Slice((_miRow & 31) * Av1Constants.PaletteColors, Av1Constants.PaletteColors);
        var aboveIdx = 0;
        var leftIdx = 0;
        var n = 0;
        while (aboveIdx < aboveN && leftIdx < leftN)
        {
            var aboveC = above[aboveIdx];
            var leftC = left[leftIdx];
            if (leftC < aboveC)
            {
                if (n is 0 || leftC != cache[n - 1])
                    cache[n++] = leftC;

                leftIdx++;
            }
            else
            {
                if (n is 0 || aboveC != cache[n - 1])
                    cache[n++] = aboveC;

                aboveIdx++;
                if (leftC == aboveC)
                    leftIdx++;
            }
        }

        while (aboveIdx < aboveN)
        {
            var val = above[aboveIdx++];
            if (n is 0 || val != cache[n - 1])
                cache[n++] = val;
        }

        while (leftIdx < leftN)
        {
            var val = left[leftIdx++];
            if (n is 0 || val != cache[n - 1])
                cache[n++] = val;
        }

        return n;
    }

    // Section 5.11.49 "Palette tokens syntax"
    private readonly void PaletteTokens()
    {
        if (_paletteSizeY is not 0)
        {
            GetColorMapSize(isLuma: true, out var blockWidth, out var blockHeight, out var onscreenWidth, out var onscreenHeight);
            ReadColorMap(_scratch.GetColorMap(0), _paletteSizeY, blockWidth, blockHeight, onscreenWidth, onscreenHeight, isLuma: true);
        }

        if (_paletteSizeUv is not 0)
        {
            GetColorMapSize(isLuma: false, out var blockWidth, out var blockHeight, out var onscreenWidth, out var onscreenHeight);
            ReadColorMap(_scratch.GetColorMap(1), _paletteSizeUv, blockWidth, blockHeight, onscreenWidth, onscreenHeight, isLuma: false);
        }
    }

    private readonly void ReadColorMap(Span<byte> colorMap, int paletteSize, int blockWidth, int blockHeight, int onscreenWidth, int onscreenHeight, bool isLuma)
    {
        var cdfOffset = GetPaletteColorCdf(paletteSize, isLuma);
        var cdfSize = paletteSize + 1;
        Span<byte> colorOrder = stackalloc byte[Av1Constants.PaletteColors];
        colorMap[0] = (byte)_symbols.ReadNonSymmetric(paletteSize);
        for (var i = 1; i < onscreenHeight + onscreenWidth - 1; i++)
        {
            for (var j = Math.Min(i, onscreenWidth - 1); j >= Math.Max(0, i - onscreenHeight + 1); j--)
            {
                var hash = GetPaletteColorContext(colorMap, i - j, j, paletteSize, colorOrder);
                var ctx = Av1Tables.PaletteColorContext[hash];
                var index = ReadSymbol(cdfOffset + (ctx * cdfSize), cdfSize);
                colorMap[((i - j) * ColorMapStride) + j] = colorOrder[index];
            }
        }

        ExtendColorMap(colorMap, blockWidth, blockHeight, onscreenWidth, onscreenHeight);
    }

    // The colors of the samples of the block that are not in the frame: the ones of the last column and of the last row
    private static void ExtendColorMap(Span<byte> colorMap, int blockWidth, int blockHeight, int onscreenWidth, int onscreenHeight)
    {
        for (var i = 0; i < onscreenHeight; i++)
        {
            var row = colorMap.Slice(i * ColorMapStride, blockWidth);
            row[onscreenWidth..].Fill(row[onscreenWidth - 1]);
        }

        for (var i = onscreenHeight; i < blockHeight; i++)
        {
            colorMap.Slice((onscreenHeight - 1) * ColorMapStride, blockWidth).CopyTo(colorMap[(i * ColorMapStride)..]);
        }
    }

    // The size of the color map of the luma plane (isLuma) or of the chroma planes of the block, and of its part that is
    // in the frame (section 5.11.49)
    private readonly void GetColorMapSize(bool isLuma, out int blockWidth, out int blockHeight, out int onscreenWidth, out int onscreenHeight)
    {
        blockHeight = BlockHeight(_miSize);
        blockWidth = BlockWidth(_miSize);
        onscreenHeight = Math.Min(blockHeight, (_miRows - _miRow) * Av1Constants.MiSize);
        onscreenWidth = Math.Min(blockWidth, (_miCols - _miCol) * Av1Constants.MiSize);
        if (isLuma)
            return;

        blockHeight >>= _subY;
        blockWidth >>= _subX;
        onscreenHeight >>= _subY;
        onscreenWidth >>= _subX;
        if (blockWidth < 4)
        {
            blockWidth += 2;
            onscreenWidth += 2;
        }

        if (blockHeight < 4)
        {
            blockHeight += 2;
            onscreenHeight += 2;
        }
    }

    private static int GetPaletteColorCdf(int paletteSize, bool isLuma) => (paletteSize, isLuma) switch
    {
        (2, true) => Av1CdfLayout.PaletteSize2YColor,
        (3, true) => Av1CdfLayout.PaletteSize3YColor,
        (4, true) => Av1CdfLayout.PaletteSize4YColor,
        (5, true) => Av1CdfLayout.PaletteSize5YColor,
        (6, true) => Av1CdfLayout.PaletteSize6YColor,
        (7, true) => Av1CdfLayout.PaletteSize7YColor,
        (_, true) => Av1CdfLayout.PaletteSize8YColor,
        (2, false) => Av1CdfLayout.PaletteSize2UvColor,
        (3, false) => Av1CdfLayout.PaletteSize3UvColor,
        (4, false) => Av1CdfLayout.PaletteSize4UvColor,
        (5, false) => Av1CdfLayout.PaletteSize5UvColor,
        (6, false) => Av1CdfLayout.PaletteSize6UvColor,
        (7, false) => Av1CdfLayout.PaletteSize7UvColor,
        _ => Av1CdfLayout.PaletteSize8UvColor,
    };

    // Section 5.11.50 "Palette color context function": returns ColorContextHash and fills ColorOrder
    private static int GetPaletteColorContext(ReadOnlySpan<byte> colorMap, int r, int c, int n, Span<byte> colorOrder)
    {
        Span<int> scores = stackalloc int[Av1Constants.PaletteColors];
        for (var i = 0; i < Av1Constants.PaletteColors; i++)
        {
            colorOrder[i] = (byte)i;
        }

        if (c > 0)
            scores[colorMap[(r * ColorMapStride) + c - 1]] += 2;

        if (r > 0 && c > 0)
            scores[colorMap[((r - 1) * ColorMapStride) + c - 1]] += 1;

        if (r > 0)
            scores[colorMap[((r - 1) * ColorMapStride) + c]] += 2;

        for (var i = 0; i < Av1Constants.PaletteNumNeighbors; i++)
        {
            var maxScore = scores[i];
            var maxIdx = i;
            for (var j = i + 1; j < n; j++)
            {
                if (scores[j] > maxScore)
                {
                    maxScore = scores[j];
                    maxIdx = j;
                }
            }

            if (maxIdx != i)
            {
                maxScore = scores[maxIdx];
                var maxColorOrder = colorOrder[maxIdx];
                for (var k = maxIdx; k > i; k--)
                {
                    scores[k] = scores[k - 1];
                    colorOrder[k] = colorOrder[k - 1];
                }

                scores[i] = maxScore;
                colorOrder[i] = maxColorOrder;
            }
        }

        var hash = 0;
        for (var i = 0; i < Av1Constants.PaletteNumNeighbors; i++)
        {
            hash += scores[i] * Av1Tables.PaletteColorHashMultipliers[i];
        }

        return hash;
    }

    // Section 7.11.4 "Palette prediction process"
    private readonly void PredictPalette(Span<ushort> samples, int stride, int plane, int startX, int startY, int x, int y, int txSz)
    {
        var w = Av1Tables.TxWidth[txSz];
        var h = Av1Tables.TxHeight[txSz];
        var palette = _scratch.GetPaletteColors(plane);
        var map = _scratch.GetColorMap(plane is 0 ? 0 : 1);
        for (var i = 0; i < h; i++)
        {
            var row = samples.Slice(((startY + i) * stride) + startX, w);
            var mapRow = map.Slice((((y * 4) + i) * ColorMapStride) + (x * 4), w);
            for (var j = 0; j < w; j++)
            {
                row[j] = palette[mapRow[j]];
            }
        }
    }
}
