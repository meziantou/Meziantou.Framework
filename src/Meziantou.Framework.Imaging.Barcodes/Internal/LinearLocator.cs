using Meziantou.Framework.Barcodes;

namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>
/// Reads the linear barcodes of a binary image: parallel scan lines at a few angles cross the image, and
/// <see cref="BarcodeDecoder.DecodeScanLines"/> reads the barcodes they cross.
/// </summary>
internal static class LinearLocator
{
    // The rows and columns first; the diagonals for the barcodes that are turned by another angle
    private static ReadOnlySpan<int> Angles => [0, 90, 30, 150, 60, 120];

    public static void Locate(BinaryImage image, Func<BarcodeType, bool> isIncluded, List<BarcodeImageResult> results, int maximumResults, CancellationToken cancellationToken)
    {
        // A few hundred lines across the image: close enough for the rows of the stacked GS1 DataBar symbols
        var step = Math.Max(1, Math.Min(image.Width, image.Height) / 400.0);
        foreach (var degrees in Angles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var angle = degrees * Math.PI / 180;
            var direction = new PointD(Math.Cos(angle), Math.Sin(angle));
            var normal = new PointD(-direction.Y, direction.X);
            var (alongStart, alongEnd) = GetRange(image, direction);
            var (acrossStart, acrossEnd) = GetRange(image, normal);

            var offsets = new List<double>();
            var lines = new List<int[]>();
            for (var offset = acrossStart + (step / 2); offset < acrossEnd; offset += step)
            {
                offsets.Add(offset);
                lines.Add(GetRuns(image, direction, normal, offset, alongStart, alongEnd));
            }

            foreach (var result in BarcodeDecoder.DecodeScanLines(lines))
            {
                if (!isIncluded(result.Type))
                    continue;

                // A symbology without check character needs two lines that agree
                if (result.Type is BarcodeType.Code39 or BarcodeType.Codabar or BarcodeType.Itf && result.FirstScanLine == result.LastScanLine && lines.Count > 2)
                    continue;

                PointD At(int line, double position)
                {
                    var along = alongStart + position;
                    return new PointD((direction.X * along) + (normal.X * offsets[line]), (direction.Y * along) + (normal.Y * offsets[line]));
                }

                PointD[] corners = [At(result.FirstScanLine, result.Start), At(result.FirstScanLine, result.End), At(result.LastScanLine, result.End), At(result.LastScanLine, result.Start)];
                Add(results, new BarcodeImageResult(result, OrderCorners(corners)));
                if (results.Count >= maximumResults)
                    return;
            }
        }
    }

    // The range of the projections of the image on an axis through the origin
    private static (double Start, double End) GetRange(BinaryImage image, PointD axis)
    {
        ReadOnlySpan<PointD> corners = [new(0, 0), new(image.Width, 0), new(0, image.Height), new(image.Width, image.Height)];
        var start = double.MaxValue;
        var end = double.MinValue;
        foreach (var corner in corners)
        {
            var projection = (corner.X * axis.X) + (corner.Y * axis.Y);
            start = Math.Min(start, projection);
            end = Math.Max(end, projection);
        }

        return (start, end);
    }

    // The runs of a line, one sample per pixel, starting with a light run: the pixels outside the image are light
    private static int[] GetRuns(BinaryImage image, PointD direction, PointD normal, double offset, double start, double end)
    {
        var runs = new List<int> { 0 };
        var dark = false;
        for (var along = start + 0.5; along < end; along++)
        {
            var isDark = image.IsDark((direction.X * along) + (normal.X * offset), (direction.Y * along) + (normal.Y * offset));
            if (isDark != dark)
            {
                runs.Add(0);
                dark = isDark;
            }

            runs[^1]++;
        }

        return [.. runs];
    }

    // Clockwise in the image, from the corner nearest to its top-left
    private static PointD[] OrderCorners(PointD[] corners)
    {
        var center = new PointD(corners.Average(corner => corner.X), corners.Average(corner => corner.Y));
        var ordered = corners.OrderBy(corner => Math.Atan2(corner.Y - center.Y, corner.X - center.X)).ToArray();
        var first = Array.IndexOf(ordered, ordered.MinBy(corner => corner.X + corner.Y));
        return [.. ordered[first..], .. ordered[..first]];
    }

    // A barcode found at several angles is one result. The lines that cross a barcode at an angle can read other symbols in
    // its bars: a result over a barcode found before (at an angle closer to its bars) is not one.
    private static void Add(List<BarcodeImageResult> results, BarcodeImageResult result)
    {
        var bounds = GetBounds(result);
        foreach (var existing in results)
        {
            var existingBounds = GetBounds(existing);
            if (bounds.Left < existingBounds.Right && existingBounds.Left < bounds.Right && bounds.Top < existingBounds.Bottom && existingBounds.Top < bounds.Bottom)
                return;
        }

        results.Add(result);
    }

    // The bounding box of the corners, with a margin: a barcode crossed by a single line has a zero height
    private static (double Left, double Top, double Right, double Bottom) GetBounds(BarcodeImageResult result)
    {
        var length = result.Corners[0].DistanceTo(result.Corners[2]);
        var margin = Math.Max(2, length / 100);
        return (result.Corners.Min(corner => corner.X) - margin, result.Corners.Min(corner => corner.Y) - margin, result.Corners.Max(corner => corner.X) + margin, result.Corners.Max(corner => corner.Y) + margin);
    }
}
