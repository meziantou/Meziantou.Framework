using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using Meziantou.Framework.Globbing.Internals;

namespace Meziantou.Framework.Globbing;

/// <summary>Represents a collection of glob patterns that can be evaluated together.</summary>
/// <example>
/// Combine multiple glob patterns with include and exclude rules:
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../samples/Meziantou.Framework.Globbing.Samples/Examples.cs" ref:region="GlobCollection_EnumerateFiles" ref:dedent="true">
/// var globs = new GlobCollection(
///     Glob.Parse("**/*.txt", GlobDialect.Standard),
///     Glob.Parse("!temp/**/*", GlobDialect.Standard));
///
/// foreach (var file in globs.EnumerateFiles("C:/MyProject"))
/// {
///     Console.WriteLine(file);
/// }
/// </code>
/// </example>
[System.Runtime.CompilerServices.CollectionBuilder(typeof(GlobCollection), nameof(Create))]
public sealed class GlobCollection : IReadOnlyList<IGlobEvaluatable>, IGlobEvaluatable
{
    private static readonly char[] DirectorySeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private readonly IGlobEvaluatable[] _globs;

    // gitignore resolves a path against the last pattern that matches it, whereas a hand-built collection uses
    // the order-independent "any exclude wins" rule. Only the gitignore factories set this.
    private readonly bool _lastMatchWins;

    // The directory, relative to the root of the paths given to the collection, that holds the gitignore content.
    // Its rules only apply to the paths below it, matched relative to it. Empty for the root.
    private readonly string _baseDirectory = "";

    // Only set for gitignore content: finds the last matching rule without evaluating every rule
    private readonly GitIgnoreRuleIndex? _ruleIndex;

    private ExclusionInfo? _exclusionInfo;

    /// <summary>Initializes a new instance of the <see cref="GlobCollection"/> class.</summary>
    /// <param name="globs">The glob patterns to include in the collection. The collection keeps a copy of the array.</param>
    /// <exception cref="ArgumentNullException"><paramref name="globs"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="globs"/> contains a <see langword="null"/> element.</exception>
    public GlobCollection(params IGlobEvaluatable[] globs)
    {
        ArgumentNullException.ThrowIfNull(globs);

        // Copy the array so that the collection stays immutable when the caller changes it
        _globs = [.. globs];
        if (Array.Exists(_globs, glob => glob is null))
            throw new ArgumentException("The collection cannot contain a null glob.", nameof(globs));
    }

    private GlobCollection(Glob[] gitIgnoreRules, string baseDirectory)
    {
        _globs = gitIgnoreRules;
        _lastMatchWins = true;
        _baseDirectory = baseDirectory;
        _ruleIndex = new GitIgnoreRuleIndex(gitIgnoreRules);
    }

    /// <summary>Loads gitignore content and creates a <see cref="GlobCollection"/> from it.</summary>
    /// <param name="gitIgnoreContent">The gitignore content.</param>
    /// <remarks>
    ///     As git does, an entry that no path can match, such as one holding an unterminated bracket expression or
    ///     ending with an escape character, is ignored instead of making the whole content invalid.
    /// </remarks>
    public static GlobCollection ParseGitIgnore(ReadOnlySpan<char> gitIgnoreContent) => ParseGitIgnore(gitIgnoreContent, baseDirectory: "", skipByteOrderMark: true);

    /// <summary>Loads the content of a gitignore file located in a subdirectory and creates a <see cref="GlobCollection"/> from it.</summary>
    /// <param name="gitIgnoreContent">The gitignore content.</param>
    /// <param name="baseDirectory">
    ///     The directory that holds the gitignore file, relative to the root of the paths given to the collection, such
    ///     as <c>src/App</c>. <see langword="null"/>, an empty string or <c>.</c> stand for the root.
    /// </param>
    /// <remarks>
    ///     The rules only apply to the paths below <paramref name="baseDirectory"/>, and they are matched relative to it,
    ///     as git does for the <c>.gitignore</c> file of a subdirectory. The collection does not match the other paths,
    ///     nor <paramref name="baseDirectory"/> itself.
    /// </remarks>
    public static GlobCollection ParseGitIgnore(ReadOnlySpan<char> gitIgnoreContent, string? baseDirectory) => ParseGitIgnore(gitIgnoreContent, NormalizeBaseDirectory(baseDirectory), skipByteOrderMark: true);

