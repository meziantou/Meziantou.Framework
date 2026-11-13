namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>The 8 rotations and reflections of a module matrix, for the decoders that find the orientation of a symbol.</summary>
internal static class MatrixTransforms
{
    public const int Count = 8;

    /// <summary>Gets the size of the matrix after the transform: the rotations by 90° and 270° swap the height and the width.</summary>
    public static (int Height, int Width) GetSize(bool[,] modules, int transform)
    {
        var height = modules.GetLength(0);
        var width = modules.GetLength(1);
        return SwapsDimensions(transform) ? (width, height) : (height, width);
    }

    /// <summary>Applies a transform: 0 to 3 are rotations by a quarter turn clockwise, 4 to 7 the same after a reflection.</summary>
    public static bool[,] Apply(bool[,] modules, int transform)
    {
        if (transform is 0)
            return modules;

        var (height, width) = GetSize(modules, transform);
        var result = new bool[height, width];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var (sourceRow, sourceColumn) = GetSource(row, column, height, width, transform);
                result[row, column] = modules[sourceRow, sourceColumn];
            }
        }

        return result;
    }

    /// <summary>Gets whether the transform is a reflection.</summary>
    public static bool IsMirrored(int transform) => transform >= 4;

    private static bool SwapsDimensions(int transform) => transform % 2 is 1;

    // The module of the source matrix that goes to (row, column) of the transformed matrix of the given size
    private static (int Row, int Column) GetSource(int row, int column, int height, int width, int transform)
    {
        if (transform >= 4)
        {
            // Reflect first: the source is mirrored left to right
            var (r, c) = GetSource(row, column, height, width, transform - 4);
            var sourceWidth = SwapsDimensions(transform) ? height : width;
            return (r, sourceWidth - 1 - c);
        }

        return transform switch
        {
            0 => (row, column),
            1 => (width - 1 - column, row), // A quarter turn clockwise: the source is width x height
            2 => (height - 1 - row, width - 1 - column),
            _ => (column, height - 1 - row),
        };
    }
}
