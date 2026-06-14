namespace Meziantou.Framework.Yamlish;

internal sealed class YamlishParser
{
    private readonly string[] _lines;
    private int _index;

    private YamlishParser(string content)
    {
        _lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    }

    public static YamlishNode Parse(string content)
    {
        var parser = new YamlishParser(content);
        parser.SkipBlankLines();
        if (parser._index >= parser._lines.Length)
            return new YamlishMapping();

        var indent = parser.GetIndent(parser._index);
        var result = parser.ParseBlock(indent);
        parser.SkipBlankLines();
        if (parser._index < parser._lines.Length)
            Throw("Unexpected content", parser._index);

        return result;
    }

    private YamlishNode ParseBlock(int indent)
    {
        ValidateIndent(_index, indent);
        var content = _lines[_index].AsSpan(indent);
        if (IsSequenceLine(content))
            return ParseSequence(indent);

        if (FindMappingSeparator(content) >= 0)
            return ParseMapping(indent);

        var lineIndex = _index++;
        return ParseInlineValue(content, lineIndex);
    }

    private YamlishMapping ParseMapping(int indent)
    {
        var result = new YamlishMapping();
        while (_index < _lines.Length)
        {
            if (IsBlank(_lines[_index]))
            {
                _index++;
                continue;
            }

            var lineIndent = GetIndent(_index);
            if (lineIndent < indent)
                break;

            if (lineIndent > indent)
                Throw("Unexpected indentation", _index);

            var content = _lines[_index].AsSpan(indent);
            if (IsSequenceLine(content))
                Throw("A sequence item is not valid inside a mapping", _index);

            var separator = FindMappingSeparator(content);
            if (separator <= 0)
                Throw("Expected a mapping entry in the form 'key: value'", _index);

            var key = content[..separator].Trim().ToString();
            if (key.Length is 0)
                Throw("Mapping keys cannot be empty", _index);

            var valueText = content[(separator + 1)..].TrimStart();
            var lineIndex = _index++;
            YamlishNode value;
            if (valueText.IsEmpty)
            {
                value = ParseNestedValue(indent, lineIndex);
            }
            else if (valueText.SequenceEqual("|"))
            {
                value = ParseLiteral(indent);
            }
            else
            {
                value = ParseInlineValue(valueText, lineIndex);
            }

            try
            {
                result.Add(key, value);
            }
            catch (ArgumentException)
            {
                Throw($"Duplicate mapping key '{key}'", lineIndex);
                throw;
            }
        }

        return result;
    }

    private YamlishSequence ParseSequence(int indent)
    {
        var result = new YamlishSequence();
        while (_index < _lines.Length)
        {
            if (IsBlank(_lines[_index]))
            {
                _index++;
                continue;
            }

            var lineIndent = GetIndent(_index);
            if (lineIndent < indent)
                break;

            if (lineIndent > indent)
                Throw("Unexpected indentation", _index);

            var content = _lines[_index].AsSpan(indent);
            if (!IsSequenceLine(content))
                break;

            var valueText = content[1..].TrimStart();
            var lineIndex = _index++;
            if (valueText.IsEmpty)
            {
                result.Add(ParseNestedValue(indent, lineIndex));
            }
            else if (valueText.SequenceEqual("|"))
            {
                result.Add(ParseLiteral(indent));
            }
            else if (FindMappingSeparator(valueText) > 0)
            {
                result.Add(ParseCompactMapping(valueText, indent, lineIndex));
            }
            else
            {
                result.Add(ParseInlineValue(valueText, lineIndex));
            }
        }

        return result;
    }

    private YamlishMapping ParseCompactMapping(ReadOnlySpan<char> firstEntry, int sequenceIndent, int lineIndex)
    {
        var result = new YamlishMapping();
        AddCompactMappingEntry(result, firstEntry, sequenceIndent, lineIndex);

        SkipBlankLines();
        if (_index < _lines.Length && GetIndent(_index) > sequenceIndent)
        {
            var additionalEntries = ParseMapping(GetIndent(_index));
            foreach (var entry in additionalEntries)
            {
                try
                {
                    result.Add(entry.Key, entry.Value);
                }
                catch (ArgumentException)
                {
                    Throw($"Duplicate mapping key '{entry.Key}'", _index);
                }
            }
        }

        return result;
    }

    private void AddCompactMappingEntry(YamlishMapping mapping, ReadOnlySpan<char> content, int parentIndent, int lineIndex)
    {
        var separator = FindMappingSeparator(content);
        var key = content[..separator].Trim().ToString();
        if (key.Length is 0)
            Throw("Mapping keys cannot be empty", lineIndex);

        var valueText = content[(separator + 1)..].TrimStart();
        YamlishNode value;
        if (valueText.IsEmpty)
        {
            value = ParseNestedValue(parentIndent, lineIndex);
        }
        else if (valueText.SequenceEqual("|"))
        {
            value = ParseLiteral(parentIndent);
        }
        else
        {
            value = ParseInlineValue(valueText, lineIndex);
        }

        mapping.Add(key, value);
    }

    private YamlishNode ParseNestedValue(int parentIndent, int parentLine)
    {
        SkipBlankLines();
        if (_index >= _lines.Length || GetIndent(_index) <= parentIndent)
            Throw("Expected an indented value", parentLine);

        return ParseBlock(GetIndent(_index));
    }

