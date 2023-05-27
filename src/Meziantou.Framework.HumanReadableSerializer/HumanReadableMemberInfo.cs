using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace Meziantou.Framework.HumanReadable;

[DebuggerDisplay("{DebuggerDisplay}")]
internal sealed class HumanReadableMemberInfo
{
    public HumanReadableMemberInfo(Type memberType, Func<object, object?> getValue, HumanReadableIgnoreCondition ignoreCondition, string propertyName, HumanReadableConverter? converter, int? order, object? defaultValue)
    {
        MemberType = memberType;
        GetValue = getValue;
        IgnoreCondition = ignoreCondition;
        Name = propertyName;
        Converter = converter;
        Order = order;
        DefaultValue = defaultValue;
    }

    public Type MemberType { get; }
    public Func<object, object?> GetValue { get; }
    public HumanReadableIgnoreCondition IgnoreCondition { get; }
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
            if (data != null)
                members.Add(data);
        }

        foreach (var member in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var data = Get(member, options);
            if (data != null)
                members.Add(data);
        }

        members.Sort((a, b) => (a.Order, b.Order) switch
        {
            (not null, null) => -1,
            (null, not null) => 1,
            ({ } order1, { } order2) => order1.CompareTo(order2),
            _ => options.PropertyOrder == null ? 0 : options.PropertyOrder.Compare(a.Name, b.Name),
        });
        return members.ToArray();
    }

    public static HumanReadableMemberInfo? Get(PropertyInfo member, HumanReadableSerializerOptions options)
    {
        if (!member.CanRead)
            return null;

        var hasInclude = options.GetCustomAttribute<HumanReadableIncludeAttribute>(member) != null;
        if (!hasInclude && !(member.GetGetMethod()?.IsPublic ?? false))
            return null;

        if (!options.IncludeObsoleteMembers)
        {
            var obsoleteAttribute = member.GetCustomAttribute<ObsoleteAttribute>();
            if (obsoleteAttribute != null)
                return null;
        }

        var ignore = options.GetCustomAttribute<HumanReadableIgnoreAttribute>(member)?.Condition ?? options.DefaultIgnoreCondition;
        if (ignore == HumanReadableIgnoreCondition.Always)
            return null;

        var propertyName = options.GetCustomAttribute<HumanReadablePropertyNameAttribute>(member)?.Name ?? member.Name;
        var order = options.GetCustomAttribute<HumanReadablePropertyOrderAttribute>(member)?.Order;
        var converter = GetConverter(member, member.PropertyType, options);

        var defaultValueAttribute = options.GetCustomAttribute<HumanReadableDefaultValueAttribute>(member);
        var defaultValue = defaultValueAttribute != null ? defaultValueAttribute.DefaultValue : GetDefaultValue(ignore, member.PropertyType);
        return new HumanReadableMemberInfo(member.PropertyType, member.GetValue, ignore, propertyName, converter, order, defaultValue);
    }

    public static HumanReadableMemberInfo? Get(FieldInfo member, HumanReadableSerializerOptions options)
    {
        var hasInclude = options.GetCustomAttribute<HumanReadableIncludeAttribute>(member) != null;
        if (!hasInclude && !(member.IsPublic && options.IncludeFields))
            return null;

        var ignore = options.GetCustomAttribute<HumanReadableIgnoreAttribute>(member)?.Condition ?? options.DefaultIgnoreCondition;
        if (ignore == HumanReadableIgnoreCondition.Always)
            return null;

        var propertyName = options.GetCustomAttribute<HumanReadablePropertyNameAttribute>(member)?.Name ?? member.Name;
        var order = options.GetCustomAttribute<HumanReadablePropertyOrderAttribute>(member)?.Order;
        var converter = GetConverter(member, member.FieldType, options);
        var defaultValue = GetDefaultValue(ignore, member.FieldType);
        return new HumanReadableMemberInfo(member.FieldType, member.GetValue, ignore, propertyName, converter, order, defaultValue);
    }

    private static HumanReadableConverter? GetConverter(MemberInfo member, Type memberType, HumanReadableSerializerOptions options)
    {
        var converterAttribute = options.GetCustomAttribute<HumanReadableConverterAttribute>(member);
        if (converterAttribute != null)
            return HumanReadableConverter.CreateFromAttribute(converterAttribute, memberType);

        return null;
    }

    private static object? GetDefaultValue(HumanReadableIgnoreCondition ignoreCondition, Type type)
    {
        if (ignoreCondition is not HumanReadableIgnoreCondition.WhenWritingDefault and not HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection)
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

    public bool MustIgnore(object? memberValue)
    {
        return IgnoreCondition switch
        {
            HumanReadableIgnoreCondition.WhenWritingDefault => Equals(memberValue, DefaultValue),
            HumanReadableIgnoreCondition.WhenWritingNull => memberValue == null,
            HumanReadableIgnoreCondition.WhenWritingEmptyCollection => IsEmptyCollection(memberValue),
            HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection => Equals(memberValue, DefaultValue) || IsEmptyCollection(memberValue),
            _ => false,
        };

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
}
