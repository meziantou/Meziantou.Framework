using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml;

/// <summary>
/// Specifies behavior when an unknown derived type discriminator is encountered.
/// </summary>
/// <remarks>
/// It applies only when no default derived type (one registered without a discriminator) exists. A default derived type reads
/// every unknown discriminator.
/// </remarks>
public enum TomlUnknownDerivedTypeHandling
{
    /// <summary>Use the serializer options default. This value is only valid on attribute properties and must not be used on <see cref="TomlPolymorphismOptions"/>.</summary>
    Unspecified = -1,
    /// <summary>Fail deserialization.</summary>
    Fail = 0,
    /// <summary>Fallback to the base type.</summary>
    FallBackToBaseType = 1,
}
