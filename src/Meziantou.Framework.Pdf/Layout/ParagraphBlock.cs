using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

internal sealed record ParagraphBlock(FlowRun[] Runs, PdfParagraphStyle? Style, int HeadingLevel) : FlowBlock;
