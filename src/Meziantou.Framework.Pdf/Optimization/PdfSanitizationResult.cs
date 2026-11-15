namespace Meziantou.Framework.Pdf;

/// <summary>The result of <see cref="PdfSaveSanitizer.Sanitize(ReadOnlyMemory{byte}, PdfReadOptions?)"/>.</summary>
public sealed class PdfSanitizationResult
{
    internal PdfSanitizationResult(ReadOnlyMemory<byte> output, IReadOnlyList<PdfSanitizerRepair> repairs)
    {
        Output = output;
        Repairs = repairs;
    }

    /// <summary>Gets the file: the original file followed by one incremental update with the repairs, or the original file itself when nothing was repaired.</summary>
    public ReadOnlyMemory<byte> Output { get; }

    /// <summary>Gets the repairs made.</summary>
    public IReadOnlyList<PdfSanitizerRepair> Repairs { get; }

    /// <summary>Gets a value indicating whether the file changed.</summary>
    public bool HasChanges => Repairs.Count > 0;
}
