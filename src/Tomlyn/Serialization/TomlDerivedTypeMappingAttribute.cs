// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Globalization;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Registers a derived type mapping on a source-generated <see cref="TomlSerializerContext"/>, enabling cross-project polymorphism.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TomlDerivedTypeMappingAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlDerivedTypeMappingAttribute"/> class with no discriminator.
    /// </summary>
    /// <param name="baseType">The polymorphic base type.</param>
    /// <param name="derivedType">The derived CLR type.</param>
    public TomlDerivedTypeMappingAttribute(Type baseType, Type derivedType)
    {
        ArgumentGuard.ThrowIfNull(baseType, nameof(baseType));
        ArgumentGuard.ThrowIfNull(derivedType, nameof(derivedType));

        BaseType = baseType;
        DerivedType = derivedType;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlDerivedTypeMappingAttribute"/> class with a string discriminator.
    /// </summary>
    /// <param name="baseType">The polymorphic base type.</param>
    /// <param name="derivedType">The derived CLR type.</param>
    /// <param name="discriminator">The discriminator value.</param>
    public TomlDerivedTypeMappingAttribute(Type baseType, Type derivedType, string discriminator)
    {
        ArgumentGuard.ThrowIfNull(baseType, nameof(baseType));
        ArgumentGuard.ThrowIfNull(derivedType, nameof(derivedType));
        ArgumentGuard.ThrowIfNull(discriminator, nameof(discriminator));

        BaseType = baseType;
        DerivedType = derivedType;
        Discriminator = discriminator;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlDerivedTypeMappingAttribute"/> class with an integer discriminator.
    /// </summary>
    /// <param name="baseType">The polymorphic base type.</param>
    /// <param name="derivedType">The derived CLR type.</param>
    /// <param name="discriminator">The integer discriminator value.</param>
    public TomlDerivedTypeMappingAttribute(Type baseType, Type derivedType, int discriminator)
    {
        ArgumentGuard.ThrowIfNull(baseType, nameof(baseType));
        ArgumentGuard.ThrowIfNull(derivedType, nameof(derivedType));

        BaseType = baseType;
        DerivedType = derivedType;
        Discriminator = discriminator.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets the polymorphic base type.
    /// </summary>
    public Type BaseType { get; }

    /// <summary>
    /// Gets the derived CLR type.
    /// </summary>
    public Type DerivedType { get; }

    /// <summary>
    /// Gets the discriminator value, or <see langword="null"/> when this is the default derived type.
    /// </summary>
    public string? Discriminator { get; }
}
