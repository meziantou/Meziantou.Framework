using System.Collections;
using System.ComponentModel;

namespace Meziantou.Framework.Globbing;

/// <summary>Represents a collection of glob patterns that can be evaluated together.</summary>
/// <example>
/// Combine multiple glob patterns with include and exclude rules:
/// <code>
/// var globs = new GlobCollection(
///     Glob.Parse("**/*.txt", GlobDialect.Standard),
///     Glob.Parse("!temp/**/*", GlobDialect.Standard)
/// );
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

    /// <summary>Initializes a new instance of the <see cref="GlobCollection"/> class.</summary>
    /// <param name="globs">The glob patterns to include in the collection.</param>
    public GlobCollection(params IGlobEvaluatable[] globs) => _globs = globs;

    private GlobCollection(IGlobEvaluatable[] globs, bool lastMatchWins)
    {
        _globs = globs;
        _lastMatchWins = lastMatchWins;
    }

    /// <summary>Loads gitignore content and creates a <see cref="GlobCollection"/> from it.</summary>
    /// <param name="gitIgnoreContent">The gitignore content.</param>
    /// <remarks>
    ///     As git does, an entry that no path can match, such as one holding an unterminated bracket expression or
    ///     ending with an escape character, is ignored instead of making the whole content invalid.
    /// </remarks>
    public static GlobCollection ParseGitIgnore(ReadOnlySpan<char> gitIgnoreContent)
    {
        // git skips a UTF-8 byte order mark at the start of the file
        if (!gitIgnoreContent.IsEmpty && gitIgnoreContent[0] == '\uFEFF')
        {
            gitIgnoreContent = gitIgnoreContent[1..];
        }

        // git only breaks lines on LF and removes the CR of a CRLF. A lone CR, U+0085, U+2028 and U+2029 are legal
        // characters in a file name, so they belong to the entry.
        var globs = new List<IGlobEvaluatable>();
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

        return new GlobCollection([.. globs], lastMatchWins: true);
    }

    /// <summary>Loads a gitignore file asynchronously and creates a <see cref="GlobCollection"/> from it.</summary>
    /// <param name="path">The path to the gitignore file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<GlobCollection> LoadGitIgnoreAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        return await LoadGitIgnoreAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads a gitignore stream asynchronously and creates a <see cref="GlobCollection"/> from it.</summary>
    /// <param name="stream">The stream containing the gitignore content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<GlobCollection> LoadGitIgnoreAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        return await LoadGitIgnoreAsync(reader, cancellationToken).ConfigureAwait(false);
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

    private static void AddGitIgnoreLine(ReadOnlySpan<char> line, List<IGlobEvaluatable> globs)
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
    bool IGlobEvaluatable.TraverseDirectories => _globs.Any(g => g.Mode is GlobMode.Include && ((IGlobEvaluatable)g).TraverseDirectories);
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
        // git cannot re-include a path whose parent directory is excluded, so an excluded ancestor decides on its
        // own and the patterns written for the path itself are not even looked at.
        if (HasExcludedAncestor(directory, filename))
            return true;

        return IsLastMatchCore(directory, filename, itemType);
    }

    private bool IsLastMatchCore(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType)
    {
        // The last pattern that matches decides, so walk backwards and stop at the first hit.
        for (var i = _globs.Length - 1; i >= 0; i--)
        {
            var glob = _globs[i];
            if (glob.IsMatch(directory, filename, itemType))
                return glob.Mode is GlobMode.Include;
        }

        return false;
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
            if (IsLastMatchCore(directory[..end], [], PathItemType.Directory))
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
    /// <returns><see langword="true"/> if the directory could contain matches; otherwise, <see langword="false"/>.</returns>
    public bool IsPartialMatch(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename)
    {
        foreach (var glob in _globs)
        {
            if (glob.Mode is GlobMode.Exclude)
                continue;

            if (glob.IsPartialMatch(folderPath, filename))
                return true;
        }

        return false;
    }

    public IEnumerator<IGlobEvaluatable> GetEnumerator() => ((IEnumerable<IGlobEvaluatable>)_globs).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _globs.GetEnumerator();
}
