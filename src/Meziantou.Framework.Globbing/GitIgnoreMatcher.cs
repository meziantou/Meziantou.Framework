using System.Collections.Concurrent;
using System.IO.Enumeration;
using Meziantou.Framework.Globbing.Internals;

namespace Meziantou.Framework.Globbing;

/// <summary>
///     Determines which paths of a git working tree are ignored, as <c>git status</c> and <c>git check-ignore</c> do, and
///     enumerates the files that are not ignored.
/// </summary>
/// <remarks>
///     <para>
///         The patterns come from the <c>.gitignore</c> files of the working tree, the <c>info/exclude</c> file of the
///         repository and the global excludes file, with the precedence git gives them: the <c>.gitignore</c> file of a
///         directory takes precedence over the ones of its parent directories, which take precedence over
///         <c>info/exclude</c>, which takes precedence over the global excludes file.
///     </para>
///     <para>
///         A <c>.gitignore</c> file is read the first time a path of its directory is evaluated, then cached for the
///         lifetime of the instance. Create a new instance to read the files again. The instance can be used from
///         several threads.
///     </para>
///     <para>
///         Only the ignore rules are evaluated: a file that is ignored but already tracked by git is still reported as
///         ignored, and the <c>.git</c> entries are never returned by <see cref="EnumerateFiles(EnumerationOptions?)"/>.
///     </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Globbing.Tests/GitIgnoreMatcherTests.cs" ref:region="GitIgnoreMatcher_EnumerateFiles" ref:dedent="true">
/// using var rootDirectory = TemporaryDirectory.Create();
/// rootDirectory.CreateTextFile(".gitignore", "*.log\n");
/// rootDirectory.CreateTextFile("src/.gitignore", "bin/\n!important.log\n");
/// rootDirectory.CreateEmptyFile("src/Program.cs");
/// rootDirectory.CreateEmptyFile("src/bin/App.dll");
/// rootDirectory.CreateEmptyFile("src/trace.log");
/// rootDirectory.CreateEmptyFile("src/important.log");
///
/// var matcher = new GitIgnoreMatcher(rootDirectory.FullPath);
///
/// // The .gitignore file of a directory applies to its content, and takes precedence over the parent ones
/// Assert.True(matcher.IsIgnored("src/trace.log"));
/// Assert.False(matcher.IsIgnored("src/important.log"));
///
/// // Enumerate the files that are not ignored. The enumeration does not go into src/bin.
/// var files = matcher.EnumerateFiles()
///     .Select(path =&gt; Path.GetRelativePath(rootDirectory.FullPath, path).Replace('\\', '/'))
///     .Order(StringComparer.Ordinal)
///     .ToList();
///
/// Assert.Equal([".gitignore", "src/.gitignore", "src/Program.cs", "src/important.log"], files);
/// </code>
/// </example>
public sealed class GitIgnoreMatcher
{
    private const string GitIgnoreFileName = ".gitignore";

    private static readonly char[] DirectorySeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
    private static readonly EnumerationOptions DefaultEnumerationOptions = new() { RecurseSubdirectories = true, AttributesToSkip = default(FileAttributes) };

    private readonly ConcurrentDictionary<string, GlobCollection?> _gitIgnoreFiles = new(StringComparer.Ordinal);

    // The patterns that apply to the whole working tree, from the lowest precedence to the highest
    private readonly GlobCollection[] _excludeFiles;

    /// <summary>Initializes a new instance of the <see cref="GitIgnoreMatcher"/> class.</summary>
    /// <param name="rootDirectory">The root directory of the git working tree.</param>
    /// <param name="options">The options, or <see langword="null"/> to use the default ones.</param>
    public GitIgnoreMatcher(string rootDirectory, GitIgnoreMatcherOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        options ??= new GitIgnoreMatcherOptions();

        var excludeFiles = new List<GlobCollection>();
        if (options.GlobalExcludesFile is not null && TryLoadGitIgnoreFile(options.GlobalExcludesFile, baseDirectory: "") is { } globalExcludes)
        {
            excludeFiles.Add(globalExcludes);
        }

        if (options.UseRepositoryExcludeFile && GetCommonGitDirectory(RootDirectory) is { } gitDirectory && TryLoadGitIgnoreFile(Path.Join(gitDirectory, "info", "exclude"), baseDirectory: "") is { } repositoryExcludes)
        {
            excludeFiles.Add(repositoryExcludes);
        }

        _excludeFiles = [.. excludeFiles];
    }

