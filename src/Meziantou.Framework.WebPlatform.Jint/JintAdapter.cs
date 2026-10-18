using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Jint;

/// <summary>A Jint host that can create additional realms (one per window).</summary>
internal sealed class JsHost(JintScriptEngine engine) : Host
{
    public Realm CreateAdditionalRealm() => CreateRealm();

    /// <summary>
    /// Whether the next compilation of a string by Jint is already checked (by the hooks of <c>eval</c> and of the
    /// <c>Function</c> constructors, which know the code), or is the code of the implementation: Jint calls
    /// <see cref="EnsureCanCompileStrings"/> first, which resets it.
    /// </summary>
    public bool StringCompilationChecked { get; set; }

    /// <summary>
    /// HostEnsureCanCompileStrings (https://html.spec.whatwg.org/multipage/webappapis.html#hostensurecancompilestrings(realm,-parameterstrings,-bodystring,-codestring,-compilationtype,-parameterargs,-bodyarg)):
    /// Jint does not give the code, which the hooks installed by <see cref="JintScriptEngine"/> check before calling Jint.
    /// </summary>
    public override void EnsureCanCompileStrings(Realm callerRealm, Realm evalRealm)
    {
        base.EnsureCanCompileStrings(callerRealm, evalRealm);
        if (StringCompilationChecked)
        {
            StringCompilationChecked = false;
            return;
        }

        engine.EnsureCanCompileUnknownStrings(callerRealm, evalRealm);
    }

    /// <summary>Runs a compilation of a string of the implementation (e.g. a trampoline of a realm), which no content security policy blocks.</summary>
    public T RunTrustedCompilation<T>(Func<T> action)
    {
        StringCompilationChecked = true;
        try
        {
            return action();
        }
        finally
        {
            StringCompilationChecked = false;
        }
    }

    /// <summary>import.meta (https://html.spec.whatwg.org/multipage/webappapis.html#hostgetimportmetaproperties).</summary>
    public override List<KeyValuePair<JsValue, JsValue>> GetImportMetaProperties(global::Jint.Runtime.Modules.Module module) => engine.GetImportMetaProperties(module);
}

/// <summary>
/// A realm of the Jint engine. Jint runs code in the realm of the running function: the realm is entered by calling a
/// trampoline function created by the indirect eval of the realm.
/// </summary>
internal sealed class JintRealm
{
    public JintRealm(Engine engine, JsHost host, ObjectInstance global)
    {
        Host = host;
        Global = global;
        Eval = global.Get("eval");
        Trampoline = host.RunTrustedCompilation(() => engine.Call(Eval, JsValue.Undefined, [new JsString("(function (f) { return f(); })")]));
    }

    public JsHost Host { get; }

    public ObjectInstance Global { get; }

    public JsValue Eval { get; }

    public JsValue Trampoline { get; }
}

/// <summary>The state shared by the copies of a <see cref="JintAdapter"/>: the engine and its realms.</summary>
internal sealed class JintAdapterState(Engine engine, JsHost host, bool retainFunctionSourceText)
{
    private readonly Dictionary<ObjectInstance, JintRealm> _realms = new(ReferenceEqualityComparer.Instance);

    public Engine Engine { get; } = engine;

    /// <summary>Whether the functions keep their source text (<see cref="Options.RetainFunctionSourceText"/>).</summary>
    public bool RetainFunctionSourceText { get; } = retainFunctionSourceText;

    /// <summary>Returns the realm whose global object is <paramref name="global"/>.</summary>
    public JintRealm GetRealm(ObjectInstance global)
    {
        if (!_realms.TryGetValue(global, out var realm))
        {
            realm = new JintRealm(Engine, host, global);
            _realms[global] = realm;
        }

        return realm;
    }

    public JintRealm CreateRealm() => GetRealm(host.CreateAdditionalRealm().GlobalObject);
}

