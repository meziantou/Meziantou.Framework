using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages.Common;

internal static class CommonModes
{
    public const string IdentRe = @"[a-zA-Z]\w*";
    public const string UnderscoreIdentRe = @"[a-zA-Z_]\w*";
    public const string NumberRe = @"\b\d+(\.\d+)?";
    public const string CNumberRe = @"(-?)(\b0[xX][a-fA-F0-9]+|(\b\d+(\.\d*)?|\.\d+)([eE][-+]?\d+)?)";

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
