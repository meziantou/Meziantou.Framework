using System.IO.Enumeration;

namespace Meziantou.Framework.Globbing;

/// <summary>Enumerates the entries that match a glob pattern for the methods of <see cref="GlobExtensions"/>.</summary>
internal sealed class GlobRootEnumerator<T> : GlobFileSystemEnumerator<T>
{
    private readonly FileSystemEnumerable<T>.FindTransform _transform;
    private readonly HashSet<string>? _returnedPaths;

    public GlobRootEnumerator(IGlobEvaluatable glob, string directory, EnumerationOptions options, bool includeDirectories, string? matchPrefix, FileSystemEnumerable<T>.FindTransform transform, HashSet<string>? returnedPaths)
        : base(glob, directory, options, includeDirectories, matchPrefix)
    {
        _transform = transform;
        _returnedPaths = returnedPaths;
    }

    protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
    {
        if (!base.ShouldIncludeEntry(ref entry))
            return false;

        // The base directories of a collection can overlap through '..' or an absolute path, so the same entry can be
        // reached from two roots
        return _returnedPaths is null || _returnedPaths.Add(entry.ToFullPath());
    }

    protected override T TransformEntry(ref FileSystemEntry entry) => _transform(ref entry);
}
