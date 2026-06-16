namespace Meziantou.Framework.Yamlish.Internals;

internal static class YamlishWriter
{
    public static void Write(TextWriter writer, YamlishNode node, char indentCharacter, int indentSize, string newLine)
    {
        using var buffer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = newLine };
        WriteNode(buffer, node, indent: 0, indentCharacter, indentSize);

        var content = buffer.ToString();
        if (content.EndsWith(newLine, StringComparison.Ordinal))
            content = content[..^newLine.Length];

        writer.Write(content);
    }

    private static void WriteNode(TextWriter writer, YamlishNode node, int indent, char indentCharacter, int indentSize)
    {
        switch (node)
        {
            case YamlishScalar scalar:
                WriteScalar(writer, scalar, indent, indentCharacter, indentSize);
                break;

            case YamlishMapping mapping:
                WriteMapping(writer, mapping, indent, indentCharacter, indentSize);
                break;

            case YamlishSequence sequence:
                WriteSequence(writer, sequence, indent, indentCharacter, indentSize);
                break;
        }
    }

    private static void WriteMapping(TextWriter writer, YamlishMapping mapping, int indent, char indentCharacter, int indentSize)
    {
        foreach (var entry in mapping)
        {
            WriteIndent(writer, indent, indentCharacter);
            writer.Write(entry.Key);
            writer.Write(':');
            if (entry.Value is YamlishScalar scalar)
            {
                writer.Write(' ');
                WriteScalar(writer, scalar, indent, indentCharacter, indentSize);
            }
            else if (entry.Value is YamlishSequence { Count: 0, Style: not YamlishSequenceStyle.Block })
            {
                writer.WriteLine(" []");
            }
            else if (entry.Value is YamlishSequence { Style: YamlishSequenceStyle.Flow } sequence)
            {
                writer.Write(' ');
                WriteFlowSequence(writer, sequence);
                writer.WriteLine();
            }
            else
            {
                writer.WriteLine();
                WriteNode(writer, entry.Value, indent + indentSize, indentCharacter, indentSize);
            }
        }
    }

    private static void WriteSequence(TextWriter writer, YamlishSequence sequence, int indent, char indentCharacter, int indentSize)
    {
        if (sequence.Count is 0)
        {
            WriteIndent(writer, indent, indentCharacter);
            writer.WriteLine("[]");
            return;
        }

        if (sequence.Style is YamlishSequenceStyle.Flow && sequence.Any(item => item is not YamlishScalar))
            throw new InvalidOperationException("Flow sequence style is only supported for scalar sequences.");

        if (sequence.Style is not YamlishSequenceStyle.Block && sequence.All(item => item is YamlishScalar))
        {
            WriteIndent(writer, indent, indentCharacter);
            WriteFlowSequence(writer, sequence);
            writer.WriteLine();
            return;
        }

        foreach (var item in sequence)
        {
            WriteIndent(writer, indent, indentCharacter);
            writer.Write('-');
            if (item is YamlishScalar scalar)
            {
                writer.Write(' ');
                WriteScalar(writer, scalar, indent, indentCharacter, indentSize);
            }
            else
            {
                writer.WriteLine();
                WriteNode(writer, item, indent + indentSize, indentCharacter, indentSize);
            }
        }
    }

    private static void WriteFlowSequence(TextWriter writer, YamlishSequence sequence)
    {
        if (sequence.Any(item => item is not YamlishScalar))
            throw new InvalidOperationException("Flow sequence style is only supported for scalar sequences.");

        writer.Write('[');
        for (var i = 0; i < sequence.Count; i++)
        {
            if (i > 0)
                writer.Write(", ");

            WriteInlineScalar(writer, (YamlishScalar)sequence[i]);
        }

        writer.Write(']');
    }

    private static void WriteScalar(TextWriter writer, YamlishScalar scalar, int indent, char indentCharacter, int indentSize)
    {
        var value = scalar.Value;
        if (scalar.Style is YamlishScalarStyle.Literal or YamlishScalarStyle.Folded)
        {
            WriteBlockScalar(writer, scalar, indent, indentCharacter, indentSize);
            return;
        }

        if (scalar.Style is YamlishScalarStyle.Auto && value.Contains('\n', StringComparison.Ordinal) && !value.EndsWith("\n", StringComparison.Ordinal))
        {
            writer.WriteLine("|-");
            var lines = value.Split('\n');
            var count = lines[^1].Length is 0 ? lines.Length - 1 : lines.Length;
            for (var i = 0; i < count; i++)
            {
                WriteIndent(writer, indent + indentSize, indentCharacter);
                writer.WriteLine(lines[i]);
            }

            return;
        }

        WriteInlineScalar(writer, scalar);
        writer.WriteLine();
    }

    private static void WriteBlockScalar(TextWriter writer, YamlishScalar scalar, int indent, char indentCharacter, int indentSize)
    {
        writer.Write(scalar.Style is YamlishScalarStyle.Folded ? '>' : '|');
        writer.Write(scalar.Chomping switch
        {
            YamlishScalarChomping.Clip => string.Empty,
            YamlishScalarChomping.Strip => "-",
            YamlishScalarChomping.Keep => "+",
            _ => throw new ArgumentOutOfRangeException(nameof(scalar)),
        });
        writer.WriteLine();

        var lines = scalar.Value.Split('\n');
        var count = scalar.Value.EndsWith("\n", StringComparison.Ordinal) ? lines.Length - 1 : lines.Length;
        for (var i = 0; i < count; i++)
        {
            WriteIndent(writer, indent + indentSize, indentCharacter);
            writer.WriteLine(lines[i]);
        }
    }

    private static void WriteInlineScalar(TextWriter writer, YamlishScalar scalar)
    {
        if (scalar.Style is YamlishScalarStyle.Plain || scalar.Style is YamlishScalarStyle.Auto && !RequiresQuotes(scalar.Value))
        {
            writer.Write(scalar.Value);
            return;
        }

        if (scalar.Style is YamlishScalarStyle.SingleQuoted)
        {
            writer.Write('\'');
            writer.Write(scalar.Value.Replace("'", "''", StringComparison.Ordinal));
            writer.Write('\'');
            return;
        }

        WriteDoubleQuotedScalar(writer, scalar.Value);
    }

    private static void WriteDoubleQuotedScalar(TextWriter writer, string value)
    {
        writer.Write('"');
        foreach (var character in value)
        {
            writer.Write(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => character.ToString(CultureInfo.InvariantCulture),
            });
        }

        writer.Write('"');
    }

    private static bool RequiresQuotes(string value)
    {
        if (value.Length is 0 || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
            return true;

        if (value[0] is '[' or '"' or '\'' or '|' or '>' or '&' or '*' or '!' or '%' or '-' or '#')
            return true;

        return value.Contains(": ", StringComparison.Ordinal) || value.Contains(" #", StringComparison.Ordinal) || value.Contains(',', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal) || value.Contains('\t', StringComparison.Ordinal);
    }

    private static void WriteIndent(TextWriter writer, int indent, char indentCharacter)
    {
        for (var i = 0; i < indent; i++)
        {
            writer.Write(indentCharacter);
        }
    }
}
