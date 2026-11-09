namespace Meziantou.Framework.Barcodes.Internal.DataMatrix;

// ISO/IEC 16022:2006, Table 7: the ECC 200 symbol attributes
internal sealed record DataMatrixSymbolSize(int Rows, int Columns, int RegionRows, int RegionColumns, int DataCodewords, int ErrorCorrectionCodewords, int Blocks)
{
    // Ordered by the number of data codewords, then by the number of modules
    public static IReadOnlyList<DataMatrixSymbolSize> All { get; } =
    [
        new(10, 10, 8, 8, 3, 5, 1),
        new(12, 12, 10, 10, 5, 7, 1),
        new(8, 18, 6, 16, 5, 7, 1),
        new(14, 14, 12, 12, 8, 10, 1),
        new(8, 32, 6, 14, 10, 11, 1),
        new(16, 16, 14, 14, 12, 12, 1),
        new(12, 26, 10, 24, 16, 14, 1),
        new(18, 18, 16, 16, 18, 14, 1),
        new(20, 20, 18, 18, 22, 18, 1),
        new(12, 36, 10, 16, 22, 18, 1),
        new(22, 22, 20, 20, 30, 20, 1),
        new(16, 36, 14, 16, 32, 24, 1),
        new(24, 24, 22, 22, 36, 24, 1),
        new(26, 26, 24, 24, 44, 28, 1),
        new(16, 48, 14, 22, 49, 28, 1),
        new(32, 32, 14, 14, 62, 36, 1),
        new(36, 36, 16, 16, 86, 42, 1),
        new(40, 40, 18, 18, 114, 48, 1),
        new(44, 44, 20, 20, 144, 56, 1),
        new(48, 48, 22, 22, 174, 68, 1),
        new(52, 52, 24, 24, 204, 84, 2),
        new(64, 64, 14, 14, 280, 112, 2),
        new(72, 72, 16, 16, 368, 144, 4),
        new(80, 80, 18, 18, 456, 192, 4),
        new(88, 88, 20, 20, 576, 224, 4),
        new(96, 96, 22, 22, 696, 272, 4),
        new(104, 104, 24, 24, 816, 336, 6),
        new(120, 120, 18, 18, 1050, 408, 6),
        new(132, 132, 20, 20, 1304, 496, 8),
        new(144, 144, 22, 22, 1558, 620, 10),
    ];

    public bool IsSquare => Rows == Columns;

    /// <summary>Gets the number of data regions in a column of regions (vertically).</summary>
    public int VerticalRegions => Rows / (RegionRows + 2);

    /// <summary>Gets the number of data regions in a row of regions (horizontally).</summary>
    public int HorizontalRegions => Columns / (RegionColumns + 2);

    public static bool IsHeight(int value) => All.Any(size => size.Rows == value);

    public static bool IsWidth(int value) => All.Any(size => size.Columns == value);

    public bool Matches(DataMatrixShape shape, int? height, int? width)
    {
        // An exact height or width takes precedence over the shape
        if (height is not null || width is not null)
            return (height is null || height == Rows) && (width is null || width == Columns);

        return shape switch
        {
            DataMatrixShape.Square => IsSquare,
            DataMatrixShape.Rectangle => !IsSquare,
            _ => true,
        };
    }
}
