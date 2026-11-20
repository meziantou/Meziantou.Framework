namespace Meziantou.Framework.Pdf;

/// <summary>The problems the reader finds in a file and repairs (<see cref="PdfDiagnostic.Kind"/>).</summary>
public enum PdfDiagnosticKind
{
    /// <summary>Bytes come before the header (<c>%PDF-</c>, ISO 32000-2, 7.5.2): the offsets of the file may count from the header.</summary>
    HeaderNotAtStart,

    /// <summary>The file has no header: it is read anyway.</summary>
    MissingHeader,

    /// <summary>The <c>startxref</c> keyword at the end of the file is missing, or its offset is not valid (7.5.5): the cross-reference information is rebuilt.</summary>
    InvalidStartXref,

    /// <summary>A cross-reference section cannot be read (7.5.4, 7.5.8): its table or stream is damaged.</summary>
    DamagedCrossReferenceSection,

    /// <summary>The <c>/Prev</c> offset of a cross-reference section does not lead to a section (7.5.6): the older sections are not read, and the objects they define are found by scanning the file.</summary>
    BrokenCrossReferenceChain,

    /// <summary>A cross-reference table starts at object 1 with the entry of object 0: its entries are shifted to start at 0.</summary>
    ShiftedCrossReferenceTable,

    /// <summary>The trailer has no catalog (<c>/Root</c>, 7.5.5): the cross-reference information is rebuilt.</summary>
    MissingCatalog,

    /// <summary>The cross-reference information was rebuilt by scanning the file for objects (<see cref="PdfDocument.IsRepaired"/>).</summary>
    CrossReferenceRebuilt,

    /// <summary>The file has no trailer with a catalog: the last catalog of the file is the root.</summary>
    CatalogFoundByScan,

    /// <summary>An object is not at the offset its cross-reference entry gives: it was found at another offset.</summary>
    WrongObjectOffset,

    /// <summary>An object cannot be found: references to it are the null object (7.3.10).</summary>
    MissingObject,

    /// <summary>A reference has a generation that is not the one of the object; the object is used, since the file defines one generation only.</summary>
    GenerationMismatch,

    /// <summary>An object is not at the index of the object stream its cross-reference entry gives (7.5.7): it was found at another index.</summary>
    WrongObjectStreamIndex,

    /// <summary>An object stream cannot be decoded (7.5.7): the objects it contains are missing.</summary>
    UndecodableObjectStream,

    /// <summary>An object has no <c>endobj</c> keyword (7.3.10).</summary>
    MissingEndObj,

    /// <summary>A stream has no <c>endstream</c> keyword, but its <c>/Length</c> is right (7.3.8.1).</summary>
    MissingEndStream,

    /// <summary>The <c>/Length</c> of a stream is missing or wrong: the data ends at the <c>endstream</c> keyword (7.3.8.1).</summary>
    InvalidStreamLength,

    /// <summary>A keyword is where an object is expected: it is read as the null object.</summary>
    UnknownKeyword,

    /// <summary>An array or a dictionary is not closed: it ends where the object ends.</summary>
    UnclosedContainer,

    /// <summary>A dictionary has a key that is not a name (7.3.7): the entry is skipped.</summary>
    InvalidDictionaryKey,

    /// <summary>A node of the page tree is not valid (7.7.3): a child that is not a dictionary, a node reached twice, a page that is not an indirect object, or a tree too deep. The node is skipped.</summary>
    InvalidPageTreeNode,

    /// <summary>The data of a Flate stream is damaged (7.4.4): the bytes decoded before the damage are kept.</summary>
    DamagedStreamData,

    /// <summary>A stream cannot be decoded: its filters are not valid, not supported, or its data exceeds the limits (<see cref="PdfDocument.ValidateStructure"/> reports it).</summary>
    UndecodableStream,
}
