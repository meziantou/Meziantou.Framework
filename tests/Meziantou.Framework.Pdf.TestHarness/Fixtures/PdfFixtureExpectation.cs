using System.Collections.ObjectModel;

namespace Meziantou.Framework.Pdf.TestHarness.Fixtures;

/// <summary>What the library must read from a fixture.</summary>
public sealed class PdfFixtureExpectation
{
    /// <summary>Gets or sets the number of pages.</summary>
    public int? Pages { get; set; }

    /// <summary>Gets or sets the version of the document.</summary>
    public string? Version { get; set; }

    /// <summary>Gets or sets a value indicating whether the reader must rebuild the cross-reference information.</summary>
    public bool? Repaired { get; set; }

    /// <summary>Gets or sets the text of each page.</summary>
    public Collection<string>? Text { get; init; }

    /// <summary>Gets or sets the title of the document information dictionary.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the name of the exception the library must throw for an invalid fixture.</summary>
    public string? Exception { get; set; }
}
