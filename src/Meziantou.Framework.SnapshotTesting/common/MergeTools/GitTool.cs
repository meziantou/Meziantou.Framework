using System.Diagnostics;
using System.Text.RegularExpressions;

#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;
#else
namespace Meziantou.Framework.SnapshotTesting.MergeTools;
#endif

internal abstract partial class GitTool : MergeTool
{
    private const int GitTimeoutInMilliseconds = 10_000;

    protected static readonly Lazy<string?> GitPath = new(() => ExecutableFinder.GetFullExecutablePath("git"));

    private static readonly Lazy<string?> GitShellPath = new(FindGitShell);

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

    /// <summary>
    /// Reads the <c>cmd</c> of the tool selected by <paramref name="toolKey" /> (<c>merge.tool</c> or <c>diff.tool</c>),
    /// or returns <see langword="null" /> when no tool is selected or the tool has no command.
    /// </summary>
    protected static string? GetToolCommand(string? workingDirectory, string toolKey, string toolSection)
    {
        var toolName = GetGitConfiguration(workingDirectory, toolKey);
        if (string.IsNullOrWhiteSpace(toolName))
            return null;

        var command = GetGitConfiguration(workingDirectory, toolSection + "." + toolName + ".cmd");
        if (string.IsNullOrWhiteSpace(command))
            return null;

        return command;
    }

    protected static string? GetGitConfiguration(string? workingDirectory, string key)
    {
        // The key is a single argument: a tool name may contain spaces, which git accepts as a subsection name.
        return RunGit(workingDirectory, "config", "--get", "--null", key)?.TrimEnd('\0');
    }

    /// <summary>
    /// Creates the start information for a <c>mergetool.&lt;tool&gt;.cmd</c> or <c>difftool.&lt;tool&gt;.cmd</c> command.
    /// </summary>
    /// <remarks>
    /// Git evaluates these commands with a shell in which <c>LOCAL</c>, <c>REMOTE</c>, <c>BASE</c> and <c>MERGED</c> are
    /// variables. Running the command the same way - verbatim, through <c>sh -c</c>, with the variables in the
    /// environment - supports everything git supports: quoted or unquoted placeholders, <c>${LOCAL}</c>, paths with spaces
    /// or quotes, <c>~</c>, <c>&amp;&amp;</c>, <c>VAR=value tool</c>. On Windows, the shell that comes with Git for
    /// Windows is used. When it cannot be found, each placeholder is replaced by its value quoted as a single argument
    /// and the command is split using the Windows command-line rules, which covers the usual <c>tool "$LOCAL" "$REMOTE"</c>
    /// commands.
    /// </remarks>
    protected internal static ProcessStartInfo CreateCommandStartInfo(string command, string? workingDirectory, IReadOnlyList<KeyValuePair<string, string>> variables)
    {
        var shellPath = OperatingSystem.IsWindows() ? GitShellPath.Value : "/bin/sh";
        var startInfo = shellPath is not null ? CreateShellCommandStartInfo(shellPath, command, variables) : CreateCommandStartInfoWithoutShell(command, variables);
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        return startInfo;
    }

    protected internal static ProcessStartInfo CreateShellCommandStartInfo(string shellPath, string command, IReadOnlyList<KeyValuePair<string, string>> variables)
    {
        var startInfo = new ProcessStartInfo(shellPath)
        {
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);
        foreach (var variable in variables)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        return startInfo;
    }

    protected internal static ProcessStartInfo CreateCommandStartInfoWithoutShell(string command, IReadOnlyList<KeyValuePair<string, string>> variables)
    {
        var (fileName, arguments) = ExpandCommandWithoutShell(command, variables);
        if (fileName is "")
            throw new InvalidOperationException($"The command '{command}' does not specify an executable.");

        return new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
        };
    }

    protected internal static (string Command, string Arguments) ExpandCommandWithoutShell(string command, IReadOnlyList<KeyValuePair<string, string>> variables)
    {
        var expandedCommand = PlaceholderRegex().Replace(command, match =>
        {
            var name = match.Groups["name"].Value;
            foreach (var variable in variables)
            {
                if (string.Equals(variable.Key, name, StringComparison.Ordinal))
                {
                    // The quotes that surround the placeholder in the command are part of the match, so a quoted
                    // placeholder does not end up quoted twice.
                    return CommandLineBuilder.WindowsQuotedArgument(variable.Value);
                }
            }

            return match.Value;
        });

        return ParseCommandFromConfiguration(expandedCommand);
    }

    private static string? RunGit(string? workingDirectory, params string[] arguments)
    {
        var gitPath = GitPath.Value;
        if (gitPath is null)
            return null;

        var psi = new ProcessStartInfo(gitPath)
        {
            RedirectStandardOutput = true,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi);
        if (process is null)
            return null;

        // The pipe must be drained while git is still running. Waiting for exit first deadlocks as soon as git
        // writes more than a pipe buffer. Only stdout is redirected: stderr carries warnings such as the
        // safe.directory ownership diagnostics that nothing here reads, so it is left inherited.
        var standardOutput = process.StandardOutput.ReadToEndAsync();

        // This runs on the failure path of every failing snapshot assertion, so a git that never returns must
        // not take the test run down with it.
        if (!process.WaitForExit(GitTimeoutInMilliseconds))
        {
            TryKill(process);
            return null;
        }

        // WaitForExit(int) returns as soon as the process ends, without waiting for the redirected stream.
        if (!standardOutput.Wait(GitTimeoutInMilliseconds))
            return null;

        if (process.ExitCode != 0)
            return null;

        return standardOutput.Result;
    }

    /// <summary>Finds the <c>sh.exe</c> that Git for Windows ships next to <c>git.exe</c>.</summary>
    private static string? FindGitShell()
    {
        var gitPath = GitPath.Value;
        if (gitPath is null)
            return null;

        // git.exe is found in "cmd", "bin" or "mingw64\bin" depending on what was added to the PATH.
        var gitDirectory = Path.GetDirectoryName(gitPath);
        if (gitDirectory is not null)
        {
            var shell = FindShell(gitDirectory, "sh.exe", @"..\bin\sh.exe", @"..\usr\bin\sh.exe", @"..\..\bin\sh.exe", @"..\..\usr\bin\sh.exe");
            if (shell is not null)
                return shell;
        }

        // A shim (scoop, chocolatey, ...) is not in the installation directory, but git knows where it is installed.
        // The exec path is "<installation>\mingw64\libexec\git-core".
        var execPath = RunGit(workingDirectory: null, "--exec-path")?.Trim();
        if (string.IsNullOrEmpty(execPath))
            return null;

        return FindShell(execPath, @"..\..\..\bin\sh.exe", @"..\..\..\usr\bin\sh.exe");

        static string? FindShell(string directory, params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    var path = Path.GetFullPath(Path.Combine(directory, candidate));
                    if (File.Exists(path))
                        return path;
                }
                catch (ArgumentException)
                {
                }
                catch (IOException)
                {
                }
            }

            return null;
        }
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

    // $LOCAL, ${LOCAL}, "$LOCAL" or '$LOCAL', but not $LOCALS or $LOCAL_PATH, which are other shell variables.
    [GeneratedRegex("""(?<quote>["']?)\$(?:\{(?<name>LOCAL|REMOTE|BASE|MERGED)\}|(?<name>LOCAL|REMOTE|BASE|MERGED)(?![A-Za-z0-9_]))\k<quote>""", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlaceholderRegex();
}
