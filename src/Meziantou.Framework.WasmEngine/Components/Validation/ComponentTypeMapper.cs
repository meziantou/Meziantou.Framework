namespace Meziantou.Framework.WasmEngine.Components.Validation;

/// <summary>
/// Replaces resource types and named types in types: binds the resource types of the imports of a component to the
/// resource types of the arguments, creates new resource types (each instantiation and each import of an instance type
/// defines new resource types), and gives new identities to the types of an exported instance.
/// </summary>
internal sealed class ComponentTypeMapper
{
    private readonly Dictionary<ComponentResourceType, ComponentResourceType> _roots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, object> _mapped = new(ReferenceEqualityComparer.Instance);

    /// <summary>Creates a set of resource types, compared by identity.</summary>
    public static HashSet<ComponentResourceType> CreateSet(IEnumerable<ComponentResourceType> resources) => new(resources, ReferenceEqualityComparer.Instance);

    /// <summary>Whether the resource type has a replacement.</summary>
    public bool IsMapped(ComponentResourceType root) => _roots.ContainsKey(root);

    /// <summary>Replaces a resource type (and its aliases) with another resource type.</summary>
    public void MapRoot(ComponentResourceType root, ComponentResourceType replacement)
    {
        _roots[root] = replacement;
    }

    /// <summary>Replaces a type (an identity) with another type.</summary>
    public void MapObject(object type, object replacement)
    {
        _mapped[type] = replacement;
    }

    /// <summary>Replaces resource types with new resource types, and returns the new resource types.</summary>
    public List<ComponentResourceType> Freshen(IEnumerable<ComponentResourceType> roots)
    {
        var result = new List<ComponentResourceType>();
        foreach (var root in roots)
        {
            var fresh = new ComponentResourceType(root.Name, implementation: null, root.Is64, destructor: null) { IsAbstract = true };
            MapRoot(root, fresh);
            result.Add(fresh);
        }

        return result;
    }

    /// <summary>Creates a type with a new identity: the type of an import or an export of a type.</summary>
    public static ComponentType CreateAlias(ComponentType type) => type switch
    {
        ComponentResourceType resource => resource.CreateAlias(),
        ComponentRecordType record => new ComponentRecordType(record.Fields),
        ComponentVariantType variant => new ComponentVariantType(variant.Cases),
        ComponentEnumType @enum => new ComponentEnumType(@enum.Labels),
        ComponentFlagsType flags => new ComponentFlagsType(flags.Labels),
        _ => type,
    };

    public ComponentExternType Map(ComponentExternType type)
    {
        var mappedType = type.Type is null ? null : Map(type.Type);
        var valueType = type.ValueType is null ? null : Map(type.ValueType);
        if (ReferenceEquals(mappedType, type.Type) && ReferenceEquals(valueType, type.ValueType))
            return type;

        return new ComponentExternType(type.Kind, mappedType, valueType, type.ModuleType);
    }

    public ComponentType Map(ComponentType type) => type switch
    {
        ComponentValueType value => Map(value),
        ComponentResourceType resource => Map(resource),
        ComponentFunctionType function => Map(function),
        ComponentInstanceType instance => Map(instance),
        ComponentComponentType component => Map(component),
        _ => type,
    };

    public ComponentResourceType Map(ComponentResourceType resource)
    {
        if (_mapped.TryGetValue(resource, out var mapped))
            return (ComponentResourceType)mapped;

        var result = resource;
        if (_roots.TryGetValue(resource.Root, out var target))
        {
            result = ReferenceEquals(resource, resource.Root) ? target : target.CreateAlias();
        }

        _mapped[resource] = result;
        return result;
    }

    public ComponentFunctionType Map(ComponentFunctionType type)
    {
        if (_mapped.TryGetValue(type, out var mapped))
            return (ComponentFunctionType)mapped;

        var changed = false;
        var parameters = new ComponentField[type.Parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = type.Parameters[i];
            var parameterType = Map(parameter.Type);
            changed |= !ReferenceEquals(parameterType, parameter.Type);
            parameters[i] = new ComponentField(parameter.Name, parameterType);
        }

        var result = type.Result is null ? null : Map(type.Result);
        changed |= !ReferenceEquals(result, type.Result);
        var function = changed ? new ComponentFunctionType(parameters, result, type.IsAsync) : type;
        _mapped[type] = function;
        return function;
    }

    public ComponentInstanceType Map(ComponentInstanceType type)
    {
        if (_mapped.TryGetValue(type, out var mapped))
            return (ComponentInstanceType)mapped;

        var exports = type.Exports.Select(export => new ComponentExternDescriptor(export.Name, Map(export.Type))).ToArray();
        var resources = type.DefinedResources.Select(resource => Map(resource).Root).ToArray();
        var changed = !exports.Select(export => export.Type).SequenceEqual(type.Exports.Select(export => export.Type)) || !resources.SequenceEqual(type.DefinedResources);
        var instance = changed ? new ComponentInstanceType(exports) { DefinedResources = [.. resources] } : type;
        _mapped[type] = instance;
        return instance;
    }

