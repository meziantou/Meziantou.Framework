using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn.Helpers;
using Tomlyn.Serialization;

namespace Tomlyn;

/// <summary>
/// Specifies preferred default string style.
/// </summary>
public enum TomlStringStyle
{
    /// <summary>Basic string.</summary>
    Basic = 0,
    /// <summary>Literal string.</summary>
    Literal = 1,
    /// <summary>Multiline basic string.</summary>
    MultilineBasic = 2,
    /// <summary>Multiline literal string.</summary>
    MultilineLiteral = 3,
}
