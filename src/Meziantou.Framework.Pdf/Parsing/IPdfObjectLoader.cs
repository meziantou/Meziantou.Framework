using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>Reads the indirect objects of a file when a reference to them is resolved.</summary>
internal interface IPdfObjectLoader
{
    PdfObject Load(IndirectObjectEntry entry);
}
