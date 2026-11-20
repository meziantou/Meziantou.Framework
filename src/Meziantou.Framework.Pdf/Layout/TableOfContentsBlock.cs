using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

internal sealed record TableOfContentsBlock(string Title) : FlowBlock;
