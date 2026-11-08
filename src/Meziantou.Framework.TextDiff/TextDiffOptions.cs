namespace Meziantou.Framework;

public sealed class TextDiffOptions
{
    /// <summary>
    /// Gets or sets the algorithm used to compute the diff.
    /// <see cref="TextDiffAlgorithm.Myers"/> is the default and is recommended for general use.
    /// </summary>
    public TextDiffAlgorithm Algorithm { get; set; } = TextDiffAlgorithm.Myers;


    /// <summary>
    /// Gets or sets how the texts are split into the chunks that are compared against each other.
    /// Defaults to <see cref="TextChunker.Lines"/>.
    /// </summary>
    public TextChunker Chunker { get; set; } = TextChunker.Lines;

    /// <summary>
    /// Gets or sets a value indicating whether two chunks that differ only in casing are considered equal.
    /// The comparison is ordinal, so casing rules are not culture-sensitive.
    /// </summary>
    public bool IgnoreCase { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the leading and trailing whitespace of a chunk is ignored
    /// when comparing chunks.
    /// </summary>
    /// <remarks>
    /// Only the <em>edges</em> of a chunk are trimmed; whitespace inside a chunk remains significant. With the
    /// default <see cref="TextChunker.Lines"/> chunker, <c>"a  b"</c> and <c>"a b"</c> are still different. Use
    /// <see cref="TextChunker.Words"/> to make internal whitespace insignificant: it puts each run of whitespace
    /// in its own chunk, which trimming then reduces to an empty one.
    /// </remarks>
    public bool IgnoreWhitespace { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether line terminators are normalized to <c>"\n"</c> before the texts
    /// are chunked, so that <c>"\r\n"</c>, <c>"\r"</c> and <c>"\n"</c> compare equal.
    /// </summary>
    /// <remarks>
    /// The normalization happens before chunking, so the entries of the result carry the normalized text rather
    /// than the original line terminators.
    /// </remarks>
    public bool IgnoreEndOfLine { get; set; }

    /// <summary>
    /// Gets or sets the time after which the diff stops searching for matching chunks. Defaults to <see langword="null"/>:
    /// no time limit.
    /// </summary>
    /// <remarks>
    /// Once the time limit is reached, every region of the texts that is not diffed yet is reported as deleted and
    /// inserted as a whole, so the diff completes quickly and is still valid: it rebuilds both texts, but may report as
    /// changed chunks that a full search would match. The common start and end of a region are still matched. A
    /// <see langword="null"/> value and <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> disable the limit.
    /// </remarks>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of deleted and inserted chunks the Myers search looks for in a region before it
    /// settles for a diff that may not be minimal. Defaults to <see langword="null"/>: no limit, the diff is minimal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cost of the Myers search grows with the square of the number of differences. When the shortest edit script
    /// of a region needs more than this many deleted and inserted chunks, the search stops and splits the region at the
    /// furthest point it reached, as git does, then diffs both parts. A region that needs at most this many edits is
    /// still diffed exactly.
    /// </para>
    /// <para>
    /// The limit applies to <see cref="TextDiffAlgorithm.Myers"/>, and to the regions <see cref="TextDiffAlgorithm.Patience"/>
    /// and <see cref="TextDiffAlgorithm.Histogram"/> hand to Myers. <see cref="TextDiffAlgorithm.HuntSzymanski"/> does
    /// not use it; use <see cref="Timeout"/> to bound it.
    /// </para>
    /// </remarks>
    public int? MaxEditCost { get; set; }
}
