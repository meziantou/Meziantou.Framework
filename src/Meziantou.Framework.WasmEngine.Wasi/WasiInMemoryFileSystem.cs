using Meziantou.Framework.WasmEngine.Wasi.Internal;

namespace Meziantou.Framework.WasmEngine.Wasi;

/// <summary>
/// A file system in memory: files, directories, symbolic links and hard links that the module reads and writes, and that
/// the host prepares (<see cref="AddFile"/>, <see cref="AddDirectory"/>, <see cref="AddSymbolicLink"/>) and reads
/// (<see cref="ReadAllBytes"/>). It does not access the disk, so it works on every platform, including the browser.
/// </summary>
/// <remarks>The paths of the methods of the host are relative to the root, with <c>/</c> or <c>\</c> separators. The file system is not thread-safe.</remarks>
public sealed class WasiInMemoryFileSystem : WasiFileSystem
{
    private readonly DirectoryNode _root;
    private readonly bool _isReadOnly;
    private ulong _lastInode;

    /// <summary>Initializes a new instance of the <see cref="WasiInMemoryFileSystem"/> class, which is empty.</summary>
    /// <param name="isReadOnly">Whether the module can only read the file system. The host can still change it.</param>
    public WasiInMemoryFileSystem(bool isReadOnly = false)
    {
        _isReadOnly = isReadOnly;
        _root = new DirectoryNode(NextInode(), TimeProvider.System.GetUtcNow());
    }

    /// <inheritdoc/>
    public override bool IsReadOnly => _isReadOnly;

    /// <summary>Gets the clock of the times of the files. The default is <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Gets the maximum size of a file in bytes: a write or a truncation beyond fails with <see cref="WasiErrno.FileTooLarge"/>.
    /// The default and the upper limit is the maximum length of a .NET array.
    /// </summary>
    public long MaxFileSize { get; init; } = Array.MaxLength;

    private DateTimeOffset Now => TimeProvider.GetUtcNow();

    // The bytes of a file are an array: a file cannot be larger than the maximum length of an array
    private long EffectiveMaxFileSize => Math.Min(MaxFileSize, Array.MaxLength);

    private ulong NextInode() => ++_lastInode;

    /// <summary>Adds a file, or replaces the content of a file, and creates its parent directories.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="content">The content of the file.</param>
    /// <exception cref="ArgumentException">The path is not valid, or an entry of the path is not a directory.</exception>
    public void AddFile(string path, ReadOnlySpan<byte> content)
    {
        var normalized = WasiPaths.Normalize(path, nameof(path));
        if (normalized.Length == 0)
            throw new ArgumentException("The path of a file cannot be the root", nameof(path));

        var parent = EnsureDirectory(WasiPaths.GetParent(normalized), nameof(path));
        var name = WasiPaths.GetName(normalized);
        if (parent.Children.TryGetValue(name, out var existing))
        {
            if (existing is not FileNode file)
                throw new ArgumentException("The path '" + path + "' is not a file", nameof(path));

            file.Data.SetContent(content);
            file.SetWriteTime(Now);
            return;
        }

        var node = new FileNode(NextInode(), Now);
        node.Data.SetContent(content);
        parent.Children.Add(name, node);
    }

    /// <summary>Adds a directory and its parents.</summary>
    /// <param name="path">The path of the directory.</param>
    /// <exception cref="ArgumentException">The path is not valid, or an entry of the path is not a directory.</exception>
    public void AddDirectory(string path) => EnsureDirectory(WasiPaths.Normalize(path, nameof(path)), nameof(path));

    /// <summary>Adds a symbolic link, and creates its parent directories.</summary>
    /// <param name="path">The path of the link.</param>
    /// <param name="target">The target of the link, relative to the directory of the link, with <c>/</c> separators.</param>
    /// <exception cref="ArgumentException">The path is not valid or exists, or an entry of the path is not a directory.</exception>
    public void AddSymbolicLink(string path, string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var normalized = WasiPaths.Normalize(path, nameof(path));
        if (normalized.Length == 0)
            throw new ArgumentException("The path of a link cannot be the root", nameof(path));

        var parent = EnsureDirectory(WasiPaths.GetParent(normalized), nameof(path));
        if (!parent.Children.TryAdd(WasiPaths.GetName(normalized), new SymbolicLinkNode(NextInode(), Now, target)))
            throw new ArgumentException("The path '" + path + "' exists", nameof(path));
    }

