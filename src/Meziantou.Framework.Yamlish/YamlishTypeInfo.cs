using System.Reflection;

namespace Meziantou.Framework.Yamlish;

internal sealed class YamlishTypeInfo
{
    private YamlishTypeInfo(YamlishMemberInfo[] serializableMembers, YamlishMemberInfo[] deserializableMembers)
    {
        SerializableMembers = serializableMembers;
        DeserializableMembers = deserializableMembers;
    }

    public YamlishMemberInfo[] SerializableMembers { get; }

    public YamlishMemberInfo[] DeserializableMembers { get; }

    public static YamlishTypeInfo Create(Type type, YamlishSerializerOptions options)
    {
        return new YamlishTypeInfo(GetSerializableMembers(type, options).ToArray(), GetDeserializableMembers(type, options).ToArray());
    }

    private static IEnumerable<YamlishMemberInfo> GetSerializableMembers(Type type, YamlishSerializerOptions options)
    {
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod is null || property.GetIndexParameters().Length > 0 || (options.IgnoreReadOnlyProperties && property.SetMethod is null))
                continue;

            var ignoreCondition = GetIgnoreCondition(property, options);
            if (ignoreCondition is not YamlishIgnoreCondition.Always)
                yield return new YamlishMemberInfo(GetName(property, options), property.PropertyType, property.GetValue, SetValue: null, ignoreCondition, GetDefaultValue(property.PropertyType, ignoreCondition));
        }

        if (options.IncludeFields)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (options.IgnoreReadOnlyFields && field.IsInitOnly)
                    continue;

                var ignoreCondition = GetIgnoreCondition(field, options);
                if (ignoreCondition is not YamlishIgnoreCondition.Always)
                    yield return new YamlishMemberInfo(GetName(field, options), field.FieldType, field.GetValue, field.SetValue, ignoreCondition, GetDefaultValue(field.FieldType, ignoreCondition));
            }
        }
    }

    private static IEnumerable<YamlishMemberInfo> GetDeserializableMembers(Type type, YamlishSerializerOptions options)
    {
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if ((property.SetMethod is null && options.PreferredObjectCreationHandling is YamlishObjectCreationHandling.Replace) ||
                property.GetIndexParameters().Length > 0 ||
                GetAttributeIgnoreCondition(property, options) is YamlishIgnoreCondition.Always)
                continue;

            yield return new YamlishMemberInfo(GetName(property, options), property.PropertyType, property.GetValue, property.SetMethod is null ? null : property.SetValue, YamlishIgnoreCondition.Never, DefaultValue: null);
        }

        if (options.IncludeFields)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!field.IsInitOnly && GetAttributeIgnoreCondition(field, options) is not YamlishIgnoreCondition.Always)
                    yield return new YamlishMemberInfo(GetName(field, options), field.FieldType, field.GetValue, field.SetValue, YamlishIgnoreCondition.Never, DefaultValue: null);
            }
        }
    }

    private static YamlishIgnoreCondition GetIgnoreCondition(MemberInfo member, YamlishSerializerOptions options)
    {
        return GetAttributeIgnoreCondition(member, options) ?? options.DefaultIgnoreCondition;
    }

    private static YamlishIgnoreCondition? GetAttributeIgnoreCondition(MemberInfo member, YamlishSerializerOptions options)
    {
        return options.GetCustomAttribute<YamlishIgnoreAttribute>(member)?.Condition;
    }

    private static string GetName(MemberInfo member, YamlishSerializerOptions options)
    {
        var attribute = options.GetCustomAttribute<YamlishPropertyNameAttribute>(member);
        return attribute?.Name ?? options.PropertyNamingPolicy?.ConvertName(member.Name) ?? member.Name;
    }

    private static object? GetDefaultValue(Type type, YamlishIgnoreCondition ignoreCondition)
    {
        return ignoreCondition is YamlishIgnoreCondition.WhenWritingDefault && type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
