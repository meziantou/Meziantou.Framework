using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages.Common;

internal static class ReplModes
{
    /// <summary>
    /// Creates the modes of a REPL session whose input lines start with a prompt followed by a space, and whose output
    /// lines are left plain (the highlight.js python-repl and node-repl grammars): the code after a prompt is highlighted
    /// with <paramref name="subLanguage"/> up to the end of its line.
    /// </summary>
    /// <param name="prompt">The pattern of the prompt that starts a statement (e.g. <c>&gt;&gt;&gt;</c>).</param>
    /// <param name="continuationPrompt">The pattern of the prompt of the next lines of a statement (e.g. <c>\.\.\.</c>).</param>
    /// <param name="subLanguage">The language of the code.</param>
    public static Mode[] CreatePrompts(string prompt, string continuationPrompt, string subLanguage)
    {
        return
        [
            // Deviation from highlight.js: a statement starts from the root of the embedded grammar. Upstream, it
            // resumed the state the previous one ended in, so after an unterminated string (a SyntaxError shown in the
            // session), every following statement was a string.
            CreatePrompt("^" + prompt + "(?=[ ]|$)", subLanguage, restartsSubLanguage: true),

            // Deviation from highlight.js: a continuation prompt is one only on the line after a prompt line, so an
            // output line that starts like it stays plain. As upstream, a continuation line resumes the state the
            // previous line ended in, so a string can span several lines.
            CreatePrompt("^(?<=^(?:" + prompt + "|" + continuationPrompt + @")(?:[ ][^\n]*)?\n)" + continuationPrompt + "(?=[ ]|$)", subLanguage, restartsSubLanguage: false),
        ];
    }

    private static Mode CreatePrompt(string begin, string subLanguage, bool restartsSubLanguage)
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
                    SubLanguage = subLanguage,
                    RestartsSubLanguage = restartsSubLanguage,
                },
            },
        };
    }
}
