namespace Meziantou.Framework.Barcodes.Internal;

internal sealed class MatrixBuilder
{
    private readonly int _version;
    private readonly int _size;
    private readonly bool[,] _modules;
    private readonly bool[,] _isReserved;

    public MatrixBuilder(int version)
    {
        _version = version;
        _size = QRCodeVersion.GetSideLength(version);
        _modules = new bool[_size, _size];
        _isReserved = new bool[_size, _size];
    }

    public int Size => _size;

    public bool[,] Modules => _modules;

    public bool[,] IsReserved => _isReserved;

    /// <summary>
    /// Places everything that does not depend on the mask: function patterns, version information and unmasked data.
    /// The format information areas are reserved but left empty; use <see cref="ApplyMask"/> and <see cref="PlaceFormatInfo"/> to complete the symbol.
    /// </summary>
    public void PlaceFunctionPatternsAndData(byte[] codewords)
    {
        PlaceFinderPatterns();
        PlaceAlignmentPatterns();
        PlaceTimingPatterns();
        PlaceDarkModule();
        ReserveFormatInfoAreas();
        ReserveVersionInfoAreas();
        PlaceDataBits(codewords);
        PlaceVersionInfo();
    }

    private void PlaceFinderPatterns()
    {
        // Top-left
        PlaceFinderPattern(0, 0);
        // Top-right
        PlaceFinderPattern(0, _size - 7);
        // Bottom-left
        PlaceFinderPattern(_size - 7, 0);

        // Separators around top-left finder
        for (var i = 0; i < 8; i++)
        {
            SetReserved(7, i);
            SetReserved(i, 7);
        }

        // Separators around top-right finder
        for (var i = 0; i < 8; i++)
        {
            SetReserved(7, _size - 8 + i);
            SetReserved(i, _size - 8);
        }

        // Separators around bottom-left finder
        for (var i = 0; i < 8; i++)
        {
            SetReserved(_size - 8, i);
            SetReserved(_size - 8 + i, 7);
        }
    }

