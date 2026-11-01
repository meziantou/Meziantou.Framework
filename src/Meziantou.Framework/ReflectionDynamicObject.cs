using System.Dynamic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Meziantou.Framework;

public sealed class ReflectionDynamicObject : DynamicObject
{
    private const string DynamicCodeMessage = "Dynamic objects are bound at runtime, which may require generating code";
    private const BindingFlags InstanceDefaultBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticDefaultBindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    // A weak table does not keep the types alive, so collectible AssemblyLoadContexts can be unloaded
    private static readonly ConditionalWeakTable<Type, TypeCache> Cache = new();

    private readonly object? _originalObject;
    private readonly TypeCache _typeCache;

    [RequiresUnreferencedCode("Use reflection")]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public ReflectionDynamicObject(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        _originalObject = obj;

        var type = obj.GetType();
        _typeCache = Cache.GetValue(type, TypeCache.Create);
    }

    [RequiresUnreferencedCode("Use reflection")]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public ReflectionDynamicObject(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        _typeCache = Cache.GetValue(type, TypeCache.Create);
    }

    [RequiresUnreferencedCode("Use reflection")]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public ReflectionDynamicObject CreateInstance(params object[] parameters)
    {
        var exceptions = new List<Exception>();

        foreach (var (constructor, parameterCount) in _typeCache.Constructors)
        {
            if (parameterCount != parameters.Length)
                continue;

            try
            {
                return new ReflectionDynamicObject(constructor.Invoke(parameters));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        Exception? innerException = exceptions.Count is 0 ? null : new AggregateException(exceptions);
        throw new ArgumentException($"Cannot create an instance of {_typeCache.Type.FullName} with the provided parameters.", nameof(parameters), innerException);
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        return TryGetMemberValue(binder.Name, out result);
    }

    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        return TrySetMemberValue(binder.Name, value);
    }

    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object? result)
    {
        foreach (var indexer in GetIndexerCandidates(indexes, setter: false))
        {
            if (TryGetIndex(indexer, _originalObject, indexes, out result))
                return true;
        }

        result = null;
        return false;
    }

