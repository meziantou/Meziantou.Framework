using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class NodeRepl
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        return new Mode
        {
            // Same deviations from highlight.js as the Python REPL: a `>` statement starts from the root of the
            // JavaScript grammar, and `...` is a continuation prompt only on the line after a prompt line.
            Contains = ReplModes.CreatePrompts(">", @"\.\.\.", "javascript"),
        };
    }
}
