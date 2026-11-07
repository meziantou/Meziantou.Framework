using System.Collections.ObjectModel;

namespace Meziantou.Framework.Pdf.TestHarness.Fixtures;

/// <summary>How the generated fixtures are produced.</summary>
public sealed class PdfFixtureGenerator
{
    /// <summary>Gets or sets the script that generates the fixtures.</summary>
    public string Script { get; set; } = "";

    /// <summary>Gets or sets the command that regenerates them.</summary>
    public string Command { get; set; } = "";

    /// <summary>Gets the tools the script runs, with their versions.</summary>
    public Collection<string> Tools { get; init; } = [];
}
