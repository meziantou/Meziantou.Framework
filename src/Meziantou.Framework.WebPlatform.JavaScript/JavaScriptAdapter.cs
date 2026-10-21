using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Scripting;
using static Meziantou.Framework.WebPlatform.JavaScript.JavaScriptValues;
using EngineBufferKind = Meziantou.Framework.JavaScriptEngine.JsBufferKind;
using EngineInternalSlot = Meziantou.Framework.JavaScriptEngine.JsInternalSlot;
using WebIdlBufferKind = Meziantou.Framework.WebPlatform.Scripting.JsBufferKind;
using WebIdlInternalSlot = Meziantou.Framework.WebPlatform.Scripting.JsInternalSlot;

namespace Meziantou.Framework.WebPlatform.JavaScript;

/// <summary>The state shared by the copies of a <see cref="JavaScriptAdapter"/>: the engine and its host.</summary>
internal sealed class JavaScriptAdapterState(JsEngine engine, JavaScriptHost host)
{
    public JsEngine Engine { get; } = engine;

    public JavaScriptHost Host { get; } = host;

    /// <summary>
    /// The [[ArrayBufferDetachKey]] of the buffers created by <see cref="JavaScriptAdapter.CreateArrayBufferWithData"/>,
    /// created on first use (the container of a ConditionalWeakTable is finalized twice).
    /// </summary>
    public ConditionalWeakTable<JsObject, string> DetachKeys => field ??= [];

    public JsRealm CreateRealm() => Engine.CreateRealm();
}

