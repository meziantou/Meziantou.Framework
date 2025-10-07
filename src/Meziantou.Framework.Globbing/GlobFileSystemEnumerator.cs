#if NET472
using Microsoft.IO;
using Microsoft.IO.Enumeration;
#else
using System.IO.Enumeration;
#endif

namespace Meziantou.Framework.Globbing;

public abstract class GlobFileSystemEnumerator<T> : FileSystemEnumerator<T>
{
    private readonly IGlobEvaluatable _glob;

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

public sealed class GlobFileSystemEnumerator : GlobFileSystemEnumerator<string>
{
    public GlobFileSystemEnumerator(IGlobEvaluatable glob, string directory, EnumerationOptions? options = null)
        : base(glob, directory, options)
    {
    }

    protected override string TransformEntry(ref FileSystemEntry entry)
    {
        return entry.ToFullPath();
    }
}
