using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class ClojureRepl
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        return new Mode
        {
            Contains =
            [
                // The prompt of a form is the current namespace followed by `=>` (e.g. `user=>`).
                // Deviation from highlight.js: a form starts from the root of the Clojure grammar. Upstream, it resumed
                // the state the previous one ended in, so after an unterminated string or an unbalanced parenthesis (an
                // error shown in the session), every following form was a string or stayed nested.
                CreatePrompt(@"^[\w.-]*=>", restartsClojure: true),

                // Leiningen's REPL prompts the next lines of a form with `#_=>`, aligned with the first one. As
                // upstream, a continuation line resumes the state the previous line ended in, so a form can span
                // several lines.
                // Deviation from highlight.js: the indentation cannot contain line breaks (`\s*` upstream), so the
                // blank lines before a continuation prompt are not part of it.
                CreatePrompt(@"^[^\S\n]*#_=>", restartsClojure: false),
            ],
        };
    }

    private static Mode CreatePrompt(string begin, bool restartsClojure)
    {
        return new Mode
        {
            Scope = "meta.prompt",
            Begin = begin,
            Starts = new Mode
            {
                End = "$",
                SubLanguage = "clojure",
                RestartsSubLanguage = restartsClojure,
            },
        };
    }
}
