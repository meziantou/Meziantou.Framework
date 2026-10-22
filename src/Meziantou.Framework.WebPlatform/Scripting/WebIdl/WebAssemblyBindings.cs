using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The interfaces of the WebAssembly namespace (https://webassembly.github.io/spec/js-api/#webassembly-namespace), declared
/// with [LegacyNamespace=WebAssembly]: their interface objects are properties of the namespace object (see
/// <see cref="WebIdlRealm{TAdapter, TValue}.GetWebAssemblyNamespace"/>). They are written by hand: their values are converted
/// by the algorithms of the specification (ToWebAssemblyValue, AddressValueToU64...).
/// </summary>
internal static class WebAssemblyBindings<TAdapter, TValue>
    where TAdapter : struct, IJsAdapter<TValue>
    where TValue : class
{
    private const double MaxMemoryPages32 = 65536;
    private const double MaxMemoryPages64 = 281_474_976_710_656; // 2^48
    private const ulong MaxTableSize = 10_000_000;

    // ----------------------------------------------------------------------------------------------------------------
    // Module (https://webassembly.github.io/spec/js-api/#modules)
    // ----------------------------------------------------------------------------------------------------------------

    private static readonly InterfaceBinding<TAdapter, TValue> Module = new()
    {
        Name = "Module",
        ClassString = "WebAssembly.Module",
        ClrType = typeof(WebAssemblyModuleObject),
        Flags = InterfaceBindingFlags.ModuleSource,
        Exposure = ExposureSet.All,
        ConstructorLength = 1,
        Constructor = static (r, a) =>
        {
            if (a.Length < 1)
                throw r.NotEnoughArguments("Module", 1, a.Length);

            var bytes = r.ToWasmBytes(a[0]);
            var options = r.ToWasmCompileOptions(a, 1);

            // HostEnsureCanCompileWasmBytes (https://webassembly.github.io/content-security-policy/js-api/#host-ensure-can-compile-wasm-bytes)
            if (ContentSecurityPolicyEval.IsWasmByteCompilationBlocked(r.GlobalScope))
                throw r.CreateWasmError("CompileError", WebIdlRealm<TAdapter, TValue>.WasmCompilationBlockedMessage);

            var module = r.WasmCall(() => r.WebAssemblyRuntime!.Compile(bytes));
            if (!WebIdlRealm<TAdapter, TValue>.AreValidBuiltinSetNames(options.BuiltinSets))
                throw r.CreateWasmError("CompileError", "The builtin set names are not valid");

            return new WebAssemblyModuleObject(module, options.BuiltinSets, options.ImportedStringModule);
        },
        DefineMembers = static (r, interfaceObject, prototype, instanceTarget) =>
        {
            r.DefineOperation(interfaceObject, "exports", 1, static (r, _, a) =>
            {
                if (a.Length < 1)
                    throw r.NotEnoughArguments("exports", 1, a.Length);

                var module = r.ToInterface<WebAssemblyModuleObject>(a[0], "Module").Module;
                return r.CreateArray([.. module.Exports.Select(export =>
                {
                    // ModuleExportDescriptor: the members in lexicographical order
                    var descriptor = r.CreateObject();
                    r.SetMember(descriptor, "kind", r.Adapter.FromString(export.Type.KindName));
                    r.SetMember(descriptor, "name", r.Adapter.FromString(export.Name));
                    return descriptor;
                })]);
            });
            r.DefineOperation(interfaceObject, "imports", 1, static (r, _, a) =>
            {
                if (a.Length < 1)
                    throw r.NotEnoughArguments("imports", 1, a.Length);

                var module = r.ToInterface<WebAssemblyModuleObject>(a[0], "Module").Module;
                return r.CreateArray([.. module.Imports.Select(import =>
                {
                    var descriptor = r.CreateObject();
                    r.SetMember(descriptor, "kind", r.Adapter.FromString(import.Type.KindName));
                    r.SetMember(descriptor, "module", r.Adapter.FromString(import.Module));
                    r.SetMember(descriptor, "name", r.Adapter.FromString(import.Name));
                    return descriptor;
                })]);
            });
            r.DefineOperation(interfaceObject, "customSections", 2, static (r, _, a) =>
            {
                if (a.Length < 2)
                    throw r.NotEnoughArguments("customSections", 2, a.Length);

                var module = r.ToInterface<WebAssemblyModuleObject>(a[0], "Module").Module;
                var sectionName = r.ToDomString(a[1]);
                var bytes = module.Bytes;
                return r.CreateArray([.. module.Sections.CustomSections
                    .Where(section => string.Equals(section.Name, sectionName, StringComparison.Ordinal))
                    .Select(section => r.FromBuffer(JsBufferKind.ArrayBuffer, bytes.AsMemory(section.Offset, section.Length)))]);
            });
        },
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Instance (https://webassembly.github.io/spec/js-api/#instances)
    // ----------------------------------------------------------------------------------------------------------------

    private static readonly InterfaceBinding<TAdapter, TValue> Instance = new()
    {
        Name = "Instance",
        ClassString = "WebAssembly.Instance",
        ClrType = typeof(WebAssemblyInstanceObject),
        Exposure = ExposureSet.All,
        ConstructorLength = 1,
        Constructor = static (r, a) =>
        {
            if (a.Length < 1)
                throw r.NotEnoughArguments("Instance", 1, a.Length);

            var moduleObject = r.ToInterface<WebAssemblyModuleObject>(a[0], "Module");
            var importObject = r.ToWasmImportObject(a, 1);
            var imports = r.ReadWasmImports(moduleObject, importObject);
            return r.InstantiateWasmModule(moduleObject, imports);
        },
        DefineMembers = static (r, interfaceObject, prototype, instanceTarget) =>
        {
            r.DefineAttribute(instanceTarget, "exports", static (r, t) => (TValue)r.This<WebAssemblyInstanceObject>(t, Instance!).Exports, setter: null);
        },
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Memory (https://webassembly.github.io/spec/js-api/#memories)
    // ----------------------------------------------------------------------------------------------------------------

    private static readonly InterfaceBinding<TAdapter, TValue> Memory = new()
    {
        Name = "Memory",
        ClassString = "WebAssembly.Memory",
        ClrType = typeof(WebAssemblyMemoryObject),
        Exposure = ExposureSet.All,
        ConstructorLength = 1,
        Constructor = static (r, a) =>
        {
            if (a.Length < 1)
                throw r.NotEnoughArguments("Memory", 1, a.Length);

            // MemoryDescriptor: the members are read in lexicographical order, the AddressValue members are converted by the steps
            var descriptor = a[0];
            r.CheckDictionary(descriptor, "MemoryDescriptor");
            var is64 = ReadAddressType(r, descriptor);
            // The AddressValue members are converted when they are read (as the engines do)
            var initialValue = r.GetDictionaryMember(descriptor, "initial");
            if (r.Adapter.IsUndefined(initialValue))
                throw r.MissingRequiredDictionaryMember("MemoryDescriptor", "initial");

            var initial = r.ToWasmAddress(initialValue, is64);
            var maximumValue = r.GetDictionaryMember(descriptor, "maximum");
            ulong? maximum = r.Adapter.IsUndefined(maximumValue) ? null : r.ToWasmAddress(maximumValue, is64);
            var sharedValue = r.GetDictionaryMember(descriptor, "shared");
            var shared = !r.Adapter.IsUndefined(sharedValue) && r.Adapter.ToBoolean(sharedValue);

            var limit = is64 ? MaxMemoryPages64 : MaxMemoryPages32;
            if (initial > limit || maximum > limit || initial > maximum)
                throw r.CreateRangeError("The memory descriptor is not valid");

            if (shared)
            {
                if (maximum is null)
                    throw r.CreateTypeError("A shared memory must have a maximum");

                return CreateSharedMemory(r, initial, maximum.Value, is64);
            }

            var memory = r.WasmCall(() => r.WebAssemblyState.Store.CreateMemory(new WasmMemoryType(initial, maximum, is64, shared: false)));
            var memoryObject = new WebAssemblyMemoryObject(memory);
            r.RegisterWasmObject(memoryObject);
            return memoryObject;
        },
        DefineMembers = static (r, interfaceObject, prototype, instanceTarget) =>
        {
            r.DefineOperation(instanceTarget, "grow", 1, static (r, t, a) =>
            {
                var memoryObject = r.This<WebAssemblyMemoryObject>(t, Memory!);
                if (a.Length < 1)
                    throw r.NotEnoughArguments("grow", 1, a.Length);

                var delta = r.ToWasmAddress(a[0], memoryObject.Is64);
                if (memoryObject.Memory is not { } memory)
                    return GrowSharedMemory(r, memoryObject, delta);

                var previousBuffer = memoryObject.BufferObject;
                var previousSize = r.WasmCall(() => memory.Grow(delta));

                // https://webassembly.github.io/spec/js-api/#grow-the-memory-buffer: the buffer is refreshed, even when the memory did not move
                if (ReferenceEquals(previousBuffer, memoryObject.BufferObject))
                {
                    r.RefreshWasmMemoryBuffer(memoryObject);
                }

                return r.FromWasmAddress(previousSize, memoryObject.Is64);
            });
            r.DefineOperation(instanceTarget, "toFixedLengthBuffer", 0, static (r, t, _) => (TValue)r.This<WebAssemblyMemoryObject>(t, Memory!).BufferObject!);
            r.DefineOperation(instanceTarget, "toResizableBuffer", 0, static (r, t, _) =>
            {
                var memoryObject = r.This<WebAssemblyMemoryObject>(t, Memory!);
                if (memoryObject.Memory is { Type.Maximum: null })
                    throw r.CreateTypeError("The memory has no maximum");

                throw r.CreateTypeError("The resizable buffers of the memories are not supported");
            });
            r.DefineAttribute(instanceTarget, "buffer", static (r, t) => (TValue)r.This<WebAssemblyMemoryObject>(t, Memory!).BufferObject!, setter: null);
        },
    };

    /// <summary>The AddressType member of a descriptor: whether the address type is i64.</summary>
    private static bool ReadAddressType(WebIdlRealm<TAdapter, TValue> r, TValue descriptor)
    {
        var address = r.GetDictionaryMember(descriptor, "address");
        return !r.Adapter.IsUndefined(address) && r.ToEnumerationValue(address, "AddressType", ["i32", "i64"]) == "i64";
    }

    /// <summary>A shared memory: a growable SharedArrayBuffer (the runtimes do not support the shared memories).</summary>
    private static WebAssemblyMemoryObject CreateSharedMemory(WebIdlRealm<TAdapter, TValue> r, ulong initial, ulong maximum, bool is64)
    {
        if (r.SharedArrayBufferConstructor is not { } constructor || maximum * 65536 > (ulong)Array.MaxLength)
            throw r.CreateRangeError("The shared memory cannot be allocated");

        var options = r.CreateObject();
        r.SetMember(options, "maxByteLength", r.Adapter.FromNumber(maximum * 65536));
        var buffer = r.Adapter.RunInRealm(r.EngineRealm, () => r.Adapter.Construct(constructor, [r.Adapter.FromNumber(initial * 65536), options], constructor));
        return new WebAssemblyMemoryObject(buffer, is64);
    }

    private static TValue GrowSharedMemory(WebIdlRealm<TAdapter, TValue> r, WebAssemblyMemoryObject memoryObject, ulong delta)
    {
        var buffer = (TValue)memoryObject.BufferObject!;
        var byteLength = (ulong)r.Adapter.ToNumber(r.Adapter.Get(buffer, r.Adapter.FromString("byteLength")));
        var maxByteLength = (ulong)r.Adapter.ToNumber(r.Adapter.Get(buffer, r.Adapter.FromString("maxByteLength")));
        if (delta > (maxByteLength - byteLength) / 65536)
            throw r.CreateRangeError("The memory cannot grow");

        var grow = r.Adapter.Get(buffer, r.Adapter.FromString("grow"));
        r.Adapter.Call(grow, buffer, [r.Adapter.FromNumber(byteLength + (delta * 65536))]);
        return r.FromWasmAddress(byteLength / 65536, memoryObject.Is64);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Table (https://webassembly.github.io/spec/js-api/#tables)
    // ----------------------------------------------------------------------------------------------------------------

    private static readonly InterfaceBinding<TAdapter, TValue> Table = new()
    {
        Name = "Table",
        ClassString = "WebAssembly.Table",
        ClrType = typeof(WebAssemblyTableObject),
        Exposure = ExposureSet.All,
        ConstructorLength = 1,
        Constructor = static (r, a) =>
        {
            if (a.Length < 1)
                throw r.NotEnoughArguments("Table", 1, a.Length);

            // TableDescriptor: address, element, initial, maximum
            var descriptor = a[0];
            r.CheckDictionary(descriptor, "TableDescriptor");
            var is64 = ReadAddressType(r, descriptor);
            var elementValue = r.GetDictionaryMember(descriptor, "element");
            if (r.Adapter.IsUndefined(elementValue))
                throw r.MissingRequiredDictionaryMember("TableDescriptor", "element");

            var element = r.ToEnumerationValue(elementValue, "TableKind", ["externref", "anyfunc"]);
            var initialValue = r.GetDictionaryMember(descriptor, "initial");
            if (r.Adapter.IsUndefined(initialValue))
                throw r.MissingRequiredDictionaryMember("TableDescriptor", "initial");

            var initial = r.ToWasmAddress(initialValue, is64);
            var maximumValue = r.GetDictionaryMember(descriptor, "maximum");
            ulong? maximum = r.Adapter.IsUndefined(maximumValue) ? null : r.ToWasmAddress(maximumValue, is64);
            var elementType = element == "externref" ? WasmValueType.ExternRef : WasmValueType.FuncRef;
            if (initial > maximum || initial > MaxTableSize)
                throw r.CreateRangeError("The table descriptor is not valid");

            // The value of a table is missing when it is not given (an undefined value is converted, as the engines do)
            var value = a.Length < 2 ? DefaultValue(r, elementType) : r.ToWasmValue(a[1], elementType);
            var table = r.WasmCall(() => r.WebAssemblyState.Store.CreateTable(new WasmTableType(elementType, initial, maximum, is64), value));
            var tableObject = new WebAssemblyTableObject(table);
            r.RegisterWasmObject(tableObject);
            return tableObject;
        },
        DefineMembers = static (r, interfaceObject, prototype, instanceTarget) =>
        {
            r.DefineOperation(instanceTarget, "grow", 1, static (r, t, a) =>
            {
                var table = r.This<WebAssemblyTableObject>(t, Table!).Table;
                if (a.Length < 1)
                    throw r.NotEnoughArguments("grow", 1, a.Length);

                var type = table.Type;
                var delta = r.ToWasmAddress(a[0], type.Is64);
                var value = a.Length < 2 ? DefaultValue(r, type.ElementType) : r.ToWasmValue(a[1], type.ElementType);
                var previousSize = r.WasmCall(() =>
                {
                    if (delta > MaxTableSize || table.Size + delta > MaxTableSize)
                        throw new WasmAllocationException("The table cannot grow");

                    return table.Grow(delta, value);
                });
                return r.FromWasmAddress(previousSize, type.Is64);
            });
            r.DefineOperation(instanceTarget, "get", 1, static (r, t, a) =>
            {
                var table = r.This<WebAssemblyTableObject>(t, Table!).Table;
                if (a.Length < 1)
                    throw r.NotEnoughArguments("get", 1, a.Length);

                var type = table.Type;
                if (type.ElementType.IsV128OrExnRef)
                    throw r.CreateTypeError("The elements of the table cannot be converted to JavaScript values");

                var index = r.ToWasmAddress(a[0], type.Is64);
                return r.ToJsValue(r.WasmCall(() => table.Get(index)));
            });
            r.DefineOperation(instanceTarget, "set", 1, static (r, t, a) =>
            {
                var table = r.This<WebAssemblyTableObject>(t, Table!).Table;
                if (a.Length < 1)
                    throw r.NotEnoughArguments("set", 1, a.Length);

                var type = table.Type;
                if (type.ElementType.IsV128OrExnRef)
                    throw r.CreateTypeError("The elements of the table cannot be converted from JavaScript values");

                var index = r.ToWasmAddress(a[0], type.Is64);
                var value = a.Length < 2 ? DefaultValue(r, type.ElementType) : r.ToWasmValue(a[1], type.ElementType);
                r.WasmCall(() =>
                {
                    table.Set(index, value);
                    return 0;
                });
                return r.Adapter.Undefined;
            });
            r.DefineAttribute(instanceTarget, "length", static (r, t) =>
            {
                var table = r.This<WebAssemblyTableObject>(t, Table!).Table;
                return r.FromWasmAddress(table.Size, table.Type.Is64);
            }, setter: null);
        },
    };

    /// <summary>https://webassembly.github.io/spec/js-api/#defaultvalue: an externref is undefined, a nullable reference is null.</summary>
    private static WasmValue DefaultValue(WebIdlRealm<TAdapter, TValue> r, WasmValueType type)
    {
        if (type.IsReference && type.Heap == WasmHeapKind.Extern && type.Nullable)
            return r.ToWasmValue(r.Adapter.Undefined, type);

        if (type.IsReference && !type.Nullable)
            throw r.CreateTypeError($"The type {type} has no default value");

        return WasmValue.Default(type);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Global (https://webassembly.github.io/spec/js-api/#globals)
    // ----------------------------------------------------------------------------------------------------------------

    private static readonly InterfaceBinding<TAdapter, TValue> Global = new()
    {
        Name = "Global",
        ClassString = "WebAssembly.Global",
        ClrType = typeof(WebAssemblyGlobalObject),
        Exposure = ExposureSet.All,
        ConstructorLength = 1,
        Constructor = static (r, a) =>
        {
            if (a.Length < 1)
                throw r.NotEnoughArguments("Global", 1, a.Length);

            // GlobalDescriptor: mutable, value
            var descriptor = a[0];
            r.CheckDictionary(descriptor, "GlobalDescriptor");
            var mutableValue = r.GetDictionaryMember(descriptor, "mutable");
            var mutable = !r.Adapter.IsUndefined(mutableValue) && r.Adapter.ToBoolean(mutableValue);
            var typeValue = r.GetDictionaryMember(descriptor, "value");
            if (r.Adapter.IsUndefined(typeValue))
                throw r.MissingRequiredDictionaryMember("GlobalDescriptor", "value");

            var valueType = r.ToWasmValueType(typeValue);
            if (valueType.IsV128OrExnRef)
                throw r.CreateTypeError("A global of type v128 cannot be created by JavaScript");

            var value = r.IsMissing(a, 1) ? DefaultValue(r, valueType) : r.ToWasmValue(a[1], valueType);
            var global = r.WasmCall(() => r.WebAssemblyState.Store.CreateGlobal(new WasmGlobalType(valueType, mutable), value));
            var globalObject = new WebAssemblyGlobalObject(global);
            r.RegisterWasmObject(globalObject);
            return globalObject;
        },
        DefineMembers = static (r, interfaceObject, prototype, instanceTarget) =>
        {
            r.DefineOperation(instanceTarget, "valueOf", 0, static (r, t, _) => GetGlobalValue(r, r.This<WebAssemblyGlobalObject>(t, Global!).Global));
            r.DefineAttribute(instanceTarget, "value", static (r, t) => GetGlobalValue(r, r.This<WebAssemblyGlobalObject>(t, Global!).Global), static (r, t, v) =>
            {
                var global = r.This<WebAssemblyGlobalObject>(t, Global!).Global;
                var type = global.Type;
                if (type.ValueType.IsV128OrExnRef)
                    throw r.CreateTypeError("The value of the global cannot be converted from a JavaScript value");

                if (!type.Mutable)
                    throw r.CreateTypeError("The global is immutable");

                var value = r.ToWasmValue(v, type.ValueType);
                r.WasmCall(() =>
                {
                    global.Set(value);
                    return 0;
                });
            });
        },
    };

    /// <summary>https://webassembly.github.io/spec/js-api/#getglobalvalue</summary>
    private static TValue GetGlobalValue(WebIdlRealm<TAdapter, TValue> r, WasmGlobal global)
    {
        if (global.Type.ValueType.IsV128OrExnRef)
            throw r.CreateTypeError("The value of the global cannot be converted to a JavaScript value");

        return r.ToJsValue(global.Get());
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Tag (https://webassembly.github.io/spec/js-api/#tags)
    // ----------------------------------------------------------------------------------------------------------------

    private static readonly InterfaceBinding<TAdapter, TValue> Tag = new()
    {
        Name = "Tag",
        ClassString = "WebAssembly.Tag",
        ClrType = typeof(WebAssemblyTagObject),
        Exposure = ExposureSet.All,
        ConstructorLength = 1,
        Constructor = static (r, a) =>
        {
            if (a.Length < 1)
                throw r.NotEnoughArguments("Tag", 1, a.Length);

            // TagType: parameters
            var type = a[0];
            r.CheckDictionary(type, "TagType");
            var parametersValue = r.GetDictionaryMember(type, "parameters");
            if (r.Adapter.IsUndefined(parametersValue))
                throw r.MissingRequiredDictionaryMember("TagType", "parameters");

            var parameters = r.ToSequence(parametersValue, static (r, v) => r.ToWasmValueType(v)).ToArray();
            var tag = r.WasmCall(() => r.WebAssemblyState.Store.CreateTag(new WasmTagType(parameters)));
            var tagObject = new WebAssemblyTagObject(tag);
            r.RegisterWasmObject(tagObject);
            return tagObject;
        },
        DefineMembers = static (r, interfaceObject, prototype, instanceTarget) =>
        {
        },
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Exception (https://webassembly.github.io/spec/js-api/#exceptions)
    // ----------------------------------------------------------------------------------------------------------------

    private static readonly InterfaceBinding<TAdapter, TValue> Exception = new()
    {
        Name = "Exception",
        ClassString = "WebAssembly.Exception",
        ClrType = typeof(WebAssemblyExceptionObject),
        Exposure = ExposureSet.All,
        ConstructorLength = 2,
        Constructor = static (r, a) =>
        {
            if (a.Length < 2)
                throw r.NotEnoughArguments("Exception", 2, a.Length);

            var tag = r.ToInterface<WebAssemblyTagObject>(a[0], "Tag").Tag;
            var payload = r.ToSequence(a[1], static (_, v) => v).ToArray();

            // ExceptionOptions: traceStack
            var options = r.IsMissing(a, 2) ? r.Adapter.Undefined : a[2];
            r.CheckDictionary(options, "ExceptionOptions");
            var traceStackValue = r.GetDictionaryMember(options, "traceStack");
            var traceStack = !r.Adapter.IsUndefined(traceStackValue) && r.Adapter.ToBoolean(traceStackValue);

            var store = r.WebAssemblyState.Store;
            if (ReferenceEquals(tag, store.JavaScriptExceptionTag))
                throw r.CreateTypeError("An exception cannot be created with the JavaScript exception tag");

            var types = tag.Type.Parameters;
            if (types.Length != payload.Length)
                throw r.CreateTypeError($"The payload has {payload.Length} values instead of {types.Length}");

            var values = new WasmValue[types.Length];
            for (var i = 0; i < types.Length; i++)
            {
                if (types[i].IsV128OrExnRef)
                    throw r.CreateTypeError("The payload cannot have a value of type v128 or exnref");

                values[i] = r.ToWasmValue(payload[i], types[i]);
            }

            var exception = r.WasmCall(() => store.CreateException(tag, values));
            var exceptionObject = new WebAssemblyExceptionObject(exception);
            if (traceStack)
            {
                var stack = r.Adapter.Get(r.EvaluateBuiltIn("new Error()"), r.Adapter.FromString("stack"));
                exceptionObject.Stack = r.Adapter.IsString(stack) ? r.Adapter.AsString(stack) : null;
            }

            r.RegisterWasmObject(exceptionObject);
            return exceptionObject;
        },
        DefineMembers = static (r, interfaceObject, prototype, instanceTarget) =>
        {
            r.DefineOperation(instanceTarget, "getArg", 2, static (r, t, a) =>
            {
                var exceptionObject = r.This<WebAssemblyExceptionObject>(t, Exception!);
                if (a.Length < 2)
                    throw r.NotEnoughArguments("getArg", 2, a.Length);

                var tag = r.ToInterface<WebAssemblyTagObject>(a[0], "Tag").Tag;
                var index = r.ToUnsignedLong(a[1], IntegerConversion.EnforceRange);
                if (!ReferenceEquals(exceptionObject.Tag, tag))
                    throw r.CreateTypeError("The tag of the exception is not the given tag");

                var exception = exceptionObject.Exception;
                if (index >= exception.ArgumentCount)
                    throw r.CreateRangeError("The index is out of the bounds of the payload");

                if (tag.Type.Parameters[index].IsV128OrExnRef)
                    throw r.CreateTypeError("The value cannot be converted to a JavaScript value");

                return r.ToJsValue(exception.GetArgument((int)index));
            });
            r.DefineOperation(instanceTarget, "is", 1, static (r, t, a) =>
            {
                var exceptionObject = r.This<WebAssemblyExceptionObject>(t, Exception!);
                if (a.Length < 1)
                    throw r.NotEnoughArguments("is", 1, a.Length);

                var tag = r.ToInterface<WebAssemblyTagObject>(a[0], "Tag").Tag;
                return r.Adapter.FromBoolean(ReferenceEquals(exceptionObject.Tag, tag));
            });
            r.DefineAttribute(instanceTarget, "stack", static (r, t) => r.This<WebAssemblyExceptionObject>(t, Exception!).Stack is { } stack ? r.Adapter.FromString(stack) : r.Adapter.Undefined, setter: null);
        },
    };

    /// <summary>The interfaces, in the order of the namespace object. It is declared after the interfaces (the static fields are initialized in order).</summary>
    public static InterfaceBinding<TAdapter, TValue>[] Interfaces { get; } = [Module, Instance, Memory, Table, Global, Tag, Exception];
}
