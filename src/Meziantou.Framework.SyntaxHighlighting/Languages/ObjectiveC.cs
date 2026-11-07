using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class ObjectiveC
{
    // Deviation from highlight.js, whose pattern is `[a-zA-Z@][a-zA-Z0-9_]*`: a word cannot start with `_` there, so
    // the keywords and types that do (`__weak`, `__block`, `_Nonnull`, `_Bool`) are never highlighted, and `__weak`
    // is rendered as `__` followed by the keyword `weak`.
    private const string IdentifierRe = "[a-zA-Z@_][a-zA-Z0-9_]*";

    private static readonly string[] Types =
    [
        "int", "float", "char", "unsigned", "signed", "short", "long", "double", "wchar_t", "unichar", "void", "bool",
        "BOOL", "id", "_Bool",
    ];

    private static readonly string[] ReservedKeywords =
    [
        "while", "export", "sizeof", "typedef", "const", "struct", "for", "union", "volatile", "static", "mutable", "if",
        "do", "return", "goto", "enum", "else", "break", "extern", "asm", "case", "default", "register", "explicit",
        "typename", "switch", "continue", "inline", "readonly", "assign", "readwrite", "self", "@synchronized", "id",
        "typeof", "nonatomic", "IBOutlet", "IBAction", "strong", "weak", "copy", "in", "out", "inout", "bycopy",
        "byref", "oneway", "__strong", "__weak", "__block", "__autoreleasing", "@private", "@protected", "@public",
        "@try", "@property", "@end", "@throw", "@catch", "@finally", "@autoreleasepool", "@synthesize", "@dynamic",
        "@selector", "@optional", "@required", "@encode", "@package", "@import", "@defs", "@compatibility_alias",
        "__bridge", "__bridge_transfer", "__bridge_retained", "__bridge_retain", "__covariant", "__contravariant",
        "__kindof", "_Nonnull", "_Nullable", "_Null_unspecified", "__FUNCTION__", "__PRETTY_FUNCTION__",
        "__attribute__", "getter", "setter", "retain", "unsafe_unretained", "nonnull", "nullable", "null_unspecified",
        "null_resettable", "class", "instancetype", "NS_DESIGNATED_INITIALIZER", "NS_UNAVAILABLE", "NS_REQUIRES_SUPER",
        "NS_RETURNS_INNER_POINTER", "NS_INLINE", "NS_AVAILABLE", "NS_DEPRECATED", "NS_ENUM", "NS_OPTIONS",
        "NS_SWIFT_UNAVAILABLE", "NS_ASSUME_NONNULL_BEGIN", "NS_ASSUME_NONNULL_END", "NS_REFINED_FOR_SWIFT",
        "NS_SWIFT_NAME", "NS_SWIFT_NOTHROW", "NS_DURING", "NS_HANDLER", "NS_ENDHANDLER", "NS_VALUERETURN",
        "NS_VOIDRETURN",
    ];

    private static readonly string[] Literals = ["false", "true", "FALSE", "TRUE", "nil", "YES", "NO", "NULL"];

    private static readonly string[] BuiltIns = ["dispatch_once_t", "dispatch_queue_t", "dispatch_sync", "dispatch_async", "dispatch_once"];

    private static readonly string[] ClassKeywords = ["@interface", "@class", "@protocol", "@implementation"];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        return new Mode
        {
            KeywordPattern = IdentifierRe,
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["variable.language"] = ["this", "super"],
                ["keyword"] = ReservedKeywords,
                ["literal"] = Literals,
                ["built_in"] = BuiltIns,
                ["type"] = Types,
            }),
            Illegal = "</",
            Contains =
            [
                new Mode
                {
                    Scope = "built_in",
                    Begin = @"\b(AV|CA|CF|CG|CI|CL|CM|CN|CT|MK|MP|MTK|MTL|NS|SCN|SK|UI|WK|XC)\w+",
                },
                CommonModes.CLineCommentMode,
                CommonModes.CBlockCommentMode,
                CommonModes.CNumberMode,
                CommonModes.QuoteStringMode,
                CommonModes.AposStringMode,
                new Mode
                {
                    Scope = "string",
                    Begin = "@\"",
                    End = "\"",
                    Illegal = @"\n",
                    Contains = [CommonModes.BackslashEscape],
                },
                new Mode
                {
                    Scope = "meta",
                    Begin = @"#\s*[a-z]+\b",
                    End = "$",
                    Keywords = Engine.Keywords.FromWords(["if", "else", "elif", "endif", "define", "undef", "warning", "error", "line", "pragma", "ifdef", "ifndef", "include"]),
                    Contains =
                    [
                        // Line continuation
                        new Mode { Begin = @"\\\n" },
                        CommonModes.QuoteStringMode,
                        new Mode
                        {
                            Scope = "string",
                            Begin = "<.*?>",
                            End = "$",
                            Illegal = @"\n",
                        },
                        CommonModes.CLineCommentMode,
                        CommonModes.CBlockCommentMode,
                    ],
                },
                new Mode
                {
                    Scope = "class",
                    Begin = "(" + string.Join('|', ClassKeywords) + @")\b",
                    End = @"(\{|$)",
                    ExcludeEnd = true,
                    KeywordPattern = IdentifierRe,
                    Keywords = Engine.Keywords.FromWords(ClassKeywords),
                    Contains = [CommonModes.UnderscoreTitleMode],
                },
                // Member access: a property named like a keyword (`self.copy`) is not a keyword.
                new Mode { Begin = @"\." + CommonModes.UnderscoreIdentRe },
            ],
        };
    }
}
