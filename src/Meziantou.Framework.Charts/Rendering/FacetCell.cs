using System.Runtime.InteropServices;

namespace Meziantou.Framework.Charts;

/// <summary>A chart of the facet grid: its position in the grid and its facets.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FacetCell(int Column, int Row, string? XKey, string? YKey, bool IsLeft, bool IsRight, bool IsBottom, bool HasHeader);
