using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.HumanReadable;

[DebuggerDisplay("{DebuggerDisplay}")]
internal sealed class HumanReadableMemberInfo
{
    public HumanReadableMemberInfo(Type memberType, Func<object, object?> getValue, HumanReadableIgnoreAttribute[] ignoreAttributes, string propertyName, HumanReadableConverter? converter, int? order, object? defaultValue)
    {
        MemberType = memberType;
        GetValue = getValue;
        IgnoreAttributes = ignoreAttributes;
        Name = propertyName;
        Converter = converter;
        Order = order;
        DefaultValue = defaultValue;
    }

    public Type MemberType { get; }
    public Func<object, object?> GetValue { get; }
    public HumanReadableIgnoreAttribute[] IgnoreAttributes { get; }
    public string Name { get; }
    public HumanReadableConverter? Converter { get; }
    public int? Order { get; }
    public object? DefaultValue { get; }

    private string DebuggerDisplay
    {
        get
        {
            return $"{Name} ({MemberType.FullName})";
        }
    }

    // Used for members that are not discovered by walking the type, such as the fields of an F# union case
    public static HumanReadableMemberInfo[] Get(Type type, IEnumerable<PropertyInfo> properties, HumanReadableSerializerOptions options)
    {
        var members = new List<HumanReadableMemberInfo>();
        foreach (var property in properties)
        {
            var data = Get(type, property, options);
            if (data is not null)
                members.Add(data);
        }

        return [.. members.Order(new MemberComparer(options))];
    }

    public static HumanReadableMemberInfo[] Get(Type type, HumanReadableSerializerOptions options)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // Walk the hierarchy explicitly: Type.GetFields/GetProperties never return the private members of the base types
        // (so [HumanReadableInclude] on them would be ignored), and they return both members when one hides the other with `new`.
        var fields = new List<(FieldInfo Member, int Depth)>();
        var properties = new List<(PropertyInfo Member, int Depth)>();
        foreach (var (current, depth) in GetTypeHierarchy(type))
        {
            foreach (var field in current.GetFields(Flags))
            {
                if (IsSerializable(type, field, options))
                    fields.Add((field, depth));
            }

            foreach (var property in current.GetProperties(Flags))
            {
                if (IsSerializable(type, property, options))
                    properties.Add((property, depth));
            }
        }

