using System.Globalization;
using Meziantou.Framework.WasmEngine.Components.Binary;
using Meziantou.Framework.WasmEngine.Components.Runtime;

namespace Meziantou.Framework.WasmEngine.Components.Validation;

/// <summary>
/// Validates a component (https://github.com/WebAssembly/component-model/blob/main/design/mvp/Binary.md): computes the
/// type of each definition, and checks the indices, the names, the types of the imports and the exports, the
/// instantiations of the nested components and modules, and the canonical definitions.
/// </summary>
/// <remarks>
/// The resource types are generative: each resource type definition, each instantiation of a component and each import of
/// an instance type creates new resource types. Each import or export of a type gives the type a new identity, which
/// "names" the type: the types of an import or an export can only refer to the types that are named in its namespace.
/// </remarks>
internal sealed class ComponentValidator
{
    private const ulong MaxValueSize = 1UL << 28;

    private readonly IReadOnlyDictionary<CoreModuleDefinition, WasmModule> _modules;

    private ComponentValidator(IReadOnlyDictionary<CoreModuleDefinition, WasmModule> modules)
    {
        _modules = modules;
    }

    /// <summary>Validates a component.</summary>
    /// <exception cref="WasmCompileException">The component is not valid.</exception>
    public static void Validate(ComponentAst ast, IReadOnlyDictionary<CoreModuleDefinition, WasmModule> modules)
    {
        _ = new ComponentValidator(modules).ValidateComponent(ast, parent: null);
    }

    private enum ScopeKind
    {
        Component,
        ComponentType,
        InstanceType,
    }

    /// <summary>The index spaces and the names of a component, of a component type or of an instance type.</summary>
    private sealed class Scope(ScopeKind kind, Scope? parent)
    {
        public ScopeKind Kind { get; } = kind;

        public Scope? Parent { get; } = parent;

        public List<WasmFunctionType> CoreFunctions { get; } = [];

        public List<WasmTableType> CoreTables { get; } = [];

        public List<WasmMemoryType> CoreMemories { get; } = [];

        public List<WasmGlobalType> CoreGlobals { get; } = [];

        public List<WasmTagType> CoreTags { get; } = [];

        /// <summary>The core types: a <see cref="WasmFunctionType"/>, a <see cref="ComponentModuleType"/>, or null for the other types.</summary>
        public List<object?> CoreTypes { get; } = [];

        public List<ComponentModuleType> CoreModules { get; } = [];

        public List<Dictionary<string, WasmExternType>> CoreInstances { get; } = [];

        public List<ComponentFunctionType> Functions { get; } = [];

        public List<ComponentValueType> Values { get; } = [];

        public List<ComponentType> Types { get; } = [];

        public List<ComponentComponentType> Components { get; } = [];

        public List<ComponentInstanceType> Instances { get; } = [];

        public List<ComponentExternDescriptor> Imports { get; } = [];

        public List<ComponentExternDescriptor> Exports { get; } = [];

