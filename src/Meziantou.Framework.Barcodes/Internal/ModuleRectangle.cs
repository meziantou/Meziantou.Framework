using System.Runtime.InteropServices;

namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>A rectangle of modules, by row then column.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ModuleRectangle(int Row, int Column, int RowCount, int ColumnCount)
{
    public int EndRow => Row + RowCount;

    public int EndColumn => Column + ColumnCount;

    public bool Contains(int row, int column) => row >= Row && row < EndRow && column >= Column && column < EndColumn;
}
