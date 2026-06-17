using System.Reflection;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Yamlish.Internals;

internal sealed class YamlishTypeInfo
{
    private YamlishTypeInfo(YamlishMemberInfo[] serializableMembers, YamlishMemberInfo[] deserializableMembers, string[] ignoredDeserializationMemberNames, YamlishConstructorInfo? constructor, YamlishPolymorphismInfo? polymorphismInfo)
    {
        SerializableMembers = serializableMembers;
        DeserializableMembers = deserializableMembers;
        IgnoredDeserializationMemberNames = ignoredDeserializationMemberNames;
        Constructor = constructor;
        PolymorphismInfo = polymorphismInfo;
    }

    public YamlishMemberInfo[] SerializableMembers { get; }

    public YamlishMemberInfo[] DeserializableMembers { get; }

    public string[] IgnoredDeserializationMemberNames { get; }

    public YamlishConstructorInfo? Constructor { get; }

    public YamlishPolymorphismInfo? PolymorphismInfo { get; }

    public static YamlishTypeInfo Create(Type type, YamlishSerializerOptions options)
    {
        var nullabilityInfoContext = new NullabilityInfoContext();
        return new YamlishTypeInfo(
            GetSerializableMembers(type, options, nullabilityInfoContext).ToArray(),
            GetDeserializableMembers(type, options, nullabilityInfoContext).ToArray(),
            GetIgnoredDeserializationMemberNames(type, options).ToArray(),
            GetConstructor(type, options, nullabilityInfoContext),
            GetPolymorphismInfo(type, options));
    }

    private static YamlishPolymorphismInfo? GetPolymorphismInfo(Type type, YamlishSerializerOptions options)
    {
        var derivedTypeAttributes = options.GetCustomAttributes<YamlishDerivedTypeAttribute>(type).ToArray();
        if (derivedTypeAttributes.Length is 0)
            return null;

        var polymorphicAttribute = options.GetCustomAttribute<YamlishPolymorphicAttribute>(type);
        var typeDiscriminatorPropertyName = polymorphicAttribute?.TypeDiscriminatorPropertyName ?? "$type";
        var derivedTypes = new YamlishDerivedTypeInfo[derivedTypeAttributes.Length];
        var discriminatorValues = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < derivedTypeAttributes.Length; i++)
        {
            var attribute = derivedTypeAttributes[i];
            if (!type.IsAssignableFrom(attribute.DerivedType))
                throw new InvalidOperationException($"The derived type '{attribute.DerivedType.FullName}' is not assignable to polymorphic base type '{type.FullName}'.");

            if (!discriminatorValues.Add(attribute.TypeDiscriminator))
                throw new InvalidOperationException($"The type discriminator '{attribute.TypeDiscriminator}' is already used for polymorphic base type '{type.FullName}'.");

            derivedTypes[i] = new YamlishDerivedTypeInfo(attribute.DerivedType, attribute.TypeDiscriminator);
        }

