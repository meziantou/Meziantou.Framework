using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.GitGraphDiagrams;

/// <summary>The tokens that the Langium grammars of Mermaid share: the strings with escapes and the titles.</summary>
internal static class GitGraphLangium
{
    /// <summary>Reads a string between double or single quotes, whose backslash escapes a character as in Langium.</summary>
    public static string ReadString(Scanner scanner)
    {
        var start = scanner.Position;
        var quote = scanner.Current;
        scanner.Position++;
        var builder = new StringBuilder();
        while (true)
        {
            if (scanner.IsEnd)
                throw scanner.Error(start, "The string is not closed.");

            var c = scanner.Current;
            scanner.Position++;
            if (c == quote)
                return builder.ToString();

            if (c == '\\')
            {
                if (scanner.IsEnd)
                    throw scanner.Error(start, "The string is not closed.");

                var escaped = scanner.Current;
                scanner.Position++;
                builder.Append(escaped switch
                {
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'v' => '\v',
                    '0' => '\0',
                    _ => escaped,
                });
                continue;
            }

            builder.Append(c);
        }
    }

    /// <summary>
    /// Writes a string between double quotes, which <see cref="ReadString"/> reads back. A <c>%</c> after a <c>%</c> is
    /// written <c>\%</c>, as <c>%%</c> starts a directive or a comment anywhere in the text, quoted strings included.
    /// </summary>
    public static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            builder.Append(c switch
            {
                '%' when i > 0 && value[i - 1] is '%' => "\\%",
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\b' => "\\b",
                '\f' => "\\f",
                '\v' => "\\v",
                '\0' => "\\0",
                _ => c.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    /// <summary>Reads a <c>title</c> statement: the rest of the line, its runs of spaces collapsed, as the common grammar of Mermaid.</summary>
    public static bool TryParseTitle(Scanner scanner, ISceneSource diagram)
    {
        if (!scanner.StartsWith("title") || (scanner.Peek(5) is not (' ' or '\t' or '\n' or '\0') && scanner.Position + 5 < scanner.Text.Length))
            return false;

        scanner.Position += 5;
        var text = scanner.ReadRestOfLine();
        var builder = new StringBuilder();
        var space = false;
        foreach (var c in text.Trim())
        {
            if (c is ' ' or '\t')
            {
                space = true;
                continue;
            }

            if (space && builder.Length > 0)
            {
                builder.Append(' ');
            }

            space = false;
            builder.Append(c);
        }

        // The title is written in the YAML front matter, which cannot hold a lone surrogate
        new MermaidContent(diagram).Title = builder.Length is 0 ? null : ReplaceLoneSurrogates(builder.ToString());
        return true;
    }

    /// <summary>Replaces the lone surrogates by U+FFFD, for the texts written in YAML, which cannot hold them.</summary>
    public static string ReplaceLoneSurrogates(string value)
    {
        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder?.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                builder ??= new StringBuilder(value, 0, i, value.Length);
                builder.Append('\uFFFD');
                continue;
            }

            builder?.Append(c);
        }

        return builder?.ToString() ?? value;
    }
}
