namespace Meziantou.Framework.Pdf;

/// <summary>A check of <see cref="PdfRoundTripValidator"/> that failed.</summary>
public sealed class PdfRoundTripFailure
{
    internal PdfRoundTripFailure(PdfRoundTripFailureKind kind, string message, int? pageIndex)
    {
        Kind = kind;
        Message = message;
        PageIndex = pageIndex;
    }

    /// <summary>Gets the check that failed.</summary>
    public PdfRoundTripFailureKind Kind { get; }

    /// <summary>Gets a description of the failure.</summary>
    public string Message { get; }

    /// <summary>Gets the index of the page that changed, for <see cref="PdfRoundTripFailureKind.PageChanged"/>.</summary>
    public int? PageIndex { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Kind}: {Message}";
}
