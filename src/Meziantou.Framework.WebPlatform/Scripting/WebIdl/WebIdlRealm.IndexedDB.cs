using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The operations of IndexedDB on the JavaScript values (https://w3c.github.io/IndexedDB/#key-construct,
// https://w3c.github.io/IndexedDB/#key-path-construct), with the primitives of the engine.
public sealed partial class WebIdlRealm<TAdapter, TValue> : IIndexedDBValueOperations
{
    // Whether a buffer source is detached: the intrinsic getters, captured before the user code can replace them
    private const string IsDetachedScript = """
        (function () {
          'use strict';
          const getter = (o, k) => Object.getOwnPropertyDescriptor(o, k).get;
          const detached = getter(ArrayBuffer.prototype, 'detached');
          const typedArrayBuffer = getter(Object.getPrototypeOf(Uint8Array.prototype), 'buffer');
          const dataViewBuffer = getter(DataView.prototype, 'buffer');
          const isDetached = (buffer) => { try { return Reflect.apply(detached, buffer, []); } catch { return false; } };
          return (value) => {
            try { return isDetached(Reflect.apply(typedArrayBuffer, value, [])); } catch { }
            try { return isDetached(Reflect.apply(dataViewBuffer, value, [])); } catch { }
            return isDetached(value);
          };
        })
        """;

    private TValue? _isDetached;

    IdbKey? IIndexedDBValueOperations.ConvertToKey(object? value, bool multiEntry)
        => Adapter.RunInRealm(EngineRealm, () => multiEntry ? ConvertToMultiEntryKey(ToJsAny(value)) : ConvertToKey(ToJsAny(value), seen: null));

    object? IIndexedDBValueOperations.ConvertToValue(IdbKey key) => Adapter.RunInRealm(EngineRealm, () => KeyToValue(key));

    bool IIndexedDBValueOperations.TryEvaluateKeyPath(object? value, string keyPath, out object? result)
    {
        var (success, evaluated) = Adapter.RunInRealm(EngineRealm, () => EvaluateKeyPath(ToJsAny(value), keyPath));
        result = evaluated;
        return success;
    }

    bool IIndexedDBValueOperations.CanInjectKey(object? value, string keyPath) => Adapter.RunInRealm(EngineRealm, () => CanInjectKey(ToJsAny(value), keyPath));

    void IIndexedDBValueOperations.InjectKey(object? value, IdbKey key, string keyPath)
        => Adapter.RunInRealm(EngineRealm, () =>
        {
            InjectKey(ToJsAny(value), key, keyPath);
            return true;
        });

    AgentSerializedValue IIndexedDBValueOperations.SerializeForStorage(object? value) => (AgentSerializedValue)StructuredSerializeForAgent(value, transferList: null, forStorage: true);

    object? IIndexedDBValueOperations.Deserialize(AgentSerializedValue serialized) => StructuredDeserialize(serialized);

    bool IIndexedDBValueOperations.IsNullOrUndefined(object? value) => value switch
    {
        null or NullValue => true,
        TValue jsValue => IsNullOrUndefined(jsValue),
        _ => false,
    };

    bool IIndexedDBValueOperations.IsPotentiallyValidKey(object? value)
    {
        if (value is string)
            return true;

        if (value is not TValue input)
            return false;

        var adapter = Adapter;
        return adapter.IsNumber(input) || adapter.IsString(input) || (adapter.IsObject(input) && (adapter.IsDate(input) || adapter.IsArray(input) || adapter.TryGetBufferSource(input, out _, out _, out _)));
    }

    object IIndexedDBValueOperations.CreateArray(IReadOnlyList<object?> items)
    {
        var values = new TValue[items.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ToJsAny(items[i]);
        }

        return CreateArray(values);
    }

    // https://webidl.spec.whatwg.org/#js-dictionary: the members in lexicographical order
    IdbGetAllOptions IIndexedDBValueOperations.ConvertGetAllOptions(object? value)
    {
        var input = ToJsAny(value);
        var adapter = Adapter;
        if (IsNullOrUndefined(input))
            return new IdbGetAllOptions();

        if (!adapter.IsObject(input))
            throw CreateTypeError("The value is not a dictionary");

        uint? count = null;
        var countValue = adapter.Get(input, Key("count"));
        if (!adapter.IsUndefined(countValue))
        {
            // [EnforceRange] unsigned long
            var number = adapter.ToNumber(countValue);
            if (double.IsNaN(number) || double.IsInfinity(number))
                throw CreateTypeError("The count is not a finite number");

            number = Math.Truncate(number);
            if (number < 0 || number > uint.MaxValue)
                throw CreateTypeError("The count is out of range");

            count = (uint)number;
        }

        var direction = IdbCursorDirection.Next;
        var directionValue = adapter.Get(input, Key("direction"));
        if (!adapter.IsUndefined(directionValue))
        {
            direction = adapter.ToString(directionValue) switch
            {
                "next" => IdbCursorDirection.Next,
                "nextunique" => IdbCursorDirection.NextUnique,
                "prev" => IdbCursorDirection.Prev,
                "prevunique" => IdbCursorDirection.PrevUnique,
                var other => throw CreateTypeError($"'{other}' is not a valid cursor direction"),
            };
        }

        var queryValue = adapter.Get(input, Key("query"));
        return new IdbGetAllOptions { Count = count, Direction = direction, Query = adapter.IsUndefined(queryValue) ? NullValue.Instance : ToAny(queryValue) };
    }

    // The IDL any values: null is undefined, the other .NET values are converted
    private TValue ToJsAny(object? value) => value switch
    {
        null => Adapter.Undefined,
        TValue jsValue => jsValue,
        _ => ToJs(value),
    };

    // https://w3c.github.io/IndexedDB/#convert-value-to-key
    private IdbKey? ConvertToKey(TValue input, List<TValue>? seen)
    {
        var adapter = Adapter;
        if (seen is not null && seen.Exists(item => ReferenceEquals(item, input)))
            return null;

        if (adapter.IsNumber(input))
        {
            var number = adapter.AsNumber(input);
            return double.IsNaN(number) ? null : IdbKey.FromNumber(number);
        }

        if (adapter.IsString(input))
            return IdbKey.FromString(adapter.AsString(input));

        if (!adapter.IsObject(input))
            return null;

        if (adapter.IsDate(input))
        {
            var time = adapter.AsDate(input);
            return double.IsNaN(time) ? null : IdbKey.FromDate(time);
        }

        if (adapter.TryGetBufferSource(input, out var kind, out var isShared, out var bytes))
        {
            if (bytes.IsEmpty && !isShared && kind != JsBufferKind.SharedArrayBuffer && IsDetached(input))
                return null;

            return IdbKey.FromBinary(bytes.ToArray());
        }

        if (adapter.IsArray(input))
        {
            var length = ToLength(adapter.Get(input, Key("length")));
            seen ??= [];
            seen.Add(input);
            var keys = new List<IdbKey>();
            for (var index = 0L; index < length; index++)
            {
                var indexKey = adapter.FromString(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (!adapter.GetOwnProperty(input, indexKey, out _))
                    return null;

                var entry = adapter.Get(input, indexKey);
                if (ConvertToKey(entry, seen) is not { } key)
                    return null;

                keys.Add(key);
            }

            return IdbKey.FromArray([.. keys]);
        }

        return null;
    }

    // https://w3c.github.io/IndexedDB/#convert-a-value-to-a-multientry-key: the invalid items and the duplicates are ignored
    private IdbKey? ConvertToMultiEntryKey(TValue input)
    {
        var adapter = Adapter;
        if (!adapter.IsArray(input))
            return ConvertToKey(input, seen: null);

        var length = ToLength(adapter.Get(input, Key("length")));
        List<TValue> seen = [input];
        var keys = new List<IdbKey>();
        for (var index = 0L; index < length; index++)
        {
            // The entries that cannot be converted (including the exceptions) are ignored
            IdbKey? key;
            try
            {
                var entry = adapter.Get(input, adapter.FromString(index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                key = ConvertToKey(entry, seen);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                continue;
            }

            if (key is not null && !keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        return IdbKey.FromArray([.. keys]);
    }

    // https://w3c.github.io/IndexedDB/#convert-a-key-to-a-value
    private TValue KeyToValue(IdbKey key) => key.Type switch
    {
        IdbKeyType.Number => Adapter.FromNumber(key.Number),
        IdbKeyType.String => Adapter.FromString(key.String!),
        IdbKeyType.Date => Adapter.CreateDate(key.Number),
        IdbKeyType.Binary => Adapter.CreateArrayBuffer(key.Binary),
        _ => Adapter.CreateArray([.. key.Items!.Select(KeyToValue)]),
    };

    // https://w3c.github.io/IndexedDB/#evaluate-a-key-path-on-a-value (a string key path)
    private (bool Success, object? Value) EvaluateKeyPath(TValue value, string keyPath)
    {
        if (keyPath.Length == 0)
            return (true, value);

        var adapter = Adapter;
        foreach (var identifier in keyPath.Split('.'))
        {
            if (adapter.IsString(value) && identifier == "length")
            {
                value = adapter.FromNumber(adapter.AsString(value).Length);
            }
            else if (adapter.IsArray(value) && identifier == "length")
            {
                value = adapter.FromNumber(ToLength(adapter.Get(value, Key("length"))));
            }
            else if (adapter.IsObject(value) && adapter.TryGetPlatformObject(value, out var target) && target is Blob blob && identifier is "size" or "type" or "name" or "lastModified")
            {
                if (identifier == "size")
                {
                    value = adapter.FromNumber(blob.Size);
                }
                else if (identifier == "type")
                {
                    value = adapter.FromString(blob.Type);
                }
                else if (blob is Dom.FileApi.File file)
                {
                    value = identifier == "name" ? adapter.FromString(file.Name) : adapter.FromNumber(file.LastModified);
                }
                else
                {
                    return (false, null);
                }
            }
            else
            {
                if (!adapter.IsObject(value))
                    return (false, null);

                var key = Key(identifier);
                if (!adapter.GetOwnProperty(value, key, out _))
                    return (false, null);

                value = adapter.Get(value, key);
                if (adapter.IsUndefined(value))
                    return (false, null);
            }
        }

        return (true, value);
    }

    // https://w3c.github.io/IndexedDB/#check-that-a-key-could-be-injected-into-a-value
    private bool CanInjectKey(TValue value, string keyPath)
    {
        var adapter = Adapter;
        var identifiers = keyPath.Split('.');
        for (var i = 0; i < identifiers.Length - 1; i++)
        {
            if (!adapter.IsObject(value))
                return false;

            var key = Key(identifiers[i]);
            if (!adapter.GetOwnProperty(value, key, out _))
                return true;

            value = adapter.Get(value, key);
        }

        return adapter.IsObject(value);
    }

    // https://w3c.github.io/IndexedDB/#inject-a-key-into-a-value-using-a-key-path
    private void InjectKey(TValue value, IdbKey key, string keyPath)
    {
        var adapter = Adapter;
        var identifiers = keyPath.Split('.');
        for (var i = 0; i < identifiers.Length - 1; i++)
        {
            var identifier = Key(identifiers[i]);
            if (!adapter.GetOwnProperty(value, identifier, out _))
            {
                adapter.DefineOwnProperty(value, identifier, JsPropertyDescriptor<TValue>.Data(adapter.CreateObject(ObjectPrototype), writable: true, enumerable: true, configurable: true));
            }

            value = adapter.Get(value, identifier);
        }

        adapter.DefineOwnProperty(value, Key(identifiers[^1]), JsPropertyDescriptor<TValue>.Data(KeyToValue(key), writable: true, enumerable: true, configurable: true));
    }

    private bool IsDetached(TValue value)
    {
        _isDetached ??= Adapter.Call(EvaluateBuiltIn(IsDetachedScript), Adapter.Undefined, []);
        return Adapter.ToBoolean(Adapter.Call(_isDetached, Adapter.Undefined, [value]));
    }

    // https://tc39.es/ecma262/#sec-tolength
    private double ToLength(TValue value)
    {
        var length = Adapter.ToNumber(value);
        if (double.IsNaN(length) || length <= 0)
            return 0;

        return Math.Min(Math.Floor(length), 9007199254740991);
    }
}
