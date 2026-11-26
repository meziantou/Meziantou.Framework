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
        // rescanning the rest of the line. A key after a stray backslash is therefore a string, even
        // when the tokenizer resumes right after the backslash (see Tokenizer.ScanEntry about \G).
        //
        // The content is highlight.js's `(\\.|[^\\"\r\n])*`, unrolled into an atomic group (four times faster). Every
        // quote of the input starts a candidate, and most of them are values not followed by `:`: giving characters
        // back cannot end the content on a quote, so there is nothing to backtrack into.
        var attribute = new Mode
        {
            Scope = "attr",
            Begin = @"(?<!\\)""(?>[^\\""\r\n]*(?:\\.[^\\""\r\n]*)*)""(?=\s*:)",
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
