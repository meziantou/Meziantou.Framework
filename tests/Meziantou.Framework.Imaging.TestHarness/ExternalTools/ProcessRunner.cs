using System.ComponentModel;
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
    public static Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
#if !NET11_0_OR_GREATER
        // Process.Start forks on macOS before .NET 11: the starts of a process then run one at a time, at several milliseconds each
        if (OperatingSystem.IsMacOS())
            return PosixSpawnProcess.RunAsync(fileName, arguments, cancellationToken);
#endif

        return RunWithProcessAsync(fileName, arguments, cancellationToken);
    }

    private static async Task<ProcessResult> RunWithProcessAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
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

        using var process = await StartAsync(startInfo, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Starts a process. Linux refuses to run a file that any process holds open for writing ("Text file busy"), and a
    /// tool that was just downloaded is in that state for a moment: a process forked by another thread inherited the
    /// handle the tool was written with, and keeps it until it runs its own image. The start is tried again until that
    /// process is gone; any other failure, such as a file that does not exist, is reported at once.
    /// </summary>
    private static async Task<Process> StartAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        const int TextFileBusy = 26;

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return Process.Start(startInfo) ?? throw new InvalidOperationException($"Cannot start '{startInfo.FileName}'.");
            }
            catch (Win32Exception ex) when (!OperatingSystem.IsWindows() && ex.NativeErrorCode is TextFileBusy && stopwatch.Elapsed <= TimeSpan.FromSeconds(30))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
