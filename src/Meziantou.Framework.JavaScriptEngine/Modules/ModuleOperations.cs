using System.Diagnostics;
using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine.Modules;

/// <summary>The state of LoadRequestedModules (GraphLoadingState Record).</summary>
internal sealed class GraphLoadingState(PromiseCapability capability, object? hostDefined)
{
    public PromiseCapability Capability { get; } = capability;

    public bool IsLoading { get; set; } = true;

    public int PendingModulesCount { get; set; } = 1;

    /// <summary>[[Visited]]: the loaded modules, with the names already imported from them.</summary>
    public PreviouslyImportedNames Visited { get; } = new();

    public object? HostDefined { get; } = hostDefined;
}

/// <summary>The payload of the loading of the module of an <c>import()</c> or <c>import.defer()</c> call.</summary>
internal sealed record DynamicImportPayload(PromiseCapability Capability, ModulePhase Phase);

/// <summary>The value of the onDeferredNamespace parameter of BuildEvaluationList and ReadyForSyncExecution.</summary>
internal enum DeferredNamespaceHandling
{
    /// <summary>~ignore-deferred-namespace~: the requests with the defer phase are ignored.</summary>
    Ignore,

    /// <summary>~gather-async-deps~: the asynchronous dependencies of the deferred modules are collected.</summary>
    GatherAsyncDependencies,
}

/// <summary>The algorithms of the module graphs (https://tc39.es/ecma262/#sec-cyclic-module-records): loading, linking and evaluation.</summary>
internal static class ModuleOperations
{
    private static readonly PropertyKey WithKey = new("with");
    private static readonly PropertyKey ExportsKey = new("exports");

    // ------------------------------------------------------------------------------------------------------------------
    // Loading
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>LoadRequestedModules([importedNames], [hostDefined]): <paramref name="importedNames"/> is null for all the names.</summary>
    public static JsValue LoadRequestedModules(JsModule module, object? hostDefined, List<string>? importedNames = null)
    {
        var capability = PromiseCapability.CreateIntrinsic(module.Realm);
        if (module is not JsSourceTextModule)
        {
            capability.Resolve(default);
            return capability.Promise;
        }

        var state = new GraphLoadingState(capability, hostDefined);
        InnerModuleLoading(state, module, importedNames);
        return capability.Promise;
    }

    /// <summary>
    /// InnerModuleLoading(state, module, importedNames): the dependencies of the module of a source phase import are not
    /// loaded. A module is visited once, but the <c>export defer</c> declarations that provide new imported names are
    /// loaded on each visit.
    /// </summary>
    private static void InnerModuleLoading(GraphLoadingState state, JsModule module, List<string>? importedNames, bool loadDependencies = true)
    {
        if (loadDependencies && module is JsSourceTextModule cyclic)
        {
            List<ModuleRequest> requestsToLoad = [];
            if (state.Visited.TryAdd(cyclic) && cyclic.Status == JsModuleStatus.New)
            {
                requestsToLoad.AddRange(cyclic.RequestedModules);
            }

            requestsToLoad.AddRange(GetNewOptionalIndirectExportsModuleRequests(cyclic, new ImportedNames(importedNames, []), state.Visited));
            state.PendingModulesCount += requestsToLoad.Count;
            foreach (var request in requestsToLoad)
            {
                if (!AllImportAttributesSupported(request.Attributes))
                {
                    ContinueModuleLoading(state, module: null, module.Realm.CreateError(ErrorType.SyntaxError, "Unsupported import attribute"), importedNames: null);
                }
                else if (FindLoadedModule(cyclic, request) is { } loaded)
                {
                    InnerModuleLoading(state, loaded, request.ImportedNames.All, request.Phase != ModulePhase.Source);
                }
                else
                {
                    HostLoadImportedModule(cyclic.Realm, cyclic.Realm, cyclic, request, state.HostDefined, state);
                }

                if (!state.IsLoading)
                    return;
            }
        }

        state.PendingModulesCount--;
        if (state.PendingModulesCount == 0)
        {
            state.IsLoading = false;
            foreach (var loaded in state.Visited.Modules)
            {
                if (loaded.Status == JsModuleStatus.New)
                {
                    loaded.Status = JsModuleStatus.Unlinked;
                }
            }

            state.Capability.Resolve(default);
        }
    }

    /// <summary>ContinueModuleLoading(state, moduleCompletion)</summary>
    private static void ContinueModuleLoading(GraphLoadingState state, JsModule? module, JsValue error, List<string>? importedNames, bool loadDependencies = true)
    {
        if (!state.IsLoading)
            return;

        if (module is not null)
        {
            InnerModuleLoading(state, module, importedNames, loadDependencies);
        }
        else
        {
            state.IsLoading = false;
            state.Capability.Reject(error);
        }
    }

    private static JsModule? FindLoadedModule(JsSourceTextModule referrer, ModuleRequest request)
    {
        foreach (var (loadedRequest, module) in referrer.LoadedModules)
        {
            if (loadedRequest.Equals(request))
                return module;
        }

        return null;
    }

