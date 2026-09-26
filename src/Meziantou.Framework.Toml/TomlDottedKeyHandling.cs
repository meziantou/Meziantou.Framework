using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml;

/// <summary>
/// Specifies how member names and dictionary keys containing '.' are written.
/// </summary>
/// <remarks>
/// Only writing is affected. A key read from TOML is a single key, so a document written with <see cref="Expand"/> is read
/// back as nested tables, and a model whose member names or dictionary keys contain '.' does not round-trip with it.
/// </remarks>
public enum TomlDottedKeyHandling
{
    /// <summary>
    /// Treat keys containing '.' as a literal key name by quoting/escaping as needed.
    /// </summary>
    Literal = 0,

    /// <summary>
    /// Treat keys containing '.' as a dotted path and expand into subtables. This is a write-only layout: the subtables are
    /// read back as nested tables.
    /// </summary>
    Expand = 1,
}
