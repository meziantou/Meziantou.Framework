using System.Text;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Finds the images a Dockerfile pulls, so a build can be given the credentials of their registries.</summary>
internal static class DockerfileBaseImages
{
    /// <summary>Lists the images of the <c>FROM</c> instructions and of the <c>--from</c> options of <c>COPY</c>, leaving out the stages of the Dockerfile itself.</summary>
    /// <param name="content">The content of the Dockerfile.</param>
    /// <param name="buildArguments">The build arguments, which replace the default values of the <c>ARG</c> instructions that come before the first <c>FROM</c>.</param>
    /// <returns>The image references, in the order they appear.</returns>
    public static List<string> Parse(string content, IEnumerable<KeyValuePair<string, string>> buildArguments)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in buildArguments)
            overrides[name] = value;

        var stages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var images = new List<string>();
        var seenFrom = false;
        foreach (var instruction in ReadInstructions(content))
        {
            var tokens = instruction.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;

            var keyword = tokens[0].ToUpperInvariant();
            if (keyword is "ARG" && !seenFrom)
            {
                // Only the arguments declared before the first FROM can be used in a FROM instruction.
                foreach (var declaration in tokens.Skip(1))
                {
                    var separator = declaration.IndexOf('=', StringComparison.Ordinal);
                    var name = separator < 0 ? declaration : declaration[..separator];
                    if (overrides.TryGetValue(name, out var overrideValue))
                        arguments[name] = overrideValue;
                    else if (separator >= 0)
                        arguments[name] = Unquote(declaration[(separator + 1)..]);
                }
            }
            else if (keyword is "FROM")
            {
                seenFrom = true;
                var index = 1;
                while (index < tokens.Length && tokens[index].StartsWith("--", StringComparison.Ordinal))
                    index++;

                if (index >= tokens.Length)
                    continue;

                AddImage(Expand(tokens[index], arguments));
                if (index + 2 < tokens.Length && string.Equals(tokens[index + 1], "AS", StringComparison.OrdinalIgnoreCase))
                    stages.Add(tokens[index + 2]);
            }
            else if (keyword is "COPY")
            {
                foreach (var token in tokens.Skip(1))
                {
                    if (token.StartsWith("--from=", StringComparison.OrdinalIgnoreCase))
                        AddImage(Expand(token["--from=".Length..], arguments));
                }
            }
        }

        return images;

        void AddImage(string image)
        {
            // A stage of the Dockerfile, or a stage by its index, is not pulled, and neither is an image whose name
            // depends on an argument that has no value.
            if (image.Length is 0 || stages.Contains(image) || image.Contains('$', StringComparison.Ordinal) || int.TryParse(image, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                return;

            if (string.Equals(image, "scratch", StringComparison.OrdinalIgnoreCase))
                return;

            if (!images.Contains(image, StringComparer.Ordinal))
                images.Add(image);
        }
    }

    /// <summary>Joins the lines that end with a backslash, and drops the comments and the empty lines.</summary>
    private static IEnumerable<string> ReadInstructions(string content)
    {
        var current = new StringBuilder();
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#', StringComparison.Ordinal))
                continue;

            if (trimmed.EndsWith('\\', StringComparison.Ordinal))
            {
                current.Append(trimmed.AsSpan(0, trimmed.Length - 1)).Append(' ');
                continue;
            }

            current.Append(trimmed);
            if (current.Length > 0)
                yield return current.ToString();

            current.Clear();
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static string Expand(string value, Dictionary<string, string> arguments)
    {
        if (!value.Contains('$', StringComparison.Ordinal))
            return value;

        var result = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '$' || i + 1 >= value.Length)
            {
                result.Append(value[i]);
                continue;
            }

            int nameStart;
            int nameEnd;
            int next;
            if (value[i + 1] == '{')
            {
                var close = value.IndexOf('}', i + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    result.Append(value[i]);
                    continue;
                }

                nameStart = i + 2;
                nameEnd = close;
                next = close + 1;
            }
            else
            {
                nameStart = i + 1;
                nameEnd = nameStart;
                while (nameEnd < value.Length && (char.IsAsciiLetterOrDigit(value[nameEnd]) || value[nameEnd] == '_'))
                    nameEnd++;

                next = nameEnd;
            }

            // '${name:-default}' and the other modifiers are not supported: the reference stays, and the image is skipped.
            var name = value[nameStart..nameEnd];
            if (name.Length > 0 && arguments.TryGetValue(name, out var argumentValue))
            {
                result.Append(argumentValue);
                i = next - 1;
            }
            else
            {
                result.Append(value[i]);
            }
        }

        return result.ToString();
    }

    private static string Unquote(string value)
        => value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'') ? value[1..^1] : value;
}
