using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>A field of the interactive form, with its fully qualified name and its inherited type (ISO 32000-2, 12.7.4).</summary>
internal sealed record PdfFieldEntry(string Name, PdfDictionary Field, PdfReference? Reference, string? Type, int Depth);
