using System.Globalization;

namespace Meziantou.Framework.DnsFuzz;

/// <summary>Extracts the string literals of a C# source file (regular, verbatim and raw), to use the inputs of the unit tests as seeds.</summary>
internal static class CSharpStringLiterals
{
    /// <summary>A string literal: its value, and the positions of its first and past its last character in the source.</summary>
    public sealed record Literal(string Value, int Start, int End);

    public static List<Literal> Extract(string source)
    {
        var literals = new List<Literal>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c is '/' && i + 1 < source.Length && source[i + 1] is '/')
            {
                i = source.AsSpan(i).IndexOf('\n') is var end and >= 0 ? i + end : source.Length;
            }
            else if (c is '/' && i + 1 < source.Length && source[i + 1] is '*')
            {
                i = source.IndexOf("*/", i + 2, StringComparison.Ordinal) is var end and >= 0 ? end + 2 : source.Length;
            }
            else if (c is '\'')
            {
                // A character literal, which can hold a quote
                i++;
                while (i < source.Length && source[i] is not '\'')
                {
                    i += source[i] is '\\' ? 2 : 1;
                }

                i++;
            }
            else if (c is '"')
            {
                var start = i;
                var verbatim = i > 0 && source[i - 1] is '@' || (i > 1 && source[i - 1] is '$' && source[i - 2] is '@');
                var quotes = 0;
                while (i + quotes < source.Length && source[i + quotes] is '"')
                {
                    quotes++;
                }

                if (quotes >= 3)
                {
                    var delimiter = new string('"', quotes);
                    var bodyStart = i + quotes;
                    var bodyEnd = source.IndexOf(delimiter, bodyStart, StringComparison.Ordinal);
                    if (bodyEnd < 0)
                        break;

                    literals.Add(new Literal(GetRawValue(source[bodyStart..bodyEnd]), start, bodyEnd + quotes));
                    i = bodyEnd + quotes;
                }
                else if (quotes is 2 && !verbatim)
                {
                    literals.Add(new Literal("", start, i + 2));
                    i += 2;
                }
                else
                {
                    var (value, end) = verbatim ? ReadVerbatim(source, i + 1) : ReadRegular(source, i + 1);
                    literals.Add(new Literal(value, start, end));
                    i = end;
                }
            }
            else
            {
                i++;
            }
        }

        return literals;
    }

    private static string GetRawValue(string body)
    {
        if (!body.Contains('\n', StringComparison.Ordinal))
            return body;

        // A multi-line raw string: the lines between the delimiters, without the indentation of the closing delimiter
        var lines = body.ReplaceLineEndings("\n").Split('\n');
        var indentation = lines[^1];
        return string.Join('\n', lines[1..^1].Select(line => line.StartsWith(indentation, StringComparison.Ordinal) ? line[indentation.Length..] : line.TrimStart()));
    }

    private static (string Value, int End) ReadVerbatim(string source, int index)
    {
        var builder = new StringBuilder();
        while (index < source.Length)
        {
            if (source[index] is '"')
            {
                if (index + 1 < source.Length && source[index + 1] is '"')
                {
                    builder.Append('"');
                    index += 2;
                    continue;
                }

                return (builder.ToString(), index + 1);
            }

            builder.Append(source[index++]);
        }

        return (builder.ToString(), index);
    }

    private static (string Value, int End) ReadRegular(string source, int index)
    {
        var builder = new StringBuilder();
        while (index < source.Length && source[index] is not '"' and not '\n')
        {
            var c = source[index++];
            if (c is not '\\' || index >= source.Length)
            {
                builder.Append(c);
                continue;
            }

            var escape = source[index++];
            switch (escape)
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                case 'u' when index + 4 <= source.Length && int.TryParse(source.AsSpan(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code):
                    builder.Append((char)code);
                    index += 4;
                    break;
                default:
                    builder.Append(escape);
                    break;
            }
        }

        return (builder.ToString(), index + 1);
    }
}
