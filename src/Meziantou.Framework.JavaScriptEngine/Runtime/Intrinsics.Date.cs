namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%Date.prototype% (an ordinary object, not a Date)</summary>
    public JsObject DatePrototype { get; private set; } = null!;

    /// <summary>%Date%</summary>
    public JsFunction Date { get; private set; } = null!;

    partial void InitializeDate()
    {
        DatePrototype = new JsObject(ObjectPrototype);
        Date = DateBuiltins.Initialize(this);
    }
}
