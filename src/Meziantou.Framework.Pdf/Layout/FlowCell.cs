using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A cell of a table of a flow document, as the row builder created it.</summary>
internal sealed record FlowCell(List<FlowBlock> Blocks, int ColumnSpan, int RowSpan);
