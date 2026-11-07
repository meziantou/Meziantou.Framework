using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

// https://crystal-lang.org/reference/latest/syntax_and_semantics/
internal static class Crystal
{
    private const string IntSuffix = "(_?[ui](8|16|32|64|128))?";
    private const string FloatSuffix = "(_?f(32|64))?";
    private const string IdentRe = @"[a-zA-Z_]\w*[!?=]?";
    private const string MethodRe = @"[a-zA-Z_]\w*[!?=]?|[-+~]@|<<|>>|[=!]~|===?|<=>|[<>]=?|\*\*|[-/+%^&*~|]|//|//=|&[-+*]=?|&\*\*|\[\][=?]?";
    private const string PathRe = @"[A-Za-z_]\w*(::\w+)*(\?|!)?";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["keyword"] =
                "abstract alias annotation as as? asm begin break case class def do else elsif end ensure enum extend for fun if "
                + "include instance_sizeof is_a? lib macro module next nil? of out pointerof private protected rescue responds_to? "
                + "return require select self sizeof struct super then type typeof union uninitialized unless until verbatim when "
                + "while with yield __DIR__ __END_LINE__ __FILE__ __LINE__",
            ["literal"] = "false nil true",
        });

        var subst = new Mode { Scope = "subst", Begin = @"#\{", End = @"\}", KeywordPattern = IdentRe, Keywords = keywords };

        // Deviation from highlight.js: braces inside an interpolation (`#{items.map { |i| i.name }}`) do not end it.
        var substBraces = new Mode { Begin = @"\{", End = @"\}", KeywordPattern = IdentRe, Keywords = keywords };

        // Borrowed from Ruby. The negative lookaheads prevent false matches like `@ident@` or `$ident$`.
        var variable = new Mode
        {
            Scope = "variable",
            Begin = @"(\$\W)|((\$|@@?)(\w+))(?=[^@$?])(?![A-Za-z])(?![@$?'])",
        };

        var expansion = new Mode
        {
            Scope = "template-variable",
            Variants =
            [
                new Mode { Begin = @"\{\{", End = @"\}\}" },
                new Mode { Begin = @"\{%", End = @"%\}" },
            ],
            KeywordPattern = IdentRe,
            Keywords = keywords,
        };

        var strings = new Mode
        {
            Scope = "string",
            Contains = [CommonModes.BackslashEscape, subst],
            Variants =
            [
                new Mode { Begin = "'", End = "'" },
                new Mode { Begin = "\"", End = "\"" },
                new Mode { Begin = "`", End = "`" },
                new Mode { Begin = @"%[Qwi]?\(", End = @"\)", Contains = RecursiveParentheses(@"\(", @"\)") },
                new Mode { Begin = @"%[Qwi]?\[", End = @"\]", Contains = RecursiveParentheses(@"\[", @"\]") },
                new Mode { Begin = @"%[Qwi]?\{", End = @"\}", Contains = RecursiveParentheses(@"\{", @"\}") },
                new Mode { Begin = "%[Qwi]?<", End = ">", Contains = RecursiveParentheses("<", ">") },
                new Mode { Begin = @"%[Qwi]?\|", End = @"\|" },
                Heredoc(@"<<-(\w+)$"),
            ],
        };

        var qStrings = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode { Begin = @"%q\(", End = @"\)", Contains = RecursiveParentheses(@"\(", @"\)") },
                new Mode { Begin = @"%q\[", End = @"\]", Contains = RecursiveParentheses(@"\[", @"\]") },
                new Mode { Begin = @"%q\{", End = @"\}", Contains = RecursiveParentheses(@"\{", @"\}") },
                new Mode { Begin = "%q<", End = ">", Contains = RecursiveParentheses("<", ">") },
                new Mode { Begin = @"%q\|", End = @"\|" },
                Heredoc(@"<<-'(\w+)'$"),
            ],
        };

        var regexp = new Mode
        {
            Begin = @"(?!%\})(" + CommonModes.ReStartersRe + @"|\n|\b(case|if|select|unless|until|when|while)\b)\s*",
            Keywords = Engine.Keywords.FromWords(["case", "if", "select", "unless", "until", "when", "while"]),
            Contains =
            [
                new Mode
                {
                    Scope = "regexp",
                    Contains = [CommonModes.BackslashEscape, subst],
                    Variants =
                    [
                        new Mode { Begin = "//[a-z]*" },
                        new Mode { Begin = @"/(?!\/)", End = "/[a-z]*" },
                    ],
                },
            ],
        };

        var regexp2 = new Mode
        {
            Scope = "regexp",
            Contains = [CommonModes.BackslashEscape, subst],
            Variants =
            [
                new Mode { Begin = @"%r\(", End = @"\)", Contains = RecursiveParentheses(@"\(", @"\)") },
                new Mode { Begin = @"%r\[", End = @"\]", Contains = RecursiveParentheses(@"\[", @"\]") },
                new Mode { Begin = @"%r\{", End = @"\}", Contains = RecursiveParentheses(@"\{", @"\}") },
                new Mode { Begin = "%r<", End = ">", Contains = RecursiveParentheses("<", ">") },
                new Mode { Begin = @"%r\|", End = @"\|" },
            ],
        };

        var attribute = new Mode
        {
            Scope = "meta",
            Begin = @"@\[",
            End = @"\]",
            Contains = [CommonModes.QuoteStringMode],
        };

        // Deviation from highlight.js, where the regexp container comes first and takes every `:` (it is a regexp
        // starter), so that `:name` is never a symbol: the symbol comes before it. A colon followed by whitespace (a type
        // restriction `x : Int32`, the ternary operator) or by another colon (`::Foo`) is still not a symbol.
        var colonSymbol = new Mode
        {
            Scope = "symbol",
            Begin = @"(?<!:):(?![\s:])",
            Contains = [strings, new Mode { Begin = MethodRe }],
        };

        var pathTitle = new Mode { Scope = "title", Begin = PathRe };
        var methodTitle = new Mode { Scope = "title", Begin = MethodRe, EndsParent = true };

        Mode[] defaultContains =
        [
            expansion,
            strings,
            qStrings,
            regexp2,
            colonSymbol,
            regexp,
            attribute,
            variable,
            CommonModes.HashCommentMode,
            new Mode
            {
                Scope = "class",
                BeginKeywords = ["class", "module", "struct"],
                End = "$|;",
                Illegal = "=",
                Contains = [CommonModes.HashCommentMode, pathTitle, new Mode { Begin = "<" }],
            },
            new Mode
            {
                Scope = "class",
                BeginKeywords = ["lib", "enum", "union"],
                End = "$|;",
                Illegal = "=",
                Contains = [CommonModes.HashCommentMode, pathTitle],
            },
            new Mode
            {
                BeginKeywords = ["annotation"],
                End = "$|;",
                Illegal = "=",
                Contains = [CommonModes.HashCommentMode, pathTitle],
            },

            // highlight.js's MATCH_NOTHING_RE: the method name ends the mode.
            new Mode { Scope = "function", BeginKeywords = ["def"], End = @"\b\B", Contains = [methodTitle] },
            new Mode { Scope = "function", BeginKeywords = ["fun", "macro"], End = @"\b\B", Contains = [methodTitle] },

            // Deviation from highlight.js, where `Foo:` of `Foo::Bar` is a symbol: namespace qualifiers are swallowed.
            // Only the first position of a word is tried: otherwise, each character of a long word that is not followed
            // by `::` would rescan the rest of the word.
            new Mode { Begin = CommonModes.RunStart(@"\w", "a-zA-Z") + CommonModes.IdentRe + "::" },
            new Mode { Scope = "symbol", Begin = CommonModes.RunStart(@"\w", "a-zA-Z_") + CommonModes.UnderscoreIdentRe + @"(!|\?)?:" },

            new Mode
            {
                Scope = "number",
                Variants =
                [
                    new Mode { Begin = @"\b0b([01_]+)" + IntSuffix },
                    new Mode { Begin = @"\b0o([0-7_]+)" + IntSuffix },
                    new Mode { Begin = @"\b0x([A-Fa-f0-9_]+)" + IntSuffix },
                    // Deviation from highlight.js, whose `(?!_)` backtracks to a shorter number (`100_u8` is `10` followed
                    // by `0_u8`): the number cannot stop before a digit either, so an integer with a suffix is left to the
                    // next variant.
                    new Mode { Begin = @"\b([1-9][0-9_]*[0-9]|[0-9])(\.[0-9][0-9_]*)?([eE]_?[-+]?[0-9_]*)?" + FloatSuffix + "(?![_0-9])" },
                    new Mode { Begin = @"\b([1-9][0-9_]*|0)" + IntSuffix },
                ],
            },
        ];

        // The braces come after the expansions (`{{ }}`, `{% %}`) and before the regexp container, which would otherwise
        // take the `{`.
        subst.Contains = [expansion, substBraces, .. defaultContains[1..]];
        substBraces.Contains = subst.Contains;
        expansion.Contains = defaultContains[1..];

        return new Mode
        {
            KeywordPattern = IdentRe,
            Keywords = keywords,
            Contains = defaultContains,
        };
    }

    private static Mode[] RecursiveParentheses(string begin, string end)
    {
        var mode = new Mode { Begin = begin, End = end };
        mode.Contains = [mode];
        return [mode];
    }

    // Deviation from highlight.js, whose heredoc ends at the first line made of a single word (`<<-EOS` ends at a line
    // `  hello`): it ends at the line of its own identifier. IndentedLineStartRe is `^\s*`, without rescanning a run of
    // blank lines from each of its line starts.
    private static Mode Heredoc(string begin) => new()
    {
        Begin = begin,
        End = CommonModes.IndentedLineStartRe + @"(\w+)$",
        EndSameAsBegin = true,
    };
}
