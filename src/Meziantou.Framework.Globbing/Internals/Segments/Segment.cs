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

    /// <summary>
    ///     The text of the bracket expression the segment was parsed from, such as <c>[[:alpha:]-]</c>. A bracket
    ///     expression is written back as is: its syntax depends on the dialect, and the parser does not keep the
    ///     order of its items.
    /// </summary>
    public string? BracketExpressionText { get; set; }

    /// <summary>
    ///     Writes a pattern that <paramref name="dialect"/> parses back into an equivalent segment. The default
    ///     implementation writes <see cref="ToString"/>, which is enough for a segment that holds no literal text. A
    ///     segment that holds literal text must override this method, as the characters to escape depend on the
    ///     dialect.
    /// </summary>
    public virtual void AppendPattern(ref ValueStringBuilder sb, GlobDialect dialect)
    {
        sb.Append(BracketExpressionText ?? ToString());
    }

    /// <summary>Returns the pattern that <paramref name="dialect"/> parses back into an equivalent segment.</summary>
    public string ToString(GlobDialect dialect)
    {
        var sb = new ValueStringBuilder(stackalloc char[64]);
        AppendPattern(ref sb, dialect);
        return sb.ToString();
    }
}
