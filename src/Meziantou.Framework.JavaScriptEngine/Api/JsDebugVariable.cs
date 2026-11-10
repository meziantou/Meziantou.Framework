namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A variable of a <see cref="JsDebugScope"/>.</summary>
public readonly struct JsDebugVariable
{
    internal JsDebugVariable(string name, JsValue value, bool isInitialized)
    {
        Name = name;
        Value = value;
        IsInitialized = isInitialized;
    }

    /// <summary>Gets the name of the variable.</summary>
    public string Name { get; }

    /// <summary>Gets the value of the variable (undefined when it is not initialized).</summary>
    public JsValue Value { get; }

    /// <summary>
    /// Gets whether the variable is initialized. A <c>let</c>, <c>const</c> or <c>class</c> declaration is not initialized
    /// before it runs (its temporal dead zone): reading it throws a <c>ReferenceError</c>.
    /// </summary>
    public bool IsInitialized { get; }

    /// <inheritdoc/>
    public override string ToString() => IsInitialized ? Name + " = " + Value.ToString() : Name + " (uninitialized)";
}
