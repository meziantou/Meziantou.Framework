using System.Text;

namespace Meziantou.Framework.WasmEngine.Wasi.Internal;

internal sealed partial class WasiImplementation
{
    private WasiDirectoryFd GetDirectory(int fd) => GetFd(fd) as WasiDirectoryFd ?? throw new WasiException(WasiErrno.NotDirectory);

    /// <summary>Reads a path of the module: UTF-8, without null characters.</summary>
    internal static string ReadPath(WasiMemory memory, uint address, uint length)
    {
        if (length > MaxPathLength)
            throw new WasiException(WasiErrno.NameTooLong);

        var bytes = memory.Slice(address, length);
        if (bytes.Contains((byte)0))
            throw new WasiException(WasiErrno.InvalidArgument);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new WasiException(WasiErrno.IllegalByteSequence);
        }
    }

    /// <summary>
    /// Resolves a path of the module relative to a directory: the result is a canonical path of the file system of the
    /// directory, inside the directory. The entries before the last one are directories, or symbolic links that are
    /// followed; the last entry is followed when <paramref name="followFinalLink"/> is true or when the path ends with a
    /// slash. The absolute paths, the paths and the links that leave the directory, and the absolute links fail with
    /// <see cref="WasiErrno.NotCapable"/>.
    /// </summary>
    /// <returns>The path, and whether the path ends with a slash (the entry must be a directory).</returns>
    internal static (string Path, bool TrailingSlash) Resolve(WasiDirectoryFd directory, string path, bool followFinalLink)
    {
        if (path.Length == 0)
            throw new WasiException(WasiErrno.NoEntry);

        if (path[0] == '/')
            throw new WasiException(WasiErrno.NotCapable);

        // ReadPath rejects them too: the file systems never receive a null character
        if (path.Contains('\0', StringComparison.Ordinal))
            throw new WasiException(WasiErrno.InvalidArgument);

        var fileSystem = directory.FileSystem;
        var resolved = new List<string>();
        var pending = new List<string>();
        var trailingSlash = PushSegments(pending, path);
        var links = 0;
        while (pending.Count > 0)
        {
            var segment = pending[^1];
            pending.RemoveAt(pending.Count - 1);
            if (segment == ".")
                continue;

            if (segment == "..")
            {
                if (resolved.Count == 0)
                    throw new WasiException(WasiErrno.NotCapable);

                resolved.RemoveAt(resolved.Count - 1);
                continue;
            }

            var isLast = pending.Count == 0;
            var candidate = Combine(directory.Path, resolved, segment);
            var stat = fileSystem.GetStat(candidate);
            if (stat is { Type: WasiFileType.SymbolicLink } && (!isLast || followFinalLink || trailingSlash))
            {
                if (++links > MaxSymbolicLinks)
                    throw new WasiException(WasiErrno.Loop);

                var target = fileSystem.ReadSymbolicLink(candidate);
                if (target.Length == 0)
                    throw new WasiException(WasiErrno.NoEntry);

                if (target[0] == '/')
                    throw new WasiException(WasiErrno.NotCapable);

                if (target.Contains('\0', StringComparison.Ordinal))
                    throw new WasiException(WasiErrno.InvalidArgument);

                // The target is relative to the directory of the link: its segments replace the link
                var targetTrailingSlash = PushSegments(pending, target);
                if (isLast)
                {
                    trailingSlash |= targetTrailingSlash;
                }

                continue;
            }

            if (!isLast)
            {
                if (stat is null)
                    throw new WasiException(WasiErrno.NoEntry);

                if (stat.Value.Type != WasiFileType.Directory)
                    throw new WasiException(WasiErrno.NotDirectory);
            }

            resolved.Add(segment);
        }

        return (Combine(directory.Path, resolved, segment: null), trailingSlash);
    }

    /// <summary>Pushes the segments of a path in reverse order (the first segment is the last item), and returns whether the path designates a directory (a trailing slash, or a last segment <c>.</c> or <c>..</c>).</summary>
    private static bool PushSegments(List<string> pending, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            pending.Add(segments[i]);
        }

        return path.EndsWith('/', StringComparison.Ordinal) || segments is [.., "." or ".."];
    }

    private static string Combine(string directory, List<string> segments, string? segment)
    {
        var builder = new StringBuilder(directory);
        foreach (var item in segments)
        {
            if (builder.Length > 0)
            {
                builder.Append('/');
            }

            builder.Append(item);
        }

        if (segment is not null)
        {
            if (builder.Length > 0)
            {
                builder.Append('/');
            }

            builder.Append(segment);
        }

        return builder.ToString();
    }

    private static (string Path, bool TrailingSlash) Resolve(WasiMemory memory, WasiDirectoryFd directory, uint pathAddress, uint pathLength, bool followFinalLink) => Resolve(directory, ReadPath(memory, pathAddress, pathLength), followFinalLink);

    public WasiErrno PathCreateDirectory(Span<byte> bytes, int fd, uint pathAddress, uint pathLength)
    {
        var memory = new WasiMemory(bytes);
        var directory = GetDirectory(fd);
        directory.Require(WasiRights.PathCreateDirectory);
        var (path, _) = Resolve(memory, directory, pathAddress, pathLength, followFinalLink: false);
        var fileSystem = directory.FileSystem;
        if (fileSystem.GetStat(path) is not null)
            return WasiErrno.Exists;

        ThrowIfReadOnly(fileSystem);
        fileSystem.CreateDirectory(path);
        return WasiErrno.Success;
    }

    public WasiErrno PathFilestatGet(Span<byte> bytes, int fd, uint flags, uint pathAddress, uint pathLength, uint statAddress)
    {
        var memory = new WasiMemory(bytes);
        _ = memory.Slice(statAddress, FilestatSize);
        var directory = GetDirectory(fd);
        directory.Require(WasiRights.PathFilestatGet);
        var (path, trailingSlash) = Resolve(memory, directory, pathAddress, pathLength, (flags & WasiRights.LookupSymlinkFollow) != 0);
        var stat = directory.FileSystem.GetStat(path) ?? throw new WasiException(WasiErrno.NoEntry);
        if (trailingSlash && stat.Type != WasiFileType.Directory)
            return WasiErrno.NotDirectory;

        WriteFilestat(memory, statAddress, directory.FileSystem.Device, stat);
        return WasiErrno.Success;
    }

    public WasiErrno PathFilestatSetTimes(Span<byte> bytes, int fd, uint flags, uint pathAddress, uint pathLength, ulong accessTime, ulong writeTime, ushort timeFlags)
    {
        var memory = new WasiMemory(bytes);
        var directory = GetDirectory(fd);
        directory.Require(WasiRights.PathFilestatSetTimes);
        var (access, write) = GetTimes(accessTime, writeTime, timeFlags);
        var (path, trailingSlash) = Resolve(memory, directory, pathAddress, pathLength, (flags & WasiRights.LookupSymlinkFollow) != 0);
        var fileSystem = directory.FileSystem;
        var stat = fileSystem.GetStat(path) ?? throw new WasiException(WasiErrno.NoEntry);
        if (trailingSlash && stat.Type != WasiFileType.Directory)
            return WasiErrno.NotDirectory;

        ThrowIfReadOnly(fileSystem);
        fileSystem.SetTimes(path, access, write);
        return WasiErrno.Success;
    }

    public WasiErrno PathLink(Span<byte> bytes, int oldFd, uint oldFlags, uint oldPathAddress, uint oldPathLength, int newFd, uint newPathAddress, uint newPathLength)
    {
        var memory = new WasiMemory(bytes);
        var oldDirectory = GetDirectory(oldFd);
        var newDirectory = GetDirectory(newFd);
        oldDirectory.Require(WasiRights.PathLinkSource);
        newDirectory.Require(WasiRights.PathLinkTarget);
        var (source, sourceTrailingSlash) = Resolve(memory, oldDirectory, oldPathAddress, oldPathLength, (oldFlags & WasiRights.LookupSymlinkFollow) != 0);
        var (destination, destinationTrailingSlash) = Resolve(memory, newDirectory, newPathAddress, newPathLength, followFinalLink: false);
        var fileSystem = oldDirectory.FileSystem;
        var stat = fileSystem.GetStat(source) ?? throw new WasiException(WasiErrno.NoEntry);
        if (stat.Type == WasiFileType.Directory)
            return WasiErrno.NotPermitted;

        if (sourceTrailingSlash)
            return WasiErrno.NotDirectory;

        if (fileSystem.GetStat(destination) is not null)
            return WasiErrno.Exists;

        // A trailing slash designates a directory, which a hard link cannot be
        if (destinationTrailingSlash)
            return WasiErrno.NoEntry;

        if (newDirectory.FileSystem != fileSystem)
            return WasiErrno.CrossDevice;

        ThrowIfReadOnly(fileSystem);
        fileSystem.CreateHardLink(destination, source);
        return WasiErrno.Success;
    }

    public WasiErrno PathOpen(Span<byte> bytes, int fd, uint lookupFlags, uint pathAddress, uint pathLength, ushort openFlags, ulong rightsBase, ulong rightsInheriting, ushort fdFlags, uint openedAddress)
    {
        var memory = new WasiMemory(bytes);
        _ = memory.Slice(openedAddress, 4);
        var directory = GetDirectory(fd);
        directory.Require(WasiRights.PathOpen);
        if ((openFlags & ~(WasiRights.OpenCreate | WasiRights.OpenDirectory | WasiRights.OpenExclusive | WasiRights.OpenTruncate)) != 0 || (fdFlags & ~WasiRights.FdFlagsAll) != 0)
            return WasiErrno.InvalidArgument;

        var create = (openFlags & WasiRights.OpenCreate) != 0;
        var exclusive = (openFlags & WasiRights.OpenExclusive) != 0;
        var truncate = (openFlags & WasiRights.OpenTruncate) != 0;
        var mustBeDirectory = (openFlags & WasiRights.OpenDirectory) != 0;
        if (create)
        {
            directory.Require(WasiRights.PathCreateFile);
        }

        if (truncate)
        {
            directory.Require(WasiRights.PathFilestatSetSize);
        }

        var (path, trailingSlash) = Resolve(memory, directory, pathAddress, pathLength, (lookupFlags & WasiRights.LookupSymlinkFollow) != 0);
        var fileSystem = directory.FileSystem;
        var stat = fileSystem.GetStat(path);

        // The rights of the new descriptor are the rights that the directory gives to the files opened from it
        rightsBase &= directory.RightsInheriting;
        rightsInheriting &= directory.RightsInheriting;
        var append = (fdFlags & WasiRights.FdFlagAppend) != 0;
        var write = (rightsBase & (WasiRights.FdWrite | WasiRights.FdAllocate | WasiRights.FdFilestatSetSize)) != 0 || append || truncate;

        WasiFd entry;
        if (stat is { Type: WasiFileType.Directory })
        {
            if (create && exclusive)
                return WasiErrno.Exists;

            if (truncate || (rightsBase & WasiRights.FdWrite) != 0 || append)
                return WasiErrno.IsDirectory;

            entry = new WasiDirectoryFd(fileSystem, path, preopenName: null, rightsBase & WasiRights.Directory, rightsInheriting);
        }
        else if (stat is { Type: WasiFileType.SymbolicLink })
        {
            // The last link is not followed (no LOOKUP_SYMLINK_FOLLOW): a link cannot be opened
            return create && exclusive ? WasiErrno.Exists : WasiErrno.Loop;
        }
        else
        {
            if (stat is not null)
            {
                if (create && exclusive)
                    return WasiErrno.Exists;

                if (mustBeDirectory || trailingSlash)
                    return WasiErrno.NotDirectory;
            }
            else
            {
                if (!create)
                    return WasiErrno.NoEntry;

                if (mustBeDirectory)
                    return WasiErrno.InvalidArgument;

                if (trailingSlash)
                    return WasiErrno.IsDirectory;
            }

            if (fileSystem.IsReadOnly)
            {
                if (stat is null || write)
                    return WasiErrno.ReadOnlyFileSystem;

                rightsBase &= ~(WasiRights.FdWrite | WasiRights.FdAllocate | WasiRights.FdFilestatSetSize | WasiRights.FdFilestatSetTimes | WasiRights.FdDatasync | WasiRights.FdSync);
            }

            var read = (rightsBase & (WasiRights.FdRead | WasiRights.FdReaddir)) != 0 || !write;
            var access = (read, write) switch
            {
                (true, true) => FileAccess.ReadWrite,
                (false, true) => FileAccess.Write,
                _ => FileAccess.Read,
            };
            var mode = (create, truncate) switch
            {
                (true, _) when stat is null => FileMode.CreateNew,
                (_, true) => FileMode.Truncate,
                _ => FileMode.Open,
            };

            var stream = fileSystem.OpenFile(path, mode, access);
            entry = new WasiFileFd(fileSystem, path, stream, rightsBase & WasiRights.RegularFile, rightsInheriting);
        }

        entry.Flags = fdFlags;
        memory.WriteUInt32(openedAddress, (uint)AddFd(entry));
        return WasiErrno.Success;
    }

    public WasiErrno PathReadlink(Span<byte> bytes, int fd, uint pathAddress, uint pathLength, uint bufferAddress, uint bufferLength, uint usedAddress)
    {
        var memory = new WasiMemory(bytes);
        var buffer = memory.Slice(bufferAddress, bufferLength);
        _ = memory.Slice(usedAddress, 4);
        var directory = GetDirectory(fd);
        directory.Require(WasiRights.PathReadlink);
        var (path, _) = Resolve(memory, directory, pathAddress, pathLength, followFinalLink: false);
        var fileSystem = directory.FileSystem;
        var stat = fileSystem.GetStat(path) ?? throw new WasiException(WasiErrno.NoEntry);
        if (stat.Type != WasiFileType.SymbolicLink)
            return WasiErrno.InvalidArgument;

        var target = Encoding.UTF8.GetBytes(fileSystem.ReadSymbolicLink(path));
        var used = CopyTruncated(target, buffer);
        memory.WriteUInt32(usedAddress, (uint)used);
        return WasiErrno.Success;
    }

    public WasiErrno PathRemoveDirectory(Span<byte> bytes, int fd, uint pathAddress, uint pathLength)
    {
        var memory = new WasiMemory(bytes);
        var directory = GetDirectory(fd);
        directory.Require(WasiRights.PathRemoveDirectory);
        var (path, _) = Resolve(memory, directory, pathAddress, pathLength, followFinalLink: false);
        var fileSystem = directory.FileSystem;
        var stat = fileSystem.GetStat(path) ?? throw new WasiException(WasiErrno.NoEntry);
        if (stat.Type != WasiFileType.Directory)
            return WasiErrno.NotDirectory;

        // The directory of the descriptor (".") or the root cannot be removed
        if (path.Length == 0 || string.Equals(path, directory.Path, StringComparison.Ordinal))
            return WasiErrno.Busy;

        if (fileSystem.EnumerateDirectory(path).Any())
            return WasiErrno.NotEmpty;

        ThrowIfReadOnly(fileSystem);
        fileSystem.DeleteDirectory(path);
        return WasiErrno.Success;
    }

    public WasiErrno PathRename(Span<byte> bytes, int fd, uint oldPathAddress, uint oldPathLength, int newFd, uint newPathAddress, uint newPathLength)
    {
        var memory = new WasiMemory(bytes);
        var oldDirectory = GetDirectory(fd);
        var newDirectory = GetDirectory(newFd);
        oldDirectory.Require(WasiRights.PathRenameSource);
        newDirectory.Require(WasiRights.PathRenameTarget);
        var (source, sourceTrailingSlash) = Resolve(memory, oldDirectory, oldPathAddress, oldPathLength, followFinalLink: false);
        var (destination, destinationTrailingSlash) = Resolve(memory, newDirectory, newPathAddress, newPathLength, followFinalLink: false);
        var fileSystem = oldDirectory.FileSystem;
        if (newDirectory.FileSystem != fileSystem)
            return WasiErrno.CrossDevice;

        var sourceStat = fileSystem.GetStat(source) ?? throw new WasiException(WasiErrno.NoEntry);
        var isDirectory = sourceStat.Type == WasiFileType.Directory;
        if (!isDirectory && (sourceTrailingSlash || destinationTrailingSlash))
            return WasiErrno.NotDirectory;

        if (source.Length == 0 || destination.Length == 0 || string.Equals(source, oldDirectory.Path, StringComparison.Ordinal) || string.Equals(destination, newDirectory.Path, StringComparison.Ordinal))
            return WasiErrno.Busy;

        if (string.Equals(source, destination, StringComparison.Ordinal))
            return WasiErrno.Success;

        if (WasiPaths.IsSameOrInside(destination, source))
            return WasiErrno.InvalidArgument;

        ThrowIfReadOnly(fileSystem);
        if (fileSystem.GetStat(destination) is { } destinationStat)
        {
            // As POSIX: a file replaces a file, and a directory replaces an empty directory
            if (isDirectory)
            {
                if (destinationStat.Type != WasiFileType.Directory)
                    return WasiErrno.NotDirectory;

                if (fileSystem.EnumerateDirectory(destination).Any())
                    return WasiErrno.NotEmpty;

                fileSystem.DeleteDirectory(destination);
            }
            else
            {
                if (destinationStat.Type == WasiFileType.Directory)
                    return WasiErrno.IsDirectory;

                fileSystem.Replace(source, destination);
                return WasiErrno.Success;
            }
        }

        fileSystem.Move(source, destination);
        return WasiErrno.Success;
    }

    public WasiErrno PathSymlink(Span<byte> bytes, uint targetAddress, uint targetLength, int fd, uint pathAddress, uint pathLength)
    {
        var memory = new WasiMemory(bytes);
        var target = ReadPath(memory, targetAddress, targetLength);
        var directory = GetDirectory(fd);
        directory.Require(WasiRights.PathSymlink);
        var (path, trailingSlash) = Resolve(memory, directory, pathAddress, pathLength, followFinalLink: false);
        var fileSystem = directory.FileSystem;
        if (fileSystem.GetStat(path) is not null)
            return WasiErrno.Exists;

        if (trailingSlash)
            return WasiErrno.NoEntry;

        // An absolute link could designate a file outside of the file system: the links are relative
        if (target.Length == 0)
            return WasiErrno.NoEntry;

        if (target[0] == '/')
            return WasiErrno.NotPermitted;

        ThrowIfReadOnly(fileSystem);
        fileSystem.CreateSymbolicLink(path, target);
        return WasiErrno.Success;
    }

    public WasiErrno PathUnlinkFile(Span<byte> bytes, int fd, uint pathAddress, uint pathLength)
    {
        var memory = new WasiMemory(bytes);
        var directory = GetDirectory(fd);
        directory.Require(WasiRights.PathUnlinkFile);
        var (path, trailingSlash) = Resolve(memory, directory, pathAddress, pathLength, followFinalLink: false);
        var fileSystem = directory.FileSystem;
        var stat = fileSystem.GetStat(path) ?? throw new WasiException(WasiErrno.NoEntry);
        if (stat.Type == WasiFileType.Directory)
            return WasiErrno.IsDirectory;

        if (trailingSlash)
            return WasiErrno.NotDirectory;

        ThrowIfReadOnly(fileSystem);
        fileSystem.DeleteFile(path);
        return WasiErrno.Success;
    }
}
