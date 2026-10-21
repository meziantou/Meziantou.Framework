using System.Diagnostics;
using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>A scope of the code (compile time): the bindings it declares, and where they are stored.</summary>
[DebuggerDisplay("{Kind} ({Variables.Count} variables)")]
internal sealed class Scope
{
    public Scope(ScopeKind kind, Scope? parent, FunctionInfo function, Node? node)
    {
        Kind = kind;
        Parent = parent;
        Function = function;
        Node = node;
    }

    public ScopeKind Kind { get; }

    public Scope? Parent { get; }

    /// <summary>Gets the function (or the script, module, eval code, class initializer) whose code contains the scope.</summary>
    public FunctionInfo Function { get; }

    public Node? Node { get; }

    /// <summary>Gets the declared bindings, in declaration order.</summary>
    public Dictionary<string, Variable> Variables { get; } = new(StringComparer.Ordinal);

    public List<Variable> OrderedVariables { get; } = [];

    /// <summary>Gets whether this scope is the variable environment of its function (var declarations, sloppy eval vars).</summary>
    public bool IsVarScope => Kind is ScopeKind.Script or ScopeKind.Module or ScopeKind.Eval or ScopeKind.FunctionBody || (Kind == ScopeKind.Function && Function.BodyVarScope is null);

    /// <summary>Gets or sets whether a direct eval call in this scope (or a nested block of the same function) can see the bindings by name.</summary>
    public bool ContainsDirectEval { get; set; }

    /// <summary>Gets or sets whether sloppy direct eval code can add var bindings to this (var) scope.</summary>
    public bool HasSloppyEvalVars { get; set; }

    /// <summary>Gets or sets whether the scope has an environment at runtime.</summary>
    public bool NeedsContext { get; set; }

    /// <summary>Gets or sets the runtime description of the environment of the scope.</summary>
    public ScopeInfo? Info { get; set; }

    public int ContextSlotCount { get; set; }

    public Variable Declare(string name, VariableKind kind)
    {
        var variable = new Variable(name, kind, this);
        Variables[name] = variable;
        OrderedVariables.Add(variable);
        return variable;
    }

    public Variable? Find(string name) => Variables.GetValueOrDefault(name);
}
