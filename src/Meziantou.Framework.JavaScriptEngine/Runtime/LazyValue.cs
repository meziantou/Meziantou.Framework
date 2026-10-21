namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The value of a data property computed the first time the property is read (e.g. the built-in objects of a realm). The
/// factory gets a state, so that a host does not allocate a closure per property.
/// </summary>
internal sealed class LazyValue(Func<object?, JsValue> factory, object? state) : JsCell(JsValueKind.Undefined)
{
    public LazyValue(Func<JsValue> factory)
        : this(Invoke, factory)
    {
    }

    /// <summary>Gets the callback that invokes a <see cref="Func{JsValue}"/> given as the state.</summary>
    public static Func<object?, JsValue> Invoke { get; } = static state => ((Func<JsValue>)state!)();

    public JsValue Evaluate() => factory(state);
}
