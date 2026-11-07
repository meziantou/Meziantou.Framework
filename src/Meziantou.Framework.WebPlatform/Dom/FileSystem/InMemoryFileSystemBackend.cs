namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A <see cref="FileSystemBackend"/> that keeps the files in memory: the default backend of the browsing contexts (see
/// <see cref="BrowsingContextOptions.FileSystem"/>). It is thread-safe, and can be shared by several browsing contexts.
/// </summary>
public sealed class InMemoryFileSystemBackend : FileSystemBackend
{
    // The bytes that an entry counts for in the usage, besides the content of the files
    private const int EntryOverhead = 64;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, DirectoryNode> _roots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _usages = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public override FileSystemHandleKind? GetEntryKind(string origin, IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(path);
        lock (_lock)
        {
            return Find(origin, path) switch
            {
                DirectoryNode => FileSystemHandleKind.Directory,
                FileNode => FileSystemHandleKind.File,
                _ => null,
            };
        }
    }

    /// <inheritdoc/>
    public override IReadOnlyList<FileSystemEntry>? GetEntries(string origin, IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(path);
        lock (_lock)
        {
            if (Find(origin, path) is not DirectoryNode directory)
                return null;

            var entries = new FileSystemEntry[directory.Entries.Count];
            var index = 0;
            foreach (var (name, node) in directory.Entries)
            {
                entries[index++] = new FileSystemEntry(name, node is DirectoryNode ? FileSystemHandleKind.Directory : FileSystemHandleKind.File);
            }

            return entries;
        }
    }

    /// <inheritdoc/>
    public override bool CreateEntry(string origin, IReadOnlyList<string> path, FileSystemHandleKind kind, long modificationTime)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0)
            return false;

        lock (_lock)
        {
            if (FindParent(origin, path, create: true) is not { } parent)
                return false;

            Node node = kind == FileSystemHandleKind.Directory ? new DirectoryNode() : new FileNode { ModificationTime = modificationTime };
            if (!parent.Entries.TryAdd(path[^1], node))
                return false;

            AddUsage(origin, EntryOverhead);
            return true;
        }
    }

    /// <inheritdoc/>
    public override bool RemoveEntry(string origin, IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(path);
        lock (_lock)
        {
            if (path.Count == 0)
            {
                if (!_roots.Remove(origin))
                    return false;

                _usages.Remove(origin);
                return true;
            }

            if (FindParent(origin, path, create: false) is not { } parent || !parent.Entries.Remove(path[^1], out var node))
                return false;

            AddUsage(origin, -GetSize(node));
            return true;
        }
    }

    /// <inheritdoc/>
    public override bool MoveFile(string origin, IReadOnlyList<string> path, IReadOnlyList<string> newPath)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(newPath);
        if (path.Count == 0 || newPath.Count == 0)
            return false;

        lock (_lock)
        {
            if (FindParent(origin, path, create: false) is not { } parent || !parent.Entries.TryGetValue(path[^1], out var node) || node is not FileNode file)
                return false;

            if (FindParent(origin, newPath, create: false) is not { } newParent)
                return false;

            if (newParent.Entries.TryGetValue(newPath[^1], out var existing))
            {
                if (ReferenceEquals(existing, file))
                    return true;

                if (existing is not FileNode)
                    return false;

                AddUsage(origin, -GetSize(existing));
            }

            parent.Entries.Remove(path[^1]);
            newParent.Entries[newPath[^1]] = file;
            return true;
        }
    }

    /// <inheritdoc/>
    public override FileSystemFileInfo? GetFileInfo(string origin, IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(path);
        lock (_lock)
        {
            return Find(origin, path) is FileNode file ? new FileSystemFileInfo(file.Length, file.ModificationTime) : null;
        }
    }

    /// <inheritdoc/>
    public override int ReadFile(string origin, IReadOnlyList<string> path, long position, Span<byte> buffer)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        lock (_lock)
        {
            if (Find(origin, path) is not FileNode file || position >= file.Length)
                return 0;

            var count = (int)Math.Min(buffer.Length, file.Length - position);
            file.Content.AsSpan((int)position, count).CopyTo(buffer);
            return count;
        }
    }

    /// <inheritdoc/>
    public override void WriteFile(string origin, IReadOnlyList<string> path, long position, ReadOnlySpan<byte> data, long modificationTime)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        lock (_lock)
        {
            if (Find(origin, path) is not FileNode file)
                throw new FileNotFoundException("The file does not exist");

            var end = checked(position + data.Length);
            if (end > file.Length)
            {
                Resize(origin, file, end);
            }

            data.CopyTo(file.Content.AsSpan((int)position));
            file.ModificationTime = modificationTime;
        }
    }

    /// <inheritdoc/>
    public override void SetFileSize(string origin, IReadOnlyList<string> path, long size, long modificationTime)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        lock (_lock)
        {
            if (Find(origin, path) is not FileNode file)
                throw new FileNotFoundException("The file does not exist");

            Resize(origin, file, size);
            file.ModificationTime = modificationTime;
        }
    }

    /// <inheritdoc/>
    public override bool ReplaceFileContent(string origin, IReadOnlyList<string> path, ReadOnlyMemory<byte> content, long modificationTime)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(path);
        lock (_lock)
        {
            if (Find(origin, path) is not FileNode file)
                return false;

            AddUsage(origin, content.Length - file.Length);
            file.Content = content.ToArray();
            file.Length = content.Length;
            file.ModificationTime = modificationTime;
            return true;
        }
    }

    /// <inheritdoc/>
    public override long GetUsage(string origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            return _usages.GetValueOrDefault(origin);
        }
    }

    // The content array of a file grows by doubling: its length is the capacity of the file
    private void Resize(string origin, FileNode file, long size)
    {
        if (size > Array.MaxLength)
            throw new IOException("The file is too large");

        if (size > file.Content.Length)
        {
            var content = new byte[(int)Math.Min(Array.MaxLength, Math.Max(size, file.Content.Length * 2L))];
            file.Content.AsSpan(0, (int)file.Length).CopyTo(content);
            file.Content = content;
        }
        else if (size < file.Length)
        {
            // The bytes after the end of the file are zeros
            file.Content.AsSpan((int)size, (int)(file.Length - size)).Clear();
        }

        AddUsage(origin, size - file.Length);
        file.Length = size;
    }

    private void AddUsage(string origin, long delta) => _usages[origin] = Math.Max(0, _usages.GetValueOrDefault(origin) + delta);

    private static long GetSize(Node node)
    {
        if (node is FileNode file)
            return EntryOverhead + file.Length;

        long size = EntryOverhead;
        foreach (var child in ((DirectoryNode)node).Entries.Values)
        {
            size += GetSize(child);
        }

        return size;
    }

    private Node? Find(string origin, IReadOnlyList<string> path)
    {
        if (!_roots.TryGetValue(origin, out var directory))
            return path.Count == 0 ? new DirectoryNode() : null;

        Node node = directory;
        foreach (var name in path)
        {
            if (node is not DirectoryNode current || !current.Entries.TryGetValue(name, out var child))
                return null;

            node = child;
        }

        return node;
    }

    private DirectoryNode? FindParent(string origin, IReadOnlyList<string> path, bool create)
    {
        if (!_roots.TryGetValue(origin, out var directory))
        {
            // The root directory of an origin always exists: it is stored once it has an entry
            if (!create || path.Count != 1)
                return null;

            directory = new DirectoryNode();
            _roots.Add(origin, directory);
        }

        for (var i = 0; i < path.Count - 1; i++)
        {
            if (!directory.Entries.TryGetValue(path[i], out var child) || child is not DirectoryNode childDirectory)
                return null;

            directory = childDirectory;
        }

        return directory;
    }

    private abstract class Node;

    private sealed class DirectoryNode : Node
    {
        public Dictionary<string, Node> Entries { get; } = new(StringComparer.Ordinal);
    }

    private sealed class FileNode : Node
    {
        public byte[] Content { get; set; } = [];

        public long Length { get; set; }

        public long ModificationTime { get; set; }
    }
}
