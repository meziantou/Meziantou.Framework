#!/usr/bin/env dotnet
#:sdk Meziantou.NET.Sdk
// Materializes tests/Meziantou.Framework.Pdf.External/<suite> at the commits and paths pinned in
// tests/Meziantou.Framework.Pdf.Corpus/corpus.json (the test files of pdf.js, PDFium, qpdf, the PDF Association,
// Apache PDFBox and the veraPDF corpus). Each suite is a shallow, blob-less, sparse git clone, so changing the commit only downloads the files
// that changed. A stamp file makes a run with unchanged pins a no-op. A path may be a pattern ("*.pdf").
//
// The suites are fetched from a mirror first, so that the tests do not depend on the upstream repositories: the mirror
// repository of tests/Meziantou.Framework.Pdf.Corpus/corpus.mirror.json holds a snapshot of each pin, a commit without
// parent that contains only the pinned paths, tagged <suite>/<snapshot>. corpus.mirror.json maps each pin to its
// snapshot: git checks the objects it receives against their hashes, so the snapshot commit pins the content. A suite
// whose pin has no snapshot, or whose mirror cannot be fetched, is fetched from upstream, with a warning. The files of
// a snapshot are the files of the upstream commit, so their objects are the same and the two sources can alternate
// without downloading the files again.
//
// --publish fetches every suite from upstream, creates its snapshot, pushes the snapshots that the mirror does not
// have, and writes corpus.mirror.json. A snapshot is deterministic (fixed author, dates and message, no signature): the
// same pin always gives the same commit, so anyone can check a snapshot of the mirror against upstream. Pushing
// requires the right to write to the mirror repository.
//
// --mirror <owner/name|url|path> uses another mirror repository (a fork, or a local repository to test the mirror).
//
// Usage: dotnet run eng/pdf/fetch-tests.cs [-- [--publish] [--mirror <owner/name|url|path>]]
using System.Diagnostics;
using System.Text;
using System.Text.Json;

var root = FindRoot(Environment.CurrentDirectory) ?? FindRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Cannot find the root of the repository (Meziantou.Framework.slnx)");
var pinsPath = Path.Combine(root, "tests", "Meziantou.Framework.Pdf.Corpus", "corpus.json");
var mirrorPath = Path.Combine(root, "tests", "Meziantou.Framework.Pdf.Corpus", "corpus.mirror.json");
var externalPath = Path.Combine(root, "tests", "Meziantou.Framework.Pdf.External");

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
            Console.Error.WriteLine($"Unknown argument '{args[i]}'. Usage: dotnet run eng/pdf/fetch-tests.cs [-- [--publish] [--mirror <owner/name|url|path>]]");
            return 1;
    }
}

var mirror = MirrorLock.Load(mirrorPath);
var mirrorName = mirrorOverride ?? mirror.Repository;
var mirrorUrl = ToUrl(mirrorName);
var snapshots = new SortedDictionary<string, MirrorSnapshot>(StringComparer.Ordinal);
using var pins = JsonDocument.Parse(File.ReadAllText(pinsPath));
foreach (var suite in pins.RootElement.EnumerateObject())
{
    var repository = suite.Value.GetProperty("repository").GetString()!;
    var commit = suite.Value.GetProperty("commit").GetString()!;
    var paths = suite.Value.GetProperty("paths").EnumerateArray().Select(p => p.GetString()!).ToArray();
    var target = Path.Combine(externalPath, suite.Name);
    var upstream = new Source(repository, ToUrl(repository), commit, commit);
    Initialize(target, upstream.Url);
    if (publish)
    {
        Sync(target, commit, paths, [upstream]);
        var snapshot = CreateSnapshot(target, repository, commit, paths);
        Publish(target, mirrorName, mirrorUrl, suite.Name + "/" + snapshot, snapshot);
        snapshots[suite.Name] = new MirrorSnapshot(commit, paths, snapshot);
    }
    else if (mirror.Snapshots.TryGetValue(suite.Name, out var snapshot) && snapshot.Matches(commit, paths))
    {
        Sync(target, commit, paths, [new Source(mirrorName, mirrorUrl, $"refs/tags/{suite.Name}/{snapshot.Snapshot}", snapshot.Snapshot), upstream]);
    }
    else
    {
        Warning($"The mirror has no snapshot of {suite.Name} at {commit}: it is fetched from {repository}. 'dotnet run eng/pdf/fetch-tests.cs -- --publish' publishes it.");
        Sync(target, commit, paths, [upstream]);
    }
}

if (publish)
{
    new MirrorLock(mirror.Repository, snapshots).Save(mirrorPath);
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

// A repository of GitHub (owner/name), else a URL or a path that git understands
static string ToUrl(string repository)
{
    if (repository.Contains("://", StringComparison.Ordinal) || Path.IsPathRooted(repository))
        return repository;

    return $"https://github.com/{repository}.git";
}

static void Warning(string message)
{
    if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is "true")
    {
        Console.WriteLine("::warning::" + message);
    }
    else
    {
        Console.Error.WriteLine("warning: " + message);
    }
}