        // A member hides the members with the same name declared in its base types, as it does in C#.
        // This includes ignored members, so ignoring a `new` member does not reveal the hidden one.
        var visibleDepths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (member, memberDepth) in fields)
        {
            UpdateVisibleDepth(member.Name, memberDepth);
        }

        foreach (var (member, memberDepth) in properties)
        {
            UpdateVisibleDepth(member.Name, memberDepth);
        }

        var members = new List<HumanReadableMemberInfo>();
        foreach (var (member, memberDepth) in fields)
        {
            if (visibleDepths[member.Name] != memberDepth)
                continue;

            var data = Get(type, member, options);
            if (data is not null)
                members.Add(data);
        }

        foreach (var (member, memberDepth) in properties)
        {
            if (visibleDepths[member.Name] != memberDepth)
                continue;

            var data = Get(type, member, options);
            if (data is not null)
                members.Add(data);
        }

        var result = members.Order(new MemberComparer(options)).ToArray();
        return result;

        void UpdateVisibleDepth(string name, int memberDepth)
        {
            if (!visibleDepths.TryGetValue(name, out var existingDepth) || memberDepth < existingDepth)
            {
                visibleDepths[name] = memberDepth;
            }
        }

        static IEnumerable<(Type Type, int Depth)> GetTypeHierarchy(Type type)
        {
            if (type.IsInterface)
            {
                // Interfaces have no base type, their members are declared by the interfaces they inherit from
                yield return (type, 0);
                foreach (var iface in type.GetInterfaces())
                {
                    yield return (iface, 1);
                }

                yield break;
            }

            var depth = 0;
            for (var current = type; current is not null; current = current.BaseType)
            {
                yield return (current, depth);
                depth++;
            }
        }
    }

    private static bool IsSerializable(Type type, PropertyInfo member, HumanReadableSerializerOptions options)
    {
        if (!member.CanRead)
            return false;

        // Do not serializer indexer (e.g. this[int index])
        if (member.GetIndexParameters().Length > 0)
            return false;

        return (member.GetGetMethod()?.IsPublic ?? false) || options.GetCustomAttribute<HumanReadableIncludeAttribute>(type, member) is not null;
    }

    private static bool IsSerializable(Type type, FieldInfo member, HumanReadableSerializerOptions options)
    {
        return (member.IsPublic && options.IncludeFields) || options.GetCustomAttribute<HumanReadableIncludeAttribute>(type, member) is not null;
    }

    private static HumanReadableMemberInfo? Get(Type type, PropertyInfo member, HumanReadableSerializerOptions options)
    {
        var ignoreAttributes = GetIgnoreAttributes(type, member, options);
        if (ignoreAttributes is null)
            return null;

        object? GetValue(object? instance)
        {
            try
            {
                return member.GetValue(instance);
            }
            catch (TargetInvocationException ex)
            {
                if (ex.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                }

                throw;
            }
        }

        // PropertyInfo.GetValue dereferences the value of a ref-returning property (e.g. ref int Value => ref _value)
        var memberType = member.PropertyType.IsByRef ? member.PropertyType.GetElementType()! : member.PropertyType;
        return Create(type, member, memberType, GetValue, ignoreAttributes, options);
    }

    private static HumanReadableMemberInfo? Get(Type type, FieldInfo member, HumanReadableSerializerOptions options)
    {
        var ignoreAttributes = GetIgnoreAttributes(type, member, options);
        if (ignoreAttributes is null)
            return null;

        return Create(type, member, member.FieldType, member.GetValue, ignoreAttributes, options);
    }

    private static HumanReadableMemberInfo Create(Type type, MemberInfo member, Type memberType, Func<object, object?> getValue, HumanReadableIgnoreAttribute[] ignoreAttributes, HumanReadableSerializerOptions options)
    {
        var propertyName = options.GetCustomAttribute<HumanReadablePropertyNameAttribute>(type, member)?.Name ?? member.Name;
        var order = options.GetCustomAttribute<HumanReadablePropertyOrderAttribute>(type, member)?.Order;
        var converter = GetConverter(type, member, memberType, options);

        var defaultValueAttribute = options.GetCustomAttribute<HumanReadableDefaultValueAttribute>(type, member);
        var defaultValue = defaultValueAttribute is not null ? ConvertDefaultValue(member, defaultValueAttribute.DefaultValue, memberType) : GetDefaultValue(ignoreAttributes, memberType);
        return new HumanReadableMemberInfo(memberType, getValue, ignoreAttributes, propertyName, converter, order, defaultValue);
    }

    // Returns the conditions to evaluate, or null when the member is always ignored
    private static HumanReadableIgnoreAttribute[]? GetIgnoreAttributes(Type type, MemberInfo member, HumanReadableSerializerOptions options)
    {
        if (!options.IncludeObsoleteMembers && member.GetCustomAttribute<ObsoleteAttribute>() is not null)
            return null;

        var ignoreAttributes = options.GetCustomAttributes<HumanReadableIgnoreAttribute>(type, member).ToArray();
        if (ignoreAttributes.Any(attr => attr.Condition is HumanReadableIgnoreCondition.Always))
            return null;

        // A condition set on the member replaces the default condition. Custom conditions are added on top of it instead,
        // as they are often set on every member at once (e.g. IgnoreMembersThatThrow) and only handle specific cases.
        if (ignoreAttributes.All(attr => attr.Condition is HumanReadableIgnoreCondition.Custom))
        {
            if (options.DefaultIgnoreCondition is HumanReadableIgnoreCondition.Always)
                return null;

            ignoreAttributes = [.. ignoreAttributes, new HumanReadableIgnoreAttribute() { Condition = options.DefaultIgnoreCondition }];
        }

        return ignoreAttributes;
    }

    private static HumanReadableConverter? GetConverter(Type type, MemberInfo member, Type memberType, HumanReadableSerializerOptions options)
    {
        var converterAttribute = options.GetCustomAttribute<HumanReadableConverterAttribute>(type, member);
        if (converterAttribute is not null)
            return HumanReadableConverter.CreateFromAttribute(converterAttribute, memberType, options);

        return null;
    }

    // Attribute arguments are often literals of another type, such as an int for a long member, which object.Equals never considers equal
    private static object? ConvertDefaultValue(MemberInfo member, object? value, Type memberType)
    {
        var targetType = Nullable.GetUnderlyingType(memberType) ?? memberType;
        if (value is null || targetType.IsInstanceOfType(value))
            return value;

        try
        {
            if (targetType.IsEnum)
                return Enum.ToObject(targetType, value);

            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidCastException or FormatException or OverflowException)
        {
            throw new HumanReadableSerializerException($"The default value '{value}' of type '{value.GetType()}' cannot be converted to the type '{memberType}' of the member '{member.DeclaringType}.{member.Name}'", ex);
        }

        throw new HumanReadableSerializerException($"The default value '{value}' of type '{value.GetType()}' cannot be converted to the type '{memberType}' of the member '{member.DeclaringType}.{member.Name}'");
    }

    private static object? GetDefaultValue(HumanReadableIgnoreAttribute[] attributes, Type type)
    {
        if (attributes.All(attr => attr.Condition is not HumanReadableIgnoreCondition.WhenWritingDefault and not HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection))
            return null;

        // A byref-like type (e.g. Span<T>) cannot be a generic argument, and its value cannot be read through reflection anyway
        if (type.IsByRefLike)
            return null;

        if (type.IsValueType)
        {
            if (type.IsPrimitive || type.IsEnum || Nullable.GetUnderlyingType(type) != null)
                return Activator.CreateInstance(type);

            var defaultType = typeof(DefaultProvider<>).MakeGenericType(type);
            return defaultType.InvokeMember(nameof(DefaultProvider<>.Value), BindingFlags.Public | BindingFlags.Static | BindingFlags.GetProperty, binder: null, target: null, args: null, culture: null);
        }

        return null;
    }

    // Returns false when the member must not be written
    public bool TryGetValue(object instance, out object? value)
    {
        try
        {
            value = GetValue(instance);
            return !MustIgnore(new HumanReadableIgnoreData(value, exception: null));
        }
        catch (Exception ex)
        {
            if (MustIgnore(new HumanReadableIgnoreData(value: null, exception: ex)))
            {
                value = null;
                return false;
            }

            throw;
        }
    }

    public void WriteValue(HumanReadableTextWriter writer, object? value, HumanReadableSerializerOptions options)
    {
        if (Converter is not null)
        {
            Converter.WriteValue(writer, value, MemberType, options);
            return;
        }

        var actualType = value?.GetType() ?? MemberType;
        if (actualType == typeof(object))
        {
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteEmptyObject();
            }

            return;
        }

        HumanReadableSerializer.Serialize(writer, value, actualType, options);
    }

    public bool MustIgnore(HumanReadableIgnoreData data)
    {
        if (data.Exception is not null)
        {
            foreach (var ignoreAttribute in IgnoreAttributes)
            {
                if (ignoreAttribute.Condition is HumanReadableIgnoreCondition.Custom && ignoreAttribute.CustomCondition is not null && ignoreAttribute.CustomCondition(data))
                    return true;
            }

            return false;
        }

        foreach (var ignoreAttribute in IgnoreAttributes)
        {
            var result = ignoreAttribute.Condition switch
            {
                HumanReadableIgnoreCondition.WhenWritingDefault => Equals(data.Value, DefaultValue),
                HumanReadableIgnoreCondition.WhenWritingNull => data.Value is null,
                HumanReadableIgnoreCondition.WhenWritingEmptyCollection => IsEmptyCollection(data.Value),
                HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection => Equals(data.Value, DefaultValue) || IsEmptyCollection(data.Value),
                HumanReadableIgnoreCondition.Custom when ignoreAttribute.CustomCondition is not null => ignoreAttribute.CustomCondition(data),
                _ => false,
            };

            if (result)
                return true;
        }

        return false;

        static bool IsEmptyCollection(object? value)
        {
            if (value is IEnumerable enumerable)
            {
                var enumerator = enumerable.GetEnumerator();
                try
                {
                    return !enumerator.MoveNext();
                }
                finally
                {
                    if (enumerator is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }

            return false;
        }
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private static class DefaultProvider<T>
    {
        public static T? Value => default;
    }

    private sealed class MemberComparer(HumanReadableSerializerOptions options) : IComparer<HumanReadableMemberInfo>
    {
        public int Compare(HumanReadableMemberInfo? x, HumanReadableMemberInfo? y)
        {
            if (x is null && y is null)
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            if (x.Order is not null && y.Order is null)
                return -1;

            if (x.Order is null && y.Order is not null)
                return 1;

            if (x.Order is not null && y.Order is not null)
                return x.Order.Value.CompareTo(y.Order.Value);

            if (options.PropertyOrder is not null)
            {
                return options.PropertyOrder.Compare(x.Name, y.Name);
            }

            return 0;
        }
    }
}
