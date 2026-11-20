using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

internal sealed record ListBlock(List<List<FlowBlock>> Items, bool Ordered) : FlowBlock;
