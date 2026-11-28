using Meziantou.Framework.WasmEngine.Components.Binary;

namespace Meziantou.Framework.WasmEngine.Components.Runtime;

/// <summary>Instantiates a component: evaluates its definitions in order, in the index spaces of a new component instance.</summary>
internal sealed class ComponentInstantiator
{
    private readonly ManagedStore _store;
    private readonly ComponentStoreState _components;
    private readonly IReadOnlyDictionary<CoreModuleDefinition, WasmModule> _modules;

    public ComponentInstantiator(ManagedStore store, IReadOnlyDictionary<CoreModuleDefinition, WasmModule> modules)
    {
        _store = store;
        _components = store.Components;
        _modules = modules;
    }

    /// <summary>Instantiates a component, with the imports that <paramref name="resolveImport"/> returns by name.</summary>
    public ComponentInstanceValue Instantiate(ComponentAst ast, ComponentScope? definitionScope, Func<string, object?> resolveImport)
    {
        var state = new ComponentInstanceState(_components);
        var scope = new ComponentScope(definitionScope, state);
        var exports = new List<(string, ComponentExternValue)>();

        // The core start functions and the realloc of the instantiation run in a synchronous task of the instance
        var task = new ComponentTask(new ComponentFunctionType([], null), CanonicalOptions.Default, state, () => [], _ => { });
        var thread = ComponentThread.CreateInline(task);
        var previous = ComponentThread.Current;
        ComponentThread.Current = thread;
        try
        {
            foreach (var definition in ast.Definitions)
            {
                Evaluate(definition, scope, resolveImport, exports);
            }
        }
        finally
        {
            thread.MarkExited();
            ComponentThread.Current = previous;
        }

        return new ComponentInstanceValue(state, exports);
    }

