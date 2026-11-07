using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Delphi
{
    private static readonly string[] KeywordList =
    [
        "exports", "register", "file", "shl", "array", "record", "property", "for", "mod", "while", "set", "ally", "label", "uses",
        "raise", "not", "stored", "class", "safecall", "var", "interface", "or", "private", "static", "exit", "index", "inherited",
        "to", "else", "stdcall", "override", "shr", "asm", "far", "resourcestring", "finalization", "packed", "virtual", "out", "and",
        "protected", "library", "do", "xorwrite", "goto", "near", "function", "end", "div", "overload", "object", "unit", "begin",
        "string", "on", "inline", "repeat", "until", "destructor", "write", "message", "program", "with", "read", "initialization",
        "except", "default", "nil", "if", "case", "cdecl", "in", "downto", "threadvar", "of", "try", "pascal", "const", "external",
        "constructor", "type", "public", "then", "implementation", "finally", "published", "procedure", "absolute", "reintroduce",
        "operator", "as", "is", "abstract", "alias", "assembler", "bitpacked", "break", "continue", "cppdecl", "cvar", "enumerator",
        "experimental", "platform", "deprecated", "unimplemented", "dynamic", "export", "far16", "forward", "generic", "helper",
        "implements", "interrupt", "iochecks", "local", "name", "nodefault", "noreturn", "nostackframe", "oldfpccall", "otherwise",
        "saveregisters", "softfloat", "specialize", "strict", "unaligned", "varargs",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromWords(KeywordList);

        Mode[] comments =
        [
            CommonModes.CLineCommentMode,
            CommonModes.Comment(@"\{", @"\}"),
            CommonModes.Comment(@"\(\*", @"\*\)"),
        ];

        // Compiler directives: {$IFDEF DEBUG}, (*$R+*).
        var directive = new Mode
        {
            Scope = "meta",
            Variants =
            [
                new Mode { Begin = @"\{\$", End = @"\}" },
                new Mode { Begin = @"\(\*\$", End = @"\*\)" },
            ],
        };

        // 'It''s': a quote is escaped by doubling it.
        var stringMode = new Mode
        {
            Scope = "string",
            Begin = "'",
            End = "'",
            Contains = [new Mode { Begin = "''" }],
        };

        // Source: https://www.freepascal.org/docs-html/ref/refse6.html
        var number = new Mode
        {
            Scope = "number",
            Variants =
            [
                // Regular numbers, e.g., 123, 123.456, 1.5E-3.
                // Deviation from highlight.js, which does not support exponents (1.5 then plain E-3).
                new Mode { Match = @"\b\d[\d_]*(\.\d[\d_]*)?([eE][-+]?\d[\d_]*)?" },

                // Hexadecimal notation, e.g., $7F.
                new Mode { Match = @"\$[\dA-Fa-f_]+" },

                // Hexadecimal literal with no digits.
                new Mode { Match = @"\$" },

                // Octal notation, e.g., &42.
                new Mode { Match = "&[0-7][0-7_]*" },

                // Binary notation, e.g., %1010.
                new Mode { Match = "%[01_]+" },

                // Binary literal with no digits.
                new Mode { Match = "%" },
            ],
        };

        // Character codes: #13, #$0D, #&15, #%1101.
        var charString = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode { Match = @"#\d[\d_]*" },
                new Mode { Match = @"#\$[\dA-Fa-f][\dA-Fa-f_]*" },
                new Mode { Match = "#&[0-7][0-7_]*" },
                new Mode { Match = "#%[01][01_]*" },
            ],
        };

        // TFoo = class(TBar): the class name is a title. RunStart only lets the pattern start at the first character of
        // an identifier: it fails identically from every later position, which is quadratic on a long identifier.
        // Deviation from highlight.js, whose identifiers cannot start with `_` (the title of `_TFoo` is `TFoo`): here and
        // in function titles, they can, as in Pascal.
        var classMode = new Mode
        {
            Begin = CommonModes.RunStart(@"\w", "a-zA-Z_") + CommonModes.UnderscoreIdentRe + @"\s*=\s*class\s*\(",
            ReturnBegin = true,
            Contains = [CommonModes.UnderscoreTitleMode],
        };

        // Deviation from highlight.js, whose function heading only ends at `:` or `;`: it also ends before `begin`, `var`,
        // `const` and `of`, so an anonymous method without parameters (`procedure begin ... end`) and a method pointer
        // type (`procedure(Sender: TObject) of object`) do not turn the following words into titles.
        const string HeadingTerminatorRe = @"\b(?:begin|var|const|of)\b";
        var function = new Mode
        {
            Scope = "function",
            BeginKeywords = ["function", "constructor", "destructor", "procedure"],
            End = "[:;]|(?=" + HeadingTerminatorRe + ")",
            Contains =
            [
                new Mode { Scope = "title", Begin = "(?!" + HeadingTerminatorRe + ")" + CommonModes.UnderscoreIdentRe },
                new Mode
                {
                    Scope = "params",
                    Begin = @"\(",
                    End = @"\)",
                    Keywords = keywords,
                    Contains = [stringMode, charString, directive, .. comments],
                },
                directive,
                .. comments,
            ],
        };

        return new Mode
        {
            CaseInsensitive = true,
            Keywords = keywords,
            Illegal = @"""|\$[G-Zg-z]|\/\*|<\/|\|",
            Contains =
            [
                stringMode,
                charString,
                number,
                classMode,
                function,
                directive,
                .. comments,
            ],
        };
    }
}
