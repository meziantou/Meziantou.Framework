using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml;

/// <summary>
/// Specifies behavior for scalar/array roots that do not naturally map to a TOML document.
/// </summary>
public enum TomlRootValueHandling
{
    /// <summary>
    /// Throw when attempting to serialize/deserialize a non-table root.
    /// </summary>
    Error = 0,

    /// <summary>
    /// Wrap the root value under <see cref="TomlSerializerOptions.RootValueKeyName"/>.
    /// </summary>
    WrapInRootKey = 1,
}
