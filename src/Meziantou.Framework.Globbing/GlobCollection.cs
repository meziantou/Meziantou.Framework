using System.Collections;
using System.ComponentModel;

namespace Meziantou.Framework.Globbing;

/// <summary>Represents a collection of glob patterns that can be evaluated together.</summary>
/// <example>
/// Combine multiple glob patterns with include and exclude rules:
/// <code>
/// var globs = new GlobCollection(
///     Glob.Parse("**/*.txt", GlobOptions.None),
///     Glob.Parse("!temp/**/*", GlobOptions.None)
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
    private readonly IGlobEvaluatable[] _globs;

    /// <summary>Initializes a new instance of the <see cref="GlobCollection"/> class.</summary>
    /// <param name="globs">The glob patterns to include in the collection.</param>
    public GlobCollection(params IGlobEvaluatable[] globs) => _globs = globs;

    /// <summary>Loads gitignore content and creates a <see cref="GlobCollection"/> from it.</summary>
    /// <param name="gitIgnoreContent">The gitignore content.</param>
    public static GlobCollection ParseGitIgnore(ReadOnlySpan<char> gitIgnoreContent)
    {
        var globs = new List<IGlobEvaluatable>();
        foreach (var entry in new StringExtensions.LineSplitEnumerator(gitIgnoreContent))
        {
            AddGitIgnoreLine(entry.Line, globs);
        }

        return new GlobCollection([.. globs]);
    }

    /// <summary>Loads a gitignore file asynchronously and creates a <see cref="GlobCollection"/> from it.</summary>
    /// <param name="path">The path to the gitignore file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<GlobCollection> LoadGitIgnoreAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

#if NET472
        using var stream = File.OpenRead(path);
#else
        await using var stream = File.OpenRead(path);
#endif
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

        var globs = new List<IGlobEvaluatable>();

        while (true)
        {

            string? line;
#if NET472
            cancellationToken.ThrowIfCancellationRequested();
            line = await reader.ReadLineAsync().ConfigureAwait(false);
#else
            line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
#endif
            if (line is null)
                break;

            AddGitIgnoreLine(line.AsSpan(), globs);
        }

        return new GlobCollection([.. globs]);
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

        globs.Add(Glob.Parse(line, GlobOptions.Git));
    }

    private static ReadOnlySpan<char> TrimGitIgnoreLineEnd(ReadOnlySpan<char> line)
    {
        var end = line.Length;
        while (end > 0)
        {
            var c = line[end - 1];
            if (c is not (' ' or '\t'))
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

    /// <summary>Gets the number of glob patterns in the collection.</summary>
    public int Count => _globs.Length;

    /// <summary>Gets the glob pattern at the specified index.</summary>
    /// <param name="index">The zero-based index of the glob pattern to get.</param>
    public IGlobEvaluatable this[int index] => _globs[index];

    /// <summary>Determines whether the specified path matches any pattern in the collection.</summary>
    /// <param name="directory">The directory part of the path to match.</param>
    /// <param name="filename">The filename part of the path to match.</param>
    /// <param name="itemType">The type of the path item (file or directory), or <see langword="null"/> if unknown.</param>
    /// <returns><see langword="true"/> if the path matches any include pattern and no exclude pattern; otherwise, <see langword="false"/>.</returns>
    public bool IsMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType)
    {
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
