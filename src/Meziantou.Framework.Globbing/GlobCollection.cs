using System.Collections;

#if NET472
using Microsoft.IO;
using Microsoft.IO.Enumeration;
#else
using System.IO.Enumeration;
#endif

namespace Meziantou.Framework.Globbing;

public sealed class GlobCollection : IReadOnlyList<Glob>
{
    private static readonly EnumerationOptions DefaultEnumerationOptions = new() { RecurseSubdirectories = true };

    private readonly Glob[] _globs;

    public GlobCollection(params Glob[] globs) => _globs = globs;

    public int Count => _globs.Length;
    public Glob this[int index] => _globs[index];

    public bool IsMatch(string path) => IsMatch(path.AsSpan());
    public bool IsMatch(ReadOnlySpan<char> path) => IsMatch(path, []);
    public bool IsMatch(string directory, string filename) => IsMatch(directory.AsSpan(), filename.AsSpan());
    public bool IsMatch(ref FileSystemEntry entry) => IsMatch(Glob.GetRelativeDirectory(ref entry), entry.FileName);

    public bool IsMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename)
    {
        var match = false;
        foreach (var glob in _globs)
        {
            if (match && glob.Mode == GlobMode.Include)
                continue;

            if (glob.IsMatchCore(directory, filename))
            {
                if (glob.Mode == GlobMode.Exclude)
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
            if (glob.Mode == GlobMode.Exclude)
                continue;

            if (glob.IsPartialMatchCore(folderPath, filename))
                return true;
        }

        return false;
    }

    public IEnumerable<string> EnumerateFiles(string directory, EnumerationOptions? options = null)
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
