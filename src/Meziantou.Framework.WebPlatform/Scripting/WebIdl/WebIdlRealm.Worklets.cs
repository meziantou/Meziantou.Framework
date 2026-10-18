using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// A JavaScript class registered in a worklet global scope (e.g. by <c>registerPaint()</c>): the properties of the
/// constructor and of its prototype are read in the realm of the worklet.
/// </summary>
internal sealed class JsWorkletClassConstructor<TAdapter, TValue>(WebIdlRealm<TAdapter, TValue> realm, TValue function) : WorkletClassConstructor
    where TAdapter : struct, IJsAdapter<TValue>
    where TValue : class
{
    public TValue Function { get; } = function;

    internal override bool IsConstructor => realm.Adapter.IsConstructor(Function);

    internal override IReadOnlyList<string>? GetStringSequence(string name)
    {
        var value = realm.Adapter.Get(Function, realm.Key(name));
        return realm.Adapter.IsUndefined(value) ? null : realm.ToSequence(value, static (r, v) => r.ToDomString(v));
    }

    internal override bool GetDictionaryBoolean(string name, string member, bool defaultValue)
    {
        // https://webidl.spec.whatwg.org/#js-dictionary
        var adapter = realm.Adapter;
        var value = adapter.Get(Function, realm.Key(name));
        if (adapter.IsUndefined(value) || adapter.IsNull(value))
            return defaultValue;

        if (!adapter.IsObject(value))
            throw realm.CreateTypeError($"'{name}' is not an object");

        var memberValue = adapter.Get(value, realm.Key(member));
        return adapter.IsUndefined(memberValue) ? defaultValue : adapter.ToBoolean(memberValue);
    }

    internal override string? GetDictionaryString(string name, string member)
    {
        // https://webidl.spec.whatwg.org/#js-dictionary
        var adapter = realm.Adapter;
        var value = adapter.Get(Function, realm.Key(name));
        if (adapter.IsUndefined(value) || adapter.IsNull(value))
            return null;

        if (!adapter.IsObject(value))
            throw realm.CreateTypeError($"'{name}' is not an object");

        var memberValue = adapter.Get(value, realm.Key(member));
        return adapter.IsUndefined(memberValue) ? null : realm.ToDomString(memberValue);
    }

    internal override bool HasPrototypeObject => realm.Adapter.IsObject(realm.Adapter.Get(Function, realm.Key("prototype")));

    internal override object GetPrototypeFunction(string name)
    {
        var adapter = realm.Adapter;
        var value = adapter.Get(adapter.Get(Function, realm.Key("prototype")), realm.Key(name));
        if (!adapter.IsCallable(value))
            throw realm.CreateTypeError($"'{name}' is not a function");

        return value;
    }

    internal override object Construct() => Construct([]);

    internal override object Construct(object?[] arguments)
    {
        realm.Agent.Host.OnCallbackInvoking();
        try
        {
            return realm.Run(() =>
            {
                var jsArguments = new TValue[arguments.Length];
                for (var i = 0; i < arguments.Length; i++)
                {
                    jsArguments[i] = realm.ToJs(arguments[i]);
                }

                return realm.Adapter.Construct(Function, jsArguments, Function);
            });
        }
        finally
        {
            realm.Agent.Host.OnCallbackInvoked(realm.GlobalScope);
        }
    }

    internal override object? Invoke(object function, object instance, object?[] arguments)
    {
        realm.Agent.Host.OnCallbackInvoking();
        try
        {
            return realm.Run(() =>
            {
                var jsArguments = new TValue[arguments.Length];
                for (var i = 0; i < arguments.Length; i++)
                {
                    jsArguments[i] = realm.ToJs(arguments[i]);
                }

                return realm.Adapter.Call((TValue)function, (TValue)instance, jsArguments);
            });
        }
        finally
        {
            realm.Agent.Host.OnCallbackInvoked(realm.GlobalScope);
        }
    }

    internal override WorkletPromise? ObservePromise(object? value)
    {
        if (value is not TValue jsValue || !realm.Adapter.IsObject(jsValue))
            return null;

        // Promise.prototype.then throws a TypeError when its this value is not a promise (https://tc39.es/ecma262/#sec-ispromise)
        var promise = new WorkletPromise();
        return realm.Run(() =>
        {
            var adapter = realm.Adapter;
            var observe = realm.EvaluateBuiltIn("(function () { const then = Promise.prototype.then; return function (value, onFulfilled, onRejected) { try { then.call(value, onFulfilled, onRejected); return true; } catch { return false; } }; })()");
            var onFulfilled = adapter.CreateFunction("", 1, (_, arguments) =>
            {
                promise.Settle(fulfilled: true, arguments.Length > 0 ? arguments[0] : adapter.Undefined);
                return adapter.Undefined;
            });
            var onRejected = adapter.CreateFunction("", 1, (_, arguments) =>
            {
                promise.Settle(fulfilled: false, arguments.Length > 0 ? arguments[0] : adapter.Undefined);
                return adapter.Undefined;
            });
            return adapter.ToBoolean(adapter.Call(observe, adapter.Undefined, [jsValue, onFulfilled, onRejected])) ? promise : null;
        });
    }

    internal override object? AsPlatformObject(object? value)
        => value is TValue jsValue && realm.Adapter.IsObject(jsValue) && realm.Adapter.TryGetPlatformObject(jsValue, out var target) ? target : null;

    internal override bool IsUndefined(object? value) => value is TValue jsValue && realm.Adapter.IsUndefined(jsValue);

    internal override T ConvertToDictionary<T>(object? value)
    {
        var jsValue = value as TValue ?? realm.Adapter.Undefined;
        return realm.Run(() =>
        {
            if (typeof(T) == typeof(IntrinsicSizesResultOptions))
            {
                // The members are converted in lexicographical order (https://webidl.spec.whatwg.org/#js-dictionary)
                realm.CheckDictionary(jsValue, "IntrinsicSizesResultOptions");
                var maxContentSize = realm.GetDictionaryMember(jsValue, "maxContentSize");
                var minContentSize = realm.GetDictionaryMember(jsValue, "minContentSize");
                return (T)(object)new IntrinsicSizesResultOptions
                {
                    MaxContentSize = realm.Adapter.IsUndefined(maxContentSize) ? null : realm.ToDouble(maxContentSize, restricted: true),
                    MinContentSize = realm.Adapter.IsUndefined(minContentSize) ? null : realm.ToDouble(minContentSize, restricted: true),
                };
            }

            if (typeof(T) == typeof(FragmentResultOptions))
                return (T)(object)Generated.WebIdlHelpers<TAdapter, TValue>.ToDictionary_Dom_FragmentResultOptions(realm, jsValue);

            throw new NotSupportedException($"The dictionary '{typeof(T)}' is not supported");
        });
    }

    internal override void ReportRejection(object? reason)
        => realm.Agent.Host.ReportException(realm.GlobalScope, realm.Adapter.CreateException(reason as TValue ?? realm.Adapter.Undefined));

    internal override void ReportException(Exception exception) => realm.Agent.Host.ReportException(realm.GlobalScope, exception);
}

// The classes of the worklets (https://html.spec.whatwg.org/multipage/worklets.html)
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>Returns the .NET object of a class registered in a worklet global scope (a callable value).</summary>
    internal WorkletClassConstructor ToWorkletClassConstructor(TValue value)
    {
        if (!Adapter.IsCallable(value))
            throw CreateTypeError("The value is not a function");

        return new JsWorkletClassConstructor<TAdapter, TValue>(Agent.GetAssociatedRealm(value) ?? this, value);
    }
}
