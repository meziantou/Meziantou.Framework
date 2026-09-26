using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml;

/// <summary>
/// Specifies behavior when duplicate keys are encountered.
/// </summary>
/// <remarks>
/// This setting only applies to keys assigned a value twice. A document that redefines a table, extends an inline table
/// or a static array, or assigns a value to a key defined as a table is invalid TOML and is always rejected.
/// </remarks>
public enum TomlDuplicateKeyHandling
{
    /// <summary>
    /// Reject duplicate keys, as required by the TOML specification.
    /// </summary>
    Error = 0,

    /// <summary>
    /// Keep the last value for a key assigned more than once.
    /// </summary>
    LastWins = 1,
}
