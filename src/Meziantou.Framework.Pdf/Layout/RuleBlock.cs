using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

internal sealed record RuleBlock(PdfColor Color, double Width) : FlowBlock;
