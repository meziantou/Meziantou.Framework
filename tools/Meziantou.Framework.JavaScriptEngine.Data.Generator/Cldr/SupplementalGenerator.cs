using System.Globalization;
using System.Text.Json;

namespace Meziantou.Framework.JavaScriptEngine.Data.Generator.Cldr;

/// <summary>Builds supplemental.bin (read by CldrSupplemental): one data tree per section, each in its own chunk.</summary>
internal static class SupplementalGenerator
{
    public static List<byte[]> Build(CldrJsonSource source)
    {
        var sections = new SortedDictionary<string, TreeTable>(StringComparer.Ordinal)
        {
            ["likelySubtags"] = Read(source, "likelySubtags.json", "likelySubtags"),
            ["aliases"] = SimplifyAliases(Read(source, "aliases.json", "metadata", "alias")),
            ["plurals"] = Read(source, "plurals.json", "plurals-type-cardinal"),
            ["ordinals"] = Read(source, "ordinals.json", "plurals-type-ordinal"),
            ["pluralRanges"] = Read(source, "pluralRanges.json", "plurals"),
            ["currencyData"] = CurrencyData(source),
            ["timeData"] = Read(source, "timeData.json", "timeData"),
            ["weekData"] = Read(source, "weekData.json", "weekData"),
            ["calendarPreferenceData"] = Read(source, "calendarPreferenceData.json", "calendarPreferenceData"),
            ["calendarData"] = Read(source, "calendarData.json", "calendarData"),
            ["numberingSystems"] = Read(source, "numberingSystems.json", "numberingSystems"),
            ["metaZones"] = MetaZones(source),
            ["dayPeriods"] = Read(source, "dayPeriods.json", "dayPeriodRuleSet"),
            ["primaryZones"] = Read(source, "primaryZones.json", "primaryZones"),
            ["bcp47"] = Bcp47(source),
            ["scriptMetadata"] = ScriptMetadata(source),
        };

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in sections.Values)
        {
            section.CollectKeys(keys);
        }

        var treeKeys = new TreeKeys(keys);
        var chunks = new List<byte[]> { Array.Empty<byte>() };
        var header = new BinaryBuffer();
        treeKeys.Write(header);
        header.WriteVarUInt(sections.Count);
        foreach (var (name, section) in sections)
        {
            var writer = new TreeChunkWriter(treeKeys);
            var table = writer.AddTable(section);
            header.WriteString(name);
            header.WriteVarUInt(chunks.Count);
            header.WriteVarUInt(table);
            chunks.Add(writer.ToArray());
        }

        chunks[0] = header.ToArray();
        return chunks;
    }

    private static TreeTable Read(CldrJsonSource source, string fileName, params string[] path)
    {
        using var document = source.Read("cldr-core/supplemental/" + fileName);
        var element = document.RootElement.GetProperty("supplemental");
        foreach (var segment in path)
        {
            element = element.GetProperty(segment);
        }

        return (TreeTable)CldrJsonSource.ToTree(element);
    }

    // { "_reason": "deprecated", "_replacement": "he" } -> "he"
    private static TreeTable SimplifyAliases(TreeTable table)
    {
        var result = new TreeTable();
        foreach (var (key, value) in table.Entries)
        {
            var child = (TreeTable)value;
            result[key] = child.TryGetValue("_replacement", out var replacement) ? replacement : SimplifyAliases(child);
        }

        return result;
    }

    private static TreeTable CurrencyData(CldrJsonSource source)
    {
        var data = Read(source, "currencyData.json", "currencyData");
        var result = new TreeTable();
        result["fractions"] = data["fractions"];
        return result;
    }

    // metazoneInfo/timezone/Europe/Paris: [{ usesMetazone: { _mzone, _from, _to } }] -> zones/Europe/Paris/0 = { mzone, from, to } (seconds since the epoch)
    // metazones: [{ mapZone: { _other, _territory, _type } }] -> mapZones/<metazone>/<territory> = zone
    private static TreeTable MetaZones(CldrJsonSource source)
    {
        using var document = source.Read("cldr-core/supplemental/metaZones.json");
        var root = document.RootElement.GetProperty("supplemental").GetProperty("metaZones");
        var result = new TreeTable();
        var zones = result.GetOrAddTable("zones");
        AddZones(zones, root.GetProperty("metazoneInfo").GetProperty("timezone"));

        var mapZones = result.GetOrAddTable("mapZones");
        foreach (var item in root.GetProperty("metazones").EnumerateArray())
        {
            var mapZone = item.GetProperty("mapZone");
            mapZones.GetOrAddTable(mapZone.GetProperty("_other").GetString()!)[mapZone.GetProperty("_territory").GetString()!] = mapZone.GetProperty("_type").GetString()!;
        }

        return result;

        static void AddZones(TreeTable target, JsonElement element)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object)
                {
                    AddZones(target.GetOrAddTable(property.Name), property.Value);
                    continue;
                }

                var periods = target.GetOrAddTable(property.Name);
                var index = 0;
                foreach (var item in property.Value.EnumerateArray())
                {
                    var uses = item.GetProperty("usesMetazone");
                    var period = new TreeTable();
                    period["mzone"] = uses.GetProperty("_mzone").GetString()!;
                    if (uses.TryGetProperty("_from", out var from))
                    {
                        period["from"] = ToEpochSeconds(from.GetString()!);
                    }

                    if (uses.TryGetProperty("_to", out var to))
                    {
                        period["to"] = ToEpochSeconds(to.GetString()!);
                    }

                    periods[index.ToString(CultureInfo.InvariantCulture)] = period;
                    index++;
                }
            }
        }

        static string ToEpochSeconds(string value)
        {
            var date = DateTimeOffset.ParseExact(value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            return date.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }
    }

    // scriptMetadata/Arab/{ rtl: "YES", ... } -> Arab = "YES" (the "rtl" field: YES, NO or UNKNOWN)
    private static TreeTable ScriptMetadata(CldrJsonSource source)
    {
        using var document = source.Read("cldr-core/scriptMetadata.json");
        var result = new TreeTable();
        foreach (var property in document.RootElement.GetProperty("scriptMetadata").EnumerateObject())
        {
            if (property.Value.TryGetProperty("rtl", out var rtl))
            {
                result[property.Name] = rtl.GetString()!;
            }
        }

        return result;
    }

    // keyword/u/ca/{ _alias, buddhist: { _description }, ethioaa: { _alias } } -> u/ca/{ _alias, buddhist: {}, ethioaa: { _alias } }
    private static TreeTable Bcp47(CldrJsonSource source)
    {
        var result = new TreeTable();
        foreach (var fileName in source.FileNames.Where(name => name.StartsWith("cldr-bcp47/bcp47/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var document = source.Read(fileName);
            if (!document.RootElement.TryGetProperty("keyword", out var keyword))
                continue;

            var tree = (TreeTable)CldrJsonSource.ToTree(keyword);
            RemoveDescriptions(tree);
            CldrJsonSource.Merge(result, tree);
        }

        return result;

        static void RemoveDescriptions(TreeTable table)
        {
            table.Remove("_description");
            table.Remove("_since");
            table.Remove("_valueType");
            foreach (var value in table.Entries.Values)
            {
                if (value is TreeTable child)
                {
                    RemoveDescriptions(child);
                }
            }
        }
    }
}
