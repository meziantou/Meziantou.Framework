using System.Globalization;

namespace SpecGen;

/// <summary>
/// Reads the files of the Unicode Character Database (tests/Meziantou.Framework.WebPlatform.SpecData/unicode) that the data of
/// the complex-script shapers is generated from. The properties of the code points themselves come from the
/// Meziantou.Framework.Unicode package.
/// </summary>
internal static class UnicodeDataFile
{
    internal static IEnumerable<(int Start, int End, string[] Fields)> ReadDataFile(string path)
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            var hash = rawLine.IndexOf('#', StringComparison.Ordinal);
            var line = hash >= 0 ? rawLine[..hash] : rawLine;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = line.Split(';').Select(f => f.Trim()).ToArray();
            var (start, end) = ParseRange(fields[0]);
            yield return (start, end, fields);
        }
    }

    private static (int Start, int End) ParseRange(string value)
    {
        var separator = value.IndexOf("..", StringComparison.Ordinal);
        if (separator < 0)
        {
            var codePoint = int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return (codePoint, codePoint);
        }

        return (int.Parse(value[..separator], NumberStyles.HexNumber, CultureInfo.InvariantCulture), int.Parse(value[(separator + 2)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    internal static string Version(string path)
    {
        foreach (var line in File.ReadLines(path).Take(10))
        {
            var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+\.\d+\.\d+)");
            if (match.Success)
                return match.Groups[1].Value;
        }

        return "unknown";
    }

    internal static void WriteArray<T>(CodeWriter writer, string name, string type, IEnumerable<T> values)
        where T : IFormattable
    {
        writer.Line($"public static ReadOnlySpan<{type}> {name} =>");
        writer.Line("[");
        writer.Numbers(values, perLine: 16);
        writer.Line("];").Line();
    }
}
