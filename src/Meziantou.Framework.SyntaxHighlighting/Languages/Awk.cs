using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Awk
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var variable = new Mode
        {
            Scope = "variable",
            Variants =
            [
                new Mode { Begin = @"\$[\w\d#@][\w\d_]*" },
                // A `${` that follows an unclosed `${` of the same line would end on the same `}`, or fail the same way:
                // it is not tried, so that a long line of them is not rescanned from each one.
                new Mode { Begin = @"(?=\$\{)(?:\G|(?<!\$(?!\G)\{(?:(?!\G)[^}\n])*?))\$\{(.*?)\}" },
            ],
        };

        var strings = new Mode
        {
            Scope = "string",
            Contains = [CommonModes.BackslashEscape],
            Variants =
            [
                new Mode { Begin = "(u|b)?r?'''", End = "'''" },
                new Mode { Begin = "(u|b)?r?\"\"\"", End = "\"\"\"" },
                new Mode { Begin = "(u|r|ur)'", End = "'" },
                new Mode { Begin = "(u|r|ur)\"", End = "\"" },
                new Mode { Begin = "(b|br)'", End = "'" },
                new Mode { Begin = "(b|br)\"", End = "\"" },
                CommonModes.AposStringMode,
                CommonModes.QuoteStringMode,
            ],
        };

        // highlight.js's REGEXP_MODE
        var regexp = new Mode
        {
            Scope = "regexp",
            Begin = @"\/(?=[^/\n]*\/)",
            End = @"\/[gimuy]*",
            Contains =
            [
                CommonModes.BackslashEscape,
                new Mode { Begin = @"\[", End = @"\]", Contains = [CommonModes.BackslashEscape] },
            ],
        };

        return new Mode
        {
            Keywords = Keywords.FromWords("BEGIN END if else while do for in break continue delete next nextfile function func exit".Split(' ')),
            Contains =
            [
                variable,
                strings,
                regexp,
                CommonModes.HashCommentMode,
                CommonModes.NumberMode,
            ],
        };
    }
}
