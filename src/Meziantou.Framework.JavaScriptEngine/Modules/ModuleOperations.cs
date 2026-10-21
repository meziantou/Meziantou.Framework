using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine.Modules;

/// <summary>The state of LoadRequestedModules (GraphLoadingState Record).</summary>
internal sealed class GraphLoadingState(PromiseCapability capability, object? hostDefined)
{
    public PromiseCapability Capability { get; } = capability;

    public bool IsLoading { get; set; } = true;

    public int PendingModulesCount { get; set; } = 1;

    public HashSet<JsSourceTextModule> Visited { get; } = [];

    public object? HostDefined { get; } = hostDefined;
}

/// <summary>The algorithms of the module graphs (https://tc39.es/ecma262/#sec-cyclic-module-records): loading, linking and evaluation.</summary>
internal static class ModuleOperations
{
    private static readonly PropertyKey WithKey = new("with");

    // ------------------------------------------------------------------------------------------------------------------
    // Loading
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>LoadRequestedModules([hostDefined])</summary>
    public static JsValue LoadRequestedModules(JsModule module, object? hostDefined)
    {
        var capability = PromiseCapability.CreateIntrinsic(module.Realm);
        if (module is not JsSourceTextModule)
        {
            capability.Resolve(default);
            return capability.Promise;
        }

        var state = new GraphLoadingState(capability, hostDefined);
        InnerModuleLoading(state, module);
        return capability.Promise;
    }

