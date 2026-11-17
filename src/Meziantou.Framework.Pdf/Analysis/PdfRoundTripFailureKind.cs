namespace Meziantou.Framework.Pdf;

/// <summary>The checks of <see cref="PdfRoundTripValidator"/>.</summary>
public enum PdfRoundTripFailureKind
{
    /// <summary>An output cannot be read back.</summary>
    ReadBackFailed,

    /// <summary>An output is damaged: the reader repairs it.</summary>
    ReadBackRepaired,

    /// <summary>An output does not have the number of pages of the document it was written from.</summary>
    PageCountChanged,

    /// <summary>A page of an output differs from the page it was written from: boxes, rotation, annotations or content.</summary>
    PageChanged,

    /// <summary>The object graphs of the two outputs differ.</summary>
    ObjectGraphChanged,

    /// <summary>The two outputs have the same structure but different bytes: the writer is not deterministic.</summary>
    OutputsDiffer,
}
