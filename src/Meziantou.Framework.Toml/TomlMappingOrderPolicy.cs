using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml;

/// <summary>
/// Specifies member ordering behavior for emitted mappings.
/// </summary>
public enum TomlMappingOrderPolicy
{
    /// <summary>
    /// Order members by declaration order.
    /// </summary>
    Declaration = 0,

    /// <summary>
    /// Order members alphabetically by serialized name.
    /// </summary>
    Alphabetical = 1,

    /// <summary>
    /// Order members by explicit order then declaration order.
    /// </summary>
    OrderThenDeclaration = 2,

    /// <summary>
    /// Order members by explicit order then alphabetical order.
    /// </summary>
    OrderThenAlphabetical = 3,
}
