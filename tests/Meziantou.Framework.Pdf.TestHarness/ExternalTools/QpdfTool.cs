using System.Globalization;
using System.Text.Json;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Pdf.TestHarness.ExternalTools;

/// <summary>
/// qpdf: <c>--check</c> checks the structure of a file and the encoding of its streams, <c>--json</c> dumps its objects
/// (the oracle of the differential tests of the parser), and the rewriting options (<c>--object-streams</c>,
/// <c>--linearize</c>, <c>--qdf</c>) produce the variants of the fixtures.
/// </summary>
public sealed class QpdfTool
{
    private QpdfTool(string path)
    {
        Path = path;
    }

    /// <summary>Gets the path of the executable.</summary>
    public string Path { get; }

    /// <summary>Gets the version of the tool.</summary>
    public static string Version => PrebuiltTools.Qpdf.Version;

    /// <summary>Downloads the tool and checks its version.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tool.</returns>
    /// <exception cref="ExternalToolUnavailableException">The tool is not available.</exception>
    public static async Task<QpdfTool> LocateAsync(CancellationToken cancellationToken) => new(await PrebuiltToolLocator.LocateAsync(PrebuiltTools.Qpdf, ["--version"], cancellationToken).ConfigureAwait(false));

    /// <summary>Runs <c>qpdf --check</c>.</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The result: exit code 0 without warning, 3 with warnings, 2 with errors.</returns>
    public Task<ProcessResult> CheckAsync(string path, CancellationToken cancellationToken) => ProcessRunner.RunAsync(Path, ["--check", path], cancellationToken);

    /// <summary>Gets the objects of a file as qpdf reads them (<c>--json=2 --json-key=qpdf</c>).</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The objects, by their key (<c>obj:12 0 R</c>), and whether qpdf reported warnings.</returns>
    /// <exception cref="InvalidOperationException">qpdf failed.</exception>
    public async Task<QpdfObjects> GetObjectsAsync(string path, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(Path, ["--json=2", "--json-key=qpdf", path], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode is not (0 or 3))
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"qpdf --json failed on '{path}' (exit code {result.ExitCode}): {result.StandardError}"));

        using var document = JsonDocument.Parse(result.StandardOutput);
        var objects = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.GetProperty("qpdf")[1].EnumerateObject())
        {
            objects[item.Name] = item.Value.Clone();
        }

        return new QpdfObjects(objects, result.ExitCode == 3);
    }

    /// <summary>Rewrites a file with options, such as <c>--object-streams=generate</c> or <c>--linearize</c>.</summary>
    /// <param name="inputPath">The file to read.</param>
    /// <param name="outputPath">The file to write.</param>
    /// <param name="arguments">The options.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="InvalidOperationException">qpdf failed.</exception>
    public async Task RewriteAsync(string inputPath, string outputPath, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(Path, [.. arguments, inputPath, outputPath], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode is not (0 or 3))
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"qpdf failed on '{inputPath}' (exit code {result.ExitCode}): {result.StandardError}"));
    }
}