/// <summary>The primitives of Jint used by the WebIDL bindings (<see cref="WebIdlAgent{TAdapter, TValue}"/>).</summary>
internal readonly struct JintAdapter(JintAdapterState state) : IJsAdapter<JsValue>
{
    private Engine Engine => state.Engine;

    public JsValue Undefined => JsValue.Undefined;

    public JsValue Null => JsValue.Null;

    public JsValue FromBoolean(bool value) => value ? JsBoolean.True : JsBoolean.False;

    public JsValue FromNumber(double value) => JsNumber.Create(value);

    public JsValue FromString(string value) => JsString.Create(value);

    public JsValue FromBigInt(BigInteger value) => new JsBigInt(value);

    public bool IsUndefined(JsValue value) => value.IsUndefined();

    public bool IsNull(JsValue value) => value.IsNull();

    public bool IsBoolean(JsValue value) => value.IsBoolean();

    public bool IsNumber(JsValue value) => value.IsNumber();

    public bool IsBigInt(JsValue value) => value.IsBigInt();

    public bool IsString(JsValue value) => value.IsString();

    public bool IsSymbol(JsValue value) => value.IsSymbol();

    public bool IsObject(JsValue value) => value.IsObject();

    public bool IsCallable(JsValue value) => value.IsCallable();

    public bool IsConstructor(JsValue value) => value.IsConstructor();

    public bool IsArray(JsValue value) => value.IsArray();

    public bool IsDate(JsValue value) => value.IsDate();

    public bool AsBoolean(JsValue value) => value.AsBoolean();

    public double AsNumber(JsValue value) => value.AsNumber();

    public string AsString(JsValue value) => value.AsString();

    public double AsDate(JsValue value) => value.AsDate().DateValue;

    public JsValue CreateDate(double timeValue) => Engine.Construct(Engine.Global.Get("Date"), [JsNumber.Create(timeValue)]);

    public double ToNumber(JsValue value)
    {
        try
        {
            return TypeConverter.ToNumber(value);
        }
        catch (JintException ex) when (IsTypeErrorException(ex))
        {
#pragma warning disable MA0054 // A JavaScript exception has no inner exception
            throw new JavaScriptException(Engine.Intrinsics.TypeError, ex.Message);
#pragma warning restore MA0054
        }
    }

    public string ToString(JsValue value)
    {
        try
        {
            return TypeConverter.ToString(value);
        }
        catch (JintException ex) when (IsTypeErrorException(ex))
        {
#pragma warning disable MA0054 // A JavaScript exception has no inner exception
            throw new JavaScriptException(Engine.Intrinsics.TypeError, ex.Message);
#pragma warning restore MA0054
        }
    }

    /// <summary>
    /// Whether an exception is the internal TypeErrorException of Jint, thrown by the conversions that have no engine (e.g.
    /// ToNumber of a BigInt): it is a TypeError of JavaScript.
    /// </summary>
    private static bool IsTypeErrorException(JintException exception) => exception.GetType().FullName == "Jint.Runtime.TypeErrorException";

    public bool ToBoolean(JsValue value) => TypeConverter.ToBoolean(value);

    public BigInteger ToBigInt(JsValue value)
    {
        try
        {
            return TypeConverter.ToBigInt(value);
        }
        catch (JintException ex) when (IsTypeErrorException(ex))
        {
#pragma warning disable MA0054 // A JavaScript exception has no inner exception
            throw new JavaScriptException(Engine.Intrinsics.TypeError, ex.Message);
#pragma warning restore MA0054
        }
    }

    public JsValue Get(JsValue obj, JsValue key) => obj.Get(key, obj);

    public void Set(JsValue obj, JsValue key, JsValue value)
    {
        if (!obj.AsObject().Set(key, value, obj))
            throw new JavaScriptException(Engine.Intrinsics.TypeError, $"Cannot assign to property '{key}'");
    }

    public bool DefineOwnProperty(JsValue obj, JsValue key, in JsPropertyDescriptor<JsValue> descriptor) => obj.AsObject().DefineOwnProperty(key, ToJint(descriptor));

    public bool GetOwnProperty(JsValue obj, JsValue key, out JsPropertyDescriptor<JsValue> descriptor) => FromJint(obj.AsObject().GetOwnProperty(key), out descriptor);

    public bool HasProperty(JsValue obj, JsValue key) => obj.AsObject().HasProperty(key);

    public bool Delete(JsValue obj, JsValue key) => obj.AsObject().Delete(key);

    public IReadOnlyList<JsValue> OwnKeys(JsValue obj) => obj.AsObject().GetOwnPropertyKeys(Types.String | Types.Symbol);

    public JsValue GetPrototypeOf(JsValue obj) => (JsValue?)obj.AsObject().Prototype ?? JsValue.Null;

    public bool IsRevokedProxy(JsValue value, out JsValue? objectPrototype)
    {
        // The [[Prototype]] field of a proxy is not used by its internal methods: it is the %Object.prototype% of the realm
        // that created the proxy
        if (value is ObjectInstance obj && obj.GetType() == JintProxies.ProxyType && JintProxies.ProxyTarget(obj) is null)
        {
            objectPrototype = JintProxies.ObjectPrototype(obj);
            return true;
        }

        objectPrototype = null;
        return false;
    }

    public bool SetPrototypeOf(JsValue obj, JsValue prototype)
    {
        if (obj is JintObject jintObject && prototype is ObjectInstance prototypeObject)
        {
            jintObject.SetPrototype(prototypeObject);
            return true;
        }

        // Object.setPrototypeOf throws a TypeError when [[SetPrototypeOf]] returns false
        try
        {
            Engine.Call(Engine.Intrinsics.Object.Get("setPrototypeOf"), JsValue.Undefined, [obj, prototype]);
            return true;
        }
        catch (JavaScriptException)
        {
            return false;
        }
    }

    public JsValue CreateObject(JsValue prototype)
    {
        // An ordinary object of the current realm has the Object.prototype of the realm
        if (ReferenceEquals(prototype, Engine.Intrinsics.Object.PrototypeObject))
            return new JsObject(Engine);

        // Jint cannot set a null prototype from .NET: Object.create(null)
        if (prototype is not ObjectInstance prototypeObject)
            return Engine.Call(Engine.Intrinsics.Object.Get("create"), JsValue.Undefined, [JsValue.Null]);

        return new JintObject(Engine, prototypeObject);
    }

    public JsValue CreateArray(ReadOnlySpan<JsValue> items) => new JsArray(Engine, items.ToArray());

    public JsValue CreateFunction(string name, int length, JsHostFunction<JsValue> function)
    {
        var engine = Engine;
        return new ClrFunction(engine, name, (thisValue, arguments) =>
        {
            // A recursion through the platform objects (e.g. a toString() method that calls the constructor that converts
            // it) throws a RangeError, as in the browsers, instead of overflowing the stack of the thread
            if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
                throw new JavaScriptException((global::Jint.Native.Error.ErrorConstructor)engine.Global.Get("RangeError"), "Maximum call stack size exceeded");

            return function(thisValue, arguments);
        }, length, PropertyFlag.Configurable);
    }

    public JsValue Call(JsValue function, JsValue thisValue, ReadOnlySpan<JsValue> arguments) => Engine.Call(function, thisValue, arguments.ToArray());

    public JsValue Construct(JsValue constructor, ReadOnlySpan<JsValue> arguments, JsValue newTarget)
    {
        if (ReferenceEquals(constructor, newTarget))
            return Engine.Construct(constructor, arguments.ToArray());

        // Reflect.construct(constructor, arguments, newTarget)
        var reflect = Engine.Global.Get("Reflect");
        return Engine.Call(reflect.Get("construct"), reflect, [constructor, CreateArray(arguments), newTarget]);
    }

    public JsValue CreatePlatformObject(JsValue prototype, object target, IJsHostObjectHandler<JsValue>? handler)
        => new JintPlatformObject(Engine, target, handler, prototype.AsObject());

    public bool TryGetPlatformObject(JsValue value, [NotNullWhen(true)] out object? target)
    {
        target = (value as JintPlatformObject)?.Target;
        return target is not null;
    }

    public bool OrdinaryGetOwnProperty(JsValue obj, JsValue key, out JsPropertyDescriptor<JsValue> descriptor)
        => FromJint(obj is JintPlatformObject platformObject ? platformObject.OrdinaryGetOwnProperty(key) : obj.AsObject().GetOwnProperty(key), out descriptor);

    public bool OrdinaryDefineOwnProperty(JsValue obj, JsValue key, in JsPropertyDescriptor<JsValue> descriptor)
        => obj is JintPlatformObject platformObject ? platformObject.OrdinaryDefineOwnProperty(key, ToJint(descriptor)) : obj.AsObject().DefineOwnProperty(key, ToJint(descriptor));

    public bool OrdinaryDelete(JsValue obj, JsValue key)
        => obj is JintPlatformObject platformObject ? platformObject.OrdinaryDelete(key) : obj.AsObject().Delete(key);

    public IReadOnlyList<JsValue> OrdinaryOwnKeys(JsValue obj)
        => obj is JintPlatformObject platformObject ? platformObject.OrdinaryOwnKeys(Types.String | Types.Symbol) : obj.AsObject().GetOwnPropertyKeys(Types.String | Types.Symbol);

    public Exception CreateException(JsValue value) => new JavaScriptException(value);

    public bool TryGetThrownValue(Exception exception, [NotNullWhen(true)] out JsValue? value)
    {
        value = (exception as JavaScriptException)?.Error;
        return value is not null;
    }

    public bool IsTerminationException(Exception exception) => exception is ExecutionCanceledException;

    public JsErrorInformation GetErrorInformation(JsValue error)
    {
        var message = error is ObjectInstance obj && obj.HasProperty("message") ? TypeConverter.ToString(obj.Get("message")) : TypeConverter.ToString(error);
        return new JsErrorInformation(message, "", 0, 0);
    }

    public object CurrentRealm => state.GetRealm(Engine.Global);

    public JsValue GetGlobalObject(object realm) => ((JintRealm)realm).Global;

    public bool IsCurrentRealm(object realm) => ReferenceEquals(Engine.Global, ((JintRealm)realm).Global);

    public TResult RunInRealm<TResult>(object realm, Func<TResult> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var jintRealm = (JintRealm)realm;
        if (ReferenceEquals(Engine.Global, jintRealm.Global))
            return action();

        // The function called by the trampoline runs in the realm of the trampoline
        TResult result = default!;
        Engine.Call(jintRealm.Trampoline, JsValue.Undefined, [new ClrFunction(Engine, "", (_, _) =>
        {
            result = action();
            return JsValue.Undefined;
        })]);
        return result;
    }

    public JsValue Evaluate(object realm, string code)
    {
        // The code of the implementation or of the host is not blocked by the content security policies
        var jintRealm = (JintRealm)realm;
        var engine = Engine;
        return jintRealm.Host.RunTrustedCompilation(() => engine.Call(jintRealm.Eval, JsValue.Undefined, [JsString.Create(code)]));
    }

    // The scripts of the implementation, parsed once for all the engines (parsing Streams.js and Fetch.js in each realm is
    // most of the cost of their first use). The ones that retain the source text of the functions are distinct.
    private static readonly ConcurrentDictionary<string, Prepared<Acornima.Ast.Script>> BuiltInScripts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Prepared<Acornima.Ast.Script>> BuiltInScriptsWithSourceText = new(StringComparer.Ordinal);
    private static readonly ScriptPreparationOptions RetainSourceTextPreparationOptions = ScriptPreparationOptions.Default with { ParsingOptions = ScriptParsingOptions.Default with { RetainFunctionSourceText = true } };

    public JsValue EvaluateBuiltIn(object realm, string code)
    {
        // A prepared script is evaluated as a script, and Jint runs the pending jobs at the end of a script unless it is already
        // running them: the jobs must not run during the evaluation (e.g. when a script uses fetch() for the first time while
        // promise reactions are pending), so the code is evaluated by the indirect eval when jobs are pending. The scripts of the
        // implementation are function expressions or expressions: evaluated as a script or by eval, they have the same result.
        var eventLoop = JintEventLoop.Get(Engine);
        if (!JintEventLoop.IsEmpty(eventLoop) && !JintEventLoop.IsRunningJob(eventLoop))
            return Evaluate(realm, code);

        var retainSourceText = state.RetainFunctionSourceText;
        var prepared = (retainSourceText ? BuiltInScriptsWithSourceText : BuiltInScripts).GetOrAdd(code, static (code, retainSourceText) => Engine.PrepareScript(code, options: retainSourceText ? RetainSourceTextPreparationOptions : null), retainSourceText);
        var engine = Engine;
        return RunInRealm(realm, () => engine.Evaluate(prepared));
    }

    public JsValue GetWellKnownSymbol(JsWellKnownSymbol symbol) => symbol switch
    {
        JsWellKnownSymbol.Iterator => GlobalSymbolRegistry.Iterator,
        JsWellKnownSymbol.AsyncIterator => GlobalSymbolRegistry.AsyncIterator,
        JsWellKnownSymbol.ToStringTag => GlobalSymbolRegistry.ToStringTag,
        JsWellKnownSymbol.Unscopables => GlobalSymbolRegistry.Unscopables,
        _ => throw new ArgumentOutOfRangeException(nameof(symbol)),
    };

    public bool? HasInternalSlot(JsValue value, JsInternalSlot slot) => slot switch
    {
        JsInternalSlot.BooleanData => JintBuiltInTypes.Boolean.IsInstanceOfType(value),
        JsInternalSlot.NumberData => JintBuiltInTypes.Number.IsInstanceOfType(value),
        JsInternalSlot.BigIntData => JintBuiltInTypes.BigInt.IsInstanceOfType(value),
        JsInternalSlot.StringData => JintBuiltInTypes.String.IsInstanceOfType(value),
        JsInternalSlot.SymbolData => JintBuiltInTypes.Symbol.IsInstanceOfType(value),
        JsInternalSlot.DateValue => value is JsDate,
        JsInternalSlot.ArrayBufferData => value is JsArrayBuffer buffer && !JintBuffers.IsShared(buffer),
        JsInternalSlot.SharedArrayBufferData => value is JsArrayBuffer buffer && JintBuffers.IsShared(buffer),
        JsInternalSlot.DataView => JintBuiltInTypes.DataView.IsInstanceOfType(value),
        JsInternalSlot.MapData => value is JsMap,
        JsInternalSlot.SetData => value is JsSet,
        JsInternalSlot.WeakMapData => JintBuiltInTypes.WeakMap.IsInstanceOfType(value),
        JsInternalSlot.WeakSetData => JintBuiltInTypes.WeakSet.IsInstanceOfType(value),
        _ => null,
    };

    public bool TrySetErrorStack(JsValue error, string stack)
    {
        // The [[Stack]] internal slot read by the Error.prototype.stack accessor of Jint
        if (error is not JsError jsError)
            return false;

        JintErrors.Stack(jsError) = JsString.Create(stack);
        return true;
    }

    public bool TryGetBufferSource(JsValue value, out JsBufferKind kind, out bool isShared, out Memory<byte> bytes)
    {
        switch (value)
        {
            case JsArrayBuffer buffer:
                isShared = JintBuffers.IsShared(buffer);
                kind = isShared ? JsBufferKind.SharedArrayBuffer : JsBufferKind.ArrayBuffer;
                bytes = JintBuffers.GetBytes(buffer, 0, int.MaxValue);
                return true;

            case JsTypedArray typedArray:
                kind = JintBuffers.GetTypedArrayKind(typedArray);
                var viewedBuffer = JintBuffers.TypedArrayBuffer(typedArray);
                isShared = JintBuffers.IsShared(viewedBuffer);
                bytes = JintBuffers.GetBytes(viewedBuffer, JintBuffers.TypedArrayByteOffset(typedArray), (int)typedArray.Length * JsBufferSource.GetElementSize(kind));
                return true;

            case not null when value.IsDataView():
                kind = JsBufferKind.DataView;
                var dataViewBuffer = JintBuffers.DataViewBuffer(value);
                isShared = dataViewBuffer is not null && JintBuffers.IsShared(dataViewBuffer);
                bytes = dataViewBuffer is null ? default : JintBuffers.GetBytes(dataViewBuffer, (int)JintBuffers.DataViewByteOffset(value), (int)Math.Min(JintBuffers.DataViewByteLength(value), int.MaxValue));
                return true;
        }

        kind = default;
        isShared = false;
        bytes = default;
        return false;
    }

    public JsValue CreateArrayBuffer(ReadOnlySpan<byte> bytes) => Engine.Intrinsics.ArrayBuffer.Construct(bytes.ToArray());

    public JsValue? CreateArrayBufferWithData(byte[] data, string detachKey)
    {
        var buffer = Engine.Intrinsics.ArrayBuffer.Construct(data);
        JintBuffers.DetachKey(buffer) = new JsString(detachKey);
        return buffer;
    }

    public void DetachArrayBuffer(JsValue arrayBuffer)
    {
        if (arrayBuffer is JsArrayBuffer buffer && !JintBuffers.IsShared(buffer))
        {
            JintBuffers.Data(buffer) = null;
        }
    }

    // The data block of a SharedArrayBuffer of Jint is a byte array, which the buffers of the engine instances can share
    public object? GetSharedDataBlock(JsValue sharedArrayBuffer) => sharedArrayBuffer is JsArrayBuffer buffer && JintBuffers.IsShared(buffer) ? JintBuffers.Data(buffer) : null;

    public bool SetSharedDataBlock(JsValue sharedArrayBuffer, object dataBlock)
    {
        if (sharedArrayBuffer is not JsArrayBuffer buffer || !JintBuffers.IsShared(buffer) || dataBlock is not byte[] data || JintBuffers.Data(buffer)?.Length != data.Length)
            return false;

        JintBuffers.Data(buffer) = data;
        return true;
    }

    public JsValue CreateArrayBufferView(JsBufferKind kind, JsValue buffer, int byteOffset, int length)
    {
        var intrinsics = Engine.Intrinsics;
        global::Jint.Native.TypedArray.TypedArrayConstructor? constructor = kind switch
        {
            JsBufferKind.Int8Array => intrinsics.Int8Array,
            JsBufferKind.Uint8Array => intrinsics.Uint8Array,
            JsBufferKind.Uint8ClampedArray => intrinsics.Uint8ClampedArray,
            JsBufferKind.Int16Array => intrinsics.Int16Array,
            JsBufferKind.Uint16Array => intrinsics.Uint16Array,
            JsBufferKind.Int32Array => intrinsics.Int32Array,
            JsBufferKind.Uint32Array => intrinsics.Uint32Array,
            JsBufferKind.BigInt64Array => intrinsics.BigInt64Array,
            JsBufferKind.BigUint64Array => intrinsics.BigUint64Array,
            JsBufferKind.Float16Array => intrinsics.Float16Array,
            JsBufferKind.Float32Array => intrinsics.Float32Array,
            JsBufferKind.Float64Array => intrinsics.Float64Array,
            _ => null,
        };

        if (constructor is not null)
            return constructor.Construct((JsArrayBuffer)buffer, byteOffset, length);

        var dataView = Engine.Global.Get(JsBufferSource.GetName(kind));
        return Engine.Construct(dataView, [buffer, JsNumber.Create(byteOffset), JsNumber.Create(length)]);
    }

    /// <summary>The value is created on first access (e.g. the interface objects of the global object and the operations of the interfaces, most of which are never used).</summary>
    public void DefineLazyDataProperty(JsValue obj, JsValue key, Func<JsValue> factory, bool writable, bool enumerable, bool configurable)
    {
        if (!CanDefineLazyProperty(obj))
        {
            DefineOwnProperty(obj, key, JsPropertyDescriptor<JsValue>.Data(factory(), writable, enumerable, configurable));
            return;
        }

        var flags = (writable ? PropertyFlag.Writable : PropertyFlag.None) | (enumerable ? PropertyFlag.Enumerable : PropertyFlag.None) | (configurable ? PropertyFlag.Configurable : PropertyFlag.None);
        obj.AsObject().FastSetProperty(key, PropertyDescriptor.CreateLazy(factory, flags));
    }

    public void DefineLazyAccessorProperty(JsValue obj, JsValue key, Func<JsValue> getterFactory, Func<JsValue>? setterFactory, bool enumerable, bool configurable)
    {
        if (!CanDefineLazyProperty(obj))
        {
            DefineOwnProperty(obj, key, JsPropertyDescriptor<JsValue>.Accessor(getterFactory(), setterFactory is null ? JsValue.Undefined : setterFactory(), enumerable, configurable));
            return;
        }

        obj.AsObject().FastSetProperty(key, new LazyAccessorPropertyDescriptor(getterFactory, setterFactory, enumerable, configurable));
    }

    /// <summary>
    /// Whether a lazy property can be stored in the properties of an object: not a proxy (e.g. the interface object of an
    /// HTML element, whose properties are the ones of its target), nor a platform object, whose properties have their own
    /// semantics.
    /// </summary>
    private static bool CanDefineLazyProperty(JsValue obj) => obj is not JintPlatformObject && !JintProxies.ProxyType.IsInstanceOfType(obj);

    internal static PropertyDescriptor ToJint(in JsPropertyDescriptor<JsValue> descriptor)
    {
        var flags = descriptor.Flags;
        bool? writable = (flags & JsPropertyFlags.HasWritable) != JsPropertyFlags.None ? descriptor.Writable : null;
        bool? enumerable = (flags & JsPropertyFlags.HasEnumerable) != JsPropertyFlags.None ? descriptor.Enumerable : null;
        bool? configurable = (flags & JsPropertyFlags.HasConfigurable) != JsPropertyFlags.None ? descriptor.Configurable : null;
        if (descriptor.IsAccessorDescriptor)
        {
            var getter = (flags & JsPropertyFlags.HasGet) != JsPropertyFlags.None ? descriptor.Getter ?? JsValue.Undefined : null;
            var setter = (flags & JsPropertyFlags.HasSet) != JsPropertyFlags.None ? descriptor.Setter ?? JsValue.Undefined : null;
            return new GetSetPropertyDescriptor(getter, setter, enumerable, configurable);
        }

        var value = (flags & JsPropertyFlags.HasValue) != JsPropertyFlags.None ? descriptor.Value ?? JsValue.Undefined : null;
        return new PropertyDescriptor(value, writable, enumerable, configurable);
    }

    internal static bool FromJint(PropertyDescriptor property, out JsPropertyDescriptor<JsValue> descriptor)
    {
        if (property == PropertyDescriptor.Undefined)
        {
            descriptor = default;
            return false;
        }

        var flags = JsPropertyFlags.None;
        if (property.EnumerableSet)
        {
            flags |= JsPropertyFlags.HasEnumerable | (property.Enumerable ? JsPropertyFlags.Enumerable : JsPropertyFlags.None);
        }

        if (property.ConfigurableSet)
        {
            flags |= JsPropertyFlags.HasConfigurable | (property.Configurable ? JsPropertyFlags.Configurable : JsPropertyFlags.None);
        }

        if (property.IsAccessorDescriptor())
        {
            flags |= (property.Get is not null ? JsPropertyFlags.HasGet : JsPropertyFlags.None) | (property.Set is not null ? JsPropertyFlags.HasSet : JsPropertyFlags.None);
            descriptor = JsPropertyDescriptor<JsValue>.Create(value: null, property.Get, property.Set, flags);
            return true;
        }

        if (property.WritableSet)
        {
            flags |= JsPropertyFlags.HasWritable | (property.Writable ? JsPropertyFlags.Writable : JsPropertyFlags.None);
        }

        var value = property.Value;
        if (value is not null)
        {
            flags |= JsPropertyFlags.HasValue;
        }

        descriptor = JsPropertyDescriptor<JsValue>.Create(value, getter: null, setter: null, flags);
        return true;
    }
}

