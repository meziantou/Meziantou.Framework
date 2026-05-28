using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Bnf
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode() => new()
    {
        Contains =
        [
            new() { Scope = "attribute", Begin = "<", End = ">" },
            new()
            {
                Begin = "::=",
                End = "$",
                Contains =
                [
                    new() { Begin = "<", End = ">" },
                    CommonModes.CLineCommentMode,
                    CommonModes.CBlockCommentMode,
                    CommonModes.AposStringMode,
                    CommonModes.QuoteStringMode,
                ],
            },
        ],
    };
}
