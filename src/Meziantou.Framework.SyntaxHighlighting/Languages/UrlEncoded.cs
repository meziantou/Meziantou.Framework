using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class UrlEncoded
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        // A name or a value is only looked for from the first character of a run of name characters: from any other
        // character of the run, it succeeds or fails like from the first one, so trying it there was quadratic on a long
        // run that is not followed by `=`.
        var runStart = CommonModes.RunStart(@"^=&\s");
        return new Mode
        {
            Contains =
            [
                new() { Scope = "attr", Begin = runStart + @"[^=&\s]+(?==)" },
                new() { Scope = "punctuation", Begin = "[=&]" },

                // `[^&\s]+` before, which matched over the whole rest of a run of `=` or of `a=` after each punctuation
                // or name that wins against it at the same position (quadratic). A value that wins never contains a `=`:
                // the punctuation or the name would have matched at the same position.
                new() { Scope = "string", Begin = runStart + @"[^=&\s]+(?![^&\s])" },
            ],
        };
    }
}
