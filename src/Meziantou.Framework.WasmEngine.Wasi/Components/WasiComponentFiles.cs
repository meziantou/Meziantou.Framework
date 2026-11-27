using Meziantou.Framework.WasmEngine.Wasi.Internal;

namespace Meziantou.Framework.WasmEngine.Wasi.Components;

/// <summary>A descriptor of WASI 0.2 and 0.3 (<c>wasi:filesystem/types.descriptor</c>): a file or a directory of a file system.</summary>
internal sealed class WasiFileDescriptor(WasiFileSystem fileSystem, string path, bool isDirectory, Stream? stream, bool canRead, bool canWrite, bool canMutate)
{
    public WasiFileSystem FileSystem { get; } = fileSystem;

    public string Path { get; } = path;

    public bool IsDirectory { get; } = isDirectory;

    public Stream? Stream { get; } = stream;

    public bool CanRead { get; } = canRead;

    public bool CanWrite { get; } = canWrite;

    public bool CanMutate { get; } = canMutate;

    /// <summary>The serial number of the file when it was opened, or 0 (a directory, or a file system without serial numbers).</summary>
    public ulong Inode { get; } = isDirectory ? 0 : fileSystem.GetStat(path)?.Inode ?? 0;

    /// <summary>Returns the entry at the path of the descriptor, or null when the component moved or deleted the file: the path then designates another file, or none.</summary>
    public WasiFileStat? GetEntry() => FileSystem.GetStat(Path) is { } stat && (Inode == 0 || stat.Inode == Inode) ? stat : null;

    public bool IsClosed { get; private set; }

    public WasiDirectoryFd AsDirectory()
    {
        if (!IsDirectory)
            throw new WasiException(WasiErrno.NotDirectory);

        return new WasiDirectoryFd(FileSystem, Path, preopenName: null, WasiRights.Directory, WasiRights.DirectoryInheriting);
    }

    public Stream GetStream() => IsDirectory ? throw new WasiException(WasiErrno.IsDirectory) : Stream ?? throw new WasiException(WasiErrno.BadFileDescriptor);

    public void Close()
    {
        IsClosed = true;
        Stream?.Dispose();
    }
}

/// <summary>The operations of the file systems of WASI 0.2 and 0.3: they throw a <see cref="WasiException"/> for the errors.</summary>
internal static class WasiComponentFiles
{
    /// <summary>The name of the case of <c>error-code</c> of an error.</summary>
    public static string GetErrorCode(WasiErrno errno) => errno switch
    {
        WasiErrno.AccessDenied => "access",
        WasiErrno.Again => "would-block",
        WasiErrno.Already => "already",
        WasiErrno.BadFileDescriptor => "bad-descriptor",
        WasiErrno.Busy => "busy",
        WasiErrno.Deadlock => "deadlock",
        WasiErrno.DiskQuota => "quota",
        WasiErrno.Exists => "exist",
        WasiErrno.FileTooLarge => "file-too-large",
        WasiErrno.IllegalByteSequence => "illegal-byte-sequence",
        WasiErrno.InProgress => "in-progress",
        WasiErrno.Interrupted => "interrupted",
        WasiErrno.InvalidArgument => "invalid",
        WasiErrno.IsDirectory => "is-directory",
        WasiErrno.Loop => "loop",
        WasiErrno.TooManyLinks => "too-many-links",
        WasiErrno.MessageTooLarge => "message-size",
        WasiErrno.NameTooLong => "name-too-long",
        WasiErrno.NoDevice => "no-device",
        WasiErrno.NoEntry => "no-entry",
        WasiErrno.NoLock => "no-lock",
        WasiErrno.NoMemory => "insufficient-memory",
        WasiErrno.NoSpace => "insufficient-space",
        WasiErrno.NotDirectory => "not-directory",
        WasiErrno.NotEmpty => "not-empty",
        WasiErrno.NotRecoverable => "not-recoverable",
        WasiErrno.NotSupported or WasiErrno.NotImplemented => "unsupported",
        WasiErrno.NotTerminal => "no-tty",
        WasiErrno.NoDeviceOrAddress => "no-such-device",
        WasiErrno.Overflow => "overflow",
        WasiErrno.NotPermitted or WasiErrno.NotCapable => "not-permitted",
        WasiErrno.BrokenPipe => "pipe",
        WasiErrno.ReadOnlyFileSystem => "read-only",
        WasiErrno.InvalidSeek => "invalid-seek",
        WasiErrno.TextFileBusy => "text-file-busy",
        WasiErrno.CrossDevice => "cross-device",
        _ => "io",
    };

