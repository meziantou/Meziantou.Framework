using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Elixir
{
    private const string IdentRe = @"[a-zA-Z_][a-zA-Z0-9_.]*(!|\?)?";
    private const string MethodRe = @"[a-zA-Z_]\w*[!?=]?|[-+~]@|<<|>>|=~|===?|<=>|[<>]=?|\*\*|[-/+%^&*~`|]|\[\]=?";
    private const string SigilDelimitersRe = "[/|([{<\"']";

    // The modifiers of a sigil other than a regex (`~w(a b)a`). highlight.js only includes them for regexes.
    private const string ModifiersRe = "[a-zA-Z]*";

    private static readonly string[] ReservedKeywords =
    [
        "after", "alias", "and", "case", "catch", "cond", "defstruct", "defguard", "do", "else", "end", "fn", "for", "if",
        "import", "in", "not", "or", "quote", "raise", "receive", "require", "reraise", "rescue", "try", "unless", "unquote",
        "unquote_splicing", "use", "when", "with",

        // Not in highlight.js, which lists defstruct and defguard but not these definitions.
        "defexception", "defguardp", "defoverridable",
    ];

    private static readonly string[] Literals = ["false", "nil", "true"];

    // The delimiters of a sigil, as (begin, end) patterns. The heredoc delimiters are not in highlight.js, which ends
    // `~s"""` at the second quote; they must come before the single-character quotes.
    private static readonly (string Begin, string End)[] SigilDelimiters =
    [
        ("\"\"\"", "\"\"\""),
        ("'''", "'''"),
        ("\"", "\""),
        ("'", "'"),
        ("/", "/"),
        (@"\|", @"\|"),
        (@"\(", @"\)"),
        (@"\[", @"\]"),
        (@"\{", @"\}"),
        ("<", ">"),
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = ReservedKeywords,
            ["literal"] = Literals,
        });

        var subst = new Mode
        {
            Scope = "subst",
            Begin = @"#\{",
            End = @"\}",
            Keywords = keywords,
            KeywordPattern = IdentRe,
        };

        var number = new Mode
        {
            Scope = "number",
            Begin = @"(\b0o[0-7_]+)|(\b0b[01_]+)|(\b0x[0-9a-fA-F_]+)|(-?\b[0-9][0-9_]*(\.[0-9_]+([eE][-+]?[0-9]+)?)?)",
        };

        var backslashEscape = new Mode
        {
            Scope = "char.escape",
            Match = @"\\[\s\S]",
        };

        static Mode EscapeSigilEnd(string end) => new()
        {
            Scope = "char.escape",
            Begin = @"\\" + end,
        };

        static Mode[] Delimiters(Func<string, IList<Mode>> contains, string endSuffix) =>
        [
            .. SigilDelimiters.Select(delimiter => new Mode
            {
                Begin = delimiter.Begin,
                End = delimiter.End + endSuffix,
                Contains = contains(delimiter.End),
            }),
        ];

        var lowercaseSigil = new Mode
        {
            Scope = "string",
            Begin = "~[a-z](?=" + SigilDelimitersRe + ")",
            Contains = Delimiters(end => [EscapeSigilEnd(end), backslashEscape, subst], ModifiersRe),
        };

        var uppercaseSigil = new Mode
        {
            Scope = "string",
            Begin = "~[A-Z](?=" + SigilDelimitersRe + ")",
            Contains = Delimiters(end => [EscapeSigilEnd(end)], ModifiersRe),
        };

        var regexSigil = new Mode
        {
            Scope = "regex",
            Variants =
            [
                new Mode
                {
                    Begin = "~r(?=" + SigilDelimitersRe + ")",
                    Contains = Delimiters(end => [EscapeSigilEnd(end), backslashEscape, subst], "[uismxfU]{0,7}"),
                },
                new Mode
                {
                    Begin = "~R(?=" + SigilDelimitersRe + ")",
                    Contains = Delimiters(end => [EscapeSigilEnd(end)], "[uismxfU]{0,7}"),
                },
            ],
        };

        // The `~S` variants have no escapes nor interpolation: a variant without contains inherits those of its mode, so
        // the contains are set on each variant rather than on the mode.
        Mode[] stringContains = [CommonModes.BackslashEscape, subst];
        var @string = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode { Begin = "\"\"\"", End = "\"\"\"", Contains = stringContains },
                new Mode { Begin = "'''", End = "'''", Contains = stringContains },
                new Mode { Begin = "~S\"\"\"", End = "\"\"\"" },
                new Mode { Begin = "~S\"", End = "\"" },
                new Mode { Begin = "~S'''", End = "'''" },
                new Mode { Begin = "~S'", End = "'" },
                new Mode { Begin = "'", End = "'", Contains = stringContains },
                new Mode { Begin = "\"", End = "\"", Contains = stringContains },
            ],
        };

        var title = new Mode
        {
            Scope = "title",
            Begin = IdentRe,
            EndsParent = true,
        };

        var function = new Mode
        {
            Scope = "function",
            // defdelegate is not in highlight.js.
            BeginKeywords = ["def", "defp", "defmacro", "defmacrop", "defdelegate"],
            // The mode is ended by the title.
            End = @"\B\b",
            Contains = [title],
        };

        var @class = new Mode
        {
            Scope = "class",
            BeginKeywords = ["defimpl", "defmodule", "defprotocol", "defrecord"],
            End = @"\bdo\b|$|;",
            Contains = [title],
        };

        Mode[] defaultContains =
        [
            @string,
            regexSigil,
            uppercaseSigil,
            lowercaseSigil,
            CommonModes.HashCommentMode,
            @class,
            function,
            new Mode { Begin = "::" },
            new Mode
            {
                Scope = "symbol",
                Begin = @":(?![\s:])",
                Contains =
                [
                    @string,
                    new Mode { Begin = MethodRe },
                ],
            },
            new Mode
            {
                Scope = "symbol",
                // Only try the first position of a word: otherwise, each character of a long word that is not followed
                // by `:` would rescan the rest of the word.
                Begin = CommonModes.RunStart("a-zA-Z0-9_.", "a-zA-Z_") + IdentRe + ":(?!:)",
            },
            new Mode
            {
                // Usage of a module, struct, etc.
                Scope = "title.class",
                Begin = @"(\b[A-Z][a-zA-Z0-9_]+)",
            },
            number,
            new Mode
            {
                Scope = "variable",
                Begin = @"(\$\W)|((\$|@@?)(\w+))",
            },
        ];
        subst.Contains = defaultContains;

        return new Mode
        {
            Keywords = keywords,
            KeywordPattern = IdentRe,
            Contains = defaultContains,
        };
    }
}
