// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using Tomlyn.Helpers;
using Tomlyn.Model;
using Tomlyn.Serialization.Converters;

namespace Tomlyn.Serialization.Internal;

internal static class TomlTableHeaderExtensionHelper
{
    public static bool IsTableHeaderExtension(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        return reader.TokenType == TomlTokenType.StartTable && reader.CurrentSpan is null;
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
