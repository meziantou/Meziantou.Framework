#pragma warning disable MA0004 // Use Task.ConfigureAwait
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meziantou.Framework;
using Meziantou.Framework.JavaScriptEngine.Data.Generator;
using Meziantou.Framework.JavaScriptEngine.Data.Generator.Cldr;
using Meziantou.Framework.JavaScriptEngine.TimeZones;

// Generates the data embedded in Meziantou.Framework.JavaScriptEngine, at the versions pinned in data-versions.json:
// - tzdb: TimeZones/tzdb.bin from the IANA time zone database and the CLDR mapping of the Windows time zone identifiers
// - cldr: Intl/Data/*.bin from cldr-json (locale and supplemental data) and the CLDR XML release (collation)
// Exit code: 0 when the files are up to date, 1 when they were updated, any other value on failure.
var update = false;
var verify = false;
var steps = new HashSet<string>(StringComparer.Ordinal);
foreach (var arg in args)
{
    switch (arg)
    {
        case "--update":
            update = true;
            break;
        case "--verify":
            verify = true;
            break;
        case "tzdb" or "cldr":
            steps.Add(arg);
            break;
        default:
            await Console.Error.WriteLineAsync("Unexpected argument: " + arg);
            await Console.Error.WriteLineAsync("Usage: Meziantou.Framework.JavaScriptEngine.Data.Generator [--update] [--verify] [tzdb] [cldr]");
            return 2;
    }
}

if (steps.Count == 0)
{
    steps.Add("tzdb");
    steps.Add("cldr");
}

if (!FullPath.CurrentDirectory().TryFindGitRepositoryRoot(out var root))
    throw new InvalidOperationException("Cannot find git root from " + FullPath.CurrentDirectory());

var pinsPath = root / "tools" / "Meziantou.Framework.JavaScriptEngine.Data.Generator" / "data-versions.json";
var downloadCache = root / "artifacts" / "javascriptengine-data";

var pins = JsonNode.Parse(await File.ReadAllTextAsync(pinsPath))!.AsObject();
var updated = false;
if (update)
{
    var pinsUpdated = false;
    if (steps.Contains("tzdb"))
    {
        pinsUpdated |= await UpdatePinAsync(pins, "tzdata", await GetLatestTzdataVersionAsync(), GetTzdataUrl);
    }

    var latestCldr = await GetLatestCldrVersionAsync();
    pinsUpdated |= await UpdatePinAsync(pins, "cldr", latestCldr, GetWindowsZonesUrl);
    if (steps.Contains("cldr"))
    {
        pinsUpdated |= await UpdatePinAsync(pins, "cldr-json", await GetLatestCldrJsonVersionAsync(), CldrGenerator.GetCldrJsonUrl);
        pinsUpdated |= await UpdatePinAsync(pins, "cldr-common", CldrGenerator.GetCldrCommonVersion(latestCldr), CldrGenerator.GetCldrCommonUrl);
        var common = await Downloads.DownloadCachedAsync(CldrGenerator.GetCldrCommonUrl((string)pins["cldr-common"]!["version"]!), (string)pins["cldr-common"]!["sha256"]!, downloadCache);
        pinsUpdated |= await UpdatePinAsync(pins, "ucd", CldrGenerator.GetUcaVersion(common), CldrGenerator.GetPropListUrl);
    }

    if (pinsUpdated)
    {
        await File.WriteAllTextAsync(pinsPath, pins.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true }).ReplaceLineEndings("\n") + "\n");
        updated = true;
    }
}

if (steps.Contains("tzdb"))
{
    var result = await RunTzdbAsync();
    if (result is not (0 or 1))
        return result;

    updated |= result == 1;
}

if (steps.Contains("cldr"))
{
    var result = await CldrGenerator.RunAsync(root, pins, downloadCache);
    if (result is not (0 or 1))
        return result;

    updated |= result == 1;
}

return updated ? 1 : 0;