    private static GlobCollection ParseGitIgnore(ReadOnlySpan<char> gitIgnoreContent, string baseDirectory, bool skipByteOrderMark)
    {
        // git skips a UTF-8 byte order mark at the start of the file, but only one: a U+FEFF that follows it belongs to
        // the first entry
        if (skipByteOrderMark && !gitIgnoreContent.IsEmpty && gitIgnoreContent[0] == '\uFEFF')
        {
            gitIgnoreContent = gitIgnoreContent[1..];
        }

        // git only breaks lines on LF and removes the CR of a CRLF. A lone CR, U+0085, U+2028 and U+2029 are legal
        // characters in a file name, so they belong to the entry.
        var globs = new List<Glob>();
        while (!gitIgnoreContent.IsEmpty)
        {
            var index = gitIgnoreContent.IndexOf('\n');
            var line = index < 0 ? gitIgnoreContent : gitIgnoreContent[..index];
            if (index >= 0 && !line.IsEmpty && line[^1] == '\r')
            {
                line = line[..^1];
            }

            AddGitIgnoreLine(line, globs);
            gitIgnoreContent = index < 0 ? [] : gitIgnoreContent[(index + 1)..];
        }

        return new GlobCollection([.. globs], baseDirectory);
    }

    /// <summary>Loads a gitignore file asynchronously and creates a <see cref="GlobCollection"/> from it.</summary>
    /// <param name="path">The path to the gitignore file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>The file is decoded as <see cref="LoadGitIgnoreAsync(Stream, CancellationToken)"/> decodes a stream.</remarks>
    public static Task<GlobCollection> LoadGitIgnoreAsync(string path, CancellationToken cancellationToken = default) => LoadGitIgnoreAsync(path, baseDirectory: null, cancellationToken);

    /// <summary>Loads a gitignore file located in a subdirectory asynchronously and creates a <see cref="GlobCollection"/> from it.</summary>
    /// <param name="path">The path to the gitignore file.</param>
    /// <param name="baseDirectory">
    ///     The directory that holds the gitignore file, relative to the root of the paths given to the collection, such
    ///     as <c>src/App</c>. <see langword="null"/>, an empty string or <c>.</c> stand for the root.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    ///     The file is decoded as <see cref="LoadGitIgnoreAsync(Stream, CancellationToken)"/> decodes a stream. The rules
    ///     apply as described in <see cref="ParseGitIgnore(ReadOnlySpan{char}, string?)"/>.
    /// </remarks>
    public static async Task<GlobCollection> LoadGitIgnoreAsync(string path, string? baseDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var normalizedBaseDirectory = NormalizeBaseDirectory(baseDirectory);
        await using var stream = File.OpenRead(path);
        using var reader = CreateGitIgnoreReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return ParseGitIgnore(content.AsSpan(), normalizedBaseDirectory, skipByteOrderMark: false);
    }

    internal static GlobCollection LoadGitIgnore(string path, string baseDirectory)
    {
        using var stream = File.OpenRead(path);
        using var reader = CreateGitIgnoreReader(stream);
        return ParseGitIgnore(reader.ReadToEnd().AsSpan(), baseDirectory, skipByteOrderMark: false);
    }

    private static StreamReader CreateGitIgnoreReader(Stream stream)
    {
        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
    }

