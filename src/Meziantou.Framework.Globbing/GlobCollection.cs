using System.Collections;
using System.ComponentModel;

namespace Meziantou.Framework.Globbing;

/// <summary>
/// Represents a collection of glob patterns that can be evaluated together.
/// </summary>
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

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static GlobCollection Create(ReadOnlySpan<IGlobEvaluatable> globs) => new(globs.ToArray());

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
