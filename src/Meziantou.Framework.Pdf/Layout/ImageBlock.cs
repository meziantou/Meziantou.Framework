using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

internal sealed record ImageBlock(PdfImage Image, string AlternateDescription, double? Width) : FlowBlock;
