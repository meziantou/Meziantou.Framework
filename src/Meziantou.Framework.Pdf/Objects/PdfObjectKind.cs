namespace Meziantou.Framework.Pdf.Objects;

/// <summary>The kinds of the objects of the PDF object model (ISO 32000-2, 7.3).</summary>
public enum PdfObjectKind
{
    /// <summary>The null object (7.3.9).</summary>
    Null,

    /// <summary>A boolean (7.3.2).</summary>
    Boolean,

    /// <summary>An integer (7.3.3).</summary>
    Integer,

    /// <summary>A real number (7.3.3).</summary>
    Real,

    /// <summary>A string (7.3.4).</summary>
    String,

    /// <summary>A name (7.3.5).</summary>
    Name,

    /// <summary>An array (7.3.6).</summary>
    Array,

    /// <summary>A dictionary (7.3.7).</summary>
    Dictionary,

    /// <summary>A stream (7.3.8).</summary>
    Stream,

    /// <summary>A reference to an indirect object (7.3.10).</summary>
    Reference,
}
