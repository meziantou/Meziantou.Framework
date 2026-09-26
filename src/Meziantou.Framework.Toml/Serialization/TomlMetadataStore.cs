using System.Runtime.CompilerServices;
using Tomlyn.Model;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Default <see cref="ITomlMetadataStore"/> implementation backed by a <see cref="ConditionalWeakTable{TKey,TValue}"/>.
/// </summary>
public sealed class TomlMetadataStore : ITomlMetadataStore
{
    private readonly ConditionalWeakTable<object, TomlPropertiesMetadata> _table = new();

    /// <inheritdoc />
    public bool TryGetProperties(object instance, out TomlPropertiesMetadata? metadata)
    {
        ArgumentGuard.ThrowIfNull(instance, nameof(instance));
        return _table.TryGetValue(instance, out metadata);
    }

    /// <inheritdoc />
    public void SetProperties(object instance, TomlPropertiesMetadata? metadata)
    {
        ArgumentGuard.ThrowIfNull(instance, nameof(instance));

        if (metadata is null)
        {
            _table.Remove(instance);
            return;
        }

        _table.Remove(instance);
        _table.Add(instance, metadata);
    }
}
