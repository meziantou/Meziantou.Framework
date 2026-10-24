#!/usr/bin/env dotnet
#:sdk Meziantou.NET.Sdk
// Materializes tests/Meziantou.Framework.JavaScriptEngine.External/<suite> at the commits and paths pinned in
// tests/Meziantou.Framework.JavaScriptEngine.Test262/test262.json (test262 and test262-parser-tests) and
// tests/Meziantou.Framework.JavaScriptEngine.WebToolingBenchmark/web-tooling-benchmark.json. Each suite is a shallow,
// blob-less, sparse git clone, so changing the commit only downloads the files that changed. A stamp file makes a run
// with unchanged pins a no-op.
//
// A suite with an "npm" section also gets the npm packages it lists, installed in <suite>/node_modules from the tarballs
// and with the integrity hashes of the package-lock.json of the suite (lockfileVersion 1), like 'npm ci --ignore-scripts'
// would do for these packages and their dependencies. 'a>b' is the package 'b' as 'a' sees it (a nested dependency).
//
// The npm-packages suite is the packages of tests/Meziantou.Framework.JavaScriptEngine.NpmPackages/package-lock.json
// (lockfileVersion 3), installed in npm-packages/node_modules as 'npm ci --ignore-scripts --omit=optional' would do, with
// npm-packages/importmap.json, the import map that loads them in a browser.
//
// Without arguments, every suite is fetched except the ones marked "optional" (parse-number-fxx-test-data). The arguments
// name the suites to fetch, optional or not: dotnet run eng/javascriptengine/fetch-tests.cs parse-number-fxx-test-data
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

var root = FindRoot(Environment.CurrentDirectory) ?? FindRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Cannot find the root of the repository (Meziantou.Framework.slnx)");
string[] pinsPaths =
[
    Path.Combine(root, "tests", "Meziantou.Framework.JavaScriptEngine.Test262", "test262.json"),
    Path.Combine(root, "tests", "Meziantou.Framework.JavaScriptEngine.WebToolingBenchmark", "web-tooling-benchmark.json"),
];
var npmLockFiles = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["npm-packages"] = Path.Combine(root, "tests", "Meziantou.Framework.JavaScriptEngine.NpmPackages", "package-lock.json"),
};
var externalPath = Path.Combine(root, "tests", "Meziantou.Framework.JavaScriptEngine.External");
var pinsDocuments = pinsPaths.Select(path => JsonDocument.Parse(File.ReadAllText(path))).ToArray();
var requestedSuites = args.ToHashSet(StringComparer.Ordinal);
foreach (var name in requestedSuites)
{
    if (!npmLockFiles.ContainsKey(name) && !pinsDocuments.Any(pins => pins.RootElement.TryGetProperty(name, out _)))
        throw new InvalidOperationException($"'{name}' is not a suite of {string.Join(" or ", [.. pinsPaths, .. npmLockFiles.Values])}");
}

foreach (var pins in pinsDocuments)
{
    foreach (var suite in pins.RootElement.EnumerateObject())
    {
        var optional = suite.Value.TryGetProperty("optional", out var optionalValue) && optionalValue.GetBoolean();
        if (requestedSuites.Count > 0 ? !requestedSuites.Contains(suite.Name) : optional)
            continue;

        var repository = suite.Value.GetProperty("repository").GetString()!;
        var commit = suite.Value.GetProperty("commit").GetString()!;
        var paths = suite.Value.GetProperty("paths").EnumerateArray().Select(p => p.GetString()!).ToArray();
        var target = Path.Combine(externalPath, suite.Name);
        Initialize(target, repository);
        Sync(target, repository, commit, paths);
        if (suite.Value.TryGetProperty("npm", out var npm))
        {
            var lockFile = npm.GetProperty("lockfile").GetString()!;
            var packages = npm.GetProperty("packages").EnumerateArray().Select(p => p.GetString()!).ToArray();
            await InstallNpmPackages(target, lockFile, packages);
        }
    }
}

