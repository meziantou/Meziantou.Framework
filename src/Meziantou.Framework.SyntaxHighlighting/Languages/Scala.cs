using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Scala
{
    private static readonly string[] Literals = ["true", "false", "null"];

    private static readonly string[] ReservedKeywords =
    [
        "type", "yield", "lazy", "override", "def", "with", "val", "var", "sealed", "abstract", "private", "trait",
        "object", "if", "then", "forSome", "for", "while", "do", "throw", "finally", "protected", "extends", "import",
        "final", "return", "else", "break", "new", "catch", "super", "class", "case", "package", "default", "try", "this",
        "match", "continue", "throws", "implicit", "export", "enum", "given", "transparent",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        // Used in strings for interpolation
        var substitution = new Mode
        {
            Scope = "subst",
            Variants =
            [
                new Mode { Begin = @"\$[A-Za-z0-9_]+" },
                new Mode { Begin = @"\$\{", End = @"\}" },
            ],
        };

        // The prefix of an interpolated string (`s"..."`) is a run of lowercase letters: only try the first position of
        // the run, otherwise each letter of a long word would rescan the rest of it.
        var interpolatorRe = CommonModes.RunStart("a-z") + "[a-z]+";

        var strings = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode
                {
                    Begin = "\"\"\"",
                    End = "\"\"\"",
                },
                new Mode
                {
                    Begin = "\"",
                    End = "\"",
                    Illegal = @"\n",
                    Contains = [CommonModes.BackslashEscape],
                },
                // highlight.js tries this variant after the single-quoted one below, so `s"""a"""` is the empty
                // string `s""` followed by `"a"` and `""`.
                new Mode
                {
                    Begin = interpolatorRe + "\"\"\"",
                    End = "\"\"\"",
                    Contains = [substitution],
                },
                new Mode
                {
                    Begin = interpolatorRe + "\"",
                    End = "\"",
                    Illegal = @"\n",
                    Contains = [CommonModes.BackslashEscape, substitution],
                },
                // highlight.js does not highlight character literals. `'symbol` is a (Scala 2) symbol literal, not a
                // character.
                new Mode
                {
                    Match = @"'(?:[^'\\\n]|\\u[0-9a-fA-F]{4}|\\[^\n])'",
                },
            ],
        };

        // highlight.js uses C_NUMBER_MODE, which does not support the `L`, `f` and `d` suffixes and the `_` separators
        // (`1_000_000L`), and makes `1.` of `1.toString` a number.
        var number = new Mode
        {
            Scope = "number",
            Begin = @"(-?)(\b0[xX][a-fA-F0-9](?:_*[a-fA-F0-9])*[lL]?|(\b\d(?:_*\d)*(\.\d(?:_*\d)*)?|\.\d(?:_*\d)*)([eE][-+]?\d+)?[lLfFdD]?)",
        };

        var type = new Mode
        {
            Scope = "type",
            Begin = @"\b[A-Z][A-Za-z0-9_]*",
        };

        var name = new Mode
        {
            Scope = "title",
            Begin = """[^0-9\n\t "'(),.`{}\[\]:;][^\n\t "'(),.`{}\[\]:;]+|[^0-9\n\t "'(),.`{}\[\]:;=]""",
        };

        var classDeclaration = new Mode
        {
            Scope = "class",
            BeginKeywords = ["class", "object", "trait", "type"],
            End = @"[:={\[\n;]",
            ExcludeEnd = true,
            Contains =
            [
                CommonModes.CLineCommentMode,
                CommonModes.CBlockCommentMode,
                new Mode { BeginKeywords = ["extends", "with"] },
                new Mode
                {
                    Begin = @"\[",
                    End = @"\]",
                    ExcludeBegin = true,
                    ExcludeEnd = true,
                    Contains = [type, CommonModes.CLineCommentMode, CommonModes.CBlockCommentMode],
                },
                new Mode
                {
                    Scope = "params",
                    Begin = @"\(",
                    End = @"\)",
                    ExcludeBegin = true,
                    ExcludeEnd = true,
                    Contains = [type, CommonModes.CLineCommentMode, CommonModes.CBlockCommentMode],
                },
                name,
            ],
        };

        var method = new Mode
        {
            Scope = "function",
            BeginKeywords = ["def"],
            End = @"(?=[:={\[(\n;])",
            Contains = [name],
        };

        var extension = new Mode
        {
            // The first token of the line, followed by at least one space and `[` or `(`
            BeginParts = [CommonModes.IndentedLineStartRe, "extension", @"\s+(?=[[(])"],
            BeginScope = new Dictionary<int, string>
            {
                [2] = "keyword",
            },
        };

        var end = new Mode
        {
            // `extension` is the only marker that follows an `end` that cannot be captured by another rule.
            BeginParts = [CommonModes.IndentedLineStartRe, "end", @"\s+", @"(extension\b)?"],
            BeginScope = new Dictionary<int, string>
            {
                [2] = "keyword",
                [4] = "keyword",
            },
        };

        var usingParameterClause = new Mode
        {
            // The opening `(` of a parameter or argument list, followed by spaces that are not followed by `)`
            BeginParts = [@"\(\s*", "using", @"\s+(?!\))"],
            BeginScope = new Dictionary<int, string>
            {
                [2] = "keyword",
            },
        };

        // Directives (//> using dep "...")
        var usingDirective = new Mode
        {
            BeginParts = ["//>", @"\s+", "using", @"\s+", @"\S+"],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "comment",
                [3] = "keyword",
                [5] = "type",
            },
            End = "$",
            Contains =
            [
                new Mode
                {
                    Scope = "string",
                    Begin = @"\S+",
                },
            ],
        };

        return new Mode
        {
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["literal"] = Literals,
                ["keyword"] = ReservedKeywords,
            }),
            Contains =
            [
                usingDirective,
                CommonModes.CLineCommentMode,
                CommonModes.CBlockCommentMode,
                strings,
                type,
                method,
                classDeclaration,
                number,
                extension,
                end,
                new Mode { Match = @"\.inline\b" },
                new Mode
                {
                    Begin = @"\binline(?=\s)",
                    Keywords = Engine.Keywords.FromWords(["inline"]),
                },
                usingParameterClause,
                new Mode
                {
                    Scope = "meta",
                    Begin = "@[A-Za-z]+",
                },
            ],
        };
    }
}
