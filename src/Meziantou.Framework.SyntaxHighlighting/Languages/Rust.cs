using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Rust
{
    // `r#` makes a keyword usable as an identifier (`r#type`).
    private const string RawIdentifierRe = "(?:r#)?";
    private const string IdentRe = RawIdentifierRe + CommonModes.IdentRe;
    private const string UnderscoreIdentRe = RawIdentifierRe + CommonModes.UnderscoreIdentRe;
    // The name bound by `let`, but not a pattern such as `Some(x)`, `Point { x, y }` or `Enum::Variant(x)`.
    private const string BindingRe = UnderscoreIdentRe + @"\b(?!\s*(?:[({]|::))";
    private const string NumberSuffixRe = "(?:[ui](?:8|16|32|64|128|size)|f(?:32|64))?";

    private static readonly string[] ReservedKeywords =
    [
        "abstract", "as", "async", "await", "become", "box", "break", "const", "continue", "crate", "do", "dyn", "else",
        "enum", "extern", "false", "final", "fn", "for", "if", "impl", "in", "let", "loop", "macro", "match", "mod",
        "move", "mut", "override", "priv", "pub", "ref", "return", "self", "Self", "static", "struct", "super", "trait",
        "true", "try", "type", "typeof", "union", "unsafe", "unsized", "use", "virtual", "where", "while", "yield",
    ];

    private static readonly string[] Literals = ["true", "false", "Some", "None", "Ok", "Err"];

    private static readonly string[] BuiltIns =
    [
        // functions
        "drop",
        // traits
        "Copy", "Send", "Sized", "Sync", "Drop", "Fn", "FnMut", "FnOnce", "ToOwned", "Clone", "Debug", "PartialEq",
        "PartialOrd", "Eq", "Ord", "AsRef", "AsMut", "Into", "From", "Default", "Iterator", "Extend", "IntoIterator",
        "DoubleEndedIterator", "ExactSizeIterator", "SliceConcatExt", "ToString",
        // macros
        "assert!", "assert_eq!", "bitflags!", "bytes!", "cfg!", "col!", "concat!", "concat_idents!", "debug_assert!",
        "debug_assert_eq!", "env!", "eprintln!", "panic!", "file!", "format!", "format_args!", "include_bytes!",
        "include_str!", "line!", "local_data_key!", "module_path!", "option_env!", "print!", "println!", "select!",
        "stringify!", "try!", "unimplemented!", "unreachable!", "vec!", "write!", "writeln!", "macro_rules!",
        "assert_ne!", "debug_assert_ne!",
    ];

    private static readonly string[] Types =
    [
        "i8", "i16", "i32", "i64", "i128", "isize", "u8", "u16", "u32", "u64", "u128", "usize", "f32", "f64", "str",
        "char", "bool", "Box", "Option", "Result", "String", "Vec",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var functionInvoke = new Mode
        {
            Scope = "title.function.invoke",
            // Keywords and literals followed by `(` (`pub(crate)`, `Some(x)`, `match (a, b)`) are not calls.
            Begin = @"\b(?!(?:" + string.Join('|', ReservedKeywords.Union(Literals, StringComparer.Ordinal)) + @")\b)" + IdentRe + @"(?=\s*\()",
        };

        var strings = new Mode
        {
            Scope = "string",
            Variants =
            [
                // The delimiter is captured at begin and the end is searched from inside the string, rather than
                // matching the whole string at once: an unterminated raw string would otherwise rescan the rest of
                // the document from every place it can start.
                new Mode
                {
                    Begin = @"\b[bc]?r(#*)""",
                    End = @"""(#*)",
                    EndSameAsBegin = true,
                },
                new Mode
                {
                    Begin = "b?'",
                    End = "'",
                    Contains =
                    [
                        new Mode
                        {
                            Scope = "char.escape",
                            Match = @"\\(?:x[0-9A-Fa-f]{2}|u\{[0-9A-Fa-f_]{1,6}\}|['""\\\w])",
                        },
                    ],
                },
            ],
        };

        var numbers = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode { Begin = @"\b0b[01_]+" + NumberSuffixRe },
                new Mode { Begin = @"\b0o[0-7_]+" + NumberSuffixRe },
                new Mode { Begin = @"\b0x[A-Fa-f0-9_]+" + NumberSuffixRe },
                new Mode { Begin = @"\b\d[\d_]*(?:\.[0-9_]+)?(?:[eE][+-]?[0-9_]+)?" + NumberSuffixRe },
            ],
        };

        return new Mode
        {
            KeywordPattern = RawIdentifierRe + @"[a-zA-Z_]\w*!?",
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["type"] = Types,
                ["keyword"] = ReservedKeywords,
                ["literal"] = Literals,
                ["built_in"] = BuiltIns,
            }),
            Illegal = "</",
            Contains =
            [
                CommonModes.CLineCommentMode,
                CommonModes.Comment(@"/\*", @"\*/", extraContains: [Mode.Self]),
                new Mode
                {
                    Scope = "string",
                    Begin = "[bc]?\"",
                    End = "\"",
                    Contains = [CommonModes.BackslashEscape],
                },
                new Mode
                {
                    // A lifetime or a label; the lookahead leaves `'a'` to the character literal.
                    Scope = "symbol",
                    Begin = @"'[a-zA-Z_][a-zA-Z0-9_]*(?!')",
                },
                strings,
                numbers,
                new Mode
                {
                    BeginParts = [@"\bfn", @"\s+", UnderscoreIdentRe],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "keyword",
                        [3] = "title.function",
                    },
                },
                new Mode
                {
                    Scope = "meta",
                    Begin = @"#!?\[",
                    End = @"\]",
                    Contains =
                    [
                        new Mode
                        {
                            Scope = "string",
                            Begin = "\"",
                            End = "\"",
                            Contains = [CommonModes.BackslashEscape],
                        },
                    ],
                },
                new Mode
                {
                    BeginParts = [@"\blet", @"\s+", @"(?:mut\s+)?", BindingRe],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "keyword",
                        [3] = "keyword",
                        [4] = "variable",
                    },
                },
                // Must come before the `impl`/`for` rule below
                new Mode
                {
                    BeginParts = [@"\bfor", @"\s+", UnderscoreIdentRe, @"\s+", @"in\b"],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "keyword",
                        [3] = "variable",
                        [5] = "keyword",
                    },
                },
                new Mode
                {
                    BeginParts = [@"\btype", @"\s+", UnderscoreIdentRe],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "keyword",
                        [3] = "title.class",
                    },
                },
                new Mode
                {
                    BeginParts = [@"\b(?:trait|enum|struct|union|impl|for)", @"\s+", UnderscoreIdentRe],
                    BeginScope = new Dictionary<int, string>
                    {
                        [1] = "keyword",
                        [3] = "title.class",
                    },
                },
                new Mode
                {
                    // Only try the first position of a word: otherwise, each character of a long word that is not
                    // followed by `::` would rescan the rest of the word.
                    Begin = CommonModes.RunStart(@"\w", "a-zA-Z") + CommonModes.IdentRe + "::",
                    Keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["keyword"] = ["Self"],
                        ["built_in"] = BuiltIns,
                        ["type"] = Types,
                    }),
                },
                new Mode
                {
                    Scope = "punctuation",
                    Begin = "->",
                },
                functionInvoke,
            ],
        };
    }
}
