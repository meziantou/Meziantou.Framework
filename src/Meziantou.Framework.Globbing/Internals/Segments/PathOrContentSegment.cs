namespace Meziantou.Framework.Globbing.Internals;

/// <summary>
///     Matches the path the previous segments matched, or any path below it, for a <see cref="GlobDialect.GitHubCodeOwners"/>
///     pattern: a pattern that matches a directory owns every file below it.
/// </summary>
internal sealed class PathOrContentSegment : Segment
{
    private PathOrContentSegment()
    {
    }

    public static PathOrContentSegment Instance { get; } = new PathOrContentSegment();

    public override bool IsMatch(ref PathReader pathReader)
    {
        pathReader.ConsumeToEnd();
        return true;
    }

    // The segment consumes the rest of the path itself, so the matcher must not collapse it with a preceding '**'.
    public override bool IsRecursiveMatchAll => true;

    public override bool CanMatchEmptyPath => true;

    // The segment is implied by the pattern, so it is not written: Glob.ToString skips it with its separator.
    public override string ToString() => "";
}
