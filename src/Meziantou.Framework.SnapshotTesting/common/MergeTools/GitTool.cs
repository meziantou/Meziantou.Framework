using System.Diagnostics;

#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;
#else
namespace Meziantou.Framework.SnapshotTesting.MergeTools;
#endif

internal abstract class GitTool : MergeTool
{
    private const int GitConfigurationTimeoutInMilliseconds = 10_000;

    protected static readonly Lazy<string?> GitPath = new(() => ExecutableFinder.GetFullExecutablePath("git"));

    protected internal static (string Command, string Arguments) ParseCommandFromConfiguration(string value)
    {
        if (value is null)
            return ("", "");

        value = value.Trim();
        if (value is "")
            return ("", "");

        if (value[0] is '"')
        {
            var end = value.IndexOf('"', 1, StringComparison.Ordinal);
            if (end < 0)
                return (value, "");

            return (value[1..end], value[(end + 1)..].TrimStart());
        }

        var space = value.IndexOf(' ', StringComparison.Ordinal);
        if (space < 0)
            return (value, "");

        return (value[..space], value[(space + 1)..].TrimStart());
    }

    protected static string? GetGitConfiguration(string? workingDirectory, string key)
    {
        var gitPath = GitPath.Value;
        if (gitPath is null)
            return null;

        var psi = new ProcessStartInfo(gitPath)
        {
            Arguments = "config --get --null " + key,
            RedirectStandardOutput = true,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return null;

        // The pipe must be drained while git is still running. Waiting for exit first deadlocks as soon as git
        // writes more than a pipe buffer. Only stdout is redirected: stderr carries warnings such as the
        // safe.directory ownership diagnostics that nothing here reads, so it is left inherited.
        var standardOutput = process.StandardOutput.ReadToEndAsync();

        // This runs on the failure path of every failing snapshot assertion, so a git that never returns must
        // not take the test run down with it.
        if (!process.WaitForExit(GitConfigurationTimeoutInMilliseconds))
        {
            TryKill(process);
            return null;
        }

        // WaitForExit(int) returns as soon as the process ends, without waiting for the redirected stream.
        if (!standardOutput.Wait(GitConfigurationTimeoutInMilliseconds))
            return null;

        if (process.ExitCode != 0)
            return null;

        return standardOutput.Result.TrimEnd('\0');
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
