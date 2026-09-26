using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Tomlyn.Helpers;
using Tomlyn.Model;
using Tomlyn.Serialization.Converters;

namespace Tomlyn.Serialization.Internal;

internal static class TomlBuiltInTypeInfoResolver
{
    private const string ReflectionBasedSerializationMessage =
        "Reflection-based TOML serialization is not compatible with trimming/NativeAOT. " +
        "Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.";

    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static TomlTypeInfo? GetTypeInfo(Type type, TomlSerializerOptions options)
    {
        ArgumentGuard.ThrowIfNull(type, nameof(type));
        ArgumentGuard.ThrowIfNull(options, nameof(options));

        if (type == typeof(char)) return new BuiltInTomlTypeInfo<char>(options, TomlCharConverter.Instance);
        if (type == typeof(string)) return new BuiltInTomlTypeInfo<string>(options, TomlStringConverter.Instance);
        if (type == typeof(bool)) return new BuiltInTomlTypeInfo<bool>(options, TomlBooleanConverter.Instance);
        if (type == typeof(sbyte)) return new BuiltInTomlTypeInfo<sbyte>(options, TomlSByteConverter.Instance);
        if (type == typeof(byte)) return new BuiltInTomlTypeInfo<byte>(options, TomlByteConverter.Instance);
        if (type == typeof(short)) return new BuiltInTomlTypeInfo<short>(options, TomlInt16Converter.Instance);
        if (type == typeof(ushort)) return new BuiltInTomlTypeInfo<ushort>(options, TomlUInt16Converter.Instance);
        if (type == typeof(int)) return new BuiltInTomlTypeInfo<int>(options, TomlInt32Converter.Instance);
        if (type == typeof(uint)) return new BuiltInTomlTypeInfo<uint>(options, TomlUInt32Converter.Instance);
        if (type == typeof(long)) return new BuiltInTomlTypeInfo<long>(options, TomlInt64Converter.Instance);
        if (type == typeof(ulong)) return new BuiltInTomlTypeInfo<ulong>(options, TomlUInt64Converter.Instance);
        if (type == typeof(nint)) return new BuiltInTomlTypeInfo<nint>(options, TomlNIntConverter.Instance);
        if (type == typeof(nuint)) return new BuiltInTomlTypeInfo<nuint>(options, TomlNUIntConverter.Instance);
        if (type == typeof(float)) return new BuiltInTomlTypeInfo<float>(options, TomlSingleConverter.Instance);
        if (type == typeof(double)) return new BuiltInTomlTypeInfo<double>(options, TomlDoubleConverter.Instance);
        if (type == typeof(decimal)) return new BuiltInTomlTypeInfo<decimal>(options, TomlDecimalConverter.Instance);

        if (type == typeof(Half)) return new BuiltInTomlTypeInfo<Half>(options, TomlHalfConverter.Instance);

        if (type == typeof(Int128)) return new BuiltInTomlTypeInfo<Int128>(options, TomlInt128Converter.Instance);
        if (type == typeof(UInt128)) return new BuiltInTomlTypeInfo<UInt128>(options, TomlUInt128Converter.Instance);

        if (type == typeof(DateTime)) return new BuiltInTomlTypeInfo<DateTime>(options, TomlDateTimeConverter.Instance);
        if (type == typeof(DateTimeOffset)) return new BuiltInTomlTypeInfo<DateTimeOffset>(options, TomlDateTimeOffsetConverter.Instance);
        if (type == typeof(TomlDateTime)) return new BuiltInTomlTypeInfo<TomlDateTime>(options, TomlTomlDateTimeConverter.Instance);

        if (type == typeof(DateOnly)) return new BuiltInTomlTypeInfo<DateOnly>(options, TomlDateOnlyConverter.Instance);
        if (type == typeof(TimeOnly)) return new BuiltInTomlTypeInfo<TimeOnly>(options, TomlTimeOnlyConverter.Instance);

        if (type == typeof(Guid)) return new BuiltInTomlTypeInfo<Guid>(options, TomlGuidConverter.Instance);
        if (type == typeof(TimeSpan)) return new BuiltInTomlTypeInfo<TimeSpan>(options, TomlTimeSpanConverter.Instance);
        if (type == typeof(Uri)) return new BuiltInTomlTypeInfo<Uri>(options, TomlUriConverter.Instance);
        if (type == typeof(Version)) return new BuiltInTomlTypeInfo<Version>(options, TomlVersionConverter.Instance);

        if (type == typeof(TomlObject)) return new BuiltInTomlTypeInfo<TomlObject>(options, TomlTomlObjectConverter.Instance);
        if (type == typeof(TomlTable)) return new BuiltInTomlTypeInfo<TomlTable>(options, TomlTomlTableConverter.Instance, writesTable: true);
        if (type == typeof(TomlArray)) return new BuiltInTomlTypeInfo<TomlArray>(options, TomlTomlArrayConverter.Instance);
        if (type == typeof(TomlTableArray)) return new BuiltInTomlTypeInfo<TomlTableArray>(options, TomlTomlTableArrayConverter.Instance);

        if (type == typeof(object)) return new BuiltInUntypedTomlTypeInfo(type, options, TomlUntypedObjectConverter.Instance);
        if (type.IsEnum) return new BuiltInUntypedTomlTypeInfo(type, options, TomlEnumConverter.Instance);

        var collection = TryCreateCollectionTypeInfo(type, options);
        if (collection is not null)
        {
            return collection;
        }

        return null;
    }

    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    private static TomlTypeInfo? TryCreateCollectionTypeInfo(Type type, TomlSerializerOptions options)
    {
        if (type.IsArray && type.GetArrayRank() == 1)
        {
            var elementType = type.GetElementType();
            if (elementType is null)
            {
                return null;
            }

            var typeInfoType = typeof(TomlArrayTypeInfo<>).MakeGenericType(elementType);
            return (TomlTypeInfo?)Activator.CreateInstance(typeInfoType, options);
        }

        if (type.IsGenericType)
        {
            var genericDefinition = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();

            if (genericDefinition == typeof(List<>))
            {
                var typeInfoType = typeof(TomlListTypeInfo<>).MakeGenericType(args[0]);
                return (TomlTypeInfo?)Activator.CreateInstance(typeInfoType, options);
            }

            if (genericDefinition == typeof(HashSet<>))
            {
                var typeInfoType = typeof(TomlHashSetTypeInfo<>).MakeGenericType(args[0]);
                return (TomlTypeInfo?)Activator.CreateInstance(typeInfoType, options);
            }

            if (genericDefinition == typeof(ISet<>))
            {
                var typeInfoType = typeof(TomlHashSetBackedEnumerableTypeInfo<,>).MakeGenericType(type, args[0]);
                return (TomlTypeInfo?)Activator.CreateInstance(typeInfoType, options);
            }

            if (genericDefinition == typeof(ImmutableArray<>))
            {
                var typeInfoType = typeof(TomlImmutableArrayTypeInfo<>).MakeGenericType(args[0]);
                return (TomlTypeInfo?)Activator.CreateInstance(typeInfoType, options);
            }

            if (genericDefinition == typeof(ImmutableList<>))
            {
                var typeInfoType = typeof(TomlImmutableListTypeInfo<>).MakeGenericType(args[0]);
                return (TomlTypeInfo?)Activator.CreateInstance(typeInfoType, options);
            }

            if (genericDefinition == typeof(ImmutableHashSet<>))
            {
                var typeInfoType = typeof(TomlImmutableHashSetTypeInfo<>).MakeGenericType(args[0]);
                return (TomlTypeInfo?)Activator.CreateInstance(typeInfoType, options);
            }

            if (genericDefinition == typeof(Dictionary<,>))
            {
                if (args[0] != typeof(string))
                {
                    return null;
                }

                var typeInfoType = typeof(TomlDictionaryTypeInfo<,>).MakeGenericType(type, args[1]);
                return (TomlTypeInfo?)Activator.CreateInstance(typeInfoType, options);
            }

            if (genericDefinition == typeof(IDictionary<,>) ||
                genericDefinition == typeof(IReadOnlyDictionary<,>))
            {
                if (args[0] != typeof(string))
                {
                    return null;
                }

                var typeInfoType = typeof(TomlDictionaryTypeInfo<,>).MakeGenericType(type, args[1]);
                return (TomlTypeInfo?)Activator.CreateInstance(typeInfoType, options);
            }

            if (genericDefinition == typeof(IEnumerable<>) ||
                genericDefinition == typeof(ICollection<>) ||
                genericDefinition == typeof(IReadOnlyCollection<>) ||
                genericDefinition == typeof(IList<>) ||
                genericDefinition == typeof(IReadOnlyList<>))
            {
                var typeInfoType = typeof(TomlListBackedEnumerableTypeInfo<,>).MakeGenericType(type, args[0]);
                return (TomlTypeInfo?)Activator.CreateInstance(typeInfoType, options);
            }
        }

        if (TryGetMutableCollectionElementType(type, out var collectionElementType))
        {
            var typeInfoType = typeof(TomlMutableCollectionTypeInfo<,>).MakeGenericType(type, collectionElementType);
            return (TomlTypeInfo?)Activator.CreateInstance(typeInfoType, options);
        }

        return null;
    }

