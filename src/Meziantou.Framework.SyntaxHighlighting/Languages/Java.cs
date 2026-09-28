using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Java
{
    private const string IdentStartChars = @"\u00C0-\u02B8a-zA-Z_$";
    private const string IdentChars = IdentStartChars + "0-9";
    private const string IdentRe = "[" + IdentStartChars + "][" + IdentChars + "]*";

    private const string DecimalDigitsRe = "[0-9](?:_*[0-9])*";
    private const string FractionRe = @"\.(?:" + DecimalDigitsRe + ")";
    private const string HexDigitsRe = "[0-9a-fA-F](?:_*[0-9a-fA-F])*";

    // An identifier with optional type arguments, nested up to two levels (`Map<String, List<Integer>>`).
    private static readonly string GenericIdentRe = IdentRe + RecurRegex("(?:<" + IdentRe + @"~~~(?:\s*,\s*" + IdentRe + "~~~)*>)?", depth: 2);

    // highlight.js lists `const ` with a trailing space, so it never highlights the reserved word `const`.
    private static readonly string[] MainKeywords =
    [
        "synchronized", "abstract", "private", "var", "static", "if", "const", "for", "while", "strictfp", "finally",
        "protected", "import", "native", "final", "void", "enum", "else", "break", "transient", "catch", "instanceof",
        "volatile", "case", "assert", "package", "default", "public", "try", "switch", "continue", "throws", "module",
        "requires", "exports", "do", "sealed", "yield", "permits", "goto", "when",
    ];

    private static readonly string[] BuiltIns = ["super", "this"];

    private static readonly string[] Literals = ["false", "true", "null"];

    private static readonly string[] Types = ["char", "boolean", "long", "float", "int", "byte", "short", "double"];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    /// <summary>
    /// The number literals of Java, shared with Kotlin (highlight.js's <c>NUMERIC</c> mode of java.js).
    /// </summary>
    internal static Mode CreateNumberMode() => new()
    {
        Scope = "number",
        Variants =
        [
            // Decimal floating point with an exponent
            new Mode { Begin = @"(?:\b(?:" + DecimalDigitsRe + ")(?:(?:" + FractionRe + @")|\.)?|(?:" + FractionRe + @"))[eE][+-]?(?:" + DecimalDigitsRe + @")[fFdD]?\b" },
            // Decimal floating point without an exponent
            new Mode { Begin = @"\b(?:" + DecimalDigitsRe + ")(?:(?:" + FractionRe + @")[fFdD]?\b|\.(?:[fFdD]\b)?)" },
            new Mode { Begin = "(?:" + FractionRe + @")[fFdD]?\b" },
            new Mode { Begin = @"\b(?:" + DecimalDigitsRe + @")[fFdD]\b" },
            // Hexadecimal floating point
            new Mode { Begin = @"\b0[xX](?:(?:" + HexDigitsRe + @")\.?|(?:" + HexDigitsRe + @")?\.(?:" + HexDigitsRe + @"))[pP][+-]?(?:" + DecimalDigitsRe + @")[fFdD]?\b" },
            // Decimal integer
            new Mode { Begin = @"\b(?:0|[1-9](?:_*[0-9])*)[lL]?\b" },
            // Hexadecimal integer
            new Mode { Begin = @"\b0[xX](?:" + HexDigitsRe + @")[lL]?\b" },
            // Octal integer
            new Mode { Begin = @"\b0(?:_*[0-7])*[lL]?\b" },
            // Binary integer
            new Mode { Begin = @"\b0[bB][01](?:_*[01])*[lL]?\b" },
        ],
    };

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = MainKeywords,
            ["literal"] = Literals,
            ["type"] = Types,
            ["built_in"] = BuiltIns,
        });

        var numbers = CreateNumberMode();

        var annotationArguments = new Mode
        {
            Begin = @"\(",
            End = @"\)",
        };
        annotationArguments.Contains = [Mode.Self];

        var annotation = new Mode
        {
            Scope = "meta",
            Begin = "@" + IdentRe,
            Contains = [annotationArguments],
        };

        var recordParameters = new Mode
        {
            Scope = "params",
            Begin = @"\(",
            End = @"\)",
            Keywords = keywords,
            Contains = [CommonModes.CBlockCommentMode],
            EndsParent = true,
        };

        return new Mode
        {
            Keywords = keywords,
            Illegal = "</|#",
            Contains =
            [
                CommonModes.Comment(@"/\*\*", @"\*/", extraContains:
                [
                    // Eat the `@` of e-mail addresses so they are not doc tags
                    new Mode { Begin = CommonModes.RunStart(@"\w") + @"\w+@" },
                    new Mode { Scope = "doctag", Begin = "@[A-Za-z]+" },
                ]),
                new Mode
                {
                    Begin = @"import java\.[a-z]+\.",
                    Keywords = Engine.Keywords.FromWords(["import"]),
                },
                CommonModes.CLineCommentMode,
                CommonModes.CBlockCommentMode,
                new Mode
                {
                    Scope = "string",
                    Begin = "\"\"\"",
                    End = "\"\"\"",
                    Contains = [CommonModes.BackslashEscape],
                },
                CommonModes.AposStringMode,
                CommonModes.QuoteStringMode,
                new Mode
                {
                    BeginParts = [@"\b(?:class|interface|enum|extends|implements|new)", @"\s+", IdentRe],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "keyword",
                        [3] = "title.class",
                    },
                },
                new Mode
                {
                    // Exception for hyphenated keywords
                    Scope = "keyword",
                    Match = "non-sealed",
                },
                new Mode
                {
                    // Only try the first position of a word where the pattern can start: otherwise, each character of a
                    // long word would rescan the rest of it. highlight.js uses `(?!else)`, which also rejects any word
                    // that starts with `else` and then matches from its second character (`e<type>lsewhere</type> x = 1`).
                    BeginParts = [CommonModes.RunStart(IdentChars, IdentStartChars) + @"(?!else\b)" + IdentRe, @"\s+", IdentRe, @"\s+", "=(?!=)"],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "type",
                        [3] = "variable",
                        [5] = "operator",
                    },
                },
                new Mode
                {
                    BeginParts = ["record", @"\s+", IdentRe],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "keyword",
                        [3] = "title.class",
                    },
                    Contains = [recordParameters, CommonModes.CLineCommentMode, CommonModes.CBlockCommentMode],
                },
                new Mode
                {
                    // Expression keywords prevent `keyword Name(...)` from being recognized as a function definition
                    BeginKeywords = ["new", "throw", "return", "else"],
                },
                new Mode
                {
                    // highlight.js does not allow an array return type (`String[] split(String s)`).
                    BeginParts = [CommonModes.RunStart(IdentChars, IdentStartChars) + "(?:" + GenericIdentRe + @"(?:\s*\[\s*\])*\s+)", CommonModes.UnderscoreIdentRe, @"\s*(?=\()"],
                    BeginScope = new Dictionary<int, string>
                    {
                        [2] = "title.function",
                    },
                    Keywords = keywords,
                    Contains =
                    [
                        new Mode
                        {
                            Scope = "params",
                            Begin = @"\(",
                            End = @"\)",
                            Keywords = keywords,
                            Contains =
                            [
                                annotation,
                                CommonModes.AposStringMode,
                                CommonModes.QuoteStringMode,
                                numbers,
                                CommonModes.CBlockCommentMode,
                            ],
                        },
                        CommonModes.CLineCommentMode,
                        CommonModes.CBlockCommentMode,
                    ],
                },
                numbers,
                annotation,
            ],
        };
    }

    /// <summary>
    /// Replaces each <c>~~~</c> of <paramref name="pattern"/> with the pattern itself, <paramref name="depth"/> times,
    /// and then with nothing, to match nested constructs up to a given depth.
    /// </summary>
    private static string RecurRegex(string pattern, int depth)
    {
        if (depth is -1)
            return "";

        return pattern.Replace("~~~", RecurRegex(pattern, depth - 1), StringComparison.Ordinal);
    }
}
