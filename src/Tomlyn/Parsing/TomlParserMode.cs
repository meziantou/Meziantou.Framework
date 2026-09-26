// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace Tomlyn.Parsing;

/// <summary>
/// Specifies how <see cref="TomlParser"/> handles syntax errors.
/// </summary>
public enum TomlParserMode
{
    /// <summary>
    /// Stops at the first error and throws a <see cref="TomlException"/>.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// Records diagnostics and terminates the event stream without throwing.
    /// </summary>
    Tolerant = 1,
}

