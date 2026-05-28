using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Json
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var attribute = new Mode
        {
            Scope = "attr",
            Begin = @"""(\\.|[^\\""\r\n])*""(?=\s*:)",
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
