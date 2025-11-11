#if NET472
using Microsoft.IO;
using Microsoft.IO.Enumeration;
#else
using System.IO.Enumeration;
#endif

namespace Meziantou.Framework.Globbing;

/// <summary>Provides a base class for enumerating file system entries that match a glob pattern.</summary>
/// <typeparam name="T">The type of objects to enumerate.</typeparam>
public abstract class GlobFileSystemEnumerator<T> : FileSystemEnumerator<T>
{
    private readonly IGlobEvaluatable _glob;

    /// <summary>Initializes a new instance of the <see cref="GlobFileSystemEnumerator{T}"/> class.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory to enumerate.</param>
    /// <param name="options">Options for controlling enumeration behavior.</param>
    protected GlobFileSystemEnumerator(IGlobEvaluatable glob, string directory, EnumerationOptions? options = null)
        : base(directory, options)
    {
        _glob = glob;
    }

    protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
    {
        return base.ShouldRecurseIntoEntry(ref entry) && _glob.IsPartialMatch(ref entry);
    }

    protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
    {
        if (entry.IsDirectory && !_glob.CanMatchDirectories)
            return false;

        if (!entry.IsDirectory && !_glob.CanMatchFiles)
            return false;

        return base.ShouldIncludeEntry(ref entry) && _glob.IsMatch(ref entry);
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
