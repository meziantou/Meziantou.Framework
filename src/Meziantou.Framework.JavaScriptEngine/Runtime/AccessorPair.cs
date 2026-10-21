namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The getter and the setter of an accessor property (the value of its slot); they are <c>undefined</c> or functions. The
/// functions of a lazy accessor (the derived classes) are created on first access.
/// </summary>
internal class AccessorPair : JsCell
{
    public AccessorPair(JsValue getter, JsValue setter)
        : base(JsValueKind.Undefined)
    {
        GetterValue = getter;
        SetterValue = setter;
    }

    private protected AccessorPair()
        : base(JsValueKind.Undefined)
    {
    }

    public virtual JsValue Getter => GetterValue;

    public virtual JsValue Setter => SetterValue;

    private protected JsValue GetterValue { get; set; }

    private protected JsValue SetterValue { get; set; }
}

/// <summary>An accessor whose functions are created by factories, which get their state (an accessor defined by a host).</summary>
internal sealed class LazyAccessorPair : AccessorPair
{
    private Func<object?, JsValue>? _getterFactory;
    private Func<object?, JsValue>? _setterFactory;
    private object? _getterState;
    private object? _setterState;

    public LazyAccessorPair(Func<JsValue> getterFactory, Func<JsValue>? setterFactory)
        : this(LazyValue.Invoke, getterFactory, setterFactory is null ? null : LazyValue.Invoke, setterFactory)
    {
    }

    public LazyAccessorPair(Func<object?, JsValue> getterFactory, object? getterState, Func<object?, JsValue>? setterFactory, object? setterState)
    {
        _getterFactory = getterFactory;
        _getterState = getterState;
        _setterFactory = setterFactory;
        _setterState = setterState;
    }

    public override JsValue Getter
    {
        get
        {
            if (_getterFactory is { } factory)
            {
                _getterFactory = null;
                GetterValue = factory(_getterState);
                _getterState = null;
            }

            return GetterValue;
        }
    }

    public override JsValue Setter
    {
        get
        {
            if (_setterFactory is { } factory)
            {
                _setterFactory = null;
                SetterValue = factory(_setterState);
                _setterState = null;
            }

            return SetterValue;
        }
    }
}

/// <summary>
/// A built-in accessor (<see cref="JsRealm.DefineAccessor"/>) whose functions ("get name" and "set name") are created on
/// first access: one object instead of the pair and its functions.
/// </summary>
internal sealed class LazyBuiltinAccessorPair(JsRealm realm, in PropertyKey name, BuiltinFunctionCallback? getter, BuiltinFunctionCallback? setter) : AccessorPair
{
    private readonly object? _nameValue = name.RawValue;
    private readonly uint _nameIndex = name.RawIndex;
    private BuiltinFunctionCallback? _getterCallback = getter;
    private BuiltinFunctionCallback? _setterCallback = setter;

    public override JsValue Getter
    {
        get
        {
            if (_getterCallback is { } callback)
            {
                _getterCallback = null;
                GetterValue = realm.CreateBuiltinFunction(PropertyKey.FromRaw(_nameValue, _nameIndex), 0, callback, prefix: "get");
            }

            return GetterValue;
        }
    }

    public override JsValue Setter
    {
        get
        {
            if (_setterCallback is { } callback)
            {
                _setterCallback = null;
                SetterValue = realm.CreateBuiltinFunction(PropertyKey.FromRaw(_nameValue, _nameIndex), 1, callback, prefix: "set");
            }

            return SetterValue;
        }
    }
}
