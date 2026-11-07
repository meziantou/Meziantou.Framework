using Meziantou.Framework.JavaScriptEngine.Compiler;
using Meziantou.Framework.JavaScriptEngine.Interpreter;
using Meziantou.Framework.JavaScriptEngine.Modules;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A Synthetic Module Record (https://tc39.es/ecma262/#sec-synthetic-module-records): a module whose exports are
/// set by the host (e.g. a JSON module).
/// </summary>
public sealed class JsSyntheticModule : JsModule
{
    private readonly string[] _exportNames;
    private readonly ScopeInfo _environmentInfo;
    private readonly Action<JsSyntheticModule> _evaluationSteps;

    internal JsSyntheticModule(JsRealm realm, string? url, object? hostDefined, IEnumerable<string> exportNames, Action<JsSyntheticModule> evaluationSteps)
        : base(realm, url, hostDefined)
    {
        _exportNames = [.. exportNames.Distinct(StringComparer.Ordinal)];
        _environmentInfo = new ScopeInfo(ScopeKind.Module, [.. _exportNames], new ScopeSlotFlags[_exportNames.Length], isVarScope: true);
        _evaluationSteps = evaluationSteps;
        Status = JsModuleStatus.Unlinked;
    }

    /// <summary>Gets the names of the exports of the module.</summary>
    public IReadOnlyList<string> ExportNames => _exportNames;

    /// <summary>The evaluation promise, once the module is evaluated.</summary>
    private JsObject? _evaluationPromise;

    /// <summary>Sets the value of an export (SetSyntheticModuleExport).</summary>
    /// <exception cref="ArgumentException">The module does not export the name.</exception>
    public void SetExport(string name, JsValue value)
    {
        var slot = _environmentInfo.IndexOf(name);
        if (slot < 0)
            throw new ArgumentException("The module does not export '" + name + "'", nameof(name));

        var environment = Environment ?? throw new InvalidOperationException("The module is not linked");
        environment.Slots[slot] = value;
    }

    /// <summary>Link() of a synthetic module: creates the environment, with the exports initialized to undefined.</summary>
    internal void LinkCore()
    {
        if (Environment is not null)
            return;

        Environment = new DeclarativeEnvironment(_environmentInfo, Realm.GlobalEnvironment);
        Status = JsModuleStatus.Linked;
    }

    /// <summary>Evaluate() of a synthetic module: runs the evaluation steps; the promise is settled.</summary>
    internal JsObject EvaluateCore()
    {
        if (_evaluationPromise is not null)
            return _evaluationPromise;

        var capability = PromiseCapability.CreateIntrinsic(Realm);
        Status = JsModuleStatus.Evaluated;
        try
        {
            _evaluationSteps(this);
            capability.Resolve(default);
        }
        catch (JsException ex)
        {
            EvaluationError = ex.Value;
            HasEvaluationError = true;
            capability.Reject(ex.Value);
        }

        _evaluationPromise = capability.Promise;
        return capability.Promise;
    }

    internal override List<string> GetExportedNames(List<JsModule> exportStarSet) => [.. _exportNames];

    internal override ResolvedBinding? ResolveExport(string exportName, List<(JsModule Module, string ExportName)> resolveSet)
        => _environmentInfo.IndexOf(exportName) >= 0 ? new ResolvedBinding(this, exportName) : null;

    internal override int GetBindingSlot(string bindingName) => _environmentInfo.IndexOf(bindingName);
}
