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
                new Mode { Begin = @"(-?)\b([\d']+(\.[\d']*)?|\.[\d']+)(u|U|l|L|ul|UL|f|F|b|B)" },
                new Mode { Begin = @"(-?)(\b0[xX][a-fA-F0-9']+|(\b[\d']+(\.[\d']*)?|\.[\d']+)([eE][-+]?[\d']+)?)" },
            ],
        };

        var rawString = new Mode
        {
            Scope = "string",
            Begin = "\"\"\"(\"*)(?!\")(.|\\n)*?\"\"\"\\1",
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
            Illegal = "::",
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
                    BeginKeywords = ["class", "interface"],
                    End = "[{;=]",
                    Illegal = @"[^\s:,]",
                    Contains =
                    [
                        new() { BeginKeywords = ["where", "class"] },
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
                    BeginKeywords = ["record"],
                    End = "[{;=]",
                    Illegal = @"[^\s:]",
                    Contains =
                    [
                        titleMode,
                        genericModifier,
                        CommonModes.CLineCommentMode,
                        CommonModes.CBlockCommentMode,
                    ],
                },
                new()
                {
                    Scope = "meta",
                    Begin = @"^\s*\[(?=[\w])",
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
                    Begin = "(" + typeIdentRe + @"\s+)+" + CommonModes.IdentRe + @"\s*(<[^=]+>\s*)?\(",
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
                            Begin = CommonModes.IdentRe + @"\s*(<[^=]+>\s*)?\(",
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
