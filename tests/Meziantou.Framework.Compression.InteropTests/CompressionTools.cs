using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Compression.InteropTests;

/// <summary>
/// The pinned archivers, downloaded from the meziantou/prebuilt release pinned by the Meziantou.Prebuilt package: 7zz (7-Zip)
/// and unrar. A tool that cannot be downloaded or reports an unexpected version fails the test, it is never skipped.
/// </summary>
internal sealed class CompressionTools
{
    private static readonly Lazy<Task<CompressionTools>> Instance = new(() => LocateAsync(CancellationToken.None));

    private CompressionTools(string sevenZip, string unrar)
    {
        SevenZipPath = sevenZip;
        UnrarPath = unrar;
    }

    public string SevenZipPath { get; }

    public string UnrarPath { get; }

    /// <summary>Gets a value indicating whether the 7-Zip of the platform has the RAR codec (<c>7za.exe</c> of Windows has none).</summary>
    public static bool SevenZipHasRar => !OperatingSystem.IsWindows();

    public static Task<CompressionTools> GetAsync() => Instance.Value;

    private static async Task<CompressionTools> LocateAsync(CancellationToken cancellationToken)
    {
        var sevenZip = await PrebuiltTools.SevenZip.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var unrar = await PrebuiltTools.Unrar.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);

        // Neither has a version option: run without arguments, they print their banner and their usage
        await CheckVersionAsync(sevenZip, PrebuiltTools.SevenZip.Version, cancellationToken).ConfigureAwait(false);
        await CheckVersionAsync(unrar, PrebuiltTools.Unrar.Version, cancellationToken).ConfigureAwait(false);
        return new CompressionTools(sevenZip, unrar);
    }

    private static async Task CheckVersionAsync(string path, string expectedVersion, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(path, [], cancellationToken).ConfigureAwait(false);
        var output = result.StandardOutputText + result.StandardError;
        if (!output.Contains(expectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"'{path}' does not report version {expectedVersion}: {output}");
    }

    /// <summary>Runs 7zz and throws when it fails.</summary>
    public Task<ProcessResult> RunSevenZipAsync(params string[] arguments) => RunAsync(SevenZipPath, arguments);

    /// <summary>Runs unrar and throws when it fails.</summary>
    public Task<ProcessResult> RunUnrarAsync(params string[] arguments) => RunAsync(UnrarPath, arguments);

    private static async Task<ProcessResult> RunAsync(string path, string[] arguments)
    {
        var result = await ProcessRunner.RunAsync(path, arguments, TestContext.Current.CancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(path)} {string.Join(' ', arguments)} failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");

        return result;
    }
}
