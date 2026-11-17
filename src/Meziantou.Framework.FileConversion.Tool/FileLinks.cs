namespace Meziantou.Framework.FileConversionTool;

/// <summary>Resolves the symbolic links and the junctions of the paths of the file system.</summary>
internal static class FileLinks
{
    /// <summary>Gets the path of a file with the links of its path resolved: the links of the folders, and the link of the file.</summary>
    public static FullPath Resolve(FullPath path) => Resolve(path, depth: 0);

    /// <summary>
    /// Gets a key that is equal for two paths of the same file: the path with its links resolved, in upper case on Windows
    /// and macOS, whose file systems usually ignore the case.
    /// </summary>
    public static FullPath GetComparisonKey(FullPath path)
    {
        var result = Resolve(path);
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? FullPath.FromPath(result.Value.ToUpperInvariant()) : result;
    }

    private static FullPath Resolve(FullPath path, int depth)
    {
        // A cycle of links, or a too long chain: the path as it is
        if (depth > 40)
            return path;

        var value = path.Value;
        var root = Path.GetPathRoot(value);
        if (string.IsNullOrEmpty(root))
            return path;

        var current = root;
        foreach (var segment in value[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            try
            {
                FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
                if (info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                {
                    next = Resolve(FullPath.FromPath(target.FullName), depth + 1).Value;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A broken or unreadable link: its path as it is
            }

            current = next;
        }

        return FullPath.FromPath(current);
    }
}
