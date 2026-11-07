namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>How this, new.target, the home object or the active function are read.</summary>
internal enum SpecialReferenceKind
{
    /// <summary>From the frame of the function (the function's own binding).</summary>
    Frame,

    /// <summary>From a binding of the environment of an enclosing function (arrow functions, eval).</summary>
    Variable,

    /// <summary>The this of the global code of a script (the global this value of the realm).</summary>
    GlobalThis,

    /// <summary>undefined (module code, or new.target outside a function).</summary>
    Undefined,

    /// <summary>Resolved by name at runtime (the code of a direct eval).</summary>
    Dynamic,
}

internal sealed class SpecialReference(SpecialReferenceKind kind, Variable? variable, string name)
{
    public SpecialReferenceKind Kind { get; } = kind;

    public Variable? Variable { get; } = variable;

    /// <summary>Gets the name of the binding in the environment ("this", "new.target", "%home", "%func").</summary>
    public string Name { get; } = name;
}
