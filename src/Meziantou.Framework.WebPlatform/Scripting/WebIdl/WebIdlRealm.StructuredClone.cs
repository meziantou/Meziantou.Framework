using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The structured serialization of the JavaScript values (https://html.spec.whatwg.org/multipage/structured-data.html),
// implemented in JavaScript with the primitives of the engine. The serialized form is a tree of records (plain objects of
// the realm that serialized the value), which the realm of a destination deserializes as new objects of its own.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    private (TValue Serialize, TValue Deserialize)? _structuredClone;

    /// <summary>
    /// StructuredSerialize (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserialize) in this realm:
    /// <paramref name="value"/> is a value of the engine, a .NET platform object, or null for <c>undefined</c>. Throws a
    /// <c>DataCloneError</c> when the value cannot be serialized. The objects of <paramref name="transferList"/> (values of
    /// the engine) are transferred (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializewithtransfer):
    /// only the <c>ArrayBuffer</c> objects are transferable.
    /// </summary>
    internal object StructuredSerialize(object? value, IReadOnlyList<object>? transferList = null, bool forStorage = false)
    {
        var jsValue = value switch
        {
            null => Adapter.Undefined,
            TValue v when Adapter.IsValue(v) && WebIdlBindings<TAdapter, TValue>.GetInterface(v.GetType()) is null => v,
            _ => ToJs(value),
        };

        var transfer = new TValue[transferList?.Count ?? 0];
        for (var i = 0; i < transfer.Length; i++)
        {
            transfer[i] = TryAsValue(transferList![i], out var v) ? v : ToJs(transferList[i]);
        }

        var (serialize, _) = GetStructuredCloneHelper();
        return new SerializedRecord(Adapter.RunInRealm(EngineRealm, () => Adapter.Call(serialize, Adapter.Undefined, [jsValue, Adapter.CreateArray(transfer), Adapter.FromBoolean(forStorage)])));
    }

    /// <summary>StructuredDeserialize (https://html.spec.whatwg.org/multipage/structured-data.html#structureddeserialize) in this realm.</summary>
    internal TValue StructuredDeserialize(object? serialized)
    {
        TValue record;
        if (serialized is SerializedRecord serializedRecord)
        {
            record = serializedRecord.Value;
        }
        else if (serialized is AgentSerializedValue agentValue)
        {
            // A value serialized by another agent: its records are created in this realm
            record = Adapter.RunInRealm(EngineRealm, () => ImportRecord(agentValue.Root, new Dictionary<object, TValue>(ReferenceEqualityComparer.Instance)));
        }
        else
        {
            return ToJs(StructuredClone.Deserialize(serialized));
        }

        var (_, deserialize) = GetStructuredCloneHelper();
        return Adapter.RunInRealm(EngineRealm, () => Adapter.Call(deserialize, Adapter.Undefined, [record]));
    }

    /// <summary>
    /// StructuredSerializeWithTransfer in this realm for another agent (e.g. a message posted to a worker): the records of
    /// the serialized value are converted to .NET values (<see cref="AgentSerializedValue"/>), which another engine
    /// instance can deserialize on another thread. With <paramref name="forStorage"/>, it is StructuredSerializeForStorage
    /// (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializeforstorage), e.g. an IndexedDB value.
    /// </summary>
    internal object StructuredSerializeForAgent(object? value, IReadOnlyList<object>? transferList = null, bool forStorage = false)
    {
        var record = (SerializedRecord)StructuredSerialize(value, transferList, forStorage);
        return new AgentSerializedValue(ExportRecord(record.Value, new Dictionary<TValue, object?>(ReferenceEqualityComparer.Instance)));
    }

    private object? ExportRecord(TValue value, Dictionary<TValue, object?> memory)
    {
        var adapter = Adapter;
        if (adapter.IsUndefined(value))
            return UndefinedValue.Instance;

        if (adapter.IsNull(value))
            return null;

        if (adapter.IsBoolean(value))
            return adapter.AsBoolean(value);

        if (adapter.IsNumber(value))
            return adapter.AsNumber(value);

        if (adapter.IsString(value))
            return adapter.AsString(value);

        if (adapter.IsBigInt(value))
            return adapter.ToBigInt(value);

        if (!adapter.IsObject(value))
            throw new DomException("The value could not be cloned", "DataCloneError");

        if (memory.TryGetValue(value, out var existing))
            return existing;

        if (Agent.SerializedPlatformObjects.TryGetValue(value, out var platformObject))
        {
            var exported = new ExportedPlatformObject(platformObject);
            memory[value] = exported;
            return exported;
        }

        if (adapter.TryGetBufferSource(value, out var kind, out var isShared, out var bytes))
        {
            // The memory of a SharedArrayBuffer cannot be shared with another engine
            if (isShared || kind != JsBufferKind.ArrayBuffer)
                throw new DomException("A SharedArrayBuffer could not be sent to another agent", "DataCloneError");

            var copy = bytes.ToArray();
            memory[value] = copy;
            return copy;
        }

        if (adapter.IsArray(value))
        {
            var length = (int)adapter.ToNumber(adapter.Get(value, Key("length")));
            var array = new object?[length];
            memory[value] = array;
            for (var i = 0; i < length; i++)
            {
                array[i] = ExportRecord(adapter.Get(value, adapter.FromNumber(i)), memory);
            }

            return array;
        }

        var record = new ExportedRecord();
        memory[value] = record;
        foreach (var key in adapter.OwnKeys(value))
        {
            if (adapter.IsString(key))
            {
                record.Fields.Add(new(adapter.AsString(key), ExportRecord(adapter.Get(value, key), memory)));
            }
        }

        return record;
    }

    private TValue ImportRecord(object? value, Dictionary<object, TValue> memory)
    {
        var adapter = Adapter;
        switch (value)
        {
            case null:
                return adapter.Null;
            case UndefinedValue:
                return adapter.Undefined;
            case bool boolean:
                return adapter.FromBoolean(boolean);
            case double number:
                return adapter.FromNumber(number);
            case string text:
                return adapter.FromString(text);
            case System.Numerics.BigInteger bigInt:
                return adapter.FromBigInt(bigInt);
        }

        if (memory.TryGetValue(value, out var existing))
            return existing;

        switch (value)
        {
            case byte[] bytes:
            {
                var buffer = adapter.CreateArrayBuffer(bytes);
                memory[value] = buffer;
                return buffer;
            }

            case object?[] items:
            {
                var values = new TValue[items.Length];
                for (var i = 0; i < items.Length; i++)
                {
                    values[i] = ImportRecord(items[i], memory);
                }

                var array = adapter.CreateArray(values);
                memory[value] = array;
                return array;
            }

            case ExportedPlatformObject platformObject:
            {
                var handle = CreateObject();
                memory[value] = handle;
                if (platformObject.Value is { } data)
                {
                    Agent.SerializedPlatformObjects.AddOrUpdate(handle, data);
                }

                return handle;
            }

            case ExportedRecord exportedRecord:
            {
                var record = CreateObject();
                memory[value] = record;
                foreach (var (key, field) in exportedRecord.Fields)
                {
                    adapter.Set(record, Key(key), ImportRecord(field, memory));
                }

                return record;
            }

            default:
                throw new DomException("The value could not be deserialized", "DataCloneError");
        }
    }

    private (TValue Serialize, TValue Deserialize) GetStructuredCloneHelper()
    {
        if (_structuredClone is { } helper)
            return helper;

        var adapter = Adapter;
        var platformSerialize = Adapter.RunInRealm(EngineRealm, () => adapter.CreateFunction("platformSerialize", 2, (_, args) =>
        {
            if (Unwrap(args[0]) is not { } target)
                return adapter.Undefined;

            // https://webassembly.github.io/spec/web-api/#serialization: the compiled module is shared by the realms of the agent cluster
            if (target is WebAssemblyModuleObject moduleObject)
            {
                if (adapter.ToBoolean(args[1]))
                    throw adapter.CreateException(ToJsError(new DomException("A WebAssembly.Module could not be stored", "DataCloneError")));

                var moduleHandle = CreateObject();
                Agent.SerializedPlatformObjects.AddOrUpdate(moduleHandle, new SerializedWasmModule(moduleObject, GlobalScope.AgentClusterKey));
                return moduleHandle;
            }

            if (target is not IStructuredSerializable serializable)
                throw adapter.CreateException(ToJsError(new DomException($"The {WebIdlBindings<TAdapter, TValue>.GetInterface(target.GetType())?.Name ?? target.GetType().Name} object could not be cloned", "DataCloneError")));

            // The serialized form is the clone taken now, held by an empty object of the record
            var handle = CreateObject();
            Agent.SerializedPlatformObjects.AddOrUpdate(handle, serializable.CloneForStructuredSerialization());
            return handle;
        }));
        var platformDeserialize = Adapter.RunInRealm(EngineRealm, () => adapter.CreateFunction("platformDeserialize", 1, (_, args) =>
        {
            var serializedObject = Agent.SerializedPlatformObjects.TryGetValue(args[0], out var value) ? value : null;
            if (serializedObject is SerializedWasmModule serializedModule)
            {
                var module = serializedModule.Module;
                if (serializedModule.AgentCluster is null || !ReferenceEquals(serializedModule.AgentCluster, GlobalScope.AgentClusterKey) || !ReferenceEquals(module.Module.Runtime, WebAssemblyRuntime))
                    throw adapter.CreateException(ToJsError(new DomException("A WebAssembly.Module cannot be deserialized in another agent cluster", "DataCloneError")));

                return GetWrapper(new WebAssemblyModuleObject(module.Module, module.BuiltinSets, module.ImportedStringModule));
            }

            // https://fs.spec.whatwg.org/#filesystemhandle-deserialization-steps: the handle is created in this realm
            if (serializedObject is Dom.SerializedFileSystemHandle fileSystemHandle)
                return DeserializeFileSystemHandle(fileSystemHandle);

            // https://html.spec.whatwg.org/multipage/structured-data.html#structureddeserialize: a platform object whose
            // interface is not exposed in the realm (e.g. a Blob in a worklet) cannot be deserialized
            var deserialized = StructuredClone.Deserialize(serializedObject);
            if (deserialized is not null && WebIdlBindings<TAdapter, TValue>.GetInterface(deserialized.GetType()) is { } binding && !IsInterfaceExposed(binding))
                throw adapter.CreateException(ToJsError(new DomException($"The {binding.Name} object cannot be deserialized: its interface is not exposed in the realm", "DataCloneError")));

            return ToJs(deserialized);
        }));
        var platformCanTransfer = Adapter.RunInRealm(EngineRealm, () => adapter.CreateFunction("platformCanTransfer", 2, (_, args) =>
        {
            if (Unwrap(args[0]) is not IStructuredTransferable transferable)
                return adapter.FromBoolean(false);

            try
            {
                transferable.ValidateTransfer(serialized: adapter.ToBoolean(args[1]));
            }
            catch (DomException ex)
            {
                throw adapter.CreateException(ToJsError(ex));
            }

            return adapter.FromBoolean(true);
        }));
        var platformTransfer = Adapter.RunInRealm(EngineRealm, () => adapter.CreateFunction("platformTransfer", 1, (_, args) =>
        {
            // The data of the transferred object is held by an empty object of the record, as the serialized platform objects
            var handle = CreateObject();
            Agent.SerializedPlatformObjects.AddOrUpdate(handle, StructuredClone.Transfer((IStructuredTransferable)Unwrap(args[0])!));
            return handle;
        }));
        var dataCloneError = Adapter.RunInRealm(EngineRealm, () => adapter.CreateFunction("dataCloneError", 1, (_, args) => ToJsError(new DomException(adapter.ToString(args[0]), "DataCloneError"))));

        var scriptPlatformObjects = Agent.StreamInternals ??= EvaluateBuiltIn("new WeakMap()");
        var fetchPlatformObjects = Agent.FetchInternals ??= EvaluateBuiltIn("new WeakMap()");
        var cryptoPlatformObjects = Agent.CryptoInternals ??= EvaluateBuiltIn("new WeakMap()");
        var platformDeserializeCryptoKey = Adapter.RunInRealm(EngineRealm, () => adapter.CreateFunction("platformDeserializeCryptoKey", 1, (_, args) => DeserializeCryptoKey(args[0])));
        var setErrorStack = Adapter.RunInRealm(EngineRealm, () => adapter.CreateFunction("setErrorStack", 2, (_, args) => adapter.FromBoolean(adapter.TrySetErrorStack(args[0], adapter.ToString(args[1])))));

        // null when the adapter cannot tell whether a value has an internal slot
        var hasInternalSlot = Adapter.HasInternalSlot(Adapter.Undefined, JsInternalSlot.BooleanData) is null
            ? Adapter.Null
            : Adapter.RunInRealm(EngineRealm, () => adapter.CreateFunction("hasInternalSlot", 2, (_, args) => adapter.FromBoolean(adapter.HasInternalSlot(args[0], (JsInternalSlot)(int)adapter.ToNumber(args[1])) ?? true)));
        // https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializeinternal: the data block of a
        // SharedArrayBuffer is held by an empty object of the record, as the serialized platform objects
        var platformShareBuffer = Adapter.RunInRealm(EngineRealm, () => adapter.CreateFunction("platformShareBuffer", 1, (_, args) =>
        {
            var handle = CreateObject();
            Agent.SerializedPlatformObjects.AddOrUpdate(handle, new SerializedSharedArrayBuffer(adapter.GetSharedDataBlock(args[0]), args[0], Agent, GlobalScope.AgentClusterKey));
            return handle;
        }));

        // https://html.spec.whatwg.org/multipage/structured-data.html#structureddeserialize
        var platformReceiveBuffer = Adapter.RunInRealm(EngineRealm, () => adapter.CreateFunction("platformReceiveBuffer", 3, (_, args) =>
        {
            if (!Agent.SerializedPlatformObjects.TryGetValue(args[0], out var value) || value is not SerializedSharedArrayBuffer serialized)
                throw adapter.CreateException(ToJsError(new DomException("The SharedArrayBuffer could not be deserialized", "DataCloneError")));

            if (serialized.AgentCluster is null || !ReferenceEquals(serialized.AgentCluster, GlobalScope.AgentClusterKey))
                throw adapter.CreateException(ToJsError(new DomException("A SharedArrayBuffer cannot be deserialized in another agent cluster", "DataCloneError")));

            if (serialized.DataBlock is { } sharedDataBlock && adapter.CreateSharedArrayBuffer(sharedDataBlock) is { } sharedBuffer)
                return sharedBuffer;

            if (serialized.DataBlock is { } dataBlock && SharedArrayBufferConstructor is { } constructor)
            {
                TValue[] arguments = [args[1]];
                if (!adapter.IsUndefined(args[2]))
                {
                    var options = CreateObject();
                    SetMember(options, "maxByteLength", args[2]);
                    arguments = [args[1], options];
                }

                var buffer = adapter.Construct(constructor, arguments, constructor);
                if (adapter.SetSharedDataBlock(buffer, dataBlock))
                    return buffer;
            }

            // The engines that cannot share the data block of a buffer: the buffer itself, in the same engine instance
            if (ReferenceEquals(serialized.Agent, Agent) && TryAsValue(serialized.Value, out var sameBuffer))
                return sameBuffer;

            throw adapter.CreateException(ToJsError(new DomException("A SharedArrayBuffer could not be sent to another agent", "DataCloneError")));
        }));

        var helpers = Adapter.Call(EvaluateBuiltIn(StructuredCloneScript.Source), Adapter.Undefined, [platformSerialize, platformDeserialize, platformCanTransfer, platformTransfer, dataCloneError, Adapter.FromBoolean(GlobalScope.CrossOriginIsolated), scriptPlatformObjects, fetchPlatformObjects, cryptoPlatformObjects, platformDeserializeCryptoKey, hasInternalSlot, setErrorStack, SharedArrayBufferConstructor ?? Adapter.Undefined, platformShareBuffer, platformReceiveBuffer]);
        helper = (Adapter.Get(helpers, Adapter.FromString("serialize")), Adapter.Get(helpers, Adapter.FromString("deserialize")));
        _structuredClone = helper;
        return helper;
    }

    /// <summary>A value serialized by a realm: the record created by the structured clone helper.</summary>
    private sealed class SerializedRecord(TValue value)
    {
        public TValue Value { get; } = value;
    }
}

/// <summary>
/// A value serialized for another agent (<see cref="ScriptEngine.StructuredSerializeForAgent"/>): the records of the
/// structured clone helper as .NET values, so that the realm of another engine instance can deserialize it.
/// </summary>
internal sealed class AgentSerializedValue(object? root)
{
    public object? Root { get; } = root;
}

/// <summary>A record of the structured clone helper: its fields in order.</summary>
internal sealed class ExportedRecord
{
    public List<KeyValuePair<string, object?>> Fields { get; } = [];
}

/// <summary>
/// A serialized <c>SharedArrayBuffer</c>: its data block (shared by the engine instances of the adapter, null when the
/// engine cannot share it), the buffer itself (for the same engine instance) and the agent cluster of the realm that
/// serialized it.
/// </summary>
internal sealed class SerializedSharedArrayBuffer(object? dataBlock, object value, object agent, object? agentCluster)
{
    public object? DataBlock { get; } = dataBlock;

    public object Value { get; } = value;

    public object Agent { get; } = agent;

    public object? AgentCluster { get; } = agentCluster;
}

/// <summary>The serialized form of a platform object (or the data of a transferred object) of a record.</summary>
internal sealed class ExportedPlatformObject(object? value)
{
    public object? Value { get; } = value;
}