    private static bool TryGetMutableCollectionElementType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type,
        [NotNullWhen(true)] out Type? elementType)
    {
        elementType = null;

        if (type.IsInterface || type.IsAbstract || typeof(IDictionary).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
        {
            return false;
        }

        Type? matchedElementType = null;
        foreach (var interfaceType in type.GetInterfaces())
        {
            if (interfaceType.IsGenericType &&
                (interfaceType.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                 interfaceType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
            {
                return false;
            }

            if (!interfaceType.IsGenericType || interfaceType.GetGenericTypeDefinition() != typeof(ICollection<>))
            {
                continue;
            }

            var currentElementType = interfaceType.GetGenericArguments()[0];
            if (matchedElementType is not null && matchedElementType != currentElementType)
            {
                return false;
            }

            matchedElementType = currentElementType;
        }

        elementType = matchedElementType;
        return elementType is not null;
    }

    private sealed class BuiltInUntypedTomlTypeInfo : TomlTypeInfo
    {
        private readonly TomlConverter _converter;

        public BuiltInUntypedTomlTypeInfo(Type type, TomlSerializerOptions options, TomlConverter converter)
            : base(type, options)
        {
            _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        }

        public override void Write(TomlWriter writer, object? value)
        {
            ArgumentGuard.ThrowIfNull(writer, nameof(writer));
            _converter.Write(writer, value);
        }

        public override object? ReadAsObject(TomlReader reader)
        {
            ArgumentGuard.ThrowIfNull(reader, nameof(reader));
            return _converter.Read(reader, Type);
        }
    }

    private sealed class BuiltInTomlTypeInfo<T> : TomlTypeInfo<T>
    {
        private readonly TomlConverter<T> _converter;
        private readonly bool _writesTable;

        public BuiltInTomlTypeInfo(TomlSerializerOptions options, TomlConverter<T> converter, bool writesTable = false)
            : base(options)
        {
            _converter = converter ?? throw new ArgumentNullException(nameof(converter));
            _writesTable = writesTable;
        }

        public override bool WritesTable => _writesTable;

        public override void Write(TomlWriter writer, T value)
        {
            ArgumentGuard.ThrowIfNull(writer, nameof(writer));
            _converter.Write(writer, value);
        }

        public override T? Read(TomlReader reader)
        {
            ArgumentGuard.ThrowIfNull(reader, nameof(reader));
            return _converter.Read(reader);
        }
    }
}
