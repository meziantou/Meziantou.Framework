using System.Numerics;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The WebAssembly JavaScript interface (https://webassembly.github.io/spec/js-api/) and its Web API
// (https://webassembly.github.io/spec/web-api/), on the runtime of BrowsingContextOptions.WebAssemblyRuntime. The
// interfaces of the namespace are in WebAssemblyBindings.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    private const uint AllBufferSourceKinds = uint.MaxValue;

    /// <summary>The detach key of the buffers of the memories (https://webassembly.github.io/spec/js-api/#create-a-fixed-length-memory-buffer).</summary>
    private const string MemoryDetachKey = "WebAssembly.Memory";

    private TValue? _webAssemblyNamespace;
    private TValue? _wasmCompileError;
    private TValue? _wasmLinkError;
    private TValue? _wasmRuntimeError;
    private TValue? _wasmObjectFreeze;
    private TValue? _wasmCreateGcObject;
    private TValue? _wasmIterableToList;
    private TValue? _wasmStreaming;

    internal WebAssemblyRuntime? WebAssemblyRuntime => GlobalScope.Options.WebAssemblyRuntime;

    /// <summary>The WebAssembly state of the agent: its store and its object caches.</summary>
    internal WebAssemblyAgentState<TValue> WebAssemblyState => Agent.WebAssemblyState ??= new(WebAssemblyRuntime!.CreateStore(new WebAssemblyHost(Agent.Host)));

    /// <summary>Defines the WebAssembly namespace in the global object ([Exposed=*]).</summary>
    private void DefineWebAssemblyGlobal()
    {
        if (WebAssemblyRuntime is null || !Features.WebAssemblyEnabled)
            return;

        // An engine that has its own implementation (e.g. V8) keeps it
        if (Adapter.HasProperty(Global, Key("WebAssembly")))
            return;

        Adapter.DefineLazyDataProperty(Global, Key("WebAssembly"), GetWebAssemblyNamespace, writable: true, enumerable: false, configurable: true);
    }

    /// <summary>Returns the namespace object of the WebAssembly namespace in this realm (https://webidl.spec.whatwg.org/#namespace-object).</summary>
    internal TValue GetWebAssemblyNamespace()
    {
        if (_webAssemblyNamespace is { } existing)
            return existing;

        return Adapter.RunInRealm(EngineRealm, () =>
        {
            if (_webAssemblyNamespace is { } created)
                return created;

            var namespaceObject = Adapter.CreateObject(ObjectPrototype);
            Adapter.DefineOwnProperty(namespaceObject, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.ToStringTag), JsPropertyDescriptor<TValue>.Data(Key("WebAssembly"), writable: false, enumerable: false, configurable: true));
            _webAssemblyNamespace = namespaceObject;

            DefineOperation(namespaceObject, "validate", 1, static (r, _, a) => r.WasmValidate(a));
            DefineOperation(namespaceObject, "compile", 1, static (r, _, a) => r.WasmCompile(a), returnsPromise: true);
            DefineOperation(namespaceObject, "instantiate", 1, static (r, _, a) => r.WasmInstantiate(a), returnsPromise: true);
            DefineOperation(namespaceObject, "compileStreaming", 1, static (r, _, a) => r.WasmStreaming(a, instantiate: false), returnsPromise: true);
            DefineOperation(namespaceObject, "instantiateStreaming", 1, static (r, _, a) => r.WasmStreaming(a, instantiate: true), returnsPromise: true);
            DefineAttribute(namespaceObject, "JSTag", static (r, _) => r.GetWasmTagObject(r.WebAssemblyState.Store.JavaScriptExceptionTag), setter: null);

            // [LegacyNamespace=WebAssembly]: the interface objects are properties of the namespace object
            foreach (var binding in WebAssemblyBindings<TAdapter, TValue>.Interfaces)
            {
                Adapter.DefineOwnProperty(namespaceObject, Key(binding.Name), JsPropertyDescriptor<TValue>.Data(EnsureInterfaceObject(binding).InterfaceObject, writable: true, enumerable: false, configurable: true));
            }

            // https://webassembly.github.io/spec/js-api/#error-objects: NativeError constructors
            // The prototype of an error created with a NewTarget whose prototype is not an object is the prototype of the
            // constructor of the same name in the realm of the NewTarget (GetFunctionRealm)
            var realmPrototype = Adapter.CreateFunction("realmPrototype", 2, (_, arguments) =>
            {
                var realm = Agent.GetAssociatedRealm(arguments[0]) ?? this;
                var namespaceObject = realm.GetWebAssemblyNamespace();
                return Adapter.Get(Adapter.Get(namespaceObject, arguments[1]), Key("prototype"));
            });
            var createErrors = Adapter.Call(EvaluateBuiltIn("""
                (function (realmPrototype) {
                  'use strict';
                  const { construct, defineProperty, setPrototypeOf } = Reflect;
                  const create = (name) => {
                    const constructor = { [name]: function (message, options) {
                      // OrdinaryCreateFromConstructor(newTarget, "%NativeError.prototype%"): the prototype is read before the message is converted
                      const newTarget = new.target === undefined ? constructor : new.target;
                      let prototype = newTarget.prototype;
                      if ((typeof prototype !== 'object' && typeof prototype !== 'function') || prototype === null) prototype = realmPrototype(newTarget, name);
                      const error = construct(Error, arguments.length === 0 ? [] : arguments.length === 1 ? [message] : [message, options], constructor);
                      setPrototypeOf(error, prototype);
                      return error;
                    } }[name];
                    defineProperty(constructor, 'length', { value: 1, writable: false, enumerable: false, configurable: true });
                    const prototype = Object.create(Error.prototype);
                    defineProperty(prototype, 'constructor', { value: constructor, writable: true, enumerable: false, configurable: true });
                    defineProperty(prototype, 'message', { value: '', writable: true, enumerable: false, configurable: true });
                    defineProperty(prototype, 'name', { value: name, writable: true, enumerable: false, configurable: true });
                    defineProperty(constructor, 'prototype', { value: prototype, writable: false, enumerable: false, configurable: false });
                    setPrototypeOf(constructor, Error);
                    return constructor;
                  };
                  return [create('CompileError'), create('LinkError'), create('RuntimeError')];
                })
                """), Adapter.Undefined, [realmPrototype]);
            _wasmCompileError = Adapter.Get(createErrors, Key("0"));
            _wasmLinkError = Adapter.Get(createErrors, Key("1"));
            _wasmRuntimeError = Adapter.Get(createErrors, Key("2"));
            foreach (var (name, constructor) in (ReadOnlySpan<(string, TValue)>)[("CompileError", _wasmCompileError), ("LinkError", _wasmLinkError), ("RuntimeError", _wasmRuntimeError)])
            {
                Adapter.DefineOwnProperty(namespaceObject, Key(name), JsPropertyDescriptor<TValue>.Data(constructor, writable: true, enumerable: false, configurable: true));
            }

            return namespaceObject;
        });
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Errors
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The message of the CompileError thrown when a content security policy blocks the compilation of a module.</summary>
    internal const string WasmCompilationBlockedMessage = "The compilation of WebAssembly is blocked by the 'script-src' directive of the Content Security Policy ('wasm-unsafe-eval' is not allowed)";

    /// <summary>Returns the exception that throws a CompileError, a LinkError or a RuntimeError of this realm.</summary>
    internal Exception CreateWasmError(string name, string message)
    {
        GetWebAssemblyNamespace();
        var constructor = name switch
        {
            "CompileError" => _wasmCompileError!,
            "LinkError" => _wasmLinkError!,
            _ => _wasmRuntimeError!,
        };

        return Adapter.CreateException(Adapter.RunInRealm(EngineRealm, () => Adapter.Construct(constructor, [Key(message)], constructor)));
    }

    /// <summary>Converts the exceptions of the runtime to the JavaScript exceptions (https://webassembly.github.io/spec/js-api/#error-condition-mappings-to-javascript).</summary>
    private Exception ToWasmJsException(Exception exception) => exception switch
    {
        WasmCompileException => CreateWasmError("CompileError", exception.Message),
        WasmLinkException => CreateWasmError("LinkError", exception.Message),

        // A stack overflow throws the exception of a stack overflow of JavaScript
        WasmTrapException { TrapKind: WasmTrapKind.StackOverflow } => CreateRangeError(exception.Message),
        WasmTrapException => CreateWasmError("RuntimeError", exception.Message),
        WasmThrowException thrown => Adapter.CreateException(ToJsThrownValue(thrown.Exception)),
        WasmTypeMismatchException => CreateTypeError(exception.Message),
        WasmAllocationException => CreateRangeError(exception.Message),
        _ => exception,
    };

    /// <summary>Runs an operation of the runtime: its exceptions are converted to JavaScript exceptions.</summary>
    internal T WasmCall<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is WasmCompileException or WasmLinkException or WasmTrapException or WasmThrowException or WasmTypeMismatchException or WasmAllocationException)
        {
            throw ToWasmJsException(ex);
        }
    }

    /// <summary>
    /// The value thrown in JavaScript for an exception thrown by WebAssembly (https://webassembly.github.io/spec/js-api/#call-an-exported-function):
    /// the JavaScript value of an exception of the JavaScript exception tag, else an Exception object.
    /// </summary>
    private TValue ToJsThrownValue(WasmExceptionReference exception)
    {
        if (ReferenceEquals(exception.Tag, WebAssemblyState.Store.JavaScriptExceptionTag))
            return ToJsValue(exception.GetArgument(0));

        return GetWrapper(GetWasmExceptionObject(exception));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Namespace operations
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Converts an <c>[AllowResizable] AllowSharedBufferSource</c> and returns a copy of its bytes (stableBytes).</summary>
    internal byte[] ToWasmBytes(TValue value) => ToBufferSource(value, AllBufferSourceKinds, allowShared: true, "AllowSharedBufferSource").ToArray();

    /// <summary>https://webassembly.github.io/spec/js-api/#dictdef-webassemblycompileoptions</summary>
    internal (string[] BuiltinSets, string? ImportedStringModule) ToWasmCompileOptions(ReadOnlySpan<TValue> arguments, int index)
    {
        var options = IsMissing(arguments, index) ? Adapter.Undefined : arguments[index];
        CheckDictionary(options, "WebAssemblyCompileOptions");
        var builtins = GetDictionaryMember(options, "builtins");
        var builtinSets = Adapter.IsUndefined(builtins) ? [] : ToSequence(builtins, static (r, v) => r.ToUsvString(v)).ToArray();
        var importedStringConstants = GetDictionaryMember(options, "importedStringConstants");
        var importedStringModule = IsNullOrUndefined(importedStringConstants) ? null : ToUsvString(importedStringConstants);
        return (builtinSets, importedStringModule);
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#validate-builtin-set-names: the builtin sets of the JS String Builtins are not implemented.</summary>
    internal static bool AreValidBuiltinSetNames(string[] builtinSets) => builtinSets.Distinct(StringComparer.Ordinal).Count() == builtinSets.Length;

    /// <summary>https://webassembly.github.io/spec/js-api/#dom-webassembly-validate</summary>
    private TValue WasmValidate(ReadOnlySpan<TValue> arguments)
    {
        if (arguments.Length < 1)
            throw NotEnoughArguments("validate", 1, arguments.Length);

        var bytes = ToWasmBytes(arguments[0]);
        var (builtinSets, _) = ToWasmCompileOptions(arguments, 1);
        return Adapter.FromBoolean(WebAssemblyRuntime!.Validate(bytes) && AreValidBuiltinSetNames(builtinSets));
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#dom-webassembly-compile</summary>
    private TValue WasmCompile(ReadOnlySpan<TValue> arguments)
    {
        if (arguments.Length < 1)
            throw NotEnoughArguments("compile", 1, arguments.Length);

        var bytes = ToWasmBytes(arguments[0]);
        var options = ToWasmCompileOptions(arguments, 1);
        var (promise, resolve, reject) = CreateWasmPromise();
        CompileWasmModuleAsync(bytes, options, module => Adapter.Call(resolve, Adapter.Undefined, [GetWrapper(module)]), error => Adapter.Call(reject, Adapter.Undefined, [error]));
        return promise;
    }

    /// <summary>
    /// https://webassembly.github.io/spec/js-api/#asynchronously-compile-a-webassembly-module: the module is compiled on the
    /// thread of the agent (the compilation is not observable by the scripts, and does not depend on the clock of the event
    /// loop), then a task of the event loop runs the steps.
    /// </summary>
    private void CompileWasmModuleAsync(byte[] bytes, (string[] BuiltinSets, string? ImportedStringModule) options, Action<WebAssemblyModuleObject> onSuccess, Action<TValue> onError)
    {
        WasmModule? module = null;
        Exception? exception = null;

        // HostEnsureCanCompileWasmBytes (https://webassembly.github.io/content-security-policy/js-api/#host-ensure-can-compile-wasm-bytes):
        // the promise is rejected with a CompileError
        if (ContentSecurityPolicyEval.IsWasmByteCompilationBlocked(GlobalScope))
        {
            exception = new WasmCompileException(WasmCompilationBlockedMessage);
        }
        else
        {
            try
            {
                module = WebAssemblyRuntime!.Compile(bytes);
            }
            catch (WasmCompileException ex)
            {
                exception = ex;
            }
        }

        GlobalScope.QueueGlobalTask(() =>
        {
            if (exception is not null)
            {
                onError(ToJsError(CreateWasmError("CompileError", exception.Message)));
            }
            else if (!AreValidBuiltinSetNames(options.BuiltinSets))
            {
                onError(ToJsError(CreateWasmError("CompileError", "The builtin set names are not valid")));
            }
            else
            {
                onSuccess(new WebAssemblyModuleObject(module!, options.BuiltinSets, options.ImportedStringModule));
            }
        });
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#dom-webassembly-instantiate (both overloads)</summary>
    private TValue WasmInstantiate(ReadOnlySpan<TValue> arguments)
    {
        if (arguments.Length < 1)
            throw NotEnoughArguments("instantiate", 1, arguments.Length);

        var importObject = ToWasmImportObject(arguments, 1);
        var (promise, resolve, reject) = CreateWasmPromise();

        // instantiate(Module moduleObject, optional object importObject)
        if (Unwrap(arguments[0]) is WebAssemblyModuleObject moduleObject)
        {
            InstantiateWasmModuleAsync(moduleObject, importObject, instance => Adapter.Call(resolve, Adapter.Undefined, [instance]), error => Adapter.Call(reject, Adapter.Undefined, [error]));
            return promise;
        }

        // instantiate(bytes, optional object importObject, optional WebAssemblyCompileOptions options)
        var bytes = ToWasmBytes(arguments[0]);
        var options = ToWasmCompileOptions(arguments, 2);
        CompileWasmModuleAsync(bytes, options, module =>
        {
            InstantiateWasmModuleAsync(module, importObject, instance =>
            {
                // WebAssemblyInstantiatedSource
                var result = CreateObject();
                SetMember(result, "module", GetWrapper(module));
                SetMember(result, "instance", instance);
                Adapter.Call(resolve, Adapter.Undefined, [result]);
            }, error => Adapter.Call(reject, Adapter.Undefined, [error]));
        }, error => Adapter.Call(reject, Adapter.Undefined, [error]));
        return promise;
    }

    /// <summary>Converts the <c>optional object importObject</c> argument (null when it is missing).</summary>
    internal TValue? ToWasmImportObject(ReadOnlySpan<TValue> arguments, int index)
    {
        if (IsMissing(arguments, index) || Adapter.IsUndefined(arguments[index]))
            return null;

        if (!Adapter.IsObject(arguments[index]))
            throw CreateTypeError("The import object is not an object");

        return arguments[index];
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#asynchronously-instantiate-a-webassembly-module</summary>
    private void InstantiateWasmModuleAsync(WebAssemblyModuleObject moduleObject, TValue? importObject, Action<TValue> onSuccess, Action<TValue> onError)
    {
        IReadOnlyList<WasmExtern> imports;
        try
        {
            imports = ReadWasmImports(moduleObject, importObject);
        }
        catch (Exception ex)
        {
            onError(ToJsError(ex));
            return;
        }

        GlobalScope.QueueGlobalTask(() =>
        {
            TValue instance;
            try
            {
                instance = Run(() => GetWrapper(InstantiateWasmModule(moduleObject, imports)));
            }
            catch (Exception ex) when (Adapter.TryGetThrownValue(ex, out TValue? _) || ex is WasmCompileException or WasmLinkException or WasmTrapException or WasmThrowException or WasmTypeMismatchException or WasmAllocationException)
            {
                onError(ToJsError(ToWasmJsException(ex)));
                return;
            }

            onSuccess(instance);
        });
    }

    /// <summary>Creates a promise of this realm, and its resolving functions.</summary>
    private (TValue Promise, TValue Resolve, TValue Reject) CreateWasmPromise()
    {
        _createPromiseCapability ??= EvaluateBuiltIn("(function () { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return [promise, resolve, reject]; })");
        var capability = Adapter.Call(_createPromiseCapability, Adapter.Undefined, []);
        return (Adapter.Get(capability, Key("0")), Adapter.Get(capability, Key("1")), Adapter.Get(capability, Key("2")));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Instantiation
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://webassembly.github.io/spec/js-api/#read-the-imports</summary>
    internal IReadOnlyList<WasmExtern> ReadWasmImports(WebAssemblyModuleObject moduleObject, TValue? importObject)
    {
        var moduleImports = moduleObject.Module.Imports;
        if (moduleImports.Count > 0 && importObject is null)
            throw CreateTypeError("The module has imports, but no import object was given");

        var state = WebAssemblyState;
        var imports = new List<WasmExtern>(moduleImports.Count);
        var functionIndex = 0;
        foreach (var import in moduleImports)
        {
            var o = Adapter.Get(importObject!, Key(import.Module));
            if (!Adapter.IsObject(o))
                throw CreateTypeError($"The import object has no object for the module '{import.Module}'");

            var v = Adapter.Get(o, Key(import.Name));
            switch (import.Type)
            {
                case WasmFunctionType functionType:
                    if (!Adapter.IsCallable(v))
                        throw CreateWasmError("LinkError", $"The import '{import.Module}.{import.Name}' is not a function");

                    if (!state.FunctionAddresses.TryGetValue(v, out var function))
                    {
                        function = CreateWasmHostFunction(v, functionType);
                        state.FunctionNames.AddOrUpdate(function, functionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                    functionIndex++;
                    imports.Add(function);
                    break;

                case WasmGlobalType globalType:
                    if (Unwrap(v) is WebAssemblyGlobalObject globalObject)
                    {
                        imports.Add(globalObject.Global);
                        break;
                    }

                    var valueType = globalType.ValueType;
                    if ((valueType.Kind == WasmValueKind.I64 && !Adapter.IsBigInt(v)) ||
                        (valueType.Kind is WasmValueKind.I32 or WasmValueKind.F32 or WasmValueKind.F64 && !Adapter.IsNumber(v)) ||
                        valueType.Kind == WasmValueKind.V128)
                    {
                        throw CreateWasmError("LinkError", $"The import '{import.Module}.{import.Name}' is not a valid global value");
                    }

                    WasmValue value;
                    try
                    {
                        value = ToWasmValue(v, valueType);
                    }
                    catch (Exception ex) when (Adapter.TryGetThrownValue(ex, out var thrown) && IsTypeError(thrown))
                    {
                        throw CreateWasmError("LinkError", $"The import '{import.Module}.{import.Name}' is not a valid global value");
                    }

                    imports.Add(WasmCall(() => state.Store.CreateGlobal(new WasmGlobalType(valueType, mutable: false) { Native = globalType.Mutable ? null : globalType.Native }, value)));
                    break;

                case WasmMemoryType:
                    if (Unwrap(v) is not WebAssemblyMemoryObject { Memory: { } memory })
                        throw CreateWasmError("LinkError", $"The import '{import.Module}.{import.Name}' is not a WebAssembly.Memory");

                    imports.Add(memory);
                    break;

                case WasmTableType:
                    if (Unwrap(v) is not WebAssemblyTableObject tableObject)
                        throw CreateWasmError("LinkError", $"The import '{import.Module}.{import.Name}' is not a WebAssembly.Table");

                    imports.Add(tableObject.Table);
                    break;

                default:
                    if (Unwrap(v) is not WebAssemblyTagObject tagObject)
                        throw CreateWasmError("LinkError", $"The import '{import.Module}.{import.Name}' is not a WebAssembly.Tag");

                    imports.Add(tagObject.Tag);
                    break;
            }
        }

        return imports;
    }

    private bool IsTypeError(TValue value)
    {
        var typeError = Adapter.Get(Global, Key("TypeError"));
        return Adapter.IsObject(value) && Adapter.IsObject(typeError) && Adapter.ToBoolean(Adapter.Call(_wasmIsInstance ??= EvaluateBuiltIn("(function (value, constructor) { return value instanceof constructor; })"), Adapter.Undefined, [value, typeError]));
    }

    private TValue? _wasmIsInstance;

    /// <summary>https://webassembly.github.io/spec/js-api/#instantiate-the-core-of-a-webassembly-module and https://webassembly.github.io/spec/js-api/#initialize-an-instance-object</summary>
    internal WebAssemblyInstanceObject InstantiateWasmModule(WebAssemblyModuleObject moduleObject, IReadOnlyList<WasmExtern> imports)
    {
        var instance = WasmCall(() => WebAssemblyState.Store.Instantiate(moduleObject.Module, imports));
        return new WebAssemblyInstanceObject(instance, CreateWasmExportsObject(moduleObject.Module, instance));
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#create-an-exports-object</summary>
    private TValue CreateWasmExportsObject(WasmModule module, WasmInstance instance)
    {
        var state = WebAssemblyState;
        var exportsObject = Adapter.RunInRealm(EngineRealm, () => Adapter.CreateObject(Adapter.Null));
        foreach (var (name, value) in instance.Exports)
        {
            TValue exportValue;
            switch (value)
            {
                case WasmFunction function:
                    if (module.Sections.GetExportedFunctionIndex(name) is { } index)
                    {
                        state.FunctionNames.TryAdd(function, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                    exportValue = GetWasmExportedFunction(function);
                    break;

                default:
                    exportValue = GetWasmExternObject(value);
                    break;
            }

            Adapter.DefineOwnProperty(exportsObject, Key(name), JsPropertyDescriptor<TValue>.Data(exportValue, writable: true, enumerable: true, configurable: true));
        }

        _wasmObjectFreeze ??= EvaluateBuiltIn("Object.freeze");
        Adapter.Call(_wasmObjectFreeze, Adapter.Undefined, [exportsObject]);
        return exportsObject;
    }

    /// <summary>Returns the JavaScript object of a memory, a table, a global or a tag (the object caches).</summary>
    internal TValue GetWasmExternObject(WasmExtern value)
    {
        var state = WebAssemblyState;
        var target = state.ExternObjects.GetValue(value, static item => item switch
        {
            WasmMemory memory => new WebAssemblyMemoryObject(memory),
            WasmTable table => new WebAssemblyTableObject(table),
            WasmGlobal global => new WebAssemblyGlobalObject(global),
            WasmTag tag => new WebAssemblyTagObject(tag),
            _ => throw new InvalidOperationException("A function is not an extern object"),
        });

        if (target is WebAssemblyMemoryObject memoryObject)
        {
            InitializeWasmMemoryObject(memoryObject);
        }

        return GetWrapper(target);
    }

    /// <summary>Returns the Tag object of a tag.</summary>
    internal TValue GetWasmTagObject(WasmTag tag) => GetWasmExternObject(tag);

    /// <summary>Returns the platform object of an exception (https://webassembly.github.io/spec/js-api/#create-an-exception-object).</summary>
    internal WebAssemblyExceptionObject GetWasmExceptionObject(WasmExceptionReference exception) => WebAssemblyState.ExceptionObjects.GetValue(exception, static e => new WebAssemblyExceptionObject(e));

    /// <summary>Registers the platform object of a memory, a table, a global, a tag or an exception created by its constructor (the object caches).</summary>
    internal void RegisterWasmObject(object platformObject)
    {
        var state = WebAssemblyState;
        switch (platformObject)
        {
            case WebAssemblyMemoryObject { Memory: { } memory } memoryObject:
                state.ExternObjects.AddOrUpdate(memory, memoryObject);
                InitializeWasmMemoryObject(memoryObject);
                break;
            case WebAssemblyTableObject tableObject:
                state.ExternObjects.AddOrUpdate(tableObject.Table, tableObject);
                break;
            case WebAssemblyGlobalObject globalObject:
                state.ExternObjects.AddOrUpdate(globalObject.Global, globalObject);
                break;
            case WebAssemblyTagObject tagObject:
                state.ExternObjects.AddOrUpdate(tagObject.Tag, tagObject);
                break;
            case WebAssemblyExceptionObject exceptionObject:
                state.ExceptionObjects.AddOrUpdate(exceptionObject.Exception, exceptionObject);
                break;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Memories
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://webassembly.github.io/spec/js-api/#initialize-a-memory-object: the buffer of the memory, refreshed when the memory grows.</summary>
    private void InitializeWasmMemoryObject(WebAssemblyMemoryObject memoryObject)
    {
        if (memoryObject.BufferObject is not null || memoryObject.Memory is not { } memory)
            return;

        memoryObject.Realm = this;
        memoryObject.BufferObject = CreateWasmMemoryBuffer(memory.Data);
        memory.DataChanged = _ => RefreshWasmMemoryBuffer(memoryObject);
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#create-a-fixed-length-memory-buffer</summary>
    private TValue CreateWasmMemoryBuffer(byte[] data)
    {
        return Adapter.RunInRealm(EngineRealm, () => Adapter.CreateArrayBufferWithData(data, MemoryDetachKey))
            ?? throw new NotSupportedException("The script engine cannot create an ArrayBuffer of the bytes of a memory");
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#refresh-the-memory-buffer: the buffer is detached and replaced.</summary>
    internal void RefreshWasmMemoryBuffer(WebAssemblyMemoryObject memoryObject)
    {
        if (memoryObject.BufferObject is TValue buffer)
        {
            Adapter.DetachArrayBuffer(buffer);
        }

        memoryObject.BufferObject = CreateWasmMemoryBuffer(memoryObject.Memory!.Data);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Exported Functions and host functions
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://webassembly.github.io/spec/js-api/#a-new-exported-function</summary>
    internal TValue GetWasmExportedFunction(WasmFunction function)
    {
        var state = WebAssemblyState;
        if (state.ExportedFunctions.TryGetValue(function, out var existing))
            return (TValue)existing;

        var name = state.FunctionNames.TryGetValue(function, out var index) ? index : "0";
        var exportedFunction = Adapter.RunInRealm(EngineRealm, () => Adapter.CreateFunction(name, function.Type.Parameters.Length, (_, arguments) => CallWasmExportedFunction(function, arguments)));
        state.ExportedFunctions.AddOrUpdate(function, exportedFunction);
        state.FunctionAddresses.AddOrUpdate(exportedFunction, function);
        return exportedFunction;
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#call-an-exported-function</summary>
    private TValue CallWasmExportedFunction(WasmFunction function, ReadOnlySpan<TValue> arguments)
    {
        var type = function.Type;
        if (type.HasV128OrExnRef)
            throw CreateTypeError("The function has a parameter or a result of type v128 or exnref");

        var parameters = type.Parameters;
        var wasmArguments = new WasmValue[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            wasmArguments[i] = ToWasmValue(i < arguments.Length ? arguments[i] : Adapter.Undefined, parameters[i]);
        }

        var results = new WasmValue[type.Results.Length];
        WasmCall(() =>
        {
            function.Invoke(wasmArguments, results);
            return 0;
        });

        return results.Length switch
        {
            0 => Adapter.Undefined,
            1 => ToJsValue(results[0]),
            _ => CreateArray([.. results.Select(ToJsValue)]),
        };
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#create-a-host-function</summary>
    private WasmFunction CreateWasmHostFunction(TValue function, WasmFunctionType type)
    {
        var store = WebAssemblyState.Store;
        return WasmCall(() => store.CreateHostFunction(type, function, (arguments, results) => RunWasmHostFunction(function, type, arguments, results)));
    }

    /// <summary>
    /// https://webassembly.github.io/spec/js-api/#run-a-host-function: an exception thrown by the function is thrown in
    /// WebAssembly, as an exception of the JavaScript exception tag (or as the exception of an Exception object).
    /// </summary>
    private void RunWasmHostFunction(TValue function, WasmFunctionType type, ReadOnlySpan<WasmValue> arguments, Span<WasmValue> results)
    {
        var callbackRealm = Agent.GetAssociatedRealm(function) ?? this;
        TValue value;
        try
        {
            if (type.HasV128OrExnRef)
                throw CreateTypeError("The function has a parameter or a result of type v128 or exnref");

            var jsArguments = new TValue[arguments.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                jsArguments[i] = ToJsValue(arguments[i]);
            }

            var ret = InvokeWasmHostFunction(callbackRealm, function, jsArguments);
            var resultTypes = type.Results;
            if (resultTypes.Length == 1)
            {
                results[0] = ToWasmValue(ret, resultTypes[0]);
            }
            else if (resultTypes.Length > 1)
            {
                // The iterable of the results
                var method = Adapter.IsObject(ret) ? Adapter.Get(ret, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.Iterator)) : Adapter.Undefined;
                if (!Adapter.IsCallable(method))
                    throw CreateTypeError("The results of the function are not iterable");

                _wasmIterableToList ??= EvaluateBuiltIn("(function (iterable, method) { const result = []; for (const value of { [Symbol.iterator]() { return method.call(iterable); } }) result.push(value); return result; })");
                var list = Adapter.Call(_wasmIterableToList, Adapter.Undefined, [ret, method]);
                var length = (int)Adapter.ToNumber(Adapter.Get(list, Key("length")));
                if (length != resultTypes.Length)
                    throw CreateTypeError($"The function returned {length} values instead of {resultTypes.Length}");

                for (var i = 0; i < resultTypes.Length; i++)
                {
                    results[i] = ToWasmValue(Adapter.Get(list, Key(i.ToString(System.Globalization.CultureInfo.InvariantCulture))), resultTypes[i]);
                }
            }

            return;
        }
        catch (Exception ex) when (Adapter.TryGetThrownValue(ex, out var thrown))
        {
            value = thrown;
        }

        // The exception is thrown in WebAssembly
        var store = WebAssemblyState.Store;
        var exception = Unwrap(value) is WebAssemblyExceptionObject exceptionObject
            ? exceptionObject.Exception
            : store.CreateException(store.JavaScriptExceptionTag, [WasmValue.FromReference(GetWasmHostReference(value))]);
        throw new WasmThrowException(exception);
    }

    /// <summary>Calls the JavaScript function of a host function (prepare to run script and to run a callback).</summary>
    private TValue InvokeWasmHostFunction(WebIdlRealm<TAdapter, TValue> callbackRealm, TValue function, TValue[] arguments)
    {
        if (Agent.Host.IsScriptRunning)
            return Adapter.Call(function, Adapter.Undefined, arguments);

        // A start function called by an instantiation of a task (WebAssembly.instantiate)
        return callbackRealm.Run(() => InvokeCallback(function, Adapter.Undefined, arguments, CallbackBehavior.RunWhenInactive));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Values
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The host reference of a JavaScript value: the same reference for the same object (the host value cache).</summary>
    private WasmHostReference GetWasmHostReference(TValue value)
    {
        if (!Adapter.IsObject(value))
            return new WasmHostReference(value);

        return WebAssemblyState.HostReferences.GetValue(value, static v => new WasmHostReference(v));
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#tojsvalue</summary>
    internal TValue ToJsValue(WasmValue value)
    {
        switch (value.Kind)
        {
            case WasmValueKind.I32:
                return Adapter.FromNumber(value.I32);
            case WasmValueKind.I64:
                return Adapter.FromBigInt(value.I64);
            case WasmValueKind.F32:
                return Adapter.FromNumber(value.F32);
            case WasmValueKind.F64:
                return Adapter.FromNumber(value.F64);
            case WasmValueKind.V128:
                throw CreateTypeError("A v128 value cannot be converted to a JavaScript value");
        }

        return value.Reference switch
        {
            null => Adapter.Null,
            WasmI31Reference i31 => Adapter.FromNumber(i31.Value),
            WasmFunction function => GetWasmExportedFunction(function),
            WasmHostReference host => (TValue)host.Value,
            WasmGcObject gcObject => GetWasmExportedGcObject(gcObject),
            _ => throw CreateTypeError("An exnref value cannot be converted to a JavaScript value"),
        };
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#towebassemblyvalue</summary>
    internal WasmValue ToWasmValue(TValue value, WasmValueType type)
    {
        switch (type.Kind)
        {
            case WasmValueKind.I64:
                // ToBigInt64
                var bigint = Adapter.ToBigInt(value) & ulong.MaxValue;
                return WasmValue.FromI64(unchecked((long)(ulong)bigint));
            case WasmValueKind.I32:
                return WasmValue.FromI32(ToLong(value));
            case WasmValueKind.F32:
                return WasmValue.FromF32((float)Adapter.ToNumber(value));
            case WasmValueKind.F64:
                return WasmValue.FromF64(Adapter.ToNumber(value));
            case WasmValueKind.V128:
                throw CreateTypeError("A JavaScript value cannot be converted to a v128 value");
        }

        if (type.IsV128OrExnRef)
            throw CreateTypeError("A JavaScript value cannot be converted to an exnref value");

        object? reference;
        if (Adapter.IsNull(value))
        {
            if (!type.Nullable)
                throw CreateTypeError("The value cannot be null");

            return WasmValue.Null;
        }

        var state = WebAssemblyState;
        if (type.IsExternReference)
        {
            // ref.extern of the conversion to (ref any)
            if (type.Heap == WasmHeapKind.NoExtern)
                throw CreateTypeError("The value is not of type (ref noextern)");

            return WasmValue.FromReference(ToWasmAnyReference(value));
        }

        if (state.FunctionAddresses.TryGetValue(value, out var function) && type.IsFunctionReference)
        {
            reference = function;
        }
        else
        {
            reference = ToWasmAnyReference(value);
        }

        // match_valtype(actualtype, type): the concrete types are checked by the runtime
        var matches = reference switch
        {
            WasmFunction => type.Heap is WasmHeapKind.Func or WasmHeapKind.ConcreteFunc,
            WasmI31Reference => type.Heap is WasmHeapKind.Any or WasmHeapKind.Eq or WasmHeapKind.I31,
            WasmGcObject gcObject => type.Heap is WasmHeapKind.Any or WasmHeapKind.Eq || (gcObject.IsArray ? type.Heap is WasmHeapKind.Array or WasmHeapKind.ConcreteArray : type.Heap is WasmHeapKind.Struct or WasmHeapKind.ConcreteStruct),
            _ => type.Heap == WasmHeapKind.Any,
        };

        if (!matches)
            throw CreateTypeError($"The value is not of type {type}");

        return WasmValue.FromReference(reference);
    }

    /// <summary>The conversion of a JavaScript value (not null) to a reference of type <c>(ref any)</c>: an i31 reference, a struct or an array, or a host value.</summary>
    private object ToWasmAnyReference(TValue value)
    {
        if (Adapter.IsNumber(value))
        {
            var number = Adapter.AsNumber(value);
            if (number == Math.Truncate(number) && number >= -(1 << 30) && number < (1 << 30))
                return new WasmI31Reference((int)number);
        }

        if (WebAssemblyState.GcObjectAddresses.TryGetValue(value, out var gcObject))
            return gcObject;

        return GetWasmHostReference(value);
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#create-a-new-exported-gc-object: an exotic object without properties.</summary>
    private TValue GetWasmExportedGcObject(WasmGcObject gcObject)
    {
        var state = WebAssemblyState;
        if (state.GcObjects.TryGetValue(gcObject, out var existing))
            return (TValue)existing;

        _wasmCreateGcObject ??= EvaluateBuiltIn("""
            (function () {
              'use strict';
              const handler = {
                getPrototypeOf() { return null; },
                setPrototypeOf() { return false; },
                isExtensible() { return false; },
                preventExtensions() { return false; },
                getOwnPropertyDescriptor() { return undefined; },
                defineProperty() { return false; },
                has() { return false; },
                get() { return undefined; },
                set() { throw new TypeError('The properties of a WebAssembly object cannot be set'); },
                deleteProperty() { throw new TypeError('The properties of a WebAssembly object cannot be deleted'); },
                ownKeys() { return []; },
              };
              return function () { return new Proxy(Object.preventExtensions(Object.create(null)), handler); };
            })()
            """);
        var result = Adapter.RunInRealm(EngineRealm, () => Adapter.Call(_wasmCreateGcObject, Adapter.Undefined, []));
        state.GcObjects.AddOrUpdate(gcObject, result);
        state.GcObjectAddresses.AddOrUpdate(result, gcObject);
        return result;
    }

    /// <summary>
    /// https://webassembly.github.io/spec/js-api/#addressvaluetou64: [EnforceRange] unsigned long for i32, a BigInt in the
    /// range of u64 for i64.
    /// </summary>
    internal ulong ToWasmAddress(TValue value, bool is64)
    {
        if (!is64)
            return ToUnsignedLong(value, IntegerConversion.EnforceRange);

        var bigint = Adapter.ToBigInt(value);
        if (bigint < 0 || bigint > ulong.MaxValue)
            throw CreateTypeError("The value is outside the range of an i64 address");

        return (ulong)bigint;
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#u64toaddressvalue</summary>
    internal TValue FromWasmAddress(ulong value, bool is64) => is64 ? Adapter.FromBigInt(new BigInteger(value)) : Adapter.FromNumber(value);

    /// <summary>https://webassembly.github.io/spec/js-api/#towebassemblyvaluetype (the ValueType enumeration)</summary>
    internal WasmValueType ToWasmValueType(TValue value)
    {
        var name = ToEnumerationValue(value, "ValueType", ["i32", "i64", "f32", "f64", "v128", "externref", "anyfunc"]);
        return name switch
        {
            "i32" => WasmValueType.I32,
            "i64" => WasmValueType.I64,
            "f32" => WasmValueType.F32,
            "f64" => WasmValueType.F64,
            "v128" => WasmValueType.V128,
            "externref" => WasmValueType.ExternRef,
            _ => WasmValueType.FuncRef,
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Web API (https://webassembly.github.io/spec/web-api/)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// compileStreaming() and instantiateStreaming(): https://webassembly.github.io/spec/web-api/#compile-a-potential-webassembly-response.
    /// The steps that read the Response object run in JavaScript (Fetch.js).
    /// </summary>
    private TValue WasmStreaming(ReadOnlySpan<TValue> arguments, bool instantiate)
    {
        var operationName = instantiate ? "instantiateStreaming" : "compileStreaming";
        if (arguments.Length < 1)
            throw NotEnoughArguments(operationName, 1, arguments.Length);

        var importObject = instantiate ? ToWasmImportObject(arguments, 1) : null;
        var options = ToWasmCompileOptions(arguments, instantiate ? 2 : 1);
        var (promise, resolve, reject) = CreateWasmPromise();

        if (_wasmStreaming is null)
        {
            var fetch = Features.FetchEnabled ? GetFetch() : Adapter.Undefined;
            var response = Features.FetchEnabled ? Adapter.Get(fetch, Key("Response")) : Adapter.Undefined;
            var create = EvaluateBuiltIn("""
                (function (internals, arrayBuffer, headersGet) {
                  'use strict';
                  const apply = Reflect.apply;
                  const weakMapGet = WeakMap.prototype.get;
                  const then = Promise.prototype.then;
                  const resolve = Promise.resolve.bind(Promise);
                  // https://webassembly.github.io/spec/web-api/#compile-a-potential-webassembly-response: the bytes of the response
                  return function (source, onBytes, onError) {
                    apply(then, resolve(source), [(unwrappedSource) => {
                      const record = internals !== null && ((typeof unwrappedSource === 'object' && unwrappedSource !== null) || typeof unwrappedSource === 'function') ? apply(weakMapGet, internals, [unwrappedSource]) : undefined;
                      if (record === undefined || record.kind !== 'Response') return onError(new TypeError('The argument is not a Response'));
                      let mimeType = apply(headersGet, record.headers, ['Content-Type']);
                      if (mimeType === null) return onError(new TypeError("The response has no 'Content-Type' header"));
                      mimeType = mimeType.replace(/^[\t ]+|[\t ]+$/g, '');
                      if (mimeType.toLowerCase() !== 'application/wasm') return onError(new TypeError("The 'Content-Type' of the response is not 'application/wasm'"));
                      const response = record.response;
                      if (response.type !== 'basic' && response.type !== 'cors' && response.type !== 'default') return onError(new TypeError('The response is not CORS-same-origin'));
                      if (response.status < 200 || response.status > 299) return onError(new TypeError('The status of the response is not an ok status'));
                      let bodyPromise;
                      try {
                        bodyPromise = apply(arrayBuffer, unwrappedSource, []);
                      } catch (e) {
                        return onError(e);
                      }
                      apply(then, bodyPromise, [onBytes, onError]);
                    }, onError]);
                  };
                })
                """);
            _wasmStreaming = Adapter.Call(create, Adapter.Undefined,
            [
                Agent.FetchInternals ?? Adapter.Null,
                Adapter.IsObject(response) ? Adapter.Get(Adapter.Get(response, Key("prototype")), Key("arrayBuffer")) : Adapter.Undefined,
                Adapter.Get(GetPrototype("Headers"), Key("get")),
            ]);
        }

        var onBytes = Adapter.CreateFunction("", 1, (_, args) =>
        {
            byte[] bytes;
            try
            {
                bytes = ToWasmBytes(args[0]);
            }
            catch (Exception ex) when (Adapter.TryGetThrownValue(ex, out var thrown))
            {
                Adapter.Call(reject, Adapter.Undefined, [thrown]);
                return Adapter.Undefined;
            }

            CompileWasmModuleAsync(bytes, options, module =>
            {
                if (!instantiate)
                {
                    Adapter.Call(resolve, Adapter.Undefined, [GetWrapper(module)]);
                    return;
                }

                InstantiateWasmModuleAsync(module, importObject, instance =>
                {
                    var result = CreateObject();
                    SetMember(result, "module", GetWrapper(module));
                    SetMember(result, "instance", instance);
                    Adapter.Call(resolve, Adapter.Undefined, [result]);
                }, error => Adapter.Call(reject, Adapter.Undefined, [error]));
            }, error => Adapter.Call(reject, Adapter.Undefined, [error]));
            return Adapter.Undefined;
        });
        Adapter.Call(_wasmStreaming, Adapter.Undefined, [arguments[0], onBytes, reject]);
        return promise;
    }
}
