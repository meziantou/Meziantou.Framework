using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

internal sealed record RuleBlock(PdfColor Color, double Width) : FlowBlock;
