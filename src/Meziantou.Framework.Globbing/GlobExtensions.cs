using System.IO.Enumeration;
using Meziantou.Framework.Globbing.Internals;

namespace Meziantou.Framework.Globbing;

/// <summary>Provides extension methods for working with glob patterns.</summary>
public static class GlobExtensions
{
    private static readonly EnumerationOptions DefaultEnumerationOptions = new();
    private static readonly EnumerationOptions DefaultEnumerationOptionsIncludingHidden = new() { AttributesToSkip = default(FileAttributes) };
    private static readonly EnumerationOptions DefaultRecursiveEnumerationOptions = new() { RecurseSubdirectories = true };
    private static readonly EnumerationOptions DefaultRecursiveEnumerationOptionsIncludingHidden = new() { RecurseSubdirectories = true, AttributesToSkip = default(FileAttributes) };

    // Windows and macOS file systems are usually case-insensitive. Comparing paths case-insensitively there only
    // matters to detect two base directories that are the same directory.
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly StringComparer PathComparer = StringComparer.FromComparison(PathComparison);

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
    /// <returns>An enumerable collection of the full paths of the files that match the glob pattern.</returns>
    /// <remarks>
    ///     The enumeration starts at the base path of the pattern (see <see cref="Glob.TrySplitBasePath(out string?, out Glob?)"/>),
    ///     or at the base path of each include pattern of a <see cref="GlobCollection"/>. A pattern that starts with
    ///     <c>..</c> or that is absolute can therefore return files outside <paramref name="directory"/>.
    /// </remarks>
    public static IEnumerable<string> EnumerateFiles(this IGlobEvaluatable glob, string directory, EnumerationOptions? options = null)
    {
        return EnumerateFiles(glob, directory, GlobPathFormat.FullPath, options);
    }

    /// <summary>Enumerates files in the specified directory that match the glob pattern.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory to search.</param>
    /// <param name="pathFormat">The format of the returned paths.</param>
    /// <param name="options">Options for controlling file enumeration behavior.</param>
    /// <returns>An enumerable collection of the paths of the files that match the glob pattern.</returns>
    /// <remarks>
    ///     A relative path is relative to <paramref name="directory"/>, so it starts with <c>..</c> for a file found
    ///     through a pattern that goes up from the directory. See <see cref="EnumerateFiles(IGlobEvaluatable, string, EnumerationOptions?)"/>.
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs" ref:region="Glob_EnumerateFilesRelativePaths" ref:dedent="true">
    /// using var rootDirectory = TemporaryDirectory.Create();
    /// rootDirectory.CreateEmptyFile("src/App/Program.cs");
    /// rootDirectory.CreateEmptyFile("src/Lib/Helper.cs");
    ///
    /// // Return the paths relative to the enumerated directory, as the pattern matches them
    /// var glob = Glob.Parse("src/**/*.cs", GlobDialect.Standard);
    /// var files = glob.EnumerateFiles(rootDirectory.FullPath, GlobPathFormat.RelativePath).Order(StringComparer.Ordinal).ToList();
    ///
    /// Assert.Equal(
    ///     [
    ///         Path.Combine("src", "App", "Program.cs"),
    ///         Path.Combine("src", "Lib", "Helper.cs"),
    ///     ],
    ///     files);
    /// </code>
    /// </example>
    public static IEnumerable<string> EnumerateFiles(this IGlobEvaluatable glob, string directory, GlobPathFormat pathFormat, EnumerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(glob);
        ArgumentNullException.ThrowIfNull(directory);

        return Enumerate(glob, directory, options, includeDirectories: false, GetPathTransformFactory(pathFormat));
    }

