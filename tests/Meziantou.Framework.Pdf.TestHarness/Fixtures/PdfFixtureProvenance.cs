namespace Meziantou.Framework.Pdf.TestHarness.Fixtures;

/// <summary>Where a fixture comes from.</summary>
public sealed class PdfFixtureProvenance
{
    /// <summary>Gets or sets the origin: <c>hand-authored</c> (a template of <c>sources/</c>) or <c>tool</c> (written by an external tool from another fixture).</summary>
    public string Origin { get; set; } = "hand-authored";

    /// <summary>Gets or sets the SPDX identifier of the license.</summary>
    public string License { get; set; } = "CC0-1.0";

    /// <summary>Gets or sets the author.</summary>
    public string? Author { get; set; }

    /// <summary>Gets or sets the file the fixture is made from, relative to the fixtures directory.</summary>
    public string? Source { get; set; }

    /// <summary>Gets or sets the tool and the arguments that wrote the fixture.</summary>
    public string? Tool { get; set; }
}
