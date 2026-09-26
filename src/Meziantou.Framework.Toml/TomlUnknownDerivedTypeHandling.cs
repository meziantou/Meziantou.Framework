using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn.Helpers;
using Tomlyn.Serialization;

namespace Tomlyn;

/// <summary>
/// Specifies behavior when an unknown derived type discriminator is encountered.
/// </summary>
public enum TomlUnknownDerivedTypeHandling
{
    /// <summary>Use the serializer options default. This value is only valid on attribute properties and must not be used on <see cref="TomlPolymorphismOptions"/>.</summary>
    Unspecified = -1,
    /// <summary>Fail deserialization.</summary>
    Fail = 0,
    /// <summary>Fallback to the base type.</summary>
    FallBackToBaseType = 1,
}
