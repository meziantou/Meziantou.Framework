using System.Diagnostics;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>Runs external tools with explicit arguments (no shell), capturing binary standard output.</summary>
public static class ProcessRunner
{
    /// <summary>Runs a process to completion.</summary>
    /// <param name="fileName">The executable path.</param>
    /// <param name="arguments">The arguments, passed without shell interpretation.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests; the process is killed on cancellation.</param>
    /// <returns>The result.</returns>
    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Cannot start '{fileName}'.");
        process.StandardInput.Close();

        // Not disposed: MemoryStream holds no unmanaged resource
        var output = new MemoryStream();
#pragma warning disable CA2025 // False positive: both tasks are awaited in the finally block below, before the process is disposed
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
#pragma warning restore CA2025
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        finally
        {
            // Never dispose the process while the redirected streams are still being read
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        var error = await errorTask.ConfigureAwait(false);
        await outputTask.ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, output.ToArray(), error);
    }
}
