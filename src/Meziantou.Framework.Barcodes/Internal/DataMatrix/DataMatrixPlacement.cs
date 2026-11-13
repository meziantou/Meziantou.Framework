namespace Meziantou.Framework.Barcodes.Internal.DataMatrix;

// ISO/IEC 16022:2006, Annex F: the placement of the codewords in the mapping matrix (the data regions without their
// finder patterns), in diagonal "utah" shapes of 8 modules, with special shapes in the corners
internal sealed class DataMatrixPlacement
{
    private readonly int[] _codewords;
    private readonly int _rows;
    private readonly int _columns;
    private readonly bool[,] _bits;
    private readonly bool[,] _placed;
    private readonly bool _isReading;

    private DataMatrixPlacement(int[] codewords, int rows, int columns, bool[,]? source)
    {
        _codewords = codewords;
        _rows = rows;
        _columns = columns;
        _bits = source ?? new bool[rows, columns];
        _placed = new bool[rows, columns];
        _isReading = source is not null;
    }

    public static bool[,] Place(int[] codewords, int rows, int columns)
    {
        var placement = new DataMatrixPlacement(codewords, rows, columns, source: null);
        placement.Place();
        return placement._bits;
    }

    /// <summary>Reads the codewords from the mapping matrix, in the placement order.</summary>
    public static int[] Read(bool[,] mapping, int codewordCount)
    {
        var placement = new DataMatrixPlacement(new int[codewordCount], mapping.GetLength(0), mapping.GetLength(1), mapping);
        placement.Place();
        return placement._codewords;
    }

    private void Place()
    {
        var position = 0;
        var row = 4;
        var column = 0;
        do
        {
            if (row == _rows && column == 0)
            {
                Corner1(position++);
            }

            if (row == _rows - 2 && column == 0 && _columns % 4 != 0)
            {
                Corner2(position++);
            }

            if (row == _rows - 2 && column == 0 && _columns % 8 == 4)
            {
                Corner3(position++);
            }

            if (row == _rows + 4 && column == 2 && _columns % 8 == 0)
            {
                Corner4(position++);
            }

            // Sweep upward diagonally
            do
            {
                if (row < _rows && column >= 0 && !_placed[row, column])
                {
                    Utah(row, column, position++);
                }

                row -= 2;
                column += 2;
            }
            while (row >= 0 && column < _columns);

            row++;
            column += 3;

            // Sweep downward diagonally
            do
            {
                if (row >= 0 && column < _columns && !_placed[row, column])
                {
                    Utah(row, column, position++);
                }

                row += 2;
                column -= 2;
            }
            while (row < _rows && column >= 0);

            row += 3;
            column++;
        }
        while (row < _rows || column < _columns);

        // The bottom-right corner is not covered by any codeword in some sizes: it is a fixed pattern
        if (!_isReading && !_placed[_rows - 1, _columns - 1])
        {
            _bits[_rows - 1, _columns - 1] = true;
            _bits[_rows - 2, _columns - 2] = true;
        }
    }

    private void Module(int row, int column, int position, int bit)
    {
        if (row < 0)
        {
            row += _rows;
            column += 4 - ((_rows + 4) % 8);
        }

        if (column < 0)
        {
            column += _columns;
            row += 4 - ((_columns + 4) % 8);
        }

        // Bit 1 is the most significant bit of the codeword
        _placed[row, column] = true;
        if (_isReading)
        {
            if (position < _codewords.Length && _bits[row, column])
            {
                _codewords[position] |= 1 << (8 - bit);
            }

            return;
        }

        _bits[row, column] = position < _codewords.Length && (_codewords[position] & (1 << (8 - bit))) != 0;
    }

    private void Utah(int row, int column, int position)
    {
        Module(row - 2, column - 2, position, 1);
        Module(row - 2, column - 1, position, 2);
        Module(row - 1, column - 2, position, 3);
        Module(row - 1, column - 1, position, 4);
        Module(row - 1, column, position, 5);
        Module(row, column - 2, position, 6);
        Module(row, column - 1, position, 7);
        Module(row, column, position, 8);
    }

    private void Corner1(int position)
    {
        Module(_rows - 1, 0, position, 1);
        Module(_rows - 1, 1, position, 2);
        Module(_rows - 1, 2, position, 3);
        Module(0, _columns - 2, position, 4);
        Module(0, _columns - 1, position, 5);
        Module(1, _columns - 1, position, 6);
        Module(2, _columns - 1, position, 7);
        Module(3, _columns - 1, position, 8);
    }

    private void Corner2(int position)
    {
        Module(_rows - 3, 0, position, 1);
        Module(_rows - 2, 0, position, 2);
        Module(_rows - 1, 0, position, 3);
        Module(0, _columns - 4, position, 4);
        Module(0, _columns - 3, position, 5);
        Module(0, _columns - 2, position, 6);
        Module(0, _columns - 1, position, 7);
        Module(1, _columns - 1, position, 8);
    }

    private void Corner3(int position)
    {
        Module(_rows - 3, 0, position, 1);
        Module(_rows - 2, 0, position, 2);
        Module(_rows - 1, 0, position, 3);
        Module(0, _columns - 2, position, 4);
        Module(0, _columns - 1, position, 5);
        Module(1, _columns - 1, position, 6);
        Module(2, _columns - 1, position, 7);
        Module(3, _columns - 1, position, 8);
    }

    private void Corner4(int position)
    {
        Module(_rows - 1, 0, position, 1);
        Module(_rows - 1, _columns - 1, position, 2);
        Module(0, _columns - 3, position, 3);
        Module(0, _columns - 2, position, 4);
        Module(0, _columns - 1, position, 5);
        Module(1, _columns - 3, position, 6);
        Module(1, _columns - 2, position, 7);
        Module(1, _columns - 1, position, 8);
    }
}