/// <summary>An ordinary object whose prototype is set by the bindings.</summary>
internal sealed class JintObject : ObjectInstance
{
    public JintObject(Engine engine, ObjectInstance prototype)
        : base(engine)
    {
        Prototype = prototype;
    }

    public void SetPrototype(ObjectInstance prototype) => Prototype = prototype;
}

/// <summary>
/// The JavaScript object of a .NET platform object. When it has a handler, it is a host exotic object whose internal
/// methods call the handler first (the legacy platform objects, https://webidl.spec.whatwg.org/#es-legacy-platform-objects).
/// </summary>
internal sealed class JintPlatformObject : ObjectInstance
{
    private readonly IJsHostObjectHandler<JsValue>? _handler;

    public JintPlatformObject(Engine engine, object target, IJsHostObjectHandler<JsValue>? handler, ObjectInstance prototype)
        : base(engine)
    {
        Target = target;
        _handler = handler;
        Prototype = prototype;
    }

    public object Target { get; }

    private bool TryGetHandledProperty(JsValue property, out JsPropertyDescriptor<JsValue> descriptor)
    {
        if (_handler is null)
        {
            descriptor = default;
            return false;
        }

        return _handler.TryGetOwnProperty(this, property, out descriptor);
    }

    private JsValue GetValue(in JsPropertyDescriptor<JsValue> descriptor, JsValue receiver)
    {
        if (descriptor.IsAccessorDescriptor)
            return descriptor.Getter is { } getter && getter.IsCallable() ? Engine.Call(getter, receiver, []) : JsValue.Undefined;

        return descriptor.Value ?? JsValue.Undefined;
    }

