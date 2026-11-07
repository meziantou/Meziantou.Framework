namespace Meziantou.Framework.Globbing.Internals;

/// <summary>
///     Matches a directory, and optionally everything below it, for a gitignore entry ending with a '/'.
/// </summary>
internal sealed class DirectoryContentSegment : Segment
{
    private readonly bool _matchContent;

    private DirectoryContentSegment(bool matchContent)
    {
        _matchContent = matchContent;
    }

    /// <summary>An entry that excludes a directory also excludes its whole content, so the segment matches any path below the directory.</summary>
    public static DirectoryContentSegment IncludingContent { get; } = new DirectoryContentSegment(matchContent: true);

    /// <summary>A negated entry only re-includes the directory: git does not re-include the content of a directory, so the segment stops at the directory itself.</summary>
    public static DirectoryContentSegment DirectoryOnly { get; } = new DirectoryContentSegment(matchContent: false);

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (pathReader.IsEndOfPath)
            return pathReader.IsDirectory;

        if (!_matchContent)
            return false;

        pathReader.ConsumeToEnd();
        return true;
    }

    // The segment consumes the rest of the path itself, so the matcher must not collapse it with a preceding '**'.
    public override bool IsRecursiveMatchAll => true;

    public override bool CanMatchEmptyPath => true;

    // Segments are joined with a '/', so an empty text renders the trailing '/' of the original pattern.
    public override string ToString() => "";
}
