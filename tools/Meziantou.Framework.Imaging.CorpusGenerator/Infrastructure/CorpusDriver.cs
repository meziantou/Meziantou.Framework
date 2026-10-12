using System.Text;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

/// <param name="Output">Generates into this (new or empty) directory and keeps it, instead of a temporary directory.</param>
/// <param name="IccProfiles">The directory of the pinned checkout of the external ICC profiles (icc generator).</param>
/// <param name="Fonts">The directory of the pinned third-party font files (fonts generator).</param>
internal sealed record GeneratorOptions(bool Write, bool AcceptToolVersions, string? QoiHeader, string? Output = null, string? IccProfiles = null, string? Fonts = null);

/// <summary>Regenerates (a part of) the corpus in a temporary directory, then reports the differences with the committed
/// corpus (check) or replaces it (write).</summary>
internal static partial class CorpusDriver
{
    /// <summary>The version of the manifest schema every generator writes.</summary>
    public const int SchemaVersion = 5;

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

    /// <summary>Copies the committed AV1 conformance vectors to the output directory: AvifCorpus owns them, the other
    /// generators keep them (and their manifest section) unchanged.</summary>
    public static void CopyCommittedAv1Vectors(FullPath outDir, Obj committed)
    {
        foreach (var path in Av1VectorFiles(committed))
        {
            var target = outDir / path;
            target.CreateParentDirectory();
            File.Copy(CorpusDir / path, target, overwrite: true);
        }
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

    /// <summary>The other generators keep the font section of the committed manifest, and its files, unchanged (the fonts
    /// generator owns them).</summary>
    public static void PreserveFonts(FullPath outDir, Obj manifest)
    {
        var committed = LoadCommittedManifest();
        if (committed.TryGetValue(FontsKey, out var value))
            manifest[FontsKey] = value;

        if (committed.TryGetValue(FontSuitesKey, out var suites))
            manifest[FontSuitesKey] = suites;

        foreach (var path in FontFiles(committed, CorpusDir))
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
    /// committed corpus is kept unchanged (the other generators own them), and so are the AV1 conformance vectors.</summary>
    public static void MergeIntoCommittedManifest(FullPath outDir, IReadOnlyList<Obj> fixtures, Func<Obj, bool> owns)
    {
        var committed = LoadCommittedManifest();
        var others = Fixtures(committed).Where(f => !owns(f)).ToList();
        CopyCommittedFiles(outDir, others);
        CopyCommittedAv1Vectors(outDir, committed);
        PreserveColorSections(outDir, committed);
        PreserveFonts(outDir, committed);
        committed["fixtures"] = others.Concat(fixtures).OrderBy(f => (string)f["id"]!, StringComparer.Ordinal).Cast<object?>().ToList();
        WriteManifest(outDir, committed);
    }

    /// <summary>Stops before the output is compared or written when it is not a whole corpus: the write pass removes the
    /// committed files that are not in the output, so a file of another section that was not kept would be deleted.</summary>
    private static void CheckOutput(FullPath outDir, SortedSet<string> generated)
    {
        var manifest = (Obj)PyJson.Loads(File.ReadAllText(outDir / "manifest.json", Encoding.UTF8))!;

        // A section kept from the committed manifest whose files were not all copied
        var missing = DescribedFiles(manifest).Where(path => !generated.Contains(path)).ToList();
        if (missing.Count > 0)
            throw new FatalException($"The manifest describes {missing.Count} files that the generator neither wrote nor kept ({Sample(missing)}). The lists of CorpusDriver.Files.cs must name every file of a section.");

        // A file the lists do not name: the generator of every other section would not keep it
        var referenced = ReferencedFiles(manifest, outDir);
        var unlisted = generated.Where(path => path != "manifest.json" && !referenced.Contains(path)).ToList();
        if (unlisted.Count > 0)
            throw new FatalException($"The generator wrote {unlisted.Count} files that the lists of CorpusDriver.Files.cs do not name ({Sample(unlisted)}): the other generators would remove them.");

        static string Sample(List<string> paths) => string.Join(", ", paths.Take(3)) + (paths.Count > 3 ? ", ..." : "");
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
            CheckOutput(temp, generated);
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
