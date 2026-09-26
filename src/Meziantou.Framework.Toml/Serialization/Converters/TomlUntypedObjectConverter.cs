using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization.Internal;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal sealed class TomlUntypedObjectConverter : TomlConverter
{
    public static TomlUntypedObjectConverter Instance { get; } = new();

    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(object);

    public override object? Read(TomlReader reader, Type typeToConvert)
    {
        return reader.TokenType switch
        {
            TomlTokenType.StartTable => ReadTable(reader),
            TomlTokenType.StartArray => ReadArrayValue(reader),
            TomlTokenType.String => reader.GetString().AlsoAdvance(reader),
            TomlTokenType.Boolean => reader.GetBoolean().AlsoAdvance(reader),
            TomlTokenType.Integer => reader.GetInt64().AlsoAdvance(reader),
            TomlTokenType.Float => reader.GetDouble().AlsoAdvance(reader),
            TomlTokenType.DateTime => reader.GetTomlDateTime().AlsoAdvance(reader),
            _ => throw reader.CreateException($"Unexpected token {reader.TokenType} when reading object."),
        };
    }

    public override void Write(TomlWriter writer, object? value)
    {
        if (value is null)
        {
            throw new TomlException("TOML does not support null values.");
        }

        var options = writer.Options;
        var runtimeType = value.GetType();

        // Custom converters always win (mirrors serializer pipeline).
        var fromConverters = Meziantou.Framework.Toml.Serialization.Internal.TomlTypeInfoResolverPipeline.TryResolveFromConverters(options, runtimeType);
        if (fromConverters is not null)
        {
            fromConverters.Write(writer, value);
            return;
        }

        // The type info a generated context returns for a DOM type writes it through this converter, so asking the resolver
        // first would recurse until the stack overflows
        switch (value)
        {
            case TomlTable table:
                WriteTable(writer, table);
                return;
            case TomlArray array:
                WriteArray(writer, array);
                return;
            case TomlTableArray tableArray:
                WriteTableArray(writer, tableArray);
                return;
        }

        var fromResolver = options.TypeInfoResolver?.GetTypeInfo(runtimeType, options);
        if (fromResolver is not null)
        {
            fromResolver.Write(writer, value);
            return;
        }

        switch (value)
        {
            case string s:
                writer.WriteStringValue(s);
                return;
            case char c:
                TomlCharConverter.Instance.Write(writer, c);
                return;
            case bool b:
                writer.WriteBooleanValue(b);
                return;
            case sbyte i8:
                TomlSByteConverter.Instance.Write(writer, i8);
                return;
            case byte u8:
                TomlByteConverter.Instance.Write(writer, u8);
                return;
            case short i16:
                TomlInt16Converter.Instance.Write(writer, i16);
                return;
            case ushort u16:
                TomlUInt16Converter.Instance.Write(writer, u16);
                return;
            case uint u32:
                TomlUInt32Converter.Instance.Write(writer, u32);
                return;
            case long l:
                writer.WriteIntegerValue(l);
                return;
            case int i:
                writer.WriteIntegerValue(i);
                return;
            case ulong u64:
                TomlUInt64Converter.Instance.Write(writer, u64);
                return;
            case nint ni:
                TomlNIntConverter.Instance.Write(writer, ni);
                return;
            case nuint nu:
                TomlNUIntConverter.Instance.Write(writer, nu);
                return;
            case double d:
                writer.WriteFloatValue(d);
                return;
            case float f:
                writer.WriteFloatValue(f);
                return;
            case decimal m:
                TomlDecimalConverter.Instance.Write(writer, m);
                return;
            case TomlDateTime dt:
                writer.WriteDateTimeValue(dt);
                return;
            case DateTime dt2:
                TomlDateTimeConverter.Instance.Write(writer, dt2);
                return;
            case DateTimeOffset dto:
                TomlDateTimeOffsetConverter.Instance.Write(writer, dto);
                return;
            case DateOnly dateOnly:
                TomlDateOnlyConverter.Instance.Write(writer, dateOnly);
                return;
            case TimeOnly timeOnly:
                TomlTimeOnlyConverter.Instance.Write(writer, timeOnly);
                return;
            case Half half:
                TomlHalfConverter.Instance.Write(writer, half);
                return;
            case Int128 i128:
                TomlInt128Converter.Instance.Write(writer, i128);
                return;
            case UInt128 u128:
                TomlUInt128Converter.Instance.Write(writer, u128);
                return;
            case Guid guid:
                TomlGuidConverter.Instance.Write(writer, guid);
                return;
            case TimeSpan ts:
                TomlTimeSpanConverter.Instance.Write(writer, ts);
                return;
            case Uri uri:
                TomlUriConverter.Instance.Write(writer, uri);
                return;
            case Version version:
                TomlVersionConverter.Instance.Write(writer, version);
                return;
            case Enum:
                TomlEnumConverter.Instance.Write(writer, value);
                return;
            default:
                if (runtimeType == typeof(object))
                {
                    throw new TomlException("An untyped System.Object instance cannot be represented as TOML. Use a supported scalar/container type or a custom converter.");
                }

                // A collection or a POCO stored in an object member, like the serializer does for its root value
                if (TomlSerializer.IsReflectionEnabledByDefault)
                {
                    WriteUsingRuntimeType(writer, value, runtimeType);
                    return;
                }

                throw new TomlException($"Unsupported untyped TOML value `{runtimeType.FullName}`. Reflection-based serialization is disabled, so register metadata for this type with {nameof(TomlSerializerOptions)}.{nameof(TomlSerializerOptions.TypeInfoResolver)}.");
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Only called when reflection-based serialization is enabled; the feature switch removes the call when trimming.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "Only called when reflection-based serialization is enabled; the feature switch removes the call when trimming.")]
    private static void WriteUsingRuntimeType(TomlWriter writer, object value, Type runtimeType)
    {
        writer.ResolveTypeInfo(runtimeType).Write(writer, value);
    }

    internal static object ReadValue(TomlReader reader)
    {
        return reader.TokenType switch
        {
            TomlTokenType.StartTable => ReadTable(reader),
            TomlTokenType.StartArray => ReadArrayValue(reader),
            TomlTokenType.String => reader.GetString().AlsoAdvance(reader),
            TomlTokenType.Boolean => reader.GetBoolean().AlsoAdvance(reader),
            TomlTokenType.Integer => reader.GetInt64().AlsoAdvance(reader),
            TomlTokenType.Float => reader.GetDouble().AlsoAdvance(reader),
            TomlTokenType.DateTime => reader.GetTomlDateTime().AlsoAdvance(reader),
            _ => throw reader.CreateException($"Unexpected token {reader.TokenType} when reading untyped TOML value."),
        };
    }

    internal static TomlTable ReadTable(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.StartTable)
        {
            throw reader.CreateException($"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.");
        }

        var table = new TomlTable(inline: reader.IsInlineContainer);
        ReadTableInto(reader, table);
        return table;
    }

    internal static void ReadTableInto(TomlReader reader, TomlTable table)
    {
        if (reader.TokenType != TomlTokenType.StartTable)
        {
            throw reader.CreateException($"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.");
        }

        var store = reader.Options.MetadataStore;
        var hasStore = store is not null;
        TomlPropertiesMetadata? propertiesMetadata = null;
        var capturedAnyMetadata = false;
        if (hasStore)
        {
            if (store!.TryGetProperties(table, out var existingMetadata) && existingMetadata is not null)
            {
                propertiesMetadata = existingMetadata;
            }
            else
            {
                propertiesMetadata = new TomlPropertiesMetadata();
            }
        }

        reader.Read();
        while (reader.TokenType != TomlTokenType.EndTable)
        {
            if (reader.TokenType != TomlTokenType.PropertyName)
            {
                throw reader.CreateException($"Expected {TomlTokenType.PropertyName} token but was {reader.TokenType}.");
            }

            var name = reader.PropertyName!;
            var nameSpan = reader.CurrentSpan;
            var leadingTrivia = reader.CurrentLeadingTrivia;
            reader.Read();
            if (propertiesMetadata is not null)
            {
                capturedAnyMetadata |= CapturePropertyMetadata(propertiesMetadata, name, nameSpan, leadingTrivia, reader.CurrentTrailingTrivia, GetDisplayKind(reader));
            }

            // A table header or a dotted key extends the existing table; any other value replaces it (DuplicateKeyHandling.LastWins)
            if (TomlTableHeaderExtensionHelper.IsTableHeaderExtension(reader) &&
                table.TryGetValue(name, out var existingValue) &&
                existingValue is TomlTable existingTable)
            {
                if (existingTable.Kind == ObjectKind.InlineTable)
                {
                    throw reader.CreateException($"Cannot extend inline table '{name}' with a non-inline table definition.");
                }

                ReadTableInto(reader, existingTable);
                continue;
            }

            table[name] = ReadValue(reader);
        }

        reader.Read();
        if (hasStore && propertiesMetadata is not null && capturedAnyMetadata)
        {
            store!.SetProperties(table, propertiesMetadata);
        }
    }

    private static bool CapturePropertyMetadata(
        TomlPropertiesMetadata propertiesMetadata,
        string name,
        TomlSourceSpan? span,
        TomlSyntaxTriviaMetadata[]? leadingTrivia,
        TomlSyntaxTriviaMetadata[]? trailingTrivia,
        TomlPropertyDisplayKind displayKind)
    {
        var hasLeading = leadingTrivia is { Length: > 0 };
        var hasTrailing = trailingTrivia is { Length: > 0 };
        if (span is null && !hasLeading && !hasTrailing && displayKind == TomlPropertyDisplayKind.Default)
        {
            return false;
        }

        var propertyMetadata = new TomlPropertyMetadata
        {
            DisplayKind = displayKind,
        };

        if (span is { } locatedSpan)
        {
            propertyMetadata.Span = new SourceSpan(
                locatedSpan.SourceName,
                new TextPosition(locatedSpan.Start.Offset, locatedSpan.Start.Line, locatedSpan.Start.Column),
                new TextPosition(locatedSpan.End.Offset, locatedSpan.End.Line, locatedSpan.End.Column));
        }

        if (hasLeading)
        {
            propertyMetadata.LeadingTrivia = new List<TomlSyntaxTriviaMetadata>(leadingTrivia!);
        }

        if (hasTrailing)
        {
            propertyMetadata.TrailingTrivia = new List<TomlSyntaxTriviaMetadata>(trailingTrivia!);
        }

        propertiesMetadata.SetProperty(name, propertyMetadata);
        return true;
    }

    private static TomlPropertyDisplayKind GetDisplayKind(TomlReader reader)
    {
        switch (reader.TokenType)
        {
            case TomlTokenType.Integer:
            {
                var raw = reader.GetRawText();
                if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return TomlPropertyDisplayKind.IntegerHexadecimal;
                if (raw.StartsWith("0o", StringComparison.OrdinalIgnoreCase)) return TomlPropertyDisplayKind.IntegerOctal;
                if (raw.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) return TomlPropertyDisplayKind.IntegerBinary;
                return TomlPropertyDisplayKind.Default;
            }
            case TomlTokenType.String:
            {
                return reader.CurrentStringTokenKind switch
                {
                    TokenKind.StringMulti => TomlPropertyDisplayKind.StringMulti,
                    TokenKind.StringLiteral => TomlPropertyDisplayKind.StringLiteral,
                    TokenKind.StringLiteralMulti => TomlPropertyDisplayKind.StringLiteralMulti,
                    _ => TomlPropertyDisplayKind.Default,
                };
            }
            case TomlTokenType.DateTime:
            {
                var value = reader.GetTomlDateTime();
                return value.Kind switch
                {
                    TomlDateTimeKind.OffsetDateTimeByZ => TomlPropertyDisplayKind.OffsetDateTimeByZ,
                    TomlDateTimeKind.OffsetDateTimeByNumber => TomlPropertyDisplayKind.OffsetDateTimeByNumber,
                    TomlDateTimeKind.LocalDateTime => TomlPropertyDisplayKind.LocalDateTime,
                    TomlDateTimeKind.LocalDate => TomlPropertyDisplayKind.LocalDate,
                    TomlDateTimeKind.LocalTime => TomlPropertyDisplayKind.LocalTime,
                    _ => TomlPropertyDisplayKind.Default,
                };
            }
            case TomlTokenType.StartTable:
                return reader.IsInlineContainer ? TomlPropertyDisplayKind.InlineTable : TomlPropertyDisplayKind.Default;
            default:
                return TomlPropertyDisplayKind.Default;
        }
    }

    internal static object ReadArrayValue(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.StartArray)
        {
            throw reader.CreateException($"Expected {TomlTokenType.StartArray} token but was {reader.TokenType}.");
        }

        // Prefer preserving arrays-of-tables as TomlTableArray to match TOML semantics and writer expectations.
        // Note that empty arrays can't be distinguished and are treated as TomlArray.
        reader.Read();
        if (reader.TokenType == TomlTokenType.StartTable && !reader.IsInlineContainer)
        {
            var tableArray = new TomlTableArray();
            while (reader.TokenType != TomlTokenType.EndArray)
            {
                if (reader.TokenType != TomlTokenType.StartTable)
                {
                    throw reader.CreateException($"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.");
                }

                tableArray.Add(ReadTable(reader));
            }

            reader.Read();
            return tableArray;
        }

        var array = new TomlArray();
        while (reader.TokenType != TomlTokenType.EndArray)
        {
            array.Add(ReadValue(reader));
        }

        reader.Read();
        return array;
    }

    internal static TomlArray ReadArray(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.StartArray)
        {
            throw reader.CreateException($"Expected {TomlTokenType.StartArray} token but was {reader.TokenType}.");
        }

        var array = new TomlArray();
        reader.Read();
        while (reader.TokenType != TomlTokenType.EndArray)
        {
            array.Add(ReadValue(reader));
        }

        reader.Read();
        return array;
    }

    private static void WriteTable(TomlWriter writer, TomlTable table)
    {
        if (table.Kind == ObjectKind.InlineTable)
        {
            writer.WriteStartInlineTable();
        }
        else
        {
            writer.WriteStartTable();
        }

        writer.TryAttachMetadata(table);
        if (table.PropertiesMetadata is { } metadata && writer.CurrentTable is { } current)
        {
            current.PropertiesMetadata = metadata;
        }

        // The keys of a table are final: DottedKeyHandling applies to the names of members and dictionary keys, which
        // become table keys once, so a key such as "a.b" is not expanded again
        foreach (var pair in table)
        {
            writer.WritePropertyNameLiteral(pair.Key);
            Instance.Write(writer, pair.Value);
        }

        if (table.Kind == ObjectKind.InlineTable)
        {
            writer.WriteEndInlineTable();
        }
        else
        {
            writer.WriteEndTable();
        }
    }

    private static void WriteArray(TomlWriter writer, TomlArray array)
    {
        writer.WriteStartArray();
        foreach (var item in array)
        {
            Instance.Write(writer, item);
        }

        writer.WriteEndArray();
    }

    private static void WriteTableArray(TomlWriter writer, TomlTableArray array)
    {
        writer.WriteStartTableArray();
        foreach (var item in array)
        {
            WriteTable(writer, item);
        }

        writer.WriteEndTableArray();
    }
}
