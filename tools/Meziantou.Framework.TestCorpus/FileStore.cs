using System.Globalization;

namespace Meziantou.Framework.TestCorpus;

/// <summary>
/// A directory of downloaded files named by their hash (npm tarballs, the files of a conformance corpus), whose snapshot
/// is in the mirror. The fetcher reads a file from the directory when it has it, and downloads it from upstream else, so
/// the mirror only avoids the downloads. The directory is a git working copy of the snapshot; the files that are not in
/// the snapshot are untracked.
/// </summary>
/// <remarks>
/// GitHub rejects the files larger than 100 MB (and warns above 50 MB): a larger file is stored in the snapshot as parts
/// of <see cref="PartSize"/> bytes, <c>&lt;name&gt;.part-000</c>, <c>&lt;name&gt;.part-001</c>..., which
/// <see cref="TryFetch"/> joins into the file. The readers check the files against their hashes, so a part is not checked.
/// </remarks>
public static class FileStore
{
    /// <summary>The size of the parts of a large file.</summary>
    public const int PartSize = 45 * 1024 * 1024;

    private const string StampFileName = "fetch-tests-stamp";
    private const string PartSeparator = ".part-";

    /// <summary>Checks out the snapshot of a pin in a directory. Returns <see langword="false"/>, with a warning, when the mirror has no snapshot of the pin or cannot be fetched.</summary>
    /// <param name="directory">The directory of the files. Its existing files are kept.</param>
    /// <param name="mirror">The mirror.</param>
    /// <param name="name">The name of the files in the mirror.</param>
    /// <param name="pin">The pin of the files.</param>
    public static bool TryFetch(string directory, CorpusMirror mirror, string name, IReadOnlyList<string> pin)
    {
        var stamp = string.Join('\n', pin);
        var stampPath = Path.Combine(directory, ".git", StampFileName);
        if (File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
        {
            Console.WriteLine($"{name} is up to date");
            return true;
        }

        var source = mirror.GetSource(name, pin);
        if (source is null)
            return false;

        try
        {
            SparseClone.Initialize(directory, source.Url, keepFiles: true);
            SparseClone.Checkout(directory, [source], _ => SparseClone.AllFiles);
        }
        catch (InvalidOperationException)
        {
            Log.Warning($"Cannot fetch {name} from {mirror.Name}: the files are downloaded from upstream");
            return false;
        }

        JoinParts(directory);
        File.WriteAllText(stampPath, stamp);
        return true;
    }

    /// <summary>Creates the snapshot of files of a directory, publishes it to the mirror and checks it out.</summary>
    /// <param name="directory">The directory of the files.</param>
    /// <param name="mirror">The mirror.</param>
    /// <param name="name">The name of the files in the mirror.</param>
    /// <param name="pin">The pin of the files.</param>
    /// <param name="files">The paths of the files, relative to <paramref name="directory"/>.</param>
    /// <param name="description">The first line of the message of the snapshot.</param>
    public static void Publish(string directory, CorpusMirror mirror, string name, IReadOnlyList<string> pin, IEnumerable<string> files, string description)
    {
        SparseClone.Initialize(directory, mirror.Url, keepFiles: true);
        var snapshotFiles = new List<string>();
        foreach (var file in files)
        {
            snapshotFiles.AddRange(SplitIntoParts(directory, file));
        }

        var snapshot = SparseClone.CreateFileSnapshot(directory, description + "\n\nPin:\n" + string.Concat(pin.Select(item => "- " + item + "\n")), snapshotFiles);
        mirror.Publish(directory, name, pin, snapshot);

        // The working copy is the snapshot, which is in the repository: nothing is downloaded
        File.WriteAllLines(Path.Combine(directory, ".git", "info", "sparse-checkout"), SparseClone.AllFiles);
        Git.Run(directory, "remote", "set-url", "origin", mirror.Url);
        Git.Run(directory, "checkout", "-q", "-f", "--detach", snapshot);
        File.WriteAllText(Path.Combine(directory, ".git", StampFileName), string.Join('\n', pin));
    }

    // The file, or its parts when it is larger than PartSize (written next to it)
    private static List<string> SplitIntoParts(string directory, string file)
    {
        var path = Path.Combine(directory, file);
        var length = new FileInfo(path).Length;
        if (length <= PartSize)
            return [file];

        var parts = new List<string>();
        var buffer = new byte[PartSize];
        using var stream = File.OpenRead(path);
        for (var index = 0; index * (long)PartSize < length; index++)
        {
            var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var part = file + PartSeparator + index.ToString("D3", CultureInfo.InvariantCulture);
            File.WriteAllBytes(Path.Combine(directory, part), buffer.AsSpan(0, count));
            parts.Add(part);
        }

        return parts;
    }

    // Joins the parts of the large files whose file is missing or does not have the size of its parts
    private static void JoinParts(string directory)
    {
        var groups = Directory.EnumerateFiles(directory, "*" + PartSeparator + "*")
            .Select(Path.GetFileName)
            .GroupBy(part => part![..part!.LastIndexOf(PartSeparator, StringComparison.Ordinal)], StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var parts = group.Order(StringComparer.Ordinal).Select(part => Path.Combine(directory, part!)).ToList();
            var path = Path.Combine(directory, group.Key);
            if (File.Exists(path) && new FileInfo(path).Length == parts.Sum(part => new FileInfo(part).Length))
                continue;

            var temporary = path + ".join.tmp";
            using (var output = File.Create(temporary))
            {
                foreach (var part in parts)
                {
                    using var input = File.OpenRead(part);
                    input.CopyTo(output);
                }
            }

            File.Move(temporary, path, overwrite: true);
        }
    }
}