    /// <summary>Gets the full path of the root directory of the git working tree.</summary>
    public string RootDirectory { get; }

    /// <summary>Determines whether a path of the working tree is ignored.</summary>
    /// <param name="path">
    ///     The path relative to <see cref="RootDirectory"/>. A path ending with a directory separator is a directory;
    ///     otherwise, the file system tells whether the path is a directory.
    /// </param>
    /// <returns><see langword="true"/> if git ignores the path; otherwise, <see langword="false"/>.</returns>
    public bool IsIgnored(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var itemType = (path.Length > 0 && PathReader.IsPathSeparator(path[^1])) || Directory.Exists(Path.Join(RootDirectory, path))
            ? PathItemType.Directory
            : PathItemType.File;
        return IsIgnored(path.AsSpan(), itemType);
    }

    /// <summary>Determines whether a path of the working tree is ignored.</summary>
    /// <param name="path">The path relative to <see cref="RootDirectory"/>.</param>
    /// <param name="itemType">The type of the item.</param>
    /// <returns>
    ///     <see langword="true"/> if git ignores the path, or one of its parent directories; otherwise,
    ///     <see langword="false"/>.
    /// </returns>
    public bool IsIgnored(ReadOnlySpan<char> path, PathItemType itemType)
    {
        path = path.TrimEnd(DirectorySeparators.AsSpan());
        if (path.IsEmpty)
            return false;

        // Walk the ancestors from the root: an excluded directory excludes everything below it, and git does not even
        // read the .gitignore files it holds
        var patterns = GetPatterns([]);
        var index = 0;
        while (true)
        {
            var separatorIndex = path[index..].IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (separatorIndex < 0)
                break;

            var directory = path[..(index + separatorIndex)];
            if (IsExcluded(patterns, directory, [], PathItemType.Directory))
                return true;

            patterns = AddGitIgnoreFile(patterns, directory);
            index += separatorIndex + 1;
        }

        return IsExcluded(patterns, path, [], itemType);
    }

    /// <summary>Enumerates the files of the working tree that are not ignored.</summary>
    /// <param name="options">
    ///     Options for controlling the enumeration, or <see langword="null"/> to enumerate every subdirectory, including
    ///     the hidden entries.
    /// </param>
    /// <returns>The full paths of the files that git does not ignore.</returns>
    /// <remarks>
    ///     The enumeration does not go into the ignored directories, the <c>.git</c> directories, and the symbolic links
    ///     to a directory, which are returned as files as git does. It returns the files that git lists with
    ///     <c>git ls-files --cached --others --exclude-standard</c>, except the ignored files that are tracked.
    /// </remarks>
    public IEnumerable<string> EnumerateFiles(EnumerationOptions? options = null)
    {
        using var enumerator = new NotIgnoredFileEnumerator(this, options ?? DefaultEnumerationOptions);
        while (enumerator.MoveNext())
            yield return enumerator.Current;
    }

    // The patterns that apply to the entries of the directory, from the lowest precedence to the highest
    private GlobCollection[] GetPatterns(ReadOnlySpan<char> relativeDirectory)
    {
        var patterns = AddGitIgnoreFile(_excludeFiles, []);
        var index = 0;
        while (index < relativeDirectory.Length)
        {
            var separatorIndex = relativeDirectory[index..].IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var end = separatorIndex < 0 ? relativeDirectory.Length : index + separatorIndex;
            patterns = AddGitIgnoreFile(patterns, relativeDirectory[..end]);
            index = end + 1;
        }

        return patterns;
    }

    private GlobCollection[] AddGitIgnoreFile(GlobCollection[] patterns, ReadOnlySpan<char> relativeDirectory)
    {
        var gitIgnore = GetGitIgnoreFile(relativeDirectory);
        return gitIgnore is null ? patterns : [.. patterns, gitIgnore];
    }

    private GlobCollection? GetGitIgnoreFile(ReadOnlySpan<char> relativeDirectory)
    {
        var key = relativeDirectory.ToString();
        if (Path.DirectorySeparatorChar != '/')
        {
            key = key.Replace(Path.DirectorySeparatorChar, '/');
        }

        return _gitIgnoreFiles.GetOrAdd(key, (directory, root) => TryLoadGitIgnoreFile(Path.Join(root, directory, GitIgnoreFileName), directory), RootDirectory);
    }