    /// <summary>Enumerates file system entries (files and directories) in the specified directory that match the glob pattern.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory to search.</param>
    /// <param name="options">Options for controlling file enumeration behavior.</param>
    /// <returns>An enumerable collection of the full paths of the entries that match the glob pattern.</returns>
    /// <remarks>See <see cref="EnumerateFiles(IGlobEvaluatable, string, EnumerationOptions?)"/>.</remarks>
    public static IEnumerable<string> EnumerateFileSystemEntries(this IGlobEvaluatable glob, string directory, EnumerationOptions? options = null)
    {
        return EnumerateFileSystemEntries(glob, directory, GlobPathFormat.FullPath, options);
    }

    /// <summary>Enumerates file system entries (files and directories) in the specified directory that match the glob pattern.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory to search.</param>
    /// <param name="pathFormat">The format of the returned paths.</param>
    /// <param name="options">Options for controlling file enumeration behavior.</param>
    /// <returns>An enumerable collection of the paths of the entries that match the glob pattern.</returns>
    /// <remarks>See <see cref="EnumerateFiles(IGlobEvaluatable, string, GlobPathFormat, EnumerationOptions?)"/>.</remarks>
    public static IEnumerable<string> EnumerateFileSystemEntries(this IGlobEvaluatable glob, string directory, GlobPathFormat pathFormat, EnumerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(glob);
        ArgumentNullException.ThrowIfNull(directory);

        return Enumerate(glob, directory, options, includeDirectories: true, GetPathTransformFactory(pathFormat));
    }

    /// <summary>Enumerates the files in the specified directory that match the glob pattern, as <see cref="FileInfo"/> objects.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory to search.</param>
    /// <param name="options">Options for controlling file enumeration behavior.</param>
    /// <returns>An enumerable collection of the files that match the glob pattern.</returns>
    /// <remarks>
    ///     The objects are initialized with the data read during the enumeration, so reading their length or their
    ///     timestamps does not access the file system again. See <see cref="EnumerateFiles(IGlobEvaluatable, string, EnumerationOptions?)"/>.
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Globbing.Tests/GlobTests.cs" ref:region="Glob_EnumerateFileInfos" ref:dedent="true">
    /// using var rootDirectory = TemporaryDirectory.Create();
    /// rootDirectory.CreateTextFile("logs/app.log", "started");
    /// rootDirectory.CreateTextFile("logs/error.log", "");
    ///
    /// // The FileInfo objects are filled during the enumeration: reading Length does not access the disk again
    /// var glob = Glob.Parse("logs/*.log", GlobDialect.Standard);
    /// var nonEmptyFiles = glob.EnumerateFileInfos(rootDirectory.FullPath)
    ///     .Where(file =&gt; file.Length &gt; 0)
    ///     .Select(file =&gt; file.Name)
    ///     .ToList();
    ///
    /// Assert.Equal(["app.log"], nonEmptyFiles);
    /// </code>
    /// </example>
    public static IEnumerable<FileInfo> EnumerateFileInfos(this IGlobEvaluatable glob, string directory, EnumerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(glob);
        ArgumentNullException.ThrowIfNull(directory);

        return Enumerate<FileInfo>(glob, directory, options, includeDirectories: false, static _ => static (ref entry) => (FileInfo)entry.ToFileSystemInfo());
    }

    /// <summary>Enumerates the file system entries (files and directories) in the specified directory that match the glob pattern, as <see cref="FileSystemInfo"/> objects.</summary>
    /// <param name="glob">The glob pattern to match against.</param>
    /// <param name="directory">The directory to search.</param>
    /// <param name="options">Options for controlling file enumeration behavior.</param>
    /// <returns>An enumerable collection of the entries that match the glob pattern: a <see cref="FileInfo"/> for a file, and a <see cref="DirectoryInfo"/> for a directory.</returns>
    /// <remarks>See <see cref="EnumerateFileInfos(IGlobEvaluatable, string, EnumerationOptions?)"/>.</remarks>
    public static IEnumerable<FileSystemInfo> EnumerateFileSystemInfos(this IGlobEvaluatable glob, string directory, EnumerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(glob);
        ArgumentNullException.ThrowIfNull(directory);

        return Enumerate<FileSystemInfo>(glob, directory, options, includeDirectories: true, static _ => static (ref entry) => entry.ToFileSystemInfo());
    }

