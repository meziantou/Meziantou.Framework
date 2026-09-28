using System.Collections.Frozen;
using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Dart
{
    private static readonly string[] BuiltInTypes =
    [
        // dart:core
        "Comparable", "DateTime", "Duration", "Function", "Iterable", "Iterator", "List", "Map", "Match", "Object",
        "Pattern", "RegExp", "Set", "Stopwatch", "String", "StringBuffer", "StringSink", "Symbol", "Type", "Uri", "bool",
        "double", "int", "num",
        // dart:html
        "Element", "ElementList",
    ];

    private static readonly string[] BasicKeywords =
    [
        "abstract", "as", "assert", "async", "await", "base", "break", "case", "catch", "class", "const", "continue",
        "covariant", "default", "deferred", "do", "dynamic", "else", "enum", "export", "extends", "extension",
        "external", "factory", "false", "final", "finally", "for", "Function", "get", "hide", "if", "implements",
        "import", "in", "interface", "is", "late", "library", "mixin", "new", "null", "on", "operator", "part",
        "required", "rethrow", "return", "sealed", "set", "show", "static", "super", "switch", "sync", "this", "throw",
        "true", "try", "typedef", "var", "void", "when", "while", "with", "yield",
    ];

    private static readonly string[] OtherBuiltIns =
    [
        // dart:core
        "Never", "Null", "dynamic", "print",
        // dart:html
        "document", "querySelector", "querySelectorAll", "window",
    ];

    private static readonly string[] BuiltIns = [.. BuiltInTypes, .. BuiltInTypes.Select(type => type + "?"), .. OtherBuiltIns];

    // The words highlighted as keywords: `Function` and `dynamic` are also built-ins, and the later group wins.
    private static readonly FrozenSet<string> KeywordSet = BasicKeywords.Except(BuiltIns, StringComparer.Ordinal).ToFrozenSet(StringComparer.Ordinal);

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var subst = new Mode
        {
            Scope = "subst",
            Begin = @"\$[A-Za-z0-9_]+",
        };

        var number = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode { Match = @"\b[0-9][0-9_]*(?:\.[0-9][0-9_]*)?(?:[eE][+-]?[0-9][0-9_]*)?\b" },
                new Mode { Match = @"\b0[xX][0-9A-Fa-f][0-9A-Fa-f_]*\b" },
            ],
        };

        var bracedSubst = new Mode
        {
            Scope = "subst",
            Begin = @"\$\{",
            End = @"\}",
            Keywords = Engine.Keywords.FromWords(["true", "false", "null", "this", "is", "new", "super"]),
        };

        Mode[] interpolated = [CommonModes.BackslashEscape, subst, bracedSubst];
        var @string = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode { Begin = "r'''", End = "'''" },
                new Mode { Begin = "r\"\"\"", End = "\"\"\"" },
                new Mode { Begin = "r'", End = "'", Illegal = @"\n" },
                new Mode { Begin = "r\"", End = "\"", Illegal = @"\n" },
                new Mode { Begin = "'''", End = "'''", Contains = interpolated },
                new Mode { Begin = "\"\"\"", End = "\"\"\"", Contains = interpolated },
                new Mode { Begin = "'", End = "'", Illegal = @"\n", Contains = interpolated },
                new Mode { Begin = "\"", End = "\"", Illegal = @"\n", Contains = interpolated },
            ],
        };
        bracedSubst.Contains = [number, @string];

        var docTag = new Mode
        {
            Scope = "doctag",
            Begin = "[ ]*(?=(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):)",
            End = "(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):",
            ExcludeBegin = true,
        };

        // Deviation from highlight.js, which highlights the whole `/** */` comment, delimiters included, as Markdown:
        // the `*` that starts each line became a list bullet and the closing `*/` an emphasis. The comment is
        // highlighted like `///` comments instead: the text of each line, after its leading `* `, is Markdown.
        var blockDocComment = new Mode
        {
            Scope = "comment",
            Begin = @"/\*\*(?!/) ?",
            End = @"\*/",
            Contains =
            [
                new Mode { Begin = @"^[ \t]*\*(?!/) ?" },
                docTag,
                new Mode
                {
                    SubLanguage = "markdown",
                    Begin = @"(?![ \t]*\*/)[^\n]",
                    End = @"$|(?=\*/)",
                    Contains = [docTag],
                },
            ],
        };

        return new Mode
        {
            // Deviation from highlight.js, whose pattern is `[A-Za-z][A-Za-z0-9_]*\??`: a word cannot start with `_`
            // there, so the end of a private name was highlighted when it is a keyword (`_show`, `_in`, `_print`).
            KeywordPattern = @"[A-Za-z_][A-Za-z0-9_]*\??",
            KeywordValidator = ValidateKeyword,
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["keyword"] = BasicKeywords,
                ["built_in"] = BuiltIns,
            }),
            Contains =
            [
                @string,
                blockDocComment,
                CommonModes.Comment(@"/{3,} ?", "$", extraContains:
                [
                    new Mode
                    {
                        SubLanguage = "markdown",
                        Begin = ".",
                        End = "$",
                    },
                ]),
                CommonModes.CLineCommentMode,
                CommonModes.CBlockCommentMode,
                new Mode
                {
                    Scope = "class",
                    BeginKeywords = ["class", "interface"],
                    End = @"\{",
                    ExcludeEnd = true,
                    Contains =
                    [
                        // Deviation from highlight.js, which highlights `with` (mixins) and the `class` of
                        // `interface class` as class names.
                        new Mode { BeginKeywords = ["extends", "implements", "with", "class"] },
                        CommonModes.UnderscoreTitleMode,
                    ],
                },
                number,
                new Mode
                {
                    Scope = "meta",
                    Begin = "@[A-Za-z]+",
                },
                new Mode { Begin = "=>" },
            ],
        };
    }

    // Deviation from highlight.js: a member named like a keyword (`http.get()`, `dialog.show()`, `..set()`) is not a
    // keyword. A spread (`...var rest`) is not a member access.
    private static bool ValidateKeyword(string input, int index, ReadOnlySpan<char> word)
    {
        if (index is 0 || input[index - 1] is not '.' || !KeywordSet.GetAlternateLookup<ReadOnlySpan<char>>().Contains(word))
            return true;

        return index >= 3 && input.AsSpan(index - 3, 3) is "...";
    }
}
