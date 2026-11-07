using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Shell
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        return new Mode
        {
            Contains =
            [
                new Mode
                {
                    Scope = "meta.prompt",

                    // Spaces cannot be part of the prompt, otherwise "echo /path/to/home > t.exe" would be a prompt.
                    // Deviations from highlight.js:
                    // - `:` and `.` are allowed, so the common `user@host:~/src$ ` prompt is not output;
                    // - the indentation cannot contain line breaks (`\s{0,3}` upstream), so the blank lines before a
                    //   prompt are not part of it.
                    Begin = @"^[^\S\n]{0,3}[/~\w\d[\]()@:.-]*[>%$#][ ]?",
                    Starts = new Mode
                    {
                        // The command ends at the last character of the line that is not a backslash (a line
                        // continuation). A whitespace character preceded by another one can never be the leftmost
                        // match, so it is skipped to keep a long run of spaces from being rescanned from each of its
                        // characters.
                        // Deviation from highlight.js: an empty command ends at the end of its line. Upstream, the
                        // next line (possibly another prompt) was part of the command.
                        End = @"(?:\G|(?<!\s)|(?!\s))[^\\](?=\s*$)|(?<!\\)$",
                        SubLanguage = "bash",
                    },
                },
            ],
        };
    }
}
