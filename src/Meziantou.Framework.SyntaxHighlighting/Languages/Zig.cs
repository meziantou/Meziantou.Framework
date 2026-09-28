using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <summary>
/// Zig, and ZON (Zig Object Notation, the syntax of <c>build.zig.zon</c>).
/// </summary>
/// <remarks>
/// highlight.js has no Zig grammar, so this one is written from scratch following the scopes of the other grammars:
/// primitive types (including the arbitrary bit-width integers such as <c>u7</c>) are types, builtin functions
/// (<c>@import</c>, <c>@as</c>, …) are built-ins, the names of <c>fn</c> declarations are function titles, the names
/// of container declarations (<c>const Point = struct</c>) are class titles, labels (<c>blk:</c>, <c>break :blk</c>)
/// and enum literals (<c>.red</c>) are symbols, the fields of anonymous struct literals (<c>.{ .x = 1 }</c>) are attrs,
/// and doc comments (<c>///</c> and <c>//!</c>) are comments like the other line comments.
/// See https://ziglang.org/documentation/master/#Grammar.
/// </remarks>
internal static class Zig
{
    private const string IdentifierRe = "[a-zA-Z_][a-zA-Z0-9_]*";

    // An identifier can only start where no identifier character precedes it, which also keeps the patterns below
    // from being retried from every character of a long identifier.
    private static readonly string IdentifierStart = CommonModes.RunStart(@"\w", "a-zA-Z_");

    private static readonly string[] ReservedKeywords =
    [
        "addrspace", "align", "allowzero", "and", "anyframe", "anytype", "asm", "break", "callconv", "catch", "comptime",
        "const", "continue", "defer", "else", "enum", "errdefer", "error", "export", "extern", "fn", "for", "if", "inline",
        "linksection", "noalias", "noinline", "nosuspend", "opaque", "or", "orelse", "packed", "pub", "resume", "return",
        "struct", "suspend", "switch", "test", "threadlocal", "try", "union", "var", "volatile", "while",

        // Removed from recent versions of the language, but still common in existing code.
        "async", "await", "usingnamespace",
    ];

    // The arbitrary bit-width integers (`u7`, `i48`) are matched by a pattern, not listed here.
    private static readonly string[] Types =
    [
        "isize", "usize", "c_char", "c_short", "c_ushort", "c_int", "c_uint", "c_long", "c_ulong", "c_longlong",
        "c_ulonglong", "c_longdouble", "f16", "f32", "f64", "f80", "f128", "bool", "anyopaque", "void", "noreturn",
        "type", "anyerror", "comptime_int", "comptime_float",
    ];

