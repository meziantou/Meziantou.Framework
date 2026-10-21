using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The structured serialization of the JavaScript values (https://html.spec.whatwg.org/multipage/structured-data.html),
// implemented in JavaScript with the primitives of the engine. The serialized form is a tree of records (plain objects of
// the realm that serialized the value), which the realm of a destination deserializes as new objects of its own.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    // The internal slots are read with the intrinsic functions, captured when the helper is created (before the user code
    // can replace them). The records: { t: type, v: value, ... }.
    private const string StructuredCloneScript = """
        (function (platformSerialize, platformDeserialize, platformCanTransfer, platformTransfer, dataCloneError, crossOriginIsolated, scriptPlatformObjects, fetchPlatformObjects, cryptoPlatformObjects, platformDeserializeCryptoKey, hasInternalSlot, setErrorStack, SharedArrayBuffer_, platformShareBuffer, platformReceiveBuffer) {
          'use strict';
          const getter = (o, k) => Object.getOwnPropertyDescriptor(o, k).get;
          const getOwnPropertyDescriptorOrNull = (o, k) => Object.getOwnPropertyDescriptor(o, k) ?? null;
          const tryCall = (f, v) => { try { return { ok: true, value: Reflect.apply(f, v, []) }; } catch { return { ok: false }; } };
          const apply = Reflect.apply;
          const booleanValueOf = Boolean.prototype.valueOf;
          const numberValueOf = Number.prototype.valueOf;
          const stringValueOf = String.prototype.valueOf;
          const bigIntValueOf = typeof BigInt === 'function' ? BigInt.prototype.valueOf : null;
          const symbolValueOf = Symbol.prototype.valueOf;
          const dateGetTime = Date.prototype.getTime;
          const regExpSource = getter(RegExp.prototype, 'source');
          const regExpFlags = getter(RegExp.prototype, 'flags');
          const regExpExec = RegExp.prototype.exec;
          const arrayBufferByteLength = getter(ArrayBuffer.prototype, 'byteLength');
          const arrayBufferSlice = ArrayBuffer.prototype.slice;
          const arrayBufferResizable = getter(ArrayBuffer.prototype, 'resizable');
          const arrayBufferMaxByteLength = getter(ArrayBuffer.prototype, 'maxByteLength');
          const arrayBufferDetached = getter(ArrayBuffer.prototype, 'detached');
          const arrayBufferResize = ArrayBuffer.prototype.resize;
          const arrayBufferTransfer = ArrayBuffer.prototype.transfer;
          // The global object has no SharedArrayBuffer property when the agent cluster is not cross-origin isolated: the
          // intrinsic constructor is given by the host
          const sharedArrayBufferByteLength = typeof SharedArrayBuffer_ === 'function' ? getter(SharedArrayBuffer_.prototype, 'byteLength') : null;
          const sharedArrayBufferGrowable = typeof SharedArrayBuffer_ === 'function' ? getter(SharedArrayBuffer_.prototype, 'growable') ?? null : null;
          const sharedArrayBufferMaxByteLength = typeof SharedArrayBuffer_ === 'function' ? getter(SharedArrayBuffer_.prototype, 'maxByteLength') ?? null : null;
          const typedArrayPrototype = Object.getPrototypeOf(Uint8Array.prototype);
          const typedArrayName = getter(typedArrayPrototype, Symbol.toStringTag);
          const typedArrayBuffer = getter(typedArrayPrototype, 'buffer');
          const typedArrayByteOffset = getter(typedArrayPrototype, 'byteOffset');
          const typedArrayLength = getter(typedArrayPrototype, 'length');
          const typedArrayKeys = typedArrayPrototype.keys;
          const typedArraySet = typedArrayPrototype.set;
          const typedArraySlice = typedArrayPrototype.slice;
          const dataViewBuffer = getter(DataView.prototype, 'buffer');
          const dataViewByteOffset = getter(DataView.prototype, 'byteOffset');
          const dataViewByteLength = getter(DataView.prototype, 'byteLength');
          const mapSize = getter(Map.prototype, 'size');
          const mapEntries = Map.prototype.entries;
          const mapSet = Map.prototype.set;
          const setSize = getter(Set.prototype, 'size');
          const setValues = Set.prototype.values;
          const setAdd = Set.prototype.add;
          const weakMapHas = WeakMap.prototype.has;
          const weakSetHas = WeakSet.prototype.has;
          const weakMapGet = WeakMap.prototype.get;
          const errorStack = getOwnPropertyDescriptorOrNull(Error.prototype, 'stack');
          const errorStackGetter = errorStack !== null && typeof errorStack.get === 'function' ? errorStack.get : null;
          const objectToString = Object.prototype.toString;
          const isArray = Array.isArray;
          const keys = Object.keys;
          const hasOwn = Object.prototype.hasOwnProperty;
          const getOwnPropertyDescriptor = Object.getOwnPropertyDescriptor;
          const defineProperty = Object.defineProperty;
          // The items and the fields are defined as data properties: the setters of the prototypes (e.g. a setter of
          // Object.prototype for an index) are not called
          const objectCreate = Object.create;
          const append = (array, item) => { defineProperty(array, array.length, { value: item, writable: true, enumerable: true, configurable: true }); };
          const errorConstructors = { Error, EvalError, RangeError, ReferenceError, SyntaxError, TypeError, URIError };
          const constructors = { Boolean, Number, String, Date, RegExp, ArrayBuffer, DataView, Map, Set, Array, Object, Uint8Array };
          for (const name of ['Int8Array', 'Uint8Array', 'Uint8ClampedArray', 'Int16Array', 'Uint16Array', 'Int32Array', 'Uint32Array', 'BigInt64Array', 'BigUint64Array', 'Float16Array', 'Float32Array', 'Float64Array']) {
            if (typeof globalThis[name] === 'function') constructors[name] = globalThis[name];
          }

          const hasSlot = (f, v) => f !== null && tryCall(f, v).ok;

          // The intrinsic functions that throw when their this value does not have an internal slot (JsInternalSlot): the
          // host tells whether the value has the slot, so that the function is not called when it would throw (a thrown
          // exception is slow, and most cloned objects are ordinary objects)
          const slots = { boolean: 0, number: 1, bigInt: 2, string: 3, symbol: 4, date: 5, arrayBuffer: 6, sharedArrayBuffer: 7, dataView: 8, map: 9, set: 10, weakMap: 11, weakSet: 12 };
          const notOk = { ok: false };
          const tryCallSlot = (slot, f, v) => f === null || (hasInternalSlot !== null && !hasInternalSlot(v, slot)) ? notOk : tryCall(f, v);
          const hasSlotOf = (slot, f, v) => tryCallSlot(slot, f, v).ok;
          const isResizable = (buffer) => hasSlotOf(slots.arrayBuffer, arrayBufferByteLength, buffer) && apply(arrayBufferResizable, buffer, []);

          // Whether a view of a resizable ArrayBuffer tracks the length of its buffer ([[ArrayLength]] or [[ByteLength]] is
          // auto). JavaScript does not expose it: the buffer is resized by one element and back, which is not observable
          // (the view is not out of bounds, and the bytes are restored).
          const isLengthTracking = (buffer, getLength, elementSize) => {
            if (!isResizable(buffer)) return false;
            const byteLength = apply(arrayBufferByteLength, buffer, []);
            const length = getLength();
            if (byteLength + elementSize <= apply(arrayBufferMaxByteLength, buffer, [])) {
              apply(arrayBufferResize, buffer, [byteLength + elementSize]);
              try { return getLength() !== length; } finally { apply(arrayBufferResize, buffer, [byteLength]); }
            }
            if (byteLength < elementSize) return false;
            const bytes = new constructors.Uint8Array(buffer);
            const tail = apply(typedArraySlice, bytes, [byteLength - elementSize]);
            apply(arrayBufferResize, buffer, [byteLength - elementSize]);
            try { const result = tryCall(getLength); return result.ok && result.value === length - 1; }
            finally { apply(arrayBufferResize, buffer, [byteLength]); apply(typedArraySet, new constructors.Uint8Array(buffer), [tail, byteLength - elementSize]); }
          };
          const serializeProperties = (value, record, memory) => {
            record.p = [];
            for (const key of keys(value)) {
              if (apply(hasOwn, value, [key])) append(record.p, [key, serialize(value[key], memory)]);
            }
          };

          // https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializeinternal
          function serialize(value, memory) {
            const type = typeof value;
            if (type === 'symbol') throw dataCloneError('A symbol could not be cloned');
            if ((type !== 'object' && type !== 'function') || value === null) return { t: 'primitive', v: value };
            if (memory.has(value)) return memory.get(value);
            const record = objectCreate(null);
            memory.set(value, record);
            let result;
            if ((result = tryCallSlot(slots.boolean, booleanValueOf, value)).ok) { record.t = 'Boolean'; record.v = result.value; }
            else if ((result = tryCallSlot(slots.number, numberValueOf, value)).ok) { record.t = 'Number'; record.v = result.value; }
            else if ((result = tryCallSlot(slots.bigInt, bigIntValueOf, value)).ok) { record.t = 'BigInt'; record.v = result.value; }
            else if ((result = tryCallSlot(slots.string, stringValueOf, value)).ok) { record.t = 'String'; record.v = result.value; }
            else if (hasSlotOf(slots.symbol, symbolValueOf, value)) throw dataCloneError('A Symbol object could not be cloned');
            else if ((result = tryCallSlot(slots.date, dateGetTime, value)).ok) { record.t = 'Date'; record.v = result.value; }
            else if (value !== RegExp.prototype && hasSlot(regExpFlags, value) && apply(objectToString, value, []) === '[object RegExp]') {
              record.t = 'RegExp'; record.source = apply(regExpSource, value, []); record.flags = apply(regExpFlags, value, []);
            }
            else if (hasSlotOf(slots.arrayBuffer, arrayBufferByteLength, value)) {
              let copy;
              try { copy = apply(arrayBufferSlice, value, [0]); } catch { throw dataCloneError('A detached ArrayBuffer could not be cloned'); }
              record.t = 'ArrayBuffer'; record.v = copy;
              if (apply(arrayBufferResizable, value, [])) record.maxByteLength = apply(arrayBufferMaxByteLength, value, []);
            }
            else if (hasSlotOf(slots.sharedArrayBuffer, sharedArrayBufferByteLength, value)) {
              // https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializeinternal: a SharedArrayBuffer
              // can only be serialized when the current settings object is cross-origin isolated
              if (!crossOriginIsolated) throw dataCloneError('A SharedArrayBuffer could not be cloned: the document is not cross-origin isolated');
              if (forStorage) throw dataCloneError('A SharedArrayBuffer could not be stored');
              // The data block is shared (with the agent cluster of the realm), the length is the current one
              record.t = 'SharedArrayBuffer'; record.v = platformShareBuffer(value); record.byteLength = apply(sharedArrayBufferByteLength, value, []);
              if (sharedArrayBufferGrowable !== null && apply(sharedArrayBufferGrowable, value, [])) record.maxByteLength = apply(sharedArrayBufferMaxByteLength, value, []);
            }
            else if ((result = tryCall(typedArrayName, value)).ok && result.value !== undefined) {
              // An out of bounds view (its buffer was shrunk) throws
              if (!tryCall(typedArrayKeys, value).ok) throw dataCloneError('An out of bounds or detached typed array could not be cloned');
              const buffer = apply(typedArrayBuffer, value, []);
              record.t = 'TypedArray'; record.name = result.value;
              record.buffer = serialize(buffer, memory);
              record.byteOffset = apply(typedArrayByteOffset, value, []); record.length = apply(typedArrayLength, value, []);
              if (isLengthTracking(buffer, () => apply(typedArrayLength, value, []), constructors[record.name].BYTES_PER_ELEMENT)) record.length = undefined;
            }
            else if ((result = tryCallSlot(slots.dataView, dataViewBuffer, value)).ok) {
              const buffer = result.value;
              const byteLength = tryCall(dataViewByteLength, value);
              if (!byteLength.ok) throw dataCloneError('An out of bounds or detached DataView could not be cloned');
              record.t = 'DataView'; record.buffer = serialize(buffer, memory);
              record.byteOffset = apply(dataViewByteOffset, value, []); record.byteLength = byteLength.value;
              if (isLengthTracking(buffer, () => apply(dataViewByteLength, value, []), 1)) record.byteLength = undefined;
            }
            else if (hasSlotOf(slots.map, mapSize, value)) {
              record.t = 'Map'; record.entries = [];
              const entries = [...apply(mapEntries, value, [])];
              for (const [k, v] of entries) append(record.entries, [serialize(k, memory), serialize(v, memory)]);
            }
            else if (hasSlotOf(slots.set, setSize, value)) {
              record.t = 'Set'; record.values = [];
              const values = [...apply(setValues, value, [])];
              for (const v of values) append(record.values, serialize(v, memory));
            }
            else if (apply(objectToString, value, []) === '[object Error]') {
              record.t = 'Error';
              const name = value.name;
              record.name = Object.prototype.hasOwnProperty.call(errorConstructors, name) ? name : 'Error';
              const message = getOwnPropertyDescriptor(value, 'message');
              if (message !== undefined && 'value' in message) record.message = String(message.value);
              // The stack trace ([[Stack]] of https://tc39.es/proposal-error-stack-accessor/): an own data property (e.g. a
              // stack set by the engine or by the code), else the Error.prototype.stack accessor
              const stack = getOwnPropertyDescriptor(value, 'stack');
              if (stack !== undefined) { if ('value' in stack && typeof stack.value === 'string') record.stack = stack.value; }
              else if (errorStackGetter !== null && (result = tryCall(errorStackGetter, value)).ok && typeof result.value === 'string') record.stack = result.value;
              if (apply(hasOwn, value, ['cause'])) record.cause = serialize(value.cause, memory);
            }
            else if (isArray(value)) { record.t = 'Array'; record.length = value.length; serializeProperties(value, record, memory); }
            else if ((result = platformSerialize(value, forStorage)) !== undefined) { record.t = 'Platform'; record.v = result; }
            // The CryptoKey objects (WebCrypto.js) are serializable (https://w3c.github.io/webcrypto/#cryptokey-interface-serialization)
            else if (cryptoPlatformObjects !== null && (result = apply(weakMapGet, cryptoPlatformObjects, [value])) !== undefined) {
              if (result.kind !== 'CryptoKey') throw dataCloneError(`The ${result.kind} object could not be cloned`);
              record.t = 'CryptoKey'; record.v = result.realm.serializeKey(result);
            }
            // The platform objects implemented in JavaScript (Streams.js: the streams, their readers and controllers...;
            // Fetch.js: Request and Response) are not serializable
            else if ((result = apply(weakMapGet, scriptPlatformObjects, [value])) !== undefined) throw dataCloneError(`The ${result.kind} object could not be cloned`);
            else if (fetchPlatformObjects !== null && (result = apply(weakMapGet, fetchPlatformObjects, [value])) !== undefined) throw dataCloneError(`The ${result.kind} object could not be cloned`);
            else if (type === 'function') throw dataCloneError('A function could not be cloned');
            else if (hasSlotOf(slots.weakMap, weakMapHas, value) || hasSlotOf(slots.weakSet, weakSetHas, value)) throw dataCloneError('The object could not be cloned');
            else { record.t = 'Object'; serializeProperties(value, record, memory); }
            return record;
          }

          // https://html.spec.whatwg.org/multipage/structured-data.html#structureddeserialize
          function deserialize(record, memory) {
            if (record.t === 'primitive') return record.v;
            if (memory.has(record)) return memory.get(record);
            let value;
            switch (record.t) {
              case 'Boolean': value = new constructors.Boolean(record.v); break;
              case 'Number': value = new constructors.Number(record.v); break;
              case 'BigInt': value = constructors.Object(record.v); break;
              case 'String': value = new constructors.String(record.v); break;
              case 'Date': value = new constructors.Date(record.v); break;
              case 'RegExp': value = new constructors.RegExp(record.source, record.flags); break;
              case 'ArrayBuffer': {
                const byteLength = apply(arrayBufferByteLength, record.v, []);
                value = 'maxByteLength' in record ? new constructors.ArrayBuffer(byteLength, { maxByteLength: record.maxByteLength }) : new constructors.ArrayBuffer(byteLength);
                new constructors.Uint8Array(value).set(new constructors.Uint8Array(record.v));
                break;
              }
              // A new SharedArrayBuffer object of the realm, which shares the data block (it throws in another agent cluster)
              case 'SharedArrayBuffer': value = platformReceiveBuffer(record.v, record.byteLength, record.maxByteLength); break;
              case 'TypedArray': {
                const buffer = deserialize(record.buffer, memory);
                value = new constructors[record.name](buffer, record.byteOffset, record.length);
                break;
              }
              case 'DataView': {
                const buffer = deserialize(record.buffer, memory);
                value = new constructors.DataView(buffer, record.byteOffset, record.byteLength);
                break;
              }
              case 'Map':
                value = new constructors.Map();
                memory.set(record, value);
                for (const [k, v] of record.entries) apply(mapSet, value, [deserialize(k, memory), deserialize(v, memory)]);
                return value;
              case 'Set':
                value = new constructors.Set();
                memory.set(record, value);
                for (const v of record.values) apply(setAdd, value, [deserialize(v, memory)]);
                return value;
              case 'Error':
                value = new errorConstructors[record.name]();
                if ('message' in record) defineProperty(value, 'message', { value: record.message, writable: true, enumerable: false, configurable: true });
                // The engines without a [[Stack]] internal slot have a stack own property
                if ('stack' in record && !setErrorStack(value, record.stack)) defineProperty(value, 'stack', { value: record.stack, writable: true, enumerable: false, configurable: true });
                if ('cause' in record) {
                  memory.set(record, value);
                  defineProperty(value, 'cause', { value: deserialize(record.cause, memory), writable: true, enumerable: false, configurable: true });
                  return value;
                }
                break;
              case 'Array':
                value = new constructors.Array(record.length);
                memory.set(record, value);
                for (const [k, v] of record.p) defineProperty(value, k, { value: deserialize(v, memory), writable: true, enumerable: true, configurable: true });
                return value;
              case 'Object':
                value = new constructors.Object();
                memory.set(record, value);
                for (const [k, v] of record.p) defineProperty(value, k, { value: deserialize(v, memory), writable: true, enumerable: true, configurable: true });
                return value;
              case 'Platform': value = platformDeserialize(record.v); break;
              case 'CryptoKey': value = platformDeserializeCryptoKey(record.v); break;
            }

            memory.set(record, value);
            return value;
          }

          // https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializewithtransfer: the ArrayBuffer
          // objects and the transferable platform objects (e.g. MessagePort)
          function serializeWithTransfer(value, transferList) {
            const memory = new Map();
            const buffers = [];
            const platformObjects = [];
            for (const transferable of transferList) {
              if (memory.has(transferable)) throw dataCloneError('An object could not be transferred twice');
              if (hasSlotOf(slots.arrayBuffer, arrayBufferByteLength, transferable)) {
                const record = objectCreate(null);
                record.t = 'ArrayBuffer';
                memory.set(transferable, record);
                append(buffers, transferable);
              } else if (platformCanTransfer(transferable, false)) {
                const record = objectCreate(null);
                record.t = 'Platform';
                memory.set(transferable, record);
                append(platformObjects, transferable);
              } else {
                throw dataCloneError('The object could not be transferred');
              }
            }

            const serialized = serialize(value, memory);
            for (const transferable of buffers) {
              if (apply(arrayBufferDetached, transferable, [])) throw dataCloneError('A detached ArrayBuffer could not be transferred');
            }

            // The platform objects are checked after the serialization too (e.g. a detached MessagePort)
            for (const transferable of platformObjects) platformCanTransfer(transferable, true);

            for (const transferable of buffers) {
              // The transferred buffer keeps the data, and the maximum length of a resizable buffer; the transferable is detached
              const record = memory.get(transferable);
              if (apply(arrayBufferResizable, transferable, [])) record.maxByteLength = apply(arrayBufferMaxByteLength, transferable, []);
              record.v = apply(arrayBufferTransfer, transferable, []);
            }

            // The transfer steps of the platform objects (e.g. a MessagePort is detached)
            for (const transferable of platformObjects) memory.get(transferable).v = platformTransfer(transferable);
            return serialized;
          }

          // https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializeforstorage (e.g. the state of the
          // history entries)
          let forStorage = false;
          return {
            serialize: (value, transferList, storage) => {
              forStorage = storage;
              try { return transferList.length === 0 ? serialize(value, new Map()) : serializeWithTransfer(value, transferList); }
              finally { forStorage = false; }
            },
            deserialize: (record) => deserialize(record, new Map()),
          };
        })
        """;

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

            return ToJs(StructuredClone.Deserialize(serializedObject));
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

        var helpers = Adapter.Call(EvaluateBuiltIn(StructuredCloneScript), Adapter.Undefined, [platformSerialize, platformDeserialize, platformCanTransfer, platformTransfer, dataCloneError, Adapter.FromBoolean(GlobalScope.CrossOriginIsolated), scriptPlatformObjects, fetchPlatformObjects, cryptoPlatformObjects, platformDeserializeCryptoKey, hasInternalSlot, setErrorStack, SharedArrayBufferConstructor ?? Adapter.Undefined, platformShareBuffer, platformReceiveBuffer]);
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
