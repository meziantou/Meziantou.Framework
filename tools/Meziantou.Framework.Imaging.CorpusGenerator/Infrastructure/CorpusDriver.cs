using System.Text;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

/// <param name="Output">Generates into this (new or empty) directory and keeps it, instead of a temporary directory.</param>
/// <param name="IccProfiles">The directory of the pinned checkout of the external ICC profiles (icc generator).</param>
internal sealed record GeneratorOptions(bool Write, bool AcceptToolVersions, string? QoiHeader, string? Output = null, string? IccProfiles = null);

/// <summary>Regenerates (a part of) the corpus in a temporary directory, then reports the differences with the committed
/// corpus (check) or replaces it (write).</summary>
internal static class CorpusDriver
{
    private static readonly string[] InfrastructureFiles = ["README.md", "manifest.json", "manifest.schema.json"];

    public static FullPath RepoRoot { get; } = FindRepositoryRoot();

    public static FullPath CorpusDir => RepoRoot / "tests" / "Meziantou.Framework.Imaging.Fixtures";

    private static FullPath FindRepositoryRoot()
    {
        foreach (var start in new[] { FullPath.FromPath(AppContext.BaseDirectory), FullPath.CurrentDirectory() })
        {
            if (start.TryFindFirstAncestorOrSelf(p => File.Exists(p / "tests" / "Meziantou.Framework.Imaging.Fixtures" / "manifest.json"), out var root))
                return root;
        }

        throw new FatalException("Cannot find the repository root (tests/Meziantou.Framework.Imaging.Fixtures/manifest.json)");
    }

    public static Obj LoadCommittedManifest() => (Obj)PyJson.Loads(File.ReadAllText(CorpusDir / "manifest.json", Encoding.UTF8))!;

    public static void WriteManifest(FullPath outDir, Obj manifest) =>
        File.WriteAllText(outDir / "manifest.json", PyJson.Dumps(manifest) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public static List<Obj> Fixtures(Obj manifest) => [.. ((List<object?>)manifest["fixtures"]!).Cast<Obj>()];

    /// <summary>Every file a list of manifest entries references (inputs and raw references).</summary>
    public static SortedSet<string> ManifestFiles(IEnumerable<Obj> fixtures)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var fixture in fixtures)
        {
            paths.Add((string)((Obj)fixture["input"]!)["path"]!);
            var expected = fixture.TryGetValue("expected", out var value) && value is Obj e ? e : [];
            var frames = expected.TryGetValue("frames", out var f) && f is List<object?> list ? list.Cast<Obj>().ToList() : [];
            if (expected.TryGetValue("poster", out var poster) && poster is Obj posterEntry)
                frames.Add(posterEntry);
            foreach (var frame in frames)
            {
                foreach (var buffer in ((List<object?>)frame["buffers"]!).Cast<Obj>())
                    paths.Add((string)buffer["path"]!);
            }
        }

