using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A run of text of a paragraph of a flow document: its text, its style and the URI it links to.</summary>
internal readonly record struct FlowRun(string Text, PdfTextStyle? Style, Uri? Link);
