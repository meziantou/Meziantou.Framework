// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization.Internal;

internal sealed class TomlSingleOrArrayCollectionHelper
{
    private readonly Dictionary<Type, CollectionHandler?> _handlers = new();

    public static bool CanPopulateCollection<T>(object existingValue)
    {
        ArgumentGuard.ThrowIfNull(existingValue, nameof(existingValue));
        return existingValue is ICollection<T>;
    }

    public static object AddSingleElementToCollection<T>(object existingValue, T element)
    {
        ArgumentGuard.ThrowIfNull(existingValue, nameof(existingValue));

        var collection = existingValue as ICollection<T>
            ?? throw new InvalidOperationException($"Existing collection '{existingValue.GetType().FullName}' does not support population.");
        collection.Add(element);
        return existingValue;
    }

    public static object AddCollectionToExisting<T>(object existingValue, IEnumerable<T> incomingCollection)
    {
        ArgumentGuard.ThrowIfNull(existingValue, nameof(existingValue));
        ArgumentGuard.ThrowIfNull(incomingCollection, nameof(incomingCollection));

        var collection = existingValue as ICollection<T>
            ?? throw new InvalidOperationException($"Existing collection '{existingValue.GetType().FullName}' does not support population.");
        foreach (var item in incomingCollection)
        {
            collection.Add(item);
        }

        return existingValue;
    }

    public static T[] CreateSingleElementArray<T>(T element) => new[] { element };

    public static List<T> CreateSingleElementList<T>(T element) => new() { element };

    public static TCollection CreateSingleElementCollection<TCollection, TElement>(TElement element)
        where TCollection : ICollection<TElement>, new()
        => new() { element };

    public static HashSet<T> CreateSingleElementHashSet<T>(T element) => new() { element };

    public static ImmutableArray<T> CreateSingleElementImmutableArray<T>(T element) => ImmutableArray.Create(element);

    public static ImmutableList<T> CreateSingleElementImmutableList<T>(T element) => ImmutableList.Create(element);

    public static ImmutableHashSet<T> CreateSingleElementImmutableHashSet<T>(T element) => ImmutableHashSet.Create(element);

    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    public bool IsSupported(Type collectionType)
    {
        return GetHandler(collectionType) is not null;
    }

    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    public bool CanPopulate(Type collectionType, object existingValue)
    {
        ArgumentGuard.ThrowIfNull(collectionType, nameof(collectionType));
        ArgumentGuard.ThrowIfNull(existingValue, nameof(existingValue));

        return GetHandler(collectionType)?.CanPopulate(existingValue) == true;
    }

    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    public object ReadSingleElementAsCollection(TomlReader reader, Type collectionType)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(collectionType, nameof(collectionType));

