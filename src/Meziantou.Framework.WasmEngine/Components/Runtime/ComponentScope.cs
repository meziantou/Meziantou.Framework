using Meziantou.Framework.WasmEngine.Components.Binary;

namespace Meziantou.Framework.WasmEngine.Components.Runtime;

/// <summary>The index spaces of a component instance, or of a component or an instance type.</summary>
internal sealed class ComponentScope(ComponentScope? parent, ComponentInstanceState? instance)
{
    public ComponentScope? Parent { get; } = parent;

    /// <summary>The instance of the component, or null for the scope of a type.</summary>
    public ComponentInstanceState? Instance { get; } = instance;

    public List<WasmFunction> CoreFunctions { get; } = [];

    public List<WasmTable> CoreTables { get; } = [];

    public List<WasmMemory> CoreMemories { get; } = [];

    public List<WasmGlobal> CoreGlobals { get; } = [];

    public List<WasmTag> CoreTags { get; } = [];

    public List<CoreTypeExpr> CoreTypes { get; } = [];

    public List<WasmModule> CoreModules { get; } = [];

    public List<CoreInstanceValue> CoreInstances { get; } = [];

    public List<ComponentFunctionValue> Functions { get; } = [];

    public List<ComponentType> Types { get; } = [];

    public List<ComponentDefinitionValue> Components { get; } = [];

    public List<ComponentInstanceValue> Instances { get; } = [];

    /// <summary>The types of the instances of a type scope (instance and component types declare instances by their types).</summary>
    public List<ComponentInstanceType> InstanceTypes { get; } = [];

    public ComponentScope GetOuter(int count)
    {
        var scope = this;
        for (var i = 0; i < count; i++)
        {
            scope = scope.Parent ?? throw new WasmLinkException("invalid outer alias count of " + count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return scope;
    }

    public static T Get<T>(List<T> items, int index, string sort)
    {
        if ((uint)index >= (uint)items.Count)
            throw new WasmLinkException("unknown " + sort + " " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + sort + " index out of bounds");

        return items[index];
    }
}

/// <summary>A core instance: the exports of an instance of a core module, or inline exports.</summary>
internal sealed class CoreInstanceValue(IReadOnlyDictionary<string, WasmExtern> exports)
{
    public IReadOnlyDictionary<string, WasmExtern> Exports { get; } = exports;
}

/// <summary>A component that can be instantiated: a nested component and the scope it is defined in, or a compiled component.</summary>
internal sealed class ComponentDefinitionValue(ComponentAst ast, ComponentScope? definitionScope, WasmComponent? compiled)
{
    public ComponentAst Ast { get; } = ast;

    public ComponentScope? DefinitionScope { get; } = definitionScope;

    public WasmComponent? Compiled { get; } = compiled;
}

/// <summary>An export of an instance: its sort and its value.</summary>
internal readonly record struct ComponentExternValue(ComponentSort Sort, object Value);

/// <summary>A component instance, or an instance of inline exports: its exports.</summary>
internal sealed class ComponentInstanceValue(ComponentInstanceState? state, IReadOnlyList<(string Name, ComponentExternValue Value)> exports)
{
    public ComponentInstanceState? State { get; } = state;

    public IReadOnlyList<(string Name, ComponentExternValue Value)> Exports { get; } = exports;

    public ComponentExternValue? GetExport(string name)
    {
        foreach (var (exportName, value) in Exports)
        {
            if (string.Equals(exportName, name, StringComparison.Ordinal))
                return value;
        }

        return null;
    }
}

/// <summary>Replaces abstract resource types with the resource types that the imports provide.</summary>
internal sealed class ComponentTypeSubstitution
{
    private readonly Dictionary<ComponentResourceType, ComponentResourceType> _resources = new(ReferenceEqualityComparer.Instance);

    public bool IsEmpty => _resources.Count == 0;

    public void Add(ComponentResourceType abstractType, ComponentResourceType concreteType)
    {
        if (!ReferenceEquals(abstractType, concreteType))
        {
            _resources[abstractType] = concreteType;
        }
    }

    public ComponentResourceType Substitute(ComponentResourceType type) => _resources.TryGetValue(type, out var concrete) ? concrete : type;

    public ComponentType Substitute(ComponentType type) => type switch
    {
        ComponentValueType value => Substitute(value),
        ComponentFunctionType function => Substitute(function),
        ComponentResourceType resource => Substitute(resource),
        ComponentInstanceType instance => Substitute(instance),
        ComponentComponentType component => new ComponentComponentType(component.Imports.Select(Substitute), component.Exports.Select(Substitute)),
        _ => type,
    };

    public ComponentInstanceType Substitute(ComponentInstanceType type) => IsEmpty ? type : new ComponentInstanceType(type.Exports.Select(Substitute));

    private ComponentExternDescriptor Substitute(ComponentExternDescriptor descriptor) => new(descriptor.Name, Substitute(descriptor.Type));

    public ComponentExternType Substitute(ComponentExternType type) => IsEmpty ? type : new ComponentExternType(type.Kind, type.Type is null ? null : Substitute(type.Type), type.ValueType is null ? null : Substitute(type.ValueType), type.ModuleType);

    public ComponentFunctionType Substitute(ComponentFunctionType type)
    {
        if (IsEmpty)
            return type;

        return new ComponentFunctionType(type.Parameters.Select(parameter => new ComponentField(parameter.Name, Substitute(parameter.Type))), type.Result is null ? null : Substitute(type.Result), type.IsAsync);
    }

    public ComponentValueType Substitute(ComponentValueType type)
    {
        if (IsEmpty)
            return type;

        return type switch
        {
            ComponentListType list => new ComponentListType(Substitute(list.ElementType), list.FixedLength),
            ComponentRecordType record => new ComponentRecordType(record.Fields.Select(field => new ComponentField(field.Name, Substitute(field.Type)))),
            ComponentTupleType tuple => new ComponentTupleType(tuple.Types.Select(Substitute)),
            ComponentVariantType variant => new ComponentVariantType(variant.Cases.Select(@case => new ComponentCase(@case.Name, @case.Type is null ? null : Substitute(@case.Type)))),
            ComponentOptionType option => new ComponentOptionType(Substitute(option.Type)),
            ComponentResultType result => new ComponentResultType(result.Ok is null ? null : Substitute(result.Ok), result.Error is null ? null : Substitute(result.Error)),
            ComponentOwnType own => new ComponentOwnType(Substitute(own.Resource)),
            ComponentBorrowType borrow => new ComponentBorrowType(Substitute(borrow.Resource)),
            ComponentStreamType stream => new ComponentStreamType(stream.ElementType is null ? null : Substitute(stream.ElementType)),
            ComponentFutureType future => new ComponentFutureType(future.ElementType is null ? null : Substitute(future.ElementType)),
            ComponentMapType map => new ComponentMapType(Substitute(map.KeyType), Substitute(map.ValueType)),
            _ => type,
        };
    }
}
