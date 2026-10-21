namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%WeakRef%</summary>
    public JsFunction WeakRef { get; private set; } = null!;

    /// <summary>%WeakRef.prototype%</summary>
    public JsObject WeakRefPrototype { get; private set; } = null!;

    /// <summary>%FinalizationRegistry%</summary>
    public JsFunction FinalizationRegistry { get; private set; } = null!;

    /// <summary>%FinalizationRegistry.prototype%</summary>
    public JsObject FinalizationRegistryPrototype { get; private set; } = null!;

    partial void InitializeWeakRefs()
    {
        WeakRefPrototype = new JsObject(ObjectPrototype);
        WeakRef = WeakRefBuiltins.Initialize(this, WeakRefPrototype);

        FinalizationRegistryPrototype = new JsObject(ObjectPrototype);
        FinalizationRegistry = FinalizationRegistryBuiltins.Initialize(this, FinalizationRegistryPrototype);
    }
}