        return new YamlishPolymorphismInfo(typeDiscriminatorPropertyName, derivedTypes);
    }

    private static YamlishConstructorInfo? GetConstructor(Type type, YamlishSerializerOptions options, NullabilityInfoContext nullabilityInfoContext)
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
                parameter.IsOptional ? parameter.DefaultValue : GetDefaultValue(parameter.ParameterType),
                IsNullable(nullabilityInfoContext.Create(parameter).ReadState));
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

    private static IEnumerable<YamlishMemberInfo> GetSerializableMembers(Type type, YamlishSerializerOptions options, NullabilityInfoContext nullabilityInfoContext)
    {
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod is null || property.GetIndexParameters().Length > 0 || (options.IgnoreReadOnlyProperties && property.SetMethod is null))
                continue;

            var ignoreCondition = GetIgnoreCondition(property, options);
            if (ignoreCondition is not YamlishIgnoreCondition.Always)
            {
                var nullabilityInfo = nullabilityInfoContext.Create(property);
                var serializationStyle = GetSerializationStyle(property, options);
                yield return new YamlishMemberInfo(GetName(property, options), property.PropertyType, property.GetValue, SetValue: null, ignoreCondition, serializationStyle.SequenceStyle, serializationStyle.ScalarStyle, serializationStyle.ScalarChomping, GetDefaultValue(property.PropertyType, ignoreCondition), IsRequired: false, IsNullable(nullabilityInfo.ReadState), IsSetNullable: true);
            }
        }

        if (options.IncludeFields)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (options.IgnoreReadOnlyFields && field.IsInitOnly)
                    continue;

                var ignoreCondition = GetIgnoreCondition(field, options);
                if (ignoreCondition is not YamlishIgnoreCondition.Always)
                {
                    var nullabilityInfo = nullabilityInfoContext.Create(field);
                    var serializationStyle = GetSerializationStyle(field, options);
                    yield return new YamlishMemberInfo(GetName(field, options), field.FieldType, field.GetValue, field.SetValue, ignoreCondition, serializationStyle.SequenceStyle, serializationStyle.ScalarStyle, serializationStyle.ScalarChomping, GetDefaultValue(field.FieldType, ignoreCondition), IsRequired: false, IsNullable(nullabilityInfo.ReadState), IsSetNullable: true);
                }
            }
        }
    }

    private static IEnumerable<YamlishMemberInfo> GetDeserializableMembers(Type type, YamlishSerializerOptions options, NullabilityInfoContext nullabilityInfoContext)
    {
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if ((property.SetMethod is null && options.PreferredObjectCreationHandling is YamlishObjectCreationHandling.Replace) ||
                property.GetIndexParameters().Length > 0 ||
                GetAttributeIgnoreCondition(property, options) is YamlishIgnoreCondition.Always)
                continue;

            var nullabilityInfo = nullabilityInfoContext.Create(property);
            yield return new YamlishMemberInfo(
                GetName(property, options),
                property.PropertyType,
                property.GetValue,
                property.SetMethod is null ? null : property.SetValue,
                YamlishIgnoreCondition.Never,
                YamlishSequenceStyle.Auto,
                YamlishScalarStyle.Auto,
                YamlishScalarChomping.Clip,
                DefaultValue: null,
                property.IsDefined(typeof(RequiredMemberAttribute)),
                IsGetNullable: true,
                IsNullable(nullabilityInfo.WriteState));
        }

        if (options.IncludeFields)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!field.IsInitOnly && GetAttributeIgnoreCondition(field, options) is not YamlishIgnoreCondition.Always)
                {
                    var nullabilityInfo = nullabilityInfoContext.Create(field);
                    yield return new YamlishMemberInfo(
                        GetName(field, options),
                        field.FieldType,
                        field.GetValue,
                        field.SetValue,
                        YamlishIgnoreCondition.Never,
                        YamlishSequenceStyle.Auto,
                        YamlishScalarStyle.Auto,
                        YamlishScalarChomping.Clip,
                        DefaultValue: null,
                        field.IsDefined(typeof(RequiredMemberAttribute)),
                        IsGetNullable: true,
                        IsNullable(nullabilityInfo.WriteState));
                }
            }
        }
    }

    private static IEnumerable<string> GetIgnoredDeserializationMemberNames(Type type, YamlishSerializerOptions options)
    {
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length is 0 && GetAttributeIgnoreCondition(property, options) is YamlishIgnoreCondition.Always)
                yield return GetName(property, options);
        }

        if (options.IncludeFields)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (GetAttributeIgnoreCondition(field, options) is YamlishIgnoreCondition.Always)
                    yield return GetName(field, options);
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

    private static SerializationStyle GetSerializationStyle(MemberInfo member, YamlishSerializerOptions options)
    {
        var sequenceStyle = options.GetCustomAttribute<YamlishSequenceStyleAttribute>(member)?.Style ?? YamlishSequenceStyle.Auto;
        var scalarAttribute = options.GetCustomAttribute<YamlishScalarStyleAttribute>(member);
        var scalarStyle = scalarAttribute?.Style ?? YamlishScalarStyle.Auto;
        var scalarChomping = scalarAttribute?.Chomping ?? YamlishScalarChomping.Clip;

        if (!Enum.IsDefined(sequenceStyle))
            throw new InvalidOperationException($"The sequence style configured on member '{member.Name}' is invalid.");

        if (!Enum.IsDefined(scalarStyle))
            throw new InvalidOperationException($"The scalar style configured on member '{member.Name}' is invalid.");

        if (!Enum.IsDefined(scalarChomping))
            throw new InvalidOperationException($"The scalar chomping configured on member '{member.Name}' is invalid.");

        return new SerializationStyle(sequenceStyle, scalarStyle, scalarChomping);
    }

    private static object? GetDefaultValue(Type type, YamlishIgnoreCondition ignoreCondition)
    {
        return ignoreCondition is YamlishIgnoreCondition.WhenWritingDefault && type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static object? GetDefaultValue(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static bool IsNullable(NullabilityState state)
    {
        return state is not NullabilityState.NotNull;
    }

    private sealed record SerializationStyle(YamlishSequenceStyle SequenceStyle, YamlishScalarStyle ScalarStyle, YamlishScalarChomping ScalarChomping);
}
