// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Syntax.Inlines;

/// <summary>
/// Defines the LocalLabel enumeration.
/// </summary>
public enum LocalLabel : byte
{
    /// <summary>
    /// Gets or sets the local.
    /// </summary>
    Local, // [foo][bar]
    /// <summary>
    /// Gets or sets the empty.
    /// </summary>
    Empty, // [foo][]
    /// <summary>
    /// Gets or sets the none.
    /// </summary>
    None, // [foo]
}