        public Dictionary<string, string> ImportNames { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> ExportNames { get; } = new(StringComparer.Ordinal);

        /// <summary>The types that the imports name.</summary>
        public HashSet<object> ImportNamed { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>The types that the imports and the exports name.</summary>
        public HashSet<object> ExportNamed { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>The names of the resource types that the imports define (the top-level names only).</summary>
        public Dictionary<ComponentResourceType, string> ImportResourceNames { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>The names of the resource types that the exports define (the top-level names only).</summary>
        public Dictionary<ComponentResourceType, string> ExportResourceNames { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>The resource types defined by the resource type definitions of the component.</summary>
        public HashSet<ComponentResourceType> LocalResources { get; } = new(ReferenceEqualityComparer.Instance);

        public List<ComponentResourceType> ImportedResources { get; } = [];

        public List<ComponentResourceType> DefinedResources { get; } = [];
    }

    private ComponentComponentType ValidateComponent(ComponentAst ast, Scope? parent)
    {
        var scope = new Scope(ScopeKind.Component, parent);
        foreach (var definition in ast.Definitions)
        {
            Visit(definition, scope);
        }

        return new ComponentComponentType(scope.Imports, scope.Exports)
        {
            ImportedResources = [.. scope.ImportedResources],
            DefinedResources = [.. scope.DefinedResources],
        };
    }

    private void Visit(ComponentDefinition definition, Scope scope)
    {
        switch (definition)
        {
            case CoreModuleDefinition module:
                scope.CoreModules.Add(GetModuleType(_modules[module]));
                break;

            case CoreInstanceDefinition coreInstance:
                scope.CoreInstances.Add(ValidateCoreInstance(coreInstance, scope));
                break;

            case CoreTypeDefinition coreType:
                scope.CoreTypes.Add(ResolveCoreType(coreType.Type, scope));
                break;

            case NestedComponentDefinition nested:
                scope.Components.Add(ValidateComponent(nested.Component, scope));
                break;

            case InstanceDefinition instance:
                scope.Instances.Add(ValidateInstance(instance, scope));
                break;

            case AliasDefinition alias:
                ValidateAlias(alias, scope);
                break;

            case TypeDefinition type:
                scope.Types.Add(ResolveDefType(type.Type, scope));
                break;

            case CanonDefinition canon:
                ValidateCanon(canon, scope);
                break;

            case ImportDefinition import:
                ValidateImport(import.Name, import.Attributes, import.Type, scope);
                break;

            case ExportDefinition export:
                ValidateExport(export, scope);
                break;

            case StartDefinition:
                // The start definitions take and return values, which are not supported
                throw Error("start definitions are not supported");
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Index spaces
    // ------------------------------------------------------------------------------------------------------------

    private static T Get<T>(List<T> items, int index, string sort)
    {
        if ((uint)index >= (uint)items.Count)
            throw Error("unknown " + sort + " " + Format(index) + ": " + sort + " index out of bounds");

        return items[index];
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static WasmCompileException Error(string message) => new(message);

    private static ComponentExternType GetExtern(Scope scope, ComponentSort sort, int index) => sort switch
    {
        ComponentSort.CoreModule => new ComponentExternType(ComponentExternKind.CoreModule, null, moduleType: Get(scope.CoreModules, index, "module")),
        ComponentSort.Func => new ComponentExternType(ComponentExternKind.Function, Get(scope.Functions, index, "func")),
        ComponentSort.Value => new ComponentExternType(ComponentExternKind.Value, null, Get(scope.Values, index, "value")),
        ComponentSort.Type => new ComponentExternType(ComponentExternKind.Type, Get(scope.Types, index, "type")),
        ComponentSort.Component => new ComponentExternType(ComponentExternKind.Component, Get(scope.Components, index, "component")),
        ComponentSort.Instance => new ComponentExternType(ComponentExternKind.Instance, Get(scope.Instances, index, "instance")),
        _ => throw Error("a core " + DescribeSort(sort) + " cannot be imported, exported or given to an instantiation"),
    };

    private static void AddExtern(Scope scope, ComponentExternType type)
    {
        switch (type.Kind)
        {
            case ComponentExternKind.CoreModule:
                scope.CoreModules.Add(type.ModuleType!);
                break;
            case ComponentExternKind.Function:
                scope.Functions.Add((ComponentFunctionType)type.Type!);
                break;
            case ComponentExternKind.Value:
                scope.Values.Add(type.ValueType!);
                break;
            case ComponentExternKind.Type:
                scope.Types.Add(type.Type!);
                break;
            case ComponentExternKind.Component:
                scope.Components.Add((ComponentComponentType)type.Type!);
                break;
            case ComponentExternKind.Instance:
                scope.Instances.Add((ComponentInstanceType)type.Type!);
                break;
        }
    }

    private static ComponentExternKind GetKind(ComponentSort sort) => sort switch
    {
        ComponentSort.CoreModule => ComponentExternKind.CoreModule,
        ComponentSort.Func => ComponentExternKind.Function,
        ComponentSort.Value => ComponentExternKind.Value,
        ComponentSort.Type => ComponentExternKind.Type,
        ComponentSort.Component => ComponentExternKind.Component,
        _ => ComponentExternKind.Instance,
    };

    private static string DescribeSort(ComponentSort sort) => sort switch
    {
        ComponentSort.CoreFunc => "func",
        ComponentSort.CoreTable => "table",
        ComponentSort.CoreMemory => "memory",
        ComponentSort.CoreGlobal => "global",
        ComponentSort.CoreTag => "tag",
        ComponentSort.CoreType => "core type",
        ComponentSort.CoreModule => "module",
        ComponentSort.CoreInstance => "core instance",
        _ => sort.ToString().ToLowerInvariant(),
    };

    // ------------------------------------------------------------------------------------------------------------
    // Core modules and core instances
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentModuleType GetModuleType(WasmModule module)
    {
        CheckUniqueImports(module.Imports.Select(import => (import.Module, import.Name)));
        return new ComponentModuleType(module.Imports, module.Exports);
    }

    private static void CheckUniqueImports(IEnumerable<(string Module, string Name)> imports)
    {
        var names = new HashSet<(string, string)>();
        foreach (var import in imports)
        {
            if (!names.Add(import))
                throw Error("duplicate import name `" + import.Module + ":" + import.Name + "`");
        }
    }

    private static Dictionary<string, WasmExternType> ValidateCoreInstance(CoreInstanceDefinition definition, Scope scope)
    {
        if (definition.Exports is { } inlineExports)
        {
            var exports = new Dictionary<string, WasmExternType>(StringComparer.Ordinal);
            foreach (var (name, sort, index) in inlineExports)
            {
                if (!exports.TryAdd(name, GetCoreExtern(scope, sort, index)))
                    throw Error("export name `" + name + "` already defined");
            }

            return exports;
        }

        var module = Get(scope.CoreModules, definition.ModuleIndex, "module");
        var arguments = new Dictionary<string, Dictionary<string, WasmExternType>>(StringComparer.Ordinal);
        foreach (var (name, instanceIndex) in definition.Arguments!)
        {
            if (!arguments.TryAdd(name, Get(scope.CoreInstances, instanceIndex, "core instance")))
                throw Error("duplicate module instantiation argument named `" + name + "`");
        }

        foreach (var import in module.Imports)
        {
            if (!arguments.TryGetValue(import.Module, out var argument))
                throw Error("missing module instantiation argument named `" + import.Module + "`");

            if (!argument.TryGetValue(import.Name, out var value))
                throw Error("module instantiation argument `" + import.Module + "` does not export an item named `" + import.Name + "`");

            try
            {
                ComponentTypeMatcher.MatchCore(value, import.Type);
            }
            catch (WasmCompileException ex)
            {
                throw Error("type mismatch for export `" + import.Name + "` of module instantiation argument `" + import.Module + "`: " + ex.Message);
            }
        }

        return module.Exports.ToDictionary(export => export.Name, export => export.Type, StringComparer.Ordinal);
    }

    private static WasmExternType GetCoreExtern(Scope scope, ComponentSort sort, int index) => sort switch
    {
        ComponentSort.CoreFunc => Get(scope.CoreFunctions, index, "func"),
        ComponentSort.CoreTable => Get(scope.CoreTables, index, "table"),
        ComponentSort.CoreMemory => Get(scope.CoreMemories, index, "memory"),
        ComponentSort.CoreGlobal => Get(scope.CoreGlobals, index, "global"),
        ComponentSort.CoreTag => Get(scope.CoreTags, index, "tag"),
        _ => throw Error("a core " + DescribeSort(sort) + " cannot be exported by a core instance"),
    };

    private static void AddCoreExtern(Scope scope, WasmExternType type)
    {
        switch (type)
        {
            case WasmFunctionType function:
                scope.CoreFunctions.Add(function);
                break;
            case WasmTableType table:
                scope.CoreTables.Add(table);
                break;
            case WasmMemoryType memory:
                scope.CoreMemories.Add(memory);
                break;
            case WasmGlobalType global:
                scope.CoreGlobals.Add(global);
                break;
            case WasmTagType tag:
                scope.CoreTags.Add(tag);
                break;
        }
    }

    private static object? ResolveCoreType(CoreTypeExpr expr, Scope scope) => expr switch
    {
        CoreFunctionTypeExpr function => ToFunctionType(function),
        CoreModuleTypeExpr module => ResolveModuleType(module, scope),
        _ => null,
    };

    private static WasmFunctionType ToFunctionType(CoreFunctionTypeExpr function) => new(function.ParameterTypes ?? [.. function.Parameters.Select(CanonicalAbi.GetValueType)], function.ResultTypes ?? [.. function.Results.Select(CanonicalAbi.GetValueType)]);

    private static ComponentModuleType ResolveModuleType(CoreModuleTypeExpr expr, Scope scope)
    {
        var types = new List<object?>();
        var imports = new List<WasmImport>();
        var exports = new List<WasmExport>();
        var exportNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in expr.Declarations)
        {
            switch (declaration)
            {
                case CoreTypeModuleDeclaration type:
                    types.Add(type.Type is CoreFunctionTypeExpr function ? ToFunctionType(function) : null);
                    break;

                case CoreAliasDeclaration alias:
                    if (alias.OuterCount != 1)
                        throw Error("invalid outer alias count of " + Format(alias.OuterCount));

                    types.Add(Get(scope.CoreTypes, alias.Index, "core type"));
                    break;

                case CoreImportDeclaration import:
                    imports.Add(new WasmImport(import.Module, import.Name, ResolveCoreExternType(import.Type, types)));
                    break;

                case CoreExportDeclaration export:
                    if (!exportNames.Add(export.Name))
                        throw Error("export name `" + export.Name + "` already defined");

                    exports.Add(new WasmExport(export.Name, ResolveCoreExternType(export.Type, types)));
                    break;
            }
        }

        CheckUniqueImports(imports.Select(import => (import.Module, import.Name)));
        return new ComponentModuleType(imports, exports);
    }

    private static WasmExternType ResolveCoreExternType(CoreExternTypeExpr expr, List<object?> types)
    {
        switch (expr.Kind)
        {
            case WasmExternKind.Function:
                return Get(types, expr.TypeIndex, "type") as WasmFunctionType ?? throw Error("core type index " + Format(expr.TypeIndex) + " is not a function type");

            case WasmExternKind.Tag:
                var tag = Get(types, expr.TypeIndex, "type") as WasmFunctionType ?? throw Error("core type index " + Format(expr.TypeIndex) + " is not a function type");
                if (tag.Results.Length != 0)
                    throw Error("tag types must not have results");

                return new WasmTagType(tag.Parameters);

            case WasmExternKind.Memory:
                var memory = expr.Memory!;
                var maximumPages = memory.Is64 ? 1UL << 48 : 1UL << 16;
                if (memory.Minimum > maximumPages || memory.Maximum > maximumPages)
                    throw Error("memory size must be at most " + maximumPages.ToString(CultureInfo.InvariantCulture) + " pages");

                if (memory.Maximum < memory.Minimum)
                    throw Error("size minimum must not be greater than maximum");

                return memory;

            case WasmExternKind.Table:
                var table = expr.Table!;
                if (table.Maximum < table.Minimum)
                    throw Error("size minimum must not be greater than maximum");

                return table;

            default:
                return expr.Global!;
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Instances
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentInstanceType ValidateInstance(InstanceDefinition definition, Scope scope)
    {
        if (definition.Exports is { } inlineExports)
        {
            // A bag of exports: the names are validated as the exports of an instance type, and its type exports name the types
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var resourceNames = new Dictionary<ComponentResourceType, string>(ReferenceEqualityComparer.Instance);
            var exports = new List<ComponentExternDescriptor>();
            for (var i = 0; i < inlineExports.Length; i++)
            {
                var (name, sort, index) = inlineExports[i];
                var type = GetExtern(scope, sort, index);
                // A resource type export gives the resource a name in the instance (the defined value types keep their identity)
                if (type is { Kind: ComponentExternKind.Type, Type: ComponentResourceType resource })
                {
                    type = new ComponentExternType(ComponentExternKind.Type, resource.CreateAlias());
                }

                var parsed = ComponentNameValidator.Validate(name, definition.ExportAttributes?[i], type.Kind);
                AddUniqueName(names, parsed.Key, name, "export");
                RegisterResourceName(resourceNames, type, name);
                CheckAnnotatedName(parsed, type, resourceNames, name);
                exports.Add(new ComponentExternDescriptor(name, type));
            }

            return new ComponentInstanceType(exports);
        }

        var component = Get(scope.Components, definition.ComponentIndex, "component");
        var arguments = new Dictionary<string, ComponentExternType>(StringComparer.Ordinal);
        foreach (var (name, sort, index) in definition.Arguments!)
        {
            if (!arguments.TryAdd(name, GetExtern(scope, sort, index)))
                throw Error("instantiation argument `" + name + "` conflicts with previous argument `" + name + "`");
        }

        return Instantiate(component, arguments, scope);
    }

    /// <summary>Instantiates a component type: checks the arguments, and returns the type of the instance.</summary>
    private static ComponentInstanceType Instantiate(ComponentComponentType component, Dictionary<string, ComponentExternType> arguments, Scope scope)
    {
        var matcher = new ComponentTypeMatcher(new ComponentTypeMapper(), ComponentTypeMapper.CreateSet(component.ImportedResources));
        foreach (var import in component.Imports)
        {
            if (!arguments.TryGetValue(import.Name, out var argument))
                throw Error("missing import named `" + import.Name + "`");

            try
            {
                matcher.Match(argument, import.Type);
            }
            catch (WasmCompileException ex)
            {
                throw Error("type mismatch for import `" + import.Name + "`: " + ex.Message);
            }
        }

        scope.DefinedResources.AddRange(matcher.Mapper.Freshen(component.DefinedResources));
        return new ComponentInstanceType(component.Exports.Select(export => new ComponentExternDescriptor(export.Name, matcher.Mapper.Map(export.Type))));
    }

    // ------------------------------------------------------------------------------------------------------------
    // Aliases
    // ------------------------------------------------------------------------------------------------------------

    private static void ValidateAlias(AliasDefinition alias, Scope scope)
    {
        if (scope.Kind != ScopeKind.Component)
        {
            var allowed = alias.Kind switch
            {
                AliasKind.Export => alias.Sort is ComponentSort.Type or ComponentSort.Instance,
                AliasKind.Outer => alias.Sort is ComponentSort.Type or ComponentSort.CoreType,
                _ => false,
            };

            if (!allowed)
                throw Error("an alias of a component type or an instance type may only refer to types or instances");
        }

        switch (alias.Kind)
        {
            case AliasKind.Export:
                var instance = Get(scope.Instances, alias.Index, "instance");
                var export = instance.GetExport(alias.Name!) ?? throw Error("instance " + Format(alias.Index) + " has no export named `" + alias.Name + "`");
                if (export.Kind != GetKind(alias.Sort) || alias.Sort is not (ComponentSort.CoreModule or ComponentSort.Func or ComponentSort.Value or ComponentSort.Type or ComponentSort.Component or ComponentSort.Instance))
                    throw Error("export `" + alias.Name + "` for instance " + Format(alias.Index) + " is not a " + DescribeSort(alias.Sort));

                AddExtern(scope, export);
                break;

            case AliasKind.CoreExport:
                var coreInstance = Get(scope.CoreInstances, alias.Index, "core instance");
                if (!coreInstance.TryGetValue(alias.Name!, out var coreExport))
                    throw Error("core instance " + Format(alias.Index) + " has no export named `" + alias.Name + "`");

                var expected = alias.Sort switch
                {
                    ComponentSort.CoreFunc => WasmExternKind.Function,
                    ComponentSort.CoreTable => WasmExternKind.Table,
                    ComponentSort.CoreMemory => WasmExternKind.Memory,
                    ComponentSort.CoreGlobal => WasmExternKind.Global,
                    ComponentSort.CoreTag => WasmExternKind.Tag,
                    _ => (WasmExternKind?)null,
                };

                if (coreExport.Kind != expected)
                    throw Error("export `" + alias.Name + "` for core instance " + Format(alias.Index) + " is not a " + DescribeSort(alias.Sort));

                AddCoreExtern(scope, coreExport);
                break;

            default:
                ValidateOuterAlias(alias, scope);
                break;
        }
    }

    private static void ValidateOuterAlias(AliasDefinition alias, Scope scope)
    {
        var target = scope;
        var crossesComponent = false;
        for (var i = 0; i < alias.OuterCount; i++)
        {
            crossesComponent |= target.Kind == ScopeKind.Component;
            target = target.Parent ?? throw Error("invalid outer alias count of " + Format(alias.OuterCount));
        }

        switch (alias.Sort)
        {
            case ComponentSort.CoreModule:
                scope.CoreModules.Add(Get(target.CoreModules, alias.Index, "module"));
                break;

            case ComponentSort.CoreType:
                scope.CoreTypes.Add(Get(target.CoreTypes, alias.Index, "core type"));
                break;

            case ComponentSort.Component:
                scope.Components.Add(Get(target.Components, alias.Index, "component"));
                break;

            default:
                var type = Get(target.Types, alias.Index, "type");
                if (crossesComponent && ContainsResources(type))
                    throw Error("cannot alias outer type which transitively refers to resources not defined in the current component");

                scope.Types.Add(type);
                break;
        }
    }

    private static bool ContainsResources(ComponentType type) => type switch
    {
        ComponentResourceType => true,
        ComponentValueType value => CanonicalAbi.Contains(value, t => t is ComponentHandleType),
        ComponentFunctionType function => function.Parameters.Any(parameter => ContainsResources(parameter.Type)) || (function.Result is { } result && ContainsResources(result)),
        ComponentInstanceType instance => instance.Exports.Any(export => ContainsResources(export.Type)),
        ComponentComponentType component => component.Imports.Any(import => ContainsResources(import.Type)) || component.Exports.Any(export => ContainsResources(export.Type)),
        _ => false,
    };

    private static bool ContainsResources(ComponentExternType type) => (type.Type is { } t && ContainsResources(t)) || (type.ValueType is { } value && ContainsResources(value));

    // ------------------------------------------------------------------------------------------------------------
    // Imports and exports
    // ------------------------------------------------------------------------------------------------------------

    private static void AddUniqueName(Dictionary<string, string> names, string key, string name, string kind)
    {
        if (!names.TryAdd(key, name))
            throw Error(kind + " name `" + name + "` conflicts with previous name `" + names[key] + "`");
    }

    /// <summary>Resolves the type of an import or an export, and creates the resource types that it defines.</summary>
    private static ComponentExternType ResolveExternType(ExternTypeExpr expr, Scope scope, string name, List<ComponentResourceType> createdResources)
    {
        switch (expr.Kind)
        {
            case ComponentExternKind.CoreModule:
                var moduleType = Get(scope.CoreTypes, expr.Index, "core type") as ComponentModuleType ?? throw Error("core type index " + Format(expr.Index) + " is not a module type");
                return new ComponentExternType(ComponentExternKind.CoreModule, null, moduleType: moduleType);

            case ComponentExternKind.Function:
                return new ComponentExternType(ComponentExternKind.Function, Get(scope.Types, expr.Index, "type") as ComponentFunctionType ?? throw Error("type index " + Format(expr.Index) + " is not a function type"));

            case ComponentExternKind.Value:
                throw Error("value imports and exports are not supported");

            case ComponentExternKind.Type:
                if (expr.Index < 0)
                {
                    var resource = new ComponentResourceType(name, implementation: null, is64: false, destructor: null) { IsAbstract = true };
                    createdResources.Add(resource);
                    return new ComponentExternType(ComponentExternKind.Type, resource);
                }

                return new ComponentExternType(ComponentExternKind.Type, ComponentTypeMapper.CreateAlias(Get(scope.Types, expr.Index, "type")));

            case ComponentExternKind.Component:
                return new ComponentExternType(ComponentExternKind.Component, Get(scope.Types, expr.Index, "type") as ComponentComponentType ?? throw Error("type index " + Format(expr.Index) + " is not a component type"));

            default:
                var instance = Get(scope.Types, expr.Index, "type") as ComponentInstanceType ?? throw Error("type index " + Format(expr.Index) + " is not an instance type");
                if (instance.DefinedResources.Length > 0)
                {
                    var mapper = new ComponentTypeMapper();
                    createdResources.AddRange(mapper.Freshen(instance.DefinedResources));
                    instance = mapper.Map(instance);
                }

                return new ComponentExternType(ComponentExternKind.Instance, instance);
        }
    }

    private static void ValidateImport(string name, ComponentNameAttributes? attributes, ExternTypeExpr expr, Scope scope)
    {
        var type = ResolveExternType(expr, scope, name, scope.ImportedResources);
        var parsed = ComponentNameValidator.Validate(name, attributes, type.Kind);
        AddUniqueName(scope.ImportNames, parsed.Key, name, "import");
        if (scope.Kind != ScopeKind.InstanceType && !IsNamed(type, scope.ImportNamed))
            throw Error(ComponentTypeMatcher.Describe(type.Kind) + " not valid to be used as import: `" + name + "`");

        AddNamed(type, scope.ImportNamed);
        AddNamed(type, scope.ExportNamed);
        RegisterResourceName(scope.ImportResourceNames, type, name);
        CheckAnnotatedName(parsed, type, scope.ImportResourceNames, name);
        scope.Imports.Add(new ComponentExternDescriptor(name, type));
        AddExtern(scope, type);
    }

    private static void ValidateExport(ExportDefinition export, Scope scope)
    {
        var actual = GetExtern(scope, export.Sort, export.Index);
        ComponentExternType type;
        if (export.Type is { } ascription)
        {
            var created = new List<ComponentResourceType>();
            type = ResolveExternType(ascription, scope, export.Name, created);
            try
            {
                new ComponentTypeMatcher(new ComponentTypeMapper(), ComponentTypeMapper.CreateSet(created)).Match(actual, type);
            }
            catch (WasmCompileException ex)
            {
                throw Error("ascribed type of export is not compatible with item's type: " + ex.Message);
            }

            scope.DefinedResources.AddRange(created);
        }
        else
        {
            // An export of a type gives the type a new identity, which is named; an export of an instance names its types
            type = actual.Kind == ComponentExternKind.Type ? new ComponentExternType(ComponentExternKind.Type, ComponentTypeMapper.CreateAlias(actual.Type!)) : actual;
        }

        DeclareExport(export.Name, export.Attributes, type, scope);
    }

    private static void DeclareExport(string name, ComponentNameAttributes? attributes, ComponentExternType type, Scope scope)
    {
        var parsed = ComponentNameValidator.Validate(name, attributes, type.Kind);
        AddUniqueName(scope.ExportNames, parsed.Key, name, "export");
        if (scope.Kind != ScopeKind.InstanceType && !IsNamed(type, scope.ExportNamed))
            throw Error(ComponentTypeMatcher.Describe(type.Kind) + " not valid to be used as export: `" + name + "`");

        AddNamed(type, scope.ExportNamed);
        RegisterResourceName(scope.ExportResourceNames, type, name);
        CheckAnnotatedName(parsed, type, scope.ExportResourceNames, name);
        scope.Exports.Add(new ComponentExternDescriptor(name, type));
        AddExtern(scope, type);
    }

    private static void RegisterResourceName(Dictionary<ComponentResourceType, string> names, ComponentExternType type, string name)
    {
        if (type is { Kind: ComponentExternKind.Type, Type: ComponentResourceType resource })
        {
            names[resource] = name;
        }
    }

    private static void CheckAnnotatedName(ComponentName parsed, ComponentExternType type, Dictionary<ComponentResourceType, string> resourceNames, string name)
    {
        switch (parsed.Kind)
        {
            case ComponentNameKind.Constructor:
            {
                var function = (ComponentFunctionType)type.Type!;
                var resource = function.Result switch
                {
                    null => throw Error("function should return one value"),
                    ComponentOwnType own => own.Resource,
                    ComponentResultType { Ok: ComponentOwnType own } => own.Resource,
                    _ => throw Error("function should return `(own $T)` or `(result (own $T))`"),
                };

                CheckResourceName(resource, parsed.Resource!, resourceNames);
                break;
            }

            case ComponentNameKind.Method:
            {
                var function = (ComponentFunctionType)type.Type!;
                if (function.Parameters.Length == 0)
                    throw Error("function should have at least one argument");

                var (parameterName, parameterType) = function.Parameters[0];
                if (!string.Equals(parameterName, "self", StringComparison.Ordinal))
                    throw Error("function should have a first argument called `self`");

                if (parameterType is not ComponentBorrowType borrow)
                    throw Error("function should take a first argument of `(borrow $T)`");

                CheckResourceName(borrow.Resource, parsed.Resource!, resourceNames);
                break;
            }

            case ComponentNameKind.Static:
                if (!resourceNames.ContainsValue(parsed.Resource!))
                    throw Error("static resource name is not known in this context: `" + name + "`");

                break;
        }
    }

    private static void CheckResourceName(ComponentResourceType resource, string expected, Dictionary<ComponentResourceType, string> resourceNames)
    {
        if (!resourceNames.TryGetValue(resource, out var actual))
            throw Error("resource used in function does not have a name in this context");

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw Error("function does not match expected resource name `" + actual + "`");
    }

    // ------------------------------------------------------------------------------------------------------------
    // Names of the types (external visibility)
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Whether the types that an import or an export refers to are named in its namespace.</summary>
    private static bool IsNamed(ComponentExternType type, HashSet<object> named) => type.Kind switch
    {
        ComponentExternKind.Function => IsNamed((ComponentFunctionType)type.Type!, named),
        ComponentExternKind.Value => IsNamed(type.ValueType!, named),
        ComponentExternKind.Type => AreContentsNamed(type.Type!, named),
        ComponentExternKind.Instance => IsNamed((ComponentInstanceType)type.Type!, named),
        _ => true,
    };

    private static bool IsNamed(ComponentFunctionType function, HashSet<object> named) => function.Parameters.All(parameter => IsNamed(parameter.Type, named)) && (function.Result is not { } result || IsNamed(result, named));

    private static bool IsNamed(ComponentInstanceType instance, HashSet<object> named)
    {
        var local = new HashSet<object>(named, ReferenceEqualityComparer.Instance);
        foreach (var export in instance.Exports)
        {
            if (!IsNamed(export.Type, local))
                return false;

            AddNamed(export.Type, local);
        }

        return true;
    }

    /// <summary>Whether the types that a type refers to are named (the type itself is named by the import or the export).</summary>
    private static bool AreContentsNamed(ComponentType type, HashSet<object> named) => type switch
    {
        ComponentResourceType or ComponentComponentType or ComponentLabelsType => true,
        ComponentRecordType record => record.Fields.All(field => IsNamed(field.Type, named)),
        ComponentVariantType variant => variant.Cases.All(@case => @case.Type is not { } caseType || IsNamed(caseType, named)),
        ComponentValueType value => IsNamed(value, named),
        ComponentFunctionType function => IsNamed(function, named),
        ComponentInstanceType instance => IsNamed(instance, named),
        _ => true,
    };

    private static bool IsNamed(ComponentValueType type, HashSet<object> named) => type switch
    {
        ComponentRecordType or ComponentVariantType or ComponentLabelsType => named.Contains(type),
        ComponentHandleType handle => named.Contains(handle.Resource),
        ComponentListType list => IsNamed(list.ElementType, named),
        ComponentTupleType tuple => tuple.Types.All(t => IsNamed(t, named)),
        ComponentOptionType option => IsNamed(option.Type, named),
        ComponentResultType result => (result.Ok is not { } ok || IsNamed(ok, named)) && (result.Error is not { } error || IsNamed(error, named)),
        ComponentAsyncValueType value => value.ElementType is not { } element || IsNamed(element, named),
        ComponentMapType map => IsNamed(map.KeyType, named) && IsNamed(map.ValueType, named),
        _ => true,
    };

    private static void AddNamed(ComponentExternType type, HashSet<object> named)
    {
        if (type.Kind == ComponentExternKind.Type)
        {
            named.Add(type.Type!);
        }
        else if (type.Kind == ComponentExternKind.Instance)
        {
            foreach (var export in ((ComponentInstanceType)type.Type!).Exports)
            {
                AddNamed(export.Type, named);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Types
    // ------------------------------------------------------------------------------------------------------------

    private ComponentType ResolveDefType(TypeExpr expr, Scope scope)
    {
        switch (expr)
        {
            case FunctionTypeExpr function:
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                var parameters = new List<ComponentField>();
                foreach (var (name, parameterType) in function.Parameters)
                {
                    ComponentNameValidator.ValidateLabel(name, "function parameter");
                    if (!names.TryAdd(name.ToLowerInvariant(), name))
                        throw Error("function parameter name `" + name + "` conflicts with previous parameter name `" + names[name.ToLowerInvariant()] + "`");

                    parameters.Add(new ComponentField(name, ResolveValType(parameterType, scope)));
                }

                var result = function.Result is null ? null : ResolveValType(function.Result, scope);
                if (CanonicalAbi.ContainsBorrow(result))
                    throw Error("function result cannot contain a `borrow` type");

                return new ComponentFunctionType(parameters, result, function.Async);

            case ResourceTypeExpr resource:
                if (scope.Kind != ScopeKind.Component)
                    throw Error("resources can only be defined within a concrete component");

                if (resource.Destructor is { } index)
                {
                    var destructor = Get(scope.CoreFunctions, index, "func");
                    var representation = resource.Is64 ? WasmValueType.I64 : WasmValueType.I32;
                    if (destructor is not { Parameters: [var parameter], Results: [] } || parameter.Kind != representation.Kind)
                        throw Error("wrong signature for a destructor");
                }

                var type = new ComponentResourceType(null, implementation: null, resource.Is64, destructor: null);
                scope.LocalResources.Add(type);
                scope.DefinedResources.Add(type);
                return type;

            case InstanceTypeExpr instance:
                return ResolveInstanceType(instance, scope);

            case ComponentTypeExpr component:
                return ResolveComponentType(component, scope);

            default:
                return ResolveDefValType(expr, scope);
        }
    }

    private ComponentInstanceType ResolveInstanceType(InstanceTypeExpr expr, Scope parent)
    {
        var scope = new Scope(ScopeKind.InstanceType, parent);
        foreach (var declaration in expr.Declarations)
        {
            VisitDeclaration(declaration, scope);
        }

        return new ComponentInstanceType(scope.Exports) { DefinedResources = [.. scope.DefinedResources] };
    }

    private ComponentComponentType ResolveComponentType(ComponentTypeExpr expr, Scope parent)
    {
        var scope = new Scope(ScopeKind.ComponentType, parent);
        foreach (var declaration in expr.Declarations)
        {
            VisitDeclaration(declaration, scope);
        }

        return new ComponentComponentType(scope.Imports, scope.Exports)
        {
            ImportedResources = [.. scope.ImportedResources],
            DefinedResources = [.. scope.DefinedResources],
        };
    }

    private void VisitDeclaration(TypeDeclaration declaration, Scope scope)
    {
        switch (declaration)
        {
            case CoreTypeDeclaration core:
                scope.CoreTypes.Add(ResolveCoreType(core.Type, scope));
                break;

            case TypeTypeDeclaration type:
                scope.Types.Add(ResolveDefType(type.Type, scope));
                break;

            case AliasTypeDeclaration alias:
                ValidateAlias(alias.Alias, scope);
                break;

            case ImportTypeDeclaration import:
                if (scope.Kind != ScopeKind.ComponentType)
                    throw Error("instance types cannot have imports");

                ValidateImport(import.Name, import.Attributes, import.Type, scope);
                break;

            case ExportTypeDeclaration export:
                DeclareExport(export.Name, export.Attributes, ResolveExternType(export.Type, scope, export.Name, scope.DefinedResources), scope);
                break;
        }
    }

    private static ComponentValueType ResolveValType(TypeExpr expr, Scope scope) => expr switch
    {
        PrimitiveTypeExpr primitive => GetPrimitive(primitive.Kind),
        IndexTypeExpr index => Get(scope.Types, index.Index, "type") as ComponentValueType ?? throw Error("type index " + Format(index.Index) + " is not a defined type"),
        _ => ResolveDefValType(expr, scope),
    };

    private static ComponentValueType ResolveDefValType(TypeExpr expr, Scope scope)
    {
        ComponentValueType type = expr switch
        {
            PrimitiveTypeExpr primitive => GetPrimitive(primitive.Kind),
            IndexTypeExpr index => ResolveValType(index, scope),
            RecordTypeExpr record => new ComponentRecordType(ResolveLabeled([.. record.Fields.Select(field => (field.Name, (TypeExpr?)field.Type))], "record field", "field", scope).Select(field => new ComponentField(field.Name, field.Type!))),
            VariantTypeExpr variant => new ComponentVariantType(ResolveLabeled(variant.Cases, "variant case", "case", scope).Select(@case => new ComponentCase(@case.Name, @case.Type))),
            ListTypeExpr list => new ComponentListType(ResolveValType(list.Element, scope), list.Length),
            TupleTypeExpr tuple => new ComponentTupleType(tuple.Types.Select(t => ResolveValType(t, scope))),
            FlagsTypeExpr flags => new ComponentFlagsType(ValidateLabels(flags.Labels, "flag", "flag")),
            EnumTypeExpr @enum => new ComponentEnumType(ValidateLabels(@enum.Labels, "enum tag", "tag")),
            OptionTypeExpr option => new ComponentOptionType(ResolveValType(option.Type, scope)),
            ResultTypeExpr result => new ComponentResultType(result.Ok is null ? null : ResolveValType(result.Ok, scope), result.Error is null ? null : ResolveValType(result.Error, scope)),
            HandleTypeExpr handle => GetResource(scope, handle.ResourceIndex) is var resource && handle.Own ? new ComponentOwnType(resource) : new ComponentBorrowType(resource),
            AsyncValueTypeExpr value => ResolveAsyncValueType(value, scope),
            MapTypeExpr map => ResolveMapType(map, scope),
            _ => throw Error("type is not a value type"),
        };

        if (ElementSize(type) >= MaxValueSize)
            throw Error("type exceeds maximum byte size");

        return type;
    }

    private static ComponentValueType ResolveAsyncValueType(AsyncValueTypeExpr expr, Scope scope)
    {
        var element = expr.Element is null ? null : ResolveValType(expr.Element, scope);
        if (!expr.Stream)
            return new ComponentFutureType(element);

        if (element is { Kind: ComponentValueTypeKind.Char })
            throw Error("`stream<char>` is not valid at this time");

        return new ComponentStreamType(element);
    }

    private static ComponentMapType ResolveMapType(MapTypeExpr expr, Scope scope)
    {
        var key = ResolveValType(expr.Key, scope);
        if (key.Kind is not (ComponentValueTypeKind.Bool or ComponentValueTypeKind.S8 or ComponentValueTypeKind.U8 or ComponentValueTypeKind.S16 or ComponentValueTypeKind.U16 or ComponentValueTypeKind.S32 or ComponentValueTypeKind.U32 or ComponentValueTypeKind.S64 or ComponentValueTypeKind.U64 or ComponentValueTypeKind.Char or ComponentValueTypeKind.String))
            throw Error("invalid map key type");

        var value = ResolveValType(expr.Value, scope);
        if (CanonicalAbi.Contains(value, t => t.Kind is ComponentValueTypeKind.Own or ComponentValueTypeKind.Borrow or ComponentValueTypeKind.Stream or ComponentValueTypeKind.Future))
            throw Error("map value type cannot contain a handle");

        return new ComponentMapType(key, value);
    }

    private static List<(string Name, ComponentValueType? Type)> ResolveLabeled((string Name, TypeExpr? Type)[] items, string description, string previous, Scope scope)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new List<(string, ComponentValueType?)>();
        foreach (var (name, type) in items)
        {
            ComponentNameValidator.ValidateLabel(name, description);
            if (!names.TryAdd(name.ToLowerInvariant(), name))
                throw Error(description + " name `" + name + "` conflicts with previous " + previous + " name `" + names[name.ToLowerInvariant()] + "`");

            result.Add((name, type is null ? null : ResolveValType(type, scope)));
        }

        return result;
    }

    private static string[] ValidateLabels(string[] labels, string description, string previous)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var label in labels)
        {
            ComponentNameValidator.ValidateLabel(label, description);
            if (!names.TryAdd(label.ToLowerInvariant(), label))
                throw Error(description + " name `" + label + "` conflicts with previous " + previous + " name `" + names[label.ToLowerInvariant()] + "`");
        }

        return labels;
    }

    private static ComponentResourceType GetResource(Scope scope, int index) => Get(scope.Types, index, "type") as ComponentResourceType ?? throw Error("type index " + Format(index) + " is not a resource type");

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

    /// <summary>The size of a value of the type in a 64-bit memory (canonical ABI "elem_size"), saturated.</summary>
    private static ulong ElementSize(ComponentValueType type) => Layout(type).Size;

    private static (ulong Alignment, ulong Size) Layout(ComponentValueType type)
    {
        const ulong Saturated = 1UL << 40;
        switch (type)
        {
            case ComponentListType { FixedLength: { } length } list:
                var element = Layout(list.ElementType);
                var product = (UInt128)element.Size * (ulong)length;
                return (element.Alignment, product > Saturated ? Saturated : (ulong)product);

            case ComponentListType or ComponentMapType or ComponentPrimitiveType { Kind: ComponentValueTypeKind.String }:
                return (8, 16);

            case ComponentRecordType or ComponentTupleType:
                IEnumerable<ComponentValueType> fields = type is ComponentRecordType record ? record.Fields.Select(field => field.Type) : ((ComponentTupleType)type).Types;
                ulong alignment = 1;
                ulong size = 0;
                foreach (var field in fields)
                {
                    var layout = Layout(field);
                    size = Math.Min(CanonicalAbi.AlignTo(size, layout.Alignment) + layout.Size, Saturated);
                    alignment = Math.Max(alignment, layout.Alignment);
                }

                return (alignment, CanonicalAbi.AlignTo(size, alignment));

            case ComponentVariantType or ComponentOptionType or ComponentResultType or ComponentEnumType:
                ComponentValueType?[] cases = type switch
                {
                    ComponentVariantType variant => [.. variant.Cases.Select(@case => @case.Type)],
                    ComponentOptionType option => [null, option.Type],
                    ComponentResultType result => [result.Ok, result.Error],
                    _ => new ComponentValueType?[((ComponentEnumType)type).Labels.Length],
                };

                var discriminant = CanonicalAbi.DiscriminantSize(cases.Length);
                ulong caseAlignment = 1;
                ulong caseSize = 0;
                foreach (var @case in cases)
                {
                    if (@case is not null)
                    {
                        var layout = Layout(@case);
                        caseAlignment = Math.Max(caseAlignment, layout.Alignment);
                        caseSize = Math.Max(caseSize, layout.Size);
                    }
                }

                var variantAlignment = Math.Max(discriminant, caseAlignment);
                return (variantAlignment, CanonicalAbi.AlignTo(CanonicalAbi.AlignTo(discriminant, caseAlignment) + caseSize, variantAlignment));

            case ComponentFlagsType flags:
                var flagsSize = flags.Labels.Length <= 8 ? 1UL : flags.Labels.Length <= 16 ? 2UL : 4UL;
                return (flagsSize, flagsSize);

            default:
                return type.Kind switch
                {
                    ComponentValueTypeKind.Bool or ComponentValueTypeKind.S8 or ComponentValueTypeKind.U8 => (1, 1),
                    ComponentValueTypeKind.S16 or ComponentValueTypeKind.U16 => (2, 2),
                    ComponentValueTypeKind.S64 or ComponentValueTypeKind.U64 or ComponentValueTypeKind.F64 => (8, 8),
                    _ => (4, 4),
                };
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Canonical definitions
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>The resolved canonical options.</summary>
    private sealed record Options(bool HasMemory, bool Is64, bool HasRealloc, WasmFunctionType? PostReturn, bool Async, bool HasCallback)
    {
        public CoreValKind Pointer => Is64 ? CoreValKind.I64 : CoreValKind.I32;
    }

    private static Options ResolveOptions(CanonOptions options, Scope scope, string definition)
    {
        var memory = options.Memory is { } memoryIndex ? Get(scope.CoreMemories, memoryIndex, "memory") : null;
        var pointer = memory is { Is64: true } ? WasmValueType.I64 : WasmValueType.I32;
        if (options.Realloc is { } reallocIndex)
        {
            var realloc = Get(scope.CoreFunctions, reallocIndex, "func");
            if (memory is null)
                throw Error("canonical option `realloc` requires `memory` to also be specified");

            if (realloc is not { Parameters: [var a, var b, var c, var d], Results: [var r] } || !new[] { a, b, c, d, r }.All(t => t.Kind == pointer.Kind))
                throw Error("canonical option `realloc` uses a core function with an incorrect signature");
        }

        var postReturn = options.PostReturn is { } postReturnIndex ? Get(scope.CoreFunctions, postReturnIndex, "func") : null;
        if (options.Callback is { } callbackIndex)
        {
            var callback = Get(scope.CoreFunctions, callbackIndex, "func");
            if (!options.Async)
                throw Error("canonical option `callback` requires `async`");

            if (callback is not { Parameters: [{ Kind: WasmValueKind.I32 }, { Kind: WasmValueKind.I32 }, { Kind: WasmValueKind.I32 }], Results: [{ Kind: WasmValueKind.I32 }] })
                throw Error("canonical option `callback` uses a core function with an incorrect signature");
        }

        if (postReturn is not null && definition != "lift")
            throw Error("canonical option `post-return` cannot be specified for lowerings");

        if (options.Callback is not null && definition != "lift")
            throw Error("canonical option `callback` cannot be specified for " + definition);

        return new Options(memory is not null, memory is { Is64: true }, options.Realloc is not null, postReturn, options.Async, options.Callback is not null);
    }

    private static void RequireMemory(Options options)
    {
        if (!options.HasMemory)
            throw Error("canonical option `memory` is required");
    }

    private static void RequireRealloc(Options options)
    {
        RequireMemory(options);
        if (!options.HasRealloc)
            throw Error("canonical option `realloc` is required");
    }

    private static WasmFunctionType CoreType(IEnumerable<CoreValKind> parameters, IEnumerable<CoreValKind> results) => new([.. parameters.Select(CanonicalAbi.GetValueType)], [.. results.Select(CanonicalAbi.GetValueType)]);

    private static void ValidateCanon(CanonDefinition canon, Scope scope)
    {
        try
        {
            ValidateCanonCore(canon, scope);
        }
        catch (WasmCompileException ex)
        {
            var function = canon.Kind switch
            {
                CanonKind.Lift when (uint)canon.Index2 < (uint)scope.Types.Count => " " + scope.Types[canon.Index2],
                CanonKind.Lower when (uint)canon.Index < (uint)scope.Functions.Count => " " + scope.Functions[canon.Index],
                _ => "",
            };

            throw new WasmCompileException(ex.Message + " (canon " + canon.Kind.ToString().ToLowerInvariant() + function + ")", ex);
        }
    }

    private static void ValidateCanonCore(CanonDefinition canon, Scope scope)
    {
        var i32 = CoreValKind.I32;
        var functions = scope.CoreFunctions;
        switch (canon.Kind)
        {
            case CanonKind.Lift:
            {
                var core = Get(functions, canon.Index, "func");
                var type = Get(scope.Types, canon.Index2, "type") as ComponentFunctionType ?? throw Error("type index " + Format(canon.Index2) + " is not a function type");
                var options = ResolveOptions(canon.Options, scope, "lift");
                if (options.Async && !type.IsAsync)
                    throw Error("the `async` canonical option requires an async function type");

                var parameters = Flatten(type.ParameterTypes, options, scope, passRepresentation: true);
                var results = Flatten(type.ResultTypes, options, scope, passRepresentation: false);
                if (type.ParameterTypes.Any(ContainsPointer) || parameters.Count > CanonicalAbi.MaxFlatParams)
                {
                    RequireRealloc(options);
                }

                if (parameters.Count > CanonicalAbi.MaxFlatParams)
                {
                    parameters = [options.Pointer];
                }

                if (options.Async)
                {
                    results = options.HasCallback ? [i32] : [];
                    if (options.PostReturn is not null)
                        throw Error("canonical option `post-return` cannot be specified with `async`");
                }
                else
                {
                    if (type.ResultTypes.Any(ContainsPointer) || results.Count > CanonicalAbi.MaxFlatResults)
                    {
                        RequireMemory(options);
                    }

                    if (results.Count > CanonicalAbi.MaxFlatResults)
                    {
                        results = [options.Pointer];
                    }
                }

                CheckCoreType(core, parameters, results);
                if (options.PostReturn is { } postReturn && (!Matches(postReturn.Parameters, results) || postReturn.Results.Length != 0))
                    throw Error("canonical option `post-return` uses a core function with an incorrect signature");

                scope.Functions.Add(type);
                break;
            }

            case CanonKind.Lower:
            {
                var type = Get(scope.Functions, canon.Index, "func");
                var options = ResolveOptions(canon.Options, scope, "lowerings");
                if (options.Async && !type.IsAsync)
                    throw Error("the `async` canonical option requires an async function type");

                var parameters = Flatten(type.ParameterTypes, options, scope, passRepresentation: false);
                var results = Flatten(type.ResultTypes, options, scope, passRepresentation: false);
                var maxParameters = options.Async ? CanonicalAbi.MaxFlatAsyncParams : CanonicalAbi.MaxFlatParams;
                if (type.ParameterTypes.Any(ContainsPointer) || parameters.Count > maxParameters)
                {
                    RequireMemory(options);
                }

                if (parameters.Count > maxParameters)
                {
                    parameters = [options.Pointer];
                }

                if (type.ResultTypes.Any(ContainsPointer))
                {
                    RequireRealloc(options);
                }

                if (options.Async)
                {
                    if (results.Count > 0)
                    {
                        RequireMemory(options);
                        parameters.Add(options.Pointer);
                    }

                    results = [i32];
                }
                else if (results.Count > CanonicalAbi.MaxFlatResults)
                {
                    RequireMemory(options);
                    parameters.Add(options.Pointer);
                    results = [];
                }

                functions.Add(CoreType(parameters, results));
                break;
            }

            case CanonKind.ResourceNew or CanonKind.ResourceRep:
            {
                var resource = GetResource(scope, canon.Index);
                if (!scope.LocalResources.Contains(resource.Root))
                    throw Error("type index " + Format(canon.Index) + " is not a local resource");

                var representation = resource.Root.Is64 ? CoreValKind.I64 : i32;
                functions.Add(canon.Kind == CanonKind.ResourceNew ? CoreType([representation], [i32]) : CoreType([i32], [representation]));
                break;
            }

            case CanonKind.ResourceDrop or CanonKind.ResourceDropAsync:
                _ = GetResource(scope, canon.Index);
                functions.Add(CoreType([i32], []));
                break;

            case CanonKind.BackpressureInc or CanonKind.BackpressureDec or CanonKind.TaskCancel:
                functions.Add(CoreType([], []));
                break;

            case CanonKind.TaskReturn:
            {
                var resultType = canon.ResultType is null ? null : ResolveValType(canon.ResultType, scope);
                if (CanonicalAbi.ContainsBorrow(resultType))
                    throw Error("`task.return` result cannot contain a `borrow` type");

                var options = ResolveOptions(canon.Options, scope, "task.return");
                var flat = Flatten(resultType is null ? [] : [resultType], options, scope, passRepresentation: false);
                if ((resultType is not null && ContainsPointer(resultType)) || flat.Count > CanonicalAbi.MaxFlatParams)
                {
                    RequireMemory(options);
                }

                if (flat.Count > CanonicalAbi.MaxFlatParams)
                {
                    flat = [options.Pointer];
                }

                functions.Add(CoreType(flat, []));
                break;
            }

            case CanonKind.ContextGet or CanonKind.ContextSet:
                if (canon.Index > 1)
                    throw Error("invalid context index " + Format(canon.Index));

                functions.Add(canon.Kind == CanonKind.ContextGet ? CoreType([], [canon.CoreType]) : CoreType([canon.CoreType], []));
                break;

            case CanonKind.SubtaskCancel:
                functions.Add(CoreType([i32], [i32]));
                break;

            case CanonKind.SubtaskDrop or CanonKind.ErrorContextDrop or CanonKind.WaitableSetDrop or CanonKind.ThreadResumeLater:
                functions.Add(CoreType([i32], []));
                break;

            case CanonKind.StreamNew or CanonKind.FutureNew:
                GetAsyncValueType(scope, canon);
                functions.Add(CoreType([], [CoreValKind.I64]));
                break;

            case CanonKind.StreamRead or CanonKind.StreamWrite or CanonKind.FutureRead or CanonKind.FutureWrite:
            {
                GetAsyncValueType(scope, canon);
                var options = ResolveOptions(canon.Options, scope, "stream and future operations");
                var pointer = options.Pointer;
                functions.Add(canon.Kind is CanonKind.StreamRead or CanonKind.StreamWrite ? CoreType([i32, pointer, pointer], [pointer]) : CoreType([i32, pointer], [i32]));
                break;
            }

            case CanonKind.StreamCancelRead or CanonKind.StreamCancelWrite or CanonKind.FutureCancelRead or CanonKind.FutureCancelWrite:
                GetAsyncValueType(scope, canon);
                functions.Add(CoreType([i32], [i32]));
                break;

            case CanonKind.StreamDropReadable or CanonKind.StreamDropWritable or CanonKind.FutureDropReadable or CanonKind.FutureDropWritable:
                GetAsyncValueType(scope, canon);
                functions.Add(CoreType([i32], []));
                break;

            case CanonKind.StreamForward or CanonKind.FutureForward:
                GetAsyncValueType(scope, canon);
                functions.Add(CoreType([i32, i32], []));
                break;

            case CanonKind.ErrorContextNew:
            {
                var options = ResolveOptions(canon.Options, scope, "error-context.new");
                functions.Add(CoreType([options.Pointer, options.Pointer], [i32]));
                break;
            }

            case CanonKind.ErrorContextDebugMessage:
            {
                var options = ResolveOptions(canon.Options, scope, "error-context.debug-message");
                functions.Add(CoreType([i32, options.Pointer], []));
                break;
            }

            case CanonKind.WaitableSetNew or CanonKind.ThreadIndex:
                functions.Add(CoreType([], [i32]));
                break;

            case CanonKind.WaitableSetWait or CanonKind.WaitableSetPoll:
            {
                var memory = Get(scope.CoreMemories, canon.Options.Memory!.Value, "memory");
                functions.Add(CoreType([i32, memory.Is64 ? CoreValKind.I64 : i32], [i32]));
                break;
            }

            case CanonKind.WaitableJoin:
                functions.Add(CoreType([i32, i32], []));
                break;

            case CanonKind.ThreadNewIndirect:
            {
                var functionType = Get(scope.CoreTypes, canon.Index, "core type") as WasmFunctionType;
                if (functionType is not { Parameters: [{ Kind: WasmValueKind.I32 or WasmValueKind.I64 } parameter], Results: [] })
                    throw Error("thread.new-indirect requires a function type (func (param i32))");

                var table = Get(scope.CoreTables, canon.Index2, "table");
                functions.Add(new WasmFunctionType([table.Is64 ? WasmValueType.I64 : WasmValueType.I32, parameter], [WasmValueType.I32]));
                break;
            }

            case CanonKind.ThreadSuspend or CanonKind.ThreadYield:
                functions.Add(CoreType([], [i32]));
                break;

            default:
                functions.Add(CoreType([i32], [i32]));
                break;
        }
    }

    private static void GetAsyncValueType(Scope scope, CanonDefinition canon)
    {
        var stream = canon.Kind is CanonKind.StreamNew or CanonKind.StreamRead or CanonKind.StreamWrite or CanonKind.StreamCancelRead or CanonKind.StreamCancelWrite or CanonKind.StreamDropReadable or CanonKind.StreamDropWritable or CanonKind.StreamForward;
        var type = Get(scope.Types, canon.Index, "type");
        if (stream ? type is not ComponentStreamType : type is not ComponentFutureType)
            throw Error("`" + (stream ? "stream" : "future") + "` operation requires a " + (stream ? "stream" : "future") + " type");
    }

    private static void CheckCoreType(WasmFunctionType function, List<CoreValKind> parameters, List<CoreValKind> results)
    {
        if (!Matches(function.Parameters, parameters))
            throw Error("lowered parameter types `[" + string.Join(", ", parameters) + "]` do not match parameter types `[" + string.Join(", ", function.Parameters.Select(CanonicalAbi.GetKind)) + "]` of core function");

        if (!Matches(function.Results, results))
            throw Error("lowered result types `[" + string.Join(", ", results) + "]` do not match result types `[" + string.Join(", ", function.Results.Select(CanonicalAbi.GetKind)) + "]` of core function");
    }

    private static bool Matches(WasmValueType[] actual, List<CoreValKind> expected) => actual.Length == expected.Count && actual.Select(CanonicalAbi.GetKind).SequenceEqual(expected);

    /// <summary>Whether the values of the type are stored in the memory: strings and lists.</summary>
    /// <remarks>The elements of the streams and the futures are not part of their values (handles).</remarks>
    private static bool ContainsPointer(ComponentValueType type) => CanonicalAbi.Despecialize(type) switch
    {
        ComponentPrimitiveType primitive => primitive.Kind == ComponentValueTypeKind.String,
        ComponentListType { FixedLength: null } => true,
        ComponentListType list => ContainsPointer(list.ElementType),
        ComponentRecordType record => record.Fields.Any(field => ContainsPointer(field.Type)),
        ComponentVariantType variant => variant.Cases.Any(@case => @case.Type is { } caseType && ContainsPointer(caseType)),
        _ => false,
    };

    /// <summary>Flattens types (canonical ABI "flatten_types").</summary>
    private static List<CoreValKind> Flatten(IEnumerable<ComponentValueType> types, Options options, Scope scope, bool passRepresentation)
    {
        var result = new List<CoreValKind>();
        foreach (var type in types)
        {
            Flatten(type, options, scope, passRepresentation, result);
        }

        return result;
    }

    private static void Flatten(ComponentValueType type, Options options, Scope scope, bool passRepresentation, List<CoreValKind> result)
    {
        var despecialized = CanonicalAbi.Despecialize(type);
        switch (despecialized)
        {
            case ComponentPrimitiveType { Kind: ComponentValueTypeKind.S64 or ComponentValueTypeKind.U64 }:
                result.Add(CoreValKind.I64);
                break;
            case ComponentPrimitiveType { Kind: ComponentValueTypeKind.F32 }:
                result.Add(CoreValKind.F32);
                break;
            case ComponentPrimitiveType { Kind: ComponentValueTypeKind.F64 }:
                result.Add(CoreValKind.F64);
                break;
            case ComponentPrimitiveType { Kind: ComponentValueTypeKind.String } or ComponentListType { FixedLength: null }:
                result.Add(options.Pointer);
                result.Add(options.Pointer);
                break;
            case ComponentListType { FixedLength: { } length } list:
                for (var i = 0; i < length && result.Count <= CanonicalAbi.MaxFlatParams; i++)
                {
                    Flatten(list.ElementType, options, scope, passRepresentation, result);
                }

                break;
            case ComponentRecordType record:
                foreach (var field in record.Fields)
                {
                    Flatten(field.Type, options, scope, passRepresentation, result);
                }

                break;
            case ComponentVariantType variant:
                var flat = new List<CoreValKind>();
                foreach (var @case in variant.Cases)
                {
                    if (@case.Type is null)
                        continue;

                    var caseFlat = new List<CoreValKind>();
                    Flatten(@case.Type, options, scope, passRepresentation, caseFlat);
                    for (var i = 0; i < caseFlat.Count; i++)
                    {
                        if (i < flat.Count)
                        {
                            flat[i] = Join(flat[i], caseFlat[i]);
                        }
                        else
                        {
                            flat.Add(caseFlat[i]);
                        }
                    }
                }

                result.Add(CoreValKind.I32);
                result.AddRange(flat);
                break;
            case ComponentBorrowType borrow when passRepresentation && scope.LocalResources.Contains(borrow.Resource.Root) && borrow.Resource.Root.Is64:
                result.Add(CoreValKind.I64);
                break;
            default:
                result.Add(CoreValKind.I32);
                break;
        }
    }

    private static CoreValKind Join(CoreValKind a, CoreValKind b)
    {
        if (a == b)
            return a;

        if ((a == CoreValKind.I32 && b == CoreValKind.F32) || (a == CoreValKind.F32 && b == CoreValKind.I32))
            return CoreValKind.I32;

        return CoreValKind.I64;
    }
}
