using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

/// <summary>A cell of a table placed in the grid of its columns.</summary>
internal sealed record TableCell(int Row, int Column, int ColumnSpan, int RowSpan, bool IsHeader, List<FlowBlock> Blocks);
