using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Pycon
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        return new Mode
        {
            Contains =
            [
                // Deviation from highlight.js: a statement starts from the root of the Python grammar. Upstream, it
                // resumed the state the previous one ended in, so after an unterminated string (a SyntaxError shown
                // in the session), every following statement was a string.
                CreatePrompt(@"^>>>(?=[ ]|$)", restartsPython: true),

                // Deviation from highlight.js: `...` is a continuation prompt only on the line after a prompt line, so
                // an output line starting with `...` stays plain. As upstream, a continuation line resumes the Python
                // state the previous line ended in, so a string can span several lines.
                CreatePrompt(@"^(?<=^(?:>>>|\.\.\.)(?:[ ][^\n]*)?\n)\.\.\.(?=[ ]|$)", restartsPython: false),
            ],
        };
    }

    private static Mode CreatePrompt(string begin, bool restartsPython)
    {
        return new Mode
        {
            Scope = "meta.prompt",
            Begin = begin,
            Starts = new Mode
            {
                // A space separates the prompt from the code; it is left out of both.
                End = " |$",
                Starts = new Mode
                {
                    End = "$",
                    SubLanguage = "python",
                    RestartsSubLanguage = restartsPython,
                },
            },
        };
    }
}
