using System.Reflection;

namespace Meziantou.Framework.Yamlish;

internal sealed class YamlishTypeInfo
{
    private YamlishTypeInfo(YamlishMemberInfo[] serializableMembers, YamlishMemberInfo[] deserializableMembers, YamlishConstructorInfo? constructor)
    {
        SerializableMembers = serializableMembers;
        DeserializableMembers = deserializableMembers;
        Constructor = constructor;
    }

    public YamlishMemberInfo[] SerializableMembers { get; }

    public YamlishMemberInfo[] DeserializableMembers { get; }

    public YamlishConstructorInfo? Constructor { get; }

    public static YamlishTypeInfo Create(Type type, YamlishSerializerOptions options)
    {
        return new YamlishTypeInfo(GetSerializableMembers(type, options).ToArray(), GetDeserializableMembers(type, options).ToArray(), GetConstructor(type, options));
    }

    private static YamlishConstructorInfo? GetConstructor(Type type, YamlishSerializerOptions options)
    {
        if (type.IsValueType)
            return null;

        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        var constructor = constructors.FirstOrDefault(constructor => constructor.GetParameters().Length is 0);
        if (constructor is not null)
            return new YamlishConstructorInfo(constructor, []);

        if (constructors.Length is not 1)
            return null;

        constructor = constructors[0];
        var parameters = constructor.GetParameters();
        var constructorParameters = new YamlishConstructorParameterInfo[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var member = FindConstructorParameterMember(type, parameter, options);
            if (member is null)
                throw new InvalidOperationException($"Constructor parameter '{parameter.Name}' on type '{type.FullName}' must bind to a public property or an included public field.");

            constructorParameters[i] = new YamlishConstructorParameterInfo(
                GetName(member, options),
                parameter.ParameterType,
                parameter.IsOptional,
                parameter.IsOptional ? parameter.DefaultValue : GetDefaultValue(parameter.ParameterType));
        }

        return new YamlishConstructorInfo(constructor, constructorParameters);
    }

    private static MemberInfo? FindConstructorParameterMember(Type type, ParameterInfo parameter, YamlishSerializerOptions options)
    {
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length is 0 &&
                property.PropertyType == parameter.ParameterType &&
                string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
            {
                return property;
            }
        }

        if (options.IncludeFields)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.FieldType == parameter.ParameterType && string.Equals(field.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                    return field;
            }
        }

        return null;
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

    private static object? GetDefaultValue(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
