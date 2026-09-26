using System;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization.Converters;

namespace Meziantou.Framework.Toml.Serialization.Internal;

internal static class TomlTableHeaderExtensionHelper
{
    public static bool IsTableHeaderExtension(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        return reader.TokenType == TomlTokenType.StartTable && !reader.IsInlineContainer;
    }

    public static bool TryReadIntoExisting(TomlReader reader, object? existingValue, TomlTypeInfo typeInfo, out object? populatedValue)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));

        populatedValue = existingValue;
        if (!IsTableHeaderExtension(reader) || existingValue is null)
        {
            return false;
        }

        if (existingValue is TomlTable existingTable)
        {
            TomlUntypedObjectConverter.ReadTableInto(reader, existingTable);
            populatedValue = existingTable;
            return true;
        }

        populatedValue = typeInfo.ReadInto(reader, existingValue);
        return true;
    }
}
