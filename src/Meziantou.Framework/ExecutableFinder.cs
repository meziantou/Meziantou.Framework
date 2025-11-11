namespace Meziantou.Framework;

/// <summary>
/// Provides methods to locate executables in the system PATH.
/// </summary>
/// <example>
/// <code>
/// string? gitPath = ExecutableFinder.GetFullExecutablePath("git");
/// if (gitPath is not null)
/// {
///     Console.WriteLine($"Git found at: {gitPath}");
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
        var separator = Path.PathSeparator;
        var extensions = OperatingSystem.IsWindows() ? (Environment.GetEnvironmentVariable("PATHEXT") ?? "").Split(separator) : [];
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(separator);

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
            if (File.Exists(fullPath))
                return fullPath;

            foreach (var extension in extensions)
            {
                var pathWithExt = fullPath + extension;
                if (File.Exists(pathWithExt))
                    return pathWithExt;
            }

            return null;
        }
    }
}
