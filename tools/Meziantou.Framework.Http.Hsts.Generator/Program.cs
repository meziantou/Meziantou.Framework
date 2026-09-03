#pragma warning disable CA1812 // Avoid uninstantiated internal classes
#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
#pragma warning disable CA1849 // Call async methods when in an async method
#pragma warning disable MA0042 // Do not use blocking calls in an async method
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework;
using Meziantou.Framework.Versioning;

// A preload entry that disappears is a silent security regression for every consumer, so a run that would drop
// a large share of the list fails instead of shipping. Tuned to absorb ordinary churn only.
const double MinimumEntryRatio = 0.95;

if (!FullPath.CurrentDirectory().TryFindGitRepositoryRoot(out var root))
    throw new InvalidOperationException("Cannot find git root from " + FullPath.CurrentDirectory());

var outputPath = root / "src" / "Meziantou.Framework.Http.Hsts";
var outputFilePath = outputPath / "HstsPreloadList.g.cs";
var manifestPath = outputPath / "preload-hosts.txt";
var csprojPath = outputPath / "Meziantou.Framework.Http.Hsts.csproj";

var (entries, fileUrl, commitSha, commitDate) = await LoadEntries();

// The generated code indexes the resources by label count, so every count from 1 to the deepest name needs a
// slot even if nothing lands in it
var maxSegments = entries.Max(e => e.SegmentCount);
var buckets = new List<Data>[maxSegments];
for (var i = 0; i < maxSegments; i++)
{
    buckets[i] = [];
}

foreach (var entry in entries)
{
    buckets[entry.SegmentCount - 1].Add(entry);
}

foreach (var bucket in buckets)
{
    bucket.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
}

// The manifest is the only reviewable form of this data: the .bin resources are opaque in a pull request diff,
// so the added and removed hosts would otherwise be visible nowhere.
var manifest = BuildManifest();
CheckEntryCountAgainstTheCommittedManifest();

var preloadFiles = BuildPreloadData();
var result = BuildGeneratedCode();

// Everything is produced before anything is written, so a failure cannot leave the generated file and the
// resources it indexes out of sync. The resources are rewritten before the stale ones are removed, so there
// is no moment where a resource the generated file loads is missing.
foreach (var (name, content) in preloadFiles)
{
    File.WriteAllBytes(outputPath / name, content);
}

foreach (var file in Directory.GetFiles(outputPath, "preload_*.bin"))
{
    if (!preloadFiles.ContainsKey(Path.GetFileName(file)))
    {
        File.Delete(file);
    }
}

var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
var changed = false;
changed |= WriteIfChanged(manifestPath, manifest);
changed |= WriteIfChanged(outputFilePath, result);

if (changed)
{
    Console.WriteLine($"The files have been updated ({entries.Count} entries)");

    var doc = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
    var versionNode = doc.Descendants().First(e => e.Name.LocalName == "Version");
    var version = SemanticVersion.Parse(versionNode.Value);
    versionNode.Value = version.NextPatchVersion().ToString();

    var xws = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false, Encoding = encoding };
    using var writer = XmlWriter.Create(csprojPath, xws);
    doc.Save(writer);

    // The exit code is the result, not a status: 1 means the files were updated and 0 means they were already
    // up to date. Any other code is a failure and the workspace must not be committed. The workflow runs
    // `dotnet build` first so that a build failure, which also exits 1, cannot be read as "files updated".
    return 1;
}

return 0;

bool WriteIfChanged(FullPath path, string content)
{
    if (File.Exists(path) && File.ReadAllText(path).ReplaceLineEndings("\n") == content)
        return false;

    File.WriteAllText(path, content, encoding);
    return true;
}

