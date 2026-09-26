using System;
using System.Collections.Generic;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Internal;

// Records how a property was written (comments, span, display kind), for the reflection-based and generated metadata
internal static class TomlPropertyMetadataCapture
{
    public static void Capture(
        TomlPropertiesMetadata? propertiesMetadata,
        string name,
        TomlSourceSpan? span,
        TomlSyntaxTriviaMetadata[]? leadingTrivia,
        TomlSyntaxTriviaMetadata[]? trailingTrivia,
        TomlPropertyDisplayKind displayKind)
    {
        if (propertiesMetadata is null)
        {
            return;
        }

        var hasLeading = leadingTrivia is { Length: > 0 };
        var hasTrailing = trailingTrivia is { Length: > 0 };
        if (span is null && !hasLeading && !hasTrailing && displayKind == TomlPropertyDisplayKind.Default)
        {
            return;
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
    }

    // The trailing comment of an inline array or table is only known once its closing token is read
    public static bool AppendTrailingTrivia(TomlPropertiesMetadata? propertiesMetadata, string name, TomlSyntaxTriviaMetadata[]? trailingTrivia)
    {
        if (propertiesMetadata is null || trailingTrivia is not { Length: > 0 })
        {
            return false;
        }

        if (propertiesMetadata.TryGetProperty(name, out var propertyMetadata) && propertyMetadata is not null)
        {
            (propertyMetadata.TrailingTrivia ??= []).AddRange(trailingTrivia);
        }
        else
        {
            propertiesMetadata.SetProperty(name, new TomlPropertyMetadata { TrailingTrivia = new List<TomlSyntaxTriviaMetadata>(trailingTrivia) });
        }

        return true;
    }

    public static TomlPropertyDisplayKind GetDisplayKind(TomlReader reader)
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
}
