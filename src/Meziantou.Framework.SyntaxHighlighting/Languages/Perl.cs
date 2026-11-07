using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Perl
{
    // https://perldoc.perl.org/perlre#Modifiers (`aa` and `xx` are valid, hence the maximum length of 12).
    // Deviation from highlight.js: `e` (evaluate the replacement of `s///`) and `c` (complement of `tr///`) too.
    private const string RegexModifiersRe = "[dualxmsipngrec]{0,12}";

    // The delimiters of `s!a!b!`, `m|a|`, ..., captured so that the closing delimiter can match the same one.
    private const string RegexDelimitersRe = @"(!|\/|\||\?|'|""|#)";

    private static readonly string[] ReservedKeywords =
    [
        "abs", "accept", "alarm", "and", "atan2", "bind", "binmode", "bless", "break", "caller", "chdir", "chmod",
        "chomp", "chop", "chown", "chr", "chroot", "class", "close", "closedir", "connect", "continue", "cos", "crypt",
        "dbmclose", "dbmopen", "defined", "delete", "die", "do", "dump", "each", "else", "elsif", "endgrent",
        "endhostent", "endnetent", "endprotoent", "endpwent", "endservent", "eof", "eval", "exec", "exists", "exit",
        "exp", "fcntl", "field", "fileno", "flock", "for", "foreach", "fork", "format", "formline", "getc", "getgrent",
        "getgrgid", "getgrnam", "gethostbyaddr", "gethostbyname", "gethostent", "getlogin", "getnetbyaddr",
        "getnetbyname", "getnetent", "getpeername", "getpgrp", "getpriority", "getprotobyname", "getprotobynumber",
        "getprotoent", "getpwent", "getpwnam", "getpwuid", "getservbyname", "getservbyport", "getservent",
        "getsockname", "getsockopt", "given", "glob", "gmtime", "goto", "grep", "gt", "hex", "if", "index", "int",
        "ioctl", "join", "keys", "kill", "last", "lc", "lcfirst", "length", "link", "listen", "local", "localtime",
        "log", "lstat", "lt", "ma", "map", "method", "mkdir", "msgctl", "msgget", "msgrcv", "msgsnd", "my", "ne",
        "next", "no", "not", "oct", "open", "opendir", "or", "ord", "our", "pack", "package", "pipe", "pop", "pos",
        "print", "printf", "prototype", "push", "q", "qq", "quotemeta", "qw", "qx", "rand", "read", "readdir",
        "readline", "readlink", "readpipe", "recv", "redo", "ref", "rename", "require", "reset", "return", "reverse",
        "rewinddir", "rindex", "rmdir", "say", "scalar", "seek", "seekdir", "select", "semctl", "semget", "semop",
        "send", "setgrent", "sethostent", "setnetent", "setpgrp", "setpriority", "setprotoent", "setpwent",
        "setservent", "setsockopt", "shift", "shmctl", "shmget", "shmread", "shmwrite", "shutdown", "sin", "sleep",
        "socket", "socketpair", "sort", "splice", "split", "sprintf", "sqrt", "srand", "stat", "state", "study", "sub",
        "substr", "symlink", "syscall", "sysopen", "sysread", "sysseek", "system", "syswrite", "tell", "telldir", "tie",
        "tied", "time", "times", "tr", "truncate", "uc", "ucfirst", "umask", "undef", "unless", "unlink", "unpack",
        "unshift", "untie", "until", "use", "utime", "values", "vec", "wait", "waitpid", "wantarray", "warn", "when",
        "while", "write", "x", "xor", "y",

        // Deviation from highlight.js, which lists `lt`, `gt` and `ne` but not the other string comparison operators.
        "eq", "le", "ge", "cmp",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromWords(ReservedKeywords);
        const string KeywordPattern = @"[\w.]+";

        var subst = new Mode { Scope = "subst", Begin = @"[$@]\{", End = @"\}", Keywords = keywords, KeywordPattern = KeywordPattern };
        var method = new Mode { Begin = @"->\{", End = @"\}" };
        var attribute = new Mode { Scope = "attr", Match = @"\s+:\s*\w+(\s*\(.*?\))?" };
        var variable = CreateVariable("[$%@]", attribute);

        // Deviation from highlight.js: `%` does not interpolate in a string, so `printf "%s"` has no hash variable.
        var interpolatedVariable = CreateVariable("[$@]", attribute);

        var number = new Mode
        {
            Scope = "number",
            Variants =
            [
                // A number that starts with a dot (`.9`); the leading `0?` avoids mixing it with the next variant on `0.x`.
                // Deviations from highlight.js: a single digit after the dot (`.5`) and an exponent are allowed, and
                // the end of a range (`1..10`) is not a decimal number.
                new Mode { Match = @"(?<![\w.])0?\.[0-9][0-9_]*(?:[eE][+-]?[0-9_]+)?\b" },
                // Also versions (`v5.38`). Deviation from highlight.js: an exponent is allowed (`1.5e-3`).
                new Mode { Match = @"\bv?(0|[1-9][0-9_]*(\.[0-9_]+)?|[1-9][0-9_]*)(?:[eE][+-]?[0-9_]+)?\b" },
                new Mode { Match = @"\b0[0-7][0-7_]*\b" },
                new Mode { Match = @"\b0x[0-9a-fA-F][0-9a-fA-F_]*\b" },
                new Mode { Match = @"\b0b[0-1][0-1_]*\b" },
            ],
        };

        Mode[] stringContains = [CommonModes.BackslashEscape, subst, interpolatedVariable];

        // Deviation from highlight.js, which has no heredoc support: `<<"EOF"`, `<<'EOF'`, `<<EOF` and their `<<~`
        // forms. The lookahead makes sure that the terminator line is there. Only a single-quoted heredoc is not
        // interpolated.
        const string HeredocBodyRe = @"[^\n]*\n(?:[^\n]*\n)*?[ \t]*\1$)";
        var heredocTerminator = new Mode { Begin = @"(\w+)", End = @"^[ \t]*(\w+)$", EndSameAsBegin = true };

        var strings = new Mode
        {
            Scope = "string",
            Contains = stringContains,
            Variants =
            [
                new Mode { Begin = @"q[qwxr]?\s*\(", End = @"\)" },
                new Mode { Begin = @"q[qwxr]?\s*\[", End = @"\]" },
                new Mode { Begin = @"q[qwxr]?\s*\{", End = @"\}" },
                new Mode { Begin = @"q[qwxr]?\s*\|", End = @"\|" },
                new Mode { Begin = @"q[qwxr]?\s*<", End = ">" },
                // Deviation from highlight.js: `qw/a b/`. It must start a word so that `$freq/2` is a division, and `qr//`
                // stays a regexp.
                new Mode { Begin = @"\bq[qwx]?/", End = "/" },
                new Mode { Begin = @"qw\s+q", End = "q" },
                new Mode { Begin = "'", End = "'", Contains = [CommonModes.BackslashEscape] },
                new Mode { Begin = "\"", End = "\"" },
                new Mode { Begin = "`", End = "`", Contains = [CommonModes.BackslashEscape] },
                new Mode { Begin = @"\{\w+\}" },
                // Only the first position of a word is tried: otherwise, each character of a long word that is not
                // followed by `=>` would rescan the rest of the word.
                new Mode { Begin = "(?:-|" + CommonModes.RunStart(@"\w") + @")\w+\s*=>" },
                new Mode
                {
                    Begin = @"<<~?'(?=(\w+)'" + HeredocBodyRe,
                    Contains = [new Mode(heredocTerminator)],
                },
                new Mode
                {
                    Begin = @"<<~?""?(?=(\w+)""?" + HeredocBodyRe,
                    Contains = [new Mode(heredocTerminator) { Contains = stringContains }],
                },
            ],
        };

        var regexpContainer = new Mode
        {
            Begin = @"(\/\/|" + CommonModes.ReStartersRe + @"|\b(split|return|print|reverse|grep)\b)\s*",
            Keywords = Engine.Keywords.FromWords(["split", "return", "print", "reverse", "grep"]),
            Contains =
            [
                CommonModes.HashCommentMode,
                new Mode
                {
                    Scope = "regexp",
                    Variants =
                    [
                        // Common delimiters
                        new Mode { Begin = PairedDoubleRe("s|tr|y", RegexDelimitersRe, @"\1", @"(?!\1)[^\\]") },
                        // Paired delimiters
                        new Mode { Begin = PairedDoubleRe("s|tr|y", @"\(", @"\)", @"[^\\)]") },
                        new Mode { Begin = PairedDoubleRe("s|tr|y", @"\[", @"\]", @"[^\\\]]") },
                        new Mode { Begin = PairedDoubleRe("s|tr|y", @"\{", @"\}", @"[^\\}]") },
                    ],
                },
                new Mode
                {
                    Scope = "regexp",
                    Variants =
                    [
                        new Mode { Begin = @"(m|qr)\/\/" },
                        // The prefix is optional with /regex/
                        new Mode { Begin = PairedRe("(?:m|qr)?", @"\/", @"\/", @"[^\\\/]") },
                        // Common delimiters
                        new Mode { Begin = PairedRe("m|qr", RegexDelimitersRe, @"\1", @"(?!\1)[^\\]") },
                        // Paired delimiters
                        new Mode { Begin = PairedRe("m|qr", @"\(", @"\)", @"[^\\)]") },
                        new Mode { Begin = PairedRe("m|qr", @"\[", @"\]", @"[^\\\]]") },
                        new Mode { Begin = PairedRe("m|qr", @"\{", @"\}", @"[^\\}]") },
                    ],
                },
            ],
        };

        Mode[] defaultContains =
        [
            variable,
            CommonModes.HashCommentMode,
            // POD
            new Mode(CommonModes.Comment(@"^=\w", "=cut")) { EndsWithParent = true },
            method,
            strings,
            number,
            regexpContainer,
            new Mode
            {
                Scope = "function",
                BeginKeywords = ["sub", "method"],
                End = "[;{]",
                ExcludeEnd = true,
                // Deviation from highlight.js, which skips a prototype or a signature only when it is directly followed
                // by `{` or `;`, so that the parameters of `sub add ($x, $y) {` were titles: the parameters are
                // variables, and a prototype (`($$)`) is plain text.
                Contains =
                [
                    CommonModes.TitleMode,
                    attribute,
                    new Mode
                    {
                        Begin = @"\(",
                        End = @"\)",
                        Contains = [new Mode { Scope = "variable", Begin = @"[$@%]\w+" }, number, strings],
                    },
                ],
            },
            new Mode
            {
                Scope = "class",
                BeginKeywords = ["class"],
                End = "[;{]",
                ExcludeEnd = true,
                Contains = [CommonModes.TitleMode, attribute, number],
            },
            new Mode { Begin = @"-\w\b" },
            // highlight.js highlights the data section as `mojolicious`, which is not supported: it is plain text,
            // like highlight.js renders it when that language is not registered.
            new Mode
            {
                Begin = "^__DATA__$",
                End = "^__END__$",
                Contains = [new Mode { Scope = "comment", Begin = "^@@.*", End = "$" }],
            },
        ];

        subst.Contains = defaultContains;
        method.Contains = defaultContains;

        return new Mode
        {
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
            Contains = defaultContains,
        };
    }

    private static Mode CreateVariable(string sigil, Mode attribute) => new()
    {
        Scope = "variable",
        Contains = [attribute],
        Variants =
        [
            new Mode { Begin = @"\$\d" },
            // The negative lookaheads try to avoid matching patterns that are not Perl at all, like `$ident$`.
            // Deviation from highlight.js: a dereference (`$$ref`, `@$ref`) is one variable, not the `$$` variable.
            new Mode { Begin = sigil + @"(?!"")\$?(\^\w\b|#\w+(::\w+)*|\{\w+\}|\w+(::\w*)*)(?![A-Za-z])(?![@$%])" },
            // Only `$=` is a special variable; one can't declare `@=` or `%=`.
            new Mode { Begin = sigil + @"(?!"")[^\s\w{=]|\$=" },
        ],
    };

    // A regexp content character is anything but a backslash and the closing delimiter, or an escaped character.
    // Deviation from highlight.js, which excludes the slash whatever the delimiter: `m{^/path}` and `s!/!\\!` are
    // regexps.
    private static string PairedDoubleRe(string prefix, string open, string close, string contentCharacter)
    {
        var middle = close is @"\1" ? close : close + open;
        var content = @"(?:\\.|" + contentCharacter + ")*?";
        return "(?:" + prefix + ")" + open + content + middle + content + close + RegexModifiersRe;
    }

    private static string PairedRe(string prefix, string open, string close, string contentCharacter)
    {
        return "(?:" + prefix + ")" + open + @"(?:\\.|" + contentCharacter + ")*?" + close + RegexModifiersRe;
    }
}
