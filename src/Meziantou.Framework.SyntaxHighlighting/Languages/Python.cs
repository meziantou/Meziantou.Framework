using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Python
{
    // highlight.js uses /[\p{XID_Start}_]\p{XID_Continue}*/u, which .NET does not support.
    private const string IdentRe = @"[\p{L}\p{Nl}_][\p{L}\p{Nl}\p{Mn}\p{Mc}\p{Nd}\p{Pc}]*";

    private const string DigitPartRe = "[0-9](_?[0-9])*";
    private const string PointFloatRe = @"(\b(" + DigitPartRe + @"))?\.(" + DigitPartRe + @")|\b(" + DigitPartRe + @")\.";

    private static readonly string[] ReservedWords =
    [
        "and", "as", "assert", "async", "await", "break", "case", "class", "continue", "def", "del", "elif", "else",
        "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "match", "nonlocal", "not",
        "or", "pass", "raise", "return", "try", "while", "with", "yield",
    ];

    private static readonly string[] BuiltIns =
    [
        "__import__", "abs", "all", "any", "ascii", "bin", "bool", "breakpoint", "bytearray", "bytes", "callable", "chr",
        "classmethod", "compile", "complex", "delattr", "dict", "dir", "divmod", "enumerate", "eval", "exec", "filter",
        "float", "format", "frozenset", "getattr", "globals", "hasattr", "hash", "help", "hex", "id", "input", "int",
        "isinstance", "issubclass", "iter", "len", "list", "locals", "map", "max", "memoryview", "min", "next", "object",
        "oct", "open", "ord", "pow", "print", "property", "range", "repr", "reversed", "round", "set", "setattr", "slice",
        "sorted", "staticmethod", "str", "sum", "super", "tuple", "type", "vars", "zip",
    ];

    private static readonly string[] Literals = ["__debug__", "Ellipsis", "False", "None", "NotImplemented", "True"];

    private static readonly string[] Types =
    [
        "Any", "Callable", "Coroutine", "Dict", "List", "Literal", "Generic", "Optional", "Sequence", "Set", "Tuple",
        "Type", "Union",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = ReservedWords,
            ["built_in"] = BuiltIns,
            ["literal"] = Literals,
            ["type"] = Types,
        });
        const string KeywordPattern = @"[A-Za-z]\w+|__\w+__";

        var prompt = new Mode
        {
            Scope = "meta",
            Begin = @"^(>>>|\.\.\.) ",
        };

        // Deviation from highlight.js: a replacement field can contain braces (a nested replacement field in the
        // format spec, `f"{x:{width}}"`, or a dict or set display), which must not end it.
        var nestedBraces = new Mode
        {
            Begin = @"\{",
            End = @"\}",
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
            KeywordValidator = ValidateKeyword,
            Illegal = "#",
        };

        var subst = new Mode
        {
            Scope = "subst",
            Begin = @"\{",
            End = @"\}",
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
            KeywordValidator = ValidateKeyword,
            Illegal = "#",
        };

        var literalBracket = new Mode { Begin = @"\{\{" };

        const string Prefix = "([uU]|[bB]|[rR]|[bB][rR]|[rR][bB])";
        const string FormattedPrefix = "([fF][rR]|[rR][fF]|[fF])";
        var strings = new Mode
        {
            Scope = "string",
            Contains = [CommonModes.BackslashEscape],
            Variants =
            [
                new Mode { Begin = Prefix + "?'''", End = "'''", Contains = [CommonModes.BackslashEscape, prompt] },
                new Mode { Begin = Prefix + "?\"\"\"", End = "\"\"\"", Contains = [CommonModes.BackslashEscape, prompt] },
                new Mode { Begin = FormattedPrefix + "'''", End = "'''", Contains = [CommonModes.BackslashEscape, prompt, literalBracket, subst] },
                new Mode { Begin = FormattedPrefix + "\"\"\"", End = "\"\"\"", Contains = [CommonModes.BackslashEscape, prompt, literalBracket, subst] },
                new Mode { Begin = "([uU]|[rR])'", End = "'" },
                new Mode { Begin = "([uU]|[rR])\"", End = "\"" },
                new Mode { Begin = "([bB]|[bB][rR]|[rR][bB])'", End = "'" },
                new Mode { Begin = "([bB]|[bB][rR]|[rR][bB])\"", End = "\"" },
                new Mode { Begin = FormattedPrefix + "'", End = "'", Contains = [CommonModes.BackslashEscape, literalBracket, subst] },
                new Mode { Begin = FormattedPrefix + "\"", End = "\"", Contains = [CommonModes.BackslashEscape, literalBracket, subst] },
                CommonModes.AposStringMode,
                CommonModes.QuoteStringMode,
            ],
        };

        // Whitespace after a number is only needed if its absence would change the tokenization, so a number can be
        // directly followed by a keyword (`1if x else 2`).
        var lookahead = @"\b|" + string.Join('|', ReservedWords);
        var numbers = new Mode
        {
            Scope = "number",
            Variants =
            [
                // exponentfloat and pointfloat, optionally imaginary
                new Mode { Begin = @"(\b(" + DigitPartRe + ")|(" + PointFloatRe + "))[eE][+-]?(" + DigitPartRe + ")[jJ]?(?=" + lookahead + ")" },
                new Mode { Begin = "(" + PointFloatRe + ")[jJ]?" },

                // decinteger, bininteger, octinteger, hexinteger, optionally "long" (Python 2)
                new Mode { Begin = @"\b([1-9](_?[0-9])*|0+(_?0)*)[lLjJ]?(?=" + lookahead + ")" },
                new Mode { Begin = @"\b0[bB](_?[01])+[lL]?(?=" + lookahead + ")" },
                new Mode { Begin = @"\b0[oO](_?[0-7])+[lL]?(?=" + lookahead + ")" },
                new Mode { Begin = @"\b0[xX](_?[0-9a-fA-F])+[lL]?(?=" + lookahead + ")" },

                // imagnumber
                new Mode { Begin = @"\b(" + DigitPartRe + ")[jJ](?=" + lookahead + ")" },
            ],
        };

        var typeComment = new Mode
        {
            Scope = "comment",
            Begin = "(?=# type:)",
            End = "$",
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
            KeywordValidator = ValidateKeyword,
            Contains =
            [
                // prevents keywords from coloring `type`
                new Mode { Begin = "# type:" },
                // a comment within a type comment includes no keywords
                new Mode { Begin = "#", End = @"\b\B", EndsWithParent = true },
            ],
        };

        var parameters = new Mode
        {
            Scope = "params",
            Variants =
            [
                // Excludes the parameters of functions without parameters
                new Mode { ClearScope = true, Begin = @"\(\s*\)", Skip = true },
                new Mode
                {
                    Begin = @"\(",
                    End = @"\)",
                    ExcludeBegin = true,
                    ExcludeEnd = true,
                    Keywords = keywords,
                    KeywordPattern = KeywordPattern,
                    KeywordValidator = ValidateKeyword,
                    Contains = [Mode.Self, prompt, numbers, strings, CommonModes.HashCommentMode],
                },
            ],
        };

        // Deviation from highlight.js: the type parameters of a generic function (`def first[T](items: list[T])`) are
        // not the end of the declaration, so the parameters that follow them are still highlighted.
        var typeParameters = new Mode
        {
            Begin = @"\[",
            End = @"\]",
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
            KeywordValidator = ValidateKeyword,
        };
        typeParameters.Contains = [typeParameters];

        nestedBraces.Contains = [strings, numbers, prompt, nestedBraces];
        subst.Contains = [strings, numbers, prompt, nestedBraces];

        return new Mode
        {
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
            KeywordValidator = ValidateKeyword,
            Illegal = @"(<\/|\?)|=>",
            Contains =
            [
                prompt,
                numbers,
                new Mode
                {
                    // very common convention
                    Scope = "variable.language",
                    Match = @"\bself\b",
                },
                // eats "if" prior to a string so that it is not labeled as an f-string
                new Mode { BeginKeywords = ["if"] },
                new Mode { Match = @"\bor\b", Scope = "keyword" },
                strings,
                typeComment,
                CommonModes.HashCommentMode,
                new Mode
                {
                    BeginParts = [@"\bdef", @"\s+", IdentRe],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "keyword",
                        [3] = "title.function",
                    },
                    Contains = [typeParameters, parameters],
                },
                new Mode
                {
                    Variants =
                    [
                        new Mode { BeginParts = [@"\bclass", @"\s+", IdentRe, @"\s*", @"\(\s*", IdentRe, @"\s*\)"] },
                        new Mode { BeginParts = [@"\bclass", @"\s+", IdentRe] },
                    ],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "keyword",
                        [3] = "title.class",
                        [6] = "title.class.inherited",
                    },
                },
                new Mode
                {
                    Scope = "meta",
                    Begin = @"^[\t ]*@",
                    End = "(?=#)|$",
                    Contains = [numbers, parameters, strings],
                },
            ],
        };
    }

    /// <summary>
    /// Deviations from highlight.js, which highlights these words wherever they appear.
    /// </summary>
    private static bool ValidateKeyword(string input, int index, ReadOnlySpan<char> word)
    {
        // An attribute (`re.match`, `self.id`, `"".format`) is not the keyword or the built-in of the same name. The
        // names of the typing module stay types (`typing.Optional`).
        if (index > 0 && input[index - 1] is '.')
            return IsType(word);

        // `match` and `case` are soft keywords: they are only keywords at the start of a `match` statement or a `case`
        // clause, and are common names otherwise (`match = re.match(...)`).
        if (word is "match" or "case")
            return IsSoftKeywordStatement(input, index, word.Length);

        return true;
    }

    private static bool IsType(ReadOnlySpan<char> word)
    {
        foreach (var type in Types)
        {
            if (word.SequenceEqual(type))
                return true;
        }

        return false;
    }

    private static bool IsSoftKeywordStatement(string input, int index, int length)
    {
        for (var i = index - 1; i >= 0 && input[i] is not '\n'; i--)
        {
            if (input[i] is not (' ' or '\t'))
                return false;
        }

        var next = index + length;
        if (next >= input.Length || input[next] is not (' ' or '\t'))
            return false;

        while (next < input.Length && input[next] is ' ' or '\t')
        {
            next++;
        }

        return next < input.Length && (char.IsLetterOrDigit(input[next]) || input[next] is '_' or '(' or '[' or '{' or '"' or '\'' or '-' or '*' or '~');
    }
}