string BuildManifest()
{
    var sb = new StringBuilder();
    sb.Append("# Chromium HSTS preload hosts compiled into this package. Generated; do not edit.\n");
    sb.Append("# One host per line, sorted. A trailing tab and '+' marks include_subdomains.\n");
    sb.Append($"# Source: {fileUrl}\n");
    foreach (var bucket in buckets)
    {
        foreach (var entry in bucket)
        {
            sb.Append(entry.Name);
            if (entry.IncludeSubdomains)
            {
                sb.Append("\t+");
            }

            sb.Append('\n');
        }
    }

    return sb.ToString();
}

void CheckEntryCountAgainstTheCommittedManifest()
{
    if (!File.Exists(manifestPath))
        return;

    var previousCount = File.ReadLines(manifestPath).Count(line => line.Length > 0 && line[0] != '#');
    if (previousCount == 0)
        return;

    var minimum = (int)(previousCount * MinimumEntryRatio);
    if (entries.Count < minimum)
    {
        throw new InvalidOperationException(
            $"The upstream list yielded {entries.Count} entries but the committed manifest has {previousCount}. " +
            $"Fewer than {minimum} means the upstream format probably changed and the filtering silently dropped entries. " +
            "Losing a preload entry is a security regression, so this must be reviewed by hand.");
    }
}

Dictionary<string, byte[]> BuildPreloadData()
{
    var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    for (var segmentCount = 1; segmentCount <= maxSegments; segmentCount++)
    {
        var bucket = buckets[segmentCount - 1];
        if (bucket.Count == 0)
            continue;

        // The reader binary-searches the names in place, so the layout is: the entry count, one length byte
        // per name, the concatenated names, then the include_subdomains bits.
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize))
        using (var writer = new BinaryWriter(gz))
        {
            writer.Write(bucket.Count);
            foreach (var entry in bucket)
            {
                writer.Write(checked((byte)entry.Name.Length));
            }

            foreach (var entry in bucket)
            {
                writer.Write(Encoding.ASCII.GetBytes(entry.Name));
            }

            var bitmap = new byte[(bucket.Count + 7) / 8];
            for (var i = 0; i < bucket.Count; i++)
            {
                if (bucket[i].IncludeSubdomains)
                {
                    bitmap[i >> 3] |= (byte)(1 << (i & 7));
                }
            }

            writer.Write(bitmap);
        }

        files.Add(GetResourceName(segmentCount), ms.ToArray());
    }

    return files;
}

string BuildGeneratedCode()
{
    var sb = new StringBuilder();
    for (var segmentCount = 1; segmentCount <= maxSegments; segmentCount++)
    {
        var count = buckets[segmentCount - 1].Count;
        var resource = count == 0 ? "null" : $"\"{GetResourceName(segmentCount)}\"";
        sb.Append($"        ({resource}, {count.ToString(CultureInfo.InvariantCulture)}),\n");
    }

    return $$"""
        // <auto-generated />
        #nullable enable

        namespace Meziantou.Framework.Http;

        partial class HstsPreloadList
        {
            // HSTS preload data source: {{fileUrl}}
            // Commit date: {{commitDate.ToString("O", CultureInfo.InvariantCulture)}}
            // Entries: {{entries.Count.ToString(CultureInfo.InvariantCulture)}}
            // The index is the label count minus one; see preload-hosts.txt for the host names themselves.
            private static (string? ResourceName, int EntryCount)[] GetResources() =>
            [
        {{sb.ToString().TrimEnd('\n')}}
            ];
        }
        """.ReplaceLineEndings("\n") + "\n";
}

static string GetResourceName(int segmentCount) => $"preload_{segmentCount.ToString(CultureInfo.InvariantCulture)}.bin";

