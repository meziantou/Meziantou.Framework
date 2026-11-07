namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

// The files of the corpus and the files each section of a manifest references: what a generator keeps of the sections
// it does not own, and so what its write pass leaves in place. This file has no dependency on the rest of the driver:
// the tests compile it to check these lists against the committed corpus (CorpusManifestTests).
internal static partial class CorpusDriver
{
    private static readonly string[] InfrastructureFiles = ["README.md", "manifest.json", "manifest.schema.json"];

    /// <summary>The manifest section of the AV1 conformance vectors (AvifCorpus): raw OBU streams with plane digests, kept
    /// apart from the fixtures because they have no pixel reference.</summary>
    public const string Av1VectorsKey = "av1Vectors";

    /// <summary>The manifest section of the HEVC conformance vectors (HevcCorpus): raw HEVC byte streams with plane
    /// digests, kept apart from the fixtures because they have no pixel reference.</summary>
    public const string HevcVectorsKey = "hevcVectors";

    /// <summary>The manifest section of the fonts (FontCorpus): font files and the values independent font readers give
    /// for them, kept apart from the fixtures because they are not images.</summary>
    public const string FontsKey = "fonts";

    /// <summary>The manifest section of the third-party suites of test fonts (FontCorpus), each a directory stored as published.</summary>
    public const string FontSuitesKey = "fontSuites";

    /// <summary>The sections of the manifest owned by the icc generator.</summary>
    public static readonly string[] ColorSections = ["colorProfiles", "colorTransforms"];

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

            // The converted pixels of a CMYK JPEG, which is only loaded with a color conversion
            if (fixture.TryGetValue("inkConversion", out var conversion) && conversion is Obj inkConversion)
                paths.Add((string)((Obj)inkConversion["buffer"]!)["path"]!);

            // The pixels of an OpenEXR file decoded with options other than the default ones (a layer, a window mode)
            if (fixture.TryGetValue("exrDecodings", out var decodings) && decodings is List<object?> exrDecodings)
            {
                foreach (var decoding in exrDecodings.Cast<Obj>())
                {
                    if (decoding.TryGetValue("buffer", out var buffer) && buffer is Obj bufferEntry)
                        paths.Add((string)bufferEntry["path"]!);
                }
            }
        }

        return paths;
    }

    /// <summary>The input files of the AV1 conformance vectors a manifest lists.</summary>
    public static SortedSet<string> Av1VectorFiles(Obj manifest) => VectorFiles(manifest, Av1VectorsKey);

    /// <summary>The input files of the HEVC conformance vectors a manifest lists.</summary>
    public static SortedSet<string> HevcVectorFiles(Obj manifest) => VectorFiles(manifest, HevcVectorsKey);

    private static SortedSet<string> VectorFiles(Obj manifest, string key)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        if (manifest.TryGetValue(key, out var value) && value is Obj section)
        {
            foreach (var entry in ((List<object?>)section["entries"]!).Cast<Obj>())
                paths.Add((string)((Obj)entry["input"]!)["path"]!);
        }

        return paths;
    }

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

    /// <summary>Every file the font sections of a manifest reference (fonts, vectors, shaping cases, renders and the
    /// files of the suites).</summary>
    /// <param name="corpusDirectory">The directory of the corpus the manifest describes: a suite is a directory, whose files are listed there.</param>
    public static SortedSet<string> FontFiles(Obj manifest, FullPath corpusDirectory)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        if (manifest.TryGetValue(FontsKey, out var fonts) && fonts is List<object?> fontList)
        {
            foreach (var font in fontList.Cast<Obj>())
            {
                paths.Add((string)((Obj)font["file"]!)["path"]!);
                if (font.TryGetValue("vectors", out var vectors) && vectors is Obj vectorsFile)
                    paths.Add((string)vectorsFile["path"]!);

                if (font.TryGetValue("shaping", out var shaping) && shaping is Obj shapingFile)
                    paths.Add((string)shapingFile["path"]!);

                if (font.TryGetValue("renders", out var renders) && renders is List<object?> renderList)
                {
                    foreach (var render in renderList.Cast<Obj>())
                        paths.Add((string)((Obj)render["file"]!)["path"]!);
                }
            }
        }

        if (manifest.TryGetValue(FontSuitesKey, out var suites) && suites is List<object?> suiteList)
        {
            foreach (var suite in suiteList.Cast<Obj>())
            {
                var directory = corpusDirectory / (string)suite["directory"]!;
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    paths.Add(FullPath.FromPath(file).MakePathRelativeTo(corpusDirectory).Replace('\\', '/'));
                }
            }
        }

        return paths;
    }

    /// <summary>Every file the sections of a manifest reference: the files a generator writes or keeps. A file of the
    /// corpus that is not in this list belongs to no generator, and the write pass of every generator removes it.</summary>
    /// <param name="corpusDirectory">The directory of the corpus the manifest describes.</param>
    public static SortedSet<string> ReferencedFiles(Obj manifest, FullPath corpusDirectory)
    {
        var paths = ManifestFiles(Fixtures(manifest));
        paths.UnionWith(Av1VectorFiles(manifest));
        paths.UnionWith(HevcVectorFiles(manifest));
        paths.UnionWith(ColorFiles(manifest));
        paths.UnionWith(FontFiles(manifest, corpusDirectory));
        return paths;
    }

    /// <summary>The path of every file description (an object with a path and a hash) anywhere in a manifest, whatever
    /// its section: a list that does not depend on the per-section lists above, to check them.</summary>
    public static SortedSet<string> DescribedFiles(object? node)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        Collect(node);
        return paths;

        void Collect(object? value)
        {
            if (value is Obj obj)
            {
                if (obj.TryGetValue("path", out var path) && path is string text && obj.TryGetValue("sha256", out var hash) && hash is string)
                    paths.Add(text);

                foreach (var item in obj.Values)
                    Collect(item);
            }
            else if (value is List<object?> list)
            {
                foreach (var item in list)
                    Collect(item);
            }
        }
    }

    /// <summary>The files of a corpus directory that the generators compare and replace: every file but the
    /// documentation, the schema and the license texts. The manifest is one of them.</summary>
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
}
