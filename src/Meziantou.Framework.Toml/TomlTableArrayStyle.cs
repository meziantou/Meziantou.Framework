using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn.Helpers;
using Tomlyn.Serialization;

namespace Tomlyn;

/// <summary>
/// Specifies how arrays of tables are emitted.
/// </summary>
public enum TomlTableArrayStyle
{
    /// <summary>
    /// Emit arrays of tables using headers (<c>[[a]]</c> style).
    /// </summary>
    Headers = 0,

    /// <summary>
    /// Emit arrays of tables as an inline array of inline tables (<c>a = [{...}, {...}]</c>).
    /// </summary>
    InlineArrayOfTables = 1,
}
