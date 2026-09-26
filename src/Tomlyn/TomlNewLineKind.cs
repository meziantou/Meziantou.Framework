// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn.Helpers;
using Tomlyn.Serialization;

namespace Tomlyn;

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
