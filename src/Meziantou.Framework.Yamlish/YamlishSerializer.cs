using System.Collections;

namespace Meziantou.Framework.Yamlish;

public static class YamlishSerializer
{
    public static string Serialize<T>(T? value, YamlishSerializerOptions? options = null)
    {
        return Serialize(value, typeof(T), options);
    }

    public static string Serialize(object? value, Type type, YamlishSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (value is not null && !type.IsInstanceOfType(value))
            throw new ArgumentException($"The value is not assignable to type '{type}'.", nameof(value));

        options ??= new YamlishSerializerOptions();
        ValidateOptions(options);
        options.MakeReadOnly();
        var root = ObjectBinder.Serialize(value, type, options, depth: 0);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        YamlishWriter.Write(writer, root, options.IndentCharacter, options.IndentSize, options.NewLine);
        return writer.ToString();
    }

    public static T? Deserialize<T>(string content, YamlishSerializerOptions? options = null)
    {
        return (T?)Deserialize(content, typeof(T), options);
    }

    public static object? Deserialize(string content, Type type, YamlishSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(type);
        options ??= new YamlishSerializerOptions();
        ValidateOptions(options);
        options.MakeReadOnly();
        var document = new YamlishDocument(YamlishParser.Parse(content, options.AllowDuplicateProperties));
        return ObjectBinder.Deserialize(document.Root, type, options, depth: 0);
    }

    internal static YamlishNode SerializeToNode(object value, Type type, YamlishSerializerOptions options)
    {
        return ObjectBinder.Serialize(value, type, options, depth: 0);
    }

    internal static object? DeserializeNode(YamlishNode node, Type type, YamlishSerializerOptions options)
    {
        return ObjectBinder.Deserialize(node, type, options, depth: 0);
    }

    private static void ValidateOptions(YamlishSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.PropertyNameComparer);
        ArgumentNullException.ThrowIfNull(options.NewLine);
        if (options.IndentCharacter is not (' ' or '\t'))
            throw new ArgumentOutOfRangeException(nameof(options), "IndentCharacter must be a space or a tab.");

        if (options.IndentSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "IndentSize must be greater than zero.");

        if (options.NewLine is not ("\n" or "\r\n"))
            throw new ArgumentOutOfRangeException(nameof(options), "NewLine must be '\\n' or '\\r\\n'.");

        if (options.MaxDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDepth must be greater than zero.");

        if (!Enum.IsDefined(options.PreferredObjectCreationHandling))
            throw new ArgumentOutOfRangeException(nameof(options), "PreferredObjectCreationHandling is invalid.");
    }

    private static class ObjectBinder
    {
        public static YamlishNode Serialize(object? value, Type declaredType, YamlishSerializerOptions options, int depth)
        {
            EnsureDepth(options, depth);
            if (value is null)
                throw new InvalidOperationException("Yamlish does not have a null scalar. Configure DefaultIgnoreCondition or provide a non-null value.");

            var runtimeType = value.GetType();
            var converter = options.GetConverter(declaredType);
            var converterType = declaredType;
            if (converter is null && runtimeType != declaredType)
            {
                converter = options.GetConverter(runtimeType);
                converterType = runtimeType;
            }

            if (converter is not null)
                return converter.Write(value, converterType, options) ?? throw new InvalidOperationException($"The converter '{converter.GetType().FullName}' returned null.");

            if (value is IDictionary dictionary)
            {
                TryGetDictionaryValueType(declaredType, out var dictionaryValueType);
                var result = new YamlishMapping();
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not string key)
                        throw new NotSupportedException("Yamlish dictionaries must have string keys.");

                    if (ShouldIgnore(entry.Value, GetDefaultValue(entry.Value?.GetType() ?? typeof(object), options.DefaultIgnoreCondition), options.DefaultIgnoreCondition))
                        continue;

                    result.Add(key, Serialize(entry.Value, dictionaryValueType ?? entry.Value?.GetType() ?? typeof(object), options, depth + 1));
                }

                return result;
            }

            if (value is IEnumerable enumerable)
            {
                TryGetCollectionElementType(declaredType, out var elementType);
                var result = new YamlishSequence();
                foreach (var item in enumerable)
                {
                    if (ShouldIgnore(item, GetDefaultValue(item?.GetType() ?? typeof(object), options.DefaultIgnoreCondition), options.DefaultIgnoreCondition))
                        continue;

                    result.Add(Serialize(item, elementType ?? item?.GetType() ?? typeof(object), options, depth + 1));
                }

                return result;
            }

