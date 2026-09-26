using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml;

/// <summary>
/// Configures string scalar style preferences for TOML serialization.
/// </summary>
public sealed record TomlStringStylePreferences
{
    /// <summary>Preferred default string style.</summary>
    public TomlStringStyle DefaultStyle { get; init; } = TomlStringStyle.Basic;

    /// <summary>If true, prefer literal strings when no escaping is required.</summary>
    public bool PreferLiteralWhenNoEscapes { get; init; }

    /// <summary>
    /// If true, control characters up to U+00FF are escaped with the TOML 1.1 <c>\xHH</c> and <c>\e</c> escapes. Otherwise,
    /// they are escaped with <c>\uXXXX</c>, which TOML 1.0 readers also accept. Keys use this option too.
    /// </summary>
    public bool AllowHexEscapes { get; init; }
}
