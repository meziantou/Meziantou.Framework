using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A bounding box in font units.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontBounds(int XMin, int YMin, int XMax, int YMax)
{
    public static FontBounds Empty => new(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);

    public bool IsEmpty => XMin > XMax;

    public FontBounds Include(int x, int y) => new(Math.Min(XMin, x), Math.Min(YMin, y), Math.Max(XMax, x), Math.Max(YMax, y));
}
