using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Haskell
{
    private const string DecimalDigits = "([0-9]_*)+";
    private const string HexDigits = "([0-9a-fA-F]_*)+";
    private const string BinaryDigits = "([01]_*)+";
    private const string OctalDigits = "([0-7]_*)+";
    private const string AscSymbol = @"[!#$%&*+.\/<=>?@\\^~-]";
    private const string UniSymbol = @"(\p{S}|\p{P})";
    private const string Special = @"[(),;\[\]`|{}]";
    private const string Symbol = "(" + AscSymbol + "|(?!(" + Special + "|[_:\"']))" + UniSymbol + ")";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var comment = new Mode
        {
            Variants =
            [
                // A double dash is a comment only if it is not part of an operator; see the no-markup rule of the root.
                CommonModes.Comment("--+", "$"),
                CommonModes.Comment(@"\{-", @"-\}", extraContains: [Mode.Self]),
            ],
        };

        var pragma = new Mode
        {
            Scope = "meta",
            Begin = @"\{-#",
            End = "#-}",
        };

        var preprocessor = new Mode
        {
            Scope = "meta",
            Begin = "^#",
            End = "$",
        };

        var constructor = new Mode
        {
            Scope = "type",
            Begin = @"\b[A-Z][\w']*",
        };

        Mode[] listContains =
        [
            pragma,
            preprocessor,
            new Mode
            {
                Scope = "type",
                Begin = @"\b[A-Z][\w]*(\((\.\.|,|\w+)\))?",
            },
            new Mode
            {
                Scope = "title",
                Begin = @"[_a-z][\w']*",
            },
            comment,
        ];

        var list = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Illegal = "\"",
            Contains = listContains,
        };

        var record = new Mode
        {
            Begin = @"\{",
            End = @"\}",
            Contains = listContains,
        };

        var number = new Mode
        {
            Scope = "number",
            Variants =
            [
                // decimal floating-point-literal (subsumes decimal-literal)
                new Mode { Match = @"\b(" + DecimalDigits + @")(\.(" + DecimalDigits + @"))?([eE][+-]?(" + DecimalDigits + @"))?\b" },
                // hexadecimal floating-point-literal (subsumes hexadecimal-literal)
                new Mode { Match = @"\b0[xX]_*(" + HexDigits + @")(\.(" + HexDigits + @"))?([pP][+-]?(" + DecimalDigits + @"))?\b" },
                // octal-literal
                new Mode { Match = @"\b0[oO](" + OctalDigits + @")\b" },
                // binary-literal
                new Mode { Match = @"\b0[bB](" + BinaryDigits + @")\b" },
            ],
        };

        return new Mode
        {
            Keywords = Engine.Keywords.FromWords(
            [
                "let", "in", "if", "then", "else", "case", "of", "where", "do", "module", "import", "hiding", "qualified", "type",
                "data", "newtype", "deriving", "class", "instance", "as", "default", "infix", "infixl", "infixr", "foreign",
                "export", "ccall", "stdcall", "cplusplus", "jvm", "dotnet", "safe", "unsafe", "family", "forall", "mdo", "proc",
                "rec",
            ]),
            Contains =
            [
                // Top-level constructions.
                new Mode
                {
                    BeginKeywords = ["module"],
                    End = "where",
                    Keywords = Engine.Keywords.FromWords(["module", "where"]),
                    Contains = [list, comment],
                    Illegal = @"\W\.|;",
                },
                new Mode
                {
                    Begin = @"\bimport\b",
                    End = "$",
                    Keywords = Engine.Keywords.FromWords(["import", "qualified", "as", "hiding"]),
                    Contains = [list, comment],
                    Illegal = @"\W\.|;",
                },
                new Mode
                {
                    Scope = "class",
                    // `^(\s*)?(class|instance)\b` in highlight.js, which is quadratic on a run of blank lines.
                    Begin = CommonModes.IndentedLineStartRe + @"(class|instance)\b",
                    End = "where",
                    Keywords = Engine.Keywords.FromWords(["class", "family", "instance", "where"]),
                    Contains = [constructor, list, comment],
                },
                new Mode
                {
                    Scope = "class",
                    Begin = @"\b(data|(new)?type)\b",
                    End = "$",
                    // `where` (GADT syntax: `data Expr a where`) is not in highlight.js.
                    Keywords = Engine.Keywords.FromWords(["data", "family", "type", "newtype", "deriving", "where"]),
                    Contains = [pragma, constructor, list, record, comment],
                },
                new Mode
                {
                    BeginKeywords = ["default"],
                    End = "$",
                    Contains = [constructor, list, comment],
                },
                new Mode
                {
                    BeginKeywords = ["infix", "infixl", "infixr"],
                    End = "$",
                    Contains = [CommonModes.CNumberMode, comment],
                },
                new Mode
                {
                    Begin = @"\bforeign\b",
                    End = "$",
                    Keywords = Engine.Keywords.FromWords(["foreign", "import", "export", "ccall", "stdcall", "cplusplus", "jvm", "dotnet", "safe", "unsafe"]),
                    Contains = [constructor, CommonModes.QuoteStringMode, comment],
                },
                new Mode
                {
                    Scope = "meta",
                    Begin = @"#!\/usr\/bin\/env runhaskell",
                    End = "$",
                },

                // "Whitespaces".
                pragma,
                preprocessor,

                // Literals and names.

                // Single characters.
                new Mode
                {
                    Scope = "string",
                    Begin = @"'(?=\\?.')",
                    End = "'",
                    Contains =
                    [
                        new Mode
                        {
                            Scope = "char.escape",
                            Match = @"\\.",
                        },
                    ],
                },
                CommonModes.QuoteStringMode,
                number,
                constructor,
                new Mode
                {
                    Scope = "title",
                    Begin = @"^[_a-z][\w']*",
                },

                // No markup, prevents infix operators from being recognized as comments.
                // `--+(?!-)Symbol` can only succeed after a whole run of dashes, so it is only tried from the first dash of a
                // run: trying it from every dash made a long run of dashes quadratic.
                new Mode { Begin = "(?!-)" + Symbol + "--+|" + CommonModes.RunStart("-") + "--+(?!-)" + Symbol },
                comment,

                // No markup, relevance booster
                new Mode { Begin = "->|<-" },
            ],
        };
    }
}
