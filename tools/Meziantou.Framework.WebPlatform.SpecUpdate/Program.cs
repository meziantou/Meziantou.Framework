using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpecUpdate;

// Keeps the specification inputs (tests/Meziantou.Framework.WebPlatform.SpecData/) and the external test suites (tests/Meziantou.Framework.WebPlatform.External/) up to date.
//
//   SpecUpdate update        Resolve the latest revision of every source (or of the given sources, --except to exclude some), download spec data and rewrite tests/Meziantou.Framework.WebPlatform.SpecData/versions.json
//   SpecUpdate fetch-tests   Materialize tests/Meziantou.Framework.WebPlatform.External/* at the revisions pinned in tests/Meziantou.Framework.WebPlatform.SpecData/versions.json and the paths of tests/Meziantou.Framework.WebPlatform.SpecData/sources.json
//   SpecUpdate verify        Check that tests/Meziantou.Framework.WebPlatform.SpecData/ matches the checksums recorded in tests/Meziantou.Framework.WebPlatform.SpecData/versions.json
//   SpecUpdate wpt-coverage  Generate src/Meziantou.Framework.WebPlatform/docs/wpt-coverage.md and src/Meziantou.Framework.WebPlatform/docs/wpt-coverage/ from the WPT manifest, the expectations and the reasons (--check: fail if they are outdated or a reason is missing)

var root = FindRepositoryRoot();
var specDataDirectory = Path.Combine(root, "tests", "Meziantou.Framework.WebPlatform.SpecData");
var versionsPath = Path.Combine(specDataDirectory, "versions.json");
var sourcesPath = Path.Combine(specDataDirectory, "sources.json");

var command = args.Length > 0 ? args[0] : "help";
switch (command)
{
    case "update":
        await UpdateAsync(args.Skip(1).ToArray());
        return 0;
    case "fetch-tests":
        await FetchTestsAsync();
        return 0;
    case "verify":
        return Verify() ? 0 : 1;
    case "wpt-coverage":
        return await GenerateWptCoverageAsync(check: args.Contains("--check", StringComparer.Ordinal)) ? 0 : 1;
    default:
        Console.WriteLine("Usage: SpecUpdate <update [source...] [--except source...]|fetch-tests|verify|wpt-coverage [--check]>");
        return command == "help" ? 0 : 1;
}

async Task UpdateAsync(string[] arguments)
{
    // update [source...] [--except source...]: the given sources (all by default), except the excluded ones
    var exceptIndex = Array.IndexOf(arguments, "--except");
    var onlySources = exceptIndex < 0 ? arguments : arguments[..exceptIndex];
    var excludedSources = exceptIndex < 0 ? [] : arguments[(exceptIndex + 1)..];
    var sources = LoadSources();
    var versions = File.Exists(versionsPath) ? JsonNode.Parse(File.ReadAllText(versionsPath))!.AsObject() : [];

    using var http = CreateHttpClient();
    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? TryRun("gh", "auth token");
    if (!string.IsNullOrWhiteSpace(token))
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    foreach (var (name, sourceNode) in sources)
    {
        if ((onlySources.Length > 0 && !onlySources.Contains(name, StringComparer.Ordinal)) || excludedSources.Contains(name, StringComparer.Ordinal))
            continue;

        var source = sourceNode!.AsObject();
        var kind = (string)source["kind"]!;
        Console.WriteLine($"Updating {name} ({kind})");
        var entry = new JsonObject { ["kind"] = kind };
        switch (kind)
        {
            case "github-files":
            {
                // Files copied into tests/Meziantou.Framework.WebPlatform.SpecData/<name>/ from a GitHub repository at a resolved commit.
                var repo = (string)source["repo"]!;
                var commit = await ResolveCommitAsync(http, repo, (string)source["ref"]!);
                var targetDirectory = Path.Combine(specDataDirectory, name);
                RecreateDirectory(targetDirectory);
                SparseCheckout(repo, commit, source["paths"]!.AsArray().Select(p => (string)p!).ToArray(), checkoutDirectory =>
                {
                    foreach (var path in source["paths"]!.AsArray().Select(p => (string)p!))
                    {
                        foreach (var match in ExpandPattern(checkoutDirectory, path))
                        {
                            CopyPath(Path.Combine(checkoutDirectory, match), Path.Combine(targetDirectory, match));
                        }
                    }
                });

                entry["repo"] = repo;
                entry["ref"] = (string)source["ref"]!;
                entry["commit"] = commit;
                entry["files"] = ComputeChecksums(targetDirectory);
                break;
            }

            case "url-files":
            {
                // Files downloaded from stable URLs. The version is extracted from the file header when available.
                var targetDirectory = Path.Combine(specDataDirectory, name);
                RecreateDirectory(targetDirectory);
                var urls = new JsonObject();
                foreach (var (fileName, urlNode) in source["files"]!.AsObject())
                {
                    var url = (string)urlNode!;
                    var content = await http.GetByteArrayAsync(new Uri(url));
                    var path = Path.Combine(targetDirectory, fileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllBytesAsync(path, content);
                    urls[fileName] = url;
                }

                entry["urls"] = urls;
                entry["downloaded"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                entry["files"] = ComputeChecksums(targetDirectory);
                break;
            }

            case "github-test-suite":
            {
                // Test suites are only pinned here; `fetch-tests` materializes them under tests/Meziantou.Framework.WebPlatform.External/.
                var repo = (string)source["repo"]!;
                entry["repo"] = repo;
                entry["ref"] = (string)source["ref"]!;
                if (source["manifest"]?.GetValue<bool>() is true)
                {
                    // The commit of the latest release of the repository, which provides the manifest of the test suite
                    // (MANIFEST-<commit>.json.gz, published a few minutes after a pull request is merged into the ref).
                    var (commit, manifest) = await ResolveManifestReleaseAsync(http, repo);
                    entry["commit"] = commit;
                    entry["manifest"] = manifest;
                }
                else
                {
                    entry["commit"] = await ResolveCommitAsync(http, repo, (string)source["ref"]!);
                }

                break;
            }

            default:
                throw new InvalidOperationException($"Unknown source kind '{kind}'");
        }

        versions[name] = entry;
    }

    File.WriteAllText(versionsPath, versions.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true }) + "\n");
    Console.WriteLine($"Wrote {versionsPath}");
}