static async Task<(List<Data> entries, string fileUrl, string commit, DateTimeOffset commitDate)> LoadEntries()
{
    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        token = GetTokenFromGitHubCli();
    }

    var jsonOptions = new JsonSerializerOptions
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    using var getCommitsRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/chromium/chromium/commits?path=net/http/transport_security_state_static.json&per_page=1");
    getCommitsRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("Meziantou.Framework.Http.Hsts.Generator", "1.0"));
    getCommitsRequest.Headers.Add("Authorization", "Bearer " + token);
    using var commitsResponse = await SharedHttpClient.Instance.SendAsync(getCommitsRequest);
    commitsResponse.EnsureSuccessStatusCode();
    var commits = await commitsResponse.Content.ReadFromJsonAsync<JsonDocument>(jsonOptions);
    var lastCommit = commits!.RootElement.EnumerateArray().First();
    var sha = lastCommit.GetProperty("sha").GetString()!;

    var commitDate = lastCommit.GetProperty("commit").GetProperty("committer").GetProperty("date").GetDateTimeOffset();
    var fileUrl = $"https://raw.githubusercontent.com/chromium/chromium/{sha}/net/http/transport_security_state_static.json";
    var content = await SharedHttpClient.Instance.GetFromJsonAsync<JsonDocument>(fileUrl, jsonOptions);
    if (content is null)
        throw new InvalidOperationException("The document is invalid");

    var entries = content.RootElement.GetProperty("entries").Deserialize<List<Data>>(jsonOptions);
    if (entries is null || entries.Count == 0)
        throw new InvalidOperationException("The entries are invalid");

    // Remove entries that are not relevant
    entries.RemoveAll(entry => entry.Mode != "force-https" || entry.Policy == "test");
    if (entries.Count == 0)
        throw new InvalidOperationException("No entry is a force-https entry; the upstream format has probably changed");

    // The lookup binary-searches a blob of lower-case ASCII names, so the data is folded to that form here and
    // anything that cannot be represented in it is rejected rather than silently mangled
    foreach (var entry in entries)
    {
        entry.Name = entry.Name.ToLowerInvariant();
    }

    var invalidDomains = entries.Where(e => !IsValidHostName(e.Name)).Select(e => e.Name).ToList();
    if (invalidDomains.Count > 0)
        throw new InvalidOperationException("Invalid domain names: " + string.Join(", ", invalidDomains));

    // check if there are duplicated domains
    var duplicatedDomains = entries.GroupBy(e => e.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
    if (duplicatedDomains.Count > 0)
        throw new InvalidOperationException("Duplicated domains: " + string.Join(", ", duplicatedDomains));

    return (entries, fileUrl, sha, commitDate);
}

static string GetTokenFromGitHubCli()
{
    Process process;
    try
    {
        process = Process.Start(new ProcessStartInfo
        {
            FileName = "gh",
            Arguments = "auth token",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
    }
    catch (Win32Exception ex)
    {
        throw new InvalidOperationException("Cannot run 'gh auth token'. Set the GITHUB_TOKEN environment variable or install the GitHub CLI.", ex);
    }

    using (process)
    {
        // Read the pipes before waiting: a command that fills one would otherwise block forever
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException($"'gh auth token' exited with code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}. Set the GITHUB_TOKEN environment variable or run 'gh auth login'. {error.Trim()}");

        return output.Trim();
    }
}

static bool IsValidHostName(string name)
{
    // The blob stores names as ASCII bytes with a single length byte each, and the generated code has to be
    // able to name the resource, so a preload entry outside this character set is data to reject.
    if (name.Length is 0 or > 253 || name[0] is '.' or '-' || name[^1] is '.' or '-')
        return false;

    var previousWasDot = false;
    foreach (var c in name)
    {
        if (c is '.')
        {
            if (previousWasDot)
                return false;

            previousWasDot = true;
            continue;
        }

        previousWasDot = false;
        if (!char.IsAsciiDigit(c) && !char.IsAsciiLetterLower(c) && c is not ('-' or '_'))
            return false;
    }

    return true;
}

internal sealed class Data
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("policy")]
    public string? Policy { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("include_subdomains")]
    public bool IncludeSubdomains { get; set; }

    public int SegmentCount => Name.Count(c => c == '.') + 1;
}
