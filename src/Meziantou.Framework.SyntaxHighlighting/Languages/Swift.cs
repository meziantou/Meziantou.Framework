using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Swift
{
    // highlight.js builds these from alternations of character classes; a single class is equivalent and faster.
    private const string IdentifierHeadChars =
        @"a-zA-Z_¨ª­¯²-µ·-º¼-¾À-ÖØ-öø-ÿ" +
        @"Ā-˿Ͱ-ᙿᚁ-᠍᠏-ᶿḀ-῿​-‍‪-‮‿-⁀⁔" +
        @"⁠-⁯⁰-⃏℀-↏①-⓿❶-➓Ⰰ-ⷿ⺀-⿿〄-〇" +
        @"〡-〯〱-〿぀-퟿豈-ﴽ﵀-﷏ﷰ-︟︰-﹄﹇-﻾＀-�";

    private const string IdentifierChars = IdentifierHeadChars + @"0-9̀-ͯ᷀-᷿⃐-⃿︠-︯";

    private const string OperatorHeadChars =
        @"/=\-+!*%<>&|^~?¡-§©«¬®°±¶»¿×÷‖-‗" +
        @"†-‧‰-‾⁁-⁓⁕-⁞←-⏿─-❵➔-⯿⸀-⹿" +
        @"、-〃〈-〠〰";

    private const string OperatorChars = OperatorHeadChars + @"̀-ͯ᷀-᷿⃐-⃿︀-️︠-︯";

    private const string IdentifierCharacter = "[" + IdentifierChars + "]";
    private const string Identifier = "[" + IdentifierHeadChars + "]" + IdentifierCharacter + "*";
    private const string TypeIdentifier = "[A-Z]" + IdentifierCharacter + "*";
    private const string OperatorRe = "[" + OperatorHeadChars + "][" + OperatorChars + "]*";
    private const string QuotedIdentifierRe = "`" + Identifier + "`";

    private const string DecimalDigits = "(?:[0-9]_*)+";
    private const string HexDigits = "(?:[0-9a-fA-F]_*)+";

    // Keywords that require a leading dot.
    private static readonly string[] DotKeywords = ["Protocol", "Type"];

    // Keywords that may have a leading dot.
    private static readonly string[] OptionalDotKeywords = ["init", "self"];

    // Registered as keywords, not types.
    private static readonly string[] KeywordTypes = ["Any", "Self"];

    // Regular expression sources: the words are also matched by the keyword scanner, the others (`as?`,
    // `private(set)`) only by their own mode.
    private static readonly string[] KeywordSources =
    [
        "actor", "any", "associatedtype", "async", "await", @"as\?", "as!", "as", "borrowing", "break", "case", "catch",
        "class", "consume", "consuming", "continue", "convenience", "copy", "default", "defer", "deinit", "didSet",
        "distributed", "do", "dynamic", "each", "else", "enum", "extension", "fallthrough", @"fileprivate\(set\)",
        "fileprivate", "final", "for", "func", "get", "guard", "if", "import", "indirect", "infix", @"init\?", "init!",
        "inout", @"internal\(set\)", "internal", "in", "is", "isolated", "nonisolated", "lazy", "let", "macro",
        "mutating", "nonmutating", @"open\(set\)", "open", "operator", "optional", "override", "package", "postfix",
        "precedencegroup", "prefix", @"private\(set\)", "private", "protocol", @"public\(set\)", "public", "repeat",
        "required", "rethrows", "return", "set", "some", "static", "struct", "subscript", "super", "switch", "throws",
        "throw", @"try\?", "try!", "try", "typealias", @"unowned\(safe\)", @"unowned\(unsafe\)", "unowned", "var",
        "weak", "where", "while", "willSet",
    ];

    private static readonly string[] Literals = ["false", "nil", "true"];

    private static readonly string[] PrecedenceGroupKeywords = ["assignment", "associativity", "higherThan", "left", "lowerThan", "none", "right"];

    // #(un)available is handled separately.
    private static readonly string[] NumberSignKeywords =
    [
        "#colorLiteral", "#column", "#dsohandle", "#else", "#elseif", "#endif", "#error", "#file", "#fileID",
        "#fileLiteral", "#filePath", "#function", "#if", "#imageLiteral", "#keyPath", "#line", "#selector",
        "#sourceLocation", "#warning",
    ];

    // Global functions of the standard library.
    private static readonly string[] BuiltIns =
    [
        "abs", "all", "any", "assert", "assertionFailure", "debugPrint", "dump", "fatalError", "getVaList",
        "isKnownUniquelyReferenced", "max", "min", "numericCast", "pointwiseMax", "pointwiseMin", "precondition",
        "preconditionFailure", "print", "readLine", "repeatElement", "sequence", "stride", "swap",
        "swift_unboxFromSwiftValueWithType", "transcode", "type", "unsafeBitCast", "unsafeDowncast",
        "withExtendedLifetime", "withUnsafeMutablePointer", "withUnsafePointer", "withVaList", "withoutActuallyEscaping",
        "zip",
    ];

    // Built-in attributes, highlighted as keywords. @available is handled separately.
    private static readonly string[] KeywordAttributes =
    [
        "attached", "autoclosure", @"convention\((?:swift|block|c)\)", "discardableResult", "dynamicCallable",
        "dynamicMemberLookup", "escaping", "freestanding", "frozen", "GKInspectable", "IBAction", "IBDesignable",
        "IBInspectable", "IBOutlet", "IBSegueAction", "inlinable", "main", "nonobjc", "NSApplicationMain", "NSCopying",
        "NSManaged", @"objc\(" + Identifier + @"\)", "objc", "objcMembers", "propertyWrapper",
        "requires_stored_property_inits", "resultBuilder", "Sendable", "testable", "UIApplicationMain", "unchecked",
        "unknown", "usableFromInline", "warn_unqualified_access",
    ];

    // Contextual keywords of @available and #(un)available.
    private static readonly string[] AvailabilityKeywords =
    [
        "iOS", "iOSApplicationExtension", "macOS", "macOSApplicationExtension", "macCatalyst",
        "macCatalystApplicationExtension", "watchOS", "watchOSApplicationExtension", "tvOS", "tvOSApplicationExtension",
        "swift",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static string Either(IEnumerable<string> alternatives) => "(?:" + string.Join('|', alternatives) + ")";

    private static bool IsPlainWord(string source) => source.All(c => char.IsAsciiLetterOrDigit(c) || c is '_');

    // A word ends with a word boundary; a pattern ending with punctuation (`as?`) must be followed by a non-boundary.
    private static string KeywordWrapper(string keyword) => @"\b" + keyword + (char.IsAsciiLetterOrDigit(keyword[^1]) || keyword[^1] is '_' ? @"\b" : @"\B");

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = [.. KeywordSources.Where(IsPlainWord), "_", .. NumberSignKeywords],
            ["literal"] = Literals,
        });
        const string KeywordPattern = @"(?:\b\w+|#\w+)";

        var whitespace = new Mode { Match = @"\s+" };
        var blockComment = CommonModes.Comment(@"/\*", @"\*/", extraContains: [Mode.Self]);
        Mode[] comments = [CommonModes.CLineCommentMode, blockComment];

        var dotKeyword = new Mode
        {
            BeginParts = [@"\.", Either(DotKeywords.Concat(OptionalDotKeywords).Select(KeywordWrapper))],
            BeginScope = new Dictionary<int, string> { [2] = "keyword" },
        };

        // Consumes `.keyword` so that properties and methods are not highlighted as keywords.
        var keywordGuard = new Mode { Match = @"\." + Either(KeywordSources) };

        var keyword = new Mode
        {
            Scope = "keyword",
            Match = Either(KeywordSources.Where(source => !IsPlainWord(source)).Concat(KeywordTypes).Concat(OptionalDotKeywords).Select(KeywordWrapper)),
        };

        Mode[] keywordModes = [dotKeyword, keywordGuard, keyword];

        // Consumes `.builtIn` so that properties and methods are not highlighted.
        var builtInGuard = new Mode { Match = @"\." + Either(BuiltIns) };
        var builtIn = new Mode
        {
            Scope = "built_in",
            Match = @"\b" + Either(BuiltIns) + @"(?=\()",
        };
        Mode[] builtIns = [builtInGuard, builtIn];

        // Prevents `->` from being highlighted as an operator.
        var operatorGuard = new Mode { Match = "->" };
        var @operator = new Mode
        {
            Scope = "operator",
            Variants =
            [
                new Mode { Match = OperatorRe },
                // Only operators that start with a dot can contain dots (`...`, `..<`).
                new Mode { Match = @"\.[." + OperatorChars + "]+" },
            ],
        };
        Mode[] operators = [operatorGuard, @operator];

        var number = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode { Match = @"\b" + DecimalDigits + @"(?:\." + DecimalDigits + ")?(?:[eE][+-]?" + DecimalDigits + @")?\b" },
                new Mode { Match = @"\b0x" + HexDigits + @"(?:\." + HexDigits + ")?(?:[pP][+-]?" + DecimalDigits + @")?\b" },
                new Mode { Match = @"\b0o(?:[0-7]_*)+\b" },
                new Mode { Match = @"\b0b(?:[01]_*)+\b" },
            ],
        };

        var interpolations = new List<Mode>();

        Mode EscapedCharacter(string rawDelimiter) => new()
        {
            Scope = "subst",
            Variants =
            [
                new Mode { Match = @"\\" + rawDelimiter + @"[0\\tnr""']" },
                new Mode { Match = @"\\" + rawDelimiter + @"u\{[0-9a-fA-F]{1,8}\}" },
            ],
        };

        Mode EscapedNewline(string rawDelimiter) => new()
        {
            Scope = "subst",
            Match = @"\\" + rawDelimiter + @"[\t ]*(?:[\r\n]|\r\n)",
        };

        Mode Interpolation(string rawDelimiter)
        {
            var interpolation = new Mode
            {
                Scope = "subst",
                Begin = @"\\" + rawDelimiter + @"\(",
                End = @"\)",
                Keywords = keywords,
                KeywordPattern = KeywordPattern,
            };
            interpolations.Add(interpolation);
            return interpolation;
        }

        Mode MultilineString(string rawDelimiter) => new()
        {
            Begin = rawDelimiter + "\"\"\"",
            End = "\"\"\"" + rawDelimiter,
            Contains = [EscapedCharacter(rawDelimiter), EscapedNewline(rawDelimiter), Interpolation(rawDelimiter)],
        };

        Mode SingleLineString(string rawDelimiter) => new()
        {
            Begin = rawDelimiter + "\"",
            End = "\"" + rawDelimiter,
            Contains = [EscapedCharacter(rawDelimiter), Interpolation(rawDelimiter)],
        };

        var @string = new Mode
        {
            Scope = "string",
            Variants =
            [
                MultilineString(""),
                MultilineString("#"),
                MultilineString("##"),
                MultilineString("###"),
                SingleLineString(""),
                SingleLineString("#"),
                SingleLineString("##"),
                SingleLineString("###"),
            ],
        };

        Mode[] regexpContents =
        [
            CommonModes.BackslashEscape,
            new Mode
            {
                Begin = @"\[",
                End = @"\]",
                Contains = [CommonModes.BackslashEscape],
            },
        ];

        Mode ExtendedRegexpLiteral(string rawDelimiter) => new()
        {
            Begin = rawDelimiter + "/",
            End = "/" + rawDelimiter,
            Contains =
            [
                .. regexpContents,
                new Mode
                {
                    Scope = "comment",
                    Begin = "#(?!.*/" + rawDelimiter + ")",
                    End = "$",
                },
            ],
        };

        var regexp = new Mode
        {
            Scope = "regexp",
            Variants =
            [
                ExtendedRegexpLiteral("###"),
                ExtendedRegexpLiteral("##"),
                ExtendedRegexpLiteral("#"),
                new Mode
                {
                    Begin = @"/[^\s](?=[^/\n]*/)",
                    End = "/",
                    Contains = regexpContents,
                },
            ],
        };

        var quotedIdentifier = new Mode { Match = QuotedIdentifierRe };
        Mode[] identifiers =
        [
            quotedIdentifier,
            // Implicit closure parameter
            new Mode { Scope = "variable", Match = @"\$\d+" },
            // Property wrapper projection
            new Mode { Scope = "variable", Match = @"\$" + IdentifierCharacter + "+" },
        ];

        Mode[] attributes =
        [
            new Mode
            {
                Scope = "keyword",
                Match = "(@|#(un)?)available",
                Starts = new Mode
                {
                    Contains =
                    [
                        new Mode
                        {
                            Begin = @"\(",
                            End = @"\)",
                            Keywords = Engine.Keywords.FromWords(AvailabilityKeywords),
                            Contains = [.. operators, number, @string],
                        },
                    ],
                },
            },
            new Mode
            {
                Scope = "keyword",
                Match = "@" + Either(KeywordAttributes) + @"(?=(?:\(|\s+))",
            },
            new Mode
            {
                Scope = "meta",
                Match = "@" + Identifier,
            },
            // Deviation from highlight.js, which leaves the `#` of a freestanding macro expansion (`#Preview`,
            // `#stringify(x)`) as plain text and highlights the rest as a type when it is capitalized. It is
            // highlighted like an attached macro (`@Observable`) instead.
            new Mode
            {
                Scope = "meta",
                Match = "#(?!" + Either(NumberSignKeywords.Select(keyword => keyword[1..]).Concat(["available", "unavailable"])) + @"\b)" + Identifier,
            },
        ];

        var typeContains = new List<Mode>
        {
            // Common Apple frameworks
            new Mode
            {
                Scope = "type",
                Match = "(?:AV|CA|CF|CG|CI|CL|CM|CN|CT|MK|MP|MTK|MTL|NS|SCN|SK|UI|WK|XC)" + IdentifierCharacter + "+",
            },
            new Mode { Scope = "type", Match = TypeIdentifier },
            // Optional type
            new Mode { Match = "[?!]+" },
            // Variadic parameter
            new Mode { Match = @"\.\.\." },
            // Protocol composition
            new Mode { Match = @"\s+&\s+(?=" + TypeIdentifier + ")" },
        };
        var type = new Mode
        {
            Match = @"(?=\b[A-Z])",
            Contains = typeContains,
        };
        typeContains.Add(new Mode
        {
            Begin = "<",
            End = ">",
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
            Contains = [.. comments, .. keywordModes, .. attributes, operatorGuard, type],
        });

        // Matches tuples as well as the parameter list of a function type.
        var tuple = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
            Contains =
            [
                Mode.Self,
                // Prevents element names from being highlighted as keywords.
                new Mode
                {
                    Match = Identifier + @"\s*:",
                    Keywords = Engine.Keywords.FromWords(["_"]),
                },
                .. comments,
                regexp,
                .. keywordModes,
                .. builtIns,
                .. operators,
                number,
                @string,
                .. identifiers,
                .. attributes,
                type,
            ],
        };

        var genericParameters = new Mode
        {
            Begin = "<",
            End = ">",
            Keywords = Engine.Keywords.FromWords(["repeat", "each"]),
            Contains = [.. comments, type],
        };

        var functionParameters = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
            Contains =
            [
                new Mode
                {
                    Begin = "(?:(?=" + Identifier + @"\s*:)|(?=" + Identifier + @"\s+" + Identifier + @"\s*:))",
                    End = ":",
                    Contains =
                    [
                        new Mode { Scope = "keyword", Match = @"\b_\b" },
                        new Mode { Scope = "params", Match = Identifier },
                    ],
                },
                .. comments,
                .. keywordModes,
                .. operators,
                number,
                @string,
                .. attributes,
                type,
                tuple,
            ],
            EndsParent = true,
            Illegal = "[\"']",
        };

        var functionOrMacro = new Mode
        {
            BeginParts = ["(?:func|macro)", @"\s+", Either([QuotedIdentifierRe, Identifier, OperatorRe])],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title.function",
            },
            Contains = [genericParameters, functionParameters, whitespace],
            Illegal = @"(?:\[|%)",
        };

        var initSubscript = new Mode
        {
            BeginParts = [@"\b(?:subscript|init[?!]?)", @"\s*(?=[<(])"],
            BeginScope = new Dictionary<int, string> { [1] = "keyword" },
            Contains = [genericParameters, functionParameters, whitespace],
            Illegal = @"\[|%",
        };

        var operatorDeclaration = new Mode
        {
            BeginParts = ["operator", @"\s+", OperatorRe],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title",
            },
        };

        var precedenceGroup = new Mode
        {
            BeginParts = ["precedencegroup", @"\s+", TypeIdentifier],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title",
            },
            Contains = [type],
            Keywords = Engine.Keywords.FromWords([.. PrecedenceGroupKeywords, .. Literals]),
            End = "}",
        };

        var classFuncDeclaration = new Mode
        {
            BeginParts = [@"class\b", @"\s+", @"func\b", @"\s+", @"\b[A-Za-z_][A-Za-z0-9_]*\b"],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "keyword",
                [5] = "title.function",
            },
        };

        var classVarDeclaration = new Mode
        {
            BeginParts = [@"class\b", @"\s+", @"var\b"],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "keyword",
            },
        };

        var typeDeclaration = new Mode
        {
            BeginParts = ["(?:struct|protocol|class|extension|enum|actor)", @"\s+", Identifier, @"\s*"],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title.class",
            },
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
            Contains =
            [
                genericParameters,
                .. keywordModes,
                new Mode
                {
                    Begin = ":",
                    End = @"\{",
                    Keywords = keywords,
                    KeywordPattern = KeywordPattern,
                    Contains =
                    [
                        new Mode { Scope = "title.class.inherited", Match = TypeIdentifier },
                        .. keywordModes,
                    ],
                },
            ],
        };

        // Interpolation can contain any expression; this covers the common ones.
        Mode[] interpolationSubmodes = [.. keywordModes, .. builtIns, .. operators, number, @string, .. identifiers];
        var interpolationParentheses = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Contains = [Mode.Self, .. interpolationSubmodes],
        };
        foreach (var interpolation in interpolations)
        {
            interpolation.Contains = [.. interpolationSubmodes, interpolationParentheses];
        }

        return new Mode
        {
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
            Contains =
            [
                .. comments,
                functionOrMacro,
                initSubscript,
                classFuncDeclaration,
                classVarDeclaration,
                typeDeclaration,
                operatorDeclaration,
                precedenceGroup,
                new Mode
                {
                    BeginKeywords = ["import"],
                    End = "$",
                    Contains = comments,
                },
                regexp,
                .. keywordModes,
                .. builtIns,
                .. operators,
                number,
                @string,
                .. identifiers,
                .. attributes,
                type,
                tuple,
            ],
        };
    }
}
