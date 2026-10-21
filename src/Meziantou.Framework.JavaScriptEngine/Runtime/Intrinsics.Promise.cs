namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%Promise%</summary>
    public JsFunction Promise { get; private set; } = null!;

    /// <summary>%Promise.prototype%</summary>
    public JsObject PromisePrototype { get; private set; } = null!;

    /// <summary>%Promise.prototype.then%</summary>
    public JsFunction PromisePrototypeThen { get; private set; } = null!;

    partial void InitializePromise()
    {
        PromisePrototype = new JsObject(ObjectPrototype);
        Promise = PromiseBuiltins.Initialize(this, PromisePrototype, out var then);
        PromisePrototypeThen = then;
    }
}