static void Initialize(string target, string url)
{
    if (Directory.Exists(Path.Combine(target, ".git")))
        return;

    if (Directory.Exists(target))
    {
        Directory.Delete(target, recursive: true);
    }

    Directory.CreateDirectory(target);
    Git(target, "init", "-q");
    Git(target, "remote", "add", "origin", url);
    Git(target, "config", "core.sparseCheckout", "true");
    Git(target, "config", "core.sparseCheckoutCone", "false");
    Git(target, "config", "core.symlinks", "false");
    Git(target, "config", "core.longpaths", "true");
    Git(target, "config", "core.autocrlf", "false");
    Git(target, "config", "gc.auto", "0");
}

// Checks out the first source that can be fetched. origin is the URL of the source of HEAD: the blobs that a change of
// the paths adds are fetched from it.
static void Sync(string target, string commit, string[] paths, IReadOnlyList<Source> sources)
{
    var stampPath = Path.Combine(target, ".git", "fetch-tests-stamp");
    var stamp = commit + "\n" + string.Join('\n', paths);
    if (File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
    {
        Console.WriteLine($"{Path.GetFileName(target)} is up to date ({commit})");
        return;
    }

    File.WriteAllLines(Path.Combine(target, ".git", "info", "sparse-checkout"), paths.Select(p => "/" + p.TrimStart('/')));
    var head = TryGit(target, "rev-parse", "-q", "--verify", "HEAD")?.Trim();
    var current = sources.FirstOrDefault(source => source.Commit == head);
    if (current is not null)
    {
        Git(target, "remote", "set-url", "origin", current.Url);
        GitWithRetry(target, "sparse-checkout", "reapply");
    }
    else
    {
        for (var i = 0; i < sources.Count && current is null; i++)
        {
            var source = sources[i];
            Console.WriteLine($"Fetching {source.Name} at {source.Commit}");
            Git(target, "remote", "set-url", "origin", source.Url);
            if (TryCheckout(target, source))
            {
                current = source;
            }
            else if (i + 1 < sources.Count)
            {
                Warning($"Cannot fetch {Path.GetFileName(target)} from {source.Name}: it is fetched from {sources[i + 1].Name}");
            }
        }

        if (current is null)
            throw new InvalidOperationException($"Cannot fetch {Path.GetFileName(target)} at {commit}");
    }

    foreach (var path in paths.Where(p => !p.Contains('*', StringComparison.Ordinal)))
    {
        if (!Path.Exists(Path.Combine(target, path)))
            throw new InvalidOperationException($"'{path}' does not exist in {current.Name} at {current.Commit}");
    }

    File.WriteAllText(stampPath, stamp);
}

static bool TryCheckout(string target, Source source)
{
    try
    {
        GitWithRetry(target, "fetch", "-q", "--no-tags", "--depth", "1", "--filter=blob:none", "origin", source.Reference);
        var fetched = Git(target, "rev-parse", "FETCH_HEAD^{commit}").Trim();
        if (fetched != source.Commit)
        {
            Console.Error.WriteLine($"{source.Reference} of {source.Name} is {fetched}, not {source.Commit}");
            return false;
        }

        GitWithRetry(target, "checkout", "-q", "-f", "--detach", source.Commit);
        Git(target, "reflog", "expire", "--expire=now", "--all");
        Git(target, "gc", "-q", "--prune=now");
        GitWithRetry(target, "sparse-checkout", "reapply");
        return true;
    }
    catch (InvalidOperationException)
    {
        return false;
    }
}

// The commit without parent of the files of the sparse checkout of HEAD: the entries of the index that are not
// skip-worktree, without the submodules, with their modes and objects. Deterministic: the same files give the same commit.
static string CreateSnapshot(string target, string repository, string commit, string[] paths)
{
    var entries = new StringBuilder();
    foreach (var entry in Git(target, "ls-files", "-t", "-s", "-z").Split('\0', StringSplitOptions.RemoveEmptyEntries))
    {
        // <tag> <mode> <object> <stage>\t<path>
        var tab = entry.IndexOf('\t', StringComparison.Ordinal);
        var fields = entry[..tab].Split(' ');
        if (fields[0] is "S" || fields[1] is "160000")
            continue;

        entries.Append(fields[1]).Append(' ').Append(fields[2]).Append('\t').Append(entry.AsSpan(tab + 1)).Append('\0');
    }

    var indexPath = Path.Combine(target, ".git", "snapshot-index");
    File.Delete(indexPath);
    var indexEnvironment = new Dictionary<string, string>(StringComparer.Ordinal) { ["GIT_INDEX_FILE"] = indexPath };
    GitWithInput(target, indexEnvironment, entries.ToString(), "update-index", "--add", "-z", "--index-info");
    var tree = GitWithInput(target, indexEnvironment, input: null, "write-tree").Trim();
    File.Delete(indexPath);

    var message = new StringBuilder($"Snapshot of {repository} at {commit}\n\nPaths:\n");
    foreach (var path in paths)
    {
        message.Append("- ").Append(path).Append('\n');
    }

    var commitEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GIT_AUTHOR_NAME"] = "Meziantou.Framework test corpus",
        ["GIT_AUTHOR_EMAIL"] = "test-corpus@users.noreply.github.com",
        ["GIT_AUTHOR_DATE"] = "2000-01-01T00:00:00+0000",
        ["GIT_COMMITTER_NAME"] = "Meziantou.Framework test corpus",
        ["GIT_COMMITTER_EMAIL"] = "test-corpus@users.noreply.github.com",
        ["GIT_COMMITTER_DATE"] = "2000-01-01T00:00:00+0000",
    };

    // A signature would make the commit depend on who creates it
    var snapshot = GitWithInput(target, commitEnvironment, message.ToString(), "-c", "i18n.commitEncoding=UTF-8", "commit-tree", "--no-gpg-sign", tree).Trim();
    Console.WriteLine($"Snapshot of {Path.GetFileName(target)}: {snapshot}");
    return snapshot;
}

