using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The ESM integration of WebAssembly (https://webassembly.github.io/esm-integration/js-api/index.html#integration-with-ecmascript-modules):
// the WebAssembly modules imported by the module scripts. The script engine creates the module records; these steps parse
// the module, instantiate it with the values of its imports, and give the values of its exports.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>The module of the imported string constants of a WebAssembly module script.</summary>
    internal const string WasmModuleScriptImportedStringModule = "wasm:js/string-constants";

    /// <summary>
    /// https://webassembly.github.io/esm-integration/js-api/index.html#parse-a-webassembly-module: compiles the module with
    /// the builtin set "js-string" and the imported string constants, and checks the reserved names. Throws a CompileError
    /// or a LinkError. <paramref name="requestedModules"/> are the modules of its external imports, in order.
    /// </summary>
    internal WebAssemblyModuleObject ParseWasmModuleScript(byte[] bytes, out List<string> requestedModules, out List<string> exportNames)
    {
        WasmModule module;
        try
        {
            module = WebAssemblyRuntime!.Compile(bytes);
        }
        catch (WasmCompileException ex)
        {
            throw CreateWasmError("CompileError", ex.Message);
        }

        string[] builtinSets = [JsStringBuiltinSetName];
        if (!ValidateWasmBuiltinsAndImportedStrings(module, builtinSets, WasmModuleScriptImportedStringModule))
            throw CreateWasmError("CompileError", "The builtins or the imported string constants of the module are not valid");

        var moduleObject = new WebAssemblyModuleObject(module, builtinSets, WasmModuleScriptImportedStringModule);
        requestedModules = [];
        foreach (var import in module.Imports)
        {
            if (IsReflectedAsWasmBuiltinOrStringImport(import, builtinSets, WasmModuleScriptImportedStringModule))
                continue;

            if (import.Module.StartsWith("wasm-js:", StringComparison.Ordinal))
                throw CreateWasmError("LinkError", $"The module name '{import.Module}' of an import is reserved");

            if (IsReservedWasmName(import.Name))
                throw CreateWasmError("LinkError", $"The name '{import.Name}' of an import is reserved");

            if (!requestedModules.Contains(import.Module, StringComparer.Ordinal))
            {
                requestedModules.Add(import.Module);
            }
        }

        exportNames = [];
        foreach (var export in module.Exports)
        {
            if (IsReservedWasmName(export.Name))
                throw CreateWasmError("LinkError", $"The name '{export.Name}' of an export is reserved");

            exportNames.Add(export.Name);
        }

        return moduleObject;

        static bool IsReservedWasmName(string name) => name.StartsWith("wasm:", StringComparison.Ordinal) || name.StartsWith("wasm-js:", StringComparison.Ordinal);
    }

    /// <summary>Registers the WebAssembly Module Record of a module script (the module record of the script engine).</summary>
    internal void RegisterWasmModuleRecord(object moduleRecord, WebAssemblyModuleObject moduleObject)
        => WebAssemblyState.ModuleRecords.AddOrUpdate(moduleRecord, new WebAssemblyModuleRecord { ModuleObject = moduleObject });

    /// <summary>
    /// ExecuteModule() of a WebAssembly Module Record (https://webassembly.github.io/esm-integration/js-api/index.html#module-execution):
    /// reads the imports (<paramref name="resolveImport"/> resolves an import in the module graph), instantiates the module,
    /// and calls <paramref name="initializeExport"/> for each export: with its value, with a getter for a mutable global
    /// (a live binding), or with neither for a v128 or an exnref global (the binding stays uninitialized).
    /// </summary>
    internal void ExecuteWasmModuleScript(object moduleRecord, Func<WasmImport, WasmModuleScriptImport<TValue>> resolveImport, Action<string, TValue?, Func<TValue>?> initializeExport)
    {
        var record = WebAssemblyState.ModuleRecords.TryGetValue(moduleRecord, out var r) ? r : throw new InvalidOperationException("The module is not a WebAssembly module");
        var moduleObject = record.ModuleObject!;
        var module = moduleObject.Module;
        var incumbent = GetWasmIncumbentGlobal();
        var imports = new List<WasmExtern>(module.Imports.Count);
        var functionIndex = 0;
        foreach (var import in module.Imports)
        {
            imports.Add(ReadWasmModuleScriptImport(moduleObject, import, resolveImport, incumbent, functionIndex));
            if (import.Type is WasmFunctionType)
            {
                functionIndex++;
            }
        }

        var instanceObject = InstantiateWasmModule(moduleObject, imports);
        record.Instance = instanceObject;
        foreach (var (name, value) in instanceObject.Instance.Exports)
        {
            if (value is WasmGlobal global)
            {
                var type = global.Type;
                if (type.ValueType.IsV128OrExnRef)
                {
                    // The binding stays uninitialized: reading it throws a ReferenceError
                    initializeExport(name, null, null);
                }
                else if (type.Mutable)
                {
                    initializeExport(name, null, () => ToJsValue(global.Get()));
                }
                else
                {
                    initializeExport(name, ToJsValue(global.Get()), null);
                }

                continue;
            }

            initializeExport(name, Adapter.Get((TValue)instanceObject.Exports, Key(name)), null);
        }
    }

    /// <summary>The extern of an import of a WebAssembly module script: a builtin, an export of a WebAssembly module, or the value of a binding.</summary>
    private WasmExtern ReadWasmModuleScriptImport(WebAssemblyModuleObject moduleObject, WasmImport import, Func<WasmImport, WasmModuleScriptImport<TValue>> resolveImport, GlobalScope? incumbent, int functionIndex)
    {
        if (IsReflectedAsWasmBuiltinOrStringImport(import, moduleObject.BuiltinSets, moduleObject.ImportedStringModule))
            return ReadWasmBuiltinOrStringImport(moduleObject, import);

        var resolution = resolveImport(import);
        if (resolution.ModuleRecord is { } importedRecord && WebAssemblyState.ModuleRecords.TryGetValue(importedRecord, out var wasmRecord))
        {
            // The export of the instance of a WebAssembly module: its type is checked by the instantiation
            if (wasmRecord.Instance is not { } importedInstance)
                throw CreateWasmError("LinkError", $"The import '{import.Module}.{import.Name}' is the export of a WebAssembly module that is not instantiated (a cycle)");

            foreach (var (name, value) in importedInstance.Instance.Exports)
            {
                if (name == resolution.BindingName)
                    return value;
            }

            throw CreateWasmError("LinkError", $"The WebAssembly module '{import.Module}' does not export '{resolution.BindingName}'");
        }

        // The binding of a module that is not evaluated yet (a cycle) cannot be read: as in the browsers, it is a LinkError
        if (resolution.Value is not { } v)
            throw CreateWasmError("LinkError", $"The import '{import.Module}.{import.Name}' is not initialized (a cycle)");

        return ReadWasmImportValue(import, v, incumbent, functionIndex);
    }

    /// <summary>https://webassembly.github.io/esm-integration/js-api/index.html#dom-webassembly-namespaceinstance</summary>
    private TValue WasmNamespaceInstance(ReadOnlySpan<TValue> arguments)
    {
        if (arguments.Length < 1)
            throw NotEnoughArguments("namespaceInstance", 1, arguments.Length);

#pragma warning disable WEBPLATEXP0014 // The ESM integration is experimental for the consumers of the package only
        if (Adapter.GetModuleOfNamespace(arguments[0]) is not { } moduleRecord)
            throw CreateTypeError("The argument is not a module namespace object");
#pragma warning restore WEBPLATEXP0014

        if (!WebAssemblyState.ModuleRecords.TryGetValue(moduleRecord, out var record))
            throw CreateTypeError("The module of the namespace object is not a WebAssembly module");

        if (record.Instance is not { } instance)
            throw CreateTypeError("The WebAssembly module of the namespace object is not instantiated");

        return GetWrapper(instance);
    }
}

/// <summary>
/// The resolution of an import of a WebAssembly module script in the module graph: the module record and the name of the
/// binding it resolves to, and the value of the binding (null when it is not initialized, or when the module is a
/// WebAssembly module, whose instance gives the export).
/// </summary>
internal readonly record struct WasmModuleScriptImport<TValue>(object? ModuleRecord, string? BindingName, TValue? Value)
    where TValue : class;
