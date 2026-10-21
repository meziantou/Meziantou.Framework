namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The getter and the setter of an accessor property (the value of its slot); they are <c>undefined</c> or functions. The
/// functions of a lazy accessor are created on first access.
/// </summary>
internal sealed class AccessorPair : JsCell
{
    private JsValue _getter;
    private JsValue _setter;
    private Func<object?, JsValue>? _getterFactory;
    private Func<object?, JsValue>? _setterFactory;
    private object? _getterState;
    private object? _setterState;

    public AccessorPair(JsValue getter, JsValue setter)
        : base(JsValueKind.Undefined)
    {
        _getter = getter;
        _setter = setter;
    }

    public AccessorPair(Func<JsValue> getterFactory, Func<JsValue>? setterFactory)
        : this(LazyValue.Invoke, getterFactory, setterFactory is null ? null : LazyValue.Invoke, setterFactory)
    {
    }

    public AccessorPair(Func<object?, JsValue> getterFactory, object? getterState, Func<object?, JsValue>? setterFactory, object? setterState)
        : base(JsValueKind.Undefined)
    {
        _getterFactory = getterFactory;
        _getterState = getterState;
        _setterFactory = setterFactory;
        _setterState = setterState;
    }

    public JsValue Getter
    {
        get
        {
            if (_getterFactory is { } factory)
            {
                _getterFactory = null;
                _getter = factory(_getterState);
                _getterState = null;
            }

            return _getter;
        }
        set
        {
            _getterFactory = null;
            _getter = value;
        }
    }

    public JsValue Setter
    {
        get
        {
            if (_setterFactory is { } factory)
            {
                _setterFactory = null;
                _setter = factory(_setterState);
                _setterState = null;
            }

            return _setter;
        }
        set
        {
            _setterFactory = null;
            _setter = value;
        }
    }
}
