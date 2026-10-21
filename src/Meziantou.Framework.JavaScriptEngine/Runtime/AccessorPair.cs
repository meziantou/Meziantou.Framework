namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The getter and the setter of an accessor property (the value of its slot); they are <c>undefined</c> or functions. The
/// functions of a lazy accessor are created on first access.
/// </summary>
internal sealed class AccessorPair : JsCell
{
    private JsValue _getter;
    private JsValue _setter;
    private Func<JsValue>? _getterFactory;
    private Func<JsValue>? _setterFactory;

    public AccessorPair(JsValue getter, JsValue setter)
        : base(JsValueKind.Undefined)
    {
        _getter = getter;
        _setter = setter;
    }

    public AccessorPair(Func<JsValue> getterFactory, Func<JsValue>? setterFactory)
        : base(JsValueKind.Undefined)
    {
        _getterFactory = getterFactory;
        _setterFactory = setterFactory;
    }

    public JsValue Getter
    {
        get
        {
            if (_getterFactory is { } factory)
            {
                _getterFactory = null;
                _getter = factory();
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
                _setter = factory();
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
