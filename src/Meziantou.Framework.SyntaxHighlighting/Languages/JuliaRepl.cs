using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class JuliaRepl
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
                    // The code starts right after `julia>`, and goes on while the next lines are indented by at least
                    // six spaces (aligned with the code of the first line). The other lines are output, left plain.
                    Scope = "meta.prompt",
                    Begin = "^julia>",
                    Starts = new Mode
                    {
                        End = "^(?![ ]{6})",
                        SubLanguage = "julia",

                        // Deviation from highlight.js: each prompt starts from the root of the Julia grammar. Upstream,
                        // it resumed the state the previous one ended in, so after an unterminated string (an error
                        // shown in the session), the code of every following prompt was a string.
                        RestartsSubLanguage = true,
                    },
                },
            ],
        };
    }
}
