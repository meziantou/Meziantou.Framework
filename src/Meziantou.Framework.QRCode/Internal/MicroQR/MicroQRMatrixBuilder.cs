namespace Meziantou.Framework.Internal.MicroQR;

internal sealed class MicroQRMatrixBuilder
{
    private readonly int _version;
    private readonly int _size;
    private readonly bool[,] _modules;
    private readonly bool[,] _isReserved;

    public MicroQRMatrixBuilder(int version)
    {
        _version = version;
        _size = MicroQRVersion.GetSideLength(version);
        _modules = new bool[_size, _size];
        _isReserved = new bool[_size, _size];
    }

    public int Size => _size;
    public bool[,] Modules => _modules;
    public bool[,] IsReserved => _isReserved;

    public void Build(byte[] codewords, ErrorCorrectionLevel ecLevel, int maskPattern)
    {
        PlaceFinderPattern();
        PlaceSeparators();
        PlaceTimingPatterns();
        ReserveFormatInfoArea();
        PlaceDataBits(codewords);
        ApplyMask(maskPattern);
        PlaceFormatInfo(ecLevel, maskPattern);
    }

    private void PlaceFinderPattern()
    {
        // Single finder pattern at top-left (0,0), 7x7
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

    private void PlaceSeparators()
    {
        // Horizontal separator: row 7, cols 0-7
        for (var c = 0; c <= 7; c++)
        {
            SetModule(7, c, false, reserved: true);
        }

        // Vertical separator: col 7, rows 0-7 (row 7 already set above)
        for (var r = 0; r < 7; r++)
        {
            SetModule(r, 7, false, reserved: true);
        }
    }

    private void PlaceTimingPatterns()
    {
        // Horizontal timing pattern: row 0, cols 8 to size-1
        for (var c = 8; c < _size; c++)
        {
            var isDark = c % 2 == 0;
            SetModule(0, c, isDark, reserved: true);
        }

        // Vertical timing pattern: col 0, rows 8 to size-1
        for (var r = 8; r < _size; r++)
        {
            var isDark = r % 2 == 0;
            SetModule(r, 0, isDark, reserved: true);
        }
    }

    private void ReserveFormatInfoArea()
    {
        // Format info is placed along row 8 (cols 1-8) and col 8 (rows 1-8)
        // Reserve these positions
        for (var i = 1; i <= 8; i++)
        {
            SetReserved(8, i);  // Row 8, cols 1-8
            SetReserved(i, 8);  // Col 8, rows 1-8
        }
    }

    public void PlaceDataBits(byte[] codewords)
    {
        var bitIndex = 0;
        var totalBits = codewords.Length * 8;

        // Zigzag pattern from bottom-right, moving upward in column pairs.
        // Column 0 is skipped (timing pattern), analogous to column 6 in regular QR.
        var col = _size - 1;
        while (col >= 0)
        {
            // Skip column 0 (timing pattern column)
            if (col == 0)
            {
                col--;
                continue;
            }

            for (var row = 0; row < _size; row++)
            {
                // Determine direction: columns are processed in pairs from right edge.
                // The pair index from the right determines upward/downward direction.
                // We need to account for the skipped column 0.
                // Column pairs (right to left, skipping col 0):
                //   col size-1, size-2 (pair 0) -> upward
                //   col size-3, size-4 (pair 1) -> downward
                //   ...etc
                // For Micro QR with col 0 skipped, the pair index is:
                //   pairIndex = (size - 1 - col) / 2
                // Even pairs go upward, odd pairs go downward.
                var pairIndex = (_size - 1 - col) / 2;
                var isUpward = pairIndex % 2 == 0;
                var actualRow = isUpward ? _size - 1 - row : row;

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
                if (col - 1 >= 1 && !_isReserved[actualRow, col - 1])
                {
                    if (bitIndex < totalBits)
                    {
                        var bit = (codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1;
                        _modules[actualRow, col - 1] = bit == 1;
                        bitIndex++;
                    }
                }
            }

            col -= 2;
        }
    }

    public void ApplyMask(int maskPattern)
    {
        for (var row = 0; row < _size; row++)
        {
            for (var col = 0; col < _size; col++)
            {
                if (_isReserved[row, col])
                {
                    continue;
                }

                if (ShouldApplyMask(maskPattern, row, col))
                {
                    _modules[row, col] = !_modules[row, col];
                }
            }
        }
    }

    private static bool ShouldApplyMask(int maskPattern, int row, int col)
    {
        return maskPattern switch
        {
            0 => row % 2 == 0,
            1 => ((row / 2) + (col / 3)) % 2 == 0,
            2 => ((row * col) % 2 + (row * col) % 3) % 2 == 0,
            3 => ((row + col) % 2 + (row * col) % 3) % 2 == 0,
            _ => throw new ArgumentOutOfRangeException(nameof(maskPattern), $"Micro QR mask pattern must be 0-3, got {maskPattern}."),
        };
    }

    public void PlaceFormatInfo(ErrorCorrectionLevel ecLevel, int maskPattern)
    {
        var symbolNumber = MicroQRVersion.GetSymbolNumber(_version, ecLevel);
        var formatInfo = MicroQRFormatInfo.GetFormatInfo(symbolNumber, maskPattern);

        // 15 bits of format information
        // Placed along row 8 (cols 1 to 8) = 8 bits (MSB to LSB: bits 14..7)
        // and col 8 (rows 1 to 8) = 7 bits (remaining: bits 6..0) -- wait, that's only 15 total
        // Actually: row 8, cols 1-8 = 8 modules, col 8, rows 1-8 = 8 modules = 16 total
        // But format info is 15 bits, so the layout is:
        // Row 8: cols 1..8 -> bits 14..7 (8 bits)
        // Col 8: rows 1..7 -> bits 6..0 (7 bits)
        // Total = 15 bits

        // Row 8, cols 1 to 8 (bits 14 down to 7)
        for (var i = 0; i < 8; i++)
        {
            var bit = (formatInfo >> (14 - i)) & 1;
            _modules[8, 1 + i] = bit == 1;
        }

        // Col 8, rows 1 to 7 (bits 6 down to 0)
        for (var i = 0; i < 7; i++)
        {
            var bit = (formatInfo >> (6 - i)) & 1;
            _modules[1 + i, 8] = bit == 1;
        }
    }

    // Evaluate mask penalty for Micro QR.
    // Count dark modules in last row and last column (outside finder/timing area).
    // Score = min(darkLastRow, darkLastCol) * 16 + max(darkLastRow, darkLastCol)
    // Best mask = highest score.
    public static int EvaluateMaskPenalty(bool[,] modules, int size)
    {
        var darkInLastRow = 0;
        var darkInLastCol = 0;

        // Last row (row size-1), columns 1 to size-1 (skip col 0 = timing)
        for (var c = 1; c < size; c++)
        {
            if (modules[size - 1, c])
            {
                darkInLastRow++;
            }
        }

        // Last column (col size-1), rows 1 to size-1 (skip row 0 = timing)
        for (var r = 1; r < size; r++)
        {
            if (modules[r, size - 1])
            {
                darkInLastCol++;
            }
        }

        var min = Math.Min(darkInLastRow, darkInLastCol);
        var max = Math.Max(darkInLastRow, darkInLastCol);
        return (min * 16) + max;
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