    public static WasiErrno GetErrno(Exception exception) => WasiErrors.TryGetErrno(exception, out var errno) ? errno : WasiErrno.IOError;

    public static bool IsError(Exception exception) => WasiErrors.TryGetErrno(exception, out _);

    private static (string Path, bool TrailingSlash) Resolve(WasiFileDescriptor directory, string path, bool follow)
    {
        if (path.Contains('\0', StringComparison.Ordinal))
            throw new WasiException(WasiErrno.IllegalByteSequence);

        return WasiImplementation.Resolve(directory.AsDirectory(), path, follow);
    }

    private static void RequireMutate(WasiFileDescriptor directory)
    {
        if (!directory.CanMutate)
            throw new WasiException(WasiErrno.NotPermitted);

        if (directory.FileSystem.IsReadOnly)
            throw new WasiException(WasiErrno.ReadOnlyFileSystem);
    }

    public static WasiFileDescriptor OpenAt(WasiFileDescriptor directory, bool follow, string path, bool create, bool mustBeDirectory, bool exclusive, bool truncate, bool read, bool write, bool mutate)
    {
        // A descriptor opened without read and write is readable, and creating a file implies writing it (as wasmtime does)
        if (!read && !write)
        {
            read = true;
        }

        var (resolved, trailingSlash) = Resolve(directory, path, follow);
        var fileSystem = directory.FileSystem;
        var stat = fileSystem.GetStat(resolved);
        if ((create || truncate || write) && !directory.CanMutate && !directory.CanWrite)
            throw new WasiException(WasiErrno.NotPermitted);

        if (stat is { Type: WasiFileType.Directory })
        {
            if (create && exclusive)
                throw new WasiException(WasiErrno.Exists);

            if (truncate || write)
                throw new WasiException(WasiErrno.IsDirectory);

            return new WasiFileDescriptor(fileSystem, resolved, isDirectory: true, stream: null, canRead: true, canWrite: false, canMutate: mutate && directory.CanMutate);
        }

        if (stat is { Type: WasiFileType.SymbolicLink })
            throw new WasiException(create && exclusive ? WasiErrno.Exists : WasiErrno.Loop);

        if (stat is not null)
        {
            if (create && exclusive)
                throw new WasiException(WasiErrno.Exists);

            if (mustBeDirectory || trailingSlash)
                throw new WasiException(WasiErrno.NotDirectory);
        }
        else
        {
            if (!create)
                throw new WasiException(WasiErrno.NoEntry);

            if (mustBeDirectory || trailingSlash)
                throw new WasiException(mustBeDirectory ? WasiErrno.InvalidArgument : WasiErrno.IsDirectory);

            RequireMutate(directory);
            write = true;
        }

        if (fileSystem.IsReadOnly && (stat is null || write || truncate))
            throw new WasiException(WasiErrno.ReadOnlyFileSystem);

        var access = (read || !write, write || truncate) switch
        {
            (true, true) => FileAccess.ReadWrite,
            (false, true) => FileAccess.Write,
            _ => FileAccess.Read,
        };

        var mode = stat is null ? FileMode.CreateNew : truncate ? FileMode.Truncate : FileMode.Open;
        var stream = fileSystem.OpenFile(resolved, mode, access);
        return new WasiFileDescriptor(fileSystem, resolved, isDirectory: false, stream, canRead: read, canWrite: write || truncate, canMutate: false);
    }

