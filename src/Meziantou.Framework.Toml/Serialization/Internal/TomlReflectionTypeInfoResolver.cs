using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.Serialization.Converters;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Internal;

[RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
[RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
internal static class TomlReflectionTypeInfoResolver
{
    public static TomlTypeInfo? TryCreateTypeInfo(Type type, TomlSerializerOptions options)
    {
        ArgumentGuard.ThrowIfNull(type, nameof(type));
        ArgumentGuard.ThrowIfNull(options, nameof(options));

        if (!IsSupportedPocoType(type))
        {
            return null;
        }

        var mappingOrder = type.GetCustomAttribute<TomlMappingOrderAttribute>(inherit: true)?.Policy ?? options.MappingOrder;
        var dottedKeyHandling = type.GetCustomAttribute<TomlDottedKeyHandlingAttribute>(inherit: true)?.Handling;
        var members = CollectMembers(type, options, mappingOrder);

        var constructor = SelectConstructor(type);

        return new ReflectionObjectTomlTypeInfo(type, options, members, constructor, dottedKeyHandling);
    }

    private static ConstructorInfo? SelectConstructor(Type type)
    {
        if (type.IsValueType)
        {
            return null;
        }

        if (type.IsAbstract)
        {
            // Abstract base types can participate in polymorphic graphs even though they can't be instantiated.
            return null;
        }

        var ctors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        ConstructorInfo? annotated = null;

        for (var i = 0; i < ctors.Length; i++)
        {
            var ctor = ctors[i];
            if (!ctor.IsDefined(typeof(TomlConstructorAttribute), inherit: true) &&
                !ctor.IsDefined(typeof(JsonConstructorAttribute), inherit: true))
            {
                continue;
            }

            if (annotated is not null)
            {
                throw new TomlException($"Multiple constructors on type '{type.FullName}' are annotated with [TomlConstructor] or [JsonConstructor].");
            }

            annotated = ctor;
        }

        if (annotated is not null)
        {
            return annotated;
        }

        var parameterless = type.GetConstructor(Type.EmptyTypes);
        if (parameterless is not null)
        {
            return parameterless;
        }

        var publicCtors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        if (publicCtors.Length == 1)
        {
            return publicCtors[0];
        }

        throw new TomlException($"No suitable constructor could be selected for type '{type.FullName}'.");
    }

    private static bool IsSupportedPocoType(Type type)
    {
        if (type.IsPrimitive || type.IsEnum)
        {
            return false;
        }

        if (type == typeof(string) || type == typeof(object))
        {
            return false;
        }

        if (typeof(Delegate).IsAssignableFrom(type))
        {
            return false;
        }

        if (type.IsArray)
        {
            return false;
        }

        if (type.IsGenericTypeDefinition)
        {
            return false;
        }

        // Note: Type.IsByRefLike is not available on netstandard2.0.
        return type.IsClass || (type.IsValueType && !type.IsByRefLike);
    }

    private static List<MemberModel> CollectMembers(Type type, TomlSerializerOptions options, TomlMappingOrderPolicy mappingOrder)
    {
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var members = new List<MemberModel>(properties.Length);
        var typeObjectCreationHandling = GetObjectCreationHandling(type, options);

        foreach (var property in properties)
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (property.GetMethod is null || property.GetMethod.GetParameters().Length != 0)
            {
                continue;
            }

            if (!property.GetMethod.IsPublic && !HasIncludeAttribute(property))
            {
                continue;
            }

            var ignore = GetIgnoreBehavior(property);
            if (ignore.IgnoreAlways)
            {
                continue;
            }

            var name = GetSerializedName(property, property.Name, options);
            if (name is null)
            {
                continue;
            }

            Action<object, object?>? setter = null;
            if (property.SetMethod is not null && (property.SetMethod.IsPublic || HasIncludeAttribute(property)))
            {
                setter = (instance, value) => property.SetValue(instance, value);
            }

            // A property without an accessible setter is read-only; an init accessor makes it writable
            var writeIgnoreCondition = setter is null && options.IgnoreReadOnlyProperties ? TomlIgnoreCondition.WhenWriting : ignore.WriteIgnoreCondition;

            members.Add(new MemberModel(
                property,
                name,
                property.PropertyType,
                instance => property.GetValue(instance),
                setter,
                GetOrder(property),
                writeIgnoreCondition,
                ignore.IgnoreOnRead,
                GetDefaultValue(property.PropertyType),
                GetObjectCreationHandling(property, typeObjectCreationHandling),
                HasExplicitObjectCreationHandling(property),
                HasSingleOrArrayAttribute(property),
                IsRequired(property),
                IsExtensionData(property),
                CreateFormattingMetadata(property, property.PropertyType),
                TryCreateMemberConverter(property, property.PropertyType, options)));
        }

        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var field in fields)
        {
            if (!HasIncludeAttribute(field) && !(options.IncludeFields && field.IsPublic))
            {
                continue;
            }

            var ignore = GetIgnoreBehavior(field);
            if (ignore.IgnoreAlways)
            {
                continue;
            }

            var name = GetSerializedName(field, field.Name, options);
            if (name is null)
            {
                continue;
            }

            Action<object, object?>? setter = null;
            if (!field.IsInitOnly && !field.IsLiteral)
            {
                setter = (instance, value) => field.SetValue(instance, value);
            }

            var writeIgnoreCondition = field.IsInitOnly && options.IgnoreReadOnlyFields ? TomlIgnoreCondition.WhenWriting : ignore.WriteIgnoreCondition;

            members.Add(new MemberModel(
                field,
                name,
                field.FieldType,
                instance => field.GetValue(instance),
                setter,
                GetOrder(field),
                writeIgnoreCondition,
                ignore.IgnoreOnRead,
                GetDefaultValue(field.FieldType),
                GetObjectCreationHandling(field, typeObjectCreationHandling),
                HasExplicitObjectCreationHandling(field),
                HasSingleOrArrayAttribute(field),
                IsRequired(field),
                IsExtensionData(field),
                CreateFormattingMetadata(field, field.FieldType),
                TryCreateMemberConverter(field, field.FieldType, options)));
        }

        return OrderMembers(members, mappingOrder);
    }

    private static bool HasIncludeAttribute(MemberInfo member)
    {
        if (member.IsDefined(typeof(TomlIncludeAttribute), inherit: true))
        {
            return true;
        }

        if (member.IsDefined(typeof(JsonIncludeAttribute), inherit: true))
        {
            return true;
        }

        return false;
    }

    private static bool IsRequired(MemberInfo member)
    {
        if (member.IsDefined(typeof(TomlRequiredAttribute), inherit: true))
        {
            return true;
        }

        if (member.IsDefined(typeof(JsonRequiredAttribute), inherit: true))
        {
            return true;
        }

        return false;
    }

    private static bool IsExtensionData(MemberInfo member)
    {
        if (member.IsDefined(typeof(TomlExtensionDataAttribute), inherit: true))
        {
            return true;
        }

        if (member.IsDefined(typeof(JsonExtensionDataAttribute), inherit: true))
        {
            return true;
        }

        return false;
    }

    private static TomlPropertyMetadata? CreateFormattingMetadata(MemberInfo member, Type memberType)
    {
        TomlPropertyMetadata? metadata = null;

        var tableArrayStyle = member.GetCustomAttribute<TomlTableArrayStyleAttribute>(inherit: true);
        if (tableArrayStyle is not null)
        {
            metadata ??= new TomlPropertyMetadata();
            metadata.TableArrayStyle = tableArrayStyle.Style;
        }

        var inlineTable = member.GetCustomAttribute<TomlInlineTableAttribute>(inherit: true);
        if (inlineTable is not null)
        {
            metadata ??= new TomlPropertyMetadata();
            metadata.InlineTablePolicy = inlineTable.Policy;
        }

        var stringStyle = member.GetCustomAttribute<TomlStringStyleAttribute>(inherit: true);
        if (stringStyle is not null)
        {
            if (memberType != typeof(string))
            {
                throw new TomlException($"[TomlStringStyle] can only be applied to string members. Member '{member.Name}' is of type '{memberType.FullName}'.");
            }

            metadata ??= new TomlPropertyMetadata();
            metadata.StringStyle = stringStyle.Style;
            metadata.PreferLiteralWhenNoEscapes = ToNullableBool(stringStyle.PreferLiteralWhenNoEscapes);
            metadata.AllowHexEscapes = ToNullableBool(stringStyle.AllowHexEscapes);
        }

        return metadata;
    }

    private static bool? ToNullableBool(TomlBooleanPreference preference)
        => preference switch
        {
            TomlBooleanPreference.True => true,
            TomlBooleanPreference.False => false,
            _ => null,
        };

    private static TomlConverter? TryCreateMemberConverter(MemberInfo member, Type memberType, TomlSerializerOptions options)
    {
        var tomlConverter = member.GetCustomAttribute<TomlConverterAttribute>(inherit: true);
        if (tomlConverter is not null)
        {
            return CreateConverterFromAttribute(tomlConverter.ConverterType, memberType, options);
        }

        var jsonConverter = member.GetCustomAttribute<JsonConverterAttribute>(inherit: true);
        if (jsonConverter is not null && jsonConverter.ConverterType is not null)
        {
            if (memberType.IsEnum && IsJsonStringEnumConverter(jsonConverter.ConverterType))
            {
                return TomlStringEnumConverter.Instance;
            }

            return CreateConverterFromAttribute(jsonConverter.ConverterType, memberType, options);
        }

        return null;
    }

    private static bool IsJsonStringEnumConverter(Type converterType)
    {
        if (converterType.FullName == "System.Text.Json.Serialization.JsonStringEnumConverter")
        {
            return true;
        }

        return converterType.IsGenericType &&
            converterType.GetGenericTypeDefinition().FullName == "System.Text.Json.Serialization.JsonStringEnumConverter`1";
    }

    private static TomlConverter CreateConverterFromAttribute(Type converterType, Type typeToConvert, TomlSerializerOptions options)
    {
        if (!typeof(TomlConverter).IsAssignableFrom(converterType))
        {
            throw new TomlException($"Converter type '{converterType.FullName}' must derive from '{typeof(TomlConverter).FullName}'.");
        }

        if (converterType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new TomlException($"Converter type '{converterType.FullName}' must declare a public parameterless constructor.");
        }

        TomlConverter converter;
        try
        {
            converter = (TomlConverter)Activator.CreateInstance(converterType)!;
        }
        catch (Exception ex)
        {
            throw new TomlException($"Failed to create converter '{converterType.FullName}'.", ex);
        }

        if (converter is TomlConverterFactory factory)
        {
            var created = factory.CreateConverter(typeToConvert, options);
            if (created is null)
            {
                throw new TomlException($"The converter factory '{factory.GetType().FullName}' returned null.");
            }

            if (created is TomlConverterFactory)
            {
                throw new TomlException($"The converter factory '{factory.GetType().FullName}' returned another {nameof(TomlConverterFactory)}.");
            }

            if (!created.CanConvert(typeToConvert))
            {
                throw new TomlException(
                    $"The converter factory '{factory.GetType().FullName}' returned a converter that cannot convert '{typeToConvert.FullName}'.");
            }

            converter = created;
        }

        if (!converter.CanConvert(typeToConvert))
        {
            throw new TomlException($"Converter '{converterType.FullName}' cannot convert '{typeToConvert.FullName}'.");
        }

        return converter;
    }

    private static object? ReadWithConverter(TomlReader reader, TomlConverter converter, Type typeToConvert)
    {
        return TomlConverterHelper.Read(reader, converter, typeToConvert);
    }

    private static bool TryGetExtensionDataValueType(Type dictionaryType, out Type valueType)
    {
        valueType = typeof(object);

        // Extension data requires string keys (System.Text.Json parity).
        // We intentionally do not accept non-generic IDictionary since it cannot express a string-key invariant.
        var interfaces = dictionaryType.GetInterfaces();
        for (var i = -1; i < interfaces.Length; i++)
        {
            var iface = i < 0 ? dictionaryType : interfaces[i];
            if (!iface.IsGenericType)
            {
                continue;
            }

            var definition = iface.GetGenericTypeDefinition();
            if (definition != typeof(IDictionary<,>))
            {
                continue;
            }

            var args = iface.GetGenericArguments();
            if (args.Length == 2 && args[0] == typeof(string))
            {
                valueType = args[1];
                return true;
            }
        }

        return false;
    }

    private readonly record struct IgnoreBehavior(bool IgnoreAlways, bool IgnoreOnRead, TomlIgnoreCondition? WriteIgnoreCondition);

    private static IgnoreBehavior GetIgnoreBehavior(MemberInfo member)
    {
        var tomlIgnore = member.GetCustomAttribute<TomlIgnoreAttribute>(inherit: true);
        if (tomlIgnore is not null)
        {
            return tomlIgnore.Condition switch
            {
                TomlIgnoreCondition.Never => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: false, WriteIgnoreCondition: null),
                TomlIgnoreCondition.WhenWritingNull => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: false, WriteIgnoreCondition: TomlIgnoreCondition.WhenWritingNull),
                TomlIgnoreCondition.WhenWritingDefault => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: false, WriteIgnoreCondition: TomlIgnoreCondition.WhenWritingDefault),
                TomlIgnoreCondition.WhenWriting => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: false, WriteIgnoreCondition: TomlIgnoreCondition.WhenWriting),
                TomlIgnoreCondition.WhenReading => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: true, WriteIgnoreCondition: null),
                _ => new IgnoreBehavior(IgnoreAlways: true, IgnoreOnRead: false, WriteIgnoreCondition: null),
            };
        }

        var jsonIgnore = member.GetCustomAttribute<JsonIgnoreAttribute>(inherit: true);
        if (jsonIgnore is not null)
        {
            return (int)jsonIgnore.Condition switch
            {
                0 => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: false, WriteIgnoreCondition: null),
                1 => new IgnoreBehavior(IgnoreAlways: true, IgnoreOnRead: false, WriteIgnoreCondition: null),
                2 => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: false, WriteIgnoreCondition: TomlIgnoreCondition.WhenWritingDefault),
                3 => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: false, WriteIgnoreCondition: TomlIgnoreCondition.WhenWritingNull),
                4 => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: false, WriteIgnoreCondition: TomlIgnoreCondition.WhenWriting),
                5 => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: true, WriteIgnoreCondition: null),
                _ => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: false, WriteIgnoreCondition: null),
            };
        }

        return new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: false, WriteIgnoreCondition: null);
    }

    private static object? GetDefaultValue(Type type)
    {
        if (!type.IsValueType)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }

    private static TomlObjectCreationHandling GetObjectCreationHandling(Type type, TomlSerializerOptions options)
    {
        return GetDeclaredObjectCreationHandling(type) ?? options.PreferredObjectCreationHandling;
    }

    private static TomlObjectCreationHandling GetObjectCreationHandling(MemberInfo member, TomlObjectCreationHandling declaringTypeHandling)
    {
        return GetDeclaredObjectCreationHandling(member) ?? declaringTypeHandling;
    }

    private static bool HasExplicitObjectCreationHandling(MemberInfo member)
    {
        return GetDeclaredObjectCreationHandling(member) is not null;
    }

    // [TomlObjectCreationHandling] takes precedence over [JsonObjectCreationHandling]
    private static TomlObjectCreationHandling? GetDeclaredObjectCreationHandling(MemberInfo member)
    {
        var tomlAttribute = member.GetCustomAttribute<TomlObjectCreationHandlingAttribute>(inherit: true);
        if (tomlAttribute is not null)
        {
            return tomlAttribute.Handling;
        }

        var jsonAttribute = member.GetCustomAttribute<JsonObjectCreationHandlingAttribute>(inherit: true);
        if (jsonAttribute is not null)
        {
            return jsonAttribute.Handling switch
            {
                JsonObjectCreationHandling.Populate => TomlObjectCreationHandling.Populate,
                _ => TomlObjectCreationHandling.Replace,
            };
        }

        return null;
    }

    private static bool HasSingleOrArrayAttribute(MemberInfo member)
    {
        return member.IsDefined(typeof(TomlSingleOrArrayAttribute), inherit: true);
    }

    private static string? GetSerializedName(MemberInfo member, string defaultName, TomlSerializerOptions options)
    {
        var tomlName = member.GetCustomAttribute<TomlPropertyNameAttribute>(inherit: true);
        if (tomlName is not null)
        {
            return tomlName.Name;
        }

        var jsonName = member.GetCustomAttribute<JsonPropertyNameAttribute>(inherit: true);
        if (jsonName is not null)
        {
            return jsonName.Name;
        }

        var namingPolicy = options.PropertyNamingPolicy;
        if (namingPolicy is not null)
        {
            return namingPolicy.ConvertName(defaultName);
        }

        return defaultName;
    }

    private static int GetOrder(MemberInfo member)
    {
        var tomlOrder = member.GetCustomAttribute<TomlPropertyOrderAttribute>(inherit: true);
        if (tomlOrder is not null)
        {
            return tomlOrder.Order;
        }

        var jsonOrder = member.GetCustomAttribute<JsonPropertyOrderAttribute>(inherit: true);
        if (jsonOrder is not null)
        {
            return jsonOrder.Order;
        }

        return 0;
    }

    private static List<MemberModel> OrderMembers(List<MemberModel> members, TomlMappingOrderPolicy mappingOrder)
    {
        // Best-effort declaration order using MetadataToken.
        static int DeclarationOrder(MemberModel m)
        {
            try
            {
                return m.Member.MetadataToken;
            }
            catch
            {
                return 0;
            }
        }

        var comparer = StringComparer.Ordinal;

        return mappingOrder switch
        {
            TomlMappingOrderPolicy.Declaration => members.OrderBy(DeclarationOrder).ToList(),
            TomlMappingOrderPolicy.Alphabetical => members.OrderBy(m => m.SerializedName, comparer).ToList(),
            TomlMappingOrderPolicy.OrderThenDeclaration => members.OrderBy(m => m.Order).ThenBy(DeclarationOrder).ToList(),
            TomlMappingOrderPolicy.OrderThenAlphabetical => members.OrderBy(m => m.Order).ThenBy(m => m.SerializedName, comparer).ToList(),
            _ => members,
        };
    }

    private readonly record struct MemberModel(
        MemberInfo Member,
        string SerializedName,
        Type MemberType,
        Func<object, object?> Getter,
        Action<object, object?>? Setter,
        int Order,
        TomlIgnoreCondition? WriteIgnoreCondition,
        bool IgnoreOnRead,
        object? DefaultValue,
        TomlObjectCreationHandling ObjectCreationHandling,
        bool HasExplicitObjectCreationHandling,
        bool HasSingleOrArray,
        bool IsRequired,
        bool IsExtensionData,
        TomlPropertyMetadata? FormattingMetadata,
        TomlConverter? Converter);

    private static bool ShouldIgnoreValue(object? memberValue, TomlIgnoreCondition ignoreCondition, object? defaultValue)
    {
        return ignoreCondition switch
        {
            TomlIgnoreCondition.WhenWritingNull => memberValue is null,
            TomlIgnoreCondition.WhenWritingDefault => memberValue is null || (defaultValue is not null && Equals(memberValue, defaultValue)),
            TomlIgnoreCondition.WhenWriting => true,
            _ => false,
        };
    }

    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    private sealed class ReflectionObjectTomlTypeInfo : TomlTypeInfo
    {
        private readonly List<MemberModel> _members;
        private readonly Dictionary<string, int> _indexByName;
        private readonly ConstructorInfo? _constructor;
        private readonly ParameterBinding[] _parameters;
        private readonly Dictionary<string, int>? _parameterIndexByName;
        private readonly bool _hasRequiredMembers;
        private readonly int _extensionDataIndex;
        private readonly Type? _extensionDataValueType;
        private readonly StringComparer _nameComparer;
        private readonly bool _invokeOnSerializing;
        private readonly bool _invokeOnSerialized;
        private readonly bool _invokeOnDeserializing;
        private readonly bool _invokeOnDeserialized;
        private readonly TomlDottedKeyHandling? _dottedKeyHandling;

        public ReflectionObjectTomlTypeInfo(Type type, TomlSerializerOptions options, List<MemberModel> members, ConstructorInfo? constructor, TomlDottedKeyHandling? dottedKeyHandling)
            : base(type, options)
        {
            _members = members ?? throw new ArgumentNullException(nameof(members));
            _constructor = constructor;
            _dottedKeyHandling = dottedKeyHandling;
            _nameComparer = options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            _invokeOnSerializing = typeof(ITomlOnSerializing).IsAssignableFrom(type);
            _invokeOnSerialized = typeof(ITomlOnSerialized).IsAssignableFrom(type);
            _invokeOnDeserializing = typeof(ITomlOnDeserializing).IsAssignableFrom(type);
            _invokeOnDeserialized = typeof(ITomlOnDeserialized).IsAssignableFrom(type);
            _indexByName = new Dictionary<string, int>(
                _members.Count,
                _nameComparer);

            _extensionDataIndex = -1;
            _extensionDataValueType = null;
            for (var i = 0; i < _members.Count; i++)
            {
                if (!_members[i].IsExtensionData)
                {
                    continue;
                }

                if (_extensionDataIndex != -1)
                {
                    throw new TomlException($"Multiple extension data members are not supported on type '{type.FullName}'.");
                }

                if (!TryGetExtensionDataValueType(_members[i].MemberType, out var valueType))
                {
                    throw new TomlException($"Extension data member '{_members[i].Member.Name}' on '{type.FullName}' must be a dictionary-like type with string keys.");
                }

                _extensionDataIndex = i;
                _extensionDataValueType = valueType;
            }

            for (var i = 0; i < _members.Count; i++)
            {
                var name = _members[i].SerializedName;
                if (_members[i].IsExtensionData)
                {
                    continue;
                }

                if (!_indexByName.ContainsKey(name))
                {
                    _indexByName.Add(name, i);
                }
            }

            _hasRequiredMembers = false;
            for (var i = 0; i < _members.Count; i++)
            {
                if (_members[i].IsRequired && !_members[i].IgnoreOnRead)
                {
                    _hasRequiredMembers = true;
                    break;
                }
            }

            if (_constructor is null)
            {
                _parameters = Array.Empty<ParameterBinding>();
                _parameterIndexByName = null;
                return;
            }

            var ctorParameters = _constructor.GetParameters();
            if (ctorParameters.Length == 0)
            {
                _parameters = Array.Empty<ParameterBinding>();
                _parameterIndexByName = null;
                return;
            }

            _parameters = new ParameterBinding[ctorParameters.Length];
            _parameterIndexByName = new Dictionary<string, int>(
                ctorParameters.Length,
                options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            for (var i = 0; i < ctorParameters.Length; i++)
            {
                var parameter = ctorParameters[i];
                var parameterName = parameter.Name;
                if (string.IsNullOrEmpty(parameterName))
                {
                    var fallback = $"arg{i}";
                    _parameterIndexByName.Add(fallback, i);
                    _parameters[i] = new ParameterBinding(fallback, parameter.ParameterType, parameter.HasDefaultValue, parameter.DefaultValue, MemberIndex: null);
                    continue;
                }

                var memberIndex = FindMemberIndexByClrName(parameterName);
                var keyName = memberIndex >= 0
                    ? _members[memberIndex].SerializedName
                    : options.PropertyNamingPolicy?.ConvertName(parameterName) ?? parameterName;

                if (_parameterIndexByName.ContainsKey(keyName))
                {
                    throw new TomlException($"Constructor parameter name collision for key '{keyName}' on type '{type.FullName}'.");
                }

                _parameterIndexByName.Add(keyName, i);
                _parameters[i] = new ParameterBinding(keyName, parameter.ParameterType, parameter.HasDefaultValue, parameter.DefaultValue, memberIndex >= 0 ? memberIndex : null);
            }
        }

        public override bool WritesTable => true;

        public override void Write(TomlWriter writer, object? value)
        {
            ArgumentGuard.ThrowIfNull(writer, nameof(writer));

            if (value is null)
            {
                throw new TomlException("TOML does not support null values.");
            }

            if (_invokeOnSerializing)
            {
                ((ITomlOnSerializing)value).OnTomlSerializing();
            }

            writer.WriteStartTable();
            writer.TryAttachMetadata(value);
            HashSet<string>? usedKeys = null;
            if (_extensionDataIndex != -1)
            {
                usedKeys = new HashSet<string>(_nameComparer);
            }

            for (var i = 0; i < _members.Count; i++)
            {
                var member = _members[i];
                if (member.IsExtensionData || member.WriteIgnoreCondition == TomlIgnoreCondition.WhenWriting)
                {
                    continue;
                }

                var memberValue = member.Getter(value);

                var ignoreCondition = member.WriteIgnoreCondition ?? Options.DefaultIgnoreCondition;
                if (ShouldIgnoreValue(memberValue, ignoreCondition, member.DefaultValue))
                {
                    continue;
                }

                if (member.FormattingMetadata is not null)
                {
                    writer.ApplyPropertyMetadata(member.SerializedName, member.FormattingMetadata);
                }

                writer.WritePropertyName(member.SerializedName, _dottedKeyHandling);
                usedKeys?.Add(member.SerializedName);
                if (member.Converter is { } converter)
                {
                    converter.Write(writer, memberValue);
                }
                else
                {
                    var typeInfo = writer.ResolveTypeInfo(member.MemberType);
                    typeInfo.Write(writer, memberValue);
                }
            }

            if (_extensionDataIndex != -1)
            {
                var extensionMember = _members[_extensionDataIndex];
                var extensionDictionary = extensionMember.Getter(value) as IDictionary;
                if (extensionDictionary is not null)
                {
                    foreach (DictionaryEntry entry in extensionDictionary)
                    {
                        if (entry.Key is not string key)
                        {
                            throw new TomlException($"Extension data keys must be strings but encountered '{entry.Key?.GetType().FullName}'.");
                        }

                        if (Options.DictionaryKeyPolicy is { } keyPolicy)
                        {
                            key = keyPolicy.ConvertName(key);
                        }

                        if (usedKeys is not null && usedKeys.Contains(key))
                        {
                            throw new TomlException($"Extension data key '{key}' conflicts with an existing member key.");
                        }

                        writer.WritePropertyName(key);
                        if (_extensionDataValueType == typeof(object))
                        {
                            TomlUntypedObjectConverter.Instance.Write(writer, entry.Value);
                        }
                        else
                        {
                            var typeInfo = writer.ResolveTypeInfo(_extensionDataValueType!);
                            typeInfo.Write(writer, entry.Value);
                        }
                    }
                }
            }

            writer.WriteEndTable();

            if (_invokeOnSerialized)
            {
                ((ITomlOnSerialized)value).OnTomlSerialized();
            }
        }

        public override object? ReadAsObject(TomlReader reader)
        {
            ArgumentGuard.ThrowIfNull(reader, nameof(reader));

            if (reader.TokenType != TomlTokenType.StartTable)
            {
                throw reader.CreateException($"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.");
            }

            var tableStartSpan = reader.CurrentSpan;

            if (_parameters.Length > 0)
            {
                return ReadWithConstructor(reader, tableStartSpan);
            }

            var instance = CreateInstance();
            return ReadIntoExistingInstance(reader, instance, tableStartSpan, invokeDeserializingCallback: true);
        }

        public override object? ReadInto(TomlReader reader, object? existingValue)
        {
            ArgumentGuard.ThrowIfNull(reader, nameof(reader));

            if (existingValue is null || !Type.IsInstanceOfType(existingValue))
            {
                return base.ReadInto(reader, existingValue);
            }

            if (reader.TokenType != TomlTokenType.StartTable)
            {
                throw reader.CreateException($"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.");
            }

            return ReadIntoExistingInstance(reader, existingValue, reader.CurrentSpan, invokeDeserializingCallback: true);
        }

        private object ReadIntoExistingInstance(TomlReader reader, object instance, TomlSourceSpan? tableStartSpan, bool invokeDeserializingCallback)
        {
            if (invokeDeserializingCallback && _invokeOnDeserializing)
            {
                ((ITomlOnDeserializing)instance).OnTomlDeserializing();
            }

            TomlPropertiesMetadata? propertiesMetadata = null;
            if (Options.MetadataStore is not null && !Type.IsValueType)
            {
                propertiesMetadata = new TomlPropertiesMetadata();
            }

            var needsSeen = Options.DuplicateKeyHandling == TomlDuplicateKeyHandling.Error || _hasRequiredMembers;
            var seen = needsSeen ? new bool[_members.Count] : null;

            reader.Read(); // first property or end
            while (reader.TokenType != TomlTokenType.EndTable)
            {
                if (reader.TokenType != TomlTokenType.PropertyName)
                {
                    throw reader.CreateException($"Expected {TomlTokenType.PropertyName} token but was {reader.TokenType}.");
                }

                var leadingTrivia = reader.CurrentLeadingTrivia;
                var nameSpan = reader.CurrentSpan;
                var name = reader.PropertyName!;
                reader.Read(); // value
                CapturePropertyMetadata(propertiesMetadata, name, nameSpan, leadingTrivia, reader.CurrentTrailingTrivia, GetDisplayKind(reader));

                if (_indexByName.TryGetValue(name, out var memberIndex))
                {
                    var member = _members[memberIndex];
                    if (member.IgnoreOnRead)
                    {
                        reader.Skip();
                        continue;
                    }

                    if (seen is not null && seen[memberIndex])
                    {
                        if (TryReadTableHeaderExtension(reader, instance, member))
                        {
                            continue;
                        }

                        throw reader.CreateException($"Duplicate key '{name}' was encountered.");
                    }

                    if (seen is not null)
                    {
                        seen[memberIndex] = true;
                    }

                    if (member.Setter is null && member.ObjectCreationHandling != TomlObjectCreationHandling.Populate && !member.HasSingleOrArray)
                    {
                        reader.Skip();
                        continue;
                    }

                    var memberValueStartState = reader.CurrentState;
                    object? memberValue;
                    try
                    {
                        memberValue = ReadMemberValue(reader, instance, member);
                    }
                    catch (TomlException ex) when (reader.OperationState.CanAddDiagnostics(ex) && reader.IsStateUnchanged(memberValueStartState))
                    {
                        reader.OperationState.AddDiagnostics(ex);
                        reader.Skip();
                        continue;
                    }

                    if (member.Setter is not null)
                    {
                        member.Setter(instance, memberValue);
                    }

                    continue;
                }

                if (_extensionDataIndex != -1)
                {
                    var extensionDictionary = EnsureExtensionDataDictionary(instance);
                    var extensionValue = ReadExtensionValue(reader);
                    extensionDictionary[name] = extensionValue;
                    continue;
                }

                reader.Skip();
            }

            var endTableSpan = reader.CurrentSpan;
            reader.Read(); // consume EndTable

            if (seen is not null)
            {
                ValidateRequiredMembers(seen, endTableSpan ?? tableStartSpan);
            }

            if (propertiesMetadata is not null)
            {
                Options.MetadataStore!.SetProperties(instance, propertiesMetadata);
            }

            if (_invokeOnDeserialized)
            {
                ((ITomlOnDeserialized)instance).OnTomlDeserialized();
            }

            return instance;
        }

        private object? ReadMemberValue(TomlReader reader, object instance, MemberModel member)
        {
            if (member.HasSingleOrArray)
            {
                return ReadSingleOrArrayMemberValue(reader, instance, member);
            }

            if (member.ObjectCreationHandling != TomlObjectCreationHandling.Populate)
            {
                return ReflectionObjectTomlTypeInfo.ReadMemberValue(reader, member);
            }

            var existingValue = member.Getter(instance);
            return ReadMemberValueWithPopulate(reader, member, existingValue);
        }

        private static object? ReadMemberValue(TomlReader reader, MemberModel member)
        {
            if (member.Converter is { } converter)
            {
                return ReadWithConverter(reader, converter, member.MemberType);
            }

            var typeInfo = reader.ResolveTypeInfo(member.MemberType);
            return typeInfo.ReadAsObject(reader);
        }

        private object? ReadMemberValueWithPopulate(TomlReader reader, MemberModel member, object? existingValue)
        {
            if (member.MemberType.IsValueType && member.Setter is null)
            {
                if (member.HasExplicitObjectCreationHandling)
                {
                    throw reader.CreateException(
                        $"Member '{member.Member.Name}' on '{Type.FullName}' uses {nameof(TomlObjectCreationHandling)}.{nameof(TomlObjectCreationHandling.Populate)} but requires a setter because '{member.MemberType.FullName}' is a value type.");
                }

                reader.Skip();
                return existingValue;
            }

            if (existingValue is null)
            {
                if (member.Setter is null)
                {
                    reader.Skip();
                    return null;
                }

                return ReflectionObjectTomlTypeInfo.ReadMemberValue(reader, member);
            }

            object? populatedValue;
            if (member.Converter is { } converter)
            {
                populatedValue = ReadWithConverter(reader, converter, member.MemberType);
            }
            else
            {
                var typeInfo = reader.ResolveTypeInfo(member.MemberType);
                populatedValue = typeInfo.ReadInto(reader, existingValue);
            }

            if (member.Setter is null)
            {
                if (!ReferenceEquals(existingValue, populatedValue))
                {
                    if (member.HasExplicitObjectCreationHandling)
                    {
                        throw reader.CreateException(
                            $"Member '{member.Member.Name}' on '{Type.FullName}' uses {nameof(TomlObjectCreationHandling)}.{nameof(TomlObjectCreationHandling.Populate)} but '{member.MemberType.FullName}' doesn't support populating.");
                    }

                    return existingValue;
                }

                return existingValue;
            }

            return populatedValue;
        }

        private object? ReadSingleOrArrayMemberValue(TomlReader reader, object instance, MemberModel member)
        {
            var existingValue = member.Getter(instance);
            var shouldPopulateExisting = existingValue is not null && (member.Setter is null || member.ObjectCreationHandling == TomlObjectCreationHandling.Populate);

            if (reader.TokenType == TomlTokenType.StartArray)
            {
                if (!shouldPopulateExisting)
                {
                    return ReflectionObjectTomlTypeInfo.ReadMemberValue(reader, member);
                }

                if (!reader.OperationState.SingleOrArrayCollections.CanPopulate(member.MemberType, existingValue!))
                {
                    if (member.Setter is null)
                    {
                        throw reader.CreateException(
                            $"Member '{member.Member.Name}' on '{Type.FullName}' uses [TomlSingleOrArray] but '{member.MemberType.FullName}' doesn't support populating the existing collection.");
                    }

                    return ReflectionObjectTomlTypeInfo.ReadMemberValue(reader, member);
                }

                if (member.Converter is { } converter)
                {
                    return ReadWithConverter(reader, converter, member.MemberType);
                }

                var typeInfo = reader.ResolveTypeInfo(member.MemberType);
                var populatedValue = typeInfo.ReadInto(reader, existingValue);
                if (member.Setter is null && !ReferenceEquals(existingValue, populatedValue))
                {
                    throw reader.CreateException(
                        $"Member '{member.Member.Name}' on '{Type.FullName}' uses [TomlSingleOrArray] but '{member.MemberType.FullName}' doesn't support populating the existing collection.");
                }

                return populatedValue;
            }

            if (!reader.OperationState.SingleOrArrayCollections.IsSupported(member.MemberType))
            {
                throw reader.CreateException(
                    $"Member '{member.Member.Name}' on '{Type.FullName}' uses [TomlSingleOrArray] but '{member.MemberType.FullName}' is not a supported collection type.");
            }

            if (shouldPopulateExisting)
            {
                if (!reader.OperationState.SingleOrArrayCollections.CanPopulate(member.MemberType, existingValue!))
                {
                    if (member.Setter is null)
                    {
                        throw reader.CreateException(
                            $"Member '{member.Member.Name}' on '{Type.FullName}' uses [TomlSingleOrArray] but '{member.MemberType.FullName}' doesn't support populating the existing collection.");
                    }

                    return reader.OperationState.SingleOrArrayCollections.ReadSingleElementAsCollection(reader, member.MemberType);
                }

                return reader.OperationState.SingleOrArrayCollections.ReadSingleElementIntoExisting(reader, member.MemberType, existingValue!);
            }

            if (member.Setter is null)
            {
                throw reader.CreateException(
                    $"Member '{member.Member.Name}' on '{Type.FullName}' uses [TomlSingleOrArray] but the existing collection is null or cannot be populated.");
            }

            return reader.OperationState.SingleOrArrayCollections.ReadSingleElementAsCollection(reader, member.MemberType);
        }

        private object CreateInstance()
        {
            try
            {
                if (_constructor is not null && _constructor.GetParameters().Length == 0)
                {
                    return _constructor.Invoke(Array.Empty<object>())!;
                }

                return Activator.CreateInstance(Type)!;
            }
            catch (Exception ex)
            {
                throw new TomlException($"Failed to create an instance of '{Type.FullName}'.", ex);
            }
        }

        private IDictionary EnsureExtensionDataDictionary(object instance)
        {
            if (_extensionDataIndex == -1)
            {
                throw new InvalidOperationException("No extension data member is configured.");
            }

            var extensionMember = _members[_extensionDataIndex];
            var existing = extensionMember.Getter(instance);
            if (existing is IDictionary dictionary)
            {
                return dictionary;
            }

            if (existing is null)
            {
                if (extensionMember.Setter is null)
                {
                    throw new TomlException($"Extension data member '{extensionMember.Member.Name}' is null and cannot be initialized.");
                }

                var created = CreateExtensionDataDictionary(extensionMember.MemberType);
                extensionMember.Setter(instance, created);
                return created;
            }

            throw new TomlException($"Extension data member '{extensionMember.Member.Name}' must be a dictionary-like type.");
        }

        private IDictionary CreateExtensionDataDictionary(Type memberType)
        {
            if (!memberType.IsInterface && !memberType.IsAbstract)
            {
                try
                {
                    var created = Activator.CreateInstance(memberType);
                    if (created is IDictionary dictionary)
                    {
                        return dictionary;
                    }
                }
                catch
                {
                }
            }

            var valueType = _extensionDataValueType ?? typeof(object);
            var fallbackType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
            return (IDictionary)Activator.CreateInstance(fallbackType)!;
        }

        private object? ReadExtensionValue(TomlReader reader)
        {
            if (_extensionDataValueType == typeof(object))
            {
                return TomlUntypedObjectConverter.ReadValue(reader);
            }

            var typeInfo = reader.ResolveTypeInfo(_extensionDataValueType!);
            return typeInfo.ReadAsObject(reader);
        }

        private object ReadWithConstructor(TomlReader reader, TomlSourceSpan? tableStartSpan)
        {
            TomlPropertiesMetadata? propertiesMetadata = null;
            if (Options.MetadataStore is not null && !Type.IsValueType)
            {
                propertiesMetadata = new TomlPropertiesMetadata();
            }

            var ctorArgs = new object?[_parameters.Length];
            var ctorSeen = new bool[_parameters.Length];
            var memberValues = new object?[_members.Count];
            var memberSeen = new bool[_members.Count];
            Dictionary<string, object?>? extensionData = null;

            reader.Read(); // first property or end
            while (reader.TokenType != TomlTokenType.EndTable)
            {
                if (reader.TokenType != TomlTokenType.PropertyName)
                {
                    throw reader.CreateException($"Expected {TomlTokenType.PropertyName} token but was {reader.TokenType}.");
                }

                var leadingTrivia = reader.CurrentLeadingTrivia;
                var nameSpan = reader.CurrentSpan;
                var name = reader.PropertyName!;
                reader.Read(); // value
                CapturePropertyMetadata(propertiesMetadata, name, nameSpan, leadingTrivia, reader.CurrentTrailingTrivia, GetDisplayKind(reader));

                if (_indexByName.TryGetValue(name, out var ignoredMemberIndex) && _members[ignoredMemberIndex].IgnoreOnRead)
                {
                    reader.Skip();
                    continue;
                }

                if (_parameterIndexByName is not null && _parameterIndexByName.TryGetValue(name, out var parameterIndex))
                {
                    if (ctorSeen[parameterIndex] && Options.DuplicateKeyHandling == TomlDuplicateKeyHandling.Error)
                    {
                        var duplicateBinding = _parameters[parameterIndex];
                        if (ReflectionObjectTomlTypeInfo.TryReadTableHeaderExtension(reader, ctorArgs, parameterIndex, duplicateBinding.ParameterType, out var updatedArgument))
                        {
                            ctorArgs[parameterIndex] = updatedArgument;
                            if (duplicateBinding.MemberIndex is { } duplicateLinkedMemberIndex && duplicateLinkedMemberIndex >= 0 && duplicateLinkedMemberIndex < _members.Count)
                            {
                                memberSeen[duplicateLinkedMemberIndex] = true;
                                memberValues[duplicateLinkedMemberIndex] = updatedArgument;
                            }

                            continue;
                        }

                        throw reader.CreateException($"Duplicate key '{name}' was encountered.");
                    }

                    ctorSeen[parameterIndex] = true;
                    var binding = _parameters[parameterIndex];

                    var valueStartState = reader.CurrentState;
                    object? value;
                    try
                    {
                        TomlConverter? converter = null;
                        if (binding.MemberIndex is { } linkedIndex && linkedIndex >= 0 && linkedIndex < _members.Count)
                        {
                            converter = _members[linkedIndex].Converter;
                        }

                        if (converter is not null)
                        {
                            value = ReadWithConverter(reader, converter, binding.ParameterType);
                        }
                        else
                        {
                            var typeInfo = reader.ResolveTypeInfo(binding.ParameterType);
                            value = typeInfo.ReadAsObject(reader);
                        }
                    }
                    catch (TomlException ex) when (reader.OperationState.CanAddDiagnostics(ex) && reader.IsStateUnchanged(valueStartState))
                    {
                        reader.OperationState.AddDiagnostics(ex);
                        reader.Skip();
                        continue;
                    }

                    ctorArgs[parameterIndex] = value;

                    if (binding.MemberIndex is { } linkedMemberIndex && linkedMemberIndex >= 0 && linkedMemberIndex < _members.Count)
                    {
                        memberSeen[linkedMemberIndex] = true;
                        memberValues[linkedMemberIndex] = value;
                    }

                    continue;
                }

                if (_indexByName.TryGetValue(name, out var memberIndex))
                {
                    if (memberSeen[memberIndex] && Options.DuplicateKeyHandling == TomlDuplicateKeyHandling.Error)
                    {
                        var duplicateMember = _members[memberIndex];
                        if (ReflectionObjectTomlTypeInfo.TryReadTableHeaderExtension(reader, memberValues, memberIndex, duplicateMember))
                        {
                            continue;
                        }

                        throw reader.CreateException($"Duplicate key '{name}' was encountered.");
                    }

                    memberSeen[memberIndex] = true;
                    var member = _members[memberIndex];

                    if (member.Setter is null && !member.HasSingleOrArray)
                    {
                        reader.Skip();
                        continue;
                    }

                    var valueStartState = reader.CurrentState;
                    object? value;
                    try
                    {
                        if (member.HasSingleOrArray && reader.TokenType != TomlTokenType.StartArray)
                        {
                            value = reader.OperationState.SingleOrArrayCollections.ReadSingleElementAsCollection(reader, member.MemberType);
                        }
                        else if (member.Converter is { } converter)
                        {
                            value = ReadWithConverter(reader, converter, member.MemberType);
                        }
                        else
                        {
                            var typeInfo = reader.ResolveTypeInfo(member.MemberType);
                            value = typeInfo.ReadAsObject(reader);
                        }
                    }
                    catch (TomlException ex) when (reader.OperationState.CanAddDiagnostics(ex) && reader.IsStateUnchanged(valueStartState))
                    {
                        reader.OperationState.AddDiagnostics(ex);
                        reader.Skip();
                        continue;
                    }

                    memberValues[memberIndex] = value;
                    continue;
                }

                reader.Skip();
            }

            var endTableSpan = reader.CurrentSpan;
            reader.Read(); // consume EndTable

            if (reader.OperationState.Diagnostics is { Count: > 0 } diagnostics)
            {
                throw new TomlException(diagnostics);
            }

            for (var i = 0; i < _parameters.Length; i++)
            {
                if (ctorSeen[i])
                {
                    continue;
                }

                var binding = _parameters[i];
                if (binding.HasDefaultValue)
                {
                    ctorArgs[i] = binding.DefaultValue;
                    continue;
                }

                if (endTableSpan is { } span)
                {
                    throw new TomlException(span, $"Missing required constructor parameter '{binding.KeyName}' when deserializing '{Type.FullName}'.");
                }

                if (tableStartSpan is { } startSpan)
                {
                    throw new TomlException(startSpan, $"Missing required constructor parameter '{binding.KeyName}' when deserializing '{Type.FullName}'.");
                }

                throw new TomlException($"Missing required constructor parameter '{binding.KeyName}' when deserializing '{Type.FullName}'.");
            }

            ValidateRequiredMembers(memberSeen, endTableSpan ?? tableStartSpan);

            object instance;
            try
            {
                instance = _constructor!.Invoke(ctorArgs)!;
            }
            catch (Exception ex)
            {
                throw new TomlException($"Failed to create an instance of '{Type.FullName}'.", ex);
            }

            if (_invokeOnDeserializing)
            {
                ((ITomlOnDeserializing)instance).OnTomlDeserializing();
            }

            for (var i = 0; i < _members.Count; i++)
            {
                if (!memberSeen[i])
                {
                    continue;
                }

                var member = _members[i];
                if (member.Setter is null)
                {
                    if (member.HasSingleOrArray)
                    {
                        var existingValue = member.Getter(instance);
                        if (existingValue is null)
                        {
                            throw new TomlException($"Member '{member.Member.Name}' on '{Type.FullName}' uses [TomlSingleOrArray] but the existing collection is null or cannot be populated.");
                        }

                        if (!reader.OperationState.SingleOrArrayCollections.CanPopulate(member.MemberType, existingValue))
                        {
                            throw new TomlException($"Member '{member.Member.Name}' on '{Type.FullName}' uses [TomlSingleOrArray] but '{member.MemberType.FullName}' doesn't support populating the existing collection.");
                        }

                        reader.OperationState.SingleOrArrayCollections.PopulateExistingFromCollection(member.MemberType, existingValue, memberValues[i]!);
                    }

                    continue;
                }

                member.Setter(instance, memberValues[i]);
            }

            if (extensionData is not null && extensionData.Count > 0)
            {
                var dictionary = EnsureExtensionDataDictionary(instance);
                foreach (var pair in extensionData)
                {
                    dictionary[pair.Key] = pair.Value;
                }
            }

            if (propertiesMetadata is not null)
            {
                Options.MetadataStore!.SetProperties(instance, propertiesMetadata);
            }

            if (_invokeOnDeserialized)
            {
                ((ITomlOnDeserialized)instance).OnTomlDeserialized();
            }

            return instance;
        }

        private bool TryReadTableHeaderExtension(TomlReader reader, object instance, MemberModel member)
        {
            if (member.Converter is not null)
            {
                return false;
            }

            var existingValue = member.Getter(instance);
            if (existingValue is null)
            {
                return false;
            }

            var typeInfo = reader.ResolveTypeInfo(member.MemberType);
            if (!TomlTableHeaderExtensionHelper.TryReadIntoExisting(reader, existingValue, typeInfo, out var populatedValue))
            {
                return false;
            }

            if (member.Setter is not null)
            {
                member.Setter(instance, populatedValue);
                return true;
            }

            if (!ReferenceEquals(existingValue, populatedValue))
            {
                throw reader.CreateException(
                    $"Member '{member.Member.Name}' on '{Type.FullName}' cannot be extended by an additional TOML table definition because '{member.MemberType.FullName}' does not support in-place population.");
            }

            return true;
        }

        private static bool TryReadTableHeaderExtension(TomlReader reader, object?[] values, int index, Type valueType, out object? populatedValue)
        {
            populatedValue = values[index];
            if (populatedValue is null)
            {
                return false;
            }

            var typeInfo = reader.ResolveTypeInfo(valueType);
            return TomlTableHeaderExtensionHelper.TryReadIntoExisting(reader, populatedValue, typeInfo, out populatedValue);
        }

        private static bool TryReadTableHeaderExtension(TomlReader reader, object?[] values, int index, MemberModel member)
        {
            if (member.Converter is not null)
            {
                return false;
            }

            if (!ReflectionObjectTomlTypeInfo.TryReadTableHeaderExtension(reader, values, index, member.MemberType, out var populatedValue))
            {
                return false;
            }

            values[index] = populatedValue;
            return true;
        }

        private static void CapturePropertyMetadata(
            TomlPropertiesMetadata? propertiesMetadata,
            string name,
            TomlSourceSpan? span,
            TomlSyntaxTriviaMetadata[]? leadingTrivia,
            TomlSyntaxTriviaMetadata[]? trailingTrivia,
            TomlPropertyDisplayKind displayKind)
        {
            if (propertiesMetadata is null)
            {
                return;
            }

            var hasLeading = leadingTrivia is { Length: > 0 };
            var hasTrailing = trailingTrivia is { Length: > 0 };
            if (span is null && !hasLeading && !hasTrailing && displayKind == TomlPropertyDisplayKind.Default)
            {
                return;
            }

            var propertyMetadata = new TomlPropertyMetadata
            {
                DisplayKind = displayKind,
            };

            if (span is { } locatedSpan)
            {
                propertyMetadata.Span = new SourceSpan(
                    locatedSpan.SourceName,
                    new TextPosition(locatedSpan.Start.Offset, locatedSpan.Start.Line, locatedSpan.Start.Column),
                    new TextPosition(locatedSpan.End.Offset, locatedSpan.End.Line, locatedSpan.End.Column));
            }

            if (hasLeading)
            {
                propertyMetadata.LeadingTrivia = new List<TomlSyntaxTriviaMetadata>(leadingTrivia!);
            }

            if (hasTrailing)
            {
                propertyMetadata.TrailingTrivia = new List<TomlSyntaxTriviaMetadata>(trailingTrivia!);
            }

            propertiesMetadata.SetProperty(name, propertyMetadata);
        }

        private static TomlPropertyDisplayKind GetDisplayKind(TomlReader reader)
        {
            switch (reader.TokenType)
            {
                case TomlTokenType.Integer:
                {
                    var raw = reader.GetRawText();
                    if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return TomlPropertyDisplayKind.IntegerHexadecimal;
                    if (raw.StartsWith("0o", StringComparison.OrdinalIgnoreCase)) return TomlPropertyDisplayKind.IntegerOctal;
                    if (raw.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) return TomlPropertyDisplayKind.IntegerBinary;
                    return TomlPropertyDisplayKind.Default;
                }
                case TomlTokenType.String:
                {
                    return reader.CurrentStringTokenKind switch
                    {
                        TokenKind.StringMulti => TomlPropertyDisplayKind.StringMulti,
                        TokenKind.StringLiteral => TomlPropertyDisplayKind.StringLiteral,
                        TokenKind.StringLiteralMulti => TomlPropertyDisplayKind.StringLiteralMulti,
                        _ => TomlPropertyDisplayKind.Default,
                    };
                }
                case TomlTokenType.DateTime:
                {
                    var value = reader.GetTomlDateTime();
                    return value.Kind switch
                    {
                        TomlDateTimeKind.OffsetDateTimeByZ => TomlPropertyDisplayKind.OffsetDateTimeByZ,
                        TomlDateTimeKind.OffsetDateTimeByNumber => TomlPropertyDisplayKind.OffsetDateTimeByNumber,
                        TomlDateTimeKind.LocalDateTime => TomlPropertyDisplayKind.LocalDateTime,
                        TomlDateTimeKind.LocalDate => TomlPropertyDisplayKind.LocalDate,
                        TomlDateTimeKind.LocalTime => TomlPropertyDisplayKind.LocalTime,
                        _ => TomlPropertyDisplayKind.Default,
                    };
                }
                default:
                    return TomlPropertyDisplayKind.Default;
            }
        }

        private void ValidateRequiredMembers(bool[] seen, TomlSourceSpan? span)
        {
            for (var i = 0; i < _members.Count; i++)
            {
                var member = _members[i];
                if (!member.IsRequired || member.IgnoreOnRead)
                {
                    continue;
                }

                if (!seen[i])
                {
                    if (span is { } locatedSpan)
                    {
                        throw new TomlException(locatedSpan, $"Missing required TOML key '{member.SerializedName}' when deserializing '{Type.FullName}'.");
                    }

                    throw new TomlException($"Missing required TOML key '{member.SerializedName}' when deserializing '{Type.FullName}'.");
                }
            }
        }

        private int FindMemberIndexByClrName(string parameterName)
        {
            for (var i = 0; i < _members.Count; i++)
            {
                if (string.Equals(_members[i].Member.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private readonly record struct ParameterBinding(string KeyName, Type ParameterType, bool HasDefaultValue, object? DefaultValue, int? MemberIndex);
    }
}
