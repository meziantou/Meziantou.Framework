namespace Meziantou.Framework.WasmEngine.Wasi.Internal;

internal sealed partial class WasiImplementation
{
    private const int IovecSize = 8;
    private const int FdstatSize = 24;
    private const int FilestatSize = 64;
    private const int DirentSize = 24;
    private const int PrestatSize = 8;

    private const byte WhenceSet = 0;
    private const byte WhenceCurrent = 1;
    private const byte WhenceEnd = 2;

    private WasiFileFd GetFile(int fd) => GetFd(fd) as WasiFileFd ?? throw new WasiException(WasiErrno.BadFileDescriptor);

    public WasiErrno FdAdvise(int fd, ulong offset, ulong length, byte advice)
    {
        _ = offset;
        _ = length;
        var file = GetFile(fd);
        file.Require(WasiRights.FdAdvise);
        return advice > 5 ? WasiErrno.InvalidArgument : WasiErrno.Success;
    }

    public WasiErrno FdAllocate(int fd, ulong offset, ulong length)
    {
        var file = GetFile(fd);
        file.Require(WasiRights.FdAllocate);
        var end = offset + length;
        if (end < offset || end > long.MaxValue)
            return WasiErrno.FileTooLarge;

        if (!file.Stream.CanWrite)
            return WasiErrno.BadFileDescriptor;

        if ((long)end > file.Stream.Length)
        {
            file.Stream.SetLength((long)end);
        }

        return WasiErrno.Success;
    }

    public WasiErrno FdClose(int fd)
    {
        var entry = GetFd(fd);
        _fds[fd] = null;
        entry.Close();
        return WasiErrno.Success;
    }

    public WasiErrno FdDatasync(int fd) => Sync(fd, WasiRights.FdDatasync);

    public WasiErrno FdSync(int fd) => Sync(fd, WasiRights.FdSync);

    private WasiErrno Sync(int fd, ulong right)
    {
        var entry = GetFd(fd);
        entry.Require(right);
        if (entry is WasiFileFd file)
        {
            if (file.Stream is FileStream stream)
            {
                if (stream.CanWrite)
                {
                    stream.Flush(flushToDisk: true);
                }
            }
            else
            {
                file.Stream.Flush();
            }
        }
        else if (entry is WasiStreamFd { Stream: { CanWrite: true } output })
        {
            output.Flush();
        }

        return WasiErrno.Success;
    }

    public WasiErrno FdFdstatGet(Span<byte> bytes, int fd, uint address)
    {
        var memory = new WasiMemory(bytes);
        var entry = GetFd(fd);
        var stat = memory.Slice(address, FdstatSize);
        stat.Clear();
        memory.WriteByte(address, (byte)entry.FileType);
        memory.WriteUInt16(address + 2, entry.Flags);
        memory.WriteUInt64(address + 8, entry.RightsBase);
        memory.WriteUInt64(address + 16, entry.RightsInheriting);
        return WasiErrno.Success;
    }

    public WasiErrno FdFdstatSetFlags(int fd, ushort flags)
    {
        var entry = GetFd(fd);
        entry.Require(WasiRights.FdFdstatSetFlags);
        if ((flags & ~WasiRights.FdFlagsAll) != 0)
            return WasiErrno.InvalidArgument;

        // The synchronized writes cannot be changed after the file is open
        if ((flags & (WasiRights.FdFlagDsync | WasiRights.FdFlagRsync | WasiRights.FdFlagSync)) != (entry.Flags & (WasiRights.FdFlagDsync | WasiRights.FdFlagRsync | WasiRights.FdFlagSync)))
            return WasiErrno.NotSupported;

        entry.Flags = flags;
        return WasiErrno.Success;
    }

    public WasiErrno FdFdstatSetRights(int fd, ulong rightsBase, ulong rightsInheriting)
    {
        var entry = GetFd(fd);

        // The rights can only be removed
        if ((rightsBase & ~entry.RightsBase) != 0 || (rightsInheriting & ~entry.RightsInheriting) != 0)
            return WasiErrno.NotCapable;

        entry.RightsBase = rightsBase;
        entry.RightsInheriting = rightsInheriting;
        return WasiErrno.Success;
    }