async Task<int> RunTzdbAsync()
{
    var outputDirectory = root / "src" / "Meziantou.Framework.JavaScriptEngine" / "TimeZones";
    var binPath = outputDirectory / "tzdb.bin";
    var versionFilePath = outputDirectory / "TimeZoneDatabase.g.cs";
    var changed = false;
    var tzdataVersion = (string)pins["tzdata"]!["version"]!;
    var cldrVersion = (string)pins["cldr"]!["version"]!;
    Console.WriteLine($"tzdata {tzdataVersion}, CLDR {cldrVersion}");

    var archive = await Downloads.DownloadAsync(GetTzdataUrl(tzdataVersion), (string)pins["tzdata"]!["sha256"]!);
    var files = ExtractTarGz(archive);
    var windowsZones = await Downloads.DownloadAsync(GetWindowsZonesUrl(cldrVersion), (string)pins["cldr"]!["sha256"]!);

    var source = TzdbSource.Parse(name => files.TryGetValue(name, out var content) ? content : throw new InvalidOperationException("Missing file in the tzdata archive: " + name));
    var compiler = new ZoneCompiler(source);
    var zones = source.Zones.Values.Select(compiler.Compile).ToList();
    var windowsIds = ParseWindowsZones(windowsZones, source);
    var payload = TzdbWriter.Write(tzdataVersion, zones, source.Links, windowsIds);
    Console.WriteLine($"{zones.Count} zones, {source.Links.Count} links, {zones.Sum(zone => zone.Transitions.Count)} transitions, {windowsIds.Count} Windows identifiers");

    if (!IsSamePayload(binPath, payload))
    {
        using var memory = new MemoryStream();
        using (var brotli = new BrotliStream(memory, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(payload);
        }

        await File.WriteAllBytesAsync(binPath, memory.ToArray());
        Console.WriteLine($"Updated {binPath} ({memory.Length} bytes)");
        changed = true;
    }

    var versionFile = $$"""
        // <auto-generated/>
        // Generated by tools/Meziantou.Framework.JavaScriptEngine.Data.Generator from data-versions.json
        namespace Meziantou.Framework.JavaScriptEngine.TimeZones;

        internal sealed partial class TimeZoneDatabase
        {
            /// <summary>The release of the IANA time zone database embedded in tzdb.bin.</summary>
            public const string EmbeddedTzdataVersion = "{{tzdataVersion}}";

            /// <summary>The release of CLDR whose windowsZones.xml is embedded in tzdb.bin.</summary>
            public const string EmbeddedCldrVersion = "{{cldrVersion}}";
        }

        """;
    if (!File.Exists(versionFilePath) || (await File.ReadAllTextAsync(versionFilePath)).ReplaceLineEndings("\n") != versionFile)
    {
        await File.WriteAllTextAsync(versionFilePath, versionFile);
        changed = true;
    }

    if (verify && !Verify(source, payload))
        return 3;

    return changed ? 1 : 0;
}

static string GetTzdataUrl(string version) => $"https://data.iana.org/time-zones/releases/tzdata{version}.tar.gz";

static string GetWindowsZonesUrl(string version) => $"https://raw.githubusercontent.com/unicode-org/cldr/{version}/common/supplemental/windowsZones.xml";

static async Task<bool> UpdatePinAsync(JsonObject pins, string name, string latestVersion, Func<string, string> getUrl)
{
    var pin = pins[name]!.AsObject();
    if ((string)pin["version"]! == latestVersion)
        return false;

    Console.WriteLine($"Moving {name} from {pin["version"]} to {latestVersion}");
    var content = await Downloads.DownloadAsync(getUrl(latestVersion), expectedSha256: null);
    pin["version"] = latestVersion;
    pin["sha256"] = Convert.ToHexStringLower(SHA256.HashData(content));
    return true;
}

static async Task<string> GetLatestTzdataVersionAsync()
{
    var listing = await SharedHttpClient.InstanceWithAutoRedirect.GetStringAsync("https://data.iana.org/time-zones/releases/");
    return Regex.Matches(listing, @"tzdata(?<version>[0-9]{4}[a-z]+)\.tar\.gz", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10))
        .Select(match => match.Groups["version"].Value)
        .OrderBy(version => version[..4], StringComparer.Ordinal)
        .ThenBy(version => version.Length - 4)
        .ThenBy(version => version, StringComparer.Ordinal)
        .Last();
}

static async Task<string> GetLatestCldrJsonVersionAsync()
{
    var json = await SharedHttpClient.InstanceWithAutoRedirect.GetStringAsync("https://api.github.com/repos/unicode-org/cldr-json/releases/latest");
    return (string)JsonNode.Parse(json)!["tag_name"]!;
}

static async Task<string> GetLatestCldrVersionAsync()
{
    var json = await SharedHttpClient.InstanceWithAutoRedirect.GetStringAsync("https://api.github.com/repos/unicode-org/cldr/releases/latest");
    return (string)JsonNode.Parse(json)!["tag_name"]!;
}