foreach (var (name, lockFile) in npmLockFiles)
{
    if (requestedSuites.Count == 0 || requestedSuites.Contains(name))
    {
        await InstallNpmLockFile(Path.Combine(externalPath, name), lockFile);
    }
}

return 0;

// The packages of a package-lock.json whose lockfileVersion is 3: the keys of "packages" are the paths where npm installs
// them. The optional packages (the native binaries of the packages that have a JavaScript fallback) are not installed.
// The import map of the packages is written to <target>/importmap.json, each time (see WriteImportMap).
static async Task InstallNpmLockFile(string target, string lockFile)
{
    var nodeModules = Path.Combine(target, "node_modules");
    var stampPath = Path.Combine(nodeModules, ".fetch-tests-stamp");
    var lockContent = File.ReadAllText(lockFile);
    var stamp = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(lockContent)));

    using var lockDocument = JsonDocument.Parse(lockContent);
    if (lockDocument.RootElement.GetProperty("lockfileVersion").GetInt32() != 3)
        throw new InvalidOperationException($"{lockFile} must have lockfileVersion 3");

    var packages = new List<(string Path, string Url, string Integrity)>();
    var installedPaths = new List<string>();
    foreach (var entry in lockDocument.RootElement.GetProperty("packages").EnumerateObject())
    {
        if (entry.Name.Length == 0 || IsTrue(entry.Value, "optional") || IsTrue(entry.Value, "dev") || IsTrue(entry.Value, "link"))
            continue;

        if (!entry.Name.StartsWith("node_modules/", StringComparison.Ordinal) || entry.Name.Split('/').Contains(".."))
            throw new InvalidOperationException($"Unexpected package path '{entry.Name}' in {lockFile}");

        installedPaths.Add(entry.Name["node_modules/".Length..]);

        // The bundled dependencies are in the tarball of the package that bundles them
        if (IsTrue(entry.Value, "inBundle"))
            continue;

        packages.Add((entry.Name["node_modules/".Length..], entry.Value.GetProperty("resolved").GetString()!, entry.Value.GetProperty("integrity").GetString()!));
    }

    if (File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
    {
        Console.WriteLine($"{Path.GetFileName(target)}/node_modules is up to date");
    }
    else
    {
        if (Directory.Exists(nodeModules))
        {
            Directory.Delete(nodeModules, recursive: true);
        }

        Console.WriteLine($"Installing {packages.Count} npm packages in {nodeModules}");
        using var httpClient = new HttpClient();
        await Parallel.ForEachAsync(packages, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (item, cancellationToken) =>
        {
            var content = await DownloadWithRetry(httpClient, item.Url, cancellationToken);
            VerifyIntegrity(item.Url, content, item.Integrity);
            Extract(content, Path.Combine(nodeModules, item.Path.Replace('/', Path.DirectorySeparatorChar)));
        });

        File.WriteAllText(stampPath, stamp);
    }

    WriteImportMap(target, installedPaths);

    static bool IsTrue(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;
}

// The import map (https://html.spec.whatwg.org/multipage/webappapis.html#import-maps) that lets a browser import the
// installed packages from /node_modules without a bundler: the "exports" of the package.json files with the "browser",
// "import", "module", "production" and "default" conditions, else the "module", "browser" or "main" field. A package installed in the node_modules of another package is mapped in
// the scope of that package, which is what the resolution of Node.js gives (the node_modules of the importing package,
// then the ones of its parents). An export whose pattern cannot be written as a prefix (e.g. "./*": "./dist/*.js") is
// written as one entry for each matching file. The "imports" of a package and the object form of its "browser" field
// are mapped too.
static void WriteImportMap(string target, IEnumerable<string> packagePaths)
{
    // The module of the "false" replacements of the "browser" field
    const string EmptyModule = "data:text/javascript,export default {};";

    var nodeModules = Path.Combine(target, "node_modules");
    var imports = new SortedDictionary<string, string>(StringComparer.Ordinal);
    var scopes = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
    foreach (var packagePath in packagePaths)
    {
        var directory = Path.Combine(nodeModules, packagePath.Replace('/', Path.DirectorySeparatorChar));
        var packageJsonPath = Path.Combine(directory, "package.json");
        if (!File.Exists(packageJsonPath))
            continue;

        using var packageJson = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        var separator = packagePath.LastIndexOf("/node_modules/", StringComparison.Ordinal);
        var name = separator < 0 ? packagePath : packagePath[(separator + "/node_modules/".Length)..];
        var entries = imports;
        if (separator >= 0)
        {
            var scope = "/node_modules/" + packagePath[..separator] + "/";
            if (!scopes.TryGetValue(scope, out entries))
            {
                entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
                scopes.Add(scope, entries);
            }
        }

        var baseUrl = "/node_modules/" + packagePath + "/";
        foreach (var (specifier, url) in GetPackageEntries(name, directory, baseUrl, packageJson.RootElement))
        {
            entries.TryAdd(specifier, url);
        }

        // The "imports" of the package ("#name" specifiers), in the scope of the package
        if (packageJson.RootElement.TryGetProperty("imports", out var packageImports) && packageImports.ValueKind is JsonValueKind.Object)
        {
            var packageScope = GetScope(scopes, baseUrl);
            foreach (var (specifier, url) in GetMapEntries("", directory, baseUrl, packageImports.EnumerateObject().Select(property => (property.Name, property.Value)), imports, scopes, packagePath))
            {
                packageScope.TryAdd(specifier, url);
            }
        }

        // The object form of the "browser" field (https://github.com/defunctzombie/package-browser-field-spec): the
        // files of the package replaced by other files, or by an empty module (false), as the bundlers do
        if (packageJson.RootElement.TryGetProperty("browser", out var browser) && browser.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in browser.EnumerateObject())
            {
                var replacement = property.Value.ValueKind switch
                {
                    JsonValueKind.False => EmptyModule,
                    JsonValueKind.String when property.Value.GetString()!.StartsWith('.', StringComparison.Ordinal) => baseUrl + NormalizeRelativePath(property.Value.GetString()!),
                    _ => (string?)null,
                };
                if (replacement is null)
                    continue;

                if (property.Name.StartsWith("./", StringComparison.Ordinal))
                {
                    var file = NormalizeRelativePath(property.Name);
                    foreach (var candidate in new[] { file, file + ".js" }.Where(candidate => File.Exists(Path.Combine(directory, candidate))))
                    {
                        imports.TryAdd(baseUrl + candidate, replacement);
                    }
                }
                else
                {
                    GetScope(scopes, baseUrl).TryAdd(property.Name, replacement);
                }
            }
        }
    }

    var options = new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    using var stream = File.Create(Path.Combine(target, "importmap.json"));
    using (var writer = new Utf8JsonWriter(stream, options))
    {
        writer.WriteStartObject();
        writer.WriteStartObject("imports");
        foreach (var (key, value) in imports)
        {
            writer.WriteString(key, value);
        }

        writer.WriteEndObject();
        writer.WriteStartObject("scopes");
        foreach (var (scope, entries) in scopes)
        {
            writer.WriteStartObject(scope);
            foreach (var (key, value) in entries)
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    Console.WriteLine($"Wrote the import map of the npm packages ({imports.Count} entries, {scopes.Count} scopes)");
}

static SortedDictionary<string, string> GetScope(SortedDictionary<string, SortedDictionary<string, string>> scopes, string scope)
{
    if (!scopes.TryGetValue(scope, out var entries))
    {
        entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        scopes.Add(scope, entries);
    }

    return entries;
}

static IEnumerable<(string Specifier, string Url)> GetPackageEntries(string name, string directory, string baseUrl, JsonElement packageJson)
{
    if (!packageJson.TryGetProperty("exports", out var exports) || exports.ValueKind is JsonValueKind.Null)
    {
        // No exports: the entry point, and any file of the package
        var main = new[] { "module", "browser", "main" }
            .Select(field => packageJson.TryGetProperty(field, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null)
            .FirstOrDefault(value => value is not null) ?? "index.js";
        var mainFile = new[] { main, main + ".js", main + "/index.js" }.Select(NormalizeRelativePath).FirstOrDefault(path => File.Exists(Path.Combine(directory, path)));
        if (mainFile is not null)
            yield return (name, baseUrl + mainFile);

        yield return (name + "/", baseUrl);
        yield break;
    }

    var isConditions = exports.ValueKind is JsonValueKind.Object && !exports.EnumerateObject().Any(property => property.Name.StartsWith('.', StringComparison.Ordinal));
    var map = exports.ValueKind is JsonValueKind.Object && !isConditions ? exports.EnumerateObject().Select(property => (property.Name, property.Value)).ToList() : [(".", exports)];
    foreach (var entry in GetMapEntries(name, directory, baseUrl, map, imports: null, scopes: null, packagePath: null))
        yield return entry;
}

// The entries of the "exports" (keys "." and "./path", the specifiers are relative to the name of the package) or of the
// "imports" (keys "#name", the name is empty) of a package. The target of an "imports" entry can be another package: it
// is resolved with the import map built so far, which has the packages installed before it.
static IEnumerable<(string Specifier, string Url)> GetMapEntries(string name, string directory, string baseUrl, IEnumerable<(string Key, JsonElement Value)> map, SortedDictionary<string, string>? imports, SortedDictionary<string, SortedDictionary<string, string>>? scopes, string? packagePath)
{
    foreach (var (key, value) in map)
    {
        var targetPath = ResolveExportTarget(value);
        if (targetPath is null)
            continue;

        var specifier = key == "." ? name : key.StartsWith('#', StringComparison.Ordinal) ? key : name + key[1..];
        if (!targetPath.StartsWith("./", StringComparison.Ordinal))
        {
            // A package ("#dep": "other-package"): the entry of the scope of the package, else the top-level one
            if (key.StartsWith('#', StringComparison.Ordinal) && !key.Contains('*', StringComparison.Ordinal) && imports is not null)
            {
                var resolved = scopes!.TryGetValue("/node_modules/" + packagePath + "/", out var scope) && scope.TryGetValue(targetPath, out var scoped) ? scoped : imports.GetValueOrDefault(targetPath);
                if (resolved is not null)
                    yield return (specifier, resolved);
            }

            continue;
        }

        var star = key.IndexOf('*', StringComparison.Ordinal);
        var targetStar = targetPath.IndexOf('*', StringComparison.Ordinal);
        if (star < 0)
        {
            yield return (specifier, baseUrl + NormalizeRelativePath(targetPath));
            continue;
        }

        if (targetStar < 0)
            continue;

        var keyPrefix = key[..star];
        var keySuffix = key[(star + 1)..];
        var specifierPrefix = keyPrefix.StartsWith('#', StringComparison.Ordinal) ? keyPrefix : name + keyPrefix[1..];
        var targetPrefix = targetPath[..targetStar];
        var targetSuffix = targetPath[(targetStar + 1)..];
        if (keySuffix.Length == 0 && targetSuffix.Length == 0 && keyPrefix.EndsWith('/', StringComparison.Ordinal) && targetPrefix.EndsWith('/', StringComparison.Ordinal))
        {
            yield return (specifierPrefix, baseUrl + NormalizeRelativePath(targetPrefix));
            continue;
        }

        // The files of the package that match the target pattern
        var normalizedPrefix = NormalizeRelativePath(targetPrefix);
        var searchDirectory = Path.Combine(directory, normalizedPrefix[..(normalizedPrefix.LastIndexOf('/', StringComparison.Ordinal) + 1)].Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(searchDirectory))
            continue;

        foreach (var file in Directory.EnumerateFiles(searchDirectory, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal))
        {
            if (file.Length >= normalizedPrefix.Length + targetSuffix.Length && file.StartsWith(normalizedPrefix, StringComparison.Ordinal) && file.EndsWith(targetSuffix, StringComparison.Ordinal))
            {
                var match = file[normalizedPrefix.Length..(file.Length - targetSuffix.Length)];
                yield return (specifierPrefix + match + keySuffix, baseUrl + file);
            }
        }
    }
}

// The target of a conditional export for a browser that loads ES modules
static string? ResolveExportTarget(JsonElement value)
{
    switch (value.ValueKind)
    {
        case JsonValueKind.String:
            return value.GetString();
        case JsonValueKind.Array:
            return value.EnumerateArray().Select(ResolveExportTarget).FirstOrDefault(item => item is not null);
        case JsonValueKind.Object:
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name is "browser" or "import" or "module" or "production" or "default" && ResolveExportTarget(property.Value) is { } result)
                    return result;
            }

            return null;
        default:
            return null;
    }
}

static string NormalizeRelativePath(string path)
{
    var parts = new List<string>();
    foreach (var part in path.Split('/'))
    {
        if (part is "" or ".")
            continue;

        if (part is "..")
        {
            if (parts.Count > 0)
                parts.RemoveAt(parts.Count - 1);
        }
        else
        {
            parts.Add(part);
        }
    }

    return string.Join('/', parts) + (path.EndsWith('/', StringComparison.Ordinal) && parts.Count > 0 ? "/" : "");
}

static async Task InstallNpmPackages(string target, string lockFile, string[] packages)
{
    var stampPath = Path.Combine(target, "node_modules", ".fetch-tests-stamp");
    var lockContent = File.ReadAllText(Path.Combine(target, lockFile));
    var stamp = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(lockContent))) + "\n" + string.Join('\n', packages);
    if (File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
    {
        Console.WriteLine($"{Path.GetFileName(target)}/node_modules is up to date");
        return;
    }

    // The packages to install: the requested ones and their dependencies, resolved as Node.js does from the location
    // of the package that requires them (its own node_modules, then the ones of its parents)
    using var lockDocument = JsonDocument.Parse(lockContent);
    var lockRoot = lockDocument.RootElement.GetProperty("dependencies");
    var installed = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
    var queue = new Queue<List<(string Name, JsonElement Entry)>>();
    foreach (var package in packages)
    {
        var chain = new List<(string Name, JsonElement Entry)>();
        foreach (var name in package.Split('>'))
        {
            chain = Resolve(lockRoot, chain, name);
        }

        queue.Enqueue(chain);
    }

    while (queue.TryDequeue(out var chain))
    {
        var path = string.Join("/node_modules/", chain.Select(item => item.Name));
        var entry = chain[^1].Entry;
        if (!installed.TryAdd(path, entry))
            continue;

        if (entry.TryGetProperty("requires", out var requires))
        {
            foreach (var dependency in requires.EnumerateObject())
            {
                queue.Enqueue(Resolve(lockRoot, chain, dependency.Name));
            }
        }
    }

    var nodeModules = Path.Combine(target, "node_modules");
    if (Directory.Exists(nodeModules))
    {
        Directory.Delete(nodeModules, recursive: true);
    }

    Console.WriteLine($"Installing {installed.Count} npm packages in {nodeModules}");
    using var httpClient = new HttpClient();
    await Parallel.ForEachAsync(installed, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (item, cancellationToken) =>
    {
        var url = item.Value.GetProperty("resolved").GetString()!;
        var integrity = item.Value.GetProperty("integrity").GetString()!;
        var content = await DownloadWithRetry(httpClient, url, cancellationToken);
        VerifyIntegrity(url, content, integrity);
        Extract(content, Path.Combine(nodeModules, item.Key.Replace('/', Path.DirectorySeparatorChar)));
    });

    File.WriteAllText(stampPath, stamp);
}

