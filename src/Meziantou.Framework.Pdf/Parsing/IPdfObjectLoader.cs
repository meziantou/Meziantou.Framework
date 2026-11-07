using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>Reads the indirect objects of a file when a reference to them is resolved.</summary>
internal interface IPdfObjectLoader
{
    PdfObject Load(IndirectObjectEntry entry);

    /// <summary>
    /// Indicates whether a reference whose generation is not the generation of the object can resolve to it: in a
    /// repaired file, when the file defines a single generation of the object.
    /// </summary>
    bool AcceptsOtherGeneration(int objectNumber);
}