    /// <summary>InnerModuleLoading(state, module)</summary>
    private static void InnerModuleLoading(GraphLoadingState state, JsModule module)
    {
        if (module is JsSourceTextModule { Status: JsModuleStatus.New } cyclic && state.Visited.Add(cyclic))
        {
            state.PendingModulesCount += cyclic.RequestedModules.Count;
            foreach (var request in cyclic.RequestedModules)
            {
                if (!AllImportAttributesSupported(request))
                {
                    ContinueModuleLoading(state, module: null, module.Realm.CreateError(ErrorType.SyntaxError, "Unsupported import attribute"));
                }
                else if (FindLoadedModule(cyclic, request) is { } loaded)
                {
                    InnerModuleLoading(state, loaded);
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
            foreach (var loaded in state.Visited)
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
    private static void ContinueModuleLoading(GraphLoadingState state, JsModule? module, JsValue error)
    {
        if (!state.IsLoading)
            return;

        if (module is not null)
        {
            InnerModuleLoading(state, module);
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
    private static bool AllImportAttributesSupported(ModuleRequest request)
    {
        foreach (var attribute in request.Attributes)
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

        loader.LoadImportedModule(loadRequest);
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
            ContinueModuleLoading(state, module, error);
        }
        else
        {
            ContinueDynamicImport((PromiseCapability)payload, module, error);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Linking
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Link()</summary>
    public static void Link(JsModule module)
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
        catch (JsException)
        {
            foreach (var m in stack)
            {
                m.Status = JsModuleStatus.Unlinked;
            }

            throw;
        }
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
        foreach (var request in cyclic.RequestedModules)
        {
            var required = cyclic.GetImportedModule(request);
            index = InnerModuleLinking(required, stack, index);
            if (required is JsSourceTextModule { Status: JsModuleStatus.Linking } requiredCyclic)
            {
                cyclic.DfsAncestorIndex = Math.Min(cyclic.DfsAncestorIndex, requiredCyclic.DfsAncestorIndex);
            }
        }

        InitializeEnvironment(cyclic);
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

    /// <summary>InitializeEnvironment() of a Source Text Module Record.</summary>
    private static void InitializeEnvironment(JsSourceTextModule module)
    {
        foreach (var entry in module.IndirectExportEntries)
        {
            var resolution = module.ResolveExport(entry.ExportName!, []);
            if (resolution is null || resolution == ResolvedBinding.Ambiguous)
                ThrowHelper.ThrowSyntaxError(GetResolutionError(entry.ExportName!, resolution, module));
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
                environment.Slots[slot] = GetModuleNamespace(imported);
                continue;
            }

            var resolution = imported.ResolveExport(entry.ImportName!, []);
            if (resolution is null || resolution == ResolvedBinding.Ambiguous)
                ThrowHelper.ThrowSyntaxError(GetResolutionError(entry.ImportName!, resolution, imported));

            environment.Slots[slot] = resolution.IsNamespace
                ? GetModuleNamespace(resolution.Module)
                : new JsValue(new ImportBindingCell(resolution.Module, resolution.Module.GetBindingSlot(resolution.BindingName!)));
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
            foreach (var m in stack)
            {
                m.Status = JsModuleStatus.Evaluated;
                m.EvaluationError = ex.Value;
                m.EvaluationErrorLocation = ex.Location;
                m.HasEvaluationError = true;
            }

            capability.Reject(ex.Value);
            return capability.Promise;
        }

        if (!cyclic.IsAsyncEvaluation)
        {
            capability.Resolve(default);
        }

        return capability.Promise;
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
        foreach (var request in cyclic.RequestedModules)
        {
            var required = cyclic.GetImportedModule(request);
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

        JsInterpreter.StartAsyncBlock(frame, capability);
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
        foreach (var m in execList)
        {
            if (m.Status == JsModuleStatus.Evaluated)
                continue;

            if (m.HasTopLevelAwait)
            {
                ExecuteAsyncModule(m);
                continue;
            }

            try
            {
                ExecuteModule(m);
            }
            catch (JsException ex)
            {
                AsyncModuleExecutionRejected(m, ex.Value);
                continue;
            }

            m.AsyncEvaluationOrder = -1;
            m.Status = JsModuleStatus.Evaluated;
            m.TopLevelCapability?.Resolve(default);
        }
    }

    /// <summary>AsyncModuleExecutionRejected(module, error)</summary>
    private static void AsyncModuleExecutionRejected(JsSourceTextModule module, JsValue error)
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

    /// <summary>GetModuleNamespace(module)</summary>
    public static JsObject GetModuleNamespace(JsModule module)
    {
        if (module.Namespace is { } existing)
            return existing;

        var names = new List<string>();
        foreach (var name in module.GetExportedNames([]))
        {
            var resolution = module.ResolveExport(name, []);
            if (resolution is not null && resolution != ResolvedBinding.Ambiguous)
            {
                names.Add(name);
            }
        }

        var ns = new ModuleNamespaceObject(module, names);
        module.Namespace = ns;
        return ns;
    }

    /// <summary>EvaluateImportCall: the promise of <c>import(specifier, options)</c>.</summary>
    public static JsValue EvaluateImportCall(JsRealm realm, IScriptOrModule? referrer, JsValue specifier, JsValue options)
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

            request = ModuleRequest.Create(specifierString, attributes);
            if (!AllImportAttributesSupported(request))
                return Reject(capability, realm, "Unsupported import attribute");
        }
        catch (JsException ex)
        {
            capability.Reject(ex.Value);
            return capability.Promise;
        }

        HostLoadImportedModule(referrer?.Realm ?? realm, realm, referrer, request, hostDefined: null, capability);
        return capability.Promise;
    }

    private static JsValue Reject(PromiseCapability capability, JsRealm realm, string message)
    {
        capability.Reject(realm.CreateError(ErrorType.TypeError, message));
        return capability.Promise;
    }

    /// <summary>ContinueDynamicImport(promiseCapability, moduleCompletion)</summary>
    private static void ContinueDynamicImport(PromiseCapability capability, JsModule? module, JsValue error)
    {
        if (module is null)
        {
            capability.Reject(error);
            return;
        }

        var loadPromise = (JsPromise)LoadRequestedModules(module, hostDefined: null).Cell!;
        JsPromise.PerformPromiseThen(loadPromise, static (state, _) => LinkAndEvaluate((DynamicImportState)state!), static (state, reason) => ((DynamicImportState)state!).Capability.Reject(reason), new DynamicImportState(capability, module));
    }

    private static void LinkAndEvaluate(DynamicImportState state)
    {
        try
        {
            Link(state.Module);
        }
        catch (JsException ex)
        {
            state.Capability.Reject(ex.Value);
            return;
        }

        var evaluatePromise = (JsPromise)Evaluate(state.Module).Cell!;
        JsPromise.PerformPromiseThen(
            evaluatePromise,
            static (s, _) =>
            {
                var importState = (DynamicImportState)s!;
                importState.Capability.Resolve(GetModuleNamespace(importState.Module));
            },
            static (s, reason) => ((DynamicImportState)s!).Capability.Reject(reason),
            state);
    }

    private sealed record DynamicImportState(PromiseCapability Capability, JsModule Module);

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