static List<(string Name, JsonElement Entry)> Resolve(JsonElement lockRoot, List<(string Name, JsonElement Entry)> chain, string name)
{
    for (var i = chain.Count; i >= 0; i--)
    {
        var dependencies = i == 0 ? lockRoot : chain[i - 1].Entry.TryGetProperty("dependencies", out var nested) ? nested : default;
        if (dependencies.ValueKind is JsonValueKind.Object && dependencies.TryGetProperty(name, out var entry))
            return [.. chain.Take(i), (name, entry)];
    }

    throw new InvalidOperationException($"Cannot resolve the npm package '{name}' from '{string.Join('/', chain.Select(item => item.Name))}'");
}

static async Task<byte[]> DownloadWithRetry(HttpClient httpClient, string url, CancellationToken cancellationToken)
{
    for (var attempt = 0; ; attempt++)
    {
        try
        {
            return await httpClient.GetByteArrayAsync(url, cancellationToken);
        }
        catch (HttpRequestException) when (attempt < 4)
        {
            await Task.Delay(TimeSpan.FromSeconds(2 << attempt), cancellationToken);
        }
    }
}

static void VerifyIntegrity(string url, byte[] content, string integrity)
{
    // Subresource Integrity: "<algorithm>-<base64 hash>"
    var separator = integrity.IndexOf('-', StringComparison.Ordinal);
    var expected = integrity[(separator + 1)..];
    var actual = integrity[..separator] switch
    {
#pragma warning disable CA5350 // Most entries of a lockfileVersion 1 package-lock.json only have a SHA-1 hash
        "sha1" => Convert.ToBase64String(SHA1.HashData(content)),
#pragma warning restore CA5350
        "sha512" => Convert.ToBase64String(SHA512.HashData(content)),
        var algorithm => throw new InvalidOperationException($"Unsupported integrity algorithm '{algorithm}' for {url}"),
    };
    if (actual != expected)
        throw new InvalidOperationException($"The integrity of {url} does not match: expected {integrity}");
}

