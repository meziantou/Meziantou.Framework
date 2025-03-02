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

    public static HumanReadableMemberInfo[] Get(Type type, HumanReadableSerializerOptions options)
    {
        var members = new List<HumanReadableMemberInfo>();

        foreach (var member in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var data = Get(member, options);
            if (data is not null)
                members.Add(data);
        }

        foreach (var member in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var data = Get(member, options);
            if (data is not null)
                members.Add(data);
        }

        var result = members.Order(new MemberComparer(options)).ToArray();
        return result;
    }

    public static HumanReadableMemberInfo? Get(PropertyInfo member, HumanReadableSerializerOptions options)
    {
        if (!member.CanRead)
            return null;

        // Do not serializer indexer (e.g. this[int index])
        if (member.GetIndexParameters().Length > 0)
            return null;

        var hasInclude = options.GetCustomAttribute<HumanReadableIncludeAttribute>(member) is not null;
        if (!hasInclude && !(member.GetGetMethod()?.IsPublic ?? false))
            return null;

        if (!options.IncludeObsoleteMembers)
        {
            var obsoleteAttribute = member.GetCustomAttribute<ObsoleteAttribute>();
            if (obsoleteAttribute is not null)
                return null;
        }

        var ignoreAttributes = options.GetCustomAttributes<HumanReadableIgnoreAttribute>(member).ToArray();
        if (options.DefaultIgnoreCondition is HumanReadableIgnoreCondition.Always || ignoreAttributes.Any(attr => attr.Condition is HumanReadableIgnoreCondition.Always))
            return null;

        if (ignoreAttributes.Length == 0)
        {
            ignoreAttributes = [new HumanReadableIgnoreAttribute() { Condition = options.DefaultIgnoreCondition }];
        }

        var propertyName = options.GetCustomAttribute<HumanReadablePropertyNameAttribute>(member)?.Name ?? member.Name;
        var order = options.GetCustomAttribute<HumanReadablePropertyOrderAttribute>(member)?.Order;
        var converter = GetConverter(member, member.PropertyType, options);

        var defaultValueAttribute = options.GetCustomAttribute<HumanReadableDefaultValueAttribute>(member);
        var defaultValue = defaultValueAttribute is not null ? defaultValueAttribute.DefaultValue : GetDefaultValue(ignoreAttributes, member.PropertyType);

        object GetValue(object? instance)
        {
            try
            {
                return member.GetValue(instance);
            }
            catch (TargetInvocationException ex)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                return null;
            }
        }

        return new HumanReadableMemberInfo(member.PropertyType, GetValue, ignoreAttributes, propertyName, converter, order, defaultValue);
    }

    public static HumanReadableMemberInfo? Get(FieldInfo member, HumanReadableSerializerOptions options)
    {
        var hasInclude = options.GetCustomAttribute<HumanReadableIncludeAttribute>(member) is not null;
        if (!hasInclude && !(member.IsPublic && options.IncludeFields))
            return null;

        var ignoreAttributes = options.GetCustomAttributes<HumanReadableIgnoreAttribute>(member).ToArray();
        if (options.DefaultIgnoreCondition is HumanReadableIgnoreCondition.Always || ignoreAttributes.Any(attr => attr.Condition is HumanReadableIgnoreCondition.Always))
            return null;

        if (ignoreAttributes.Length == 0)
        {
            ignoreAttributes = [new HumanReadableIgnoreAttribute() { Condition = options.DefaultIgnoreCondition }];
        }

        var propertyName = options.GetCustomAttribute<HumanReadablePropertyNameAttribute>(member)?.Name ?? member.Name;
        var order = options.GetCustomAttribute<HumanReadablePropertyOrderAttribute>(member)?.Order;
        var converter = GetConverter(member, member.FieldType, options);
        var defaultValue = GetDefaultValue(ignoreAttributes, member.FieldType);
        return new HumanReadableMemberInfo(member.FieldType, member.GetValue, ignoreAttributes, propertyName, converter, order, defaultValue);
    }

    private static HumanReadableConverter? GetConverter(MemberInfo member, Type memberType, HumanReadableSerializerOptions options)
    {
        var converterAttribute = options.GetCustomAttribute<HumanReadableConverterAttribute>(member);
        if (converterAttribute is not null)
            return HumanReadableConverter.CreateFromAttribute(converterAttribute, memberType);

        return null;
    }

    private static object? GetDefaultValue(HumanReadableIgnoreAttribute[] attributes, Type type)
    {
        if (attributes.All(attr => attr.Condition is not HumanReadableIgnoreCondition.WhenWritingDefault and not HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection))
            return null;

        if (type.IsValueType)
        {
            if (type.IsPrimitive || type.IsEnum || Nullable.GetUnderlyingType(type) != null)
                return Activator.CreateInstance(type);

            var defaultType = typeof(DefaultProvider<>).MakeGenericType(type);
            return defaultType.InvokeMember(nameof(DefaultProvider<int>.Value), BindingFlags.Public | BindingFlags.Static | BindingFlags.GetProperty, binder: null, target: null, args: null, culture: null);
        }

        return null;
    }

    public bool MustIgnore(HumanReadableIgnoreData data)
    {
        if (data.Exception is not null)
        {
            foreach (var ignoreAttribute in IgnoreAttributes)
            {
                if (ignoreAttribute.Condition is HumanReadableIgnoreCondition.Custom && ignoreAttribute.CustomCondition is not null)
                    return ignoreAttribute.CustomCondition(data);
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
