using Meziantou.Framework.JavaScriptEngine.Parsing;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>
/// A function whose body is compiled on its first call (<see cref="FunctionTemplate.EnsureCompiled"/>): its source text
/// is parsed again and compiled in the scopes that enclose it, as the compilation of its enclosing code left them. It does
/// not reference any syntax tree.
/// </summary>
internal sealed class LazyFunction(SourceText source, LazyFunctionSyntax syntax, FunctionInfoKind kind, string name, FunctionTemplateFlags extraFlags, ScopeSnapshot scope)
{
    public SourceText Source { get; } = source;

    public LazyFunctionSyntax Syntax { get; } = syntax;

    public FunctionInfoKind Kind { get; } = kind;

    /// <summary>Gets the name of the function given by its enclosing code (e.g. "get x", "default").</summary>
    public string Name { get; } = name;

    public FunctionTemplateFlags ExtraFlags { get; } = extraFlags;

    /// <summary>Gets the scope that encloses the function (the scope of its name, for a named function expression).</summary>
    public ScopeSnapshot Scope { get; } = scope;

    /// <summary>Gets the lock of the compilation: a function is compiled once, even when several threads call it.</summary>
    public Lock Lock { get; } = new();
}

/// <summary>
/// An immutable copy of a scope that encloses a lazily compiled function: what the compilation of the function needs to
/// resolve its references (the bindings, where they are stored, the environments between the function and them).
/// </summary>
internal sealed class ScopeSnapshot(ScopeKind kind, ScopeSnapshot? parent, FunctionSnapshot function, bool needsContext, bool hasSloppyEvalVars, bool hasParameterEvalVars, VariableSnapshot[] variables)
{
    public ScopeKind Kind { get; } = kind;

    public ScopeSnapshot? Parent { get; } = parent;

    public FunctionSnapshot Function { get; } = function;

    /// <summary>Gets whether the scope has an environment at runtime.</summary>
    public bool NeedsContext { get; } = needsContext;

    /// <summary>Gets whether sloppy direct eval code can add var bindings to this scope (a variable environment).</summary>
    public bool HasSloppyEvalVars { get; } = hasSloppyEvalVars;

    /// <summary>Gets whether sloppy direct eval code of the parameters adds var bindings to this parameter scope.</summary>
    public bool HasParameterEvalVars { get; } = hasParameterEvalVars;

    // The bindings are searched linearly when there are few of them, as in Scope
    private const int IndexThreshold = 8;

    // Created on first use, and shared by the threads (read-only once created)
    private Dictionary<string, int>? _index;

    /// <summary>Gets the bindings, in declaration order.</summary>
    public VariableSnapshot[] Variables { get; } = variables;

    /// <summary>Gets the indexes in <see cref="Variables"/> of the special bindings (this, new.target, super, the active function).</summary>
    public int[] SpecialVariables { get; } = GetSpecialVariables(variables);

    /// <summary>Gets the index in <see cref="Variables"/> of the binding of a name (the last declared one), or -1.</summary>
    public int FindVariable(string name)
    {
        if (Variables.Length <= IndexThreshold)
        {
            for (var i = Variables.Length - 1; i >= 0; i--)
            {
                if (string.Equals(Variables[i].Name, name, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        var index = Volatile.Read(ref _index) ?? CreateIndex();
        return index.GetValueOrDefault(name, -1);
    }

    private Dictionary<string, int> CreateIndex()
    {
        var index = new Dictionary<string, int>(Variables.Length, StringComparer.Ordinal);
        for (var i = 0; i < Variables.Length; i++)
        {
            index[Variables[i].Name] = i;
        }

        return Interlocked.CompareExchange(ref _index, index, comparand: null) ?? index;
    }

    private static int[] GetSpecialVariables(VariableSnapshot[] variables)
    {
        List<int>? result = null;
        for (var i = 0; i < variables.Length; i++)
        {
            if (variables[i].Kind is VariableKind.This or VariableKind.NewTarget or VariableKind.HomeObject or VariableKind.ActiveFunction)
            {
                (result ??= []).Add(i);
            }
        }

        return result is null ? [] : [.. result];
    }

    /// <summary>Gets whether the outermost scope is the scope of a module (the source text is parsed as a module).</summary>
    public bool IsModule
    {
        get
        {
            var scope = this;
            while (scope.Parent is not null)
            {
                scope = scope.Parent;
            }

            return scope.Kind == ScopeKind.Module;
        }
    }
}

/// <summary>A binding of a <see cref="ScopeSnapshot"/>.</summary>
internal readonly record struct VariableSnapshot(string Name, VariableKind Kind, VariableLocation Location, int Index, bool IsParameterWithTdz);

/// <summary>The function (or script, module, eval code, class initializer) of a <see cref="ScopeSnapshot"/>.</summary>
internal sealed class FunctionSnapshot(FunctionInfoKind kind, bool isStrict, FunctionSnapshot? parent)
{
    public FunctionInfoKind Kind { get; } = kind;

    public bool IsStrict { get; } = isStrict;

    public FunctionSnapshot? Parent { get; } = parent;
}
