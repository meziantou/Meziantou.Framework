using System.Collections.ObjectModel;

namespace Meziantou.Framework.Pdf.TestHarness.Fixtures;

/// <summary>A fixture of the manifest.</summary>
public sealed class PdfFixture
{
    /// <summary>Gets or sets the path of the file, relative to the fixtures directory.</summary>
    public string Path { get; set; } = "";

    /// <summary>Gets or sets the SHA-256 of the file.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>Gets or sets the kind of the file: <c>valid</c> (every reader and qpdf read it without warning), <c>damaged</c> (the readers repair it), <c>invalid</c> (the library must reject it).</summary>
    public string Kind { get; set; } = "valid";

    /// <summary>Gets or sets where the file comes from.</summary>
    public PdfFixtureProvenance Provenance { get; set; } = new();

    /// <summary>Gets or sets the features of the file (<c>xref.stream</c>, <c>objstm</c>, <c>incremental</c>, ...).</summary>
    public Collection<string> Features { get; init; } = [];

    /// <summary>Gets or sets what the library must read from the file, or <see langword="null"/>.</summary>
    public PdfFixtureExpectation? Expected { get; set; }
}
