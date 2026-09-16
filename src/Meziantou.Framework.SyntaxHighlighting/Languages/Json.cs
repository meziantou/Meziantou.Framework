using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Json
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        // A key never starts right after a backslash: outside of a string or a comment, a backslash is
        // illegal. Skipping those quotes keeps the escaped quotes of a long string value from each
        // rescanning the rest of the line.
        var attribute = new Mode
        {
            Scope = "attr",
            Begin = @"(?:\G|(?<!\\))""(\\.|[^\\""\r\n])*""(?=\s*:)",
        };

        var punctuation = new Mode
        {
            Scope = "punctuation",
            Match = @"[{}[\],:]",
        };

        var literals = new[] { "true", "false", "null" };

        var literalsMode = new Mode
        {
            Scope = "literal",
            BeginKeywords = literals,
        };

        return new Mode
        {
            Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["literal"] = literals,
            }),
            Contains =
            [
                attribute,
                punctuation,
                CommonModes.QuoteStringMode,
                literalsMode,
                CommonModes.CNumberMode,
                CommonModes.CLineCommentMode,
                CommonModes.CBlockCommentMode,
            ],
            Illegal = @"\S",
        };
    }
}
