using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>
/// The stack check of the recursive passes of the compiler (the scope analysis, the code generator). The parser limits the
/// nesting of the code, but a pass can need more stack per level than the parser: a deeply nested program is a syntax error
/// instead of a stack overflow (which would terminate the process).
/// </summary>
internal static class CompilerStack
{
    public static void EnsureSufficientStack()
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            ThrowNestedTooDeeply();
    }

    /// <summary>The start of the message of the syntax errors of the code nested too deeply (the parser uses it too).</summary>
    public const string NestedTooDeeplyMessage = "Maximum call stack size exceeded: the code is nested too deeply";

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNestedTooDeeply() => throw new JsSyntaxErrorException(NestedTooDeeplyMessage);
}
