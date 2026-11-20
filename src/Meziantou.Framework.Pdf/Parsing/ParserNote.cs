namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>A repair the parser made while it read an object, reported when the object is loaded (<see cref="PdfObjectParser.Notes"/>).</summary>
internal readonly record struct ParserNote(PdfDiagnosticKind Kind, string Message, long Offset);
