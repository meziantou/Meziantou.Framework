using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Erlang
{
    private const string BasicAtomRe = "[a-z'][a-zA-Z0-9_']*";
    private const string FunctionNameRe = "(" + BasicAtomRe + ":" + BasicAtomRe + "|" + BasicAtomRe + ")";

    private static readonly string[] Directives =
    [
        "-module", "-record", "-undef", "-export", "-ifdef", "-ifndef", "-author", "-copyright", "-doc", "-moduledoc", "-vsn",
        "-import", "-include", "-include_lib", "-compile", "-define", "-else", "-endif", "-file", "-behaviour", "-behavior",
        "-spec", "-on_load", "-nifs",

        // Not in highlight.js.
        "-type", "-opaque", "-export_type", "-callback", "-optional_callbacks", "-if", "-elif", "-error", "-warning",
        "-feature", "-dialyzer", "-deprecated",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var reserved = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // `or` and `bsr` are not in highlight.js, which lists `bzr` instead of `bsr`.
            ["keyword"] = "after and andalso band begin bnot bor bsl bzr bxor case catch cond div end fun if let not of orelse query receive rem try when xor maybe else or bsr",
            ["literal"] = "false true",
        });

        var comment = CommonModes.Comment("%", "$");

        var number = new Mode
        {
            Scope = "number",
            Begin = @"\b(\d+(_\d+)*#[a-fA-F0-9]+(_[a-fA-F0-9]+)*|\d+(_\d+)*(\.\d+(_\d+)*)?([eE][-+]?\d+)?)",
        };

        var namedFun = new Mode { Begin = @"fun\s+" + BasicAtomRe + @"/\d+" };

        var functionCallArguments = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            EndsWithParent = true,
            ReturnEnd = true,
        };

        var functionCall = new Mode
        {
            // Only try the first position of an atom: otherwise, each character of a long atom that is not followed by
            // `(` would rescan the rest of the atom.
            Begin = CommonModes.RunStart("a-zA-Z0-9_'", "a-z'") + FunctionNameRe + @"\(",
            End = @"\)",
            ReturnBegin = true,
            Contains =
            [
                new Mode { Begin = FunctionNameRe },
                functionCallArguments,
            ],
        };

        var tuple = new Mode
        {
            Begin = @"\{",
            End = @"\}",
        };

        var var1 = new Mode { Begin = @"\b_([A-Z][A-Za-z0-9_]*)?" };
        var var2 = new Mode { Begin = "[A-Z][a-zA-Z0-9_]*" };

        var recordFields = new Mode
        {
            Begin = @"\{",
            End = @"\}",
        };

        var recordAccess = new Mode
        {
            Begin = "#" + CommonModes.UnderscoreIdentRe,
            ReturnBegin = true,
            Contains =
            [
                new Mode { Begin = "#" + CommonModes.UnderscoreIdentRe },
                recordFields,
            ],
        };

        var charLiteral = new Mode
        {
            Scope = "string",
            Match = @"\$(\\([^0-9]|[0-9]{1,3}|)|.)",
        };

        var tripleQuote = new Mode
        {
            Scope = "string",
            Match = "\"\"\"(\"*)(?!\")[\\s\\S]*?\"\"\"\\1",
        };

        var sigil = new Mode
        {
            Scope = "string",
            Contains = [CommonModes.BackslashEscape],
            Variants =
            [
                new Mode { Match = "~\\w?\"\"\"(\"*)(?!\")[\\s\\S]*?\"\"\"\\1" },
                new Mode { Begin = @"~\w?\(", End = @"\)" },
                new Mode { Begin = @"~\w?\[", End = @"\]" },
                new Mode { Begin = @"~\w?\{", End = @"\}" },
                new Mode { Begin = @"~\w?<", End = ">" },
                new Mode { Begin = @"~\w?/", End = "/" },
                new Mode { Begin = @"~\w?\|", End = @"\|" },
                new Mode { Begin = @"~\w?'", End = "'" },
                new Mode { Begin = "~\\w?\"", End = "\"" },
                new Mode { Begin = @"~\w?`", End = "`" },
                new Mode { Begin = @"~\w?#", End = "#" },
            ],
        };

        var blockStatements = new Mode
        {
            BeginKeywords = ["fun", "receive", "if", "try", "case", "maybe"],
            End = "end",
            Keywords = reserved,
        };
        blockStatements.Contains =
        [
            comment,
            namedFun,
            new Mode(CommonModes.AposStringMode) { Scope = null },
            blockStatements,
            functionCall,
            sigil,
            tripleQuote,
            CommonModes.QuoteStringMode,
            number,
            tuple,
            var1,
            var2,
            recordAccess,
            charLiteral,
        ];

        Mode[] basicModes =
        [
            comment,
            namedFun,
            blockStatements,
            functionCall,
            sigil,
            tripleQuote,
            CommonModes.QuoteStringMode,
            number,
            tuple,
            var1,
            var2,
            recordAccess,
            charLiteral,
        ];
        functionCallArguments.Contains = basicModes;
        tuple.Contains = basicModes;
        recordFields.Contains = basicModes;

        // Not in highlight.js: parentheses that are not a call (`-define(SQUARE(X), (X) * (X)).`) must not end the
        // parameters.
        var nestedParentheses = new Mode
        {
            Begin = @"\(",
            End = @"\)",
        };
        nestedParentheses.Contains = [.. basicModes, nestedParentheses];

        var parameters = new Mode
        {
            Scope = "params",
            Begin = @"\(",
            End = @"\)",
            Contains = [.. basicModes, nestedParentheses],
        };

        // Not in highlight.js, where every atom of a function head is a title, including `when` and the calls of a
        // guard, and where numbers are not highlighted in a guard.
        var guard = new Mode
        {
            Begin = @"\bwhen\b",
            End = "(?=->)",
            Keywords = reserved,
            Contains = basicModes,
        };

        return new Mode
        {
            Keywords = reserved,
            Illegal = @"(</|\*=|\+=|-=|/\*|\*/|\(\*|\*\))",
            Contains =
            [
                new Mode
                {
                    Scope = "function",
                    Begin = "^" + BasicAtomRe + @"\s*\(",
                    End = "->",
                    ReturnBegin = true,
                    Illegal = @"\(|#|//|/\*|\\|:|;",
                    Contains =
                    [
                        parameters,
                        guard,
                        new Mode { Scope = "title", Begin = BasicAtomRe },
                    ],
                    Starts = new Mode
                    {
                        End = @";|\.",
                        Keywords = reserved,
                        Contains = basicModes,
                    },
                },
                comment,
                new Mode
                {
                    Begin = "^-",
                    End = @"\.",
                    ExcludeEnd = true,
                    ReturnBegin = true,
                    KeywordPattern = "-" + CommonModes.IdentRe,
                    Keywords = Engine.Keywords.FromWords(Directives),
                    Contains =
                    [
                        parameters,
                        sigil,
                        tripleQuote,
                        CommonModes.QuoteStringMode,
                    ],
                },
                number,
                sigil,
                tripleQuote,
                CommonModes.QuoteStringMode,
                recordAccess,
                var1,
                var2,
                tuple,
                charLiteral,
                new Mode { Begin = @"\.$" },
            ],
        };
    }
}