    /// <summary>Returns the content of a file.</summary>
    /// <param name="path">The path of the file. The symbolic links are not followed.</param>
    /// <exception cref="FileNotFoundException">The file does not exist, or is not a regular file.</exception>
    public byte[] ReadAllBytes(string path)
    {
        var normalized = WasiPaths.Normalize(path, nameof(path));
        if (Find(normalized) is FileNode file)
            return file.Data.ToArray();

        throw new FileNotFoundException("The file '" + path + "' does not exist", path);
    }

    /// <summary>Returns whether a file, a directory or a symbolic link exists.</summary>
    /// <param name="path">The path of the entry. The symbolic links are not followed.</param>
    public bool Exists(string path) => Find(WasiPaths.Normalize(path, nameof(path))) is not null;

    private DirectoryNode EnsureDirectory(string path, string parameterName)
    {
        var current = _root;
        if (path.Length == 0)
            return current;

        foreach (var segment in path.Split('/'))
        {
            if (!current.Children.TryGetValue(segment, out var child))
            {
                child = new DirectoryNode(NextInode(), Now);
                current.Children.Add(segment, child);
            }

            current = child as DirectoryNode ?? throw new ArgumentException("The path '" + path + "' has an entry that is not a directory", parameterName);
        }

        return current;
    }

    private Node? Find(string path)
    {
        Node current = _root;
        if (path.Length == 0)
            return current;

        foreach (var segment in path.Split('/'))
        {
            if (current is not DirectoryNode directory || !directory.Children.TryGetValue(segment, out var child))
                return null;

            current = child;
        }

        return current;
    }

    private Node Get(string path) => Find(path) ?? throw new WasiException(WasiErrno.NoEntry);

    private DirectoryNode GetDirectory(string path) => Get(path) as DirectoryNode ?? throw new WasiException(WasiErrno.NotDirectory);

    private (DirectoryNode Parent, string Name) GetParent(string path)
    {
        if (path.Length == 0)
            throw new WasiException(WasiErrno.Busy);

        return (GetDirectory(WasiPaths.GetParent(path)), WasiPaths.GetName(path));
    }

    private void ThrowIfReadOnly()
    {
        if (_isReadOnly)
            throw new WasiException(WasiErrno.ReadOnlyFileSystem);
    }

    /// <inheritdoc/>
    public override WasiFileStat? GetStat(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Find(path) switch
        {
            FileNode file => file.GetStat(WasiFileType.RegularFile, (ulong)file.Data.Length),
            DirectoryNode directory => directory.GetStat(WasiFileType.Directory, 0),
            SymbolicLinkNode link => link.GetStat(WasiFileType.SymbolicLink, (ulong)System.Text.Encoding.UTF8.GetByteCount(link.Target)),
            _ => null,
        };
    }

    /// <inheritdoc/>
    public override string ReadSymbolicLink(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Get(path) is SymbolicLinkNode link ? link.Target : throw new WasiException(WasiErrno.InvalidArgument);
    }

    /// <inheritdoc/>
    public override Stream OpenFile(string path, FileMode mode, FileAccess access)
    {
        ArgumentNullException.ThrowIfNull(path);
        var (parent, name) = GetParent(path);
        var writes = access.HasFlag(FileAccess.Write) || mode is FileMode.Create or FileMode.CreateNew or FileMode.Truncate or FileMode.Append;
        if (writes)
        {
            ThrowIfReadOnly();
        }

        parent.Children.TryGetValue(name, out var existing);
        FileNode file;
        switch (existing)
        {
            case null when mode is FileMode.Open or FileMode.Truncate:
                throw new WasiException(WasiErrno.NoEntry);

            case null:
                ThrowIfReadOnly();
                file = new FileNode(NextInode(), Now);
                parent.Children.Add(name, file);
                parent.SetWriteTime(Now);
                break;

            case FileNode when mode is FileMode.CreateNew:
                throw new WasiException(WasiErrno.Exists);

            case FileNode existingFile:
                file = existingFile;
                if (mode is FileMode.Create or FileMode.Truncate)
                {
                    file.Data.SetLength(0);
                    file.SetWriteTime(Now);
                }

                break;

            case DirectoryNode:
                throw new WasiException(WasiErrno.IsDirectory);

            default:
                throw new WasiException(WasiErrno.Loop);
        }

        var stream = new InMemoryFileStream(this, file, access.HasFlag(FileAccess.Read), access.HasFlag(FileAccess.Write));
        if (mode is FileMode.Append)
        {
            stream.Seek(0, SeekOrigin.End);
        }

        return stream;
    }

