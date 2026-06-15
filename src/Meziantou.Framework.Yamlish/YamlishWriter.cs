namespace Meziantou.Framework.Yamlish;

internal static class YamlishWriter
{
    public static void Write(TextWriter writer, YamlishNode node, int indentSize)
    {
        using var buffer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = writer.NewLine };
        WriteNode(buffer, node, indent: 0, indentSize);

        var content = buffer.ToString();
        if (content.EndsWith(writer.NewLine, StringComparison.Ordinal))
            content = content[..^writer.NewLine.Length];

        writer.Write(content);
    }

    private static void WriteNode(TextWriter writer, YamlishNode node, int indent, int indentSize)
    {
        switch (node)
        {
            case YamlishScalar scalar:
                WriteScalar(writer, scalar.Value, indent, indentSize);
                break;

            case YamlishMapping mapping:
                WriteMapping(writer, mapping, indent, indentSize);
                break;

            case YamlishSequence sequence:
                WriteSequence(writer, sequence, indent, indentSize);
                break;
        }
    }

    private static void WriteMapping(TextWriter writer, YamlishMapping mapping, int indent, int indentSize)
    {
        foreach (var entry in mapping)
        {
            WriteIndent(writer, indent);
            writer.Write(entry.Key);
            writer.Write(':');
            if (entry.Value is YamlishScalar scalar)
            {
                writer.Write(' ');
                WriteScalar(writer, scalar.Value, indent, indentSize);
            }
            else if (entry.Value is YamlishSequence { Count: 0 })
            {
                writer.WriteLine(" []");
            }
            else
            {
                writer.WriteLine();
                WriteNode(writer, entry.Value, indent + indentSize, indentSize);
            }
        }
    }

    private static void WriteSequence(TextWriter writer, YamlishSequence sequence, int indent, int indentSize)
    {
        if (sequence.Count is 0)
        {
            WriteIndent(writer, indent);
            writer.WriteLine("[]");
            return;
        }

        if (sequence.All(item => item is YamlishScalar))
        {
            WriteIndent(writer, indent);
            writer.Write('[');
            for (var i = 0; i < sequence.Count; i++)
            {
                if (i > 0)
                    writer.Write(", ");

                WriteInlineScalar(writer, ((YamlishScalar)sequence[i]).Value);
            }

            writer.WriteLine(']');
            return;
        }

        foreach (var item in sequence)
        {
            WriteIndent(writer, indent);
            writer.Write('-');
            if (item is YamlishScalar scalar)
            {
                writer.Write(' ');
                WriteScalar(writer, scalar.Value, indent, indentSize);
            }
            else
            {
                writer.WriteLine();
                WriteNode(writer, item, indent + indentSize, indentSize);
            }
        }
    }

    private static void WriteScalar(TextWriter writer, string value, int indent, int indentSize)
    {
        if (value.Contains('\n', StringComparison.Ordinal) && !value.EndsWith("\n", StringComparison.Ordinal))
        {
            writer.WriteLine("|-");
            var lines = value.Split('\n');
            var count = lines[^1].Length is 0 ? lines.Length - 1 : lines.Length;
            for (var i = 0; i < count; i++)
            {
                WriteIndent(writer, indent + indentSize);
                writer.WriteLine(lines[i]);
            }

            return;
        }

        WriteInlineScalar(writer, value);
        writer.WriteLine();
    }

    private static void WriteInlineScalar(TextWriter writer, string value)
    {
        if (!RequiresQuotes(value))
        {
            writer.Write(value);
            return;
        }

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

    private static void WriteIndent(TextWriter writer, int indent)
    {
        for (var i = 0; i < indent; i++)
        {
            writer.Write(' ');
        }
    }
}