    public WasiErrno FdFilestatGet(Span<byte> bytes, int fd, uint address)
    {
        var memory = new WasiMemory(bytes);
        _ = memory.Slice(address, FilestatSize);
        var entry = GetFd(fd);
        entry.Require(WasiRights.FdFilestatGet);
        switch (entry)
        {
            case WasiFileFd file:
                var stat = file.GetEntry();
                var size = (ulong)file.Stream.Length;
                var fileStat = stat is { Type: WasiFileType.RegularFile } existing
                    ? existing with { Size = size }
                    : new WasiFileStat(WasiFileType.RegularFile, size, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) { LinkCount = 0, Inode = file.Inode };
                WriteFilestat(memory, address, file.FileSystem.Device, fileStat);
                break;

            case WasiDirectoryFd directory:
                WriteFilestat(memory, address, directory.FileSystem.Device, directory.FileSystem.GetStat(directory.Path) ?? throw new WasiException(WasiErrno.NoEntry));
                break;

            default:
                WriteFilestat(memory, address, 0, new WasiFileStat(WasiFileType.CharacterDevice, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
                break;
        }

        return WasiErrno.Success;
    }

    private static void WriteFilestat(WasiMemory memory, ulong address, ulong device, WasiFileStat stat)
    {
        memory.Slice(address, FilestatSize).Clear();
        memory.WriteUInt64(address, device);
        memory.WriteUInt64(address + 8, stat.Inode);
        memory.WriteByte(address + 16, (byte)stat.Type);
        memory.WriteUInt64(address + 24, stat.LinkCount);
        memory.WriteUInt64(address + 32, stat.Size);
        memory.WriteUInt64(address + 40, ToTimestamp(stat.LastAccessTime));
        memory.WriteUInt64(address + 48, ToTimestamp(stat.LastWriteTime));
        memory.WriteUInt64(address + 56, ToTimestamp(stat.ChangeTime));
    }

    public WasiErrno FdFilestatSetSize(int fd, ulong size)
    {
        var file = GetFile(fd);
        file.Require(WasiRights.FdFilestatSetSize);
        if (size > long.MaxValue)
            return WasiErrno.FileTooLarge;

        if (!file.Stream.CanWrite)
            return WasiErrno.BadFileDescriptor;

        file.Stream.SetLength((long)size);
        return WasiErrno.Success;
    }

    public WasiErrno FdFilestatSetTimes(int fd, ulong accessTime, ulong writeTime, ushort flags)
    {
        var entry = GetFd(fd);
        entry.Require(WasiRights.FdFilestatSetTimes);
        var (access, write) = GetTimes(accessTime, writeTime, flags);
        switch (entry)
        {
            case WasiFileFd file:
                ThrowIfReadOnly(file.FileSystem);

                // The times are those of the entry of the file: a file that was moved or deleted has no entry to change
                if (file.GetEntry() is null)
                    return WasiErrno.NoEntry;

                file.FileSystem.SetTimes(file.Path, access, write);
                break;

            case WasiDirectoryFd directory:
                ThrowIfReadOnly(directory.FileSystem);
                directory.FileSystem.SetTimes(directory.Path, access, write);
                break;

            default:
                return WasiErrno.BadFileDescriptor;
        }

        return WasiErrno.Success;
    }

    private (DateTimeOffset? Access, DateTimeOffset? Write) GetTimes(ulong accessTime, ulong writeTime, ushort flags)
    {
        if ((flags & ~(WasiRights.SetAccessTime | WasiRights.SetAccessTimeNow | WasiRights.SetWriteTime | WasiRights.SetWriteTimeNow)) != 0)
            throw new WasiException(WasiErrno.InvalidArgument);

        if ((flags & (WasiRights.SetAccessTime | WasiRights.SetAccessTimeNow)) == (WasiRights.SetAccessTime | WasiRights.SetAccessTimeNow) ||
            (flags & (WasiRights.SetWriteTime | WasiRights.SetWriteTimeNow)) == (WasiRights.SetWriteTime | WasiRights.SetWriteTimeNow))
        {
            throw new WasiException(WasiErrno.InvalidArgument);
        }

        var now = _timeProvider.GetUtcNow();
        DateTimeOffset? access = (flags & WasiRights.SetAccessTimeNow) != 0 ? now : (flags & WasiRights.SetAccessTime) != 0 ? FromTimestamp(accessTime) : null;
        DateTimeOffset? write = (flags & WasiRights.SetWriteTimeNow) != 0 ? now : (flags & WasiRights.SetWriteTime) != 0 ? FromTimestamp(writeTime) : null;
        return (access, write);
    }

    private static void ThrowIfReadOnly(WasiFileSystem fileSystem)
    {
        if (fileSystem.IsReadOnly)
            throw new WasiException(WasiErrno.ReadOnlyFileSystem);
    }

    public WasiErrno FdPrestatGet(Span<byte> bytes, int fd, uint address)
    {
        var memory = new WasiMemory(bytes);
        if (GetFd(fd) is not WasiDirectoryFd { PreopenName: { } name })
            return WasiErrno.BadFileDescriptor;

        memory.Slice(address, PrestatSize).Clear();
        memory.WriteByte(address, 0); // dir
        memory.WriteUInt32(address + 4, (uint)System.Text.Encoding.UTF8.GetByteCount(name));
        return WasiErrno.Success;
    }

    public WasiErrno FdPrestatDirName(Span<byte> bytes, int fd, uint address, uint length)
    {
        var memory = new WasiMemory(bytes);
        if (GetFd(fd) is not WasiDirectoryFd { PreopenName: { } name })
            return WasiErrno.BadFileDescriptor;

        var encoded = System.Text.Encoding.UTF8.GetBytes(name);
        if (length < encoded.Length)
            return WasiErrno.NameTooLong;

        encoded.CopyTo(memory.Slice(address, (uint)encoded.Length));
        return WasiErrno.Success;
    }

    public WasiErrno FdRead(Span<byte> bytes, int fd, uint iovecs, uint iovecCount, uint readAddress)
    {
        var memory = new WasiMemory(bytes);
        _ = memory.Slice(readAddress, 4);
        var entry = GetFd(fd);

        // A descriptor that is not open for reading fails as on POSIX (EBADF), before its rights are checked
        var stream = GetReadableStream(entry);
        entry.Require(WasiRights.FdRead);
        var total = stream is null ? 0 : ReadVectors(memory, iovecs, iovecCount, stream, offset: null);
        memory.WriteUInt32(readAddress, total);
        return WasiErrno.Success;
    }

    public WasiErrno FdPread(Span<byte> bytes, int fd, uint iovecs, uint iovecCount, ulong offset, uint readAddress)
    {
        var memory = new WasiMemory(bytes);
        _ = memory.Slice(readAddress, 4);
        var entry = GetFd(fd);
        var stream = GetReadableStream(entry);
        if (entry is not WasiFileFd || stream is null || !stream.CanSeek)
            return WasiErrno.InvalidSeek;

        entry.Require(WasiRights.FdRead | WasiRights.FdSeek);

        if (offset > long.MaxValue)
            return WasiErrno.InvalidArgument;

        var total = ReadVectors(memory, iovecs, iovecCount, stream, (long)offset);
        memory.WriteUInt32(readAddress, total);
        return WasiErrno.Success;
    }

    private static Stream? GetReadableStream(WasiFd entry) => entry switch
    {
        WasiFileFd file => file.Stream.CanRead ? file.Stream : throw new WasiException(WasiErrno.BadFileDescriptor),
        WasiStreamFd { IsInput: true } input => input.Stream,
        WasiStreamFd => throw new WasiException(WasiErrno.BadFileDescriptor),
        _ => throw new WasiException(WasiErrno.IsDirectory),
    };

    /// <summary>Reads the stream into the buffers of the vectors, at the current position or at <paramref name="offset"/> (the position does not change).</summary>
    private static uint ReadVectors(WasiMemory memory, uint iovecs, uint iovecCount, Stream stream, long? offset)
    {
        var vectors = memory.Slice(iovecs, (ulong)iovecCount * IovecSize);
        var savedPosition = offset is null ? 0 : stream.Position;
        if (offset is { } position)
        {
            stream.Position = position;
        }

        try
        {
            var total = 0u;
            for (var i = 0; i < iovecCount; i++)
            {
                var vector = new WasiMemory(vectors.Slice(i * IovecSize, IovecSize));
                var buffer = memory.Slice(vector.ReadUInt32(0), vector.ReadUInt32(4));
                if (buffer.Length > uint.MaxValue - total)
                {
                    buffer = buffer[..(int)(uint.MaxValue - total)];
                }

                var read = ReadFully(stream, buffer);
                total += (uint)read;
                if (read < buffer.Length)
                    break;
            }

            return total;
        }
        finally
        {
            if (offset is not null)
            {
                stream.Position = savedPosition;
            }
        }
    }

    // A stream can return fewer bytes than available: a short read of a file means the end of the file for the module. A
    // stream that is not seekable (the standard input) returns what it has, without waiting for more
    private static int ReadFully(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
                break;

            total += read;
            if (!stream.CanSeek)
                break;
        }

        return total;
    }

    public WasiErrno FdWrite(Span<byte> bytes, int fd, uint iovecs, uint iovecCount, uint writtenAddress)
    {
        var memory = new WasiMemory(bytes);
        _ = memory.Slice(writtenAddress, 4);
        var entry = GetFd(fd);

        // A descriptor that is not open for writing fails as on POSIX (EBADF), before its rights are checked
        var stream = entry switch
        {
            WasiFileFd file => file.Stream.CanWrite ? file.Stream : throw new WasiException(WasiErrno.BadFileDescriptor),
            WasiStreamFd { IsInput: false } output => output.Stream,
            WasiStreamFd => throw new WasiException(WasiErrno.BadFileDescriptor),
            _ => throw new WasiException(WasiErrno.IsDirectory),
        };

        entry.Require(WasiRights.FdWrite);
        if (entry is WasiFileFd { IsAppend: true })
        {
            stream!.Seek(0, SeekOrigin.End);
        }

        var total = WriteVectors(memory, iovecs, iovecCount, stream);
        if (entry is WasiStreamFd)
        {
            stream?.Flush();
        }

        memory.WriteUInt32(writtenAddress, total);
        return WasiErrno.Success;
    }

    public WasiErrno FdPwrite(Span<byte> bytes, int fd, uint iovecs, uint iovecCount, ulong offset, uint writtenAddress)
    {
        var memory = new WasiMemory(bytes);
        _ = memory.Slice(writtenAddress, 4);
        var entry = GetFd(fd);
        entry.Require(WasiRights.FdWrite | WasiRights.FdSeek);
        if (entry is not WasiFileFd file)
            return entry is WasiDirectoryFd ? WasiErrno.IsDirectory : WasiErrno.InvalidSeek;

        if (!file.Stream.CanWrite)
            return WasiErrno.BadFileDescriptor;

        if (offset > long.MaxValue)
            return WasiErrno.InvalidArgument;

        // As POSIX: the data is written at the offset, even when the file is opened in append mode, and the position does not change
        var savedPosition = file.Stream.Position;
        try
        {
            file.Stream.Position = (long)offset;
            var total = WriteVectors(memory, iovecs, iovecCount, file.Stream);
            memory.WriteUInt32(writtenAddress, total);
        }
        finally
        {
            file.Stream.Position = savedPosition;
        }

        return WasiErrno.Success;
    }

    private static uint WriteVectors(WasiMemory memory, uint iovecs, uint iovecCount, Stream? stream)
    {
        var vectors = memory.Slice(iovecs, (ulong)iovecCount * IovecSize);
        var total = 0u;
        for (var i = 0; i < iovecCount; i++)
        {
            var vector = new WasiMemory(vectors.Slice(i * IovecSize, IovecSize));
            var buffer = memory.Slice(vector.ReadUInt32(0), vector.ReadUInt32(4));
            if (buffer.Length > uint.MaxValue - total)
                throw new WasiException(WasiErrno.InvalidArgument);

            stream?.Write(buffer);
            total += (uint)buffer.Length;
        }

        return total;
    }

    public WasiErrno FdReaddir(Span<byte> bytes, int fd, uint bufferAddress, uint bufferLength, ulong cookie, uint usedAddress)
    {
        var memory = new WasiMemory(bytes);
        var buffer = memory.Slice(bufferAddress, bufferLength);
        _ = memory.Slice(usedAddress, 4);
        var entry = GetFd(fd);
        if (entry is not WasiDirectoryFd directory)
            return entry is WasiFileFd ? WasiErrno.NotDirectory : WasiErrno.BadFileDescriptor;

        directory.Require(WasiRights.FdReaddir);
        var fileSystem = directory.FileSystem;
        var self = fileSystem.GetStat(directory.Path) ?? throw new WasiException(WasiErrno.NoEntry);
        var parentInode = directory.Path.Length == 0 ? self.Inode : fileSystem.GetStat(WasiPaths.GetParent(directory.Path))?.Inode ?? 0;
        var entries = new List<WasiDirectoryEntry>
        {
            new(".", WasiFileType.Directory, self.Inode),
            new("..", WasiFileType.Directory, parentInode),
        };
        entries.AddRange(fileSystem.EnumerateDirectory(directory.Path).OrderBy(child => child.Name, StringComparer.Ordinal));

        // The entries are written one after the other, the last one truncated when the buffer is full: a buffer that is
        // not full is the end of the directory
        var used = 0;
        Span<byte> header = stackalloc byte[DirentSize];
        for (var index = cookie; index < (ulong)entries.Count && used < buffer.Length; index++)
        {
            var child = entries[(int)index];
            var name = System.Text.Encoding.UTF8.GetBytes(child.Name);
            var headerMemory = new WasiMemory(header);
            header.Clear();
            headerMemory.WriteUInt64(0, index + 1);
            headerMemory.WriteUInt64(8, child.Inode);
            headerMemory.WriteUInt32(16, (uint)name.Length);
            headerMemory.WriteByte(20, (byte)child.Type);
            used += CopyTruncated(header, buffer[used..]);
            used += CopyTruncated(name, buffer[used..]);
        }

        memory.WriteUInt32(usedAddress, (uint)used);
        return WasiErrno.Success;
    }

    private static int CopyTruncated(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var count = Math.Min(source.Length, destination.Length);
        source[..count].CopyTo(destination);
        return count;
    }

    public WasiErrno FdRenumber(int fd, int to)
    {
        var entry = GetFd(fd);
        var target = GetFd(to);
        if (fd == to)
            return WasiErrno.Success;

        _fds[to] = entry;
        _fds[fd] = null;
        target.Close();
        return WasiErrno.Success;
    }

    public WasiErrno FdSeek(Span<byte> bytes, int fd, long offset, byte whence, uint positionAddress)
    {
        var memory = new WasiMemory(bytes);
        _ = memory.Slice(positionAddress, 8);
        var entry = GetFd(fd);

        // Moving to the current position is fd_tell, which needs fewer rights
        entry.Require(offset == 0 && whence == WhenceCurrent ? WasiRights.FdTell : WasiRights.FdSeek);
        if (entry is not WasiFileFd file)
            return entry is WasiDirectoryFd ? WasiErrno.BadFileDescriptor : WasiErrno.InvalidSeek;

        var stream = file.Stream;
        var origin = whence switch
        {
            WhenceSet => 0,
            WhenceCurrent => stream.Position,
            WhenceEnd => stream.Length,
            _ => throw new WasiException(WasiErrno.InvalidArgument),
        };

        var position = origin + offset;
        if ((offset > 0 && position < origin) || position < 0)
            return WasiErrno.InvalidArgument;

        stream.Position = position;
        memory.WriteUInt64(positionAddress, (ulong)position);
        return WasiErrno.Success;
    }

    public WasiErrno FdTell(Span<byte> bytes, int fd, uint positionAddress)
    {
        var memory = new WasiMemory(bytes);
        _ = memory.Slice(positionAddress, 8);
        var entry = GetFd(fd);
        entry.Require(WasiRights.FdTell);
        if (entry is not WasiFileFd file)
            return entry is WasiDirectoryFd ? WasiErrno.BadFileDescriptor : WasiErrno.InvalidSeek;

        memory.WriteUInt64(positionAddress, (ulong)file.Stream.Position);
        return WasiErrno.Success;
    }
}
