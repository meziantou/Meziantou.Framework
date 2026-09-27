using System;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization.Converters;

namespace Meziantou.Framework.Toml.Serialization.Internal;

internal static class TomlTableHeaderExtensionHelper
{
    public static bool IsTableHeaderExtension(TomlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.TokenType == TomlTokenType.StartTable && !reader.IsInlineContainer;
    }

    public static bool TryReadIntoExisting(TomlReader reader, object? existingValue, TomlTypeInfo typeInfo, out object? populatedValue)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeInfo);

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
