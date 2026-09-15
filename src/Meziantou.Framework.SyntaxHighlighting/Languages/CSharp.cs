using System.Text.RegularExpressions;
using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static partial class CSharp
{
    private static readonly string[] BuiltInKeywords =
    [
        "bool","byte","char","decimal","delegate","double","dynamic","enum","float","int",
        "long","nint","nuint","object","sbyte","short","string","ulong","uint","ushort",
    ];

    private static readonly string[] FunctionModifiers =
    [
        "public","private","protected","static","internal","protected","abstract","async",
        "extern","override","unsafe","virtual","new","sealed","partial",
    ];

    private static readonly string[] LiteralKeywords =
    [
        "default","false","null","true",
    ];

    private static readonly string[] NormalKeywords =
    [
        "abstract","as","base","break","case","catch","class","const","continue","do","else",
        "event","explicit","extern","finally","fixed","for","foreach","goto","if","implicit",
        "in","interface","internal","is","lock","namespace","new","operator","out","override",
        "params","private","protected","public","readonly","record","ref","return","scoped",
        "sealed","sizeof","stackalloc","static","struct","switch","this","throw","try","typeof",
        "unchecked","unsafe","using","virtual","void","volatile","while",
    ];

    private static readonly string[] ContextualKeywords =
    [
        "add","allows","alias","and","ascending","args","async","await","by","closed",
        "descending","dynamic","equals","extension","field","file","from","get","global","group",
        "init","into","join","let","nameof","not","notnull","on","or","orderby","partial",
        "record","remove","required","scoped","select","set","unmanaged","value","var","when",
        "where","with","yield",
    ];

    private static Keywords BuildKeywords() => Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["keyword"] = [.. NormalKeywords, .. ContextualKeywords],
        ["built_in"] = BuiltInKeywords,
        ["literal"] = LiteralKeywords,
    });

    // Contextual keywords whose meaning depends on appearing inside a LINQ query
    // expression. Outside that context, the same words are valid identifiers
    // (parameter names, variable names, etc.) and must not be highlighted.
    private static readonly HashSet<string> LinqContextualKeywords = new(StringComparer.Ordinal)
    {
        "from", "where", "select", "let", "into", "join", "on", "equals",
        "by", "group", "orderby", "ascending", "descending",
    };

    [GeneratedRegex(@"^\s+\w+\s+in\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: -1)]
    private static partial Regex FromInPattern();

    [GeneratedRegex(@"^\s+\w+\s*:", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: -1)]
    private static partial Regex GenericConstraintPattern();

    [GeneratedRegex(@"\bfrom\s+\w+\s+in\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: -1)]
    private static partial Regex PrecedingFromPattern();

    private static bool ValidateKeyword(string input, int index, ReadOnlySpan<char> word)
    {
        // Universal: a keyword preceded by `.` is a member name, not a keyword
        // (covers `obj.from`, `x?.where`, `o.class`, etc.).
        if (index > 0 && input[index - 1] == '.')
            return false;

        if (!LinqContextualKeywords.Contains(word.ToString()))
            return true;

        var after = input.AsSpan(index + word.Length);

        if (word is "from")
            return FromInPattern().IsMatch(after);

        if (word is "where")
        {
            // Generic constraint: `where T : ...` is valid outside LINQ context.
            if (GenericConstraintPattern().IsMatch(after))
                return true;
            return HasPrecedingLinqFrom(input, index);
        }

        // select, let, into, join, on, equals, by, group, orderby,
        // ascending, descending: only valid inside a LINQ query.
        return HasPrecedingLinqFrom(input, index);
    }

    // Walks back from `index` to the previous statement boundary (`;`, `{`, `}`)
    // and looks for a `from <id> in` pattern within that slice. The bounded
    // search prevents matches across unrelated statements.
    private static bool HasPrecedingLinqFrom(string input, int index)
    {
        var start = 0;
        for (var i = index - 1; i >= 0; i--)
        {
            var c = input[i];
            if (c is ';' or '{' or '}')
            {
                start = i + 1;
                break;
            }
        }
        return PrecedingFromPattern().IsMatch(input.AsSpan(start, index - start));
    }

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = BuildKeywords();

        var titleMode = new Mode { Scope = "title", Begin = @"[a-zA-Z](\.?\w)*" };
        var plainTitleMode = new Mode { Scope = "title", Begin = CommonModes.IdentRe };

        var numbers = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode { Begin = @"\b(0b[01']+)" },
                // A number that starts inside a run of digits and separators can also be matched from the
                // first boundary of the run, so only that one is tried: otherwise, each position of a long run
                // would rescan it.
                new Mode { Begin = @"(-?)\b(?=[\d'.])(?:\G|(?<!\b[\d'](?:(?!\G)[\d'])*?))([\d']+(\.[\d']*)?|\.[\d']+)(u|U|l|L|ul|UL|f|F|b|B)" },
                new Mode { Begin = @"(-?)(\b0[xX][a-fA-F0-9']+|(\b[\d']+(\.[\d']*)?|\.[\d']+)([eE][-+]?[\d']+)?)" },
            ],
        };

        // The delimiter is limited to 64 quotes: from each position of a longer run of quotes, the
        // unbounded `"*` would rescan the rest of the run.
        var rawString = new Mode
        {
            Scope = "string",
            Begin = "\"\"\"((?>\"{0,61}))(?!\")(.|\\n)*?\"\"\"\\1",
        };

        var verbatimStringEscape = new Mode { Begin = "\"\"" };
        var verbatimString = new Mode
        {
            Scope = "string",
            Begin = "@\"",
            End = "\"",
            Contains = [verbatimStringEscape],
        };
        var verbatimStringNoLf = new Mode
        {
            Scope = "string",
            Begin = "@\"",
            End = "\"",
            Illegal = @"\n",
            Contains = [verbatimStringEscape],
        };

        var subst = new Mode
        {
            Scope = "subst",
            Begin = @"\{",
            End = @"\}",
            Keywords = keywords,
            KeywordValidator = ValidateKeyword,
        };
        var substNoLf = new Mode
        {
            Scope = "subst",
            Begin = @"\{",
            End = @"\}",
            Illegal = @"\n",
            Keywords = keywords,
            KeywordValidator = ValidateKeyword,
        };

        var braceEscapeOpen = new Mode { Begin = @"\{\{" };
        var braceEscapeClose = new Mode { Begin = @"\}\}" };

        // Substitution hole inside a single-`$` raw interpolated string: `{expr}`.
        var rawSubst = new Mode
        {
            Scope = "subst",
            Begin = @"\{",
            End = @"\}",
            Keywords = keywords,
            KeywordValidator = ValidateKeyword,
        };
        // Substitution hole inside a double-`$$` raw interpolated string: `{{expr}}`.
        // With two leading dollars, single `{` / `}` are literal and require no escape.
        var rawSubstDouble = new Mode
        {
            Scope = "subst",
            Begin = @"\{\{",
            End = @"\}\}",
            Keywords = keywords,
            KeywordValidator = ValidateKeyword,
        };

        // Raw interpolated string with a single `$`. Quote count is captured in
        // group 1 and locked via EndSameAsBegin so an N-quote opener requires an
        // N-quote closer, allowing the body to contain shorter runs of `"` freely.
        var rawInterpolatedString = new Mode
        {
            Scope = "string",
            Begin = "\\$\"\"\"(\"*)(?!\")",
            End = "\"\"\"(\"*)(?!\")",
            EndSameAsBegin = true,
            Contains =
            [
                braceEscapeOpen,
                braceEscapeClose,
                rawSubst,
            ],
        };

        // Raw interpolated string with `$$`.
        var rawInterpolatedStringDouble = new Mode
        {
            Scope = "string",
            Begin = "\\$\\$\"\"\"(\"*)(?!\")",
            End = "\"\"\"(\"*)(?!\")",
            EndSameAsBegin = true,
            Contains =
            [
                rawSubstDouble,
            ],
        };

        var interpolatedString = new Mode
        {
            Scope = "string",
            Begin = @"\$""",
            End = "\"",
            Illegal = @"\n",
            Contains =
            [
                braceEscapeOpen,
                braceEscapeClose,
                CommonModes.BackslashEscape,
                substNoLf,
            ],
        };
        var interpolatedVerbatimString = new Mode
        {
            Scope = "string",
            Begin = @"\$@""",
            End = "\"",
            Contains =
            [
                braceEscapeOpen,
                braceEscapeClose,
                verbatimStringEscape,
                subst,
            ],
        };
        var interpolatedVerbatimStringNoLf = new Mode
        {
            Scope = "string",
            Begin = @"\$@""",
            End = "\"",
            Illegal = @"\n",
            Contains =
            [
                braceEscapeOpen,
                braceEscapeClose,
                verbatimStringEscape,
                substNoLf,
            ],
        };

        var cBlockCommentNoLf = new Mode
        {
            Scope = "comment",
            Begin = @"/\*",
            End = @"\*/",
            Illegal = @"\n",
            Contains = [],
        };

        // Wire the recursive interpolation graph.
        subst.Contains =
        [
            rawInterpolatedStringDouble,
            rawInterpolatedString,
            interpolatedVerbatimString,
            interpolatedString,
            verbatimString,
            CommonModes.AposStringMode,
            CommonModes.QuoteStringMode,
            numbers,
            CommonModes.CBlockCommentMode,
        ];
        substNoLf.Contains =
        [
            rawInterpolatedStringDouble,
            rawInterpolatedString,
            interpolatedVerbatimStringNoLf,
            interpolatedString,
            verbatimStringNoLf,
            CommonModes.AposStringMode,
            CommonModes.QuoteStringMode,
            numbers,
            cBlockCommentNoLf,
        ];
        rawSubst.Contains = subst.Contains;
        rawSubstDouble.Contains = subst.Contains;

        var stringMode = new Mode
        {
            Variants =
            [
                rawInterpolatedStringDouble,
                rawInterpolatedString,
                rawString,
                interpolatedVerbatimString,
                interpolatedString,
                verbatimString,
                CommonModes.AposStringMode,
                CommonModes.QuoteStringMode,
            ],
        };

        var genericModifier = new Mode
        {
            Begin = "<",
            End = ">",
            Contains =
            [
                new() { BeginKeywords = ["in", "out"] },
                titleMode,
            ],
        };
        var typeIdentRe = CommonModes.IdentRe + @"(<" + CommonModes.IdentRe + @"(\s*,\s*" + CommonModes.IdentRe + @")*>)?(\[\])?";

        // Generic arguments before a parameter list: `<T>`, `<Dictionary<string, List<int>>>`. They may be
        // nested up to three levels, and cannot contain `=` (which would be an assignment or a lambda).
        // Unlike a plain `<[^=]+>`, they cannot run past an unbalanced `<` or `>`, so a text full of `<`
        // is not rescanned up to the next `=` from each identifier.
        const string GenericArgumentsRe = @"<(?:[^<>=]|<(?:[^<>=]|<[^<>=]*>)*>)+>";

        // An identifier followed by its parameter list, optionally with generic arguments. The function
        // modes only need the position of the match (they return to it), so the generic arguments can be
        // balanced.
        var identifierWithParametersRe = CommonModes.RunStart(@"\w", "a-zA-Z") + CommonModes.IdentRe + @"\s*(" + GenericArgumentsRe + @"\s*)?\(";

        // A declaration is a sequence of types followed by that identifier. A type that follows another
        // type of the sequence can also be matched from the start of the sequence, so it can never be the
        // leftmost match and is skipped: otherwise, each type of a long sequence would rescan it. The
        // previous type does not count when the scan starts after it (e.g. after a preprocessor directive).
        var typeIdentNoCaptureRe = CommonModes.IdentRe + @"(?:<" + CommonModes.IdentRe + @"(?:\s*,\s*" + CommonModes.IdentRe + @")*>)?(?:\[\])?";
        var functionDeclarationRe = CommonModes.RunStart(@"\w", "a-zA-Z") + @"(?:\G|(?<!" + typeIdentNoCaptureRe + @"(?:(?!\G)\s)+))(" + typeIdentRe + @"\s+)+" + identifierWithParametersRe;

        // A class, struct or record with a parameter list (`class Foo<T>(T value)`, `record Person(string Name)`,
        // `record struct Point(int X, int Y)`) declares a primary constructor. The class and record modes do not
        // support parameter lists, so they give way to the function mode, which highlights these declarations the
        // same way as when they have modifiers (`public record Person(string Name)`).
        var notPrimaryConstructorRe = @"(?!\s+(?:(?:class|struct)\s+)?" + CommonModes.IdentRe + @"\s*(?:" + GenericArgumentsRe + @"\s*)?\()";

        // Parentheses inside a parameter list, e.g. the arguments of an attribute
        // (`record Person([property: JsonPropertyName("name")] string Name)`): the list does not end there.
        var nestedParameterParentheses = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Keywords = keywords,
            KeywordValidator = ValidateKeyword,
        };
        nestedParameterParentheses.Contains = [stringMode, numbers, CommonModes.CBlockCommentMode, nestedParameterParentheses];

        // The `new()` generic constraint (`class Foo<T> where T : new()`): its parentheses are not illegal.
        var newConstraint = new Mode { Begin = @"\bnew\s*\(\s*\)", Keywords = Keywords.FromWords(["new"]) };
        var atIdentifier = new Mode { Begin = "@" + CommonModes.IdentRe };

        var xmlDocComment = CommonModes.Comment("///", "$",
            returnBegin: true,
            extraContains:
            [
                new()
                {
                    Scope = "doctag",
                    Variants =
                    [
                        new Mode { Begin = "///" },
                        new Mode { Begin = "<!--|-->" },
                        new Mode { Begin = "</?", End = ">" },
                    ],
                },
            ]);

        return new Mode
        {
            Keywords = keywords,
            KeywordValidator = ValidateKeyword,
            Contains =
            [
                xmlDocComment,
                CommonModes.CLineCommentMode,
                CommonModes.CBlockCommentMode,
                new()
                {
                    Scope = "meta",
                    Begin = "#",
                    End = "$",
                    Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["keyword"] = "if else elif endif define undef warning error line region endregion pragma checksum",
                    }),
                },
                stringMode,
                numbers,
                new()
                {
                    Begin = @"(?<!\.)\b(class|interface)(?!\.)(?=\b|\s)" + notPrimaryConstructorRe,
                    Keywords = Keywords.FromWords(["class", "interface"]),
                    End = "[{;=]",
                    Illegal = @"[^\s:,]",
                    Contains =
                    [
                        new() { BeginKeywords = ["where", "class"] },
                        newConstraint,
                        titleMode,
                        genericModifier,
                        CommonModes.CLineCommentMode,
                        CommonModes.CBlockCommentMode,
                    ],
                },
                new()
                {
                    BeginKeywords = ["namespace"],
                    End = "[{;=]",
                    Illegal = @"[^\s:]",
                    Contains =
                    [
                        titleMode,
                        CommonModes.CLineCommentMode,
                        CommonModes.CBlockCommentMode,
                    ],
                },
                new()
                {
                    Begin = @"(?<!\.)\b(record)(?!\.)(?=\b|\s)" + notPrimaryConstructorRe,
                    Keywords = Keywords.FromWords(["record"]),
                    End = "[{;=]",
                    Illegal = @"[^\s:,]",
                    Contains =
                    [
                        new() { BeginKeywords = ["where", "class", "struct"] },
                        newConstraint,
                        titleMode,
                        genericModifier,
                        CommonModes.CLineCommentMode,
                        CommonModes.CBlockCommentMode,
                    ],
                },
                new()
                {
                    Scope = "meta",
                    Begin = CommonModes.IndentedLineStartRe + @"\[(?=[\w])",
                    ExcludeBegin = true,
                    End = @"\]",
                    ExcludeEnd = true,
                    Contains =
                    [
                        new() { Scope = "string", Begin = "\"", End = "\"" },
                    ],
                },
                new() { BeginKeywords = ["new", "return", "throw", "await", "else"] },
                new()
                {
                    Scope = "function",
                    Begin = functionDeclarationRe,
                    ReturnBegin = true,
                    End = @"\s*[{;=]",
                    ExcludeEnd = true,
                    Keywords = keywords,
                    KeywordValidator = ValidateKeyword,
                    Contains =
                    [
                        new() { BeginKeywords = FunctionModifiers },
                        new()
                        {
                            Begin = identifierWithParametersRe,
                            ReturnBegin = true,
                            Contains =
                            [
                                plainTitleMode,
                                genericModifier,
                            ],
                        },
                        new() { Match = @"\(\)" },
                        new()
                        {
                            Scope = "params",
                            Begin = @"\(",
                            End = @"\)",
                            ExcludeBegin = true,
                            ExcludeEnd = true,
                            Keywords = keywords,
                            KeywordValidator = ValidateKeyword,
                            Contains =
                            [
                                stringMode,
                                numbers,
                                CommonModes.CBlockCommentMode,
                                nestedParameterParentheses,
                            ],
                        },
                        CommonModes.CLineCommentMode,
                        CommonModes.CBlockCommentMode,
                    ],
                },
                atIdentifier,
            ],
        };
    }
}
