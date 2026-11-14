namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>A square pattern of concentric rings found in an image, with the size of its modules in pixels.</summary>
internal sealed record PatternCenter(double X, double Y, double ModuleSize, int Count)
{
    public PointD Point => new(X, Y);
}
