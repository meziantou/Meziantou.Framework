namespace Meziantou.Framework.Globbing;

/// <summary>Represents an object that can evaluate glob patterns against file system paths.</summary>
public interface IGlobEvaluatable
{
    /// <summary>Determines whether the specified path matches the glob pattern.</summary>
    /// <param name="directory">The directory part of the path to match.</param>
    /// <param name="filename">The filename part of the path to match.</param>
    /// <param name="itemType">The type of the path item (file or directory), or <see langword="null"/> if unknown.</param>
    /// <returns><see langword="true"/> if the path matches the pattern; otherwise, <see langword="false"/>.</returns>
    bool IsMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType);

    /// <summary>Determines whether a directory should be recursed into when enumerating files.</summary>
    /// <param name="folderPath">The folder path to check.</param>
    /// <param name="filename">The filename part of the path to check.</param>
    /// <returns><see langword="true"/> if the directory could contain matches; otherwise, <see langword="false"/>.</returns>
    bool IsPartialMatch(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename);

    /// <summary>Gets a value indicating whether this glob pattern can match files.</summary>
    bool CanMatchFiles { get; }

    /// <summary>Gets a value indicating whether this glob pattern can match directories.</summary>
    bool CanMatchDirectories { get; }

    /// <summary>Gets the glob mode indicating whether this pattern includes or excludes matches.</summary>
    GlobMode Mode { get; }

    /// <summary>Gets a value indicating whether directories should be traversed when enumerating files.</summary>
    bool TraverseDirectories { get; }
}
