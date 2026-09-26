using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Internal;

internal static class TomlModelTextWriter
{
    /// <summary>
    /// Gets a value indicating whether every value of the table is one the converters write unchanged, such as a string
    /// or an integer, rather than a value they convert, such as a <see cref="Guid"/>, an enum or an object.
    /// </summary>
    public static bool CanWriteDirectly(TomlTable root)
    {
        // A loop rather than a recursion, as the depth is only checked when writing
        var pending = new Stack<object>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case TomlTable table:
                    foreach (var pair in table)
                    {
                        pending.Push(pair.Value);
                    }

                    break;
                case TomlArray array:
                    foreach (var item in array)
                    {
                        pending.Push(item!);
                    }

                    break;
                case TomlTableArray tableArray:
                    foreach (var item in tableArray)
                    {
                        pending.Push(item);
                    }

                    break;
                case string or bool or sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                case TomlDateTime or DateTime or DateTimeOffset or DateOnly or TimeOnly:
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    public static void WriteDocument(TextWriter writer, TomlTable root, TomlSerializerOptions options)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        ArgumentGuard.ThrowIfNull(root, nameof(root));
        ArgumentGuard.ThrowIfNull(options, nameof(options));

        var state = new State(writer, options);
        var path = new List<string>();
        state.WriteTableBody(root, path, headerKind: HeaderKind.None, depth: 1);
    }

    private enum HeaderKind
    {
        None = 0,
        Table = 1,
        TableArrayElement = 2,
    }

    private sealed class State
    {
        private readonly TextWriter _writer;
        private readonly TomlSerializerOptions _options;
        private readonly int _effectiveMaxDepth;
        private readonly string _newLine;
        private readonly int _indentSize;
        private string _indent = string.Empty;

        public State(TextWriter writer, TomlSerializerOptions options)
        {
            _writer = writer;
            _options = options;
            _effectiveMaxDepth = TomlDepthHelper.GetEffectiveMaxDepth(options.MaxDepth);
            _newLine = options.NewLine == TomlNewLineKind.CrLf ? "\r\n" : "\n";
            _indentSize = options.WriteIndented ? options.IndentSize : 0;
        }

        public void WriteTableBody(TomlTable table, List<string> path, HeaderKind headerKind, int depth)
        {
            ValidateDepth(depth);

            // The header of a table and its entries are indented once per table they are nested in, like [a], then [a.b]
            _indent = _indentSize == 0 || path.Count <= 1 ? string.Empty : new string(' ', _indentSize * (path.Count - 1));
            if (headerKind != HeaderKind.None)
            {
                WriteHeader(path, headerKind, table.PropertiesMetadata);
            }

            // 1) scalar/array/inline-table key-values
            foreach (var pair in table)
            {
                var propertyMetadata = GetPropertyMetadata(table.PropertiesMetadata, pair.Key);
                var tableArrayStyle = propertyMetadata?.TableArrayStyle ?? _options.TableArrayStyle;
                if (pair.Value is TomlTableArray tableArray)
                {
                    // TOML has no header for an empty array of tables, so it is written as an empty array
                    if (tableArray.Count == 0 || (tableArrayStyle == TomlTableArrayStyle.InlineArrayOfTables &&
                        IsInlineableTableArray(tableArray, depth + 1)))
                    {
                        WriteTableArrayInline(pair.Key, tableArray, table.PropertiesMetadata, depth);
                        continue;
                    }

                    continue;
                }

                if (pair.Value is TomlTable subTable && !ShouldInlineTable(subTable, depth + 1, propertyMetadata?.InlineTablePolicy))
                {
                    continue;
                }

                WriteKeyValuePair(table, pair.Key, pair.Value, depth);
            }

            // 2) named tables
            foreach (var pair in table)
            {
                var propertyMetadata = GetPropertyMetadata(table.PropertiesMetadata, pair.Key);
                if (pair.Value is TomlTable subTable && !ShouldInlineTable(subTable, depth + 1, propertyMetadata?.InlineTablePolicy))
                {
                    path.Add(pair.Key);
                    WriteTableBody(subTable, path, HeaderKind.Table, depth + 1);
                    path.RemoveAt(path.Count - 1);
                }
            }

            // 3) table arrays
            foreach (var pair in table)
            {
                if (pair.Value is not TomlTableArray { Count: > 0 } tableArray)
                {
                    continue;
                }

                var propertyMetadata = GetPropertyMetadata(table.PropertiesMetadata, pair.Key);
                var tableArrayStyle = propertyMetadata?.TableArrayStyle ?? _options.TableArrayStyle;
                if (tableArrayStyle == TomlTableArrayStyle.InlineArrayOfTables &&
                    IsInlineableTableArray(tableArray, depth + 1))
                {
                    continue;
                }

                path.Add(pair.Key);
                WriteTableArray(tableArray, path, depth + 1);
                path.RemoveAt(path.Count - 1);
            }
        }

