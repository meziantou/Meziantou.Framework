using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

internal sealed record ColumnsBlock(int Count, double Gap, List<FlowBlock> Blocks) : FlowBlock;