    private void Evaluate(ComponentDefinition definition, ComponentScope scope, Func<string, object?> resolveImport, List<(string, ComponentExternValue)> exports)
    {
        switch (definition)
        {
            case CoreModuleDefinition module:
                scope.CoreModules.Add(_modules[module]);
                break;

            case CoreInstanceDefinition coreInstance:
                scope.CoreInstances.Add(EvaluateCoreInstance(coreInstance, scope));
                break;

            case CoreTypeDefinition coreType:
                scope.CoreTypes.Add(coreType.Type);
                break;

            case NestedComponentDefinition nested:
                scope.Components.Add(new ComponentDefinitionValue(nested.Component, scope, compiled: null));
                break;

            case InstanceDefinition instance:
                scope.Instances.Add(EvaluateInstance(instance, scope));
                break;

            case AliasDefinition alias:
                EvaluateAlias(alias, scope);
                break;

            case TypeDefinition type:
                scope.Types.Add(EvaluateDefType(type.Type, scope, name: null));
                break;

            case CanonDefinition canon:
                EvaluateCanon(canon, scope);
                break;

            case ImportDefinition import:
                var declared = EvaluateExternType(import.Type, scope, import.Name);
                var provided = resolveImport(import.Name);
                if (provided is null && declared is { Kind: ComponentExternKind.Type, Type: { } knownType } && knownType is not ComponentResourceType { IsAbstract: true })
                {
                    // A type equal to a known type is the declared type: the host does not need to define the types that name the types of the functions
                    AddToScope(scope, ComponentSort.Type, knownType);
                    break;
                }

                AddToScope(scope, ToSort(declared.Kind), Bind(provided ?? throw new WasmLinkException("missing import named `" + import.Name + "`"), declared, import.Name));
                break;

            case ExportDefinition export:
                var value = GetFromScope(scope, export.Sort, export.Index);
                exports.Add((export.Name, new ComponentExternValue(export.Sort, value)));
                AddToScope(scope, export.Sort, value);
                break;

            case StartDefinition:
                throw new WasmLinkException("start definitions are not supported");
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Index spaces
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentSort ToSort(ComponentExternKind kind) => kind switch
    {
        ComponentExternKind.CoreModule => ComponentSort.CoreModule,
        ComponentExternKind.Function => ComponentSort.Func,
        ComponentExternKind.Value => ComponentSort.Value,
        ComponentExternKind.Type => ComponentSort.Type,
        ComponentExternKind.Component => ComponentSort.Component,
        _ => ComponentSort.Instance,
    };

    private static object GetFromScope(ComponentScope scope, ComponentSort sort, int index) => sort switch
    {
        ComponentSort.CoreFunc => ComponentScope.Get(scope.CoreFunctions, index, "core func"),
        ComponentSort.CoreTable => ComponentScope.Get(scope.CoreTables, index, "core table"),
        ComponentSort.CoreMemory => ComponentScope.Get(scope.CoreMemories, index, "core memory"),
        ComponentSort.CoreGlobal => ComponentScope.Get(scope.CoreGlobals, index, "core global"),
        ComponentSort.CoreTag => ComponentScope.Get(scope.CoreTags, index, "core tag"),
        ComponentSort.CoreType => ComponentScope.Get(scope.CoreTypes, index, "core type"),
        ComponentSort.CoreModule => ComponentScope.Get(scope.CoreModules, index, "module"),
        ComponentSort.CoreInstance => ComponentScope.Get(scope.CoreInstances, index, "core instance"),
        ComponentSort.Func => ComponentScope.Get(scope.Functions, index, "func"),
        ComponentSort.Type => ComponentScope.Get(scope.Types, index, "type"),
        ComponentSort.Component => ComponentScope.Get(scope.Components, index, "component"),
        ComponentSort.Instance => ComponentScope.Get(scope.Instances, index, "instance"),
        _ => throw new WasmLinkException("value definitions are not supported"),
    };

    private static void AddToScope(ComponentScope scope, ComponentSort sort, object value)
    {
        switch (sort)
        {
            case ComponentSort.CoreFunc:
                scope.CoreFunctions.Add((WasmFunction)value);
                break;
            case ComponentSort.CoreTable:
                scope.CoreTables.Add((WasmTable)value);
                break;
            case ComponentSort.CoreMemory:
                scope.CoreMemories.Add((WasmMemory)value);
                break;
            case ComponentSort.CoreGlobal:
                scope.CoreGlobals.Add((WasmGlobal)value);
                break;
            case ComponentSort.CoreTag:
                scope.CoreTags.Add((WasmTag)value);
                break;
            case ComponentSort.CoreType:
                scope.CoreTypes.Add((CoreTypeExpr)value);
                break;
            case ComponentSort.CoreModule:
                scope.CoreModules.Add((WasmModule)value);
                break;
            case ComponentSort.CoreInstance:
                scope.CoreInstances.Add((CoreInstanceValue)value);
                break;
            case ComponentSort.Func:
                scope.Functions.Add((ComponentFunctionValue)value);
                break;
            case ComponentSort.Type:
                scope.Types.Add((ComponentType)value);
                break;
            case ComponentSort.Component:
                scope.Components.Add((ComponentDefinitionValue)value);
                break;
            case ComponentSort.Instance:
                scope.Instances.Add((ComponentInstanceValue)value);
                break;
            default:
                throw new WasmLinkException("value definitions are not supported");
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Instances and aliases
    // ------------------------------------------------------------------------------------------------------------

    private CoreInstanceValue EvaluateCoreInstance(CoreInstanceDefinition definition, ComponentScope scope)
    {
        if (definition.Exports is { } inlineExports)
        {
            var values = new Dictionary<string, WasmExtern>(StringComparer.Ordinal);
            foreach (var (name, sort, index) in inlineExports)
            {
                if (GetFromScope(scope, sort, index) is not WasmExtern value)
                    throw new WasmLinkException("the export `" + name + "` of a core instance is not a core definition");

                if (!values.TryAdd(name, value))
                    throw new WasmLinkException("duplicate core instance export name `" + name + "`");
            }

            return new CoreInstanceValue(values);
        }

        var module = ComponentScope.Get(scope.CoreModules, definition.ModuleIndex, "module");
        var arguments = new Dictionary<string, CoreInstanceValue>(StringComparer.Ordinal);
        foreach (var (name, instanceIndex) in definition.Arguments!)
        {
            if (!arguments.TryAdd(name, ComponentScope.Get(scope.CoreInstances, instanceIndex, "core instance")))
                throw new WasmLinkException("duplicate module instantiation argument named `" + name + "`");
        }

        var imports = new List<WasmExtern>(module.Imports.Count);
        foreach (var import in module.Imports)
        {
            if (!arguments.TryGetValue(import.Module, out var argument))
                throw new WasmLinkException("module instantiation argument `" + import.Module + "` not provided");

            if (!argument.Exports.TryGetValue(import.Name, out var value))
                throw new WasmLinkException("module instantiation argument `" + import.Module + "` does not export an item named `" + import.Name + "`");

            imports.Add(value);
        }

        var instance = _store.Instantiate(module, imports);
        var exports = new Dictionary<string, WasmExtern>(StringComparer.Ordinal);
        foreach (var (name, value) in instance.Exports)
        {
            exports[name] = value;
        }

        return new CoreInstanceValue(exports);
    }

    private ComponentInstanceValue EvaluateInstance(InstanceDefinition definition, ComponentScope scope)
    {
        if (definition.Exports is { } inlineExports)
        {
            var exports = new List<(string, ComponentExternValue)>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (name, sort, index) in inlineExports)
            {
                if (!names.Add(name))
                    throw new WasmLinkException("export name `" + name + "` conflicts with previous name `" + name + "`");

                exports.Add((name, new ComponentExternValue(sort, GetFromScope(scope, sort, index))));
            }

            return new ComponentInstanceValue(null, exports);
        }

        var component = ComponentScope.Get(scope.Components, definition.ComponentIndex, "component");
        var arguments = new Dictionary<string, ComponentExternValue>(StringComparer.Ordinal);
        foreach (var (name, sort, index) in definition.Arguments!)
        {
            if (!arguments.TryAdd(name, new ComponentExternValue(sort, GetFromScope(scope, sort, index))))
                throw new WasmLinkException("instantiation argument `" + name + "` conflicts with previous argument `" + name + "`");
        }

        var modules = component.Compiled?.ModuleMap ?? _modules;
        return new ComponentInstantiator(_store, modules).Instantiate(component.Ast, component.DefinitionScope, name => arguments.TryGetValue(name, out var value) ? value : null);
    }

    private static void EvaluateAlias(AliasDefinition alias, ComponentScope scope)
    {
        switch (alias.Kind)
        {
            case AliasKind.Export:
                var instance = ComponentScope.Get(scope.Instances, alias.Index, "instance");
                var export = instance.GetExport(alias.Name!) ?? throw new WasmLinkException("instance " + alias.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " has no export named `" + alias.Name + "`");
                if (export.Sort != alias.Sort)
                    throw new WasmLinkException("export `" + alias.Name + "` for instance " + alias.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not a " + alias.Sort.ToString().ToLowerInvariant());

                AddToScope(scope, alias.Sort, export.Value);
                break;

            case AliasKind.CoreExport:
                var coreInstance = ComponentScope.Get(scope.CoreInstances, alias.Index, "core instance");
                if (!coreInstance.Exports.TryGetValue(alias.Name!, out var value))
                    throw new WasmLinkException("core instance " + alias.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " has no export named `" + alias.Name + "`");

                var expected = alias.Sort switch
                {
                    ComponentSort.CoreFunc => value is WasmFunction,
                    ComponentSort.CoreTable => value is WasmTable,
                    ComponentSort.CoreMemory => value is WasmMemory,
                    ComponentSort.CoreGlobal => value is WasmGlobal,
                    ComponentSort.CoreTag => value is WasmTag,
                    _ => false,
                };

                if (!expected)
                    throw new WasmLinkException("export `" + alias.Name + "` for core instance " + alias.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not a " + alias.Sort.ToString().ToLowerInvariant());

                AddToScope(scope, alias.Sort, value);
                break;

            default:
                var outer = scope.GetOuter(alias.OuterCount);
                AddToScope(scope, alias.Sort, GetFromScope(outer, alias.Sort, alias.Index));
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Types
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentType EvaluateDefType(TypeExpr expr, ComponentScope scope, string? name)
    {
        switch (expr)
        {
            case FunctionTypeExpr function:
                return new ComponentFunctionType(
                    function.Parameters.Select(parameter => new ComponentField(parameter.Name, EvaluateValType(parameter.Type, scope))),
                    function.Result is null ? null : EvaluateValType(function.Result, scope),
                    function.Async);

            case ResourceTypeExpr resource:
                var instance = scope.Instance ?? throw new WasmLinkException("resources can only be defined within a concrete component");
                var destructor = resource.Destructor is { } index ? ComponentScope.Get(scope.CoreFunctions, index, "core func") : null;
                return new ComponentResourceType(name, instance, resource.Is64, destructor);

            case InstanceTypeExpr instanceType:
                return EvaluateInstanceType(instanceType, scope);

            case ComponentTypeExpr componentType:
                return EvaluateComponentType(componentType, scope);

            default:
                return EvaluateDefValType(expr, scope);
        }
    }

    public static ComponentValueType EvaluateValType(TypeExpr expr, ComponentScope scope)
    {
        if (expr is IndexTypeExpr index)
        {
            return ComponentScope.Get(scope.Types, index.Index, "type") as ComponentValueType ?? throw new WasmLinkException("type index " + index.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not a defined type");
        }

        return EvaluateDefValType(expr, scope);
    }

    private static ComponentValueType EvaluateDefValType(TypeExpr expr, ComponentScope scope) => expr switch
    {
        PrimitiveTypeExpr primitive => GetPrimitive(primitive.Kind),
        RecordTypeExpr record => new ComponentRecordType(record.Fields.Select(field => new ComponentField(field.Name, EvaluateValType(field.Type, scope)))),
        VariantTypeExpr variant => new ComponentVariantType(variant.Cases.Select(@case => new ComponentCase(@case.Name, @case.Type is null ? null : EvaluateValType(@case.Type, scope)))),
        ListTypeExpr list => new ComponentListType(EvaluateValType(list.Element, scope), list.Length),
        TupleTypeExpr tuple => new ComponentTupleType(tuple.Types.Select(type => EvaluateValType(type, scope))),
        FlagsTypeExpr flags => new ComponentFlagsType(flags.Labels),
        EnumTypeExpr @enum => new ComponentEnumType(@enum.Labels),
        OptionTypeExpr option => new ComponentOptionType(EvaluateValType(option.Type, scope)),
        ResultTypeExpr result => new ComponentResultType(result.Ok is null ? null : EvaluateValType(result.Ok, scope), result.Error is null ? null : EvaluateValType(result.Error, scope)),
        HandleTypeExpr handle => GetResource(scope, handle.ResourceIndex) is var resource && handle.Own ? new ComponentOwnType(resource) : new ComponentBorrowType(resource),
        AsyncValueTypeExpr value => value.Stream ? new ComponentStreamType(value.Element is null ? null : EvaluateValType(value.Element, scope)) : new ComponentFutureType(value.Element is null ? null : EvaluateValType(value.Element, scope)),
        MapTypeExpr map => new ComponentMapType(EvaluateValType(map.Key, scope), EvaluateValType(map.Value, scope)),
        IndexTypeExpr index => EvaluateValType(index, scope),
        _ => throw new WasmLinkException("type is not a value type"),
    };

    private static ComponentResourceType GetResource(ComponentScope scope, int index) => ComponentScope.Get(scope.Types, index, "type") as ComponentResourceType ?? throw new WasmLinkException("type index " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not a resource type");

    private static ComponentPrimitiveType GetPrimitive(ComponentValueTypeKind kind) => kind switch
    {
        ComponentValueTypeKind.Bool => ComponentValueType.Bool,
        ComponentValueTypeKind.S8 => ComponentValueType.S8,
        ComponentValueTypeKind.U8 => ComponentValueType.U8,
        ComponentValueTypeKind.S16 => ComponentValueType.S16,
        ComponentValueTypeKind.U16 => ComponentValueType.U16,
        ComponentValueTypeKind.S32 => ComponentValueType.S32,
        ComponentValueTypeKind.U32 => ComponentValueType.U32,
        ComponentValueTypeKind.S64 => ComponentValueType.S64,
        ComponentValueTypeKind.U64 => ComponentValueType.U64,
        ComponentValueTypeKind.F32 => ComponentValueType.F32,
        ComponentValueTypeKind.F64 => ComponentValueType.F64,
        ComponentValueTypeKind.Char => ComponentValueType.Char,
        ComponentValueTypeKind.String => ComponentValueType.String,
        _ => ComponentValueType.ErrorContext,
    };

    private static ComponentInstanceType EvaluateInstanceType(InstanceTypeExpr expr, ComponentScope outer)
    {
        var scope = new ComponentScope(outer, instance: null);
        var exports = new List<ComponentExternDescriptor>();
        foreach (var declaration in expr.Declarations)
        {
            EvaluateTypeDeclaration(declaration, scope, exports, imports: null);
        }

        return new ComponentInstanceType(exports);
    }

    private static ComponentComponentType EvaluateComponentType(ComponentTypeExpr expr, ComponentScope outer)
    {
        var scope = new ComponentScope(outer, instance: null);
        var imports = new List<ComponentExternDescriptor>();
        var exports = new List<ComponentExternDescriptor>();
        foreach (var declaration in expr.Declarations)
        {
            EvaluateTypeDeclaration(declaration, scope, exports, imports);
        }

        return new ComponentComponentType(imports, exports);
    }

    private static void EvaluateTypeDeclaration(TypeDeclaration declaration, ComponentScope scope, List<ComponentExternDescriptor> exports, List<ComponentExternDescriptor>? imports)
    {
        switch (declaration)
        {
            case CoreTypeDeclaration core:
                scope.CoreTypes.Add(core.Type);
                break;

            case TypeTypeDeclaration type:
                scope.Types.Add(EvaluateDefType(type.Type, scope, name: null));
                break;

            case AliasTypeDeclaration { Alias: var alias }:
                if (alias.Kind == AliasKind.Outer)
                {
                    var outer = scope.GetOuter(alias.OuterCount);
                    AddToScope(scope, alias.Sort, GetFromScope(outer, alias.Sort, alias.Index));
                }
                else if (alias.Kind == AliasKind.Export && alias.Sort == ComponentSort.Type)
                {
                    var instanceType = ComponentScope.Get(scope.InstanceTypes, alias.Index, "instance");
                    var export = instanceType.GetExport(alias.Name!) ?? throw new WasmLinkException("instance has no export named `" + alias.Name + "`");
                    scope.Types.Add(export.Type ?? throw new WasmLinkException("export `" + alias.Name + "` is not a type"));
                }
                else if (alias.Kind == AliasKind.Export && alias.Sort == ComponentSort.Instance)
                {
                    var instanceType = ComponentScope.Get(scope.InstanceTypes, alias.Index, "instance");
                    var export = instanceType.GetExport(alias.Name!) ?? throw new WasmLinkException("instance has no export named `" + alias.Name + "`");
                    scope.InstanceTypes.Add(export.Type as ComponentInstanceType ?? throw new WasmLinkException("export `" + alias.Name + "` is not an instance"));
                }
                else
                {
                    throw new WasmLinkException("invalid alias in a type declaration");
                }

                break;

            case ExportTypeDeclaration export:
                var exportType = EvaluateExternType(export.Type, scope, export.Name);
                exports.Add(new ComponentExternDescriptor(export.Name, exportType));
                AddDeclaredToScope(scope, exportType);
                break;

            case ImportTypeDeclaration import:
                var importType = EvaluateExternType(import.Type, scope, import.Name);
                imports!.Add(new ComponentExternDescriptor(import.Name, importType));
                AddDeclaredToScope(scope, importType);
                break;
        }
    }

    private static void AddDeclaredToScope(ComponentScope scope, ComponentExternType type)
    {
        if (type.Kind == ComponentExternKind.Type)
        {
            scope.Types.Add(type.Type!);
        }
        else if (type.Kind == ComponentExternKind.Instance)
        {
            scope.InstanceTypes.Add((ComponentInstanceType)type.Type!);
        }
    }

    private static ComponentExternType EvaluateExternType(ExternTypeExpr expr, ComponentScope scope, string name)
    {
        switch (expr.Kind)
        {
            case ComponentExternKind.CoreModule:
                return new ComponentExternType(ComponentExternKind.CoreModule, null, moduleType: null);

            case ComponentExternKind.Function:
                var function = ComponentScope.Get(scope.Types, expr.Index, "type") as ComponentFunctionType ?? throw new WasmLinkException("type index " + expr.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not a function type");
                return new ComponentExternType(ComponentExternKind.Function, function);

            case ComponentExternKind.Type:
                if (expr.Index < 0)
                    return new ComponentExternType(ComponentExternKind.Type, new ComponentResourceType(name, implementation: null, is64: false, destructor: null) { IsAbstract = true });

                return new ComponentExternType(ComponentExternKind.Type, ComponentScope.Get(scope.Types, expr.Index, "type"));

            case ComponentExternKind.Component:
                return new ComponentExternType(ComponentExternKind.Component, ComponentScope.Get(scope.Types, expr.Index, "type") as ComponentComponentType ?? throw new WasmLinkException("type index " + expr.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not a component type"));

            case ComponentExternKind.Instance:
                if (expr.Index < scope.Types.Count && scope.Types[expr.Index] is ComponentInstanceType instance)
                    return new ComponentExternType(ComponentExternKind.Instance, instance);

                throw new WasmLinkException("type index " + expr.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not an instance type");

            default:
                throw new WasmLinkException("value imports and exports are not supported");
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Imports
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Converts a value given to an import to the value of the import, and checks its type.</summary>
    private object Bind(object provided, ComponentExternType declared, string name)
    {
        if (provided is ComponentImports.ExportedValue exported)
        {
            provided = exported.Value;
        }

        switch (declared.Kind)
        {
            case ComponentExternKind.Function:
                var functionType = (ComponentFunctionType)declared.Type!;
                return provided switch
                {
                    ComponentFunction function => function.Bind(_components, functionType),
                    ComponentExternValue { Sort: ComponentSort.Func, Value: ComponentFunctionValue value } => CheckFunction(value, functionType, name),
                    _ => throw new WasmLinkException("expected func found " + Describe(provided) + " for `" + name + "`"),
                };

            case ComponentExternKind.Instance:
                return BindInstance(provided, (ComponentInstanceType)declared.Type!, name);

            case ComponentExternKind.Type:
                var type = provided switch
                {
                    ComponentType value => value,
                    ComponentExternValue { Sort: ComponentSort.Type, Value: ComponentType value } => value,
                    _ => throw new WasmLinkException("expected type found " + Describe(provided) + " for `" + name + "`"),
                };

                if (declared.Type is ComponentResourceType { IsAbstract: true })
                {
                    if (type is not ComponentResourceType)
                        throw new WasmLinkException("expected resource found type for `" + name + "`");

                    return type;
                }

                if (!AreTypesEqual(type, declared.Type!))
                    throw new WasmLinkException("type mismatch for `" + name + "`");

                return type;

            case ComponentExternKind.Component:
                return provided switch
                {
                    WasmComponent component => new ComponentDefinitionValue(component.Ast, null, component),
                    ComponentExternValue { Sort: ComponentSort.Component, Value: ComponentDefinitionValue value } => value,
                    _ => throw new WasmLinkException("expected component found " + Describe(provided) + " for `" + name + "`"),
                };

            case ComponentExternKind.CoreModule:
                return provided switch
                {
                    WasmModule module => module,
                    ComponentExternValue { Sort: ComponentSort.CoreModule, Value: WasmModule value } => value,
                    _ => throw new WasmLinkException("expected module found " + Describe(provided) + " for `" + name + "`"),
                };

            default:
                throw new WasmLinkException("value imports are not supported");
        }
    }

    private static string Describe(object value) => value switch
    {
        ComponentExternValue extern_ => extern_.Sort.ToString().ToLowerInvariant(),
        ComponentFunction => "func",
        ComponentImports or WasmComponentInstance => "instance",
        ComponentType => "type",
        WasmComponent => "component",
        WasmModule => "module",
        _ => value.GetType().Name,
    };

    private static bool AreTypesEqual(ComponentType left, ComponentType right) => (left, right) switch
    {
        (ComponentValueType a, ComponentValueType b) => a.Equals(b),
        (ComponentFunctionType a, ComponentFunctionType b) => a.Equals(b),
        _ => ReferenceEquals(left, right),
    };

    private static ComponentFunctionValue CheckFunction(ComponentFunctionValue value, ComponentFunctionType type, string name)
    {
        if (!value.Type.Equals(type))
            throw new WasmLinkException("type mismatch with the function `" + name + "`: expected " + type + ", found " + value.Type);

        return value;
    }

    private ComponentInstanceValue BindInstance(object provided, ComponentInstanceType declared, string name)
    {
        Func<string, object?> getExport;
        ComponentInstanceState? state = null;
        switch (provided)
        {
            case ComponentImports imports:
                getExport = imports.Find;
                break;

            case WasmComponentInstance instance:
                state = instance.Value.State;
                getExport = exportName => instance.Value.GetExport(exportName);
                break;

            case ComponentExternValue { Sort: ComponentSort.Instance, Value: ComponentInstanceValue instance }:
                state = instance.State;
                getExport = exportName => instance.GetExport(exportName);
                break;

            default:
                throw new WasmLinkException("expected instance found " + Describe(provided) + " for `" + name + "`");
        }

        // The abstract resource types of the instance type are the resource types that the instance provides
        var substitution = new ComponentTypeSubstitution();
        foreach (var export in declared.Exports)
        {
            if (export.Type is { Kind: ComponentExternKind.Type, Type: ComponentResourceType { IsAbstract: true } resource })
            {
                var value = getExport(export.Name) ?? throw new WasmLinkException("missing expected export `" + export.Name + "` of `" + name + "`");
                var concrete = value switch
                {
                    ComponentResourceType type => type,
                    ComponentExternValue { Sort: ComponentSort.Type, Value: ComponentResourceType type } => type,
                    _ => throw new WasmLinkException("expected resource found " + Describe(value) + " for `" + name + "/" + export.Name + "`"),
                };

                substitution.Add(resource, concrete);
            }
        }

        var exports = new List<(string, ComponentExternValue)>();
        foreach (var export in substitution.Substitute(declared).Exports)
        {
            var value = getExport(export.Name);
            if (value is null)
            {
                // A type equal to a known type (not an abstract resource type) is the declared type: an instance of the
                // host does not need to define the types of its functions
                if (export.Type is { Kind: ComponentExternKind.Type, Type: { } knownType } && knownType is not ComponentResourceType { IsAbstract: true })
                {
                    exports.Add((export.Name, new ComponentExternValue(ComponentSort.Type, knownType)));
                    continue;
                }

                throw new WasmLinkException("missing expected export `" + export.Name + "` of `" + name + "`");
            }

            var bound = Bind(value, export.Type, name + "#" + export.Name);
            exports.Add((export.Name, new ComponentExternValue(ToSort(export.Type.Kind), bound)));
        }

        return new ComponentInstanceValue(state, exports);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Canonical definitions
    // ------------------------------------------------------------------------------------------------------------

    private static CanonicalOptions ResolveOptions(CanonOptions options, ComponentScope scope) => new()
    {
        StringEncoding = options.StringEncoding ?? ComponentStringEncoding.Utf8,
        Memory = options.Memory is { } memory ? ComponentScope.Get(scope.CoreMemories, memory, "core memory") : null,
        Realloc = options.Realloc is { } realloc ? ComponentScope.Get(scope.CoreFunctions, realloc, "core func") : null,
        PostReturn = options.PostReturn is { } postReturn ? ComponentScope.Get(scope.CoreFunctions, postReturn, "core func") : null,
        Async = options.Async,
        Callback = options.Callback is { } callback ? ComponentScope.Get(scope.CoreFunctions, callback, "core func") : null,
    };

    private static WasmFunctionType CoreType(IEnumerable<CoreValKind> parameters, IEnumerable<CoreValKind> results) => new([.. parameters.Select(CanonicalAbi.GetValueType)], [.. results.Select(CanonicalAbi.GetValueType)]);

    private static ComponentValueType? GetAsyncElementType(ComponentScope scope, int index, bool stream)
    {
        var type = ComponentScope.Get(scope.Types, index, "type");
        return (type, stream) switch
        {
            (ComponentStreamType streamType, true) => streamType.ElementType,
            (ComponentFutureType futureType, false) => futureType.ElementType,
            _ => throw new WasmLinkException("type index " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not a " + (stream ? "stream" : "future") + " type"),
        };
    }

    private void EvaluateCanon(CanonDefinition canon, ComponentScope scope)
    {
        var instance = scope.Instance!;
        var store = _store;
        var i32 = CoreValKind.I32;
        WasmFunction Create(WasmFunctionType type, WasmHostCallback callback) => store.CreateFunction(type, callback);

        switch (canon.Kind)
        {
            case CanonKind.Lift:
            {
                var callee = ComponentScope.Get(scope.CoreFunctions, canon.Index, "core func");
                var type = ComponentScope.Get(scope.Types, canon.Index2, "type") as ComponentFunctionType ?? throw new WasmLinkException("type index " + canon.Index2.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not a function type");
                var options = ResolveOptions(canon.Options, scope);
                var cx = new LiftLowerContext(LiftLowerDirection.Lift, options, instance);
                var (parameters, results) = CanonicalAbi.FlattenFunctionType(cx, type);
                CheckCoreType(callee, parameters, results, "lifted");
                scope.Functions.Add(new ComponentFunctionValue(type, Canon.Lift(callee, type, options, instance)));
                break;
            }

            case CanonKind.Lower:
            {
                var callee = ComponentScope.Get(scope.Functions, canon.Index, "func");
                var type = callee.Type;
                var options = ResolveOptions(canon.Options, scope);
                var cx = new LiftLowerContext(LiftLowerDirection.Lower, options, instance);
                var (parameters, results) = CanonicalAbi.FlattenFunctionType(cx, type);
                scope.CoreFunctions.Add(Create(CoreType(parameters, results), (arguments, output) =>
                {
                    var values = Canon.Lower(callee, type, options, instance, arguments);
                    values.CopyTo(output);
                }));
                break;
            }

            case CanonKind.ResourceNew:
            {
                var resource = GetLocalResource(scope, canon.Index, instance);
                scope.CoreFunctions.Add(Create(CoreType([resource.Is64 ? CoreValKind.I64 : i32], [i32]), (arguments, output) => output[0] = Canon.ResourceNew(instance, resource, arguments[0])));
                break;
            }

            case CanonKind.ResourceDrop or CanonKind.ResourceDropAsync:
            {
                var resource = GetResource(scope, canon.Index);
                scope.CoreFunctions.Add(Create(CoreType([i32], []), (arguments, _) => Canon.ResourceDrop(instance, resource, (uint)arguments[0].I32)));
                break;
            }

            case CanonKind.ResourceRep:
            {
                var resource = GetLocalResource(scope, canon.Index, instance);
                scope.CoreFunctions.Add(Create(CoreType([i32], [resource.Is64 ? CoreValKind.I64 : i32]), (arguments, output) => output[0] = Canon.ResourceRep(instance, resource, (uint)arguments[0].I32)));
                break;
            }

            case CanonKind.BackpressureInc:
                scope.CoreFunctions.Add(Create(CoreType([], []), (_, _) => Canon.BackpressureIncrement(instance)));
                break;

            case CanonKind.BackpressureDec:
                scope.CoreFunctions.Add(Create(CoreType([], []), (_, _) => Canon.BackpressureDecrement(instance)));
                break;

            case CanonKind.TaskReturn:
            {
                var resultType = canon.ResultType is null ? null : EvaluateValType(canon.ResultType, scope);
                var options = ResolveOptions(canon.Options, scope);
                var cx = new LiftLowerContext(LiftLowerDirection.Lift, options, instance);
                var flat = CanonicalAbi.FlattenTypes(cx, resultType is null ? [] : [resultType]);
                if (flat.Count > CanonicalAbi.MaxFlatParams)
                {
                    flat = [options.Is64 ? CoreValKind.I64 : i32];
                }

                scope.CoreFunctions.Add(Create(CoreType(flat, []), (arguments, _) => Canon.TaskReturn(instance, resultType, options, arguments)));
                break;
            }

            case CanonKind.TaskCancel:
                scope.CoreFunctions.Add(Create(CoreType([], []), (_, _) => Canon.TaskCancel(instance)));
                break;

            case CanonKind.ContextGet:
            {
                var (type, index) = (canon.CoreType, canon.Index);
                if (index > 1)
                    throw new WasmLinkException("invalid context index");

                scope.CoreFunctions.Add(Create(CoreType([], [type]), (_, output) => output[0] = Canon.ContextGet(instance, type, index)));
                break;
            }

            case CanonKind.ContextSet:
            {
                var (type, index) = (canon.CoreType, canon.Index);
                if (index > 1)
                    throw new WasmLinkException("invalid context index");

                scope.CoreFunctions.Add(Create(CoreType([type], []), (arguments, _) => Canon.ContextSet(instance, type, index, arguments[0])));
                break;
            }

            case CanonKind.SubtaskCancel:
            {
                var isAsync = canon.Async;
                scope.CoreFunctions.Add(Create(CoreType([i32], [i32]), (arguments, output) => output[0] = Canon.SubtaskCancel(instance, isAsync, (uint)arguments[0].I32)));
                break;
            }

            case CanonKind.SubtaskDrop:
                scope.CoreFunctions.Add(Create(CoreType([i32], []), (arguments, _) => Canon.SubtaskDrop(instance, (uint)arguments[0].I32)));
                break;

            case CanonKind.StreamNew or CanonKind.FutureNew:
            {
                var stream = canon.Kind == CanonKind.StreamNew;
                var elementType = GetAsyncElementType(scope, canon.Index, stream);
                scope.CoreFunctions.Add(Create(CoreType([], [CoreValKind.I64]), (_, output) => output[0] = Canon.StreamOrFutureNew(instance, stream, elementType)));
                break;
            }

            case CanonKind.StreamRead or CanonKind.StreamWrite or CanonKind.FutureRead or CanonKind.FutureWrite:
            {
                var stream = canon.Kind is CanonKind.StreamRead or CanonKind.StreamWrite;
                var read = canon.Kind is CanonKind.StreamRead or CanonKind.FutureRead;
                var elementType = GetAsyncElementType(scope, canon.Index, stream);
                var options = ResolveOptions(canon.Options, scope);
                var pointer = options.Is64 ? CoreValKind.I64 : i32;
                if (stream)
                {
                    scope.CoreFunctions.Add(Create(CoreType([i32, pointer, pointer], [pointer]), (arguments, output) =>
                    {
                        var result = Canon.Copy(instance, stream, read, elementType, options, (uint)arguments[0].I32, ToPointer(arguments[1], options), ToPointer(arguments[2], options));
                        output[0] = options.Is64 ? WasmValue.FromI64((uint)result.I32 == uint.MaxValue ? -1 : (uint)result.I32) : result;
                    }));
                }
                else
                {
                    scope.CoreFunctions.Add(Create(CoreType([i32, pointer], [i32]), (arguments, output) => output[0] = Canon.Copy(instance, stream, read, elementType, options, (uint)arguments[0].I32, ToPointer(arguments[1], options), 1)));
                }

                break;
            }

            case CanonKind.StreamCancelRead or CanonKind.StreamCancelWrite or CanonKind.FutureCancelRead or CanonKind.FutureCancelWrite:
            {
                var stream = canon.Kind is CanonKind.StreamCancelRead or CanonKind.StreamCancelWrite;
                var read = canon.Kind is CanonKind.StreamCancelRead or CanonKind.FutureCancelRead;
                var elementType = GetAsyncElementType(scope, canon.Index, stream);
                var isAsync = canon.Async;
                scope.CoreFunctions.Add(Create(CoreType([i32], [i32]), (arguments, output) => output[0] = Canon.CancelCopy(instance, stream, read, elementType, isAsync, (uint)arguments[0].I32)));
                break;
            }

            case CanonKind.StreamDropReadable or CanonKind.StreamDropWritable or CanonKind.FutureDropReadable or CanonKind.FutureDropWritable:
            {
                var stream = canon.Kind is CanonKind.StreamDropReadable or CanonKind.StreamDropWritable;
                var readable = canon.Kind is CanonKind.StreamDropReadable or CanonKind.FutureDropReadable;
                var elementType = GetAsyncElementType(scope, canon.Index, stream);
                scope.CoreFunctions.Add(Create(CoreType([i32], []), (arguments, _) => Canon.DropEnd(instance, stream, readable, elementType, (uint)arguments[0].I32)));
                break;
            }

            case CanonKind.StreamForward or CanonKind.FutureForward:
            {
                var stream = canon.Kind == CanonKind.StreamForward;
                var elementType = GetAsyncElementType(scope, canon.Index, stream);
                scope.CoreFunctions.Add(Create(CoreType([i32, i32], []), (arguments, _) => Canon.Forward(instance, stream, elementType, (uint)arguments[0].I32, (uint)arguments[1].I32)));
                break;
            }

            case CanonKind.ErrorContextNew:
            {
                var options = ResolveOptions(canon.Options, scope);
                var pointer = options.Is64 ? CoreValKind.I64 : i32;
                scope.CoreFunctions.Add(Create(CoreType([pointer, pointer], [i32]), (arguments, output) => output[0] = Canon.ErrorContextNew(instance, options, ToPointer(arguments[0], options), ToPointer(arguments[1], options))));
                break;
            }

            case CanonKind.ErrorContextDebugMessage:
            {
                var options = ResolveOptions(canon.Options, scope);
                var pointer = options.Is64 ? CoreValKind.I64 : i32;
                scope.CoreFunctions.Add(Create(CoreType([i32, pointer], []), (arguments, _) => Canon.ErrorContextDebugMessage(instance, options, (uint)arguments[0].I32, ToPointer(arguments[1], options))));
                break;
            }

            case CanonKind.ErrorContextDrop:
                scope.CoreFunctions.Add(Create(CoreType([i32], []), (arguments, _) => Canon.ErrorContextDrop(instance, (uint)arguments[0].I32)));
                break;

            case CanonKind.WaitableSetNew:
                scope.CoreFunctions.Add(Create(CoreType([], [i32]), (_, output) => output[0] = Canon.WaitableSetNew(instance)));
                break;

            case CanonKind.WaitableSetWait or CanonKind.WaitableSetPoll:
            {
                var memory = ComponentScope.Get(scope.CoreMemories, canon.Options.Memory!.Value, "core memory");
                var pointer = memory.Type.Is64 ? CoreValKind.I64 : i32;
                var wait = canon.Kind == CanonKind.WaitableSetWait;
                scope.CoreFunctions.Add(Create(CoreType([i32, pointer], [i32]), (arguments, output) =>
                {
                    var address = memory.Type.Is64 ? (uint)arguments[1].I64 : (uint)arguments[1].I32;
                    output[0] = wait ? Canon.WaitableSetWait(instance, memory, (uint)arguments[0].I32, address) : Canon.WaitableSetPoll(instance, memory, (uint)arguments[0].I32, address);
                }));
                break;
            }

            case CanonKind.WaitableSetDrop:
                scope.CoreFunctions.Add(Create(CoreType([i32], []), (arguments, _) => Canon.WaitableSetDrop(instance, (uint)arguments[0].I32)));
                break;

            case CanonKind.WaitableJoin:
                scope.CoreFunctions.Add(Create(CoreType([i32, i32], []), (arguments, _) => Canon.WaitableJoin(instance, (uint)arguments[0].I32, (uint)arguments[1].I32)));
                break;

            case CanonKind.ThreadIndex:
                scope.CoreFunctions.Add(Create(CoreType([], [i32]), (_, output) => output[0] = Canon.ThreadIndex(instance)));
                break;

            case CanonKind.ThreadNewIndirect:
            {
                var functionType = ComponentScope.Get(scope.CoreTypes, canon.Index, "core type") as CoreFunctionTypeExpr;
                if (functionType is not { Parameters: [CoreValKind.I32 or CoreValKind.I64], Results: [] })
                    throw new WasmLinkException("thread.new-indirect requires a function type (func (param i32))");

                var table = ComponentScope.Get(scope.CoreTables, canon.Index2, "core table");
                var index = table.Type.Is64 ? CoreValKind.I64 : i32;
                scope.CoreFunctions.Add(Create(CoreType([index, functionType.Parameters[0]], [i32]), (arguments, output) =>
                {
                    var functionIndex = table.Type.Is64 ? (uint)arguments[0].I64 : (uint)arguments[0].I32;
                    output[0] = Canon.ThreadNewIndirect(instance, table, functionType, functionIndex, arguments[1]);
                }));
                break;
            }

            case CanonKind.ThreadResumeLater:
                scope.CoreFunctions.Add(Create(CoreType([i32], []), (arguments, _) => Canon.ThreadResumeLater(instance, (uint)arguments[0].I32)));
                break;

            case CanonKind.ThreadSuspend or CanonKind.ThreadYield:
            {
                var kind = canon.Kind;
                scope.CoreFunctions.Add(Create(CoreType([], [i32]), (_, output) => output[0] = Canon.ThreadSwitch(instance, kind, 0)));
                break;
            }

            default:
            {
                var kind = canon.Kind;
                scope.CoreFunctions.Add(Create(CoreType([i32], [i32]), (arguments, output) => output[0] = Canon.ThreadSwitch(instance, kind, (uint)arguments[0].I32)));
                break;
            }
        }
    }

    private static ulong ToPointer(WasmValue value, CanonicalOptions options) => options.Is64 ? (ulong)value.I64 : (uint)value.I32;

    private static ComponentResourceType GetLocalResource(ComponentScope scope, int index, ComponentInstanceState instance)
    {
        var resource = GetResource(scope, index);
        if (resource.Implementation != instance)
            throw new WasmLinkException("resource type " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not a local resource");

        return resource;
    }

    private static void CheckCoreType(WasmFunction function, CoreValKind[] parameters, CoreValKind[] results, string kind)
    {
        var type = function.Type;
        var actualParameters = type.Parameters.Select(CanonicalAbi.GetKind).ToArray();
        var actualResults = type.Results.Select(CanonicalAbi.GetKind).ToArray();
        if (!actualParameters.SequenceEqual(parameters) || !actualResults.SequenceEqual(results))
            throw new WasmLinkException("type mismatch with the " + kind + " function: expected " + Format(parameters, results) + ", found " + Format(actualParameters, actualResults));
    }

    private static string Format(CoreValKind[] parameters, CoreValKind[] results) => "(func (param" + string.Concat(parameters.Select(parameter => " " + parameter.ToString().ToLowerInvariant())) + ") (result" + string.Concat(results.Select(result => " " + result.ToString().ToLowerInvariant())) + "))";
}