    /// <inheritdoc/>
    public override IEnumerable<WasiDirectoryEntry> EnumerateDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var directory = GetDirectory(path);
        var entries = new List<WasiDirectoryEntry>(directory.Children.Count);
        foreach (var (name, node) in directory.Children)
        {
            entries.Add(new WasiDirectoryEntry(name, node.Type, node.Inode));
        }

        return entries;
    }

    /// <inheritdoc/>
    public override void CreateDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfReadOnly();
        var (parent, name) = GetParent(path);
        if (!parent.Children.TryAdd(name, new DirectoryNode(NextInode(), Now)))
            throw new WasiException(WasiErrno.Exists);

        parent.SetWriteTime(Now);
    }

    /// <inheritdoc/>
    public override void DeleteDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfReadOnly();
        var (parent, name) = GetParent(path);
        if (!parent.Children.TryGetValue(name, out var node))
            throw new WasiException(WasiErrno.NoEntry);

        if (node is not DirectoryNode directory)
            throw new WasiException(WasiErrno.NotDirectory);

        if (directory.Children.Count > 0)
            throw new WasiException(WasiErrno.NotEmpty);

        parent.Children.Remove(name);
        parent.SetWriteTime(Now);
    }

    /// <inheritdoc/>
    public override void DeleteFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfReadOnly();
        var (parent, name) = GetParent(path);
        if (!parent.Children.TryGetValue(name, out var node))
            throw new WasiException(WasiErrno.NoEntry);

        if (node is DirectoryNode)
            throw new WasiException(WasiErrno.IsDirectory);

        parent.Children.Remove(name);
        node.LinkCount--;
        node.ChangeTime = Now;
        parent.SetWriteTime(Now);
    }

    /// <inheritdoc/>
    public override void Move(string sourcePath, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(destinationPath);
        ThrowIfReadOnly();
        var (sourceParent, sourceName) = GetParent(sourcePath);
        var (destinationParent, destinationName) = GetParent(destinationPath);
        if (!sourceParent.Children.TryGetValue(sourceName, out var node))
            throw new WasiException(WasiErrno.NoEntry);

        if (WasiPaths.IsSameOrInside(destinationPath, sourcePath))
            throw new WasiException(WasiErrno.InvalidArgument);

        if (!destinationParent.Children.TryAdd(destinationName, node))
            throw new WasiException(WasiErrno.Exists);

        sourceParent.Children.Remove(sourceName);
        var now = Now;
        node.ChangeTime = now;
        sourceParent.SetWriteTime(now);
        destinationParent.SetWriteTime(now);
    }

    /// <inheritdoc/>
    public override void SetTimes(string path, DateTimeOffset? lastAccessTime, DateTimeOffset? lastWriteTime)
    {
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfReadOnly();
        var node = Get(path);
        if (lastAccessTime is { } access)
        {
            node.LastAccessTime = access;
        }

        if (lastWriteTime is { } write)
        {
            node.LastWriteTime = write;
        }

        node.ChangeTime = Now;
    }

    /// <inheritdoc/>
    public override void CreateSymbolicLink(string path, string target)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(target);
        ThrowIfReadOnly();
        var (parent, name) = GetParent(path);
        if (!parent.Children.TryAdd(name, new SymbolicLinkNode(NextInode(), Now, target)))
            throw new WasiException(WasiErrno.Exists);

        parent.SetWriteTime(Now);
    }

    /// <inheritdoc/>
    public override void CreateHardLink(string path, string existingPath)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(existingPath);
        ThrowIfReadOnly();
        var node = Get(existingPath);
        if (node is DirectoryNode)
            throw new WasiException(WasiErrno.NotPermitted);

        var (parent, name) = GetParent(path);
        if (!parent.Children.TryAdd(name, node))
            throw new WasiException(WasiErrno.Exists);

        node.LinkCount++;
        node.ChangeTime = Now;
        parent.SetWriteTime(Now);
    }

    private abstract class Node(ulong inode, DateTimeOffset now)
    {
        public ulong Inode { get; } = inode;

        public ulong LinkCount { get; set; } = 1;

        public DateTimeOffset LastAccessTime { get; set; } = now;

        public DateTimeOffset LastWriteTime { get; set; } = now;

        public DateTimeOffset ChangeTime { get; set; } = now;

        public abstract WasiFileType Type { get; }

        public void SetWriteTime(DateTimeOffset now)
        {
            LastWriteTime = now;
            ChangeTime = now;
        }

        public WasiFileStat GetStat(WasiFileType type, ulong size) => new(type, size, LastAccessTime, LastWriteTime, ChangeTime) { Inode = Inode, LinkCount = LinkCount };
    }

    private sealed class DirectoryNode(ulong inode, DateTimeOffset now) : Node(inode, now)
    {
        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);

        public override WasiFileType Type => WasiFileType.Directory;
    }

    private sealed class FileNode(ulong inode, DateTimeOffset now) : Node(inode, now)
    {
        public FileData Data { get; } = new();

        public override WasiFileType Type => WasiFileType.RegularFile;
    }

    private sealed class SymbolicLinkNode(ulong inode, DateTimeOffset now, string target) : Node(inode, now)
    {
        public string Target { get; } = target;

        public override WasiFileType Type => WasiFileType.SymbolicLink;
    }

    /// <summary>The bytes of a file, shared by the streams of the file.</summary>
    private sealed class FileData
    {
        private byte[] _bytes = [];

        public long Length { get; private set; }

        public void SetContent(ReadOnlySpan<byte> content)
        {
            _bytes = content.ToArray();
            Length = content.Length;
        }

        public byte[] ToArray() => _bytes.AsSpan(0, (int)Length).ToArray();

        public void SetLength(long length)
        {
            EnsureCapacity(length);
            if (length > Length)
            {
                _bytes.AsSpan((int)Length, (int)(length - Length)).Clear();
            }

            Length = length;
        }

        public int Read(long position, Span<byte> buffer)
        {
            if (position >= Length)
                return 0;

            var count = (int)Math.Min(buffer.Length, Length - position);
            _bytes.AsSpan((int)position, count).CopyTo(buffer);
            return count;
        }

        public void Write(long position, ReadOnlySpan<byte> buffer)
        {
            var end = position + buffer.Length;
            if (end > Length)
            {
                SetLength(end);
            }

            buffer.CopyTo(_bytes.AsSpan((int)position));
        }

        private void EnsureCapacity(long length)
        {
            if (length <= _bytes.Length)
                return;

            var capacity = Math.Max(length, Math.Min((long)_bytes.Length * 2, Array.MaxLength));
            Array.Resize(ref _bytes, (int)capacity);
        }
    }

    private sealed class InMemoryFileStream(WasiInMemoryFileSystem fileSystem, FileNode file, bool canRead, bool canWrite) : Stream
    {
        private long _position;
        private bool _disposed;

        public override bool CanRead => !_disposed && canRead;

        public override bool CanSeek => !_disposed;

        public override bool CanWrite => !_disposed && canWrite;

        public override long Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return file.Data.Length;
            }
        }

        public override long Position
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _position;
            }
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!canRead)
                throw new NotSupportedException("The stream cannot be read");

            var count = file.Data.Read(_position, buffer);
            _position += count;
            file.LastAccessTime = fileSystem.Now;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => file.Data.Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };

            if (position < 0)
                throw new IOException("The position is before the beginning of the file");

            _position = position;
            return position;
        }

        public override void SetLength(long value)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!canWrite)
                throw new NotSupportedException("The stream cannot be written");

            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (value > fileSystem.EffectiveMaxFileSize)
                throw new WasiException(WasiErrno.FileTooLarge);

            file.Data.SetLength(value);
            file.SetWriteTime(fileSystem.Now);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!canWrite)
                throw new NotSupportedException("The stream cannot be written");

            if (buffer.IsEmpty)
                return;

            if (_position > fileSystem.EffectiveMaxFileSize - buffer.Length)
                throw new WasiException(WasiErrno.FileTooLarge);

            file.Data.Write(_position, buffer);
            _position += buffer.Length;
            file.SetWriteTime(fileSystem.Now);
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
