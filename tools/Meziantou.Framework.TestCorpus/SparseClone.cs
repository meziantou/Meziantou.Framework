using System.Text;

namespace Meziantou.Framework.TestCorpus;

/// <summary>
/// The working copy of a corpus: a shallow, blob-less, sparse git clone, so changing the commit only downloads the files
/// that changed. The files of a snapshot are the files of the upstream commit, so their objects are the same and the
/// sources can alternate without downloading the files again.
/// </summary>
public static class SparseClone
{
    private const string SnapshotAuthor = "Meziantou.Framework test corpus";
    private const string SnapshotEmail = "test-corpus@users.noreply.github.com";
    private const string SnapshotDate = "2000-01-01T00:00:00+0000";

    /// <summary>Gets the sparse checkout pattern of a snapshot: all its files, which are the files of the pin.</summary>
    public static IEnumerable<string> AllFiles { get; } = ["/*"];

    /// <summary>Creates the git repository of a corpus if it does not exist yet.</summary>
    /// <param name="target">The directory of the corpus.</param>
    /// <param name="url">The URL of the first remote.</param>
    /// <param name="keepFiles">Whether the files of an existing directory without git repository are kept (else it is deleted).</param>
    /// <param name="excludes">The patterns of the untracked files that git ignores (<c>.git/info/exclude</c>).</param>
    public static void Initialize(string target, string url, bool keepFiles = false, IEnumerable<string>? excludes = null)
    {
        var gitDirectory = Path.Combine(target, ".git");
        if (Directory.Exists(gitDirectory))
            return;

        if (Directory.Exists(target) && !keepFiles)
        {
            Directory.Delete(target, recursive: true);
        }

        Directory.CreateDirectory(target);
        Git.Run(target, "init", "-q");
        Git.Run(target, "remote", "add", "origin", url);
        Git.Run(target, "config", "core.sparseCheckout", "true");
        Git.Run(target, "config", "core.sparseCheckoutCone", "false");
        Git.Run(target, "config", "core.symlinks", "false");
        Git.Run(target, "config", "core.longpaths", "true");
        Git.Run(target, "config", "core.autocrlf", "false");
        Git.Run(target, "config", "gc.auto", "0");
        if (excludes is not null)
        {
            File.AppendAllLines(Path.Combine(gitDirectory, "info", "exclude"), excludes);
        }
    }

    /// <summary>Gets the commit of the working copy, or <see langword="null"/> when nothing is checked out.</summary>
    public static string? GetHead(string target) => Git.TryRun(target, "rev-parse", "-q", "--verify", "HEAD")?.Trim();

    /// <summary>
    /// Checks out the first source that can be fetched, unless the working copy is already one of them: then only its
    /// sparse checkout patterns are applied. <c>origin</c> is the URL of the source of HEAD, so the blobs that a change of
    /// the patterns adds are fetched from it.
    /// </summary>
    /// <param name="target">The directory of the corpus.</param>
    /// <param name="sources">The sources, in order of preference.</param>
    /// <param name="patterns">The sparse checkout patterns of a source (gitignore syntax), computed once its commit is fetched.</param>
    /// <returns>The source that is checked out.</returns>
    /// <exception cref="InvalidOperationException">No source can be fetched.</exception>
    public static CorpusSource Checkout(string target, IReadOnlyList<CorpusSource> sources, Func<CorpusSource, IEnumerable<string>> patterns)
    {
        var head = GetHead(target);
        var current = sources.FirstOrDefault(source => source.Commit == head);
        if (current is not null)
        {
            Git.Run(target, "remote", "set-url", "origin", current.Url);
            Reapply(target, patterns(current));
            return current;
        }

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            Console.WriteLine($"Fetching {Path.GetFileName(target)} from {source.Name} at {source.Commit}");
            Git.Run(target, "remote", "set-url", "origin", source.Url);
            if (TryCheckout(target, source, patterns))
                return source;

            if (i + 1 < sources.Count)
            {
                Log.Warning($"Cannot fetch {Path.GetFileName(target)} from {source.Name}: it is fetched from {sources[i + 1].Name}");
            }
        }

