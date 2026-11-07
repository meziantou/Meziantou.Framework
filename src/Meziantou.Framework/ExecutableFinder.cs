namespace Meziantou.Framework;

/// <summary>Provides methods to locate executables in the system PATH.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Tests/ExecutableFinderTests.cs" ref:region="ExecutableFinder_GetFullExecutablePath" ref:dedent="true">
/// string? gitPath = ExecutableFinder.GetFullExecutablePath("git");
/// if (gitPath is not null)
/// {
///     Assert.True(File.Exists(gitPath));
/// }
/// </code>
/// </example>
#if PUBLIC_EXECUTABLE_FINDER
public
#else
internal
#endif
static class ExecutableFinder
{
    /// <summary>Searches for an executable in the system PATH and returns its full path if found.</summary>
    // https://docs.microsoft.com/en-us/windows-server/administration/windows-commands/path
    public static string? GetFullExecutablePath(string executableName, string? workingDirectory = null)
    {
        return GetFullExecutablePath(executableName, workingDirectory, Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT"));
    }

    internal static string? GetFullExecutablePath(string executableName, string? workingDirectory, string? pathVariable, string? pathExtVariable)
    {
        var extensions = OperatingSystem.IsWindows() ? GetWindowsExecutableExtensions(pathExtVariable) : [];
        var path = SplitPathVariable(pathVariable, OperatingSystem.IsWindows());

        IEnumerable<string> searchPaths = path;
        if (workingDirectory is not null)
        {
            searchPaths = path.Prepend(workingDirectory);
        }

        foreach (var searchPath in searchPaths)
        {
            var result = TryFindInDirectory(executableName, searchPath, extensions);
            if (result is not null)
                return Path.GetFullPath(result);
        }

        return null;

        static string? TryFindInDirectory(string executableName, string directory, string[] extensions)
        {
            var fullPath = Path.Combine(directory, executableName);

            // On Windows, cmd.exe never runs an extensionless file. For instance, the Node.js folder contains both 'npm' (a shell
            // script for Git Bash) and 'npm.cmd', and only the latter can be started by Process.Start.
            if (!OperatingSystem.IsWindows() || Path.HasExtension(executableName))
            {
                if (File.Exists(fullPath) && IsExecutable(fullPath))
                    return fullPath;
            }

            foreach (var extension in extensions)
            {
                var pathWithExt = fullPath + extension;
                if (File.Exists(pathWithExt) && IsExecutable(pathWithExt))
                    return pathWithExt;
            }

            return null;
        }

        static string[] GetWindowsExecutableExtensions(string? pathExt)
        {
            // Same default as cmd.exe when PATHEXT is not set
            if (string.IsNullOrWhiteSpace(pathExt))
                return [".COM", ".EXE", ".BAT", ".CMD"];

            return pathExt.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        static bool IsExecutable(string path)
        {
            if (OperatingSystem.IsWindows())
                return true;

            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != UnixFileMode.None;
        }
    }

    internal static List<string> SplitPathVariable(string? pathVariable, bool isWindows)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(pathVariable))
            return result;

        if (!isWindows)
        {
            // POSIX treats an empty entry as the current directory, which Path.Combine resolves the same way
            result.AddRange(pathVariable.Split(':'));
            return result;
        }

        // cmd.exe removes the quotes around an entry, and a quoted entry can contain ';'. Empty entries are ignored.
        var entry = new StringBuilder();
        var inQuotes = false;
        foreach (var c in pathVariable)
        {
            if (c is '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c is ';' && !inQuotes)
            {
                AddEntry(result, entry);
            }
            else
            {
                entry.Append(c);
            }
        }

        AddEntry(result, entry);
        return result;

        static void AddEntry(List<string> result, StringBuilder entry)
        {
            if (entry.Length > 0)
            {
                result.Add(entry.ToString());
                entry.Clear();
            }
        }
    }
}
