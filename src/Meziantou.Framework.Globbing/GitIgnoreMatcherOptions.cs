namespace Meziantou.Framework.Globbing;

/// <summary>Options for <see cref="GitIgnoreMatcher"/>.</summary>
public sealed class GitIgnoreMatcherOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether the <c>info/exclude</c> file of the git directory of the repository is
    ///     read. The default value is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    ///     The git directory is the <c>.git</c> directory of the root directory. When <c>.git</c> is a file, as in a
    ///     worktree or a submodule, the file is read from the common git directory it points to.
    /// </remarks>
    public bool UseRepositoryExcludeFile { get; set; } = true;

    /// <summary>
    ///     Gets or sets the path of the global excludes file, which git reads from the <c>core.excludesFile</c>
    ///     setting. The default value is <see langword="null"/>: no global excludes file is read.
    /// </summary>
    /// <remarks>
    ///     <see cref="GitIgnoreMatcher"/> does not read the git configuration. When <c>core.excludesFile</c> is not set,
    ///     git reads <c>$XDG_CONFIG_HOME/git/ignore</c>, or <c>~/.config/git/ignore</c> when <c>XDG_CONFIG_HOME</c> is not
    ///     set. A file that does not exist is skipped.
    /// </remarks>
    public string? GlobalExcludesFile { get; set; }
}
