using System.Diagnostics;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

[DebuggerDisplay("{Kind} {Name} ({Location} {Index})")]
internal sealed class Variable(string name, VariableKind kind, Scope scope)
{
    public string Name { get; } = name;

    public VariableKind Kind { get; set; } = kind;

    public Scope Scope { get; } = scope;

    /// <summary>Gets or sets whether the variable is referenced from another function (or by name at runtime): it is stored in the environment.</summary>
    public bool IsCaptured { get; set; }

    public VariableLocation Location { get; set; }

    /// <summary>Gets or sets the register or the slot of the environment.</summary>
    public int Index { get; set; } = -1;

    /// <summary>Gets or sets the offset of the end of the declaration: a reference after it in the same scope does not need a TDZ check.</summary>
    public int DeclarationEnd { get; set; } = int.MaxValue;

    /// <summary>Gets whether the binding has a temporal dead zone (let, const, class, the class name, imports...).</summary>
    public bool HasTdz => IsParameterWithTdz || Kind is VariableKind.Let or VariableKind.Const or VariableKind.Class or VariableKind.ClassName or VariableKind.Import or VariableKind.Hidden;

    /// <summary>Gets or sets whether the variable is a parameter of a function whose parameters have expressions: it is uninitialized until its parameter is evaluated.</summary>
    public bool IsParameterWithTdz { get; set; }

    /// <summary>Gets or sets whether a reference to the binding was resolved (e.g. the arguments object is only created when it is used).</summary>
    public bool IsReferenced { get; set; }

    /// <summary>Gets whether an assignment to the binding is an error (const, class name, imports) or ignored (the name of a function expression in sloppy code).</summary>
    public bool IsImmutable => Kind is VariableKind.Const or VariableKind.ClassName or VariableKind.FunctionName or VariableKind.Import;

    /// <summary>Gets or sets whether a const binding is a 'using' declaration (disposed at the end of the block).</summary>
    public bool IsUsing { get; set; }
}
