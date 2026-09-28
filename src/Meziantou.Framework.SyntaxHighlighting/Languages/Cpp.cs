using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Cpp
{
    private const string DecimalStartRe = @"(?:\G|(?<![0-9](?:(?!\G)')?))";

    private static readonly string FunctionDeclarationRe = CFamily.CreateFunctionDeclarationRe("(?!struct)");

    private static readonly string[] ReservedKeywords =
    [
        "alignas","alignof","and","and_eq","asm","atomic_cancel","atomic_commit","atomic_noexcept",
        "auto","bitand","bitor","break","case","catch","class","co_await","co_return","co_yield",
        "compl","concept","const_cast|10","consteval","constexpr","constinit","continue","decltype",
        "default","delete","do","dynamic_cast|10","else","enum","explicit","export","extern","false",
        "final","for","friend","goto","if","import","inline","module","mutable","namespace","new",
        "noexcept","not","not_eq","nullptr","operator","or","or_eq","override","private","protected",
        "public","reflexpr","register","reinterpret_cast|10","requires","return","sizeof",
        "static_assert","static_cast|10","struct","switch","synchronized","template","this",
        "thread_local","throw","transaction_safe","transaction_safe_dynamic","true","try","typedef",
        "typeid","typename","union","using","virtual","volatile","while","xor","xor_eq",
    ];

    internal static readonly string[] ReservedTypes =
    [
        "bool","char","char16_t","char32_t","char8_t","double","float","int","long","short","void",
        "wchar_t","unsigned","signed","const","static",
    ];

    internal static readonly string[] Literals = ["NULL", "false", "nullopt", "nullptr", "true"];
    internal static readonly string[] BuiltIn = ["_Pragma"];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode(ReservedTypes, Literals, BuiltIn));

    /// <summary>
    /// Creates the C++ grammar with the given keyword groups, so that a superset of C++ (Arduino) can add its own
    /// words to every mode that recognizes the C++ keywords.
    /// </summary>
    internal static Mode CreateMode(string[] types, string[] literals, string[] builtIns)
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["type"] = types,
            ["keyword"] = ReservedKeywords,
            ["literal"] = literals,
            ["built_in"] = builtIns,
        });

        var cLineCommentBackslash = new Mode { Begin = @"\\\n" };
        var cLineComment = new Mode
        {
            Scope = "comment",
            Begin = "//",
            End = "$",
            Contains = [cLineCommentBackslash],
        };

        var primitiveTypes = new Mode { Scope = "type", Begin = @"\b[a-z\d_]*_t\b" };

        var strings = CFamily.CreateStrings();

        var numbers = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode
                {
                    // A decimal part that follows a digit (or a digit and a separator) can also be matched
                    // from that digit, so only the first digit of a run may start one: otherwise, each
                    // digit of a long run would rescan it.
                    Begin =
                        @"[+-]?(?:" +
                        @"(?:" +
                            DecimalStartRe + @"[0-9](?:'?[0-9])*\.(?:[0-9](?:'?[0-9])*)?" +
                            @"|\.[0-9](?:'?[0-9])*" +
                        @")(?:[Ee][+-]?[0-9](?:'?[0-9])*)?" +
                        @"|" + DecimalStartRe + @"[0-9](?:'?[0-9])*[Ee][+-]?[0-9](?:'?[0-9])*" +
                        @"|0[Xx](?:" +
                            @"[0-9A-Fa-f](?:'?[0-9A-Fa-f])*(?:\.(?:[0-9A-Fa-f](?:'?[0-9A-Fa-f])*)?)?" +
                            @"|\.[0-9A-Fa-f](?:'?[0-9A-Fa-f])*" +
                        @")[Pp][+-]?[0-9](?:'?[0-9])*" +
                        @")(?:" +
                            @"[Ff](?:16|32|64|128)?" +
                            @"|(BF|bf)16" +
                            @"|[Ll]" +
                            @"|" +
                        @")",
                },
                new Mode
                {
                    Begin =
                        @"[+-]?\b(?:" +
                            @"0[Bb][01](?:'?[01])*" +
                            @"|0[Xx][0-9A-Fa-f](?:'?[0-9A-Fa-f])*" +
                            @"|0(?:'?[0-7])*" +
                            @"|[1-9](?:'?[0-9])*" +
                        @")(?:" +
                            @"[Uu](?:LL?|ll?)" +
                            @"|[Uu][Zz]?" +
                            @"|(?:LL?|ll?)[Uu]?" +
                            @"|[Zz][Uu]" +
                            @"|" +
                        @")",
                },
            ],
        };

        var preprocessor = new Mode
        {
            Scope = "meta",
            Begin = @"#\s*[a-z]+\b",
            End = "$",
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] = "if else elif endif define undef warning error line pragma _Pragma ifdef ifndef include",
            }),
            Contains =
            [
                new() { Begin = @"\\\n" },
                strings,
                new() { Scope = "string", Begin = CFamily.HeaderNameRe },
                cLineComment,
                CommonModes.CBlockCommentMode,
            ],
        };

        var titleMode = new Mode { Scope = "title", Begin = CFamily.ScopedIdentifierRe };

        var functionDispatch = new Mode
        {
            Scope = "function.dispatch",
            Begin = @"\b(?!decltype)(?!if)(?!for)(?!switch)(?!while)" + CommonModes.IdentRe + @"(?=(?:<[^<>]+>|)\s*\()",
        };

        var expressionContains = new List<Mode>
        {
            functionDispatch,
            preprocessor,
            primitiveTypes,
            cLineComment,
            CommonModes.CBlockCommentMode,
            numbers,
            strings,
        };

        var innerParens = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Keywords = keywords,
        };
        innerParens.Contains = [.. expressionContains, innerParens];

        var expressionContextContains = new List<Mode>(expressionContains)
        {
            new()
            {
                Begin = @"\(",
                End = @"\)",
                Keywords = keywords,
                Contains = [.. expressionContains],
            },
        };
        // Wire self for the inner ( ) mode
        var expressionInner = (Mode)expressionContextContains[^1];
        expressionInner.Contains.Add(expressionInner);

        var expressionContext = new Mode
        {
            Variants =
            [
                new Mode { Begin = "=", End = ";" },
                new Mode { Begin = @"\(", End = @"\)" },
                new Mode { BeginKeywords = ["new", "throw", "return", "else"], End = ";" },
            ],
            Keywords = keywords,
            Contains = expressionContextContains,
        };

        var paramsInnerParens = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Keywords = keywords,
        };
        paramsInnerParens.Contains =
        [
            paramsInnerParens,
            cLineComment,
            CommonModes.CBlockCommentMode,
            strings,
            numbers,
            primitiveTypes,
        ];

        var functionDeclaration = new Mode
        {
            Scope = "function",
            Begin = FunctionDeclarationRe,
            ReturnBegin = true,
            End = "[{;=]",
            ExcludeEnd = true,
            Keywords = keywords,
            Illegal = @"[^\w\s\*&:<>.]",
            Contains =
            [
                new() { Begin = CFamily.DeclTypeAutoRe, Keywords = keywords },
                new()
                {
                    Begin = CFamily.FunctionTitleRe,
                    ReturnBegin = true,
                    Contains = [titleMode],
                },
                new() { Begin = "::" },
                new()
                {
                    Begin = ":",
                    EndsWithParent = true,
                    Contains = [strings, numbers],
                },
                new() { Match = "," },
                new()
                {
                    Scope = "params",
                    Begin = @"\(",
                    End = @"\)",
                    Keywords = keywords,
                    Contains =
                    [
                        cLineComment,
                        CommonModes.CBlockCommentMode,
                        strings,
                        numbers,
                        primitiveTypes,
                        paramsInnerParens,
                    ],
                },
                primitiveTypes,
                cLineComment,
                CommonModes.CBlockCommentMode,
                preprocessor,
            ],
        };

        var containerTemplates = new Mode
        {
            Begin = @"\b(deque|list|queue|priority_queue|pair|stack|vector|map|set|bitset|multiset|multimap|unordered_map|unordered_set|unordered_multiset|unordered_multimap|array|tuple|optional|variant|function|flat_map|flat_set)\s*<(?!<)",
            End = ">",
            Keywords = keywords,
        };
        containerTemplates.Contains = [containerTemplates, primitiveTypes];

        var classDeclaration = new Mode
        {
            BeginParts =
            [
                @"\b(?:enum(?:\s+(?:class|struct))?|class|struct|union)",
                @"\s+",
                @"\w+",
            ],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title.class",
            },
        };

        var contains = new List<Mode> { expressionContext, functionDeclaration, functionDispatch };
        contains.AddRange(expressionContains);
        contains.Add(preprocessor);
        contains.Add(containerTemplates);
        contains.Add(new Mode { Begin = CFamily.ScopeQualifierRe, Keywords = keywords });
        contains.Add(classDeclaration);

        return new Mode
        {
            Keywords = keywords,
            Illegal = "</",
            ClassNameAliases = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["function.dispatch"] = "built_in",
            },
            Contains = contains,
        };
    }
}