// The files of a tarball are in a top-level directory ('package' for most packages): it is removed. System.Formats.Tar
// rejects the headers of some old npm tarballs (numeric fields it cannot parse), so the format is read here: 512-byte
// headers, pax ('x') and GNU ('L') long names.
static void Extract(byte[] content, string destination)
{
    using var gzip = new GZipStream(new MemoryStream(content), CompressionMode.Decompress);
    using var tar = new MemoryStream();
    gzip.CopyTo(tar);
    var data = tar.GetBuffer().AsSpan(0, (int)tar.Length);
    var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
    string? longName = null;
    for (var offset = 0; offset + 512 <= data.Length;)
    {
        var header = data.Slice(offset, 512);
        if (!header.ContainsAnyExcept((byte)0))
            break;

        var size = (int)ParseOctal(header.Slice(124, 12));
        var body = data.Slice(offset + 512, size);
        offset += 512 + ((size + 511) & ~511);
        var type = (char)header[156];
        var name = longName ?? ReadString(header[..100]);
        longName = null;
        if (header.Slice(257, 5).SequenceEqual("ustar"u8) && header[345] != 0 && name == ReadString(header[..100]))
        {
            name = ReadString(header.Slice(345, 155)) + "/" + name;
        }

        switch (type)
        {
            case 'x':
                longName = ReadPaxPath(body);
                break;
            case 'L':
                longName = ReadString(body);
                break;
            case '0' or '\0':
                var separator = name.IndexOf('/', StringComparison.Ordinal);
                if (separator < 0)
                    break;

                var path = Path.GetFullPath(Path.Combine(destination, name[(separator + 1)..]));
                if (!path.StartsWith(root, StringComparison.Ordinal))
                    throw new InvalidOperationException($"The tarball entry '{name}' is outside of the package");

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, body);
                break;
        }
    }

    static string ReadString(ReadOnlySpan<byte> value)
    {
        var end = value.IndexOf((byte)0);
        return System.Text.Encoding.UTF8.GetString(end < 0 ? value : value[..end]);
    }

    static long ParseOctal(ReadOnlySpan<byte> value)
    {
        long result = 0;
        foreach (var c in value)
        {
            if (c is >= (byte)'0' and <= (byte)'7')
            {
                result = (result * 8) + (c - '0');
            }
            else if (c is 0 && result > 0)
            {
                break;
            }
        }

        return result;
    }

    // Records "<length> <key>=<value>\n"
    static string? ReadPaxPath(ReadOnlySpan<byte> body)
    {
        var text = System.Text.Encoding.UTF8.GetString(body);
        foreach (var record in text.Split('\n'))
        {
            var space = record.IndexOf(' ', StringComparison.Ordinal);
            if (space >= 0 && record[(space + 1)..].StartsWith("path=", StringComparison.Ordinal))
                return record[(space + 6)..];
        }

        return null;
    }
}

