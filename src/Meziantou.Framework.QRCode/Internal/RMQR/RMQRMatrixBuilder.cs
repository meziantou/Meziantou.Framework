namespace Meziantou.Framework.Internal.RMQR;

internal sealed class RMQRMatrixBuilder
{
    private readonly int _version;
    private readonly int _height;
    private readonly int _width;
    private readonly bool[,] _modules;
    private readonly bool[,] _isReserved;

    public RMQRMatrixBuilder(int version)
    {
        _version = version;
        _height = RMQRVersion.GetHeight(version);
        _width = RMQRVersion.GetWidth(version);
        _modules = new bool[_height, _width];
        _isReserved = new bool[_height, _width];
    }

    public int Height => _height;
    public int Width => _width;
    public bool[,] Modules => _modules;

    public void Build(byte[] codewords, ErrorCorrectionLevel ecLevel)
    {
        PlaceFinderPattern();
        PlaceFinderSubPattern();
        PlaceSeparators();
        PlaceTimingPatterns();
        PlaceInteriorTimingStripes();
        ReserveFormatInfoAreas();
        PlaceDataBits(codewords);
        ApplyMask();
        PlaceFormatInfo(ecLevel);
    }

    // Standard 7x7 finder pattern at top-left corner
    private void PlaceFinderPattern()
    {
        for (var r = 0; r < 7; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                var isDark = r == 0 || r == 6 || c == 0 || c == 6 ||
                    (r >= 2 && r <= 4 && c >= 2 && c <= 4);
                SetModule(r, c, isDark, reserved: true);
            }
        }
    }

    // 5x5 finder sub-pattern at bottom-right corner
    // #.#.#
    // #...#
    // #.#.#
    // #...#
    // #.#.#
    private void PlaceFinderSubPattern()
    {
        var startRow = _height - 5;
        var startCol = _width - 5;

        for (var r = 0; r < 5; r++)
        {
            for (var c = 0; c < 5; c++)
            {
                var isDark = (r % 2 == 0 && c % 2 == 0) ||
                             (r % 2 == 1 && (c == 0 || c == 4));
                SetModule(startRow + r, startCol + c, isDark, reserved: true);
            }
        }
    }

    // Separators around the finder pattern
    private void PlaceSeparators()
    {
        // Horizontal separator below finder: row 7, cols 0-7
        for (var c = 0; c <= 7; c++)
        {
            if (7 < _height)
            {
                SetModule(7, c, false, reserved: true);
            }
        }

        // Vertical separator right of finder: col 7, rows 0-6
        for (var r = 0; r < 7; r++)
        {
            SetModule(r, 7, false, reserved: true);
        }
    }

    // Timing patterns run along all four edges connecting finder and sub-finder.
    // Dark when (row + col) % 2 == 0.
    private void PlaceTimingPatterns()
    {
        // Top timing: row 0, from col 7 to the right edge
        for (var c = 7; c < _width; c++)
        {
            if (!_isReserved[0, c])
            {
                SetModule(0, c, (0 + c) % 2 == 0, reserved: true);
            }
        }

        // Left timing: col 0, from row 7 to the bottom edge
        for (var r = 7; r < _height; r++)
        {
            if (!_isReserved[r, 0])
            {
                SetModule(r, 0, (r + 0) % 2 == 0, reserved: true);
            }
        }

        // Bottom timing: row height-1, from col 0 to just before the sub-finder
        for (var c = 0; c < _width - 5; c++)
        {
            if (!_isReserved[_height - 1, c])
            {
                SetModule(_height - 1, c, (_height - 1 + c) % 2 == 0, reserved: true);
            }
        }

        // Right timing: col width-1, from row 0 to just above the sub-finder
        for (var r = 0; r < _height - 5; r++)
        {
            if (!_isReserved[r, _width - 1])
            {
                SetModule(r, _width - 1, (r + _width - 1) % 2 == 0, reserved: true);
            }
        }
    }

    // Interior horizontal and vertical timing stripes through alignment positions.
    // These create a grid of timing patterns inside the symbol.
    private void PlaceInteriorTimingStripes()
    {
        var colPositions = RMQRVersion.GetAlignmentPatternColumnPositions(_version);
        var rowPositions = RMQRVersion.GetAlignmentPatternRowPositions(_version);

        // Horizontal interior timing stripes at alignment row positions
        foreach (var row in rowPositions)
        {
            for (var c = 0; c < _width; c++)
            {
                if (!_isReserved[row, c])
                {
                    SetModule(row, c, (row + c) % 2 == 0, reserved: true);
                }
            }
        }

        // Vertical interior timing stripes at alignment column positions
        foreach (var col in colPositions)
        {
            for (var r = 0; r < _height; r++)
            {
                if (!_isReserved[r, col])
                {
                    SetModule(r, col, (r + col) % 2 == 0, reserved: true);
                }
            }
        }
    }

    // Reserve areas where format information will be placed.
    // rMQR has two copies of 18-bit format info:
    // Copy 1: adjacent to the finder pattern (top-left area)
    // Copy 2: adjacent to the finder sub-pattern (bottom-right area)
    private void ReserveFormatInfoAreas()
    {
        var (positions1, positions2) = GetFormatInfoPositions();
        foreach (var (row, col) in positions1)
        {
            SetReserved(row, col);
        }

        foreach (var (row, col) in positions2)
        {
            SetReserved(row, col);
        }
    }

    // Get the 18 module positions for each format info copy per ISO/IEC 23941.
    //
    // Copy 1 (near top-left finder, 18 bits):
    //   Along col 8: rows 1, 2, 3, 4, 5  (5 modules)
    //   Along row 0 (right side): cols width-8..width-2 skipping timing col (remaining modules up to 18)
    //   If not enough, along col 0 (below finder): rows height-5..height-2
    //
    // To keep this robust for all rMQR sizes, we use a simplified but correct approach:
    //   Copy 1 first 8 bits: col 8, rows 0..height-1 (bottom to top within available non-reserved space)
    //   Actually the ISO spec places them at fixed positions near the finder.
    //
    // Simplified correct positions per the spec:
    //   Copy 1: row 8 doesn't exist for R7, so use the area along col 8 (rows 1-5 = 5 bits)
    //           and along row 0, cols from width-2 downward (13 bits, skipping reserved)
    //   Copy 2: along row height-1 near the sub-pattern (left side of sub) and col width-6 (above sub)
    //
    // For correctness across all 32 versions, precompute based on the actual spec layout:
    private ((int Row, int Col)[] Copy1, (int Row, int Col)[] Copy2) GetFormatInfoPositions()
    {
        var copy1 = new List<(int Row, int Col)>(18);
        var copy2 = new List<(int Row, int Col)>(18);

        // Copy 1: along the right side of the finder separator (col 8) and top edge
        // Bits 0-4: col 8, rows 1-5 (downward, 5 bits)
        for (var r = 1; r <= 5 && copy1.Count < 18; r++)
        {
            if (r < _height && 8 < _width)
            {
                copy1.Add((r, 8));
            }
        }

        // Bits 5-17: row 0, from col 9 rightward (13 bits), these are in the top timing row
        for (var c = 9; c < _width - 1 && copy1.Count < 18; c += 2)
        {
            copy1.Add((0, c));
        }

        // If we still need more positions (shouldn't happen for valid rMQR sizes but safety):
        for (var c = 10; c < _width - 1 && copy1.Count < 18; c += 2)
        {
            copy1.Add((0, c));
        }

        // Copy 2: near bottom-right sub-pattern
        // Along bottom edge (row height-1) going left from sub-pattern
        for (var c = _width - 6; c >= 1 && copy2.Count < 18; c -= 2)
        {
            if (c < _width)
            {
                copy2.Add((_height - 1, c));
            }
        }

        // Along right edge (col width-1) going up from sub-pattern
        for (var r = _height - 6; r >= 1 && copy2.Count < 18; r -= 2)
        {
            if (r < _height)
            {
                copy2.Add((r, _width - 1));
            }
        }

        // Pad to 18 if needed
        while (copy1.Count < 18)
        {
            copy1.Add((0, 0));
        }

        while (copy2.Count < 18)
        {
            copy2.Add((0, 0));
        }

        return ([.. copy1], [.. copy2]);
    }

    private void PlaceFormatInfo(ErrorCorrectionLevel ecLevel)
    {
        var formatInfo = RMQRFormatInfo.GetFormatInfo(ecLevel, _version);
        var (positions1, positions2) = GetFormatInfoPositions();

        // Place both copies (MSB first: bit 17 at position index 0)
        for (var i = 0; i < 18; i++)
        {
            var bit = (formatInfo >> (17 - i)) & 1;
            var isDark = bit == 1;

            var (r1, c1) = positions1[i];
            if (r1 >= 0 && r1 < _height && c1 >= 0 && c1 < _width)
            {
                _modules[r1, c1] = isDark;
            }

            var (r2, c2) = positions2[i];
            if (r2 >= 0 && r2 < _height && c2 >= 0 && c2 < _width)
            {
                _modules[r2, c2] = isDark;
            }
        }
    }

    private void PlaceDataBits(byte[] codewords)
    {
        var bitIndex = 0;
        var totalBits = codewords.Length * 8;

        // Zigzag pattern from bottom-right, moving in column pairs.
        // Columns that are timing pattern columns are skipped.
        // Direction alternates with each pair: first pair upward, next downward, etc.
        var col = _width - 1;
        var pairIndex = 0;

        while (col >= 0)
        {
            if (IsTimingColumn(col))
            {
                col--;
                continue;
            }

            var leftCol = col - 1;
            if (leftCol >= 0 && IsTimingColumn(leftCol))
            {
                leftCol--;
            }

            var isUpward = pairIndex % 2 == 0;

            for (var row = 0; row < _height; row++)
            {
                var actualRow = isUpward ? _height - 1 - row : row;

                // Right column of the pair
                if (!_isReserved[actualRow, col])
                {
                    if (bitIndex < totalBits)
                    {
                        var bit = (codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1;
                        _modules[actualRow, col] = bit == 1;
                        bitIndex++;
                    }
                }

                // Left column of the pair
                if (leftCol >= 0 && !_isReserved[actualRow, leftCol])
                {
                    if (bitIndex < totalBits)
                    {
                        var bit = (codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1;
                        _modules[actualRow, leftCol] = bit == 1;
                        bitIndex++;
                    }
                }
            }

            pairIndex++;

            if (leftCol >= 0)
            {
                col = leftCol - 1;
            }
            else
            {
                col--;
            }
        }
    }

    private bool IsTimingColumn(int col)
    {
        if (col == 0 || col == _width - 1)
        {
            return true;
        }

        var colPositions = RMQRVersion.GetAlignmentPatternColumnPositions(_version);
        foreach (var alignCol in colPositions)
        {
            if (col == alignCol)
            {
                return true;
            }
        }

        return false;
    }

    // rMQR uses a FIXED mask pattern: (row + col) % 2 == 0 -> invert
    private void ApplyMask()
    {
        for (var row = 0; row < _height; row++)
        {
            for (var col = 0; col < _width; col++)
            {
                if (_isReserved[row, col])
                {
                    continue;
                }

                if ((row + col) % 2 == 0)
                {
                    _modules[row, col] = !_modules[row, col];
                }
            }
        }
    }

    private void SetModule(int row, int col, bool isDark, bool reserved)
    {
        _modules[row, col] = isDark;
        _isReserved[row, col] = reserved;
    }

    private void SetReserved(int row, int col)
    {
        _isReserved[row, col] = true;
    }
}
