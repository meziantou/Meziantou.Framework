using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class C
{
    private const string IntegerSuffixRe = "(?:[uU](?:ll|LL|l|L)?|(?:ll|LL|l|L)[uU]?)?";

    // highlight.js includes the `-` of `n-1` in the number.
    private const string SignRe = @"(?:(?<![\w)\]])-)?";

    private static readonly string[] ReservedKeywords =
    [
        "asm", "auto", "break", "case", "continue", "default", "do", "else", "enum", "extern", "for", "fortran", "goto",
        "if", "inline", "register", "restrict", "return", "sizeof", "typeof", "typeof_unqual", "struct", "switch",
        "typedef", "union", "volatile", "while", "_Alignas", "_Alignof", "_Atomic", "_Generic", "_Noreturn",
        "_Static_assert", "_Thread_local",
        // aliases
        "alignas", "alignof", "noreturn", "static_assert", "thread_local",
        // not a C keyword but is, for all intents and purposes, treated exactly like one.
        "_Pragma",
    ];

    private static readonly string[] Types =
    [
        "float", "double", "signed", "unsigned", "int", "short", "long", "char", "void", "_Bool", "_BitInt", "_Complex",
        "_Imaginary", "_Decimal32", "_Decimal64", "_Decimal96", "_Decimal128", "_Decimal64x", "_Decimal128x",
        "_Float16", "_Float32", "_Float64", "_Float128", "_Float32x", "_Float64x", "_Float128x",
        // modifiers
        "const", "static", "constexpr",
        // aliases
        "complex", "bool", "imaginary",
    ];

    private static readonly string[] Literals = ["true", "false", "NULL"];

    private static readonly string[] BuiltIns =
    [
        "std", "string", "wstring", "cin", "cout", "cerr", "clog", "stdin", "stdout", "stderr", "stringstream",
        "istringstream", "ostringstream", "auto_ptr", "deque", "list", "queue", "stack", "vector", "map", "set", "pair",
        "bitset", "multiset", "multimap", "unordered_set", "unordered_map", "unordered_multiset", "unordered_multimap",
        "priority_queue", "make_pair", "array", "shared_ptr", "abort", "terminate", "abs", "acos", "asin", "atan2",
        "atan", "calloc", "ceil", "cosh", "cos", "exit", "exp", "fabs", "floor", "fmod", "fprintf", "fputs", "free",
        "frexp", "fscanf", "future", "isalnum", "isalpha", "iscntrl", "isdigit", "isgraph", "islower", "isprint",
        "ispunct", "isspace", "isupper", "isxdigit", "tolower", "toupper", "labs", "ldexp", "log10", "log", "malloc",
        "realloc", "memchr", "memcmp", "memcpy", "memset", "modf", "pow", "printf", "putchar", "puts", "scanf", "sinh",
        "sin", "snprintf", "sprintf", "sqrt", "sscanf", "strcat", "strchr", "strcmp", "strcpy", "strcspn", "strlen",
        "strncat", "strncmp", "strncpy", "strpbrk", "strrchr", "strspn", "strstr", "tanh", "tan", "vfprintf", "vprintf",
        "vsprintf", "endl", "initializer_list", "unique_ptr",
    ];

    // Deviation from highlight.js: a keyword or a type is not the name of a function, so `typedef void (*callback)(int);`
    // or `__asm__ volatile ("nop");` are not function declarations.
    private static readonly string TitleGuardRe = "(?!(?:" + string.Join('|', ReservedKeywords.Concat(Types)) + @")\b)";

    private static readonly string FunctionTitleRe = CFamily.CreateFunctionTitleRe(TitleGuardRe);

    private static readonly string FunctionDeclarationRe = CFamily.CreateFunctionDeclarationRe(typeGuard: null, TitleGuardRe);

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = ReservedKeywords,
            ["type"] = Types,
            ["literal"] = Literals,
            ["built_in"] = BuiltIns,
        });

        var lineComment = CommonModes.Comment("//", "$", extraContains: [new Mode { Begin = @"\\\n" }]);

        var types = new Mode
        {
            Scope = "type",
            Variants =
            [
                new Mode { Begin = @"\b[a-z\d_]*_t\b" },
                new Mode { Match = @"\batomic_[a-z]{3,6}\b" },
            ],
        };

        var strings = CFamily.CreateStrings();

        // Deviation from highlight.js, whose patterns miss the fraction of `.5`, the suffix of `1e-3f` and `0xFFul`, and
        // include the `-` of `n-1`.
        var numbers = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode { Match = SignRe + @"\b0[xX](?:[a-fA-F0-9]+(?:'[a-fA-F0-9]+)*(?:\.[a-fA-F0-9]*)?|\.[a-fA-F0-9]+)(?:[pP][-+]?\d+[fFlL]?|" + IntegerSuffixRe + ")" },
                new Mode { Match = SignRe + @"\b0[bB][01]+(?:'[01]+)*" + IntegerSuffixRe },
                new Mode { Match = SignRe + @"(?:\b\d+(?:'\d+)*(?:\.(?:\d+(?:'\d+)*)?)?|(?<![\w.])\.\d+(?:'\d+)*)(?:[eE][-+]?\d+)?(?:[fF]|" + IntegerSuffixRe + ")" },
            ],
        };

        var preprocessor = new Mode
        {
            Scope = "meta",
            Begin = @"#\s*[a-z]+\b",
            End = "$",
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] = "if else elif endif define undef warning error line pragma _Pragma ifdef ifndef elifdef elifndef include",
            }),
            Contains =
            [
                new Mode { Begin = @"\\\n" },
                strings,
                new Mode { Scope = "string", Begin = CFamily.HeaderNameRe },
                lineComment,
                CommonModes.CBlockCommentMode,
            ],
        };

        List<Mode> expressionContains = [preprocessor, types, lineComment, CommonModes.CBlockCommentMode, numbers, strings];

        var expressionParentheses = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Keywords = keywords,
        };
        expressionParentheses.Contains = [.. expressionContains, expressionParentheses];

        // Covers the expression context where a function definition cannot be expected and nothing that looks like
        // one must be highlighted: `return some()`, `else if()`, `(x*sum(1, 2))`
        var expressionContext = new Mode
        {
            Variants =
            [
                new Mode { Begin = "=", End = ";" },
                new Mode { Begin = @"\(", End = @"\)" },
                new Mode { BeginKeywords = ["new", "throw", "return", "else"], End = ";" },
            ],
            Keywords = keywords,
            Contains = [.. expressionContains, expressionParentheses],
        };

        // Counts the matching parentheses of a parameter list.
        var parametersParentheses = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Keywords = keywords,
        };
        parametersParentheses.Contains = [parametersParentheses, lineComment, CommonModes.CBlockCommentMode, strings, numbers, types];

        var functionDeclaration = new Mode
        {
            Begin = FunctionDeclarationRe,
            ReturnBegin = true,
            End = "[{;=]",
            ExcludeEnd = true,
            Keywords = keywords,
            Illegal = @"[^\w\s\*&:<>.]",
            Contains =
            [
                // prevents it from being confused with the function title
                new Mode { Begin = CFamily.DeclTypeAutoRe, Keywords = keywords },
                new Mode
                {
                    Begin = FunctionTitleRe,
                    ReturnBegin = true,
                    Contains = [new Mode { Scope = "title.function", Begin = CFamily.ScopedIdentifierRe }],
                },
                // allows multiple declarations: `extern void f(int), g(char);`
                new Mode { Match = "," },
                new Mode
                {
                    Scope = "params",
                    Begin = @"\(",
                    End = @"\)",
                    Keywords = keywords,
                    Contains = [lineComment, CommonModes.CBlockCommentMode, strings, numbers, types, parametersParentheses],
                },
                types,
                lineComment,
                CommonModes.CBlockCommentMode,
                preprocessor,
            ],
        };

        return new Mode
        {
            Keywords = keywords,
            Illegal = "</",
            Contains =
            [
                expressionContext,
                functionDeclaration,
                .. expressionContains,
                preprocessor,
                new Mode { Begin = CFamily.ScopeQualifierRe, Keywords = keywords },
                new Mode
                {
                    Scope = "class",
                    BeginKeywords = ["enum", "class", "struct", "union"],
                    End = "[{;:<>=]",
                    Contains =
                    [
                        new Mode { BeginKeywords = ["final", "class", "struct"] },
                        new Mode
                        {
                            Scope = "title",
                            Begin = CommonModes.IdentRe,
                            // Deviation from highlight.js: only the tag is a title. The declarators that follow it
                            // (`struct node *next;`, `enum color c = RED;`) are not.
                            Starts = new Mode { EndsWithParent = true },
                        },
                    ],
                },
            ],
        };
    }
}
