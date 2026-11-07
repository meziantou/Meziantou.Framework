using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Go
{
    private const string DecimalsRe = @"\d(?:_?\d)*";
    private const string HexDigitsRe = "[a-fA-F0-9](?:_?[a-fA-F0-9])*";
    private const string ExponentRe = "[eE][+-]?" + DecimalsRe;
    private const string HexExponentRe = "[pP][+-]?" + DecimalsRe;

    private static readonly string[] Literals = ["true", "false", "iota", "nil"];

    private static readonly string[] BuiltIns =
    [
        "append", "cap", "clear", "close", "complex", "copy", "delete", "imag", "len", "make", "max", "min", "new",
        "panic", "print", "println", "real", "recover",
    ];

    private static readonly string[] Types =
    [
        "any", "bool", "byte", "comparable", "complex64", "complex128", "error", "float32", "float64", "int8", "int16",
        "int32", "int64", "string", "uint8", "uint16", "uint32", "uint64", "int", "uint", "uintptr", "rune",
    ];

    private static readonly string[] ReservedKeywords =
    [
        "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough", "for", "func", "go",
        "goto", "if", "import", "interface", "map", "package", "range", "return", "select", "struct", "switch", "type",
        "var",
    ];

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

        var numbers = new Mode
        {
            Scope = "number",
            Variants =
            [
                // Hexadecimal without a digit before the point, so a digit is required after it.
                new Mode { Match = @"-?\b0[xX]\." + HexDigitsRe + HexExponentRe + "i?" },
                // Hexadecimal with a digit before the point, so the digits after it are optional.
                new Mode { Match = @"-?\b0[xX](?:_?[a-fA-F0-9])+(?:(?:\.(?:" + HexDigitsRe + ")?)?" + HexExponentRe + ")?i?" },
                new Mode { Match = @"-?\b0[bB](?:_?[01])+i?" },
                new Mode { Match = @"-?\b0[oO](?:_?[0-7])*i?" },
                // Decimal without a digit before the point, so a digit is required after it.
                new Mode { Match = @"-?\." + DecimalsRe + "(?:" + ExponentRe + ")?i?" },
                // Decimal with a digit before the point, so the digits after it are optional.
                new Mode { Match = @"-?\b" + DecimalsRe + @"(?:\.(?:" + DecimalsRe + ")?)?(?:" + ExponentRe + ")?i?" },
            ],
        };

        // A parameter can have a function type (`f func(int) error`), whose parentheses must not end the list.
        var nestedParentheses = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Keywords = keywords,
            Contains = [Mode.Self],
        };

        var parameters = new Mode
        {
            Scope = "params",
            Begin = @"\(",
            End = @"\)",
            Keywords = keywords,
            Illegal = "[\"']",
            Contains = [nestedParentheses],
        };

        return new Mode
        {
            Keywords = keywords,
            Illegal = "</",
            Contains =
            [
                CommonModes.CLineCommentMode,
                CommonModes.CBlockCommentMode,
                new Mode
                {
                    Scope = "string",
                    Variants =
                    [
                        CommonModes.QuoteStringMode,
                        CommonModes.AposStringMode,
                        new Mode { Begin = "`", End = "`" },
                    ],
                },
                numbers,
                new Mode
                {
                    Scope = "function",
                    BeginKeywords = ["func"],
                    End = @"\s*(?:\{|$)",
                    ExcludeEnd = true,
                    Contains =
                    [
                        // The receiver of a method (`func (s *Server) Start()`) is followed by the method name, so it
                        // must not end the declaration like the parameter list does. A keyword there is the result
                        // type of a function (`func adder() func(int) int`).
                        new Mode(parameters)
                        {
                            Begin = @"\((?=[^()]*\)\s*(?!(?:" + string.Join('|', ReservedKeywords) + @")\b)[a-zA-Z_]\w*\s*\()",
                        },
                        CommonModes.TitleMode,
                        // Type parameters (`func Map[T, U any]`) are not titles. A constraint can contain brackets
                        // (`[S ~[]E, E any]`).
                        new Mode
                        {
                            Begin = @"\[",
                            End = @"\]",
                            Keywords = keywords,
                            Contains = [Mode.Self, nestedParentheses],
                        },
                        new Mode(parameters)
                        {
                            EndsParent = true,
                        },
                    ],
                },
            ],
        };
    }
}
