using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class VbScript
{
    private static readonly string[] BuiltInFunctions =
    [
        "lcase", "month", "vartype", "instrrev", "ubound", "setlocale", "getobject", "rgb", "getref", "string",
        "weekdayname", "rnd", "dateadd", "monthname", "now", "day", "minute", "isarray", "cbool", "round", "formatcurrency",
        "conversions", "csng", "timevalue", "second", "year", "space", "abs", "clng", "timeserial", "fixs", "len", "asc",
        "isempty", "maths", "dateserial", "atn", "timer", "isobject", "filter", "weekday", "datevalue", "ccur", "isdate",
        "instr", "datediff", "formatdatetime", "replace", "isnull", "right", "sgn", "array", "snumeric", "log", "cdbl", "hex",
        "chr", "lbound", "msgbox", "ucase", "getlocale", "cos", "cdate", "cbyte", "rtrim", "join", "hour", "oct", "typename",
        "trim", "strcomp", "int", "createobject", "loadpicture", "tan", "formatnumber", "mid", "split", "cint", "sin",
        "datepart", "ltrim", "sqr", "time", "derived", "eval", "date", "formatpercent", "exp", "inputbox", "left", "ascw",
        "chrw", "regexp", "cstr", "err",
    ];

    private static readonly string[] BuiltInObjects =
    [
        "server", "response", "request",

        // These take no arguments, so they can be called without parentheses.
        "scriptengine", "scriptenginebuildversion", "scriptengineminorversion", "scriptenginemajorversion",
    ];

    private static readonly string[] Literals = ["true", "false", "null", "nothing", "empty"];

    private static readonly string[] KeywordList =
    [
        "call", "class", "const", "dim", "do", "loop", "erase", "execute", "executeglobal", "exit", "for", "each", "next",
        "function", "if", "then", "else", "on", "error", "option", "explicit", "new", "private", "property", "let", "get",
        "public", "randomize", "redim", "rem", "select", "case", "set", "stop", "sub", "while", "wend", "with", "end", "to",
        "elseif", "is", "or", "xor", "and", "not", "class_initialize", "class_terminate", "default", "preserve", "in", "me",
        "byval", "byref", "step", "resume", "goto",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var builtInCall = new Mode
        {
            // Deviation from highlight.js: the function name must start a word. Upstream, the end of a longer name
            // was highlighted (`MyRound(` as `My` and a built-in `Round`, and `DoRound(` as a keyword `Do` and a
            // built-in `Round`).
            Begin = @"\b(?:" + string.Join('|', BuiltInFunctions) + @")\s*\(",
            Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["built_in"] = BuiltInFunctions,
            }),
        };

        return new Mode
        {
            CaseInsensitive = true,
            Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["keyword"] = KeywordList,
                ["built_in"] = BuiltInObjects,
                ["literal"] = Literals,
            }),
            Illegal = "//",
            Contains =
            [
                builtInCall,
                new Mode
                {
                    Scope = "string",
                    Begin = "\"",
                    End = "\"",
                    Illegal = @"\n",
                    Contains = [new Mode { Begin = "\"\"" }],
                },
                CommonModes.Comment("'", "$"),
                CommonModes.CNumberMode,
            ],
        };
    }
}
