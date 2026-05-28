using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class UrlEncoded
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        return new Mode
        {
            Contains =
            [
                new() { Scope = "attr", Begin = "[^=&\\s]+(?==)" },
                new() { Scope = "punctuation", Begin = "[=&]" },
                new() { Scope = "string", Begin = "[^&\\s]+" },
            ],
        };
    }
}