        throw new InvalidOperationException($"Cannot fetch {Path.GetFileName(target)} from {string.Join(" or ", sources.Select(source => source.Name))}");
    }

    /// <summary>Applies new sparse checkout patterns to the working copy: the blobs of the files they add are fetched from <c>origin</c>.</summary>
    public static void Reapply(string target, IEnumerable<string> patterns)
    {
        File.WriteAllLines(Path.Combine(target, ".git", "info", "sparse-checkout"), patterns);
        Git.RunWithRetry(target, "sparse-checkout", "reapply");
    }

    private static bool TryCheckout(string target, CorpusSource source, Func<CorpusSource, IEnumerable<string>> patterns)
    {
        try
        {
            Git.RunWithRetry(target, "fetch", "-q", "--no-tags", "--depth", "1", "--filter=blob:none", "origin", source.Reference);
            var fetched = Git.Run(target, "rev-parse", "FETCH_HEAD^{commit}").Trim();
            if (fetched != source.Commit)
            {
                Console.Error.WriteLine($"{source.Reference} of {source.Name} is {fetched}, not {source.Commit}");
                return false;
            }

            File.WriteAllLines(Path.Combine(target, ".git", "info", "sparse-checkout"), patterns(source));
            Git.RunWithRetry(target, "checkout", "-q", "-f", "--detach", source.Commit);

            // A shallow clone only keeps the commit that is checked out
            Git.Run(target, "reflog", "expire", "--expire=now", "--all");
            Git.Run(target, "gc", "-q", "--prune=now");
            Git.RunWithRetry(target, "sparse-checkout", "reapply");
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Gets the message of the snapshot of a commit of a repository.</summary>
    public static string GetSnapshotMessage(string repository, string commit, IEnumerable<string> paths)
    {
        var message = new StringBuilder($"Snapshot of {repository} at {commit}\n\nPaths:\n");
        foreach (var path in paths)
        {
            message.Append("- ").Append(path).Append('\n');
        }

        return message.ToString();
    }

    /// <summary>
    /// Creates the snapshot of the working copy: a commit without parent of the files of its sparse checkout (the entries
    /// of the index that are not skip-worktree, without the submodules, with their modes and objects), and of additional
    /// files. Deterministic (fixed author, dates and message, no signature): the same files give the same commit.
    /// </summary>
    /// <param name="target">The directory of the corpus.</param>
    /// <param name="message">The message of the commit (<see cref="GetSnapshotMessage"/>).</param>
    /// <param name="additionalFiles">The files to add, by their path in the snapshot (relative to <paramref name="target"/>).</param>
    /// <returns>The snapshot commit.</returns>
    public static string CreateSnapshot(string target, string message, IEnumerable<string>? additionalFiles = null)
    {
        var entries = new StringBuilder();
        foreach (var entry in Git.Run(target, "ls-files", "-t", "-s", "-z").Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // <tag> <mode> <object> <stage>\t<path>
            var tab = entry.IndexOf('\t', StringComparison.Ordinal);
            var fields = entry[..tab].Split(' ');
            if (fields[0] is "S" || fields[1] is "160000")
                continue;

            entries.Append(fields[1]).Append(' ').Append(fields[2]).Append('\t').Append(entry.AsSpan(tab + 1)).Append('\0');
        }

        AppendFiles(target, entries, additionalFiles ?? []);
        return CommitTree(target, entries.ToString(), message);
    }

    /// <summary>
    /// Creates a snapshot of files of the directory (for instance, the files of a corpus named by their hash). Deterministic:
    /// the same files give the same commit.
    /// </summary>
    /// <param name="target">The git repository, whose directory has the files.</param>
    /// <param name="message">The message of the commit.</param>
    /// <param name="files">The paths of the files, relative to <paramref name="target"/>.</param>
    /// <returns>The snapshot commit.</returns>
    public static string CreateFileSnapshot(string target, string message, IEnumerable<string> files)
    {
        var entries = new StringBuilder();
        AppendFiles(target, entries, files);
        return CommitTree(target, entries.ToString(), message);
    }

    private static void AppendFiles(string target, StringBuilder entries, IEnumerable<string> files)
    {
        var paths = files.Select(path => path.Replace('\\', '/')).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (paths.Count == 0)
            return;

        var objects = Git.Run(target, environment: null, string.Join('\n', paths) + "\n", "hash-object", "-w", "--no-filters", "--stdin-paths").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < paths.Count; i++)
        {
            entries.Append("100644 ").Append(objects[i]).Append('\t').Append(paths[i]).Append('\0');
        }
    }

    private static string CommitTree(string target, string entries, string message)
    {
        var indexPath = Path.Combine(target, ".git", "snapshot-index");
        File.Delete(indexPath);
        var indexEnvironment = new Dictionary<string, string>(StringComparer.Ordinal) { ["GIT_INDEX_FILE"] = indexPath };
        Git.Run(target, indexEnvironment, entries, "update-index", "--add", "-z", "--index-info");
        var tree = Git.Run(target, indexEnvironment, input: null, "write-tree").Trim();
        File.Delete(indexPath);

        var commitEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_AUTHOR_NAME"] = SnapshotAuthor,
            ["GIT_AUTHOR_EMAIL"] = SnapshotEmail,
            ["GIT_AUTHOR_DATE"] = SnapshotDate,
            ["GIT_COMMITTER_NAME"] = SnapshotAuthor,
            ["GIT_COMMITTER_EMAIL"] = SnapshotEmail,
            ["GIT_COMMITTER_DATE"] = SnapshotDate,
        };

        // A signature would make the commit depend on who creates it
        var snapshot = Git.Run(target, commitEnvironment, message, "-c", "i18n.commitEncoding=UTF-8", "commit-tree", "--no-gpg-sign", tree).Trim();
        Console.WriteLine($"Snapshot of {Path.GetFileName(target)}: {snapshot}");
        return snapshot;
    }
}
