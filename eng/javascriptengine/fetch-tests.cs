#!/usr/bin/env dotnet
#:sdk Meziantou.NET.Sdk
// Materializes tests/Meziantou.Framework.JavaScriptEngine.External/<suite> at the commits and paths pinned in
// tests/Meziantou.Framework.JavaScriptEngine.Test262/test262.json (test262 and test262-parser-tests). Each suite is a
// shallow, blob-less, sparse git clone, so changing the commit only downloads the files that changed. A stamp file makes
// a run with unchanged pins a no-op. The suites marked "optional" (parse-number-fxx-test-data) are only fetched when
// named on the command line: dotnet run eng/javascriptengine/fetch-tests.cs parse-number-fxx-test-data
using System.Diagnostics;
using System.Text.Json;

var root = FindRoot(Environment.CurrentDirectory) ?? FindRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Cannot find the root of the repository (Meziantou.Framework.slnx)");
var pinsPath = Path.Combine(root, "tests", "Meziantou.Framework.JavaScriptEngine.Test262", "test262.json");
var externalPath = Path.Combine(root, "tests", "Meziantou.Framework.JavaScriptEngine.External");
using var pins = JsonDocument.Parse(File.ReadAllText(pinsPath));
var requestedSuites = args.ToHashSet(StringComparer.Ordinal);
foreach (var name in requestedSuites)
{
    if (!pins.RootElement.TryGetProperty(name, out _))
        throw new InvalidOperationException($"'{name}' is not a suite of {pinsPath}");
}

foreach (var suite in pins.RootElement.EnumerateObject())
{
    var optional = suite.Value.TryGetProperty("optional", out var optionalValue) && optionalValue.GetBoolean();
    if (optional && !requestedSuites.Contains(suite.Name))
        continue;

    var repository = suite.Value.GetProperty("repository").GetString()!;
    var commit = suite.Value.GetProperty("commit").GetString()!;
    var paths = suite.Value.GetProperty("paths").EnumerateArray().Select(p => p.GetString()!).ToArray();
    var target = Path.Combine(externalPath, suite.Name);
    Initialize(target, repository);
    Sync(target, repository, commit, paths);
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
