using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages.Common;

internal static class CommonModes
{
    public const string IdentRe = @"[a-zA-Z]\w*";
    public const string UnderscoreIdentRe = @"[a-zA-Z_]\w*";
    public const string NumberRe = @"\b\d+(\.\d+)?";
    public const string CNumberRe = @"(-?)(\b0[xX][a-fA-F0-9]+|(\b\d+(\.\d*)?|\.\d+)([eE][-+]?\d+)?)";

    /// <summary>
    /// <c>^\s*</c>, restricted to the first line of a run of blank lines.
    /// </summary>
    /// <remarks>
    /// <c>^\s*X</c> is quadratic on a run of blank lines that is not followed by <c>X</c>: every line
    /// start of the run rescans the whole run. When the pattern can match from a line start, it can
    /// also match from the first line of the blank run that precedes it, so the later line starts can
    /// never be the leftmost match. They are rejected, unless the scan started in the middle of the
    /// blank run. <c>X</c> must not start with whitespace.
    /// </remarks>
    public const string IndentedLineStartRe = @"^(?:\G|(?<!^[^\S\n]*\n)|(?<=(?<!^)\G[^\S\n]*\n))\s*";

    /// <summary>
    /// A zero-width assertion to put in front of a pattern that starts with a <paramref name="start"/>
    /// character followed by a greedy run of <paramref name="run"/> characters, and that can only
    /// continue with a character outside of that run.
    /// </summary>
    /// <remarks>
    /// Such a pattern succeeds or fails identically from every position of the run where it can start,
    /// so it is quadratic on a long run that is not followed by the expected text. This assertion only
    /// lets it start at the first of those positions that is not before the scan start: a position is
    /// rejected when an earlier one exists in the same run, at or after the scan start (<c>\G</c>).
    /// Both parameters are the content of a character class (e.g. <c>\w</c>, <c>a-zA-Z_</c>);
    /// <paramref name="start"/> must be a subset of <paramref name="run"/> and defaults to it.
    /// <paramref name="startAssertion"/> is a zero-width assertion the pattern also requires at its
    /// start, such as <c>\b</c>.
    /// </remarks>
    public static string RunStart(string run, string? start = null, string? startAssertion = null)
    {
        if (start is null && startAssertion is null)
            return @"(?:\G|(?<![" + run + "]))";

        // The lookahead keeps the lookbehind, which scans back to the previous start, from running at
        // every position of a run of characters that cannot start the pattern.
        start ??= run;
        return "(?=[" + start + @"])(?:\G|(?<!" + startAssertion + "[" + start + @"](?:(?!\G)[" + run + "])*?))";
    }

    public static readonly Mode BackslashEscape = new()
    {
        Begin = @"\\[\s\S]",
    };

    public static Mode QuoteStringMode { get; } = new()
    {
        Scope = "string",
        Begin = "\"",
        End = "\"",
        Illegal = @"\n",
        Contains = [BackslashEscape],
    };

    public static Mode AposStringMode { get; } = new()
    {
        Scope = "string",
        Begin = "'",
        End = "'",
        Illegal = @"\n",
        Contains = [BackslashEscape],
    };

    public static Mode CLineCommentMode { get; } = Comment("//", "$");

    public static Mode CBlockCommentMode { get; } = Comment(@"/\*", @"\*/");

    public static Mode HashCommentMode { get; } = Comment("#", "$");

    public static Mode NumberMode { get; } = new()
    {
        Scope = "number",
        Begin = NumberRe,
    };

    public static Mode CNumberMode { get; } = new()
    {
        Scope = "number",
        Begin = CNumberRe,
    };

    public static Mode TitleMode { get; } = new()
    {
        Scope = "title",
        Begin = IdentRe,
    };

    public static Mode UnderscoreTitleMode { get; } = new()
    {
        Scope = "title",
        Begin = UnderscoreIdentRe,
    };

    public static Mode Comment(string begin, string end, bool returnBegin = false, IList<Mode>? extraContains = null, string? illegal = null)
    {
        var contains = new List<Mode>();
        if (extraContains is not null)
            contains.AddRange(extraContains);
        contains.Add(new Mode
        {
            Scope = "doctag",
            Begin = "[ ]*(?=(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):)",
            End = "(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):",
            ExcludeBegin = true,
        });

        return new Mode
        {
            Scope = "comment",
            Begin = begin,
            End = end,
            ReturnBegin = returnBegin,
            Illegal = illegal,
            Contains = contains,
        };
    }
}
