using System.Diagnostics;
using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>A scope of the code (compile time): the bindings it declares, and where they are stored.</summary>
[DebuggerDisplay("{Kind} ({OrderedVariables.Count} variables)")]
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

    // The scopes have few bindings: they are searched linearly (the names are interned by the parser), and indexed by name
    // when there are many of them
    private const int IndexThreshold = 8;
    private Dictionary<string, Variable>? _index;

    /// <summary>Gets the declared bindings, in declaration order (a redeclared name hides the previous binding).</summary>
    public List<Variable> OrderedVariables { get; } = [];

    /// <summary>Gets whether this scope is the variable environment of its function (var declarations, sloppy eval vars).</summary>
    public bool IsVarScope => Kind is ScopeKind.Script or ScopeKind.Module or ScopeKind.Eval or ScopeKind.FunctionBody || (Kind == ScopeKind.Function && Function.BodyVarScope is null);

    /// <summary>Gets or sets whether a direct eval call in this scope (or a nested block of the same function) can see the bindings by name.</summary>
    public bool ContainsDirectEval { get; set; }

    /// <summary>Gets or sets whether sloppy direct eval code can add var bindings to this (var) scope.</summary>
    public bool HasSloppyEvalVars { get; set; }

    /// <summary>
    /// Gets or sets whether sloppy direct eval code of the parameters adds var bindings to this parameter scope (of a function
    /// whose parameters have expressions). The spec puts them in an environment outside the parameters: they cannot redeclare
    /// a parameter.
    /// </summary>
    public bool HasParameterEvalVars { get; set; }

    /// <summary>Gets or sets whether the scope has an environment at runtime.</summary>
    public bool NeedsContext { get; set; }

    /// <summary>Gets or sets the runtime description of the environment of the scope.</summary>
    public ScopeInfo? Info { get; set; }

    public int ContextSlotCount { get; set; }

    /// <summary>Gets or sets the snapshot this scope was created from (a scope that encloses a lazily compiled function): its bindings are allocated.</summary>
    public ScopeSnapshot? Snapshot { get; set; }

    public Variable Declare(string name, VariableKind kind)
    {
        var variable = new Variable(name, kind, this);
        OrderedVariables.Add(variable);
        if (_index is not null)
        {
            _index[name] = variable;
        }
        else if (OrderedVariables.Count > IndexThreshold)
        {
            _index = new Dictionary<string, Variable>(OrderedVariables.Count * 2, StringComparer.Ordinal);
            foreach (var declared in OrderedVariables)
            {
                _index[declared.Name] = declared;
            }
        }

        return variable;
    }

    /// <summary>Gets the binding of a name (the last declared one), or null.</summary>
    public Variable? Find(string name)
    {
        if (_index is not null)
            return _index.GetValueOrDefault(name);

        var variables = OrderedVariables;
        for (var i = variables.Count - 1; i >= 0; i--)
        {
            if (string.Equals(variables[i].Name, name, StringComparison.Ordinal))
                return variables[i];
        }

        return null;
    }
}
