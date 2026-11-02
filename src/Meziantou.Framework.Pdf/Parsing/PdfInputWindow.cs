namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>Where the data a lexer reads is in the input of a file.</summary>
/// <param name="Input">The input.</param>
/// <param name="Offset">The offset in the input of the first byte of the data of the lexer.</param>
/// <param name="Limit">The offset the object ends before at the latest: the length of the input, or the next boundary found by the recovery.</param>
internal sealed record PdfInputWindow(PdfInput Input, long Offset, long Limit);
