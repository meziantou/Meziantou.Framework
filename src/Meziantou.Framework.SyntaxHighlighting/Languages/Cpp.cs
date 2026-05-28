using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Cpp
{
    private const string DeclTypeAutoRe = @"decltype\(auto\)";
    private const string NamespaceRe = @"[a-zA-Z_]\w*::";
    private const string TemplateArgumentRe = @"<[^<>]+>";

    private const string FunctionTypeRe =
        "(?!struct)(" + DeclTypeAutoRe + "|"
        + "(?:" + NamespaceRe + ")?"
        + @"[a-zA-Z_]\w*"
        + "(?:" + TemplateArgumentRe + ")?"
        + ")";

    private const string FunctionTitleRe =
        "(?:" + NamespaceRe + ")?" + CommonModes.IdentRe + @"\s*\(";

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

    private static readonly string[] ReservedTypes =
    [
        "bool","char","char16_t","char32_t","char8_t","double","float","int","long","short","void",
        "wchar_t","unsigned","signed","const","static",
    ];

    private static readonly string[] Literals = ["NULL", "false", "nullopt", "nullptr", "true"];
    private static readonly string[] BuiltIn = ["_Pragma"];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["type"] = ReservedTypes,
            ["keyword"] = ReservedKeywords,
            ["literal"] = Literals,
            ["built_in"] = BuiltIn,
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

        const string CharacterEscapes = @"\\(x[0-9A-Fa-f]{2}|u[0-9A-Fa-f]{4,8}|[0-7]{3}|\S)";

        var strings = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode
                {
                    Begin = @"(u8?|U|L)?""",
                    End = @"""",
                    Illegal = @"\n",
                    Contains = [CommonModes.BackslashEscape],
                },
                new Mode
                {
                    Begin = @"(u8?|U|L)?'(" + CharacterEscapes + "|.)",
                    End = "'",
                    Illegal = ".",
                },
                new Mode
                {
                    Begin = @"(?:u8?|U|L)?R""([^()\\ ]{0,16})\(",
                    End = @"\)([^()\\ ]{0,16})""",
                    EndSameAsBegin = true,
                },
            ],
        };

        var numbers = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode
                {
                    Begin =
                        @"[+-]?(?:" +
                        @"(?:" +
                            @"[0-9](?:'?[0-9])*\.(?:[0-9](?:'?[0-9])*)?" +
                            @"|\.[0-9](?:'?[0-9])*" +
                        @")(?:[Ee][+-]?[0-9](?:'?[0-9])*)?" +
                        @"|[0-9](?:'?[0-9])*[Ee][+-]?[0-9](?:'?[0-9])*" +
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
                new() { Scope = "string", Begin = "<.*?>" },
                cLineComment,
                CommonModes.CBlockCommentMode,
            ],
        };

        var titleMode = new Mode { Scope = "title", Begin = "(?:" + NamespaceRe + ")?" + CommonModes.IdentRe };

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
            Begin = "(" + FunctionTypeRe + @"[\*&\s]+)+" + FunctionTitleRe,
            ReturnBegin = true,
            End = "[{;=]",
            ExcludeEnd = true,
            Keywords = keywords,
            Illegal = @"[^\w\s\*&:<>.]",
            Contains =
            [
                new() { Begin = DeclTypeAutoRe, Keywords = keywords },
                new()
                {
                    Begin = FunctionTitleRe,
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
        contains.Add(new Mode { Begin = CommonModes.IdentRe + "::", Keywords = keywords });
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