    private static Func<string, FileSystemEnumerable<string>.FindTransform> GetPathTransformFactory(GlobPathFormat pathFormat)
    {
        return pathFormat switch
        {
            GlobPathFormat.FullPath => static _ => static (ref entry) => entry.ToFullPath(),
            GlobPathFormat.RelativePath => static relativeRoot => (ref entry) => GetRelativePath(ref entry, relativeRoot),
            _ => throw new ArgumentOutOfRangeException(nameof(pathFormat), pathFormat, message: null),
        };
    }

    private static string GetRelativePath(ref FileSystemEntry entry, string relativeRoot)
    {
        var directory = Glob.GetRelativeDirectory(ref entry);
        var sb = new ValueStringBuilder(stackalloc char[256]);
        if (relativeRoot.Length > 0)
        {
            sb.Append(relativeRoot);
            if (!Path.EndsInDirectorySeparator(relativeRoot))
            {
                sb.Append(Path.DirectorySeparatorChar);
            }
        }

        if (!directory.IsEmpty)
        {
            sb.Append(directory);
            sb.Append(Path.DirectorySeparatorChar);
        }

        sb.Append(entry.FileName);
        return sb.ToString();
    }

    /// <param name="createTransform">Creates the transform of the entries of a root, from the path of the root relative to the directory.</param>
    private static IEnumerable<T> Enumerate<T>(IGlobEvaluatable glob, string directory, EnumerationOptions? options, bool includeDirectories, Func<string, FileSystemEnumerable<T>.FindTransform> createTransform)
    {
        if (!includeDirectories && !glob.CanMatchFiles)
            yield break;

        var roots = GetEnumerationRoots(glob, directory, options, out var mayOverlap);
        var returnedPaths = mayOverlap ? new HashSet<string>(PathComparer) : null;
        foreach (var root in roots)
        {
            using var enumerator = new GlobRootEnumerator<T>(root.Glob, root.Directory, root.Options, includeDirectories, root.MatchPrefix, createTransform(root.RelativeRoot), returnedPaths);
            while (enumerator.MoveNext())
                yield return enumerator.Current;
        }
    }

    private static List<EnumerationRoot> GetEnumerationRoots(IGlobEvaluatable glob, string directory, EnumerationOptions? options, out bool mayOverlap)
    {
        mayOverlap = false;
        var directoryRoot = new EnumerationRoot(directory, glob, MatchPrefix: null, RelativeRoot: "", options ?? GetDefaultEnumerationOptions(glob));
        if (glob is Glob singleGlob)
        {
            if (!singleGlob.TrySplitBasePath(out var basePath, out var relativeGlob))
                return [directoryRoot];

            // The relative pattern decides whether to go into subdirectories, but the whole pattern decides whether the
            // entries starting with a dot can match
            var rootOptions = options ?? GetDefaultEnumerationOptions(((IGlobEvaluatable)relativeGlob).TraverseDirectories, singleGlob.MatchLeadingDot);
            return TryGetBaseDirectory(directory, basePath, rootOptions, hasCustomOptions: options is not null, out var baseDirectory) switch
            {
                BaseDirectoryStatus.Found => [new EnumerationRoot(baseDirectory.Path, relativeGlob, MatchPrefix: null, baseDirectory.RelativeRoot, baseDirectory.Options)],
                BaseDirectoryStatus.Missing => [],
                _ => [directoryRoot],
            };
        }

        if (glob is GlobCollection collection)
            return GetEnumerationRoots(collection, directory, options, directoryRoot, out mayOverlap);

        return [directoryRoot];
    }

