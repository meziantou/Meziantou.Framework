using System.Reflection;

namespace Meziantou.Framework.HumanReadable;

internal sealed class HumanReadableMemberInfo
{
    public HumanReadableMemberInfo(Type memberType, Func<object, object?> getValue, HumanReadableIgnoreCondition ignoreCondition, string propertyName, HumanReadableConverter? converter, int order, object? defaultValue)
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
    public int Order { get; }
    public object? DefaultValue { get; }

    public static List<HumanReadableMemberInfo> Get(Type type, HumanReadableSerializerOptions options)
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

        members.Sort((a, b) => a.Order.CompareTo(b.Order));
        return members;
    }

    public static HumanReadableMemberInfo? Get(PropertyInfo member, HumanReadableSerializerOptions options)
    {
        if (!member.CanRead)
            return null;

        var hasInclude = options.GetCustomAttribute<HumanReadableIncludeAttribute>(member) != null;
        if (!hasInclude && !(member.GetGetMethod()?.IsPublic ?? false))
            return null;

        var ignore = options.GetCustomAttribute<HumanReadableIgnoreAttribute>(member)?.Condition ?? options.DefaultIgnoreCondition;
        if (ignore == HumanReadableIgnoreCondition.Always)
            return null;

        var propertyName = options.GetCustomAttribute<HumanReadablePropertyNameAttribute>(member)?.Name ?? member.Name;
        var order = options.GetCustomAttribute<HumanReadablePropertyOrderAttribute>(member)?.Order ?? 0;
        var converter = GetConverter(member, member.PropertyType, options);
        var defaultValue = GetDefaultValue(ignore, member.PropertyType);
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
        var order = options.GetCustomAttribute<HumanReadablePropertyOrderAttribute>(member)?.Order ?? 0;
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
        if (ignoreCondition != HumanReadableIgnoreCondition.WhenWritingDefault)
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
            _ => false,
        };
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private static class DefaultProvider<T>
    {
        public static T? Value => default;
    }
}
