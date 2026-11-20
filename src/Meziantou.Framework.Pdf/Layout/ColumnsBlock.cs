using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

internal sealed record ColumnsBlock(int Count, double Gap, List<FlowBlock> Blocks) : FlowBlock;