async Task FetchTestsAsync()
{
    var sources = LoadSources();
    var versions = JsonNode.Parse(File.ReadAllText(versionsPath))!.AsObject();
    foreach (var (name, entryNode) in versions)
    {
        var entry = entryNode!.AsObject();
        if ((string)entry["kind"]! != "github-test-suite")
            continue;

        // The commit is pinned in versions.json, the paths are read from sources.json, so adding a path does not change the pin
        var repo = (string)entry["repo"]!;
        var commit = (string)entry["commit"]!;
        var paths = (sources[name]?["paths"] ?? throw new InvalidOperationException($"tests/Meziantou.Framework.WebPlatform.SpecData/sources.json has no paths for '{name}'")).AsArray().Select(p => (string)p!).ToArray();
        var target = Path.Combine(root, "tests", "Meziantou.Framework.WebPlatform.External", name);

        // The repository is created before the manifest is downloaded into it, so creating it cannot delete the manifest
        InitializeTestSuite(target, repo);

        // The manifest lists every test of the suite, including the directories that are not checked out. It is the one of the
        // pinned commit: the manifest of the previous commit is deleted first, so an interrupted update cannot keep it.
        string[] referencePaths = [];
        if (entry["manifest"] is JsonNode manifest)
        {
            var head = TryRun("git", $"-C \"{target}\" rev-parse -q --verify HEAD")?.Trim();
            if (head != commit)
            {
                File.Delete(Path.Combine(target, "MANIFEST.json.gz"));
            }

            var manifestPath = await EnsureManifestAsync(target, (string)manifest!);

            // The references of the reftests of the paths are fetched with them (e.g. the green squares of css/reference)
            referencePaths = [.. WptManifest.GetReferencesOutsideOfPaths(manifestPath, paths)];
            if (referencePaths.Length > 0)
            {
                Console.WriteLine($"{name}: {referencePaths.Length} references of the reftests outside of the paths");
            }
        }

        SyncTestSuite(target, repo, commit, paths, referencePaths);
    }
}

// Creates the git repository of a test suite (tests/Meziantou.Framework.WebPlatform.External/<name>) if it does not exist yet
static void InitializeTestSuite(string target, string repo)
{
    var gitDirectory = Path.Combine(target, ".git");
    if (Directory.Exists(gitDirectory))
        return;

    // New directory, or a directory materialized by a previous version of this tool (a copy of the files)
    RecreateDirectory(target);
    Git(target, "init -q");
    Git(target, $"remote add origin https://github.com/{repo}.git");
    Git(target, "config core.sparseCheckout true");
    Git(target, "config core.sparseCheckoutCone false");
    Git(target, "config core.symlinks false");
    Git(target, "config core.longpaths true");
    Git(target, "config core.autocrlf false");
    Git(target, "config gc.auto 0");
    File.AppendAllText(Path.Combine(gitDirectory, "info", "exclude"), "/MANIFEST.json.gz\n");
}

