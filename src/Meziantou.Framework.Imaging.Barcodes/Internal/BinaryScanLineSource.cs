namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>The runs of the lines of a binary image, one sample per pixel.</summary>
internal sealed class BinaryScanLineSource(BinaryImage image) : ScanLineSource(image.Width, image.Height, unitsPerPixel: 1)
{
    public override int[] GetRuns(PointD direction, PointD normal, double offset, double start, double end)
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
}
