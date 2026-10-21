namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The value of a data property computed the first time the property is read (e.g. the built-in objects of a realm).</summary>
internal sealed class LazyValue(Func<JsValue> factory) : JsCell(JsValueKind.Undefined)
{
    public JsValue Evaluate() => factory();
}
