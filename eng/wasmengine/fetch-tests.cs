#!/usr/bin/env dotnet
#:sdk Meziantou.NET.Sdk
#:project ../../tools/Meziantou.Framework.TestCorpus/Meziantou.Framework.TestCorpus.csproj
// Materializes tests/Meziantou.Framework.WasmEngine.External/<suite> at the commits and paths pinned in
// tests/Meziantou.Framework.WasmEngine.SpecTests/testsuite.json (the official WebAssembly test suite, https://github.com/WebAssembly/testsuite).
// Each suite is a shallow, blob-less, sparse git clone, so changing the commit only downloads the files
// that changed. A stamp file makes a run with unchanged pins a no-op. A path may be a pattern ("*.wast").
//
// The suites are fetched from their snapshots in the mirror repository of eng/wasmengine/corpus.mirror.json,
// else from upstream, with a warning (tools/Meziantou.Framework.TestCorpus/readme.md).
//
// Usage: dotnet run eng/wasmengine/fetch-tests.cs [-- [--publish] [--mirror <owner/name|url|path>]]
//   --publish  fetches the suites that the mirror has no snapshot of from upstream, pushes their snapshots, and writes eng/wasmengine/corpus.mirror.json
//   --mirror   uses another mirror repository (a fork, or a local repository to test the mirror)
using System.Text.Json;
using Meziantou.Framework.TestCorpus;

const string Usage = "dotnet run eng/wasmengine/fetch-tests.cs [-- [--publish] [--mirror <owner/name|url|path>]]";
var root = FindRoot(Environment.CurrentDirectory) ?? FindRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Cannot find the root of the repository (Meziantou.Framework.slnx)");
var pinsPath = Path.Combine(root, "tests", "Meziantou.Framework.WasmEngine.SpecTests", "testsuite.json");
var externalPath = Path.Combine(root, "tests", "Meziantou.Framework.WasmEngine.External");

var publish = false;
string? mirrorOverride = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--publish":
            publish = true;
            break;

        case "--mirror" when i + 1 < args.Length:
            mirrorOverride = args[++i];
            break;

        default:
            Console.Error.WriteLine($"Unknown argument '{args[i]}'. Usage: {Usage}");
            return 1;
    }
}

var mirror = CorpusMirror.Load(Path.Combine(root, "eng", "wasmengine", "corpus.mirror.json"), "dotnet run eng/wasmengine/fetch-tests.cs -- --publish", mirrorOverride);
using var pins = JsonDocument.Parse(File.ReadAllText(pinsPath));
foreach (var suite in pins.RootElement.EnumerateObject())
{
    var repository = suite.Value.GetProperty("repository").GetString()!;
    var commit = suite.Value.GetProperty("commit").GetString()!;
    var paths = suite.Value.GetProperty("paths").EnumerateArray().Select(p => p.GetString()!).ToArray();
    var target = Path.Combine(externalPath, suite.Name);
    var upstream = CorpusSource.Upstream(repository, commit);
    string[] pin = [$"{repository}@{commit}", .. paths];
    SparseClone.Initialize(target, upstream.Url);
    if (publish && !mirror.HasSnapshot(suite.Name, pin))
    {
        Sync(target, commit, paths, [upstream]);
        mirror.Publish(target, suite.Name, pin, SparseClone.CreateSnapshot(target, SparseClone.GetSnapshotMessage(repository, commit, paths)));
    }
    else
    {
        Sync(target, commit, paths, mirror.GetSource(suite.Name, pin) is { } snapshot ? [snapshot, upstream] : [upstream]);
    }
}

if (publish)
{
    mirror.Retain(pins.RootElement.EnumerateObject().Select(suite => suite.Name));
    mirror.Save();
}

return 0;

static string? FindRoot(string directory)
{
    for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "Meziantou.Framework.slnx")))
            return current.FullName;
    }

    return null;
}

static void Sync(string target, string commit, string[] paths, IReadOnlyList<CorpusSource> sources)
{
    var stampPath = Path.Combine(target, ".git", "fetch-tests-stamp");
    var stamp = commit + "\n" + string.Join('\n', paths);
    if (File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
    {
        Console.WriteLine($"{Path.GetFileName(target)} is up to date ({commit})");
        return;
    }

    var source = SparseClone.Checkout(target, sources, source => source.IsSnapshot ? SparseClone.AllFiles : paths.Select(p => "/" + p.TrimStart('/')));
    foreach (var path in paths.Where(p => !p.Contains('*', StringComparison.Ordinal)))
    {
        if (!Path.Exists(Path.Combine(target, path)))
            throw new InvalidOperationException($"'{path}' does not exist in {source.Name} at {source.Commit}");
    }

    File.WriteAllText(stampPath, stamp);
}
