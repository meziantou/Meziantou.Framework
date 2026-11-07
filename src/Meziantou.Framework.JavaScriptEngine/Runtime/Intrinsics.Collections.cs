namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Map, Set, WeakMap and WeakSet built-ins are created on first use (a global property, or an intrinsic used by another built-in or by the host).</summary>
internal sealed partial class Intrinsics
{
    private JsFunction? _map;
    private JsObject? _mapPrototype;
    private JsObject? _mapIteratorPrototype;
    private JsFunction? _set;
    private JsObject? _setPrototype;
    private JsObject? _setIteratorPrototype;
    private JsFunction? _weakMap;
    private JsObject? _weakMapPrototype;
    private JsFunction? _weakSet;
    private JsObject? _weakSetPrototype;
    private bool _isCollectionsInitializing;

    /// <summary>%Map%</summary>
    public JsFunction Map => EnsureCollections()._map!;

    /// <summary>%Map.prototype%</summary>
    public JsObject MapPrototype => EnsureCollections()._mapPrototype!;

    /// <summary>%MapIteratorPrototype%</summary>
    public JsObject MapIteratorPrototype => EnsureCollections()._mapIteratorPrototype!;

    /// <summary>%Set%</summary>
    public JsFunction Set => EnsureCollections()._set!;

    /// <summary>%Set.prototype%</summary>
    public JsObject SetPrototype => EnsureCollections()._setPrototype!;

    /// <summary>%SetIteratorPrototype%</summary>
    public JsObject SetIteratorPrototype => EnsureCollections()._setIteratorPrototype!;

    /// <summary>%WeakMap%</summary>
    public JsFunction WeakMap => EnsureCollections()._weakMap!;

    /// <summary>%WeakMap.prototype%</summary>
    public JsObject WeakMapPrototype => EnsureCollections()._weakMapPrototype!;

    /// <summary>%WeakSet%</summary>
    public JsFunction WeakSet => EnsureCollections()._weakSet!;

    /// <summary>%WeakSet.prototype%</summary>
    public JsObject WeakSetPrototype => EnsureCollections()._weakSetPrototype!;

    partial void InitializeCollections()
    {
        AddLazyGlobal("Map", static intrinsics => intrinsics.Map);
        AddLazyGlobal("Set", static intrinsics => intrinsics.Set);
        AddLazyGlobal("WeakMap", static intrinsics => intrinsics.WeakMap);
        AddLazyGlobal("WeakSet", static intrinsics => intrinsics.WeakSet);
    }

    /// <summary>Creates the collection built-ins, if they are not created yet.</summary>
    private Intrinsics EnsureCollections()
    {
        if (_map is null && !_isCollectionsInitializing)
        {
            _isCollectionsInitializing = true;
            _mapPrototype = new JsObject(ObjectPrototype);
            _mapIteratorPrototype = new JsObject(IteratorPrototype);
            _map = MapBuiltins.Initialize(this, _mapPrototype, _mapIteratorPrototype);
            _setPrototype = new JsObject(ObjectPrototype);
            _setIteratorPrototype = new JsObject(IteratorPrototype);
            _set = SetBuiltins.Initialize(this, _setPrototype, _setIteratorPrototype);
            _weakMapPrototype = new JsObject(ObjectPrototype);
            _weakMap = WeakMapBuiltins.Initialize(this, _weakMapPrototype);
            _weakSetPrototype = new JsObject(ObjectPrototype);
            _weakSet = WeakSetBuiltins.Initialize(this, _weakSetPrototype);
        }

        return this;
    }
}
