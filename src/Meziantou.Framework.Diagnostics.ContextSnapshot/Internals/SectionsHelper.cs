using System.Text.RegularExpressions;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

internal static partial class SectionsHelper
{
    internal static readonly char[] Separators = ['\r', '\n'];

    public static Dictionary<string, string> ParseSection(string? content, char separator)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var list = content?.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (list is not null)
        {
            foreach (var line in list)
            {
                if (line.Contains(separator, StringComparison.Ordinal))
                {
                    var lineParts = line.Split(separator);
                    if (lineParts.Length >= 2)
                    {
                        values[lineParts[0].Trim()] = lineParts[1].Trim();
                    }
                }
            }
        }

        return values;
    }

    public static Dictionary<string, string>[] ParseSections(string? content, char separator)
    {
        // wmic doubles the carriage return character due to a bug.
        // Therefore, the * quantifier should be used to workaround it.
        return SectionRegex().Split(content ?? "").Select(s => ParseSection(s, separator))
                .Where(s => s.Count > 0)
                .ToArray();
    }

    [GeneratedRegex("(\r*\n){2,}", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: -1)]
    private static partial Regex SectionRegex();
}
