namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>The runs of the scan lines of an image that cross its linear barcodes.</summary>
internal abstract class ScanLineSource
{
    protected ScanLineSource(int width, int height, int unitsPerPixel)
    {
        Width = width;
        Height = height;
        UnitsPerPixel = unitsPerPixel;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the number of units of the widths of the runs in a pixel.</summary>
    public int UnitsPerPixel { get; }

    /// <summary>
    /// Gets the runs of a line, alternately light and dark, starting with a light run: the points
    /// <c>direction * along + normal * offset</c> for <c>along</c> from <paramref name="start"/> to <paramref name="end"/>.
    /// The pixels outside the image are light.
    /// </summary>
    public abstract int[] GetRuns(PointD direction, PointD normal, double offset, double start, double end);
}