    public override PropertyDescriptor GetOwnProperty(JsValue property)
        => TryGetHandledProperty(property, out var descriptor) ? JintAdapter.ToJint(descriptor) : base.GetOwnProperty(property);

    public override JsValue Get(JsValue property, JsValue receiver)
        => TryGetHandledProperty(property, out var descriptor) ? GetValue(descriptor, receiver) : base.Get(property, receiver);

    protected override bool TryGetOwnPropertyValue(JsValue property, JsValue receiver, out JsValue value)
    {
        if (TryGetHandledProperty(property, out var descriptor))
        {
            value = GetValue(descriptor, receiver);
            return true;
        }

        return base.TryGetOwnPropertyValue(property, receiver, out value);
    }

    public override bool HasProperty(JsValue property) => TryGetHandledProperty(property, out _) || base.HasProperty(property);

    public override bool Set(JsValue property, JsValue value, JsValue receiver)
    {
        if (_handler is not null && (ReferenceEquals(receiver, this) ? _handler.TrySet(this, property, value, out var result) : _handler.TrySet(this, property, value, receiver, out result)))
            return result;

        return base.Set(property, value, receiver);
    }

    public override bool DefineOwnProperty(JsValue property, PropertyDescriptor desc)
    {
        if (_handler is not null && JintAdapter.FromJint(desc, out var descriptor) && _handler.TryDefineOwnProperty(this, property, descriptor, out var result))
            return result;

        return base.DefineOwnProperty(property, desc);
    }