// Updates the sparse checkout of a test suite (tests/Meziantou.Framework.WebPlatform.External/<name>) to the commit and the paths. The directory is a
// shallow, blob-less clone, so changing the commit or the paths only downloads the files that changed. The symbolic links
// are not materialized: they are excluded from the sparse checkout.
static void SyncTestSuite(string target, string repo, string commit, string[] paths, string[] additionalFiles)
{
    var gitDirectory = Path.Combine(target, ".git");
    var stampPath = Path.Combine(gitDirectory, "specupdate-stamp");
    var stamp = commit + "\n" + string.Join('\n', paths.Concat(additionalFiles));
    if (File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
    {
        Console.WriteLine($"{Path.GetFileName(target)} is up to date ({commit})");
        return;
    }

    var head = TryRun("git", $"-C \"{target}\" rev-parse -q --verify HEAD")?.Trim();
    var commitChanged = head != commit;
    if (commitChanged)
    {
        Console.WriteLine($"Fetching {Path.GetFileName(target)} at {commit}");
        Git(target, $"fetch -q --depth 1 --filter=blob:none origin {commit}");
    }
    else
    {
        Console.WriteLine($"Updating the paths of {Path.GetFileName(target)}");
    }

    var symbolicLinks = RunWithOutput("git", $"-C \"{target}\" ls-tree -r -z --full-tree {commit}")
        .Split('\0', StringSplitOptions.RemoveEmptyEntries)
        .Where(line => line.StartsWith("120000 ", StringComparison.Ordinal))
        .Select(line => line[(line.IndexOf('\t', StringComparison.Ordinal) + 1)..]);
    File.WriteAllLines(
        Path.Combine(gitDirectory, "info", "sparse-checkout"),
        paths.Select(p => "/" + p.TrimStart('/')).Concat(additionalFiles.Select(p => "/" + EscapeSparsePattern(p))).Concat(symbolicLinks.Select(p => "!/" + EscapeSparsePattern(p))));

    if (commitChanged)
    {
        Git(target, $"checkout -q -f --detach {commit}");

        // Drop the previous commit (a shallow clone only keeps the pinned commit)
        Git(target, "reflog expire --expire=now --all");
        Git(target, "gc -q --prune=now");
    }

    Git(target, "sparse-checkout reapply");

    foreach (var path in paths)
    {
        if (!path.Contains('*', StringComparison.Ordinal) && !Path.Exists(Path.Combine(target, path)))
            throw new InvalidOperationException($"'{path}' does not exist in {repo} at {commit}");
    }

    File.WriteAllText(stampPath, stamp);
}

// Escapes the special characters of a path in a sparse checkout pattern (gitignore syntax)
static string EscapeSparsePattern(string path)
{
    var builder = new System.Text.StringBuilder(path.Length);
    foreach (var c in path)
    {
        if (c is '\\' or '*' or '?' or '[' or '!' or '#' or ' ')
        {
            builder.Append('\\');
        }

        builder.Append(c);
    }

    return builder.ToString();
}

// Downloads the manifest of a test suite (MANIFEST.json.gz) next to its files. fetch-tests deletes it when the pinned
// commit changes, so an existing manifest is the one of the commit.
static async Task<string> EnsureManifestAsync(string testSuiteDirectory, string url)
{
    var path = Path.Combine(testSuiteDirectory, "MANIFEST.json.gz");
    if (!File.Exists(path))
    {
        Console.WriteLine($"Downloading {url}");
        using var http = CreateHttpClient();
        var content = await http.GetByteArrayAsync(new Uri(url));
        Directory.CreateDirectory(testSuiteDirectory);
        await File.WriteAllBytesAsync(path, content);
    }

    return path;
}

async Task<bool> GenerateWptCoverageAsync(bool check)
{
    var versions = JsonNode.Parse(File.ReadAllText(versionsPath))!.AsObject();
    var entry = versions["wpt"]!.AsObject();
    var commit = (string)entry["commit"]!;
    var manifestUrl = (string?)entry["manifest"] ?? throw new InvalidOperationException("tests/Meziantou.Framework.WebPlatform.SpecData/versions.json has no manifest for wpt. Run 'SpecUpdate update wpt'.");
    var manifestPath = await EnsureManifestAsync(Path.Combine(root, "tests", "Meziantou.Framework.WebPlatform.External", "wpt"), manifestUrl);
    var wptProjectDirectory = Path.Combine(root, "tests", "Meziantou.Framework.WebPlatform.Wpt");
    var result = WptCoverage.Generate(manifestPath, wptProjectDirectory, commit);

    foreach (var pattern in result.UnusedReasons)
    {
        Console.WriteLine($"warning: the reason of '{pattern}' (wpt-reasons.json) matches no test that does not pass");
    }

    foreach (var path in result.MissingReasons)
    {
        Console.Error.WriteLine($"error: '{path}' does not pass and has no reason in wpt-reasons.json");
    }

    foreach (var error in result.InvalidReasons)
    {
        Console.Error.WriteLine($"error: invalid reason in {error}");
    }

    // The pages of the sections are the only files of src/Meziantou.Framework.WebPlatform/docs/wpt-coverage/, so the pages of the removed sections are deleted
    var docsDirectory = Path.Combine(root, "src", "Meziantou.Framework.WebPlatform", "docs");
    var sectionDirectory = Path.Combine(docsDirectory, WptCoverage.SectionDirectory);
    var existingPages = Directory.Exists(sectionDirectory)
        ? Directory.EnumerateFiles(sectionDirectory, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(docsDirectory, p).Replace(Path.DirectorySeparatorChar, '/')).ToList()
        : [];
    if (check)
    {
        var outdated = result.Pages
            .Where(p => { var path = Path.Combine(docsDirectory, p.Key); return !File.Exists(path) || File.ReadAllText(path).ReplaceLineEndings("\n") != p.Value; })
            .Select(p => p.Key)
            .Concat(existingPages.Where(p => !result.Pages.ContainsKey(p)))
            .ToList();
        if (outdated.Count > 0)
        {
            Console.Error.WriteLine($"The WPT coverage pages are out of date ({string.Join(", ", outdated.Take(5))}{(outdated.Count > 5 ? ", ..." : "")}). Run 'dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -- wpt-coverage'.");
            return false;
        }

        Console.WriteLine("src/Meziantou.Framework.WebPlatform/docs/wpt-coverage.md is up to date");
        return result.MissingReasons.Count == 0 && result.InvalidReasons.Count == 0;
    }

    foreach (var page in existingPages.Where(p => !result.Pages.ContainsKey(p)))
    {
        File.Delete(Path.Combine(docsDirectory, page));
    }

    foreach (var (page, content) in result.Pages)
    {
        var path = Path.Combine(docsDirectory, page);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    Console.WriteLine($"Wrote src/Meziantou.Framework.WebPlatform/docs/{WptCoverage.IndexPath} and {result.Pages.Count - 1} section pages ({result.MissingReasons.Count} tests without reason)");
    return result.InvalidReasons.Count == 0;
}

bool Verify()
{
    var versions = JsonNode.Parse(File.ReadAllText(versionsPath))!.AsObject();
    var success = true;
    foreach (var (name, entryNode) in versions)
    {
        var files = entryNode!["files"]?.AsObject();
        if (files is null)
            continue;

        var actual = ComputeChecksums(Path.Combine(specDataDirectory, name));
        if (!JsonNode.DeepEquals(actual, files))
        {
            Console.Error.WriteLine($"tests/Meziantou.Framework.WebPlatform.SpecData/{name} does not match versions.json. Run 'SpecUpdate update {name}'.");
            success = false;
        }
    }

    return success;
}

JsonObject LoadSources() => JsonNode.Parse(File.ReadAllText(sourcesPath))!.AsObject();

static HttpClient CreateHttpClient()
{
    var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Meziantou.Framework.WebPlatform.SpecUpdate", "1.0"));
    return http;
}

static async Task<(string Commit, string Manifest)> ResolveManifestReleaseAsync(HttpClient http, string repo)
{
    using var response = await http.GetAsync(new Uri($"https://api.github.com/repos/{repo}/releases/latest"));
    response.EnsureSuccessStatusCode();
    var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    var commit = (string)json["target_commitish"]!;
    var assetName = $"MANIFEST-{commit}.json.gz";
    var asset = json["assets"]!.AsArray().FirstOrDefault(a => (string?)a!["name"] == assetName)
        ?? throw new InvalidOperationException($"The release '{json["tag_name"]}' of {repo} has no asset '{assetName}'");
    return (commit, (string)asset!["browser_download_url"]!);
}

static async Task<string> ResolveCommitAsync(HttpClient http, string repo, string reference)
{
    using var response = await http.GetAsync(new Uri($"https://api.github.com/repos/{repo}/commits/{reference}"));
    response.EnsureSuccessStatusCode();
    var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    return (string)json["sha"]!;
}

static void SparseCheckout(string repo, string commit, string[] paths, Action<string> action)
{
    var directory = Path.Combine(Path.GetTempPath(), "specupdate-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        Run("git", $"init -q \"{directory}\"");
        Run("git", $"-C \"{directory}\" remote add origin https://github.com/{repo}.git");
        Run("git", $"-C \"{directory}\" sparse-checkout set --no-cone {string.Join(' ', paths.Select(p => "\"/" + p.TrimStart('/') + "\""))}");
        Run("git", $"-C \"{directory}\" fetch -q --depth 1 --filter=blob:none origin {commit}");
        Run("git", $"-C \"{directory}\" checkout -q FETCH_HEAD");
        action(directory);
    }
    finally
    {
        DeleteDirectory(directory);
    }
}

