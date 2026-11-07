using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Gherkin
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        return new Mode
        {
            // Multi-word keywords (`Scenario Outline`, `Business Need`) are split on spaces, so each word is a keyword.
            Keywords = Engine.Keywords.FromWords("Feature Background Ability Business Need Scenario Scenarios Scenario Outline Scenario Template Examples Given And Then But When".Split(' ')),
            Contains =
            [
                new Mode { Scope = "symbol", Begin = @"\*" },
                new Mode { Scope = "meta", Begin = @"@[^@\s]+" },

                // Deviation from highlight.js, whose table row ends at a last `|` followed by word characters only, and whose
                // cells can span lines: a row with trailing spaces or text after its last `|`, or without one, turned the
                // following lines into cells up to the next row that ends properly. A row ends at the end of its line.
                new Mode
                {
                    Begin = @"\|",
                    End = @"\|[^|\n]*$|$",
                    Contains = [new Mode { Scope = "string", Begin = @"[^|\n]+" }],
                },

                // Deviation from highlight.js, whose placeholder ends at the next `>`, even lines later: a `<` without a
                // `>` on the same line (`Then the total is < 10`) turned the rest of the document into a placeholder.
                new Mode { Scope = "variable", Begin = @"<[^<>\n]*>" },
                CommonModes.HashCommentMode,
                new Mode { Scope = "string", Begin = "\"\"\"", End = "\"\"\"" },
                CommonModes.QuoteStringMode,
            ],
        };
    }
}