/// <summary>The primitives of the engine used by the WebIDL bindings (<see cref="WebIdlAgent{TAdapter, TValue}"/>). The values are the ones of <see cref="JavaScriptValues"/>.</summary>
internal readonly struct JavaScriptAdapter(JavaScriptAdapterState state) : IJsAdapter<object>
{
    // The scripts of the implementation, parsed once for all the engines (e.g. Streams.js and Fetch.js)
    private static readonly ConcurrentDictionary<string, JsPreparedScript> BuiltInScripts = new(StringComparer.Ordinal);

    private JsEngine Engine => state.Engine;

    /// <summary>The realm of the running execution context: the objects are created in it.</summary>
    private JsRealm Realm => state.Engine.CurrentRealm!;

    /// <summary>Converts a property key of the bindings: a string or a symbol (any other value is converted by ToPropertyKey).</summary>
    private JsPropertyKey Key(object key) => key is string or JsSymbol ? ToKey(key) : Engine.ToPropertyKey(ToJs(key));

    public object Undefined => UndefinedValue.Instance;

    public object Null => NullValue.Instance;

    public object FromBoolean(bool value) => JavaScriptValues.FromBoolean(value);

    public object FromNumber(double value) => JavaScriptValues.FromNumber(value);

    public object FromString(string value) => value;

    public object FromBigInt(BigInteger value) => value;

    public bool IsUndefined(object value) => value is UndefinedValue;

    public bool IsNull(object value) => value is NullValue;

    public bool IsBoolean(object value) => value is bool;

    public bool IsNumber(object value) => value is double;

    public bool IsBigInt(object value) => value is BigInteger;

    public bool IsString(object value) => value is string;

    public bool IsSymbol(object value) => value is JsSymbol;

    public bool IsObject(object value) => value is JsObject;

    public bool IsValue(object value) => value is JsObject or string or double or bool or UndefinedValue or NullValue or JsSymbol or BigInteger;

    public bool IsCallable(object value) => value is JsObject { IsCallable: true };

    public bool IsConstructor(object value) => value is JsObject { IsConstructor: true };

    // An Array exotic object: a Proxy of an array is not one (e.g. it is not a valid key of IndexedDB)
    public bool IsArray(object value) => value is JsArray;

    public bool IsDate(object value) => value is JsObject obj && obj.HasInternalSlot(EngineInternalSlot.DateValue);

    public bool AsBoolean(object value) => (bool)value;

    public double AsNumber(object value) => (double)value;

    public string AsString(object value) => (string)value;

    public double AsDate(object value) => ((JsObject)value).TryGetDateValue(out var timeValue) ? timeValue : double.NaN;

    public object CreateDate(double timeValue) => Realm.CreateDate(timeValue);

    public double ToNumber(object value) => value is double number ? number : Engine.ToNumber(ToJs(value));

    public string ToString(object value) => value is string text ? text : Engine.ToString(ToJs(value));

    public bool ToBoolean(object value) => value is bool boolean ? boolean : JsEngine.ToBoolean(ToJs(value));

    public BigInteger ToBigInt(object value) => Engine.ToBigInt(ToJs(value));

    public object Get(object obj, object key)
    {
        using var scope = Engine.Enter();
        return ToObject(((JsObject)obj).GetProperty(Key(key)));
    }

    public void Set(object obj, object key, object value)
    {
        using var scope = Engine.Enter();
        if (!((JsObject)obj).SetProperty(Key(key), ToJs(value)))
            throw Realm.CreateException(JsErrorType.TypeError, $"Cannot assign to property '{key}'");
    }

    public bool DefineOwnProperty(object obj, object key, in JsPropertyDescriptor<object> descriptor)
    {
        using var scope = Engine.Enter();
        return ((JsObject)obj).DefineProperty(Key(key), ToDescriptor(descriptor));
    }

    public bool GetOwnProperty(object obj, object key, out JsPropertyDescriptor<object> descriptor)
    {
        using var scope = Engine.Enter();
        if (((JsObject)obj).TryGetOwnProperty(Key(key), out var engineDescriptor))
        {
            descriptor = FromDescriptor(engineDescriptor);
            return true;
        }

        descriptor = default;
        return false;
    }

    public bool HasProperty(object obj, object key)
    {
        using var scope = Engine.Enter();
        return ((JsObject)obj).ContainsProperty(Key(key));
    }

    public bool Delete(object obj, object key)
    {
        using var scope = Engine.Enter();
        return ((JsObject)obj).DeleteProperty(Key(key));
    }

    public IReadOnlyList<object> OwnKeys(object obj)
    {
        using var scope = Engine.Enter();
        return FromKeys(((JsObject)obj).GetOwnPropertyKeys());
    }

    public object GetPrototypeOf(object obj)
    {
        using var scope = Engine.Enter();
        return (object?)((JsObject)obj).GetPrototype() ?? NullValue.Instance;
    }

    public bool IsRevokedProxy(object value, out object? objectPrototype)
    {
        if (value is JsProxy { IsRevoked: true } proxy)
        {
            objectPrototype = proxy.CreationRealm?.ObjectPrototype;
            return true;
        }

        objectPrototype = null;
        return false;
    }

    public bool SetPrototypeOf(object obj, object prototype)
    {
        using var scope = Engine.Enter();
        return ((JsObject)obj).SetPrototype(prototype as JsObject);
    }

    public object CreateObject(object prototype) => Realm.CreateObject(prototype as JsObject);

    public object CreateArray(ReadOnlySpan<object> items)
    {
        using var arguments = new EngineArguments(items);
        return Realm.CreateArray(arguments.Span);
    }

    public object CreateFunction(string name, int length, JsHostFunction<object> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        // The functions of the bindings run in the realm of their caller: e.g. the traps of a WindowProxy check the realm
        // of the code that uses it, and a window is the global object for the code of its own realm
        return Realm.CreateFunction(name, length, (thisValue, arguments) =>
        {
            using var bindingArguments = new BindingArguments(arguments);
            return ToJs(function(ToObject(thisValue), bindingArguments.Span));
        }, runInCallerRealm: true);
    }

    public object Call(object function, object thisValue, ReadOnlySpan<object> arguments)
    {
        using var engineArguments = new EngineArguments(arguments);
        return ToObject(Engine.Call(ToJs(function), ToJs(thisValue), engineArguments.Span));
    }

    public object Construct(object constructor, ReadOnlySpan<object> arguments, object newTarget)
    {
        using var engineArguments = new EngineArguments(arguments);
        return ToObject(Engine.Construct(ToJs(constructor), engineArguments.Span, ToJs(newTarget)));
    }

    public object CreatePlatformObject(object prototype, object target, IJsHostObjectHandler<object>? handler)
    {
        var prototypeObject = prototype as JsObject;
        return handler is null ? new JavaScriptPlatformObject(prototypeObject, target) : new JavaScriptLegacyPlatformObject(prototypeObject, target, handler);
    }

    public bool TryGetPlatformObject(object value, [NotNullWhen(true)] out object? target)
    {
        target = (value as JavaScriptPlatformObject)?.Target;
        return target is not null;
    }

    public bool OrdinaryGetOwnProperty(object obj, object key, out JsPropertyDescriptor<object> descriptor)
    {
        if (obj is not JsHostObject hostObject)
            return GetOwnProperty(obj, key, out descriptor);

        using var scope = Engine.Enter();
        if (hostObject.OrdinaryGetOwnProperty(Key(key), out var engineDescriptor))
        {
            descriptor = FromDescriptor(engineDescriptor);
            return true;
        }

        descriptor = default;
        return false;
    }

    public bool OrdinaryDefineOwnProperty(object obj, object key, in JsPropertyDescriptor<object> descriptor)
    {
        if (obj is not JsHostObject hostObject)
            return DefineOwnProperty(obj, key, descriptor);

        using var scope = Engine.Enter();
        return hostObject.OrdinaryDefineOwnProperty(Key(key), ToDescriptor(descriptor));
    }

    public bool OrdinaryDelete(object obj, object key)
    {
        if (obj is not JsHostObject hostObject)
            return Delete(obj, key);

        using var scope = Engine.Enter();
        return hostObject.OrdinaryDelete(Key(key));
    }

    public IReadOnlyList<object> OrdinaryOwnKeys(object obj)
    {
        if (obj is not JsHostObject hostObject)
            return OwnKeys(obj);

        using var scope = Engine.Enter();
        return FromKeys(hostObject.OrdinaryOwnPropertyKeys());
    }

    public Exception CreateException(object value) => new JsException(ToJs(value));

    public bool TryGetThrownValue(Exception exception, [NotNullWhen(true)] out object? value)
    {
        if (exception is JsException jsException)
        {
            value = ToObject(jsException.Value);
            return true;
        }

        value = null;
        return false;
    }

    public bool IsTerminationException(Exception exception) => exception is JsTerminationException;

    public JsErrorInformation GetErrorInformation(object error)
    {
        using var scope = Engine.Enter();
        var value = ToJs(error);
        var message = error is JsObject obj && obj.ContainsProperty("message") ? Engine.ToString(obj.GetProperty("message")) : Engine.ToString(value);
        return new JsErrorInformation(message, "", 0, 0);
    }

    public object CurrentRealm => Engine.CurrentRealm!;

    public object GetGlobalObject(object realm) => ((JsRealm)realm).GlobalObject;

    public bool IsCurrentRealm(object realm) => ReferenceEquals(Engine.CurrentRealm, realm);

    public TResult RunInRealm<TResult>(object realm, Func<TResult> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (ReferenceEquals(Engine.CurrentRealm, realm))
            return action();

        return Engine.RunInRealm((JsRealm)realm, action);
    }

    public object Evaluate(object realm, string code)
    {
        // The code of the implementation or of the host is not blocked by the content security policies
        var host = state.Host;
        host.SkipNextCompilationCheck = true;
        try
        {
            return ToObject(((JsRealm)realm).Evaluate(code));
        }
        finally
        {
            host.SkipNextCompilationCheck = false;
        }
    }

    public object EvaluateBuiltIn(object realm, string code)
    {
        var script = BuiltInScripts.GetOrAdd(code, static code => JsPreparedScript.Parse(code));
        return ToObject(((JsRealm)realm).Execute(script, hostDefined: null));
    }

    public object GetWellKnownSymbol(JsWellKnownSymbol symbol) => symbol switch
    {
        JsWellKnownSymbol.Iterator => JsSymbol.Iterator,
        JsWellKnownSymbol.AsyncIterator => JsSymbol.AsyncIterator,
        JsWellKnownSymbol.ToStringTag => JsSymbol.ToStringTag,
        JsWellKnownSymbol.Unscopables => JsSymbol.Unscopables,
        _ => throw new ArgumentOutOfRangeException(nameof(symbol)),
    };

    public bool? HasInternalSlot(object value, WebIdlInternalSlot slot)
    {
        if (value is not JsObject obj)
            return false;

        return slot switch
        {
            WebIdlInternalSlot.BooleanData => obj.HasInternalSlot(EngineInternalSlot.BooleanData),
            WebIdlInternalSlot.NumberData => obj.HasInternalSlot(EngineInternalSlot.NumberData),
            WebIdlInternalSlot.BigIntData => obj.HasInternalSlot(EngineInternalSlot.BigIntData),
            WebIdlInternalSlot.StringData => obj.HasInternalSlot(EngineInternalSlot.StringData),
            WebIdlInternalSlot.SymbolData => obj.HasInternalSlot(EngineInternalSlot.SymbolData),
            WebIdlInternalSlot.DateValue => obj.HasInternalSlot(EngineInternalSlot.DateValue),
            WebIdlInternalSlot.ArrayBufferData => obj.HasInternalSlot(EngineInternalSlot.ArrayBufferData),
            WebIdlInternalSlot.SharedArrayBufferData => obj.HasInternalSlot(EngineInternalSlot.SharedArrayBufferData),
            WebIdlInternalSlot.DataView => obj.HasInternalSlot(EngineInternalSlot.DataView),
            WebIdlInternalSlot.MapData => obj.HasInternalSlot(EngineInternalSlot.MapData),
            WebIdlInternalSlot.SetData => obj.HasInternalSlot(EngineInternalSlot.SetData),
            WebIdlInternalSlot.WeakMapData => obj.HasInternalSlot(EngineInternalSlot.WeakMapData),
            WebIdlInternalSlot.WeakSetData => obj.HasInternalSlot(EngineInternalSlot.WeakSetData),
            _ => null,
        };
    }

    public bool TrySetErrorStack(object error, string stack) => error is JsObject obj && obj.TrySetErrorStack(stack);

    public bool TryGetBufferSource(object value, out WebIdlBufferKind kind, out bool isShared, out Memory<byte> bytes)
    {
        if (value is JsObject obj && JsBinaryData.TryGetBufferSource(obj, out var engineKind, out isShared, out bytes))
        {
            kind = (WebIdlBufferKind)engineKind;
            return true;
        }

        kind = default;
        isShared = false;
        bytes = default;
        return false;
    }

    public object CreateArrayBuffer(ReadOnlySpan<byte> bytes) => Realm.CreateArrayBuffer(bytes);

    public object? CreateArrayBufferWithData(byte[] data, string detachKey)
    {
        var buffer = Realm.CreateArrayBuffer(data.AsMemory(), detachKey);
        state.DetachKeys.AddOrUpdate(buffer, detachKey);
        return buffer;
    }

    public void DetachArrayBuffer(object arrayBuffer)
    {
        if (arrayBuffer is not JsObject buffer || !buffer.HasInternalSlot(EngineInternalSlot.ArrayBufferData))
            return;

        using var scope = Engine.Enter();
        JsBinaryData.DetachArrayBuffer(buffer, state.DetachKeys.TryGetValue(buffer, out var key) ? key : default(JsValue));
    }

    public object? GetSharedDataBlock(object sharedArrayBuffer) => sharedArrayBuffer is JsObject obj ? JsBinaryData.GetSharedDataBlock(obj) : null;

    public object? CreateSharedArrayBuffer(object dataBlock) => dataBlock is JsSharedDataBlock block ? Realm.CreateSharedArrayBuffer(block) : null;

    public object CreateArrayBufferView(WebIdlBufferKind kind, object buffer, int byteOffset, int length) => Realm.CreateBufferView((EngineBufferKind)kind, (JsObject)buffer, byteOffset, length);

    // The factories of the lazy properties are their state: no closure is allocated per property
    private static readonly Func<object?, JsValue> InvokeFactory = static state => ToJs(((Func<object>)state!)());

    public void DefineLazyDataProperty(object obj, object key, Func<object> factory, bool writable, bool enumerable, bool configurable)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using var scope = Engine.Enter();
        ((JsObject)obj).DefineLazyDataProperty(Key(key), InvokeFactory, factory, writable, enumerable, configurable);
    }

    public void DefineLazyAccessorProperty(object obj, object key, Func<object> getterFactory, Func<object>? setterFactory, bool enumerable, bool configurable)
    {
        ArgumentNullException.ThrowIfNull(getterFactory);
        using var scope = Engine.Enter();
        ((JsObject)obj).DefineLazyAccessorProperty(Key(key), InvokeFactory, getterFactory, setterFactory is null ? null : InvokeFactory, setterFactory, enumerable, configurable);
    }

    private static readonly Func<object?, JsValue> CreateLazyValue = static state => ToJs(((IJsLazyValue<object>)state!).CreateValue());
    private static readonly Func<object?, JsValue> CreateLazyGetter = static state => ToJs(((IJsLazyAccessor<object>)state!).CreateGetter());
    private static readonly Func<object?, JsValue> CreateLazySetter = static state => ToJs(((IJsLazyAccessor<object>)state!).CreateSetter());

    public void DefineLazyDataProperty(object obj, object key, IJsLazyValue<object> factory, bool writable, bool enumerable, bool configurable)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using var scope = Engine.Enter();
        ((JsObject)obj).DefineLazyDataProperty(Key(key), CreateLazyValue, factory, writable, enumerable, configurable);
    }

    public void ReserveProperties(object obj, int count) => ((JsObject)obj).ReserveProperties(count);

    public bool TryMakePrototypeImmutable(object obj)
    {
        if (obj is not JsProxy)
        {
            ((JsObject)obj).MakePrototypeImmutable();
        }

        return true;
    }

    public void DefineLazyAccessorProperty(object obj, object key, IJsLazyAccessor<object> factory, bool hasSetter, bool enumerable, bool configurable)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using var scope = Engine.Enter();
        ((JsObject)obj).DefineLazyAccessorProperty(Key(key), CreateLazyGetter, factory, hasSetter ? CreateLazySetter : null, factory, enumerable, configurable);
    }
}

