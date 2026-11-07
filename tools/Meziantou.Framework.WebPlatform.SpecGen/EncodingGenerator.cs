using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SpecGen;

/// <summary>
/// Generates the encoding registry (names and labels) and the index tables from the WHATWG Encoding Standard data.
/// </summary>
internal static class EncodingGenerator
{
    public static void Generate(GeneratorContext context)
    {
        using var encodingsDocument = JsonDocument.Parse(File.ReadAllText(context.SpecData("encoding", "encodings.json")));
        using var indexesDocument = JsonDocument.Parse(File.ReadAllText(context.SpecData("encoding", "indexes.json")));

        var encodings = new List<(string Name, string[] Labels)>();
        foreach (var heading in encodingsDocument.RootElement.EnumerateArray())
        {
            foreach (var encoding in heading.GetProperty("encodings").EnumerateArray())
            {
                encodings.Add((
                    encoding.GetProperty("name").GetString()!,
                    encoding.GetProperty("labels").EnumerateArray().Select(l => l.GetString()!).ToArray()));
            }
        }

        GenerateRegistry(context, encodings);
        GenerateIndexes(context, indexesDocument.RootElement, encodings);
    }

    public static string ToIdentifier(string name)
    {
        var sb = new StringBuilder();
        var upper = true;
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                upper = false;
            }
            else
            {
                upper = true;
            }
        }

        // Keep digits readable: "ISO-8859-2" -> "Iso8859_2"
        return sb.ToString();
    }

    private static string EncodingIdentifier(string name) => name switch
    {
        "UTF-8" => "Utf8",
        "UTF-16BE" => "Utf16BE",
        "UTF-16LE" => "Utf16LE",
        "GBK" => "Gbk",
        "gb18030" => "Gb18030",
        "Big5" => "Big5",
        "EUC-JP" => "EucJP",
        "ISO-2022-JP" => "Iso2022JP",
        "Shift_JIS" => "ShiftJis",
        "EUC-KR" => "EucKR",
        "IBM866" => "Ibm866",
        "KOI8-R" => "Koi8R",
        "KOI8-U" => "Koi8U",
        "ISO-8859-8-I" => "Iso8859_8I",
        _ when name.StartsWith("ISO-8859-", StringComparison.Ordinal) => "Iso8859_" + name["ISO-8859-".Length..],
        _ when name.StartsWith("windows-", StringComparison.Ordinal) => "Windows" + name["windows-".Length..],
        _ => ToIdentifier(name),
    };

    private static void GenerateRegistry(GeneratorContext context, List<(string Name, string[] Labels)> encodings)
    {
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/encoding/encodings.json");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Encodings;").Line();

        writer.Line("/// <summary>Encodings defined by the WHATWG Encoding Standard.</summary>");
        using (writer.Block("internal enum WebEncodingId : byte"))
        {
            foreach (var (name, _) in encodings)
            {
                writer.Line($"/// <summary>{name}</summary>");
                writer.Line(EncodingIdentifier(name) + ",");
            }
        }

        writer.Line();
        using (writer.Block("internal static partial class WebEncodingRegistry"))
        {
            writer.Line($"public const int Count = {encodings.Count};").Line();

            writer.Line("/// <summary>Canonical names, indexed by <see cref=\"WebEncodingId\"/>.</summary>");
            using (writer.Block("private static readonly string[] Names ="))
            {
                foreach (var (name, _) in encodings)
                {
                    writer.Line(CodeWriter.Literal(name) + ",");
                }
            }

            writer.Line(";").Line();
            writer.Line("/// <summary>Labels (ASCII lowercase) and the encoding they map to.</summary>");
            using (writer.Block("private static readonly (string Label, WebEncodingId Encoding)[] Labels ="))
            {
                foreach (var (name, labels) in encodings)
                {
                    foreach (var label in labels)
                    {
                        writer.Line($"({CodeWriter.Literal(label)}, WebEncodingId.{EncodingIdentifier(name)}),");
                    }
                }
            }

            writer.Line(";").Line();
            writer.Line($"public const int MaxLabelLength = {encodings.SelectMany(e => e.Labels).Max(l => l.Length)};");
        }

        context.WriteFile(context.Source("Encodings", "WebEncodingRegistry.g.cs"), writer.ToString());
    }

    private static void GenerateIndexes(GeneratorContext context, JsonElement indexes, List<(string Name, string[] Labels)> encodings)
    {
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/encoding/indexes.json");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Encodings;").Line();
        writer.Line("/// <summary>Index tables. A value of 0 means the pointer is not in the index (code point U+0000 is never mapped).</summary>");
        using (writer.Block("internal static class EncodingIndexes"))
        {
            // Single-byte encodings: one table of 128 entries per encoding, for bytes 0x80 to 0xFF.
            var singleByte = encodings.Where(e => indexes.TryGetProperty(e.Name.ToLowerInvariant(), out var value) && value.GetArrayLength() == 128 && e.Name != "ISO-8859-8-I").ToList();
            writer.Line("/// <summary>Returns the offset in <see cref=\"SingleByte\"/> of the index of a single-byte encoding, or -1.</summary>");
            using (writer.Block("public static int GetSingleByteIndexOffset(WebEncodingId encoding) => encoding switch"))
            {
                for (var i = 0; i < singleByte.Count; i++)
                {
                    writer.Line($"WebEncodingId.{EncodingIdentifier(singleByte[i].Name)} => {(i * 128).ToString(CultureInfo.InvariantCulture)},");
                }

                // ISO-8859-8-I uses the same index as ISO-8859-8
                writer.Line($"WebEncodingId.Iso8859_8I => {(singleByte.FindIndex(e => e.Name == "ISO-8859-8") * 128).ToString(CultureInfo.InvariantCulture)},");
                writer.Line("_ => -1,");
            }

            writer.Line(";").Line();
            WriteTable(writer, "SingleByte", "ushort", singleByte.SelectMany(e => ReadIndex(indexes, e.Name.ToLowerInvariant())));

            WriteTable(writer, "Gb18030", "ushort", ReadIndex(indexes, "gb18030"));
            WriteTable(writer, "Big5", "int", ReadIndex(indexes, "big5"));
            WriteTable(writer, "Jis0208", "ushort", ReadIndex(indexes, "jis0208"));
            WriteTable(writer, "Jis0212", "ushort", ReadIndex(indexes, "jis0212"));
            WriteTable(writer, "EucKR", "ushort", ReadIndex(indexes, "euc-kr"));
            WriteTable(writer, "Iso2022JPKatakana", "ushort", ReadIndex(indexes, "iso-2022-jp-katakana"));

            var ranges = indexes.GetProperty("gb18030-ranges").EnumerateArray().Select(r => (Pointer: r[0].GetInt32(), CodePoint: r[1].GetInt32())).ToList();
            WriteTable(writer, "Gb18030RangesPointers", "int", ranges.Select(r => r.Pointer));
            WriteTable(writer, "Gb18030RangesCodePoints", "int", ranges.Select(r => r.CodePoint));
        }

        context.WriteFile(context.Source("Encodings", "EncodingIndexes.g.cs"), writer.ToString());
    }

    private static IEnumerable<int> ReadIndex(JsonElement indexes, string name)
    {
        foreach (var value in indexes.GetProperty(name).EnumerateArray())
        {
            yield return value.ValueKind == JsonValueKind.Null ? 0 : value.GetInt32();
        }
    }

    private static void WriteTable(CodeWriter writer, string name, string type, IEnumerable<int> values)
    {
        writer.Line($"public static ReadOnlySpan<{type}> {name} =>");
        writer.Line("[");
        writer.Numbers(values, perLine: 20);
        writer.Line("];").Line();
    }
}