    public static WasiFileStat Stat(WasiFileDescriptor descriptor)
    {
        var stat = descriptor.GetEntry();
        if (!descriptor.IsDirectory && descriptor.Stream is { } stream)
        {
            var size = (ulong)stream.Length;
            return stat is { Type: WasiFileType.RegularFile } existing ? existing with { Size = size } : new WasiFileStat(WasiFileType.RegularFile, size, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) { LinkCount = 0, Inode = descriptor.Inode };
        }

        return stat ?? throw new WasiException(WasiErrno.NoEntry);
    }

    public static WasiFileStat StatAt(WasiFileDescriptor directory, bool follow, string path)
    {
        var (resolved, trailingSlash) = Resolve(directory, path, follow);
        var stat = directory.FileSystem.GetStat(resolved) ?? throw new WasiException(WasiErrno.NoEntry);
        if (trailingSlash && stat.Type != WasiFileType.Directory)
            throw new WasiException(WasiErrno.NotDirectory);

        return stat;
    }

    /// <summary>The value of a metadata hash: the device and the serial number of the file.</summary>
    public static (ulong Lower, ulong Upper) MetadataHash(WasiFileSystem fileSystem, WasiFileStat stat) => (stat.Inode, fileSystem.Device);

    public static IReadOnlyList<WasiDirectoryEntry> ReadDirectory(WasiFileDescriptor descriptor)
    {
        var directory = descriptor.AsDirectory();
        if (!descriptor.CanRead)
            throw new WasiException(WasiErrno.BadFileDescriptor);

        return [.. directory.FileSystem.EnumerateDirectory(directory.Path).OrderBy(entry => entry.Name, StringComparer.Ordinal)];
    }

    public static void CreateDirectoryAt(WasiFileDescriptor directory, string path)
    {
        var (resolved, _) = Resolve(directory, path, follow: false);
        if (directory.FileSystem.GetStat(resolved) is not null)
            throw new WasiException(WasiErrno.Exists);

        RequireMutate(directory);
        directory.FileSystem.CreateDirectory(resolved);
    }

    public static void RemoveDirectoryAt(WasiFileDescriptor directory, string path)
    {
        // A path whose last segment is "." is the directory itself: rmdir fails with EINVAL
        var trimmed = path.TrimEnd('/');
        if (trimmed is "." || trimmed.EndsWith("/.", StringComparison.Ordinal))
            throw new WasiException(WasiErrno.InvalidArgument);

        var (resolved, _) = Resolve(directory, path, follow: false);
        var fileSystem = directory.FileSystem;
        var stat = fileSystem.GetStat(resolved) ?? throw new WasiException(WasiErrno.NoEntry);
        if (stat.Type != WasiFileType.Directory)
            throw new WasiException(WasiErrno.NotDirectory);

        if (resolved.Length == 0 || string.Equals(resolved, directory.Path, StringComparison.Ordinal))
            throw new WasiException(WasiErrno.Busy);

        if (fileSystem.EnumerateDirectory(resolved).Any())
            throw new WasiException(WasiErrno.NotEmpty);

        RequireMutate(directory);
        fileSystem.DeleteDirectory(resolved);
    }

    public static void UnlinkFileAt(WasiFileDescriptor directory, string path)
    {
        var (resolved, trailingSlash) = Resolve(directory, path, follow: false);
        var fileSystem = directory.FileSystem;
        var stat = fileSystem.GetStat(resolved) ?? throw new WasiException(WasiErrno.NoEntry);
        if (stat.Type == WasiFileType.Directory)
            throw new WasiException(WasiErrno.IsDirectory);

        if (trailingSlash)
            throw new WasiException(WasiErrno.NotDirectory);

        RequireMutate(directory);
        fileSystem.DeleteFile(resolved);
    }

