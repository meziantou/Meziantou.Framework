namespace Meziantou.Framework;

public static class ExecutableFinder
{
    // https://docs.microsoft.com/en-us/windows-server/administration/windows-commands/path
    public static string? GetFullExecutablePath(string executableName, string? workingDirectory = null)
    {
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var extensions = OperatingSystem.IsWindows() ? (Environment.GetEnvironmentVariable("PATHEXT") ?? "").Split(separator) : Array.Empty<string>();
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(separator);

        IEnumerable<string> searchPaths = path;
        if (workingDirectory != null)
        {
            searchPaths = path.Prepend(workingDirectory);
        }

        foreach (var searchPath in searchPaths)
        {
            var result = TryFindInDirectory(executableName, searchPath, extensions);
            if (result != null)
                return Path.GetFullPath(result);
        }

        return null;

        static string? TryFindInDirectory(string executableName, string directory, string[] extensions)
        {
            var fullPath = Path.Combine(directory, executableName);
            if (File.Exists(fullPath))
                return fullPath;

            if (!executableName.Contains('.', StringComparison.Ordinal))
            {
                foreach (var extension in extensions)
                {
                    var pathWithExt = fullPath + extension;
                    if (File.Exists(pathWithExt))
                        return pathWithExt;
                }
            }

            return null;
        }
    }
}
