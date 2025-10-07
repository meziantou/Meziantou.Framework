#if NET472
using Microsoft.IO;
using Microsoft.IO.Enumeration;
#else
using System.IO.Enumeration;
#endif

namespace Meziantou.Framework.Globbing;

public static class GlobExtensions
{
    public static bool IsMatch(this IGlob glob, string path) => IsMatch(glob, path.AsSpan());
    public static bool IsMatch(this IGlob glob, ReadOnlySpan<char> path) => glob.IsMatch(path, []);
    public static bool IsMatch(this IGlob glob, string directory, string filename) => glob.IsMatch(directory.AsSpan(), filename.AsSpan());
    public static bool IsMatch(this IGlob glob, ref FileSystemEntry entry) => glob.IsMatch(Glob.GetRelativeDirectory(ref entry), entry.FileName, entry.IsDirectory ? PathItemType.Directory : PathItemType.File);
    public static bool IsMatch(this IGlob glob, ReadOnlySpan<char> directory, ReadOnlySpan<char> filename) => glob.IsMatch(directory, filename, itemType: null);

    public static bool IsPartialMatch(this IGlob glob, string folderPath) => IsPartialMatch(glob, folderPath.AsSpan());
    public static bool IsPartialMatch(this IGlob glob, ReadOnlySpan<char> folderPath) => glob.IsPartialMatch(folderPath, []);
    public static bool IsPartialMatch(this IGlob glob, ref FileSystemEntry entry) => glob.IsPartialMatch(Glob.GetRelativeDirectory(ref entry), entry.FileName);
    public static bool IsPartialMatch(this IGlob glob, string folderPath, string filename) => glob.IsPartialMatch(folderPath.AsSpan(), filename.AsSpan());
}
