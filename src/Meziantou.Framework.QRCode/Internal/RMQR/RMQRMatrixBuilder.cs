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

    public bool[,] Modules => _modules;

    public void Build(byte[] codewords, ErrorCorrectionLevel ecLevel)
    {
        PlaceFixedPatterns();
        PlaceDataBits(codewords);
        PlaceFormatInfo(ecLevel);
    }

    private void PlaceFixedPatterns()
    {
        ReserveFunctionPattern();
        PlaceTimingAndAlignmentPatterns();
        PlaceFinderPattern();
        PlaceFinderSubPattern();
        PlaceSeparators();
        PlaceCornerFinders();
    }

    private void ReserveFunctionPattern()
    {
        // Edge timing patterns
        SetReservedRegion(0, 0, _width, 1); // Top
        SetReservedRegion(0, _height - 1, _width, 1); // Bottom
        SetReservedRegion(0, 1, 1, _height - 2); // Left
        SetReservedRegion(_width - 1, 1, 1, _height - 2); // Right

        var alignmentCenters = RMQRVersion.GetAlignmentPatternColumnPositions(_version);
        foreach (var center in alignmentCenters)
        {
            SetReservedRegion(center - 1, 1, 3, 2); // Top alignment
            SetReservedRegion(center - 1, _height - 3, 3, 2); // Bottom alignment
            SetReservedRegion(center, 3, 1, _height - 6); // Vertical timing
        }

        // Top-left finder + separator area
        SetReservedRegion(1, 1, 7, 7 - (_height == 7 ? 1 : 0));

        // Top-left format information
        SetReservedRegion(8, 1, 3, 5);
        SetReservedRegion(11, 1, 1, 3);

        // Bottom-right finder sub-pattern
        SetReservedRegion(_width - 5, _height - 5, 4, 4);

        // Bottom-right format information
        SetReservedRegion(_width - 8, _height - 6, 3, 5);
        SetReservedRegion(_width - 5, _height - 6, 3, 1);

        SetReserved(1, _width - 2); // Top-right corner finder
        if (_height > 9)
        {
            SetReserved(_height - 2, 1); // Bottom-left corner finder
        }

        foreach (var (row, col) in GetFinderSideFormatCoordinates())
        {
            SetReserved(row, col);
        }

        foreach (var (row, col) in GetSubFinderSideFormatCoordinates())
        {
            SetReserved(row, col);
        }
    }

    private void PlaceFinderPattern()
    {
        for (var r = 0; r < 7; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                var isDark = r == 0 || r == 6 || c == 0 || c == 6 || (r >= 2 && r <= 4 && c >= 2 && c <= 4);
                _modules[r, c] = isDark;
            }
        }
    }

    private void PlaceFinderSubPattern()
    {
        var startRow = _height - 5;
        var startCol = _width - 5;

        for (var r = 0; r < 5; r++)
        {
            for (var c = 0; c < 5; c++)
            {
                var row = startRow + r;
                var col = startCol + c;
                if (!_isReserved[row, col])
                {
                    continue;
                }

                var isDark = (r % 2 == 0 && c % 2 == 0) || (r % 2 == 1 && (c == 0 || c == 4));
                _modules[row, col] = isDark;
            }
        }
    }

    private void PlaceSeparators()
    {
        if (_height > 7)
        {
            for (var c = 0; c <= 7; c++)
            {
                _modules[7, c] = false;
            }
        }

        for (var r = 0; r < 7 && r < _height; r++)
        {
            if (7 < _width)
            {
                _modules[r, 7] = false;
            }
        }
    }

    private void PlaceTimingAndAlignmentPatterns()
    {
        if (_height == 7)
        {
            for (var col = 0; col < _width; col++)
            {
                _modules[0, col] = col <= 6 || col >= _width - 3 || col % 2 == 0;
                _modules[_height - 1, col] = col <= 6 || col >= _width - 5 || col % 2 == 0;
            }

            for (var row = 0; row < _height; row++)
            {
                _modules[row, 0] = true;
                _modules[row, _width - 1] = true;
            }

            var centersForHeight7 = RMQRVersion.GetAlignmentPatternColumnPositions(_version);
            foreach (var center in centersForHeight7)
            {
                _modules[1, center - 1] = true;
                _modules[1, center] = false;
                _modules[1, center + 1] = true;

                _modules[2, center - 1] = true;
                _modules[2, center] = true;
                _modules[2, center + 1] = true;

                _modules[_height - 3, center - 1] = true;
                _modules[_height - 3, center] = true;
                _modules[_height - 3, center + 1] = true;

                _modules[_height - 2, center - 1] = true;
                _modules[_height - 2, center] = false;
                _modules[_height - 2, center + 1] = true;
            }

            return;
        }

        // Top edge
        for (var col = 0; col < _width; col++)
        {
            _modules[0, col] = col <= 6 || col >= _width - 3 || col % 2 == 0;
        }

        // Bottom edge
        for (var col = 0; col < _width; col++)
        {
            _modules[_height - 1, col] = col <= 2 || col >= _width - 5 || col % 2 == 0;
        }

        // Left edge
        for (var row = 0; row <= 6; row++)
        {
            _modules[row, 0] = true;
        }

        _modules[7, 0] = false;
        for (var row = 8; row <= _height - 3; row++)
        {
            _modules[row, 0] = row % 2 == 0;
        }

        _modules[_height - 2, 0] = true;
        _modules[_height - 1, 0] = true;

        // Right edge
        for (var row = 0; row <= 2; row++)
        {
            _modules[row, _width - 1] = true;
        }

        for (var row = 3; row <= _height - 5; row++)
        {
            _modules[row, _width - 1] = row % 2 == 0;
        }

        for (var row = _height - 4; row < _height; row++)
        {
            _modules[row, _width - 1] = true;
        }

        // Alignment and vertical timing patterns
        var centers = RMQRVersion.GetAlignmentPatternColumnPositions(_version);
        foreach (var center in centers)
        {
            _modules[1, center - 1] = true;
            _modules[1, center] = false;
            _modules[1, center + 1] = true;

            _modules[2, center - 1] = true;
            _modules[2, center] = true;
            _modules[2, center + 1] = true;

            for (var row = 3; row <= _height - 4; row++)
            {
                _modules[row, center] = row % 2 == 0;
            }

            _modules[_height - 3, center - 1] = true;
            _modules[_height - 3, center] = true;
            _modules[_height - 3, center + 1] = true;

            _modules[_height - 2, center - 1] = true;
            _modules[_height - 2, center] = false;
            _modules[_height - 2, center + 1] = true;
        }
    }

    private void PlaceCornerFinders()
    {
        _modules[1, _width - 2] = true;
        if (_height > 9)
        {
            _modules[_height - 2, 1] = true;
        }
    }

    private void PlaceDataBits(byte[] codewords)
    {
        var codewordIndex = 0;
        var bitPosition = 7;
        var readingUp = true;

        // Read/write columns in pairs from right to left, skipping the right edge timing column.
        for (var x = _width - 2; x > 0; x -= 2)
        {
            for (var row = 0; row < _height; row++)
            {
                var y = readingUp ? _height - 1 - row : row;
                for (var colOffset = 0; colOffset < 2; colOffset++)
                {
                    var xx = x - colOffset;
                    if (_isReserved[y, xx])
                    {
                        continue;
                    }

                    var dataBit = TryReadBit(codewords, ref codewordIndex, ref bitPosition);
                    var maskedBit = GetMaskBit(xx, y) ? !dataBit : dataBit;
                    _modules[y, xx] = maskedBit;
                }
            }

            readingUp = !readingUp;
        }
    }

    private void PlaceFormatInfo(ErrorCorrectionLevel ecLevel)
    {
        var finderSideInfo = RMQRFormatInfo.GetFinderSideFormatInfo(ecLevel, _version);
        var subFinderSideInfo = RMQRFormatInfo.GetSubFinderSideFormatInfo(ecLevel, _version);

        var finderCoordinates = GetFinderSideFormatCoordinates();
        for (var i = 0; i < finderCoordinates.Length; i++)
        {
            var bit = ((finderSideInfo >> (17 - i)) & 1) != 0;
            var (row, col) = finderCoordinates[i];
            _modules[row, col] = bit;
        }

        var subFinderCoordinates = GetSubFinderSideFormatCoordinates();
        for (var i = 0; i < subFinderCoordinates.Length; i++)
        {
            var bit = ((subFinderSideInfo >> (17 - i)) & 1) != 0;
            var (row, col) = subFinderCoordinates[i];
            _modules[row, col] = bit;
        }
    }

    private (int Row, int Col)[] GetFinderSideFormatCoordinates()
    {
        var coordinates = new (int Row, int Col)[18];
        var index = 0;

        for (var y = 3; y >= 1; y--)
        {
            coordinates[index++] = (y, 11);
        }

        for (var x = 10; x >= 8; x--)
        {
            for (var y = 5; y >= 1; y--)
            {
                coordinates[index++] = (y, x);
            }
        }

        return coordinates;
    }

    private (int Row, int Col)[] GetSubFinderSideFormatCoordinates()
    {
        var coordinates = new (int Row, int Col)[18];
        var index = 0;

        for (var x = 3; x <= 5; x++)
        {
            coordinates[index++] = (_height - 6, _width - x);
        }

        for (var x = 6; x <= 8; x++)
        {
            for (var y = 2; y <= 6; y++)
            {
                coordinates[index++] = (_height - y, _width - x);
            }
        }

        return coordinates;
    }

    private static bool GetMaskBit(int x, int y)
    {
        // rMQR uses fixed mask pattern 4
        return ((y / 2) + (x / 3)) % 2 == 0;
    }

    private static bool TryReadBit(byte[] codewords, ref int codewordIndex, ref int bitPosition)
    {
        if ((uint)codewordIndex >= (uint)codewords.Length)
        {
            return false;
        }

        var bit = ((codewords[codewordIndex] >> bitPosition) & 1) != 0;
        bitPosition--;
        if (bitPosition < 0)
        {
            codewordIndex++;
            bitPosition = 7;
        }

        return bit;
    }

    private void SetReservedRegion(int x, int y, int width, int height)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var col = x; col < x + width; col++)
            {
                SetReserved(row, col);
            }
        }
    }

    private void SetReserved(int row, int col)
    {
        if (row >= 0 && row < _height && col >= 0 && col < _width)
        {
            _isReserved[row, col] = true;
        }
    }
}