    public override bool Delete(JsValue property)
    {
        if (_handler is not null && _handler.TryDelete(this, property, out var result))
            return result;

        return base.Delete(property);
    }

    public override List<JsValue> GetOwnPropertyKeys(Types types = Types.String | Types.Symbol)
    {
        var keys = _handler?.GetOwnKeys(this);
        if (keys is null)
            return base.GetOwnPropertyKeys(types);

        var result = new List<JsValue>();
        foreach (var key in keys)
        {
            if (key.IsSymbol() ? (types & Types.Symbol) != Types.Empty : (types & Types.String) != Types.Empty)
            {
                result.Add(key);
            }
        }

        result.AddRange(base.GetOwnPropertyKeys(types));
        return result;
    }

    public override IEnumerable<KeyValuePair<JsValue, PropertyDescriptor>> GetOwnProperties()
    {
        if (_handler is null)
            return base.GetOwnProperties();

        return GetOwnPropertyKeys().Select(key => new KeyValuePair<JsValue, PropertyDescriptor>(key, GetOwnProperty(key)));
    }

    public override object ToObject() => Target;

    public PropertyDescriptor OrdinaryGetOwnProperty(JsValue property) => base.GetOwnProperty(property);

    public bool OrdinaryDefineOwnProperty(JsValue property, PropertyDescriptor desc) => base.DefineOwnProperty(property, desc);