    // Base directories are compared segment by segment, so only keep the segments: "./src/App/" is "src/App"
    internal static string NormalizeBaseDirectory(string? baseDirectory)
    {
        if (string.IsNullOrEmpty(baseDirectory))
            return "";

        var result = baseDirectory.AsSpan().Trim(DirectorySeparators.AsSpan());
        while (result.StartsWith(".") && (result.Length == 1 || PathReader.IsPathSeparator(result[1])))
        {
            result = result[1..].TrimStart(DirectorySeparators.AsSpan());
        }

        return result.ToString();
    }

    /// <summary>Loads a gitignore stream asynchronously and creates a <see cref="GlobCollection"/> from it.</summary>
    /// <param name="stream">The stream containing the gitignore content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    ///     The content is decoded as UTF-8, unless it starts with the byte order mark of another encoding (UTF-16 or
    ///     UTF-32). As git does, only that first byte order mark is skipped.
    /// </remarks>
    public static async Task<GlobCollection> LoadGitIgnoreAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = CreateGitIgnoreReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        // The reader already skipped the byte order mark, so a U+FEFF at the start of the text is a second one
        return ParseGitIgnore(content.AsSpan(), baseDirectory: "", skipByteOrderMark: false);
    }

    /// <summary>Loads a gitignore reader asynchronously and creates a <see cref="GlobCollection"/> from it.</summary>
    /// <param name="reader">The reader providing the gitignore content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<GlobCollection> LoadGitIgnoreAsync(TextReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        // TextReader.ReadLine also breaks on a lone CR, which git reads as part of the entry
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return ParseGitIgnore(content.AsSpan());
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static GlobCollection Create(ReadOnlySpan<IGlobEvaluatable> globs) => new(globs.ToArray());

    private static void AddGitIgnoreLine(ReadOnlySpan<char> line, List<Glob> globs)
    {
        if (line.IsEmpty)
            return;

        line = TrimGitIgnoreLineEnd(line);
        if (line.IsEmpty)
            return;

        if (line[0] == '#')
            return;

        // git accepts any entry, and one that is not a valid pattern simply never matches. Such an entry has no
        // effect on the last-match-wins resolution, so dropping it is equivalent.
        if (Glob.TryParseGitIgnoreEntry(line, out var glob))
        {
            globs.Add(glob);
        }
    }

    private static ReadOnlySpan<char> TrimGitIgnoreLineEnd(ReadOnlySpan<char> line)
    {
        // git only drops trailing spaces. A trailing tab is part of the pattern.
        var end = line.Length;
        while (end > 0)
        {
            var c = line[end - 1];
            if (c is not ' ')
                break;

            var backslashCount = 0;
            var index = end - 2;
            while (index >= 0 && line[index] == '\\')
            {
                backslashCount++;
                index--;
            }

            if (backslashCount % 2 == 1)
                break;

            end--;
        }

        return line[..end];
    }

    GlobMode IGlobEvaluatable.Mode => (_globs.Length == 0 || _globs.Any(g => g.Mode is GlobMode.Include)) ? GlobMode.Include : GlobMode.Exclude;
    bool IGlobEvaluatable.CanMatchFiles => _globs.Any(g => g.Mode is GlobMode.Include && g.CanMatchFiles);
    bool IGlobEvaluatable.CanMatchDirectories => _globs.Any(g => g.Mode is GlobMode.Include && g.CanMatchDirectories);
    // gitignore content matches everything below an excluded directory, whatever the depth of the pattern
    bool IGlobEvaluatable.TraverseDirectories => _globs.Any(g => g.Mode is GlobMode.Include && (_lastMatchWins || ((IGlobEvaluatable)g).TraverseDirectories));
    internal bool MatchLeadingDot => _globs.Any(static g => g is Glob { MatchLeadingDot: true } or GlobCollection { MatchLeadingDot: true });

    /// <summary>Gets the number of glob patterns in the collection.</summary>
    public int Count => _globs.Length;

    /// <summary>Gets the glob pattern at the specified index.</summary>
    /// <param name="index">The zero-based index of the glob pattern to get.</param>
    public IGlobEvaluatable this[int index] => _globs[index];

    /// <summary>Determines whether the specified path matches any pattern in the collection.</summary>
    /// <param name="directory">The directory part of the path to match.</param>
    /// <param name="filename">The filename part of the path to match.</param>
    /// <param name="itemType">The type of the path item (file or directory), or <see langword="null"/> if unknown.</param>
    /// <returns>
    ///     <see langword="true"/> if the path matches any include pattern and no exclude pattern; otherwise,
    ///     <see langword="false"/>. A collection created from gitignore content resolves the path against the last
    ///     pattern that matches it instead, as git does, and reports a path as matched when one of its ancestor
    ///     directories is: git cannot re-include a path whose parent directory is excluded.
    /// </returns>
    public bool IsMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType)
    {
        if (_lastMatchWins)
            return IsLastMatch(directory, filename, itemType);

        var match = false;
        foreach (var glob in _globs)
        {
            if (match && glob.Mode is GlobMode.Include)
                continue;

            if (glob.IsMatch(directory, filename, itemType))
            {
                if (glob.Mode is GlobMode.Exclude)
                    return false;

                match = true;
            }
        }

        return match;
    }

    private bool IsLastMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType)
    {
        if (GetRelativePath(ref directory, ref filename) is not BaseDirectoryRelation.Inside)
            return false;

        // git cannot re-include a path whose parent directory is excluded, so an excluded ancestor decides on its
        // own and the patterns written for the path itself are not even looked at.
        if (HasExcludedAncestor(directory, filename))
            return true;

        return GetLastMatchCore(directory, filename, itemType) is GlobMode.Include;
    }

    // The last pattern that matches decides. The index only evaluates the patterns that can match the path.
    private GlobMode? GetLastMatchCore(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType)
    {
        Debug.Assert(_ruleIndex is not null);
        return _ruleIndex.GetLastMatch(directory, filename, itemType);
    }

    private bool HasExcludedAncestor(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename)
    {
        directory = directory.TrimEnd(DirectorySeparators.AsSpan());

        var length = directory.Length;
        if (filename.IsEmpty)
        {
            // The whole path is in 'directory', so its last segment is the item itself, not one of its ancestors.
            length = directory.LastIndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (length < 0)
                return false;
        }

        // Walk the ancestors from the root: the first excluded one decides, so the ones below it are never reached
        // and their own patterns cannot re-include anything.
        var index = 0;
        while (index < length)
        {
            var separatorIndex = directory[index..length].IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var end = separatorIndex < 0 ? length : index + separatorIndex;
            if (GetLastMatchCore(directory[..end], [], PathItemType.Directory) is GlobMode.Include)
                return true;

            if (separatorIndex < 0)
                break;

            index = end + 1;
        }

        return false;
    }

    /// <summary>Determines whether a directory should be recursed into when enumerating files.</summary>
    /// <param name="folderPath">The folder path to check.</param>
    /// <param name="filename">The filename part of the path to check.</param>
    /// <returns>
    ///     <see langword="true"/> if the directory could contain matches; otherwise, <see langword="false"/>. A directory
    ///     whose whole content is excluded by an exclude pattern, such as <c>!**/node_modules/**</c>, cannot contain
    ///     matches. For a collection created from gitignore content, a directory can contain matches when it is
    ///     excluded, or when a pattern can exclude a path below it.
    /// </returns>
    public bool IsPartialMatch(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename)
    {
        if (_lastMatchWins)
            return IsGitIgnorePartialMatch(folderPath, filename, directoryIgnored: null);

        var include = false;
        foreach (var glob in _globs)
        {
            if (glob.Mode is GlobMode.Include && glob.IsPartialMatch(folderPath, filename))
            {
                include = true;
                break;
            }
        }

        return include && !IsDirectoryContentExcluded(folderPath, filename);
    }

    // An exclude pattern always wins in a collection that is not created from gitignore content, so a directory whose
    // whole content is excluded cannot hold a match
    private bool IsDirectoryContentExcluded(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename)
    {
        var info = _exclusionInfo ??= ExclusionInfo.Create(_globs);
        foreach (var exclude in info.Excludes)
        {
            // The pattern must exclude every kind of item the include patterns can match
            if (info.IncludesCanMatchFiles && exclude.MatchType is GlobMatchType.Directory)
                continue;

            if (info.IncludesCanMatchDirectories && exclude.MatchType is GlobMatchType.File)
                continue;

            var match = exclude.MatchesDirectoryContent(folderPath, filename);
            if (match is DirectoryContentMatch.All)
                return true;

            // The paths that the exclude pattern does not match cannot be matched by an include pattern either
            if (match is DirectoryContentMatch.AllExceptLeadingDot && !info.IncludesCanMatchLeadingDot)
                return true;
        }

        return false;
    }

    internal bool IsGitIgnore => _lastMatchWins;

    /// <summary>
    ///     Returns the mode of the last gitignore pattern that matches the path itself, or <see langword="null"/> when no
    ///     pattern matches it or the path is not below the base directory. Unlike <see cref="IsMatch(ReadOnlySpan{char}, ReadOnlySpan{char}, PathItemType?)"/>,
    ///     the ancestor directories are not evaluated: the caller already knows whether one of them is excluded.
    /// </summary>
    internal GlobMode? GetLastMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType)
    {
        Debug.Assert(_lastMatchWins);
        if (GetRelativePath(ref directory, ref filename) is not BaseDirectoryRelation.Inside)
            return null;

        return GetLastMatchCore(directory, filename, itemType);
    }

    /// <summary>Determines whether a directory can contain a path that the gitignore content excludes.</summary>
    /// <param name="folderPath">The folder path to check.</param>
    /// <param name="filename">The filename part of the path to check.</param>
    /// <param name="directoryIgnored">Whether the directory, or one of its ancestors, is excluded, when the caller already knows it.</param>
    internal bool IsGitIgnorePartialMatch(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename, bool? directoryIgnored)
    {
        Debug.Assert(_lastMatchWins);
        switch (GetRelativePath(ref folderPath, ref filename))
        {
            case BaseDirectoryRelation.Outside:
                return false;

            case BaseDirectoryRelation.Ancestor:
                return _globs.Any(glob => glob.Mode is GlobMode.Include);

            case BaseDirectoryRelation.BaseDirectory:
                folderPath = [];
                filename = [];
                directoryIgnored = false;
                break;
        }

        // Everything below an excluded directory is excluded
        directoryIgnored ??= HasExcludedAncestor(folderPath, filename) || GetLastMatchCore(folderPath, filename, PathItemType.Directory) is GlobMode.Include;
        if (directoryIgnored.Value)
            return true;

        // A negated pattern that matches the whole content of the directory decides for every path below it, unless a
        // later pattern matches the path too. So only the patterns written after it can exclude something.
        var first = 0;
        for (var i = _globs.Length - 1; i >= 0; i--)
        {
            if (_globs[i] is Glob { Mode: GlobMode.Exclude } negation && negation.MatchesDirectoryContent(folderPath, filename) is DirectoryContentMatch.All)
            {
                first = i + 1;
                break;
            }
        }

        for (var i = first; i < _globs.Length; i++)
        {
            var glob = _globs[i];
            if (glob.Mode is GlobMode.Include && glob.IsPartialMatch(folderPath, filename))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Makes the path relative to the base directory of the gitignore content. Only a path below the base directory
    ///     is <see cref="BaseDirectoryRelation.Inside"/>: the patterns of the gitignore file of a directory do not apply
    ///     to the directory itself.
    /// </summary>
    private BaseDirectoryRelation GetRelativePath(ref ReadOnlySpan<char> directory, ref ReadOnlySpan<char> filename)
    {
        if (_baseDirectory.Length == 0)
            return BaseDirectoryRelation.Inside;

        var baseDirectory = _baseDirectory.AsSpan();
        if (StartsWithPath(directory, baseDirectory) && (directory.Length == baseDirectory.Length || PathReader.IsPathSeparator(directory[baseDirectory.Length])))
        {
            var rest = directory[baseDirectory.Length..];
            if (!rest.IsEmpty)
            {
                rest = rest[1..];
            }

            if (rest.IsEmpty && filename.IsEmpty)
                return BaseDirectoryRelation.BaseDirectory;

            directory = rest;
            return BaseDirectoryRelation.Inside;
        }

        // The path may be an ancestor of the base directory, or the base directory given as a directory and a name
        var parent = directory.TrimEnd(DirectorySeparators.AsSpan());
        if (parent.IsEmpty || (baseDirectory.Length > parent.Length && StartsWithPath(baseDirectory, parent) && PathReader.IsPathSeparator(baseDirectory[parent.Length])))
        {
            var remaining = parent.IsEmpty ? baseDirectory : baseDirectory[(parent.Length + 1)..];
            if (filename.IsEmpty)
                return BaseDirectoryRelation.Ancestor;

            if (remaining.SequenceEqual(filename))
                return BaseDirectoryRelation.BaseDirectory;

            if (remaining.Length > filename.Length && remaining.StartsWith(filename) && PathReader.IsPathSeparator(remaining[filename.Length]))
                return BaseDirectoryRelation.Ancestor;
        }

        return BaseDirectoryRelation.Outside;

        static bool StartsWithPath(ReadOnlySpan<char> path, ReadOnlySpan<char> prefix)
        {
            if (path.Length < prefix.Length)
                return false;

            for (var i = 0; i < prefix.Length; i++)
            {
                if (path[i] != prefix[i] && !(PathReader.IsPathSeparator(path[i]) && PathReader.IsPathSeparator(prefix[i])))
                    return false;
            }

            return true;
        }
    }

    private enum BaseDirectoryRelation
    {
        Outside,
        Ancestor,
        BaseDirectory,
        Inside,
    }

    private sealed class ExclusionInfo
    {
        private ExclusionInfo(Glob[] excludes, bool includesCanMatchFiles, bool includesCanMatchDirectories, bool includesCanMatchLeadingDot)
        {
            Excludes = excludes;
            IncludesCanMatchFiles = includesCanMatchFiles;
            IncludesCanMatchDirectories = includesCanMatchDirectories;
            IncludesCanMatchLeadingDot = includesCanMatchLeadingDot;
        }

        public Glob[] Excludes { get; }
        public bool IncludesCanMatchFiles { get; }
        public bool IncludesCanMatchDirectories { get; }

        // Whether an include pattern can match a path holding a segment that starts with a dot. A pattern of another
        // type is assumed to match anything.
        public bool IncludesCanMatchLeadingDot { get; }

        public static ExclusionInfo Create(IGlobEvaluatable[] globs)
        {
            var excludes = globs.OfType<Glob>().Where(glob => glob.Mode is GlobMode.Exclude).ToArray();
            var includes = globs.Where(glob => glob.Mode is GlobMode.Include).ToArray();
            return new ExclusionInfo(
                excludes,
                includesCanMatchFiles: includes.Any(glob => glob.CanMatchFiles),
                includesCanMatchDirectories: includes.Any(glob => glob.CanMatchDirectories),
                includesCanMatchLeadingDot: includes.Any(glob => glob is not (Glob { MatchLeadingDot: false } or GlobCollection { MatchLeadingDot: false })));
        }
    }

    public IEnumerator<IGlobEvaluatable> GetEnumerator() => ((IEnumerable<IGlobEvaluatable>)_globs).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _globs.GetEnumerator();
}