    /// <summary>AllImportAttributesSupported(attributes): the only supported attribute is <c>type</c>.</summary>
    private static bool AllImportAttributesSupported(IEnumerable<KeyValuePair<string, string>> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.Key != "type")
                return false;
        }

        return true;
    }

    /// <summary>HostLoadImportedModule(referrer, moduleRequest, hostDefined, payload)</summary>
    private static void HostLoadImportedModule(JsRealm realm, JsRealm currentRealm, IScriptOrModule? referrer, ModuleRequest request, object? hostDefined, object payload)
    {
        var loadRequest = new JsModuleLoadRequest(realm, currentRealm, referrer, request, hostDefined, payload);
        if (realm.Engine.Options.ModuleLoader is not { } loader)
        {
            loadRequest.Fail("Cannot load the module '" + request.Specifier + "': the engine has no module loader");
            return;
        }

        loadRequest.Load(loader);
    }

    /// <summary>FinishLoadingImportedModule(referrer, moduleRequest, payload, result)</summary>
    public static void FinishLoadingImportedModule(IScriptOrModule? referrer, ModuleRequest request, object payload, JsModule? module, JsValue error)
    {
        if (module is not null && referrer is JsSourceTextModule referrerModule)
        {
            if (FindLoadedModule(referrerModule, request) is { } existing)
            {
                if (existing != module)
                    throw new InvalidOperationException("The module loader returned a different module for the same request");
            }
            else
            {
                referrerModule.LoadedModules.Add((request, module));
            }
        }

        if (payload is GraphLoadingState state)
        {
            ContinueModuleLoading(state, module, error, request.ImportedNames.All, request.Phase != ModulePhase.Source);
        }
        else
        {
            ContinueDynamicImport((DynamicImportPayload)payload, module, error, request.ImportedNames.All);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Linking
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Link([importedNames]): <paramref name="importedNames"/> is null for all the names.</summary>
    public static void Link(JsModule module, List<string>? importedNames = null)
    {
        if (module is JsSyntheticModule synthetic)
        {
            synthetic.LinkCore();
            return;
        }

        var cyclic = (JsSourceTextModule)module;
        if (cyclic.Status is JsModuleStatus.New or JsModuleStatus.Linking or JsModuleStatus.Evaluating)
            throw new InvalidOperationException("The module cannot be linked: its status is " + cyclic.Status);

        var stack = new List<JsSourceTextModule>();
        try
        {
            InnerModuleLinking(cyclic, stack, 0);
        }
        catch (JsException) when (ResetLinkingModules(stack))
        {
            throw new UnreachableException();
        }

        // The modules of the export defer declarations that provide the imported names (https://tc39.es/proposal-deferred-reexports/)
        var optionalRequests = cyclic.GetOptionalIndirectExportsModuleRequests(importedNames);
        var linkingList = new List<JsModule>();
        var previouslyImportedNames = new PreviouslyImportedNames();
        BuildLinkingList(linkingList, cyclic, optionalRequests, previouslyImportedNames);
        foreach (var required in linkingList)
        {
            Link(required, importedNames: []);
        }

        ValidateAllRequestedOptionalNamespaceExports(previouslyImportedNames);
        ValidateRequestedOptionalIndirectExports(cyclic, importedNames);
        if (importedNames is null)
        {
            ValidateOwnNamespaceExports(cyclic, names: null);
        }
    }

    /// <summary>
    /// An exception filter that resets the status of the modules being linked when the linking fails, and does not catch
    /// the exception (a catch block that rethrows nests the exception dispatch).
    /// </summary>
    private static bool ResetLinkingModules(List<JsSourceTextModule> stack)
    {
        foreach (var m in stack)
        {
            m.Status = JsModuleStatus.Unlinked;
        }

        return false;
    }

    /// <summary>InnerModuleLinking(module, stack, index)</summary>
    private static int InnerModuleLinking(JsModule module, List<JsSourceTextModule> stack, int index)
    {
        if (module is not JsSourceTextModule cyclic)
        {
            ((JsSyntheticModule)module).LinkCore();
            return index;
        }

        if (cyclic.Status is JsModuleStatus.Linking or JsModuleStatus.Linked or JsModuleStatus.EvaluatingAsync or JsModuleStatus.Evaluated)
            return index;

        cyclic.Status = JsModuleStatus.Linking;
        cyclic.DfsIndex = index;
        cyclic.DfsAncestorIndex = index;
        index++;
        stack.Add(cyclic);
        var linkingList = new List<JsModule>();
        var previouslyImportedNames = new PreviouslyImportedNames();
        BuildLinkingList(linkingList, cyclic, cyclic.RequestedModules, previouslyImportedNames);
        foreach (var required in linkingList)
        {
            index = InnerModuleLinking(required, stack, index);
            if (required is JsSourceTextModule { Status: JsModuleStatus.Linking } requiredCyclic)
            {
                cyclic.DfsAncestorIndex = Math.Min(cyclic.DfsAncestorIndex, requiredCyclic.DfsAncestorIndex);
            }
        }

        InitializeEnvironment(cyclic);
        ValidateAllRequestedOptionalNamespaceExports(previouslyImportedNames);
        if (cyclic.DfsAncestorIndex == cyclic.DfsIndex)
        {
            JsSourceTextModule required;
            do
            {
                required = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                required.Status = JsModuleStatus.Linked;
            }
            while (required != cyclic);
        }

        return index;
    }

    /// <summary>
    /// BuildLinkingList(linkingList, referrer, moduleRequests, previouslyImportedNames): the modules to link for the
    /// requests, and for the <c>export defer</c> declarations that provide the names they import. The module of a source
    /// phase import is not linked.
    /// </summary>
    private static void BuildLinkingList(List<JsModule> linkingList, JsSourceTextModule referrer, List<ModuleRequest> moduleRequests, PreviouslyImportedNames previouslyImportedNames)
    {
        foreach (var request in moduleRequests)
        {
            if (request.Phase == ModulePhase.Source)
                continue;

            var required = referrer.GetImportedModule(request);
            if (!linkingList.Contains(required))
            {
                linkingList.Add(required);
                if (required is JsSourceTextModule cyclicRequired)
                {
                    previouslyImportedNames.TryAdd(cyclicRequired);
                }
            }

            if (required is JsSourceTextModule requiredCyclic)
            {
                var optionalRequests = GetNewOptionalIndirectExportsModuleRequests(requiredCyclic, request.ImportedNames, previouslyImportedNames);
                BuildLinkingList(linkingList, requiredCyclic, optionalRequests, previouslyImportedNames);
            }
        }
    }

    /// <summary>ValidateAllRequestedOptionalNamespaceExports(previouslyImportedNames)</summary>
    private static void ValidateAllRequestedOptionalNamespaceExports(PreviouslyImportedNames previouslyImportedNames)
    {
        foreach (var module in previouslyImportedNames.Modules)
        {
            var names = previouslyImportedNames.Get(module);
            if (names.Namespace is null)
            {
                ValidateOwnNamespaceExports(module, names: null);
            }

            ValidateRequestedOptionalIndirectExports(module, names.All);
        }
    }

    /// <summary>
    /// ValidateOwnNamespaceExports(module, names): the names of a namespace object of the module (null for all of them)
    /// must be resolvable; the names of a filtered namespace must not be ambiguous.
    /// </summary>
    private static void ValidateOwnNamespaceExports(JsModule module, List<string>? names)
    {
        if (names is null)
        {
            // Only the export defer declarations and the star exports of a source text module can be invalid: the other
            // exports are validated by the InitializeEnvironment of the module
            if (module is not JsSourceTextModule { NamespaceExportsValidated: false, OptionalIndirectExportEntries.Count: > 0 } cyclic)
                return;

            foreach (var name in cyclic.GetExportedNames([]))
            {
                EnsureResolvableBinding(cyclic, name, allowAmbiguous: true);
            }

            cyclic.NamespaceExportsValidated = true;
            return;
        }

        foreach (var name in names)
        {
            EnsureResolvableBinding(module, name, allowAmbiguous: false);
        }
    }

    /// <summary>
    /// ValidateRequestedOptionalIndirectExports(module, importedNames): the <c>export defer</c> declarations that provide
    /// <paramref name="importedNames"/> (null for all the names) must be resolvable.
    /// </summary>
    private static void ValidateRequestedOptionalIndirectExports(JsSourceTextModule module, List<string>? importedNames)
    {
        if (module.OptionalIndirectExportEntries.Count == 0)
            return;

        foreach (var name in importedNames ?? module.GetExportedNames([]))
        {
            foreach (var request in module.GetOptionalIndirectExportsModuleRequests([name]))
            {
                if (request.IsStarExport)
                    continue;

                EnsureResolvableBinding(module, name, allowAmbiguous: false);
                if (request.ImportedNames.Namespace is { } filter)
                {
                    ValidateOwnNamespaceExports(module.GetImportedModule(request), filter);
                }
            }
        }
    }

    /// <summary>EnsureResolvableBinding(module, name, onAmbiguous): throws a SyntaxError when the module does not export the name.</summary>
    private static void EnsureResolvableBinding(JsModule module, string name, bool allowAmbiguous)
    {
        var resolution = module.ResolveExport(name, []);
        if (resolution is null || (!allowAmbiguous && resolution == ResolvedBinding.Ambiguous))
            ThrowHelper.ThrowSyntaxError(GetResolutionError(name, resolution, module));
    }

    /// <summary>InitializeEnvironment() of a Source Text Module Record.</summary>
    private static void InitializeEnvironment(JsSourceTextModule module)
    {
        foreach (var entry in module.IndirectExportEntries)
        {
            EnsureResolvableBinding(module, entry.ExportName!, allowAmbiguous: false);
            switch (entry.ImportKind)
            {
                case ExportImportKind.All:
                    ValidateOwnNamespaceExports(module.GetImportedModule(entry.ModuleRequest!), names: null);
                    break;
                case ExportImportKind.FilteredNamespace:
                    ValidateOwnNamespaceExports(module.GetImportedModule(entry.ModuleRequest!), entry.NamespaceNamesFilter);
                    break;
            }
        }

        var realm = module.Realm;
        var environment = new DeclarativeEnvironment(module.Code.Environment, realm.GlobalEnvironment);
        module.Environment = environment;
        foreach (var entry in module.ImportEntries)
        {
            var imported = module.GetImportedModule(entry.ModuleRequest);
            var slot = module.GetBindingSlot(entry.LocalName);
            if (entry.IsNamespace)
            {
                if (entry.ModuleRequest.Phase != ModulePhase.Source)
                {
                    ValidateOwnNamespaceExports(imported, entry.NamespaceNamesFilter);
                }

                environment.Slots[slot] = entry.NamespaceNamesFilter is { } filter
                    ? GetModuleNamespace(imported, entry.ModuleRequest.Phase, filter)
                    : GetModuleNamespaceOrSource(imported, entry.ModuleRequest.Phase);
                continue;
            }

            var resolution = imported.ResolveExport(entry.ImportName!, []);
            if (resolution is null || resolution == ResolvedBinding.Ambiguous)
                ThrowHelper.ThrowSyntaxError(GetResolutionError(entry.ImportName!, resolution, imported));

            environment.Slots[slot] = resolution switch
            {
                { FilteredNamespaceExport: { } export } => ((JsSourceTextModule)resolution.Module).GetFilteredNamespaceExport(export),
                { IsNamespace: true } => GetModuleNamespaceOrSource(resolution.Module, resolution.NamespacePhase),
                _ => new JsValue(new ImportBindingCell(resolution.Module, resolution.Module.GetBindingSlot(resolution.BindingName!))),
            };
        }

        // The function declarations are instantiated now: the other modules of a cycle can call them before this module runs
        var frame = new Frame(module.Code.Initialization, realm, environment, [])
        {
            Script = module,
        };

        JsInterpreter.Run(frame);
    }

    private static string GetResolutionError(string name, ResolvedBinding? resolution, JsModule module)
    {
        var url = module.Url is null ? "the module" : "'" + module.Url + "'";
        return resolution is null
            ? "The requested module " + url + " does not provide an export named '" + name + "'"
            : "The requested module " + url + " contains conflicting star exports for name '" + name + "'";
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Evaluation
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Evaluate()</summary>
    public static JsValue Evaluate(JsModule module)
    {
        if (module is JsSyntheticModule synthetic)
            return synthetic.EvaluateCore();

        var cyclic = (JsSourceTextModule)module;
        if (cyclic.Status is not (JsModuleStatus.Linked or JsModuleStatus.EvaluatingAsync or JsModuleStatus.Evaluated))
            throw new InvalidOperationException("The module cannot be evaluated: its status is " + cyclic.Status);

        if (cyclic.Status is JsModuleStatus.EvaluatingAsync or JsModuleStatus.Evaluated)
        {
            cyclic = cyclic.CycleRoot ?? cyclic;
        }

        if (cyclic.TopLevelCapability is { } existing)
            return existing.Promise;

        var stack = new List<JsSourceTextModule>();
        var capability = PromiseCapability.CreateIntrinsic(cyclic.Realm);
        cyclic.TopLevelCapability = capability;
        try
        {
            InnerModuleEvaluation(cyclic, stack, 0);
        }
        catch (JsException ex)
        {
            SetEvaluationError(stack, ex.Value, ex.Location);
            capability.Reject(ex.Value);
            return capability.Promise;
        }
        catch (Exception ex)
        {
            // A termination or a .NET exception: the modules being evaluated cannot complete, they are evaluated with an
            // error that describes it, and the exception goes to the host
            var error = JsInterpreter.CreateAbortError(cyclic.Realm, ex);
            SetEvaluationError(stack, error, location: null);
            capability.Reject(error);
            throw;
        }

        if (!cyclic.IsAsyncEvaluation)
        {
            capability.Resolve(default);
        }

        return capability.Promise;
    }

    private static void SetEvaluationError(List<JsSourceTextModule> stack, JsValue error, JsSourceLocation? location)
    {
        foreach (var m in stack)
        {
            m.Status = JsModuleStatus.Evaluated;
            m.EvaluationError = error;
            m.EvaluationErrorLocation = location;
            m.HasEvaluationError = true;

            // An asynchronous dependency of the module that completes later finds the error of its strongly connected component
            m.CycleRoot ??= m;
        }
    }

    /// <summary>InnerModuleEvaluation(module, stack, index)</summary>
    private static int InnerModuleEvaluation(JsModule module, List<JsSourceTextModule> stack, int index)
    {
        if (module is not JsSourceTextModule cyclic)
        {
            var promise = (JsPromise)((JsSyntheticModule)module).EvaluateCore();
            if (promise.State == PromiseState.Rejected)
                throw new JsException(promise.Result);

            return index;
        }

        if (cyclic.Status is JsModuleStatus.EvaluatingAsync or JsModuleStatus.Evaluated)
        {
            if (cyclic.HasEvaluationError)
                throw new JsException(cyclic.EvaluationError);

            return index;
        }

        if (cyclic.Status == JsModuleStatus.Evaluating)
            return index;

        cyclic.Status = JsModuleStatus.Evaluating;
        cyclic.DfsIndex = index;
        cyclic.DfsAncestorIndex = index;
        cyclic.PendingAsyncDependencies = 0;
        index++;
        stack.Add(cyclic);

        // The deferred modules are not evaluated, only their asynchronous dependencies (https://tc39.es/proposal-defer-import-eval/),
        // and the modules of the export defer declarations that provide the imported names are evaluated after the module
        // that re-exports them (https://tc39.es/proposal-deferred-reexports/)
        var evaluationList = new List<JsModule>(cyclic.RequestedModules.Count);
        BuildEvaluationList(evaluationList, cyclic, cyclic.RequestedModules, new PreviouslyImportedNames(), DeferredNamespaceHandling.GatherAsyncDependencies);

        foreach (var required in evaluationList)
        {
            index = InnerModuleEvaluation(required, stack, index);
            if (required is JsSourceTextModule requiredCyclic)
            {
                if (requiredCyclic.Status == JsModuleStatus.Evaluating)
                {
                    cyclic.DfsAncestorIndex = Math.Min(cyclic.DfsAncestorIndex, requiredCyclic.DfsAncestorIndex);
                }
                else
                {
                    requiredCyclic = requiredCyclic.CycleRoot!;
                    if (requiredCyclic.HasEvaluationError)
                        throw new JsException(requiredCyclic.EvaluationError);
                }

                if (requiredCyclic.IsAsyncEvaluation)
                {
                    cyclic.PendingAsyncDependencies++;
                    requiredCyclic.AsyncParentModules.Add(cyclic);
                }
            }
        }

        if (cyclic.PendingAsyncDependencies > 0 || cyclic.HasTopLevelAwait)
        {
            cyclic.AsyncEvaluationOrder = ++cyclic.Realm.Engine.ModuleAsyncEvaluationCount;
            if (cyclic.PendingAsyncDependencies == 0)
            {
                ExecuteAsyncModule(cyclic);
            }
        }
        else
        {
            ExecuteModule(cyclic);
        }

        if (cyclic.DfsAncestorIndex == cyclic.DfsIndex)
        {
            JsSourceTextModule required;
            do
            {
                required = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                required.Status = required.IsAsyncEvaluation ? JsModuleStatus.EvaluatingAsync : JsModuleStatus.Evaluated;
                required.CycleRoot = cyclic;
            }
            while (required != cyclic);
        }

        return index;
    }

    private static void AddUnique(List<JsModule> list, JsModule module)
    {
        if (!list.Contains(module))
        {
            list.Add(module);
        }
    }

    /// <summary>ListAppendUnique(list1, list2)</summary>
    private static void AddUnique(List<JsModule> list, List<JsModule> modules)
    {
        foreach (var module in modules)
        {
            AddUnique(list, module);
        }
    }

    /// <summary>
    /// BuildEvaluationList(evaluationList, referrer, moduleRequests, previouslyImportedNames, onDeferredNamespace): the
    /// modules to evaluate for the requests, in depth-first pre-order, and for the <c>export defer</c> declarations that
    /// provide the names they import. The asynchronous dependencies of the deferred modules are gathered, or the deferred
    /// requests are ignored, depending on <paramref name="onDeferredNamespace"/>.
    /// </summary>
    private static void BuildEvaluationList(List<JsModule> evaluationList, JsSourceTextModule referrer, List<ModuleRequest> moduleRequests, PreviouslyImportedNames previouslyImportedNames, DeferredNamespaceHandling onDeferredNamespace)
    {
        foreach (var request in moduleRequests)
        {
            if (request.Phase == ModulePhase.Source || (request.Phase == ModulePhase.Defer && onDeferredNamespace == DeferredNamespaceHandling.Ignore))
                continue;

            var required = referrer.GetImportedModule(request);
            if (request.Phase == ModulePhase.Defer)
            {
                // The names requested while gathering the asynchronous dependencies are recorded in a copy: a later request
                // with the evaluation phase for the same names must still add their modules to the list
                AddUnique(evaluationList, GatherAsynchronousTransitiveDependencies(required, [], previouslyImportedNames.Copy()));
            }
            else
            {
                AddUnique(evaluationList, required);
            }

            if (required is not JsSourceTextModule requiredCyclic)
                continue;

            previouslyImportedNames.TryAdd(requiredCyclic);
            if (onDeferredNamespace == DeferredNamespaceHandling.GatherAsyncDependencies && request.ImportedNames.Namespace is not { Count: 0 })
            {
                // The export defer declarations reached through a namespace object are evaluated on access: only their
                // asynchronous dependencies are evaluated now
                var namespaceRequests = GetNewOptionalIndirectExportsModuleRequests(requiredCyclic, new ImportedNames([], request.ImportedNames.Namespace), previouslyImportedNames);
                if (namespaceRequests.Count > 0)
                {
                    AddUnique(evaluationList, GatherAsynchronousTransitiveDependenciesForRequests(requiredCyclic, namespaceRequests, [], previouslyImportedNames.Copy()));
                }
            }

            var optionalRequests = GetNewOptionalIndirectExportsModuleRequests(requiredCyclic, new ImportedNames(request.ImportedNames.Direct, []), previouslyImportedNames);
            BuildEvaluationList(evaluationList, requiredCyclic, optionalRequests, previouslyImportedNames, onDeferredNamespace);
        }
    }

    /// <summary>
    /// GetNewOptionalIndirectExportsModuleRequests(module, importedNames, previouslyImportedNames): the requests of the
    /// <c>export defer</c> declarations for the names that were not already requested from the module, which are recorded.
    /// </summary>
    private static List<ModuleRequest> GetNewOptionalIndirectExportsModuleRequests(JsSourceTextModule module, ImportedNames importedNames, PreviouslyImportedNames previouslyImportedNames)
    {
        previouslyImportedNames.TryAdd(module);

        // The names requested from a module without optional entries are not used: its exports are validated by its own
        // InitializeEnvironment
        if (module.OptionalIndirectExportEntries.Count == 0)
            return [];

        var previous = previouslyImportedNames.Get(module);
        var newDirect = ImportedNames.Exclude(importedNames.Direct, previous.Direct);
        var newNamespace = ImportedNames.Exclude(importedNames.Namespace, previous.Namespace);
        previous.Direct = ImportedNames.Merge(previous.Direct, newDirect);
        previous.Namespace = ImportedNames.Merge(previous.Namespace, newNamespace);
        var names = ImportedNames.Merge(newDirect, newNamespace);
        if (names is { Count: 0 })
            return [];

        return module.GetOptionalIndirectExportsModuleRequests(names);
    }

    /// <summary>
    /// GatherAsynchronousTransitiveDependencies(module, [seen], [previouslyImportedNames]): the modules with a top-level
    /// await that a deferred module depends on, including through the export defer declarations that it uses.
    /// </summary>
    private static List<JsModule> GatherAsynchronousTransitiveDependencies(JsModule module, List<JsModule>? seen = null, PreviouslyImportedNames? previouslyImportedNames = null)
    {
        seen ??= [];
        if (seen.Contains(module))
            return [];

        seen.Add(module);
        if (module is not JsSourceTextModule cyclic || cyclic.Status == JsModuleStatus.Evaluating || IsModuleSccEvaluated(cyclic))
            return [];

        if (cyclic.HasTopLevelAwait)
            return [cyclic];

        return GatherAsynchronousTransitiveDependenciesForRequests(cyclic, cyclic.RequestedModules, seen, previouslyImportedNames ?? new PreviouslyImportedNames());
    }

    /// <summary>GatherAsynchronousTransitiveDependenciesForRequests(referrer, moduleRequests, seen, previouslyImportedNames)</summary>
    private static List<JsModule> GatherAsynchronousTransitiveDependenciesForRequests(JsSourceTextModule referrer, List<ModuleRequest> moduleRequests, List<JsModule> seen, PreviouslyImportedNames previouslyImportedNames)
    {
        var result = new List<JsModule>();
        foreach (var request in moduleRequests)
        {
            if (request.Phase == ModulePhase.Source)
                continue;

            var required = referrer.GetImportedModule(request);
            AddUnique(result, GatherAsynchronousTransitiveDependencies(required, seen, previouslyImportedNames));
            if (required is JsSourceTextModule requiredCyclic)
            {
                var optionalRequests = GetNewOptionalIndirectExportsModuleRequests(requiredCyclic, request.ImportedNames, previouslyImportedNames);
                AddUnique(result, GatherAsynchronousTransitiveDependenciesForRequests(requiredCyclic, optionalRequests, seen, previouslyImportedNames));
            }
        }

        return result;
    }

    /// <summary>IsModuleSCCEvaluated(module): whether the whole strongly connected component of the module is evaluated.</summary>
    private static bool IsModuleSccEvaluated(JsSourceTextModule module) => (module.CycleRoot ?? module).Status == JsModuleStatus.Evaluated;

    /// <summary>
    /// ReadyForSyncExecution(module, importedNames, onDeferredNamespace, [seen]): whether a module, and the export defer
    /// declarations that provide <paramref name="importedNames"/> (null for all of them), can be evaluated synchronously.
    /// </summary>
    private static bool ReadyForSyncExecution(JsModule module, List<string>? importedNames, DeferredNamespaceHandling onDeferredNamespace, PreviouslyImportedNames? seen = null)
    {
        if (module is not JsSourceTextModule cyclic)
            return true;

        seen ??= new PreviouslyImportedNames();
        if (seen.TryAdd(cyclic) && !IsModuleSccEvaluated(cyclic))
        {
            if (cyclic.Status is JsModuleStatus.Evaluating or JsModuleStatus.EvaluatingAsync || cyclic.HasTopLevelAwait)
                return false;

            var seenForAsyncDependencies = onDeferredNamespace == DeferredNamespaceHandling.GatherAsyncDependencies ? seen : new PreviouslyImportedNames();
            foreach (var request in cyclic.RequestedModules)
            {
                if (request.Phase != ModulePhase.Source && !ReadyForSyncExecution(cyclic.GetImportedModule(request), request.ImportedNames.All, DeferredNamespaceHandling.GatherAsyncDependencies, seenForAsyncDependencies))
                    return false;
            }
        }

        foreach (var request in GetNewOptionalIndirectExportsModuleRequests(cyclic, new ImportedNames(importedNames, []), seen))
        {
            if (request.Phase == ModulePhase.Evaluation)
            {
                if (!ReadyForSyncExecution(cyclic.GetImportedModule(request), request.ImportedNames.All, onDeferredNamespace, seen))
                    return false;
            }
            else if (onDeferredNamespace == DeferredNamespaceHandling.GatherAsyncDependencies)
            {
                if (GatherAsynchronousTransitiveDependenciesForRequests(cyclic, [request], [], seen.Copy()).Count > 0)
                    return false;
            }
            else if (GatherAsynchronousTransitiveDependencies(cyclic.GetImportedModule(request)).Count > 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// EvaluateModuleSync(module, [importedNames]): evaluates a module synchronously (the module of a deferred namespace on
    /// access, or of an export defer declaration), with the export defer declarations that provide the direct imported names.
    /// </summary>
    public static void EvaluateModuleSync(JsModule module, ImportedNames? importedNames = null)
    {
        importedNames ??= ImportedNames.Empty();
        if (!ReadyForSyncExecution(module, importedNames.All, DeferredNamespaceHandling.Ignore))
            ThrowHelper.ThrowTypeError("The deferred module '" + module.Url + "' cannot be evaluated synchronously: it or one of its dependencies is being evaluated or has a top-level await");

        var evaluationList = new List<JsModule> { module };
        if (module is JsSourceTextModule cyclic)
        {
            var previouslyImportedNames = new PreviouslyImportedNames();
            previouslyImportedNames.TryAdd(cyclic);
            var optionalRequests = GetNewOptionalIndirectExportsModuleRequests(cyclic, new ImportedNames(importedNames.Direct, []), previouslyImportedNames);
            BuildEvaluationList(evaluationList, cyclic, optionalRequests, previouslyImportedNames, DeferredNamespaceHandling.Ignore);
        }

        foreach (var moduleToEvaluate in evaluationList)
        {
            var promise = (JsPromise)Evaluate(moduleToEvaluate).Cell!;
            if (promise.State == PromiseState.Rejected)
            {
                if (!promise.IsHandled)
                {
                    JsEngine.Current!.TrackPromiseRejection(promise, JsPromiseRejectionOperation.Handle);
                }

                promise.IsHandled = true;
                throw new JsException(promise.Result);
            }
        }
    }

    /// <summary>
    /// EnsureDeferredExportEvaluated(module, exportName, seen): evaluates the <c>export defer</c> declaration that provides
    /// an export of a namespace object, on access (https://tc39.es/proposal-deferred-reexports/).
    /// </summary>
    public static void EnsureDeferredExportEvaluated(JsSourceTextModule module, string exportName, List<JsSourceTextModule>? seen = null)
    {
        if (module.OptionalIndirectExportEntries.Count == 0)
            return;

        seen ??= [];
        if (seen.Contains(module))
            return;

        seen.Add(module);
        foreach (var request in module.GetOptionalIndirectExportsModuleRequests([exportName]))
        {
            if (request.Phase != ModulePhase.Evaluation)
                continue;

            var requested = module.GetImportedModule(request);
            if (request.IsStarExport)
            {
                if (requested is JsSourceTextModule requestedCyclic)
                {
                    EnsureDeferredExportEvaluated(requestedCyclic, exportName, seen);
                }
            }
            else
            {
                EvaluateModuleSync(requested, request.ImportedNames);
            }
        }
    }

    /// <summary>ExecuteModule() without a capability: runs the body of a module that has no top-level await.</summary>
    private static void ExecuteModule(JsSourceTextModule module)
    {
        var frame = new Frame(module.Code.Body, module.Realm, module.Environment!, [])
        {
            Script = module,
        };

        JsInterpreter.Run(frame);
    }

    /// <summary>ExecuteAsyncModule(module)</summary>
    private static void ExecuteAsyncModule(JsSourceTextModule module)
    {
        var capability = PromiseCapability.CreateIntrinsic(module.Realm);
        JsPromise.PerformPromiseThen(
            (JsPromise)capability.Promise,
            static (state, _) => AsyncModuleExecutionFulfilled((JsSourceTextModule)state!),
            static (state, error) => AsyncModuleExecutionRejected((JsSourceTextModule)state!, error),
            module);

        var frame = new Frame(module.Code.Body, module.Realm, module.Environment!, [])
        {
            Script = module,
        };

        JsInterpreter.StartAsyncBlock(frame, capability, module);
    }

    /// <summary>GatherAvailableAncestors(module, execList)</summary>
    private static void GatherAvailableAncestors(JsSourceTextModule module, List<JsSourceTextModule> execList)
    {
        foreach (var m in module.AsyncParentModules)
        {
            if (!execList.Contains(m) && !m.CycleRoot!.HasEvaluationError)
            {
                m.PendingAsyncDependencies--;
                if (m.PendingAsyncDependencies == 0)
                {
                    execList.Add(m);
                    if (!m.HasTopLevelAwait)
                    {
                        GatherAvailableAncestors(m, execList);
                    }
                }
            }
        }
    }

    /// <summary>AsyncModuleExecutionFulfilled(module)</summary>
    private static void AsyncModuleExecutionFulfilled(JsSourceTextModule module)
    {
        if (module.Status == JsModuleStatus.Evaluated)
            return;

        module.AsyncEvaluationOrder = -1;
        module.Status = JsModuleStatus.Evaluated;
        module.TopLevelCapability?.Resolve(default);

        var execList = new List<JsSourceTextModule>();
        GatherAvailableAncestors(module, execList);
        execList.Sort(static (a, b) => a.AsyncEvaluationOrder.CompareTo(b.AsyncEvaluationOrder));
        for (var i = 0; i < execList.Count; i++)
        {
            var m = execList[i];
            if (m.Status == JsModuleStatus.Evaluated)
                continue;

            try
            {
                if (m.HasTopLevelAwait)
                {
                    ExecuteAsyncModule(m);
                    continue;
                }

                ExecuteModule(m);
            }
            catch (JsException ex) when (!m.HasTopLevelAwait)
            {
                AsyncModuleExecutionRejected(m, ex.Value);
                continue;
            }
            catch (Exception ex) when (ex is not JsException)
            {
                // A termination or a .NET exception: the modules that were ready to run will not run
                var error = JsInterpreter.CreateAbortError(m.Realm, ex);
                for (var j = i; j < execList.Count; j++)
                {
                    AsyncModuleExecutionRejected(execList[j], error);
                }

                throw;
            }

            m.AsyncEvaluationOrder = -1;
            m.Status = JsModuleStatus.Evaluated;
            m.TopLevelCapability?.Resolve(default);
        }
    }

    /// <summary>AsyncModuleExecutionRejected(module, error)</summary>
    internal static void AsyncModuleExecutionRejected(JsSourceTextModule module, JsValue error)
    {
        if (module.Status == JsModuleStatus.Evaluated)
            return;

        module.EvaluationError = error;
        module.HasEvaluationError = true;
        module.Status = JsModuleStatus.Evaluated;
        module.AsyncEvaluationOrder = -1;

        // The promise of the module is rejected before the ones of the modules that depend on it
        module.TopLevelCapability?.Reject(error);
        foreach (var m in module.AsyncParentModules)
        {
            AsyncModuleExecutionRejected(m, error);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Namespaces, import() and import.meta
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The value of a namespace import (GetModuleNamespace) or of a source phase import (GetModuleSource).</summary>
    public static JsValue GetModuleNamespaceOrSource(JsModule module, ModulePhase phase, ErrorType missingSourceError = ErrorType.SyntaxError) => phase == ModulePhase.Source ? GetModuleSource(module, missingSourceError) : GetModuleNamespace(module, phase, importedNames: null);

    /// <summary>
    /// The [[ModuleSource]] of a module (e.g. a WebAssembly.Module), set by the host. The source of a JavaScript module or of a
    /// JSON module is not available: a SyntaxError is thrown (a ReferenceError for the [[Get]] of a namespace object).
    /// </summary>
    public static JsValue GetModuleSource(JsModule module, ErrorType missingSourceError = ErrorType.SyntaxError)
    {
        if (module.ModuleSource is { } moduleSource)
            return moduleSource;

        var url = module.Url is null ? "the module" : "the module '" + module.Url + "'";
        throw ThrowHelper.CreateException(missingSourceError, "The source phase import of " + url + " is not supported: it has no source representation");
    }

    /// <summary>
    /// GetModuleNamespace(module, phase, importedNames): the namespace object of the module, or a filtered namespace with
    /// some of its exports (https://tc39.es/proposal-deferred-reexports/), which is a new object for each call.
    /// </summary>
    public static JsObject GetModuleNamespace(JsModule module, ModulePhase phase = ModulePhase.Evaluation, List<string>? importedNames = null)
    {
        var deferred = phase == ModulePhase.Defer;
        if (importedNames is null && (deferred ? module.DeferredNamespace : module.Namespace) is { } existing)
            return existing;

        var names = new List<string>();
        foreach (var name in module.GetExportedNames([]))
        {
            // A deferred namespace is not a thenable
            if (!ImportedNames.Contains(importedNames, name) || (deferred && name == "then"))
                continue;

            var resolution = module.ResolveExport(name, []);
            if (resolution is not null && resolution != ResolvedBinding.Ambiguous)
            {
                names.Add(name);
            }
        }

        var ns = new ModuleNamespaceObject(module, names, deferred);
        if (importedNames is null)
        {
            if (deferred)
            {
                module.DeferredNamespace = ns;
            }
            else
            {
                module.Namespace = ns;
            }
        }

        return ns;
    }

    /// <summary>EvaluateImportCall: the promise of <c>import(specifier, options)</c> or <c>import.defer(specifier, options)</c>.</summary>
    public static JsValue EvaluateImportCall(JsRealm realm, IScriptOrModule? referrer, JsValue specifier, JsValue options, ModulePhase phase = ModulePhase.Evaluation)
    {
        var capability = PromiseCapability.CreateIntrinsic(realm);
        ModuleRequest request;
        try
        {
            var specifierString = JsOperations.ToString(specifier);
            var attributes = new List<KeyValuePair<string, string>>();
            if (!options.IsUndefined)
            {
                if (options.TryGetObject() is not { } optionsObject)
                    return Reject(capability, realm, "The second argument of import() must be an object");

                var attributesValue = optionsObject.Get(WithKey);
                if (!attributesValue.IsUndefined)
                {
                    if (attributesValue.TryGetObject() is not { } attributesObject)
                        return Reject(capability, realm, "The 'with' option of import() must be an object");

                    foreach (var key in PropertyOperations.EnumerableOwnProperties(attributesObject, EnumerableOwnPropertiesKind.Key))
                    {
                        var value = attributesObject.Get(new PropertyKey(key.AsString()));
                        if (!value.IsString)
                            return Reject(capability, realm, "The value of the import attribute '" + key.AsString() + "' must be a string");

                        attributes.Add(new(key.AsString(), value.AsString()));
                    }
                }
            }

            if (!AllImportAttributesSupported(attributes))
                return Reject(capability, realm, "Unsupported import attribute");

            // The exports option lists the names of a filtered namespace (https://tc39.es/proposal-deferred-reexports/)
            List<string>? importedNames = null;
            if (options.TryGetObject() is { } optionsObj && optionsObj.Get(ExportsKey) is { IsUndefined: false } exports)
            {
                if (!exports.IsObject)
                    return Reject(capability, realm, "The 'exports' option of import() must be an iterable object");

                importedNames = [];
                var iterator = IteratorOperations.GetIterator(exports);
                while (IteratorOperations.IteratorStepValue(iterator, out var next))
                {
                    if (!next.IsString)
                    {
                        var error = realm.CreateError(ErrorType.TypeError, "The names of the 'exports' option of import() must be strings");
                        IteratorOperations.IteratorCloseOnThrow(iterator);
                        capability.Reject(error);
                        return capability.Promise;
                    }

                    if (!importedNames.Contains(next.AsString(), StringComparer.Ordinal))
                    {
                        importedNames.Add(next.AsString());
                    }
                }
            }

            request = ModuleRequest.Create(specifierString, attributes, phase, new ImportedNames([], importedNames));
        }
        catch (JsException ex)
        {
            capability.Reject(ex.Value);
            return capability.Promise;
        }

        HostLoadImportedModule(referrer?.Realm ?? realm, realm, referrer, request, hostDefined: null, new DynamicImportPayload(capability, phase));
        return capability.Promise;
    }

    private static JsValue Reject(PromiseCapability capability, JsRealm realm, string message)
    {
        capability.Reject(realm.CreateError(ErrorType.TypeError, message));
        return capability.Promise;
    }

    /// <summary>ContinueDynamicImport(payload, moduleCompletion, importedNames)</summary>
    private static void ContinueDynamicImport(DynamicImportPayload payload, JsModule? module, JsValue error, List<string>? importedNames)
    {
        var capability = payload.Capability;
        if (module is null)
        {
            capability.Reject(error);
            return;
        }

        // import.source(): the module is not linked nor evaluated, and its dependencies are not loaded
        if (payload.Phase == ModulePhase.Source)
        {
            try
            {
                capability.Resolve(GetModuleSource(module));
            }
            catch (JsException ex)
            {
                capability.Reject(ex.Value);
            }

            return;
        }

        var loadPromise = (JsPromise)LoadRequestedModules(module, hostDefined: null, importedNames).Cell!;
        JsPromise.PerformPromiseThen(loadPromise, static (state, _) => LinkAndEvaluate((DynamicImportState)state!), static (state, reason) => ((DynamicImportState)state!).Capability.Reject(reason), new DynamicImportState(capability, module, payload.Phase, importedNames));
    }

    private static void LinkAndEvaluate(DynamicImportState state)
    {
        var module = state.Module;
        try
        {
            Link(module, state.ImportedNames);
        }
        catch (JsException ex)
        {
            state.Capability.Reject(ex.Value);
            return;
        }

        // The names of a filtered namespace must be exported
        if (state.ImportedNames is { } importedNames)
        {
            foreach (var name in importedNames)
            {
                var resolution = module.ResolveExport(name, []);
                if (resolution is null || resolution == ResolvedBinding.Ambiguous)
                {
                    state.Capability.Reject(module.Realm.CreateError(ErrorType.ReferenceError, GetResolutionError(name, resolution, module)));
                    return;
                }
            }
        }

        // import.defer(): only the asynchronous dependencies are evaluated. The export defer declarations are evaluated on
        // access to the namespace, only their asynchronous dependencies are evaluated now
        var evaluationList = state.Phase == ModulePhase.Defer ? GatherAsynchronousTransitiveDependencies(module) : [module];
        if (module is JsSourceTextModule cyclic)
        {
            var optionalRequests = cyclic.GetOptionalIndirectExportsModuleRequests(state.ImportedNames);
            AddUnique(evaluationList, GatherAsynchronousTransitiveDependenciesForRequests(cyclic, optionalRequests, [], new PreviouslyImportedNames()));
        }

        if (evaluationList.Count == 0)
        {
            state.Capability.Resolve(GetModuleNamespace(module, state.Phase, state.ImportedNames));
            return;
        }

        JsPromise evaluatePromise;
        if (evaluationList.Count == 1)
        {
            evaluatePromise = (JsPromise)Evaluate(evaluationList[0]).Cell!;
        }
        else
        {
            // SafePerformPromiseAll of the promises of the evaluations
            var all = PromiseCapability.CreateIntrinsic(module.Realm);
            var allState = new PromiseAllState(all, evaluationList.Count);
            foreach (var dependency in evaluationList)
            {
                JsPromise.PerformPromiseThen(
                    (JsPromise)Evaluate(dependency).Cell!,
                    static (s, _) =>
                    {
                        var promiseAll = (PromiseAllState)s!;
                        if (--promiseAll.Remaining == 0)
                        {
                            promiseAll.Capability.Resolve(default);
                        }
                    },
                    static (s, reason) => ((PromiseAllState)s!).Capability.Reject(reason),
                    allState);
            }

            evaluatePromise = (JsPromise)all.Promise;
        }

        JsPromise.PerformPromiseThen(
            evaluatePromise,
            static (s, _) =>
            {
                var importState = (DynamicImportState)s!;
                importState.Capability.Resolve(GetModuleNamespace(importState.Module, importState.Phase, importState.ImportedNames));
            },
            static (s, reason) => ((DynamicImportState)s!).Capability.Reject(reason),
            state);
    }

    private sealed record DynamicImportState(PromiseCapability Capability, JsModule Module, ModulePhase Phase, List<string>? ImportedNames);

    private sealed class PromiseAllState(PromiseCapability capability, int remaining)
    {
        public PromiseCapability Capability { get; } = capability;

        public int Remaining { get; set; } = remaining;
    }

    /// <summary>The import.meta object of a module (created on first use).</summary>
    public static JsObject GetImportMeta(JsSourceTextModule module)
    {
        if (module.ImportMeta is { } existing)
            return existing;

        var importMeta = new JsObject(prototype: null);
        module.ImportMeta = importMeta;
        module.Realm.Engine.Options.ModuleLoader?.InitializeImportMeta(module, importMeta);
        return importMeta;
    }
}