/// <summary>The JavaScript object of a .NET platform object (https://webidl.spec.whatwg.org/#dfn-platform-object).</summary>
/// <remarks>Its internal methods are the ordinary ones (the engine uses its fast paths), except for a legacy platform object.</remarks>
internal class JavaScriptPlatformObject(JsObject? prototype, object target, bool hasOrdinaryInternalMethods = true) : JsHostObject(prototype, target, hasOrdinaryInternalMethods)
{
    /// <summary>Gets the .NET platform object.</summary>
    public new object Target { get; } = target;
}

/// <summary>
/// The JavaScript object of a legacy platform object (https://webidl.spec.whatwg.org/#es-legacy-platform-objects): a host
/// exotic object whose internal methods call the handler first.
/// </summary>
internal sealed class JavaScriptLegacyPlatformObject(JsObject? prototype, object target, IJsHostObjectHandler<object> handler) : JavaScriptPlatformObject(prototype, target, hasOrdinaryInternalMethods: false)
{
    protected override bool GetOwnPropertyCore(JsPropertyKey key, out JsPropertyDescriptor descriptor)
    {
        if (handler.TryGetOwnProperty(this, FromKey(key), out var handled))
        {
            descriptor = ToDescriptor(handled);
            return true;
        }

        return base.GetOwnPropertyCore(key, out descriptor);
    }