static void CopyPath(string source, string destination)
{
    if (File.Exists(source))
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        return;
    }

    if (Directory.Exists(source))
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            CopyPath(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }

        return;
    }

    throw new FileNotFoundException($"'{source}' does not exist in the checkout");
}

// Expands the glob-like patterns of a path (e.g. "ed/idl/*.idl", "css/*/parsing"): '*' matches any part of a single
// segment, as in the sparse checkout patterns. Returns the matching paths, relative to the directory.
static IEnumerable<string> ExpandPattern(string directory, string pattern)
{
    var matches = new List<string> { "" };
    var isPattern = false;
    foreach (var segment in pattern.Trim('/').Split('/'))
    {
        var next = new List<string>();
        foreach (var match in matches)
        {
            var current = Path.Combine(directory, match);
            if (!segment.Contains('*', StringComparison.Ordinal))
            {
                // A literal path must exist, but a pattern only matches the paths that exist
                var path = Path.Combine(match, segment);
                if (!isPattern || Path.Exists(Path.Combine(directory, path)))
                {
                    next.Add(path);
                }
            }
            else if (Directory.Exists(current))
            {
                next.AddRange(Directory.EnumerateFileSystemEntries(current, segment).Order(StringComparer.Ordinal).Select(entry => Path.Combine(match, Path.GetFileName(entry))));
            }
        }

        matches = next;
        isPattern |= segment.Contains('*', StringComparison.Ordinal);
    }

    return matches;
}

