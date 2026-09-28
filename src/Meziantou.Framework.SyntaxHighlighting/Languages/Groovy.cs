using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Groovy
{
    private const string IdentChars = "A-Za-z0-9_$";
    private const string IdentRe = "[" + IdentChars + "]+";

    private static readonly string[] Types = ["byte", "short", "char", "int", "long", "boolean", "float", "double", "void"];

    private static readonly string[] ReservedKeywords =
    [
        // Groovy specific keywords
        "def", "as", "in", "assert", "trait",
        // Keywords shared with Java
        "abstract", "static", "volatile", "transient", "public", "private", "protected", "synchronized", "final", "class",
        "interface", "enum", "if", "else", "for", "while", "switch", "case", "break", "default", "continue", "throw",
        "throws", "try", "catch", "finally", "implements", "extends", "new", "import", "package", "return", "instanceof",
        "var",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var comment = new Mode
        {
            Variants =
            [
                CommonModes.CLineCommentMode,
                // highlight.js lists the block comment first, so it always wins and doc comments never get their doc tags.
                CommonModes.Comment(@"/\*\*", @"\*/", extraContains:
                [
                    // Eat the `@` of e-mail addresses so they are not doc tags
                    new Mode { Begin = CommonModes.RunStart(@"\w") + @"\w+@" },
                    new Mode { Scope = "doctag", Begin = "@[A-Za-z]+" },
                ]),
                CommonModes.CBlockCommentMode,
            ],
        };

        var regexp = new Mode
        {
            Scope = "regexp",
            Begin = @"~?\/[^\/\n]+\/",
            Contains = [CommonModes.BackslashEscape],
        };

        // highlight.js uses BINARY_NUMBER_MODE and C_NUMBER_MODE, which do not support the type suffixes and the `_`
        // separators (`1_000_000L`, `1.5G`), and split ranges (`1..10`) into the numbers `1.` and `.10`.
        var number = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode { Begin = @"\b0[bB][01](?:_*[01])*[lLiIgG]?" },
                new Mode { Begin = @"(-?)(\b0[xX][a-fA-F0-9](?:_*[a-fA-F0-9])*[lLiIgG]?|(\b\d(?:_*\d)*(\.\d(?:_*\d)*)?|(?<!\.)\.\d(?:_*\d)*)([eE][-+]?\d+)?[lLiIgGfFdD]?)" },
            ],
        };

        var strings = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode { Begin = "\"\"\"", End = "\"\"\"" },
                new Mode { Begin = "'''", End = "'''" },
                new Mode { Begin = @"\$/", End = @"/\$" },
                CommonModes.AposStringMode,
                CommonModes.QuoteStringMode,
            ],
        };

        var ternary = new Mode
        {
            // The middle element of the ternary operator, so it is not highlighted as a label, a named parameter or a
            // map key. highlight.js also starts it at the safe navigation operator (`person?.name`), which then disables
            // keywords up to the next `:`, possibly many lines further.
            Begin = @"\?(?!\.)",
            End = ":",
        };
        ternary.Contains = [comment, strings, regexp, number, Mode.Self];

        return new Mode
        {
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["variable.language"] = ["this", "super"],
                ["literal"] = ["true", "false", "null"],
                ["type"] = Types,
                ["keyword"] = ReservedKeywords,
            }),
            Illegal = "#|</",
            Contains =
            [
                new Mode
                {
                    // A shebang is only valid on the first line
                    Scope = "meta",
                    Begin = @"\A#![ ]*/.*\bgroovy\b.*",
                    End = "$",
                },
                comment,
                strings,
                regexp,
                number,
                new Mode
                {
                    BeginParts = ["(class|interface|trait|enum|record|extends|implements)", @"\s+", CommonModes.UnderscoreIdentRe],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "keyword",
                        [3] = "title.class",
                    },
                },
                new Mode
                {
                    Scope = "meta",
                    Begin = "@[A-Za-z]+",
                },
                new Mode
                {
                    // Map keys and named parameters. Only try the first position of a word: otherwise, each character of
                    // a long word would rescan the rest of it.
                    Scope = "attr",
                    Begin = CommonModes.RunStart(IdentChars) + IdentRe + "[ \t]*:",
                },
                ternary,
                new Mode
                {
                    // Labeled statements
                    Scope = "symbol",
                    Begin = "^[ \t]*(?=" + IdentRe + ":)",
                    ExcludeBegin = true,
                    End = IdentRe + ":",
                },
            ],
        };
    }
}
