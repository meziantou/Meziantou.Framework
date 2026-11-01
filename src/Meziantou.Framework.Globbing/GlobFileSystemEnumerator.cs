using System.Diagnostics;
using System.IO.Enumeration;

namespace Meziantou.Framework.Globbing;

/// <summary>Provides a base class for enumerating file system entries that match a glob pattern.</summary>
/// <typeparam name="T">The type of objects to enumerate.</typeparam>
public abstract class GlobFileSystemEnumerator<T> : FileSystemEnumerator<T>
{
    private readonly IGlobEvaluatable _glob;

    // A collection created from gitignore content matches every path below an excluded directory. Each directory is
    // resolved once, when deciding whether to recurse into it, and its entries reuse the result instead of
    // evaluating every ancestor again.
    private readonly GlobCollection? _gitIgnore;
    private Dictionary<string, bool>? _pendingDirectories;
    private string? _currentDirectory;
    private bool _currentDirectoryIgnored;

    /// <summary>Initializes a new instance of the <see cref="GlobFileSystemEnumerator{T}"/> class.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory to enumerate.</param>
    /// <param name="options">Options for controlling enumeration behavior.</param>
    protected GlobFileSystemEnumerator(IGlobEvaluatable glob, string directory, EnumerationOptions? options = null)
        : base(directory, options)
    {
        _glob = glob;
        _gitIgnore = glob is GlobCollection { IsGitIgnore: true } gitIgnore ? gitIgnore : null;
    }

    protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
    {
        if (!base.ShouldRecurseIntoEntry(ref entry))
            return false;

        if (_gitIgnore is null)
            return _glob.IsPartialMatch(ref entry);

        var relativeDirectory = Glob.GetRelativeDirectory(ref entry);
        var ignored = IsCurrentDirectoryIgnored(ref entry) || _gitIgnore.GetLastMatch(relativeDirectory, entry.FileName, PathItemType.Directory) is GlobMode.Include;
        if (!_gitIgnore.IsGitIgnorePartialMatch(relativeDirectory, entry.FileName, ignored))
            return false;

        (_pendingDirectories ??= new(StringComparer.Ordinal))[entry.ToFullPath()] = ignored;
        return true;
    }

    protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
    {
        if (entry.IsDirectory && !_glob.CanMatchDirectories)
            return false;

        if (!entry.IsDirectory && !_glob.CanMatchFiles)
            return false;

        if (!base.ShouldIncludeEntry(ref entry))
            return false;

        if (_gitIgnore is null)
            return _glob.IsMatch(ref entry);

        return IsCurrentDirectoryIgnored(ref entry)
            || _gitIgnore.GetLastMatch(Glob.GetRelativeDirectory(ref entry), entry.FileName, entry.IsDirectory ? PathItemType.Directory : PathItemType.File) is GlobMode.Include;
    }

    // Whether the directory that holds the entry, or one of its ancestors, is excluded by the gitignore content
    private bool IsCurrentDirectoryIgnored(ref FileSystemEntry entry)
    {
        Debug.Assert(_gitIgnore is not null);

        var directory = entry.Directory;
        if (_currentDirectory is null || !directory.SequenceEqual(_currentDirectory))
        {
            _currentDirectory = directory.ToString();
            if (directory.Length == entry.RootDirectory.Length)
            {
                _currentDirectoryIgnored = false;
            }
            else if (_pendingDirectories is not null && _pendingDirectories.Remove(_currentDirectory, out var ignored))
            {
                _currentDirectoryIgnored = ignored;
            }
            else
            {
                _currentDirectoryIgnored = _gitIgnore.IsMatch(Glob.GetRelativeDirectory(ref entry), [], PathItemType.Directory);
            }
        }

        return _currentDirectoryIgnored;
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
