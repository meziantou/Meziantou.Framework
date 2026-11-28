namespace Meziantou.Framework.Compression.Internals;

/// <summary>
/// Extracts entries under a directory, rejecting the entries whose path or link target leaves it. The times of the
/// directories are set by <see cref="Complete"/>, once their content is written.
/// </summary>
internal sealed class ArchiveExtractor
{
    // The paths are compared ordinally on every platform, even where the file system ignores the case: they all start
    // with the root as it is written, so an ordinal comparison never rejects a valid path, while an ignore-case comparison
    // would accept a sibling directory of the root on a case-sensitive volume ("../OUT/evil" from "/tmp/out").
    private const StringComparison PathComparison = StringComparison.Ordinal;

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

        if (entry.HardLinkTarget is { } hardLinkTarget)
        {
            CopyHardLink(entry, path, hardLinkTarget);
            return;
        }

        DeleteExistingLink(path);
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

        if (entry.HardLinkTarget is { } hardLinkTarget)
        {
            CopyHardLink(entry, path, hardLinkTarget);
            return;
        }

        DeleteExistingLink(path);
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

        // The root itself, such as the "./" entry of a tar archive created from a directory
        if (string.Equals(Path.TrimEndingDirectorySeparator(path), _root, PathComparison))
            return null;

        if (!IsUnderRoot(path))
            throw new IOException($"The entry '{name}' would be extracted outside of the destination directory.");

        EnsureNoLinkInPath(path, name);
        return path;
    }

    /// <summary>
    /// Ensures that the directories between the root and a path are not links. The paths are checked as text, so an
    /// archive could otherwise write through a link it created before: a link <c>b</c> to <c>.</c>, a link <c>a</c> to
    /// <c>b/..</c> (inside of the directory as text), then a file <c>a/evil</c> is written next to the directory.
    /// </summary>
    private void EnsureNoLinkInPath(string path, string entryName)
    {
        var directory = Path.GetDirectoryName(path);
        while (directory is not null && directory.Length > _root.Length)
        {
            if (new FileInfo(directory).LinkTarget is not null)
                throw new IOException($"The entry '{entryName}' would be extracted through the link '{directory}'.");

            directory = Path.GetDirectoryName(directory);
        }
    }

    /// <summary>Deletes a link at the path of a file to extract, so that the file replaces the link instead of being written to its target.</summary>
    private void DeleteExistingLink(string path)
    {
        if (new FileInfo(path).LinkTarget is null)
            return;

        if (!_overwrite)
            throw new IOException($"The file '{path}' already exists.");

        File.Delete(path);
    }

    private bool IsUnderRoot(string path)
    {
        return path.Length > _root.Length
            && path.StartsWith(_root, PathComparison)
            && (path[_root.Length] == Path.DirectorySeparatorChar || path[_root.Length] == Path.AltDirectorySeparatorChar);
    }

    private bool IsRootOrUnderRoot(string path)
    {
        return IsUnderRoot(path) || string.Equals(Path.TrimEndingDirectorySeparator(path), _root, PathComparison);
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

        EnsureLinkTargetIsUnderRoot(path, normalizedTarget, target);

        if (File.Exists(path) || Directory.Exists(path))
        {
            if (!_overwrite)
                throw new IOException($"The file '{path}' already exists.");

            File.Delete(path);
        }

        File.CreateSymbolicLink(path, normalizedTarget);
    }

    /// <summary>
    /// Ensures that the target of a symbolic link stays under the root once it is resolved on the file system. A
    /// <c>..</c> goes up from the directory reached before it, which is the parent as text only when that directory is a
    /// real directory: after a link (<c>b</c> to <c>.</c>, then <c>c</c> to <c>b/..</c>), it is the parent of the target
    /// of the link. After a path that does not exist yet, or a file, a later entry could create a link there. So a
    /// <c>..</c> is accepted only while every component before it is an existing directory that is not a link. The
    /// directories are never replaced by links during the extraction, so the check stays valid for the later entries.
    /// The names after a link stay under the root, as every link the archive creates points under the root.
    /// </summary>
    private void EnsureLinkTargetIsUnderRoot(string path, string normalizedTarget, string target)
    {
        var current = Path.GetDirectoryName(path);
        var isRealDirectory = true;
        foreach (var component in normalizedTarget.Split(Path.DirectorySeparatorChar))
        {
            if (component is "" or ".")
                continue;

            if (component is "..")
            {
                if (!isRealDirectory)
                    throw new IOException($"The symbolic link '{path}' goes up from a link or a path that does not exist, in '{target}'.");

                current = Path.GetDirectoryName(current);
                if (current is null || !IsRootOrUnderRoot(current))
                    throw new IOException($"The symbolic link '{path}' points outside of the destination directory, to '{target}'.");

                continue;
            }

            current = Path.GetFullPath(Path.Join(current, component));
            if (!IsRootOrUnderRoot(current))
                throw new IOException($"The symbolic link '{path}' points outside of the destination directory, to '{target}'.");

            if (isRealDirectory)
            {
                isRealDirectory = Directory.Exists(current) && new DirectoryInfo(current).LinkTarget is null;
            }
        }
    }

    private void CopyHardLink(ArchiveEntry entry, string path, string target)
    {
        var relative = target.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        if (relative.Length is 0 || Path.IsPathRooted(relative))
            throw new IOException($"The hard link '{entry.FullName}' points to an invalid path, '{target}'.");

        var source = Path.GetFullPath(Path.Join(_root, relative));
        if (!IsUnderRoot(source))
            throw new IOException($"The hard link '{entry.FullName}' points outside of the destination directory, to '{target}'.");

        EnsureNoLinkInPath(source, entry.FullName);
        if (new FileInfo(source).LinkTarget is not null)
            throw new IOException($"The hard link '{entry.FullName}' points to the link '{target}'.");

        DeleteExistingLink(path);

        if (!File.Exists(source))
            throw new IOException($"The hard link '{entry.FullName}' points to '{target}', which is not extracted before it.");

        File.Copy(source, path, _overwrite);
        SetMetadata(entry, path);
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
