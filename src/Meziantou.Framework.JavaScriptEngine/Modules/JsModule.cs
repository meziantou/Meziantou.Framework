using Meziantou.Framework.JavaScriptEngine.Interpreter;
using Meziantou.Framework.JavaScriptEngine.Modules;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A Module Record (https://tc39.es/ecma262/#sec-abstract-module-records). A module is loaded
/// (<see cref="LoadRequestedModules"/>), linked (<see cref="Link"/>), then evaluated (<see cref="Evaluate"/>).
/// </summary>
public abstract class JsModule : IScriptOrModule
{
    private protected JsModule(JsRealm realm, string? url, object? hostDefined)
    {
        Realm = realm;
        Url = url;
        HostDefined = hostDefined;
    }

    /// <summary>Gets the realm the module was created in ([[Realm]]).</summary>
    public JsRealm Realm { get; }

    /// <summary>Gets the URL (or the file name) of the module.</summary>
    public string? Url { get; }

    /// <summary>Gets the data of the host associated with the module ([[HostDefined]]).</summary>
    public object? HostDefined { get; }

    /// <summary>Gets the status of the module.</summary>
    public JsModuleStatus Status { get; internal set; }

    /// <summary>Gets the error thrown by the evaluation of the module ([[EvaluationError]]), or undefined.</summary>
    public JsValue EvaluationError { get; internal set; }

    /// <summary>Gets whether the evaluation of the module threw an error.</summary>
    public bool HasEvaluationError { get; internal set; }

    /// <summary>Gets the module environment ([[Environment]]), created when the module is linked.</summary>
    internal DeclarativeEnvironment? Environment { get; set; }

    /// <summary>Gets the namespace object ([[Namespace]]), created on first use.</summary>
    internal JsObject? Namespace { get; set; }

    /// <summary>Loads the dependencies of the module, recursively (LoadRequestedModules).</summary>
    /// <param name="hostDefined">A value given to the module loader for each module it loads.</param>
    /// <returns>A promise fulfilled when the whole graph is loaded, rejected when a module cannot be loaded.</returns>
    public JsValue LoadRequestedModules(object? hostDefined = null)
    {
        using var scope = Realm.Engine.Enter();
        return ModuleOperations.LoadRequestedModules(this, hostDefined);
    }

    /// <summary>Links the module and its dependencies (Link): creates the environments and resolves the imports.</summary>
    /// <exception cref="JsException">An import cannot be resolved (a SyntaxError).</exception>
    public void Link()
    {
        using var scope = Realm.Engine.Enter();
        ModuleOperations.Link(this);
    }

    /// <summary>Evaluates the module and its dependencies (Evaluate).</summary>
    /// <returns>A promise fulfilled when the evaluation completes, rejected with the error of the evaluation.</returns>
    public JsValue Evaluate()
    {
        using var scope = Realm.Engine.Enter();
        return ModuleOperations.Evaluate(this);
    }

    /// <summary>Gets the namespace object of the module (GetModuleNamespace). The module must be linked.</summary>
    public JsValue GetNamespace()
    {
        using var scope = Realm.Engine.Enter();
        return ModuleOperations.GetModuleNamespace(this);
    }

    /// <summary>GetExportedNames(exportStarSet)</summary>
    internal abstract List<string> GetExportedNames(List<JsModule> exportStarSet);

    /// <summary>ResolveExport(exportName, resolveSet): a binding, null (not found or circular), or <see cref="ResolvedBinding.Ambiguous"/>.</summary>
    internal abstract ResolvedBinding? ResolveExport(string exportName, List<(JsModule Module, string ExportName)> resolveSet);

    /// <summary>Gets the slot of a binding of the module environment.</summary>
    internal abstract int GetBindingSlot(string bindingName);
}