    private YamlishScalar ParseLiteral(int parentIndent)
    {
        var start = _index;
        var end = start;
        var contentIndent = int.MaxValue;
        while (end < _lines.Length)
        {
            if (!IsBlank(_lines[end]))
            {
                var indent = GetIndent(end);
                if (indent <= parentIndent)
                    break;

                contentIndent = Math.Min(contentIndent, indent);
            }

            end++;
        }

        if (contentIndent is int.MaxValue)
        {
            _index = end;
            return new YamlishScalar(string.Empty);
        }

        var builder = new StringBuilder();
        for (var i = start; i < end; i++)
        {
            if (i > start)
                builder.Append('\n');

            var line = _lines[i];
            if (line.Length >= contentIndent)
            {
                builder.Append(line.AsSpan(contentIndent));
            }
        }

        _index = end;
        return new YamlishScalar(builder.ToString());
    }

    private YamlishNode ParseInlineValue(ReadOnlySpan<char> value, int lineIndex)
    {
        value = value.TrimEnd();
        if (value.IsEmpty)
            return new YamlishScalar(string.Empty);

        if (value[0] is '[')
            return ParseInlineSequence(value, lineIndex);

        if (value[0] is '"' or '\'')
            return new YamlishScalar(ParseQuotedScalar(value, lineIndex));

        if (value[0] is '>' or '&' or '*' or '!' or '%')
            Throw("Unsupported YAML syntax", lineIndex);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is '#' && (i is 0 || char.IsWhiteSpace(value[i - 1])))
                Throw("Comments are not supported", lineIndex);
        }

        return new YamlishScalar(value.ToString());
    }

    private YamlishSequence ParseInlineSequence(ReadOnlySpan<char> value, int lineIndex)
    {
        if (value[^1] is not ']')
            Throw("Inline sequences must end with ']'", lineIndex);

        var result = new YamlishSequence();
        var content = value[1..^1].Trim();
        while (!content.IsEmpty)
        {
            var separator = FindInlineSeparator(content, lineIndex);
            var item = (separator < 0 ? content : content[..separator]).Trim();
            if (item.IsEmpty)
                Throw("Inline sequence items cannot be empty", lineIndex);

            if (item[0] is '[')
                Throw("Nested inline sequences are not supported", lineIndex);

            result.Add(ParseInlineValue(item, lineIndex));
            if (separator < 0)
                break;

            content = content[(separator + 1)..].Trim();
        }

        return result;
    }

    private static string ParseQuotedScalar(ReadOnlySpan<char> value, int lineIndex)
    {
        var quote = value[0];
        var builder = new StringBuilder();
        for (var i = 1; i < value.Length; i++)
        {
            var character = value[i];
            if (character == quote)
            {
                if (quote is '\'' && i + 1 < value.Length && value[i + 1] is '\'')
                {
                    builder.Append('\'');
                    i++;
                    continue;
                }

                if (!value[(i + 1)..].Trim().IsEmpty)
                    Throw("Unexpected content after quoted scalar", lineIndex);

                return builder.ToString();
            }

            if (quote is '"' && character is '\\')
            {
                if (++i >= value.Length)
                    Throw("Incomplete escape sequence", lineIndex);

                builder.Append(value[i] switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => throw CreateException($"Unsupported escape sequence '\\{value[i]}'", lineIndex),
                });
            }
            else
            {
                builder.Append(character);
            }
        }

        Throw("Unterminated quoted scalar", lineIndex);
        return string.Empty;
    }

    private static int FindMappingSeparator(ReadOnlySpan<char> content)
    {
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] is ':' && (i + 1 == content.Length || char.IsWhiteSpace(content[i + 1])))
                return i;
        }

        return -1;
    }

    private static int FindInlineSeparator(ReadOnlySpan<char> content, int lineIndex)
    {
        var quote = '\0';
        for (var i = 0; i < content.Length; i++)
        {
            var character = content[i];
            if (quote is '\0')
            {
                if (character is '"' or '\'')
                    quote = character;
                else if (character is ',')
                    return i;
                else if (character is ']')
                    Throw("Unexpected ']'", lineIndex);
            }
            else if (character == quote)
            {
                if (quote is '\'' && i + 1 < content.Length && content[i + 1] is '\'')
                    i++;
                else
                    quote = '\0';
            }
            else if (quote is '"' && character is '\\')
            {
                i++;
            }
        }

        if (quote is not '\0')
            Throw("Unterminated quoted scalar", lineIndex);

        return -1;
    }

    private void SkipBlankLines()
    {
        while (_index < _lines.Length && IsBlank(_lines[_index]))
        {
            _index++;
        }
    }

    private int GetIndent(int lineIndex)
    {
        var line = _lines[lineIndex];
        var result = 0;
        while (result < line.Length && line[result] is ' ')
        {
            result++;
        }

        if (result < line.Length && line[result] is '\t')
            Throw("Tabs are not allowed for indentation", lineIndex);

        return result;
    }

    private void ValidateIndent(int lineIndex, int indent)
    {
        if (GetIndent(lineIndex) != indent)
            Throw("Unexpected indentation", lineIndex);
    }

    private static bool IsBlank(string line) => line.AsSpan().Trim().IsEmpty;

    private static bool IsSequenceLine(ReadOnlySpan<char> content) => content.Length > 0 && content[0] is '-' && (content.Length is 1 || char.IsWhiteSpace(content[1]));

    [DoesNotReturn]
    private static void Throw(string message, int lineIndex) => throw CreateException(message, lineIndex);

    private static FormatException CreateException(string message, int lineIndex) => new($"{message} at line {lineIndex + 1}.");
}
