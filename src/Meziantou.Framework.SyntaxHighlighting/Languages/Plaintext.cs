using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Plaintext
{
    public static CompiledMode Instance { get; } = Compiler.Compile(new Mode());
}