    public bool OrdinaryDelete(JsValue property) => base.Delete(property);

    public List<JsValue> OrdinaryOwnKeys(Types types) => base.GetOwnPropertyKeys(types);
}

/// <summary>Access to the internal slots of the errors of Jint, which are not public.</summary>
internal static class JintErrors
{
    /// <summary>The rendered stack trace ([[Stack]]), which takes precedence over the call stack captured when the error was created.</summary>
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_stack")]
    public static safe extern ref JsValue? Stack(JsError error);
}

/// <summary>
/// Access to the internal slots of the buffers of Jint ([[ArrayBufferData]], [[ViewedArrayBuffer]], [[ByteOffset]]...),
/// which are not public: reading them must not run JavaScript code (e.g. the getters of the prototypes).
/// </summary>
internal static class JintBuffers
{
    public static bool IsShared(JsArrayBuffer buffer) => buffer.GetType().FullName == "Jint.Native.JsSharedArrayBuffer";

    /// <summary>Returns a view of the data block of a buffer (empty when the buffer is detached), clamped to the byte length of the buffer.</summary>
    public static Memory<byte> GetBytes(JsArrayBuffer buffer, int byteOffset, int byteLength)
    {
        var data = buffer.AsArrayBuffer();
        if (data is null)
            return default;

        var bufferLength = IsShared(buffer) ? SharedArrayBufferByteLength(buffer) : data.Length;
        if (byteOffset >= bufferLength)
            return default;

        return data.AsMemory(byteOffset, Math.Min(byteLength, bufferLength - byteOffset));
    }

