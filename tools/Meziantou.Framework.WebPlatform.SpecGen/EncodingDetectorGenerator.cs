using System.Globalization;
using System.Text.RegularExpressions;

namespace SpecGen;

/// <summary>
/// Generates the tables of the encoding detector from the data of chardetng, the detector of Firefox
/// (tests/Meziantou.Framework.WebPlatform.SpecData/chardetng/src/data.rs, https://github.com/hsivonen/chardetng, Apache-2.0 or MIT license): the arrays of
/// <c>DETECTOR_DATA</c> (the most frequent ideographs and syllables, the classes of the bytes of each single-byte encoding
/// and the scores of the pairs of classes of each family of languages) and the numbers of classes of each family.
/// </summary>
internal static partial class EncodingDetectorGenerator
{
    public static void Generate(GeneratorContext context)
    {
        var source = File.ReadAllText(context.SpecData("chardetng", "src", "data.rs"));
        source = CommentRegex().Replace(source, "");
        var dataStart = source.IndexOf("pub static DETECTOR_DATA", StringComparison.Ordinal);
        var dataEnd = source.IndexOf("\n};", dataStart, StringComparison.Ordinal);
        if (dataStart < 0 || dataEnd < 0)
            throw new InvalidOperationException("Cannot find DETECTOR_DATA in data.rs");

        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/chardetng/src/data.rs");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Encodings;").Line();
        writer.Line("/// <summary>");
        writer.Line("/// The data of the encoding detector, from chardetng (https://github.com/hsivonen/chardetng, copyright Mozilla Foundation,");
        writer.Line("/// Apache-2.0 or MIT license). See <see cref=\"LegacyEncodingDetector\"/>.");
        writer.Line("/// </summary>");
        using (writer.Block("internal static class EncodingDetectorData"))
        {
            var count = 0;
            foreach (Match match in ClassCountRegex().Matches(source))
            {
                writer.Line($"public const int {PascalCase(match.Groups["name"].Value)}ClassCount = {match.Groups["value"].Value};");
                count++;
            }

            if (count != 22)
                throw new InvalidOperationException($"Unexpected number of class counts in data.rs: {count}");

            count = 0;
            foreach (Match match in ArrayRegex().Matches(source[dataStart..dataEnd]))
            {
                var values = NumberRegex().Matches(match.Groups["values"].Value).Select(m => ParseNumber(m.Value)).ToList();
                var isWide = values.Exists(v => v > byte.MaxValue);
                writer.Line();
                writer.Line($"public static ReadOnlySpan<{(isWide ? "ushort" : "byte")}> {PascalCase(match.Groups["name"].Value)} =>");
                writer.Line("[");
                writer.Numbers(values, perLine: 32);
                writer.Line("];");
                count++;
            }

            if (count != 37)
                throw new InvalidOperationException($"Unexpected number of arrays in data.rs: {count}");
        }

        context.WriteFile(context.Source("Encodings", "EncodingDetectorData.g.cs"), writer.ToString());
    }

    private static int ParseNumber(string value)
        => value.StartsWith("0x", StringComparison.Ordinal)
            ? int.Parse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
            : int.Parse(value, CultureInfo.InvariantCulture);

    // "iso_8859_2" is "Iso8859_2", as in WebEncodingId
    private static string PascalCase(string name)
    {
        var result = new System.Text.StringBuilder();
        foreach (var part in name.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            if (result.Length > 0 && char.IsAsciiDigit(result[^1]) && char.IsAsciiDigit(part[0]))
            {
                result.Append('_');
            }

            result.Append(char.ToUpperInvariant(part[0])).Append(part.AsSpan(1).ToString().ToLowerInvariant());
        }

        return result.ToString();
    }

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"^const (?<name>[A-Z]+_(?:NON_)?ASCII): usize = (?<value>\d+);", RegexOptions.Multiline)]
    private static partial Regex ClassCountRegex();

    [GeneratedRegex(@"^    (?<name>\w+): \[(?<values>[^\]]*)\],", RegexOptions.Multiline)]
    private static partial Regex ArrayRegex();

    [GeneratedRegex(@"0x[0-9A-Fa-f]+|\d+")]
    private static partial Regex NumberRegex();
}
