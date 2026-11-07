namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>The resolution of an identifier reference (or of this, new.target, super, a private name).</summary>
internal sealed class Reference(string name, Variable? variable, bool isDynamic, bool needsTdzCheck)
{
    public string Name { get; } = name;

    /// <summary>Gets the binding, or null for a global or a dynamic reference.</summary>
    public Variable? Variable { get; } = variable;

    /// <summary>Gets whether the reference is resolved by name at runtime (it crosses a with statement or the environment of sloppy eval code).</summary>
    public bool IsDynamic { get; } = isDynamic;

    public bool NeedsTdzCheck { get; } = needsTdzCheck;

    /// <summary>Gets whether the reference is to a global binding (a property of the global object or a global lexical binding).</summary>
    public bool IsGlobal => Variable is null && !IsDynamic;
}