    public static void RenameAt(WasiFileDescriptor directory, string oldPath, WasiFileDescriptor newDirectory, string newPath)
    {
        var (source, sourceTrailingSlash) = Resolve(directory, oldPath, follow: false);
        var (destination, destinationTrailingSlash) = Resolve(newDirectory, newPath, follow: false);
        var fileSystem = directory.FileSystem;
        if (newDirectory.FileSystem != fileSystem)
            throw new WasiException(WasiErrno.CrossDevice);

        var sourceStat = fileSystem.GetStat(source) ?? throw new WasiException(WasiErrno.NoEntry);
        var isDirectory = sourceStat.Type == WasiFileType.Directory;
        if (!isDirectory && (sourceTrailingSlash || destinationTrailingSlash))
            throw new WasiException(WasiErrno.NotDirectory);

        if (source.Length == 0 || destination.Length == 0)
            throw new WasiException(WasiErrno.Busy);

        if (string.Equals(source, destination, StringComparison.Ordinal))
            return;

        if (WasiPaths.IsSameOrInside(destination, source))
            throw new WasiException(WasiErrno.InvalidArgument);

        RequireMutate(directory);
        RequireMutate(newDirectory);
        if (fileSystem.GetStat(destination) is { } destinationStat)
        {
            if (isDirectory)
            {
                if (destinationStat.Type != WasiFileType.Directory)
                    throw new WasiException(WasiErrno.NotDirectory);

                if (fileSystem.EnumerateDirectory(destination).Any())
                    throw new WasiException(WasiErrno.NotEmpty);

                fileSystem.DeleteDirectory(destination);
            }
            else
            {
                if (destinationStat.Type == WasiFileType.Directory)
                    throw new WasiException(WasiErrno.IsDirectory);

                fileSystem.Replace(source, destination);
                return;
            }
        }

        fileSystem.Move(source, destination);
    }

    public static void SymlinkAt(WasiFileDescriptor directory, string target, string path)
    {
        var (resolved, trailingSlash) = Resolve(directory, path, follow: false);
        if (directory.FileSystem.GetStat(resolved) is not null)
            throw new WasiException(WasiErrno.Exists);

        if (trailingSlash || target.Length == 0)
            throw new WasiException(WasiErrno.NoEntry);

        if (target[0] == '/')
            throw new WasiException(WasiErrno.NotPermitted);

        RequireMutate(directory);
        directory.FileSystem.CreateSymbolicLink(resolved, target);
    }

    public static string ReadlinkAt(WasiFileDescriptor directory, string path)
    {
        var (resolved, _) = Resolve(directory, path, follow: false);
        var stat = directory.FileSystem.GetStat(resolved) ?? throw new WasiException(WasiErrno.NoEntry);
        if (stat.Type != WasiFileType.SymbolicLink)
            throw new WasiException(WasiErrno.InvalidArgument);

        return directory.FileSystem.ReadSymbolicLink(resolved);
    }

    public static void LinkAt(WasiFileDescriptor directory, bool follow, string oldPath, WasiFileDescriptor newDirectory, string newPath)
    {
        var (source, sourceTrailingSlash) = Resolve(directory, oldPath, follow);
        var (destination, destinationTrailingSlash) = Resolve(newDirectory, newPath, follow: false);
        var fileSystem = directory.FileSystem;
        var stat = fileSystem.GetStat(source) ?? throw new WasiException(WasiErrno.NoEntry);
        if (stat.Type == WasiFileType.Directory)
            throw new WasiException(WasiErrno.NotPermitted);

        if (sourceTrailingSlash)
            throw new WasiException(WasiErrno.NotDirectory);

        if (fileSystem.GetStat(destination) is not null)
            throw new WasiException(WasiErrno.Exists);

        if (destinationTrailingSlash)
            throw new WasiException(WasiErrno.NoEntry);

        if (newDirectory.FileSystem != fileSystem)
            throw new WasiException(WasiErrno.CrossDevice);

        RequireMutate(newDirectory);
        fileSystem.CreateHardLink(destination, source);
    }

    public static void SetTimes(WasiFileDescriptor descriptor, DateTimeOffset? accessTime, DateTimeOffset? writeTime)
    {
        // As wasmtime: a file must be writable, and a directory mutable
        if (descriptor.IsDirectory ? !descriptor.CanMutate : !descriptor.CanWrite)
            throw new WasiException(WasiErrno.NotPermitted);

        if (descriptor.FileSystem.IsReadOnly)
            throw new WasiException(WasiErrno.ReadOnlyFileSystem);

        // The times are those of the entry of the file: a file that was moved or deleted has no entry to change
        if (descriptor.GetEntry() is null)
            throw new WasiException(WasiErrno.NoEntry);

        descriptor.FileSystem.SetTimes(descriptor.Path, accessTime, writeTime);
    }

