#if NET472
using Microsoft.IO;
using Microsoft.IO.Enumeration;
#else
using System.IO.Enumeration;
#endif

namespace Meziantou.Framework.Globbing;

public static class GlobExtensions
{
    private static readonly EnumerationOptions DefaultEnumerationOptions = new() { RecurseSubdirectories = true };

    public static bool IsMatch(this IGlobEvaluatable glob, string path) => IsMatch(glob, path.AsSpan());
    public static bool IsMatch(this IGlobEvaluatable glob, ReadOnlySpan<char> path) => glob.IsMatch(path, []);
    public static bool IsMatch(this IGlobEvaluatable glob, string directory, string filename) => glob.IsMatch(directory.AsSpan(), filename.AsSpan());
    public static bool IsMatch(this IGlobEvaluatable glob, ref FileSystemEntry entry) => glob.IsMatch(Glob.GetRelativeDirectory(ref entry), entry.FileName, entry.IsDirectory ? PathItemType.Directory : PathItemType.File);
    public static bool IsMatch(this IGlobEvaluatable glob, ReadOnlySpan<char> directory, ReadOnlySpan<char> filename) => glob.IsMatch(directory, filename, itemType: null);

    public static bool IsPartialMatch(this IGlobEvaluatable glob, string folderPath) => IsPartialMatch(glob, folderPath.AsSpan());
    public static bool IsPartialMatch(this IGlobEvaluatable glob, ReadOnlySpan<char> folderPath) => glob.IsPartialMatch(folderPath, []);
    public static bool IsPartialMatch(this IGlobEvaluatable glob, ref FileSystemEntry entry) => glob.IsPartialMatch(Glob.GetRelativeDirectory(ref entry), entry.FileName);
    public static bool IsPartialMatch(this IGlobEvaluatable glob, string folderPath, string filename) => glob.IsPartialMatch(folderPath.AsSpan(), filename.AsSpan());


    public static IEnumerable<string> EnumerateFiles(this IGlobEvaluatable glob, string directory, EnumerationOptions? options = null)
    {
        if (!glob.CanMatchFiles)
            yield break;

        if (options is null && glob.TraverseDirectories)
        {
            options = DefaultEnumerationOptions;
        }

        using var enumerator = new GlobFileSystemEnumerator(glob, directory, options);
        while (enumerator.MoveNext())
            yield return enumerator.Current;
    }

    public static IEnumerable<string> EnumerateFileSystemEntries(this IGlobEvaluatable glob, string directory, EnumerationOptions? options = null)
    {
        if (options is null && glob.TraverseDirectories)
        {
            options = DefaultEnumerationOptions;
        }

        using var enumerator = new GlobFileSystemEnumerator(glob, directory, options);
        while (enumerator.MoveNext())
            yield return enumerator.Current;
    }
}
