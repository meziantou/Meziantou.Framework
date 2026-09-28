using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <summary>
/// TOML (Tom's Obvious Minimal Language).
/// </summary>
/// <remarks>
/// highlight.js highlights TOML with its INI grammar, which does not know TOML values (multi-line strings, arrays spanning
/// lines, date-times, ...). This grammar is written from scratch, and keeps the scopes the INI grammar used: table and
/// array-of-tables headers are sections, keys (bare, quoted and dotted) are attrs, strings are strings, numbers and
/// date-times are numbers, and booleans are literals. Escape sequences in basic strings are <c>char.escape</c>.
/// Arrays and inline tables are not scoped. An array or inline table that is never closed ends before the next line that
/// cannot be part of it (a <c>key =</c> line for an array, a table header for an inline table), so it does not swallow
/// the rest of the document.
/// See https://toml.io/en/v1.0.0 (and https://toml.io/en/v1.1.0 for the escapes and optional seconds it adds).
/// </remarks>
internal static class Toml
{
    private const string BareKeyCharacter = @"A-Za-z0-9_\-";
    private const string SimpleKey = @"(?:[A-Za-z0-9_\-]+|""(?:[^""\\\n]|\\.)*""|'[^'\n]*')";
    private const string DottedKey = SimpleKey + @"(?:[ \t]*\.[ \t]*" + SimpleKey + ")*";

    // A key that continues a bare key (or a dotted key) begun earlier would end on the same `=`, so it can never be the
    // leftmost match; skipping it keeps a long key from being rescanned from each of its characters. A quote right after
    // a backslash is an escaped quote inside a quoted key, but a bare key may well follow a backslash.
    private const string KeyStart = @"(?:\G|(?<![" + BareKeyCharacter + @"])(?=[" + BareKeyCharacter + @"])|(?<!\\)(?=[""']))(?<![" + BareKeyCharacter + @"""'][ \t]*\.[ \t]*)";

    // A value cannot follow or be followed by a character that would make it part of a longer word.
    private const string ValueStart = @"(?<![\w.+\-:])";
    private const string ValueEnd = @"(?![\w.+\-:])";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var comment = CommonModes.Comment("#", "$");

        // `[table]`, `[[array.of.tables]]`, `[ a . "b c" ]`. Only at the start of a line: elsewhere, `[` opens an array.
        var header = new Mode
        {
            Scope = "section",
            Match = @"(?=\[)(?<=^[ \t]*)\[\[?[ \t]*" + DottedKey + @"[ \t]*\]\]?",
        };

        var key = new Mode
        {
            Scope = "attr",
            Match = KeyStart + DottedKey + @"(?=[ \t]*=)",
        };

        Mode[] escapes =
        [
            new Mode { Scope = "char.escape", Match = @"\\(?:[btnfre""\\]|x[0-9A-Fa-f]{2}|u[0-9A-Fa-f]{4}|U[0-9A-Fa-f]{8})" },

            // An invalid escape still cannot end the string.
            new Mode { Match = @"\\." },
        ];

        // Up to two quotes can directly precede the closing delimiter (`"""a""""` is `a"`), so the run of quotes that
        // ends the string is part of it.
        var multiLineBasicString = new Mode
        {
            Scope = "string",
            Begin = "\"\"\"",
            End = "\"{3,5}",
            Contains =
            [
                // A backslash at the end of a line trims the line break and the whitespace that follows it.
                new Mode { Scope = "char.escape", Match = @"\\(?=[ \t]*$)" },
                .. escapes,
            ],
        };

        var multiLineLiteralString = new Mode
        {
            Scope = "string",
            Begin = "'''",
            End = "'{3,5}",
        };

        // Single-line strings cannot span lines, so an unterminated one ends with its line.
        var basicString = new Mode
        {
            Scope = "string",
            Begin = "\"",
            End = "\"|$",
            Contains = escapes,
        };

        var literalString = new Mode
        {
            Scope = "string",
            Begin = "'",
            End = "'|$",
        };

        // Offset date-times, local date-times, local dates and local times: `1979-05-27T07:32:00Z`,
        // `1979-05-27 07:32:00.999999-07:00`, `1979-05-27`, `07:32:00`. TOML 1.1 makes the seconds optional.
        const string Time = @"\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?";
        var dateTime = new Mode
        {
            Scope = "number",
            Match = ValueStart + @"(?:\d{4}-\d{2}-\d{2}(?:[Tt ]" + Time + @"(?:[Zz]|[+\-]\d{2}:\d{2})?)?|" + Time + ")" + ValueEnd,
        };

        const string DecimalInteger = @"[+\-]?(?:0|[1-9](?:_?\d)*)";
        const string Digits = @"\d(?:_?\d)*";
        var number = new Mode
        {
            Scope = "number",
            Match = ValueStart + "(?:"
                + @"0x[0-9A-Fa-f](?:_?[0-9A-Fa-f])*"
                + @"|0o[0-7](?:_?[0-7])*"
                + @"|0b[01](?:_?[01])*"
                + "|" + DecimalInteger + @"(?:\." + Digits + @")?(?:[eE][+\-]?" + Digits + ")?"
                + @"|[+\-]?(?:inf|nan)"
                + ")" + ValueEnd,
        };

        var boolean = new Mode
        {
            Scope = "literal",
            Match = ValueStart + "(?:true|false)" + ValueEnd,
        };

        // A line that starts with `key =` cannot be in an array.
        const string ArrayEnd = @"\]|^(?=[ \t]*" + DottedKey + @"[ \t]*=)";

        // An array can start a line only inside another array: at the top level that is a table header, and in an inline
        // table a value follows its key on the same line.
        var array = new Mode
        {
            Begin = @"(?<!^[ \t]*)\[",
            End = ArrayEnd,
        };

        var nestedArray = new Mode
        {
            Begin = @"\[",
            End = ArrayEnd,
        };

        var inlineTable = new Mode
        {
            Begin = @"\{",

            // Inline tables are on one line in TOML 1.0, but TOML 1.1 lets them span lines. A table header cannot be in
            // one.
            End = @"\}|^(?=[ \t]*\[)",
        };

        Mode[] scalars = [multiLineBasicString, multiLineLiteralString, basicString, literalString, dateTime, number, boolean];
        Mode[] values = [.. scalars, array, inlineTable];
        Mode[] arrayContents = [comment, .. scalars, nestedArray, inlineTable];
        array.Contains = arrayContents;
        nestedArray.Contains = arrayContents;
        inlineTable.Contains = [comment, key, .. values];

        return new Mode
        {
            Contains = [comment, header, key, .. values],
        };
    }
}
