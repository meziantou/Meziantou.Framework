namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A scope of a frame paused by a debugger (<see cref="JsDebugFrame.GetScopes"/>): its variables, or its binding object.</summary>
public sealed class JsDebugScope
{
    internal JsDebugScope(JsDebugScopeKind kind, JsDebugVariable[] variables, JsObject? bindingObject)
    {
        Kind = kind;
        Variables = variables;
        BindingObject = bindingObject;
    }

    /// <summary>Gets the kind of the scope.</summary>
    public JsDebugScopeKind Kind { get; }

    /// <summary>
    /// Gets the variables of the scope, in the order of their declarations. The variables of the <see cref="JsDebugScopeKind.Global"/>
    /// and <see cref="JsDebugScopeKind.With"/> scopes are the properties of their <see cref="BindingObject"/>.
    /// </summary>
    public IReadOnlyList<JsDebugVariable> Variables { get; }

    /// <summary>Gets the object whose properties are the variables of the scope: the global object, or the object of a <c>with</c> statement. Null for the other scopes.</summary>
    public JsObject? BindingObject { get; }

    /// <summary>Gets the variable of the scope that has a name.</summary>
    /// <param name="name">The name of the variable.</param>
    /// <param name="variable">The variable, when it is found.</param>
    /// <returns>True when the scope has a variable of this name in <see cref="Variables"/>.</returns>
    public bool TryGetVariable(string name, out JsDebugVariable variable)
    {
        foreach (var item in Variables)
        {
            if (string.Equals(item.Name, name, StringComparison.Ordinal))
            {
                variable = item;
                return true;
            }
        }

        variable = default;
        return false;
    }
}
