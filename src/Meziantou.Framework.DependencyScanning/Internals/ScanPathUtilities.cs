namespace Meziantou.Framework.DependencyScanning.Internals;

internal static class ScanPathUtilities
{
    // The limit Linux applies when resolving a path (MAXSYMLINKS)
    private const int MaxSymbolicLinks = 40;

    private static readonly char[] DirectorySeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>Gets the comparison that matches how the usual file systems of the platform compare paths.</summary>
    public static StringComparison PathComparison { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Makes the root directory and the path of a scanned file comparable, so that <see cref="CandidateFileContext.RelativeDirectory"/> can be computed.</summary>
    /// <remarks>
    /// Both paths are made absolute, their <c>.</c> and <c>..</c> segments are removed, and the trailing separators of the
    /// root are trimmed. When the file is under the root in a different case, which only a case-insensitive file system
    /// allows, the root is spelled the way the file path spells it.
    /// </remarks>
    public static (string RootDirectory, string FilePath) NormalizeCandidatePaths(string rootDirectory, string filePath)
    {
        // GetFullPath collapses repeated separators, so at most one is left at the end. A root such as "/" keeps it.
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var path = Path.GetFullPath(filePath);
        if (PathComparison is StringComparison.OrdinalIgnoreCase &&
            !path.StartsWith(root, StringComparison.Ordinal) &&
            path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            root = path[..root.Length];
        }

        return (root, path);
    }

    /// <summary>Determines whether <paramref name="path"/> is under <paramref name="directory"/>. Both paths must be absolute and normalized.</summary>
    public static bool IsUnderDirectory(string path, string directory)
    {
        var trimmedDirectory = directory.AsSpan().TrimEnd(DirectorySeparators);
        return path.Length > trimmedDirectory.Length
            && path.AsSpan().StartsWith(trimmedDirectory, PathComparison)
            && Array.IndexOf(DirectorySeparators, path[trimmedDirectory.Length]) >= 0;
    }

    /// <summary>Gets the path with every symbolic link resolved, including in the directories that contain it.</summary>
    /// <returns>The resolved path, or <see langword="null"/> when it cannot be resolved, such as for a symbolic link loop.</returns>
    /// <remarks>A path that does not exist, or a dangling link, is resolved as far as it can be.</remarks>
    public static string? GetRealPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
            return null;

        // The components still to resolve. The next one is on top.
        var pending = new Stack<string>();
        PushComponents(pending, fullPath.AsSpan(root.Length));

        var current = root;
        var linkCount = 0;
        while (pending.TryPop(out var component))
        {
            if (component is ".")
                continue;

            if (component is "..")
            {
                // current never contains a link, so its parent is its real parent
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            var next = Path.Join(current, component);
            var target = GetLinkTarget(next);
            if (target is null)
            {
                current = next;
                continue;
            }

            linkCount++;
            if (linkCount > MaxSymbolicLinks)
                return null;

            // A relative target is relative to the directory that contains the link, which is current
            if (Path.IsPathFullyQualified(target))
            {
                var targetRoot = Path.GetPathRoot(target);
                if (string.IsNullOrEmpty(targetRoot))
                    return null;

                current = targetRoot;
                PushComponents(pending, target.AsSpan(targetRoot.Length));
            }
            else
            {
                PushComponents(pending, target);
            }
        }

        return current;
    }

    private static void PushComponents(Stack<string> pending, ReadOnlySpan<char> path)
    {
        var components = new List<string>();
        foreach (var range in path.SplitAny(DirectorySeparators))
        {
            var component = path[range];
            if (!component.IsEmpty)
            {
                components.Add(component.ToString());
            }
        }

        for (var i = components.Count - 1; i >= 0; i--)
        {
            pending.Push(components[i]);
        }
    }

    private static string? GetLinkTarget(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
