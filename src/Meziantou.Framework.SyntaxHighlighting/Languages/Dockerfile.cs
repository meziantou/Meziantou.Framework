using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Dockerfile
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var root = new Mode { Illegal = "</", CaseInsensitive = true, Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["keyword"] = ["from", "maintainer", "expose", "env", "arg", "user", "onbuild", "stopsignal"] }) };
        var comment = new Mode { Scope = "comment", Begin = "#", End = "$" };
        var docTag = new Mode { Scope = "doctag", Begin = "[ ]*(?=(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):)", End = "(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):", ExcludeBegin = true };
        var commentSentence = new Mode { Begin = "[ ]+((?:I|a|is|so|us|to|at|if|in|it|on|[A-Za-z]+['](d|ve|re|ll|t|s|n)|[A-Za-z]+[-][a-z]+|[A-Za-z][a-z]{2,})[.]?[:]?([.][ ]|[ ])){3}" };
        var singleQuotedString = new Mode { Scope = "string", Begin = "'", End = "'", Illegal = "\\n" };
        var escapeSequence = new Mode { Begin = "\\\\[\\s\\S]" };
        var doubleQuotedString = new Mode { Scope = "string", Begin = "\"", End = "\"", Illegal = "\\n" };
        var number = new Mode { Scope = "number", Begin = "\\b\\d+(\\.\\d+)?" };
        var shellInstruction = new Mode { BeginKeywords = ["run", "cmd", "entrypoint", "volume", "add", "copy", "workdir", "label", "healthcheck", "shell"] };
        var shellBody = new Mode { End = "[^\\\\]$", SubLanguage = "bash" };

        root.Contains = [comment, singleQuotedString, doubleQuotedString, number, shellInstruction];
        comment.Contains = [docTag, commentSentence];
        singleQuotedString.Contains = [escapeSequence];
        doubleQuotedString.Contains = [escapeSequence];
        shellInstruction.Starts = shellBody;

        return root;
    }
}
