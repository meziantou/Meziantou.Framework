﻿#pragma warning disable CA1812 // Avoid uninstantiated internal classes
#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Meziantou.Framework;
using Meziantou.Framework.Versioning;

// Enable generating a subset of the data. Otherwise the IDE is not responsive because of the large file.
var fullGeneration = true;

var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
if (token is null)
{
    // gh auth token
    var process = Process.Start(new ProcessStartInfo
    {
        FileName = "gh",
        Arguments = "auth token",
        RedirectStandardOutput = true,
        UseShellExecute = false,
    });
    await process!.WaitForExitAsync();
    token = (await process.StandardOutput.ReadToEndAsync()).Trim();
}

var jsonOptions = new JsonSerializerOptions
{
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
};

// Get commit info and download the file
using var getCommitsRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/chromium/chromium/commits?path=net/http/transport_security_state_static.json&per_page=1");
getCommitsRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("Meziantou.Framework.Http.Hsts.Generator", "1.0"));
getCommitsRequest.Headers.Add("Authorization", "Bearer " + token);
using var commitsResponse = await SharedHttpClient.Instance.SendAsync(getCommitsRequest);
commitsResponse.EnsureSuccessStatusCode();
var commits = await commitsResponse.Content.ReadFromJsonAsync<JsonDocument>(jsonOptions);
var lastCommit = commits!.RootElement.EnumerateArray().First();
var sha = lastCommit.GetProperty("sha").GetString();
var commitDate = lastCommit.GetProperty("commit").GetProperty("committer").GetProperty("date").GetDateTime();
var fileUrl = $"https://raw.githubusercontent.com/chromium/chromium/{sha}/net/http/transport_security_state_static.json";
using var content = await SharedHttpClient.Instance.GetFromJsonAsync<JsonDocument>(fileUrl, jsonOptions);
if (content is null)
    throw new InvalidOperationException("The document is invalid");

var entries = content.RootElement.GetProperty("entries").Deserialize<List<Data>>(jsonOptions);
if (entries is null)
    throw new InvalidOperationException("The entries are invalid");

// Remove entries that are not relevant
entries.RemoveAll(entries => entries.Mode != "force-https" || entries.Policy == "test");

// check if there are duplicated domains
var duplicatedDomains = entries.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
if (duplicatedDomains.Count > 0)
{
    throw new InvalidOperationException("Duplicated domains: " + string.Join(", ", duplicatedDomains));
}

// Start generating the code
var maxSegments = entries.Max(e => e.SegmentCount);

var sb = new StringBuilder();
sb.Append("        var expires126Days = timeProvider.GetUtcNow().Add(TimeSpan.FromDays(126));\n");
sb.Append("        var expires365Days = timeProvider.GetUtcNow().Add(TimeSpan.FromDays(365));\n");

for (var i = 1; i <= maxSegments; i++)
{
    var capacity = entries.Count(e => e.SegmentCount == i) + 10; // leave some space to add new entries later
    sb.Append($"        var dict{i.ToString(CultureInfo.InvariantCulture)} = new ConcurrentDictionary<string, HstsDomainPolicy>(concurrencyLevel: -1, capacity: {capacity.ToString(CultureInfo.InvariantCulture)}, comparer: StringComparer.OrdinalIgnoreCase);\n");
    sb.Append($"        _policies.Add(dict{i.ToString(CultureInfo.InvariantCulture)});\n");
}

foreach (var entryGroup in entries.GroupBy(e => e.SegmentCount).OrderBy(group => group.Key))
{
    sb.Append(CultureInfo.InvariantCulture, $"        // Segment size: {entryGroup.Key}\n");
    foreach (var entry in entryGroup.OrderBy(entry => entry.Name, StringComparer.Ordinal).Take(fullGeneration ? int.MaxValue : 10))
    {
        var expiresIn = entry.Policy switch
        {
            "bulk-18-weeks" => "expires126Days",
            "bulk-1-year" => "expires365Days",
            _ => "expires365Days",
        };

        sb.Append($"""        _ = dict{entry.SegmentCount.ToString(CultureInfo.InvariantCulture)}.TryAdd("{entry.Name}", new HstsDomainPolicy("{entry.Name}", {expiresIn}, {(entry.IncludeSubdomains ? "true" : "false")}));""" + "\n");
    }
}

var result = $$"""
    // <auto-generated />
    #nullable disable

    using System.Collections.Concurrent;

    namespace Meziantou.Framework.Http;

    partial class HstsDomainPolicyCollection
    {
        private void LoadPreloadDomains(TimeProvider timeProvider)
        {
            // HSTS preload data source: {{fileUrl}} 
            // Commit date: {{commitDate.ToString("O", CultureInfo.InvariantCulture)}}
    {{sb.ToString().TrimEnd('\n')}}
        }
    }
    """.ReplaceLineEndings("\n");

if (!FullPath.CurrentDirectory().TryFindFirstAncestorOrSelf(path => Directory.Exists(path / ".git"), out var root))
    throw new InvalidOperationException("Cannot find git root from " + FullPath.CurrentDirectory());

var outputPath = root / "src" / "Meziantou.Framework.Http.Hsts" / "HstsDomainPolicyCollection.g.cs";
var csprojPath = root / "src" / "Meziantou.Framework.Http.Hsts" / "Meziantou.Framework.Http.Hsts.csproj";
if ((await File.ReadAllTextAsync(outputPath)).ReplaceLineEndings("\n") != result)
{
    await File.WriteAllTextAsync(outputPath, result);
    Console.WriteLine("The file has been updated");

    var doc = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
    var versionNode = doc.Descendants().First(e => e.Name.LocalName == "Version");
    var version = SemanticVersion.Parse(versionNode.Value);
    versionNode.Value = version.NextPatchVersion().ToString();
    doc.Save(csprojPath, SaveOptions.DisableFormatting);
    return 1;
}

return 0;

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