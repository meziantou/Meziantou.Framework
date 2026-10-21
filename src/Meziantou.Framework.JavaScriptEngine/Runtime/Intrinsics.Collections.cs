namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%Map%</summary>
    public JsFunction Map { get; private set; } = null!;

    /// <summary>%Map.prototype%</summary>
    public JsObject MapPrototype { get; private set; } = null!;

    /// <summary>%MapIteratorPrototype%</summary>
    public JsObject MapIteratorPrototype { get; private set; } = null!;

    /// <summary>%Set%</summary>
    public JsFunction Set { get; private set; } = null!;

    /// <summary>%Set.prototype%</summary>
    public JsObject SetPrototype { get; private set; } = null!;

    /// <summary>%SetIteratorPrototype%</summary>
    public JsObject SetIteratorPrototype { get; private set; } = null!;

    /// <summary>%WeakMap%</summary>
    public JsFunction WeakMap { get; private set; } = null!;

    /// <summary>%WeakMap.prototype%</summary>
    public JsObject WeakMapPrototype { get; private set; } = null!;

    /// <summary>%WeakSet%</summary>
    public JsFunction WeakSet { get; private set; } = null!;

    /// <summary>%WeakSet.prototype%</summary>
    public JsObject WeakSetPrototype { get; private set; } = null!;

    partial void InitializeCollections()
    {
        MapPrototype = new JsObject(ObjectPrototype);
        MapIteratorPrototype = new JsObject(IteratorPrototype);
        Map = MapBuiltins.Initialize(this, MapPrototype, MapIteratorPrototype);

        SetPrototype = new JsObject(ObjectPrototype);
        SetIteratorPrototype = new JsObject(IteratorPrototype);
        Set = SetBuiltins.Initialize(this, SetPrototype, SetIteratorPrototype);

        WeakMapPrototype = new JsObject(ObjectPrototype);
        WeakMap = WeakMapBuiltins.Initialize(this, WeakMapPrototype);

        WeakSetPrototype = new JsObject(ObjectPrototype);
        WeakSet = WeakSetBuiltins.Initialize(this, WeakSetPrototype);
    }
}