    private static List<EnumerationRoot> GetEnumerationRoots(GlobCollection collection, string directory, EnumerationOptions? options, EnumerationRoot directoryRoot, out bool mayOverlap)
    {
        mayOverlap = false;

        // An entry only matches a collection when one of its include patterns matches the entry or one of its
        // ancestors, so the enumeration only has to visit the base directories of the include patterns. An empty base
        // path stands for the directory itself.
        var basePaths = new List<string>();
        foreach (var item in collection)
        {
            if (item.Mode is not GlobMode.Include)
                continue;

            basePaths.Add(item is Glob glob && glob.TrySplitBasePath(out var basePath, out _) ? basePath : "");
        }

        // A base directory that is inside another one is visited from the other one, where the whole collection
        // still sees the same relative paths
        List<string> rootBasePaths = [.. basePaths.Distinct(StringComparer.Ordinal).Where(path => !basePaths.Exists(other => other != path && ContainsBasePath(other, path)))];
        if (rootBasePaths is [] or [""])
            return [directoryRoot];

        var roots = new List<EnumerationRoot>(rootBasePaths.Count);
        var rootOptions = options ?? directoryRoot.Options;
        foreach (var basePath in rootBasePaths)
        {
            if (basePath.Length == 0)
            {
                roots.Add(directoryRoot);
                continue;
            }

            switch (TryGetBaseDirectory(directory, basePath, rootOptions, hasCustomOptions: options is not null, out var baseDirectory))
            {
                case BaseDirectoryStatus.Found:
                    var matchPrefix = basePath[^1] == '/' ? basePath : basePath + '/';
                    roots.Add(new EnumerationRoot(baseDirectory.Path, collection, matchPrefix, baseDirectory.RelativeRoot, baseDirectory.Options));
                    break;

                case BaseDirectoryStatus.Missing:
                    break;

                default:
                    return [directoryRoot];
            }
        }

        // A base path that goes up from the directory or that is absolute can lead to a directory that another root
        // also visits, under another relative path
        for (var i = 0; i < roots.Count && !mayOverlap; i++)
        {
            for (var j = 0; j < roots.Count; j++)
            {
                if (i != j && ContainsDirectory(Path.GetFullPath(roots[i].Directory), Path.GetFullPath(roots[j].Directory)))
                {
                    mayOverlap = true;
                    break;
                }
            }
        }

        return roots;

        static bool ContainsBasePath(string basePath, string other)
        {
            // The enumeration of the directory never goes up, so it does not contain a base path that leaves it
            if (basePath.Length == 0)
                return !IsOutsideDirectory(other);

            if (basePath[^1] == '/')
                return other.StartsWith(basePath, StringComparison.Ordinal);

            return other.Length > basePath.Length && other[basePath.Length] == '/' && other.StartsWith(basePath, StringComparison.Ordinal);
        }

        static bool ContainsDirectory(string directory, string other)
        {
            directory = Path.TrimEndingDirectorySeparator(directory);
            other = Path.TrimEndingDirectorySeparator(other);
            if (!other.StartsWith(directory, PathComparison))
                return false;

            return other.Length == directory.Length || Path.EndsInDirectorySeparator(directory) || PathReader.IsPathSeparator(other[directory.Length]);
        }
    }

    private static bool IsOutsideDirectory(string basePath)
    {
        return Path.IsPathRooted(basePath) || basePath is ".." || basePath.StartsWith("../", StringComparison.Ordinal);
    }

