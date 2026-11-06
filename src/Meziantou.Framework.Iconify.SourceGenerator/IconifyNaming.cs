using System.Text;

namespace Meziantou.Framework.IconifySourceGeneration;

internal static class IconifyNaming
{
    /// <summary>
    /// Converts an Iconify name (<c>account-circle-outline</c>) to a C# identifier (<c>AccountCircleOutline</c>).
    /// The characters that cannot be part of an identifier are removed, and the next character is upper-cased.
    /// </summary>
    public static string ToIdentifier(string name)
    {
        var sb = new StringBuilder(name.Length + 1);
        var upperNext = true;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = false;
            }
            else
            {
                upperNext = true;
            }
        }

        if (sb.Length == 0 || char.IsDigit(sb[0]))
        {
            sb.Insert(0, '_');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Computes a unique identifier for each name. When several names have the same identifier, the shortest name, then the first in ordinal order,
    /// gets it, and the others get a numeric suffix (<c>_123</c>, <c>_1232</c>).
    /// </summary>
    public static Dictionary<string, string> CreateUniqueIdentifiers(IEnumerable<string> names, IEnumerable<string> reservedIdentifiers)
    {
        var taken = new HashSet<string>(reservedIdentifiers, StringComparer.Ordinal);
        var groups = names
            .Distinct(StringComparer.Ordinal)
            .GroupBy(ToIdentifier, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (Identifier: group.Key, Names: group.OrderBy(name => name.Length).ThenBy(name => name, StringComparer.Ordinal).ToList()))
            .ToList();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicts = new List<(string Identifier, string Name)>();
        foreach (var group in groups)
        {
            var start = 0;
            if (taken.Add(group.Identifier))
            {
                result.Add(group.Names[0], group.Identifier);
                start = 1;
            }

            for (var i = start; i < group.Names.Count; i++)
            {
                conflicts.Add((group.Identifier, group.Names[i]));
            }
        }

        foreach (var (identifier, name) in conflicts)
        {
            var suffix = 2;
            string candidate;
            do
            {
                candidate = identifier + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                suffix++;
            }
            while (!taken.Add(candidate));

            result.Add(name, candidate);
        }

        return result;
    }
}
