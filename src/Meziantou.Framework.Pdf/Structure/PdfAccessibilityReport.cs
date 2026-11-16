namespace Meziantou.Framework.Pdf;

/// <summary>The accessibility issues of a document (<see cref="PdfAccessibilityInspector.Inspect(PdfDocument)"/>).</summary>
public sealed class PdfAccessibilityReport
{
    internal PdfAccessibilityReport(bool isTagged, IReadOnlyList<PdfAccessibilityIssue> issues)
    {
        IsTagged = isTagged;
        Issues = issues;
    }

    /// <summary>Gets a value indicating whether the document has a structure tree.</summary>
    public bool IsTagged { get; }

    /// <summary>Gets the issues, the issues of the document first, then those of each page in order.</summary>
    public IReadOnlyList<PdfAccessibilityIssue> Issues { get; }

    /// <summary>Gets a value indicating whether the inspector found no issue. The inspector checks what can be checked automatically: a document without issues may still need a human review (the alternate texts must describe the figures, for instance).</summary>
    public bool HasNoIssues => Issues.Count == 0;
}