    protected override bool SetCore(JsPropertyKey key, JsValue value, JsValue receiver)
    {
        var handled = ReferenceEquals(receiver.TryGetObject(), this)
            ? handler.TrySet(this, FromKey(key), ToObject(value), out var result)
            : handler.TrySet(this, FromKey(key), ToObject(value), ToObject(receiver), out result);
        if (handled)
            return result;

        return base.SetCore(key, value, receiver);
    }

    protected override bool DefineOwnPropertyCore(JsPropertyKey key, in JsPropertyDescriptor descriptor)
    {
        if (handler.TryDefineOwnProperty(this, FromKey(key), FromDescriptor(descriptor), out var result))
            return result;

        return base.DefineOwnPropertyCore(key, descriptor);
    }

    protected override bool DeleteCore(JsPropertyKey key)
    {
        if (handler.TryDelete(this, FromKey(key), out var result))
            return result;

        return base.DeleteCore(key);
    }

    protected override IReadOnlyList<JsPropertyKey> OwnPropertyKeysCore()
    {
        var keys = handler.GetOwnKeys(this);
        if (keys is null)
            return base.OwnPropertyKeysCore();

        var ordinaryKeys = base.OwnPropertyKeysCore();
        var result = new List<JsPropertyKey>(keys.Count + ordinaryKeys.Count);
        foreach (var key in keys)
        {
            result.Add(ToKey(key));
        }

        result.AddRange(ordinaryKeys);
        return result;
    }
}
