namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The getter and the setter of an accessor property (the value of its slot); they are <c>undefined</c> or functions.</summary>
internal sealed class AccessorPair(JsValue getter, JsValue setter) : JsCell(JsValueKind.Undefined)
{
    public JsValue Getter { get; set; } = getter;

    public JsValue Setter { get; set; } = setter;
}
