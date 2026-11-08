using System.Text.RegularExpressions;

namespace Meziantou.Framework.Tds.Protocol;

/// <summary>Recognizes the SQL statements that <c>SqlBulkCopy</c> sends around a bulk load.</summary>
internal static partial class TdsBulkCopyCommandParser
{
    /// <summary>
    /// Recognizes the query that reads the columns of the destination table. SqlClient 7 and later declare the table in
    /// <c>OBJECT_ID('…')</c> and also read the column aliases of graph tables; earlier versions select from the table with
    /// <c>SET FMTONLY ON</c>.
    /// </summary>
    public static bool TryParseMetadataQuery(string commandText, [NotNullWhen(true)] out IReadOnlyList<string>? tableName, out bool readsColumnAliases)
    {
        tableName = null;
        readsColumnAliases = false;
        if (!commandText.StartsWith("select @@trancount;", StringComparison.OrdinalIgnoreCase) || !commandText.Contains("SET FMTONLY ON", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? name = null;
        if (ObjectIdRegex().Match(commandText) is { Success: true } objectIdMatch)
        {
            name = objectIdMatch.Groups["name"].Value.Replace("''", "'", StringComparison.Ordinal);
            readsColumnAliases = commandText.Contains("#Column_Aliases", StringComparison.Ordinal);
        }
        else if (FmtOnlySelectRegex().Match(commandText) is { Success: true } selectMatch)
        {
            name = selectMatch.Groups["name"].Value;
        }

        if (name is null)
        {
            return false;
        }

        var position = 0;
        tableName = TryReadMultipartName(name, ref position);
        return tableName is not null && position == name.Length;
    }

    /// <summary>Recognizes <c>insert bulk &lt;table&gt; (&lt;columns&gt;) [with (&lt;options&gt;)]</c>.</summary>
    public static bool TryParseInsertBulk(string commandText, [NotNullWhen(true)] out IReadOnlyList<string>? tableName, out IReadOnlyList<string> options)
    {
        tableName = null;
        options = [];

        const string Prefix = "insert bulk ";
        if (!commandText.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var position = Prefix.Length;
        var name = TryReadMultipartName(commandText, ref position);
        if (name is null)
        {
            return false;
        }

        SkipWhitespace(commandText, ref position);
        if (!TrySkipParenthesizedList(commandText, ref position, out _))
        {
            return false;
        }

        SkipWhitespace(commandText, ref position);
        if (position < commandText.Length)
        {
            if (string.Compare(commandText, position, "with", 0, 4, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return false;
            }

            position += 4;
            SkipWhitespace(commandText, ref position);
            if (!TrySkipParenthesizedList(commandText, ref position, out var items))
            {
                return false;
            }

            options = items;
            SkipWhitespace(commandText, ref position);
            if (position != commandText.Length)
            {
                return false;
            }
        }

        tableName = name;
        return true;
    }

    /// <summary>Reads a name such as <c>[db].[dbo].[Orders]</c> or <c>dbo.Orders</c>, and returns its unquoted parts.</summary>
    private static List<string>? TryReadMultipartName(string text, ref int position)
    {
        var parts = new List<string>();
        while (true)
        {
            if (position >= text.Length)
            {
                return null;
            }

            var quote = text[position] switch
            {
                '[' => ']',
                '"' => '"',
                _ => '\0',
            };

            if (quote != '\0')
            {
                // A quoted part doubles its closing quote to escape it: [a]]b] is a]b
                var builder = new StringBuilder();
                position++;
                while (true)
                {
                    if (position >= text.Length)
                    {
                        return null;
                    }

                    if (text[position] == quote)
                    {
                        if (position + 1 < text.Length && text[position + 1] == quote)
                        {
                            builder.Append(quote);
                            position += 2;
                            continue;
                        }

                        position++;
                        break;
                    }

                    builder.Append(text[position++]);
                }

                parts.Add(builder.ToString());
            }
            else
            {
                var start = position;
                while (position < text.Length && text[position] is not ('.' or '(' or '[' or '"') && !char.IsWhiteSpace(text[position]))
                {
                    position++;
                }

                // An empty part is the default schema of a name such as db..Orders
                parts.Add(text[start..position]);
            }

            if (position < text.Length && text[position] == '.')
            {
                position++;
                continue;
            }

            return parts.Count <= 4 && parts[^1].Length > 0 ? parts : null;
        }
    }

    /// <summary>Skips <c>( … )</c>, and returns its top-level comma-separated items.</summary>
    private static bool TrySkipParenthesizedList(string text, ref int position, out List<string> items)
    {
        items = [];
        if (position >= text.Length || text[position] != '(')
        {
            return false;
        }

        position++;
        var depth = 0;
        var itemStart = position;
        while (position < text.Length)
        {
            var c = text[position];
            switch (c)
            {
                case '[' or '"' or '\'':
                    // Quoted names and literals can hold parentheses and commas
                    var quote = c == '[' ? ']' : c;
                    position++;
                    while (position < text.Length)
                    {
                        if (text[position] == quote)
                        {
                            if (position + 1 < text.Length && text[position + 1] == quote)
                            {
                                position += 2;
                                continue;
                            }

                            break;
                        }

                        position++;
                    }

                    break;

                case '(':
                    depth++;
                    break;

                case ')' when depth > 0:
                    depth--;
                    break;

                case ')':
                    AddItem(text, itemStart, position, items);
                    position++;
                    return true;

                case ',' when depth == 0:
                    AddItem(text, itemStart, position, items);
                    itemStart = position + 1;
                    break;
            }

            position++;
        }

        return false;

        static void AddItem(string text, int start, int end, List<string> items)
        {
            var item = text[start..end].Trim();
            if (item.Length > 0)
            {
                items.Add(item);
            }
        }
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }

    [GeneratedRegex(@"OBJECT_ID\('(?<name>(?:[^']|'')+)'\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ObjectIdRegex();

    [GeneratedRegex(@"SET FMTONLY ON\s+select \* from (?<name>.+?)\s+SET FMTONLY OFF", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FmtOnlySelectRegex();
}