    // `unreachable` is a keyword of the grammar, but it is used as a value (`else => unreachable`).
    private static readonly string[] Literals = ["true", "false", "null", "undefined", "unreachable"];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = ReservedKeywords,
            ["type"] = Types,
            ["literal"] = Literals,
        });

        // A string literal cannot span lines, so an unterminated one ends with its line.
        var stringLiteral = new Mode
        {
            Scope = "string",
            Begin = "\"",
            End = "\"|$",
            Contains = [CommonModes.BackslashEscape],
        };

        // Each line of a multiline string literal starts with `\\` and ends with the line.
        var lineString = new Mode
        {
            Scope = "string",
            Match = @"\\\\.*",
        };

        // One code point: a character, a surrogate pair, or an escape sequence (`'\n'`, `'\x41'`, `'\u{1F600}'`).
        var charLiteral = new Mode
        {
            Scope = "string",
            Match = @"'(?:\\(?:x[0-9a-fA-F]{2}|u\{[0-9a-fA-F]+\}|.)|[\uD800-\uDBFF][\uDC00-\uDFFF]|[^'\\\n])'",
        };

        // `@"a name with spaces"` is an identifier, not a string or a builtin.
        var quotedIdentifier = new Mode { Match = @"@""(?:[^""\\\n]|\\.)*""" };

        var builtinFunction = new Mode
        {
            Scope = "built_in",
            Match = "@" + IdentifierRe,
        };

        var numbers = new Mode
        {
            Scope = "number",
            Variants =
            [
                // Hexadecimal floats have a binary exponent (`0x1.8p3`).
                new Mode { Match = @"\b0x[0-9a-fA-F_]+(?:\.[0-9a-fA-F_]+)?(?:[pP][+-]?[0-9_]+)?" },
                new Mode { Match = @"\b0o[0-7_]+" },
                new Mode { Match = @"\b0b[01_]+" },

                // A digit must follow the point, so `0..10` is a range.
                new Mode { Match = @"\b\d[\d_]*(?:\.\d[\d_]*)?(?:[eE][+-]?\d[\d_]*)?" },
            ],
        };

        // `u7`, `i48`, `u0`: any `u` or `i` followed by a bit width is an integer type.
        var arbitraryIntegerType = new Mode
        {
            Scope = "type",
            Match = @"\b[iu](?:0|[1-9][0-9]*)\b",
        };

        var functionDeclaration = new Mode
        {
            BeginParts = [@"\bfn", @"\s+", IdentifierRe],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title.function",
            },
        };

        // `const Point = struct`, `pub const Color = enum(u8)`, `const Header = extern struct`, `const E = error{`.
        var containerDeclaration = new Mode
        {
            BeginParts = [@"\b(?:const|var)", @"\s+", IdentifierRe, @"\s*=\s*", @"(?:(?:extern|packed)(?=\s))?", @"\s*", @"(?:struct|enum|union|opaque)\b|error(?=\s*\{)"],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title.class",
                [5] = "keyword",
                [7] = "keyword",
            },
        };

        // `blk: {`, `outer: while (...)`, `inline for`. A struct field (`x: u8`) is not followed by a block or a loop.
        var labelDeclaration = new Mode
        {
            Scope = "symbol",
            Match = IdentifierStart + IdentifierRe + @"(?=:[ \t]*(?:\{|(?:inline[ \t]+)?(?:while|for|switch)\b))",
        };

        // `break :blk value`, `continue :outer`.
        var labelReference = new Mode
        {
            Scope = "symbol",
            Match = @"(?<=\b(?:break|continue)[ \t]*:)" + IdentifierRe,
        };

        // A `.` that does not follow an expression starts an enum literal (`.red`) or, followed by `=`, the field of
        // an anonymous struct literal (`.{ .x = 1 }`). After an expression (`a.b`, `f().b`, `ptr.*.b`), it is a field
        // access.
        const string NotAfterExpression = @"(?<![\w)\]}.""'?*])";
        var fieldInitializer = new Mode
        {
            Scope = "attr",
            Match = NotAfterExpression + @"\." + IdentifierRe + @"(?=\s*=(?![=>]))",
        };

        var enumLiteral = new Mode
        {
            Scope = "symbol",
            Match = NotAfterExpression + @"\." + IdentifierRe,
        };

        // A field access is not a keyword (`builtin.mode`, `std.Target.Cpu.Arch`); a method call is left to the call
        // pattern below.
        var fieldAccess = new Mode { Match = @"\." + IdentifierRe + @"\b(?!\s*\()" };

        // Keywords followed by `(` (`if (x)`, `enum(u8)`, `callconv(.C)`) are not calls.
        var functionCall = new Mode
        {
            Scope = "title.function.invoke",
            Match = IdentifierStart + @"(?!(?:" + string.Join('|', ReservedKeywords.Concat(Types).Concat(Literals)) + @")\b)" + IdentifierRe + @"(?=\s*\()",
        };

        return new Mode
        {
            Keywords = keywords,
            KeywordPattern = IdentifierRe,
            Contains =
            [
                CommonModes.CLineCommentMode,
                lineString,
                stringLiteral,
                charLiteral,
                quotedIdentifier,
                builtinFunction,
                numbers,
                arbitraryIntegerType,
                functionDeclaration,
                containerDeclaration,
                labelDeclaration,
                labelReference,
                fieldInitializer,
                enumLiteral,
                fieldAccess,
                functionCall,
            ],
        };
    }
}