static string? FindRoot(string directory)
{
    for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "Meziantou.Framework.slnx")))
            return current.FullName;
    }

    return null;
}

static void Initialize(string target, string repository)
{
    if (Directory.Exists(Path.Combine(target, ".git")))
        return;

    if (Directory.Exists(target))
    {
        Directory.Delete(target, recursive: true);
    }

    Directory.CreateDirectory(target);
    Git(target, "init -q");
    Git(target, $"remote add origin https://github.com/{repository}.git");
    Git(target, "config core.sparseCheckout true");
    Git(target, "config core.sparseCheckoutCone false");
    Git(target, "config core.symlinks false");
    Git(target, "config core.longpaths true");
    Git(target, "config core.autocrlf false");
    Git(target, "config gc.auto 0");
}

static void Sync(string target, string repository, string commit, string[] paths)
{
    var stampPath = Path.Combine(target, ".git", "fetch-tests-stamp");
    var stamp = commit + "\n" + string.Join('\n', paths);
    if (File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
    {
        Console.WriteLine($"{Path.GetFileName(target)} is up to date ({commit})");
        return;
    }

    Console.WriteLine($"Fetching {repository} at {commit}");
    var head = TryGit(target, "rev-parse -q --verify HEAD")?.Trim();
    if (head != commit)
    {
        GitWithRetry(target, $"fetch -q --depth 1 --filter=blob:none origin {commit}");
    }

    File.WriteAllLines(Path.Combine(target, ".git", "info", "sparse-checkout"), paths.Select(p => "/" + p.TrimStart('/')));
    if (head != commit)
    {
        GitWithRetry(target, $"checkout -q -f --detach {commit}");
        Git(target, "reflog expire --expire=now --all");
        Git(target, "gc -q --prune=now");
    }

    GitWithRetry(target, "sparse-checkout reapply");
    foreach (var path in paths)
    {
        if (!Path.Exists(Path.Combine(target, path)))
            throw new InvalidOperationException($"'{path}' does not exist in {repository} at {commit}");
    }

    File.WriteAllText(stampPath, stamp);
}

static void GitWithRetry(string directory, string arguments)
{
    for (var attempt = 0; ; attempt++)
    {
        try
        {
            Git(directory, arguments);
            return;
        }
        catch (InvalidOperationException) when (attempt < 4)
        {
            var delay = TimeSpan.FromSeconds(2 << attempt);
            Console.WriteLine($"git {arguments} failed, retrying in {delay.TotalSeconds}s");
            Thread.Sleep(delay);
        }
    }
}

static void Git(string directory, string arguments)
{
    if (TryGit(directory, arguments) is null)
        throw new InvalidOperationException($"git {arguments} failed");
}

static string? TryGit(string directory, string arguments)
{
    var startInfo = new ProcessStartInfo("git", $"-C \"{directory}\" {arguments}")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    using var process = Process.Start(startInfo)!;
    var errorTask = process.StandardError.ReadToEndAsync();
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    var error = errorTask.GetAwaiter().GetResult();
    if (process.ExitCode != 0)
    {
        Console.Error.Write(error);
        return null;
    }

    return output;
}
