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
// The arguments restrict the suites to fetch: 'dotnet run eng/javascriptengine/fetch-tests.cs -- web-tooling-benchmark'.
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
var externalPath = Path.Combine(root, "tests", "Meziantou.Framework.JavaScriptEngine.External");
foreach (var pinsPath in pinsPaths)
{
    using var pins = JsonDocument.Parse(File.ReadAllText(pinsPath));
    foreach (var suite in pins.RootElement.EnumerateObject())
    {
        if (args.Length > 0 && !args.Contains(suite.Name, StringComparer.Ordinal))
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

return 0;

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
