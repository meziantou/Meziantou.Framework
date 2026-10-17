using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.DependencyScanning.Fuzz;

/// <summary>
/// Extracts the files the scanner tests write, <c>AddFile(path, content)</c>, from their source code. The path is a string
/// literal, a constant, or the <c>[InlineData]</c> values of the test; the content is a string literal or a constant
/// declared in the test. Anything else (interpolated strings, computed bytes) is skipped.
/// </summary>
internal static partial class TestFiles
{
    public static IEnumerable<(string Path, string Content)> Read(string source)
    {
        source = source.ReplaceLineEndings("\n");
        foreach (var test in TestStartRegex().Split(source))
        {
            var inlineData = InlineDataRegex().Matches(test).Select(match => match.Groups["value"].Value).ToArray();
            var variables = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in VariableRegex().Matches(test))
            {
                if (TryReadString(test, match.Index + match.Length, out var value))
                {
                    variables[match.Groups["name"].Value] = value;
                }
            }

            foreach (Match match in AddFileRegex().Matches(test))
            {
                var position = match.Index + match.Length;
                if (!TryReadPath(test, ref position, variables, inlineData, out var paths))
                    continue;

                position = SkipTrivia(test, position);
                string? content;
                if (TryReadString(test, position, out var literal))
                {
                    content = literal;
                }
                else
                {
                    var identifier = IdentifierRegex().Match(test, position);
                    content = identifier.Success && identifier.Index == position ? variables.GetValueOrDefault(identifier.Value) : null;
                }

                if (content is null)
                    continue;

                foreach (var path in paths)
                {
                    yield return (path, content);
                }
            }
        }
    }

    private static bool TryReadPath(string test, ref int position, Dictionary<string, string> variables, string[] inlineData, out string[] paths)
    {
        paths = [];
        if (test[position] == '"')
        {
            if (!TryReadString(test, position, out var path))
                return false;

            var end = test.IndexOf(',', position + path.Length + 1, StringComparison.Ordinal);
            if (end < 0)
                return false;

            paths = [path];
            position = end + 1;
            return true;
        }

        var identifier = IdentifierRegex().Match(test, position);
        if (!identifier.Success || identifier.Index != position)
            return false;

        position += identifier.Length;
        while (position < test.Length && char.IsWhiteSpace(test[position]))
        {
            position++;
        }

        if (position >= test.Length || test[position] != ',')
            return false;

        position++;
        paths = variables.TryGetValue(identifier.Value, out var value) ? [value] : inlineData;
        return true;
    }

    /// <summary>Reads a raw string literal (<c>"""</c>, without interpolation) or a regular string literal starting at <paramref name="position"/>.</summary>
    private static bool TryReadString(string text, int position, out string value)
    {
        value = "";
        position = SkipTrivia(text, position);

        var match = RawStringRegex().Match(text, position);
        if (match.Success && match.Index == position)
        {
            var indentation = match.Groups["indent"].Value;
            var lines = match.Groups["body"].Value.Split('\n');
            value = string.Join('\n', lines.Select(line => line.StartsWith(indentation, StringComparison.Ordinal) ? line[indentation.Length..] : line.TrimStart()));
            return true;
        }

        if (position >= text.Length || text[position] != '"')
            return false;

        var builder = new StringBuilder();
        for (var i = position + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                value = builder.ToString();
                return true;
            }

            if (c == '\n')
                return false;

            if (c != '\\' || i + 1 >= text.Length)
            {
                builder.Append(c);
                continue;
            }

            i++;
            switch (text[i])
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
                case 'u' when i + 4 < text.Length && int.TryParse(text.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code):
                    builder.Append((char)code);
                    i += 4;
                    break;
                default:
                    builder.Append(text[i]);
                    break;
            }
        }

        return false;
    }

    /// <summary>Skips the whitespace and the comments, such as <c>/*lang=json,strict*/</c>.</summary>
    private static int SkipTrivia(string text, int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            else if (text.AsSpan(position).StartsWith("/*", StringComparison.Ordinal) && text.IndexOf("*/", position, StringComparison.Ordinal) is var end and >= 0)
            {
                position = end + 2;
            }
            else
            {
                break;
            }
        }

        return position;
    }

    [GeneratedRegex(@"\n    \[(?:Fact|Theory)\b", RegexOptions.None, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex TestStartRegex();

    [GeneratedRegex("""\[InlineData\("(?<value>[^"\\]*)"[,)]""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex InlineDataRegex();

    [GeneratedRegex(@"(?:const\s+string|string|var)\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?=""|\s)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex VariableRegex();

    [GeneratedRegex(@"(?<![\w.])AddFile\(\s*", RegexOptions.None, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex AddFileRegex();

    [GeneratedRegex(@"[A-Za-z_]\w*", RegexOptions.None, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex IdentifierRegex();

    // The closing quotes are alone on their line, and their indentation is removed from every line
    [GeneratedRegex("(?<q>\"{3,})[ \\t]*\\n(?<body>[\\s\\S]*?)\\n(?<indent>[ \\t]*)\\k<q>(?!\")", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex RawStringRegex();
}