            var declaredTypeInfo = options.GetTypeInfo(declaredType);
            var runtimeTypeInfo = runtimeType == declaredType ? declaredTypeInfo : options.GetTypeInfo(runtimeType);
            var polymorphismInfo = declaredTypeInfo.PolymorphismInfo;
            var derivedTypeInfo = polymorphismInfo?.GetDerivedType(runtimeType);
            if (polymorphismInfo is not null && runtimeType != declaredType && derivedTypeInfo is null)
                throw new NotSupportedException($"Runtime type '{runtimeType.FullName}' is not configured as a derived type for polymorphic base type '{declaredType.FullName}'.");

            var mapping = new YamlishMapping();
            if (derivedTypeInfo is not null)
                mapping.Add(polymorphismInfo!.TypeDiscriminatorPropertyName, new YamlishScalar(derivedTypeInfo.TypeDiscriminator));

            foreach (var member in runtimeTypeInfo.SerializableMembers)
            {
                var memberValue = member.GetValue(value);
                if (options.RespectNullableAnnotations && memberValue is null && !member.IsGetNullable)
                    throw new InvalidOperationException($"The non-nullable member '{member.SerializedName}' on type '{runtimeType.FullName}' returned null.");

                if (ShouldIgnore(memberValue, member.DefaultValue, member.IgnoreCondition))
                    continue;

                var memberNode = Serialize(memberValue, member.MemberType, options, depth + 1);
                ApplySerializationStyle(memberNode, member);
                mapping.Add(member.SerializedName, memberNode);
            }

