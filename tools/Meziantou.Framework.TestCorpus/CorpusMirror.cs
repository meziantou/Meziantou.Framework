using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.TestCorpus;

/// <summary>
/// The mirror of the corpora of a library, read from its lock file (<c>corpus.mirror.json</c>): the mirror repository,
/// and the snapshot of each pin. A pin is a list of strings that identifies the content of a corpus (its upstream commit
/// and paths, the hashes of its files, ...): a snapshot is used only for the pin it was published for.
/// </summary>
public sealed class CorpusMirror
{
    private readonly string _lockPath;
    private readonly string _publishCommand;
    private readonly SortedDictionary<string, (string[] Pin, string Snapshot)> _snapshots;

    private CorpusMirror(string lockPath, string publishCommand, string repository, string? repositoryOverride, SortedDictionary<string, (string[] Pin, string Snapshot)> snapshots)
    {
        _lockPath = lockPath;
        _publishCommand = publishCommand;
        _snapshots = snapshots;
        Repository = repository;
        Name = repositoryOverride ?? repository;
        Url = CorpusSource.ToUrl(Name);
    }

    /// <summary>Gets the mirror repository of the lock file (owner/name).</summary>
    public string Repository { get; }

    /// <summary>Gets the mirror repository that is used: the one of the lock file, or the override.</summary>
    public string Name { get; }

    /// <summary>Gets the URL of the mirror repository that is used.</summary>
    public string Url { get; }

    /// <summary>Reads a lock file.</summary>
    /// <param name="lockPath">The path of the lock file.</param>
    /// <param name="publishCommand">The command that publishes the snapshots, for the warnings.</param>
    /// <param name="repositoryOverride">Another mirror repository (owner/name, URL or path): a fork, or a local repository to test the mirror.</param>
    public static CorpusMirror Load(string lockPath, string publishCommand, string? repositoryOverride = null)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
        var snapshots = new SortedDictionary<string, (string[] Pin, string Snapshot)>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.GetProperty("snapshots").EnumerateObject())
        {
            snapshots[entry.Name] = ([.. entry.Value.GetProperty("pin").EnumerateArray().Select(item => item.GetString()!)], entry.Value.GetProperty("snapshot").GetString()!);
        }

        return new CorpusMirror(lockPath, publishCommand, document.RootElement.GetProperty("repository").GetString()!, repositoryOverride, snapshots);
    }

    /// <summary>Gets the pin of the files of a pin file: its name and the SHA-256 of its content, with LF line endings.</summary>
    public static string HashFile(string path)
    {
        var content = File.ReadAllText(path).ReplaceLineEndings("\n");
        return $"{Path.GetFileName(path)} sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))}";
    }

    /// <summary>Gets the snapshot of a pin, or <see langword="null"/> with a warning when the mirror has none.</summary>
    /// <param name="name">The name of the corpus: the tag of its snapshot is <c>&lt;name&gt;/&lt;snapshot&gt;</c>.</param>
    /// <param name="pin">The pin.</param>
    public CorpusSource? GetSource(string name, IReadOnlyList<string> pin)
    {
        if (_snapshots.TryGetValue(name, out var entry) && entry.Pin.SequenceEqual(pin, StringComparer.Ordinal))
            return new CorpusSource(Name, Url, $"refs/tags/{name}/{entry.Snapshot}", entry.Snapshot, IsSnapshot: true);

        Log.Warning($"The mirror has no snapshot of {name} for its pin ({pin[0]}): it is fetched from upstream. '{_publishCommand}' publishes it.");
        return null;
    }

    /// <summary>
    /// Gets whether the lock file has a snapshot for a pin and the mirror has its tag. <c>--publish</c> fetches such a
    /// corpus from the mirror instead of upstream, so publishing does not depend on the upstream of the pins that did not
    /// change.
    /// </summary>
    /// <param name="name">The name of the corpus.</param>
    /// <param name="pin">The pin.</param>
    public bool HasSnapshot(string name, IReadOnlyList<string> pin)
    {
        if (!_snapshots.TryGetValue(name, out var entry) || !entry.Pin.SequenceEqual(pin, StringComparer.Ordinal))
            return false;

        var tag = $"refs/tags/{name}/{entry.Snapshot}";
        if (!Git.RunWithRetry(Environment.CurrentDirectory, "ls-remote", Url, tag).StartsWith(entry.Snapshot, StringComparison.Ordinal))
            return false;

        Console.WriteLine($"{Name} has the snapshot of {name} ({entry.Snapshot})");
        return true;
    }

    /// <summary>Pushes a snapshot to the mirror, unless the mirror has it, and records it in the lock file (see <see cref="Save"/>).</summary>
    /// <param name="directory">The git repository that has the snapshot.</param>
    /// <param name="name">The name of the corpus.</param>
    /// <param name="pin">The pin of the snapshot.</param>
    /// <param name="snapshot">The snapshot commit.</param>
    public void Publish(string directory, string name, IReadOnlyList<string> pin, string snapshot)
    {
        // The tag contains the snapshot: an existing tag is the same snapshot (the tags of the mirror cannot be moved)
        var tag = $"{name}/{snapshot}";
        if (Git.RunWithRetry(directory, "ls-remote", Url, "refs/tags/" + tag).Length > 0)
        {
            Console.WriteLine($"{Name} already has {tag}");
        }
        else
        {
            // A snapshot fetched from a mirror (--depth 1) is a shallow commit, which git does not push. It has no parent:
            // its history is complete.
            var shallowPath = Path.Combine(directory, ".git", "shallow");
            if (File.Exists(shallowPath))
            {
                File.WriteAllLines(shallowPath, File.ReadAllLines(shallowPath).Where(line => line != snapshot));
            }

            Console.WriteLine($"Pushing {tag} to {Name}");
            Git.RunWithRetry(directory, "push", "-q", Url, $"{snapshot}:refs/tags/{tag}");
        }

        _snapshots[name] = ([.. pin], snapshot);
    }

    /// <summary>Removes the snapshots of the corpora that are not pinned anymore from the lock file.</summary>
    public void Retain(IEnumerable<string> names)
    {
        var retained = names.ToHashSet(StringComparer.Ordinal);
        foreach (var name in _snapshots.Keys.Where(name => !retained.Contains(name)).ToList())
        {
            _snapshots.Remove(name);
        }
    }

    /// <summary>Writes the lock file.</summary>
    public void Save()
    {
        using var stream = File.Create(_lockPath);
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("repository", Repository);
            writer.WriteStartObject("snapshots");
            foreach (var (name, (pin, snapshot)) in _snapshots)
            {
                writer.WriteStartObject(name);
                writer.WriteStartArray("pin");
                foreach (var item in pin)
                {
                    writer.WriteStringValue(item);
                }

                writer.WriteEndArray();
                writer.WriteString("snapshot", snapshot);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        Console.WriteLine($"Wrote {_lockPath}");
    }
}
