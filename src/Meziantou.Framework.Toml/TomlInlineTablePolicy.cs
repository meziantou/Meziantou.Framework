using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml;

/// <summary>
/// Specifies inline table emission policy.
/// </summary>
public enum TomlInlineTablePolicy
{
    /// <summary>
    /// Never emit inline tables.
    /// </summary>
    Never = 0,

    /// <summary>
    /// Emit inline tables when a size heuristic considers it reasonable.
    /// </summary>
    WhenSmall = 1,

    /// <summary>
    /// Always emit inline tables when possible.
    /// </summary>
    Always = 2,
}