        private void WriteTableArray(TomlTableArray tableArray, List<string> path, int depth)
        {
            ValidateDepth(depth);
            for (var index = 0; index < tableArray.Count; index++)
            {
                if (index > 0)
                {
                    WriteNewLine();
                }

                WriteTableBody(tableArray[index], path, HeaderKind.TableArrayElement, depth + 1);
            }
        }

        private void WriteKeyValuePair(TomlTable table, string key, object value, int depth)
        {
            WriteLeadingTrivia(table.PropertiesMetadata, key);

            _writer.Write(_indent);
            WriteKey(key);
            _writer.Write(" = ");

            var displayKind = TomlPropertyDisplayKind.Default;
            var propertyMetadata = GetPropertyMetadata(table.PropertiesMetadata, key);
            if (propertyMetadata is not null)
            {
                displayKind = propertyMetadata.DisplayKind;
            }

            WriteValue(value, displayKind, depth, propertyMetadata);
            WriteTrailingTrivia(table.PropertiesMetadata, key);
            WriteNewLine();
            WriteTrailingTriviaAfterEndOfLine(table.PropertiesMetadata, key);
        }

        private static TomlPropertyMetadata? GetPropertyMetadata(TomlPropertiesMetadata? metadata, string key)
        {
            if (metadata is not null && metadata.TryGetProperty(key, out var propertyMetadata))
            {
                return propertyMetadata;
            }

            return null;
        }

        private bool ShouldInlineTable(TomlTable table, int depth, TomlInlineTablePolicy? policyOverride = null)
        {
            ValidateDepth(depth);
            if (table.Kind == ObjectKind.InlineTable)
            {
                return true;
            }

            return (policyOverride ?? _options.InlineTablePolicy) switch
            {
                TomlInlineTablePolicy.Always => IsInlineableTable(table, maxMemberCount: int.MaxValue, depth),
                TomlInlineTablePolicy.WhenSmall => IsInlineableTable(table, maxMemberCount: 4, depth),
                _ => false,
            };
        }

