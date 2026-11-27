using Meziantou.Framework.WasmEngine.Components.Binary;
using Meziantou.Framework.WasmEngine.Components.Runtime;

namespace Meziantou.Framework.WasmEngine.Components;

/// <summary>
/// The steps of a function of the host: the arguments are converted to the result (null for a function without result),
/// or an exception is thrown, which aborts the call of the component.
/// </summary>
/// <param name="context">The call: the type of the function and the store.</param>
/// <param name="arguments">The arguments: one per parameter of the type of the function.</param>
public delegate ComponentValue? ComponentHostCallback(ComponentCallContext context, ReadOnlySpan<ComponentValue> arguments);

/// <summary>
/// The steps of an asynchronous function of the host. The continuations of the task run on the thread that runs the
/// components (between their calls), so they can use the values of the call, such as the streams.
/// </summary>
/// <param name="context">The call: the type of the function, the store, and the cancellation of the call.</param>
/// <param name="arguments">The arguments: one per parameter of the type of the function.</param>
public delegate ValueTask<ComponentValue?> ComponentHostAsyncCallback(ComponentCallContext context, ComponentValue[] arguments);

/// <summary>A call of a function of the host by a component.</summary>
public sealed class ComponentCallContext
{
    internal ComponentCallContext(ComponentFunctionType type, WasmStore store)
    {
        Type = type;
        Store = store;
    }

    /// <summary>Gets the type of the function, as the component imports it.</summary>
    public ComponentFunctionType Type { get; }

    /// <summary>Gets the store of the component.</summary>
    public WasmStore Store { get; }

