using System.Diagnostics;
using Meziantou.Framework.InlineSnapshotTesting.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal abstract class GitTool : MergeTool
{
    protected static readonly Lazy<string> GitPath = new(() => ExecutableFinder.GetFullExecutablePath("git"));

    protected internal static (string Command, string Arguments) ParseCommandFromConfiguration(string value)
    {
        if (value is null)
            return ("", "");

        value = value.Trim();
        if (value is "")
            return ("", "");

        if (value[0] is '"')
        {
            var end = value.IndexOf('"', 1);
            if (end < 0)
                return (value, "");

            return (value[1..end], value[(end + 1)..].TrimStart());
        }

        var space = value.IndexOf(' ', StringComparison.Ordinal);
        if (space < 0)
            return (value, "");

        return (value[..space], value[(space + 1)..].TrimStart());
    }

    protected static string? GetGitConfiguration(string workingDirectory, string key)
    {
        var gitPath = GitPath.Value;
        if (gitPath is null)
            return null;

        var psi = new ProcessStartInfo(gitPath)
        {
            Arguments = "config --get --null " + key,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return null;

        process.WaitForExit();
        if (process.ExitCode != 0)
            return null;

        return process.StandardOutput.ReadToEnd().TrimEnd('\0');
    }
}
