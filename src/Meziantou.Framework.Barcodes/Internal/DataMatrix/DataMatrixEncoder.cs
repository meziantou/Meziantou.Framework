using System.Runtime.InteropServices;

namespace Meziantou.Framework.Barcodes.Internal.DataMatrix;

// ISO/IEC 16022:2006
internal static class DataMatrixEncoder
{
    public static bool[,] Encode(ReadOnlySpan<byte> data, bool gs1, int? eci, DataMatrixOptions? options)
    {
        var message = new int[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            // GS1 element strings separate their elements with GS, which stands for FNC1
            message[i] = gs1 && data[i] == 0x1D ? DataMatrixHighLevelEncoder.Fnc1 : data[i];
        }

        var shape = options?.Shape ?? DataMatrixShape.Square;
        var height = options?.Height;
        var width = options?.Width;
        var sizes = DataMatrixSymbolSize.All
            .Where(size => size.Matches(shape, height, width))
            .OrderBy(size => size.Rows * size.Columns)
            .ThenBy(size => size.IsSquare ? 0 : 1)
            .ToList();
        if (sizes.Count == 0)
            throw new ArgumentException($"No Data Matrix size is {height?.ToString(CultureInfo.InvariantCulture) ?? "any height"} × {width?.ToString(CultureInfo.InvariantCulture) ?? "any width"} modules.", nameof(options));

        // The end of the data is encoded according to the space the symbol has left, so the data is encoded for each size
        // until one holds it. Without a size limit, the data needs at least about the codewords of an unbounded encoding.
        var minimumLength = DataMatrixHighLevelEncoder.Encode(message, gs1, eci, int.MaxValue)!.Count - 4;
        foreach (var size in sizes)
        {
            if (size.DataCodewords < minimumLength)
                continue;

            var codewords = DataMatrixHighLevelEncoder.Encode(message, gs1, eci, size.DataCodewords);
            if (codewords is not null)
                return BuildMatrix(size, codewords);
        }

        throw new InvalidOperationException($"The data does not fit in a Data Matrix symbol of the allowed sizes (the largest one holds {sizes[^1].DataCodewords.ToString(CultureInfo.InvariantCulture)} codewords).");
    }

    private static bool[,] BuildMatrix(DataMatrixSymbolSize size, List<int> dataCodewords)
    {
        var codewords = AddErrorCorrection(size, dataCodewords);

        var verticalRegions = size.VerticalRegions;
        var horizontalRegions = size.HorizontalRegions;
        var mappingRows = size.RegionRows * verticalRegions;
        var mappingColumns = size.RegionColumns * horizontalRegions;
        var mapping = DataMatrixPlacement.Place(codewords, mappingRows, mappingColumns);

        var matrix = new bool[size.Rows, size.Columns];
        for (var regionRow = 0; regionRow < verticalRegions; regionRow++)
        {
            var top = regionRow * (size.RegionRows + 2);
            for (var regionColumn = 0; regionColumn < horizontalRegions; regionColumn++)
            {
                var left = regionColumn * (size.RegionColumns + 2);
                var bottom = top + size.RegionRows + 1;
                var right = left + size.RegionColumns + 1;

                // The finder pattern: a solid left side and bottom side, and alternating modules on the top and right sides
                for (var column = left; column <= right; column++)
                {
                    matrix[bottom, column] = true;
                    matrix[top, column] = (column - left) % 2 == 0;
                }

                for (var row = top; row <= bottom; row++)
                {
                    matrix[row, left] = true;
                    if (row != bottom)
                    {
                        matrix[row, right] = (row - top) % 2 == 1;
                    }
                }

                for (var row = 0; row < size.RegionRows; row++)
                {
                    for (var column = 0; column < size.RegionColumns; column++)
                    {
                        matrix[top + 1 + row, left + 1 + column] = mapping[(regionRow * size.RegionRows) + row, (regionColumn * size.RegionColumns) + column];
                    }
                }
            }
        }

        return matrix;
    }

    // The codewords are split into blocks by interleaving (codeword i is in block i modulo the number of blocks), and the
    // error correction codewords of each block are interleaved the same way after the data codewords
    private static int[] AddErrorCorrection(DataMatrixSymbolSize size, List<int> dataCodewords)
    {
        var blocks = size.Blocks;
        var errorCorrectionPerBlock = size.ErrorCorrectionCodewords / blocks;
        var result = new int[size.DataCodewords + size.ErrorCorrectionCodewords];
        dataCodewords.CopyTo(result);

        var blockData = new List<int>(capacity: (size.DataCodewords / blocks) + 1);
        var errorCorrection = new int[errorCorrectionPerBlock];
        for (var block = 0; block < blocks; block++)
        {
            blockData.Clear();
            for (var i = block; i < size.DataCodewords; i += blocks)
            {
                blockData.Add(dataCodewords[i]);
            }

            BinaryGaloisField.Field256.ComputeErrorCorrection(CollectionsMarshal.AsSpan(blockData), errorCorrection);
            for (var i = 0; i < errorCorrectionPerBlock; i++)
            {
                result[size.DataCodewords + block + (i * blocks)] = errorCorrection[i];
            }
        }

        return result;
    }
}