// The tag contains the snapshot: an existing tag is the same snapshot
static void Publish(string target, string mirrorName, string mirrorUrl, string tag, string snapshot)
{
    var existing = GitWithRetry(target, "ls-remote", mirrorUrl, "refs/tags/" + tag);
    if (existing.Length > 0)
    {
        Console.WriteLine($"{mirrorName} already has {tag}");
        return;
    }

    Console.WriteLine($"Pushing {tag} to {mirrorName}");
    GitWithRetry(target, "push", "-q", mirrorUrl, $"{snapshot}:refs/tags/{tag}");
}

static string GitWithRetry(string directory, params string[] arguments)
{
    for (var attempt = 0; ; attempt++)
    {
        try
        {
            return Git(directory, arguments);
        }
        catch (InvalidOperationException) when (attempt < 4)
        {
            var delay = TimeSpan.FromSeconds(2 << attempt);
            Console.WriteLine($"git {string.Join(' ', arguments)} failed, retrying in {delay.TotalSeconds}s");
            Thread.Sleep(delay);
        }
    }
}

static string Git(string directory, params string[] arguments)
{
    return GitWithInput(directory, environment: null, input: null, arguments);
}

static string GitWithInput(string directory, Dictionary<string, string>? environment, string? input, params string[] arguments)
{
    return TryGitWithInput(directory, environment, input, arguments) ?? throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed");
}

static string? TryGit(string directory, params string[] arguments)
{
    return TryGitWithInput(directory, environment: null, input: null, arguments);
}

static string? TryGitWithInput(string directory, Dictionary<string, string>? environment, string? input, params string[] arguments)
{
    var startInfo = new ProcessStartInfo("git")
    {
        RedirectStandardInput = input is not null,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardInputEncoding = input is not null ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) : null,
        StandardOutputEncoding = Encoding.UTF8,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("-C");
    startInfo.ArgumentList.Add(directory);
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    if (environment is not null)
    {
        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }
    }

    using var process = Process.Start(startInfo)!;
    var errorTask = process.StandardError.ReadToEndAsync();
    var outputTask = process.StandardOutput.ReadToEndAsync();
    if (input is not null)
    {
        process.StandardInput.Write(input);
        process.StandardInput.Close();
    }

    process.WaitForExit();
    var output = outputTask.GetAwaiter().GetResult();
    var error = errorTask.GetAwaiter().GetResult();
    if (process.ExitCode != 0)
    {
        Console.Error.Write(error);
        return null;
    }

    return output;
}

sealed record Source(string Name, string Url, string Reference, string Commit);

sealed record MirrorSnapshot(string Commit, string[] Paths, string Snapshot)
{
    public bool Matches(string commit, string[] paths) => Commit == commit && Paths.SequenceEqual(paths, StringComparer.Ordinal);
}

// corpus.mirror.json: the mirror repository, and the snapshot of each pin
sealed record MirrorLock(string Repository, IReadOnlyDictionary<string, MirrorSnapshot> Snapshots)
{
    public static MirrorLock Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var snapshots = new Dictionary<string, MirrorSnapshot>(StringComparer.Ordinal);
        foreach (var suite in document.RootElement.GetProperty("snapshots").EnumerateObject())
        {
            snapshots[suite.Name] = new MirrorSnapshot(
                suite.Value.GetProperty("commit").GetString()!,
                [.. suite.Value.GetProperty("paths").EnumerateArray().Select(p => p.GetString()!)],
                suite.Value.GetProperty("snapshot").GetString()!);
        }

        return new MirrorLock(document.RootElement.GetProperty("repository").GetString()!, snapshots);
    }

    public void Save(string path)
    {
        using var stream = File.Create(path);
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            writer.WriteString("repository", Repository);
            writer.WriteStartObject("snapshots");
            foreach (var (name, snapshot) in Snapshots)
            {
                writer.WriteStartObject(name);
                writer.WriteString("commit", snapshot.Commit);
                writer.WriteStartArray("paths");
                foreach (var item in snapshot.Paths)
                {
                    writer.WriteStringValue(item);
                }

                writer.WriteEndArray();
                writer.WriteString("snapshot", snapshot.Snapshot);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
    }
}
