using System.IO.Enumeration;

namespace Meziantou.Framework.Globbing;

/// <summary>Provides a base class for enumerating file system entries that match a glob pattern.</summary>
/// <typeparam name="T">The type of objects to enumerate.</typeparam>
public abstract class GlobFileSystemEnumerator<T> : FileSystemEnumerator<T>
{
    private readonly IGlobEvaluatable _glob;
    private readonly bool _includeDirectories;

    // The path of the root directory relative to the directory the pattern is evaluated against, ending with a '/',
    // such as "src/". The pattern matches this prefix followed by the path of the entry relative to the root.
    private readonly string? _matchPrefix;
    private char[]? _matchBuffer;

    /// <summary>Initializes a new instance of the <see cref="GlobFileSystemEnumerator{T}"/> class.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory to enumerate.</param>
    /// <param name="options">Options for controlling enumeration behavior.</param>
    protected GlobFileSystemEnumerator(IGlobEvaluatable glob, string directory, EnumerationOptions? options = null)
        : this(glob, directory, options, includeDirectories: true, matchPrefix: null)
    {
    }

    private protected GlobFileSystemEnumerator(IGlobEvaluatable glob, string directory, EnumerationOptions? options, bool includeDirectories, string? matchPrefix)
        : base(directory, options)
    {
        _glob = glob;
        _includeDirectories = includeDirectories;
        _matchPrefix = matchPrefix;
    }

    protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
    {
        if (!base.ShouldRecurseIntoEntry(ref entry))
            return false;

        if (_matchPrefix is null)
            return _glob.IsPartialMatch(ref entry);

        return _glob.IsPartialMatch(GetMatchPath(ref entry), []);
    }

    protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
    {
        if (entry.IsDirectory && (!_includeDirectories || !_glob.CanMatchDirectories))
            return false;

        if (!entry.IsDirectory && !_glob.CanMatchFiles)
            return false;

        if (!base.ShouldIncludeEntry(ref entry))
            return false;

        if (_matchPrefix is null)
            return _glob.IsMatch(ref entry);

        return _glob.IsMatch(GetMatchPath(ref entry), [], entry.IsDirectory ? PathItemType.Directory : PathItemType.File);
    }

    private ReadOnlySpan<char> GetMatchPath(ref FileSystemEntry entry)
    {
        var prefix = _matchPrefix.AsSpan();
        var directory = Glob.GetRelativeDirectory(ref entry);
        var fileName = entry.FileName;
        var length = prefix.Length + directory.Length + (directory.IsEmpty ? 0 : 1) + fileName.Length;
        if (_matchBuffer is null || _matchBuffer.Length < length)
        {
            _matchBuffer = new char[Math.Max(length, (_matchBuffer?.Length ?? 128) * 2)];
        }

        var buffer = _matchBuffer.AsSpan();
        prefix.CopyTo(buffer);
        var index = prefix.Length;
        if (!directory.IsEmpty)
        {
            directory.CopyTo(buffer[index..]);
            index += directory.Length;
            buffer[index] = '/';
            index++;
        }

        fileName.CopyTo(buffer[index..]);
        return buffer[..length];
    }
}

/// <summary>Enumerates file system entries that match a glob pattern, returning full paths as strings.</summary>
public sealed class GlobFileSystemEnumerator : GlobFileSystemEnumerator<string>
{
    /// <summary>Initializes a new instance of the <see cref="GlobFileSystemEnumerator"/> class.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory to enumerate.</param>
    /// <param name="options">Options for controlling enumeration behavior.</param>
    public GlobFileSystemEnumerator(IGlobEvaluatable glob, string directory, EnumerationOptions? options = null)
        : base(glob, directory, options)
    {
    }

    /// <summary>Transforms a file system entry into its full path string representation.</summary>
    /// <param name="entry">The file system entry to transform.</param>
    /// <returns>The full path of the file system entry.</returns>
    protected override string TransformEntry(ref FileSystemEntry entry)
    {
        return entry.ToFullPath();
    }
}