static Dictionary<string, string> ExtractTarGz(byte[] archive)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    using var gzip = new GZipStream(new MemoryStream(archive), CompressionMode.Decompress);
    using var reader = new TarReader(gzip);
    while (reader.GetNextEntry() is { } entry)
    {
        if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
            continue;

        using var streamReader = new StreamReader(entry.DataStream, Encoding.UTF8);
        result[entry.Name.TrimStart('.', '/')] = streamReader.ReadToEnd();
    }

    return result;
}

static List<(string WindowsId, string IanaId)> ParseWindowsZones(byte[] content, TzdbSource source)
{
    var result = new List<(string, string)>();
    var document = XDocument.Load(new MemoryStream(content));
    foreach (var mapZone in document.Descendants("mapZone"))
    {
        if ((string?)mapZone.Attribute("territory") != "001")
            continue;

        var windowsId = (string)mapZone.Attribute("other")!;
        var ianaId = (string)mapZone.Attribute("type")!;
        if (!source.Zones.ContainsKey(ianaId) && !source.Links.ContainsKey(ianaId))
            throw new InvalidDataException($"The Windows time zone '{windowsId}' maps to an unknown zone: {ianaId}");

        result.Add((windowsId, ianaId));
    }

    result.Sort((a, b) => StringComparer.Ordinal.Compare(a.Item1, b.Item1));
    return result;
}

static bool IsSamePayload(FullPath path, byte[] payload)
{
    if (!File.Exists(path))
        return false;

    using var stream = File.OpenRead(path);
    using var brotli = new BrotliStream(stream, CompressionMode.Decompress);
    using var memory = new MemoryStream();
    try
    {
        brotli.CopyTo(memory);
    }
    catch (InvalidDataException)
    {
        return false;
    }

    return memory.ToArray().AsSpan().SequenceEqual(payload);
}

// Compiles the same source with the zic of the system and compares the transitions of every zone and link until 2037
// (zic -b fat writes the transitions until 2037 explicitly)
static bool Verify(TzdbSource source, byte[] payload)
{
    const long End = 2_145_916_800; // 2038-01-01T00:00:00Z
    var temp = FullPath.GetTempPath() / ("tzdb-verify-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        var input = temp / "main.zi";
        File.WriteAllText(input, source.GetZicInput());
        var output = temp / "out";
        using (var process = Process.Start(new ProcessStartInfo("zic", ["-b", "fat", "-d", output, input]) { UseShellExecute = false })!)
        {
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("zic failed with exit code " + process.ExitCode);
        }

        var database = TimeZoneDatabase.Load(payload);
        var errors = 0;
        foreach (var name in database.AvailableIdentifiers)
        {
            var (zicTransitions, zicInitial) = TzifReader.Read(File.ReadAllBytes(output / name));
            var expected = Normalize(zicInitial, zicTransitions.Where(transition => transition.Time < End));

            var zone = database.GetTimeZone(name)!;
            var actualInitial = ToCompiledType(zone.GetLocalTimeType(long.MinValue / 2));
            var actualTransitions = zone.GetTransitions(long.MinValue / 2, End - 1).Select(transition => (transition.Time, ToCompiledType(transition.Type)));
            var actual = Normalize(actualInitial, actualTransitions);
            if (!expected.SequenceEqual(actual))
            {
                errors++;
                Console.WriteLine($"{name}: differs from zic");
                var index = 0;
                while (index < Math.Min(expected.Count, actual.Count) && expected[index] == actual[index])
                {
                    index++;
                }

                Console.WriteLine($"  zic:  {string.Join(", ", expected.Skip(index).Take(3))}");
                Console.WriteLine($"  ours: {string.Join(", ", actual.Skip(index).Take(3))}");
            }
        }

        Console.WriteLine(errors == 0 ? $"Verified {database.AvailableIdentifiers.Count} identifiers against zic" : $"{errors} identifiers differ from zic");
        return errors == 0;
    }
    finally
    {
        Directory.Delete(temp, recursive: true);
    }

    static CompiledType ToCompiledType(TimeZoneLocalTimeType type) => new(type.OffsetSeconds, type.IsDaylightSavingTime, type.Abbreviation);

    static List<(long Time, CompiledType Type)> Normalize(CompiledType initial, IEnumerable<(long Time, CompiledType Type)> transitions)
    {
        var result = new List<(long, CompiledType)> { (long.MinValue, initial) };
        var current = initial;
        foreach (var (time, type) in transitions)
        {
            if (type == current)
                continue;

            result.Add((time, type));
            current = type;
        }

        return result;
    }
}
