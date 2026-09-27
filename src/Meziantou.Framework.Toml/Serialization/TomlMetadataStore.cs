using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Toml.Model;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Default <see cref="ITomlMetadataStore"/> implementation backed by a <see cref="ConditionalWeakTable{TKey,TValue}"/>.
/// </summary>
public sealed class TomlMetadataStore : ITomlMetadataStore
{
    private readonly ConditionalWeakTable<object, TomlPropertiesMetadata> _table = new();

    /// <inheritdoc />
    public bool TryGetProperties(object instance, [NotNullWhen(true)] out TomlPropertiesMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return _table.TryGetValue(instance, out metadata);
    }

    /// <inheritdoc />
    public void SetProperties(object instance, TomlPropertiesMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (metadata is null)
        {
            _table.Remove(instance);
            return;
        }

        _table.Remove(instance);
        _table.Add(instance, metadata);
    }
}
