using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml;

/// <summary>
/// Specifies behavior for dictionary keys containing '.'.
/// </summary>
public enum TomlDottedKeyHandling
{
    /// <summary>
    /// Treat keys containing '.' as a literal key name by quoting/escaping as needed.
    /// </summary>
    Literal = 0,

    /// <summary>
    /// Treat keys containing '.' as a dotted path and expand into subtables.
    /// </summary>
    Expand = 1,
}
