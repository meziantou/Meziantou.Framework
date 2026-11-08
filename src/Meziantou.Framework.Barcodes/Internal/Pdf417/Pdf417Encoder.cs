namespace Meziantou.Framework.Barcodes.Internal.Pdf417;

// ISO/IEC 15438:2015
internal static class Pdf417Encoder
{
    private const int MinRows = 3;
    private const int MaxRows = 90;
    private const int MaxColumns = 30;
    private const int MaxCodewords = 928;
    private const int PadCodeword = 900;

    /// <summary>Encodes the data and returns the module matrix, each row of the symbol repeated <see cref="Pdf417Options.RowHeight"/> times.</summary>
    /// <param name="data">The bytes to encode.</param>
    /// <param name="eci">The ECI assignment number to declare at the start of the data, or <see langword="null"/>.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    public static bool[,] Encode(ReadOnlySpan<byte> data, int? eci, Pdf417Options? options)
    {
        // The first codeword is the symbol length descriptor
        var codewords = new List<int>(data.Length + 8) { 0 };
        Pdf417HighLevelEncoder.Encode(data, eci, codewords);

        var level = GetErrorCorrectionLevel(codewords.Count, options?.SecurityLevel);
        var errorCorrectionCount = Pdf417ErrorCorrection.GetCodewordCount(level);
        var (rows, columns) = GetDimensions(codewords.Count + errorCorrectionCount, options?.Columns);

        var dataCount = (rows * columns) - errorCorrectionCount;
        while (codewords.Count < dataCount)
        {
            codewords.Add(PadCodeword);
        }

        codewords[0] = dataCount;
        var all = new int[rows * columns];
        codewords.CopyTo(all);
        Pdf417ErrorCorrection.Encode(all.AsSpan(0, dataCount), level, all.AsSpan(dataCount));

        return BuildMatrix(all, rows, columns, level, options?.RowHeight ?? 3);
    }

    private static int GetErrorCorrectionLevel(int dataCount, int? securityLevel)
    {
        if (securityLevel is { } level)
        {
            if (dataCount + Pdf417ErrorCorrection.GetCodewordCount(level) > MaxCodewords)
                throw new InvalidOperationException($"The data does not fit in a PDF417 symbol with the error correction level {level}.");

            return level;
        }

        // The minimum levels that ISO/IEC 15438 recommends (Annex E), lowered (down to 2) when the data does not fit otherwise
        var recommended = dataCount switch
        {
            <= 40 => 2,
            <= 160 => 3,
            <= 320 => 4,
            _ => 5,
        };

        for (var candidate = recommended; candidate >= 2; candidate--)
        {
            if (dataCount + Pdf417ErrorCorrection.GetCodewordCount(candidate) <= MaxCodewords)
                return candidate;
        }

        throw new InvalidOperationException("The data does not fit in a PDF417 symbol.");
    }

    private static (int Rows, int Columns) GetDimensions(int count, int? requestedColumns)
    {
        if (requestedColumns is { } columns)
        {
            if (!TryGetRows(count, columns, out var rows))
                throw new InvalidOperationException($"The data does not fit in a PDF417 symbol of {columns} columns.");

            return (rows, columns);
        }

        // The number of columns that zint chooses (about 3 rows of codewords per column), or the closest one that fits
        var preferred = Math.Clamp((int)(0.5 + Math.Sqrt((count - 1) / 3.0)), 1, MaxColumns);
        for (var distance = 0; distance < MaxColumns; distance++)
        {
            foreach (var candidate in (ReadOnlySpan<int>)[preferred + distance, preferred - distance])
            {
                if (candidate is >= 1 and <= MaxColumns && TryGetRows(count, candidate, out var rows))
                    return (rows, candidate);
            }
        }

        throw new InvalidOperationException("The data does not fit in a PDF417 symbol.");
    }

    private static bool TryGetRows(int count, int columns, out int rows)
    {
        rows = Math.Max(MinRows, (count + columns - 1) / columns);
        return rows <= MaxRows && rows * columns <= MaxCodewords;
    }

    private static bool[,] BuildMatrix(int[] codewords, int rows, int columns, int level, int rowHeight)
    {
        var width = (Pdf417Patterns.CodewordWidth * (columns + 4)) + 1;
        var matrix = new bool[rows * rowHeight, width];
        for (var row = 0; row < rows; row++)
        {
            var top = row * rowHeight;
            var cluster = row % 3;
            var (left, right) = GetRowIndicators(row, rows, columns, level);

            var x = 0;
            Draw(matrix, top, ref x, Pdf417Patterns.StartPattern, Pdf417Patterns.CodewordWidth);
            Draw(matrix, top, ref x, Pdf417Patterns.GetPattern(cluster, left), Pdf417Patterns.CodewordWidth);
            for (var column = 0; column < columns; column++)
            {
                Draw(matrix, top, ref x, Pdf417Patterns.GetPattern(cluster, codewords[(row * columns) + column]), Pdf417Patterns.CodewordWidth);
            }

            Draw(matrix, top, ref x, Pdf417Patterns.GetPattern(cluster, right), Pdf417Patterns.CodewordWidth);
            Draw(matrix, top, ref x, Pdf417Patterns.StopPattern, Pdf417Patterns.StopWidth);

            for (var repeat = 1; repeat < rowHeight; repeat++)
            {
                for (var column = 0; column < width; column++)
                {
                    matrix[top + repeat, column] = matrix[top, column];
                }
            }
        }

        return matrix;
    }

    // The left and right row indicators (ISO/IEC 15438, 5.6): the number of rows, the number of columns and the error
    // correction level, spread on the three clusters
    private static (int Left, int Right) GetRowIndicators(int row, int rows, int columns, int level)
    {
        var rowGroup = 30 * (row / 3);
        var rowsValue = (rows - 1) / 3;
        var levelValue = (level * 3) + ((rows - 1) % 3);
        var columnsValue = columns - 1;
        return (row % 3) switch
        {
            0 => (rowGroup + rowsValue, rowGroup + columnsValue),
            1 => (rowGroup + levelValue, rowGroup + rowsValue),
            _ => (rowGroup + columnsValue, rowGroup + levelValue),
        };
    }

    private static void Draw(bool[,] matrix, int row, ref int x, int pattern, int width)
    {
        for (var i = width - 1; i >= 0; i--)
        {
            matrix[row, x++] = ((pattern >> i) & 1) != 0;
        }
    }
}
