namespace Meziantou.Framework.Globbing.Internals;

internal abstract class Segment
{
    /// <summary>
    ///     Matches the segment against the current position of <paramref name="pathReader"/>. The reader may be
    ///     positioned at the end of the current path segment, so implementations that consume at least one character
    ///     must check <see cref="PathReader.IsEndOfCurrentSegment"/> before reading. The reader is only advanced when
    ///     the segment matches.
    /// </summary>
    public abstract bool IsMatch(ref PathReader pathReader);

    public virtual bool IsRecursiveMatchAll => false;

    /// <summary>
    ///     Whether the segment can still match once the whole path has been consumed. The matcher stops as soon as
    ///     the path is exhausted, so only a segment that says otherwise is given a chance to match nothing.
    /// </summary>
    public virtual bool CanMatchEmptyPath => false;
}
