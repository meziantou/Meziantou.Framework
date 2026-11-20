using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

internal sealed record ImageBlock(PdfImage Image, string AlternateDescription, double? Width) : FlowBlock;
