using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

// https://www.tcl-lang.org/man/tcl/TclCmd/Tcl.htm
internal static class Tcl
{
    private const string IdentRe = "[a-zA-Z_][a-zA-Z0-9_]*";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var number = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode { Begin = @"\b(0b[01]+)" },
                new Mode { Begin = CommonModes.CNumberRe },
            ],
        };

        return new Mode
        {
            Keywords = Engine.Keywords.FromWords(
            [
                "after", "append", "apply", "array", "auto_execok", "auto_import", "auto_load", "auto_mkindex", "auto_mkindex_old",
                "auto_qualify", "auto_reset", "bgerror", "binary", "break", "catch", "cd", "chan", "clock", "close", "concat",
                "continue", "dde", "dict", "encoding", "eof", "error", "eval", "exec", "exit", "expr", "fblocked", "fconfigure",
                "fcopy", "file", "fileevent", "filename", "flush", "for", "foreach", "format", "gets", "glob", "global", "history",
                "http", "if", "incr", "info", "interp", "join", "lappend|10", "lassign|10", "lindex|10", "linsert|10", "list",
                "llength|10", "load", "lrange|10", "lrepeat|10", "lreplace|10", "lreverse|10", "lsearch|10", "lset|10",
                "lsort|10", "mathfunc", "mathop", "memory", "msgcat", "namespace", "open", "package", "parray", "pid",
                "pkg::create", "pkg_mkIndex", "platform", "platform::shell", "proc", "puts", "pwd", "read", "refchan", "regexp",
                "registry", "regsub|10", "rename", "return", "safe", "scan", "seek", "set", "socket", "source", "split", "string",
                "subst", "switch", "tcl_endOfWord", "tcl_findLibrary", "tcl_startOfNextWord", "tcl_startOfPreviousWord",
                "tcl_wordBreakAfter", "tcl_wordBreakBefore", "tcltest", "tclvars", "tell", "time", "tm", "trace", "unknown",
                "unload", "unset", "update", "uplevel", "upvar", "variable", "vwait", "while",
            ]),
            Contains =
            [
                CommonModes.Comment(@";[ \t]*#", "$"),
                CommonModes.Comment(@"^[ \t]*#", "$"),
                new Mode
                {
                    BeginKeywords = ["proc"],
                    End = @"[\{]",
                    ExcludeEnd = true,
                    Contains =
                    [
                        // RunStart: the pattern fails identically from every position of a run of whitespace that is not
                        // followed by a name, which is quadratic on a long one.
                        new Mode
                        {
                            Scope = "title",
                            Begin = CommonModes.RunStart(@" \t\n\r") + @"[ \t\n\r]+(::)?[a-zA-Z_]((::)?[a-zA-Z0-9_])*",
                            End = @"[ \t\n\r]",
                            EndsWithParent = true,
                            ExcludeEnd = true,
                        },
                    ],
                },
                new Mode
                {
                    Scope = "variable",
                    Variants =
                    [
                        new Mode { Begin = @"\$(?:::)?" + IdentRe + "(::" + IdentRe + ")*" },
                        new Mode
                        {
                            Begin = @"\$\{(::)?[a-zA-Z_]((::)?[a-zA-Z0-9_])*",
                            End = @"\}",
                            Contains = [number],
                        },
                    ],
                },
                new Mode
                {
                    Scope = "string",
                    Begin = "\"",
                    End = "\"",
                    Contains = [CommonModes.BackslashEscape],
                },
                number,
            ],
        };
    }
}