        private bool IsInlineableTable(TomlTable table, int maxMemberCount, int depth)
        {
            ValidateDepth(depth);
            if (table.Count > maxMemberCount)
            {
                return false;
            }

            foreach (var pair in table)
            {
                if (pair.Value is TomlTableArray)
                {
                    return false;
                }

                if (pair.Value is TomlTable child && !IsInlineableTable(child, maxMemberCount: int.MaxValue, depth + 1))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsInlineableTableArray(TomlTableArray tableArray, int depth)
        {
            ValidateDepth(depth);
            for (var i = 0; i < tableArray.Count; i++)
            {
                if (!IsInlineableTable(tableArray[i], maxMemberCount: int.MaxValue, depth + 1))
                {
                    return false;
                }
            }

            return true;
        }

        private void WriteTableArrayInline(string key, TomlTableArray tableArray, TomlPropertiesMetadata? metadata, int depth)
        {
            WriteLeadingTrivia(metadata, key);
            _writer.Write(_indent);
            WriteKey(key);
            _writer.Write(" = ");

            _writer.Write("[");
            for (var i = 0; i < tableArray.Count; i++)
            {
                if (i > 0)
                {
                    _writer.Write(", ");
                }

                WriteInlineTable(tableArray[i], depth + 2);
            }

            _writer.Write("]");
            WriteTrailingTrivia(metadata, key);
            WriteNewLine();
            WriteTrailingTriviaAfterEndOfLine(metadata, key);
        }

        private void WriteHeader(List<string> path, HeaderKind kind, TomlPropertiesMetadata? metadata)
        {
            var name = path[path.Count - 1];
            WriteLeadingTrivia(metadata, name);

            _writer.Write(_indent);
            if (kind == HeaderKind.TableArrayElement)
            {
                _writer.Write("[[");
            }
            else
            {
                _writer.Write("[");
            }

            for (var i = 0; i < path.Count; i++)
            {
                if (i > 0)
                {
                    _writer.Write(".");
                }

                WriteKey(path[i]);
            }

            if (kind == HeaderKind.TableArrayElement)
            {
                _writer.Write("]]");
            }
            else
            {
                _writer.Write("]");
            }

            WriteTrailingTrivia(metadata, name);
            WriteNewLine();
            WriteTrailingTriviaAfterEndOfLine(metadata, name);
        }

        private void WriteInlineTable(TomlTable table, int depth)
        {
            ValidateDepth(depth);
            _writer.Write("{");

            var first = true;
            foreach (var pair in table)
            {
                if (!first)
                {
                    _writer.Write(", ");
                }

                first = false;
                WriteKey(pair.Key);
                _writer.Write(" = ");

                // Inside an inline table, tables and arrays of tables can only be written inline
                if (pair.Value is TomlTable nestedTable)
                {
                    WriteInlineTable(nestedTable, depth + 1);
                    continue;
                }

                if (pair.Value is TomlTableArray nestedTableArray)
                {
                    WriteInlineTableArray(nestedTableArray, depth + 1);
                    continue;
                }

                WriteValue(pair.Value, TomlPropertyDisplayKind.Default, depth);
            }

            _writer.Write("}");
        }

        // An array of tables written as an array of inline tables
        private void WriteInlineTableArray(TomlTableArray tableArray, int depth)
        {
            ValidateDepth(depth);
            _writer.Write("[");
            for (var i = 0; i < tableArray.Count; i++)
            {
                if (i > 0)
                {
                    _writer.Write(", ");
                }

                WriteInlineTable(tableArray[i], depth + 1);
            }

            _writer.Write("]");
        }

        private void WriteValue(object value, TomlPropertyDisplayKind displayKind, int depth, TomlPropertyMetadata? propertyMetadata = null)
        {
            if (value is null)
            {
                throw new TomlException("TOML does not support null values.");
            }

            switch (value)
            {
                case string s:
                    WriteString(s, displayKind, propertyMetadata);
                    return;
                case bool b:
                    _writer.Write(b ? "true" : "false");
                    return;
                case sbyte i8:
                    if (displayKind == TomlPropertyDisplayKind.Default)
                    {
                        WriteInteger(i8);
                    }
                    else
                    {
                        _writer.Write(TomlFormatHelper.ToString(i8, displayKind));
                    }
                    return;
                case byte u8:
                    if (displayKind == TomlPropertyDisplayKind.Default)
                    {
                        WriteInteger(u8);
                    }
                    else
                    {
                        _writer.Write(TomlFormatHelper.ToString(u8, displayKind));
                    }
                    return;
                case short i16:
                    if (displayKind == TomlPropertyDisplayKind.Default)
                    {
                        WriteInteger(i16);
                    }
                    else
                    {
                        _writer.Write(TomlFormatHelper.ToString(i16, displayKind));
                    }
                    return;
                case ushort u16:
                    if (displayKind == TomlPropertyDisplayKind.Default)
                    {
                        WriteInteger(u16);
                    }
                    else
                    {
                        _writer.Write(TomlFormatHelper.ToString(u16, displayKind));
                    }
                    return;
                case int i32:
                    if (displayKind == TomlPropertyDisplayKind.Default)
                    {
                        WriteInteger(i32);
                    }
                    else
                    {
                        _writer.Write(TomlFormatHelper.ToString(i32, displayKind));
                    }
                    return;
                case uint u32:
                    if (displayKind == TomlPropertyDisplayKind.Default)
                    {
                        WriteInteger(u32);
                    }
                    else
                    {
                        _writer.Write(TomlFormatHelper.ToString(u32, displayKind));
                    }
                    return;
                case long i64:
                    if (displayKind == TomlPropertyDisplayKind.Default)
                    {
                        WriteInteger(i64);
                    }
                    else
                    {
                        _writer.Write(TomlFormatHelper.ToString(i64, displayKind));
                    }
                    return;
                case ulong u64:
                    if (u64 > long.MaxValue)
                    {
                        throw new TomlException($"TOML integers are limited to signed 64-bit. Value {u64} cannot be written.");
                    }

                    if (displayKind == TomlPropertyDisplayKind.Default)
                    {
                        WriteInteger(unchecked((long)u64));
                    }
                    else
                    {
                        _writer.Write(TomlFormatHelper.ToString(u64, displayKind));
                    }
                    return;
                case float f32:
                    WriteFloat(f32);
                    return;
                case double f64:
                    WriteFloat(f64);
                    return;
                case decimal dec:
                    _writer.Write(ToTomlFloatString(dec));
                    return;
                case TomlDateTime dt:
                    _writer.Write(TomlFormatHelper.ToString(dt));
                    return;
                case DateTime dateTime:
                    _writer.Write(TomlFormatHelper.ToString(dateTime, displayKind));
                    return;
                case DateTimeOffset dateTimeOffset:
                    _writer.Write(TomlFormatHelper.ToString(dateTimeOffset, displayKind));
                    return;
                case DateOnly dateOnly:
                    _writer.Write(TomlFormatHelper.ToString(dateOnly, displayKind));
                    return;
                case TimeOnly timeOnly:
                    _writer.Write(TomlFormatHelper.ToString(timeOnly, displayKind));
                    return;
                case TomlArray array:
                    WriteArray(array, depth + 1);
                    return;
                case TomlTable inlineTable when ShouldInlineTable(inlineTable, depth + 1, propertyMetadata?.InlineTablePolicy):
                    WriteInlineTable(inlineTable, depth + 1);
                    return;
                case TomlTable:
                    throw new TomlException("Non-inline tables must be emitted as table headers.");
                case TomlTableArray:
                    throw new TomlException("Table arrays must be emitted as table headers or inline array of tables.");
                default:
                    if (value is Enum enumValue)
                    {
                        WriteString(enumValue.ToString(), displayKind, propertyMetadata);
                        return;
                    }

                    throw new TomlException($"Unsupported TOML value `{value.GetType().FullName}`.");
            }
        }

        private void WriteInteger(long value)
        {
            Span<char> buffer = stackalloc char[32];
            if (value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture))
            {
                _writer.Write(buffer.Slice(0, written));
                return;
            }

            _writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        private void WriteFloat(float value)
        {
            if (float.IsNaN(value))
            {
                _writer.Write("nan");
                return;
            }

            if (float.IsPositiveInfinity(value))
            {
                _writer.Write("+inf");
                return;
            }

            if (float.IsNegativeInfinity(value))
            {
                _writer.Write("-inf");
                return;
            }

            Span<char> buffer = stackalloc char[64];
            if (value.TryFormat(buffer, out var written, "g9", CultureInfo.InvariantCulture))
            {
                WriteFloatWithDecimalPoint(buffer.Slice(0, written));
                return;
            }

            _writer.Write(TomlFormatHelper.ToString(value));
        }

        private void WriteFloat(double value)
        {
            if (double.IsNaN(value))
            {
                _writer.Write("nan");
                return;
            }

            if (double.IsPositiveInfinity(value))
            {
                _writer.Write("+inf");
                return;
            }

            if (double.IsNegativeInfinity(value))
            {
                _writer.Write("-inf");
                return;
            }

            Span<char> buffer = stackalloc char[64];
            if (value.TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture))
            {
                WriteFloatWithDecimalPoint(buffer.Slice(0, written));
                return;
            }

            _writer.Write(TomlFormatHelper.ToString(value));
        }

        private void WriteFloatWithDecimalPoint(ReadOnlySpan<char> text)
        {
            if (text.Length == 1 && text[0] == '0')
            {
                _writer.Write("0.0");
                return;
            }

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '.' || c == 'e' || c == 'E')
                {
                    _writer.Write(text);
                    return;
                }
            }

