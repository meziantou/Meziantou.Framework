using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Kotlin
{
    private static readonly string[] ReservedKeywords =
    [
        "abstract", "as", "val", "var", "vararg", "get", "set", "class", "object", "open", "private", "protected", "public",
        "noinline", "crossinline", "dynamic", "final", "enum", "if", "else", "do", "while", "for", "when", "throw", "try",
        "catch", "finally", "import", "package", "is", "in", "fun", "override", "companion", "reified", "inline",
        "lateinit", "init", "interface", "annotation", "data", "sealed", "internal", "infix", "operator", "out", "by",
        "constructor", "super", "tailrec", "where", "const", "inner", "suspend", "typealias", "external", "expect",
        "actual",
    ];

    private static readonly string[] BuiltIns = ["Byte", "Short", "Char", "Int", "Long", "Boolean", "Float", "Double", "Void", "Unit", "Nothing"];

    private static readonly string[] Literals = ["true", "false", "null"];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = ReservedKeywords,
            ["built_in"] = BuiltIns,
            ["literal"] = Literals,
        });

        var keywordsWithLabel = new Mode
        {
            Scope = "keyword",
            Begin = @"\b(break|continue|return|this)\b",
            Starts = new Mode
            {
                Contains = [new Mode { Scope = "symbol", Begin = @"@\w+" }],
            },
        };

        var label = new Mode
        {
            Scope = "symbol",
            // Only try the first position of a word: otherwise, each character of a long word would rescan the rest of it.
            Begin = CommonModes.RunStart(@"\w", "a-zA-Z_") + CommonModes.UnderscoreIdentRe + "@",
        };

        // String templates
        var substitution = new Mode
        {
            Scope = "subst",
            Begin = @"\$\{",
            End = @"\}",
        };

        var variable = new Mode
        {
            Scope = "variable",
            Begin = @"\$" + CommonModes.UnderscoreIdentRe,
        };

        var strings = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode
                {
                    Begin = "\"\"\"",
                    End = "\"\"\"(?=[^\"])",
                    Contains = [variable, substitution],
                },
                new Mode
                {
                    Begin = "'",
                    End = "'",
                    Illegal = @"\n",
                    Contains = [CommonModes.BackslashEscape],
                },
                new Mode
                {
                    Begin = "\"",
                    End = "\"",
                    Illegal = @"\n",
                    Contains = [CommonModes.BackslashEscape, variable, substitution],
                },
            ],
        };
        substitution.Contains = [CommonModes.CNumberMode, strings];

        var annotationUseSite = new Mode
        {
            Scope = "meta",
            Begin = @"@(?:file|property|field|get|set|receiver|param|setparam|delegate)\s*:(?:\s*" + CommonModes.UnderscoreIdentRe + ")?",
        };

        var annotationArguments = new Mode
        {
            Begin = @"\(",
            End = @"\)",
        };
        annotationArguments.Contains = [strings, Mode.Self];

        var annotation = new Mode
        {
            Scope = "meta",
            Begin = "@" + CommonModes.UnderscoreIdentRe,
            Contains = [annotationArguments],
        };

        var nestedComment = CommonModes.Comment(@"/\*", @"\*/", extraContains: [CommonModes.CBlockCommentMode]);

        // highlight.js ends type parameters at the first `>`, so `<T : Comparable<T>>` would end after `<T`.
        var nestedAngleBrackets = new Mode
        {
            Begin = "<",
            End = ">",
        };
        nestedAngleBrackets.Contains = [Mode.Self];

        var parenthesizedType = new Mode();
        parenthesizedType.Variants =
        [
            new Mode
            {
                Scope = "type",
                Begin = CommonModes.UnderscoreIdentRe,
            },
            new Mode
            {
                Begin = @"\(",
                End = @"\)",
                Contains = [parenthesizedType],
            },
        ];

        return new Mode
        {
            Keywords = keywords,
            Contains =
            [
                CommonModes.Comment(@"/\*\*", @"\*/", extraContains: [new Mode { Scope = "doctag", Begin = "@[A-Za-z]+" }]),
                CommonModes.CLineCommentMode,
                nestedComment,
                keywordsWithLabel,
                label,
                annotationUseSite,
                annotation,
                new Mode
                {
                    Scope = "function",
                    // A functional interface (`fun interface Runnable { }`) is not a function. highlight.js makes the
                    // whole line a function declaration.
                    Begin = @"(?<!\.)\bfun(?!\.)(?=\b|\s)(?!\s+interface\b)",
                    End = "[(]|$",
                    ReturnBegin = true,
                    ExcludeEnd = true,
                    Keywords = keywords,
                    Contains =
                    [
                        new Mode
                        {
                            // highlight.js does not support backticked names (fun `adds two numbers`()), which are
                            // common in tests.
                            Begin = "(?:" + CommonModes.RunStart(@"\w", "a-zA-Z_") + CommonModes.UnderscoreIdentRe + "|`[^`\n]+`)" + @"\s*\(",
                            ReturnBegin = true,
                            Contains =
                            [
                                CommonModes.UnderscoreTitleMode,
                                new Mode
                                {
                                    Scope = "title",
                                    Begin = "`[^`\n]+`",
                                },
                            ],
                        },
                        new Mode
                        {
                            Scope = "type",
                            Begin = "<",
                            End = ">",
                            Keywords = Engine.Keywords.FromWords(["reified"]),
                            Contains = [nestedAngleBrackets],
                        },
                        new Mode
                        {
                            Scope = "params",
                            Begin = @"\(",
                            End = @"\)",
                            EndsParent = true,
                            Keywords = keywords,
                            Contains =
                            [
                                new Mode
                                {
                                    Begin = ":",
                                    End = @"[=,\/]",
                                    EndsWithParent = true,
                                    Contains = [parenthesizedType, CommonModes.CLineCommentMode, nestedComment],
                                },
                                CommonModes.CLineCommentMode,
                                nestedComment,
                                annotationUseSite,
                                annotation,
                                strings,
                                CommonModes.CNumberMode,
                            ],
                        },
                        nestedComment,
                    ],
                },
                new Mode
                {
                    // highlight.js does not include object declarations (`object Singleton { }`).
                    BeginParts = ["class|interface|trait|object", @"\s+", CommonModes.UnderscoreIdentRe],
                    BeginScope = new Dictionary<int, string>
                    {
                        [3] = "title.class",
                    },
                    // highlight.js highlights the unscoped first part with the keywords of the enclosing mode, which do not
                    // include `trait`; this engine uses the ones of this mode.
                    Keywords = Engine.Keywords.FromWords(["class", "interface", "object"]),
                    End = @"[:\{(]|$",
                    ExcludeEnd = true,
                    Illegal = "extends implements",
                    Contains =
                    [
                        new Mode { BeginKeywords = ["public", "protected", "internal", "private", "constructor"] },
                        CommonModes.UnderscoreTitleMode,
                        new Mode
                        {
                            Scope = "type",
                            Begin = "<",
                            End = ">",
                            ExcludeBegin = true,
                            ExcludeEnd = true,
                            Contains = [nestedAngleBrackets],
                        },
                        new Mode
                        {
                            Scope = "type",
                            Begin = @"[,:]\s*",
                            End = @"[<\(,){\s]|$",
                            ExcludeBegin = true,
                            ReturnEnd = true,
                        },
                        annotationUseSite,
                        annotation,
                    ],
                },
                strings,
                new Mode
                {
                    Scope = "meta",
                    Begin = "^#!/usr/bin/env",
                    End = "$",
                    Illegal = @"\n",
                },
                Java.CreateNumberMode(),
            ],
        };
    }
}