    private void PlaceFinderPattern(int row, int col)
    {
        for (var r = 0; r < 7; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                var isDark = r == 0 || r == 6 || c == 0 || c == 6 ||
                    (r >= 2 && r <= 4 && c >= 2 && c <= 4);
                SetModule(row + r, col + c, isDark, reserved: true);
            }
        }
    }

    private void PlaceAlignmentPatterns()
    {
        var positions = QRCodeVersion.GetAlignmentPatternPositions(_version);
        if (positions.Length == 0)
        {
            return;
        }

        for (var i = 0; i < positions.Length; i++)
        {
            for (var j = 0; j < positions.Length; j++)
            {
                var row = positions[i];
                var col = positions[j];

                // Skip if overlapping with finder patterns
                if (IsOverlappingFinderPattern(row, col))
                {
                    continue;
                }

                PlaceAlignmentPattern(row, col);
            }
        }
    }

    private bool IsOverlappingFinderPattern(int centerRow, int centerCol)
    {
        // Top-left finder: rows 0-8, cols 0-8
        if (centerRow <= 8 && centerCol <= 8)
        {
            return true;
        }

        // Top-right finder: rows 0-8, cols size-9..size-1
        if (centerRow <= 8 && centerCol >= _size - 9)
        {
            return true;
        }

        // Bottom-left finder: rows size-9..size-1, cols 0-8
        if (centerRow >= _size - 9 && centerCol <= 8)
        {
            return true;
        }

        return false;
    }

    private void PlaceAlignmentPattern(int centerRow, int centerCol)
    {
        for (var r = -2; r <= 2; r++)
        {
            for (var c = -2; c <= 2; c++)
            {
                var isDark = Math.Abs(r) == 2 || Math.Abs(c) == 2 || (r == 0 && c == 0);
                SetModule(centerRow + r, centerCol + c, isDark, reserved: true);
            }
        }
    }

    private void PlaceTimingPatterns()
    {
        for (var i = 8; i < _size - 8; i++)
        {
            var isDark = i % 2 == 0;

            // Horizontal timing pattern (row 6)
            if (!_isReserved[6, i])
            {
                SetModule(6, i, isDark, reserved: true);
            }

            // Vertical timing pattern (column 6)
            if (!_isReserved[i, 6])
            {
                SetModule(i, 6, isDark, reserved: true);
            }
        }
    }

    private void PlaceDarkModule()
    {
        // The dark module is always at position (4*version + 9, 8)
        SetModule((4 * _version) + 9, 8, true, reserved: true);
    }

    private void ReserveFormatInfoAreas()
    {
        // Around top-left finder pattern
        for (var i = 0; i <= 8; i++)
        {
            if (!_isReserved[8, i])
            {
                SetReserved(8, i);
            }

            if (!_isReserved[i, 8])
            {
                SetReserved(i, 8);
            }
        }

        // Below top-right finder pattern
        for (var i = 0; i <= 7; i++)
        {
            if (!_isReserved[8, _size - 1 - i])
            {
                SetReserved(8, _size - 1 - i);
            }
        }

        // Right of bottom-left finder pattern
        for (var i = 0; i <= 6; i++)
        {
            if (!_isReserved[_size - 1 - i, 8])
            {
                SetReserved(_size - 1 - i, 8);
            }
        }
    }

    private void ReserveVersionInfoAreas()
    {
        if (_version < 7)
        {
            return;
        }

        // Bottom-left area (6x3 block)
        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                SetReserved(_size - 11 + c, r);
            }
        }

        // Top-right area (6x3 block)
        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                SetReserved(r, _size - 11 + c);
            }
        }
    }

    public void PlaceDataBits(byte[] codewords)
    {
        var bitIndex = 0;
        var totalBits = codewords.Length * 8;

        // Data is placed in a zigzag pattern, moving right-to-left in pairs of columns
        // Column 6 is skipped (timing pattern)
        var col = _size - 1;
        while (col >= 0)
        {
            // Skip the timing pattern column
            if (col == 6)
            {
                col--;
            }

            // Process two columns at a time
            for (var row = 0; row < _size; row++)
            {
                // Determine actual row based on direction (upward or downward)
                var isUpward = (((_size - 1 - col) / 2) % 2) == 0;
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
                if (col > 0 && !_isReserved[actualRow, col - 1])
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

    /// <summary>
    /// Flips the data modules selected by the mask pattern. Applying the same mask twice restores the unmasked data.
    /// </summary>
    public void ApplyMask(int maskPattern)
    {
        // Dispatch once per call instead of once per module: each pattern gets its own specialized loop
        switch (maskPattern)
        {
            case 0: ApplyMask<Mask0>(); break;
            case 1: ApplyMask<Mask1>(); break;
            case 2: ApplyMask<Mask2>(); break;
            case 3: ApplyMask<Mask3>(); break;
            case 4: ApplyMask<Mask4>(); break;
            case 5: ApplyMask<Mask5>(); break;
            case 6: ApplyMask<Mask6>(); break;
            case 7: ApplyMask<Mask7>(); break;
            default: throw new ArgumentOutOfRangeException(nameof(maskPattern));
        }
    }

    private void ApplyMask<TMask>()
        where TMask : struct, IMaskPattern
    {
        for (var row = 0; row < _size; row++)
        {
            for (var col = 0; col < _size; col++)
            {
                if (!_isReserved[row, col] && TMask.ShouldApply(row, col))
                {
                    _modules[row, col] = !_modules[row, col];
                }
            }
        }
    }

    public void PlaceFormatInfo(ErrorCorrectionLevel ecLevel, int maskPattern)
    {
        var formatInfo = FormatInfo.GetFormatInfo(ecLevel, maskPattern);

        // Place around top-left finder pattern
        // Horizontal: row 8, cols 0-7 (skipping col 6 for timing)
        ReadOnlySpan<int> horizontalCols = [0, 1, 2, 3, 4, 5, 7, 8];
        for (var i = 0; i < 8; i++)
        {
            var bit = (formatInfo >> (14 - i)) & 1;
            _modules[8, horizontalCols[i]] = bit == 1;
        }

        // Vertical: col 8, rows 7 down to 0 (skipping row 6 for timing)
        ReadOnlySpan<int> verticalRows = [7, 5, 4, 3, 2, 1, 0];
        for (var i = 0; i < 7; i++)
        {
            var bit = (formatInfo >> (14 - 8 - i)) & 1;
            _modules[verticalRows[i], 8] = bit == 1;
        }

        // Place next to top-right finder pattern
        // Row 8, cols from size-1 to size-8
        for (var i = 0; i < 8; i++)
        {
            var bit = (formatInfo >> i) & 1;
            _modules[8, _size - 1 - i] = bit == 1;
        }

        // Place next to bottom-left finder pattern
        // Col 8, rows from size-1 to size-7
        for (var i = 0; i < 7; i++)
        {
            var bit = (formatInfo >> (14 - i)) & 1;
            _modules[_size - 1 - i, 8] = bit == 1;
        }
    }

    public void PlaceVersionInfo()
    {
        if (_version < 7)
        {
            return;
        }

        var versionInfo = FormatInfo.GetVersionInfo(_version);

        for (var i = 0; i < 18; i++)
        {
            var bit = (versionInfo >> i) & 1;
            var row = i / 3;
            var col = i % 3;

            // Bottom-left version info area
            _modules[_size - 11 + col, row] = bit == 1;

            // Top-right version info area
            _modules[row, _size - 11 + col] = bit == 1;
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

    private interface IMaskPattern
    {
        static abstract bool ShouldApply(int row, int col);
    }

    private readonly struct Mask0 : IMaskPattern
    {
        public static bool ShouldApply(int row, int col) => (row + col) % 2 == 0;
    }

    private readonly struct Mask1 : IMaskPattern
    {
        public static bool ShouldApply(int row, int col) => row % 2 == 0;
    }

    private readonly struct Mask2 : IMaskPattern
    {
        public static bool ShouldApply(int row, int col) => col % 3 == 0;
    }

    private readonly struct Mask3 : IMaskPattern
    {
        public static bool ShouldApply(int row, int col) => (row + col) % 3 == 0;
    }

    private readonly struct Mask4 : IMaskPattern
    {
        public static bool ShouldApply(int row, int col) => ((row / 2) + (col / 3)) % 2 == 0;
    }

    private readonly struct Mask5 : IMaskPattern
    {
        public static bool ShouldApply(int row, int col) => ((row * col) % 2) + ((row * col) % 3) == 0;
    }

    private readonly struct Mask6 : IMaskPattern
    {
        public static bool ShouldApply(int row, int col) => (((row * col) % 2) + ((row * col) % 3)) % 2 == 0;
    }

    private readonly struct Mask7 : IMaskPattern
    {
        public static bool ShouldApply(int row, int col) => (((row + col) % 2) + ((row * col) % 3)) % 2 == 0;
    }
}
