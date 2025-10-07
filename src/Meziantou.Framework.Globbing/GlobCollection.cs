using System.Collections;
using System.ComponentModel;

#if NET472
using Microsoft.IO;
using Microsoft.IO.Enumeration;
#else
using System.IO.Enumeration;
#endif

namespace Meziantou.Framework.Globbing;

[System.Runtime.CompilerServices.CollectionBuilder(typeof(GlobCollection), nameof(Create))]
public sealed class GlobCollection : IReadOnlyList<Glob>, IGlob
{
    private static readonly EnumerationOptions DefaultEnumerationOptions = new() { RecurseSubdirectories = true };

    private readonly Glob[] _globs;

    public GlobCollection(params Glob[] globs) => _globs = globs;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static GlobCollection Create(ReadOnlySpan<Glob> globs) => new(globs.ToArray());

    public int Count => _globs.Length;
    public Glob this[int index] => _globs[index];

    public bool IsMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType)
    {
        var match = false;
        foreach (var glob in _globs)
        {
            if (match && glob.Mode is GlobMode.Include)
                continue;

            if (glob.IsMatchCore(directory, filename, itemType))
            {
                if (glob.Mode is GlobMode.Exclude)
                    return false;

                match = true;
            }
        }

        return match;
    }

    public bool IsPartialMatch(string folderPath) => IsPartialMatch(folderPath.AsSpan());
    public bool IsPartialMatch(ReadOnlySpan<char> folderPath) => IsPartialMatch(folderPath, []);
    public bool IsPartialMatch(ref FileSystemEntry entry) => IsPartialMatch(Glob.GetRelativeDirectory(ref entry), entry.FileName);
    public bool IsPartialMatch(string folderPath, string filename) => IsPartialMatch(folderPath.AsSpan(), filename.AsSpan());

    public bool IsPartialMatch(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename)
    {
        foreach (var glob in _globs)
        {
            if (glob.Mode is GlobMode.Exclude)
                continue;

            if (glob.IsPartialMatchCore(folderPath, filename))
                return true;
        }

        return false;
    }

    public IEnumerable<string> EnumerateFiles(string directory, EnumerationOptions? options = null)
    {
        if (!_globs.Any(g => g.Mode is GlobMode.Include && g.MatchItemType is GlobMatchType.File or GlobMatchType.Any))
            yield break;

        if (options is null && _globs.Any(glob => glob.ShouldRecurseSubdirectories()))
        {
            options = DefaultEnumerationOptions;
        }

        using var enumerator = new GlobCollectionFileSystemEnumerator(this, directory, options);
        while (enumerator.MoveNext())
            yield return enumerator.Current;
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string directory, EnumerationOptions? options = null)
    {
        if (options is null && _globs.Any(glob => glob.ShouldRecurseSubdirectories()))
        {
            options = DefaultEnumerationOptions;
        }

        using var enumerator = new GlobCollectionFileSystemEnumerator(this, directory, options);
        while (enumerator.MoveNext())
            yield return enumerator.Current;
    }

    public IEnumerator<Glob> GetEnumerator() => ((IEnumerable<Glob>)_globs).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _globs.GetEnumerator();
}
