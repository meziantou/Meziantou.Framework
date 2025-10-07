#if NET472
using Microsoft.IO;
using Microsoft.IO.Enumeration;
#else
using System.IO.Enumeration;
#endif

namespace Meziantou.Framework.Globbing;

public abstract class GlobFileSystemEnumerator<T> : FileSystemEnumerator<T>
{
    private readonly Glob _glob;

    protected GlobFileSystemEnumerator(Glob glob, string directory, EnumerationOptions? options = null)
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
        if (_glob.MatchItemType is not GlobMatchType.Any)
        {
            if (_glob.MatchItemType is GlobMatchType.Directory && !entry.IsDirectory)
                return false;

            if (_glob.MatchItemType is GlobMatchType.File && entry.IsDirectory)
                return false;
        }

        return base.ShouldIncludeEntry(ref entry) && _glob.IsMatch(ref entry);
    }
}

public sealed class GlobFileSystemEnumerator : GlobFileSystemEnumerator<string>
{
    public GlobFileSystemEnumerator(Glob glob, string directory, EnumerationOptions? options = null)
        : base(glob, directory, options)
    {
    }

    protected override string TransformEntry(ref FileSystemEntry entry)
    {
        return entry.ToFullPath();
    }
}
