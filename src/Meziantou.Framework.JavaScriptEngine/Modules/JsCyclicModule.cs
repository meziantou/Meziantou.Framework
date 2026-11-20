using Meziantou.Framework.JavaScriptEngine.Interpreter;
using Meziantou.Framework.JavaScriptEngine.Modules;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A Cyclic Module Record (https://tc39.es/ecma262/#sec-cyclic-module-records): a module that can be part of a cycle of
/// imports, whose dependencies are loaded, linked and evaluated with it. A <see cref="JsSourceTextModule"/> is parsed from
/// JavaScript source text; a <see cref="JsHostDefinedModule"/> is defined by the host (e.g. a WebAssembly module).
/// </summary>
public abstract class JsCyclicModule : JsModule
{
    private protected JsCyclicModule(JsRealm realm, string? url, object? hostDefined)
        : base(realm, url, hostDefined)
    {
    }

    /// <summary>[[RequestedModules]]</summary>
    internal List<ModuleRequest> RequestedModules { get; } = [];

    /// <summary>[[LoadedModules]]</summary>
    internal List<(ModuleRequest Request, JsModule Module)> LoadedModules { get; } = [];

    /// <summary>
    /// [[OptionalIndirectExportEntries]] (https://tc39.es/proposal-deferred-reexports/): the entries of the <c>export defer</c>
    /// declarations, and of the <c>export * from</c> declarations that may re-export the <c>export defer</c> bindings of
    /// other modules, in source order. Only a source text module has some.
    /// </summary>
    internal List<ExportEntry> OptionalIndirectExportEntries { get; } = [];

    /// <summary>[[HasTLA]]</summary>
    internal virtual bool HasTopLevelAwait => false;

    internal int DfsIndex { get; set; } = -1;

    internal int DfsAncestorIndex { get; set; } = -1;

    /// <summary>[[CycleRoot]]</summary>
    internal JsCyclicModule? CycleRoot { get; set; }

    /// <summary>[[AsyncEvaluationOrder]]: 0 when unset, -1 when done, the order of the asynchronous evaluation otherwise.</summary>
    internal long AsyncEvaluationOrder { get; set; }

    internal bool IsAsyncEvaluation => AsyncEvaluationOrder > 0;

    /// <summary>[[TopLevelCapability]]</summary>
    internal PromiseCapability? TopLevelCapability { get; set; }

    /// <summary>[[AsyncParentModules]]</summary>
    internal List<JsCyclicModule> AsyncParentModules { get; } = [];

    /// <summary>[[PendingAsyncDependencies]]</summary>
    internal int PendingAsyncDependencies { get; set; }

    /// <summary>GetImportedModule(referrer, request)</summary>
    internal JsModule GetImportedModule(ModuleRequest request)
    {
        foreach (var (loadedRequest, module) in LoadedModules)
        {
            if (loadedRequest.Equals(request))
                return module;
        }

        throw new InvalidOperationException("The module '" + request.Specifier + "' is not loaded");
    }

    /// <summary>
    /// GetOptionalIndirectExportsModuleRequests(importedNames): the requests of the <c>export defer</c> declarations that
    /// provide <paramref name="importedNames"/> (null for all of them), and of the <c>export * from</c> declarations that
    /// may re-export them from other modules.
    /// </summary>
    internal virtual List<ModuleRequest> GetOptionalIndirectExportsModuleRequests(List<string>? importedNames) => [];
}