    /// <summary>Gets the token that is cancelled when the component cancels an asynchronous call.</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Runs an operation of the host that continues after the call returns, such as a copy between a stream of a component
    /// and a .NET stream: the store waits for it when the components wait, and its continuations run on the thread of the store.
    /// </summary>
    /// <param name="operation">The operation.</param>
    public void RunInBackground(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var store = ((ManagedStore)Store).Components;
        store.BeginHostOperation();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(store.SynchronizationContext);
        Task task;
        try
        {
            task = operation();
        }
        catch
        {
            store.EndHostOperation();
            throw;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        _ = task.ContinueWith(_ => store.Post(_ => store.EndHostOperation(), null), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}

/// <summary>A compiled component (https://github.com/WebAssembly/component-model): it is instantiated by <see cref="WasmStore.InstantiateComponent"/>.</summary>
public sealed class WasmComponent
{
    internal WasmComponent(WasmRuntime runtime, byte[] bytes, ComponentAst ast, IReadOnlyDictionary<CoreModuleDefinition, WasmModule> modules)
    {
        Runtime = runtime;
        Bytes = bytes;
        Ast = ast;
        ModuleMap = modules;
        Imports = [.. ast.Definitions.OfType<ImportDefinition>().Select(import => new WasmComponentImport(import.Name, import.Type.Kind))];
        Exports = [.. ast.Definitions.OfType<ExportDefinition>().Select(export => new WasmComponentImport(export.Name, GetKind(export.Sort)))];
    }

    /// <summary>Gets the runtime that compiled the component.</summary>
    public WasmRuntime Runtime { get; }

    /// <summary>Gets the binary of the component.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>Gets the imports of the component: their names and kinds.</summary>
    public IReadOnlyList<WasmComponentImport> Imports { get; }

    /// <summary>Gets the exports of the component: their names and kinds.</summary>
    public IReadOnlyList<WasmComponentImport> Exports { get; }

    internal ComponentAst Ast { get; }

    /// <summary>The compiled core modules of the component and of its nested components.</summary>
    internal IReadOnlyDictionary<CoreModuleDefinition, WasmModule> ModuleMap { get; }

    internal static WasmComponent Compile(WasmRuntime runtime, byte[] bytes)
    {
        ComponentAst ast;
        try
        {
            ast = ComponentDecoder.Decode(bytes);
        }
        catch (WasmDecodeException ex)
        {
            throw new WasmCompileException(ex.Message, ex);
        }

        var modules = new Dictionary<CoreModuleDefinition, WasmModule>(ReferenceEqualityComparer.Instance);
        CompileModules(runtime, ast, modules);
        Validation.ComponentValidator.Validate(ast, modules);
        return new WasmComponent(runtime, bytes, ast, modules);
    }

    private static void CompileModules(WasmRuntime runtime, ComponentAst ast, Dictionary<CoreModuleDefinition, WasmModule> modules)
    {
        foreach (var definition in ast.Definitions)
        {
            switch (definition)
            {
                case CoreModuleDefinition module:
                    modules[module] = runtime.Compile(module.Bytes.Span);
                    break;

                case NestedComponentDefinition nested:
                    CompileModules(runtime, nested.Component, modules);
                    break;
            }
        }
    }

    internal WasmComponentInstance Instantiate(ManagedStore store, ComponentImports? imports)
    {
        var instantiator = new ComponentInstantiator(store, ModuleMap);
        var value = instantiator.Instantiate(Ast, definitionScope: null, name => imports?.Find(name));
        return new WasmComponentInstance(store, value);
    }

    internal static ComponentExternKind GetKind(ComponentSort sort) => sort switch
    {
        ComponentSort.CoreModule => ComponentExternKind.CoreModule,
        ComponentSort.Func => ComponentExternKind.Function,
        ComponentSort.Value => ComponentExternKind.Value,
        ComponentSort.Type => ComponentExternKind.Type,
        ComponentSort.Component => ComponentExternKind.Component,
        _ => ComponentExternKind.Instance,
    };
}

/// <summary>An import or an export of a component: its name and its kind.</summary>
/// <param name="Name">The name, such as <c>wasi:cli/stdout@0.2.0</c> or <c>run</c>.</param>
/// <param name="Kind">The kind of the definition.</param>
public readonly record struct WasmComponentImport(string Name, ComponentExternKind Kind);

/// <summary>An instance of a component, or an instance exported by a component: its exports.</summary>
public sealed class WasmComponentInstance
{
    internal WasmComponentInstance(ManagedStore store, ComponentInstanceValue value)
    {
        Store = store;
        Value = value;
    }

    internal ManagedStore Store { get; }

    internal ComponentInstanceValue Value { get; }

    /// <summary>Gets the names and the kinds of the exports.</summary>
    public IReadOnlyList<WasmComponentImport> Exports => [.. Value.Exports.Select(export => new WasmComponentImport(export.Name, WasmComponent.GetKind(export.Value.Sort)))];

    /// <summary>
    /// Returns an exported function, or null. A name of the form <c>instance#function</c> (such as
    /// <c>wasi:cli/run@0.2.0#run</c>) is a function of an exported instance.
    /// </summary>
    /// <param name="name">The name of the export.</param>
    public ComponentFunction? GetFunction(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Value.GetExport(name) is { Sort: ComponentSort.Func, Value: ComponentFunctionValue function })
            return new ComponentFunction(Store, function);

        var separator = name.LastIndexOf('#', StringComparison.Ordinal);
        if (separator > 0 && GetInstance(name[..separator]) is { } instance)
            return instance.GetFunction(name[(separator + 1)..]);

        return null;
    }

    /// <summary>
    /// Runs an asynchronous operation of the host on the current thread, as the thread of the store, and returns its
    /// result: while the operation waits for the components (the values of their streams and futures, the tasks that
    /// continue after a call returns), the store runs them, and the continuations of the operation run on this thread.
    /// The operation can call the functions of the components and use their streams.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="operation">The operation.</param>
    /// <returns>The result of the operation.</returns>
    /// <exception cref="WasmTrapException">A component trapped, or the components cannot complete the operation (a deadlock).</exception>
    /// <exception cref="PlatformNotSupportedException">The operation waits for the host on the browser.</exception>
    public T Run<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var store = Store.Components;
        store.ThrowIfLockedDown();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(store.SynchronizationContext);
        Task<T> task;

        try
        {
            task = operation();
        }
        catch
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            throw;
        }

        // The operation waits: it is a pending operation of the host, and the store waits for its continuations instead of
        // reporting a deadlock (its synchronous part, which can call the components, is not counted)
        if (!task.IsCompleted)
        {
            store.BeginHostOperation();
            _ = task.ContinueWith(_ => store.Post(_ => store.EndHostOperation(), null), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        try
        {
            store.RunUntil(() => task.IsCompleted, instance: null);
            store.RunParked();
            store.RunPosted();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        return task.GetAwaiter().GetResult();
    }

    /// <summary>Returns an exported instance, or null.</summary>
    /// <param name="name">The name of the export.</param>
    public WasmComponentInstance? GetInstance(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Value.GetExport(name) is { Sort: ComponentSort.Instance, Value: ComponentInstanceValue instance } ? new WasmComponentInstance(Store, instance) : null;
    }

    /// <summary>Returns an exported type (such as a resource type), or null.</summary>
    /// <param name="name">The name of the export.</param>
    public ComponentType? GetType(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Value.GetExport(name) is { Sort: ComponentSort.Type, Value: ComponentType type } ? type : null;
    }
}

/// <summary>
/// A function of the component model: a function exported by a component instance (<see cref="Call"/>), or a function of
/// the host (<see cref="FromCallback"/>, <see cref="FromAsyncCallback"/>) given to the imports of a component.
/// </summary>
public sealed class ComponentFunction
{
    internal ComponentFunction(ManagedStore store, ComponentFunctionValue value)
    {
        Store = store;
        Value = value;
    }

    private ComponentFunction(ComponentHostCallback? callback, ComponentHostAsyncCallback? asyncCallback, ComponentFunctionType? type)
    {
        Callback = callback;
        AsyncCallback = asyncCallback;
        DeclaredType = type;
    }

    internal ManagedStore? Store { get; }

    internal ComponentFunctionValue? Value { get; }

    internal ComponentHostCallback? Callback { get; }

    internal ComponentHostAsyncCallback? AsyncCallback { get; }

    internal ComponentFunctionType? DeclaredType { get; }

    /// <summary>Gets the type of the function: the type of an exported function, or the type given to a function of the host (null when it takes the type of the import).</summary>
    public ComponentFunctionType? Type => Value?.Type ?? DeclaredType;

    /// <summary>
    /// Creates a function of the host. Without <paramref name="type"/>, the function has the type of the import it is given
    /// to: the callback receives it in <see cref="ComponentCallContext.Type"/>.
    /// </summary>
    /// <param name="callback">The steps of the function.</param>
    /// <param name="type">The type of the function, which must be the type of the import, or null.</param>
    public static ComponentFunction FromCallback(ComponentHostCallback callback, ComponentFunctionType? type = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return new ComponentFunction(callback, null, type);
    }

    /// <summary>
    /// Creates an asynchronous function of the host: a component that calls it asynchronously continues while it runs. Without
    /// <paramref name="type"/>, the function has the type of the import it is given to.
    /// </summary>
    /// <param name="callback">The steps of the function.</param>
    /// <param name="type">The type of the function, which must be the type of the import, or null.</param>
    public static ComponentFunction FromAsyncCallback(ComponentHostAsyncCallback callback, ComponentFunctionType? type = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return new ComponentFunction(null, callback, type);
    }

    /// <summary>
    /// Calls an exported function: the arguments are converted to the memory of the component, and the result from it. The
    /// call runs the store until it returns, including the other asynchronous calls of the store and the operations of the
    /// host they wait for.
    /// </summary>
    /// <param name="arguments">The arguments, one per parameter.</param>
    /// <returns>The result, or null for a function without result.</returns>
    /// <exception cref="InvalidOperationException">The function is a function of the host.</exception>
    /// <exception cref="WasmTrapException">The component trapped.</exception>
    public ComponentValue? Call(params ComponentValue[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (Value is null || Store is null)
            throw new InvalidOperationException("A function of the host cannot be called: give it to the imports of a component");

        Store.ThrowIfDisposed();
        return Canon.Call(Store.Components, Value, arguments);
    }

    internal ComponentFunctionValue Bind(ComponentStoreState store, ComponentFunctionType type)
    {
        if (Value is not null)
            return Value;

        if (DeclaredType is not null && !DeclaredType.Equals(type))
            throw new WasmLinkException("The function of the host has the type " + DeclaredType + ", the import has the type " + type);

        return new ComponentFunctionValue(type, Callback is not null ? Canon.HostFunction(store, type, Callback) : Canon.HostAsyncFunction(store, type, AsyncCallback!));
    }
}

/// <summary>
/// The imports of a component, by name: functions, instances (nested <see cref="ComponentImports"/>, or instances of other
/// components), resource types, core modules and components. A name with a version (<c>wasi:cli/stdout@0.2.3</c>) also
/// satisfies the imports of a compatible version (<c>wasi:cli/stdout@0.2.0</c>).
/// </summary>
public sealed class ComponentImports
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    /// <summary>Gets the names of the definitions.</summary>
    public IEnumerable<string> Names => _values.Keys;

    /// <summary>Defines a function.</summary>
    public ComponentImports Define(string name, ComponentFunction function) => DefineCore(name, function);

    /// <summary>Defines an instance: the definitions of <paramref name="instance"/>.</summary>
    public ComponentImports Define(string name, ComponentImports instance) => DefineCore(name, instance);

    /// <summary>Defines an instance of a component.</summary>
    public ComponentImports Define(string name, WasmComponentInstance instance) => DefineCore(name, instance);

    /// <summary>Defines a type, such as a resource type of the host.</summary>
    public ComponentImports Define(string name, ComponentType type) => DefineCore(name, type);

    /// <summary>Defines a core module.</summary>
    public ComponentImports Define(string name, WasmModule module) => DefineCore(name, module);

    /// <summary>Defines a component.</summary>
    public ComponentImports Define(string name, WasmComponent component) => DefineCore(name, component);

    /// <summary>Defines all the exports of an instance of a component, with their names.</summary>
    public ComponentImports DefineAll(WasmComponentInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        foreach (var (name, value) in instance.Value.Exports)
        {
            _values[name] = new ExportedValue(instance.Store, value);
        }

        return this;
    }

    private ComponentImports DefineCore(string name, object value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _values[name] = value;
        return this;
    }

    /// <summary>Returns the definition of a name, or of the highest compatible version of the name.</summary>
    internal object? Find(string name)
    {
        if (_values.TryGetValue(name, out var value))
            return value;

        if (ComponentVersions.Split(name) is not ({ } package, { } version))
            return null;

        object? best = null;
        Version? bestVersion = null;
        foreach (var (key, candidate) in _values)
        {
            if (ComponentVersions.Split(key) is ({ } candidatePackage, { } candidateVersion) && string.Equals(candidatePackage, package, StringComparison.Ordinal) && ComponentVersions.IsCompatible(version, candidateVersion) && (bestVersion is null || candidateVersion > bestVersion))
            {
                best = candidate;
                bestVersion = candidateVersion;
            }
        }

        return best;
    }

    /// <summary>An export of an instance of a component, defined by <see cref="DefineAll"/>.</summary>
    internal sealed record ExportedValue(ManagedStore Store, ComponentExternValue Value);
}

/// <summary>The versions of the names of the imports and the exports (<c>package/interface@version</c>).</summary>
internal static class ComponentVersions
{
    public static (string? Name, Version? Version) Split(string name)
    {
        var index = name.LastIndexOf('@', StringComparison.Ordinal);
        if (index <= 0)
            return (null, null);

        var text = name[(index + 1)..];
        var prerelease = text.IndexOfAny(['-', '+']);
        if (prerelease >= 0)
            return (null, null);

        return Version.TryParse(text, out var version) && version.Build >= 0 ? (name[..index], version) : (null, null);
    }

    /// <summary>Whether a provided version satisfies a requested version: same major version (or same minor version for 0.x), and not older.</summary>
    public static bool IsCompatible(Version requested, Version provided)
    {
        if (requested.Major != provided.Major)
            return false;

        if (requested.Major == 0 && requested.Minor != provided.Minor)
            return false;

        if (requested.Major == 0 && requested.Minor == 0 && requested.Build != provided.Build)
            return false;

        return provided >= requested || requested.Major > 0 || requested.Minor > 0;
    }
}
