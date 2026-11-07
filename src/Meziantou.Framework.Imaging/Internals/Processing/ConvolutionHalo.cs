using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The rows that a band of a convolution reads outside its range: the kernel center row of positions above its first row
/// and as many below its last one, mapped to image rows by the edge mode (<see cref="ConvolutionPlan.MapIndex"/>). The
/// positions mapped to the same image row share one halo row, and those that contribute nothing
/// (<see cref="ConvolutionEdgeMode.Zero"/> beyond the image) have none, so a band never holds more halo rows than the image
/// has rows.
/// </summary>
internal sealed class ConvolutionHalo
{
    private readonly int[] _rows;
    private readonly int[] _imageRows;
    private readonly int _size;

    private ConvolutionHalo(int[] rows, int[] imageRows, int size)
    {
        _rows = rows;
        _imageRows = imageRows;
        _size = size;
    }

    /// <summary>Gets the number of halo rows: the number of image rows read outside the band.</summary>
    public int RowCount => _imageRows.Length;

    /// <summary>Gets the rows that the band <c>[<paramref name="start"/>, <paramref name="end"/>)</c> reads outside its range.</summary>
    /// <param name="start">The first row of the band.</param>
    /// <param name="end">The row after the last one of the band.</param>
    /// <param name="size">The number of positions read on each side of the band (the kernel center row).</param>
    /// <param name="height">The height of the image.</param>
    /// <param name="edgeMode">The rows read outside the image.</param>
    public static ConvolutionHalo Create(int start, int end, int size, int height, ConvolutionEdgeMode edgeMode)
    {
        var rows = new int[2 * size];
        var imageRows = new List<int>();
        var indices = new Dictionary<int, int>();
        for (var position = 0; position < rows.Length; position++)
        {
            var source = position < size ? start - size + position : end + (position - size);
            var imageRow = ConvolutionPlan.MapIndex(source, height, edgeMode);
            if (imageRow < 0)
            {
                rows[position] = -1;
                continue;
            }

            ref var index = ref CollectionsMarshal.GetValueRefOrAddDefault(indices, imageRow, out var exists);
            if (!exists)
            {
                index = imageRows.Count;
                imageRows.Add(imageRow);
            }

            rows[position] = index;
        }

        return new ConvolutionHalo(rows, [.. imageRows], size);
    }

    /// <summary>Gets the image row that halo row <paramref name="index"/> holds.</summary>
    public int GetImageRow(int index) => _imageRows[index];

    /// <summary>Gets the halo row of a position above the band (0 is the farthest one), or -1 when it contributes nothing.</summary>
    public int GetTopRow(int index) => _rows[index];

    /// <summary>Gets the halo row of a position below the band (0 is the nearest one), or -1 when it contributes nothing.</summary>
    public int GetBottomRow(int index) => _rows[_size + index];
}
