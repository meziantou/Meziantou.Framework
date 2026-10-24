using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Pdf.TestHarness.ExternalTools;

/// <summary>
/// TestGrammar, the checker of the Arlington PDF Model, with the TSV files of the model at the commit it is built from.
/// It exits with 0 whatever it finds: the problems are the <c>Error:</c> lines of its report.
/// </summary>
public sealed class ArlingtonTestGrammarTool
{
    private ArlingtonTestGrammarTool(string path)
    {
        Path = path;
        TsvDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "tsv", "latest");
    }

    /// <summary>Gets the path of the executable.</summary>
    public string Path { get; }

    /// <summary>Gets the directory of the TSV files of the model the tool is built with.</summary>
    public string TsvDirectory { get; }

    /// <summary>Downloads the tool and checks its version.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tool.</returns>
    /// <exception cref="ExternalToolUnavailableException">The tool is not available.</exception>
    public static async Task<ArlingtonTestGrammarTool> LocateAsync(CancellationToken cancellationToken)
    {
        var tool = new ArlingtonTestGrammarTool(await PrebuiltToolLocator.LocateAsync(PrebuiltTools.ArlingtonTestGrammar, ["--help"], cancellationToken).ConfigureAwait(false));
        if (!Directory.Exists(tool.TsvDirectory))
            throw new ExternalToolUnavailableException($"The TSV files of the Arlington PDF Model are missing next to '{tool.Path}'");

        return tool;
    }

    /// <summary>Checks a file against the model.</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The <c>Error:</c> lines of the report.</returns>
    public async Task<IReadOnlyList<string>> CheckAsync(string path, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(Path, ["--tsvdir", TsvDirectory, "--no-color", "--brief", "--allfiles", "--pdf", path], cancellationToken).ConfigureAwait(false);
        var report = result.StandardOutputText + "\n" + result.StandardError;
        if (!report.Contains("DONE", StringComparison.Ordinal))
            throw new InvalidOperationException($"TestGrammar did not complete on '{path}' (exit code {result.ExitCode}): {report}");

        return [.. report.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("Error:", StringComparison.Ordinal))];
    }
}