            _writer.Write(text);
            _writer.Write(".0");
        }

        private void WriteArray(TomlArray array, int depth)
        {
            ValidateDepth(depth);
            _writer.Write("[");
            for (var i = 0; i < array.Count; i++)
            {
                if (i > 0)
                {
                    _writer.Write(", ");
                }

                // Inside an array, tables and arrays of tables can only be written inline
                var value = array[i]!;
                if (value is TomlTable nestedTable)
                {
                    WriteInlineTable(nestedTable, depth + 1);
                    continue;
                }

                if (value is TomlTableArray nestedTableArray)
                {
                    WriteInlineTableArray(nestedTableArray, depth + 1);
                    continue;
                }

                WriteValue(value, TomlPropertyDisplayKind.Default, depth);
            }
            _writer.Write("]");
        }

        private void ValidateDepth(int depth)
        {
            if (depth > _effectiveMaxDepth)
            {
                TomlDepthHelper.ThrowDepthExceeded(_effectiveMaxDepth);
            }

            TomlDepthHelper.EnsureSufficientExecutionStack();
        }

        private void WriteKey(string name)
        {
            ThrowIfUnpairedSurrogate(name, "key");
            if (string.IsNullOrWhiteSpace(name))
            {
                _writer.Write('\"');
                WriteEscapedBasicStringContent(name, allowNewLinesAndTabs: false);
                _writer.Write('\"');
                return;
            }

            // A-Za-z0-9_-
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (!(c >= 'a' && c <= 'z' ||
                      c >= 'A' && c <= 'Z' ||
                      c >= '0' && c <= '9' ||
                      c == '_' ||
                      c == '-') ||
                    c == '.')
                {
                    _writer.Write('\"');
                    WriteEscapedBasicStringContent(name, allowNewLinesAndTabs: false);
                    _writer.Write('\"');
                    return;
                }
            }

