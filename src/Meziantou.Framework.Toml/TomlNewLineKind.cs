using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml;

/// <summary>
/// Specifies newline kind.
/// </summary>
public enum TomlNewLineKind
{
    /// <summary>
    /// Use LF (<c>\n</c>).
    /// </summary>
    Lf = 0,

    /// <summary>
    /// Use CRLF (<c>\r\n</c>).
    /// </summary>
    CrLf = 1,
}