    // The pattern with the highest precedence that matches the path decides
    private static bool IsExcluded(GlobCollection[] patterns, ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType itemType)
    {
        for (var i = patterns.Length - 1; i >= 0; i--)
        {
            var match = patterns[i].GetLastMatch(directory, filename, itemType);
            if (match is not null)
                return match is GlobMode.Include;
        }

        return false;
    }

    private static GlobCollection? TryLoadGitIgnoreFile(string path, string baseDirectory)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            return GlobCollection.LoadGitIgnore(path, baseDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // info/exclude belongs to the repository, which is shared by the worktrees: '.git' is then a file that points to
    // the git directory of the worktree, whose 'commondir' file points to the git directory of the repository
    private static string? GetCommonGitDirectory(string rootDirectory)
    {
        try
        {
            var gitDirectory = Path.Join(rootDirectory, ".git");
            if (!Directory.Exists(gitDirectory))
            {
                if (!File.Exists(gitDirectory))
                    return null;

                const string Prefix = "gitdir:";
                var content = File.ReadAllText(gitDirectory).AsSpan().Trim();
                if (!content.StartsWith(Prefix, StringComparison.Ordinal))
                    return null;

                gitDirectory = Path.GetFullPath(content[Prefix.Length..].Trim().ToString(), rootDirectory);
            }

            var commonDirectoryFile = Path.Join(gitDirectory, "commondir");
            if (File.Exists(commonDirectoryFile))
                return Path.GetFullPath(File.ReadAllText(commonDirectoryFile).Trim(), gitDirectory);

            return gitDirectory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private sealed class NotIgnoredFileEnumerator : FileSystemEnumerator<string>
    {
        private readonly GitIgnoreMatcher _matcher;

        // The patterns of the parent of each directory that is waiting to be enumerated, so a directory does not walk
        // its ancestors again
        private readonly Dictionary<string, GlobCollection[]> _pendingDirectories = new(StringComparer.Ordinal);
        private string? _currentDirectory;
        private GlobCollection[] _currentPatterns = [];

        public NotIgnoredFileEnumerator(GitIgnoreMatcher matcher, EnumerationOptions options)
            : base(matcher.RootDirectory, options)
        {
            _matcher = matcher;
        }

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
        {
            // git does not follow symbolic links
            if (IsGitDirectory(ref entry) || IsSymbolicLink(ref entry))
                return false;

            var patterns = GetCurrentPatterns(ref entry);
            if (IsExcluded(patterns, Glob.GetRelativeDirectory(ref entry), entry.FileName, PathItemType.Directory))
                return false;

            _pendingDirectories[entry.ToFullPath()] = patterns;
            return true;
        }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
        {
            if (IsGitDirectory(ref entry))
                return false;

            if (entry.IsDirectory && !IsSymbolicLink(ref entry))
                return false;

            return !IsExcluded(GetCurrentPatterns(ref entry), Glob.GetRelativeDirectory(ref entry), entry.FileName, PathItemType.File);
        }

        protected override string TransformEntry(ref FileSystemEntry entry) => entry.ToFullPath();

        private GlobCollection[] GetCurrentPatterns(ref FileSystemEntry entry)
        {
            var directory = entry.Directory;
            if (_currentDirectory is null || !directory.SequenceEqual(_currentDirectory))
            {
                _currentDirectory = directory.ToString();
                var relativeDirectory = Glob.GetRelativeDirectory(ref entry);
                if (relativeDirectory.IsEmpty)
                {
                    _currentPatterns = _matcher.GetPatterns([]);
                }
                else if (_pendingDirectories.Remove(_currentDirectory, out var parentPatterns))
                {
                    _currentPatterns = _matcher.AddGitIgnoreFile(parentPatterns, relativeDirectory);
                }
                else
                {
                    _currentPatterns = _matcher.GetPatterns(relativeDirectory);
                }
            }

            return _currentPatterns;
        }

        private static bool IsGitDirectory(ref FileSystemEntry entry) => entry.FileName is ".git";

        private static bool IsSymbolicLink(ref FileSystemEntry entry) => entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }
}
