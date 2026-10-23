namespace Meziantou.Framework.Pdf.TestHarness.Fixtures;

/// <summary>A data set copied from another project, such as the TSV files of the Arlington PDF Model.</summary>
public sealed class PdfVendoredDataSet
{
    /// <summary>Gets or sets the directory of the data set, relative to the fixtures directory.</summary>
    public string Path { get; set; } = "";

    /// <summary>Gets or sets the URL of the project.</summary>
    public string Source { get; set; } = "";

    /// <summary>Gets or sets the commit of the project the files come from.</summary>
    public string Commit { get; set; } = "";

    /// <summary>Gets or sets the SPDX identifier of the license.</summary>
    public string License { get; set; } = "";

    /// <summary>Gets or sets the file of the license, relative to the fixtures directory.</summary>
    public string? LicenseFile { get; set; }

    /// <summary>Gets the SHA-256 of each file, by its path relative to <see cref="Path"/>.</summary>
    public SortedDictionary<string, string> Files { get; init; } = new(StringComparer.Ordinal);
}
