using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Meziantou.Framework.JavaScriptEngine.Data.Generator.Cldr;

/// <summary>The files of the cldr-json release archive (cldr-x.y.z-json-full.zip), read in memory.</summary>
internal sealed class CldrJsonSource
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Package, string FileName)>> _mainFiles = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> KeyPool = new(StringComparer.Ordinal);

    public CldrJsonSource(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.EndsWith(".json", StringComparison.Ordinal))
                continue;

            // Only the packages used by the engine
            var package = entry.FullName.Split('/')[0];
            if (package is not ("cldr-core" or "cldr-bcp47" or "cldr-numbers-full" or "cldr-dates-full" or "cldr-localenames-full" or "cldr-misc-full" or "cldr-units-full") && !package.StartsWith("cldr-cal-", StringComparison.Ordinal))
                continue;

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            _files.Add(entry.FullName, memory.ToArray());

            var parts = entry.FullName.Split('/');
            if (parts.Length == 4 && parts[1] == "main")
            {
                if (!_mainFiles.TryGetValue(parts[2], out var list))
                {
                    list = [];
                    _mainFiles.Add(parts[2], list);
                }

                list.Add((parts[0], parts[3]));
            }
        }

        foreach (var list in _mainFiles.Values)
        {
            list.Sort((a, b) => string.CompareOrdinal(a.Package + "/" + a.FileName, b.Package + "/" + b.FileName));
        }
    }

    public IEnumerable<string> FileNames => _files.Keys;

    public bool Exists(string path) => _files.ContainsKey(path);

    public JsonDocument Read(string path) => JsonDocument.Parse(_files.TryGetValue(path, out var content) ? content : throw new FileNotFoundException("Missing file in the cldr-json archive: " + path));

    /// <summary>Gets the locales of the main directory of a package (e.g. "cldr-numbers-full").</summary>
    public IEnumerable<string> GetMainLocales(string package)
    {
        var prefix = package + "/main/";
        return _files.Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Select(name => name[prefix.Length..].Split('/')[0]).Distinct(StringComparer.Ordinal);
    }

    /// <summary>Gets the files of a locale in the main directory of the packages, as (package, file name).</summary>
    public IReadOnlyList<(string Package, string FileName)> GetMainFiles(string locale) => _mainFiles.TryGetValue(locale, out var list) ? list : [];

    /// <summary>Converts a JSON value to a tree. Arrays of scalars become a string of space-separated values; arrays of objects become tables keyed by the index.</summary>
    public static object ToTree(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var table = new TreeTable();
                foreach (var property in element.EnumerateObject())
                {
                    table[Intern(property.Name)] = ToTree(property.Value);
                }

                return table;

            case JsonValueKind.String:
                return element.GetString()!;

            case JsonValueKind.Number:
                return element.GetRawText();

            case JsonValueKind.True:
                return "true";

            case JsonValueKind.False:
                return "false";

            case JsonValueKind.Array:
                if (element.EnumerateArray().All(item => item.ValueKind is JsonValueKind.String or JsonValueKind.Number))
                {
                    var items = element.EnumerateArray().Select(item => item.ValueKind is JsonValueKind.String ? item.GetString()! : item.GetRawText()).ToList();
                    if (items.Any(item => item.Contains(' ', StringComparison.Ordinal)))
                        throw new InvalidDataException("An array item contains a space: " + element.GetRawText());

                    return string.Join(' ', items);
                }

                var array = new TreeTable();
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    array[index.ToString(CultureInfo.InvariantCulture)] = ToTree(item);
                    index++;
                }

                return array;

            default:
                throw new InvalidDataException("Unexpected JSON value: " + element.GetRawText());
        }
    }

    private static string Intern(string value)
    {
        lock (KeyPool)
        {
            if (KeyPool.TryGetValue(value, out var result))
                return result;

            KeyPool.Add(value, value);
            return value;
        }
    }

    /// <summary>Merges <paramref name="source"/> into <paramref name="target"/>.</summary>
    public static void Merge(TreeTable target, TreeTable source)
    {
        foreach (var (key, value) in source.Entries)
        {
            if (value is TreeTable table && target.TryGetValue(key, out var existing) && existing is TreeTable existingTable)
            {
                Merge(existingTable, table);
            }
            else
            {
                target[key] = value;
            }
        }
    }
}
