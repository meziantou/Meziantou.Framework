using System.Text.Json;
using Meziantou.Framework.Pdf.TestHarness;
using Meziantou.Framework.Pdf.TestHarness.Templates;

namespace Meziantou.Framework.Pdf.Corpus;

/// <summary>The files of the fetched corpora: <c>tests/Meziantou.Framework.Pdf.External/&lt;suite&gt;</c>, as pinned in <c>corpus.json</c>.</summary>
internal static class CorpusFiles
{
    public static FullPath Project => RepositoryPaths.Root / "tests" / "Meziantou.Framework.Pdf.Corpus";

    public static FullPath Expectations => Project / "expectations";

    public static FullPath SkipFile => Project / "corpus-skip.txt";

    /// <summary>Gets the names of the suites of <c>corpus.json</c>.</summary>
    public static IReadOnlyList<string> Suites { get; } = ReadSuites();

    /// <summary>Gets the identifiers of the files of a suite (<c>suite/relative/path.pdf</c>), without the skipped ones.</summary>
    public static IEnumerable<string> GetFiles(string suite)
    {
        var directory = RepositoryPaths.External / suite;
        if (!Directory.Exists(directory))
            yield break;

        var skipList = CorpusSkipList.Default;
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            if (relative.StartsWith(".git/", StringComparison.Ordinal))
                continue;

            var isTemplate = suite == "pdfium" && relative.EndsWith(".in", StringComparison.Ordinal);
            if (!isTemplate && !relative.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                continue;

            var id = suite + "/" + relative;
            if (!skipList.IsSkipped(id))
                yield return id;
        }
    }

    /// <summary>Reads a file of a corpus: a PDFium template is expanded.</summary>
    public static byte[] Read(string id)
    {
        var path = RepositoryPaths.External / id;
        return id.EndsWith(".in", StringComparison.Ordinal) ? PdfTemplate.ExpandFile(path) : File.ReadAllBytes(path);
    }

    private static string[] ReadSuites()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Project / "corpus.json"));
        return [.. document.RootElement.EnumerateObject().Select(p => p.Name)];
    }
}