    public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object? value)
    {
        foreach (var indexer in GetIndexerCandidates(indexes, setter: true))
        {
            if (TrySetIndex(indexer, _originalObject, indexes, value))
                return true;
        }

        return false;
    }

    // The indexers that have as many parameters as the indexes, in the order to try them: the ones whose parameter types
    // accept the indexes without a conversion first, then the public ones, which hide the explicit interface implementations.
    private IEnumerable<PropertyInfo> GetIndexerCandidates(object[] indexes, bool setter)
    {
        var indexers = _originalObject is null ? _typeCache.StaticIndexers : _typeCache.InstanceIndexers;
        return indexers
            .Where(indexer => indexer.ParameterTypes.Length == indexes.Length && (setter ? indexer.Property.CanWrite : indexer.Property.CanRead))
            .OrderByDescending(indexer => AcceptsWithoutConversion(indexer.ParameterTypes, indexes))
            .ThenByDescending(indexer => setter ? indexer.IsPublicSetter : indexer.IsPublicGetter)
            .Select(indexer => indexer.Property);

        static bool AcceptsWithoutConversion(Type[] parameterTypes, object[] indexes)
        {
            for (var i = 0; i < parameterTypes.Length; i++)
            {
                var parameterType = parameterTypes[i];
                var index = indexes[i];
                var accepted = index is null
                    ? !parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) is not null
                    : parameterType.IsInstanceOfType(index);
                if (!accepted)
                    return false;
            }

            return true;
        }
    }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        var type = _typeCache.Type;
        var flags = _originalObject is null ? StaticDefaultBindingFlags : InstanceDefaultBindingFlags;
        flags |= BindingFlags.InvokeMethod;

        while (type != null)
        {
            try
            {
                result = type.InvokeMember(binder.Name, flags, binder: null, _originalObject, args, culture: null);
                return true;
            }
            catch (MissingMethodException)
            {
                type = type.BaseType;
            }
        }

        result = null;
        return false;
    }

    public override IEnumerable<string> GetDynamicMemberNames()
    {
        // Only the members that TryGetMember can read: the static ones for a type, the instance ones for an object.
        // Indexers have no name to use, and the compiler-generated fields, such as backing fields, are not members of the source code.
        var (properties, fields) = _originalObject is null
            ? (_typeCache.StaticProperties, _typeCache.StaticFields)
            : (_typeCache.InstanceProperties, _typeCache.InstanceFields);

        return properties.Keys
            .Concat(fields.Where(field => !field.Value.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)).Select(field => field.Key))
            .Distinct(StringComparer.Ordinal);
    }

    public override bool TryConvert(ConvertBinder binder, out object? result)
    {
        var targetType = binder.Type;
        if (_originalObject is null)
        {
            // A wrapped type has no value: it converts to null, when the target type accepts it
            result = null;
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null;
        }

        if (targetType.IsInstanceOfType(_originalObject))
        {
            result = _originalObject;
            return true;
        }

        var conversionType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (_originalObject is IConvertible)
        {
            try
            {
                result = Convert.ChangeType(_originalObject, conversionType, provider: null);
                return true;
            }
            catch (InvalidCastException)
            {
            }
        }

        result = null;
        return false;
    }

    public override string? ToString()
    {
        if (_originalObject is not null)
            return _originalObject.ToString();

        return null;
    }

    private bool TryGetMemberValue(string name, out object? result)
    {
        if (_originalObject is null)
        {
            if (_typeCache.StaticProperties.TryGetValue(name, out var property))
            {
                result = property.GetValue(null);
                return true;
            }

            if (_typeCache.StaticFields.TryGetValue(name, out var field))
            {
                result = field.GetValue(null);
                return true;
            }
        }
        else
        {
            if (_typeCache.InstanceProperties.TryGetValue(name, out var property))
            {
                result = property.GetValue(_originalObject);
                return true;
            }

            if (_typeCache.InstanceFields.TryGetValue(name, out var field))
            {
                result = field.GetValue(_originalObject);
                return true;
            }
        }

        result = null;
        return false;
    }

    private bool TrySetMemberValue(string name, object? value)
    {
        if (_originalObject is null)
        {
            if (_typeCache.StaticProperties.TryGetValue(name, out var property))
            {
                property.SetValue(null, value);
                return true;
            }

            if (_typeCache.StaticFields.TryGetValue(name, out var field))
            {
                field.SetValue(null, value);
                return true;
            }
        }
        else
        {
            if (_typeCache.InstanceProperties.TryGetValue(name, out var property))
            {
                property.SetValue(_originalObject, value);
                return true;
            }

            if (_typeCache.InstanceFields.TryGetValue(name, out var field))
            {
                field.SetValue(_originalObject, value);
                return true;
            }
        }

        return false;
    }

    // An ArgumentException means the indexes do not match the parameters, so the next indexer can be tried.
    // An exception thrown by the indexer itself, such as a KeyNotFoundException, is rethrown as is.
    private static bool TryGetIndex(PropertyInfo indexer, object? instance, object[] indexes, out object? result)
    {
        try
        {
            result = indexer.GetValue(instance, indexes);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
        }

        result = null;
        return false;
    }

    private static bool TrySetIndex(PropertyInfo indexer, object? instance, object[] indexes, object? value)
    {
        try
        {
            indexer.SetValue(instance, value, indexes);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
        }

        return false;
    }

    private sealed class TypeCache
    {
        private const DynamicallyAccessedMemberTypes CacheMembers =
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.NonPublicConstructors |
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicMethods |
            DynamicallyAccessedMemberTypes.PublicFields |
            DynamicallyAccessedMemberTypes.NonPublicFields |
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.NonPublicProperties |
            DynamicallyAccessedMemberTypes.All;

        private TypeCache([DynamicallyAccessedMembers(CacheMembers)] Type type)
        {
            Type = type;
        }

        [DynamicallyAccessedMembers(CacheMembers)]
        public Type Type { get; }

        public List<(ConstructorInfo Constructor, int ParameterCount)> Constructors { get; } = [];

        public Dictionary<string, PropertyInfo> InstanceProperties { get; } = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        public Dictionary<string, FieldInfo> InstanceFields { get; } = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        public List<IndexerInfo> InstanceIndexers { get; } = [];

        public Dictionary<string, PropertyInfo> StaticProperties { get; } = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        public Dictionary<string, FieldInfo> StaticFields { get; } = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        public List<IndexerInfo> StaticIndexers { get; } = [];

        public static TypeCache Create([DynamicallyAccessedMembers(CacheMembers)] Type type)
        {
            var typeCache = new TypeCache(type);
            foreach (var constructor in type.GetConstructors())
            {
                typeCache.Constructors.Add((constructor, constructor.GetParameters().Length));
            }

            var currentType = type;
            while (currentType != null)
            {
                // Instances
                foreach (var propertyInfo in currentType.GetProperties(InstanceDefaultBindingFlags))
                {
                    var indexParameters = propertyInfo.GetIndexParameters();
                    if (indexParameters.Length != 0)
                    {
                        typeCache.InstanceIndexers.Add(new IndexerInfo(propertyInfo, indexParameters));
                    }
                    else
                    {
                        typeCache.InstanceProperties.TryAdd(propertyInfo.Name, propertyInfo);
                    }
                }

                foreach (var fieldInfo in currentType.GetFields(InstanceDefaultBindingFlags))
                {
                    typeCache.InstanceFields.TryAdd(fieldInfo.Name, fieldInfo);
                }

                // Static
                foreach (var propertyInfo in currentType.GetProperties(StaticDefaultBindingFlags))
                {
                    var indexParameters = propertyInfo.GetIndexParameters();
                    if (indexParameters.Length != 0)
                    {
                        typeCache.StaticIndexers.Add(new IndexerInfo(propertyInfo, indexParameters));
                    }
                    else
                    {
                        typeCache.StaticProperties.TryAdd(propertyInfo.Name, propertyInfo);
                    }
                }

                foreach (var fieldInfo in currentType.GetFields(StaticDefaultBindingFlags))
                {
                    typeCache.StaticFields.TryAdd(fieldInfo.Name, fieldInfo);
                }

                currentType = currentType.BaseType;
            }

            return typeCache;
        }
    }

    private sealed class IndexerInfo(PropertyInfo property, ParameterInfo[] parameters)
    {
        public PropertyInfo Property { get; } = property;

        public Type[] ParameterTypes { get; } = Array.ConvertAll(parameters, parameter => parameter.ParameterType);

        public bool IsPublicGetter { get; } = property.GetMethod?.IsPublic is true;

        public bool IsPublicSetter { get; } = property.SetMethod?.IsPublic is true;
    }
}
