using System.Diagnostics;
using System.Text;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

/// <summary>An external tool failed or produced unusable output.</summary>
internal sealed class ToolException(string message) : Exception(message);

/// <summary>A fatal configuration error (missing tool, unexpected version): reported without a stack trace.</summary>
internal sealed class FatalException(string message) : Exception(message);

internal sealed record ProcessResult(int ExitCode, byte[] Stdout, byte[] Stderr);

internal static class Proc
{
    /// <summary>Runs a command (no shell) and returns its exit code and raw outputs.</summary>
    public static ProcessResult Exec(IReadOnlyList<string> command, byte[]? input = null) => ExecAsync(command, input).GetAwaiter().GetResult();

    private static async Task<ProcessResult> ExecAsync(IReadOnlyList<string> command, byte[]? input)
    {
        var startInfo = new ProcessStartInfo(command[0])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in command.Skip(1))
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new ToolException("Cannot start " + command[0]);
        var stdout = new MemoryStream();
        var stderr = new MemoryStream();
        // Both outputs are drained concurrently (a full pipe would block the process) and awaited before disposal
#pragma warning disable CA2025
        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderrTask = process.StandardError.BaseStream.CopyToAsync(stderr);
#pragma warning restore CA2025
        try
        {
            if (input is not null)
                await process.StandardInput.BaseStream.WriteAsync(input).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The process exited without reading its whole input: its exit code reports the failure
        }

        process.StandardInput.Close();
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, stdout.ToArray(), stderr.ToArray());
    }

    /// <summary>Runs a command and returns its standard output (or its standard error); a nonzero exit code throws.</summary>
    public static byte[] Run(IReadOnlyList<string> command, byte[]? input = null, bool stderr = false)
    {
        var result = Exec(command, input);
        if (result.ExitCode != 0)
            throw new ToolException($"Command failed ({result.ExitCode}): {string.Join(' ', command)}\n{Encoding.UTF8.GetString(result.Stderr)}");
        return stderr ? result.Stderr : result.Stdout;
    }

    public static string RunText(IReadOnlyList<string> command, byte[]? input = null, bool stderr = false) => Encoding.UTF8.GetString(Run(command, input, stderr));
}
