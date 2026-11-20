using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

internal sealed record TableOfContentsBlock(string Title) : FlowBlock;
