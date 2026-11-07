using System;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Represents an optional boolean formatting preference on an attribute.
/// </summary>
public enum TomlBooleanPreference
{
    /// <summary>
    /// Use the value from the broader serializer options scope.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The preference is enabled.
    /// </summary>
    True = 1,

    /// <summary>
    /// The preference is disabled.
    /// </summary>
    False = 2,
}
