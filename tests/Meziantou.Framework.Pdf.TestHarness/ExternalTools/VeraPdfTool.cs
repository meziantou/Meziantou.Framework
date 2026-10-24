using System.Globalization;
using System.Xml.Linq;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Pdf.TestHarness.ExternalTools;

/// <summary>veraPDF, the reference validator of PDF/A (ISO 19005) and PDF/UA (ISO 14289), at the version the Meziantou.Prebuilt release pins.</summary>
public sealed class VeraPdfTool
{
    private VeraPdfTool(string path) => Path = path;

    /// <summary>Gets the path of the executable.</summary>
    public string Path { get; }

    /// <summary>Gets the version of the tool.</summary>
    public static string Version => PrebuiltTools.VeraPdf.Version;

    /// <summary>Downloads the tool and checks its version.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tool.</returns>
    /// <exception cref="ExternalToolUnavailableException">The tool is not available.</exception>
    public static async Task<VeraPdfTool> LocateAsync(CancellationToken cancellationToken) => new(await PrebuiltToolLocator.LocateAsync(PrebuiltTools.VeraPdf, ["--version"], cancellationToken).ConfigureAwait(false));

    /// <summary>Validates a file against a flavour (<c>2b</c>, <c>2a</c>, <c>4</c>, <c>ua1</c>...).</summary>
    /// <param name="path">The file.</param>
    /// <param name="flavour">The flavour.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The rules the file fails, as <c>clause-test: description</c>; empty when the file is valid.</returns>
    /// <exception cref="InvalidOperationException">veraPDF failed.</exception>
    public async Task<IReadOnlyList<string>> ValidateAsync(string path, string flavour, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(Path, ["--flavour", flavour, "--format", "xml", path], cancellationToken).ConfigureAwait(false);
        var output = result.StandardOutputText;
        var start = output.IndexOf('<', StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"veraPDF failed on '{path}' (exit code {result.ExitCode}): {result.StandardError}"));

        var document = XDocument.Parse(output[start..]);
        var failures = new List<string>();
        foreach (var rule in document.Descendants("rule"))
        {
            if ((string?)rule.Attribute("status") != "failed")
                continue;

            failures.Add($"{rule.Attribute("clause")?.Value}-{rule.Attribute("testNumber")?.Value}: {rule.Element("description")?.Value}");
        }

        if (!document.Descendants("validationReport").Any())
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"veraPDF did not validate '{path}' (exit code {result.ExitCode}): {output}"));

        return failures;
    }
}
