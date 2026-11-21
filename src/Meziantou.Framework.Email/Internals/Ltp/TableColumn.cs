namespace Meziantou.Framework.Email.Internals.Ltp;

/// <summary>A column of a table context: its tag, the position of its cells in a row, and its bit in the cell existence bitmap.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct TableColumn(MapiPropertyTag Tag, int Offset, int Size, int CellExistenceBit);
