using System.Collections;
using System.ComponentModel;
using System.Reflection;

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
        var root = ObjectBinder.Serialize(value, type, options, depth: 0);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        YamlishWriter.Write(writer, root, options.IndentSize);
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
        var document = YamlishDocument.Parse(content);
        return ObjectBinder.Deserialize(document.Root, type, options, depth: 0);
    }

    private static void ValidateOptions(YamlishSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.PropertyNameComparer);
        if (options.IndentSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "IndentSize must be greater than zero.");

        if (options.MaxDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDepth must be greater than zero.");
    }

    private static class ObjectBinder
    {
        public static YamlishNode Serialize(object? value, Type declaredType, YamlishSerializerOptions options, int depth)
        {
            EnsureDepth(options, depth);
            if (value is null)
                throw new InvalidOperationException("Yamlish does not have a null scalar. Configure IgnoreNullValues or provide a non-null value.");

            var runtimeType = value.GetType();
            if (IsScalarType(runtimeType))
                return new YamlishScalar(FormatScalar(value, runtimeType));

            if (value is IDictionary dictionary)
            {
                var result = new YamlishMapping();
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not string key)
                        throw new NotSupportedException("Yamlish dictionaries must have string keys.");

                    if (entry.Value is null && options.IgnoreNullValues)
                        continue;

                    result.Add(key, Serialize(entry.Value, entry.Value?.GetType() ?? typeof(object), options, depth + 1));
                }

                return result;
            }

            if (value is IEnumerable enumerable)
            {
                var result = new YamlishSequence();
                foreach (var item in enumerable)
                {
                    if (item is null && options.IgnoreNullValues)
                        continue;

                    result.Add(Serialize(item, item?.GetType() ?? typeof(object), options, depth + 1));
                }

                return result;
            }

            var mapping = new YamlishMapping();
            foreach (var member in GetSerializableMembers(runtimeType, options))
            {
                var memberValue = member.GetValue(value);
                if (memberValue is null && options.IgnoreNullValues)
                    continue;

                mapping.Add(member.SerializedName, Serialize(memberValue, member.MemberType, options, depth + 1));
            }

            return mapping;
        }

        public static object? Deserialize(YamlishNode node, Type type, YamlishSerializerOptions options, int depth)
        {
            EnsureDepth(options, depth);
            var nullableType = Nullable.GetUnderlyingType(type);
            if (nullableType is not null)
                return Deserialize(node, nullableType, options, depth);

            if (type == typeof(object))
                return DeserializeUntyped(node, options, depth);

            if (node is YamlishScalar scalar)
                return ParseScalar(scalar.Value, type);

            if (TryGetDictionaryValueType(type, out var dictionaryValueType))
            {
                if (node is not YamlishMapping mapping)
                    throw CannotConvert(node, type);

                var dictionaryType = type.IsInterface || type.IsAbstract ? typeof(Dictionary<,>).MakeGenericType(typeof(string), dictionaryValueType) : type;
                var dictionary = (IDictionary)(Activator.CreateInstance(dictionaryType) ?? throw CannotCreate(type));
                foreach (var entry in mapping)
                {
                    dictionary.Add(entry.Key, Deserialize(entry.Value, dictionaryValueType, options, depth + 1));
                }

                return dictionary;
            }

            if (TryGetCollectionElementType(type, out var elementType))
            {
                if (node is not YamlishSequence sequence)
                    throw CannotConvert(node, type);

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

            var instance = Activator.CreateInstance(type) ?? throw CannotCreate(type);
            foreach (var member in GetDeserializableMembers(type, options))
            {
                var entry = objectMapping.FirstOrDefault(entry => options.PropertyNameComparer.Equals(entry.Key, member.SerializedName));
                if (entry.Key is null)
                    continue;

                member.SetValue!(instance, Deserialize(entry.Value, member.MemberType, options, depth + 1));
            }

            return instance;
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

        private static IEnumerable<SerializableMember> GetSerializableMembers(Type type, YamlishSerializerOptions options)
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetMethod is null || property.GetIndexParameters().Length > 0 || property.IsDefined(typeof(YamlishIgnoreAttribute)))
                    continue;

                yield return new SerializableMember(GetName(property, options), property.PropertyType, property.GetValue, SetValue: null);
            }

            if (options.IncludeFields)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (!field.IsDefined(typeof(YamlishIgnoreAttribute)))
                        yield return new SerializableMember(GetName(field, options), field.FieldType, field.GetValue, field.SetValue);
                }
            }
        }

        private static IEnumerable<SerializableMember> GetDeserializableMembers(Type type, YamlishSerializerOptions options)
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.SetMethod is null || property.GetIndexParameters().Length > 0 || property.IsDefined(typeof(YamlishIgnoreAttribute)))
                    continue;

                yield return new SerializableMember(GetName(property, options), property.PropertyType, property.GetValue, property.SetValue);
            }

            if (options.IncludeFields)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (!field.IsInitOnly && !field.IsDefined(typeof(YamlishIgnoreAttribute)))
                        yield return new SerializableMember(GetName(field, options), field.FieldType, field.GetValue, field.SetValue);
                }
            }
        }

        private static string GetName(MemberInfo member, YamlishSerializerOptions options)
        {
            var attribute = member.GetCustomAttribute<YamlishPropertyNameAttribute>();
            return attribute?.Name ?? options.PropertyNamingPolicy?.ConvertName(member.Name) ?? member.Name;
        }

        private static bool IsScalarType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(string) || type == typeof(char) || type == typeof(bool) || type.IsEnum || type.IsPrimitive || type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan) || type == typeof(Uri);
        }

        private static string FormatScalar(object value, Type type)
        {
            if (type == typeof(bool))
                return (bool)value ? "true" : "false";

            if (value is IFormattable formattable)
                return formattable.ToString(format: null, CultureInfo.InvariantCulture);

            var converter = TypeDescriptor.GetConverter(type);
            if (converter.CanConvertTo(typeof(string)))
                return converter.ConvertToInvariantString(value) ?? string.Empty;

            return value.ToString() ?? string.Empty;
        }

        private static object ParseScalar(string value, Type type)
        {
            if (type == typeof(string))
                return value;

            if (type == typeof(char))
                return value.Length is 1 ? value[0] : throw new FormatException($"Cannot convert '{value}' to '{type}'.");

            if (type.IsEnum)
                return Enum.Parse(type, value, ignoreCase: true);

            var converter = TypeDescriptor.GetConverter(type);
            if (converter.CanConvertFrom(typeof(string)))
            {
                try
                {
                    return converter.ConvertFromInvariantString(value) ?? throw new FormatException($"Cannot convert '{value}' to '{type}'.");
                }
                catch (Exception exception) when (exception is not FormatException)
                {
                    throw new FormatException($"Cannot convert '{value}' to '{type}'.", exception);
                }
            }

            throw new NotSupportedException($"Type '{type}' cannot be deserialized from a Yamlish scalar.");
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

        private sealed record SerializableMember(string SerializedName, Type MemberType, Func<object, object?> GetValue, Action<object, object?>? SetValue);
    }
}