        var handler = GetRequiredHandler(collectionType);
        var element = ReadElement(reader, handler.ElementType);
        return handler.CreateSingleElementCollection(element);
    }

    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    public object ReadSingleElementIntoExisting(TomlReader reader, Type collectionType, object existingValue)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(collectionType, nameof(collectionType));
        ArgumentGuard.ThrowIfNull(existingValue, nameof(existingValue));

        var handler = GetRequiredHandler(collectionType);
        var element = ReadElement(reader, handler.ElementType);
        return handler.AddSingleElement(existingValue, element);
    }

    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    public object PopulateExistingFromCollection(Type collectionType, object existingValue, object incomingCollection)
    {
        ArgumentGuard.ThrowIfNull(collectionType, nameof(collectionType));
        ArgumentGuard.ThrowIfNull(existingValue, nameof(existingValue));
        ArgumentGuard.ThrowIfNull(incomingCollection, nameof(incomingCollection));

        return GetRequiredHandler(collectionType).AddCollection(existingValue, incomingCollection);
    }

    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    private static object? ReadElement(TomlReader reader, Type elementType)
    {
        var typeInfo = reader.ResolveTypeInfo(elementType);
        return typeInfo.ReadAsObject(reader);
    }

    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    private CollectionHandler GetRequiredHandler(Type collectionType)
    {
        return GetHandler(collectionType)
            ?? throw new TomlException($"Type '{collectionType.FullName}' does not support [TomlSingleOrArray].");
    }

    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    private CollectionHandler? GetHandler(Type collectionType)
    {
        if (_handlers.TryGetValue(collectionType, out var handler))
        {
            return handler;
        }

        handler = CreateHandler(collectionType);
        _handlers[collectionType] = handler;
        return handler;
    }

    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    private static CollectionHandler? CreateHandler(Type type)
    {
        if (type.IsArray)
        {
            return (CollectionHandler?)Activator.CreateInstance(typeof(ArrayHandler<>).MakeGenericType(type.GetElementType()!));
        }

        if (type.IsGenericType)
        {
            var genericDefinition = type.GetGenericTypeDefinition();
            var elementType = type.GetGenericArguments()[0];

            if (genericDefinition == typeof(List<>) ||
                genericDefinition == typeof(IEnumerable<>) ||
                genericDefinition == typeof(ICollection<>) ||
                genericDefinition == typeof(IReadOnlyCollection<>) ||
                genericDefinition == typeof(IList<>) ||
                genericDefinition == typeof(IReadOnlyList<>))
            {
                return (CollectionHandler?)Activator.CreateInstance(typeof(ListHandler<>).MakeGenericType(elementType));
            }

            if (genericDefinition == typeof(HashSet<>) || genericDefinition == typeof(ISet<>))
            {
                return (CollectionHandler?)Activator.CreateInstance(typeof(HashSetHandler<>).MakeGenericType(elementType));
            }

            if (genericDefinition == typeof(ImmutableArray<>))
            {
                return (CollectionHandler?)Activator.CreateInstance(typeof(ImmutableArrayHandler<>).MakeGenericType(elementType));
            }

            if (genericDefinition == typeof(ImmutableList<>))
            {
                return (CollectionHandler?)Activator.CreateInstance(typeof(ImmutableListHandler<>).MakeGenericType(elementType));
            }

            if (genericDefinition == typeof(ImmutableHashSet<>))
            {
                return (CollectionHandler?)Activator.CreateInstance(typeof(ImmutableHashSetHandler<>).MakeGenericType(elementType));
            }
        }

        if (TryGetMutableCollectionElementType(type, out var collectionElementType))
        {
            return (CollectionHandler?)Activator.CreateInstance(typeof(MutableCollectionHandler<,>).MakeGenericType(type, collectionElementType));
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

    private abstract class CollectionHandler
    {
        public abstract Type ElementType { get; }

        public abstract object CreateSingleElementCollection(object? element);

        public abstract bool CanPopulate(object existingValue);

        public abstract object AddSingleElement(object existingValue, object? element);

        public abstract object AddCollection(object existingValue, object incomingCollection);
    }

    private sealed class ArrayHandler<T> : CollectionHandler
    {
        public override Type ElementType => typeof(T);

        public override object CreateSingleElementCollection(object? element)
        {
            return new[] { (T)element! };
        }

        public override bool CanPopulate(object existingValue) => false;

        public override object AddSingleElement(object existingValue, object? element)
        {
            throw new InvalidOperationException("Arrays cannot be populated in place.");
        }

        public override object AddCollection(object existingValue, object incomingCollection)
        {
            throw new InvalidOperationException("Arrays cannot be populated in place.");
        }
    }

    private sealed class ListHandler<T> : CollectionHandler
    {
        public override Type ElementType => typeof(T);

        public override object CreateSingleElementCollection(object? element)
        {
            return new List<T> { (T)element! };
        }

        public override bool CanPopulate(object existingValue) => existingValue is ICollection<T>;

        public override object AddSingleElement(object existingValue, object? element)
        {
            var collection = existingValue as ICollection<T>
                ?? throw new InvalidOperationException($"Existing collection '{existingValue.GetType().FullName}' does not support population.");
            collection.Add((T)element!);
            return existingValue;
        }

        public override object AddCollection(object existingValue, object incomingCollection)
        {
            var collection = existingValue as ICollection<T>
                ?? throw new InvalidOperationException($"Existing collection '{existingValue.GetType().FullName}' does not support population.");
            foreach (var item in (IEnumerable<T>)incomingCollection)
            {
                collection.Add(item);
            }

            return existingValue;
        }
    }

    private sealed class MutableCollectionHandler<TCollection, T> : CollectionHandler
        where TCollection : ICollection<T>, new()
    {
        public override Type ElementType => typeof(T);

        public override object CreateSingleElementCollection(object? element)
        {
            return new TCollection { (T)element! };
        }

        public override bool CanPopulate(object existingValue) => existingValue is ICollection<T>;

        public override object AddSingleElement(object existingValue, object? element)
        {
            var collection = existingValue as ICollection<T>
                ?? throw new InvalidOperationException($"Existing collection '{existingValue.GetType().FullName}' does not support population.");
            collection.Add((T)element!);
            return existingValue;
        }

        public override object AddCollection(object existingValue, object incomingCollection)
        {
            var collection = existingValue as ICollection<T>
                ?? throw new InvalidOperationException($"Existing collection '{existingValue.GetType().FullName}' does not support population.");
            foreach (var item in (IEnumerable<T>)incomingCollection)
            {
                collection.Add(item);
            }

            return existingValue;
        }
    }

    private sealed class HashSetHandler<T> : CollectionHandler
    {
        public override Type ElementType => typeof(T);

        public override object CreateSingleElementCollection(object? element)
        {
            return new HashSet<T> { (T)element! };
        }

        public override bool CanPopulate(object existingValue) => existingValue is ICollection<T>;

        public override object AddSingleElement(object existingValue, object? element)
        {
            var collection = existingValue as ICollection<T>
                ?? throw new InvalidOperationException($"Existing collection '{existingValue.GetType().FullName}' does not support population.");
            collection.Add((T)element!);
            return existingValue;
        }

        public override object AddCollection(object existingValue, object incomingCollection)
        {
            var collection = existingValue as ICollection<T>
                ?? throw new InvalidOperationException($"Existing collection '{existingValue.GetType().FullName}' does not support population.");
            foreach (var item in (IEnumerable<T>)incomingCollection)
            {
                collection.Add(item);
            }

            return existingValue;
        }
    }

    private sealed class ImmutableArrayHandler<T> : CollectionHandler
    {
        public override Type ElementType => typeof(T);

        public override object CreateSingleElementCollection(object? element)
        {
            return ImmutableArray.Create((T)element!);
        }

        public override bool CanPopulate(object existingValue) => false;

        public override object AddSingleElement(object existingValue, object? element)
        {
            throw new InvalidOperationException("Immutable arrays cannot be populated in place.");
        }

        public override object AddCollection(object existingValue, object incomingCollection)
        {
            throw new InvalidOperationException("Immutable arrays cannot be populated in place.");
        }
    }

    private sealed class ImmutableListHandler<T> : CollectionHandler
    {
        public override Type ElementType => typeof(T);

        public override object CreateSingleElementCollection(object? element)
        {
            return ImmutableList.Create((T)element!);
        }

        public override bool CanPopulate(object existingValue) => false;

        public override object AddSingleElement(object existingValue, object? element)
        {
            throw new InvalidOperationException("Immutable lists cannot be populated in place.");
        }

        public override object AddCollection(object existingValue, object incomingCollection)
        {
            throw new InvalidOperationException("Immutable lists cannot be populated in place.");
        }
    }

    private sealed class ImmutableHashSetHandler<T> : CollectionHandler
    {
        public override Type ElementType => typeof(T);

        public override object CreateSingleElementCollection(object? element)
        {
            return ImmutableHashSet.Create((T)element!);
        }

        public override bool CanPopulate(object existingValue) => false;

        public override object AddSingleElement(object existingValue, object? element)
        {
            throw new InvalidOperationException("Immutable hash sets cannot be populated in place.");
        }

        public override object AddCollection(object existingValue, object incomingCollection)
        {
            throw new InvalidOperationException("Immutable hash sets cannot be populated in place.");
        }
    }
}
