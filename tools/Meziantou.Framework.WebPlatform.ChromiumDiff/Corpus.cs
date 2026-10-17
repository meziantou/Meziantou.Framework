using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace ChromiumDiff;

internal sealed record Input(string Id, string Html);

/// <summary>The inputs of the differential testing.</summary>
internal static class Corpus
{
    public static IEnumerable<Input> Load(string root, bool pages, IEnumerable<string> directories)
    {
        // The documents of the tree construction tests (html5lib format) of WPT. The fragment tests and the tests that
        // require the scripting flag are not documents parsed with scripting disabled.
        var resources = Path.Combine(root, "tests", "Meziantou.Framework.WebPlatform.External", "wpt", "html", "syntax", "parsing", "resources");
        if (!Directory.Exists(resources))
            throw new InvalidOperationException($"'{resources}' does not exist: run 'dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -- fetch-tests'");

        foreach (var file in Directory.EnumerateFiles(resources, "*.dat").Order(StringComparer.Ordinal))
        {
            var index = 0;
            foreach (var (data, isDocument) in ReadTreeConstructionTests(file))
            {
                index++;
                if (isDocument)
                    yield return new Input($"{Path.GetFileName(file)}#{index}", data);
            }
        }

        if (pages)
        {
            foreach (var input in LoadPages(root))
                yield return input;
        }

        foreach (var directory in directories)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                yield return new Input(Path.GetRelativePath(directory, file), Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        }
    }

    private static IEnumerable<(string Data, bool IsDocument)> ReadTreeConstructionTests(string file)
    {
        var lines = File.ReadAllText(file).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i] != "#data")
                continue;

            var data = new List<string>();
            for (i++; i < lines.Length && lines[i] != "#errors"; i++)
                data.Add(lines[i]);

            var isDocument = true;
            for (; i < lines.Length && lines[i] != "#document"; i++)
            {
                if (lines[i] is "#document-fragment" or "#script-on")
                    isDocument = false;
            }

            yield return (string.Join('\n', data), isDocument);
        }
    }

    private static IEnumerable<Input> LoadPages(string root)
    {
        foreach (var page in GetPages(root))
            yield return new Input(page.Url, Encoding.UTF8.GetString(File.ReadAllBytes(page.Path)));
    }

    /// <summary>Returns the pages of pages.txt and the paths of their HTML files, downloaded once in artifacts/chromium-diff/pages.</summary>
    public static IEnumerable<Page> GetPages(string root)
    {
        var cache = Path.Combine(root, "artifacts", "chromium-diff", "pages");
        Directory.CreateDirectory(cache);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Meziantou.Framework.WebPlatform differential testing)");
        foreach (var line in File.ReadLines(Path.Combine(root, "tools", "Meziantou.Framework.WebPlatform.ChromiumDiff", "pages.txt")))
        {
            if (line.Length == 0 || line.StartsWith('#', StringComparison.Ordinal))
                continue;

            var name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(line)))[..16];
            var path = Path.Combine(cache, name + ".html");
            if (!File.Exists(path))
            {
                try
                {
                    File.WriteAllBytes(path, client.GetByteArrayAsync(new Uri(line)).GetAwaiter().GetResult());
                }
                catch (HttpRequestException ex)
                {
                    Console.Error.WriteLine($"warning: cannot download {line}: {ex.Message}");
                    continue;
                }
            }

            yield return new Page(line, path, name);
        }
    }
}

/// <summary>A page of pages.txt: its URL, the path of its downloaded HTML, and the name of its files in artifacts/chromium-diff.</summary>
internal sealed record Page(string Url, string Path, string Name);