    public ComponentComponentType Map(ComponentComponentType type)
    {
        if (_mapped.TryGetValue(type, out var mapped))
            return (ComponentComponentType)mapped;

        var imports = type.Imports.Select(import => new ComponentExternDescriptor(import.Name, Map(import.Type))).ToArray();
        var exports = type.Exports.Select(export => new ComponentExternDescriptor(export.Name, Map(export.Type))).ToArray();
        var component = new ComponentComponentType(imports, exports)
        {
            ImportedResources = [.. type.ImportedResources.Select(resource => Map(resource).Root)],
            DefinedResources = [.. type.DefinedResources.Select(resource => Map(resource).Root)],
        };

        _mapped[type] = component;
        return component;
    }

    public ComponentValueType Map(ComponentValueType type)
    {
        if (_mapped.TryGetValue(type, out var mapped))
            return (ComponentValueType)mapped;

        ComponentValueType result = type switch
        {
            ComponentRecordType record => MapFields(record.Fields, out var fields) ? new ComponentRecordType(fields) : type,
            ComponentVariantType variant => MapCases(variant.Cases, out var cases) ? new ComponentVariantType(cases) : type,
            ComponentListType list => Map(list.ElementType) is var element && !ReferenceEquals(element, list.ElementType) ? new ComponentListType(element, list.FixedLength) : type,
            ComponentTupleType tuple => MapTypes(tuple.Types, out var types) ? new ComponentTupleType(types) : type,
            ComponentOptionType option => Map(option.Type) is var some && !ReferenceEquals(some, option.Type) ? new ComponentOptionType(some) : type,
            ComponentResultType result1 => MapResult(result1),
            ComponentOwnType own => Map(own.Resource) is var ownResource && !ReferenceEquals(ownResource, own.Resource) ? new ComponentOwnType(ownResource) : type,
            ComponentBorrowType borrow => Map(borrow.Resource) is var borrowResource && !ReferenceEquals(borrowResource, borrow.Resource) ? new ComponentBorrowType(borrowResource) : type,
            ComponentStreamType stream => MapOptional(stream.ElementType, out var streamElement) ? new ComponentStreamType(streamElement) : type,
            ComponentFutureType future => MapOptional(future.ElementType, out var futureElement) ? new ComponentFutureType(futureElement) : type,
            ComponentMapType map => MapMap(map),
            _ => type,
        };

        _mapped[type] = result;
        return result;
    }

    private ComponentResultType MapResult(ComponentResultType type)
    {
        var okChanged = MapOptional(type.Ok, out var ok);
        var errorChanged = MapOptional(type.Error, out var error);
        return okChanged || errorChanged ? new ComponentResultType(ok, error) : type;
    }

    private ComponentMapType MapMap(ComponentMapType type)
    {
        var key = Map(type.KeyType);
        var value = Map(type.ValueType);
        return ReferenceEquals(key, type.KeyType) && ReferenceEquals(value, type.ValueType) ? type : new ComponentMapType(key, value);
    }

    private bool MapOptional(ComponentValueType? type, out ComponentValueType? result)
    {
        result = type is null ? null : Map(type);
        return !ReferenceEquals(result, type);
    }

    private bool MapTypes(System.Collections.Immutable.ImmutableArray<ComponentValueType> types, out ComponentValueType[] result)
    {
        result = new ComponentValueType[types.Length];
        var changed = false;
        for (var i = 0; i < types.Length; i++)
        {
            result[i] = Map(types[i]);
            changed |= !ReferenceEquals(result[i], types[i]);
        }

        return changed;
    }

    private bool MapFields(System.Collections.Immutable.ImmutableArray<ComponentField> fields, out ComponentField[] result)
    {
        result = new ComponentField[fields.Length];
        var changed = false;
        for (var i = 0; i < fields.Length; i++)
        {
            var type = Map(fields[i].Type);
            changed |= !ReferenceEquals(type, fields[i].Type);
            result[i] = new ComponentField(fields[i].Name, type);
        }

        return changed;
    }

    private bool MapCases(System.Collections.Immutable.ImmutableArray<ComponentCase> cases, out ComponentCase[] result)
    {
        result = new ComponentCase[cases.Length];
        var changed = false;
        for (var i = 0; i < cases.Length; i++)
        {
            changed |= MapOptional(cases[i].Type, out var type);
            result[i] = new ComponentCase(cases[i].Name, type);
        }

        return changed;
    }
}
