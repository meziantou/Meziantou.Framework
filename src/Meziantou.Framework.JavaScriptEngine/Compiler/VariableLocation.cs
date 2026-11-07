namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal enum VariableLocation
{
    Unallocated,

    /// <summary>A register of the frame (not captured).</summary>
    Register,

    /// <summary>A slot of the declarative environment of the scope (captured by a closure, or visible to eval and with).</summary>
    Context,

    /// <summary>A property of the global object (var and function declarations of a script).</summary>
    GlobalObject,

    /// <summary>A binding of the declarative record of the global environment (let, const and class of a script).</summary>
    GlobalLexical,

    /// <summary>Resolved at runtime by name (eval code).</summary>
    Dynamic,
}
