using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml;

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
    /// <exception cref="ArgumentNullException">Value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Value is not a valid key name.</exception>
    public string TypeDiscriminatorPropertyName
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!TomlKeyValidation.IsValidKeyName(value))
            {
                throw new ArgumentException("The discriminator property name must be non-empty and contain valid Unicode characters.", nameof(value));
            }

            field = value;
        }
    } = "$type";

    /// <summary>
    /// Gets behavior when an unknown discriminator is encountered.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when set to <see cref="TomlUnknownDerivedTypeHandling.Unspecified"/> or an undefined value.</exception>
    public TomlUnknownDerivedTypeHandling UnknownDerivedTypeHandling
    {
        get => _unknownDerivedTypeHandling;
        init
        {
            if (value == TomlUnknownDerivedTypeHandling.Unspecified || !Enum.IsDefined(value))
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
