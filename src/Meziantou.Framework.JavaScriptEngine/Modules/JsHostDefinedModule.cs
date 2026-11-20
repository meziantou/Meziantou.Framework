using Meziantou.Framework.JavaScriptEngine.Compiler;
using Meziantou.Framework.JavaScriptEngine.Interpreter;
using Meziantou.Framework.JavaScriptEngine.Modules;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A cyclic module defined by the host (e.g. a WebAssembly Module Record,
/// https://webassembly.github.io/esm-integration/js-api/index.html#integration-with-ecmascript-modules): its dependencies
/// are loaded, linked and evaluated before it, like the imports of a JavaScript module, and it can be part of a cycle. Its
/// exports are uninitialized bindings until the host sets them, when the module is evaluated.
/// </summary>
/// <remarks>
/// The module is created by <see cref="JsRealm.CreateHostDefinedModule"/>. When it is linked, the names it declares to
/// import must be exported by the modules it requests (a SyntaxError otherwise, before any module of the graph runs, as
/// for a JavaScript module). When it is evaluated, after the modules it requests, the evaluation steps read the values of
/// its imports with <see cref="GetImport"/> and set its exports with <see cref="SetExport"/> or
/// <see cref="SetExportGetter"/>. An export that is not set stays uninitialized: reading it throws a ReferenceError.
/// </remarks>
public sealed class JsHostDefinedModule : JsCyclicModule
{
    private readonly string[] _exportNames;
    private readonly string[] _requestedModules;
    private readonly KeyValuePair<string, string>[] _importedNames;
    private readonly ScopeInfo _environmentInfo;
    private readonly Action<JsHostDefinedModule> _evaluationSteps;

    internal JsHostDefinedModule(JsRealm realm, string? url, object? hostDefined, IEnumerable<string> requestedModules, IEnumerable<string> exportNames, IEnumerable<KeyValuePair<string, string>>? importedNames, Action<JsHostDefinedModule> evaluationSteps)
        : base(realm, url, hostDefined)
    {
        _requestedModules = [.. requestedModules.Distinct(StringComparer.Ordinal)];
        _exportNames = [.. exportNames.Distinct(StringComparer.Ordinal)];
        _importedNames = importedNames is null ? [] : [.. importedNames.Distinct()];
        foreach (var (specifier, importName) in _importedNames)
        {
            ArgumentNullException.ThrowIfNull(specifier, nameof(importedNames));
            ArgumentNullException.ThrowIfNull(importName, nameof(importedNames));
            if (!_requestedModules.Contains(specifier, StringComparer.Ordinal))
                throw new ArgumentException("The module does not request the module '" + specifier + "' of an imported name", nameof(importedNames));
        }

        _environmentInfo = new ScopeInfo(ScopeKind.Module, [.. _exportNames], Enumerable.Repeat(ScopeSlotFlags.Immutable | ScopeSlotFlags.Uninitialized, _exportNames.Length).ToArray(), isVarScope: true);
        _evaluationSteps = evaluationSteps;
        foreach (var specifier in _requestedModules)
        {
            // The evaluation steps can read any name of the module: all of them may be used
            RequestedModules.Add(ModuleRequest.Create(specifier, [], ModulePhase.Evaluation, new ImportedNames(direct: null, [])));
        }
    }

    /// <summary>Gets the names of the exports of the module.</summary>
    public IReadOnlyList<string> ExportNames => _exportNames;

    /// <summary>Gets the specifiers of the modules the module imports, in order and without duplicates.</summary>
    public IReadOnlyList<string> RequestedModuleSpecifiers => _requestedModules;

    /// <summary>
    /// Gets the names that the module declares to import, validated when the module is linked: the specifier of a module of
    /// <see cref="RequestedModuleSpecifiers"/> (the key) and the name of the export it imports (the value), in order and
    /// without duplicates.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> ImportedNames => _importedNames;

    /// <summary>Initializes or sets the value of an export of the module. The module must be linked.</summary>
    /// <exception cref="ArgumentException">The module does not export <paramref name="name"/>.</exception>
    /// <exception cref="InvalidOperationException">The module is not linked.</exception>
    public void SetExport(string name, JsValue value)
    {
        ArgumentNullException.ThrowIfNull(name);
        GetEnvironment().Slots[GetExportSlot(name)] = value;
    }

    /// <summary>
    /// Initializes an export of the module with a live binding: its value is read from <paramref name="getter"/> each time
    /// the binding is read (e.g. the value of a mutable WebAssembly global, which the WebAssembly code can change). The
    /// module must be linked.
    /// </summary>
    /// <exception cref="ArgumentException">The module does not export <paramref name="name"/>.</exception>
    /// <exception cref="InvalidOperationException">The module is not linked.</exception>
    public void SetExportGetter(string name, Func<JsValue> getter)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(getter);
        GetEnvironment().Slots[GetExportSlot(name)] = new JsValue(new LiveBindingCell(getter));
    }

    /// <summary>
    /// Resolves an import of the module (the binding <paramref name="importName"/> of the module requested with
    /// <paramref name="specifier"/>, ResolveExport), followed through the re-exports. The module must be linked.
    /// </summary>
    /// <param name="specifier">The specifier of a module of <see cref="RequestedModuleSpecifiers"/>.</param>
    /// <param name="importName">The name of the export of the requested module.</param>
    /// <exception cref="ArgumentException"><paramref name="specifier"/> is not a requested module.</exception>
    /// <exception cref="JsException">The requested module does not export the name, or exports it ambiguously (a SyntaxError).</exception>
    public JsModuleBinding GetImport(string specifier, string importName)
    {
        ArgumentNullException.ThrowIfNull(specifier);
        ArgumentNullException.ThrowIfNull(importName);
        var request = FindRequest(specifier);
        using var scope = Realm.Engine.Enter();
        return new JsModuleBinding(ResolveImport(request, importName));
    }

    /// <summary>Gets the module that the module imports with <paramref name="specifier"/>. The module must be loaded.</summary>
    /// <exception cref="ArgumentException"><paramref name="specifier"/> is not a requested module.</exception>
    public JsModule GetRequestedModule(string specifier)
    {
        ArgumentNullException.ThrowIfNull(specifier);
        return GetImportedModule(FindRequest(specifier));
    }

    private ModuleRequest FindRequest(string specifier)
        => RequestedModules.Find(r => string.Equals(r.Specifier, specifier, StringComparison.Ordinal))
            ?? throw new ArgumentException("The module does not request the module '" + specifier + "'", nameof(specifier));

    /// <summary>ResolveExport of an imported name: a SyntaxError when the requested module does not export it, or exports it ambiguously.</summary>
    private ResolvedBinding ResolveImport(ModuleRequest request, string importName)
    {
        var imported = GetImportedModule(request);
        var resolution = imported.ResolveExport(importName, []);
        if (resolution is null || resolution == ResolvedBinding.Ambiguous)
            ThrowHelper.ThrowSyntaxError(ModuleOperations.GetResolutionError(importName, resolution, imported));

        return resolution!;
    }

    private DeclarativeEnvironment GetEnvironment() => Environment ?? throw new InvalidOperationException("The module is not linked");

    private int GetExportSlot(string name)
    {
        var slot = _environmentInfo.IndexOf(name);
        if (slot < 0)
            throw new ArgumentException("The module does not export '" + name + "'", nameof(name));

        return slot;
    }

    /// <summary>
    /// InitializeEnvironment(): the imported names must be resolvable, as the imports of a source text module (e.g. the
    /// imports of a WebAssembly module, https://webassembly.github.io/esm-integration/js-api/index.html#module-execution
    /// asserts that they were "validated during environment initialization"), so that no module of the graph runs when
    /// one is missing. The exports are uninitialized bindings.
    /// </summary>
    internal void InitializeEnvironment()
    {
        foreach (var (specifier, importName) in _importedNames)
        {
            ResolveImport(FindRequest(specifier), importName);
        }

        Environment = new DeclarativeEnvironment(_environmentInfo, Realm.GlobalEnvironment);
    }

    /// <summary>ExecuteModule(): runs the evaluation steps of the host.</summary>
    internal void ExecuteModule() => _evaluationSteps(this);

    internal override List<string> GetExportedNames(List<JsModule> exportStarSet) => [.. _exportNames];

    internal override ResolvedBinding? ResolveExport(string exportName, List<(JsModule Module, string ExportName)> resolveSet)
        => _environmentInfo.IndexOf(exportName) >= 0 ? new ResolvedBinding(this, exportName) : null;

    internal override int GetBindingSlot(string bindingName) => _environmentInfo.IndexOf(bindingName);
}