    public static JsBufferKind GetTypedArrayKind(JsTypedArray value)
    {
        if (value.IsUint8Array())
            return JsBufferKind.Uint8Array;
        if (value.IsInt8Array())
            return JsBufferKind.Int8Array;
        if (value.IsUint8ClampedArray())
            return JsBufferKind.Uint8ClampedArray;
        if (value.IsInt16Array())
            return JsBufferKind.Int16Array;
        if (value.IsUint16Array())
            return JsBufferKind.Uint16Array;
        if (value.IsInt32Array())
            return JsBufferKind.Int32Array;
        if (value.IsUint32Array())
            return JsBufferKind.Uint32Array;
        if (value.IsBigInt64Array())
            return JsBufferKind.BigInt64Array;
        if (value.IsBigUint64Array())
            return JsBufferKind.BigUint64Array;
        if (value.IsFloat16Array())
            return JsBufferKind.Float16Array;
        if (value.IsFloat32Array())
            return JsBufferKind.Float32Array;

        return JsBufferKind.Float64Array;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_arrayBufferData")]
    public static safe extern ref byte[]? Data(JsArrayBuffer buffer);

    /// <summary>[[ArrayBufferDetachKey]]: a buffer whose key is not undefined cannot be transferred or detached by scripts.</summary>
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_arrayBufferDetachKey")]
    public static safe extern ref JsValue DetachKey(JsArrayBuffer buffer);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_viewedArrayBuffer")]
    public static safe extern ref JsArrayBuffer TypedArrayBuffer(JsTypedArray typedArray);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_byteOffset")]
    public static safe extern ref int TypedArrayByteOffset(JsTypedArray typedArray);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_viewedArrayBuffer")]
    public static safe extern ref JsArrayBuffer? DataViewBuffer([UnsafeAccessorType("Jint.Native.JsDataView, Jint")] object dataView);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_byteOffset")]
    public static safe extern ref uint DataViewByteOffset([UnsafeAccessorType("Jint.Native.JsDataView, Jint")] object dataView);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_byteLength")]
    public static safe extern ref uint DataViewByteLength([UnsafeAccessorType("Jint.Native.JsDataView, Jint")] object dataView);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_arrayBufferByteLengthData")]
    private static safe extern ref int SharedArrayBufferByteLengthField([UnsafeAccessorType("Jint.Native.JsSharedArrayBuffer, Jint")] object buffer);

    private static int SharedArrayBufferByteLength(JsArrayBuffer buffer) => SharedArrayBufferByteLengthField(buffer);
}

/// <summary>An accessor property whose functions are created on first access (see <see cref="JintAdapter.DefineLazyAccessorProperty"/>).</summary>
internal sealed class LazyAccessorPropertyDescriptor(Func<JsValue> getterFactory, Func<JsValue>? setterFactory, bool enumerable, bool configurable)
    : PropertyDescriptor(value: null, writable: null, enumerable, configurable)
{
    private Func<JsValue>? _getterFactory = getterFactory;
    private Func<JsValue>? _setterFactory = setterFactory;
    private JsValue? _get;
    private JsValue? _set = setterFactory is null ? JsValue.Undefined : null;

    public override JsValue? Get
    {
        get
        {
            if (_getterFactory is { } factory)
            {
                _get = factory();
                _getterFactory = null;
            }

            return _get;
        }
    }

    public override JsValue? Set
    {
        get
        {
            if (_setterFactory is { } factory)
            {
                _set = factory();
                _setterFactory = null;
            }

            return _set;
        }
    }
}

/// <summary>Accesses the job queue of a Jint engine (https://tc39.es/ecma262/#sec-jobs), which is internal.</summary>
internal static class JintEventLoop
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_EventLoop")]
    [return: UnsafeAccessorType("Jint.Runtime.EventLoop, Jint")]
    public static safe extern object Get(Engine engine);

