using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Diff
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
                    Scope = "meta",
                    // Deviation from highlight.js: the line count of a unified hunk range is optional, as git omits
                    // it when it is 1 (`@@ -1 +1 @@`).
                    Match = @"(?:^@@ +-\d+(?:,\d+)? +\+\d+(?:,\d+)? +@@|^\*\*\* +\d+,\d+ +\*\*\*\*$|^--- +\d+,\d+ +----$)",
                },
                new Mode
                {
                    Scope = "comment",
                    Variants =
                    [
                        new Mode
                        {
                            Begin = @"(?:Index: |^index|={3,}|^-{3}|^\*{3} |^\+{3}|^diff --git)",
                            End = "$",
                        },
                        new Mode { Match = @"^\*{15}$" },
                    ],
                },
                new Mode
                {
                    Scope = "addition",
                    Begin = @"^\+",
                    End = "$",
                },
                new Mode
                {
                    Scope = "deletion",
                    Begin = "^-",
                    End = "$",
                },
                new Mode
                {
                    Scope = "addition",
                    Begin = "^!",
                    End = "$",
                },
            ],
        };
    }
}
