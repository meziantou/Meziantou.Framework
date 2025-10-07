#if NET472
using Microsoft.IO;
using Microsoft.IO.Enumeration;
#else
using System.IO.Enumeration;
#endif

namespace Meziantou.Framework.Globbing;

public abstract class GlobCollectionFileSystemEnumerator<T> : FileSystemEnumerator<T>
{
    private readonly GlobCollection _globs;
    private readonly bool _canMatchFiles;
    private readonly bool _canMatchDirectories;

    protected GlobCollectionFileSystemEnumerator(GlobCollection globs, string directory, EnumerationOptions? options = null)
        : base(directory, options)
    {
        _globs = globs;
        _canMatchFiles = globs.Any(g => g.Mode is GlobMode.Include && g.MatchItemType is GlobMatchType.File or GlobMatchType.Any);
        _canMatchDirectories = globs.Any(g => g.Mode is GlobMode.Include && g.MatchItemType is GlobMatchType.Directory or GlobMatchType.Any);
    }

    protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
    {
        return base.ShouldRecurseIntoEntry(ref entry) && _globs.IsPartialMatch(ref entry);
    }

    protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
    {
        if (entry.IsDirectory && !_canMatchDirectories)
            return false;

        if (!entry.IsDirectory && !_canMatchFiles)
            return false;

        return base.ShouldIncludeEntry(ref entry) && _globs.IsMatch(ref entry);
    }
}

public sealed class GlobCollectionFileSystemEnumerator : GlobCollectionFileSystemEnumerator<string>
{
    public GlobCollectionFileSystemEnumerator(GlobCollection globs, string directory, EnumerationOptions? options = null)
        : base(globs, directory, options)
    {
    }

    protected override string TransformEntry(ref FileSystemEntry entry)
    {
        return entry.ToFullPath();
    }
}
