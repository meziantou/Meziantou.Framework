using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>The region segment information field (T.88, 7.4.1): the size and the position of a region, and how it is combined with the page.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Jbig2RegionInfo(int Width, int Height, int X, int Y, int CombinationOperator);
