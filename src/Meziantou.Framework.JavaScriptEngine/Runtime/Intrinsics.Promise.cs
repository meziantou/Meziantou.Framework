namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Promise built-in is created on first use (a global property, an async function, or the host).</summary>
internal sealed partial class Intrinsics
{
    private JsFunction? _promise;
    private JsObject? _promisePrototype;
    private JsFunction? _promisePrototypeThen;
    private bool _isPromiseInitializing;

    /// <summary>%Promise%</summary>
    public JsFunction Promise => EnsurePromise()._promise!;

    /// <summary>%Promise.prototype%</summary>
    public JsObject PromisePrototype => EnsurePromise()._promisePrototype!;

    /// <summary>%Promise.prototype.then%</summary>
    public JsFunction PromisePrototypeThen => EnsurePromise()._promisePrototypeThen!;

    partial void InitializePromise()
    {
        AddLazyGlobal("Promise", static intrinsics => intrinsics.Promise);
    }

    /// <summary>Creates the Promise built-in, if it is not created yet.</summary>
    private Intrinsics EnsurePromise()
    {
        if (_promise is null && !_isPromiseInitializing)
        {
            _isPromiseInitializing = true;
            _promisePrototype = new JsObject(ObjectPrototype);
            _promise = PromiseBuiltins.Initialize(this, _promisePrototype, out var then);
            _promisePrototypeThen = then;
        }

        return this;
    }
}
