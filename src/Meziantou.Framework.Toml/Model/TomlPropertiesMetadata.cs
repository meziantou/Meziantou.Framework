using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Syntax;

namespace Meziantou.Framework.Toml.Model;

/// <summary>
/// Stores metadata for TOML properties, including trivia and display hints.
/// </summary>
[DebuggerDisplay("{_properties}")]
public class TomlPropertiesMetadata
{
    private readonly Dictionary<string, TomlPropertyMetadata> _properties;

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlPropertiesMetadata"/> class.
    /// </summary>
    public TomlPropertiesMetadata()
    {
        _properties = new Dictionary<string, TomlPropertyMetadata>();
    }

    private TomlPropertiesMetadata(Dictionary<string, TomlPropertyMetadata> properties)
    {
        _properties = new Dictionary<string, TomlPropertyMetadata>(properties);
    }

    /// <summary>
    /// Clears all property metadata.
    /// </summary>
    public void Clear()
    {
        _properties.Clear();
    }

    /// <summary>
    /// Checks whether metadata exists for the specified property key.
    /// </summary>
    /// <param name="propertyKey">The property key.</param>
    /// <returns><c>true</c> if metadata exists; otherwise <c>false</c>.</returns>
    public bool ContainsProperty(string propertyKey) => _properties.ContainsKey(propertyKey);

    /// <summary>
    /// Tries to get metadata for the specified property key.
    /// </summary>
    /// <param name="propertyKey">The property key.</param>
    /// <param name="propertyMetadata">The metadata when found.</param>
    /// <returns><c>true</c> if metadata exists; otherwise <c>false</c>.</returns>
    public bool TryGetProperty(string propertyKey, [NotNullWhen(true)] out TomlPropertyMetadata? propertyMetadata)
    {
        return _properties.TryGetValue(propertyKey, out propertyMetadata);
    }

    /// <summary>
    /// Sets metadata for the specified property key.
    /// </summary>
    /// <param name="propertyKey">The property key.</param>
    /// <param name="propertyMetadata">The metadata to set.</param>
    public void SetProperty(string propertyKey, TomlPropertyMetadata propertyMetadata)
    {
        _properties[propertyKey] = propertyMetadata;
    }

    // A copy that shares the property metadata objects, so they must be replaced rather than modified
    internal TomlPropertiesMetadata Clone() => new(_properties);
}
