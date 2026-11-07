using System.Diagnostics;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

internal static class ProcessHelper
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Run external process and return the console output.
    /// In the case of any exception, null will be returned.
    /// </summary>
    internal static string? RunAndReadOutput(string fileName, string arguments = "")
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = "",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,

            // stderr is intentionally not redirected. Redirecting a stream nobody reads lets the
            // child block once the pipe buffer fills, which would deadlock the WaitForExit below.
            RedirectStandardError = false,
        };

        using var process = new Process { StartInfo = processStartInfo };
        try
        {
            process.Start();
        }
        catch (Exception)
        {
            return null;
        }

        try
        {
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return output;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