    /// <summary>Finds the directory where the enumeration of a pattern with a base path starts.</summary>
    /// <returns>
    ///     <see cref="BaseDirectoryStatus.Fallback"/> when the enumeration must start at <paramref name="directory"/>
    ///     instead to give the same result, such as when a directory of the base path is a symbolic link, which the
    ///     enumeration does not follow.
    /// </returns>
    private static BaseDirectoryStatus TryGetBaseDirectory(string directory, string basePath, EnumerationOptions options, bool hasCustomOptions, out BaseDirectory result)
    {
        result = default;
        var baseDirectory = Path.Combine(directory, basePath);
        if (IsOutsideDirectory(basePath))
        {
            if (!Directory.Exists(baseDirectory))
                return GetMissingStatus(directory);
        }
        else
        {
            // Opening the base directory directly must not return an entry that the enumeration of the directory skips
            var depth = 0;
            var currentDirectory = directory;
            foreach (var name in basePath.Split('/'))
            {
                depth++;
                currentDirectory = Path.Join(currentDirectory, name);

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(currentDirectory);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    return GetMissingStatus(directory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return BaseDirectoryStatus.Fallback;
                }

                if (!attributes.HasFlag(FileAttributes.Directory))
                    return GetMissingStatus(directory);

                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    return BaseDirectoryStatus.Fallback;

                if ((attributes & options.AttributesToSkip) is not FileAttributes.None)
                    return BaseDirectoryStatus.Missing;
            }

            // MaxRecursionDepth counts the directories from the directory, not from the base directory
            if (hasCustomOptions)
            {
                var maxDepth = options.RecurseSubdirectories ? options.MaxRecursionDepth : 0;
                if (depth > maxDepth)
                    return BaseDirectoryStatus.Missing;

                if (maxDepth != int.MaxValue)
                {
                    options = CloneOptions(options, maxDepth - depth);
                }
            }
        }

        var relativeRoot = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(baseDirectory));
        result = new BaseDirectory(baseDirectory, relativeRoot is "." ? "" : relativeRoot, options);
        return BaseDirectoryStatus.Found;

        // Enumerating a directory that does not exist throws, so keep that behavior
        static BaseDirectoryStatus GetMissingStatus(string directory) => Directory.Exists(directory) ? BaseDirectoryStatus.Missing : BaseDirectoryStatus.Fallback;
    }

    private static EnumerationOptions CloneOptions(EnumerationOptions options, int maxRecursionDepth)
    {
        return new EnumerationOptions
        {
            AttributesToSkip = options.AttributesToSkip,
            BufferSize = options.BufferSize,
            IgnoreInaccessible = options.IgnoreInaccessible,
            MatchCasing = options.MatchCasing,
            MatchType = options.MatchType,
            MaxRecursionDepth = maxRecursionDepth,
            RecurseSubdirectories = options.RecurseSubdirectories,
            ReturnSpecialDirectories = options.ReturnSpecialDirectories,
        };
    }

    private static EnumerationOptions GetDefaultEnumerationOptions(IGlobEvaluatable glob)
    {
        return GetDefaultEnumerationOptions(glob.TraverseDirectories, MatchLeadingDot(glob));
    }

    private static EnumerationOptions GetDefaultEnumerationOptions(bool traverseDirectories, bool matchLeadingDot)
    {
        return (traverseDirectories, matchLeadingDot) switch
        {
            (true, true) => DefaultRecursiveEnumerationOptionsIncludingHidden,
            (true, false) => DefaultRecursiveEnumerationOptions,
            (false, true) => DefaultEnumerationOptionsIncludingHidden,
            (false, false) => DefaultEnumerationOptions,
        };
    }

    private static bool MatchLeadingDot(IGlobEvaluatable glob)
    {
        return glob is Glob { MatchLeadingDot: true } or GlobCollection { MatchLeadingDot: true };
    }

    private enum BaseDirectoryStatus
    {
        Found,
        Missing,
        Fallback,
    }

    /// <param name="Directory">The directory where the enumeration starts.</param>
    /// <param name="Glob">The pattern that the entries of the directory must match.</param>
    /// <param name="MatchPrefix">The path of <paramref name="Directory"/> that <paramref name="Glob"/> matches, ending with a '/', when it is not the root of the paths it matches.</param>
    /// <param name="RelativeRoot">The path of <paramref name="Directory"/> relative to the enumerated directory.</param>
    /// <param name="Options">The enumeration options.</param>
    private readonly record struct EnumerationRoot(string Directory, IGlobEvaluatable Glob, string? MatchPrefix, string RelativeRoot, EnumerationOptions Options);

    private readonly record struct BaseDirectory(string Path, string RelativeRoot, EnumerationOptions Options);
}
