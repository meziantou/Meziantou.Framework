using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class FSharp
{
    private static readonly string[] Keywords =
    [
        "abstract","and","as","assert","base","begin","class","default","delegate","do","done",
        "downcast","downto","elif","else","end","exception","extern","finally","fixed","for","fun",
        "function","global","if","in","inherit","inline","interface","internal","lazy","let","match",
        "member","module","mutable","namespace","new","of","open","or","override","private","public",
        "rec","return","static","struct","then","to","try","type","upcast","use","val","void","when",
        "while","with","yield",
    ];

    private static readonly string[] PreprocessorKeywords =
    [
        "if","else","endif","line","nowarn","light","r","i","I","load","time","help","quit",
    ];

    private static readonly string[] Literals =
    [
        "true","false","null","Some","None","Ok","Error","infinity","infinityf","nan","nanf",
    ];

    private static readonly string[] SpecialIdentifiers =
    [
        "__LINE__","__SOURCE_DIRECTORY__","__SOURCE_FILE__",
    ];

    private static readonly string[] KnownTypes =
    [
        "bool","byte","sbyte","int8","int16","int32","uint8","uint16","uint32","int","uint","int64",
        "uint64","nativeint","unativeint","decimal","float","double","float32","single","char",
        "string","unit","bigint","option","voption","list","array","seq","byref","exn","inref",
        "nativeptr","obj","outref","voidptr","Result",
    ];

    private static readonly string[] Builtins =
    [
        "not","ref","raise","reraise","dict","readOnlyDict","set","get","enum","sizeof","typeof",
        "typedefof","nameof","nullArg","invalidArg","invalidOp","id","fst","snd","ignore","lock",
        "using","box","unbox","tryUnbox","printf","printfn","sprintf","eprintf","eprintfn","fprintf",
        "fprintfn","failwith","failwithf",
    ];

    private static Keywords AllKeywords() => Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["keyword"] = Keywords,
        ["literal"] = Literals,
        ["built_in"] = Builtins,
        ["variable.constant"] = SpecialIdentifiers,
    });

    private static Keywords AllKeywordsWithTypes() => Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["keyword"] = Keywords,
        ["literal"] = Literals,
        ["built_in"] = Builtins,
        ["variable.constant"] = SpecialIdentifiers,
        ["type"] = KnownTypes,
    });

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var bangKeyword = new Mode { Scope = "keyword", Match = @"\b(yield|return|let|do|match|use)!" };

        var mlComment = new Mode
        {
            Scope = "comment",
            Begin = @"\(\*(?!\))",
            End = @"\*\)",
        };
        mlComment.Contains = [mlComment];

        var comment = new Mode
        {
            Variants = [mlComment, CommonModes.CLineCommentMode],
        };

        const string IdentifierRe = @"[a-zA-Z_](\w|')*";
        var quotedIdentifier = new Mode { Scope = "variable", Begin = "``", End = "``" };
        var quotedIdentNoScope = new Mode { Begin = "``", End = "``" };

        const string BeginGenericTypeSymbolRe = @"\B('|\^)";
        var genericTypeSymbol = new Mode
        {
            Scope = "symbol",
            Variants =
            [
                new Mode { Match = BeginGenericTypeSymbolRe + @"``.*?``" },
                new Mode { Match = BeginGenericTypeSymbolRe + CommonModes.UnderscoreIdentRe },
            ],
        };

        static Mode MakeOperator(bool includeEqual)
        {
            var chars = includeEqual ? "!%&*+-/<=>@^|~?" : "!%&*+-/<>@^|~?";
            static string EscapeInClass(char c) => c is '\\' or ']' or '^' or '-' ? "\\" + c : c.ToString();
            var escaped = string.Concat(chars.Select(EscapeInClass));
            var charRe = "[" + escaped + "]";
            var charOrDotRe = "(?:" + charRe + @"|\.)";
            var firstCharRe = charOrDotRe + "(?=" + charOrDotRe + ")";
            var symbolicOp = "(?:" + firstCharRe + charOrDotRe + "*|" + charRe + "+)";
            return new Mode
            {
                Scope = "operator",
                Match = "(?:" + symbolicOp + @"|:\?>|:\?|:>|:=|::?|\$)",
            };
        }

        var op = MakeOperator(includeEqual: true);
        var opNoEqual = MakeOperator(includeEqual: false);

        // For type annotations we need group-based begin so that the prefix becomes "operator" scope
        var typeAnnotation = new Mode
        {
            BeginParts = [":"],
            BeginScope = new Dictionary<int, string> { [1] = "operator" },
            End = @"(?=\n|=)",
            Keywords = AllKeywordsWithTypes(),
            Contains = [comment, genericTypeSymbol, quotedIdentNoScope, opNoEqual],
        };
        // Constrain begin lookahead so we only start a type annotation when followed by a type expression.
        typeAnnotation = new Mode
        {
            BeginParts = [@":(?=\s*(?:\w|'|\^|#|``|\(|\{\|))"],
            BeginScope = new Dictionary<int, string> { [1] = "operator" },
            End = @"(?=\n|=)",
            Keywords = AllKeywordsWithTypes(),
            Contains = [comment, genericTypeSymbol, quotedIdentNoScope, opNoEqual],
        };

        var discriminatedUnionTypeAnnotation = new Mode
        {
            BeginParts = [@"\bof\b(?=\s*(?:\w|'|\^|#|``|\(|\{\|))"],
            BeginScope = new Dictionary<int, string> { [1] = "keyword" },
            End = @"(?=\n|=)",
            Keywords = AllKeywordsWithTypes(),
            Contains = [comment, genericTypeSymbol, quotedIdentNoScope, opNoEqual],
        };

        var typeDeclaration = new Mode
        {
            BeginParts = [@"(^|\s+)", "type", @"\s+", IdentifierRe],
            BeginScope = new Dictionary<int, string> { [2] = "keyword", [4] = "title.class" },
            End = @"(?=\(|=|$)",
            Keywords = AllKeywords(),
            Contains =
            [
                comment,
                quotedIdentNoScope,
                genericTypeSymbol,
                new() { Scope = "operator", Match = "<|>" },
                typeAnnotation,
            ],
        };

        var computationExpression = new Mode { Scope = "computation-expression", Match = @"\b[_a-z]\w*(?=\s*\{)" };

        var preprocessor = new Mode
        {
            BeginParts = [@"^\s*", "#(?:" + string.Join('|', PreprocessorKeywords) + ")", @"\b"],
            BeginScope = new Dictionary<int, string> { [2] = "meta" },
            End = @"(?=\s|$)",
        };

        var number = new Mode { Variants = [new Mode { Scope = "number", Begin = @"\b(0b[01]+)" }, CommonModes.CNumberMode] };

        var quotedString = new Mode
        {
            Scope = "string",
            Begin = "\"",
            End = "\"",
            Contains = [CommonModes.BackslashEscape],
        };
        var verbatimStringEscape = new Mode { Match = "\"\"" };
        var verbatimString = new Mode
        {
            Scope = "string",
            Begin = "@\"",
            End = "\"",
            Contains = [verbatimStringEscape, CommonModes.BackslashEscape],
        };
        var tripleQuotedString = new Mode
        {
            Scope = "string",
            Begin = "\"\"\"",
            End = "\"\"\"",
        };

        var subst = new Mode
        {
            Scope = "subst",
            Begin = @"\{",
            End = @"\}",
            Keywords = AllKeywords(),
        };
        var braceEscape1 = new Mode { Match = @"\{\{" };
        var braceEscape2 = new Mode { Match = @"\}\}" };

        var interpolatedString = new Mode
        {
            Scope = "string",
            Begin = @"\$""",
            End = "\"",
            Contains =
            [
                braceEscape1, braceEscape2, CommonModes.BackslashEscape, subst,
            ],
        };
        var interpolatedVerbatimString = new Mode
        {
            Scope = "string",
            Begin = @"(\$@|@\$)""",
            End = "\"",
            Contains =
            [
                braceEscape1, braceEscape2, verbatimStringEscape, CommonModes.BackslashEscape, subst,
            ],
        };
        var interpolatedTripleQuotedString = new Mode
        {
            Scope = "string",
            Begin = "\\$\"\"\"",
            End = "\"\"\"",
            Contains = [braceEscape1, braceEscape2, subst],
        };

        var charLiteral = new Mode
        {
            Scope = "string",
            Match = @"'(?:[^\\']|\\(?:.|\d{3}|x[a-fA-F\d]{2}|u[a-fA-F\d]{4}|U[a-fA-F\d]{8}))'",
        };

        subst.Contains =
        [
            interpolatedVerbatimString,
            interpolatedString,
            verbatimString,
            quotedString,
            charLiteral,
            bangKeyword,
            comment,
            quotedIdentifier,
            typeAnnotation,
            computationExpression,
            preprocessor,
            number,
            genericTypeSymbol,
            op,
        ];

        var stringMode = new Mode
        {
            Variants =
            [
                interpolatedTripleQuotedString,
                interpolatedVerbatimString,
                interpolatedString,
                tripleQuotedString,
                verbatimString,
                quotedString,
                charLiteral,
            ],
        };

        return new Mode
        {
            Keywords = AllKeywords(),
            Illegal = @"/\*",
            ClassNameAliases = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["computation-expression"] = "keyword",
            },
            Contains =
            [
                bangKeyword,
                stringMode,
                comment,
                quotedIdentifier,
                typeDeclaration,
                new()
                {
                    Scope = "meta",
                    Begin = @"\[<", End = @">\]",
                    Contains =
                    [
                        quotedIdentifier,
                        tripleQuotedString,
                        verbatimString,
                        quotedString,
                        charLiteral,
                        number,
                    ],
                },
                discriminatedUnionTypeAnnotation,
                typeAnnotation,
                computationExpression,
                preprocessor,
                number,
                genericTypeSymbol,
                op,
            ],
        };
    }
}
