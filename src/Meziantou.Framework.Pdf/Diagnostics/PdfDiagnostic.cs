using System.Globalization;

namespace Meziantou.Framework.Pdf;

/// <summary>A problem the reader found in a file, and how it read the file anyway (<see cref="PdfDocument.Diagnostics"/>).</summary>
public sealed class PdfDiagnostic
{
    internal PdfDiagnostic(PdfDiagnosticKind kind, string message, long? offset, int? objectNumber)
    {
        Kind = kind;
        Message = message;
        Offset = offset;
        ObjectNumber = objectNumber;
    }

    /// <summary>Gets the kind of problem.</summary>
    public PdfDiagnosticKind Kind { get; }

    /// <summary>Gets how much the problem can change what is read.</summary>
    public PdfDiagnosticSeverity Severity => GetSeverity(Kind);

    /// <summary>Gets the description of the problem and of what the reader did.</summary>
    public string Message { get; }

    /// <summary>Gets the offset in the file where the problem is, or <see langword="null"/> when it has none.</summary>
    public long? Offset { get; }

    /// <summary>Gets the number of the object the problem is in, or <see langword="null"/> when it is not in an object.</summary>
    public int? ObjectNumber { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        var location = (ObjectNumber, Offset) switch
        {
            ({ } number, { } offset) => string.Create(CultureInfo.InvariantCulture, $" (object {number}, offset {offset})"),
            ({ } number, null) => string.Create(CultureInfo.InvariantCulture, $" (object {number})"),
            (null, { } offset) => string.Create(CultureInfo.InvariantCulture, $" (offset {offset})"),
            _ => "",
        };
        return $"{Severity} {Kind}{location}: {Message}";
    }

    internal static PdfDiagnosticSeverity GetSeverity(PdfDiagnosticKind kind) => kind switch
    {
        PdfDiagnosticKind.HeaderNotAtStart or PdfDiagnosticKind.ShiftedCrossReferenceTable or PdfDiagnosticKind.MissingEndObj or PdfDiagnosticKind.MissingEndStream => PdfDiagnosticSeverity.Information,
        PdfDiagnosticKind.MissingObject or PdfDiagnosticKind.UndecodableObjectStream or PdfDiagnosticKind.DamagedStreamData or PdfDiagnosticKind.UndecodableStream or PdfDiagnosticKind.BrokenCrossReferenceChain => PdfDiagnosticSeverity.Error,
        _ => PdfDiagnosticSeverity.Warning,
    };
}
