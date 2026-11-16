namespace Meziantou.Framework.Compression.Internals;

/// <summary>
/// Extracts entries under a directory, rejecting the entries whose path or link target leaves it. The times of the
/// directories are set by <see cref="Complete"/>, once their content is written.
/// </summary>
internal sealed class ArchiveExtractor
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _root;
    private readonly bool _overwrite;
    private readonly List<(string Path, DateTimeOffset Time)> _directoryTimes = [];

    public ArchiveExtractor(string destinationDirectoryName, bool overwrite)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectoryName));
        _overwrite = overwrite;
        Directory.CreateDirectory(_root);
    }

    public void Extract(ArchiveEntry entry)
    {
        var path = GetDestinationPath(entry);
        if (path is null)
            return;

        if (entry.IsDirectory)
        {
            CreateDirectory(entry, path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (entry.LinkTarget is { } target)
        {
            CreateSymbolicLink(path, target);
            return;
        }

        ExtractFile(entry, path, _overwrite);
    }

    public async Task ExtractAsync(ArchiveEntry entry, CancellationToken cancellationToken)
    {
        var path = GetDestinationPath(entry);
        if (path is null)
            return;

        if (entry.IsDirectory)
        {
            CreateDirectory(entry, path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (entry.LinkTarget is { } target)
        {
            CreateSymbolicLink(path, target);
            return;
        }

        await ExtractFileAsync(entry, path, _overwrite, cancellationToken).ConfigureAwait(false);
    }

    public void Complete()
    {
        // Deepest first, so that setting the time of a directory does not change the time of its parent afterwards
        for (var i = _directoryTimes.Count - 1; i >= 0; i--)
        {
            var (path, time) = _directoryTimes[i];
            TrySetLastWriteTime(path, time, isDirectory: true);
        }
    }

    public static void ExtractFile(ArchiveEntry entry, string path, bool overwrite)
    {
        using (var source = entry.Open())
        using (var destination = new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 0))
        {
            source.CopyTo(destination);
        }

        SetMetadata(entry, path);
    }

    public static async Task ExtractFileAsync(ArchiveEntry entry, string path, bool overwrite, CancellationToken cancellationToken)
    {
        var source = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            var destination = new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 0, useAsync: true);
            await using (destination.ConfigureAwait(false))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
        }

        SetMetadata(entry, path);
    }

    /// <summary>Gets the full path of an entry under the root, or <see langword="null"/> for the entry of the root itself.</summary>
    private string? GetDestinationPath(ArchiveEntry entry)
    {
        var name = entry.FullName;
        if (name.Contains('\0', StringComparison.Ordinal))
            throw new IOException($"The name of the entry '{name}' contains a null character.");

        var relative = name.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        if (relative.Length is 0)
            return null;

        if (Path.IsPathRooted(relative))
            throw new IOException($"The entry '{name}' has a rooted path, which would be extracted outside of the destination directory.");

        var path = Path.GetFullPath(Path.Join(_root, relative));
        if (!IsUnderRoot(path))
            throw new IOException($"The entry '{name}' would be extracted outside of the destination directory.");

        if (string.Equals(path, _root, PathComparison))
            return null;

        return path;
    }

    private bool IsUnderRoot(string path)
    {
        return path.Length > _root.Length
            && path.StartsWith(_root, PathComparison)
            && (path[_root.Length] == Path.DirectorySeparatorChar || path[_root.Length] == Path.AltDirectorySeparatorChar);
    }

    private void CreateDirectory(ArchiveEntry entry, string path)
    {
        Directory.CreateDirectory(path);
        if (entry.LastWriteTime is { } time)
        {
            _directoryTimes.Add((path, time));
        }
    }

    private void CreateSymbolicLink(string path, string target)
    {
        var normalizedTarget = target.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedTarget))
            throw new IOException($"The symbolic link '{path}' points to an absolute path, '{target}'.");

        var resolved = Path.GetFullPath(Path.Join(Path.GetDirectoryName(path), normalizedTarget));
        if (!IsUnderRoot(resolved) && !string.Equals(resolved, _root, PathComparison))
            throw new IOException($"The symbolic link '{path}' points outside of the destination directory, to '{target}'.");

        if (File.Exists(path) || Directory.Exists(path))
        {
            if (!_overwrite)
                throw new IOException($"The file '{path}' already exists.");

            File.Delete(path);
        }

        File.CreateSymbolicLink(path, normalizedTarget);
    }

    private static void SetMetadata(ArchiveEntry entry, string path)
    {
        if (!OperatingSystem.IsWindows() && entry.UnixFileMode is { } mode)
        {
            // Only the permission bits: the setuid, setgid and sticky bits are not restored
            var permissions = mode & (System.IO.UnixFileMode)0x1FF;
            if (permissions is not System.IO.UnixFileMode.None)
            {
                try
                {
                    File.SetUnixFileMode(path, permissions);
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        if (entry.LastWriteTime is { } time)
        {
            TrySetLastWriteTime(path, time, isDirectory: false);
        }
    }

    private static void TrySetLastWriteTime(string path, DateTimeOffset time, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                Directory.SetLastWriteTimeUtc(path, time.UtcDateTime);
            }
            else
            {
                File.SetLastWriteTimeUtc(path, time.UtcDateTime);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // A time the file system cannot represent
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