        return paths;
    }

    /// <summary>Copies the committed files of these fixtures to the output directory (when not already generated).</summary>
    public static void CopyCommittedFiles(FullPath outDir, IEnumerable<Obj> fixtures)
    {
        foreach (var path in ManifestFiles(fixtures))
        {
            var target = outDir / path;
            if (!File.Exists(target))
            {
                target.CreateParentDirectory();
                File.Copy(CorpusDir / path, target);
            }
        }
    }

    /// <summary>The sections of the manifest owned by the icc generator.</summary>
    public static readonly string[] ColorSections = ["colorProfiles", "colorTransforms"];

    /// <summary>Every file the color management sections of a manifest reference (profiles and vectors).</summary>
    public static SortedSet<string> ColorFiles(Obj manifest)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        if (manifest.TryGetValue("colorProfiles", out var profiles) && profiles is List<object?> profileList)
        {
            foreach (var profile in profileList.Cast<Obj>())
                paths.Add((string)((Obj)profile["file"]!)["path"]!);
        }

        if (manifest.TryGetValue("colorTransforms", out var transforms) && transforms is List<object?> transformList)
        {
            foreach (var transform in transformList.Cast<Obj>())
                paths.Add((string)((Obj)transform["vectors"]!)["path"]!);
        }

        return paths;
    }

    /// <summary>The image generators keep the color management sections of the committed manifest, and their files,
    /// unchanged (the icc generator owns them).</summary>
    public static void PreserveColorSections(FullPath outDir, Obj manifest)
    {
        var committed = LoadCommittedManifest();
        foreach (var section in ColorSections)
        {
            if (committed.TryGetValue(section, out var value))
                manifest[section] = value;
        }

        foreach (var path in ColorFiles(committed))
        {
            var target = outDir / path;
            if (!File.Exists(target))
            {
                target.CreateParentDirectory();
                File.Copy(CorpusDir / path, target);
            }
        }
    }

    /// <summary>The generators of one format family replace only their own entries: every other fixture and file of the
    /// committed corpus is kept unchanged (the other generators own them).</summary>
    public static void MergeIntoCommittedManifest(FullPath outDir, IReadOnlyList<Obj> fixtures, Func<Obj, bool> owns)
    {
        var committed = LoadCommittedManifest();
        var others = Fixtures(committed).Where(f => !owns(f)).ToList();
        CopyCommittedFiles(outDir, others);
        PreserveColorSections(outDir, committed);
        committed["fixtures"] = others.Concat(fixtures).OrderBy(f => (string)f["id"]!, StringComparer.Ordinal).Cast<object?>().ToList();
        WriteManifest(outDir, committed);
    }

    public static SortedSet<string> CorpusFiles(FullPath directory)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = FullPath.FromPath(file).MakePathRelativeTo(directory).Replace('\\', '/');
            if ((relative != "manifest.json" && InfrastructureFiles.Contains(relative)) || relative.StartsWith("LICENSES/", StringComparison.Ordinal) || relative.EndsWith("/README.md", StringComparison.Ordinal))
                continue;
            result.Add(relative);
        }

        return result;
    }

    /// <summary>Generates into a temporary directory, prints the summary and the differences, then checks or writes.</summary>
    /// <param name="generate">Generates the corpus into the directory and returns the number of generated fixtures.</param>
    /// <param name="summary">The summary line from (fixtures, files, bytes).</param>
    /// <param name="reproducible">The message printed when the committed corpus matches.</param>
    public static int Run(GeneratorOptions options, string tempPrefix, Func<FullPath, int> generate, Func<int, int, long, string> summary, string reproducible)
    {
        var keep = options.Output is not null;
        var temp = keep ? FullPath.FromPath(options.Output!) : FullPath.FromPath(Directory.CreateTempSubdirectory(tempPrefix).FullName);
        if (keep && Directory.Exists(temp) && Directory.EnumerateFileSystemEntries(temp).Any())
            throw new FatalException("The output directory must be new or empty: " + temp);
        Directory.CreateDirectory(temp);
        try
        {
            var count = generate(temp);
            var generated = CorpusFiles(temp);
            var committed = CorpusFiles(CorpusDir);
            var changed = generated.Intersect(committed).Where(f => !File.ReadAllBytes(temp / f).AsSpan().SequenceEqual(File.ReadAllBytes(CorpusDir / f))).ToList();
            var added = generated.Except(committed).ToList();
            var removed = committed.Except(generated).ToList();
            var size = generated.Sum(f => new FileInfo(temp / f).Length);
            Console.WriteLine(summary(count, generated.Count, size));
            foreach (var (label, items) in new[] { ("changed", changed), ("added", added), ("removed", removed) })
            {
                foreach (var item in items)
                    Console.WriteLine($"  {label}: {item}");
            }

            if (!options.Write)
            {
                if (changed.Count > 0 || added.Count > 0 || removed.Count > 0)
                {
                    Console.WriteLine("The committed corpus differs from the generator output. Review, then rerun with --write.");
                    return 1;
                }

                Console.WriteLine(reproducible);
                return 0;
            }

            foreach (var item in removed)
                File.Delete(CorpusDir / item);
            foreach (var item in generated)
            {
                var target = CorpusDir / item;
                target.CreateParentDirectory();
                File.Copy(temp / item, target, overwrite: true);
            }

            foreach (var directory in Directory.EnumerateDirectories(CorpusDir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Reverse())
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }

            Console.WriteLine($"Corpus written to {CorpusDir}. Review the diff (references, hashes, tool versions) before committing.");
            return 0;
        }
        finally
        {
            if (!keep)
                Directory.Delete(temp, recursive: true);
        }
    }
}
