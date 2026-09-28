using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

// https://dlang.org/spec/lex.html
internal static class D
{
    private const string DecimalIntegerRe = @"(0|[1-9][\d_]*)";
    private const string DecimalIntegerNoSuffixRe = @"(0|[1-9][\d_]*|\d[\d_]*|[\d_]+?\d)";
    private const string BinaryIntegerRe = "0[bB][01_]+";
    private const string HexadecimalDigitsRe = @"([\da-fA-F][\da-fA-F_]*|_[\da-fA-F][\da-fA-F_]*)";
    private const string HexadecimalIntegerRe = "0[xX]" + HexadecimalDigitsRe;
    private const string DecimalExponentRe = "([eE][+-]?" + DecimalIntegerNoSuffixRe + ")";

    // Deviation from highlight.js, which has no exponent after a fraction (`6.02e23` is `6.02` followed by `e23`) nor
    // underscores in it (`1.000_001`), and takes the dot of a range or slice (`1..10`) or of a UFCS call (`5.seconds`) as
    // a fraction.
    private const string DecimalFloatRe =
        "(" + DecimalIntegerNoSuffixRe + @"(\.(?![.a-zA-Z_])[\d_]*" + DecimalExponentRe + "?|" + DecimalExponentRe + ")|"
        + @"\d+\." + DecimalIntegerNoSuffixRe + "|"
        + @"\." + DecimalIntegerRe + DecimalExponentRe + "?"
        + ")";

    private const string HexadecimalFloatRe =
        "(0[xX](" + HexadecimalDigitsRe + @"\." + HexadecimalDigitsRe + "|" + @"\.?" + HexadecimalDigitsRe + ")[pP][+-]?" + DecimalIntegerNoSuffixRe + ")";

    // Deviation from highlight.js, which tries the decimal integer first, so that `0xFF` and `0b1010` are `0` followed by
    // an identifier.
    private const string IntegerRe = "(" + BinaryIntegerRe + "|" + HexadecimalIntegerRe + "|" + DecimalIntegerRe + ")";
    private const string FloatRe = "(" + HexadecimalFloatRe + "|" + DecimalFloatRe + ")";

    // Escape sequences of string and character literals.
    private const string EscapeSequenceRe =
        @"\\("
        + @"['""\?\\abfnrtv]|" // common escapes
        + @"u[\dA-Fa-f]{4}|" // four hex digit unicode codepoint
        + "[0-7]{1,3}|" // one to three octal digit ascii char code
        + @"x[\dA-Fa-f]{2}|" // two hex digit ascii char code
        + @"U[\dA-Fa-f]{8}" // eight hex digit unicode codepoint
        + ")|"
        + @"&[a-zA-Z\d]{2,};"; // named character entity

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var nestingComment = CommonModes.Comment(@"/\+", @"\+/");
        nestingComment.Contains = [Mode.Self, .. nestingComment.Contains];

        return new Mode
        {
            KeywordPattern = CommonModes.UnderscoreIdentRe,
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] =
                    "abstract alias align asm assert auto body break byte case cast catch class const continue debug default "
                    + "delete deprecated do else enum export extern final finally for foreach foreach_reverse|10 goto if "
                    + "immutable import in inout int interface invariant is lazy macro mixin module new nothrow out override "
                    + "package pragma private protected public pure ref return scope shared static struct super switch "
                    + "synchronized template this throw try typedef typeid typeof union unittest version void volatile while "
                    + "with __FILE__ __LINE__ __gshared|10 __thread __traits __DATE__ __EOF__ __TIME__ __TIMESTAMP__ __VENDOR__ "
                    + "__VERSION__",
                ["built_in"] =
                    "bool cdouble cent cfloat char creal dchar delegate double dstring float function idouble ifloat ireal long "
                    + "real short string ubyte ucent uint ulong ushort wchar wstring",
                ["literal"] = "false null true",
            }),
            Contains =
            [
                CommonModes.CLineCommentMode,
                CommonModes.CBlockCommentMode,
                nestingComment,

                // Hexadecimal string
                new Mode { Scope = "string", Begin = @"x""[\da-fA-F\s\n\r]*""[cwd]?" },
                new Mode
                {
                    Scope = "string",
                    Begin = "\"",
                    End = "\"[cwd]?",
                    Contains = [new Mode { Begin = EscapeSequenceRe }],
                },

                // WYSIWYG and delimited strings
                new Mode { Scope = "string", Begin = "[rq]\"", End = "\"[cwd]?" },

                // Alternate WYSIWYG string
                new Mode { Scope = "string", Begin = "`", End = "`[cwd]?" },

                // Token string
                new Mode { Scope = "string", Begin = @"q""\{", End = @"\}""" },

                // Deviation from highlight.js, which tries the one-letter suffixes first, so that `1.5fi` is `1.5f`
                // followed by `i`, and `42UL` is `42U` followed by `L`.
                new Mode
                {
                    Scope = "number",
                    Begin = @"\b(" + FloatRe + "([fF]i|Li|[fF]|L|i)?|" + IntegerRe + "(i|[fF]i|Li))",
                },
                new Mode
                {
                    Scope = "number",
                    Begin = @"\b" + IntegerRe + "(Lu|LU|uL|UL|L|u|U)?",
                },
                new Mode
                {
                    Scope = "string",
                    Begin = "'(" + EscapeSequenceRe + "|.)",
                    End = "'",
                    Illegal = ".",
                },

                // Hashbang
                new Mode { Scope = "meta", Begin = "^#!", End = "$" },

                // Special token sequence
                new Mode { Scope = "meta", Begin = "#(line)", End = "$" },

                // Attribute
                new Mode { Scope = "keyword", Begin = @"@[a-zA-Z_][a-zA-Z_\d]*" },
            ],
        };
    }
}
