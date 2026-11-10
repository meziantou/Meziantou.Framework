using System.Globalization;

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
    /// </summary>
    /// <remarks>
    /// The comparison is ordinal, so casing rules are not culture-sensitive, unless <see cref="Culture"/> or
    /// <see cref="CompareOptions"/> is set: <see cref="System.Globalization.CompareOptions.IgnoreCase"/> is then added to
    /// the culture-aware comparison.
    /// </remarks>
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
    /// Gets or sets the culture whose rules compare the chunks. Defaults to <see langword="null"/>: the chunks are compared
    /// ordinally, unless <see cref="CompareOptions"/> is set, which then uses <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    /// <remarks>
    /// A culture-aware comparison treats canonically equivalent strings as equal, such as <c>"\u00E9"</c> and
    /// <c>"e\u0301"</c>. It depends on ICU: in invariant globalization mode, it is ordinal.
    /// </remarks>
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// Gets or sets the options of a culture-aware comparison of the chunks, such as
    /// <see cref="System.Globalization.CompareOptions.IgnoreNonSpace"/> to ignore accents. Defaults to
    /// <see cref="System.Globalization.CompareOptions.None"/>.
    /// </summary>
    /// <remarks>
    /// Any value but <see cref="System.Globalization.CompareOptions.None"/> compares the chunks with the
    /// <see cref="CompareInfo"/> of <see cref="Culture"/>, or of <see cref="CultureInfo.InvariantCulture"/> when
    /// <see cref="Culture"/> is <see langword="null"/>. <see cref="System.Globalization.CompareOptions.Ordinal"/>,
    /// <see cref="System.Globalization.CompareOptions.OrdinalIgnoreCase"/> and
    /// <see cref="System.Globalization.CompareOptions.StringSort"/> are not supported: leave both properties unset for an
    /// ordinal comparison.
    /// </remarks>
    public CompareOptions CompareOptions { get; set; }

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

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="TextDiff.ComputeDiff"/> looks for blocks of chunks that are
    /// deleted at one place and inserted at another. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A moved block is still reported as <see cref="TextDiffOperation.Delete"/> entries where it was and
    /// <see cref="TextDiffOperation.Insert"/> entries where it is now, so the entries rebuild both texts as usual. The
    /// entries of both places share the same <see cref="TextDiffEntry.MoveId"/>.
    /// </para>
    /// <para>
    /// A block of chunks that holds only whitespace is never a move, and neither is a block deleted and inserted at the
    /// same place. <see cref="TextDiff.ComputeHierarchyDiff"/> does not detect moves.
    /// </para>
    /// </remarks>
    public bool DetectMoves { get; set; }
}
