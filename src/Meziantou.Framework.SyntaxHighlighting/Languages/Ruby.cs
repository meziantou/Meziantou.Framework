using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Ruby
{
    private const string MethodNameRe = @"([a-zA-Z_]\w*[!?=]?|[-+~]@|<<|>>|=~|===?|<=>|[<>]=?|\*\*|[-/+%^&*~`|]|\[\]=?)";

    // CamelCase, optionally ending in capitals (`HTTPServer`, `JSONAPI`).
    private const string ClassNameRe = @"(?:\b([A-Z]+[a-z0-9]+)+|\b([A-Z]+[a-z0-9]+)+[A-Z]+)";
    private const string ClassNameWithNamespaceRe = ClassNameRe + @"(::\w+)*";

    private const string PercentLiteralRe = "%[qQwWxiI]?";

    private const string Decimal = "[1-9](_?[0-9])*|0";
    private const string Digits = "[0-9](_?[0-9])*";

    // Very popular built-ins that one might even assume are actual keywords.
    private static readonly string[] PseudoKeywords = ["include", "extend", "prepend", "public", "private", "protected", "raise", "throw"];

    private static readonly string[] ReservedKeywords =
    [
        "alias", "and", "begin", "BEGIN", "break", "case", "class", "defined", "do", "else", "elsif", "end", "END",
        "ensure", "for", "if", "in", "module", "next", "not", "or", "redo", "require", "rescue", "retry", "return", "then",
        "undef", "unless", "until", "when", "while", "yield",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["variable.constant"] = ["__FILE__", "__LINE__", "__ENCODING__"],
            ["variable.language"] = ["self", "super"],
            ["keyword"] = [.. ReservedKeywords, .. PseudoKeywords],
            ["built_in"] = ["proc", "lambda", "attr_accessor", "attr_reader", "attr_writer", "define_method", "private_constant", "module_function"],
            ["literal"] = ["true", "false", "nil"],
        });

        var yardDocTag = new Mode { Scope = "doctag", Begin = "@[A-Za-z]+" };
        var irbObject = new Mode { Begin = "#<", End = ">" };
        Mode[] comments =
        [
            CommonModes.Comment("#", "$", extraContains: [yardDocTag]),
            CommonModes.Comment("^=begin", "^=end", extraContains: [yardDocTag]),
            // highlight.js's MATCH_NOTHING_RE: everything after __END__ is a comment.
            CommonModes.Comment("^__END__", @"\b\B"),
        ];

        var subst = new Mode { Scope = "subst", Begin = @"#\{", End = @"\}", Keywords = keywords, KeywordValidator = IsKeyword };

        // Deviation from highlight.js: braces inside an interpolation (`#{items.map { |i| i.name }}`) do not end it.
        var substBraces = new Mode { Begin = @"\{", End = @"\}", Keywords = keywords, KeywordValidator = IsKeyword };

        // Deviation from highlight.js: a percent literal or a regexp can contain balanced delimiters
        // (`%w(a (b) c)`, `%r{\d{3}}`), which do not end it.
        var nestedParentheses = new Mode { Begin = @"\(", End = @"\)" };
        var nestedBrackets = new Mode { Begin = @"\[", End = @"\]" };
        var nestedBraces = new Mode { Begin = @"\{", End = @"\}" };
        var nestedAngleBrackets = new Mode { Begin = "<", End = ">" };
        foreach (var nested in new[] { nestedParentheses, nestedBrackets, nestedBraces, nestedAngleBrackets })
        {
            nested.Contains = [CommonModes.BackslashEscape, subst, nested];
        }

        var strings = new Mode
        {
            Scope = "string",
            Contains = [CommonModes.BackslashEscape, subst],
            Variants =
            [
                // Deviation from highlight.js: single-quoted strings are not interpolated.
                new Mode { Begin = "'", End = "'", Contains = [CommonModes.BackslashEscape] },
                new Mode { Begin = "\"", End = "\"" },
                new Mode { Begin = "`", End = "`" },
                // Deviation from highlight.js: `%i[]` and `%I[]` (arrays of symbols) are literals too.
                new Mode { Begin = PercentLiteralRe + @"\(", End = @"\)", Contains = [CommonModes.BackslashEscape, subst, nestedParentheses] },
                new Mode { Begin = PercentLiteralRe + @"\[", End = @"\]", Contains = [CommonModes.BackslashEscape, subst, nestedBrackets] },
                new Mode { Begin = PercentLiteralRe + @"\{", End = @"\}", Contains = [CommonModes.BackslashEscape, subst, nestedBraces] },
                new Mode { Begin = PercentLiteralRe + "<", End = ">", Contains = [CommonModes.BackslashEscape, subst, nestedAngleBrackets] },
                new Mode { Begin = PercentLiteralRe + "/", End = "/" },
                new Mode { Begin = PercentLiteralRe + "%", End = "%" },
                new Mode { Begin = PercentLiteralRe + "-", End = "-" },
                new Mode { Begin = PercentLiteralRe + @"\|", End = @"\|" },
                // `\B` suppresses `?`-sequences where `?` ends an identifier, as in `func?4`.
                new Mode { Begin = @"\B\?(\\\d{1,3})" },
                new Mode { Begin = @"\B\?(\\x[A-Fa-f0-9]{1,2})" },
                new Mode { Begin = @"\B\?(\\u\{?[A-Fa-f0-9]{1,6}\}?)" },
                new Mode { Begin = @"\B\?(\\M-\\C-|\\M-\\c|\\c\\M-|\\M-|\\C-\\M-)[\x20-\x7e]" },
                new Mode { Begin = @"\B\?\\(c|C-)[\x20-\x7e]" },
                new Mode { Begin = @"\B\?\\?\S" },
                // Heredocs. The lookahead makes sure that the whole heredoc is there, terminator included.
                // Deviation from highlight.js: a double-quoted or backquoted terminator (`<<~"EOS"`) is a heredoc
                // too, not only a bare or single-quoted one.
                new Mode
                {
                    Begin = @"<<[-~]?['""`]?(?=(\w+)(?=\W)[^\n]*\n(?:[^\n]*\n)*?\s*\1\b)",
                    Contains =
                    [
                        new Mode
                        {
                            Begin = @"(\w+)",
                            End = @"(\w+)",
                            EndSameAsBegin = true,
                            Contains = [CommonModes.BackslashEscape, subst],
                        },
                    ],
                },
            ],
        };

        var number = new Mode
        {
            Scope = "number",
            Variants =
            [
                // decimal integer/float, optionally exponential or rational, optionally imaginary
                new Mode { Begin = @"\b(" + Decimal + @")(\.(" + Digits + @"))?([eE][+-]?(" + Digits + @")|r)?i?\b" },
                // explicit decimal/binary/octal/hexadecimal integer, optionally rational and/or imaginary
                new Mode { Begin = @"\b0[dD][0-9](_?[0-9])*r?i?\b" },
                new Mode { Begin = @"\b0[bB][0-1](_?[0-1])*r?i?\b" },
                new Mode { Begin = @"\b0[oO][0-7](_?[0-7])*r?i?\b" },
                new Mode { Begin = @"\b0[xX][0-9a-fA-F](_?[0-9a-fA-F])*r?i?\b" },
                // 0-prefixed implicit octal integer, optionally rational and/or imaginary
                new Mode { Begin = @"\b0(_?[0-7])+r?i?\b" },
            ],
        };

        var parameters = new Mode
        {
            Variants =
            [
                new Mode { Match = @"\(\)" },
                new Mode
                {
                    Scope = "params",
                    Begin = @"\(",
                    End = @"(?=\))",
                    ExcludeBegin = true,
                    EndsParent = true,
                    Keywords = keywords,
                    KeywordValidator = IsKeyword,
                },
            ],
        };

        var includeExtend = new Mode
        {
            BeginParts = [@"(include|extend)\s+", ClassNameWithNamespaceRe],
            BeginScope = new Dictionary<int, string> { [2] = "title.class" },
            Keywords = keywords,
            KeywordValidator = IsKeyword,
        };

        var classDefinition = new Mode
        {
            Variants =
            [
                new Mode { BeginParts = [@"class\s+", ClassNameWithNamespaceRe, @"\s+<\s+", ClassNameWithNamespaceRe] },
                new Mode { BeginParts = [@"\b(class|module)\s+", ClassNameWithNamespaceRe] },
            ],
            BeginScope = new Dictionary<int, string>
            {
                [2] = "title.class",
                [4] = "title.class.inherited",
            },
            Keywords = keywords,
            KeywordValidator = IsKeyword,
        };

        var methodDefinition = new Mode
        {
            // Deviations from highlight.js: `def` must be a whole word (not the end of `undef`), and the receiver of a
            // singleton method (`def self.create`) is not the method name.
            BeginParts = [@"\bdef", @"\s+", @"(?:self(?=\.))?", @"\.?", MethodNameRe],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "variable.language",
                [5] = "title.function",
            },
            Contains = [parameters],
        };

        var regexpContainer = new Mode
        {
            Begin = "(" + CommonModes.ReStartersRe + @"|unless)\s*",
            Keywords = Engine.Keywords.FromWords(["unless"]),
            KeywordValidator = IsKeyword,
            Contains =
            [
                new Mode
                {
                    Scope = "regexp",
                    Contains = [CommonModes.BackslashEscape, subst],
                    Illegal = @"\n",
                    Variants =
                    [
                        new Mode { Begin = "/", End = "/[a-z]*" },
                        new Mode { Begin = @"%r\{", End = @"\}[a-z]*", Contains = [CommonModes.BackslashEscape, subst, nestedBraces] },
                        new Mode { Begin = @"%r\(", End = @"\)[a-z]*", Contains = [CommonModes.BackslashEscape, subst, nestedParentheses] },
                        new Mode { Begin = "%r!", End = "![a-z]*" },
                        new Mode { Begin = @"%r\[", End = @"\][a-z]*", Contains = [CommonModes.BackslashEscape, subst, nestedBrackets] },
                    ],
                },
                irbObject,
                .. comments,
            ],
        };

        Mode[] defaultContains =
        [
            strings,
            classDefinition,
            includeExtend,
            // Object creation
            new Mode
            {
                BeginParts = [ClassNameWithNamespaceRe, @"\.new[. (]"],
                BeginScope = new Dictionary<int, string> { [1] = "title.class" },
            },
            // Upper case constant
            new Mode { Scope = "variable.constant", Match = @"\b[A-Z][A-Z_0-9]+\b" },
            // Class reference
            new Mode { Scope = "title.class", Match = ClassNameRe },
            methodDefinition,
            // Swallow namespace qualifiers before symbols. Only the first position of a word is tried: otherwise,
            // each character of a long word that is not followed by `::` would rescan the rest of the word.
            new Mode { Begin = CommonModes.RunStart(@"\w", "a-zA-Z") + CommonModes.IdentRe + "::" },
            new Mode { Scope = "symbol", Begin = CommonModes.RunStart(@"\w", "a-zA-Z_") + CommonModes.UnderscoreIdentRe + @"(!|\?)?:" },
            // Deviation from highlight.js: the `::` of a namespace (`Net::HTTP`) is not a symbol.
            new Mode
            {
                Scope = "symbol",
                Begin = @"(?<!:):(?![\s:])",
                Contains = [strings, new Mode { Begin = MethodNameRe }],
            },
            number,
            // The negative lookaheads prevent false matches like `@ident@` or `$ident$`.
            new Mode
            {
                Scope = "variable",
                Begin = @"(\$\W)|((\$|@@?)(\w+))(?=[^@$?])(?![A-Za-z])(?![@$?'])",
            },
            // Deviation from highlight.js: block parameters must follow `{` or `do`, so that `a || b`, `a ||= b` and
            // `a | b` are not parameter lists.
            new Mode
            {
                Scope = "params",
                Begin = @"\|(?<=(?:\{|\bdo)[ \t]*\|)(?!=)",
                End = @"\|",
                ExcludeBegin = true,
                ExcludeEnd = true,
                Keywords = keywords,
                KeywordValidator = IsKeyword,
            },
            regexpContainer,
            irbObject,
            .. comments,
        ];

        // The braces come first: the regexp container would otherwise take the `{`.
        subst.Contains = [substBraces, .. defaultContains];
        substBraces.Contains = [substBraces, .. defaultContains];
        parameters.Contains = defaultContains;

        // `>>`, `?>`, `irb(main):001:0>` and RVM prompts (`2.7.2 :001 >`).
        const string SimplePrompt = "[>?]>";
        const string DefaultPrompt = @"[\w#]+\(\w+\):\d+:\d+[>*]";
        const string RvmPrompt = @"(\w+-)?\d+\.\d+\.\d+(p\d+)?[^\d][^>]+>";

        return new Mode
        {
            Keywords = keywords,
            KeywordValidator = IsKeyword,
            Illegal = @"/\*",
            Contains =
            [
                // highlight.js only accepts a shebang at the start of the document.
                new Mode { Scope = "meta", Begin = @"\A#![ ]*/.*\bruby\b.*", End = "$" },
                new Mode
                {
                    Begin = CommonModes.IndentedLineStartRe + "=>",
                    Starts = new Mode { End = "$", Contains = defaultContains },
                },
                new Mode
                {
                    Scope = "meta.prompt",
                    Begin = "^(" + SimplePrompt + "|" + DefaultPrompt + "|" + RvmPrompt + ")(?=[ ])",
                    Starts = new Mode { End = "$", Keywords = keywords, KeywordValidator = IsKeyword, Contains = defaultContains },
                },
                irbObject,
                .. comments,
                .. defaultContains,
            ],
        };
    }

    /// <summary>
    /// Deviation from highlight.js: a word called as a method (`x.nil?`, `self.class`, `params.require(:user)`) is not a
    /// keyword. A range (`1..nil`) is not a method call.
    /// </summary>
    private static bool IsKeyword(string input, int index, ReadOnlySpan<char> word)
    {
        return index is 0 || input[index - 1] is not '.' || (index >= 2 && input[index - 2] is '.');
    }
}