static JsonObject ComputeChecksums(string directory)
{
    var result = new JsonObject();
    foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        var relativePath = Path.GetRelativePath(directory, file).Replace('\\', '/');
        result[relativePath] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
    }

    return result;
}

static void RecreateDirectory(string path)
{
    DeleteDirectory(path);
    Directory.CreateDirectory(path);
}

static void DeleteDirectory(string path)
{
    if (!Directory.Exists(path))
        return;

    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
    {
        File.SetAttributes(file, FileAttributes.Normal);
    }

    Directory.Delete(path, recursive: true);
}

static void Run(string fileName, string arguments)
{
    using var process = Process.Start(new ProcessStartInfo(fileName, arguments) { RedirectStandardError = true, UseShellExecute = false })!;
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"'{fileName} {arguments}' failed with exit code {process.ExitCode}: {error}");
}

static void Git(string directory, string arguments) => Run("git", $"-C \"{directory}\" {arguments}");

static string RunWithOutput(string fileName, string arguments)
{
    using var process = Process.Start(new ProcessStartInfo(fileName, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
    var errorTask = process.StandardError.ReadToEndAsync();
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"'{fileName} {arguments}' failed with exit code {process.ExitCode}: {errorTask.Result}");

    return output;
}

static string? TryRun(string fileName, string arguments)
{
    try
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : null;
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return null;
    }
}

static string FindRepositoryRoot()
{
    var directory = AppContext.BaseDirectory;
    while (directory is not null && !File.Exists(Path.Combine(directory, "Meziantou.Framework.slnx")))
    {
        directory = Path.GetDirectoryName(directory);
    }

    return directory ?? throw new InvalidOperationException("Cannot find the repository root");
}
