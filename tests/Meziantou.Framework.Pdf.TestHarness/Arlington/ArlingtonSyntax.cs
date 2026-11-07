using System.Text;

namespace Meziantou.Framework.Pdf.TestHarness.Arlington;

/// <summary>The syntax of the cells of the TSV files: alternatives separated by <c>;</c>, lists in brackets separated by <c>,</c>, and predicates <c>fn:Name(...)</c> whose arguments contain commas.</summary>
internal static class ArlingtonSyntax
{
    /// <summary>Splits a cell into its alternatives, separated by <c>;</c> outside parentheses and brackets.</summary>
    public static List<string> SplitAlternatives(string cell) => Split(cell, ';');

    /// <summary>Gets the value of a column for each type: a cell with one value per type (<c>[a];[b]</c>), or one value for every type.</summary>
    public static List<string> SplitPerType(string cell, int typeCount)
    {
        var parts = SplitAlternatives(cell);
        if (parts.Count == typeCount)
            return parts;

        return [.. Enumerable.Repeat(cell, typeCount)];
    }

    /// <summary>Splits a list in brackets (<c>[a,b,fn:F(c,d)]</c>) into its items.</summary>
    public static IEnumerable<string> SplitList(string cell)
    {
        cell = cell.Trim();
        if (cell.Length >= 2 && cell[0] == '[' && cell[^1] == ']')
        {
            cell = cell[1..^1];
        }

        if (cell.Length == 0)
            return [];

        return Split(cell, ',').Select(s => s.Trim());
    }

    /// <summary>Removes the version predicates around a value: <c>fn:SinceVersion(1.5,stream)</c> is <c>stream</c>.</summary>
    public static string Unwrap(string value)
    {
        value = value.Trim();
        while (value.StartsWith("fn:", StringComparison.Ordinal) && value.EndsWith(')', StringComparison.Ordinal))
        {
            var open = value.IndexOf('(', StringComparison.Ordinal);
            if (open < 0)
                break;

            var function = value[3..open];
            if (function is not ("SinceVersion" or "Deprecated" or "BeforeVersion" or "IsPDFVersion" or "Extension"))
                break;

            var arguments = Split(value[(open + 1)..^1], ',');
            value = arguments[^1].Trim();
        }

        return value;
    }

    private static List<string> Split(string text, char separator)
    {
        var result = new List<string>();
        var depth = 0;
        var current = new StringBuilder();
        foreach (var c in text)
        {
            if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth--;
            }

            if (c == separator && depth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result;
    }
}
