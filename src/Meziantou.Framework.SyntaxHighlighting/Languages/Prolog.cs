using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Prolog
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var atom = new Mode { Begin = "[a-z][A-Za-z0-9_]*" };

        var variable = new Mode
        {
            Scope = "symbol",
            Variants =
            [
                new Mode { Begin = "[A-Z][a-zA-Z0-9_]*" },
                new Mode { Begin = "_[A-Za-z0-9_]*" },
            ],
        };

        var parented = new Mode
        {
            Begin = @"\(",
            End = @"\)",
        };

        var list = new Mode
        {
            Begin = @"\[",
            End = @"\]",
        };

        // highlight.js also lets its phrasal words mode (common English words) consume the text of the comment, which
        // only affects the relevance used by language detection.
        var lineComment = new Mode
        {
            Scope = "comment",
            Begin = "%",
            End = "$",
        };

        var backtickString = new Mode
        {
            Scope = "string",
            Begin = "`",
            End = "`",
            Contains = [CommonModes.BackslashEscape],
        };

        // Deviation from highlight.js, whose character code only accepts the escape \' (0'\'), and whose separate rule for
        // 0'\s comes after it, so it is never used: 0'\n and 0'\s were 0'\ followed by an atom. Any escape is accepted,
        // and so is the doubled quote of the ISO standard (0'''), whose last quote used to start a quoted atom.
        var charCode = new Mode
        {
            Scope = "string",
            Begin = @"0'(''|\\.|.)",
        };

        // Deviation from highlight.js, which uses its C number: the dot that ends a clause is part of a number that
        // precedes it (`N is N0 + 1.`, `:- dynamic counter/1.`), although a float needs digits after its dot. Binary and
        // octal numbers (0b1010, 0o17) are supported too.
        var number = new Mode
        {
            Scope = "number",
            Begin = @"(-?)(\b0[xX][a-fA-F0-9]+|\b0[bB][01]+|\b0[oO][0-7]+|(\b\d+(\.\d+)?|\.\d+)([eE][-+]?\d+)?)",
        };

        // Relevance booster.
        var predicateOperator = new Mode { Begin = ":-" };

        Mode[] inner =
        [
            atom,
            variable,
            parented,
            predicateOperator,
            list,
            lineComment,
            CommonModes.CBlockCommentMode,
            CommonModes.QuoteStringMode,
            CommonModes.AposStringMode,
            backtickString,
            charCode,
            number,
        ];

        parented.Contains = inner;
        list.Contains = inner;

        return new Mode
        {
            Contains =
            [
                .. inner,

                // Relevance booster.
                new Mode { Begin = @"\.$" },
            ],
        };
    }
}