            _writer.Write(name);
        }

        private static void ThrowIfUnpairedSurrogate(string value, string kind)
        {
            var index = CharHelper.IndexOfUnpairedSurrogate(value);
            if (index >= 0)
            {
                throw new TomlException(CharHelper.GetUnpairedSurrogateMessage(value, index, kind));
            }
        }

        private void WriteString(string value, TomlPropertyDisplayKind displayKind, TomlPropertyMetadata? propertyMetadata)
        {
            ThrowIfUnpairedSurrogate(value, "string");
            var preferences = _options.StringStylePreferences;
            var allowHexEscapes = propertyMetadata?.AllowHexEscapes ?? preferences.AllowHexEscapes;
            if (displayKind == TomlPropertyDisplayKind.Default)
            {
                var style = propertyMetadata?.StringStyle ?? preferences.DefaultStyle;
                var preferLiteralWhenNoEscapes = propertyMetadata?.PreferLiteralWhenNoEscapes ?? preferences.PreferLiteralWhenNoEscapes;
                if (preferLiteralWhenNoEscapes && style == TomlStringStyle.Basic && IsSafeStringLiteral(value))
                {
                    style = TomlStringStyle.Literal;
                }

                displayKind = style switch
                {
                    TomlStringStyle.Literal => TomlPropertyDisplayKind.StringLiteral,
                    TomlStringStyle.MultilineBasic => TomlPropertyDisplayKind.StringMulti,
                    TomlStringStyle.MultilineLiteral => TomlPropertyDisplayKind.StringLiteralMulti,
                    _ => TomlPropertyDisplayKind.Default,
                };
            }

            switch (displayKind)
            {
                // TOML trims a newline right after the opening delimiter, so always write one to keep a leading newline
                case TomlPropertyDisplayKind.StringMulti:
                {
                    _writer.Write("\"\"\"");
                    _writer.Write(_newLine);
                    WriteEscapedBasicStringContent(value, allowNewLinesAndTabs: true, allowHexEscapes);
                    _writer.Write("\"\"\"");
                    return;
                }
                case TomlPropertyDisplayKind.StringLiteral:
                    if (IsSafeStringLiteral(value))
                    {
                        _writer.Write('\'');
                        _writer.Write(value);
                        _writer.Write('\'');
                        return;
                    }
                    break;
                case TomlPropertyDisplayKind.StringLiteralMulti:
                    if (IsSafeStringLiteralMulti(value))
                    {
                        _writer.Write("'''");
                        _writer.Write(_newLine);
                        _writer.Write(value);
                        _writer.Write("'''");
                        return;
                    }
                    break;
            }

            _writer.Write('\"');
            WriteEscapedBasicStringContent(value, allowNewLinesAndTabs: false, allowHexEscapes);
            _writer.Write('\"');
        }

        private void WriteEscapedBasicStringContent(string value, bool allowNewLinesAndTabs, bool allowHexEscapes = true)
        {
            if (!RequiresEscaping(value, allowNewLinesAndTabs))
            {
                _writer.Write(value);
                return;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];

                if (allowNewLinesAndTabs)
                {
                    if (c == '\r' && i + 1 < value.Length && value[i + 1] == '\n')
                    {
                        _writer.Write("\r\n");
                        i++;
                        continue;
                    }

                    if (c == '\n')
                    {
                        _writer.Write('\n');
                        continue;
                    }

                    if (c == '\t')
                    {
                        _writer.Write('\t');
                        continue;
                    }
                }

                if (c < ' ' || c == '"' || c == '\\' || char.IsControl(c))
                {
                    switch (c)
                    {
                        case '\b':
                            _writer.Write("\\b");
                            continue;
                        case '\t':
                            _writer.Write("\\t");
                            continue;
                        case '\n':
                            _writer.Write("\\n");
                            continue;
                        case '\f':
                            _writer.Write("\\f");
                            continue;
                        case '\r':
                            _writer.Write("\\r");
                            continue;
                        case '"':
                            _writer.Write("\\\"");
                            continue;
                        case '\\':
                            _writer.Write("\\\\");
                            continue;
                        default:
                            if (!allowHexEscapes)
                            {
                                throw new TomlException("The string contains a control character that requires a hexadecimal escape, but hexadecimal escapes are disabled.");
                            }

                            _writer.Write("\\u");
                            WriteHex4((ushort)c);
                            continue;
                    }
                }

                _writer.Write(c);
            }
        }

        private static bool RequiresEscaping(string value, bool allowNewLinesAndTabs)
        {
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];

                if (allowNewLinesAndTabs)
                {
                    if (c == '\r' && i + 1 < value.Length && value[i + 1] == '\n')
                    {
                        i++;
                        continue;
                    }

                    if (c == '\n' || c == '\t')
                    {
                        continue;
                    }
                }

                if (c < ' ' || c == '"' || c == '\\' || char.IsControl(c))
                {
                    return true;
                }
            }

            return false;
        }

        private void WriteHex4(ushort value)
        {
            WriteHexDigit((value >> 12) & 0xF);
            WriteHexDigit((value >> 8) & 0xF);
            WriteHexDigit((value >> 4) & 0xF);
            WriteHexDigit(value & 0xF);
        }

        private void WriteHexDigit(int value)
        {
            _writer.Write((char)(value < 10 ? '0' + value : 'A' + (value - 10)));
        }

        private static bool IsSafeStringLiteral(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (char.IsControl(c) || c == '\'')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSafeStringLiteralMulti(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                // Newlines are permitted.
                if ((c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') || c == '\n')
                {
                    continue;
                }

                if (char.IsControl(c))
                {
                    return false;
                }

                // Sequence of 3 or more ' are not permitted.
                if (c == '\'')
                {
                    if (i + 2 < text.Length && text[i + 1] == '\'' && text[i + 2] == '\'')
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static string ToTomlFloatString(decimal value)
        {
            // TOML has no decimal type; represent `decimal` as a float.
            var text = value.ToString("G", CultureInfo.InvariantCulture);
            if (text.Contains('.', StringComparison.Ordinal) || text.Contains('e', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal))
            {
                return text;
            }

            return text + ".0";
        }

        private void WriteNewLine()
        {
            _writer.Write(_newLine);
        }

        private void WriteLeadingTrivia(TomlPropertiesMetadata? metadata, string key)
        {
            if (metadata is null || !metadata.TryGetProperty(key, out var propertyMetadata) || propertyMetadata?.LeadingTrivia is null)
            {
                return;
            }

            foreach (var trivia in propertyMetadata.LeadingTrivia)
            {
                if (trivia.Text is not null)
                {
                    _writer.Write(trivia.Text);
                }
            }
        }

        private void WriteTrailingTrivia(TomlPropertiesMetadata? metadata, string key)
        {
            if (metadata is null || !metadata.TryGetProperty(key, out var propertyMetadata) || propertyMetadata?.TrailingTrivia is null)
            {
                return;
            }

            foreach (var trivia in propertyMetadata.TrailingTrivia)
            {
                if (trivia.Text is not null)
                {
                    _writer.Write(trivia.Text);
                }
            }
        }

        private void WriteTrailingTriviaAfterEndOfLine(TomlPropertiesMetadata? metadata, string key)
        {
            if (metadata is null || !metadata.TryGetProperty(key, out var propertyMetadata) || propertyMetadata?.TrailingTriviaAfterEndOfLine is null)
            {
                return;
            }

            foreach (var trivia in propertyMetadata.TrailingTriviaAfterEndOfLine)
            {
                if (trivia.Text is not null)
                {
                    _writer.Write(trivia.Text);
                }
            }
        }
    }
}
