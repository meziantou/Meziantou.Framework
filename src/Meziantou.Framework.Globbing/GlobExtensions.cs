#if NET472
using Microsoft.IO;
using Microsoft.IO.Enumeration;
#else
using System.IO.Enumeration;
#endif

namespace Meziantou.Framework.Globbing;

/// <summary>Provides extension methods for working with glob patterns.</summary>
public static class GlobExtensions
{
    private static readonly EnumerationOptions DefaultEnumerationOptions = new() { RecurseSubdirectories = true };

    /// <summary>Determines whether the specified path matches the glob pattern.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="path">The path to check.</param>
    /// <returns><see langword="true"/> if the path matches the pattern; otherwise, <see langword="false"/>.</returns>
    public static bool IsMatch(this IGlobEvaluatable glob, string path) => IsMatch(glob, path.AsSpan());

    /// <summary>Determines whether the specified path matches the glob pattern.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="path">The path to check.</param>
    /// <returns><see langword="true"/> if the path matches the pattern; otherwise, <see langword="false"/>.</returns>
    public static bool IsMatch(this IGlobEvaluatable glob, ReadOnlySpan<char> path) => glob.IsMatch(path, []);

    /// <summary>Determines whether the specified path matches the glob pattern.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory part of the path to match.</param>
    /// <param name="filename">The filename part of the path to match.</param>
    /// <returns><see langword="true"/> if the path matches the pattern; otherwise, <see langword="false"/>.</returns>
    public static bool IsMatch(this IGlobEvaluatable glob, string directory, string filename) => glob.IsMatch(directory.AsSpan(), filename.AsSpan());

    /// <summary>Determines whether the specified file system entry matches the glob pattern.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="entry">The file system entry to check.</param>
    /// <returns><see langword="true"/> if the entry matches the pattern; otherwise, <see langword="false"/>.</returns>
    public static bool IsMatch(this IGlobEvaluatable glob, ref FileSystemEntry entry) => glob.IsMatch(Glob.GetRelativeDirectory(ref entry), entry.FileName, entry.IsDirectory ? PathItemType.Directory : PathItemType.File);

    /// <summary>Determines whether the specified path matches the glob pattern.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory part of the path to match.</param>
    /// <param name="filename">The filename part of the path to match.</param>
    /// <returns><see langword="true"/> if the path matches the pattern; otherwise, <see langword="false"/>.</returns>
    public static bool IsMatch(this IGlobEvaluatable glob, ReadOnlySpan<char> directory, ReadOnlySpan<char> filename) => glob.IsMatch(directory, filename, itemType: null);

    /// <summary>Determines whether a directory should be recursed into when enumerating files.</summary>
    /// <param name="glob">The glob pattern to check against.</param>
    /// <param name="folderPath">The folder path to check.</param>
    /// <returns><see langword="true"/> if the directory could contain matches; otherwise, <see langword="false"/>.</returns>
    public static bool IsPartialMatch(this IGlobEvaluatable glob, string folderPath) => IsPartialMatch(glob, folderPath.AsSpan());

    /// <summary>Determines whether a directory should be recursed into when enumerating files.</summary>
    /// <param name="glob">The glob pattern to check against.</param>
    /// <param name="folderPath">The folder path to check.</param>
    /// <returns><see langword="true"/> if the directory could contain matches; otherwise, <see langword="false"/>.</returns>
    public static bool IsPartialMatch(this IGlobEvaluatable glob, ReadOnlySpan<char> folderPath) => glob.IsPartialMatch(folderPath, []);

    /// <summary>Determines whether a file system entry directory should be recursed into when enumerating files.</summary>
    /// <param name="glob">The glob pattern to check against.</param>
    /// <param name="entry">The file system entry to check.</param>
    /// <returns><see langword="true"/> if the directory could contain matches; otherwise, <see langword="false"/>.</returns>
    public static bool IsPartialMatch(this IGlobEvaluatable glob, ref FileSystemEntry entry) => glob.IsPartialMatch(Glob.GetRelativeDirectory(ref entry), entry.FileName);

    /// <summary>Determines whether a directory should be recursed into when enumerating files.</summary>
    /// <param name="glob">The glob pattern to check against.</param>
    /// <param name="folderPath">The folder path to check.</param>
    /// <param name="filename">The filename part of the path to check.</param>
    /// <returns><see langword="true"/> if the directory could contain matches; otherwise, <see langword="false"/>.</returns>
    public static bool IsPartialMatch(this IGlobEvaluatable glob, string folderPath, string filename) => glob.IsPartialMatch(folderPath.AsSpan(), filename.AsSpan());


    /// <summary>Enumerates files in the specified directory that match the glob pattern.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory to search.</param>
    /// <param name="options">Options for controlling file enumeration behavior.</param>
    /// <returns>An enumerable collection of file paths that match the glob pattern.</returns>
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

    /// <summary>Enumerates file system entries (files and directories) in the specified directory that match the glob pattern.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory to search.</param>
    /// <param name="options">Options for controlling file enumeration behavior.</param>
    /// <returns>An enumerable collection of file system entry paths that match the glob pattern.</returns>
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
