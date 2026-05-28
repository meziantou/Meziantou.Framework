using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Bash
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var root = new Mode { KeywordPattern = "\\b[a-z][a-z0-9._-]+\\b", Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["keyword"] = ["if", "then", "else", "elif", "fi", "time", "for", "while", "until", "in", "do", "done", "case", "esac", "coproc", "function", "select"], ["literal"] = ["true", "false"], ["built_in"] = ["break", "cd", "continue", "eval", "exec", "exit", "export", "getopts", "hash", "pwd", "readonly", "return", "shift", "test", "times", "trap", "umask", "unset", "alias", "bind", "builtin", "caller", "command", "declare", "echo", "enable", "help", "let", "local", "logout", "mapfile", "printf", "read", "readarray", "source", "sudo", "type", "typeset", "ulimit", "unalias", "set", "shopt", "autoload", "bg", "bindkey", "bye", "cap", "chdir", "clone", "comparguments", "compcall", "compctl", "compdescribe", "compfiles", "compgroups", "compquote", "comptags", "comptry", "compvalues", "dirs", "disable", "disown", "echotc", "echoti", "emulate", "fc", "fg", "float", "functions", "getcap", "getln", "history", "integer", "jobs", "kill", "limit", "log", "noglob", "popd", "print", "pushd", "pushln", "rehash", "sched", "setcap", "setopt", "stat", "suspend", "ttyctl", "unfunction", "unhash", "unlimit", "unsetopt", "vared", "wait", "whence", "where", "which", "zcompile", "zformat", "zftp", "zle", "zmodload", "zparseopts", "zprof", "zpty", "zregexparse", "zsocket", "zstyle", "ztcp", "chcon", "chgrp", "chown", "chmod", "cp", "dd", "df", "dir", "dircolors", "ln", "ls", "mkdir", "mkfifo", "mknod", "mktemp", "mv", "realpath", "rm", "rmdir", "shred", "sync", "touch", "truncate", "vdir", "b2sum", "base32", "base64", "cat", "cksum", "comm", "csplit", "cut", "expand", "fmt", "fold", "head", "join", "md5sum", "nl", "numfmt", "od", "paste", "ptx", "pr", "sha1sum", "sha224sum", "sha256sum", "sha384sum", "sha512sum", "shuf", "sort", "split", "sum", "tac", "tail", "tr", "tsort", "unexpand", "uniq", "wc", "arch", "basename", "chroot", "date", "dirname", "du", "echo", "env", "expr", "factor", "groups", "hostid", "id", "link", "logname", "nice", "nohup", "nproc", "pathchk", "pinky", "printenv", "printf", "pwd", "readlink", "runcon", "seq", "sleep", "stat", "stdbuf", "stty", "tee", "test", "timeout", "tty", "uname", "unlink", "uptime", "users", "who", "whoami", "yes"] }) };
        var shebangKnownShell = new Mode { Scope = "meta", Begin = "^#![ ]*\\/.*\\b(fish|bash|zsh|sh|csh|ksh|tcsh|dash|scsh)\\b.*", End = "$" };
        var shebangGeneric = new Mode { Scope = "meta", Begin = "^#![ ]*\\/", End = "$" };
        var functionDef = new Mode { Scope = "function", Begin = "\\w[\\w\\d_]*\\s*\\(\\s*\\)\\s*\\{", ReturnBegin = true };
        var functionName = new Mode { Scope = "title", Begin = "\\w[\\w\\d_]*" };
        var arithmeticExpansion = new Mode { Begin = "\\$?\\(\\(", End = "\\)\\)" };
        var basedNumber = new Mode { Scope = "number", Begin = "\\d+#[0-9a-f]+" };
        var decimalNumber = new Mode { Scope = "number", Begin = "\\b\\d+(\\.\\d+)?" };
        var variable = new Mode { Scope = "variable" };
        var simpleVariable = new Mode { Begin = "\\$[\\w\\d#@][\\w\\d_]*(?![\\w\\d])(?![$])" };
        var bracedVariable = new Mode { Begin = "\\$\\{", End = "\\}" };
        var defaultValueOperator = new Mode { Begin = ":-" };
        var comment = new Mode { BeginParts = ["(^|\\s)", "#.*$"], BeginScope = new Dictionary<int, string> { [2] = "comment" } };
        var docTag = new Mode { Scope = "doctag", Begin = "[ ]*(?=(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):)", End = "(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):", ExcludeBegin = true };
        var commentSentence = new Mode { Begin = "[ ]+((?:I|a|is|so|us|to|at|if|in|it|on|[A-Za-z]+['](d|ve|re|ll|t|s|n)|[A-Za-z]+[-][a-z]+|[A-Za-z][a-z]{2,})[.]?[:]?([.][ ]|[ ])){3}" };
        var heredocStart = new Mode { Begin = "<<-?\\s*(?=\\w+)" };
        var heredocBody = new Mode { };
        var heredocString = new Mode { Scope = "string", Begin = "(\\w+)", End = "(\\w+)", EndSameAsBegin = true };
        var path = new Mode { Match = "(\\/[a-z._-]+)+" };
        var doubleQuotedString = new Mode { Scope = "string", Begin = "\"", End = "\"" };
        var escapeSequence = new Mode { Begin = "\\\\[\\s\\S]" };
        var commandSubstitution = new Mode { Scope = "subst", Begin = "\\$\\(", End = "\\)" };
        var escapedDoubleQuote = new Mode { Match = "\\\\\"" };
        var singleQuotedString = new Mode { Scope = "string", Begin = "'", End = "'" };
        var escapedSingleQuote = new Mode { Match = "\\\\'" };

        root.Contains = [shebangKnownShell, shebangGeneric, functionDef, arithmeticExpansion, comment, heredocStart, path, doubleQuotedString, escapedDoubleQuote, singleQuotedString, escapedSingleQuote, variable];
        functionDef.Contains = [functionName];
        arithmeticExpansion.Contains = [basedNumber, decimalNumber, variable];
        variable.Variants = [simpleVariable, bracedVariable];
        bracedVariable.Contains = [bracedVariable, defaultValueOperator];
        defaultValueOperator.Contains = [variable];
        comment.Contains = [docTag, commentSentence];
        heredocStart.Starts = heredocBody;
        heredocBody.Contains = [heredocString];
        doubleQuotedString.Contains = [escapeSequence, variable, commandSubstitution];
        commandSubstitution.Contains = [escapeSequence, doubleQuotedString];

        return root;
    }
}
