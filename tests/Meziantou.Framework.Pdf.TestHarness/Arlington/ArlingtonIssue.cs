namespace Meziantou.Framework.Pdf.TestHarness.Arlington;

/// <summary>A problem the validator found.</summary>
/// <param name="Kind">The kind of problem.</param>
/// <param name="Path">The path of the object from the trailer, such as <c>trailer/Root/Pages/Kids[0]</c>.</param>
/// <param name="Message">The description.</param>
public sealed record ArlingtonIssue(ArlingtonIssueKind Kind, string Path, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Path}: {Message}";
}
