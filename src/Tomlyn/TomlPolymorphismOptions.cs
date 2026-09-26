// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn.Helpers;
using Tomlyn.Serialization;

namespace Tomlyn;

/// <summary>
/// Configures discriminator-based polymorphism.
/// </summary>
public sealed record TomlPolymorphismOptions
{
    private static readonly IReadOnlyDictionary<Type, IReadOnlyList<TomlDerivedType>> EmptyDerivedTypeMappings =
        new ReadOnlyDictionary<Type, IReadOnlyList<TomlDerivedType>>(new Dictionary<Type, IReadOnlyList<TomlDerivedType>>());

    private TomlUnknownDerivedTypeHandling _unknownDerivedTypeHandling = TomlUnknownDerivedTypeHandling.Fail;
    private IReadOnlyDictionary<Type, IReadOnlyList<TomlDerivedType>> _derivedTypeMappings = EmptyDerivedTypeMappings;

    /// <summary>
    /// Gets the property name used for discriminator-based polymorphism.
    /// </summary>
    public string TypeDiscriminatorPropertyName { get; init; } = "$type";

    /// <summary>
    /// Gets behavior when an unknown discriminator is encountered.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when set to <see cref="TomlUnknownDerivedTypeHandling.Unspecified"/>.</exception>
    public TomlUnknownDerivedTypeHandling UnknownDerivedTypeHandling
    {
        get => _unknownDerivedTypeHandling;
        init
        {
            if (value == TomlUnknownDerivedTypeHandling.Unspecified)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(TomlUnknownDerivedTypeHandling.Unspecified)} is not a valid value for {nameof(TomlPolymorphismOptions)}.{nameof(UnknownDerivedTypeHandling)}.");
            }

            _unknownDerivedTypeHandling = value;
        }
    }

    /// <summary>
    /// Gets runtime-registered derived type mappings, keyed by polymorphic base type.
    /// </summary>
    /// <remarks>
    /// These mappings are used by reflection-based serialization and are merged with attribute-based registrations.
    /// Base-type attributes take precedence when the same derived type or discriminator is registered in both places.
    /// </remarks>
    public IReadOnlyDictionary<Type, IReadOnlyList<TomlDerivedType>> DerivedTypeMappings
    {
        get => _derivedTypeMappings;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Count == 0)
            {
                _derivedTypeMappings = EmptyDerivedTypeMappings;
                return;
            }

            var copy = new Dictionary<Type, IReadOnlyList<TomlDerivedType>>(value.Count);
            foreach (var pair in value)
            {
                if (pair.Key is null)
                {
                    throw new ArgumentException("Derived type mapping base types cannot be null.", nameof(value));
                }

                if (pair.Value is null)
                {
                    throw new ArgumentException($"Derived type mappings for '{pair.Key}' cannot be null.", nameof(value));
                }

                var derivedTypes = new TomlDerivedType[pair.Value.Count];
                for (var i = 0; i < derivedTypes.Length; i++)
                {
                    var entry = pair.Value[i];
                    if (entry is null)
                    {
                        throw new ArgumentException($"Derived type mappings for '{pair.Key}' cannot contain null entries.", nameof(value));
                    }

                    derivedTypes[i] = entry;
                }

                copy[pair.Key] = Array.AsReadOnly(derivedTypes);
            }

            _derivedTypeMappings = new ReadOnlyDictionary<Type, IReadOnlyList<TomlDerivedType>>(copy);
        }
    }
}
