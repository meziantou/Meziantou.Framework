using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        var constructor = SelectConstructor(type, out var constructorError);

        // Like System.Text.Json, a constructor with [SetsRequiredMembers] makes the C# required modifier optional
        var honorRequiredModifier = constructor?.IsDefined(typeof(SetsRequiredMembersAttribute), inherit: false) != true;
        var members = CollectMembers(type, options, mappingOrder, honorRequiredModifier);

        return new ReflectionObjectTomlTypeInfo(type, options, members, constructor, constructorError, dottedKeyHandling);
    }

    // A type without a constructor to use can still be serialized, like in generated code, so the error is only reported
    // when a value is read
    private static ConstructorInfo? SelectConstructor(Type type, out string? error)
    {
        error = null;
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
                error = $"Multiple constructors on type '{type.FullName}' are annotated with [TomlConstructor] or [JsonConstructor].";
                return null;
            }

            annotated = ctor;
        }

        // Like System.Text.Json, a struct without an annotated constructor is created with its parameterless constructor
        if (annotated is not null || type.IsValueType)
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

        error = $"No suitable constructor could be selected for type '{type.FullName}'.";
        return null;
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

    // A member hidden with 'new' by a member of the same name in a derived type is not serialized, like in System.Text.Json
    // and the source generator. Only a member that could be serialized hides: a private 'new' member does not.
    private static T[] RemoveHiddenMembers<T>(T[] members)
        where T : MemberInfo
    {
        return Array.FindAll(members, member => !Array.Exists(members, other =>
            !ReferenceEquals(other, member) &&
            string.Equals(other.Name, member.Name, StringComparison.Ordinal) &&
            other.DeclaringType!.IsSubclassOf(member.DeclaringType!) &&
            CanHideBaseMember(other)));
    }

    private static bool CanHideBaseMember(MemberInfo member)
    {
        return member switch
        {
            PropertyInfo property => property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true || HasIncludeAttribute(property),
            FieldInfo field => field.IsPublic || HasIncludeAttribute(field),
            _ => true,
        };
    }

    // Type.GetProperties and Type.GetFields do not return the private members of the base types, which [TomlInclude] can
    // select, as in generated code
    private static PropertyInfo[] GetInstanceProperties(Type type)
    {
        var properties = new List<PropertyInfo>();
        for (var current = type; current is not null; current = current.BaseType)
        {
            properties.AddRange(current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        }

        return RemoveHiddenMembers(properties.ToArray());
    }

    private static FieldInfo[] GetInstanceFields(Type type)
    {
        var fields = new List<FieldInfo>();
        for (var current = type; current is not null; current = current.BaseType)
        {
            fields.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        }

        return RemoveHiddenMembers(fields.ToArray());
    }

    private static List<MemberModel> CollectMembers(Type type, TomlSerializerOptions options, TomlMappingOrderPolicy mappingOrder, bool honorRequiredModifier)
    {
        var properties = GetInstanceProperties(type);
        var members = new List<MemberModel>(properties.Length);
        var typeObjectCreationHandling = GetObjectCreationHandling(type, options);
        var nullabilityContext = CreateNullabilityContext(options);

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
                // Like generated code and System.Text.Json, an exception thrown by the setter is not wrapped
                setter = (instance, value) => property.SetValue(instance, value, BindingFlags.DoNotWrapExceptions, binder: null, index: null, culture: null);
            }

            // A property without an accessible setter is read-only; an init accessor makes it writable
            var writeIgnoreCondition = setter is null && options.IgnoreReadOnlyProperties ? TomlIgnoreCondition.WhenWriting : ignore.WriteIgnoreCondition;

            members.Add(new MemberModel(
                property,
                name,
                property.PropertyType,
                instance => property.GetValue(instance, BindingFlags.DoNotWrapExceptions, binder: null, index: null, culture: null),
                setter,
                GetOrder(property),
                writeIgnoreCondition,
                ignore.IgnoreOnRead,
                GetDefaultValue(property.PropertyType),
                GetObjectCreationHandling(property, typeObjectCreationHandling),
                HasExplicitObjectCreationHandling(property),
                HasSingleOrArrayAttribute(property),
                IsRequired(property, honorRequiredModifier),
                IsExtensionData(property),
                CreateFormattingMetadata(property, property.PropertyType),
                TryCreateMemberConverter(property, property.PropertyType, options),
                DisallowNullOnSerialize: DisallowNull(property.PropertyType, nullabilityContext?.Create(property).ReadState),
                DisallowNullOnDeserialize: DisallowNull(property.PropertyType, nullabilityContext?.Create(property).WriteState)));
        }

        var fields = GetInstanceFields(type);
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
                IsRequired(field, honorRequiredModifier),
                IsExtensionData(field),
                CreateFormattingMetadata(field, field.FieldType),
                TryCreateMemberConverter(field, field.FieldType, options),
                DisallowNullOnSerialize: DisallowNull(field.FieldType, nullabilityContext?.Create(field).ReadState),
                DisallowNullOnDeserialize: DisallowNull(field.FieldType, nullabilityContext?.Create(field).WriteState)));
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

    private static bool IsRequired(MemberInfo member, bool honorRequiredModifier)
    {
        if (honorRequiredModifier && member.IsDefined(typeof(RequiredMemberAttribute), inherit: false))
        {
            return true;
        }

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
            if ((Nullable.GetUnderlyingType(memberType) ?? memberType).IsEnum && IsJsonStringEnumConverter(jsonConverter.ConverterType))
            {
                return TomlTypeInfoResolverPipeline.ResolveAttributeConverter(TomlStringEnumConverter.Instance, memberType, options);
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

        return TomlTypeInfoResolverPipeline.ResolveAttributeConverter(converter, typeToConvert, options);
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
                // An explicit Never overrides DefaultIgnoreCondition, unlike a member without the attribute
                TomlIgnoreCondition.Never => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: false, WriteIgnoreCondition: TomlIgnoreCondition.Never),
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
                0 => new IgnoreBehavior(IgnoreAlways: false, IgnoreOnRead: false, WriteIgnoreCondition: TomlIgnoreCondition.Never),
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

    // The default value of an enum parameter is stored as its underlying integer, which cannot be passed for a nullable enum
    private static object? GetParameterDefaultValue(ParameterInfo parameter)
    {
        if (!parameter.HasDefaultValue)
        {
            return null;
        }

        var value = parameter.DefaultValue;
        var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
        return value is not null && type.IsEnum && value.GetType() != type ? Enum.ToObject(type, value) : value;
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
        // Declaration order, the same as generated code: the members of the base types first, then in each type the
        // fields then the properties, in declaration order (the metadata token)
        static (int Depth, int Kind, int Token) DeclarationOrder(MemberModel m)
        {
            var depth = 0;
            for (var current = m.Member.DeclaringType?.BaseType; current is not null; current = current.BaseType)
            {
                depth++;
            }

            int token;
            try
            {
                token = m.Member.MetadataToken;
            }
            catch (InvalidOperationException)
            {
                token = 0;
            }

            return (depth, m.Member is FieldInfo ? 0 : 1, token);
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
        TomlConverter? Converter,
        bool DisallowNullOnSerialize = false,
        bool DisallowNullOnDeserialize = false);

    // NullabilityInfoContext is disabled in trimmed applications unless the application opts in
    private static NullabilityInfoContext? CreateNullabilityContext(TomlSerializerOptions options)
    {
        if (!options.RespectNullableAnnotations)
        {
            return null;
        }

        if (AppContext.TryGetSwitch("System.Reflection.NullabilityInfoContext.IsSupported", out var isSupported) && !isSupported)
        {
            return null;
        }

        return new NullabilityInfoContext();
    }

    // Value types are handled by their converters; only reference types annotated as non-nullable are enforced
    private static bool DisallowNull(Type type, NullabilityState? state)
    {
        return !type.IsValueType && state == NullabilityState.NotNull;
    }

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
        private readonly string? _constructorError;
        private readonly ParameterBinding[] _parameters;

        // Like System.Text.Json, a member bound to a constructor parameter gets its value from the constructor only, so the
        // constructor can validate or normalize it
        private readonly bool[] _memberBoundToConstructor = [];
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
        private readonly TomlUnmappedMemberHandling _unmappedMemberHandling;

        public ReflectionObjectTomlTypeInfo(Type type, TomlSerializerOptions options, List<MemberModel> members, ConstructorInfo? constructor, string? constructorError, TomlDottedKeyHandling? dottedKeyHandling)
            : base(type, options)
        {
            _members = members ?? throw new ArgumentNullException(nameof(members));
            _constructor = constructor;
            _constructorError = constructorError;
            _dottedKeyHandling = dottedKeyHandling;
            _unmappedMemberHandling = GetUnmappedMemberHandling(type, options);
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
            _memberBoundToConstructor = new bool[_members.Count];
            var nullabilityContext = CreateNullabilityContext(options);
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
                    _parameters[i] = new ParameterBinding(fallback, parameter.ParameterType, parameter.HasDefaultValue, GetParameterDefaultValue(parameter), MemberIndex: null, fallback, DisallowNull(parameter.ParameterType, nullabilityContext?.Create(parameter).WriteState));
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
                _parameters[i] = new ParameterBinding(keyName, parameter.ParameterType, parameter.HasDefaultValue, GetParameterDefaultValue(parameter), memberIndex >= 0 ? memberIndex : null, parameterName, DisallowNull(parameter.ParameterType, nullabilityContext?.Create(parameter).WriteState));
                if (memberIndex >= 0)
                {
                    _memberBoundToConstructor[memberIndex] = true;
                }
            }
        }

        public override bool WritesTable => true;

        // [TomlUnmappedMemberHandling] takes precedence over [JsonUnmappedMemberHandling], then over the options
        private static TomlUnmappedMemberHandling GetUnmappedMemberHandling(Type type, TomlSerializerOptions options)
        {
            var tomlAttribute = type.GetCustomAttribute<TomlUnmappedMemberHandlingAttribute>(inherit: false);
            if (tomlAttribute is not null)
            {
                return tomlAttribute.Handling;
            }

            var jsonAttribute = type.GetCustomAttribute<JsonUnmappedMemberHandlingAttribute>(inherit: false);
            if (jsonAttribute is not null)
            {
                return jsonAttribute.UnmappedMemberHandling == JsonUnmappedMemberHandling.Disallow ? TomlUnmappedMemberHandling.Disallow : TomlUnmappedMemberHandling.Skip;
            }

            return options.UnmappedMemberHandling;
        }

        private void SkipUnmappedMember(TomlReader reader, string name)
        {
            if (_unmappedMemberHandling == TomlUnmappedMemberHandling.Disallow)
            {
                throw reader.CreateException($"The TOML key '{name}' could not be mapped to '{Type.FullName}'.");
            }

            reader.Skip();
        }

        private TomlException CreateNullForNonNullableMemberException(TomlReader reader, string name)
        {
            return reader.CreateException($"The TOML key '{name}' cannot be null because '{Type.FullName}' declares it as non-nullable.");
        }

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

                // The check applies before DefaultIgnoreCondition, but a member whose own ignore condition skips null values is
                // skipped
                var memberValue = member.Getter(value);
                if (memberValue is null && member.DisallowNullOnSerialize && member.WriteIgnoreCondition is not (TomlIgnoreCondition.WhenWritingNull or TomlIgnoreCondition.WhenWritingDefault))
                {
                    throw new TomlException($"The member '{member.Member.Name}' on '{Type.FullName}' cannot be serialized as null because it is declared as non-nullable.");
                }

                var ignoreCondition = member.WriteIgnoreCondition ?? Options.DefaultIgnoreCondition;
                if (ShouldIgnoreValue(memberValue, ignoreCondition, member.DefaultValue))
                {
                    continue;
                }

                if (memberValue is null)
                {
                    throw new TomlException($"The member '{member.Member.Name}' on '{Type.FullName}' is null, which TOML cannot represent. Use {nameof(TomlIgnoreCondition)}.{nameof(TomlIgnoreCondition.WhenWritingNull)} to skip it.");
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
                var extensionDictionary = extensionMember.Getter(value);
                if (extensionDictionary is not null)
                {
                    foreach (var entry in EnumerateExtensionData(extensionDictionary))
                    {
                        var key = entry.Key;

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

            if (_constructorError is not null)
            {
                throw reader.CreateException(_constructorError);
            }

            var tableStartSpan = reader.CurrentSpan;

            if (_parameters.Length > 0)
            {
                return ReadWithConstructor(reader, tableStartSpan);
            }

            var instance = CreateInstance(tableStartSpan);
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

            string? inlineValueName = null;
            reader.Read(); // first property or end
            while (true)
            {
                // The trailing comment of an inline array or table follows its closing token, the token before this one
                if (inlineValueName is not null)
                {
                    TomlPropertyMetadataCapture.AppendTrailingTrivia(propertiesMetadata, inlineValueName, reader.PreviousTrailingTrivia);
                    inlineValueName = null;
                }

                if (reader.TokenType == TomlTokenType.EndTable)
                {
                    break;
                }

                if (reader.TokenType != TomlTokenType.PropertyName)
                {
                    throw reader.CreateException($"Expected {TomlTokenType.PropertyName} token but was {reader.TokenType}.");
                }

                var leadingTrivia = reader.CurrentLeadingTrivia;
                var nameSpan = reader.CurrentSpan;
                var name = reader.PropertyName!;
                reader.Read(); // value
                TomlPropertyMetadataCapture.Capture(propertiesMetadata, name, nameSpan, leadingTrivia, reader.CurrentTrailingTrivia, TomlPropertyMetadataCapture.GetDisplayKind(reader));
                if (propertiesMetadata is not null && reader.IsInlineContainer)
                {
                    inlineValueName = name;
                }

                if (_indexByName.TryGetValue(name, out var memberIndex))
                {
                    var member = _members[memberIndex];
                    if (member.IgnoreOnRead)
                    {
                        reader.Skip();
                        continue;
                    }

                    // seen also tracks the required members, so it can exist when duplicate keys are allowed
                    if (seen is not null && seen[memberIndex] && Options.DuplicateKeyHandling == TomlDuplicateKeyHandling.Error)
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

                    if (memberValue is null && member.DisallowNullOnDeserialize)
                    {
                        throw CreateNullForNonNullableMemberException(reader, name);
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
                    SetExtensionData(extensionDictionary, name, extensionValue);
                    continue;
                }

                SkipUnmappedMember(reader, name);
            }

            var endTableSpan = reader.CurrentSpan;
            reader.Read(); // consume EndTable

            if (seen is not null)
            {
                ValidateRequiredMembers(seen, tableStartSpan ?? endTableSpan);
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

        private object CreateInstance(TomlSourceSpan? span)
        {
            try
            {
                if (_constructor is not null && _constructor.GetParameters().Length == 0)
                {
                    return _constructor.Invoke(BindingFlags.DoNotWrapExceptions, binder: null, Array.Empty<object>(), culture: null)!;
                }

                return Activator.CreateInstance(Type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DoNotWrapExceptions, binder: null, args: null, culture: null)!;
            }
            catch (Exception ex)
            {
                throw CreateInstanceException(span, ex);
            }
        }

        private TomlException CreateInstanceException(TomlSourceSpan? span, Exception innerException)
        {
            var message = $"Failed to create an instance of '{Type.FullName}'.";
            return span is { } value ? new TomlException(value, message, innerException) : new TomlException(message, innerException);
        }

        // An extension data member is a non-generic IDictionary (Dictionary<string, T>) or an IDictionary<string, object>
        // (TomlTable)
        private static bool IsExtensionDataDictionary(object? value) => value is IDictionary or IDictionary<string, object>;

        private static void SetExtensionData(object dictionary, string key, object? value)
        {
            if (dictionary is IDictionary nonGenericDictionary)
            {
                nonGenericDictionary[key] = value;
            }
            else
            {
                ((IDictionary<string, object>)dictionary)[key] = value!;
            }
        }

        private static IEnumerable<KeyValuePair<string, object?>> EnumerateExtensionData(object dictionary)
        {
            if (dictionary is IDictionary nonGenericDictionary)
            {
                foreach (DictionaryEntry entry in nonGenericDictionary)
                {
                    if (entry.Key is not string key)
                    {
                        throw new TomlException($"Extension data keys must be strings but encountered '{entry.Key?.GetType().FullName}'.");
                    }

                    yield return new KeyValuePair<string, object?>(key, entry.Value);
                }
            }
            else if (dictionary is IDictionary<string, object> genericDictionary)
            {
                foreach (var entry in genericDictionary)
                {
                    yield return new KeyValuePair<string, object?>(entry.Key, entry.Value);
                }
            }
        }

        private object EnsureExtensionDataDictionary(object instance)
        {
            if (_extensionDataIndex == -1)
            {
                throw new InvalidOperationException("No extension data member is configured.");
            }

            var extensionMember = _members[_extensionDataIndex];
            var existing = extensionMember.Getter(instance);
            if (IsExtensionDataDictionary(existing))
            {
                return existing!;
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

        private object CreateExtensionDataDictionary(Type memberType)
        {
            if (!memberType.IsInterface && !memberType.IsAbstract)
            {
                try
                {
                    var created = Activator.CreateInstance(memberType);
                    if (IsExtensionDataDictionary(created))
                    {
                        return created!;
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

            string? inlineValueName = null;
            reader.Read(); // first property or end
            while (true)
            {
                // The trailing comment of an inline array or table follows its closing token, the token before this one
                if (inlineValueName is not null)
                {
                    TomlPropertyMetadataCapture.AppendTrailingTrivia(propertiesMetadata, inlineValueName, reader.PreviousTrailingTrivia);
                    inlineValueName = null;
                }

                if (reader.TokenType == TomlTokenType.EndTable)
                {
                    break;
                }

                if (reader.TokenType != TomlTokenType.PropertyName)
                {
                    throw reader.CreateException($"Expected {TomlTokenType.PropertyName} token but was {reader.TokenType}.");
                }

                var leadingTrivia = reader.CurrentLeadingTrivia;
                var nameSpan = reader.CurrentSpan;
                var name = reader.PropertyName!;
                reader.Read(); // value
                TomlPropertyMetadataCapture.Capture(propertiesMetadata, name, nameSpan, leadingTrivia, reader.CurrentTrailingTrivia, TomlPropertyMetadataCapture.GetDisplayKind(reader));
                if (propertiesMetadata is not null && reader.IsInlineContainer)
                {
                    inlineValueName = name;
                }

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

                    if (value is null && binding.DisallowNull)
                    {
                        throw reader.CreateException($"The constructor parameter '{binding.ParameterName ?? binding.KeyName}' on '{Type.FullName}' cannot be null because it is declared as non-nullable.");
                    }

                    ctorArgs[parameterIndex] = value;

                    if (binding.MemberIndex is { } linkedMemberIndex && linkedMemberIndex >= 0 && linkedMemberIndex < _members.Count)
                    {
                        memberSeen[linkedMemberIndex] = true;
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

                    if (value is null && member.DisallowNullOnDeserialize)
                    {
                        throw CreateNullForNonNullableMemberException(reader, name);
                    }

                    memberValues[memberIndex] = value;
                    continue;
                }

                // Extension data is collected here and added to the member once the instance exists
                if (_extensionDataIndex == -1)
                {
                    SkipUnmappedMember(reader, name);
                }
                else
                {
                    (extensionData ??= new Dictionary<string, object?>(StringComparer.Ordinal))[name] = ReadExtensionValue(reader);
                }
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

                if (!Options.RespectRequiredConstructorParameters)
                {
                    ctorArgs[i] = binding.ParameterType.IsValueType ? Activator.CreateInstance(binding.ParameterType) : null;
                    continue;
                }

                // The end of the table has the span of whatever follows it, possibly another table
                if ((tableStartSpan ?? endTableSpan) is { } span)
                {
                    throw new TomlException(span, $"Missing required constructor parameter '{binding.KeyName}' when deserializing '{Type.FullName}'.");
                }

                throw new TomlException($"Missing required constructor parameter '{binding.KeyName}' when deserializing '{Type.FullName}'.");
            }

            ValidateRequiredMembers(memberSeen, tableStartSpan ?? endTableSpan);

            object instance;
            try
            {
                instance = _constructor!.Invoke(BindingFlags.DoNotWrapExceptions, binder: null, ctorArgs, culture: null)!;
            }
            catch (Exception ex)
            {
                throw CreateInstanceException(tableStartSpan ?? endTableSpan, ex);
            }

            if (_invokeOnDeserializing)
            {
                ((ITomlOnDeserializing)instance).OnTomlDeserializing();
            }

            for (var i = 0; i < _members.Count; i++)
            {
                if (!memberSeen[i] || _memberBoundToConstructor[i])
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
                    SetExtensionData(dictionary, pair.Key, pair.Value);
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

        private readonly record struct ParameterBinding(string KeyName, Type ParameterType, bool HasDefaultValue, object? DefaultValue, int? MemberIndex, string? ParameterName = null, bool DisallowNull = false);
    }
}