    // The field of the job queue of an engine (UnsafeAccessorType does not support the fields)
    private static readonly System.Reflection.FieldInfo EventLoopField = typeof(Engine).GetField("_eventLoop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The job queue of the Jint engine was not found");

    /// <summary>
    /// Replaces the job queue of an engine, while a nested microtask queue is entered (see <see cref="JintScriptEngine.EnterNestedMicrotaskQueue"/>):
    /// the engine reads the field each time it queues or runs a job.
    /// </summary>
    public static void Set(Engine engine, object eventLoop) => EventLoopField.SetValue(engine, eventLoop);

    /// <summary>Creates an empty job queue.</summary>
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    [return: UnsafeAccessorType("Jint.Runtime.EventLoop, Jint")]
    public static safe extern object Create();

    /// <summary>Whether no job is pending.</summary>
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_IsEmpty")]
    public static safe extern bool IsEmpty([UnsafeAccessorType("Jint.Runtime.EventLoop, Jint")] object eventLoop);

    /// <summary>Whether the jobs are running (the end of a script does not run them again).</summary>
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_IsRunningJob")]
    public static safe extern bool IsRunningJob([UnsafeAccessorType("Jint.Runtime.EventLoop, Jint")] object eventLoop);
}

/// <summary>The internal types of the built-in objects of Jint, whose methods check the type of their this value (e.g. Boolean.prototype.valueOf).</summary>
internal static class JintBuiltInTypes
{
    public static Type Boolean { get; } = Type.GetType("Jint.Native.Boolean.BooleanInstance, Jint", throwOnError: true)!;

    public static Type Number { get; } = Type.GetType("Jint.Native.Number.NumberInstance, Jint", throwOnError: true)!;

    public static Type BigInt { get; } = Type.GetType("Jint.Native.BigInt.BigIntInstance, Jint", throwOnError: true)!;

    public static Type String { get; } = Type.GetType("Jint.Native.String.StringInstance, Jint", throwOnError: true)!;

    public static Type Symbol { get; } = Type.GetType("Jint.Native.Symbol.SymbolInstance, Jint", throwOnError: true)!;

    public static Type DataView { get; } = Type.GetType("Jint.Native.JsDataView, Jint", throwOnError: true)!;

    public static Type WeakMap { get; } = Type.GetType("Jint.Native.JsWeakMap, Jint", throwOnError: true)!;

    public static Type WeakSet { get; } = Type.GetType("Jint.Native.JsWeakSet, Jint", throwOnError: true)!;
}

/// <summary>Accesses the internal state of the proxies of Jint.</summary>
internal static class JintProxies
{
    public static Type ProxyType { get; } = Type.GetType("Jint.Native.JsProxy, Jint", throwOnError: true)!;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_target")]
    public static safe extern ref ObjectInstance? ProxyTarget([UnsafeAccessorType("Jint.Native.JsProxy, Jint")] object proxy);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_prototype")]
    public static safe extern ref ObjectInstance? ObjectPrototype(ObjectInstance obj);
}
