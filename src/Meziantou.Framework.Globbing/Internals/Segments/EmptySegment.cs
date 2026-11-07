namespace Meziantou.Framework.Globbing.Internals;

/// <summary>
///     Matches an empty path segment: the one before the leading separator of an absolute path, or the one between
///     two consecutive separators.
/// </summary>
internal sealed class EmptySegment : Segment
{
    private EmptySegment()
    {
    }

    public static EmptySegment Instance { get; } = new EmptySegment();

    // The segment consumes nothing, and it only exists when a separator follows it: the end of the path is not an
    // empty segment.
    public override bool IsMatch(ref PathReader pathReader) => pathReader.IsPathSeparator();

    // Segments are joined with a '/', so an empty text renders the extra separator of the original pattern.
    public override string ToString() => "";
}