    public static void SetTimesAt(WasiFileDescriptor directory, bool follow, string path, DateTimeOffset? accessTime, DateTimeOffset? writeTime)
    {
        var (resolved, trailingSlash) = Resolve(directory, path, follow);
        var stat = directory.FileSystem.GetStat(resolved) ?? throw new WasiException(WasiErrno.NoEntry);
        if (trailingSlash && stat.Type != WasiFileType.Directory)
            throw new WasiException(WasiErrno.NotDirectory);

        RequireMutate(directory);
        directory.FileSystem.SetTimes(resolved, accessTime, writeTime);
    }

    public static (byte[] Bytes, bool EndOfFile) Read(WasiFileDescriptor descriptor, ulong length, ulong offset)
    {
        var stream = descriptor.GetStream();
        if (!descriptor.CanRead || !stream.CanRead)
            throw new WasiException(WasiErrno.BadFileDescriptor);

        if (offset > long.MaxValue)
            throw new WasiException(WasiErrno.InvalidArgument);

        var size = stream.Length;
        if ((long)offset >= size)
            return ([], true);

        var count = (int)Math.Min(Math.Min(length, (ulong)(size - (long)offset)), 1024 * 1024);
        var buffer = new byte[count];
        stream.Position = (long)offset;
        var read = 0;
        while (read < count)
        {
            var chunk = stream.Read(buffer.AsSpan(read));
            if (chunk == 0)
                break;

            read += chunk;
        }

        return (read == count ? buffer : buffer[..read], (long)offset + read >= stream.Length);
    }

    public static ulong Write(WasiFileDescriptor descriptor, byte[] bytes, ulong offset)
    {
        var stream = descriptor.GetStream();
        if (!descriptor.CanWrite || !stream.CanWrite)
            throw new WasiException(WasiErrno.BadFileDescriptor);

        if (offset > long.MaxValue)
            throw new WasiException(WasiErrno.InvalidArgument);

        stream.Position = (long)offset;
        stream.Write(bytes);
        return (ulong)bytes.Length;
    }

    public static void SetSize(WasiFileDescriptor descriptor, ulong size)
    {
        var stream = descriptor.GetStream();
        if (!descriptor.CanWrite || !stream.CanWrite)
            throw new WasiException(WasiErrno.BadFileDescriptor);

        if (size > long.MaxValue)
            throw new WasiException(WasiErrno.FileTooLarge);

        stream.SetLength((long)size);
    }

    public static void Sync(WasiFileDescriptor descriptor)
    {
        if (descriptor.Stream is FileStream { CanWrite: true } file)
        {
            file.Flush(flushToDisk: true);
        }
        else
        {
            descriptor.Stream?.Flush();
        }
    }

    public static bool IsSameObject(WasiFileDescriptor left, WasiFileDescriptor right)
    {
        if (left.FileSystem != right.FileSystem)
            return false;

        if (string.Equals(left.Path, right.Path, StringComparison.Ordinal))
            return true;

        // The hard links of a file are the same object: the serial numbers of their entries are the same
        return left.FileSystem.GetStat(left.Path) is { Inode: not 0 and var inode } && right.FileSystem.GetStat(right.Path) is { } rightStat && rightStat.Inode == inode;
    }
}

/// <summary>A stream of the host that reads a file from an offset, without changing the position of the file of the descriptor.</summary>
internal sealed class WasiFileRangeStream(Stream file, long offset, bool append) : Stream
{
    private long _position = offset;

    public override bool CanRead => file.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => file.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => file.Flush();

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        file.Position = _position;
        var read = file.Read(buffer);
        _position += read;
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (append)
        {
            _position = file.Length;
        }

        file.Position = _position;
        file.Write(buffer);
        _position += buffer.Length;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
