using System.Text;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>Reads and writes the values of the CSS <c>font-family</c> property: a list of family names separated by commas.</summary>
internal static class CssFontFamily
{
    private static readonly string[] Keywords = ["serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "math", "emoji", "fangsong", "inherit", "initial", "unset", "default", "revert", "revert-layer"];

    /// <summary>Gets the family names of a list, unquoted, in order. The empty names are skipped.</summary>
    public static List<string> Parse(string value)
    {
        var names = new List<string>();
        var name = new StringBuilder();
        var quote = '\0';
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else if (c == '\\' && i + 1 < value.Length)
                {
                    name.Append(value[++i]);
                }
                else
                {
                    name.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                quoted = true;
            }
            else if (c == ',')
            {
                Add();
            }
            else
            {
                name.Append(c);
            }
        }

        Add();
        return names;

        void Add()
        {
            // Unquoted names are identifiers separated by white space: the spaces between them collapse to one
            var text = quoted ? name.ToString().Trim() : string.Join(' ', name.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (text.Length > 0)
            {
                names.Add(text);
            }

            name.Clear();
            quoted = false;
        }
    }

    /// <summary>Writes a family name, quoted unless it is a sequence of identifiers made of ASCII letters, digits, hyphens and underscores.</summary>
    public static string Format(string name)
    {
        if (IsIdentifierSequence(name))
            return name;

        var builder = new StringBuilder(name.Length + 2).Append('"');
        foreach (var c in name)
        {
            if (c is '"' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.Append('"').ToString();
    }

    /// <summary>Writes a list of family names.</summary>
    public static string Format(IEnumerable<string> names) => string.Join(", ", names.Select(Format));

    private static bool IsIdentifierSequence(string name)
    {
        if (name.Length is 0)
            return false;

        foreach (var word in name.Split(' '))
        {
            if (word.Length is 0 || char.IsAsciiDigit(word[0]) || (word[0] == '-' && (word.Length is 1 || char.IsAsciiDigit(word[1]) || word[1] == '-')))
                return false;

            foreach (var c in word)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
                    return false;
            }
        }

        // The generic families and the keywords of the property are names only when they are quoted
        return !Keywords.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