            return mapping;
        }

        public static object? Deserialize(YamlishNode node, Type type, YamlishSerializerOptions options, int depth, object? existingValue = null)
        {
            EnsureDepth(options, depth);
            var converter = options.GetConverter(type);
            if (converter is not null)
            {
                var result = converter.Read(node, type, options);
                if (result is null)
                {
                    if (type.IsValueType && Nullable.GetUnderlyingType(type) is null)
                        throw new InvalidOperationException($"The converter '{converter.GetType().FullName}' returned null for non-nullable type '{type.FullName}'.");
                }
                else if (!type.IsInstanceOfType(result))
                {
                    throw new InvalidOperationException($"The converter '{converter.GetType().FullName}' returned a value that is not compatible with '{type.FullName}'.");
                }

                return result;
            }

            var nullableType = Nullable.GetUnderlyingType(type);
            if (nullableType is not null)
                return Deserialize(node, nullableType, options, depth);

            if (type == typeof(object))
                return DeserializeUntyped(node, options, depth);

            if (TryGetDictionaryValueType(type, out var dictionaryValueType))
            {
                if (node is not YamlishMapping mapping)
                    throw CannotConvert(node, type);

                var dictionaryType = type.IsInterface || type.IsAbstract ? typeof(Dictionary<,>).MakeGenericType(typeof(string), dictionaryValueType) : type;
                var dictionary = existingValue as IDictionary ?? (IDictionary)(Activator.CreateInstance(dictionaryType) ?? throw CannotCreate(type));
                foreach (var entry in mapping)
                {
                    dictionary[entry.Key] = Deserialize(entry.Value, dictionaryValueType, options, depth + 1);
                }

                return dictionary;
            }

            if (TryGetCollectionElementType(type, out var elementType))
            {
                if (node is not YamlishSequence sequence)
                    throw CannotConvert(node, type);

                if (existingValue is not null && !type.IsArray && type.GetMethod("Add", [elementType]) is { } populateAddMethod)
                {
                    foreach (var item in sequence)
                    {
                        populateAddMethod.Invoke(existingValue, [Deserialize(item, elementType, options, depth + 1)]);
                    }

                    return existingValue;
                }

                var listType = typeof(List<>).MakeGenericType(elementType);
                var list = (IList)(Activator.CreateInstance(listType) ?? throw CannotCreate(type));
                foreach (var item in sequence)
                {
                    list.Add(Deserialize(item, elementType, options, depth + 1));
                }

                if (type.IsArray)
                {
                    var array = Array.CreateInstance(elementType, list.Count);
                    list.CopyTo(array, 0);
                    return array;
                }

                if (type.IsAssignableFrom(listType))
                    return list;

                var target = Activator.CreateInstance(type) ?? throw CannotCreate(type);
                var addMethod = type.GetMethod("Add", [elementType]) ?? throw CannotCreate(type);
                foreach (var item in list)
                {
                    addMethod.Invoke(target, [item]);
                }

                return target;
            }

            if (node is not YamlishMapping objectMapping)
                throw CannotConvert(node, type);

            var typeInfo = options.GetTypeInfo(type);
            if (TryGetPolymorphicType(objectMapping, type, typeInfo, options, out var polymorphicType))
                return Deserialize(node, polymorphicType, options, depth);

            HashSet<string>? constructorParameterNames = null;
            var instance = existingValue;
            if (instance is null)
            {
                instance = CreateObject(objectMapping, type, typeInfo, options, depth, out constructorParameterNames);
            }

            foreach (var member in typeInfo.DeserializableMembers)
            {
                var entry = objectMapping.FirstOrDefault(entry => options.PropertyNameComparer.Equals(entry.Key, member.SerializedName));
                if (entry.Key is null)
                {
                    if (member.IsRequired)
                        throw new FormatException($"Required member '{member.SerializedName}' for type '{type.FullName}' was not provided.");

                    continue;
                }

                if (constructorParameterNames?.Contains(member.SerializedName) is true)
                    continue;

                var memberValue = member.GetValue(instance);
                var value = options.PreferredObjectCreationHandling is YamlishObjectCreationHandling.Populate && memberValue is not null
                    ? Deserialize(entry.Value, member.MemberType, options, depth + 1, memberValue)
                    : Deserialize(entry.Value, member.MemberType, options, depth + 1);
                if (options.RespectNullableAnnotations && value is null && !member.IsSetNullable)
                    throw new FormatException($"The non-nullable member '{member.SerializedName}' on type '{type.FullName}' cannot be set to null.");

                member.SetValue?.Invoke(instance, value);
            }

            return instance;
        }

        private static bool TryGetPolymorphicType(YamlishMapping mapping, Type type, YamlishTypeInfo typeInfo, YamlishSerializerOptions options, [NotNullWhen(true)] out Type? polymorphicType)
        {
            polymorphicType = null;
            var polymorphismInfo = typeInfo.PolymorphismInfo;
            if (polymorphismInfo is null)
                return false;

            var typeDiscriminatorEntry = mapping.FirstOrDefault(entry => options.PropertyNameComparer.Equals(entry.Key, polymorphismInfo.TypeDiscriminatorPropertyName));
            if (typeDiscriminatorEntry.Key is null)
                return false;

            var typeDiscriminator = ConverterUtilities.GetScalarValue(typeDiscriminatorEntry.Value, type);
            var derivedType = polymorphismInfo.GetDerivedType(typeDiscriminator);
            if (derivedType is null)
                throw new FormatException($"The type discriminator '{typeDiscriminator}' is not configured for polymorphic base type '{type.FullName}'.");

            if (derivedType.Type == type)
                return false;

            polymorphicType = derivedType.Type;
            return true;
        }

        private static object CreateObject(YamlishMapping mapping, Type type, YamlishTypeInfo typeInfo, YamlishSerializerOptions options, int depth, out HashSet<string>? constructorParameterNames)
        {
            var constructor = typeInfo.Constructor;
            if (constructor is null)
            {
                constructorParameterNames = null;
                return Activator.CreateInstance(type) ?? throw CannotCreate(type);
            }

            if (constructor.Parameters.Length is 0)
            {
                constructorParameterNames = null;
                return constructor.Constructor.Invoke([]);
            }

            constructorParameterNames = new HashSet<string>(options.PropertyNameComparer);
            var arguments = new object?[constructor.Parameters.Length];
            for (var i = 0; i < constructor.Parameters.Length; i++)
            {
                var parameter = constructor.Parameters[i];
                var entry = mapping.FirstOrDefault(entry => options.PropertyNameComparer.Equals(entry.Key, parameter.SerializedName));
                if (entry.Key is null)
                {
                    if (options.RespectRequiredConstructorParameters && !parameter.IsOptional)
                        throw new FormatException($"Required constructor parameter '{parameter.SerializedName}' for type '{type.FullName}' was not provided.");

                    arguments[i] = parameter.DefaultValue;
                    if (options.RespectNullableAnnotations && arguments[i] is null && !parameter.IsNullable)
                        throw new FormatException($"The non-nullable constructor parameter '{parameter.SerializedName}' for type '{type.FullName}' cannot be set to null.");

                    continue;
                }

                constructorParameterNames.Add(parameter.SerializedName);
                arguments[i] = Deserialize(entry.Value, parameter.ParameterType, options, depth + 1);
                if (options.RespectNullableAnnotations && arguments[i] is null && !parameter.IsNullable)
                    throw new FormatException($"The non-nullable constructor parameter '{parameter.SerializedName}' for type '{type.FullName}' cannot be set to null.");
            }

            return constructor.Constructor.Invoke(arguments);
        }

        private static object DeserializeUntyped(YamlishNode node, YamlishSerializerOptions options, int depth)
        {
            return node switch
            {
                YamlishScalar scalar => scalar.Value,
                YamlishSequence sequence => sequence.Select(item => DeserializeUntyped(item, options, depth + 1)).ToList(),
                YamlishMapping mapping => mapping.ToDictionary(entry => entry.Key, entry => DeserializeUntyped(entry.Value, options, depth + 1), StringComparer.Ordinal),
                _ => throw new InvalidOperationException(),
            };
        }

        private static bool ShouldIgnore(object? value, object? defaultValue, YamlishIgnoreCondition condition)
        {
            return condition switch
            {
                YamlishIgnoreCondition.Never => false,
                YamlishIgnoreCondition.Always => true,
                YamlishIgnoreCondition.WhenWritingNull => value is null,
                YamlishIgnoreCondition.WhenWritingDefault => Equals(value, defaultValue),
                _ => throw new ArgumentOutOfRangeException(nameof(condition)),
            };
        }

        private static object? GetDefaultValue(Type type, YamlishIgnoreCondition condition)
        {
            return condition is YamlishIgnoreCondition.WhenWritingDefault && type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private static void ApplySerializationStyle(YamlishNode node, YamlishMemberInfo member)
        {
            if (member.SequenceStyle is not YamlishSequenceStyle.Auto)
            {
                if (node is not YamlishSequence)
                    throw new InvalidOperationException($"The member '{member.SerializedName}' is configured with a sequence style but did not serialize to a sequence node.");

                ApplySequenceStyle(node, member.SequenceStyle);
            }

            if (member.ScalarStyle is YamlishScalarStyle.Auto)
                return;

            if (node is not YamlishScalar scalar)
                throw new InvalidOperationException($"The member '{member.SerializedName}' is configured with a scalar style but did not serialize to a scalar node.");

            scalar.Style = member.ScalarStyle;
            scalar.Chomping = member.ScalarChomping;
        }

        private static void ApplySequenceStyle(YamlishNode node, YamlishSequenceStyle style)
        {
            if (node is YamlishSequence sequence)
            {
                sequence.Style = style;
                if (style is YamlishSequenceStyle.Block)
                {
                    foreach (var item in sequence)
                    {
                        ApplySequenceStyle(item, style);
                    }
                }
            }
            else if (style is YamlishSequenceStyle.Block && node is YamlishMapping mapping)
            {
                foreach (var entry in mapping)
                {
                    ApplySequenceStyle(entry.Value, style);
                }
            }
        }

        private static bool TryGetDictionaryValueType(Type type, [NotNullWhen(true)] out Type? valueType)
        {
            var dictionaryType = FindGenericInterface(type, typeof(IDictionary<,>));
            if (dictionaryType is not null && dictionaryType.GetGenericArguments()[0] == typeof(string))
            {
                valueType = dictionaryType.GetGenericArguments()[1];
                return true;
            }

            valueType = null;
            return false;
        }

        private static bool TryGetCollectionElementType(Type type, [NotNullWhen(true)] out Type? elementType)
        {
            if (type.IsArray)
            {
                elementType = type.GetElementType()!;
                return true;
            }

            var enumerableType = FindGenericInterface(type, typeof(IEnumerable<>));
            if (enumerableType is not null)
            {
                elementType = enumerableType.GetGenericArguments()[0];
                return true;
            }

            elementType = null;
            return false;
        }

        private static Type? FindGenericInterface(Type type, Type genericType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == genericType)
                return type;

            return type.GetInterfaces().FirstOrDefault(item => item.IsGenericType && item.GetGenericTypeDefinition() == genericType);
        }

        private static void EnsureDepth(YamlishSerializerOptions options, int depth)
        {
            if (depth > options.MaxDepth)
                throw new InvalidOperationException($"The maximum depth of {options.MaxDepth} was exceeded.");
        }

        private static InvalidOperationException CannotCreate(Type type) => new($"Cannot create an instance of type '{type}'.");

        private static FormatException CannotConvert(YamlishNode node, Type type) => new($"Cannot convert a {node.Kind} node to '{type}'.");

    }
}
