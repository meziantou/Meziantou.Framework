namespace Meziantou.Framework.Pdf;

/// <summary>How much a problem the reader found in a file can change what is read (<see cref="PdfDiagnostic.Severity"/>).</summary>
public enum PdfDiagnosticSeverity
{
    /// <summary>The file does not follow the specification, but nothing is lost: an offset that counts from the header, a cross-reference table that starts at 1...</summary>
    Information,

    /// <summary>The reader repaired the file: what it reads is its best guess, such as a stream that ends at <c>endstream</c> instead of its <c>/Length</c>.</summary>
    Warning,

    /// <summary>Something is lost: an object that cannot be found is null, the data after the damage of a stream is missing.</summary>
    Error,
}
